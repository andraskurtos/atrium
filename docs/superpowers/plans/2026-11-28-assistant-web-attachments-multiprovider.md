# Assistant Upgrade: Multi-Provider Models, Chat Attachments, Web/URL Tools, and Bulk Course Setup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Atrium's built-in AI assistant multi-provider/multi-model configuration with vision auto-routing, persisted chat file attachments (`.ics`, documents, images), SSRF-safe web search and URL reading, and batch university activity-item creation so users can set up entire university courses from syllabus links and Neptun `.ics` calendars via natural language.

**Architecture:** Add `ai_providers`, `ai_models`, and `chat_attachments` tables in PostgreSQL (with lazy backfill from legacy single-provider preferences), expose CRUD/upload endpoints under `/api/ai/*` and `/api/chat/attachments`, implement zero-dependency SSRF-safe HTTP fetching + HTML-to-Markdown conversion + pluggable web search (`duckduckgo` default + `tavily`/`brave`/`searxng`), add batch `create_activity_items` to `KnowledgeWriteService` and `/mcp`, wire all new tools and multimodal image handling into `OpenAiChatService`, and update `AccountPage` and `ChatSidebar` on the React frontend.

**Tech Stack:** .NET 10 Minimal API, EF Core 10 (Npgsql + InMemory test provider), xUnit, React 19, TypeScript, Vite. Zero new NuGet or npm packages required.

**Spec:** `docs/superpowers/specs/2026-11-28-assistant-web-attachments-multiprovider-design.md`

## Global Constraints

- .NET builds must pass `dotnet build -c Release -warnaserror api/Atrium.sln` with 0 warnings and 0 errors.
- No undocumented warning suppressions (`npm run check:suppressions` must pass).
- Frontend must pass `npm --prefix web run typecheck` and `npm --prefix web run build`.
- All API keys (`AiProvider.ApiKeyEncrypted`, `UserPreferences.WebSearchApiKeyEncrypted`) must be encrypted at rest using `SecretProtector` and never returned in plaintext (only `hasApiKey: bool`).
- `SafeHttpFetcher` must block non-`http(s)` schemes and any resolved loopback, private IPv4, link-local, CGNAT, multicast, or IPv6 loopback/ULA/link-local IP across initial requests and up to 5 manual redirects.
- User-configured inference URLs and SearXNG URLs are exempt from the private-IP SSRF block (users commonly self-host Ollama/SearXNG on localhost/LAN), whereas `fetch_url` and `duckduckgo` always enforce SSRF blocking.
- Existing users with `UserPreferences.AiInferenceUrl` + `AiModel` must seamlessly keep working via lazy auto-backfill into `ai_providers` + `ai_models`.
- Chat attachments allowlist: `.ics`, `.pdf`, `.docx`, `.pptx`, `.txt`, `.md`, `.csv`, `.json`, `.png`, `.jpg`, `.jpeg`, `.webp`; max 15 MB per file; max 5 files per message; max 5 MB per image for base64 vision encoding; max 8 MB total image payload across history.

## Review Focus

1. **Multi-turn image follow-up on a non-vision model:** Turn 1 attaches an image (auto-routed to `DefaultVisionModelId`), Turn 2 is text-only with a non-vision model selected — Turn 2 must NOT send `image_url` blocks (must downgrade historical images to `[Image attached: <filename>]`) and `ChatThread.LastModelId` must stay on the user's selected model. *(Pinned by test in Task 5.)*
2. **SSRF redirect bypass:** A public URL that returns HTTP 302 redirecting to `http://127.0.0.1/` or `http://169.254.169.254/` must be blocked before connecting to the redirect target. *(Pinned by test in Task 3.)*
3. **Cross-user attachment/model spoofing:** Passing another user's `modelId` or `attachmentIds` to `/api/chat/stream` or `GET /api/chat/attachments/{id}` must be rejected (404/400) and never leak data. *(Pinned by tests in Tasks 1 & 2.)*
4. **Neptun multi-course `.ics` timezone & filtering:** Querying a 100+ event Neptun `.ics` with `X-WR-TIMEZONE:Europe/Budapest` and UTC `DTSTART`/`DTEND` must return accurate distinct summaries when `query` is omitted and cleanly filter by course name without dumping all events. *(Pinned by test in Task 2.)*
5. **Deleting a provider/model referenced as default:** Deleting a model referenced by `DefaultAiModelId`, `DefaultVisionModelId`, or `ChatThread.LastModelId` must set those FKs to `null` (not fail with FK constraint error). *(Pinned by test in Task 1.)*

---

