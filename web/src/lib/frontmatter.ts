export interface FrontmatterRange {
  from: number;
  to: number;
  bodyStart: number;
}

export interface ParsedFrontmatter {
  title: string | null;
  tags: string[];
  body: string;
  hasFrontmatter: boolean;
}

const MAX_TAG_LENGTH = 40;
const MAX_TAGS_PER_NOTE = 30;

function unquoteYamlScalar(value: string): string {
  const trimmed = value.trim();
  if (
    trimmed.length >= 2 &&
    ((trimmed.startsWith('"') && trimmed.endsWith('"')) ||
      (trimmed.startsWith("'") && trimmed.endsWith("'")))
  ) {
    return trimmed.slice(1, -1).trim();
  }
  return trimmed;
}

export function normalizeTag(raw: string): string | null {
  let cleaned = unquoteYamlScalar(raw).trim();
  if (cleaned.startsWith('#')) {
    cleaned = cleaned.slice(1).trim();
  }
  if (!cleaned || cleaned.length > MAX_TAG_LENGTH) return null;
  const lower = cleaned.toLowerCase();
  if (!/^[a-z0-9/_-]+$/.test(lower)) return null;
  return lower;
}

export function findFrontmatterRange(markdown: string): FrontmatterRange | null {
  if (!markdown) return null;
  const firstLineEnd = markdown.indexOf('\n');
  if (firstLineEnd === -1) return null;
  const firstLine = markdown.slice(0, firstLineEnd).replace(/\r$/, '').trim();
  if (firstLine !== '---') return null;

  let offset = firstLineEnd + 1;
  while (offset <= markdown.length) {
    const nextNl = markdown.indexOf('\n', offset);
    const lineEnd = nextNl === -1 ? markdown.length : nextNl;
    const line = markdown.slice(offset, lineEnd).replace(/\r$/, '').trim();
    if (line === '---' || line === '...') {
      let bodyStart = nextNl === -1 ? markdown.length : nextNl + 1;
      // Consume one optional blank line immediately after closing ---
      if (bodyStart < markdown.length) {
        const afterNl = markdown.indexOf('\n', bodyStart);
        const afterEnd = afterNl === -1 ? markdown.length : afterNl;
        if (markdown.slice(bodyStart, afterEnd).replace(/\r$/, '').trim() === '') {
          bodyStart = afterNl === -1 ? markdown.length : afterNl + 1;
        }
      }
      return {
        from: 0,
        to: lineEnd,
        bodyStart,
      };
    }
    if (nextNl === -1) break;
    offset = nextNl + 1;
  }

  return null;
}

export function parseFrontmatter(markdown: string): ParsedFrontmatter {
  const range = findFrontmatterRange(markdown);
  if (!range) {
    return {
      title: null,
      tags: [],
      body: markdown,
      hasFrontmatter: false,
    };
  }

  const block = markdown.slice(0, range.to).replace(/\r\n/g, '\n');
  const lines = block.split('\n').slice(1, -1);

  let title: string | null = null;
  const tags: string[] = [];
  const seen = new Set<string>();
  let inTagsList = false;

  const pushTag = (raw: string) => {
    if (tags.length >= MAX_TAGS_PER_NOTE) return;
    const norm = normalizeTag(raw);
    if (norm && !seen.has(norm)) {
      seen.add(norm);
      tags.push(norm);
    }
  };

  for (const line of lines) {
    const trimmed = line.trim();
    if (inTagsList) {
      if (trimmed.startsWith('- ')) {
        pushTag(trimmed.slice(2));
        continue;
      }
      if (trimmed === '') {
        continue;
      }
      inTagsList = false;
    }

    if (!trimmed || trimmed.startsWith('#')) continue;
    const colon = line.indexOf(':');
    if (colon <= 0) continue;

    const key = line.slice(0, colon).trim().toLowerCase();
    const rawVal = line.slice(colon + 1).trim();

    if (key === 'title') {
      const unquoted = unquoteYamlScalar(rawVal);
      if (unquoted) title = unquoted;
    } else if (key === 'tags') {
      if (!rawVal) {
        inTagsList = true;
      } else if (rawVal.startsWith('[') && rawVal.endsWith(']')) {
        for (const part of rawVal.slice(1, -1).split(',')) {
          pushTag(part);
        }
      } else {
        for (const part of rawVal.split(',')) {
          pushTag(part);
        }
      }
    }
  }

  return {
    title,
    tags,
    body: markdown.slice(range.bodyStart),
    hasFrontmatter: true,
  };
}

