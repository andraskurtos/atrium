# AI Chat Sidebar & OpenAI-Compatible Inference — Design Spec

**Date:** 2026-11-28  
**Status:** Draft (pending user approval)  
**Builds on:** `KnowledgeReadService`, Artifact Text Extraction, `UserPreferences`, `AccountPage`, `UniversityPage`, `TopBar`

---

## 1. Intent & Why

Give Atrium a built-in, collapsible right-hand AI assistant sidebar powered by the user's own OpenAI-compatible inference endpoint (OpenAI, OpenRouter, Ollama, LM Studio, vLLM, etc.). From the Today page (or anywhere in the app), the user can ask about their schedule, checklist, courses, notes, and uploaded study artifacts. From the University page, the user can click **"Ask about this course"** on any course to open the sidebar scoped strictly to that course's notes and artifacts.

---

## 2. Goals, Non-Goals & Anti-Goals

### Goals
1. **User AI Inference Settings (`UserPreferences` + `AccountPage`):**
   - Configure **Inference Base URL** (e.g. `https://api.openai.com/v1` or `http://localhost:11434/v1`), **Model Name** (e.g. `gpt-4o-mini`, `qwen2.5:14b`), and **API Key** (optional for local Ollama/LM Studio; encrypted at rest with AES-256-GCM derived from `AuthOptions.JwtSigningKey`, never returned in plaintext — only `hasAiApiKey: boolean` + masked hint).
   - Provide a **Clear key** action and inline validation (HTTP/HTTPS URL only).
2. **Backend OpenAI Tool-Calling Chat Endpoint (`POST /api/chat/stream`):**
   - Authenticated with `HumanJwtOnlyPolicy` (the human user talking to their own assistant; sees all of their own records, matching REST behavior).
   - Sends OpenAI-compatible `POST {baseUrl}/chat/completions` requests with function-calling tools backed directly by `KnowledgeReadService`.
   - **Global mode (`courseId == null`):** exposes read-only tools `get_day`, `list_events`, `list_courses`, `get_course`, `list_notes_tree`, `read_note`, `list_artifacts`, `read_artifact_text`, and `search_knowledge`.
   - **Course-scoped mode (`courseId != null`):** enforces `courseId` server-side across every tool call — exposes only `get_course` (pinned to `courseId`), `search_knowledge` (pinned to `courseId`), `read_note` (verified to belong to `courseId`), `list_artifacts` (verified to belong to `courseId` or an item of `courseId`), and `read_artifact_text` (verified to belong to `courseId`). The model cannot read outside the selected course even if prompted.
   - Runs up to 6 tool-call rounds per turn, then streams the final assistant response back to the browser over SSE (`text/event-stream`) with events: `status` (e.g. `"Searching notes..."`), `delta` (text token), `citations` (structured source links), `done`, and `error`.
   - Graceful fallback: if the upstream model/provider does not support OpenAI tool-calling (returns `400`/`404` on `tools`), automatically falls back to a single-shot RAG context injection using `KnowledgeReadService.SearchKnowledgeAsync` + `GetCourseAsync` (in course mode) so even simple local models work.
3. **Collapsible Right-Hand Chat Sidebar (`ChatSidebar.tsx`):**
   - Toggled from a compact button in `TopBar` (and keyboard shortcut `Ctrl/Cmd+Shift+L` or similar) across all authenticated views; slides smoothly from the right without destroying page state.
   - **In-memory session threads:** keeps separate in-memory message history for Global chat vs each Course-scoped chat during the browser session, with a **New chat** button and a scope badge (e.g. `All Atrium` vs `Course: CS410 · Compilers` with a one-click `×` to switch back to Global).
   - Renders assistant replies in Markdown (reusing `react-markdown` + `remark-gfm` + `remark-math` + `rehype-katex` already in `web`) and shows clickable **source chips** (notes open directly in Notes; artifacts trigger download/open).
   - If the user hasn't configured an Inference URL/Model yet, shows a clean empty-state prompt with a button straight to **Account → AI Assistant Settings**.
