# Atrium

A private, self-hosted personal everything-app for storing personal data, calendar info, and custom modules — designed for both human use (installable PWA) and AI agents (via MCP tools).

## Architecture

- **`web/`** — React + Vite + TypeScript Progressive Web App (`vite-plugin-pwa`)
- **`api/`** — Lightweight ASP.NET Core (.NET 10) Minimal API + Entity Framework Core (`Npgsql`), serving both `/api/*` and the built PWA in production
- **`Dockerfile`** & **`docker-compose.yml`** — Unified containerized stack (Frontend PWA + `.NET 10` API + PostgreSQL 17 + optional Caddy automatic-HTTPS profile)
- **`scripts/`** — Zero-dependency Node dev/prod runners plus one-click server setup (`scripts/setup-server.sh`)

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/) (for local development)
- [Node.js 20+](https://nodejs.org/) (for local development)
- [Docker](https://www.docker.com/) (for local PostgreSQL and containerized hosting)

## Quickstart (F5 or CLI)

### One-Key Development (VS Code / Antigravity IDE)
Press **F5** (runs **`Atrium: Dev (Full Stack)`**):
1. Checks if PostgreSQL (`atrium-postgres`) is running and starts it via Docker Compose if needed.
2. Starts the `.NET 10` API in watch mode on `http://localhost:5000`.
3. Starts the Vite PWA dev server on `http://localhost:5173` (proxying `/api` to `:5000`).
4. Pipes all color-coded logs (`[db]`, `[api]`, `[web]`) to the integrated terminal.

To run the **Production Build** from the IDE, switch the launch dropdown to **`Atrium: Production (Build & Start)`** and press **F5**. It ensures Postgres is running, builds `web/dist` and the Release API, and serves both the API and built PWA from `http://localhost:5000`.

### CLI Commands
```bash
# Optional: customize local ports/credentials
cp .env.example .env

# Full-stack development (auto-starts Postgres + API watch + Vite)
npm run dev

# Production build & start (auto-starts Postgres + builds PWA & Release API + serves on :5000)
npm run prod

# Run containerized full-stack server (Frontend PWA + API + Postgres)
npm run server:up

# Run tests and typecheck
npm test

# Build everything
npm run build
```

## Self-Hosting (One-Click Container Setup)

Host the entire stack — **Frontend PWA + `.NET 10` API + PostgreSQL 17** (+ optional automatic HTTPS via Caddy) — locally, on a home server/LAN, or on a VPS with a single command:

```bash
./scripts/setup-server.sh
```

The script installs Docker on Linux if missing, generates a strong PostgreSQL password into `chmod 600 .env`, builds and starts all containers, and verifies both the frontend and `/api/health`.

See the full **[Self-Hosted Server Setup Guide](docs/deployment.md)** for HTTPS options (built-in Caddy, Tailscale Serve, Cloudflare Tunnel, Nginx), manual Docker Compose setup, backups, and troubleshooting.

## Roadmap

- [x] **Phase 1: Bootstrap** — Monorepo, private GitHub remote, local Postgres, minimal EF Core API skeleton, installable placeholder PWA, F5 dev & production runners
- [ ] **Phase 2: Authentication & Security** — Strong local auth (passkeys / WebAuthn + session hardening), secret management, scoped API tokens for agents
- [ ] **Phase 3: Core Data & Calendar** — Flexible personal knowledge/data store, calendar/events, adaptive native-feeling UI modules
- [ ] **Phase 4: MCP Server** — Model Context Protocol tools so AI agents can safely read/write Atrium data with user-controlled permissions
