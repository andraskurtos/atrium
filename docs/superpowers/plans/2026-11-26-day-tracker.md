# Atrium Day Tracker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the Atrium dashboard into a persisted, per-day Day Tracker where every calendar date has its own daily checklist, auto-saving daily shortform note, calendar/events, and day-cycling navigation (buttons, calendar click, keyboard shortcuts, and URL hash).

**Architecture:** Add two relational EF Core entities (`DailyCheckItem` and `DailyNote`) keyed by `(UserId, DateOnly)` with a unified `GET /api/days/{date}` read endpoint and atomic checklist/note write endpoints. On the frontend, replace mock dashboard tasks/notes/proposals/vault with live per-day state synced to `selectedDateKey`, `#day/YYYY-MM-DD` hash, and debounced auto-save with day-switch flushing.

**Tech Stack:** .NET 10 Minimal APIs, EF Core 10 + Npgsql (PostgreSQL), xUnit (`TestAppFactory`), React 19, TypeScript (strict), Vite PWA.

**Spec:** `docs/superpowers/specs/2026-11-26-day-tracker-design.md`

## Global Constraints
- Branch target: `feature/day-tracker` (branched from `main`).
- All backend builds must pass `dotnet build --no-restore -c Release -warnaserror api/Atrium.sln` with 0 warnings.
- No undocumented warning suppressions (`npm run check:suppressions` must pass).
- Frontend must pass `npm --prefix web run typecheck` (`tsc -b --noEmit`) and `npm --prefix web run build`.
- All API routes strictly scope reads and writes by authenticated `userId`; reads use `AuthConstants.HumanOrAgentPolicy` with PAT `tasks:read` scope; writes use `AuthConstants.HumanJwtOnlyPolicy`.
- Dates in API paths and payloads use strict `yyyy-MM-dd` (`DateOnly`).
- `DailyCheckItem.Title` is 1–280 chars (trimmed); `DailyCheckItem.DueTime` is optional `"HH:mm"` (24h, `00:00`–`23:59`); `DailyNote.Content` max length is 10,000 chars.

## Review Focus
- **Invalid or non-ISO date paths** (e.g., `2026-02-30`, `not-a-date`): API returns `400 Bad Request` rather than 500 or loose parsing — pinned in Task 2 (`DayEndpointsTests`).
- **Moving a checklist item to another date (e.g., tomorrow)** preserves `Title`, `DueTime`, `IsCompleted`, appends cleanly via recalculated `SortOrder`, and removes it from the source day — pinned in Task 2 (`DayEndpointsTests`).
- **Clearing a daily note to whitespace/empty** deletes the `DailyNote` row and returns `Note: null` cleanly rather than storing empty rows or failing — pinned in Task 2 (`DayEndpointsTests`).
- **Rapid day switching while a daily note edit is pending** flushes the dirty note to the *previous* date and ignores stale `GET /api/days/{oldDate}` responses so day A's text never leaks onto day B — pinned in Task 3 & Task 4 (sequence guard + flush-on-date-change in `DailyNoteCard` / `App.tsx`).
- **Keyboard shortcuts (`[`, `]`, `Alt+ArrowLeft`, `Alt+ArrowRight`, `t`)** never fire while focused inside `<input>`, `<textarea>`, `<select>`, `isContentEditable`, or when a `<dialog>` / Command Palette is open — pinned in Task 4 (`App.tsx`).

---

## File Structure

- **Backend Data & Migration (Task 1)**
  - Create `api/Atrium.Api/Data/Entities/DailyCheckItem.cs`
  - Create `api/Atrium.Api/Data/Entities/DailyNote.cs`
  - Modify `api/Atrium.Api/Data/AppDbContext.cs`
  - Create `api/Atrium.Api/Data/Migrations/20261126230000_AddDailyTracker.cs`
  - Create `api/Atrium.Api/Data/Migrations/20261126230000_AddDailyTracker.Designer.cs`
  - Modify `api/Atrium.Api/Data/Migrations/AppDbContextModelSnapshot.cs`
  - Modify `docs/engineering/database-migrations.md`
  - Modify `api/Atrium.Api.Tests/MigrationModelTests.cs`
