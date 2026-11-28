# Read-Only MCP Server & Artifact Knowledge Search — Design Spec

**Date:** 2026-11-28  
**Status:** Draft (pending builder check & user approval)  
**Builds on:** Agent PATs (`AuthConstants`), Day Tracker, Calendar, University Courses, Notes Vault & Linked Artifacts

---

## 1. Intent & Why

You want to integrate a local Q&A AI assistant first — and later external AI agents — with Atrium so an assistant can read your daily checklist, calendar events, university courses/lectures, Markdown notes, and uploaded study artifacts (PDFs, PowerPoint slides, documents).

Exposing a **read-only Model Context Protocol (MCP) server** directly from `Atrium.Api`, backed by **automatic artifact text extraction** and **Postgres full-text search**, gives every MCP-compatible assistant one standard, secure interface using Atrium's existing scoped Agent PATs (`atr_...`) and per-note/folder `AllowAgentRead` privacy controls — while structuring extracted text into chunks that can accept `pgvector` embeddings in Phase 2 and `*:propose` write tools later.

---

## 2. Goals, Non-Goals & Anti-Goals

### Goals
1. **Built-in Streamable HTTP MCP Endpoint (`/mcp`):**
   - Hosted directly inside `Atrium.Api` using the official C# `ModelContextProtocol.AspNetCore` SDK (`Stateless = true` HTTP transport).
   - Authenticated with Atrium's existing `Authorization: Bearer atr_...` Agent PAT (or human JWT) middleware.
2. **Strictly Read-Only MCP Toolset (9 tools):**
   - Covers Daily Tracker (`tasks:read`), Calendar (`calendar:read`), University (`university:read`), Notes (`notes:read`), and Artifacts/Knowledge search, enforcing least-privilege PAT scopes and `AllowAgentRead` inheritance on every call.
