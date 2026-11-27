# Atrium Notes Vault, Folders, Frontmatter & Agent Access (PR 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the PR 1 Markdown Notes Editor into a PostgreSQL-persisted personal knowledge base with nested folders, collapsible sidebar with search and tag filtering, a synced YAML frontmatter Properties/Tags bar, optimistic concurrency (`409 Conflict`), and per-note/per-folder AI agent visibility (`notes:read` PAT scope).

**Architecture:** Add normalized `note_folders`, `notes`, and `note_tags` tables in EF Core with sync-ready metadata (`version`, `content_hash`, `deleted_at`) and canonical raw Markdown storage (auto-compressed via Postgres TOAST). Expose `/api/notes` endpoints with ancestor-aware `effectiveAllowAgentRead` filtering for PAT callers, and connect `NotesPage` to a collapsible `NotesSidebar`, synced `NotePropertiesBar`, and debounced auto-save with edit-sequence and `409 Conflict` guards.

**Tech Stack:** .NET 10 Minimal APIs, EF Core 10 + Npgsql (PostgreSQL), xUnit (`TestAppFactory`), React 19, TypeScript (strict), CodeMirror 6, `react-markdown`.

**Spec:** `docs/superpowers/specs/2026-11-27-notes-vault-storage-design.md`

## Global Constraints
- Branch target: `feature/notes-vault-storage` (branched from `main` after PR 1 merges).
- Backend must pass `dotnet build --no-restore -c Release -warnaserror api/Atrium.sln` with 0 warnings and all xUnit tests passing.
- Frontend must pass `npm run check:suppressions`, `npm --prefix web run typecheck`, and `npm --prefix web run build`.
- All routes strictly scope by authenticated `userId`; reads use `HumanOrAgentPolicy` + `"notes:read"` PAT scope; writes use `HumanJwtOnlyPolicy`.
- `Note.Title` is 1–200 chars; `NoteFolder.Name` is 1–120 chars; `Note.Content` max 500,000 chars; normalized tags match `^[a-z0-9/_-]{1,40}$` (max 30 per note).
- Updating tags/title from the Properties bar must preserve all unknown custom YAML frontmatter keys untouched.

## Review Focus
- **Inherited folder `allow_agent_read = false`:** A `notes:read` PAT must never see a note (via `/tree`, search `?q=`, `?folderId=`, `/tags`, or `GET /api/notes/{id}`) when *any* ancestor folder (direct parent or grandparent) has `allow_agent_read = false`, even if the note itself has `allow_agent_read = true` — pinned in Task 2 (`NoteEndpointsTests`).
- **Folder move cycle prevention:** Moving a folder into itself or any of its descendants must return `400 Bad Request` rather than creating a cycle or infinite loop — pinned in Task 2 (`NoteEndpointsTests`).
- **Optimistic concurrency (`409 Conflict`) & two-way `Title` ↔ YAML `title:` precedence:** Sending a stale `expectedVersion` returns `409` with the current server note; omitting `Title` on a Source-mode save lets YAML `title:` update `Note.Title`, while explicit `Title` wins — pinned in Task 2 (`NoteEndpointsTests`) & Task 4 (`NotesPage`).
- **YAML frontmatter preservation:** Adding/removing tags or renaming via the Properties bar must not strip unrelated custom keys (e.g., `aliases`, `author`, `created`) in the note's `--- ... ---` block — pinned in Task 3 (`frontmatter.ts`).
- **Rapid note switching & in-flight auto-save races:** Switching notes while a debounced edit is pending flushes to the *previous* note ID without leaking note A's text into note B, and typing during an in-flight `PUT` never reverts the editor — pinned in Task 4 (`NotesPage`).

---

## File Structure

