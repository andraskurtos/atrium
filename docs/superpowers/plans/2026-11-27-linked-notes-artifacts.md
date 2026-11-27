# Linked Notes, Artifacts & Inline Images Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Link Markdown notes and uploadable file artifacts (PDFs, slides, etc.) to University courses/items and non-university Calendar events, organize them into a 3-section Notes sidebar (`General`, `University`, `Events`), and support hidden inline image uploads via paste/drop/toolbar in the Markdown editor.

**Architecture:** Extend `Note` with nullable link FKs (`UniversityCourseId`, `UniversityActivityItemId`, `CalendarEventId`, `CalendarOccurrenceStartAt`) and add a unified `Attachment` entity backed by a mounted disk volume (`IAttachmentStorage` / `LocalDiskAttachmentStorage`). `General` keeps manual `NoteFolder`s while `University` and `Events` sidebar sections are computed dynamically from live University and Calendar data, and embedded `/api/attachments/{id}` images render securely via Bearer-authenticated blob URLs.

**Tech Stack:** .NET 10 Minimal API, EF Core 10 (PostgreSQL + SQLite in-memory tests), xUnit, React 19, TypeScript, CodeMirror 6, Vite PWA.

**Spec:** `docs/superpowers/specs/2026-11-27-linked-notes-artifacts-design.md`

## Global Constraints

- .NET 10 with `-warnaserror` and zero undocumented warning suppressions (`npm run check:suppressions` must pass).
- EF Core migrations must stay in sync with `AppDbContext` (`MigrationModelTests` must pass) and follow `docs/engineering/database-migrations.md`.
- SQLite test compatibility: convert `DateTimeOffset` properties used in queries/unique indexes (`Note.CalendarOccurrenceStartAt`, `Attachment.CalendarOccurrenceStartAt`) to `UtcTicks` when `Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"`, and order by `DateTimeOffset` client-side.
- Upload limits: 10 MB max for `inline_image` (`image/png`, `image/jpeg`, `image/webp`, `image/gif` — never `image/svg+xml`), 25 MB max for `artifact` (always served with `Content-Disposition: attachment` and `X-Content-Type-Options: nosniff`).
- Storage keys must be `{userId}/{attachmentId}` on disk — never use user-supplied filenames in filesystem paths.
- Anti-duplication: University calendar events (`Module == "university"` or referenced by `UniversityActivityItem.CalendarEventId`) must never create Event notes or appear under the Events section.
- Least-privilege Agent PAT scopes: `notes:read`, `university:read`, and `calendar:read` are enforced independently across `GET /api/notes/tree` and `/api/attachments`.

## Review Focus

1. **Duplicate/concurrent find-or-create on `POST /api/notes/linked`**: Two rapid clicks on "Open Note" for the same `Lecture 1` or event occurrence must not create duplicate notes or return a `500`; handle unique-constraint races by re-querying and returning the existing note (`200 OK`). *(Pinned in Task 3)*
2. **Filename sanitization & header injection on artifact upload/download**: Filenames containing path separators (`../../etc/passwd`), quotes, or control/CRLF characters must be sanitized on upload and safely encoded in `Content-Disposition` on download. *(Pinned in Task 2)*
3. **Recurring event override vs. master ID**: Clicking "Open Note" on an edited occurrence of a recurring event (which has `RecurrenceMasterId != null`) must anchor `CalendarEventId` to the series master and `CalendarOccurrenceStartAt` to `RecurrenceOriginalStartAt ?? StartAt` so occurrence notes and the shared series note stay grouped under the same series. *(Pinned in Task 3)*
4. **Course/item/event deletion preserving notes & cleaning up disk files**: Deleting a `UniversityCourse`, `UniversityActivityItem`, or `CalendarEvent` must null out `Note` link FKs (preserving user-written notes in General Unfiled) while removing entity-owned `Attachment` rows cleanly. *(Pinned in Task 1 & Task 3)*
5. **Hidden inline images never leaking into artifact lists or empty-event inclusion**: Uploading an `inline_image` to a note must never appear in `GET /api/attachments`, `GET /api/notes/tree` artifact lists, or the editor's Artifacts strip; and deleting the only note/artifact on an event must remove that event from the Events sidebar section. *(Pinned in Task 2 & Task 3)*

