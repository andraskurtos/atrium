# Atrium Design System & Style Guide

**Theme:** Warm Tactile Swiss (*"Atelier"*)
**Layout Pattern:** Focus Stream + Context Utility Rail
**Target Audience:** Any human developer or AI agent building UI for Atrium

> **Rule for agents:** Treat this document as the single source of truth for all frontend styling, layout, typography, color tokens, and interaction patterns in `web/`. Do not introduce competing themes, generic SaaS blues/purples, heavy shadows, or unrequested chrome.

---

## 1. Design Philosophy

Atrium is a private, self-hosted personal everything-app used every single day alongside AI agents. The UI should feel like a **quiet, precision personal instrument**—equal parts Dieter Rams hardware, Swiss editorial print, and native macOS/iOS craftsmanship.

### Core Principles
1. **Warmth over sterility.** Use warm linen/alabaster in Light Mode and warm walnut/obsidian in Dark Mode. Never use harsh pure `#ffffff` on `#000000` or cold blue-gray enterprise palettes.
2. **Quiet hierarchy.** Let typography, whitespace, and `1px` hairlines organize the screen. Avoid loud gradients, neon glows, heavy shadows, and thick borders.
3. **One focal stream.** The user's eye should land on a single, calm primary stream (Today's brief, quick capture, checklist, agent proposals) while secondary context (calendar, upcoming events, vault shortcuts) sits quietly in a side rail.
4. **Semantic restraint.** Reserve the terracotta accent (`--accent`) strictly for primary actions, active states, and current-day markers. Keep 90%+ of the viewport neutral.
5. **Native PWA ergonomics.** Instant feedback, generous touch targets, system sans-serif UI text, keyboard-first navigation (`⌘K`), and proper safe-area handling on mobile and desktop standalone windows.

### Anti-Patterns (Never Do)
- **No** generic blue/indigo/violet SaaS gradients or glowing glass effects.
- **No** heavy drop shadows (`0 20px 50px ...`) or thick `2px+` brutalist borders.
- **No** crowded multi-column dashboard walls with 8+ competing widgets above the fold.
- **No** hardcoded light/dark colors inside components—always consume semantic CSS custom properties.
- **No** silent AI mutations: agent-drafted items must be visibly badged and reviewable.

---

## 2. Color System & Design Tokens

All colors are defined as raw `-light` / `-dark` custom properties on `:root`, mapped to semantic tokens using `@media (prefers-color-scheme: dark)` as a baseline fallback and `@supports (color: light-dark(white, black))` as progressive enhancement.

### Token Reference Table

| Semantic Token | Light Mode (*Warm Linen*) | Dark Mode (*Warm Obsidian*) | Purpose |
| :--- | :--- | :--- | :--- |
| `--bg` | `#f6f3ed` | `#141311` | Viewport / canvas background |
| `--surface` | `#ffffff` | `#1d1b18` | Primary card & container surface |
| `--surface-subtle` | `#faf8f4` | `#24211d` | Nested item / soft inset card |
| `--surface-muted` | `#f1ece2` | `#2a2621` | Inputs, hover fills, recessed wells |
| `--border` | `#e5dfd5` | `#2f2b25` | Standard `1px` structural hairline |
| `--border-strong` | `#d2c9bc` | `#433d35` | Focused/hovered border, active divider |
| `--text` | `#1c1917` | `#f3efe6` | Primary readable text |
| `--text-muted` | `#6b645c` | `#a39b8f` | Secondary metadata, timestamps, subtitles |
| `--text-faint` | `#8f877d` | `#756e64` | Placeholders, disabled text |
| `--accent` | `#c25438` | `#e07a5f` | Primary CTA, active day, focus ring |
| `--accent-hover` | `#a8442b` | `#e98d74` | Primary CTA hover state |
| `--accent-fg` | `#ffffff` | `#141311` | Text/icon on `--accent` fill |
| `--accent-soft` | `#f4e6e1` | `rgba(224, 122, 95, 0.16)` | Tinted pill/tag background |
| `--accent-soft-text` | `#9a3c25` | `#f2a48d` | Text inside `--accent-soft` pills |
| `--ok` | `#2f6f55` | `#68b08f` | Healthy status, connected DB, completed |
| `--warn` | `#b4691e` | `#e5a95c` | Degraded status, pending agent approval |
| `--danger` | `#b83227` | `#e86a5e` | Destructive actions, errors |

