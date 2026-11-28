# Assistant Upgrade: Multi-Provider Models, Chat Attachments, Web/URL Tools, and Bulk Course Setup

**Date:** 2026-11-28  
**Branch:** `feature/mcp-write-and-study-guides` (or follow-up branch off it)  
**Size:** Project

---

## 1. Intent & Why

Atrium's built-in AI assistant can already search and read the user's knowledge base and create notes, events, tasks, courses, activities, and activity items. However, setting up a university course from external sources is still manual:

- Syllabi live on university web pages (e.g., `https://eduinfo.bme.hu/subjects/BMEVITMMSMA001-00?lang=en`) with lecture topics, descriptions, credits, and assessment rules.
- Schedules live in exported calendar files (e.g., Neptun `.ics` exports containing 100+ `VEVENT`s across an entire semester of courses) or screenshots of timetables/rubrics.
- Answering general questions or looking up references requires web search and link reading.
- Using vision models for image attachments (while keeping a fast/cheap text model as the daily driver) requires configuring multiple OpenAI-compatible inference URLs/API keys and switching models from the chat window.

**Flagship workflow:** The user pastes a syllabus URL, attaches a Neptun `.ics` export (or a timetable screenshot), and prompts in natural language: *"Set up this course with its lectures, labs, and assessments using the syllabus and my Neptun calendar."* The assistant fetches the syllabus, filters the `.ics` for that course's sessions, creates the course and activities, and batch-creates all dated lectures, labs, and assessments cleanly in one conversation.

---

## 2. Goals, Non-Goals, and Anti-Goals

### Goals
1. **Multi-Provider & Multi-Model AI Hub:**
   - Support multiple OpenAI-compatible AI providers (each with name, base URL, and AES-256-GCM encrypted API key) and multiple models per provider (model ID, display label, `supportsVision` flag).
   - Auto-migrate any existing single `UserPreferences.AiInferenceUrl` / `AiModel` / `AiApiKeyEncrypted` configuration into the new provider/model tables without breaking current users.
   - Profile/Account settings allow selecting a **Default Model** and a **Default Vision Model**.
   - Chat sidebar includes a compact **Model Picker** dropdown. When a user sends a message with image attachments while a non-vision model is selected, Atrium automatically routes that turn to the configured **Default Vision Model** (with a clear status indicator) or returns a helpful message if no vision model is configured.
2. **Persisted Chat File Attachments (Docs, Calendars, Images):**
   - Users can attach files to chat messages via file picker, drag-and-drop, or paste (for images).
   - Supported types: `.ics`, `.pdf`, `.docx`, `.pptx`, `.txt`, `.md`, `.csv`, `.json`, `.png`, `.jpg`, `.jpeg`, `.webp` (up to 15 MB per file, max 5 files per message).
   - Attachments are persisted via `IAttachmentStorage` and linked to the chat thread/message so reopening a thread in Chat History shows attached file chips and allows follow-up questions without re-uploading.
   - Large files (especially 2,000-line Neptun `.ics` exports or multi-page PDFs) are **never** dumped raw into the LLM prompt; instead, the assistant receives a compact manifest of attached files and inspects them on demand via dedicated tools (`query_chat_ics`, `read_chat_attachment`).
   - Images are encoded as OpenAI-compatible `image_url` content parts (`data:<mime>;base64,...`) when sent to a vision-capable model.
3. **Web Search & URL Viewing:**
   - `web_search` tool: works out of the box with a keyless DuckDuckGo HTML fallback, plus optional configured providers in Account Settings (**Tavily**, **Brave Search**, or self-hosted **SearXNG** URL + optional encrypted API key).
   - `fetch_url` tool: fetches public `http://` / `https://` URLs with strict SSRF protection, strips scripts/nav/boilerplate, and returns clean Markdown/plain text preserving headings and tables (plus supports direct `.ics`, `.txt`, `.md`, and `.pdf` URLs).