- **Backend Data, Migration & PAT Scope (Task 1)**
  - Create `api/Atrium.Api/Data/Entities/NoteFolder.cs`
  - Create `api/Atrium.Api/Data/Entities/Note.cs`
  - Create `api/Atrium.Api/Data/Entities/NoteTag.cs`
  - Modify `api/Atrium.Api/Data/AppDbContext.cs`
  - Create `api/Atrium.Api/Data/Migrations/20261127120000_AddNotesVault.cs`
  - Modify `api/Atrium.Api/Data/Migrations/AppDbContextModelSnapshot.cs`
  - Modify `api/Atrium.Api/Auth/AuthConstants.cs`
  - Modify `web/src/components/AccountPage.tsx`
  - Modify `docs/engineering/database-migrations.md`
  - Modify `api/Atrium.Api.Tests/MigrationModelTests.cs`
- **Backend Frontmatter, API & Integration Tests (Task 2)**
  - Create `api/Atrium.Api/Notes/NoteContracts.cs`
  - Create `api/Atrium.Api/Notes/NoteFrontmatter.cs`
  - Create `api/Atrium.Api/Endpoints/NoteEndpoints.cs`
  - Modify `api/Atrium.Api/Program.cs`
  - Create `api/Atrium.Api.Tests/NoteEndpointsTests.cs`
- **Frontend Types, Frontmatter Sync, Client & Live Preview (Task 3)**
  - Modify `web/src/types/atrium.ts`
  - Create `web/src/lib/frontmatter.ts`
  - Create `web/src/lib/notesClient.ts`
  - Modify `web/src/lib/markdownLivePreview.ts`
- **Frontend Sidebar, Properties Bar, Vault Page & Styles (Task 4)**
  - Create `web/src/components/NotesSidebar.tsx`
  - Create `web/src/components/NotePropertiesBar.tsx`
  - Modify `web/src/components/NotesPage.tsx`
  - Modify `web/src/styles.css`

---

### Task 1: EF Core Schema, Migration (`AddNotesVault`) & `notes:read` Scope

**Files:**
- Create: `api/Atrium.Api/Data/Entities/NoteFolder.cs`
- Create: `api/Atrium.Api/Data/Entities/Note.cs`
- Create: `api/Atrium.Api/Data/Entities/NoteTag.cs`
- Modify: `api/Atrium.Api/Data/AppDbContext.cs`
- Create: `api/Atrium.Api/Data/Migrations/20261127120000_AddNotesVault.cs`
- Modify: `api/Atrium.Api/Data/Migrations/AppDbContextModelSnapshot.cs`
- Modify: `api/Atrium.Api/Auth/AuthConstants.cs`
- Modify: `web/src/components/AccountPage.tsx`
- Modify: `docs/engineering/database-migrations.md`
- Test: `api/Atrium.Api.Tests/MigrationModelTests.cs`

**Interfaces:**
- Produces:
  - Entities `NoteFolder`, `Note`, `NoteTag` and `AppDbContext` sets `NoteFolders`, `Notes`, `NoteTags`.
  - `"notes:read"` in `AuthConstants.AllowedScopes` and `AccountPage.tsx` PAT scope checkboxes.

- [ ] **Step 1: Add migration assertions to `api/Atrium.Api.Tests/MigrationModelTests.cs`**

Assert presence of `20261127120000_AddNotesVault`, `CREATE TABLE note_folders`, `CREATE TABLE notes`, `CREATE TABLE note_tags`, `ix_note_folders_user_parent`, `ix_notes_user_folder_deleted`, `ix_notes_user_updated`, and `ix_note_tags_user_tag`.

- [ ] **Step 2: Run `MigrationModelTests` to confirm RED**

Run: `dotnet test -c Release --no-restore api/Atrium.sln --filter MigrationModelTests`
Expected: FAIL (`Not found: "20261127120000_AddNotesVault"`).

- [ ] **Step 3: Create entities, configure `AppDbContext`, add migration & snapshot, and register `notes:read` scope**

Create `NoteFolder.cs`, `Note.cs`, `NoteTag.cs`, configure them in `AppDbContext.cs` per Spec §4, create `20261127120000_AddNotesVault.cs` (single-file `[DbContext]/[Migration]` style), update `AppDbContextModelSnapshot.cs` and `docs/engineering/database-migrations.md`, and add `"notes:read"` to `AuthConstants.AllowedScopes` and `AccountPage.tsx`.