### Required HTML & CSS Setup
1. In `web/index.html`, always declare color-scheme and matching theme colors to prevent flashes of un-themed content:
   ```html
   <meta name="color-scheme" content="light dark" />
   <meta name="theme-color" content="#f6f3ed" media="(prefers-color-scheme: light)" />
   <meta name="theme-color" content="#141311" media="(prefers-color-scheme: dark)" />
   ```
2. In `web/src/styles/tokens.css`, always set `color-scheme: light dark` on `:root` (never only on `body`), define raw `-light` / `-dark` values, provide the `@media (prefers-color-scheme: dark)` fallback, and progressively enhance with `@supports (color: light-dark(white, black))`.
3. **Contrast requirements:** Maintain WCAG AA contrast (`≥ 4.5:1` for body text, `≥ 3:1` for large headings, icons, and interactive borders) in both modes.

---

## 3. Typography

Atrium pairs an **editorial serif** for calm, human-scale landmarks with a **native system sans-serif** for crisp UI legibility and **tabular monospace** for times, metrics, and shortcuts.

### Font Stacks
```css
--font-display: Georgia, 'Iowan Old Style', 'Palatino Linotype', 'Times New Roman', serif;
--font-body: -apple-system, BlinkMacSystemFont, 'Segoe UI', Inter, Roboto, sans-serif;
--font-mono: ui-monospace, 'SFMono-Regular', Menlo, Monaco, Consolas, monospace;
```

### Type Scale & Usage

| Role | Size | Weight | Font | Line Height | Tracking | Usage |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| Page Title / Date | `1.5rem` (`24px`) | `600` | `--font-display` | `1.2` | `-0.015em` | Primary page header (*"Wednesday, Nov 25"*) |
| Section Heading | `0.9375rem` (`15px`) | `600` | `--font-display` | `1.3` | `-0.01em` | Card headers (*"Today's Checklist"*, *"Agent Proposals"*) |
| Body / Row Title | `0.875rem` (`14px`) | `400` / `500` | `--font-body` | `1.5` | `0` | Tasks, notes, descriptions, form labels |
| Secondary / Meta | `0.8125rem` (`13px`) | `400` | `--font-body` | `1.45` | `0` | Subtitles, event durations, file metadata |
| Kicker / Eyebrow | `0.6875rem` (`11px`) | `600` | `--font-body` | `1.2` | `0.08em` | Uppercase date/module eyebrows (*"ATRIUM · PERSONAL HUB"*) |
| Pill / Badge | `0.75rem` (`12px`) | `500` | `--font-body` | `1` | `0.01em` | Status tags, categories, `MCP` / `AI` markers |
| Time / Numeric | `0.75rem–0.8125rem` | `500` | `--font-mono` | `1.2` | `0` | Event times (`09:30`), counts, keyboard hints (`⌘K`) |

### Typography Rules
- Always apply `font-variant-numeric: tabular-nums` to times, dates, calendar grids, and counters so numbers align vertically.
- Use `--font-display` **only** for page titles and card/section titles—never for paragraphs, buttons, or form controls.
- Keep minimum readable text at `12px` (`0.75rem`), except uppercase kickers (`11px`).

---

## 4. Spacing, Radii, Borders & Elevation

### Spacing Scale (4px Base)
Use multiples of `4px` consistently:
- `4px` (`0.25rem`): tight icon-to-label gaps
- `8px` (`0.5rem`): inline control gaps, compact list item gaps
- `12px` (`0.75rem`): inner padding for nested inset cards (`--surface-subtle`)
- `16px` (`1rem`): standard card padding (`16px–20px`), vertical stack gap between cards
- `24px` (`1.5rem`): gutter between Focus Stream and Context Rail, page padding on desktop
- `32px` (`2rem`): major section separation

### Border Radii
- **Primary cards & drawers:** `12px` (`--radius-lg`)
- **Nested inset cards, inputs, buttons:** `8px` (`--radius-md`)
- **Tags / category chips:** `6px` (`--radius-sm`) or `9999px` (`--radius-pill`) for status pills & count badges

