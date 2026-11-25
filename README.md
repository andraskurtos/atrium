# Atrium

A private, self-hosted personal everything-app for storing personal data, calendar info, and custom modules — designed for both human use (installable PWA) and AI agents (via MCP tools).

## Architecture

- **`web/`** — React + Vite + TypeScript Progressive Web App (`vite-plugin-pwa`), deployable to GitHub Pages
- **`api/`** — Lightweight ASP.NET Core (.NET 10) Minimal API + Entity Framework Core (`Npgsql`) + production `Dockerfile`
- **`docker-compose.yml`** — PostgreSQL 17 database + containerized API + optional Caddy automatic-HTTPS profile
- **`scripts/`** — Zero-dependency Node dev/prod runners plus one-click Linux server setup (`scripts/setup-server.sh`)
- **`.github/workflows/deploy-pages.yml`** — Automated GitHub Pages build and deployment for the frontend PWA

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/) (for local development)
- [Node.js 20+](https://nodejs.org/) (for local development)
- [Docker](https://www.docker.com/) (for local PostgreSQL and containerized server hosting)

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

# Run containerized backend server (API + Postgres)
npm run server:up

# Run tests and typecheck
npm test

# Build everything
npm run build
```

## Hosting & Deployment

- **Frontend (GitHub Pages):** Enable **Settings → Pages → Source: GitHub Actions** and run the `Deploy Frontend to GitHub Pages` workflow. Set the `VITE_API_BASE_URL` repository variable or click **Configure** in the app UI to point at your backend server.
- **Backend Server (One-Click Container Setup):** Run on any Linux server to install Docker (if needed), generate secure credentials, configure CORS/HTTPS, and launch PostgreSQL + API containers:
  ```bash
  curl -fsSL https://raw.githubusercontent.com/andraskurtos/atrium/main/scripts/setup-server.sh | bash
  ```
  Or inside a cloned repository: `./scripts/setup-server.sh`

See the full **[Deployment & Backend Server Setup Guide](docs/deployment.md)** for HTTPS options (built-in Caddy, Tailscale, Cloudflare Tunnel, Nginx), manual Docker Compose setup, backups, and troubleshooting.

## Roadmap

- [x] **Phase 1: Bootstrap** — Monorepo, private GitHub remote, local Postgres, minimal EF Core API skeleton, installable placeholder PWA, F5 dev & production runners
- [ ] **Phase 2: Authentication & Security** — Strong local auth (passkeys / WebAuthn + session hardening), secret management, scoped API tokens for agents
- [ ] **Phase 3: Core Data & Calendar** — Flexible personal knowledge/data store, calendar/events, adaptive native-feeling UI modules
- [ ] **Phase 4: MCP Server** — Model Context Protocol tools so AI agents can safely read/write Atrium data with user-controlled permissions