4. **Reliable Bulk Course Setup:**
   - Add a batch `create_activity_items` write tool (creating up to 50 items per call for an activity, optionally with schedules/calendar events) so creating a full semester of lectures/labs/assessments takes 1–2 tool rounds instead of 20–30.
   - Raise `OpenAiChatService.MaxToolRounds` from `12` to `16` to comfortably cover fetch + ICS query + course creation + multiple batch item calls + final summary.

### Non-Goals
- Full headless browser / Playwright rendering for heavily JS-only SPA pages (standard HTTP fetch + HTML parsing covers server-rendered university pages like `eduinfo.bme.hu`, static docs, Wikipedia, etc.).
- Exposing `web_search`, `fetch_url`, or chat-attachment tools on the external `/mcp` endpoint (external MCP clients already have their own web/file capabilities; however, `create_activity_items` **is** exposed on `/mcp` since it is a core Atrium university write tool).
- Automatic copying of chat attachments into University course artifacts unless the user explicitly asks (out of scope for this iteration).
- Approval modals before bulk course creation (user prefers direct writes since courses/items can be edited or deleted).

### Anti-Goals
- **Context-window blowups:** Dumping a full 2,000-line `.ics` file or 50 KB raw HTML page directly into the prompt.
- **SSRF vulnerabilities:** Allowing `fetch_url` or `web_search` (except explicitly user-configured SearXNG/inference URLs) to hit loopback (`127.0.0.1`, `::1`), RFC 1918 private networks, link-local (`169.254.0.0/16`), or cloud metadata endpoints, including via HTTP redirects.
- **Breaking existing single-provider setups:** Existing users with `AiInferenceUrl`/`AiModel` configured must continue working seamlessly on first load after migration.

---

## 3. Approaches Considered

### Approach A — Unified Multi-Provider Hub + Tool-Driven Pipeline *(Chosen)*
- **Providers & Models:** Separate `ai_providers` (Name, BaseUrl, EncryptedApiKey) and `ai_models` (ProviderId, ModelId, Label, SupportsVision) tables, with `UserPreferences` holding `DefaultAiModelId`, `DefaultVisionModelId`, and optional web search provider config.
- **Attachments & Web:** Persisted `chat_attachments` on `IAttachmentStorage`, on-demand `query_chat_ics` and `read_chat_attachment` tools, `web_search` (DuckDuckGo default + Tavily/Brave/SearXNG), `fetch_url` (SSRF-safe HTML-to-Markdown), and batch `create_activity_items`.
- **Trade-offs:** Adds two small configuration tables instead of one flat table, but avoids duplicating API keys across models from the same provider (OpenRouter, OpenAI, Ollama, etc.), keeps context windows lean via on-demand tools, and delivers the complete course-setup workflow end-to-end.

### Approach B — Flat Model Profiles
- Same attachments, web tools, and batch course setup, but Settings stores flat Model Profiles (each duplicating BaseUrl + EncryptedApiKey + ModelId + SupportsVision).
- **Trade-offs:** Slightly simpler Settings UI and one fewer table, at the cost of repeating URLs/keys whenever using multiple models from the same provider.

### Approach C — Two Separate Milestones
- Ship Multi-Provider + Chat Attachments first, then Web Search + URL Fetch + Batch Course Setup second.
- **Trade-offs:** Smaller individual PRs, but delays the flagship syllabus + `.ics` workflow until milestone 2.

**Decision:** User selected **Approach A**.

---

## 4. Architecture & Design Details

### 4.1 Multi-Provider & Multi-Model Configuration

#### Database Schema (PostgreSQL / EF Core)
1. **`ai_providers`**
   - `Id` (`uuid`, PK)
   - `UserId` (`uuid`, FK → `users.id`, cascade delete, indexed)
   - `Name` (`varchar(80)`, required — e.g., `"OpenRouter"`, `"Local Ollama"`, `"OpenAI"`)
   - `BaseUrl` (`varchar(500)`, required)
   - `ApiKeyEncrypted` (`text`, nullable — encrypted via existing `SecretProtector`)
   - `CreatedAt`, `UpdatedAt` (`timestamptz`)