- **Backend API & Tests (Task 2)**
  - Create `api/Atrium.Api/Days/DayContracts.cs`
  - Create `api/Atrium.Api/Endpoints/DayEndpoints.cs`
  - Modify `api/Atrium.Api/Program.cs`
  - Create `api/Atrium.Api.Tests/DayEndpointsTests.cs`
- **Frontend Types, Client & Components (Task 3)**
  - Modify `web/src/types/atrium.ts`
  - Create `web/src/lib/dayClient.ts`
  - Create `web/src/components/DailyNoteCard.tsx`
  - Modify `web/src/components/ChecklistCard.tsx`
  - Modify `web/src/components/QuickCapture.tsx`
  - Modify `web/src/components/MobileTabBar.tsx`
- **Frontend Day Tracker Page & Styles (Task 4)**
  - Modify `web/src/App.tsx`
  - Modify `web/src/styles.css`

---

### Task 1: EF Core Entities, DbContext Configuration & Migration

**Files:**
- Create: `api/Atrium.Api/Data/Entities/DailyCheckItem.cs`
- Create: `api/Atrium.Api/Data/Entities/DailyNote.cs`
- Modify: `api/Atrium.Api/Data/AppDbContext.cs`
- Create: `api/Atrium.Api/Data/Migrations/20261126230000_AddDailyTracker.cs`
- Create: `api/Atrium.Api/Data/Migrations/20261126230000_AddDailyTracker.Designer.cs`
- Modify: `api/Atrium.Api/Data/Migrations/AppDbContextModelSnapshot.cs`
- Modify: `docs/engineering/database-migrations.md`
- Test: `api/Atrium.Api.Tests/MigrationModelTests.cs`

**Interfaces:**
- Produces:
  - `DailyCheckItem` entity (`Id`, `UserId`, `Date` (`DateOnly`), `Title`, `IsCompleted`, `CompletedAt`, `DueTime`, `SortOrder`, `CreatedAt`, `UpdatedAt`, `User`)
  - `DailyNote` entity (`Id`, `UserId`, `Date` (`DateOnly`), `Content`, `CreatedAt`, `UpdatedAt`, `User`)
  - `AppDbContext.DailyCheckItems` (`DbSet<DailyCheckItem>`) and `AppDbContext.DailyNotes` (`DbSet<DailyNote>`)

- [ ] **Step 1: Add migration assertions to `api/Atrium.Api.Tests/MigrationModelTests.cs`**

Add assertions in `IdempotentMigrationScript_ContainsCalendarsAndEventsTablesAndIndexes` verifying:
```csharp
Assert.Contains("20261126230000_AddDailyTracker", sql, StringComparison.Ordinal);
Assert.Contains("CREATE TABLE daily_check_items", sql, StringComparison.Ordinal);
Assert.Contains("CREATE TABLE daily_notes", sql, StringComparison.Ordinal);
Assert.Contains("ix_daily_check_items_user_date_sort", sql, StringComparison.Ordinal);
Assert.Contains("ix_daily_notes_user_date", sql, StringComparison.Ordinal);
```

- [ ] **Step 2: Run migration test to verify it fails**

Run: `dotnet test api/Atrium.sln --filter MigrationModelTests`
Expected: FAIL because `20261126230000_AddDailyTracker` does not exist yet.

- [ ] **Step 3: Create `DailyCheckItem.cs` and `DailyNote.cs` and configure `AppDbContext.cs`**