### Borders & Shadows
- **Hairlines:** Every card and nested item uses `1px solid var(--border)`.
- **Shadows (whisper-quiet):**
  - Light mode card: `0 1px 2px rgba(28, 25, 23, 0.04), 0 4px 12px rgba(28, 25, 23, 0.03)`
  - Dark mode card: `0 1px 3px rgba(0, 0, 0, 0.35), 0 8px 20px rgba(0, 0, 0, 0.25)`
  - Nested inset cards: `0 1px 2px rgba(28, 25, 23, 0.04)` (light) / `none` (dark)
- Never stack multiple deep shadows; rely on surface value steps (`--bg` → `--surface` → `--surface-subtle`) plus `1px` hairlines.

---

## 5. Layout Architecture: Focus Stream + Context Rail

### Shell Geometry
```text
┌─────────────────────────────────────────────────────────────────────────┐
│  [Optional Slim Top Bar / Breadcrumb + ⌘K Trigger]                      │
├──────────────────────────────────────────────────┬──────────────────────┤
│                                                  │                      │
│               FOCUS STREAM                       │   CONTEXT RAIL       │
│         max-width: 700px (centered)              │   width: 288px       │
│                                                  │   sticky top: 24px   │
│  1. Editorial Date & Summary Header              │                      │
│  2. Unified Quick-Capture Composer               │  • Mini Calendar     │
│  3. Today's Checklist / Active Tasks             │  • Up Next Events    │
│  4. Agent Proposals (MCP / AI Review)            │  • Pinned Vault      │
│  5. Recent Notes / Stream Cards                  │  • Local Status      │
│                                                  │                      │
└──────────────────────────────────────────────────┴──────────────────────┘
```

### Responsive Breakpoints
- **Desktop (`≥ 1024px`):** Two-column grid (`grid-template-columns: minmax(0, 1fr) 288px`, `gap: 24px`, max container width `1080px` centered with `24px–32px` horizontal padding). Focus Stream content centers comfortably at `max-width: 700px`. Context Rail is `position: sticky; top: 24px`.
- **Tablet (`768px – 1023px`):** Single-column Focus Stream (`max-width: 700px` centered); Context Rail stacks below in a 2-column grid of utility cards, or opens via a top-right "Context" toggle button.
- **Mobile / Standalone PWA (`< 768px`):**
  - Single-column Focus Stream with `16px` horizontal padding and `env(safe-area-inset-*)` padding.
  - Context Rail content accessible via a clean slide-over sheet or compact bottom tab switcher (`Stream`, `Calendar`, `Vault`, `Agents`).
  - All interactive tap targets must be at least `40px × 40px` (`44px` preferred on mobile).

---

## 6. Component Specifications

### Primary Card (`.card`)
- Background: `var(--surface)`, border: `1px solid var(--border)`, radius: `12px`, padding: `16px 20px`, subtle card shadow.
- Header row: left `--font-display` `15px` semibold title, right muted count or `--accent-soft` pill.

### Nested Inset Card (`.card-soft`)
- Used inside primary cards for events, agent proposals, and vault shortcuts.
- Background: `var(--surface-subtle)`, border: `1px solid var(--border)`, radius: `8px`, padding: `10px 12px`.

### Buttons
- **Primary:** `background: var(--accent)`, `color: var(--accent-fg)`, `border: 1px solid transparent`, `border-radius: 8px`, `padding: 8px 14px`, `font-size: 13px`, `font-weight: 600`. Hover transitions smoothly to `--accent-hover`.
- **Secondary:** `background: var(--surface)`, `color: var(--text)`, `border: 1px solid var(--border)`, hover shifts border to `--border-strong` and background to `--surface-subtle`.
- **Ghost:** Transparent background, `color: var(--text-muted)`, hover fills `--surface-muted` and `color: var(--text)`.
- **Destructive:** `color: var(--danger)` on subtle border, or solid `--danger` fill only inside confirmation dialogs.