### Task 1: Multi-Provider & Multi-Model Schema, `AiConfigService`, and `/api/ai/*` Endpoints

**Files:**
- Create: `api/Atrium.Api/Data/Entities/AiProvider.cs`
- Create: `api/Atrium.Api/Data/Entities/AiModel.cs`
- Create: `api/Atrium.Api/Chat/AiConfigContracts.cs`
- Create: `api/Atrium.Api/Chat/AiConfigService.cs`
- Create: `api/Atrium.Api/Endpoints/AiConfigEndpoints.cs`
- Create: `api/Atrium.Api/Data/Migrations/20261128170000_AddMultiProviderAiAndChatAttachments.cs` (and `.Designer.cs`)
- Modify: `api/Atrium.Api/Data/Entities/UserPreferences.cs`
- Modify: `api/Atrium.Api/Data/Entities/ChatThread.cs`
- Modify: `api/Atrium.Api/Data/AppDbContext.cs`
- Modify: `api/Atrium.Api/Data/Migrations/AppDbContextModelSnapshot.cs`
- Modify: `api/Atrium.Api/Program.cs`
- Test: `api/Atrium.Api.Tests/AiConfigAndAttachmentsTests.cs`

**Interfaces:**
- Produces:
  - `AiConfigService.GetConfigAsync(Guid userId, CancellationToken ct) -> Task<AiConfigResponse>` (auto-backfills legacy `UserPreferences.AiInferenceUrl`/`AiModel` into a `"Default"` provider + model if user has 0 providers)
  - `AiConfigService.ResolveModelForChatAsync(Guid userId, Guid? requestedModelId, bool turnHasImages, CancellationToken ct) -> Task<ResolvedChatModelResult>` where `ResolvedChatModelResult` contains `(bool Success, string? ErrorMessage, ResolvedChatModel? Model, bool SwitchedToVision, Guid? UserSelectedModelId)` and `ResolvedChatModel` contains `(Guid? ModelEntityId, string BaseUrl, string ModelId, string DisplayName, string? ApiKey, bool SupportsVision)`
  - `AiConfigService.GetWebSearchSettingsAsync(Guid userId, CancellationToken ct) -> Task<ResolvedWebSearchSettings>` (`Provider`, `Url`, `ApiKey`)
  - Endpoints mapped under `/api/ai` (`HumanJwtOnlyPolicy`): `GET /config`, `POST /providers`, `PUT /providers/{id:guid}`, `DELETE /providers/{id:guid}`, `POST /providers/{id:guid}/discover-models`, `POST /models`, `PUT /models/{id:guid}`, `DELETE /models/{id:guid}`, `PUT /defaults`.

- [ ] **Step 1: Write failing tests in `api/Atrium.Api.Tests/AiConfigAndAttachmentsTests.cs`**

Create tests covering:
- `AiConfig_LegacyPreferences_AutoBackfillIntoDefaultProviderAndModel`: sets legacy `AiInferenceUrl`, `AiModel`, `AiApiKey` via `/api/auth/preferences`, calls `GET /api/ai/config`, asserts 1 provider `"Default"`, 1 model matching `AiModel`, `defaultModelId` set, `hasApiKey == true`, and no plaintext key in JSON.
- `AiConfig_CrudProvidersModelsDefaults_AndDeleteNullsDefaults`: creates 2 providers (one with 2 models, one marked `supportsVision: true`), sets `defaultModelId`, `defaultVisionModelId`, and `webSearch` (`tavily` + key), deletes the vision model, asserts `GET /api/ai/config` returns `defaultVisionModelId == null` with 200 OK.
- `AiConfig_CrossUserIsolation_RejectsOtherUsersProviderAndModel`: second user cannot update/delete or set default to first user's provider/model (404/400).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test api/Atrium.sln --filter "AiConfigAndAttachmentsTests"`
Expected: FAIL (compile error / missing types).

- [ ] **Step 3: Create entities, update `AppDbContext`, and add EF migration**

- Create `AiProvider` (`Id`, `UserId`, `Name` max 80, `BaseUrl` max 500, `ApiKeyEncrypted`, `CreatedAt`, `UpdatedAt`, nav `User`, `Models`).
- Create `AiModel` (`Id`, `UserId`, `ProviderId`, `ModelId` max 160, `DisplayName` max 120, `SupportsVision`, `SortOrder`, `CreatedAt`, `UpdatedAt`, nav `User`, `Provider`).
- Add to `UserPreferences`: `Guid? DefaultAiModelId`, `Guid? DefaultVisionModelId`, `string? WebSearchProvider` (max 32), `string? WebSearchUrl` (max 500), `string? WebSearchApiKeyEncrypted`.
- Add to `ChatThread`: `Guid? LastModelId` (FK → `ai_models.id`, `DeleteBehavior.SetNull`).
- Configure `ai_providers`, `ai_models`, and nullable `SetNull` FKs in `AppDbContext.OnModelCreating`, and create the PostgreSQL migration + snapshot update.