Create `api/Atrium.Api/Data/Entities/DailyCheckItem.cs` and `api/Atrium.Api/Data/Entities/DailyNote.cs` with the exact columns from Spec §4. In `AppDbContext.cs`, add `DbSet<DailyCheckItem> DailyCheckItems` and `DbSet<DailyNote> DailyNotes`, map to `daily_check_items` and `daily_notes` with snake_case column names, cascade delete on `UserId`, `ix_daily_check_items_user_date_sort` on `(user_id, date, sort_order, created_at)`, and unique `ix_daily_notes_user_date` on `(user_id, date)`.

- [ ] **Step 4: Add migration `20261126230000_AddDailyTracker`, update `AppDbContextModelSnapshot.cs`, and update `docs/engineering/database-migrations.md`**

Create `20261126230000_AddDailyTracker.cs` + `.Designer.cs` (reversible `Up`/`Down`), update `AppDbContextModelSnapshot.cs` so `db.Database.HasPendingModelChanges()` is `false`, and add `20261126230000_AddDailyTracker` to `docs/engineering/database-migrations.md`.

- [ ] **Step 5: Run migration tests and Release build to verify**

Run: `dotnet build --no-restore -c Release -warnaserror api/Atrium.sln && dotnet test -c Release --no-build api/Atrium.sln --filter MigrationModelTests`
Expected: 0 warnings, 0 errors, all `MigrationModelTests` PASS.

- [ ] **Step 6: Commit**

```bash
git add api/Atrium.Api/Data/ docs/engineering/database-migrations.md api/Atrium.Api.Tests/MigrationModelTests.cs
git commit -m "feat(days): add EF Core schema and migration for daily checklists and notes"
```

---

### Task 2: Daily Tracker API Endpoints & Integration Tests (`/api/days`)

**Files:**
- Create: `api/Atrium.Api/Days/DayContracts.cs`
- Create: `api/Atrium.Api/Endpoints/DayEndpoints.cs`
- Modify: `api/Atrium.Api/Program.cs`
- Test: `api/Atrium.Api.Tests/DayEndpointsTests.cs`

**Interfaces:**
- Consumes: `AppDbContext.DailyCheckItems`, `AppDbContext.DailyNotes`, `AuthConstants`, `ClaimsPrincipalExtensions.GetUserId()`
- Produces:
  - Contracts in `Atrium.Api.Days`:
    - `DailyCheckItemDto(Guid Id, string Date, string Title, bool IsCompleted, DateTimeOffset? CompletedAt, string? DueTime, int SortOrder, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)`
    - `DailyNoteDto(Guid Id, string Date, string Content, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)`
    - `DailyTrackerResponse(string Date, IReadOnlyList<DailyCheckItemDto> ChecklistItems, DailyNoteDto? Note)`
    - `CreateDailyCheckItemRequest(string Title, string? DueTime = null)`
    - `UpdateDailyCheckItemRequest(string? Title = null, bool? IsCompleted = null, string? Date = null, string? DueTime = null, bool ClearDueTime = false)`
    - `UpsertDailyNoteRequest(string Content)`
    - `UpsertDailyNoteResponse(string Date, DailyNoteDto? Note)`
  - Endpoints mapped by `DayEndpoints.MapDayEndpoints(this WebApplication app)` under `/api/days`:
    - `GET /api/days/{date}` (`HumanOrAgentPolicy` + PAT `tasks:read`)
    - `POST /api/days/{date}/checklist` (`HumanJwtOnlyPolicy`)
    - `PUT /api/days/checklist/{id:guid}` (`HumanJwtOnlyPolicy`)
    - `DELETE /api/days/checklist/{id:guid}` (`HumanJwtOnlyPolicy`)
    - `PUT /api/days/{date}/note` (`HumanJwtOnlyPolicy`)

- [ ] **Step 1: Write failing integration tests in `api/Atrium.Api.Tests/DayEndpointsTests.cs`**

