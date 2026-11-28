# Multi-Note Lectures, AI Study Guides & Write MCP Tools — Design Spec

**Date:** 2026-11-28  
**Status:** Draft (pending user approval)  
**Builds on:** Read-Only MCP Server (`/mcp`), `KnowledgeReadService`, `OpenAiChatService`, `ChatSidebar`, `UniversityPage`, `NotesPage`

---

## 1. Intent & Why

Expand Atrium's AI assistant (both the built-in chat and external `/mcp` agents) from read-only Q&A to **direct create/update writes**, while upgrading University lectures to support **multiple linked Markdown notes** (e.g. your own lecture notes + AI-generated summaries, study guides, cheat sheets) and adding a **one-click "Study Guide"** button on each lecture that runs in the background with a **"View progress / Follow up"** button to jump into the chat thread whenever you want.

---

## 2. Goals, Non-Goals & Anti-Goals

### Goals
1. **Multiple Notes per University Lecture/Item:**
   - Remove the 1-note-per-lecture unique index (`ix_notes_uni_item_unique`) and replace it with a non-unique lookup index (`ix_notes_user_uni_item`).
   - Update `UniversityItemNotesTreeDto` (`NoteSummaryDto? Note` → `IReadOnlyList<NoteSummaryDto> Notes`, keeping backward-compatible `Note` = first note if helpful or cleanly updating `web/`).
   - Update `POST /api/notes` to allow `UniversityActivityItemId` (creating additional notes attached to a lecture/item), and update `UniversityPage` + `NotesPage` sidebar so each lecture displays all of its linked notes (plus a `+ Note` button to add more).