- [ ] **Step 4: Implement `AiConfigContracts.cs`, `AiConfigService.cs`, `AiConfigEndpoints.cs`, and register in `Program.cs`**

- Validate `BaseUrl` / `WebSearchUrl` as valid `http`/`https` URIs; validate `WebSearchProvider` in `{"duckduckgo", "tavily", "brave", "searxng"}`.
- `DiscoverModelsAsync` calls `GET <baseUrl>/models` using `OpenAiChatService.HttpClientName` with optional `Bearer <apiKey>` and parses `data[].id` (returning a friendly error string if the endpoint fails or is unsupported).
- `ResolveModelForChatAsync`:
  1. Ensures legacy backfill has run.
  2. Resolves `selectedModel` from `requestedModelId` (must belong to `userId`), else `DefaultAiModelId`, else first model ordered by `SortOrder, CreatedAt`.
  3. If `turnHasImages && !selectedModel.SupportsVision`: switches to `DefaultVisionModelId` (if present and `SupportsVision`) with `SwitchedToVision = true` while keeping `UserSelectedModelId = selectedModel.Id`; otherwise returns `Success = false` with the spec's error message.
- Register `AiConfigService` and call `app.MapAiConfigEndpoints()` in `Program.cs`.

- [ ] **Step 5: Run tests and build**

Run: `dotnet build --no-restore -c Release -warnaserror api/Atrium.sln && dotnet test -c Release --no-build --filter "AiConfigAndAttachmentsTests"`
Expected: 0 warnings, 0 errors, all tests PASS.

---

### Task 2: Persisted Chat Attachments (`ChatAttachment` Entity, Upload/Download, `query_chat_ics`, `read_chat_attachment`)

**Files:**
- Create: `api/Atrium.Api/Data/Entities/ChatAttachment.cs`
- Create: `api/Atrium.Api/Chat/ChatAttachmentService.cs`
- Create: `api/Atrium.Api/Data/Migrations/20261128171000_AddChatAttachments.cs` (and `.Designer.cs`)
- Modify: `api/Atrium.Api/Data/Entities/ChatThread.cs`
- Modify: `api/Atrium.Api/Data/Entities/ChatMessageEntity.cs`
- Modify: `api/Atrium.Api/Data/AppDbContext.cs`
- Modify: `api/Atrium.Api/Data/Migrations/AppDbContextModelSnapshot.cs`
- Modify: `api/Atrium.Api/Chat/ChatContracts.cs`
- Modify: `api/Atrium.Api/Endpoints/ChatEndpoints.cs`
- Modify: `api/Atrium.Api/Program.cs`
- Test: `api/Atrium.Api.Tests/AiConfigAndAttachmentsTests.cs`

**Interfaces:**
- Consumes: `IAttachmentStorage`, `IArtifactTextExtractor`, `IcsParser.Parse(string, string)`
- Produces:
  - `ChatAttachmentDto(Guid Id, string FileName, string ContentType, string Kind, long SizeBytes, string? Summary, string Url, DateTimeOffset CreatedAt)`
  - `ChatAttachmentService.UploadAsync(Guid userId, IFormFile file, string userTimezone, CancellationToken ct) -> Task<ChatAttachmentUploadResult>`
  - `ChatAttachmentService.BindToMessageAsync(Guid userId, Guid threadId, Guid messageId, IReadOnlyList<Guid> attachmentIds, CancellationToken ct) -> Task<IReadOnlyList<ChatAttachment>>`
  - `ChatAttachmentService.DeleteThreadFilesAsync(Guid userId, Guid threadId, CancellationToken ct) -> Task`
  - `ChatAttachmentService.BuildThreadManifestAsync(Guid userId, Guid threadId, CancellationToken ct) -> Task<string?>`
  - `ChatAttachmentService.QueryIcsAsync(Guid userId, Guid threadId, Guid attachmentId, string? query, string? startDate, string? endDate, int limit, string fallbackTimezone, CancellationToken ct) -> Task<string>` (JSON string)
  - `ChatAttachmentService.ReadAttachmentAsync(Guid userId, Guid threadId, Guid attachmentId, string? query, int offset, int maxChars, CancellationToken ct) -> Task<string>` (JSON string)
  - `ChatAttachmentService.LoadImageDataUrlAsync(Guid userId, ChatAttachment attachment, CancellationToken ct) -> Task<(string? DataUrl, long ByteLength)>`
  - Endpoints: `POST /api/chat/attachments`, `GET /api/chat/attachments/{id:guid}`.

