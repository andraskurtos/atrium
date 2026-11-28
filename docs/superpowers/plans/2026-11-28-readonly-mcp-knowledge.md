# Read-Only MCP Server & Artifact Knowledge Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose a strictly read-only Streamable HTTP MCP server at `/mcp` inside `Atrium.Api` authenticated via scoped Agent PATs, backed by automatic PDF/PPTX/DOCX/TXT text extraction, Postgres full-text search, and a quick-connect snippet in `AccountPage`.

**Architecture:** Add `AttachmentTextChunk` (`attachment_text_chunks`) and extraction metadata on `Attachment`, extract text on upload (plus startup background backfill and lazy on-demand fallback) using `UglyToad.PdfPig` and built-in `ZipArchive` XML parsing, expose shared scope-and-privacy-enforced read/search logic in `KnowledgeReadService`, and mount 9 read-only MCP tools at `/mcp` via `ModelContextProtocol.AspNetCore` (`Stateless = true`, `AgentPatOnlyPolicy`).

**Tech Stack:** .NET 10 Minimal API, EF Core 10 (PostgreSQL + SQLite in-memory tests), `ModelContextProtocol.AspNetCore`, `UglyToad.PdfPig`, xUnit, React 19, TypeScript.

**Spec:** `docs/superpowers/specs/2026-11-28-readonly-mcp-knowledge-design.md`

## Global Constraints

- .NET 10 with `-warnaserror` and zero undocumented warning suppressions (`npm run check:suppressions` must pass).
- EF Core migrations must stay in sync with `AppDbContext` (`MigrationModelTests` must pass).
- `/mcp` MUST require `AuthConstants.AgentPatOnlyPolicy` (only `atr_...` Agent PATs; never accept Human JWT) and use stateless Streamable HTTP (`Stateless = true`). Exclude `/mcp` from the SPA fallback in `Program.cs`.
- Shared REST endpoints (`GET /api/knowledge/search`, `GET /api/attachments/{id}/text`) use `AuthConstants.HumanOrAgentPolicy` (Human JWT sees all own records; Agent PAT enforces scopes + `AllowAgentRead`).
- Extraction caps: 500,000 extracted chars per artifact, ~4,000 chars per chunk, 5-second extraction timeout on upload; malformed/encrypted/unsupported files MUST NOT fail `POST /api/attachments`.
- Never extract or expose `inline_image` attachments in text extraction, artifact listings, or knowledge search.
- SQLite test compatibility: order `DateTimeOffset` client-side and use case-insensitive substring matching when `Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"`, while using Postgres `tsvector` + GIN indexes on PostgreSQL.

## Review Focus

1. **Corrupt/encrypted/empty file resilience on upload:** Uploading a corrupted `.pdf` or invalid `.pptx` to `POST /api/attachments` must still return `201 Created` and record `text_extraction_status = "failed"` (or `"empty"` for blank PDF) without throwing `500`. *(Pinned in Task 2)*
2. **Strict `AgentPatOnlyPolicy` & scope error handling on `/mcp`:** Presenting a Human JWT to `/mcp` must be rejected (`401`/`403`), and calling a tool without its required scope (e.g. `list_events` without `calendar:read`) must return an MCP tool error (`isError: true`) without leaking data. *(Pinned in Task 4)*
3. **Privacy inheritance across notes, folders, and artifacts:** Hidden notes (`AllowAgentRead == false`), notes inside hidden folders, and artifacts on hidden notes/items/events must be excluded from `search_knowledge`, `list_notes_tree`, `read_note`, `list_artifacts`, and `read_artifact_text`. *(Pinned in Task 3 & Task 4)*
4. **Idempotent lazy backfill of pre-existing `"pending"` artifacts:** Calling `GET /api/attachments/{id}/text` or `read_artifact_text` on an artifact with `TextExtractionStatus == "pending"` extracts and persists chunks cleanly (and handles duplicate-chunk race safely). *(Pinned in Task 2 & Task 3)*
5. **Payload & range bounds:** `read_artifact_text` clamps `maxChunks` to `1..20`, `search_knowledge` clamps `limit` to `1..25`, and `list_events` caps the date range to 90 days. *(Pinned in Task 3 & Task 4)*

