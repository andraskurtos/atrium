export const NOTES_DRAFT_STORAGE_KEY = 'atrium.notes.pr1.draft';

export const DEFAULT_SAMPLE_NOTE = `# Atrium Notes

Welcome to your **Obsidian-style Live Preview** Markdown workspace. Click any formatted line to edit the raw Markdown directly, then move your cursor away to see it render cleanly in place.

## What Works Right Now

Write naturally with **bold text**, *italic emphasis*, and ***bold italic*** combinations. You can switch between **Live Preview**, **Reading**, and **Source** modes in the top bar (or press \`Cmd/Ctrl + E\` to toggle Reading mode).

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

export function loadNotesDraft(): string {
  if (typeof window === 'undefined') return DEFAULT_SAMPLE_NOTE;
  try {
    const saved = window.localStorage.getItem(NOTES_DRAFT_STORAGE_KEY);
    return saved !== null ? saved : DEFAULT_SAMPLE_NOTE;
  } catch {
    return DEFAULT_SAMPLE_NOTE;
  }
}

export function saveNotesDraft(content: string): void {
  if (typeof window === 'undefined') return;
  try {
    window.localStorage.setItem(NOTES_DRAFT_STORAGE_KEY, content);
  } catch {
    // Ignore storage quota or private-mode errors
  }
}

