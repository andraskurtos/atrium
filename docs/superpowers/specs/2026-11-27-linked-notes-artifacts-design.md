# Linked Notes, Artifacts & Inline Images — Design Spec

**Date:** 2026-11-27  
**Status:** Draft (pending builder check & user approval)  
**Builds on:** Notes Vault & Storage (`docs/superpowers/specs/2026-11-27-notes-vault-storage-design.md`), University Courses, Calendar Backend

---

## 1. Intent & Why

Notes in Atrium currently live in an isolated vault of manual folders, while University courses/items and Calendar events live in their own views without rich Markdown notes or file attachments. This feature unifies them:

- Every university activity item (lecture, practice, lab, etc.) and every non-university calendar event can have rich Markdown notes and uploaded study/meeting artifacts (PDFs, slides, worksheets, etc.).
- University courses also act as workspaces where you can add course-wide notes (e.g., exam prep, formula sheets) and course-wide artifacts (syllabus, past exams).
- All notes and artifacts surface cleanly inside the Notes sidebar under three sections (**General**, **University**, **Events**), with visibility toggles so your sidebar stays uncluttered.
- Pasting or inserting images directly into any Markdown note uploads them quietly and embeds them inline without cluttering artifact lists or the sidebar.

---

## 2. Goals, Non-Goals & Anti-Goals

### Goals
1. **University Linked Notes & Artifacts:**
   - Single dedicated Markdown note per `UniversityActivityItem` (auto-created on first open), plus uploadable artifacts on that item.
   - Multiple freeform Markdown notes and uploadable artifacts at the `UniversityCourse` level.
2. **Calendar Event Linked Notes & Artifacts:**
   - Non-university `CalendarEvent`s support a dedicated note and uploadable artifacts.
   - Recurring events get **one note per occurrence date** by default, plus the ability to open/create a **shared series note** on the recurring event itself.
3. **3-Section Notes Sidebar:**
   - **General:** User-managed folders and unfiled notes (existing drag-and-drop behavior).
   - **University** (toggleable, **on** by default): Virtual hierarchy `Semester / Course / Activity Type / Item` showing course notes, course artifacts, item notes, and item artifacts.
   - **Events** (toggleable, **off** by default): Non-university events/occurrences that actually have a note or artifact.
4. **Hidden Inline Image Uploads:**
   - Pasting an image from clipboard, dragging/dropping an image onto the editor, or choosing an image from the toolbar uploads it as a hidden `inline_image` attachment and embeds `![alt](/api/attachments/{id})` in the Markdown.
5. **Persistent Disk File Storage:**
   - Upload binary files (artifacts and inline images) to a mounted disk volume (`atrium_uploads`) backed by Postgres metadata and authenticated streaming endpoints.

### Non-Goals
- No in-app PDF, PowerPoint, or document viewer yet (artifacts are downloaded or opened via browser download).
- No custom nested subfolders inside University courses or Events.
- No automatic PDF text extraction, OCR, or RAG vectorization in this phase.
- No external S3/object storage provider yet (disk storage sits behind an `IAttachmentStorage` interface for future swap-in).

### Anti-Goals
- **Never duplicate** university calendar events under the **Events** section.
- **Never show empty events** (events with no note and no artifacts) in the **Events** section.
- **Never clutter** artifact lists or the sidebar with pasted inline images.
- **Never drift out of sync** when courses, semesters, activity items, or calendar events are renamed, moved, or deleted.
- **Never serve** user-uploaded files publicly without authentication.

---

## 3. Approaches Considered

### Approach 1: Virtual Sidebar Trees + Disk Uploads Volume *(Selected)*
- **How it works:** Add nullable link columns on `Note` (`UniversityCourseId`, `UniversityActivityItemId`, `CalendarEventId`, `CalendarOccurrenceStartAt`) and a unified `Attachment` table whose raw bytes live on a mounted disk volume (`atrium_uploads`). `General` keeps `NoteFolder`; `University` and `Events` sidebar trees are computed dynamically from live University and Calendar records.
- **Tradeoffs:** Zero shadow-folder sync bugs when courses/semesters/events change; keeps Postgres lean by keeping multi-MB PDFs/slides on disk. Does not support arbitrary nested custom subfolders inside courses, and adds one Docker volume to back up alongside Postgres.

### Approach 2: Materialized System `NoteFolder`s + Disk Uploads Volume
- **How it works:** Auto-create `NoteFolder` rows marked `IsSystemManaged` mirroring every semester, course, activity, item, and event.
- **Tradeoffs:** Allows arbitrary nested subfolders inside courses, but requires bidirectional sync hooks on every University and Calendar mutation and risks orphaned or out-of-sync folders.

