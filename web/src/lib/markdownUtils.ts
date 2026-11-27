export type CalloutTone = 'note' | 'tip' | 'warning' | 'danger' | 'quote';

export interface ParsedCalloutHeader {
  rawType: string;
  tone: CalloutTone;
  title: string;
}

export const CALLOUT_GLYPHS: Record<CalloutTone, string> = {
  note: 'ℹ',
  tip: '✦',
  warning: '⚠',
  danger: '✕',
  quote: '❝',
};

const CALLOUT_TONE_MAP = new Map<string, CalloutTone>([
  ['note', 'note'],
  ['info', 'note'],
  ['abstract', 'note'],
  ['summary', 'note'],
  ['tldr', 'note'],
  ['todo', 'note'],
  ['tip', 'tip'],
  ['hint', 'tip'],
  ['important', 'tip'],
  ['success', 'tip'],
  ['check', 'tip'],
  ['done', 'tip'],
  ['warning', 'warning'],
  ['caution', 'warning'],
  ['attention', 'warning'],
  ['question', 'warning'],
  ['help', 'warning'],
  ['faq', 'warning'],
  ['danger', 'danger'],
  ['error', 'danger'],
  ['bug', 'danger'],
  ['failure', 'danger'],
  ['fail', 'danger'],
  ['missing', 'danger'],
  ['quote', 'quote'],
  ['cite', 'quote'],
  ['example', 'quote'],
]);

export function resolveCalloutTone(rawType: string): CalloutTone {
  return CALLOUT_TONE_MAP.get(rawType.trim().toLowerCase()) ?? 'note';
}

export function formatCalloutDefaultTitle(rawType: string): string {
  const cleaned = rawType.trim();
  if (!cleaned) return 'Note';
  return cleaned.charAt(0).toUpperCase() + cleaned.slice(1).toLowerCase();
}

export function parseCalloutHeaderLine(
  lineText: string,
): ParsedCalloutHeader | null {
  const match = lineText.match(
    /^\s*>\s*\[!([a-zA-Z0-9_-]+)\][+-]?(?:$|\s+(.*)$)/,
  );
  if (!match) return null;
  const rawType = match[1];
  const customTitle = (match[2] ?? '').trim();
  return {
    rawType,
    tone: resolveCalloutTone(rawType),
    title: customTitle || formatCalloutDefaultTitle(rawType),
  };
}

export const NOTES_DRAFT_STORAGE_KEY = 'atrium.notes.pr1.draft';

export function getNotesDraftStorageKey(userId?: string): string {
  const trimmed = userId?.trim();
  return trimmed
    ? `${NOTES_DRAFT_STORAGE_KEY}:${trimmed}`
    : NOTES_DRAFT_STORAGE_KEY;
}

export const DEFAULT_SAMPLE_NOTE = `# Atrium Notes

Welcome to your **Obsidian-style Live Preview** Markdown workspace. Click any formatted line to edit the raw Markdown directly, then move your cursor away to see it render cleanly in place.

## What Works Right Now

Write naturally with **bold text**, *italic emphasis*, and ***bold italic*** combinations. You can switch between **Live Preview**, **Reading**, and **Source** modes in the top bar (or press \`Cmd/Ctrl + E\` to toggle Reading mode).

> [!tip] Callouts supported
> Use \`> [!note]\`, \`> [!tip]\`, \`> [!warning]\`, or \`> [!danger]\` with an optional custom title. Click the header line to edit the callout type or title directly.

> "Your knowledge base is just plain Markdown files — clean, portable, and ready for future local-first desktop sync and AI agent access."

---

### Links & Images

- Hold **Cmd/Ctrl + Click** on a link in Live Preview (or click directly in Reading mode): [Explore CommonMark](https://commonmark.org/help/)
- Standard Markdown image URLs render inline right below or in place of the syntax:

![Warm sunlit desk notebook](https://images.unsplash.com/photo-1455390582262-044cdead277a?auto=format&fit=crop&w=1000&q=80)

---

### Quick Shortcuts

- **Bold:** Cmd/Ctrl + B
- *Italic:* Cmd/Ctrl + I
- Insert link: Cmd/Ctrl + Shift + K
- Toggle Reading mode: Cmd/Ctrl + E
`;

export function sanitizeMarkdownUrl(
  rawUrl: string,
  kind: 'link' | 'image',
): string | null {
  let trimmed = rawUrl.trim();
  if (trimmed.startsWith('<') && trimmed.endsWith('>') && trimmed.length >= 2) {
    trimmed = trimmed.slice(1, -1).trim();
  }
  if (!trimmed) return null;

  try {
    const parsed = new URL(trimmed);
    const protocol = parsed.protocol.toLowerCase();
    if (kind === 'image') {
      return protocol === 'http:' || protocol === 'https:' ? parsed.href : null;
    }
    return protocol === 'http:' ||
      protocol === 'https:' ||
      protocol === 'mailto:'
      ? parsed.href
      : null;
  } catch {
    return null;
  }
}

export function countWordsAndChars(markdown: string): {
  words: number;
  chars: number;
} {
  const trimmed = markdown.trim();
  const words = trimmed ? trimmed.split(/\s+/).length : 0;
  return { words, chars: markdown.length };
}

export function loadNotesDraft(userId?: string): string {
  if (typeof window === 'undefined') return DEFAULT_SAMPLE_NOTE;
  try {
    const saved = window.localStorage.getItem(getNotesDraftStorageKey(userId));
    return saved !== null ? saved : DEFAULT_SAMPLE_NOTE;
  } catch {
    return DEFAULT_SAMPLE_NOTE;
  }
}

export function saveNotesDraft(content: string, userId?: string): void {
  if (typeof window === 'undefined') return;
  try {
    const key = getNotesDraftStorageKey(userId);
    if (content === DEFAULT_SAMPLE_NOTE) {
      window.localStorage.removeItem(key);
    } else {
      window.localStorage.setItem(key, content);
    }
  } catch {
    // Ignore storage quota or private-mode errors
  }
}

export function clearNotesDraft(userId?: string): void {
  if (typeof window === 'undefined') return;
  try {
    if (userId?.trim()) {
      window.localStorage.removeItem(getNotesDraftStorageKey(userId));
    }
    window.localStorage.removeItem(NOTES_DRAFT_STORAGE_KEY);
  } catch {
    // Ignore storage errors
  }
}