---

## File Map

**Backend — Create:**
- `api/Atrium.Api/Data/Entities/AttachmentTextChunk.cs` — extracted page/chunk entity
- `api/Atrium.Api/Data/Migrations/20261128120000_AddArtifactTextChunks.cs` — EF migration (+ PostgreSQL GIN FTS indexes)
- `api/Atrium.Api/Knowledge/ArtifactTextExtractor.cs` — `IArtifactTextExtractor` + `DefaultArtifactTextExtractor` (PDF via `PdfPig`, PPTX/DOCX via `ZipArchive` + XML, TXT/MD/CSV/JSON)
- `api/Atrium.Api/Knowledge/ArtifactTextService.cs` — extract-and-persist, lazy backfill, and `ArtifactTextBackfillService` (`BackgroundService`)
- `api/Atrium.Api/Knowledge/KnowledgeContracts.cs` — DTOs for search hits, paginated artifact text, and MCP responses
- `api/Atrium.Api/Knowledge/KnowledgeReadService.cs` — shared scoped/privacy-aware read & FTS search across Day, Calendar, University, Notes, and Artifacts
- `api/Atrium.Api/Endpoints/KnowledgeEndpoints.cs` — `GET /api/knowledge/search` and `GET /api/attachments/{id}/text`
- `api/Atrium.Api/Mcp/AtriumMcpTools.cs` — 9 strictly read-only `[McpServerTool]` methods
- `api/Atrium.Api.Tests/ArtifactTextExtractionTests.cs` — extraction unit & upload/backfill integration tests
- `api/Atrium.Api.Tests/KnowledgeAndMcpTests.cs` — REST search, MCP JSON-RPC `/mcp` protocol, PAT scope, and privacy tests

**Backend — Modify:**
- `api/Atrium.Api/Atrium.Api.csproj` — add `UglyToad.PdfPig` and `ModelContextProtocol.AspNetCore`
- `api/Atrium.Api/Auth/AuthConstants.cs` — add `AgentPatOnlyPolicy`
- `api/Atrium.Api/Data/Entities/Attachment.cs` — add `TextExtractionStatus`, `ExtractedPageCount`, `TextExtractedAt`, `TextChunks`
- `api/Atrium.Api/Data/AppDbContext.cs` & `AppDbContextModelSnapshot.cs` — configure `AttachmentTextChunk` and new `Attachment` columns
- `api/Atrium.Api/Attachments/AttachmentContracts.cs` — add optional `TextExtractionStatus` and `ExtractedPageCount` to `AttachmentDto`
- `api/Atrium.Api/Endpoints/AttachmentEndpoints.cs` — trigger `ArtifactTextService` on `kind == "artifact"` upload (with 5s timeout & fault isolation)
- `api/Atrium.Api/Program.cs` — add `AgentPatOnlyPolicy`, register knowledge/extraction services, `AddMcpServer().WithHttpTransport(o => o.Stateless = true).WithTools<AtriumMcpTools>()`, `MapKnowledgeEndpoints()`, `MapMcp("/mcp")`, exclude `/mcp` from SPA fallback
- `api/Atrium.Api.Tests/TestAppFactory.cs` — disable `ArtifactTextBackfillService` background loop in tests (`Atrium:Knowledge:EnableBackfill = "false"`)

**Frontend — Modify:**
- `web/src/components/AccountPage.tsx` — add collapsible **"Connect via MCP"** snippet inside Agent Tokens card
- `web/src/styles.css` — minimal styling for the MCP connect snippet box

---

### Task 1: Schema, Migration, `AttachmentTextChunk` & NuGet Packages