### Approach 3: Virtual Sidebar Trees + Postgres `bytea` Blobs
- **How it works:** Same virtual tree model as Approach 1, storing binary files directly in Postgres `bytea` columns.
- **Tradeoffs:** Single backup target (`pg_dump`), but multi-MB slide decks and PDFs bloat database storage, WAL, and memory quickly.

**Why Approach 1:** Virtual trees guarantee University and Calendar stay 100% in sync with zero fragile folder-sync code, and disk storage keeps Postgres fast and lean for multi-MB study artifacts.

---

## 4. Data Model & Schema (PostgreSQL + EF Core)

### 4.1 Extensions to `Note` (`notes` table)
Add four nullable columns:
- `university_course_id` (`uuid`, nullable, FK → `university_courses.id`, `OnDelete(DeleteBehavior.SetNull)`)
- `university_activity_item_id` (`uuid`, nullable, FK → `university_activity_items.id`, `OnDelete(DeleteBehavior.SetNull)`)
- `calendar_event_id` (`uuid`, nullable, FK → `calendar_events.id`, `OnDelete(DeleteBehavior.SetNull)`)
- `calendar_occurrence_start_at` (`timestamptz`, nullable — UTC occurrence start for recurring event occurrence notes; `null` for single events or shared series notes)

**Link exclusivity rules** (enforced in API validation):
- **General note:** `UniversityCourseId == null && UniversityActivityItemId == null && CalendarEventId == null`. Only General notes may have a non-null `FolderId`.
- **Course freeform note:** `UniversityCourseId != null && UniversityActivityItemId == null && CalendarEventId == null && FolderId == null`.
- **University item note:** `UniversityActivityItemId != null && UniversityCourseId != null && CalendarEventId == null && FolderId == null`.
- **Event note:** `CalendarEventId != null && UniversityCourseId == null && UniversityActivityItemId == null && FolderId == null`. The target `CalendarEvent` must **not** belong to a `UserCalendar` with `Module == "university"` and must **not** be linked by any `UniversityActivityItem.CalendarEventId`.

**Partial unique indexes** (ignoring soft-deleted notes):
- `ix_notes_uni_item_unique`: unique on `(university_activity_item_id)` WHERE `university_activity_item_id IS NOT NULL AND deleted_at IS NULL` (1 dedicated note per uni item).
- `ix_notes_event_occurrence_unique`: unique on `(calendar_event_id, calendar_occurrence_start_at)` WHERE `calendar_event_id IS NOT NULL AND calendar_occurrence_start_at IS NOT NULL AND deleted_at IS NULL` (1 note per occurrence date).
- `ix_notes_event_shared_unique`: unique on `(calendar_event_id)` WHERE `calendar_event_id IS NOT NULL AND calendar_occurrence_start_at IS NULL AND deleted_at IS NULL` (1 note per single event or 1 shared series note per recurring event).

**Non-unique lookup indexes:**
- `(user_id, university_course_id, deleted_at)`
- `(user_id, calendar_event_id, deleted_at)`

**Deletion safety (designer call):**
- Using `OnDelete(DeleteBehavior.SetNull)` on `Note` FKs means deleting a course, item, or event never silently destroys user-written Markdown notes — any linked note gracefully falls back to General → Unfiled (or if a course is deleted while its item notes exist, both FKs become null).
- Legacy `UniversityActivityItem.Notes` plain-text column is preserved in the schema for backward compatibility; when a user opens a uni item's Markdown note for the first time and `UniversityActivityItem.Notes` is non-empty, its text is copied as the initial Markdown body.