export function stripFrontmatterForReading(markdown: string): string {
  const range = findFrontmatterRange(markdown);
  return range ? markdown.slice(range.bodyStart) : markdown;
}

function formatYamlTagsLines(tags: string[]): string[] {
  if (tags.length === 0) return [];
  return ['tags:', ...tags.map((t) => `  - ${t}`)];
}

function formatYamlScalar(value: string): string {
  if (/[:#[\]{},&*?|>!%@`"'\\]|^\s|\s$/.test(value)) {
    return `"${value.replace(/\\/g, '\\\\').replace(/"/g, '\\"')}"`;
  }
  return value;
}

export function updateFrontmatterTags(
  markdown: string,
  nextTags: string[],
): string {
  const normalized: string[] = [];
  const seen = new Set<string>();
  for (const t of nextTags) {
    if (normalized.length >= MAX_TAGS_PER_NOTE) break;
    const norm = normalizeTag(t);
    if (norm && !seen.has(norm)) {
      seen.add(norm);
      normalized.push(norm);
    }
  }

  const range = findFrontmatterRange(markdown);
  if (!range) {
    if (normalized.length === 0) return markdown;
    const fm = ['---', ...formatYamlTagsLines(normalized), '---', ''].join('\n');
    return markdown ? `${fm}\n${markdown}` : fm;
  }

  const blockLines = markdown
    .slice(0, range.to)
    .replace(/\r\n/g, '\n')
    .split('\n');
  const innerLines = blockLines.slice(1, -1);
  const keptLines: string[] = [];
  let skippingTags = false;
  let insertedTags = false;

  for (const line of innerLines) {
    const trimmed = line.trim();
    if (skippingTags) {
      if (trimmed.startsWith('- ') || trimmed === '') {
        continue;
      }
      skippingTags = false;
    }

    const colon = line.indexOf(':');
    if (colon > 0 && line.slice(0, colon).trim().toLowerCase() === 'tags') {
      skippingTags = !line.slice(colon + 1).trim();
      if (!insertedTags && normalized.length > 0) {
        keptLines.push(...formatYamlTagsLines(normalized));
        insertedTags = true;
      }
      continue;
    }

    keptLines.push(line);
  }

  if (!insertedTags && normalized.length > 0) {
    keptLines.push(...formatYamlTagsLines(normalized));
  }

  const body = markdown.slice(range.bodyStart);
  const hasAnyContent = keptLines.some((l) => l.trim().length > 0);
  if (!hasAnyContent) {
    return body;
  }

  const rebuilt = ['---', ...keptLines, '---'].join('\n');
  return body ? `${rebuilt}\n\n${body}` : `${rebuilt}\n`;
}

export function updateFrontmatterTitleIfPresent(
  markdown: string,
  nextTitle: string,
): string {
  const range = findFrontmatterRange(markdown);
  if (!range) return markdown;

  const blockLines = markdown
    .slice(0, range.to)
    .replace(/\r\n/g, '\n')
    .split('\n');
  const innerLines = blockLines.slice(1, -1);
  let replaced = false;

  const updatedInner = innerLines.map((line) => {
    const colon = line.indexOf(':');
    if (
      !replaced &&
      colon > 0 &&
      line.slice(0, colon).trim().toLowerCase() === 'title'
    ) {
      replaced = true;
      return `title: ${formatYamlScalar(nextTitle.trim())}`;
    }
    return line;
  });

  if (!replaced) return markdown;

  const body = markdown.slice(range.bodyStart);
  const rebuilt = ['---', ...updatedInner, '---'].join('\n');
  return body ? `${rebuilt}\n\n${body}` : `${rebuilt}\n`;
}