2. **`ai_models`**
   - `Id` (`uuid`, PK)
   - `UserId` (`uuid`, FK → `users.id`, cascade delete)
   - `ProviderId` (`uuid`, FK → `ai_providers.id`, cascade delete, indexed)
   - `ModelId` (`varchar(160)`, required — e.g., `"openai/gpt-4o-mini"`)
   - `DisplayName` (`varchar(120)`, required — defaults to `ModelId`)
   - `SupportsVision` (`boolean`, default `false`)
   - `SortOrder` (`int`, default `0`)
   - `CreatedAt`, `UpdatedAt` (`timestamptz`)
3. **`user_preferences` additions**
   - `DefaultAiModelId` (`uuid`, nullable, FK → `ai_models.id`, `ON DELETE SET NULL`)
   - `DefaultVisionModelId` (`uuid`, nullable, FK → `ai_models.id`, `ON DELETE SET NULL`)
   - `WebSearchProvider` (`varchar(32)`, nullable — `"duckduckgo"` (default when null/empty), `"tavily"`, `"brave"`, `"searxng"`)
   - `WebSearchUrl` (`varchar(500)`, nullable — used for SearXNG base URL)
   - `WebSearchApiKeyEncrypted` (`text`, nullable — encrypted via `SecretProtector`, used for Tavily/Brave or optional SearXNG auth)
   - Legacy `AiInferenceUrl`, `AiModel`, `AiApiKeyEncrypted` remain temporarily for zero-downtime migration: on first read/migration (or in the EF migration / startup data-backfill helper), if a user has `AiInferenceUrl` + `AiModel` set and zero `ai_providers`, Atrium auto-creates a `"Default"` provider and model and sets `DefaultAiModelId` to it. *(Builder call: perform the lazy backfill in `AiProviderService` on read/chat-stream so it works cleanly across both PostgreSQL and InMemory test providers without raw SQL migration fragility.)*

#### API Endpoints (`/api/ai/providers`, `/api/ai/models`, `/api/auth/preferences`)
- `GET /api/ai/config`: returns `{ providers, models, defaultModelId, defaultVisionModelId, webSearch: { provider, url, hasApiKey } }` (never returns plaintext API keys, only `hasApiKey: bool`).
- `POST /api/ai/providers`, `PUT /api/ai/providers/{id}`, `DELETE /api/ai/providers/{id}`.
- `POST /api/ai/providers/{id}/discover-models`: calls the provider's `GET <baseUrl>/models` with the stored (or supplied) key and returns a list of available model IDs so the user can add them with one click (gracefully returns an error message if the provider doesn't implement `/models`).
- `POST /api/ai/models`, `PUT /api/ai/models/{id}`, `DELETE /api/ai/models/{id}`.
- `PUT /api/ai/defaults`: updates `defaultModelId`, `defaultVisionModelId`, and web search settings (`webSearchProvider`, `webSearchUrl`, `webSearchApiKey`, `clearWebSearchApiKey`).

#### Model Selection & Vision Auto-Routing in Chat
- `ChatStreamRequest` adds optional `Guid? ModelId = null` and `IReadOnlyList<Guid>? AttachmentIds = null`.
- Resolution order:
  1. Start with `request.ModelId` (if owned by user), else `userPrefs.DefaultAiModelId`, else first configured model (with fallback to legacy `AiInferenceUrl`/`AiModel` if present).
  2. If the **current turn** includes image attachments (`request.AttachmentIds` contains any `Kind == "image"`) AND the selected model has `SupportsVision == false`:
     - If `userPrefs.DefaultVisionModelId` is configured (and `SupportsVision == true`), automatically switch to that vision model for **this turn** and emit a status event: `"Switched to <VisionModelLabel> for image input…"`.
     - Otherwise, return HTTP 400 with a clear error: `"This message includes images, but neither the selected model nor a Default Vision Model supports vision. Configure a vision-capable model in Account → AI Assistant."`