---

## File Map

**Backend — Create:**
- `api/Atrium.Api/Data/Entities/Attachment.cs` — `Attachment` entity (`artifact` | `inline_image`)
- `api/Atrium.Api/Data/Migrations/20261127140000_AddLinkedNotesAndAttachments.cs` (+ `.Designer.cs`) — EF migration
- `api/Atrium.Api/Storage/StorageOptions.cs` — config (`UploadsPath`, `MaxArtifactBytes`, `MaxImageBytes`)
- `api/Atrium.Api/Storage/IAttachmentStorage.cs` — storage interface + `LocalDiskAttachmentStorage`
- `api/Atrium.Api/Attachments/AttachmentContracts.cs` — DTOs & validation constants
- `api/Atrium.Api/Endpoints/AttachmentEndpoints.cs` — upload, list, stream, delete endpoints
- `api/Atrium.Api.Tests/AttachmentEndpointsTests.cs` — attachment & security integration tests
- `api/Atrium.Api.Tests/LinkedNoteEndpointsTests.cs` — linked notes, virtual trees, PAT scope & anti-duplication tests

**Backend — Modify:**
- `api/Atrium.Api/Data/Entities/Note.cs` — add nullable link FKs
- `api/Atrium.Api/Data/AppDbContext.cs` & `AppDbContextModelSnapshot.cs` — configure `Note` links, `Attachment`, indexes, SQLite conversions
- `api/Atrium.Api/Notes/NoteContracts.cs` — extend `NoteSummaryDto`, `NoteDetailDto`, `NotesTreeResponse`, `CreateNoteRequest`, add `OpenLinkedNoteRequest` & virtual tree DTOs
- `api/Atrium.Api/Endpoints/NoteEndpoints.cs` — add `POST /api/notes/linked`, course note creation, virtual `university` & `events` tree sections, link-aware search
- `api/Atrium.Api/Endpoints/UniversityEndpoints.cs` — include `noteCount`, `artifactCount`, `linkedNoteId` on workspace DTOs
- `api/Atrium.Api/Endpoints/CalendarEndpoints.cs` — include `universityActivityItemId` / `hasNote` / `artifactCount` metadata on event DTOs
- `api/Atrium.Api/Program.cs` & `api/Atrium.Api/appsettings.json` — register `StorageOptions`, `IAttachmentStorage`, and `MapAttachmentEndpoints()`
- `docker-compose.yml` — mount `atrium_uploads:/app/uploads` and set `Atrium__Storage__UploadsPath`
- `api/Atrium.Api.Tests/TestAppFactory.cs` — configure temp uploads directory per test factory instance (cleaned up on dispose)

**Frontend — Create:**
- `web/src/lib/attachmentsClient.ts` — upload, list, delete, authenticated download, and blob-URL cache for `/api/attachments/{id}`

**Frontend — Modify:**
- `web/src/types/atrium.ts` — add `AttachmentSummary`, `NoteLinkContext`, `UniversityNotesTree`, `EventNotesGroup`, and workspace count fields
- `web/src/lib/markdownUtils.ts` — allow relative `/api/attachments/<guid>` in `sanitizeMarkdownUrl`
- `web/src/lib/markdownLivePreview.ts` — resolve `/api/attachments/<guid>` via `attachmentsClient` in `ImageWidget`
- `web/src/lib/markdownReading.tsx` — resolve `/api/attachments/<guid>` via `attachmentsClient` in `img` renderer
- `web/src/lib/notesClient.ts` — add `openLinkedNote`, extend `createNote` (`universityCourseId`) and `getNotesTree`
- `web/src/components/MarkdownEditor.tsx` — intercept `paste` / `drop` image files and expose `insertImageFile`
- `web/src/components/NotesSidebar.tsx` — 3-section sidebar (`General`, `University` [default ON], `Events` [default OFF]) with virtual trees & artifact download/upload
- `web/src/components/NotePropertiesBar.tsx` — context breadcrumb, hide folder picker for linked notes
- `web/src/components/NotesPage.tsx` — handle `initialNoteId` / `pendingNoteTarget`, render Artifacts strip, wire image paste/drop/toolbar upload
- `web/src/components/UniversityPage.tsx` — Course Notes & Artifacts panel + Item Open Note & Artifacts controls
- `web/src/components/CalendarCard.tsx` — Event/Occurrence/Shared Note & Artifacts controls + University-event redirect link
- `web/src/App.tsx` — `openLinkedNote` cross-view navigation wiring
- `web/src/styles.css` — styles for 3-section sidebar, artifacts strip, context breadcrumb, and University/Calendar note controls

