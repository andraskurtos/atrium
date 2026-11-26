# Atrium Auth, Accounts, Invites & Scoped MCP Tokens Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement invite-only multi-user authentication (single Owner + Member accounts), rotating `HttpOnly` refresh-token sessions with reuse detection, short-lived in-memory JWTs, SHA-256-hashed scoped MCP agent tokens, Owner member management, and full Warm Swiss Atelier frontend wiring on `feature/auth-and-accounts`.

**Architecture:** Custom minimal auth on EF Core 10 (`Npgsql.EntityFrameworkCore.PostgreSQL` for Postgres, `Microsoft.EntityFrameworkCore.Sqlite` for fast deterministic integration tests), ASP.NET Core `PasswordHasher<User>`, `JwtBearer` + custom `Bearer atr_...` PAT auth scheme, built-in rate limiting, and React `authClient.ts` + `AuthScreen.tsx` replacing `WelcomeScreen.tsx` and wiring `AccountPage.tsx`.

**Tech Stack:** .NET 10, ASP.NET Core Minimal API, EF Core 10 (Npgsql + Design + SQLite for tests), `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.0, xUnit, React 19, TypeScript, Vite PWA.

**Spec:** [2026-11-25-atrium-auth-design.md](file:///home/andrish/Work/personal/assistant/docs/superpowers/specs/2026-11-25-atrium-auth-design.md)

## Global Constraints

- Branch: `feature/auth-and-accounts` (never commit to `main`).
- Strictly **one `Owner`** per instance (PostgreSQL partial unique index `WHERE role = 'Owner'` on `users`); all invites create `Member` accounts.
- JWT access tokens: 15-minute TTL, HS256, held **in memory only** in React (never `localStorage`).
- Refresh tokens: opaque 32-byte random tokens, SHA-256 hashed in `refresh_sessions`, `HttpOnly; SameSite=Lax; Path=/api/auth; Secure=IsHttps` cookie, **30-day sliding window / 90-day absolute cap** (`family_created_at`), full-family revocation on reuse.
- CSRF guard: `POST /api/auth/refresh` and `POST /api/auth/logout` require `X-Atrium-CSRF: 1`.
- Production fails fast on startup if `Atrium:Auth:JwtSigningKey` is missing, `< 32` bytes, or matches the dev sentinel (`atrium-dev-only-jwt-signing-key-do-not-use-in-production`).
- CI compliance: `npm run check:all` must pass with **0 warnings, 0 errors, 0 undocumented suppressions** (`TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended`, strict TypeScript).
- UI must strictly follow [style-guide.md](file:///home/andrish/Work/personal/assistant/docs/design/style-guide.md) (Warm Tactile Swiss + Focus Stream, Light & Dark mode).
- `WelcomeScreen.tsx` and `Reset Account` are removed; a subtle welcome badge is added to the Dashboard header.

## Review Focus

- **Concurrent Owner Setup Race:** Two simultaneous `POST /api/auth/setup` requests on an empty DB must never create two Owners — one succeeds (`200`), the other returns `409 Conflict`.
- **Refresh Token Replay / Reuse Attack:** Presenting an already-rotated `atrium_refresh` cookie must revoke every active session in that `family_id` and return `401`, so the legitimate holder's next refresh also fails.
- **Deactivated Member Lockout:** When Owner deactivates a `Member`, that member's existing refresh sessions and MCP PATs are immediately revoked, and subsequent login/refresh/PAT calls fail with `401`.
- **PAT Isolation on Account Endpoints:** A valid `Bearer atr_...` MCP token must succeed on `GET /api/agent/whoami` with its scopes, but MUST be rejected (`401`/`403`) on `/api/account/*` endpoints (which require a human JWT).
- **Production JWT Key Guard:** Starting in `Production` with empty, `< 32`-byte, or dev-sentinel `Atrium:Auth:JwtSigningKey` must throw `InvalidOperationException` on startup.

---

### Task 1: NuGet Packages, Entities, `AppDbContext`, EF Migration & Config Wiring

**Files:**
- Modify: `api/Atrium.Api/Atrium.Api.csproj`
- Modify: `api/Atrium.Api.Tests/Atrium.Api.Tests.csproj`
- Create: `api/Atrium.Api/Data/Entities/User.cs`
- Create: `api/Atrium.Api/Data/Entities/UserPreferences.cs`
- Create: `api/Atrium.Api/Data/Entities/RefreshSession.cs`
- Create: `api/Atrium.Api/Data/Entities/Invite.cs`
- Create: `api/Atrium.Api/Data/Entities/AgentToken.cs`
- Modify: `api/Atrium.Api/Data/AppDbContext.cs`
- Create: `api/Atrium.Api/Data/Migrations/<timestamp>_InitialAuthAndAccounts.cs` (via `dotnet ef` or hand-authored EF migration)
- Modify: `api/Atrium.Api/appsettings.json`
- Modify: `api/Atrium.Api/appsettings.Development.json`
- Modify: `.env.example`
- Modify: `docker-compose.yml`
- Modify: `scripts/prod.mjs`
- Modify: `scripts/setup-server.sh`
- Create: `api/Atrium.Api.Tests/TestAppFactory.cs`
- Create: `api/Atrium.Api.Tests/StartupSecurityTests.cs`

**Interfaces:**
- Produces:
  - Entities: `User`, `UserPreferences`, `RefreshSession`, `Invite`, `AgentToken` in `Atrium.Api.Data.Entities`.
  - `AppDbContext` exposing `DbSet<User> Users`, `DbSet<UserPreferences> UserPreferences`, `DbSet<RefreshSession> RefreshSessions`, `DbSet<Invite> Invites`, `DbSet<AgentToken> AgentTokens`, configured with unique indexes (`username`, `email`, `token_hash`, `code_hash`, and partial unique index `WHERE role = 'Owner'` on Postgres).
  - `AuthOptions` bound from `Atrium:Auth` (`JwtSigningKey`, `Issuer`, `Audience`, `AccessTokenMinutes`, `RefreshSlidingDays`, `RefreshAbsoluteDays`).
  - `TestAppFactory` (shared `WebApplicationFactory<Program>` using isolated SQLite in-memory DB + `AutoMigrate=false` + valid test JWT key).

- [ ] **Step 1: Add required NuGet packages (`10.0.0`)**
  - `Atrium.Api`: `Microsoft.AspNetCore.Authentication.JwtBearer` (`10.0.0`), `Microsoft.EntityFrameworkCore.Design` (`10.0.0`, PrivateAssets=all).
  - `Atrium.Api.Tests`: `Microsoft.EntityFrameworkCore.Sqlite` (`10.0.0`).
- [ ] **Step 2: Write failing tests in `StartupSecurityTests.cs`**
  - Assert Production startup throws `InvalidOperationException` when `Atrium:Auth:JwtSigningKey` is empty, `< 32` bytes, or equals `atrium-dev-only-jwt-signing-key-do-not-use-in-production`.
- [ ] **Step 3: Implement entities, `AppDbContext` model configuration, EF Core migration, and `AuthOptions` startup validation**
  - Wire `appsettings.json`, `appsettings.Development.json`, `.env.example`, `docker-compose.yml`, `scripts/prod.mjs` (ephemeral local secret if unset), and `scripts/setup-server.sh` (auto-generate `ATRIUM_JWT_SECRET`).
  - Update existing `HealthEndpointTests` and `StaticSpaServingTests` to supply a valid test `Atrium:Auth:JwtSigningKey` and `Atrium:Database:AutoMigrate=false`.
- [ ] **Step 4: Run `dotnet test api/Atrium.sln` and `docker compose config --quiet`**
  Expected: All existing + new `StartupSecurityTests` pass with 0 warnings.
- [ ] **Step 5: Commit Task 1**
  `git commit -m "feat(api): add auth EF Core schema, migrations, and JWT secret validation"`

---

### Task 2: Core Auth Services & Endpoints (Setup, Login, Register, Refresh Rotation, Logout)

**Files:**
- Create: `api/Atrium.Api/Auth/AuthConstants.cs`
- Create: `api/Atrium.Api/Auth/TokenCrypto.cs` (CSPRNG tokens, SHA-256 hex hashing, password policy validator, User-Agent device parser)
- Create: `api/Atrium.Api/Auth/JwtTokenService.cs` (creates HS256 15-min JWT with `sub`, `unique_name`, `role`, `sid`, `auth_type=jwt`)
- Create: `api/Atrium.Api/Auth/RefreshCookieHelper.cs` (sets/clears `atrium_refresh` cookie + checks `X-Atrium-CSRF: 1`)
- Create: `api/Atrium.Api/Auth/AuthContracts.cs` (request/response DTOs)
- Create: `api/Atrium.Api/Endpoints/AuthEndpoints.cs` (`GET /api/auth/status`, `POST /api/auth/setup`, `POST /api/auth/login`, `POST /api/auth/register`, `POST /api/auth/refresh`, `POST /api/auth/logout`)
- Modify: `api/Atrium.Api/Program.cs` (register services, `JwtBearer`, rate limiting on `/api/auth/*`, map `AuthEndpoints`)
- Create: `api/Atrium.Api.Tests/AuthFlowTests.cs`

**Interfaces:**
- Consumes: Task 1 entities, `AppDbContext`, `AuthOptions`, `TestAppFactory`.
- Produces:
  - `AuthConstants.JwtScheme`, `AuthConstants.PatScheme`, `AuthConstants.OwnerRole`, `AuthConstants.MemberRole`, `AuthConstants.AllowedScopes`.
  - Auth endpoints returning `{ accessToken, expiresAt, user, preferences }` and setting/rotating `atrium_refresh`.

- [ ] **Step 1: Write failing tests in `AuthFlowTests.cs`**
  - `Status_ReturnsUninitialized_OnEmptyDatabase`
  - `Setup_CreatesSoleOwner_AndBlocksSecondSetupWith409`
  - `Setup_RejectsWeakPassword`
  - `Login_SucceedsWithUsernameOrEmail_AndRejectsInvalidCredentialsWith401`
  - `Refresh_RotatesToken_AndRevokesEntireFamilyOnReplayReuse`
  - `Refresh_AndLogout_RequireCsrfHeader`
  - `Logout_RevokesSession_AndClearsCookie`
- [ ] **Step 2: Run tests to verify RED**
  Run: `dotnet test api/Atrium.sln --filter AuthFlowTests`
  Expected: FAIL.
- [ ] **Step 3: Implement `TokenCrypto`, `JwtTokenService`, `RefreshCookieHelper`, `AuthEndpoints`, and `Program.cs` wiring (including rate limiting)**
- [ ] **Step 4: Run `dotnet test api/Atrium.sln`**
  Expected: All tests PASS with 0 warnings.
- [ ] **Step 5: Commit Task 2**
  `git commit -m "feat(api): add setup, login, rotating refresh sessions, and rate-limited auth endpoints"`

---

### Task 3: Account, Profile, Preferences, Sessions, Invites, Member Management & Scoped MCP PATs

**Files:**
- Create: `api/Atrium.Api/Auth/PatAuthenticationHandler.cs` (validates `Authorization: Bearer atr_...`, checks SHA-256 hash, `revoked_at == null`, `user.is_active == true`, throttled `last_used_at` update, emits `scope` claims + `auth_type=pat`)
- Create: `api/Atrium.Api/Endpoints/AccountEndpoints.cs` (`GET /api/account/me`, `PUT /api/account/profile`, `PUT /api/account/preferences`, `POST /api/account/password`, `GET /api/account/sessions`, `DELETE /api/account/sessions/{id}`, `POST /api/account/sessions/revoke-others`)
- Create: `api/Atrium.Api/Endpoints/TokenEndpoints.cs` (`GET /api/account/tokens`, `POST /api/account/tokens`, `DELETE /api/account/tokens/{id}`, `GET /api/agent/whoami`)
- Create: `api/Atrium.Api/Endpoints/HouseholdEndpoints.cs` (Owner-only: `GET/POST/DELETE /api/account/invites`, `GET/PATCH/DELETE /api/account/members`)
- Modify: `api/Atrium.Api/Program.cs` (register `PatAuthenticationHandler`, policies `HumanJwtOnly`, `OwnerOnly`, `HumanOrAgent`, map endpoints)
- Create: `api/Atrium.Api.Tests/AccountAndTokensTests.cs`
- Create: `api/Atrium.Api.Tests/HouseholdInvitesAndMembersTests.cs`

**Interfaces:**
- Consumes: Task 1 & Task 2 auth infrastructure and `TestAppFactory`.
- Produces: Complete REST API surface specified in Section 7 of the design doc.

- [ ] **Step 1: Write failing tests in `AccountAndTokensTests.cs` and `HouseholdInvitesAndMembersTests.cs`**
  - Profile & preferences CRUD (including duplicate username/email 409 and >2 MB avatar rejection).
  - Password change verifies current password, updates `passwordUpdatedAt`, and revokes other sessions.
  - Session listing (`isCurrent`), single revoke, and revoke-others.
  - MCP PAT creation (one-time secret), `GET /api/agent/whoami` succeeds with scopes, PAT rejected on `/api/account/me`, revoked PAT returns `401`.
  - Invite creation (Owner-only, Member returns `403`), invite redemption via `POST /api/auth/register` creates `Member`, reuse of spent/revoked/expired invite returns `400`/`409`.
  - Member listing, deactivation immediately revokes member's sessions & PATs and blocks login/refresh/PAT, reactivation restores login, member deletion cascades, Owner cannot deactivate/delete self (`400`).
- [ ] **Step 2: Run tests to verify RED**
  Run: `dotnet test api/Atrium.sln --filter "AccountAndTokensTests|HouseholdInvitesAndMembersTests"`
  Expected: FAIL.
- [ ] **Step 3: Implement `PatAuthenticationHandler`, `AccountEndpoints`, `TokenEndpoints`, `HouseholdEndpoints`, and `Program.cs` authorization policies**
- [ ] **Step 4: Run `dotnet build -c Release -warnaserror api/Atrium.sln && dotnet test -c Release --no-build api/Atrium.sln`**
  Expected: 0 warnings, 0 errors, all tests PASS.
- [ ] **Step 5: Commit Task 3**
  `git commit -m "feat(api): add account, sessions, invites, member management, and scoped MCP tokens"`

---

### Task 4: Frontend Auth Client, Atelier `AuthScreen`, Welcome Badge & Real `AccountPage` Wiring

**Files:**
- Create: `web/src/lib/authClient.ts` (in-memory JWT, deduplicated refresh-on-401, typed API methods for auth, account, sessions, tokens, invites, members)
- Create: `web/src/components/AuthScreen.tsx` (Warm Swiss Atelier UI: Initial Owner Setup, Sign In, Accept Invite with `#invite=<code>` auto-detection)
- Delete: `web/src/components/WelcomeScreen.tsx`
- Modify: `web/src/types/atrium.ts` (add `UserRole`, `InviteItem`, `HouseholdMember`, update `UserProfile`, `SessionItem`, `AgentToken`, `AppView`)
- Modify: `web/src/App.tsx` (auth bootstrap on mount, gate behind `AuthScreen`, wire real profile/preferences/password/sessions/tokens/invites/members, add compact welcome badge on dashboard, remove `WelcomeScreen` & `Reset Account` wiring)
- Modify: `web/src/components/TopBar.tsx` (remove `Welcome` link, add `Sign Out` action)
- Modify: `web/src/components/CommandPalette.tsx` (remove `Welcome` command, add `Sign Out` command)
- Modify: `web/src/components/AccountPage.tsx` (wire async API handlers + inline error states, add Owner-only **Household Invites** and **Household Members** cards, remove `Reset Account` + reset dialog, mark Passkeys as *"Coming next (WebAuthn follow-up)"*, add Sign Out button)
- Modify: `web/src/styles.css` (styles for `AuthScreen`, dashboard welcome badge, invites/members rows — strictly Warm Tactile Swiss tokens from `docs/design/style-guide.md`)
- Modify: `web/src/data/mockData.ts` (remove obsolete mock auth/session/token/passkey generators no longer used)

**Interfaces:**
- Consumes: All `/api/auth/*` and `/api/account/*` endpoints from Tasks 2 & 3.
- Produces: Fully authenticated Atelier PWA with zero `localStorage` token storage and real Postgres-backed accounts.

- [ ] **Step 1: Consult `modern-web-guidance` for auth form accessibility/autofill**
  Run `npx -y modern-web-guidance@latest search "login form autofill validation"` (or retrieve relevant `forms` guide) before writing `AuthScreen.tsx`.
- [ ] **Step 2: Implement `web/src/lib/authClient.ts` and update `web/src/types/atrium.ts`**
- [ ] **Step 3: Implement `web/src/components/AuthScreen.tsx` and delete `web/src/components/WelcomeScreen.tsx`**
- [ ] **Step 4: Wire `App.tsx`, `TopBar.tsx`, `CommandPalette.tsx`, `AccountPage.tsx`, `mockData.ts`, and `styles.css`**
- [ ] **Step 5: Run `npm run check:all`**
  Expected: Suppression audit passes, `.NET 10` Release build has 0 warnings/0 errors, all backend tests pass, `tsc -b --noEmit` passes, `vite build` emits `manifest.webmanifest` + `sw.js`.
- [ ] **Step 6: Commit Task 4**
  `git commit -m "feat(web): add Atelier auth screen, live account/session/token/invite wiring, and dashboard welcome badge"`

---

### Task 5: End-to-End Verification, Whole-Branch Review & PR Readiness

- [ ] **Step 1: Run full `npm run check:all` + `docker compose config --quiet`**
  Expected: Exit 0, zero warnings, zero errors, all tests passing.
- [ ] **Step 2: Dispatch fresh whole-branch code reviewer (`pro`)**
  Verify all 5 Review Focus items, security properties, and `style-guide.md` compliance. Fix any Critical/Important findings in one TDD pass.
- [ ] **Step 3: Verify clean `git status` and branch history on `feature/auth-and-accounts`**
  Ready for `git push -u origin feature/auth-and-accounts` and opening the PR to `main`.