### 4.2 New `Attachment` Entity (`attachments` table)
Unified table for both visible **artifacts** and hidden **inline images**:

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid` PK | |
| `user_id` | `uuid` FK → `users` (Cascade) | Owner |
| `kind` | `varchar(24)` | `"artifact"` or `"inline_image"` |
| `file_name` | `varchar(255)` | Sanitized original display filename |
| `content_type` | `varchar(127)` | Validated MIME type |
| `size_bytes` | `bigint` | File size in bytes |
| `sha256` | `varchar(64)` | Hex digest |
| `storage_key` | `varchar(255)` | Relative path `{userId}/{id}` |
| `note_id` | `uuid?` FK → `notes` (Cascade) | Set for `inline_image` and note-scoped artifacts |
| `university_course_id` | `uuid?` FK → `university_courses` (Cascade) | Set for course-level artifacts |
| `university_activity_item_id` | `uuid?` FK → `university_activity_items` (Cascade) | Set for item-level artifacts |
| `calendar_event_id` | `uuid?` FK → `calendar_events` (Cascade) | Set for event/occurrence artifacts |
| `calendar_occurrence_start_at` | `timestamptz?` | Set for occurrence-specific event artifacts (`null` = single event or shared series) |
| `allow_agent_read` | `bool` | Default `true` |
| `created_at` | `timestamptz` | |
| `deleted_at` | `timestamptz?` | Soft delete |

**Attachment ownership rules:**
- Every `inline_image` must have `note_id != null` (and no course/item/event FK).
- Every `artifact` must have **exactly one** owner target: `note_id`, OR `university_course_id` (course-level), OR `university_activity_item_id` (item-level, with `university_course_id` also populated for fast tree queries), OR `calendar_event_id` (with optional `calendar_occurrence_start_at`).
- When an `Attachment` is explicitly deleted via API, its row is soft-deleted (or removed) and the underlying file on disk is deleted on a best-effort basis.
- Note on SQLite test provider: apply the same `UtcTicks` conversion on `Note.CalendarOccurrenceStartAt` and `Attachment.CalendarOccurrenceStartAt` in `AppDbContext` as `CalendarEvent.RecurrenceOriginalStartAt`.

---

## 5. File Storage & Security

### 5.1 Storage Abstraction (`IAttachmentStorage`)
- Interface with `SaveAsync(storageKey, stream, ct)`, `OpenReadAsync(storageKey, ct)`, and `DeleteAsync(storageKey, ct)`.
- Default implementation: `LocalDiskAttachmentStorage` rooted at `Atrium:Storage:UploadsPath` (default `./uploads` in Development, `/app/uploads` in Docker via named volume `atrium_uploads:/app/uploads` in `docker-compose.yml`).
- Files are stored at `{UploadsPath}/{userId}/{attachmentId}` — user-supplied filenames are **never** used in filesystem paths.

### 5.2 Validation & Limits (designer calls)
- **Max sizes** (configurable via `Atrium:Storage`):
  - `inline_image`: **10 MB**
  - `artifact`: **25 MB**
- **Allowed MIME types:**
  - `inline_image`: `image/png`, `image/jpeg`, `image/webp`, `image/gif` (explicitly excluding `image/svg+xml` to avoid stored XSS).
  - `artifact`: common document, archive, media, and code/text types (PDF, PPT/PPTX, DOC/DOCX, XLS/XLSX, ODP/ODT/ODS, TXT, MD, CSV, JSON, ZIP, PNG/JPEG/WEBP/GIF); block executable/script types (`.exe`, `.sh`, `.bat`, `.html`, `.js`).
- **Response headers on `GET /api/attachments/{id}`:**
  - Always set `X-Content-Type-Options: nosniff`.
  - For `artifact`: `Content-Disposition: attachment; filename="..."` (RFC 5987 safe).
  - For `inline_image`: `Content-Disposition: inline` + `Cache-Control: private, max-age=3600`.
- **Auth & Agent access (designer calls):**
  - Requires authenticated user (JWT) or Agent PAT.
  - Strictly scoped to `attachment.UserId == currentUserId`.
  - **Least-privilege Agent PAT scopes:**
    - `GET /api/notes/tree` requires `notes:read` for Agent PATs, and only populates the `university` section if the token also has `university:read` and the `events` section if the token also has `calendar:read` (returning empty arrays otherwise).
    - `GET /api/attachments` and `GET /api/attachments/{id}` require `notes:read` for note-owned attachments (`note_id`), `university:read` for course/item artifacts (`university_course_id` / `university_activity_item_id`), and `calendar:read` for event artifacts (`calendar_event_id`).
  - **Effective `AllowAgentRead` inheritance:** Agent requests verify `attachment.AllowAgentRead == true` AND if the attachment belongs to a note (or shares an item/event with a linked note whose `AllowAgentRead == false`), the attachment is hidden from agents. No separate per-file privacy toggle UI is needed in v1 (`Attachment.AllowAgentRead` defaults to `true` in the schema for future overrides).

### 5.3 Secure Image Rendering in Markdown (Frontend)
Because browser `<img>` tags do not send `Authorization: Bearer` headers, embedded `/api/attachments/{id}` images are loaded securely without weakening auth:
- `attachmentsClient.ts` maintains a small in-memory cache mapping `attachmentId → objectUrl` (`URL.createObjectURL(blob)` fetched with the existing Bearer JWT via `apiFetch`).
- `ImageWidget` in `markdownLivePreview.ts` and the `img` renderer in `markdownReading.tsx` detect relative `/api/attachments/{guid}` URLs, fetch the blob URL via `attachmentsClient`, and render it seamlessly.
- `sanitizeMarkdownUrl` in `markdownUtils.ts` is updated to allow relative `/api/attachments/<guid>` paths (while continuing to block `javascript:`, `data:`, and arbitrary relative paths).
- Markdown source stays clean and portable: `![alt](/api/attachments/<guid>)`.

---

## 6. API Design

### 6.1 Linked Notes
- **`POST /api/notes/linked`** (idempotent find-or-create):
  - Request:
    - `{ kind: "university_item", universityActivityItemId }` — returns existing note (`200`) or creates one (`201`) titled `{item.Title}` (seeded with legacy `item.Notes` if present).
    - `{ kind: "calendar_event", calendarEventId, occurrenceStartAt? }` — validates the event is **not** a university event; returns existing note (`200`) or creates one (`201`) titled `{event.Title}` (or `{event.Title} — {formatted date}` when `occurrenceStartAt` is set, or `{event.Title} (Shared)` when `occurrenceStartAt` is null on a recurring event).
- **`POST /api/notes`** (existing):
  - Accepts optional `universityCourseId` to create a freeform note inside a course (mutually exclusive with `folderId`).
- **`GET /api/notes/tree`** (extended):
  - Returns existing `{ folders, notes, tags }` (where `notes` are General notes only) **plus**:
    - `university`: array of semester groups → courses → `{ id, name, code, color, semester, notes: NoteSummary[], artifacts: AttachmentSummary[], activities: [{ id, activityType, title, items: [{ id, title, sequenceNumber, note: NoteSummary | null, artifacts: AttachmentSummary[] }] }] }`.
    - `events`: array of non-university event groups (only events that have at least one linked note or artifact) → `{ calendarEventId, title, startAt, isRecurring, sharedNote: NoteSummary | null, sharedArtifacts: AttachmentSummary[], occurrences: [{ occurrenceStartAt, note: NoteSummary | null, artifacts: AttachmentSummary[] }] }`.
- **`GET /api/notes`** (search):
  - Searches across **all** user notes (General, University, and Events) so `Titles` / `Content` / `Tags` search finds study and meeting notes too, returning lightweight link context on each `NoteSummary` (`linkKind: 'general' | 'university_course' | 'university_item' | 'calendar_event'`, `linkLabel`).

### 6.2 Attachments
- **`POST /api/attachments`** (`multipart/form-data`):
  - Fields: `file`, `kind` (`artifact` | `inline_image`), plus owner (`noteId`, `universityCourseId`, `universityActivityItemId`, or `calendarEventId` + optional `calendarOccurrenceStartAt`).
  - Returns `201 Created` with `AttachmentDto` (`id`, `kind`, `fileName`, `contentType`, `sizeBytes`, `url: "/api/attachments/{id}"`, `createdAt`).
- **`GET /api/attachments`** (filtered list):
  - Query by `noteId`, `universityCourseId`, `universityActivityItemId`, or `calendarEventId` (+ optional `occurrenceStartAt`), always filtering `kind == "artifact"` (never returning `inline_image`).
- **`GET /api/attachments/{id}`**:
  - Authenticated file stream.
- **`DELETE /api/attachments/{id}`**:
  - Deletes attachment record and disk file (`204 No Content`).

### 6.3 Lightweight Summaries on Existing Workspaces
- `GET /api/university/workspace`: each course includes `noteCount` and `artifactCount`; each activity item includes `linkedNoteId` (nullable) and `artifactCount`.
- `GET /api/calendar/events`: each non-university event occurrence includes `hasNote` / `artifactCount` (or a lightweight lookup) so the Calendar card can show a subtle note/attachment indicator without N+1 fetches.

---

## 7. Frontend UX & Navigation

### 7.1 Cross-View Navigation (`App.tsx`)
- Add `pendingNoteTarget` state and `openLinkedNote(target)` callback passed to `UniversityPage` and `CalendarCard`.
- Clicking **Open Note** from University or Calendar calls `POST /api/notes/linked` (or selects an existing course note), switches `activeView` to `'notes'`, and opens that note directly.

### 7.2 Notes Sidebar (`NotesSidebar.tsx`)
- **Section toggles** in the sidebar top bar (compact icon/pill buttons, persisted in `localStorage`):
  - **Uni** (default **ON**)
  - **Events** (default **OFF**)
- **Three collapsible sections:**
  1. **General:** Existing manual folders, unfiled notes, drag-and-drop into folders/Unfiled, `+ Note` and `+ Folder`.
  2. **University** (when enabled):
     ```
     University
     └── 2026 Fall
         └── Algorithms                        [+ Note] [📎 Upload]
             ├── 📄 Exam Prep
             ├── 📎 syllabus.pdf
             ├── Lectures
             │   └── Lecture 1                 [📎 Upload]
             │       ├── 📝 Lecture 1
             │       └── 📎 slides-01.pdf
             └── Labs
                 └── Lab 1 ...
     ```
     - Clicking an item node (`Lecture 1`) without a note yet offers/opens its note immediately via `POST /api/notes/linked`.
     - Clicking an artifact triggers an authenticated download.
  3. **Events** (when enabled):
     - Lists only non-university events that have at least one note or artifact, sorted by most recent date.
     - Single events show the note + artifacts directly.
     - Recurring events group their shared series note (if any) + each dated occurrence that has a note or artifact.

### 7.3 Notes Editor, Context Bar & Artifacts Strip (`NotesPage.tsx`, `MarkdownEditor.tsx`)
- **Context breadcrumb** above `NotePropertiesBar` when editing a University or Event note (e.g., `University · 2026 Fall / Algorithms / Lectures / Lecture 1` with a jump-back link to University, or `Event · Weekly Sync · Nov 27`).
- **Folder selector** in `NotePropertiesBar` is only shown for General notes (linked notes stay anchored to their course/item/event).
- **Artifacts strip** below `NotePropertiesBar`: compact row of uploaded artifact chips with filename, file size, download click, delete `×`, and `+ Attach file` button. (Inline images are never shown here.)
  - **Owner mapping (designer call):**
    - When editing a **University item note** (`Lecture 1`), the strip reads/uploads against `universityActivityItemId` (1:1 with `UniversityPage` and the sidebar's `Lecture 1` node).
    - When editing an **Event note** (single, occurrence, or shared series), the strip reads/uploads against `calendarEventId` (+ `calendarOccurrenceStartAt` for occurrence notes), 1:1 with `CalendarCard` and the Events sidebar.
    - When editing a **General note** or a **Course freeform note** (e.g. `Exam Prep`), the strip reads/uploads against `noteId` (note-scoped artifacts, shown in the editor's strip only, keeping the General sidebar clean while course-wide artifacts stay at the Course level).
- **Inline image insertion in `MarkdownEditor.tsx`:**
  - `paste` event listener on CodeMirror: if clipboard contains an image file (`image/png`, `image/jpeg`, `image/webp`, `image/gif`), prevent default, upload via `POST /api/attachments` (`kind: 'inline_image', noteId`), and insert `![pasted-image](/api/attachments/{id})` at cursor.
  - `drop` event listener on CodeMirror: same for dragged image files.
  - Toolbar **Image** button: opens a file picker for local images (uploading as `inline_image`), with a secondary option or fallback to insert an external image URL.

### 7.4 University Page & Calendar Card Entry Points
- **`UniversityPage.tsx`:**
  - Course detail panel gains a **Notes & Artifacts** section: lists course-level notes (click to open in Notes, `+ New Note` button) and course-level artifacts (`+ Upload`, download, delete).
  - Each activity item card (Lecture 1, Practice 2, etc.) gains an **Open Note** button (with visual indicator if a note exists) and an **Artifacts** control to upload/download/delete item artifacts. Replaces the old plain-text notes textarea in the item edit dialog (preserving any existing text on first Markdown note open).
- **`CalendarCard.tsx`:**
  - In the event detail/edit modal for **non-university** events:
    - Single events: **Open Note** button + **Artifacts** upload/download/delete list.
    - Recurring events: **Open Note for this Date** button, **Open Shared Series Note** button, + **Artifacts** upload/download/delete list (for this occurrence).
  - For **university** calendar events: shows a link **"Open University Item Note"** that opens the corresponding `UniversityActivityItem` note (never creating an Events duplicate).

---

## 8. What's Left to the Builder

- Exact icon choices, spacing, and responsive mobile layout details (consistent with existing Atrium design tokens in `styles.css`).
- Exact filename sanitization helper and MIME-to-extension validation table.
- Whether to implement as a single PR or two sequential PRs (PR A: disk storage + attachments API + inline image paste/embed; PR B: linked notes schema + virtual sidebar trees + University & Calendar integration) — both fit this spec cleanly.
- Test coverage structure (xUnit integration tests in `Atrium.Api.Tests` covering link exclusivity, recurring occurrence vs. shared notes, uni-event anti-duplication, attachment upload/download/auth/size limits, and tree computation).