**Files:**
- Modify: `api/Atrium.Api/Atrium.Api.csproj`
- Modify: `api/Atrium.Api/Auth/AuthConstants.cs`
- Create: `api/Atrium.Api/Data/Entities/AttachmentTextChunk.cs`
- Modify: `api/Atrium.Api/Data/Entities/Attachment.cs`
- Modify: `api/Atrium.Api/Data/AppDbContext.cs`
- Create: `api/Atrium.Api/Data/Migrations/20261128120000_AddArtifactTextChunks.cs`
- Modify: `api/Atrium.Api/Data/Migrations/AppDbContextModelSnapshot.cs`
- Modify: `api/Atrium.Api/Attachments/AttachmentContracts.cs`
- Modify: `api/Atrium.Api.Tests/TestAppFactory.cs`
- Test: `api/Atrium.Api.Tests/MigrationModelTests.cs`

**Interfaces:**
- Produces:
  - `AuthConstants.AgentPatOnlyPolicy = "AgentPatOnly"`
  - `Attachment.TextExtractionStatus` (`"pending"` | `"completed"` | `"empty"` | `"unsupported"` | `"failed"`), `ExtractedPageCount`, `TextExtractedAt`
  - `AttachmentTextChunk` (`Id`, `AttachmentId`, `UserId`, `ChunkIndex`, `PageLabel`, `Content`, `CharCount`, `CreatedAt`)

- [ ] **Step 1: Add `UglyToad.PdfPig` and `ModelContextProtocol.AspNetCore` packages and restore**
  - Add package references in `api/Atrium.Api/Atrium.Api.csproj` and run `dotnet restore api/Atrium.sln`.
- [ ] **Step 2: Add `AgentPatOnlyPolicy` to `AuthConstants.cs`, extend `Attachment.cs` & `AttachmentContracts.cs`, and create `AttachmentTextChunk.cs`**
- [ ] **Step 3: Configure `AppDbContext`, create migration `20261128120000_AddArtifactTextChunks` (with Postgres-only GIN FTS indexes), and update `AppDbContextModelSnapshot.cs`**
- [ ] **Step 4: Run `MigrationModelTests` and verify 0 warnings/errors**
  - Run: `dotnet build --no-restore -c Release -warnaserror api/Atrium.sln && dotnet test -c Release --no-build --filter MigrationModelTests api/Atrium.Api.Tests/Atrium.Api.Tests.csproj`
  - Expected: PASS (2/2), 0 warnings.

---

### Task 2: Artifact Text Extractor, `ArtifactTextService`, Backfill & Upload Wiring

**Files:**
- Create: `api/Atrium.Api/Knowledge/ArtifactTextExtractor.cs`
- Create: `api/Atrium.Api/Knowledge/ArtifactTextService.cs`
- Modify: `api/Atrium.Api/Endpoints/AttachmentEndpoints.cs`
- Modify: `api/Atrium.Api/Program.cs`
- Test: `api/Atrium.Api.Tests/ArtifactTextExtractionTests.cs`

**Interfaces:**
- Consumes: `AppDbContext`, `Attachment`, `AttachmentTextChunk`, `IAttachmentStorage`
- Produces:
  - `IArtifactTextExtractor`: `ArtifactExtractionResult Extract(Stream stream, string fileName, string contentType, CancellationToken ct)`
  - `ArtifactTextService`: `Task ExtractAndPersistAsync(Attachment attachment, Stream contentStream, CancellationToken ct)`, `Task EnsureExtractedAsync(Guid attachmentId, Guid userId, CancellationToken ct)`
  - `ArtifactTextBackfillService : BackgroundService`

