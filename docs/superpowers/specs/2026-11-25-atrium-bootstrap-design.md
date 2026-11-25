# Atrium — Bootstrap Design

**Date:** 2026-11-25
**Status:** Approved

## Intent & Why
Atrium is a private, self-hosted personal "everything-app" for storing personal data, calendar info, and future custom modules, designed to be used both by its owner (via a sleek, native-feeling PWA) and by AI agents (via MCP tools). This initial bootstrap establishes the clean repository, remote backup, local Postgres database, minimal C# EF Core API skeleton, and empty installable PWA frontend shell — without prematurely implementing auth, domain models, or business logic.

## Goals
- Initialize a local Git repository in `/home/andrish/Work/personal/assistant` and connect it to a new **private** GitHub repository named `atrium`.
- Include baseline repository hygiene: `README.md` (vision, architecture, local setup, phased roadmap), `.gitignore`, `.editorconfig`, and `.env.example`.
- Provide a local PostgreSQL 17 instance via `docker-compose.yml` with persistent storage and healthcheck.
- Scaffold a lightweight ASP.NET Core (.NET 10) Minimal API in `api/` wired to PostgreSQL via `Npgsql.EntityFrameworkCore.PostgreSQL` with an empty `AppDbContext` and a `GET /api/health` endpoint checking database reachability.
- Scaffold a placeholder React + Vite + TypeScript PWA in `web/` using `vite-plugin-pwa` (manifest, icons, service worker), adaptive light/dark styling, and a Vite `/api` dev proxy.

## Non-Goals (Deferred)
- Authentication, authorization, and user management (next milestone).
- Domain entities, migrations, calendar/data features, or MCP server tools.
- Production deployment, TLS/reverse proxy, or CI/CD pipelines.

## Constraints
- Self-hosted locally first; private repository; no secrets committed (`.env` gitignored, `.env.example` committed).
- Frontend: React, Vite, TypeScript, installable PWA.
- Backend: C# (.NET 10), Entity Framework Core, PostgreSQL (`Npgsql.EntityFrameworkCore.PostgreSQL`).
- Local toolchain: `dotnet 10.0.112`, `node v20.20.2`, `npm`, `docker`, `git`.

## Approaches Considered
1. **Single Monorepo (`web/`, `api/`, `docker-compose.yml`) — Selected.** Keeps frontend, backend, database config, and future MCP server in sync with one git history and zero cross-repo friction for a solo self-hosted app.
2. **Separate Frontend & Backend Repositories — Rejected.** Adds coordination overhead and duplicated docs/env config for a single-owner app.
3. **Full ASP.NET Core MVC Controllers Template — Rejected.** Heavier boilerplate than needed for a lightweight skeleton; Minimal APIs keep the surface small and easy to evolve.

## Architecture & Key Decisions
- **Repo name:** `atrium` (private on GitHub).
- **Database (`docker-compose.yml`):** `postgres:17-alpine` listening on `127.0.0.1:5432` (configurable via `.env`), named volume `atrium_pgdata`, `pg_isready` healthcheck.
- **API (`api/Atrium.Api/`):**
  - `Program.cs`: Registers `AppDbContext` with `UseNpgsql`, exposes `GET /api/health` returning `{ status, database, timestamp }` (200 OK when DB is reachable, 503 Service Unavailable when unreachable).
  - `Data/AppDbContext.cs`: Empty EF Core `DbContext` ready for future entities.
  - Solution file `Atrium.slnx` (or `api/Atrium.sln`) + `api/Atrium.Api.Tests/` xUnit test project verifying `/api/health` behavior (healthy & unhealthy DB cases).
- **Frontend (`web/`):**
  - React 19 + Vite + TypeScript + `vite-plugin-pwa`.
  - Clean, minimal adaptive light/dark placeholder shell showing "Atrium" and live API health status (gracefully handling when API is offline).
  - PWA manifest (`name: "Atrium"`, `short_name: "Atrium"`, `display: "standalone"`), SVG/PNG icons, and Vite dev proxy `/api` → `http://localhost:5000`.