- [ ] **Step 4: Build Release `-warnaserror` and verify `MigrationModelTests` passes**

Run: `dotnet build --no-restore -c Release -warnaserror api/Atrium.sln && dotnet test -c Release --no-build api/Atrium.sln --filter MigrationModelTests`
Expected: 0 warnings, 0 errors, `MigrationModelTests` PASS.

- [ ] **Step 5: Commit**

```bash
git add api/Atrium.Api/Data/ api/Atrium.Api/Auth/AuthConstants.cs web/src/components/AccountPage.tsx docs/engineering/database-migrations.md api/Atrium.Api.Tests/MigrationModelTests.cs
git commit -m "feat(notes): add EF Core schema, migration, and notes:read PAT scope"
```

---

### Task 2: Backend Frontmatter Parser, `/api/notes` Endpoints & Integration Tests

**Files:**
- Create: `api/Atrium.Api/Notes/NoteContracts.cs`
- Create: `api/Atrium.Api/Notes/NoteFrontmatter.cs`
- Create: `api/Atrium.Api/Endpoints/NoteEndpoints.cs`
- Modify: `api/Atrium.Api/Program.cs`
- Test: `api/Atrium.Api.Tests/NoteEndpointsTests.cs`

**Interfaces:**
- Consumes: `AppDbContext` (`NoteFolders`, `Notes`, `NoteTags`), `AuthConstants`
- Produces:
  - `NoteFrontmatter.Parse(string content)` → `(string? Title, IReadOnlyList<string> Tags, string Body)`
  - `NoteFrontmatter.ComputeContentHash(string content)` → 64-char lowercase SHA-256 hex
  - `NoteFrontmatter.BuildExcerpt(string body, int maxLength = 160)` → plain-text snippet
  - Endpoints mapped by `app.MapNoteEndpoints()` under `/api/notes`:
    - `GET /api/notes/tree`
    - `GET /api/notes?q=&tag=&folderId=&includeSubfolders=true`
    - `GET /api/notes/{id:guid}`
    - `POST /api/notes/folders`, `PUT /api/notes/folders/{id:guid}`, `DELETE /api/notes/folders/{id:guid}`
    - `POST /api/notes`, `PUT /api/notes/{id:guid}`, `DELETE /api/notes/{id:guid}`

- [ ] **Step 1: Write failing integration tests in `api/Atrium.Api.Tests/NoteEndpointsTests.cs`**

Include tests covering:
1. `Folders_CreateNestRenameMoveRejectCyclesAndSoftDeleteSubtree`: verifies nested folders, `400` on self/descendant cycle move, and subtree soft-delete clearing descendant folders, notes, and `note_tags`.
2. `Notes_CrudFrontmatterTags_TitlePrecedence_AndVersionConflict`: verifies creating/updating notes extracts normalized inline and YAML-list `tags`, respects explicit `Title` vs. omitted `Title` (falling back to YAML `title:`), computes `ContentHash`, increments `Version`, returns `409 Conflict` with current server note on stale `ExpectedVersion`, and soft-deletes cleanly.
3. `Search_FiltersByQueryTagAndFolderSubtree`: verifies `?q=`, `?tag=`, `?folderId=root`, `?folderId={id}&includeSubfolders=true` vs. `includeSubfolders=false`.
4. `UserIsolation_AndAgentReadVisibilityInheritance`: verifies cross-user `404` on reads/writes; verifies a `notes:read` PAT sees allowed notes, gets `404` on a note with `allow_agent_read = false` OR inside a grandparent folder with `allow_agent_read = false` (and excluded from `/tree`, search, and `/tags`), gets `401` on writes, and a PAT without `notes:read` gets `403`.

- [ ] **Step 2: Run `NoteEndpointsTests` to verify RED**

Run: `dotnet test -c Release --no-restore api/Atrium.sln --filter NoteEndpointsTests`
Expected: FAIL (types/endpoints do not exist yet).

