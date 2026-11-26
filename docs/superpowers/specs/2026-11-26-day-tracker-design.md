# Atrium Day Tracker Design

**Date:** 2026-11-26
**Status:** Draft (pending approval)
**Branch target:** `feature/day-tracker` (branched from `main`)

---

## 1. Intent & Why

The current Atrium dashboard is pinned to "Today" and mixes real calendar data with in-memory mockups (`ChecklistCard`, `NotesCard`, `AgentProposalsCard`, `VaultCard`). The goal is to turn this landing view into a real, persisted **Day Tracker**: every date (past, today, or future) has its own day page with its own **daily checklist**, **daily shortform note**, and **calendar/events**.

Agent proposals, recent notes, and vault will be deferred to a separate meta/overview page later so this page stays focused purely on tracking and navigating individual days.

---

## 2. Goals, Non-Goals, & Anti-Goals

### Goals
- Every calendar date (`YYYY-MM-DD`, past, today, or future) is visitable as a full Day Tracker view.
- Seamless day navigation:
  - Clicking any date in the right-hand `CalendarCard` opens that day's tracker.
  - Previous-day (`←`), Next-day (`→`), and jump-to-`Today` buttons in the Day Tracker header.
  - Keyboard shortcuts when not focused in a text input/textarea/select/contenteditable: `[` or `Alt+ArrowLeft` (previous day), `]` or `Alt+ArrowRight` (next day), and `t` (jump to today).
  - URL hash synchronization (`#day/YYYY-MM-DD` for non-today dates; empty hash for today) so refreshes and browser navigation preserve the viewed day.
- Real PostgreSQL-backed **Daily Checklists** per user and date:
  - Create, toggle completion, edit title/due time, delete, and one-click **"Move to tomorrow"** (`date + 1 day`) on unchecked items.
  - Unfinished items stay strictly on their assigned date (no automatic rollover).
- Real PostgreSQL-backed **Daily Shortform Note** per user and date:
  - Single freeform scratchpad textarea per day that auto-saves with a ~600 ms debounce, flushes on blur or before switching days, and displays a subtle save status (`Saving…`, `Saved`, or inline error with retry).
- Quick Capture on the Day Tracker targets the **currently viewed day** (`Task` adds a checklist item for that day; `Event` schedules a calendar event on that day).
- Remove `AgentProposalsCard`, `NotesCard`, and `VaultCard` from the Day Tracker page, and trim the mobile bottom tab bar accordingly (`Day`, `Calendar`, `University`).
- Keep `CalendarCard` (including its selected-day events list and full event CRUD), `UniversityTileCard`, and `LocalStatusCard` on the right utility rail.

### Non-Goals
- Automatic rollover of incomplete tasks from past days.
- Rich-text / Markdown editor, multi-note lists, or tags for daily notes (single plain-text scratchpad only).
- Building the future meta page for Agent Proposals, Recent Notes, or Vault (existing unused components may be removed from `App.tsx` rendering; deleting or keeping dead mockup files is left to the builder, provided TypeScript builds cleanly with no unused imports).
- Drag-and-drop reordering of checklist items (simple deterministic ordering by `SortOrder`, then `CreatedAt` is sufficient).

