# Atrium Notes Vault, Folders, Frontmatter & Agent Access (PR 2) Design

**Date:** 2026-11-27
**Status:** Draft (pending approval)
**Branch target:** `feature/notes-vault-storage` (branched from `main` after PR 1 merges)

---

## 1. Intent & Why

PR 1 built the fullscreen Markdown editor (`Live Preview`, `Reading`, `Source`, callouts) backed by a single temporary `localStorage` draft. **PR 2** turns Notes into a real, server-persisted knowledge base in PostgreSQL with:
- Nested **folders** and multiple notes managed from a collapsible left sidebar.
- **YAML frontmatter** (`--- ... ---` at the top of each `.md` note) synced with a clean **Properties & Tags** bar in Live Preview and Reading modes while staying 100% lossless in raw Markdown for future desktop local-file sync (PR 3).
- Fast **search & tag filtering**.
- **Per-note and per-folder AI agent visibility** (`allow_agent_read`), enforced strictly on `notes:read` PAT endpoints and ready for future `pgvector` RAG (PR 4).

---

## 2. Goals, Non-Goals, & Anti-Goals

### Goals
- Persist folders and Markdown notes in PostgreSQL (taking advantage of Postgres TOAST automatic LZ4/PGLZ compression on `content`), with sync-ready columns (`version`, `content_hash`, `deleted_at`) baked in from day 1.
- Support nested folders (`ParentFolderId`): create, rename, move (with cycle detection), toggle `allow_agent_read`, and delete (soft-delete subtree with inline confirmation).
- Support full note CRUD: create (in root or any folder), auto-save edits (700 ms debounce + blur/switch flush), move between folders, rename/update title, manage tags, toggle `allow_agent_read`, and soft-delete (with inline confirmation).
- Support YAML frontmatter (`--- ... ---` at top of note):
  - Hidden in Live Preview and Reading modes; displayed and edited via a synced **Properties & Tags** bar (editable title, add/remove tags, agent visibility toggle).
  - Visible and directly editable in Source mode.
  - Updating tags/title via the Properties bar updates the YAML block in place while preserving any extra user-defined YAML keys untouched.
  - On save, the backend parses the frontmatter so indexed tags stay accurate even when future desktop clients push raw `.md` files.