- `ChatThread` stores `Guid? LastModelId` (nullable, `ON DELETE SET NULL`), persisting the **user's selected model** (not a temporary auto-routed vision override) so subsequent text-only turns and reopened threads stay on their preferred model. *(Our call: if a later text-only turn runs on a non-vision model in a thread that had an earlier image, prior-turn images are represented as `[Image attached: <filename>]` text placeholders instead of `image_url` blocks so the provider never rejects the request with HTTP 400; if the active model supports vision, prior-turn images are inlined up to the 8 MB budget.)*

---

### 4.2 Persisted Chat Attachments

#### Database Schema
- **`chat_attachments`**
  - `Id` (`uuid`, PK)
  - `UserId` (`uuid`, FK → `users.id`, cascade delete)
  - `ThreadId` (`uuid`, nullable, FK → `chat_threads.id`, cascade delete — nullable briefly between upload and first message send, then bound to the thread; orphaned unattached uploads older than 24h can be ignored/cleaned up)
  - `MessageId` (`uuid`, nullable, FK → `chat_messages.id`, cascade delete)
  - `FileName` (`varchar(255)`, required)
  - `ContentType` (`varchar(120)`, required)
  - `Kind` (`varchar(24)` — `"image"`, `"ics"`, `"document"`)
  - `SizeBytes` (`bigint`)
  - `StorageKey` (`varchar(500)`, required — stored via `IAttachmentStorage` under `chat/{userId}/{attachmentId}{ext}`)
  - `ExtractedText` (`text`, nullable — for small/extracted documents, capped at 500,000 chars via `DefaultArtifactTextExtractor`)
  - `MetadataJson` (`text`, nullable — e.g., for `.ics`: `{ eventCount, dateRangeStart, dateRangeEnd, distinctSummaries[] }`; for docs: `{ pageCount, charCount }`)
  - `CreatedAt` (`timestamptz`)

#### Upload & Download Endpoints
- `POST /api/chat/attachments` (`multipart/form-data`, single file per request, HumanJwtOnlyPolicy):
  - Validates extension/MIME against allowlist (`.ics`, `.pdf`, `.docx`, `.pptx`, `.txt`, `.md`, `.csv`, `.json`, `.png`, `.jpg`, `.jpeg`, `.webp`) and size `<= 15 MB`.
  - Saves binary to `IAttachmentStorage`.
  - For `Kind == "ics"`: parses via `IcsParser.Parse(...)`, computes summary metadata (event count, earliest/latest date, top distinct event summaries with counts) into `MetadataJson`.
  - For `Kind == "document"`: extracts text via `DefaultArtifactTextExtractor.Extract(...)`, stores joined page-labelled text in `ExtractedText` and page/char counts in `MetadataJson`.
  - For `Kind == "image"`: validates image signature/MIME (`image/png`, `image/jpeg`, `image/webp`), stores binary.
  - Returns `ChatAttachmentDto` (`id`, `fileName`, `contentType`, `kind`, `sizeBytes`, `summary`, `url`, `createdAt`).
- `GET /api/chat/attachments/{id}`: authenticated download/preview for the owning user (used to render image thumbnails and downloadable file chips in chat history).
- Deleting a `ChatThread` cascades to `chat_attachments` rows and deletes their backing files from `IAttachmentStorage`.

#### How Attachments Reach the LLM
1. **Prompt Manifest (for all non-image attachments in the thread):**
   - `OpenAiChatService` appends a compact system-context block listing all non-image attachments on the thread:
     - ID, filename, kind, and compact summary (e.g., for `.ics`: `"142 events from 2026-09-07 to 2026-12-18; distinct summaries: Mesterséges általános intelligencia (- EA) (14), ..."`).
   - For small text/document files (`<= 8,000` chars), the assistant can still call `read_chat_attachment`, or *(builder call)* `read_chat_attachment` supports pagination/search so nothing ever floods context.
