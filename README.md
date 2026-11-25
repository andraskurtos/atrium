# Atrium

A private, self-hosted personal everything-app for storing personal data, calendar info, and custom modules — designed for both human use (installable PWA) and AI agents (via MCP tools).

## Architecture

- **`web/`** — React + Vite + TypeScript Progressive Web App (`vite-plugin-pwa`)
- **`api/`** — Lightweight ASP.NET Core (.NET 10) Minimal API + Entity Framework Core (`Npgsql`)
- **`docker-compose.yml`** — Local PostgreSQL 17 database bound strictly to `127.0.0.1`

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/)
- [Node.js 20+](https://nodejs.org/)
- [Docker](https://www.docker.com/) (for local PostgreSQL)

## Local Development

1. **Copy environment defaults:**
   ```bash
   cp .env.example .env
   ```

2. **Start PostgreSQL:**
   ```bash
   docker compose up -d
   ```

3. **Run the API** (listens on `http://localhost:5000`):
   ```bash
   dotnet run --project api/Atrium.Api
   ```

4. **Run the frontend** (proxies `/api` to `http://localhost:5000`):
   ```bash
   cd web
   npm install
   npm run dev
   ```

5. **Run tests & builds:**
   ```bash
   dotnet test api/Atrium.sln
   npm --prefix web run typecheck && npm --prefix web run build
   ```

## Roadmap

- [x] **Phase 1: Bootstrap** — Monorepo, private GitHub remote, local Postgres, minimal EF Core API skeleton, installable placeholder PWA
- [ ] **Phase 2: Authentication & Security** — Strong local auth (passkeys / WebAuthn + session hardening), secret management, scoped API tokens for agents
- [ ] **Phase 3: Core Data & Calendar** — Flexible personal knowledge/data store, calendar/events, adaptive native-feeling UI modules
- [ ] **Phase 4: MCP Server** — Model Context Protocol tools so AI agents can safely read/write Atrium data with user-controlled permissions