2. **Shared Write Service (`KnowledgeWriteService`) — Direct Writes, No Deletes:**
   - Single service enforcing user isolation, validation, and explicit PAT write scopes (`notes:write`, `calendar:write`, `tasks:write`, `university:write`) when called via `/mcp`, while allowing direct human writes when called from the built-in chat (`HumanJwtOnlyPolicy`).
   - **8 Write Tools (exposed on BOTH `/mcp` and `OpenAiChatService`):**
     1. `create_note(title, content, folderId?, universityCourseId?, universityActivityItemId?, calendarEventId?, occurrenceStartAt?, dailyDate?)` — requires `notes:write` (+ module write scope if linked to course/item/event? Designer call: `notes:write` alone suffices to create a note linked to an existing owned course/item/event after verifying ownership, so a study-guide agent doesn't need `university:write` to mutate courses!).
     2. `update_note(noteId, title?, content?)` — requires `notes:write`; respects `AllowAgentRead` on `/mcp` (cannot overwrite private notes!).
     3. `create_checklist_item(date, title, dueTime?)` — requires `tasks:write`.
     4. `toggle_checklist_item(itemId, isCompleted)` — requires `tasks:write`.
     5. `create_event(title, startAt, endAt, calendarId?, isAllDay?, location?, description?, recurrenceRule?)` — requires `calendar:write`.
     6. `create_course(name, code?, semester?, instructor?, credits?, color?, description?)` — requires `university:write`.
     7. `create_activity(courseId, activityType, title?, defaultLocation?, minAttendanceCount?, minAttendancePercent?, plannedTotalCount?)` — requires `university:write`.
     8. `create_activity_item(activityId, title, theme?, notes?, sequenceNumber?, startAt?, endAt?, isAllDay?, location?)` — requires `university:write` (also creates the linked University calendar event when `startAt`/`endAt` are provided, matching `UniversityEndpoints`).
     9. `extract_pdf_images(attachmentId, noteId, startPage?, endPage?, maxImages?)` — requires `notes:write`; uses `UglyToad.PdfPig` `page.GetImages()` to extract meaningful figures/diagrams (`>= 120×120 px`, capped at 6 per call) from a PDF artifact, saves them as `inline_image` attachments on `noteId`, and returns ready-to-embed Markdown (`![Page N diagram](/api/attachments/<id>)`) so the AI can embed PDF diagrams directly into study guides.
   - **Rich Markdown Study Guides:** The Study Guide prompt instructs the model to use structured headings, GFM tables, callout blockquotes (`> **Key Concept:**`, `> **Exam Tip:**`), syntax-highlighted code blocks, LaTeX math (`$...$` / `$$...$$`), and `extract_pdf_images` to embed relevant diagrams from lecture PDFs. Also enable `remark-gfm` in `NotesPage` reading view so tables render crisply in Notes.
   - **Strict Course-Scoped Chat Writes:** When `ChatSidebar` is in course-scoped mode (`courseId != null`), the built-in chat only exposes `create_note` (locked to `universityCourseId = courseId` or an item of `courseId`), `update_note` (verified to belong to `courseId`), and `create_activity_item` (verified to belong to `courseId`).
   - **Real-time UI sync:** When `OpenAiChatService` executes any write tool during a chat turn, it emits an SSE event `{"type":"mutation","domain":"notes"|"calendar"|"tasks"|"university","id":"...","title":"..."}` so the frontend automatically refreshes the affected view (e.g., the new Study Guide note appears immediately on the lecture row without reloading!).
3. **Background "Study Guide" Button on University Lectures:**
   - Each lecture/item row in `UniversityPage` gets a **"Study Guide"** button.
   - Clicking it triggers `POST /api/chat/stream` in the background (with `courseId` locked to the lecture's course and a structured prompt instructing the model to read the lecture's notes + extracted slide/PDF text and save a well-structured Markdown study guide via `create_note` attached to `universityActivityItemId`).
   - **`ChatSidebar` stays closed by default.** While generating, the lecture row displays a live status pill (`Generating…`) with a **"View progress"** button; once finished, the new Study Guide note appears right under the lecture and the button switches to **"Follow up"** (opening `ChatSidebar` directly to that saved chat thread so you can refine it or ask follow-up questions).
4. **AccountPage PAT Scope Chips:**
   - Add `notes:write`, `calendar:write`, `tasks:write`, and `university:write` to `AuthConstants.AllowedScopes` and `AccountPage.tsx` `AVAILABLE_SCOPES` so users can mint scoped read+write or read-only MCP Agent PATs.

### Non-Goals
- No destructive `delete_*` MCP/chat tools (user deletes manually in the UI if needed).
- No Proposals approval inbox (user explicitly chose direct writes).

### Anti-Goals
- **Never allow `/mcp` write tools to bypass `AllowAgentRead`:** `update_note` via `/mcp` MUST refuse to modify a note with `EffectiveAllowAgentRead == false` (returns NotFound).
- **Never allow cross-course writes in course-scoped chat:** `create_note` / `update_note` / `create_activity_item` verify the target belongs to `lockedCourseId`.
- **Never auto-pop the chat drawer on "Study Guide" click** — keep it closed until the user clicks **"View progress"** or **"Follow up"**.

---

## 3. Approaches Considered

### Approach 1: Shared `KnowledgeWriteService` + Background Chat-Driven Study Guide *(Selected)*
- **How it works:** One `KnowledgeWriteService` backs both `/mcp` (`AtriumMcpTools`, 8 new write tools = 17 total) and `OpenAiChatService`. Clicking **"Study Guide"** starts a persisted chat thread in the background without opening `ChatSidebar`, emitting `mutation` SSE events to auto-refresh lecture notes and exposing **"View progress / Follow up"** to open that exact thread on demand.
- **Tradeoffs:** Clean reuse of streaming, tool-calling, reasoning, and persisted chat history; zero duplicated business logic; natural follow-up editing.

### Approach 2: Separate Dedicated Summary Endpoint
- **How it works:** Dedicated `POST /api/university/items/{id}/summary` endpoint separate from chat.
- **Tradeoffs:** Duplicates OpenAI prompt/tool logic and provides no streaming progress view or follow-up refinement thread.

---

## 4. Schema & Contract Changes

1. **EF Migration `20261128160000_AllowMultipleNotesPerUniversityItem`:**
   - Drop unique index `ix_notes_uni_item_unique` on `notes(university_activity_item_id)`.
   - Create non-unique index `ix_notes_user_uni_item` on `notes(user_id, university_activity_item_id, deleted_at)`.
2. **Notes & University Contracts:**
   - `CreateNoteRequest` adds optional `Guid? UniversityActivityItemId = null` (automatically derives `UniversityCourseId` from the item and validates ownership).
   - `UniversityItemNotesTreeDto` replaces `NoteSummaryDto? Note` with `IReadOnlyList<NoteSummaryDto> Notes`.
   - `UniversityActivityItemDto` adds `int NoteCount = 0` (keeping `Guid? LinkedNoteId` pointing to the first/most-recent note for backward compatibility).
   - `UniversityPage` renders all notes attached to each lecture item as clickable chips (alongside its artifact chips) plus `+ Note` and `Study Guide`.
3. **Auth Scopes:**
   - Add `notes:write`, `calendar:write`, `tasks:write`, `university:write` to `AuthConstants.AllowedScopes` and `AccountPage.tsx`.
4. **Chat SSE Mutation Event:**
   - `{"type":"mutation","domain":"notes"|"calendar"|"tasks"|"university","id":"<guid>","title":"..."}` emitted whenever a write tool succeeds in `OpenAiChatService`.

---

## 5. What's Left to the Builder

- Exact tool parameter validation & defaults in `KnowledgeWriteService` (mirroring `DayEndpoints`, `CalendarEndpoints`, `UniversityEndpoints`, `NoteEndpoints` limits).
- Exact background study-guide state map (`studyGuideJobsByItemId`) in `App.tsx` so multiple lectures can track their latest `threadId` + `status` (`generating` | `done` | `error`) cleanly without blocking normal chat usage.
- Comprehensive xUnit tests for multi-note lectures, all 8 write MCP tools (scope enforcement + `AllowAgentRead` protection on `update_note`), and chat write tool execution + `mutation` SSE events.