---

### Task 1: Schema, Entities, EF Migration & Disk Attachment Storage

**Files:**
- Create: `api/Atrium.Api/Data/Entities/Attachment.cs`
- Create: `api/Atrium.Api/Storage/StorageOptions.cs`
- Create: `api/Atrium.Api/Storage/IAttachmentStorage.cs`
- Create: `api/Atrium.Api/Data/Migrations/20261127140000_AddLinkedNotesAndAttachments.cs`
- Create: `api/Atrium.Api/Data/Migrations/20261127140000_AddLinkedNotesAndAttachments.Designer.cs`
- Modify: `api/Atrium.Api/Data/Entities/Note.cs`
- Modify: `api/Atrium.Api/Data/AppDbContext.cs`
- Modify: `api/Atrium.Api/Data/Migrations/AppDbContextModelSnapshot.cs`
- Modify: `api/Atrium.Api/Program.cs`
- Modify: `api/Atrium.Api/appsettings.json`
- Modify: `docker-compose.yml`
- Modify: `api/Atrium.Api.Tests/TestAppFactory.cs`
- Test: `api/Atrium.Api.Tests/MigrationModelTests.cs`

**Interfaces:**
- Produces:
  - `Note.UniversityCourseId`, `Note.UniversityActivityItemId`, `Note.CalendarEventId`, `Note.CalendarOccurrenceStartAt`
  - `Attachment` entity (`Id`, `UserId`, `Kind`, `FileName`, `ContentType`, `SizeBytes`, `Sha256`, `StorageKey`, `NoteId`, `UniversityCourseId`, `UniversityActivityItemId`, `CalendarEventId`, `CalendarOccurrenceStartAt`, `AllowAgentRead`, `CreatedAt`, `DeletedAt`)
  - `IAttachmentStorage`: `Task SaveAsync(string storageKey, Stream content, CancellationToken ct)`, `Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct)`, `Task DeleteAsync(string storageKey, CancellationToken ct)`
  - `StorageOptions` (`SectionName = "Atrium:Storage"`, `UploadsPath`, `MaxArtifactBytes = 25 * 1024 * 1024`, `MaxImageBytes = 10 * 1024 * 1024`)

- [ ] **Step 1: Add `Attachment` entity and extend `Note` with nullable link FKs**
  - Add `UniversityCourseId`, `UniversityActivityItemId`, `CalendarEventId`, `CalendarOccurrenceStartAt` (plus navigation properties) to `api/Atrium.Api/Data/Entities/Note.cs`.
  - Create `api/Atrium.Api/Data/Entities/Attachment.cs` matching Spec §4.2.
- [ ] **Step 2: Configure `AppDbContext` mappings, partial unique indexes, and SQLite `UtcTicks` conversions**
  - Configure `Note` FKs with `OnDelete(DeleteBehavior.SetNull)`, partial unique indexes `ix_notes_uni_item_unique`, `ix_notes_event_occurrence_unique`, `ix_notes_event_shared_unique`, and lookup indexes `(user_id, university_course_id, deleted_at)` and `(user_id, calendar_event_id, deleted_at)`.
  - Configure `Attachment` table `attachments` with `Cascade` FKs to `User`, `Note`, `UniversityCourse`, `UniversityActivityItem`, `CalendarEvent`, and indexes on `(user_id, note_id, deleted_at)`, `(user_id, university_course_id, deleted_at)`, `(user_id, university_activity_item_id, deleted_at)`, `(user_id, calendar_event_id, deleted_at)`.
  - Add SQLite `UtcTicks` value conversions for `Note.CalendarOccurrenceStartAt` and `Attachment.CalendarOccurrenceStartAt`.