- [ ] **Step 1: Write failing tests in `api/Atrium.Api.Tests/AiConfigAndAttachmentsTests.cs`**

Add tests:
- `ChatAttachments_UploadIcsTxtPng_AndQueryNeptunIcs`: uploads a multi-course Neptun `.ics` (`X-WR-TIMEZONE:Europe/Budapest`, 2 courses, 6 events), a `.txt` syllabus excerpt, and a minimal valid PNG; verifies DTO metadata, verifies disallowed `.exe` returns 400, verifies `QueryIcsAsync` with no query returns `distinctSummaries` and with `query: "Mesterséges"` returns only matching events, verifies `ReadAttachmentAsync` returns text excerpts, and verifies `GET /api/chat/attachments/{id}` returns 404 for another user.
- `ChatAttachments_DeleteThread_RemovesBackingFiles`: binds an attachment to a thread, calls `DELETE /api/chat/threads/{id}`, verifies the row is gone and `IAttachmentStorage.OpenReadAsync` returns `null`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test api/Atrium.sln --filter "AiConfigAndAttachmentsTests"`
Expected: FAIL.

- [ ] **Step 3: Add `ChatAttachment` entity, `AppDbContext` config, and migration**

- `ChatAttachment`: `Id`, `UserId`, `ThreadId?`, `MessageId?`, `FileName` (255), `ContentType` (120), `Kind` (24: `"image"`, `"ics"`, `"document"`), `SizeBytes`, `StorageKey` (500), `ExtractedText`, `MetadataJson`, `CreatedAt`.
- Add `ICollection<ChatAttachment> Attachments` on `ChatThread` and `ChatMessageEntity`; cascade delete on `ThreadId` and `MessageId`.

- [ ] **Step 4: Implement `ChatAttachmentService` and map `/api/chat/attachments` endpoints**

- Enforce allowlist (`.ics`, `.pdf`, `.docx`, `.pptx`, `.txt`, `.md`, `.csv`, `.json`, `.png`, `.jpg`, `.jpeg`, `.webp`) and 15 MB cap.
- Store under `chat/{userId:N}/{id:N}{ext}`.
- For `.ics`: parse with `IcsParser.Parse`, store JSON metadata `{ eventCount, dateRangeStart, dateRangeEnd, timezone, distinctSummaries: [{ summary, count }] }` (top 25 summaries).
- For docs: extract with `IArtifactTextExtractor.Extract`, store joined `[PageLabel]\nContent` capped at 500,000 chars and `{ pageCount, charCount }` in `MetadataJson`.
- For `BindToMessageAsync`: verify every requested `attachmentId` belongs to `userId` and is either unbound (`ThreadId == null`) or already on `threadId` (max 5 per message).
- Update `DELETE /api/chat/threads/{id}` in `ChatEndpoints.cs` to call `DeleteThreadFilesAsync` before removing the thread.
- Register `ChatAttachmentService` in `Program.cs`.

- [ ] **Step 5: Run tests and build**

Run: `dotnet build --no-restore -c Release -warnaserror api/Atrium.sln && dotnet test -c Release --no-build --filter "AiConfigAndAttachmentsTests"`
Expected: PASS, 0 warnings.

---

### Task 3: SSRF-Safe HTTP Fetcher, HTML-to-Markdown Converter, and `WebToolsService`

**Files:**
- Create: `api/Atrium.Api/Chat/SafeHttpFetcher.cs`
- Create: `api/Atrium.Api/Chat/HtmlMarkdownConverter.cs`
- Create: `api/Atrium.Api/Chat/WebToolsService.cs`
- Modify: `api/Atrium.Api/Program.cs`
- Test: `api/Atrium.Api.Tests/WebToolsTests.cs`

**Interfaces:**
- Consumes: `AiConfigService.GetWebSearchSettingsAsync`, `IArtifactTextExtractor`, `IcsParser`
- Produces:
  - `SafeHttpFetcher.FetchAsync(string url, int maxBytes, bool allowPrivateNetworks, IDictionary<string, string>? headers, HttpMethod? method, HttpContent? body, CancellationToken ct) -> Task<SafeFetchResult>`
  - `HtmlMarkdownConverter.Convert(string html) -> (string? Title, string Markdown)`
  - `WebToolsService.FetchUrlAsync(string url, int maxChars, CancellationToken ct) -> Task<string>` (JSON string `{ url, finalUrl, title, contentType, truncated, content }`)
  - `WebToolsService.WebSearchAsync(Guid userId, string query, int maxResults, CancellationToken ct) -> Task<string>` (JSON string `{ provider, query, results: [{ title, url, snippet }] }`)

- [ ] **Step 1: Write failing tests in `api/Atrium.Api.Tests/WebToolsTests.cs`**

Add tests:
- `SafeHttpFetcher_BlocksLoopbackPrivateLinkLocalAndRedirectToPrivateIp`: verifies `http://127.0.0.1/test`, `http://10.0.0.1/`, `http://169.254.169.254/latest`, `file:///etc/passwd`, and a public host redirecting 302 → `http://127.0.0.1/admin` are all rejected before connecting to private targets.
- `HtmlMarkdownConverter_PreservesSyllabusHeadingsAndTables_StripsScriptsAndNav`: feeds a BME-style HTML syllabus page with `<nav>`, `<script>`, `<h1>`, `<h2>`, bullet lists, and a weekly lecture `<table>` (`Week | Topic | Assessment`), asserts `<script>`/`<nav>` text is absent and Markdown table rows (`| Week | Topic | Assessment |`) are preserved.
- `WebToolsService_SearchProviders_ParseDuckDuckGoTavilyBraveAndSearxng`: uses mocked `HttpMessageHandler` to test all 4 search backends (including DuckDuckGo `uddg=` URL unwrapping and rate-limit fallback message).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test api/Atrium.sln --filter "WebToolsTests"`
Expected: FAIL.

- [ ] **Step 3: Implement `SafeHttpFetcher.cs`, `HtmlMarkdownConverter.cs`, and `WebToolsService.cs`**

- `SafeHttpFetcher`:
  - Named `HttpClient` (`WebToolsService.SafeHttpClientName`) configured with `AllowAutoRedirect = false`, `Timeout = TimeSpan.FromSeconds(15)`.
  - Injectable `Func<string, CancellationToken, Task<IPAddress[]>>` DNS resolver (defaults to `Dns.GetHostAddressesAsync`) for deterministic testing.
  - `IsBlockedIpAddress(IPAddress)`: handles IPv4-mapped IPv6 (`MapToIPv4()`), blocks `127.0.0.0/8`, `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `169.254.0.0/16`, `100.64.0.0/10`, `0.0.0.0/8`, multicast (`224.0.0.0/4`), IPv6 loopback (`::1`), link-local (`fe80::/10`), ULA (`fc00::/7`), multicast (`ff00::/8`).
  - Manual redirect loop (max 5 hops) re-validating every `Location` URI.
  - Stream-reads response up to `maxBytes` (2 MB for HTML/text, 10 MB for PDF/ICS).