Include tests covering:
1. `GetDay_EmptyDate_ReturnsEmptyChecklistAndNullNote`
2. `Checklist_CreateToggleEditMoveToTomorrowAndDelete_WorksEndToEnd` (verifies `SortOrder` assignment, `CompletedAt` set/cleared, `DueTime` set/cleared, moving to `date + 1` removes from source day and appends on target day, and delete returns `204`)
3. `DailyNote_UpsertUpdateAndClearWhitespace_WorksEndToEnd` (verifies create, update, and whitespace-only upsert deletes row and returns `Note: null`)
4. `Validation_RejectsInvalidDates_InvalidDueTime_EmptyTitle_AndOversizedNote` (tests `2026-02-30`, `not-a-date`, `DueTime = "25:00"`, empty title, title > 280 chars, note > 10,000 chars)
5. `UserIsolation_AndPatScopes_AreEnforced` (invited member gets 404 on owner's checklist item mutations and sees empty day on owner's date; PAT with `tasks:read` can `GET /api/days/{date}` and gets `401` on writes; PAT with only `vault:read` gets `403` on `GET /api/days/{date}`)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test api/Atrium.sln --filter DayEndpointsTests`
Expected: FAIL (types/endpoints do not exist yet).

- [ ] **Step 3: Implement `DayContracts.cs`, `DayEndpoints.cs`, and register in `Program.cs`**

Implement all 5 routes in `DayEndpoints.cs` per Spec §4:
- Strict `DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)`.
- `DueTime` validation: exact `"HH:mm"` via `TimeOnly.TryParseExact(dueTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)`.
- `PUT /api/days/{date}/note` catches `DbUpdateException` on concurrent insert to `ix_daily_notes_user_date`, detaches the conflicting entity, re-queries the winning row, updates its `Content`/`UpdatedAt`, and saves.
- Register `app.MapDayEndpoints();` in `api/Atrium.Api/Program.cs`.

- [ ] **Step 4: Run full backend build and test suite**

Run: `dotnet build --no-restore -c Release -warnaserror api/Atrium.sln && dotnet test -c Release --no-build api/Atrium.sln`
Expected: 0 warnings, 0 errors, all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add api/Atrium.Api/Days/ api/Atrium.Api/Endpoints/DayEndpoints.cs api/Atrium.Api/Program.cs api/Atrium.Api.Tests/DayEndpointsTests.cs
git commit -m "feat(days): add daily checklist and note API endpoints with integration tests"
```

---

### Task 3: Frontend Types, API Client & Day Tracker Components

**Files:**
- Modify: `web/src/types/atrium.ts`
- Create: `web/src/lib/dayClient.ts`
- Create: `web/src/components/DailyNoteCard.tsx`
- Modify: `web/src/components/ChecklistCard.tsx`
- Modify: `web/src/components/QuickCapture.tsx`
- Modify: `web/src/components/MobileTabBar.tsx`

**Interfaces:**
- Consumes: `apiFetch` from `web/src/lib/authClient.ts`
- Produces:
  - Types in `web/src/types/atrium.ts`:
    - `CaptureKind = 'task' | 'event'`
    - `MobileTab = 'stream' | 'calendar' | 'university'`
    - `DailyCheckItem`, `DailyNote`, `DailyTrackerData`
  - Functions in `web/src/lib/dayClient.ts`:
    - `getDayTracker(dateKey: string): Promise<DailyTrackerData>`
    - `createDailyCheckItem(dateKey: string, input: { title: string; dueTime?: string | null }): Promise<DailyCheckItem>`
    - `updateDailyCheckItem(id: string, input: { title?: string; isCompleted?: boolean; date?: string; dueTime?: string | null; clearDueTime?: boolean }): Promise<DailyCheckItem>`
    - `deleteDailyCheckItem(id: string): Promise<void>`
    - `upsertDailyNote(dateKey: string, content: string): Promise<{ date: string; note: DailyNote | null }>`
    - `shiftDateKey(dateKey: string, deltaDays: number): string`
  - `DailyNoteCard` props: `{ dateKey: string; dateLabel: string; initialContent: string; loading: boolean; onSaveNote: (dateKey: string, content: string) => Promise<void> }`
  - `ChecklistCard` props: `{ dateLabel: string; isToday: boolean; items: DailyCheckItem[]; loading: boolean; onToggleItem: (item: DailyCheckItem) => Promise<void>; onEditItemTitle: (item: DailyCheckItem, title: string) => Promise<void>; onMoveItemToNextDay: (item: DailyCheckItem) => Promise<void>; onDeleteItem: (item: DailyCheckItem) => Promise<void> }`

