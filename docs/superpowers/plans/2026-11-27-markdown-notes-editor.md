# Atrium Markdown Notes Editor (PR 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a fullscreen **Notes** tab to Atrium with an Obsidian-style Live Preview Markdown editor (CodeMirror 6), Source mode, and sanitized Reading mode supporting headings, bold, italics, blockquotes, horizontal rules, links, and URL images.

**Architecture:** Use a single CodeMirror 6 `EditorView` with a dynamic `Compartment` toggling our `StateField<DecorationSet>` Live Preview extension between Live Preview and Source modes (preserving undo history and cursor position), paired with `react-markdown` (no raw HTML) for Reading mode and a shared URL sanitizer. Persist the PR 1 scratchpad draft in `localStorage` with zero backend changes.

**Tech Stack:** React 19, TypeScript (strict), Vite PWA, CodeMirror 6 (`@codemirror/state`, `@codemirror/view`, `@codemirror/commands`, `@codemirror/language`, `@codemirror/lang-markdown`, `@lezer/highlight`, `@lezer/markdown`), `react-markdown`.

**Spec:** `docs/superpowers/specs/2026-11-27-markdown-notes-editor-design.md`

## Global Constraints
- Branch target: `feature/markdown-notes-editor` (branched from `main`).
- Zero backend changes in PR 1; existing `dotnet test -c Release` suite must remain green.
- All added npm packages must be MIT licensed.
- Must pass `npm run check:suppressions`, `npm --prefix web run typecheck`, and `npm --prefix web run build` with 0 errors.
- Pure CommonMark only (no underline, no raw HTML execution).
- `Cmd/Ctrl+K` remains globally reserved for Atrium's Command Palette; inserting a link uses `Cmd/Ctrl+Shift+K`, and toggling Live Preview ↔ Reading uses `Cmd/Ctrl+E` (only when `activeView === 'notes'`).

## Review Focus
- **XSS / unsafe URL schemes** (`javascript:alert(1)`, `data:text/html,...`, `vbscript:...`, malformed URLs) in both Live Preview link/image widgets and Reading mode: must be neutralized by `sanitizeMarkdownUrl` — pinned in Task 1 & Task 2/3.
- **Line-spanning block decorations in CodeMirror 6** (horizontal rules and images): must be provided from a `StateField<DecorationSet>` (not a `ViewPlugin`) so CodeMirror never throws `Decorations that replace line breaks may not be specified via plugins` — pinned in Task 2.
- **Lossless mode switching (Live Preview ↔ Source ↔ Reading):** switching modes must never alter the raw Markdown string, and switching Live Preview ↔ Source must preserve editor undo history and cursor position — pinned in Task 3.
- **Broken or blocked image URLs:** must render a graceful fallback caption instead of a broken browser icon or layout collapse — pinned in Task 2 & Task 3.
- **Mobile 4-tab layout & keyboard shortcut isolation:** `.mobile-tabbar` must update to 4 columns (`repeat(4, minmax(0, 1fr))`), and `Cmd/Ctrl+E` must not fire outside the Notes view — pinned in Task 4.

---

## File Structure

- **Dependencies & Utilities (Task 1)**
  - Modify `web/package.json` & `web/package-lock.json`
  - Create `web/src/lib/markdownUtils.ts`
- **CodeMirror 6 Live Preview & Commands (Task 2)**
  - Create `web/src/lib/markdownLivePreview.ts`
  - Create `web/src/lib/markdownCommands.ts`
- **Editor & Fullscreen Notes Page (Task 3)**
  - Create `web/src/components/MarkdownEditor.tsx`
  - Create `web/src/components/NotesPage.tsx`
- **App Navigation & Atelier Styling (Task 4)**
  - Modify `web/src/types/atrium.ts`
  - Modify `web/src/components/Icons.tsx`
  - Modify `web/src/components/TopBar.tsx`
  - Modify `web/src/components/MobileTabBar.tsx`
  - Modify `web/src/App.tsx`
  - Modify `web/src/styles.css`

---

### Task 1: Install CodeMirror 6 + `react-markdown` & Add Markdown Utilities

**Files:**
- Modify: `web/package.json`
- Create: `web/src/lib/markdownUtils.ts`