- `HtmlMarkdownConverter`:
  - Zero-dependency HTML walker: strips `<script>`, `<style>`, `<noscript>`, `<svg>`, `<nav>`, `<footer>`, `<header>` (except headings inside `<main>`), extracts `<title>`, prefers `<main>`/`<article>` when present, renders `h1`–`h6`, `p`/`div`/`br`, `ul`/`ol`/`li`, `<a href>`, and `<table>` (`tr`/`th`/`td` → GFM Markdown table), decodes entities via `WebUtility.HtmlDecode`, collapses excess blank lines.
- `WebToolsService`:
  - `FetchUrlAsync`: dispatches by content type / extension (HTML → `HtmlMarkdownConverter`, `.ics` → `IcsParser` summary + first 50 events, `.pdf` → `IArtifactTextExtractor`, plain text → trimmed text), caps at `Math.Clamp(maxChars, 1000, 24_000)`.
  - `WebSearchAsync`: dispatches to `tavily`, `brave`, `searxng` (`allowPrivateNetworks: true` for user-configured SearXNG), or `duckduckgo` (`POST https://html.duckduckgo.com/html/`).
- Register `SafeHttpFetcher`, `WebToolsService`, and the `SafeHttpClientName` `HttpClient` in `Program.cs`.

- [ ] **Step 4: Run tests and build**

Run: `dotnet build --no-restore -c Release -warnaserror api/Atrium.sln && dotnet test -c Release --no-build --filter "WebToolsTests"`
Expected: PASS, 0 warnings.

---

### Task 4: Batch `create_activity_items` Write Tool (Chat + `/mcp`)

**Files:**
- Modify: `api/Atrium.Api/Knowledge/KnowledgeWriteService.cs`
- Modify: `api/Atrium.Api/Mcp/AtriumMcpTools.cs`
- Test: `api/Atrium.Api.Tests/KnowledgeAndMcpTests.cs`

