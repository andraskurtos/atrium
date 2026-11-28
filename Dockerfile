# syntax=docker/dockerfile:1

# Stage 1: Build the React + Vite PWA frontend
FROM node:22-alpine AS web-build
WORKDIR /src/web

COPY web/package.json web/package-lock.json ./
RUN npm ci

COPY web/ ./
RUN npm run build

# Stage 2: Build and publish the .NET 10 API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src/api

COPY api/Directory.Build.props ./
COPY api/Atrium.Api/Atrium.Api.csproj Atrium.Api/
RUN dotnet restore Atrium.Api/Atrium.Api.csproj

COPY api/Atrium.Api/ Atrium.Api/
RUN dotnet publish Atrium.Api/Atrium.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# Stage 3: Single-origin production runtime (serves both API and built PWA)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install curl (healthcheck) and poppler-utils (pdftoppm for PDF slide image extraction)
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl poppler-utils \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    Atrium__WebDistPath=/app/wwwroot

COPY --from=api-build /app/publish ./
COPY --from=web-build /src/web/dist ./wwwroot

# Ensure the uploads mount point is owned by the non-root app user so Docker named volumes initialize writable
RUN mkdir -p /app/uploads \
    && chown -R $APP_UID:$APP_UID /app/uploads

EXPOSE 8080

# Run as the built-in non-root app user
USER $APP_UID

HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=5 \
    CMD curl -fsS http://127.0.0.1:8080/api/health || exit 1

ENTRYPOINT ["dotnet", "Atrium.Api.dll"]