**Interfaces:**
- Produces in `web/src/lib/markdownUtils.ts`:
  - `NOTES_DRAFT_STORAGE_KEY = 'atrium.notes.pr1.draft'`
  - `DEFAULT_SAMPLE_NOTE: string` (showcasing H1–H3, bold, italic, blockquote, horizontal rule, link, and image)
  - `sanitizeMarkdownUrl(rawUrl: string, kind: 'link' | 'image'): string | null`
  - `countWordsAndChars(markdown: string): { words: number; chars: number }`
  - `loadNotesDraft(): string` / `saveNotesDraft(content: string): void`

- [ ] **Step 1: Install MIT packages in `web/`**

Run:
```bash
npm --prefix web install @codemirror/state @codemirror/view @codemirror/commands @codemirror/language @codemirror/lang-markdown @lezer/highlight @lezer/markdown react-markdown
```
Expected: Packages install cleanly with 0 peer dependency errors.

- [ ] **Step 2: Implement `web/src/lib/markdownUtils.ts`**

Implement `sanitizeMarkdownUrl(rawUrl, kind)` using `new URL(rawUrl.trim())`:
- For `'link'`: allow only `http:`, `https:`, `mailto:`.
- For `'image'`: allow only `http:`, `https:`.
- Return `parsed.href` on match, `null` otherwise (rejecting `javascript:`, `data:`, `vbscript:`, relative/malformed schemes).
Implement `countWordsAndChars`, `loadNotesDraft`, `saveNotesDraft`, and `DEFAULT_SAMPLE_NOTE`.

- [ ] **Step 3: Verify TypeScript compilation**

Run: `npm --prefix web run typecheck`
Expected: PASS (0 errors).

- [ ] **Step 4: Commit**

```bash
git add web/package.json web/package-lock.json web/src/lib/markdownUtils.ts
git commit -m "feat(notes): add CodeMirror 6 and react-markdown dependencies with URL sanitizer"
```

---

### Task 2: CodeMirror 6 Live Preview Extension & Formatting Commands

**Files:**
- Create: `web/src/lib/markdownLivePreview.ts`
- Create: `web/src/lib/markdownCommands.ts`

**Interfaces:**
- Consumes: `sanitizeMarkdownUrl` from `web/src/lib/markdownUtils.ts`
- Produces:
  - `web/src/lib/markdownLivePreview.ts`:
    - `markdownLivePreviewExtension: Extension` (`StateField<DecorationSet>` + `EditorView.domEventHandlers` for `Cmd/Ctrl+Click` link opening)
    - `atriumEditorTheme: Extension` (CodeMirror 6 `EditorView.theme` referencing Atrium CSS variables)
    - `atriumMarkdownHighlight: Extension` (`syntaxHighlighting` for Source & active-node Markdown tokens)
  - `web/src/lib/markdownCommands.ts`:
    - `toggleBold: Command`
    - `toggleItalic: Command`
    - `setHeading(level: 1 | 2 | 3): Command`
    - `toggleBlockquote: Command`
    - `insertLink: Command`
    - `insertImage: Command`
    - `insertHorizontalRule: Command`
    - `markdownFormattingKeymap: Extension` (`Mod-b`, `Mod-i`, `Mod-Shift-k`)

- [ ] **Step 1: Create `web/src/lib/markdownLivePreview.ts`**

Implement `buildLivePreviewDecorations(state: EditorState): DecorationSet` inside a `StateField<DecorationSet>` (recomputing on `tr.docChanged || tr.selection` via `syntaxTree(state).iterate(...)`):
- Helper `isRangeActive(state, from, to)`: returns `true` if any `r` in `state.selection.ranges` satisfies `r.from <= to && r.to >= from`.
- **Headings (`ATXHeading1`–`ATXHeading6`):** Apply line class `cm-md-line-h1`..`cm-md-line-h6`; when inactive, replace leading `HeaderMark` (plus single trailing space if present) with `Decoration.replace({})`.
- **Bold (`StrongEmphasis`):** Apply `Decoration.mark({ class: 'cm-md-strong' })`; when inactive, hide opening/closing `EmphasisMark`.
- **Italics (`Emphasis`):** Apply `Decoration.mark({ class: 'cm-md-em' })`; when inactive, hide opening/closing `EmphasisMark`.
- **Blockquotes (`Blockquote`):** Apply line class `cm-md-blockquote` to every line in the blockquote; when the line is inactive, hide leading `QuoteMark` (plus single trailing space if present).
- **Horizontal rules (`HorizontalRule`):** When inactive, replace `[node.from, node.to]` with `Decoration.replace({ widget: new HorizontalRuleWidget() })`.
- **Links (`Link`):** Extract URL from child `URL` node and sanitize via `sanitizeMarkdownUrl(url, 'link')`. Style label range with `Decoration.mark({ class: safeUrl ? 'cm-md-link' : 'cm-md-link-invalid', attributes: safeUrl ? { 'data-href': safeUrl, title: 'Cmd/Ctrl+Click to open link' } : {} })`. When inactive, hide `[` and `](url)` spans. Handle `mousedown` with `event.metaKey || event.ctrlKey` on `.cm-md-link` to open `data-href` via `window.open(href, '_blank', 'noopener,noreferrer')`.
- **Images (`Image`):** Extract `alt` and `url` from `![alt](url)`, sanitize via `sanitizeMarkdownUrl(url, 'image')`. When inactive, replace `[node.from, node.to]` with `Decoration.replace({ widget: new ImageWidget(alt, safeUrl) })`; when active, keep syntax visible AND add `Decoration.widget({ widget: new ImageWidget(alt, safeUrl), side: 1, block: true })` at line end so user sees the live preview while editing. `ImageWidget` renders a rounded responsive `<img>` with `onerror` fallback caption.
- Sort/add decorations in document order (`RangeSetBuilder` or `Decoration.set(..., true)`).