**Interfaces:**
- Produces:
  - `BatchActivityItemInput(string Title, string? Theme, string? Notes, int? SequenceNumber, DateTimeOffset? StartAt, DateTimeOffset? EndAt, bool IsAllDay, string? Location)`
  - `BatchCreateActivityItemsResponse(Guid ActivityId, Guid CourseId, int CreatedCount, IReadOnlyList<UniversityActivityItemDto> Items)`
  - `KnowledgeWriteService.CreateActivityItemsBatchAsync(ClaimsPrincipal principal, bool enforceAgentRules, Guid activityId, IReadOnlyList<BatchActivityItemInput> items, CancellationToken ct) -> Task<KnowledgeResult<BatchCreateActivityItemsResponse>>`
  - MCP tool `create_activity_items` on `AtriumMcpTools` (19 total MCP tools).

- [ ] **Step 1: Write failing tests in `api/Atrium.Api.Tests/KnowledgeAndMcpTests.cs`**

- Update `tools/list` assertion to expect `19` tools (including `create_activity_items`).
- Add `McpWriteTools_CreateActivityItemsBatch_CreatesAllItemsWithCalendarEvents_AndEnforcesLimit`: creates a course + lecture activity, calls `create_activity_items` with 5 scheduled lectures, verifies all 5 items and 5 calendar events exist in DB, verifies `> 50` items returns an error, and verifies read-only PAT is denied `university:write`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test api/Atrium.sln --filter "KnowledgeAndMcpTests"`
Expected: FAIL.

- [ ] **Step 3: Implement `CreateActivityItemsBatchAsync` and `AtriumMcpTools.CreateActivityItemsAsync`**

- Validate 1–50 items, validate every item's title (`1–200` chars) and schedule (`startAt`/`endAt` pairing), ensure the user's University calendar exists once, add all `CalendarEvent` and `UniversityActivityItem` entities in a single `SaveChangesAsync`, and return `BatchCreateActivityItemsResponse`.

- [ ] **Step 4: Run tests and build**

Run: `dotnet build --no-restore -c Release -warnaserror api/Atrium.sln && dotnet test -c Release --no-build --filter "KnowledgeAndMcpTests"`
Expected: PASS, 0 warnings.

---

### Task 5: Wire Multi-Provider, Vision Routing, Attachments, Web & Batch Tools into `ChatEndpoints` and `OpenAiChatService`

**Files:**
- Modify: `api/Atrium.Api/Chat/ChatContracts.cs`
- Modify: `api/Atrium.Api/Endpoints/ChatEndpoints.cs`
- Modify: `api/Atrium.Api/Chat/OpenAiChatService.cs`
- Test: `api/Atrium.Api.Tests/ChatEndpointsTests.cs`

**Interfaces:**
- Consumes: `AiConfigService`, `ChatAttachmentService`, `WebToolsService`, `KnowledgeWriteService.CreateActivityItemsBatchAsync`
- Produces:
  - Updated `ChatStreamRequest(IReadOnlyList<ChatMessageInput> Messages, Guid? CourseId = null, Guid? ThreadId = null, Guid? ModelId = null, IReadOnlyList<Guid>? AttachmentIds = null)`
  - Updated `ChatMessageDto` (includes `IReadOnlyList<ChatAttachmentDto> Attachments`) and `ChatThreadDetailDto` (includes `Guid? LastModelId`)
  - Updated `OpenAiChatService.StreamChatAsync` accepting `Guid threadId`, `bool supportsVision`, `string userTimezone`, and `IReadOnlyDictionary<int, IReadOnlyList<ChatAttachment>> messageImages` (or loading persisted thread attachments by `threadId`).

- [ ] **Step 1: Write failing tests in `api/Atrium.Api.Tests/ChatEndpointsTests.cs`**

Add tests:
- `ChatStream_VisionAutoRouting_SwitchesForImageTurn_AndDowngradesHistoryForTurn2NonVision`:
  - Configures 1 text-only default model + 1 vision default model.
  - Turn 1 sends text + uploaded PNG attachment with text-only model selected → asserts upstream request goes to the vision model's URL/model with `image_url` content part, emits `"Switched to ..."` status SSE event, and `ChatThread.LastModelId` remains the text-only model.
  - Turn 2 sends text-only follow-up on the same thread → asserts upstream request goes to the text-only model with `[Image attached: ...]` text placeholder (no `image_url` part).
- `ChatStream_ImageWithoutVisionModel_ReturnsBadRequest`: sends image when no vision model is configured → asserts HTTP 400 with clear message.
- `ChatStream_CourseSetupTools_FetchUrlQueryIcsAndBatchCreate`: mocks LLM calling `fetch_url`, `query_chat_ics`, `create_course`, `create_activity`, and `create_activity_items` in sequence → asserts all tools execute, emit `mutation` SSE event (`domain: "university"`), and persist the created course + batch items.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test api/Atrium.sln --filter "ChatEndpointsTests"`
Expected: FAIL.

