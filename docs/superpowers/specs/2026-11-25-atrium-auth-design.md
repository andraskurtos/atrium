# Atrium — Authentication, Accounts, Invites, Member Management & Scoped MCP Tokens Design

**Date:** 2026-11-25
**Status:** Approved
**Branch:** `feature/auth-and-accounts`
**Follow-up PR (out of scope here):** WebAuthn Passkeys (`Fido2NetLib`)

---

## 1. Intent & Why
Atrium is a self-hosted, single-origin personal & household "everything-app" designed for both humans (Atelier PWA) and AI agents (via MCP tools). Right now `AppDbContext` is empty and the frontend stores mock account state in `localStorage` with no authentication gate.

This design adds real, production-grade, invite-only multi-user authentication, persistent accounts & preferences, Owner member management, rotating refresh-token sessions, and SHA-256-hashed scoped MCP Personal Access Tokens (PATs) — wiring the existing [AccountPage.tsx](file:///home/andrish/Work/personal/assistant/web/src/components/AccountPage.tsx) and replacing `WelcomeScreen.tsx` with a Warm Swiss Atelier auth gate.

## 2. Goals
- **Single-Owner, invite-only multi-user model:**
  - Strictly **one `Owner`** per Atrium instance (enforced by a PostgreSQL partial unique index `WHERE role = 'Owner'` on `users`).
  - First visitor on an empty database completes a one-time **Initial Owner Setup**; once created, public setup is permanently locked.
  - The Owner can generate one-time, expiring **Invite Codes/Links** (default 7 days, max 30 days, revocable before redemption) to onboard household **`Member`** accounts.
  - The Owner can **manage household members**: list all members, deactivate/reactivate a `Member`, or permanently remove a `Member` (immediately revoking all their refresh sessions and MCP agent tokens).
  - Every user-owned table carries a `UserId` foreign key from day one for future household/multiplayer features.
- **Strong browser authentication:**
  - Short-lived JWT access token (**15 minutes**, signed HS256 with a configured ≥32-byte secret, held **in memory only** in the React SPA — never in `localStorage`).
  - Rotating `HttpOnly; SameSite=Lax` (and `Secure` when HTTPS) refresh-token cookie with a **30-day sliding window and 90-day absolute cap** (`expires_at = min(UtcNow + 30d, family_created_at + 90d)`), stored as a SHA-256 hash in Postgres (`refresh_sessions`) with **reuse detection** (replaying a rotated/revoked refresh token immediately revokes the entire session family).
  - Full **Active Sessions** management (list devices, last active time, revoke individual session, sign out all other sessions, sign out current session).
- **Strong password & abuse protection:**
  - Password hashing via ASP.NET Core `PasswordHasher<User>` (PBKDF2-HMAC-SHA512).
  - Server-side enforcement of the password policy already shown in `AccountPage.tsx`: ≥12 characters, uppercase, lowercase, and digit or symbol.
  - Built-in ASP.NET Core rate limiting on `/api/auth/login`, `/api/auth/setup`, `/api/auth/register`, and `/api/auth/refresh`.
  - Generic `401 Invalid credentials` responses on login failure to prevent username/email enumeration.
- **Scoped MCP Agent Tokens (PATs):**
  - Format: `atr_<8-char-prefix>_<32-byte-base64url-secret>`.
  - Only `prefix` and `SHA-256(fullToken)` are stored in Postgres; plaintext token is returned **once** on creation.
  - Supported scopes matching `AccountPage.tsx`: `calendar:read`, `calendar:propose`, `tasks:read`, `tasks:propose`, `vault:read`.
  - Custom `Bearer atr_...` authentication scheme alongside `JwtBearer`, verifying user `is_active = true`, updating `last_used_at` (throttled to at most once per minute), and exposing scope claims for authorization policies.
- **Persistent Profile & Preferences:**
  - Wire `AccountPage.tsx` profile fields (`displayName`, `username`, `email`, `bio`, `timezone`, `avatarDataUrl` up to 2 MB, `passwordUpdatedAt`) and preferences (`theme`, `weekStartsOn`, `timeFormat`, `requireAgentApproval`, `morningBrief`, `eventReminders`) to Postgres.
- **Warm Swiss Atelier Auth & Dashboard Updates:**
  - Replace `WelcomeScreen.tsx` (and its `Welcome` nav/command entries) with `AuthScreen.tsx` (**Initial Owner Setup** when `!initialized`, **Sign In** when `initialized`, **Accept Invite** via `/#invite=<code>` or toggle).
  - Add a subtle, compact welcome badge on the Dashboard header (replacing the standalone welcome screen).
  - Remove the mock **`Reset Account`** button and reset confirmation dialog from `AccountPage.tsx` (keep `Export personal archive`).
  - Add Owner-only **Household Invites** and **Household Members** sections plus **Sign Out** in `AccountPage.tsx` / `TopBar.tsx`.
  - Clearly label the existing Passkeys section in `AccountPage.tsx` as *"Coming next (WebAuthn follow-up)"* so mock passkeys are not mistaken for active security.

## 3. Non-Goals & Anti-Goals
- **Deferred to PR 2:** Real WebAuthn Passkey registration/authentication (`Fido2NetLib`).
- **Deferred to later phases:** Email delivery/SMTP password reset, OAuth/OIDC external providers, household shared groups/chores/expenses tables, persisting mock tasks/calendar/notes/vault items.
- **Anti-goals:** Never store JWTs or refresh tokens in `localStorage`; never log plaintext passwords, refresh tokens, invite codes, or PATs; never allow a committed dev placeholder JWT signing key in Production.

## 4. Constraints
- Stack: `.NET 10` Minimal API + EF Core (`Npgsql.EntityFrameworkCore.PostgreSQL` `10.0.0`) + PostgreSQL 17; React 19 + Vite + TypeScript PWA.
- CI & Hygiene: Must pass `npm run check:all` (`TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended`, zero undocumented suppressions, strict TypeScript).
- Single-origin deployment behind Caddy (`ForwardedHeaders` already configured) and local dev via Vite `/api` proxy (`http://localhost:5173` → `http://localhost:5000`).
- UI must strictly follow [style-guide.md](file:///home/andrish/Work/personal/assistant/docs/design/style-guide.md) (Warm Tactile Swiss + Focus Stream, Light & Dark mode).

## 5. Approaches Considered
1. **Custom Minimal Auth on EF Core — Selected.** Purpose-built Postgres tables (`users`, `user_preferences`, `refresh_sessions`, `invites`, `agent_tokens`), ASP.NET `PasswordHasher<User>`, `JwtBearer` + custom `atr_` PAT handler, rotating `HttpOnly` refresh cookies. Zero legacy Identity table bloat, full control over reuse detection, member deactivation, and MCP scopes.
2. **Full `Microsoft.AspNetCore.Identity` (`IdentityDbContext`) — Rejected.** Pulls in 7+ legacy `AspNet*` tables and abstractions that still require custom layers for rotating refresh tokens, invite-only registration, and scoped MCP PATs.

## 6. Data Model (PostgreSQL via EF Core Migrations)

All primary keys are `Guid` (`uuid`), timestamps are `DateTimeOffset` (`timestamptz`), and string columns have explicit max lengths and unique indexes where noted.

### `users`
- `id` (`uuid`, PK)
- `username` (`varchar(32)`, unique, normalized lowercase)
- `email` (`varchar(254)`, unique, normalized lowercase)
- `display_name` (`varchar(50)`)
- `bio` (`varchar(120)`)
- `timezone` (`varchar(64)`, default `"UTC"`)
- `avatar_data_url` (`text`, nullable — validated ≤ 2 MB image data URL)
- `role` (`varchar(16)` — `"Owner"` or `"Member"`; **partial unique index** `WHERE role = 'Owner'` enforces strictly one Owner)
- `is_active` (`boolean`, default `true` — deactivated members cannot sign in, refresh, or use PATs)
- `password_hash` (`text`, required)
- `password_updated_at` (`timestamptz`)
- `created_at` (`timestamptz`)
- `updated_at` (`timestamptz`)

### `user_preferences` (1:1 with `users`, PK/FK `user_id`)
- `user_id` (`uuid`, PK, FK → `users.id` cascade delete)
- `theme` (`varchar(16)` — `"system" | "light" | "dark"`)
- `week_starts_on` (`varchar(16)` — `"monday" | "sunday"`)
- `time_format` (`varchar(8)` — `"24h" | "12h"`)
- `require_agent_approval` (`boolean`, default `true`)
- `morning_brief` (`boolean`, default `true`)
- `event_reminders` (`boolean`, default `true`)

### `refresh_sessions`
- `id` (`uuid`, PK)
- `user_id` (`uuid`, FK → `users.id` cascade delete, indexed)
- `family_id` (`uuid`, indexed — groups rotated tokens for reuse detection)
- `family_created_at` (`timestamptz` — anchors the 90-day absolute session cap)
- `token_hash` (`varchar(64)`, unique — hex SHA-256 of opaque 32-byte refresh token)
- `device_label` (`varchar(120)` — concise browser/OS summary parsed from `User-Agent`)
- `ip_address` (`varchar(64)`)
- `created_at` (`timestamptz`)
- `last_active_at` (`timestamptz`)
- `expires_at` (`timestamptz` — `min(UtcNow + 30d, family_created_at + 90d)`)
- `revoked_at` (`timestamptz`, nullable)
- `replaced_by_session_id` (`uuid`, nullable)

### `invites`
- `id` (`uuid`, PK)
- `created_by_user_id` (`uuid`, FK → `users.id` cascade delete)
- `code_prefix` (`varchar(12)` — displayable hint e.g. `inv_a1b2c3`)
- `code_hash` (`varchar(64)`, unique — hex SHA-256 of full invite code)
- `label` (`varchar(80)`, nullable — e.g. *"Partner's laptop"*)
- `expires_at` (`timestamptz`)
- `used_at` (`timestamptz`, nullable)
- `used_by_user_id` (`uuid`, nullable, FK → `users.id` set null)
- `revoked_at` (`timestamptz`, nullable)
- `created_at` (`timestamptz`)

### `agent_tokens`
- `id` (`uuid`, PK)
- `user_id` (`uuid`, FK → `users.id` cascade delete, indexed)
- `name` (`varchar(80)`)
- `prefix` (`varchar(20)` — e.g. `atr_8f3a1c2b`)
- `token_hash` (`varchar(64)`, unique — hex SHA-256 of full secret)
- `scopes` (`varchar(256)` — comma-delimited allowlisted scopes, normalized)
- `created_at` (`timestamptz`)
- `last_used_at` (`timestamptz`, nullable)
- `revoked_at` (`timestamptz`, nullable)

## 7. API Surface

### Public / Auth Endpoints (rate-limited)
- `GET /api/auth/status` → `{ initialized: boolean }`.
- `POST /api/auth/setup` → `{ username, email, displayName, password }` — succeeds **only** if `users` table is empty (enforced by transaction + partial unique index `WHERE role = 'Owner'`). Creates the sole `Owner` + default preferences, sets `HttpOnly` refresh cookie, returns `{ accessToken, expiresAt, user, preferences }`. Returns `409 Conflict` if already initialized.
- `POST /api/auth/login` → `{ identifier, password }` (`identifier` matches normalized username or email; rejects deactivated users with `401`). Sets `HttpOnly` refresh cookie, returns `{ accessToken, expiresAt, user, preferences }`.
- `POST /api/auth/register` → `{ inviteCode, username, email, displayName, password }` — atomically validates & marks invite used, creates `Member` user + default preferences, sets refresh cookie, returns `{ accessToken, expiresAt, user, preferences }`.
- `POST /api/auth/refresh` (requires `X-Atrium-CSRF: 1` header) → Reads `atrium_refresh` `HttpOnly` cookie, validates user is active + hash/expiry/revocation:
  - If valid: marks current row `revoked_at` + `replaced_by_session_id`, inserts new `refresh_sessions` row in same `family_id` with `family_created_at` preserved and `expires_at = min(UtcNow + 30d, family_created_at + 90d)`, sets new cookie, returns `{ accessToken, expiresAt }`.
  - If token was already rotated/revoked (**reuse detected**): revokes ALL active sessions in that `family_id`, clears cookie, returns `401`.
- `POST /api/auth/logout` (requires `X-Atrium-CSRF: 1` header) → Revokes current refresh session (from cookie), clears cookie, returns `204`.

### Authenticated User Endpoints (JWT Bearer required; PATs rejected on account/security endpoints)
- `GET /api/account/me` → `{ user, preferences }`
- `PUT /api/account/profile` → updates `displayName`, `username`, `email`, `bio`, `timezone`, `avatarDataUrl` (enforces uniqueness & 2 MB avatar cap).
- `PUT /api/account/preferences` → updates `theme`, `weekStartsOn`, `timeFormat`, `requireAgentApproval`, `morningBrief`, `eventReminders`.
- `POST /api/account/password` → `{ currentPassword, newPassword }` — verifies current password, enforces 12+ char mixed policy, updates hash & `password_updated_at`, revokes all *other* refresh sessions.
- `GET /api/account/sessions` → lists active non-expired, non-revoked sessions for current user (marking `isCurrent` via `sid` claim in JWT).
- `DELETE /api/account/sessions/{id}` → revokes a specific session belonging to current user.
- `POST /api/account/sessions/revoke-others` → revokes all active sessions except current `sid`.

### Scoped MCP Agent Token Endpoints (JWT Bearer required)
- `GET /api/account/tokens` → lists active (non-revoked) agent tokens (`id`, `name`, `prefix`, `scopes`, `createdAt`, `lastUsedAt`).
- `POST /api/account/tokens` → `{ name, scopes }` → validates scopes against allowlist, returns `201` with `{ token: AgentTokenDto, secret: "atr_..." }` (plaintext shown once).
- `DELETE /api/account/tokens/{id}` → marks `revoked_at = UtcNow`.
- `GET /api/agent/whoami` → Authenticated via **either** `atr_` PAT or JWT; returns `{ userId, username, authType, scopes }`.

### Household Invite & Member Management Endpoints (JWT Bearer + `Owner` role required)
- `GET /api/account/invites` → lists invites (`id`, `codePrefix`, `label`, `expiresAt`, `usedAt`, `revokedAt`, `status`).
- `POST /api/account/invites` → `{ label?, expiresInDays? }` (default 7, max 30) → always creates a `Member` invite; returns `201` with `{ invite, code, invitePath: "/#invite=<code>" }`.
- `DELETE /api/account/invites/{id}` → revokes an unused invite.
- `GET /api/account/members` → lists all household accounts (`id`, `username`, `displayName`, `email`, `role`, `isActive`, `createdAt`, `lastActiveAt`, `activeSessionCount`, `activeTokenCount`).
- `PATCH /api/account/members/{id}` → `{ isActive: boolean }` — Owner can deactivate/reactivate a `Member` (cannot target self/`Owner`). Deactivating immediately revokes all active `refresh_sessions` and `agent_tokens` for that member.
- `DELETE /api/account/members/{id}` → permanently deletes a `Member` account and cascades their sessions/tokens/preferences (cannot target self/`Owner`).

## 8. Configuration, Secrets & Deployment Wiring (Builder Calls)
- **JWT Signing Key (`Atrium:Auth:JwtSigningKey` / `ATRIUM_JWT_SECRET`):**
  - Production **fails fast** on startup with `InvalidOperationException` if missing, `< 32` bytes, or matching the well-known dev sentinel (`atrium-dev-only-jwt-signing-key-do-not-use-in-production`).
  - `appsettings.Development.json` provides that dev sentinel so `dotnet run` / F5 Dev works with zero setup.
  - `.env.example` leaves `ATRIUM_JWT_SECRET=` blank with a clear comment so `cp .env.example .env` can never accidentally deploy a committed key.
  - `docker-compose.yml` wires `Atrium__Auth__JwtSigningKey: ${ATRIUM_JWT_SECRET:?ATRIUM_JWT_SECRET must be set}` and `scripts/setup-server.sh` auto-generates a 48-byte random `ATRIUM_JWT_SECRET` (just like `POSTGRES_PASSWORD`).
  - `scripts/prod.mjs` generates a local random `ATRIUM_JWT_SECRET` in memory (with a `[prod]` console notice) if unset, so F5 `Atrium: Production` boots cleanly locally.
- **Refresh Cookie (`atrium_refresh`):**
  - `HttpOnly = true`, `SameSite = SameSiteMode.Lax`, `Path = "/api/auth"`, `Secure = context.Request.IsHttps`.
- **Database Migrations:**
  - Standard EF Core migrations in `api/Atrium.Api/Data/Migrations/`.
  - Auto-apply pending migrations on startup when `Atrium:Database:AutoMigrate` is `true` (default `true` for self-hosted simplicity; `false` in unit tests).
- **CSRF Protection:**
  - `SameSite=Lax` cookie + required `X-Atrium-CSRF: 1` header on `POST /api/auth/refresh` and `POST /api/auth/logout`.

## 9. Frontend Integration
- New `web/src/lib/authClient.ts`:
  - Holds `accessToken` in module memory (never `localStorage`).
  - `apiFetch()` attaches `Authorization: Bearer <accessToken>`, retries once on `401` via deduplicated `POST /api/auth/refresh`, and signs out cleanly if refresh fails.
  - On mount: checks `GET /api/auth/status`, attempts silent `POST /api/auth/refresh` + `GET /api/account/me`.
- New `web/src/components/AuthScreen.tsx` (following [style-guide.md](file:///home/andrish/Work/personal/assistant/docs/design/style-guide.md)):
  - Replaces `WelcomeScreen.tsx` completely (unauthenticated visitors never see dashboard stats).
  - Modes: **Initial Owner Setup** (`!initialized`), **Sign In** (`initialized`), **Accept Invite** (auto-selected via `#invite=<code>` or toggle).
- Dashboard & Account updates (`App.tsx`, `TopBar.tsx`, `AccountPage.tsx`, `CommandPalette.tsx`):
  - Delete `WelcomeScreen.tsx` and remove `Welcome` nav/command items; render a subtle, compact welcome pill/badge on the Dashboard header instead.
  - Remove `Reset Account` and its reset dialog from `AccountPage.tsx` (keep `Export personal archive`).
  - Wire profile, preferences, password change, active sessions, and scoped MCP tokens to real API endpoints.
  - Add Owner-only **Household Invites** and **Household Members** cards in `AccountPage.tsx`, plus **Sign Out** in `TopBar.tsx` and `AccountPage.tsx`.
  - Keep Passkeys section clearly labeled as *"Coming next (WebAuthn follow-up)"*.

## 10. What's Left to the Builder
- Exact EF Core migration naming and Fluent API index configuration details.
- Concise User-Agent parser for human-readable `device_label` (e.g., `"Chrome on Linux"`).
- Exact CSS class names in `web/src/styles.css` (within the tokens and conventions of `docs/design/style-guide.md`).
- Test harness strategy: EF Core SQLite in-memory (or Npgsql when live Postgres is available) so `dotnet test` runs fast and reliably both locally and in GitHub Actions without requiring a Docker daemon during unit/integration tests.
