# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore dependencies as a separate cached layer
COPY Directory.Build.props ./
COPY Atrium.Api/Atrium.Api.csproj Atrium.Api/
RUN dotnet restore Atrium.Api/Atrium.Api.csproj

# Copy source and publish Release build
COPY Atrium.Api/ Atrium.Api/
RUN dotnet publish Atrium.Api/Atrium.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install curl for container healthcheck
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish ./

EXPOSE 8080

# Run as the built-in non-root app user
USER $APP_UID

HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=5 \
    CMD curl -fsS http://127.0.0.1:8080/api/health || exit 1

ENTRYPOINT ["dotnet", "Atrium.Api.dll"]