- [ ] **Step 2: Create `web/src/lib/markdownCommands.ts`**

Implement `toggleBold`, `toggleItalic`, `setHeading(1|2|3)`, `toggleBlockquote`, `insertLink`, `insertImage`, `insertHorizontalRule`, and `markdownFormattingKeymap` (`Mod-b`, `Mod-i`, `Mod-Shift-k`).

- [ ] **Step 3: Verify TypeScript compilation**

Run: `npm --prefix web run typecheck`
Expected: PASS (0 errors).

- [ ] **Step 4: Commit**

```bash
git add web/src/lib/markdownLivePreview.ts web/src/lib/markdownCommands.ts
git commit -m "feat(notes): add CodeMirror 6 Live Preview decoration plugin and formatting commands"
```

---

### Task 3: `MarkdownEditor.tsx` & Fullscreen `NotesPage.tsx`

**Files:**
- Create: `web/src/components/MarkdownEditor.tsx`
- Create: `web/src/components/NotesPage.tsx`

**Interfaces:**
- Consumes: `markdownLivePreview.ts`, `markdownCommands.ts`, `markdownUtils.ts`, `react-markdown`
- Produces:
  - `NotesMode = 'live' | 'reading' | 'source'`
  - `MarkdownEditorHandle`: `{ runCommand: (cmd: Command) => void; focus: () => void }`
  - `NotesPage` props: `{ onBackToDashboard: () => void; onAnnounce: (message: string) => void }`

- [ ] **Step 1: Create `web/src/components/MarkdownEditor.tsx`**

Wrap CodeMirror 6 `EditorView` in a `forwardRef` (or `useImperativeHandle`) React component:
- Uses a `Compartment` for `livePreviewCompartment` so toggling `mode: 'live' | 'source'` calls `view.dispatch({ effects: livePreviewCompartment.reconfigure(mode === 'live' ? markdownLivePreviewExtension : []) })` without recreating the editor (preserving undo stack and cursor).
- Syncs external `value` resets (e.g., `Reset sample`) only when `value !== view.state.doc.toString()`.
- Calls `onChange(update.state.doc.toString())` on `update.docChanged`.
- Includes `history()`, `historyKeymap`, `defaultKeymap`, `markdown()`, `EditorView.lineWrapping`, `atriumEditorTheme`, `atriumMarkdownHighlight`, and `markdownFormattingKeymap`.

- [ ] **Step 2: Create `web/src/components/NotesPage.tsx`**

Build the fullscreen Notes workspace:
- State: `content` (initialized via `loadNotesDraft()`), `mode` (`'live' | 'reading' | 'source'`, default `'live'`).
- Auto-saves `content` to `localStorage` via `saveNotesDraft(content)` on change.
- Keyboard shortcut: `Cmd/Ctrl+E` toggles `'live' <-> 'reading'` (and `'source' -> 'reading'`).
- Header bar:
  - Segmented mode switcher: `Live Preview` | `Reading` | `Source` (`role="tablist"` or `role="radiogroup"`).
  - Compact formatting toolbar (rendered when `mode !== 'reading'`): `H1`, `H2`, `H3`, `Bold`, `Italic`, `Quote`, `Link`, `Image`, `HR` buttons calling `editorRef.current?.runCommand(...)`.
  - Subtle `Reset sample` button (with inline confirm or immediate reset + `onAnnounce`).