### Inputs & Quick-Capture Composer
- Background: `var(--surface-muted)`, text: `var(--text)`, placeholder: `var(--text-faint)`, border: `1px solid var(--border)`, radius: `8px`, padding: `9px 12px`.
- Focus state: `border-color: var(--accent)` and subtle `outline: 2px solid var(--accent); outline-offset: 2px` on `:focus-visible`.
- Quick-Capture Composer lives near the top of the Focus Stream inside a `.card` wrapper (`padding: 10px`), pairing a full-width input with a primary `Capture` button and `Enter` submit.

### Checklist & List Rows
- Separated by `1px solid var(--border)` horizontal hairlines (`border-bottom`, omitted on `:last-child`).
- Native `<input type="checkbox">` styled with `accent-color: var(--accent)` (never reinvent native form controls unnecessarily).
- Completed items use `text-decoration: line-through` and `color: var(--text-muted)` while remaining legible.

### Badges, Tags & Status Indicators
- **Category / Context Pill:** `background: var(--accent-soft)`, `color: var(--accent-soft-text)`, `padding: 2px 8px`, `border-radius: 6px`, `font-size: 12px`, `font-weight: 500`.
- **Live Status Dot:** `8px × 8px` circle colored with `--ok` (connected), `--warn` (degraded / pending), or `--text-faint` (offline), always paired with readable status text (never color alone).

### Dialogs, Sheets & Command Palette (`⌘K`)
- Prefer native HTML `<dialog>` (`showModal()`) and Popover API where appropriate.
- Backdrop: warm translucent scrim (`rgba(28, 25, 23, 0.35)` light / `rgba(0, 0, 0, 0.6)` dark) with subtle `backdrop-filter: blur(4px)`.

---

## 7. AI & MCP Agent UX Conventions

Because Atrium is built for both you and AI agents via MCP:
- **Attribution:** Every item created or proposed by an agent displays its source name (`Calendar MCP`, `Home Assistant`, etc.) and relative timestamp in `--text-muted`.
- **Human-in-the-loop:** Pending agent proposals appear in the Focus Stream's **Agent Proposals** card with explicit, low-friction actions (`Approve`, `Review`, `Dismiss`) and a `--warn` indicator until resolved.
- **No chat bloat on the home screen:** Keep the dashboard focused on structured personal state; AI interactions enter through the Quick-Capture bar or a dedicated command/agent drawer rather than taking over the entire canvas.

---

## 8. Motion, Accessibility & PWA Polish

- **Transitions:** Fast and tactile—`120ms–180ms cubic-bezier(0.2, 0, 0, 1)` for hover, border, and surface changes. Never use bouncy/elastic springs or gratuitous entrance animations.
- **Reduced Motion:** Wrap all transitions/animations in `@media (prefers-reduced-motion: reduce) { *, *::before, *::after { animation-duration: 0.01ms !important; transition-duration: 0.01ms !important; } }`.
- **Keyboard & Focus:** Every interactive element must be reachable via `Tab`, have a clear `:focus-visible` ring (`2px solid var(--accent)`, `2px` offset), and support `Escape` to close overlays.
- **Semantic Landmarks:** Use `<main>` for the Focus Stream, `<aside>` for the Context Rail, `<nav>` for navigation, `<section aria-labelledby="...">` for dashboard cards, and `role="status" aria-live="polite"` for async status updates.

---

## 9. Agent Implementation Checklist

Before finishing any UI feature or component in `web/`, verify every item:
- [ ] Uses semantic CSS custom properties from Section 2—zero hardcoded light/dark hex colors in components.
- [ ] Tested visually in **both** Light Mode (`#f6f3ed`) and Dark Mode (`#141311`) with AA contrast.
- [ ] Display headings use `--font-display` (serif); body/controls use `--font-body`; times/counts use `--font-mono` with `tabular-nums`.
- [ ] Cards follow `12px` radius, nested items `8px`, `1px solid var(--border)` hairlines, and whisper-quiet shadows.
- [ ] Fits the **Focus Stream + Context Rail** hierarchy without turning the home screen into a dense multi-column widget wall.
- [ ] Responsive down to mobile PWA width (`375px`) with `≥ 40px` touch targets and `safe-area-inset` respect.
- [ ] Keyboard navigable with visible `:focus-visible` rings and `prefers-reduced-motion` respected.
- [ ] Any AI/MCP-sourced data is clearly attributed and never silently mutates user records without visibility.