- [ ] **Step 3: Create EF Core migration `20261127140000_AddLinkedNotesAndAttachments` and update `AppDbContextModelSnapshot.cs`**
  - Include all columns, foreign keys, and PostgreSQL partial unique indexes matching `AppDbContext`.
- [ ] **Step 4: Implement `StorageOptions`, `IAttachmentStorage`, `LocalDiskAttachmentStorage`, DI registration, `docker-compose.yml` volume, and `TestAppFactory` temp upload dir**
  - `LocalDiskAttachmentStorage` resolves `Path.GetFullPath(Path.Combine(root, storageKey))`, asserts the resolved path starts with `root` (defense in depth against path traversal), creates parent directories, streams writes, and deletes files cleanly.
  - Register `StorageOptions` and `IAttachmentStorage` in `Program.cs`, add `Atrium:Storage` defaults to `appsettings.json`, mount `atrium_uploads:/app/uploads` in `docker-compose.yml`, and give `TestAppFactory` a unique temp directory cleaned up on dispose.
- [ ] **Step 5: Run `MigrationModelTests` and full API build/test**
  - Run: `dotnet build --no-restore -c Release -warnaserror api/Atrium.sln && dotnet test -c Release --no-build --filter MigrationModelTests api/Atrium.Api.Tests/Atrium.Api.Tests.csproj`
  - Expected: 0 warnings, 0 errors, `MigrationModelTests` PASS (no pending model changes).

---

### Task 2: Attachments API & Security/Validation (`AttachmentEndpoints.cs`)

**Files:**
- Create: `api/Atrium.Api/Attachments/AttachmentContracts.cs`
- Create: `api/Atrium.Api/Endpoints/AttachmentEndpoints.cs`
- Modify: `api/Atrium.Api/Program.cs` (map `app.MapAttachmentEndpoints()`)
- Test: `api/Atrium.Api.Tests/AttachmentEndpointsTests.cs`

**Interfaces:**
- Consumes: `AppDbContext`, `Attachment`, `IAttachmentStorage`, `StorageOptions`, `AuthConstants`
- Produces:
  - `AttachmentDto(Guid Id, string Kind, string FileName, string ContentType, long SizeBytes, string Url, DateTimeOffset CreatedAt)`
  - `POST /api/attachments` (`multipart/form-data`: `file`, `kind`, `noteId?`, `universityCourseId?`, `universityActivityItemId?`, `calendarEventId?`, `calendarOccurrenceStartAt?`)
  - `GET /api/attachments` (query: `noteId?`, `universityCourseId?`, `universityActivityItemId?`, `calendarEventId?`, `occurrenceStartAt?` — returns only `kind == "artifact"`)
  - `GET /api/attachments/{id:guid}` (authenticated stream)
  - `DELETE /api/attachments/{id:guid}` (soft-delete + disk cleanup)