- [ ] **Step 1: Update `web/src/types/atrium.ts` and create `web/src/lib/dayClient.ts`**

Add `DailyCheckItem`, `DailyNote`, `DailyTrackerData` interfaces; narrow `CaptureKind` to `'task' | 'event'` and `MobileTab` to `'stream' | 'calendar' | 'university'`. Implement `dayClient.ts` using `apiFetch` (matching `calendarClient.ts` error handling) plus `shiftDateKey(dateKey, deltaDays)` using local `Date(y, m - 1, d + deltaDays)` formatted as `YYYY-MM-DD`.

- [ ] **Step 2: Create `web/src/components/DailyNoteCard.tsx`**

Implement the auto-saving scratchpad card:
- Tracks `draft`, `saveState` (`'idle' | 'saving' | 'saved' | 'error'`), `errorMessage`, and refs for `dirtyRef`, `draftRef`, `dateKeyRef`, `timerRef`.
- On change: updates `draft`, marks dirty, resets a 600 ms debounce timer that calls `onSaveNote(dateKey, content)`.
- On blur: immediately flushes pending dirty save.
- **Day-switch safety:** when `dateKey` changes while `dirtyRef.current` is true for `previousDateKey`, immediately fires `onSaveNote(previousDateKey, previousDraft)` before resetting `draft` to the new day's `initialContent`, so day A's text never leaks into day B.
- Shows subtle status badge (`Saving…`, `Saved`, or `Retry` button on error) and character count when `draft.length > 8500` (max 10,000).

- [ ] **Step 3: Update `ChecklistCard.tsx`, `QuickCapture.tsx`, and `MobileTabBar.tsx`**

- `ChecklistCard.tsx`: Render persisted `DailyCheckItem` list with `{completedCount} / {items.length}`, inline title editing (Enter to save, Escape to cancel), per-item busy state preventing double-clicks, one-click **"Move to tomorrow"** (`→`) button on unchecked items, `DueTime` badge, and delete (`×`) button.
- `QuickCapture.tsx`: Two modes (`Task` and `Event`), always showing `for {selectedDateLabel}`, with placeholder `"Add a checklist item for {selectedDateLabel}…"` / `"Schedule an event for {selectedDateLabel}…"`.
- `MobileTabBar.tsx`: Remove `Vault` and `Agents` tabs (and unused `pendingProposalCount` prop), keeping 3 tabs: `Day` (`stream`), `Calendar` (`calendar`), and `University` (`university`).

- [ ] **Step 4: Run TypeScript check on new/updated modules**

Run: `npm --prefix web run typecheck`
Expected: Only expected caller mismatches in `App.tsx` (which Task 4 updates) — zero errors in `dayClient.ts`, `DailyNoteCard.tsx`, `ChecklistCard.tsx`, `QuickCapture.tsx`, or `MobileTabBar.tsx`.

- [ ] **Step 5: Commit**

```bash
git add web/src/types/atrium.ts web/src/lib/dayClient.ts web/src/components/DailyNoteCard.tsx web/src/components/ChecklistCard.tsx web/src/components/QuickCapture.tsx web/src/components/MobileTabBar.tsx
git commit -m "feat(web): add day tracker client, auto-saving daily note, and updated checklist/capture/nav"
```

---

### Task 4: Wire Day Tracker Page, Navigation, Shortcuts, URL Hash & Styles in `App.tsx`

**Files:**
- Modify: `web/src/App.tsx`
- Modify: `web/src/styles.css`