2. **On-Demand Attachment Tools:**
   - `query_chat_ics(attachmentId, query?, startDate?, endDate?, limit?)`: parses the stored `.ics` via `IcsParser.Parse(...)`, filters events by case-insensitive substring match across `Summary`, `Description`, `Location` and/or date range, and returns structured JSON events (`summary`, `startAt`, `endAt`, `location`, `description`, `isAllDay`) in ISO-8601 with the calendar's timezone (defaulting to the user's profile timezone or `Europe/Budapest` from `X-WR-TIMEZONE`), plus a `distinctSummaries` overview when `query` is omitted.
   - `read_chat_attachment(attachmentId, query?, offset?, maxChars?)`: returns extracted text slices or keyword-matching excerpts from an attached PDF/DOCX/PPTX/text file.
3. **Images (Vision):**
   - For user messages that have `Kind == "image"` attachments, `OpenAiChatService` builds an OpenAI multimodal `content` array (`[{ type: "text", text }, { type: "image_url", image_url: { url: "data:<mime>;base64,..." } }]`).
   - *(Builder call: resize/cap raw image payloads or enforce a 5 MB per-image cap for base64 encoding; if multiple images are sent across history, only inline base64 for the current turn's images and recent turns up to a total budget of 8 MB to avoid provider payload limits.)*

---

### 4.3 Web Search & URL Fetching Tools

#### SSRF Protection (Shared `SafeHttpFetcher`)
- Only `http` and `https` schemes allowed.
- Resolves DNS hostnames via `Dns.GetHostAddressesAsync` and rejects any address in loopback, IPv4 private (`10/8`, `172.16/12`, `192.168/16`), link-local (`169.254/16`), CGNAT (`100.64/10`), multicast, or IPv6 unique-local/link-local/loopback (`::1`, `fc00::/7`, `fe80::/10`).
- Disables automatic redirects on `HttpClientHandler` and manually follows up to 5 redirects, re-validating every redirect target's IP address before connecting.
- Enforces strict timeouts (15s) and response body size caps (2 MB for HTML/text, 10 MB for direct PDF/ICS links).

#### `fetch_url(url, maxChars?)`
- Validates and fetches via `SafeHttpFetcher` with a clean `User-Agent` and `Accept-Language: en,hu;q=0.9`.
- **HTML responses:** Parses HTML (using `AngleSharp` or `HtmlAgilityPack` — *builder call: `HtmlAgilityPack` or `AngleSharp` NuGet package, zero-warning clean*), removes `<script>`, `<style>`, `<noscript>`, `<svg>`, `<nav>`, `<footer>`, extracts `<title>` and main content (`<main>`, `<article>`, or `<body>`), and converts headings (`h1`–`h6`), lists, paragraphs, links, and `<table>` rows into clean Markdown so structured syllabus tables (like BME `eduinfo.bme.hu` weekly topics, grading tables, and assessment dates) stay intact and readable!
- **Direct `.ics` / `.pdf` / plain text URLs:** Extracts via `IcsParser` (returning summary + first 50 events) or `DefaultArtifactTextExtractor`.
- Truncates cleanly at `maxChars` (default `16_000`, max `24_000`) with a truncation note.

#### `web_search(query, maxResults?)`
- Reads user's `WebSearchProvider` preference:
  - **`tavily`**: `POST https://api.tavily.com/search` with API key; returns `{ title, url, snippet }[]`.
  - **`brave`**: `GET https://api.search.brave.com/res/v1/web/search?q=...` with `X-Subscription-Token`; returns `{ title, url, snippet }[]`.
  - **`searxng`**: `GET <WebSearchUrl>/search?q=...&format=json` (allows self-hosted local/LAN URL since it is explicitly configured by the user in Settings, same as inference URLs); returns `{ title, url, snippet }[]`.
  - **`duckduckgo` (default keyless fallback)**: `POST https://html.duckduckgo.com/html/` (via `SafeHttpFetcher`) and parses result titles, URLs (unwrapping `uddg=` redirect param), and snippets. Returns a graceful message if rate-limited/blocked, suggesting configuring Tavily/Brave/SearXNG in Settings.
- Capped at `maxResults` (default 5, max 10).

---

### 4.4 Bulk Course Setup Tooling

#### `create_activity_items` (Batch Write Tool)
- Added to `KnowledgeWriteService`, `OpenAiChatService` (built-in chat), and `AtriumMcpTools` (`/mcp`, gated by `university:write` scope).
- Input:
  - `activityId` (`Guid`, required)
  - `items`: array (1–50 items) of `{ title, theme?, notes?, sequenceNumber?, startAt?, endAt?, isAllDay?, location? }`
- Runs inside a single database transaction (or single `SaveChangesAsync`), creating all `UniversityActivityItem` rows and their linked `CalendarEvent` rows in the user's University calendar, then returning the created item IDs and count.
- Emits a single `mutation` SSE event (`domain: "university"`) so the frontend refreshes the University workspace and Calendar immediately.

#### System Prompt Guidance
- Update `OpenAiChatService.BuildSystemPrompt` to document:
  - `web_search`, `fetch_url`, `query_chat_ics`, `read_chat_attachment`, and `create_activity_items`.
  - Clear course-setup playbook: when the user provides a syllabus URL and/or `.ics` attachment, (1) `fetch_url` on the syllabus, (2) `query_chat_ics` (first without query if needed to see distinct course names, then filtered for the target course), (3) `create_course` + `create_activity` for each component (lecture, laboratory, practice, midterm, homework_assignment, exam, etc.), and (4) `create_activity_items` in batches to populate all dated sessions and assessments with their themes/descriptions and rooms.

---

### 4.5 Frontend UI Changes

1. **Account Settings → AI Assistant** ([AccountPage.tsx](file:///home/andrish/Work/personal/assistant/web/src/components/AccountPage.tsx)):
   - **Providers section:** List of configured providers (Name, Base URL, masked key status, Add / Edit / Delete).
   - **Models section:** Under each provider (or unified table grouped by provider), add models manually or click **"Fetch models"** (calls `/api/ai/providers/{id}/discover-models`), toggle **"Vision"** badge per model, delete model.
   - **Defaults section:** Dropdowns for **Default Model** and **Default Vision Model** (filtered to vision-enabled models).
   - **Web Search section:** Provider selector (`DuckDuckGo (Built-in, no key)` / `Tavily` / `Brave Search` / `SearXNG`), conditional URL and API Key inputs.
2. **Chat Window** ([ChatSidebar.tsx](file:///home/andrish/Work/personal/assistant/web/src/components/ChatSidebar.tsx)):
   - **Model Picker:** Compact dropdown in the chat composer footer (or header) showing configured models with a small eye/vision indicator on vision-capable models. Defaults to the thread's last model or the user's Default Model.
   - **Attachment Controls:** Paperclip button next to the composer input, drag-and-drop overlay on the chat panel, and clipboard image paste (`onPaste`) support. Pending attachments render as removable chips (with image thumbnails for images, file icons + size for docs/ICS) above the textarea; sent messages render their persisted attachment chips/thumbnails in the message bubble (including when reloaded from Chat History).

---

## 5. What's Left to the Builder

- Choice of HTML parsing library (`AngleSharp` vs `HtmlAgilityPack`) — pick whichever is cleanest with .NET 10 and `-warnaserror`.
- Exact CSS styling of the model picker, attachment chips, and Account Settings multi-provider cards (follow Atrium's existing `.account-card`, `.btn`, `.chat-sidebar-*`, and CSS variable tokens).
- Unit & integration test coverage:
  - Multi-provider CRUD, legacy preference auto-migration, and vision auto-routing (including error when no vision model is configured).
  - Chat attachment upload (`.ics`, `.txt`, `.png`), `query_chat_ics` filtering against a Neptun-style multi-course `.ics`, and thread deletion cleanup.
  - `SafeHttpFetcher` SSRF rejection tests (loopback, private IPv4, link-local, redirect to private IP) and HTML-to-Markdown table extraction test.
  - `create_activity_items` batch creation test (both chat and `/mcp` scope enforcement).
- Clean verification: `dotnet build -c Release -warnaserror`, full `dotnet test -c Release`, `npm --prefix web run typecheck`, `npm --prefix web run build`, and `npm run check:suppressions`.