4. **University Course Integration (`UniversityPage.tsx`):**
   - Add an **"Ask about this course"** button in the course detail header (and quick action on course cards) that opens `ChatSidebar` locked to that `courseId` and course title.

### Non-Goals
- No database persistence of chat history (explicitly in-memory per browser session).
- No write/mutation tools in chat yet (strictly read-only Q&A).
- No multi-provider SDKs (Anthropic/Gemini native formats); uses the universal OpenAI-compatible Chat Completions HTTP API.

### Anti-Goals
- **Never return the decrypted API key** in any API response or log it.
- **Never allow course-scoped chat to leak data** from other courses, general notes, or calendar events (enforce `courseId` ownership server-side in every tool handler, not just via system prompt).
- **Never hang indefinitely** on unresponsive inference servers (use `HttpClient` timeouts and respect `CancellationToken` on client abort).

---

## 3. Approaches Considered

### Approach 1: Backend-Proxied OpenAI Chat Completions + Server-Side Tool Calling *(Selected)*
- **How it works:** `Atrium.Api` stores encrypted API key + inference URL + model in `UserPreferences`, exposes `POST /api/chat/stream` (SSE), runs the OpenAI `/chat/completions` tool-calling loop against `KnowledgeReadService`, and streams tokens/citations to `ChatSidebar`.
- **Tradeoffs:** Small backend addition (~3 files + migration), zero browser CORS issues with Ollama/LM Studio/OpenAI, API key never stored in `localStorage`, and strict server-side `courseId` enforcement.

### Approach 2: Browser-Direct OpenAI Calls
- **How it works:** Store API key in `localStorage`, call `{baseUrl}/chat/completions` from React, pre-stuff search hits from `/api/knowledge/search`.
- **Tradeoffs:** Simpler backend, but fails with CORS on many OpenAI-compatible endpoints, exposes API key to browser storage, and lacks multi-step tool calling.

---

## 4. Data Model & API Contracts

### 4.1 `UserPreferences` Extensions (`user_preferences` table)
Add three nullable columns via EF migration `AddUserAiSettings`:
- `ai_inference_url` (`varchar(500)`, nullable) — normalized without trailing slash (e.g. `https://api.openai.com/v1`)
- `ai_model` (`varchar(120)`, nullable) — model identifier (e.g. `gpt-4o-mini`)
- `ai_api_key_encrypted` (`text`, nullable) — base64 `"v1:<nonce>:<ciphertext>:<tag>"` encrypted with AES-256-GCM

### 4.2 Updated `PreferencesDto` & `UpdatePreferencesRequest`
- `PreferencesDto` adds: `AiInferenceUrl` (`string?`), `AiModel` (`string?`), `HasAiApiKey` (`bool`).
- `UpdatePreferencesRequest` adds optional: `AiInferenceUrl`, `AiModel`, `AiApiKey` (only updates stored key when non-null/non-empty), `ClearAiApiKey` (`bool = false`).

### 4.3 Chat Endpoint (`POST /api/chat/stream`)
- Request JSON:
  ```json
  {
    "messages": [{ "role": "user" | "assistant", "content": "..." }],
    "courseId": "optional-guid"
  }
  ```
- Validation: 1–40 messages, max 16,000 chars per message; verifies `courseId` belongs to the user if supplied; verifies `AiInferenceUrl` and `AiModel` are configured (returns `400` with friendly message if not).
- Response: `text/event-stream` (`data: {"type":"status"|"delta"|"citations"|"done"|"error",...}\n\n`).

---

## 5. What's Left to the Builder

- Exact AES-GCM helper (`SecretProtector`) using `HKDF.DeriveKey(HashAlgorithmName.SHA256, ...)` from `AuthOptions.JwtSigningKey`.
- Exact OpenAI SSE stream parsing and tool-call JSON argument deserialization in `OpenAiChatService` (with `IHttpClientFactory` + injectable `HttpMessageHandler` for deterministic unit/integration tests without real network calls).
- Exact right-hand drawer width/responsive behavior (400px docked/overlay drawer on desktop, full-width sheet on mobile) matching Atrium's warm linen/obsidian design tokens.

