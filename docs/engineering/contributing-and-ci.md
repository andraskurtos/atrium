# Atrium — Branching, Pull Requests, CI & Warning Justification Policy

This document defines how changes reach `main`, what GitHub Actions enforces on every Pull Request, and how unavoidable compiler/linter warnings must be justified. Any human developer or AI agent working in this repository **must** follow these rules.

---

## 1. Branching Model & `main` Protection

- **`main` is protected:** Direct pushes to `main` are forbidden. Every change must go through a Pull Request targeting `main` with all CI checks passing.
- **Allowed branch prefixes:**
  - `feature/<short-slug>` — new features, refactors, docs, or tooling (e.g. `feature/passkey-auth`, `feature/warm-swiss-shell`)
  - `bugfix/<short-slug>` — bug fixes and regressions (e.g. `bugfix/health-503-handling`)
- **Naming rule (enforced by CI & local hook):**
  ```regex
  ^(feature|bugfix)\/[a-z0-9][a-z0-9._-]*$
  ```
  Lowercase letters, digits, `.`, `_`, and `-` only after the prefix.

### Local Git Hook (Recommended)
Install the repository's local `pre-push` hook once per clone so invalid pushes are caught before they even hit GitHub:
```bash
npm run hooks:install
```
This configures `core.hooksPath` to `.githooks`, blocking direct pushes to `main` and validating `feature/*` / `bugfix/*` branch names via `scripts/check-branch-name.mjs`.

### Enabling Branch Protection on GitHub (`andraskurtos/atrium`)
Because GitHub admin branch-protection settings cannot be written by local git alone, enable them once in **GitHub → Settings → Rules → Rulesets → New branch ruleset** (or **Settings → Branches → Add branch protection rule**):

1. **Target branch:** `main` (Default branch)
2. **Enable:**
   - **Restrict deletions** & **Block force pushes**
   - **Require a pull request before merging** (blocks direct pushes to `main`)
   - **Require status checks to pass before merging** (and **Require branches to be up to date before merging**)
3. **Add all 4 required status checks** (these appear once `.github/workflows/pr-checks.yml` has run on at least one PR):
   - `Branch Policy`
   - `Suppression Audit`
   - `Backend (.NET 10)`
   - `Frontend (Web PWA)`
4. *(Optional)* Restrict branch creation matching `^(feature|bugfix)/.*` if you also want GitHub itself to block pushing other branch prefixes.

> **Note for private repositories:** GitHub Branch Protection / Rulesets on *private* repositories require a GitHub Pro/Team/Enterprise plan (or making the repo public). On GitHub Free for private repos, the `.github/workflows/pr-checks.yml` checks still run on every PR and `.githooks/pre-push` enforces the policy locally.

---

## 2. GitHub Actions PR Checks (`.github/workflows/pr-checks.yml`)

Every Pull Request targeting `main` runs four independent jobs:

| Job Name | What It Verifies | Fails Merge When |
| :--- | :--- | :--- |
| **Branch Policy** | Validates `github.head_ref` via `scripts/check-branch-name.mjs` | Source branch is not `feature/*` or `bugfix/*` |
| **Suppression Audit** | Runs `scripts/check-suppressions.test.mjs` and `scripts/check-suppressions.mjs` | Any undocumented warning/error suppression or banned directive exists |
| **Backend (.NET 10)** | `dotnet restore`, `dotnet build -c Release -warnaserror`, `dotnet test -c Release` | Any C# compiler/analyzer warning, build error, or test failure |
| **Frontend (Web PWA)** | `npm ci`, `npm run typecheck`, `npm run build`, PWA artifact check, `docker compose config --quiet` | Any TypeScript strict error, unused local/param, Vite build failure, missing `manifest.webmanifest`/`sw.js`, or invalid Compose file |

Run the exact same verification locally before opening a PR:
```bash
npm run check:all
```

---

## 3. Zero Undocumented Warnings & Errors Policy

Atrium treats **all compiler and static analysis warnings as errors**:
- **Backend (.NET / C#):** `api/Directory.Build.props` sets `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` and `<AnalysisLevel>latest-recommended</AnalysisLevel>`, and CI passes `-warnaserror`.
- **Frontend (TypeScript / Vite):** `web/tsconfig.app.json` enforces `"strict": true`, `"noUnusedLocals": true`, and `"noUnusedParameters": true`.

### When May a Warning Be Suppressed?
Suppressing a warning is a **last resort**, permitted only when:
1. Fixing the warning is impossible without breaking correctness, security, or an external SDK/library contract (e.g., an upstream package bug or generated code), **and**
2. The suppression is scoped as narrowly as possible (single line or single member—never file-wide or project-wide unless unavoidable), **and**
3. The suppression carries an explicit, specific **`Justification: <reason>`** explaining *why* the warning cannot be fixed and *when* it can be removed.

### Approved Justification Syntax (Checked by `scripts/check-suppressions.mjs`)

#### C# Inline Pragma
Always restore the warning immediately after the affected line(s):
```csharp
#pragma warning disable CA1031 // Justification: health endpoint must convert any DB driver fault into HTTP 503
canConnect = await dbHealth.CanConnectAsync(ct);
#pragma warning restore CA1031
```

#### C# `[SuppressMessage]` Attribute
Must include a non-empty `Justification = "..."` argument:
```csharp
[SuppressMessage("Design", "CA1031", Justification = "Health probe intentionally catches all DB transport errors")]
```

#### MSBuild `<NoWarn>` (`.csproj` / `.props`)
Must have a `<!-- Justification: ... -->` XML comment on the same or immediately preceding line, **and** must be recorded in [warning-justifications.md](./warning-justifications.md):
```xml
<!-- Justification: EF Core generated migration snapshots omit XML doc comments -->
<NoWarn>CS1591</NoWarn>
```

#### TypeScript / JavaScript
Use `@ts-expect-error` (which automatically fails once the underlying issue is fixed) with an explicit `Justification:`:
```ts
// @ts-expect-error Justification: testing runtime handling of malformed API payload
parseHealthPayload({ unexpected: true });
```
Similarly for ESLint (if added later):
```ts
// eslint-disable-next-line no-console -- Justification: CLI runner intentionally logs to stdout
```

### Strictly Forbidden (Always Rejected by CI)
- `@ts-ignore` and `@ts-nocheck` (use `@ts-expect-error` with `Justification:` instead)
- `<TreatWarningsAsErrors>false</TreatWarningsAsErrors>`
- Any `#pragma warning disable`, `[SuppressMessage]`, `<NoWarn>`, `@ts-expect-error`, or `eslint-disable` without a `Justification:`
- Vague justifications like `Justification: fix later` or `Justification: ignore`

### Logging Repo-Wide or Non-Trivial Justifications
Any project-level build property workaround (e.g., in `Directory.Build.props` or `vite.config.ts`) or recurring suppression must also be logged in [warning-justifications.md](./warning-justifications.md) with:
- Code / setting name
- Exact file location
- Technical reason
- Removal criteria (what upstream fix lets us delete it)