- [ ] **Step 1: Write failing tests in `api/Atrium.Api.Tests/ArtifactTextExtractionTests.cs`**
  - `Extract_PlainTextAndMarkdown_ChunksCleanly`: verifies `.md` / `.txt` extraction and ~4,000-char chunking.
  - `Extract_PptxAndDocx_ExtractsSlidesAndParagraphs`: builds a minimal valid `.pptx` and `.docx` ZIP in memory, verifies slide-order `"Slide 1"` / `"Slide 2"` and paragraph extraction.
  - `UploadCorruptPdf_StillReturns201_AndMarksStatusFailed`: uploads `"%PDF-1.4\ncorrupt bytes"` to `POST /api/attachments`, verifies `201 Created` and `TextExtractionStatus == "failed"` *(Review Focus #1)*.
  - `UploadValidTextArtifact_ExtractsImmediately_AndInlineImageIsSkipped`: uploads `.txt` artifact (`TextExtractionStatus == "completed"`) and `.png` inline image (0 chunks created).
  - `LazyBackfill_ExtractsPendingArtifactOnDemand`: seeds a `"pending"` artifact with file on disk, calls `EnsureExtractedAsync` twice, verifies chunks persisted once and idempotent *(Review Focus #4)*.
- [ ] **Step 2: Implement `ArtifactTextExtractor.cs`, `ArtifactTextService.cs`, wire into `AttachmentEndpoints.cs` & `Program.cs`**
- [ ] **Step 3: Run `ArtifactTextExtractionTests`**
  - Run: `dotnet build --no-restore -c Release -warnaserror api/Atrium.sln && dotnet test -c Release --no-build --filter ArtifactTextExtractionTests api/Atrium.Api.Tests/Atrium.Api.Tests.csproj`
  - Expected: All PASS, 0 warnings.

---

### Task 3: Shared `KnowledgeReadService` & REST Endpoints

**Files:**
- Create: `api/Atrium.Api/Knowledge/KnowledgeContracts.cs`
- Create: `api/Atrium.Api/Knowledge/KnowledgeReadService.cs`
- Create: `api/Atrium.Api/Endpoints/KnowledgeEndpoints.cs`
- Modify: `api/Atrium.Api/Program.cs` (register `KnowledgeReadService` and `app.MapKnowledgeEndpoints()`)

**Interfaces:**
- Consumes: `AppDbContext`, `ArtifactTextService`, `NoteFrontmatter`, `RRuleExpander`, `UniversityAttendanceCalculator`
- Produces:
  - `KnowledgeReadService` methods taking `(ClaimsPrincipal principal, bool enforceAgentPrivacy, ...)`:
    - `GetDayAsync(principal, enforceAgentPrivacy, date, ct)`
    - `ListEventsAsync(principal, enforceAgentPrivacy, from, to, calendarId, ct)`
    - `ListCoursesAsync(principal, enforceAgentPrivacy, semester, status, ct)`
    - `GetCourseAsync(principal, enforceAgentPrivacy, courseId, ct)`
    - `ListNotesTreeAsync(principal, enforceAgentPrivacy, includeUniversity, includeEvents, includeDaily, ct)`
    - `ReadNoteAsync(principal, enforceAgentPrivacy, noteId, ct)`
    - `ListArtifactsAsync(principal, enforceAgentPrivacy, noteId, courseId, itemId, eventId, occurrenceStartAt, ct)`
    - `ReadArtifactTextAsync(principal, enforceAgentPrivacy, attachmentId, startChunk, maxChunks, ct)`
    - `SearchKnowledgeAsync(principal, enforceAgentPrivacy, query, scope, courseId, limit, ct)`
  - REST endpoints:
    - `GET /api/attachments/{id:guid}/text?startChunk=0&maxChunks=10`
    - `GET /api/knowledge/search?q=...&scope=all&courseId=...&limit=10`

- [ ] **Step 1: Implement `KnowledgeContracts.cs`, `KnowledgeReadService.cs`, and `KnowledgeEndpoints.cs`**
  - Enforce scope checks, `AllowAgentRead` inheritance (folder chain + linked item/event note), pagination/range clamping (`maxChunks` 1..20, `limit` 1..25, `list_events` max 90 days), and Postgres FTS / SQLite fallback.
- [ ] **Step 2: Register in `Program.cs` and verify build**
  - Run: `dotnet build --no-restore -c Release -warnaserror api/Atrium.sln`
  - Expected: 0 warnings, 0 errors.

---

### Task 4: Read-Only MCP Server (`/mcp`, 9 Tools) & Integration Tests

**Files:**
- Create: `api/Atrium.Api/Mcp/AtriumMcpTools.cs`
- Modify: `api/Atrium.Api/Program.cs` (configure `AgentPatOnlyPolicy`, `AddMcpServer().WithHttpTransport(o => o.Stateless = true).WithTools<AtriumMcpTools>()`, `MapMcp("/mcp").RequireAuthorization(AuthConstants.AgentPatOnlyPolicy)`, exclude `/mcp` from SPA fallback)
- Test: `api/Atrium.Api.Tests/KnowledgeAndMcpTests.cs`

**Interfaces:**
- Consumes: `KnowledgeReadService`, `IHttpContextAccessor` (to read authenticated `ClaimsPrincipal`)
- Produces: Streamable HTTP MCP endpoint at `/mcp` exposing all 9 read-only tools (`get_day`, `list_events`, `list_courses`, `get_course`, `list_notes_tree`, `read_note`, `list_artifacts`, `read_artifact_text`, `search_knowledge`).

- [ ] **Step 1: Write failing tests in `api/Atrium.Api.Tests/KnowledgeAndMcpTests.cs`**
  - `McpEndpoint_RejectsHumanJwt_AndAllowsAgentPat`: sends MCP `tools/list` JSON-RPC request with Human JWT → `403`, with Agent PAT → `200` returning all 9 read-only tools *(Review Focus #2)*.
  - `McpTools_EnforceModuleScopes_AndReturnCleanScopeError`: PAT with only `notes:read` calling `list_events` returns MCP `isError: true` with scope message; calling `search_knowledge` returns matching notes without leaking university/calendar items *(Review Focus #2)*.
  - `McpTools_AndRestSearch_EnforceAllowAgentReadPrivacy`: hidden note, note in hidden folder, and artifact on hidden uni item are excluded from `search_knowledge`, `read_note`, and `read_artifact_text` for PAT, while Human JWT can still read them on REST *(Review Focus #3)*.
  - `McpTools_ReadArtifactText_PaginatesAndClampsBounds_AndListEventsCaps90Days`: verifies `startChunk`/`maxChunks` pagination and 90-day range cap *(Review Focus #5)*.
  - `McpTools_EndToEnd_DayCalendarUniversityNotesAndArtifacts`: exercises `get_day`, `list_events`, `list_courses`, `get_course`, `list_notes_tree`, `read_note`, `list_artifacts`, `read_artifact_text`, and `search_knowledge` with a multi-scope PAT.
- [ ] **Step 2: Implement `AtriumMcpTools.cs` and wire `/mcp` in `Program.cs`**
- [ ] **Step 3: Run full backend build and test suite**
  - Run: `dotnet build --no-restore -c Release -warnaserror api/Atrium.sln && dotnet test -c Release --no-build api/Atrium.sln`
  - Expected: All backend tests PASS, 0 warnings.

---

### Task 5: Account Page MCP Quick-Connect Snippet & Full CI Verification

**Files:**
- Modify: `web/src/components/AccountPage.tsx`
- Modify: `web/src/styles.css`

- [ ] **Step 1: Add collapsible "Connect via MCP" box in `AccountPage.tsx` Agent Tokens section**
  - Shows `${window.location.origin}/mcp` and a one-click **Copy config** button with a ready-to-paste MCP client JSON snippet using `Authorization: Bearer <your-agent-token>`.
- [ ] **Step 2: Add minimal CSS styling in `web/src/styles.css`**
- [ ] **Step 3: Run full CI verification suite**
  - Run:
    ```bash
    npm run check:suppressions && node --test scripts/check-suppressions.test.mjs && dotnet build --no-restore -c Release -warnaserror api/Atrium.sln && dotnet test -c Release --no-build api/Atrium.sln && npm --prefix web run typecheck && npm --prefix web run build
    ```
  - Expected: 0 suppression violations, 8/8 suppression tests pass, 0 C# warnings/errors, all xUnit tests pass, 0 TS errors, clean Vite + PWA build.