- Collapsible left sidebar in `NotesPage` (desktop toggle button + `Cmd/Ctrl+\` shortcut; slide-over drawer on mobile) with:
  - Search bar (matches title, content, and tags).
  - Tag filter chips (with counts).
  - Nested folder tree + note list + quick **New note** / **New folder** actions.
- Add `notes:read` PAT scope to `AuthConstants.AllowedScopes` and `AccountPage.tsx`. Enforce `effectiveAllowAgentRead` (both the note and every ancestor folder must have `allow_agent_read == true`) on all PAT reads.

### Non-Goals (Deferred to PR 3 & PR 4)
- Bidirectional desktop sync endpoints (`/api/notes/sync/pull`, `/push`) and offline IndexedDB cache (PR 3).
- Binary image/PDF file uploads (`uploads/` directory) (PR 3).
- Semantic vector search (`pgvector` + `note_chunks`) and MCP server tools (PR 4).
- Trash / Recycle Bin restore UI (soft-delete tombstones `deleted_at` are stored in DB for future sync/restore, but undelete UI is deferred).
- Multi-user shared notes or household collaborative editing (all notes are strictly private per `user_id`).

### Anti-Goals
- Losing unsaved edits when switching between notes rapidly or typing during an in-flight auto-save `PUT` (must use edit-sequence + per-note pending-save guards, matching `DailyNoteCard`).
- Destroying unknown custom YAML frontmatter fields when editing tags in the Properties bar.
- Leaking any note (or its title/tags/folder) to an AI agent PAT when the note or any of its ancestor folders has `allow_agent_read = false`.
- Allowing folder move cycles (moving a folder into itself or its own descendant).

---

## 3. Approaches Considered

### Approach 1: Normalized `note_folders` + `notes` + `note_tags` tables with canonical raw Markdown *(Selected)*
- Separate `note_folders` (self-referencing `parent_folder_id`), `notes` (`content` stores canonical raw `.md` including YAML frontmatter, plus `title`, `content_hash`, `version`, `allow_agent_read`, `deleted_at`), and `note_tags` (`note_id`, `user_id`, `tag`) tables.
- **Trade-offs:** First-class empty folders and per-folder `allow_agent_read`, single-row folder renames/moves, instant cross-provider (PostgreSQL + SQLite tests) tag queries, and clean sync metadata (`version`, `content_hash`, `deleted_at`) for PR 3.

### Approach 2: Flat path-based `notes` table (folders inferred from path strings)
- **Trade-offs:** Fewer tables initially, but empty folders can't exist cleanly, per-folder `allow_agent_read` requires a separate folder metadata table anyway, and renaming a folder requires bulk-rewriting every descendant note's path string.

---

## 4. Architecture & Data Model (Approach 1)

### Database Schema (EF Core + PostgreSQL Migration `AddNotesVault`)

1. **`NoteFolder`** (`note_folders`)
   - `Id` (`Guid`, PK)
   - `UserId` (`Guid`, FK → `users.id`, `ON DELETE CASCADE`)
   - `ParentFolderId` (`Guid?`, nullable FK → `note_folders.id`, `ON DELETE RESTRICT` — subtree soft-delete handled explicitly in endpoint)
   - `Name` (`string`, required, max length 120)
   - `AllowAgentRead` (`bool`, default `true`)
   - `SortOrder` (`int`, default `0`)
   - `CreatedAt` (`DateTimeOffset`)
   - `UpdatedAt` (`DateTimeOffset`)
   - `DeletedAt` (`DateTimeOffset?`, nullable)
   - **Index:** `ix_note_folders_user_parent` on `(user_id, parent_folder_id, deleted_at)`

2. **`Note`** (`notes`)
   - `Id` (`Guid`, PK)
   - `UserId` (`Guid`, FK → `users.id`, `ON DELETE CASCADE`)
   - `FolderId` (`Guid?`, nullable FK → `note_folders.id`, `ON DELETE SET NULL`)
   - `Title` (`string`, required, max length 200)
   - `Content` (`string`, required — PostgreSQL `text`, auto-compressed via TOAST; max 500,000 chars validated at API layer)
   - `ContentHash` (`string`, required, max length 64 — lowercase SHA-256 hex of `Content`)
   - `Version` (`int`, default `1`)
   - `AllowAgentRead` (`bool`, default `true`)
   - `CreatedAt` (`DateTimeOffset`)
   - `UpdatedAt` (`DateTimeOffset`)
   - `DeletedAt` (`DateTimeOffset?`, nullable)
   - **Index:** `ix_notes_user_folder_deleted` on `(user_id, folder_id, deleted_at)`
   - **Index:** `ix_notes_user_updated` on `(user_id, updated_at)`

3. **`NoteTag`** (`note_tags`)
   - `NoteId` (`Guid`, FK → `notes.id`, `ON DELETE CASCADE`)
   - `UserId` (`Guid`, FK → `users.id`, `ON DELETE CASCADE`)
   - `Tag` (`string`, required, max length 40 — normalized lowercase `[a-z0-9/_-]`)
   - **PK:** `(note_id, tag)`
   - **Index:** `ix_note_tags_user_tag` on `(user_id, tag)`

### YAML Frontmatter Contract
- A frontmatter block is recognized only at the very start of `Content`: line 1 is `---` (optional trailing spaces), followed by YAML lines, closed by `---` (or `...`) on its own line.
- **Parsed fields:**
  - `tags:` supports both inline array (`tags: [uni, math, exam-prep]`) and YAML block list (`tags:\n  - uni\n  - math`).
  - `title:` optional scalar string.
- **Two-way `Title` ↔ YAML `title:` rules *(builder call)*:**
  - `Note.Title` is the authoritative display/filename title (like Obsidian's filename).
  - Editing the title in the Properties bar updates `Note.Title` directly, and **only** updates `title:` inside YAML frontmatter if a `title:` key already exists there (avoiding injecting redundant `title:` frontmatter into notes that don't have one).
  - When saving from **Source** mode (where the user may edit `title:` directly in YAML), the client omits `Title` from the `PUT` payload when the Properties title input wasn't edited; on the backend, if `request.Title` is `null` and YAML frontmatter contains a non-empty `title:`, `Note.Title` updates to match it. If `request.Title` is explicitly provided, `request.Title` takes precedence.
- **Tag normalization:** lowercase, trim, strip leading `#`, allow `[a-z0-9/_-]`, max 40 chars, deduplicated, max 30 tags per note.
- **Preservation:** Updating `tags` from the Properties bar modifies only the `tags:` key in the frontmatter block (creating a `---\ntags:\n  - ...\n---\n\n` block if none existed, or removing empty `tags:` cleanly) while leaving all other custom YAML lines untouched.

### Backend API (`/api/notes`)

Auth policies:
- **Reads:** `AuthConstants.HumanOrAgentPolicy`; PAT callers must hold `"notes:read"` scope (403 otherwise) and only receive folders/notes where `effectiveAllowAgentRead == true` (computed by walking each folder's `ParentFolderId` chain).
- **Writes:** `AuthConstants.HumanJwtOnlyPolicy`.

Endpoints:
1. **`GET /api/notes/tree`** — Returns `{ folders, notes, tags }` where `folders` are active `NoteFolderDto`s, `notes` are active `NoteSummaryDto`s (no `content` body, keeping tree loads fast), and `tags` are `{ tag, count }[]`.
2. **`GET /api/notes?q=&tag=&folderId=&includeSubfolders=true`** — Searches active notes by optional case-insensitive substring on `Title`/`Content`, optional exact normalized `tag`, and optional `folderId` (including `"root"` for unfiled notes). When `folderId` is a folder GUID and `includeSubfolders` is `true` (default), includes notes in that folder and all permitted descendant folders (respecting `effectiveAllowAgentRead` at each level for PAT callers); when `false`, matches only direct children (`note.FolderId == folderId`). Returns `NoteSummaryDto[]` with a short plain-text `excerpt`.
3. **`GET /api/notes/{id:guid}`** — Returns full `NoteDetailDto` (including `content`, `tags`, `version`, `contentHash`, `allowAgentRead`, `effectiveAllowAgentRead`). Returns `404` if deleted, owned by another user, or excluded from a PAT caller.
4. **`POST /api/notes/folders`** — Creates a folder (`Name`, optional `ParentFolderId`, optional `AllowAgentRead = true`). Validates parent belongs to user and is not deleted. Returns `201`.
5. **`PUT /api/notes/folders/{id:guid}`** — Updates `Name`, `ParentFolderId` (rejecting self/descendant cycles with `400`), and/or `AllowAgentRead`. Returns `200`.
6. **`DELETE /api/notes/folders/{id:guid}`** — Soft-deletes (`DeletedAt = now`) the folder, all descendant folders, and all notes within that subtree in a transaction (clearing `note_tags` for soft-deleted notes). Returns `204`.
7. **`POST /api/notes`** — Creates a note (`Title`, optional `FolderId`, optional `Content = ""`, optional `AllowAgentRead = true`). Parses frontmatter tags, computes SHA-256 `ContentHash`, sets `Version = 1`. Returns `201` with `NoteDetailDto`.
8. **`PUT /api/notes/{id:guid}`** — Updates `Title`, `Content`, `FolderId` (nullable / `MoveToRoot`), and/or `AllowAgentRead`. If `ExpectedVersion` is supplied and differs from `note.Version`, returns `409 Conflict` with current server note. Re-parses frontmatter tags into `note_tags`, updates `ContentHash`, increments `Version`. Returns `200` with `NoteDetailDto`.
9. **`DELETE /api/notes/{id:guid}`** — Soft-deletes (`DeletedAt = now`) the note and removes its `note_tags`. Returns `204`.

### Frontend UX (`NotesPage.tsx`, `NotesSidebar.tsx`, `NotePropertiesBar.tsx`)

1. **Collapsible Left Sidebar (`NotesSidebar.tsx`)**
   - Toggled via sidebar icon button in the Notes top bar or `Cmd/Ctrl+\` (persisted in `localStorage`; slide-over drawer on mobile `< 768px`).
   - Top actions: **+ New Note**, **+ New Folder**, search input, and clickable tag filter chips.
   - Recursive folder tree with expand/collapse chevrons, note count, subtle eye-off badge on folders hidden from AI agents, inline rename, move/new-child-note, agent-visibility toggle, and inline delete confirmation.
   - Root-level ("Unfiled") notes listed cleanly alongside folders.
2. **Synced Properties & Tags Bar (`NotePropertiesBar.tsx`)**
   - Shown at the top of the note canvas in **Live Preview** and **Reading** modes (and editable title input at the top).
   - Displays tag chips (with `×` to remove in edit modes, `+ Add tag` inline input with Enter/comma submit) and an **AI agents: Allowed / Hidden** toggle (showing `"Hidden by folder"` lock state when an ancestor folder has `allow_agent_read = false`).
   - In **Live Preview** (`markdownLivePreview.ts`), add frontmatter hiding so the leading `--- ... ---` block is hidden when inactive (and in Reading mode strip the leading frontmatter block before passing body markdown to `ReactMarkdown`).
3. **Auto-Save, Conflict Handling & Note Switching Safety**
   - 700 ms debounced auto-save on edits, flush on blur and before switching notes, edit-sequence guard against in-flight `PUT` races, and per-note pending-save coordination (same battle-tested pattern as `DailyNoteCard`).
   - **Optimistic concurrency (`ExpectedVersion`) *(builder call)*:** The web editor sends `expectedVersion` on auto-save `PUT`s. On `409 Conflict` (e.g., note edited in another tab), auto-save pauses and displays a compact conflict banner with **"Reload latest"** and **"Overwrite with mine"** actions.
   - First-run PR 1 migration: if the user has 0 server notes and a non-empty `atrium.notes.pr1.draft` in `localStorage`, offer a one-click **"Import local draft"** button (or start with an empty state + **"Create first note"** button).

---

## 5. What's Left to the Builder
- Exact YAML frontmatter parser/serializer implementation details (lightweight, zero-dependency frontmatter helper in both C# and TypeScript covering standard scalar `title` and `tags` array/list formats while preserving arbitrary extra lines).
- Exact CSS classes and spacing for `NotesSidebar.tsx` and `NotePropertiesBar.tsx` (matching Atrium Atelier light/dark tokens).
- Comprehensive xUnit integration tests in `NotesEndpointsTests.cs` (folder nesting + cycle rejection + subtree soft-delete, note CRUD + frontmatter tag extraction + `409 Conflict` on stale `ExpectedVersion`, search/tag filtering, cross-user isolation, and `notes:read` PAT enforcement including inherited folder `allow_agent_read = false`).