- [ ] **Step 1: Write failing integration tests in `api/Atrium.Api.Tests/AttachmentEndpointsTests.cs`**
  - `UploadAndDownload_Artifact_OnCourseAndItem_StreamsWithAttachmentHeaders`: verifies `201 Created`, `Content-Disposition: attachment`, `X-Content-Type-Options: nosniff`, exact byte match, and `GET /api/attachments` returns it.
  - `UploadInlineImage_HiddenFromArtifactList_StreamsInline`: verifies `kind=inline_image` on a note is NOT returned by `GET /api/attachments?noteId=...`, and `GET /api/attachments/{id}` returns `Content-Disposition: inline`.
  - `UploadValidation_RejectsSvgInlineImage_BlockedExtensions_AndOversize`: verifies `image/svg+xml` as `inline_image` → `400`, `.exe`/`.html` as `artifact` → `400`, oversize file → `400`/`413`, and invalid/multiple owner FKs → `400`.
  - `FilenameSanitization_StripsPathTraversalAndControlChars`: uploads `../../evil"\r\n.pdf` and verifies stored `FileName` and `Content-Disposition` contain no path segments, quotes, or CRLF *(Review Focus #2)*.
  - `AuthAndAgentScopes_EnforcesUserIsolation_ModuleScopes_AndAllowAgentReadInheritance`: verifies another user gets `404`; Agent PAT with `notes:read` only can download a note attachment (`200`) but gets `403` on a course/item artifact without `university:read` and `403` on an event artifact without `calendar:read`; and when a linked note has `AllowAgentRead = false`, its shared item/event artifact is also hidden from agents (`404`).
  - `DeleteAttachment_RemovesRecordAndDiskFile`: verifies `204 No Content`, subsequent `GET` returns `404`, and disk file is removed.
- [ ] **Step 2: Run tests to verify they fail**
  - Run: `dotnet test -c Release --filter AttachmentEndpointsTests api/Atrium.Api.Tests/Atrium.Api.Tests.csproj`
  - Expected: FAIL (compile error / 404).
- [ ] **Step 3: Implement `AttachmentContracts.cs`, `AttachmentEndpoints.cs`, and register in `Program.cs`**
  - Enforce owner exclusivity (`inline_image` → `noteId` only; `artifact` → exactly one of `noteId`, `universityCourseId`, `universityActivityItemId` [auto-populating `universityCourseId`], or `calendarEventId` [+ optional `calendarOccurrenceStartAt`]).
  - Reject attaching to a university calendar event via `calendarEventId` (`400 Bad Request`).
  - Compute SHA-256 while buffering/streaming to `IAttachmentStorage` (enforcing size cap before writing full stream), sanitize filename (`Path.GetFileName`, strip control chars, clamp to 255 chars), and enforce MIME/extension allowlist.
- [ ] **Step 4: Run tests to verify they pass**
  - Run: `dotnet build --no-restore -c Release -warnaserror api/Atrium.sln && dotnet test -c Release --no-build --filter AttachmentEndpointsTests api/Atrium.Api.Tests/Atrium.Api.Tests.csproj`
  - Expected: All `AttachmentEndpointsTests` PASS, 0 warnings.

---

### Task 3: Linked Notes API, Virtual Sidebar Trees & Workspace Summaries

**Files:**
- Modify: `api/Atrium.Api/Notes/NoteContracts.cs`
- Modify: `api/Atrium.Api/Endpoints/NoteEndpoints.cs`
- Modify: `api/Atrium.Api/Endpoints/UniversityEndpoints.cs`
- Modify: `api/Atrium.Api/Endpoints/CalendarEndpoints.cs`
- Test: `api/Atrium.Api.Tests/LinkedNoteEndpointsTests.cs`

**Interfaces:**
- Consumes: `Note` link FKs, `Attachment`, `AttachmentDto`, `UniversityCourse`, `UniversityCourseActivity`, `UniversityActivityItem`, `CalendarEvent`
- Produces:
  - `OpenLinkedNoteRequest(string Kind, Guid? UniversityActivityItemId = null, Guid? CalendarEventId = null, DateTimeOffset? OccurrenceStartAt = null)`
  - `POST /api/notes/linked` → `200 OK` (existing) or `201 Created` (new) returning `NoteDetailDto`
  - Extended `NoteSummaryDto` & `NoteDetailDto` with `Guid? UniversityCourseId`, `Guid? UniversityActivityItemId`, `Guid? CalendarEventId`, `DateTimeOffset? CalendarOccurrenceStartAt`, `string LinkKind`, `string? LinkLabel`
  - Extended `NotesTreeResponse` with `IReadOnlyList<UniversitySemesterTreeDto> University` and `IReadOnlyList<EventNotesGroupDto> Events`
  - Extended `CreateNoteRequest` with `Guid? UniversityCourseId = null`
  - Extended University workspace DTOs (`NoteCount`, `ArtifactCount`, `LinkedNoteId`) and Calendar event DTOs (`UniversityActivityItemId`, `HasNote`, `ArtifactCount`)

- [ ] **Step 1: Write failing integration tests in `api/Atrium.Api.Tests/LinkedNoteEndpointsTests.cs`**
  - `OpenLinkedNote_UniversityItem_CreatesOnce_SeedsLegacyNotes_AndIsIdempotent`: creates item with legacy `Notes = "Review Big-O"`, calls `POST /api/notes/linked` twice → first `201` with content containing `"Review Big-O"`, second `200` returning the exact same note ID *(Review Focus #1)*.
  - `CourseFreeformNotes_AllowMultipleNotes_AndAppearUnderCourseInTree`: creates 2 freeform notes with `universityCourseId` + 1 course artifact, verifies `GET /api/notes/tree` returns `Semester → Course → { Notes, Artifacts, Activities → Items → { Note, Artifacts } }` and does NOT include them in `General` `notes`.
  - `CalendarEventNotes_RecurringOccurrencesAndSharedNote_AndOverrideNormalization`: opens occurrence note on a recurring event (including via an override `CalendarEvent` row whose `RecurrenceMasterId` points to master) + opens a shared series note (`occurrenceStartAt = null`), verifies both group cleanly under the master event in `GET /api/notes/tree` `events`, and events with no notes/artifacts are omitted *(Review Focus #3 & #5)*.
  - `UniversityCalendarEvent_CannotCreateDuplicateEventNote`: calling `POST /api/notes/linked` with `kind = "calendar_event"` on a uni item's calendar event returns `400`, and uni items never appear in `GET /api/notes/tree` `events`.
  - `DeleteCourseOrEvent_PreservesNoteInGeneralUnfiled_AndCleansTree`: deleting a course or event sets the note's link FK to `null` (note remains accessible in General) and removes the empty event from `events` *(Review Focus #4)*.
  - `AgentPatScopes_FilterVirtualSectionsInNotesTree`: Agent PAT with `notes:read` only gets General notes with empty `university` and `events`; adding `university:read` populates `university`; adding `calendar:read` populates `events`.
  - `SearchNotes_FindsLinkedNotesAcrossAllSections`: `GET /api/notes?q=...&searchIn=content` returns matching General, University, and Event notes with `LinkKind` and `LinkLabel`.
- [ ] **Step 2: Run tests to verify they fail**
  - Run: `dotnet test -c Release --filter LinkedNoteEndpointsTests api/Atrium.Api.Tests/Atrium.Api.Tests.csproj`
  - Expected: FAIL.
- [ ] **Step 3: Implement `NoteContracts.cs`, `NoteEndpoints.cs`, `UniversityEndpoints.cs`, and `CalendarEndpoints.cs`**
  - In `POST /api/notes/linked`: normalize recurring override events to `(masterId, occurrenceStartAt ?? evt.RecurrenceOriginalStartAt ?? evt.StartAt)`; catch `DbUpdateException` on unique index race and re-query existing note (`200 OK`).
  - In `PUT /api/notes/{id}`: disallow assigning `FolderId` on a linked note (`400 Bad Request`).
  - In `GET /api/notes/tree`: compute `university` (grouped by semester, `Uncategorized` if null) and `events` (only non-uni events having ≥1 note or ≥1 `artifact`, never `inline_image`), gated by PAT scopes (`university:read` / `calendar:read`) for agent tokens.
  - In `UniversityEndpoints` and `CalendarEndpoints`: attach lightweight `NoteCount`, `ArtifactCount`, `LinkedNoteId`, `UniversityActivityItemId`, `HasNote` counts via single grouped queries (no N+1).
- [ ] **Step 4: Run full API build and test suite**
  - Run: `dotnet build --no-restore -c Release -warnaserror api/Atrium.sln && dotnet test -c Release --no-build api/Atrium.sln`
  - Expected: All existing + new tests PASS, 0 warnings.

---

### Task 4: Frontend Attachment Client & Hidden Inline Image Paste/Drop/Render

**Files:**
- Create: `web/src/lib/attachmentsClient.ts`
- Modify: `web/src/types/atrium.ts`
- Modify: `web/src/lib/markdownUtils.ts`
- Modify: `web/src/lib/markdownLivePreview.ts`
- Modify: `web/src/lib/markdownReading.tsx`
- Modify: `web/src/components/MarkdownEditor.tsx`
- Modify: `web/src/components/NotesPage.tsx`

**Interfaces:**
- Produces:
  - `web/src/lib/attachmentsClient.ts`:
    - `uploadAttachment(file: File, options: UploadAttachmentOptions): Promise<AttachmentSummary>`
    - `listAttachments(filter: AttachmentFilter): Promise<AttachmentSummary[]>`
    - `deleteAttachment(id: string): Promise<void>`
    - `downloadAttachment(id: string, fileName: string): Promise<void>`
    - `resolveAttachmentImageUrl(url: string): Promise<string | null>` (cached `URL.createObjectURL` for `/api/attachments/{guid}`)
    - `isInternalAttachmentUrl(url: string): boolean`
  - `MarkdownEditorProps.onUploadInlineImage?: (file: File) => Promise<{ url: string; alt: string } | null>`
  - `MarkdownEditorHandle.insertImageMarkdown(alt: string, url: string): void`

- [ ] **Step 1: Add `AttachmentSummary` and linked-note types to `web/src/types/atrium.ts` and implement `web/src/lib/attachmentsClient.ts`**
  - Use `getAccessToken()` + `fetch` (handling `401` refresh via existing `authClient` pattern) for `FormData` upload, authenticated blob download (`<a download>`), and an LRU/Map blob-URL cache for `resolveAttachmentImageUrl`.
- [ ] **Step 2: Allow `/api/attachments/<guid>` in `sanitizeMarkdownUrl` (`web/src/lib/markdownUtils.ts`)**
  - Match `/^\/api\/attachments\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i` when `kind === 'image'`.
- [ ] **Step 3: Resolve internal attachment image URLs in `markdownLivePreview.ts` (`ImageWidget`) and `markdownReading.tsx`**
  - When `isInternalAttachmentUrl(safeUrl)` is true, call `resolveAttachmentImageUrl(safeUrl)` asynchronously and set `img.src = objectUrl` (with clean loading/error fallback). External `https://` images continue rendering directly.
- [ ] **Step 4: Add clipboard paste, drag-drop, and toolbar file-picker image upload in `MarkdownEditor.tsx` and `NotesPage.tsx`**
  - In `MarkdownEditor.tsx`: register `EditorView.domEventHandlers({ paste, drop })` that detects image files (`png`, `jpeg`, `webp`, `gif`), prevents default, calls `onUploadInlineImage(file)`, and inserts `![alt](url)` at the cursor/drop position.
  - In `NotesPage.tsx`: wire `onUploadInlineImage` to `uploadAttachment(file, { kind: 'inline_image', noteId: activeNote.id })`, and update the toolbar `Image` button to open a hidden `<input type="file" accept="image/png,image/jpeg,image/webp,image/gif">` (with shift-click or secondary URL button for external URLs).
- [ ] **Step 5: Run frontend typecheck and build**
  - Run: `npm --prefix web run typecheck && npm --prefix web run build`
  - Expected: 0 errors, clean build.

---

### Task 5: 3-Section Notes Sidebar & Editor Context/Artifacts Strip

**Files:**
- Modify: `web/src/lib/notesClient.ts`
- Modify: `web/src/components/NotesSidebar.tsx`
- Modify: `web/src/components/NotePropertiesBar.tsx`
- Modify: `web/src/components/NotesPage.tsx`
- Modify: `web/src/styles.css`

**Interfaces:**
- Consumes: `getNotesTree`, `openLinkedNote`, `createNote`, `uploadAttachment`, `listAttachments`, `downloadAttachment`, `deleteAttachment`
- Produces:
  - `NotesPageProps`: adds optional `initialTarget?: PendingNoteTarget | null`, `onConsumeInitialTarget?: () => void`, `onJumpToUniversityCourse?: (courseId: string) => void`
  - Sidebar section toggles persisted in `localStorage`: `atrium.notes.sidebar.showUni` (default `true`), `atrium.notes.sidebar.showEvents` (default `false`)

- [ ] **Step 1: Extend `web/src/lib/notesClient.ts`**
  - Add `openLinkedNote(input: OpenLinkedNoteInput): Promise<NoteDetail>` (`POST /api/notes/linked`).
  - Update `createNote` to accept optional `universityCourseId?: string | null`.
  - Update `getNotesTree` return type to include `university` and `events`.
- [ ] **Step 2: Redesign `NotesSidebar.tsx` with 3 sections (`General`, `University`, `Events`)**
  - Add compact `Uni` (default ON) and `Events` (default OFF) toggle chips in the sidebar top bar.
  - Render **General** section (existing folders, unfiled notes, drag-and-drop).
  - Render **University** section (when toggled ON): collapsible `Semester → Course (+ Note, + Upload) → [Course Notes, Course Artifacts, Activity Groups (Lectures/Practices/Labs) → Items (+ Upload) → Item Note + Item Artifacts]`. Clicking an item with no note yet calls `onOpenLinkedNote({ kind: 'university_item', universityActivityItemId })`.
  - Render **Events** section (when toggled OFF by default): only events returned by `tree.events`, showing single-event note + artifacts or recurring shared note + dated occurrences.
  - Clicking any artifact triggers `downloadAttachment(artifact.id, artifact.fileName)`.
- [ ] **Step 3: Add Context Breadcrumb & Artifacts Strip to `NotePropertiesBar.tsx` and `NotesPage.tsx`**
  - Hide the General folder dropdown in `NotePropertiesBar` when `activeNote.linkKind !== 'general'`, and show a compact context breadcrumb (with a jump-back link to University when applicable).
  - Render the **Artifacts strip** below `NotePropertiesBar`: loads and uploads artifacts against the exact owner target per Spec §7.3 (`universityActivityItemId` for item notes, `calendarEventId` + `calendarOccurrenceStartAt` for event notes, `noteId` for General and Course freeform notes), with download and delete buttons.
- [ ] **Step 4: Add CSS for sidebar sections, toggles, artifact chips, and context breadcrumb in `web/src/styles.css`**
- [ ] **Step 5: Run frontend typecheck and build**
  - Run: `npm --prefix web run typecheck && npm --prefix web run build`
  - Expected: 0 errors, clean build.

---

### Task 6: University Page, Calendar Card & Cross-View Navigation + Full Verification

**Files:**
- Modify: `web/src/App.tsx`
- Modify: `web/src/components/UniversityPage.tsx`
- Modify: `web/src/components/CalendarCard.tsx`
- Modify: `web/src/styles.css`

**Interfaces:**
- Consumes: `openLinkedNote`, `uploadAttachment`, `listAttachments`, `downloadAttachment`, `deleteAttachment`, `NotesPage` `initialTarget`

- [ ] **Step 1: Wire cross-view navigation in `web/src/App.tsx`**
  - Add `pendingNoteTarget` state and `handleOpenLinkedNote(target)` callback that switches `activeView` to `'notes'` and passes `initialTarget` to `NotesPage`.
  - Add `handleJumpToUniversityCourse(courseId)` so Notes breadcrumbs can jump back to the course in `UniversityPage`.
- [ ] **Step 2: Add Course Notes/Artifacts & Item Open Note/Artifacts in `web/src/components/UniversityPage.tsx`**
  - In the course detail header/panel: show course-level freeform notes (click to open in Notes, `+ New Note` button) and course-level artifacts (`+ Upload`, download, delete).
  - On each activity item card (`Lecture 1`, `Lab 1`, etc.): add **Open Note** button (highlighted when `item.linkedNoteId` exists) and an **Artifacts** control to upload/download/delete item artifacts. Replace the legacy plain-text `Notes` textarea in the item edit dialog with a link to open the Markdown note.
- [ ] **Step 3: Add Event/Occurrence/Shared Note & Artifacts controls in `web/src/components/CalendarCard.tsx`**
  - In the event edit/detail modal:
    - If the event belongs to the University calendar or has `universityActivityItemId`: show a single **"Open University Item Note"** button that routes directly to `university_item` (never creating an Events duplicate).
    - If it is a non-university single event: show **"Open Note"** button + **Artifacts** upload/download/delete list.
    - If it is a non-university recurring event: show **"Open Note for this Date"** button, **"Open Shared Series Note"** button, and **Artifacts** upload/download/delete list for the occurrence.
- [ ] **Step 4: Run full CI verification suite**
  - Run:
    ```bash
    npm run check:suppressions && node --test scripts/check-suppressions.test.mjs && dotnet build --no-restore -c Release -warnaserror api/Atrium.sln && dotnet test -c Release --no-build api/Atrium.sln && npm --prefix web run typecheck && npm --prefix web run build
    ```
  - Expected: 0 suppression violations, 8/8 suppression tests pass, 0 C# warnings/errors, all xUnit tests pass, 0 TS errors, clean Vite + PWA build.