### Anti-Goals
- Losing typed daily note text when rapidly clicking Next/Prev day or clicking another calendar date (must flush or preserve pending edits safely without writing day A's text onto day B).
- Allowing concurrent checklist toggles and note auto-saves to overwrite each other.
- Breaking existing calendar, university, or account/auth flows or weakening tenant isolation.

---

## 3. Approaches Considered

### Approach 1: Dedicated `DailyCheckItem` + `DailyNote` tables with unified day read + item CRUD *(Selected)*
- Two relational tables keyed by `(UserId, DateOnly)`: `daily_check_items` and `daily_notes`.
- `GET /api/days/{date}` returns both checklist items and the daily note in one round-trip; mutations are atomic per checklist item (`POST`/`PUT`/`DELETE`) and per daily note (`PUT`).
- **Trade-offs:** Eliminates last-write-wins races between checklist toggles and note auto-save, makes "Move to tomorrow" a single row update, and enables future cross-day queries (e.g., open tasks across days on a meta page). Slightly more endpoints than a single-blob design.

### Approach 2: Single `DailyEntry` table with JSON checklist blob
- One table `(UserId, Date, NoteContent, ChecklistJson)` with `GET`/`PUT /api/days/{date}`.
- **Trade-offs:** Fewer endpoints, but toggling a checkbox while a debounced note save is in flight risks overwriting data, moving a single item across dates requires rewriting two days' blobs, and querying tasks across dates later is awkward.

---

## 4. Architecture & Data Model (Approach 1)

### Database Schema (EF Core + PostgreSQL)

Add two entities and a new EF Core migration (`AddDailyTracker`), updating `AppDbContextModelSnapshot.cs`, `MigrationModelTests.cs`, and `docs/engineering/database-migrations.md`.

1. **`DailyCheckItem`** (`daily_check_items`)
   - `Id` (`Guid`, PK)
   - `UserId` (`Guid`, FK → `users.id`, `ON DELETE CASCADE`)
   - `Date` (`DateOnly`, required — stored as Postgres `date`)
   - `Title` (`string`, required, max length 280)
   - `IsCompleted` (`bool`, default `false`)
   - `CompletedAt` (`DateTimeOffset?`, nullable — set to UTC now when completed, cleared when unchecked)
   - `DueTime` (`string?`, nullable, max length 5 — optional `"HH:mm"` 24h time string, validated when provided)
   - `SortOrder` (`int`, default `0`)
   - `CreatedAt` (`DateTimeOffset`)
   - `UpdatedAt` (`DateTimeOffset`)
   - **Index:** `ix_daily_check_items_user_date_sort` on `(user_id, date, sort_order, created_at)`

2. **`DailyNote`** (`daily_notes`)
   - `Id` (`Guid`, PK)
   - `UserId` (`Guid`, FK → `users.id`, `ON DELETE CASCADE`)
   - `Date` (`DateOnly`, required — stored as Postgres `date`)
   - `Content` (`string`, required, max length 10,000)
   - `CreatedAt` (`DateTimeOffset`)
   - `UpdatedAt` (`DateTimeOffset`)
   - **Unique Index:** `ix_daily_notes_user_date` on `(user_id, date)`

### Backend API (`/api/days`)

All routes require authentication and strictly scope reads/writes to the authenticated `userId`.
- **Reads:** `AuthConstants.HumanOrAgentPolicy`; if authenticated via PAT (`pat`), require `tasks:read` scope (consistent with existing `AuthConstants.AllowedScopes`). *(Builder call: reuse `tasks:read` rather than adding a new PAT scope.)*
- **Writes:** `AuthConstants.HumanJwtOnlyPolicy`.
- **Date parsing:** `{date}` path parameter is strict `yyyy-MM-dd` (`DateOnly.TryParseExact(..., "yyyy-MM-dd", InvariantCulture, ...)`), returning `400 Bad Request` on invalid dates.

Endpoints:
1. **`GET /api/days/{date}`**
   - Returns `DailyTrackerResponse`:
     - `date` (`"yyyy-MM-dd"`)
     - `checklistItems` (`IReadOnlyList<DailyCheckItemDto>`, ordered by `SortOrder`, `CreatedAt`)
     - `note` (`DailyNoteDto?` — `null` if no note exists for that date)
2. **`POST /api/days/{date}/checklist`**
   - Body: `CreateDailyCheckItemRequest(string Title, string? DueTime = null)`
   - Validates `Title` trimmed 1–280 chars; optional `DueTime` matches `HH:mm` (`00:00`–`23:59`).
   - Assigns `SortOrder = (max existing SortOrder for user+date ?? -1) + 1`.
   - Returns `201 Created` with `DailyCheckItemDto`.
3. **`PUT /api/days/checklist/{id:guid}`**
   - Body: `UpdateDailyCheckItemRequest(string? Title = null, bool? IsCompleted = null, string? Date = null, string? DueTime = null, bool ClearDueTime = false)`
   - Supports partial-friendly updates (updating title, toggling `IsCompleted`, editing/clearing `DueTime`, and moving `Date` to another `yyyy-MM-dd` date such as tomorrow).
   - When `Date` changes, recalculates `SortOrder` to append to the target day's list.
   - Returns `200 OK` with `DailyCheckItemDto` (or `404` if not owned by user).
4. **`DELETE /api/days/checklist/{id:guid}`**
   - Hard-deletes the checklist item if owned by user. Returns `204 No Content` or `404`.
5. **`PUT /api/days/{date}/note`**
   - Body: `UpsertDailyNoteRequest(string Content)`
   - Validates `Content.Length <= 10_000`.
   - If `string.IsNullOrWhiteSpace(Content)`: deletes any existing `DailyNote` row for `(userId, date)` and returns `200 OK` with `null` (or `{ note: null }`, keeping response shape consistent: `UpsertDailyNoteResponse(string Date, DailyNoteDto? Note)`).
   - Otherwise inserts or updates the `(userId, date)` row (handling concurrent upsert `DbUpdateException` on `ix_daily_notes_user_date` gracefully by detaching and updating the winning row).
   - Returns `200 OK` with `UpsertDailyNoteResponse`.

### Frontend UX & State

1. **Day Header & Navigation (`App.tsx`)**
   - Displays the formatted selected date (e.g., `Thursday, November 26, 2026`) with a relative badge (`Today`, `Yesterday`, `Tomorrow`, or `Past day` / `Upcoming`) and the greeting pill when viewing Today.
   - Header controls: `← Prev` button, `Next →` button, and a `Today` button (shown whenever `selectedDateKey !== todayKey`).
   - Day summary line (when `preferences.morningBrief` is enabled): `{openCount} open · {doneCount} done · {dayEventCount} events` for the **selected day** (not hardcoded to today).
   - Keyboard shortcuts (active only on the Day Tracker view and when `event.target` is not `INPUT`, `TEXTAREA`, `SELECT`, or `isContentEditable`, and no modal dialog / command palette is open):
     - `[` or `Alt+ArrowLeft`: go to previous day.
     - `]` or `Alt+ArrowRight`: go to next day.
     - `t` (no modifier keys): jump to today.
   - Clicking a date in `CalendarCard` updates `selectedDateKey`, which loads that day's checklist and note and syncs the URL hash (`#day/YYYY-MM-DD` if not today, empty hash if today).

2. **Quick Capture (`QuickCapture.tsx`)**
   - Two modes: `Task` and `Event` (removing `Note` mode since the daily note is directly editable on the page).
   - Shows `for {selectedDateLabel}` so it is always clear which day the task or event is being added to.
   - Submitting `Task` calls `POST /api/days/{selectedDateKey}/checklist`; submitting `Event` calls `POST /api/events` on `selectedDateKey` as today.

3. **Daily Checklist Card (`ChecklistCard.tsx`)**
   - Header shows `Checklist` (or `Today's Checklist` when viewing today, `{ShortDate} Checklist` otherwise) and `{completedCount} / {totalCount}` pill.
   - Each item row supports:
     - Checkbox to toggle `IsCompleted` (with optimistic or in-flight disabled state and error announcement).
     - Optional `DueTime` badge.
     - Inline edit of title (click edit or double-click, Enter to save, Escape to cancel).
     - For unchecked items: one-click **"Move to tomorrow"** button (moves the item to `selectedDate + 1 day` via `PUT /api/days/checklist/{id}` and announces `Moved "{title}" to {nextDate}`).
     - Delete button (`×`).

4. **Daily Shortform Note Card (`DailyNoteCard.tsx`)**
   - Replaces `NotesCard.tsx` on the Day Tracker.
   - Clean multi-line `<textarea>` with placeholder `"Jot down notes, thoughts, or context for {selectedDateLabel}…"` and subtle character count near limit.
   - Auto-saves 600 ms after typing stops, and immediately flushes on blur.
   - **Day-switch safety:** If the user switches days while a note edit is dirty/unflushed, flush the save for the *previous* date (`previousDateKey`) before/while loading the new date, and track request sequence numbers so an older `GET /api/days/{oldDate}` response can never overwrite the newly selected day's state.

5. **Right Rail & Mobile Navigation**
   - Desktop right rail: `CalendarCard` (keeps selected-day events + full CRUD), `UniversityTileCard`, `LocalStatusCard`.
   - Removed from Day Tracker: `AgentProposalsCard`, `NotesCard`, `VaultCard`.
   - `MobileTabBar`: 3 tabs — `Day` (`stream`), `Calendar` (`calendar`), `University` (`university`).
   - Account JSON export (`handleExportData`) updated to include the currently loaded day's checklist/note cleanly without referencing removed mock `proposals` or `notes` state.

---

## 5. What's Left to the Builder
- Exact CSS class names and visual styling tokens (following Atrium's existing Atelier theme in `web/src/styles.css`, both light and dark modes).
- Exact component decomposition for the day navigation header and `DailyNoteCard.tsx`.
- Whether to delete unused mock components (`AgentProposalsCard.tsx`, `NotesCard.tsx`, `VaultCard.tsx`) or leave them untouched if referenced nowhere else (keeping unused code out of the bundle and ensuring zero TypeScript warnings).
- Test structure and helper placement in `api/Atrium.Api.Tests/` (must include integration tests covering CRUD, moving items across days, note upsert/clear, invalid dates, user isolation, PAT scope enforcement, and `MigrationModelTests`).