3. **Automatic Artifact Text Extraction:**
   - Extracts searchable text from uploaded visible artifacts (`kind == "artifact"`):
     - **PDF (`.pdf`)** per page via `UglyToad.PdfPig` (pure C#, no native binary dependencies).
     - **PowerPoint (`.pptx`)** per slide via `System.IO.Compression.ZipArchive` + XML (`<a:t>`).
     - **Word (`.docx`)** paragraphs via `ZipArchive` + XML (`<w:t>`).
     - **Plain text (`.txt`, `.md`, `.csv`, `.json`)** directly as UTF-8.
   - Persists extracted pages/chunks into a `pgvector`-ready `attachment_text_chunks` table, with lazy on-demand extraction for artifacts uploaded before this feature shipped.
4. **Unified Full-Text Knowledge Search:**
   - Searches across all agent-readable notes AND extracted artifact chunks via Postgres `tsvector` + GIN indexes (with SQLite fallback in unit tests), exposed as both the `search_knowledge` MCP tool and `GET /api/knowledge/search` (+ `GET /api/attachments/{id}/text`).
5. **Quick MCP Setup Info in Account UI:**
   - Display the `/mcp` endpoint URL and copyable MCP client config snippet in the Agent Tokens section of `AccountPage.tsx`.

### Non-Goals
- No write or proposal MCP tools yet (Phase 2 using `calendar:propose`, `tasks:propose`, `university:propose`).
- No `pgvector` embeddings or external embedding model/API key in Phase 1 (the chunk table is designed so a `vector` column slots in later without schema rework).
- No OCR for scanned image-only PDFs (scanned PDFs cleanly record `text_extraction_status = "empty"` without failing upload).
- No text extraction or exposure of hidden `inline_image` attachments.

### Anti-Goals
- **Never leak** another user's records, records outside the PAT's granted scopes, or any note/folder/artifact with `EffectiveAllowAgentRead == false`.
- **Never break file uploads** if text extraction fails on an encrypted, malformed, or unsupported file (log/record status `"failed"` or `"unsupported"` and keep the upload succeeding).
- **Never return unbounded payloads** from MCP tools (enforce result limits, date-span caps, and chunk pagination).

---

## 3. Approaches Considered

### Approach 1: Built-in Streamable HTTP MCP (`/mcp`) inside `Atrium.Api` *(Selected)*
- **How it works:** Mount `ModelContextProtocol.AspNetCore` at `/mcp` inside `Atrium.Api`, querying `AppDbContext` and shared read/search services directly under existing PAT auth. Extract artifact text in .NET on upload and store chunks in Postgres.
- **Tradeoffs:** Zero extra container/process to deploy, works locally (`http://localhost:5000/mcp`) and remotely over existing Caddy HTTPS, zero DTO duplication. Local `stdio`-only MCP clients connect through a tiny standard bridge (e.g. `mcp-remote`).

### Approach 2: Standalone TypeScript MCP Server (`mcp/`) Calling Atrium REST API
- **How it works:** Separate Node/TypeScript `@modelcontextprotocol/sdk` package supporting `stdio` + HTTP, calling Atrium's REST endpoints with `ATRIUM_API_URL` and `ATRIUM_PAT`.
- **Tradeoffs:** Native `stdio` without a bridge, but adds a second build/deploy service and duplicates API contracts and DTOs.

**Why Approach 1:** Single server, single deployment, direct access to Atrium's auth middleware and `AppDbContext`, and immediately ready for both local assistants and remote external agents over HTTPS.

---

## 4. Data Model & Schema (PostgreSQL + EF Core)

### 4.1 Extensions to `Attachment` (`attachments` table)
Add three columns:
- `text_extraction_status` (`varchar(24)`, required, default `"pending"` — values: `"pending"`, `"completed"`, `"empty"`, `"unsupported"`, `"failed"`)
- `extracted_page_count` (`integer`, nullable)
- `text_extracted_at` (`timestamptz`, nullable)

### 4.2 New `AttachmentTextChunk` Entity (`attachment_text_chunks` table)
| Column | Type | Notes |
|---|---|---|
| `id` | `uuid` PK | |
| `attachment_id` | `uuid` FK → `attachments.id` (Cascade) | Owning artifact |
| `user_id` | `uuid` FK → `users.id` (Cascade) | Owner (for fast user-scoped FTS) |
| `chunk_index` | `integer` | 0-based ordered index within attachment |
| `page_label` | `varchar(64)` | e.g. `"Page 1"`, `"Slide 4"`, `"Section 2"`, `"Part 1"` |
| `content` | `text` | Extracted plain text (max ~4,000 chars per chunk) |
| `char_count` | `integer` | Length of `content` |
| `created_at` | `timestamptz` | |

**Indexes:**
- Unique index `ix_attachment_text_chunks_attachment_chunk` on `(attachment_id, chunk_index)`.
- Lookup index `ix_attachment_text_chunks_user_attachment` on `(user_id, attachment_id)`.
- PostgreSQL GIN full-text indexes (created in the EF migration via `migrationBuilder.Sql` guarded for Postgres):
  - `ix_attachment_text_chunks_fts` on `to_tsvector('english', content)`
  - `ix_notes_fts` on `to_tsvector('english', title || ' ' || content)`

---

## 5. Artifact Text Extraction Pipeline (`IArtifactTextExtractor`)

- Interface `IArtifactTextExtractor` returning `ArtifactExtractionResult(string Status, int? PageCount, IReadOnlyList<ExtractedChunk> Chunks)`.
- **Extraction limits (designer calls):**
  - Max extracted text per artifact: **500,000 characters** (~200 pages of dense text), chunked to **~4,000 characters** max per chunk (splitting long pages/documents on paragraph/line boundaries).
  - Runs during `POST /api/attachments` for `kind == "artifact"` (wrapped in `try/catch` with a 5-second cancellation token linked to the request so a corrupted PDF can never fail or hang an upload).
- **Format handlers:**
  - `.pdf` / `application/pdf`: `UglyToad.PdfPig` `PdfDocument.Open(stream)` → extracts text per page (`Page {n}`). If 0 non-whitespace characters extracted → status `"empty"`.
  - `.pptx`: `ZipArchive` reading `ppt/slides/slide{N}.xml` sorted numerically by slide number → extracts `<a:t>` elements grouped by paragraph (`Slide {n}`).
  - `.docx`: `ZipArchive` reading `word/document.xml` → extracts `<w:p>` / `<w:t>` paragraphs, chunked into ~4,000-char sections (`Section {n}`).
  - `.txt`, `.md`, `.csv`, `.json`: UTF-8 text chunked into ~4,000-char parts (`Part {n}`).
  - All other extensions/types: status `"unsupported"`, 0 chunks.
- **Backfill for pre-existing artifacts (designer call):**
  - A lightweight `BackgroundService` (`ArtifactTextBackfillService`) runs after API startup, picking up any existing `Kind == "artifact" && DeletedAt == null && TextExtractionStatus == "pending"` records in small batches (10 at a time) and extracting their chunks quietly so files uploaded before this feature become searchable automatically.
  - As an immediate fallback, if `read_artifact_text` (or `GET /api/attachments/{id}/text`) is called on an artifact still in `"pending"` status before backfill reaches it, `ArtifactTextService` extracts and persists its chunks on demand.

---

## 6. Security, Scopes & Privacy Rules

Authentication and privacy follow a strict split between the agent-only MCP surface and the shared REST surface (designer call):

1. **Auth Policies:**
   - **`/mcp`** requires a dedicated **`AgentPatOnlyPolicy`** (only `atr_...` Agent PATs accepted) and **always** enforces PAT scopes + `AllowAgentRead` privacy rules, so private notes/artifacts can never be exposed to an AI client even accidentally.
   - **REST endpoints** (`GET /api/knowledge/search`, `GET /api/attachments/{id}/text`) use `AuthConstants.HumanOrAgentPolicy`: Human JWT callers can search/read all of their own notes and artifacts (matching `NoteEndpoints` and `AttachmentEndpoints`), while Agent PAT callers enforce scopes + `AllowAgentRead`.
2. **Strict User Isolation:** Every query filters by `UserId == principal.GetUserId()`.
3. **Least-Privilege Agent PAT Scopes:**
   - `get_day`: requires `tasks:read`. Daily note content is included **only** if the token also has `notes:read` AND the daily note has `AllowAgentRead == true` (otherwise `note` is returned as `null` with `noteHiddenByScopeOrPrivacy: true`).
   - `list_events`: requires `calendar:read`.
   - `list_courses`, `get_course`: requires `university:read`. (`get_course` includes `linkedNoteId` only if `notes:read` is also held and note is agent-readable.)
   - `list_notes_tree`, `read_note`: requires `notes:read` (+ `university:read` for university notes/section, + `calendar:read` for event notes/section).
   - `list_artifacts`, `read_artifact_text`: requires `notes:read` for note-owned artifacts, `university:read` for course/item artifacts, `calendar:read` for event artifacts.
   - `search_knowledge`: requires at least one of `notes:read`, `university:read`, `calendar:read`. Only searches note/artifact categories covered by the token's granted scopes.
   - Missing required scope on an MCP tool returns a clear structured MCP tool error (`isError: true`, `"Missing required scope: <scope>"`) rather than throwing an unhandled exception.
3. **Effective `AllowAgentRead` Inheritance:**
   - Reuses the exact folder-chain and linked-note inheritance rules already enforced in `NoteEndpoints` and `AttachmentEndpoints`.
   - Hidden notes, notes inside hidden folders, and artifacts attached to hidden notes/items/events are **never** returned in search results, tree listings, or `read_note` / `read_artifact_text`.

---

## 7. Read-Only MCP Toolset & Shared REST Endpoints

### 7.1 MCP Endpoint Configuration
- Mount at `/mcp` via `app.MapMcp("/mcp").RequireAuthorization(AuthConstants.AgentPatOnlyPolicy)`.
- Use stateless Streamable HTTP transport (`WithHttpTransport(o => o.Stateless = true)`).
- Exclude `/mcp` from the SPA `index.html` fallback in `Program.cs` (alongside `/api`).

### 7.2 MCP Tools (9 Read-Only Tools)
All tools are decorated with `[McpServerTool(ReadOnly = true, Idempotent = true)]` and return clean JSON objects:

1. **`get_day(date?: "yyyy-MM-dd")`** — Defaults to today (UTC or user's timezone). Returns checklist items and (if permitted) the day's Markdown note.
2. **`list_events(from?: ISO, to?: ISO, calendarId?: Guid)`** — Defaults to `[today, today + 7d]`, max span 90 days. Returns expanded calendar events (including recurrence occurrences and `universityActivityItemId` when applicable).
3. **`list_courses(semester?: string, status?: string)`** — Lists university courses with attendance summary, note count, and artifact count.
4. **`get_course(courseId: Guid)`** — Full course details including activities, items, schedules, attendance, course-level notes (if `notes:read`), and artifact summaries.
5. **`list_notes_tree(includeUniversity?: bool, includeEvents?: bool, includeDaily?: bool)`** — Returns General folders/notes, tags, and (when requested & permitted by scope) University tree, Events tree, and Daily notes.
6. **`read_note(noteId: Guid)`** — Returns full Markdown note content, tags, link context (`linkKind`, `linkLabel`), and visible artifact metadata.
7. **`list_artifacts(noteId?, universityCourseId?, universityActivityItemId?, calendarEventId?, occurrenceStartAt?)`** — Lists visible artifacts (`kind == "artifact"`) for one target owner, including `textExtractionStatus` and `extractedPageCount`.
8. **`read_artifact_text(attachmentId: Guid, startChunk?: int, maxChunks?: int)`** — Lazily extracts if `pending`; returns `{ attachmentId, fileName, status, totalChunks, startChunk, hasMore, chunks: [{ chunkIndex, pageLabel, content }] }` (`maxChunks` defaults to `10`, clamped to `1..20`).
9. **`search_knowledge(query: string, scope?: "all" | "notes" | "artifacts", courseId?: Guid, limit?: int)`** — Full-text search across permitted notes and extracted artifact chunks (`limit` defaults to `10`, clamped to `1..25`). Returns ranked matches with `{ sourceType: "note" | "artifact", id, title, linkLabel?, pageLabel?, chunkIndex?, snippet }`.

### 7.3 Shared REST Endpoints
- **`GET /api/attachments/{id:guid}/text?startChunk=0&maxChunks=10`** — Same paginated text payload as `read_artifact_text`.
- **`GET /api/knowledge/search?q=...&scope=all&courseId=...&limit=10`** — Same search payload as `search_knowledge`.

### 7.4 Frontend Quick-Connect Snippet (`AccountPage.tsx`)
- Inside the existing **Agent Tokens** card on `AccountPage.tsx`, add a compact collapsible **"Connect via MCP"** box showing the current origin's `/mcp` URL (`${window.location.origin}/mcp`) and a one-click copyable JSON config snippet illustrating `Authorization: Bearer <your-pat>`.

---

## 8. What's Left to the Builder

- Exact NuGet versions for `ModelContextProtocol.AspNetCore` and `UglyToad.PdfPig` (compatible with .NET 10 and `-warnaserror`).
- Exact snippet highlighting / excerpt centering helper for `search_knowledge`.
- Shared service decomposition (`KnowledgeReadService`, `ArtifactTextService`) so `NoteEndpoints`, `AttachmentEndpoints`, `KnowledgeEndpoints`, and `AtriumMcpTools` share logic without duplication.
- Comprehensive xUnit integration tests covering PDF/PPTX/DOCX/TXT extraction, lazy backfill, corrupted file resilience, Postgres/SQLite search, MCP JSON-RPC `tools/list` + `tools/call` over `/mcp`, PAT scope enforcement, and `AllowAgentRead` privacy enforcement.

