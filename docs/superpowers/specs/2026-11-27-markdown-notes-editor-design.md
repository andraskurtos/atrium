# Atrium Markdown Notes Editor (PR 1) Design

**Date:** 2026-11-27
**Status:** Draft (pending approval)
**Branch target:** `feature/markdown-notes-editor` (branched from `main`)

---

## 1. Intent & Why

Atrium's long-term goal is a synced, Markdown-based personal knowledge base (Obsidian/Notion-style) that AI agents can query and a future desktop app can sync directly with local `.md` files. Because that spans editor UX, server storage, folder/note management, agent APIs, and local-first sync, we are building it incrementally across focused PRs.

**PR 1** establishes the core writing and reading experience: a dedicated fullscreen **Notes** tab featuring an **Obsidian-style Live Preview** Markdown editor, **Reading** mode, and **Source** mode — with zero backend changes and no multi-note management yet.

---

## 2. Goals, Non-Goals, & Anti-Goals

### Goals
- Add a top-level **Notes** tab accessible from:
  - `TopBar` navigation pill (next to `University`)
  - `MobileTabBar` (4-tab mobile bottom bar: `Day`, `Calendar`, `University`, `Notes`)
  - `CommandPalette` (`Open Notes Editor`)
  - URL hash `#notes` (preserving view on refresh and browser Back/Forward)
- Fullscreen, distraction-free Markdown workspace styled to match Atrium's Atelier light and dark themes (centered readable measure around `760px` max-width on wide screens, responsive on mobile).
- Three seamlessly switchable view modes in the Notes header bar:
  1. **Live Preview** *(default)* — Obsidian-style single canvas powered by CodeMirror 6. Raw Markdown is edited directly; formatting syntax hides when the cursor/selection is outside a construct and reveals immediately when the cursor enters it.
  2. **Reading** — Pure rendered read-only Markdown view styled with matching typography, where links open directly in a new tab (`target="_blank" rel="noopener noreferrer"`).
  3. **Source** — Raw Markdown in CodeMirror 6 with syntax highlighting and visible markers.
- Supported Markdown elements for PR 1 (100% standard CommonMark — **no underline**):
  - **Headings** (`#` through `######`, ATX headings)
  - **Bold** (`**text**` / `__text__`)
  - **Italics** (`*text*` / `_text_`)
  - **Blockquotes** (`> quote`)
  - **Horizontal rules** (`---`, `***`, `___`)
  - **Links** (`[label](url)`)
  - **Images** (`![alt](url)`)