- Main canvas:
  - When `mode === 'reading'`: renders `<ReactMarkdown>` with custom `a` (sanitized via `sanitizeMarkdownUrl(..., 'link')`, `target="_blank" rel="noopener noreferrer"`, or plain `<span>` if unsafe) and `img` (sanitized via `sanitizeMarkdownUrl(..., 'image')` with broken-image fallback component) inside `.notes-reading-prose`.
  - When `mode === 'live' || mode === 'source'`: renders `<MarkdownEditor>`.
- Bottom status bar: `{words} words · {chars} characters` + mode hint (`Cmd/Ctrl+E to toggle Reading mode`).

- [ ] **Step 3: Verify TypeScript compilation**

Run: `npm --prefix web run typecheck`
Expected: PASS (0 errors).

- [ ] **Step 4: Commit**

```bash
git add web/src/components/MarkdownEditor.tsx web/src/components/NotesPage.tsx
git commit -m "feat(notes): add MarkdownEditor and fullscreen NotesPage with Live Preview, Reading, and Source modes"
```

---

### Task 4: Wire `Notes` Tab into App Navigation & Atelier Styles

**Files:**
- Modify: `web/src/types/atrium.ts`
- Modify: `web/src/components/Icons.tsx`
- Modify: `web/src/components/TopBar.tsx`
- Modify: `web/src/components/MobileTabBar.tsx`
- Modify: `web/src/App.tsx`
- Modify: `web/src/styles.css`

**Interfaces:**
- Consumes: `NotesPage` from `web/src/components/NotesPage.tsx`
- Produces: Complete fullscreen Notes tab integrated into Atrium's top bar, mobile tab bar, command palette, URL hash (`#notes`), and Atelier light/dark themes.

- [ ] **Step 1: Update `atrium.ts`, `Icons.tsx`, `TopBar.tsx`, `MobileTabBar.tsx`, and `App.tsx`**

- `types/atrium.ts`: Add `'notes'` to `AppView` and `MobileTab`.
- `Icons.tsx`: Add a clean `NoteIcon` SVG (16×16 stroke icon matching `GraduationCapIcon`/`CalendarIcon`).
- `TopBar.tsx`: Add `Notes` breadcrumb label and `Notes` navigation pill next to `University` (`#topbar-notes-btn`).
- `MobileTabBar.tsx`: Add 4th tab `{ id: 'notes', label: 'Notes', icon: NoteIcon }`.
- `App.tsx`:
  - Recognize `#notes` in `readInitialView()`, `hashchange`, and `navigateTo('notes')`.
  - Handle `tab === 'notes'` in `handleSelectMobileTab`.
  - Add `Open Notes Editor` command to `CommandPalette`.
  - Render `<NotesPage>` + `<MobileTabBar activeTab="notes" />` when `activeView === 'notes'`.

- [ ] **Step 2: Add Atelier light/dark styles in `web/src/styles.css`**

- Update `.mobile-tabbar` to `grid-template-columns: repeat(4, minmax(0, 1fr))`.
- Style `.notes-page-shell`, `.notes-toolbar-bar`, `.notes-mode-switcher`, `.notes-format-toolbar`, `.notes-canvas`, `.notes-statusbar`, CodeMirror Live Preview classes (`.cm-md-line-h1`..`.cm-md-line-h6`, `.cm-md-strong`, `.cm-md-em`, `.cm-md-blockquote`, `.cm-md-hr`, `.cm-md-link`, `.cm-md-image-figure`, `.cm-md-image-fallback`), and matching `.notes-reading-prose` typography so Live Preview and Reading modes look visually cohesive in both light and dark themes.

- [ ] **Step 3: Run full CI verification suite**

Run:
```bash
npm run check:suppressions && npm --prefix web run typecheck && npm --prefix web run build && dotnet test -c Release --no-build api/Atrium.sln
```
Expected:
- Suppression audit passes (0 undocumented suppressions).
- TypeScript `tsc -b --noEmit` passes (0 errors).
- Vite PWA production build succeeds.
- Backend test suite remains 85/85 passing.

- [ ] **Step 4: Commit**

```bash
git add web/src/types/atrium.ts web/src/components/Icons.tsx web/src/components/TopBar.tsx web/src/components/MobileTabBar.tsx web/src/App.tsx web/src/styles.css
git commit -m "feat(notes): wire fullscreen Notes tab into navigation and Atelier styling"
```