**Interfaces:**
- Consumes: `dayClient.ts`, `DailyNoteCard`, updated `ChecklistCard`, `QuickCapture`, `MobileTabBar`
- Produces: Complete live Day Tracker dashboard view in `App.tsx`

- [ ] **Step 1: Wire day state, URL hash sync, and day-switching handlers in `web/src/App.tsx`**

- Parse initial `selectedDateKey` from `#day/YYYY-MM-DD` (validated via regex + real date check, defaulting to `todayKey`), and sync `window.location.hash` whenever `selectedDateKey` changes on the dashboard (`#day/YYYY-MM-DD` when `selectedDateKey !== todayKey`, empty hash when `todayKey`). Also listen to `hashchange` so browser Back/Forward navigates between days.
- Add `dayItems`, `dayNote`, `dayLoading`, and `dayRequestSeqRef` to load `getDayTracker(selectedDateKey)` on auth boot, login, and whenever `selectedDateKey` changes, ignoring stale out-of-order responses.
- Add `shiftSelectedDay(deltaDays)` and keyboard shortcut listener (`[` or `Alt+ArrowLeft` → `-1`, `]` or `Alt+ArrowRight` → `+1`, `t` → `todayKey`) guarded against editable targets (`INPUT`, `TEXTAREA`, `SELECT`, `isContentEditable`), open `<dialog>` elements, or open Command Palette.
- Wire `handleCapture` (`task` → `createDailyCheckItem(selectedDateKey, ...)`, `event` → `createEvent(...)`), `handleToggleDailyItem`, `handleEditDailyItemTitle`, `handleMoveDailyItemToNextDay` (calls `updateDailyCheckItem(item.id, { date: shiftDateKey(selectedDateKey, 1) })`, removes from current day list, and announces), `handleDeleteDailyItem`, and `handleSaveDailyNote` (calls `upsertDailyNote(dateKey, content)` and updates `dayNote` only if `dateKey === selectedDateKeyRef.current`).
- Remove `AgentProposalsCard`, `NotesCard`, `VaultCard`, and mock `tasks`/`notes`/`proposals`/`vaultItems` state from `App.tsx`.
- Update the Day Tracker header to show:
  - Selected date (`formatLongDate(selectedDate)`) + relative badge (`Today`, `Yesterday`, `Tomorrow`, `Past day`, `Upcoming`) + greeting pill on Today.
  - `← Prev`, `Next →`, and `Today` (when not viewing today) navigation buttons with shortcut tooltips.
  - Selected-day summary line (`{openCount} open · {doneCount} done · {selectedDayEventCount} events`).
- Update `handleExportData` and `CommandPalette` commands (`Previous Day`, `Next Day`, `Jump to Today`).

- [ ] **Step 2: Add Atelier styles for Day Tracker navigation, Daily Note card, and Checklist inline edit/move actions in `web/src/styles.css`**

Style `.day-nav-bar`, `.day-header-row`, `.day-relative-badge`, `.daily-note-card`, `.daily-note-textarea`, `.daily-note-status`, `.checklist-edit-input`, and `.checklist-move-btn` cleanly in both light and dark modes, plus responsive mobile layout.

- [ ] **Step 3: Run full CI verification suite**

Run:
```bash
npm run check:suppressions && dotnet build --no-restore -c Release -warnaserror api/Atrium.sln && dotnet test -c Release --no-build api/Atrium.sln && npm --prefix web run typecheck && npm --prefix web run build
```
Expected:
- Suppression audit passes with 0 undocumented suppressions.
- Backend builds with 0 warnings, 0 errors; all tests pass.
- Frontend `tsc -b --noEmit` and `vite build` pass cleanly.

- [ ] **Step 4: Commit**

```bash
git add web/src/App.tsx web/src/styles.css
git commit -m "feat(web): wire Day Tracker page with day navigation, shortcuts, checklist, and daily note"
```