- [ ] **Step 3: Implement `NoteContracts.cs`, `NoteFrontmatter.cs`, `NoteEndpoints.cs`, and register `app.MapNoteEndpoints()` in `Program.cs`**

Key implementation details:
- Compute `effectiveAllowAgentRead` by loading the user's active folders into a `Dictionary<Guid, NoteFolder>` and walking `ParentFolderId` with a visited-set guard.
- For SQLite compatibility in `TestAppFactory`, order `DateTimeOffset` client-side after `ToListAsync(ct)` (same pattern as `DayEndpoints.cs`).
- On `DELETE /api/notes/folders/{id}`, collect all descendant folder IDs + notes in those folders, set `DeletedAt = now` on all of them, and remove their `NoteTags` in a single transaction.

- [ ] **Step 4: Build Release `-warnaserror` and run full `.NET` test suite**

Run: `dotnet build --no-restore -c Release -warnaserror api/Atrium.sln && dotnet test -c Release --no-build api/Atrium.sln`
Expected: 0 warnings, 0 errors, all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add api/Atrium.Api/Notes/ api/Atrium.Api/Endpoints/NoteEndpoints.cs api/Atrium.Api/Program.cs api/Atrium.Api.Tests/NoteEndpointsTests.cs
git commit -m "feat(notes): add notes vault endpoints, frontmatter tag indexing, and integration tests"
```

---

### Task 3: Frontend Frontmatter Sync, Notes API Client & Live Preview Frontmatter Hiding

**Files:**
- Modify: `web/src/types/atrium.ts`
- Create: `web/src/lib/frontmatter.ts`
- Create: `web/src/lib/notesClient.ts`
- Modify: `web/src/lib/markdownLivePreview.ts`

**Interfaces:**
- Consumes: `apiFetch` from `web/src/lib/authClient.ts`
- Produces:
  - Types in `web/src/types/atrium.ts`: `NoteFolder`, `NoteSummary`, `NoteDetail`, `NoteTagCount`, `NotesTreeResponse`
  - Helpers in `web/src/lib/frontmatter.ts`:
    - `normalizeTag(raw: string): string | null`
    - `findFrontmatterRange(markdown: string): { from: number; to: number; bodyStart: number } | null`
    - `parseFrontmatter(markdown: string): { title: string | null; tags: string[]; body: string }`
    - `updateFrontmatterTags(markdown: string, nextTags: string[]): string` (preserves unknown YAML keys)
    - `updateFrontmatterTitleIfPresent(markdown: string, nextTitle: string): string` (only updates `title:` if already present)
    - `stripFrontmatterForReading(markdown: string): string`
  - Client in `web/src/lib/notesClient.ts`: `NoteConflictError`, `getNotesTree`, `searchNotes`, `getNote`, `createFolder`, `updateFolder`, `deleteFolder`, `createNote`, `updateNote`, `deleteNote`

- [ ] **Step 1: Add Notes Vault types to `web/src/types/atrium.ts` and implement `web/src/lib/frontmatter.ts` & `web/src/lib/notesClient.ts`**

Implement `frontmatter.ts` with exact Spec §4 frontmatter rules (preserving all unknown YAML lines when editing `tags:` or existing `title:`), and `notesClient.ts` parsing `409 Conflict` responses into `NoteConflictError` containing the server's latest `NoteDetail`.

- [ ] **Step 2: Hide leading YAML frontmatter in `web/src/lib/markdownLivePreview.ts`**

Use `findFrontmatterRange(doc.toString())` at the start of `buildLivePreviewDecorations`: when a valid frontmatter block exists at offset `0`, hide `[0, range.bodyStart]` via `Decoration.replace({})` (so Live Preview shows only the note body below the synced `NotePropertiesBar`, while Source mode still shows the raw `--- ... ---` block).

- [ ] **Step 3: Verify TypeScript compilation**

Run: `npm --prefix web run typecheck`
Expected: PASS (0 errors).

- [ ] **Step 4: Commit**

```bash
git add web/src/types/atrium.ts web/src/lib/frontmatter.ts web/src/lib/notesClient.ts web/src/lib/markdownLivePreview.ts
git commit -m "feat(notes): add frontmatter sync helper, notes vault client, and Live Preview frontmatter hiding"
```

---

### Task 4: Collapsible `NotesSidebar`, `NotePropertiesBar`, Live Vault `NotesPage` & Styles

**Files:**
- Create: `web/src/components/NotesSidebar.tsx`
- Create: `web/src/components/NotePropertiesBar.tsx`
- Modify: `web/src/components/NotesPage.tsx`
- Modify: `web/src/styles.css`

**Interfaces:**
- Consumes: `notesClient.ts`, `frontmatter.ts`, `MarkdownEditor.tsx`
- Produces: Full multi-note, folder-organized, auto-saving, searchable Notes Vault UI in `NotesPage.tsx`

- [ ] **Step 1: Create `web/src/components/NotesSidebar.tsx` and `web/src/components/NotePropertiesBar.tsx`**

- `NotesSidebar.tsx`:
  - Search input (debounced), clickable tag filter chips (`tags` with counts), **+ New Note** and **+ New Folder** actions.
  - Recursive folder tree with expand/collapse, subtle eye-off icon when `!effectiveAllowAgentRead`, inline rename, create child note/folder, toggle `allowAgentRead`, and inline delete confirmation.
  - Unfiled (root) notes and active note highlight.
- `NotePropertiesBar.tsx`:
  - Editable title input (in Live Preview & Reading modes; read-only heading in Reading mode).
  - Tag chips with `×` removal and inline `+ Add tag` input (`Enter`/`,` to add, `normalizeTag`).
  - Folder selector dropdown (move note to root or any folder) and **AI agents: Allowed / Hidden** toggle (locked with `"Hidden by folder"` explanation when an ancestor folder is hidden).

- [ ] **Step 2: Upgrade `web/src/components/NotesPage.tsx` to live server vault**

- Load `/api/notes/tree` on mount; manage `selectedNoteId`, `activeNote`, `draftContent`, `draftTitle`, `saveState` (`'idle' | 'saving' | 'saved' | 'error' | 'conflict'`), and `conflictServerNote`.
- Collapsible sidebar toggle button + `Cmd/Ctrl+\` shortcut (persisted in `localStorage`; slide-over drawer on mobile).
- 700 ms debounced auto-save with edit-sequence guard, per-note pending-save map (`pendingSavesRef`), flush on blur and note switch, and `409 Conflict` banner (**Reload latest** / **Overwrite with mine**).
- Reading mode passes `stripFrontmatterForReading(draftContent)` to `<ReactMarkdown>`.
- When user has 0 server notes and a non-empty non-sample `atrium.notes.pr1.draft` in `localStorage`, show an **"Import local draft"** button alongside **"Create first note"**.

- [ ] **Step 3: Add Atelier light/dark styles in `web/src/styles.css`**

Style `.notes-vault-layout`, `.notes-sidebar`, `.notes-sidebar-drawer` (mobile `< 768px`), `.notes-tree-folder`, `.notes-tree-note`, `.notes-properties-bar`, `.notes-tag-chip`, `.notes-conflict-banner`, and `.notes-empty-state`.

- [ ] **Step 4: Run full CI verification suite**

Run:
```bash
npm run check:suppressions && dotnet build --no-restore -c Release -warnaserror api/Atrium.sln && dotnet test -c Release --no-build api/Atrium.sln && npm --prefix web run typecheck && npm --prefix web run build
```
Expected: All checks pass with 0 warnings and 0 errors.

- [ ] **Step 5: Commit**

```bash
git add web/src/components/NotesSidebar.tsx web/src/components/NotePropertiesBar.tsx web/src/components/NotesPage.tsx web/src/styles.css
git commit -m "feat(notes): add collapsible folder sidebar, synced properties bar, and live server vault"
```