- [ ] **Step 3: Update `ChatContracts.cs` and `ChatEndpoints.cs`**

- In `POST /api/chat/stream`:
  - Resolve model via `AiConfigService.ResolveModelForChatAsync` (checking whether `request.AttachmentIds` includes any `Kind == "image"` attachment).
  - Create or load `ChatThread`, persist `thread.LastModelId = resolved.UserSelectedModelId`, persist user `ChatMessageEntity`, and bind `request.AttachmentIds` via `ChatAttachmentService.BindToMessageAsync`.
  - If `resolved.SwitchedToVision`, emit `{ type = "status", message = $"Switched to {resolved.Model.DisplayName} for image input…" }`.
  - Update `GET /api/chat/threads/{id}` to include `Attachments` on each `ChatMessageDto` and `LastModelId` on `ChatThreadDetailDto`.

- [ ] **Step 4: Update `OpenAiChatService.cs`**

- Bump `MaxToolRounds` to `16`.
- Inject `ChatAttachmentService` and `WebToolsService`.
- Append non-image thread attachment manifest to system prompt.
- For messages with image attachments:
  - When `supportsVision == true`: inline base64 `image_url` parts (current turn first, then recent turns up to 8 MB total budget; images beyond budget become `[Image attached: <filename>]`).
  - When `supportsVision == false`: represent historical images as `[Image attached: <filename>]` text annotations.
- Add tool schemas and dispatchers for `web_search`, `fetch_url`, `query_chat_ics`, `read_chat_attachment`, and `create_activity_items` (respecting `lockedCourseId` when course-scoped).
- Update `BuildSystemPrompt` with concise guidance on using `fetch_url`, `query_chat_ics`, `read_chat_attachment`, `web_search`, and `create_activity_items` for syllabus + calendar course setup.

- [ ] **Step 5: Run full backend test suite and Release build**

Run: `dotnet build --no-restore -c Release -warnaserror api/Atrium.sln && dotnet test -c Release --no-build api/Atrium.sln`
Expected: 0 warnings, 0 errors, all backend tests PASS.

---

### Task 6: Frontend Multi-Provider & Web Search Settings (`aiConfigClient.ts`, `AccountPage.tsx`)

**Files:**
- Create: `web/src/lib/aiConfigClient.ts`
- Modify: `web/src/types/atrium.ts`
- Modify: `web/src/components/AccountPage.tsx`
- Modify: `web/src/App.tsx`
- Modify: `web/src/styles.css`

**Interfaces:**
- Produces:
  - Types in `web/src/types/atrium.ts`: `AiProvider`, `AiModel`, `WebSearchProviderKind`, `WebSearchConfig`, `AiConfig`, `ChatAttachment`
  - `web/src/lib/aiConfigClient.ts`: `getAiConfig`, `createAiProvider`, `updateAiProvider`, `deleteAiProvider`, `discoverProviderModels`, `createAiModel`, `updateAiModel`, `deleteAiModel`, `updateAiDefaults`
  - `AccountPage` receives `aiConfig: AiConfig | null` and `onAiConfigChange: (config: AiConfig) => void` (replaces single-URL AI form).

- [ ] **Step 1: Add TypeScript types to `web/src/types/atrium.ts` and create `web/src/lib/aiConfigClient.ts`**

- Add `ChatAttachment` (`id`, `fileName`, `contentType`, `kind`, `sizeBytes`, `summary`, `url`, `createdAt`) and extend `ChatMessage` with `attachments?: ChatAttachment[]` and `ChatThreadDetail` with `lastModelId?: string | null`.
- Implement all `/api/ai/*` wrappers using `apiFetch`.

- [ ] **Step 2: Build the Multi-Provider, Models, Defaults, and Web Search UI in `web/src/components/AccountPage.tsx`**