- Compact formatting toolbar (shown in Live Preview and Source modes) + keyboard shortcuts:
  - `Cmd/Ctrl+B` — toggle bold (`**...**`)
  - `Cmd/Ctrl+I` — toggle italic (`*...*`)
  - `Cmd/Ctrl+Shift+K` — insert/wrap link (`[text](https://)`) *(keeps `Cmd/Ctrl+K` reserved globally for Atrium's Command Palette)*
  - `Cmd/Ctrl+E` — toggle between **Live Preview** and **Reading** mode (matching Obsidian)
  - Toolbar buttons: Heading (`H1`, `H2`, `H3`), Bold, Italic, Quote, Link, Image, Horizontal Rule.
- Temporary client-side draft persistence via `localStorage` (`atrium.notes.pr1.draft`) so refreshing doesn't lose text while testing, pre-populated on first open with a concise welcome/showcase Markdown note demonstrating all 7 supported elements, plus a subtle word/character count in the footer.

### Non-Goals (Deferred to Later PRs)
- Backend database tables, API endpoints, server saving, or multi-note / folder / vault management.
- Image file uploads, clipboard image pasting, or local attachment storage (URL-based images `![alt](https://...)` only in PR 1).
- Extended Markdown constructs beyond the 7 basics (wikilinks `[[Note]]`, code blocks, tables, task lists, footnotes, LaTeX, frontmatter, callouts, underline).
- Agent read/write access to the knowledge base or desktop sync protocol.

### Anti-Goals
- Mutating or reformatting the user's raw Markdown text on mode switches (Live Preview ↔ Source ↔ Reading must be 100% lossless on the underlying string).
- XSS or unsafe URL execution from user-entered Markdown links or images (`javascript:`, `data:`, `vbscript:` URLs must be blocked; raw HTML must not be executed).
- Breaking Atrium's existing `Cmd/Ctrl+K` Command Palette or Day Tracker keyboard shortcuts (`[`, `]`, `t`).

---

## 3. Approaches Considered

### Approach 1: CodeMirror 6 + custom Live Preview extension + `react-markdown` Reading mode *(Selected)*
- Uses `@codemirror/view`, `@codemirror/state`, `@codemirror/commands`, `@codemirror/language`, `@codemirror/lang-markdown`, `@lezer/highlight`, `@lezer/markdown` (all MIT — the exact engine Obsidian uses) and `react-markdown` (MIT) for Reading mode.
- **Trade-offs:** True raw-Markdown source of truth with zero round-trip drift, single editor instance for Live Preview & Source modes, React 19 compatible, and directly extensible for future wikilinks/frontmatter/tables. Requires implementing a focused ~250–350 line CodeMirror 6 Live Preview decoration extension.

### Approach 2: Milkdown (ProseMirror-based Markdown editor, MIT)
- **Trade-offs:** WYSIWYG feel rather than true Obsidian cursor-triggered Live Preview; requires a separate Source editor and ProseMirror-to-Markdown re-serialization on every switch, which risks reformatting raw `.md` files.

### Approach 3: TipTap (ProseMirror, MIT core)
- **Trade-offs:** Same ProseMirror WYSIWYG trade-offs as Milkdown; better suited to Notion-style block editors than raw-Markdown Live Preview.

---

## 4. Architecture & Detailed Design (Approach 1)

### Dependencies (all MIT, added to `web/package.json`)
- `@codemirror/state`
- `@codemirror/view`
- `@codemirror/commands`
- `@codemirror/language`
- `@codemirror/lang-markdown`
- `@lezer/highlight`
- `@lezer/markdown`
- `react-markdown`

*(Builder call: use `react-markdown` without `rehype-raw`, which renders CommonMark safely as React elements with zero raw HTML execution by default.)*

### Safe URL Policy
Both Live Preview widgets and Reading mode must validate URLs with a shared `sanitizeMarkdownUrl(rawUrl: string): string | null` helper:
- Allow only `http:`, `https:`, and `mailto:` protocols for links, and `http:` / `https:` for images (parsed via `new URL(trimmed)`).
- Reject `javascript:`, `data:`, `vbscript:`, or malformed URLs (render plain non-clickable text for unsafe links; show a styled fallback badge for unsafe/broken images).

### Live Preview Extension (`web/src/lib/markdownLivePreview.ts`)
A CodeMirror 6 extension (`StateField<DecorationSet>` or `ViewPlugin` — note: block-level widgets like images and horizontal rules that replace or span lines work most reliably in a `StateField<DecorationSet>` in CodeMirror 6, avoiding `Decorations that replace line breaks may not be specified via plugins`).
- **Active-range rule:** A syntax node is "active" if any selection range (`state.selection.ranges`) overlaps the node's `[from, to]` span (inclusive of boundaries, i.e., `sel.from <= node.to && sel.to >= node.from`). When active, markers remain visible for direct editing; when inactive, markers are hidden with `Decoration.replace({})`.
- **Element behaviors when inactive:**
  1. **Headings (`ATXHeading1`–`ATXHeading6`):** Apply line/mark heading class (`cm-md-h1`..`cm-md-h6`) to the heading text and hide the leading `HeaderMark` (`# `..`###### ` including the following space).
  2. **Bold (`StrongEmphasis`):** Style inner text bold (`cm-md-strong`) and hide opening/closing `EmphasisMark` (`**` / `__`).
  3. **Italics (`Emphasis`):** Style inner text italic (`cm-md-em`) and hide opening/closing `EmphasisMark` (`*` / `_`).
  4. **Blockquotes (`Blockquote`):** Apply blockquote line decoration (`cm-md-blockquote` with left accent border and subtle muted text) and hide the leading `QuoteMark` (`> `).
  5. **Horizontal rules (`HorizontalRule`):** Replace the `---` / `***` / `___` line range with a clean `<hr class="cm-md-hr" />` widget when cursor is not on that line.
  6. **Links (`Link`):** Style link text with accent color and underline (`cm-md-link`), hide `[`, `]`, and `(url)` parts when inactive, and allow opening safe links via `Cmd/Ctrl+Click` (showing a subtle tooltip `"Cmd/Ctrl+Click to open"` or direct click icon).
  7. **Images (`Image`):** Parse `![alt](url)`. When inactive, hide the raw syntax (or keep syntax hidden and render a clean inline `WidgetType` displaying `<img>` with `alt`, responsive `max-width: 100%`, rounded corners, and a graceful fallback caption if the image fails to load or URL is blocked). When active (cursor on the image line/token), show the raw `![alt](url)` syntax AND render the image preview below the line so the user can edit alt text/URL while seeing the image.

### Formatting Commands (`web/src/lib/markdownCommands.ts`)
Pure CodeMirror 6 `Command` functions shared between keyboard shortcuts and toolbar buttons:
- `toggleBold` (`**`), `toggleItalic` (`*`): wrap/unwrap current selection or insert `****`/`**` with cursor centered.
- `setHeading(level: 1 | 2 | 3)`: toggle or replace the current line's leading `#` prefix.
- `toggleBlockquote`: toggle leading `> ` on selected lines.
- `insertLink`: wrap selected text as `[selected](https://)` (selecting `https://`) or insert `[link text](https://)`.
- `insertImage`: insert `![alt text](https://)` (selecting `https://`).
- `insertHorizontalRule`: insert `\n---\n` on a new line.

### Frontend Components & Integration
- **`web/src/components/MarkdownEditor.tsx`**: Encapsulates the CodeMirror 6 `EditorView`, dynamically reconfiguring the Live Preview extension via a `Compartment` when switching between `live` and `source` modes (so undo history, scroll position, and cursor position are preserved!), plus the Atrium theme compartment syncing with `data-theme="light" | "dark"`.
- **`web/src/components/NotesPage.tsx`**: Fullscreen Notes tab containing:
  - Compact top utility bar: mode segmented control (`Live Preview` | `Reading` | `Source`), formatting toolbar (visible in `live` and `source` modes), and `Reset sample` action (only if modified, or subtle utility menu).
  - Editor / Reader canvas (`MarkdownEditor` for `live`/`source`, `react-markdown` view for `reading`).
  - Subtle bottom status bar: word count, character count, active mode hint (`Live Preview · Cmd/Ctrl+E for Reading`).
- **`web/src/App.tsx`, `TopBar.tsx`, `MobileTabBar.tsx`, `Icons.tsx`, `types/atrium.ts`, `styles.css`**:
  - Add `'notes'` to `AppView` and `MobileTab`.
  - Update `.mobile-tabbar` CSS grid from 3 columns to 4 columns (`Day`, `Calendar`, `University`, `Notes`).
  - Ensure `Cmd/Ctrl+E` toggles Live Preview ↔ Reading only when `activeView === 'notes'`, and `Cmd/Ctrl+K` continues opening the Command Palette everywhere.

---

## 5. What's Left to the Builder
- Exact wording of the starter welcome/showcase Markdown note.
- Exact CSS token values and CodeMirror highlight colors (following Atrium's Atelier CSS custom properties in `web/src/styles.css`).
- Unit tests (if a lightweight Node test or helper test is appropriate) and full verification via `npm run check:suppressions`, `npm --prefix web run typecheck`, and `npm --prefix web run build` (plus verifying `dotnet test` remains unaffected).

