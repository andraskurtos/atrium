# Atrium Bootstrap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Initialize the private `atrium` GitHub monorepo with local PostgreSQL Docker config, a minimal wired-up C# (.NET 10) + EF Core API skeleton, and an installable placeholder React + Vite + TypeScript PWA.

**Architecture:** Single monorepo containing `docker-compose.yml` (PostgreSQL 17), `api/` (ASP.NET Core 10 Minimal API + EF Core Npgsql + xUnit tests), and `web/` (React + Vite + TypeScript + `vite-plugin-pwa`). No authentication, domain entities, or business logic are implemented yet — only clean, verified wiring ready for the next milestone.

**Tech Stack:** Git, GitHub (`andraskurtos/atrium`, private), Docker Compose (PostgreSQL 17), .NET 10, ASP.NET Core Minimal API, EF Core + Npgsql, xUnit, React, Vite, TypeScript, `vite-plugin-pwa`.

**Spec:** [2026-11-25-atrium-bootstrap-design.md](file:///home/andrish/Work/personal/assistant/docs/superpowers/specs/2026-11-25-atrium-bootstrap-design.md)

## Global Constraints

- Repository name is `atrium` and MUST be **private** on GitHub (`andraskurtos/atrium`).
- No secrets committed: `.env` is gitignored; `.env.example` holds placeholder local dev defaults.
- Local PostgreSQL binds to `127.0.0.1` only (`127.0.0.1:${POSTGRES_PORT:-5432}:5432`) to keep data local.
- Backend uses .NET 10 (`net10.0`), EF Core, and `Npgsql.EntityFrameworkCore.PostgreSQL`; no domain models, auth, or business logic yet.
- Frontend uses React + Vite + TypeScript + `vite-plugin-pwa`, installable as a standalone PWA, with a minimal placeholder UI.
- Consult `modern-web-guidance` before authoring HTML/CSS in `web/`.

## Review Focus

- **Missing connection string at API startup:** `Program.cs` should fail fast with a clear `InvalidOperationException` if `ConnectionStrings:DefaultConnection` is missing, rather than throwing an obscure Npgsql error on the first request.
- **Database unreachable or throwing during `/api/health`:** `GET /api/health` must catch connection exceptions and return `503 Service Unavailable` with `{ "status": "degraded", "database": "unreachable", "timestamp": ... }` instead of an unhandled `500`.
- **Docker Compose invalid or exposing Postgres publicly:** `docker compose config` must validate cleanly and bind Postgres strictly to `127.0.0.1`.
- **PWA build artifacts missing:** `npm run build` in `web/` must emit both `dist/manifest.webmanifest` and `dist/sw.js` (service worker) and pass `tsc --noEmit`.
- **Accidental secret commit:** `.gitignore` must exclude `.env`, `.env.local`, `bin/`, `obj/`, `node_modules/`, and `dist/` before the first commit.

---

### Task 1: Repository, Private GitHub Remote & Local Postgres Config

**Files:**
- Create: `.gitignore`
- Create: `.editorconfig`
- Create: `.env.example`
- Create: `docker-compose.yml`
- Create: `README.md`

**Interfaces:**
- Produces:
  - Git repository on branch `main` with remote `origin` pointing to `andraskurtos/atrium` (private).
  - Local PostgreSQL service `postgres` on `127.0.0.1:5432` with default dev credentials documented in `.env.example` (`POSTGRES_DB=atrium`, `POSTGRES_USER=atrium`, `POSTGRES_PASSWORD=atrium_dev_password`).

- [ ] **Step 1: Initialize local Git repository**
  Run: `git init -b main` in `/home/andrish/Work/personal/assistant`.
  Expected: Initialized empty Git repository on `main`.

- [ ] **Step 2: Create private GitHub repository `atrium`**
  Call `github-mcp-server` `create_repository` with `{ "name": "atrium", "description": "Private self-hosted personal everything-app and AI agent hub", "private": true, "autoInit": false }`.
  Add `origin` remote pointing to the returned repository URL.

- [ ] **Step 3: Create `.gitignore`, `.editorconfig`, `.env.example`, `docker-compose.yml`, and `README.md`**
  - `.gitignore`: ignores `.env`, `.env.*.local`, `node_modules/`, `dist/`, `bin/`, `obj/`, `.vs/`, `*.user`, `*.suo`, `.DS_Store`, `coverage/`, `*.log`.
  - `.editorconfig`: UTF-8, LF line endings, 2-space indent for TS/TSX/JS/JSON/YAML/CSS/HTML, 4-space indent for C# (`.cs`, `.csproj`).
  - `.env.example`: `POSTGRES_DB=atrium`, `POSTGRES_USER=atrium`, `POSTGRES_PASSWORD=atrium_dev_password`, `POSTGRES_PORT=5432`, `API_PORT=5000`.
  - `docker-compose.yml`: `postgres:17-alpine` service bound to `127.0.0.1:${POSTGRES_PORT:-5432}:5432`, named volume `atrium_pgdata`, `pg_isready` healthcheck.
  - `README.md`: Atrium overview, architecture (`web/`, `api/`, Postgres), prerequisites, local dev quickstart, and phased roadmap (Phase 1: Bootstrap → Phase 2: Auth & Security → Phase 3: Core Data & Calendar → Phase 4: MCP Server for AI Agents).

- [ ] **Step 4: Verify Docker Compose config**
  Run: `docker compose config --quiet`
  Expected: Exit code 0 with no errors.

- [ ] **Step 5: Commit foundation**
  ```bash
  git add .gitignore .editorconfig .env.example docker-compose.yml README.md docs/
  git commit -m "chore: initialize atrium repository and local postgres config"
  ```

---

### Task 2: Minimal C# (.NET 10) + EF Core + PostgreSQL API Skeleton

**Files:**
- Create: `api/Atrium.sln`
- Create: `api/Atrium.Api/Atrium.Api.csproj`
- Create: `api/Atrium.Api/Data/AppDbContext.cs`
- Create: `api/Atrium.Api/Health/IDatabaseHealthCheck.cs`
- Create: `api/Atrium.Api/Program.cs`
- Create: `api/Atrium.Api/appsettings.json`
- Create: `api/Atrium.Api/appsettings.Development.json`
- Create: `api/Atrium.Api/Properties/launchSettings.json`
- Create: `api/Atrium.Api.Tests/Atrium.Api.Tests.csproj`
- Create: `api/Atrium.Api.Tests/HealthEndpointTests.cs`

**Interfaces:**
- Consumes: Postgres connection defaults from Task 1 (`Host=127.0.0.1;Port=5432;Database=atrium;Username=atrium;Password=atrium_dev_password`).
- Produces:
  - `GET /api/health` → `200 OK` `{ "status": "ok", "database": "connected", "timestamp": "<UTC ISO-8601>" }` or `503 Service Unavailable` `{ "status": "degraded", "database": "unreachable", "timestamp": "<UTC ISO-8601>" }`.
  - `AppDbContext(DbContextOptions<AppDbContext> options)` wired to Npgsql.
  - API listens on `http://localhost:5000` in development (`launchSettings.json`).

- [ ] **Step 1: Scaffold solution, API project, and xUnit test project**
  Create `api/Atrium.sln`, `api/Atrium.Api` (`net10.0`, `Microsoft.NET.Sdk.Web`), and `api/Atrium.Api.Tests` (`net10.0`, xUnit + `Microsoft.AspNetCore.Mvc.Testing`).
  Add `Npgsql.EntityFrameworkCore.PostgreSQL` to `Atrium.Api`.

- [ ] **Step 2: Write failing tests in `api/Atrium.Api.Tests/HealthEndpointTests.cs`**
  Three tests using `WebApplicationFactory<Program>` with `IDatabaseHealthCheck` overridden:
  1. `HealthEndpoint_Returns200_WhenDatabaseIsReachable` — asserts `HttpStatusCode.OK`, JSON `status == "ok"`, `database == "connected"`, valid `timestamp`.
  2. `HealthEndpoint_Returns503_WhenDatabaseIsUnreachable` — asserts `HttpStatusCode.ServiceUnavailable`, JSON `status == "degraded"`, `database == "unreachable"`.
  3. `HealthEndpoint_Returns503_WhenDatabaseCheckThrows` — asserts `HttpStatusCode.ServiceUnavailable`, JSON `status == "degraded"`, `database == "unreachable"`.

- [ ] **Step 3: Run tests to verify they fail**
  Run: `dotnet test api/Atrium.sln`
  Expected: FAIL (types/endpoint not yet implemented).

- [ ] **Step 4: Implement `AppDbContext`, `IDatabaseHealthCheck`, `Program.cs`, and config files**
  - `AppDbContext`: empty `DbContext` subclass with primary constructor `(DbContextOptions<AppDbContext> options) : DbContext(options)`.
  - `IDatabaseHealthCheck`: `Task<bool> CanConnectAsync(CancellationToken cancellationToken)` implemented by `EfDatabaseHealthCheck` calling `AppDbContext.Database.CanConnectAsync(cancellationToken)`.
  - `Program.cs`: Reads `ConnectionStrings:DefaultConnection` (throws `InvalidOperationException` if missing/whitespace), registers `AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString))`, registers `IDatabaseHealthCheck`, maps `GET /api/health`, exposes `public partial class Program;` for `WebApplicationFactory`.
  - `appsettings.json` / `appsettings.Development.json`: Default local Postgres connection string in `appsettings.Development.json` matching `.env.example`.
  - `Properties/launchSettings.json`: `applicationUrl: "http://localhost:5000"`.

- [ ] **Step 5: Run tests to verify they pass**
  Run: `dotnet test api/Atrium.sln`
  Expected: PASS (3 passed, 0 failed).

- [ ] **Step 6: Commit API skeleton**
  ```bash
  git add api/
  git commit -m "feat(api): add minimal .NET 10 EF Core and Postgres skeleton"
  ```

---

### Task 3: Placeholder React + Vite + TypeScript PWA Frontend

**Files:**
- Create: `web/package.json`
- Create: `web/tsconfig.json`
- Create: `web/tsconfig.app.json`
- Create: `web/tsconfig.node.json`
- Create: `web/vite.config.ts`
- Create: `web/index.html`
- Create: `web/public/icon.svg`
- Create: `web/public/pwa-192x192.png`
- Create: `web/public/pwa-512x512.png`
- Create: `web/src/vite-env.d.ts`
- Create: `web/src/styles.css`
- Create: `web/src/App.tsx`
- Create: `web/src/main.tsx`

**Interfaces:**
- Consumes: `GET /api/health` via Vite dev proxy `/api` → `http://localhost:5000` (gracefully handles offline/unreachable API).
- Produces: Installable standalone PWA (`Atrium`) built to `web/dist/` with `manifest.webmanifest` and `sw.js`.

- [ ] **Step 1: Consult `modern-web-guidance` for dark mode / color scheme & PWA HTML basics**
  Run `npx -y modern-web-guidance@latest search "dark mode color scheme"` (and retrieve matching guide) before writing HTML/CSS.

- [ ] **Step 2: Scaffold `web/` package and dependencies**
  Create `web/package.json` with React 19, React DOM 19, Vite 6, TypeScript, `@vitejs/plugin-react`, `vite-plugin-pwa`, `@types/react`, `@types/react-dom`.
  Scripts: `dev`, `build`, `preview`, `typecheck` (`tsc --noEmit`).
  Run `npm install` in `web/`.

- [ ] **Step 3: Configure Vite + PWA + TypeScript + Icons**
  - `web/vite.config.ts`: React plugin + `VitePWA` (`registerType: 'autoUpdate'`, manifest `name: "Atrium"`, `short_name: "Atrium"`, `description: "Private personal everything-app"`, `display: "standalone"`, `start_url: "/"`, `theme_color` / `background_color`, 192x192 & 512x512 icons) + `server.proxy` for `/api` → `http://localhost:5000`.
  - Generate clean geometric Atrium icons (`icon.svg`, `pwa-192x192.png`, `pwa-512x512.png`) in `web/public/`.
  - `web/index.html`: Proper viewport, `color-scheme` meta, theme-color media queries, apple-touch-icon, title `Atrium`.

- [ ] **Step 4: Implement minimal adaptive placeholder UI**
  - `web/src/styles.css`: Clean, native-feeling adaptive light/dark styling using modern CSS (`color-scheme: light dark`, `light-dark()` or system tokens per `modern-web-guidance`).
  - `web/src/App.tsx`: Centered minimal placeholder shell ("Atrium" wordmark, short tagline, subtle live API status pill fetched from `/api/health` with clean offline fallback).
  - `web/src/main.tsx`: Mounts `<App />` in `React.StrictMode`.

- [ ] **Step 5: Verify TypeScript typecheck and PWA production build**
  Run: `npm run typecheck && npm run build` in `web/`.
  Check that `web/dist/manifest.webmanifest` and `web/dist/sw.js` exist.
  Expected: Typecheck passes with 0 errors, build succeeds, manifest & SW emitted.

- [ ] **Step 6: Commit frontend placeholder**
  ```bash
  git add web/
  git commit -m "feat(web): add placeholder React Vite TypeScript PWA shell"
  ```

---

### Task 4: Final Verification & Push to GitHub

- [ ] **Step 1: Run full verification suite**
  - `docker compose config --quiet`
  - `dotnet test api/Atrium.sln`
  - `npm --prefix web run typecheck && npm --prefix web run build`
  - `git status` (verify working tree clean and no `.env` or build artifacts tracked).

- [ ] **Step 2: Push `main` branch to private GitHub repo**
  Run: `git push -u origin main`
  Expected: Branch `main` pushed cleanly to `andraskurtos/atrium`.