- **Providers list:** Add/edit/delete providers (Name, Base URL, API key input with saved/masked badge + clear option).
- **Models per provider:** Add model manually (Model ID, Display Name, `Vision` checkbox), **"Fetch models"** button calling `discoverProviderModels` to pick from returned model IDs, toggle `Vision` inline, delete model.
- **Defaults:** Select dropdowns for **Default Model** (all models) and **Default Vision Model** (vision-enabled models only, with `"None"` option).
- **Web Search:** Select dropdown (`DuckDuckGo (Built-in, no key)`, `Tavily`, `Brave Search`, `SearXNG`), conditional Base URL input (for SearXNG) and API key input (for Tavily/Brave/optional SearXNG).
- Load `aiConfig` in `App.tsx` alongside preferences, and compute `isChatConfigured` from `(aiConfig?.models.length ?? 0) > 0 || Boolean(preferences.aiInferenceUrl && preferences.aiModel)`.

- [ ] **Step 3: Verify TypeScript compilation and production build**

Run: `npm --prefix web run typecheck && npm --prefix web run build`
Expected: 0 TS errors, clean Vite build.

---

### Task 7: Frontend Chat Model Picker & Persisted File Attachments (`chatClient.ts`, `ChatSidebar.tsx`, `App.tsx`, `styles.css`)

**Files:**
- Modify: `web/src/lib/chatClient.ts`
- Modify: `web/src/components/ChatSidebar.tsx`
- Modify: `web/src/App.tsx`
- Modify: `web/src/styles.css`

**Interfaces:**
- Consumes: `AiConfig`, `AiModel`, `ChatAttachment` from Task 6
- Produces:
  - `uploadChatAttachment(file: File): Promise<ChatAttachment>` and `fetchChatAttachmentBlobUrl(id: string): Promise<string>` in `web/src/lib/chatClient.ts`
  - Extended `StreamChatOptions` with `modelId?: string | null` and `attachmentIds?: string[]`
  - `ChatSidebar` props extended with `models: AiModel[]`, `selectedModelId: string | null`, `defaultVisionModelId: string | null`, `onSelectModel: (modelId: string) => void`, `onSend: (prompt: string, attachments: ChatAttachment[]) => void`.

- [ ] **Step 1: Extend `web/src/lib/chatClient.ts`**

- Add `uploadChatAttachment(file: File)` posting `FormData` via `authenticatedRawFetch('/api/chat/attachments', { method: 'POST', body: formData })`.
- Add `downloadChatAttachment(id: string, fileName: string)` and `fetchChatAttachmentObjectUrl(id: string)` using `authenticatedRawFetch` so image thumbnails render securely with JWT auth.
- Pass `modelId` and `attachmentIds` in `streamChat`'s JSON payload.

- [ ] **Step 2: Add Model Picker and Attachment Controls to `web/src/components/ChatSidebar.tsx`**

- **Model Picker:** Compact `<select>` in the composer toolbar listing all configured models (`DisplayName` + provider name, with `[Vision]` / eye indicator on vision models). When an image is pending and the selected model isn't vision-capable, show a subtle hint (`"Will use <DefaultVisionModel> for images"` or warning if none configured).
- **Attachments:**
  - Hidden `<input type="file" multiple accept=".ics,.pdf,.docx,.pptx,.txt,.md,.csv,.json,.png,.jpg,.jpeg,.webp" />` triggered by a paperclip button.
  - Drag-and-drop handler (`onDragOver` / `onDrop`) on the chat drawer + `onPaste` handler on the composer textarea for pasted screenshots.
  - Pending attachment strip above the textarea showing upload progress, thumbnails for images, file icons + size for docs/ICS, and remove (`×`) buttons (max 5 files per message).
  - Message bubble attachment strip rendering persisted attachments (authenticated thumbnail preview for images; clickable download chip for `.ics`/docs).

- [ ] **Step 3: Wire `selectedModelId`, `attachments`, and `aiConfig` in `web/src/App.tsx` + add styles in `web/src/styles.css`**

- Initialize `selectedModelId` from `aiConfig.defaultModelId`; restore from `detail.lastModelId` when selecting a thread in Chat History.
- Pass `modelId` and `attachmentIds` in `handleSendChatMessage` and attach the uploaded `ChatAttachment[]` to the optimistic user message.
- Style `.chat-model-picker`, `.chat-attach-btn`, `.chat-pending-attachments`, `.chat-attachment-chip`, `.chat-attachment-thumb`, `.chat-drop-overlay`, and `.ai-provider-card` in `web/src/styles.css`.

- [ ] **Step 4: Run full end-to-end verification**

Run:
```bash
dotnet build --no-restore -c Release -warnaserror api/Atrium.sln && \
dotnet test -c Release --no-build api/Atrium.sln && \
npm --prefix web run typecheck && \
npm --prefix web run build && \
npm run check:suppressions
```
Expected: 0 warnings, 0 errors, all backend tests pass, clean frontend build, 0 undocumented suppressions.

