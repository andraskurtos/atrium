import {
  HighlightStyle,
  syntaxHighlighting,
  syntaxTree,
} from '@codemirror/language';
import {
  StateField,
  type EditorState,
  type Extension,
  type Range,
} from '@codemirror/state';
import {
  Decoration,
  EditorView,
  WidgetType,
  type DecorationSet,
} from '@codemirror/view';
import { tags } from '@lezer/highlight';
import katex from 'katex';
import 'katex/dist/katex.min.css';
import {
  isInternalAttachmentUrl,
  resolveAttachmentImageUrl,
} from './attachmentsClient.ts';
import { findFrontmatterRange } from './frontmatter.ts';
import {
  CALLOUT_GLYPHS,
  parseCalloutHeaderLine,
  sanitizeMarkdownUrl,
  type CalloutTone,
} from './markdownUtils.ts';

class CalloutHeaderWidget extends WidgetType {
  readonly tone: CalloutTone;
  readonly title: string;

  constructor(tone: CalloutTone, title: string) {
    super();
    this.tone = tone;
    this.title = title;
  }

  override eq(other: CalloutHeaderWidget): boolean {
    return other.tone === this.tone && other.title === this.title;
  }

  override toDOM(): HTMLElement {
    const header = document.createElement('span');
    header.className = `cm-md-callout-title is-${this.tone}`;

    const icon = document.createElement('span');
    icon.className = 'cm-md-callout-icon';
    icon.setAttribute('aria-hidden', 'true');
    icon.textContent = CALLOUT_GLYPHS[this.tone];

    const label = document.createElement('span');
    label.textContent = this.title;

    header.appendChild(icon);
    header.appendChild(label);
    return header;
  }

  override ignoreEvent(): boolean {
    return false;
  }
}

class CodeBlockLangWidget extends WidgetType {
  readonly language: string;

  constructor(language: string) {
    super();
    this.language = language;
  }

  override eq(other: CodeBlockLangWidget): boolean {
    return other.language === this.language;
  }

  override toDOM(): HTMLElement {
    const header = document.createElement('span');
    header.className = 'cm-md-code-lang-bar';
    const badge = document.createElement('span');
    badge.className = 'cm-md-code-lang';
    badge.textContent = this.language || 'code';
    header.appendChild(badge);
    return header;
  }

  override ignoreEvent(): boolean {
    return false;
  }
}

class KatexWidget extends WidgetType {
  readonly expr: string;
  readonly displayMode: boolean;

  constructor(expr: string, displayMode: boolean) {
    super();
    this.expr = expr;
    this.displayMode = displayMode;
  }

  override eq(other: KatexWidget): boolean {
    return other.expr === this.expr && other.displayMode === this.displayMode;
  }

  override toDOM(): HTMLElement {
    const el = document.createElement(this.displayMode ? 'div' : 'span');
    el.className = this.displayMode ? 'cm-md-math-block' : 'cm-md-math-inline';
    try {
      el.innerHTML = katex.renderToString(this.expr, {
        displayMode: this.displayMode,
        throwOnError: false,
        strict: false,
        trust: false,
      });
    } catch {
      el.textContent = this.expr;
    }
    return el;
  }

  override ignoreEvent(): boolean {
    return false;
  }
}

class HorizontalRuleWidget extends WidgetType {
  override eq(): boolean {
    return true;
  }

  override toDOM(): HTMLElement {
    const hr = document.createElement('hr');
    hr.className = 'cm-md-hr';
    hr.setAttribute('aria-hidden', 'true');
    return hr;
  }

  override ignoreEvent(): boolean {
    return false;
  }
}

class ImageWidget extends WidgetType {
  readonly alt: string;
  readonly safeUrl: string | null;
  readonly rawUrl: string;

  constructor(alt: string, safeUrl: string | null, rawUrl: string) {
    super();
    this.alt = alt;
    this.safeUrl = safeUrl;
    this.rawUrl = rawUrl;
  }

  override eq(other: ImageWidget): boolean {
    return (
      other.alt === this.alt &&
      other.safeUrl === this.safeUrl &&
      other.rawUrl === this.rawUrl
    );
  }

  override toDOM(): HTMLElement {
    const figure = document.createElement('figure');
    figure.className = 'cm-md-image-figure';

    if (!this.safeUrl) {
      const fallback = document.createElement('div');
      fallback.className = 'cm-md-image-fallback';
      fallback.textContent = this.alt
        ? `Blocked or invalid image URL (${this.alt})`
        : 'Blocked or invalid image URL';
      figure.appendChild(fallback);
      return figure;
    }

    const img = document.createElement('img');
    img.className = 'cm-md-image';
    img.alt = this.alt;
    img.loading = 'lazy';
    img.decoding = 'async';
    img.referrerPolicy = 'no-referrer';

    const showFallback = () => {
      figure.innerHTML = '';
      const fallback = document.createElement('div');
      fallback.className = 'cm-md-image-fallback';
      fallback.textContent = this.alt
        ? `Image failed to load: ${this.alt}`
        : `Image failed to load (${this.rawUrl})`;
      figure.appendChild(fallback);
    };

    img.addEventListener('error', showFallback);

    if (isInternalAttachmentUrl(this.safeUrl)) {
      void resolveAttachmentImageUrl(this.safeUrl).then((blobUrl) => {
        if (!blobUrl) {
          showFallback();
          return;
        }
        img.src = blobUrl;
      });
    } else {
      img.src = this.safeUrl;
    }

    figure.appendChild(img);

    if (this.alt.trim()) {
      const caption = document.createElement('figcaption');
      caption.className = 'cm-md-image-caption';
      caption.textContent = this.alt;
      figure.appendChild(caption);
    }

    return figure;
  }

  override ignoreEvent(): boolean {
    return false;
  }
}

function isRangeActive(state: EditorState, from: number, to: number): boolean {
  for (const range of state.selection.ranges) {
    if (range.from <= to && range.to >= from) {
      return true;
    }
  }
  return false;
}

function buildLivePreviewDecorations(state: EditorState): DecorationSet {
  const decorations: Range<Decoration>[] = [];
  const doc = state.doc;
  const hiddenRanges: Array<{ from: number; to: number }> = [];

  const isFullyHidden = (from: number, to: number): boolean =>
    hiddenRanges.some((r) => from >= r.from && to <= r.to);

  const overlapsHidden = (from: number, to: number): boolean =>
    hiddenRanges.some((r) => from < r.to && to > r.from);

  const pushReplace = (
    from: number,
    to: number,
    spec: Parameters<typeof Decoration.replace>[0] = {},
  ): boolean => {
    if (to <= from || overlapsHidden(from, to)) return false;
    hiddenRanges.push({ from, to });
    decorations.push(Decoration.replace(spec).range(from, to));
    return true;
  };

  // Hide leading YAML frontmatter block in Live Preview when cursor is outside it
  const fmRange = findFrontmatterRange(doc.toString());
  if (fmRange && !isRangeActive(state, fmRange.from, fmRange.to)) {
    pushReplace(fmRange.from, fmRange.bodyStart);
  }

  syntaxTree(state).iterate({
    enter(nodeRef) {
      const { name, from, to, node } = nodeRef;

      if (isFullyHidden(from, to)) {
        return false;
      }

      // 1. Headings (ATXHeading1 .. ATXHeading6)
      if (name.startsWith('ATXHeading')) {
        if (overlapsHidden(from, to)) return;
        const level = Number.parseInt(name.slice('ATXHeading'.length), 10);
        if (level >= 1 && level <= 6) {
          const line = doc.lineAt(from);
          decorations.push(
            Decoration.line({
              attributes: { class: `cm-md-line-h${level}` },
            }).range(line.from),
          );

          if (!isRangeActive(state, line.from, line.to)) {
            let seenOpeningMark = false;
            let child = node.firstChild;
            while (child) {
              if (child.name === 'HeaderMark') {
                if (!seenOpeningMark) {
                  seenOpeningMark = true;
                  const nextChar =
                    child.to < line.to
                      ? doc.sliceString(child.to, child.to + 1)
                      : '';
                  pushReplace(
                    child.from,
                    nextChar === ' ' ? child.to + 1 : child.to,
                  );
                } else {
                  const prevChar =
                    child.from > line.from
                      ? doc.sliceString(child.from - 1, child.from)
                      : '';
                  pushReplace(
                    prevChar === ' ' ? child.from - 1 : child.from,
                    child.to,
                  );
                }
              }
              child = child.nextSibling;
            }
          }
        }
        return;
      }

      // 2. Bold (StrongEmphasis)
      if (name === 'StrongEmphasis') {
        if (overlapsHidden(from, to)) return;
        decorations.push(
          Decoration.mark({ class: 'cm-md-strong' }).range(from, to),
        );
        if (!isRangeActive(state, from, to)) {
          let child = node.firstChild;
          while (child) {
            if (child.name === 'EmphasisMark') {
              pushReplace(child.from, child.to);
            }
            child = child.nextSibling;
          }
        }
        return;
      }

      // 3. Italic (Emphasis)
      if (name === 'Emphasis') {
        if (overlapsHidden(from, to)) return;
        decorations.push(
          Decoration.mark({ class: 'cm-md-em' }).range(from, to),
        );
        if (!isRangeActive(state, from, to)) {
          let child = node.firstChild;
          while (child) {
            if (child.name === 'EmphasisMark') {
              pushReplace(child.from, child.to);
            }
            child = child.nextSibling;
          }
        }
        return;
      }

      // 3b. Inline code (`code`)
      if (name === 'InlineCode') {
        if (overlapsHidden(from, to)) return;
        if (!isRangeActive(state, from, to)) {
          let child = node.firstChild;
          while (child) {
            if (child.name === 'CodeMark') {
              pushReplace(child.from, child.to);
            }
            child = child.nextSibling;
          }
        }
        return;
      }

      // 4. Blockquotes & Callouts (> [!type] Title)
      if (name === 'Blockquote') {
        const startLineNo = doc.lineAt(from).number;
        const endLineNo = doc.lineAt(to).number;
        const firstLine = doc.line(startLineNo);
        const callout = parseCalloutHeaderLine(
          doc.sliceString(from, firstLine.to),
        );

        if (callout) {
          for (let lineNo = startLineNo; lineNo <= endLineNo; lineNo += 1) {
            const line = doc.line(lineNo);
            const classes = [
              'cm-md-callout',
              `cm-md-callout-${callout.tone}`,
            ];
            if (lineNo === startLineNo) classes.push('cm-md-callout-first');
            if (lineNo === endLineNo) classes.push('cm-md-callout-last');
            decorations.push(
              Decoration.line({
                attributes: { class: classes.join(' ') },
              }).range(line.from),
            );
          }

          if (!isRangeActive(state, firstLine.from, firstLine.to)) {
            pushReplace(from, firstLine.to, {
              widget: new CalloutHeaderWidget(callout.tone, callout.title),
            });
          }
          return;
        }

        for (let lineNo = startLineNo; lineNo <= endLineNo; lineNo += 1) {
          const line = doc.line(lineNo);
          decorations.push(
            Decoration.line({
              attributes: { class: 'cm-md-blockquote' },
            }).range(line.from),
          );
        }
        return;
      }

      if (name === 'QuoteMark') {
        if (overlapsHidden(from, to)) return;
        const line = doc.lineAt(from);
        if (!isRangeActive(state, line.from, line.to)) {
          const nextChar =
            to < line.to ? doc.sliceString(to, to + 1) : '';
          pushReplace(from, nextChar === ' ' ? to + 1 : to);
        }
        return;
      }

      // 5. Horizontal Rule
      if (name === 'HorizontalRule') {
        if (overlapsHidden(from, to)) return false;
        const line = doc.lineAt(from);
        if (!isRangeActive(state, line.from, line.to)) {
          pushReplace(from, to, { widget: new HorizontalRuleWidget() });
          return false;
        }
        return;
      }

      // 6. Images: ![alt](url)
      if (name === 'Image') {
        if (overlapsHidden(from, to)) return;
        const urlNode = node.getChild('URL');
        if (!urlNode) {
          // Reference-style images (e.g. ![alt][1]) have no inline URL child; leave unstyled rather than marking invalid
          return;
        }
        const startLine = doc.lineAt(from);
        const endLine = doc.lineAt(to);
        const rawUrl = doc.sliceString(urlNode.from, urlNode.to);
        const safeUrl = sanitizeMarkdownUrl(rawUrl, 'image');

        let closeBracketPos = -1;
        let child = node.firstChild;
        while (child) {
          if (
            child.name === 'LinkMark' &&
            doc.sliceString(child.from, child.to) === ']'
          ) {
            closeBracketPos = child.from;
            break;
          }
          child = child.nextSibling;
        }
        const altStart = from + 2; // after "!["
        const alt =
          closeBracketPos > altStart
            ? doc.sliceString(altStart, closeBracketPos)
            : '';

        if (!isRangeActive(state, startLine.from, endLine.to)) {
          pushReplace(from, to, {
            widget: new ImageWidget(alt, safeUrl, rawUrl),
          });
          return false;
        }

        // Active line(s): keep raw syntax editable and render live preview below
        decorations.push(
          Decoration.widget({
            widget: new ImageWidget(alt, safeUrl, rawUrl),
            side: 1,
            block: true,
          }).range(endLine.to),
        );
        return false;
      }

      // 7. Links: [label](url)
      if (name === 'Link') {
        if (overlapsHidden(from, to)) return;
        const urlNode = node.getChild('URL');
        if (!urlNode) {
          // Reference-style links (e.g. [ref][1]) have no inline URL child; leave unstyled rather than marking invalid
          return;
        }
        const rawUrl = doc.sliceString(urlNode.from, urlNode.to);
        const safeUrl = sanitizeMarkdownUrl(rawUrl, 'link');

        let openBracketEnd = -1;
        let closeBracketStart = -1;
        let child = node.firstChild;
        while (child) {
          if (child.name === 'LinkMark') {
            const markText = doc.sliceString(child.from, child.to);
            if (markText === '[' && openBracketEnd === -1) {
              openBracketEnd = child.to;
            } else if (markText === ']' && closeBracketStart === -1) {
              closeBracketStart = child.from;
            }
          }
          child = child.nextSibling;
        }

        if (openBracketEnd !== -1 && closeBracketStart > openBracketEnd) {
          decorations.push(
            Decoration.mark({
              class: safeUrl ? 'cm-md-link' : 'cm-md-link-invalid',
              attributes: safeUrl
                ? {
                    'data-href': safeUrl,
                    title: 'Cmd/Ctrl+Click to open link',
                  }
                : { title: 'Blocked unsafe or invalid URL' },
            }).range(openBracketEnd, closeBracketStart),
          );

          if (!isRangeActive(state, from, to)) {
            pushReplace(from, openBracketEnd);
            pushReplace(closeBracketStart, to);
          }
        }
        return;
      }

      // 8. Fenced code blocks: ```lang ... ```
      if (name === 'FencedCode') {
        if (overlapsHidden(from, to)) return;
        const firstLine = doc.lineAt(from);
        const lastLine = doc.lineAt(Math.max(from, to - 1));
        const infoNode = node.getChild('CodeInfo');
        const lang = infoNode
          ? doc.sliceString(infoNode.from, infoNode.to).trim().split(/\s+/)[0] ?? ''
          : '';

        for (let ln = firstLine.number; ln <= lastLine.number; ln += 1) {
          const line = doc.line(ln);
          const classes = ['cm-md-codeblock'];
          if (ln === firstLine.number) classes.push('cm-md-codeblock-first');
          if (ln === lastLine.number) classes.push('cm-md-codeblock-last');
          decorations.push(
            Decoration.line({ class: classes.join(' ') }).range(line.from),
          );
        }

        if (!isRangeActive(state, firstLine.from, firstLine.to)) {
          decorations.push(
            Decoration.replace({
              widget: new CodeBlockLangWidget(lang),
            }).range(firstLine.from, firstLine.to),
          );
        }

        if (
          lastLine.number > firstLine.number &&
          /^\s*(`{3,}|~{3,})\s*$/.test(lastLine.text) &&
          !isRangeActive(state, lastLine.from, lastLine.to)
        ) {
          pushReplace(lastLine.from, lastLine.to);
        }
        return;
      }
    },
  });

  // 9. LaTeX math: block ($$$...$$$ or $$...$$) and inline ($...$), skipping code & frontmatter
  const codeSkipRanges: Array<{ from: number; to: number }> = [];
  if (fmRange) {
    codeSkipRanges.push({ from: fmRange.from, to: fmRange.bodyStart });
  }
  syntaxTree(state).iterate({
    enter({ name, from, to }) {
      if (name === 'FencedCode' || name === 'InlineCode' || name === 'CodeBlock') {
        codeSkipRanges.push({ from, to });
      }
    },
  });

  const overlapsCode = (from: number, to: number): boolean =>
    codeSkipRanges.some((r) => from < r.to && to > r.from);

  const mathBlockRanges: Array<{ from: number; to: number }> = [];
  const fullText = doc.toString();

  // Block math: $$$...$$$ or $$...$$
  const blockMathRe = /(\$\$\$|\$\$)([\s\S]+?)\1/g;
  let bm: RegExpExecArray | null;
  while ((bm = blockMathRe.exec(fullText)) !== null) {
    const mFrom = bm.index;
    const mTo = mFrom + bm[0].length;
    const expr = bm[2].trim();
    if (!expr || overlapsCode(mFrom, mTo)) continue;
    mathBlockRanges.push({ from: mFrom, to: mTo });
    if (!isRangeActive(state, mFrom, mTo)) {
      const spansLines = fullText.slice(mFrom, mTo).includes('\n');
      decorations.push(
        Decoration.replace({
          widget: new KatexWidget(expr, true),
          block: spansLines,
        }).range(mFrom, mTo),
      );
    }
  }

  // Inline math: $...$ (single line, not preceded/followed by $, not escaped)
  const inlineMathRe = /(?<!\\)(?<!\$)\$(?!\$)([^$\n]+?)(?<!\\)\$(?!\$)/g;
  let im: RegExpExecArray | null;
  while ((im = inlineMathRe.exec(fullText)) !== null) {
    const mFrom = im.index;
    const mTo = mFrom + im[0].length;
    const expr = im[1].trim();
    if (!expr || overlapsCode(mFrom, mTo)) continue;
    if (mathBlockRanges.some((r) => mFrom < r.to && mTo > r.from)) continue;
    if (!isRangeActive(state, mFrom, mTo)) {
      decorations.push(
        Decoration.replace({
          widget: new KatexWidget(expr, false),
        }).range(mFrom, mTo),
      );
    }
  }

  return Decoration.set(decorations, true);
}

const livePreviewField = StateField.define<DecorationSet>({
  create(state) {
    return buildLivePreviewDecorations(state);
  },
  update(decorations, tr) {
    if (
      tr.docChanged ||
      tr.selection ||
      syntaxTree(tr.startState) !== syntaxTree(tr.state)
    ) {
      return buildLivePreviewDecorations(tr.state);
    }
    return decorations;
  },
  provide(field) {
    return EditorView.decorations.from(field);
  },
});

const linkClickHandler = EditorView.domEventHandlers({
  mousedown(event) {
    if (!(event.metaKey || event.ctrlKey)) return false;
    const target = event.target as HTMLElement | null;
    const linkEl = target?.closest('.cm-md-link') as HTMLElement | null;
    const href = linkEl?.getAttribute('data-href');
    if (!href) return false;

    const safeHref = sanitizeMarkdownUrl(href, 'link');
    if (!safeHref) return false;

    event.preventDefault();
    window.open(safeHref, '_blank', 'noopener,noreferrer');
    return true;
  },
});

const frontmatterAtomicRanges = EditorView.atomicRanges.of((view) => {
  const fmRange = findFrontmatterRange(view.state.doc.toString());
  if (!fmRange || isRangeActive(view.state, fmRange.from, fmRange.to)) {
    return Decoration.none;
  }
  return Decoration.set([
    Decoration.replace({}).range(fmRange.from, fmRange.bodyStart),
  ]);
});

export const markdownLivePreviewExtension: Extension = [
  livePreviewField,
  frontmatterAtomicRanges,
  linkClickHandler,
];

export const atriumEditorTheme: Extension = EditorView.theme({
  '&': {
    backgroundColor: 'transparent',
    color: 'var(--text)',
    fontSize: '1rem',
    lineHeight: '1.75',
    height: '100%',
  },
  '&.cm-focused': {
    outline: 'none',
  },
  '.cm-scroller': {
    fontFamily: 'var(--font-body)',
    lineHeight: '1.75',
    overflow: 'auto',
  },
  '.cm-content': {
    padding: '8px 0 120px',
    caretColor: 'var(--accent)',
  },
  '.cm-line': {
    padding: '1px 2px',
  },
  '.cm-cursor, .cm-dropCursor': {
    borderLeftColor: 'var(--accent)',
    borderLeftWidth: '2px',
  },
  '&.cm-focused .cm-selectionBackground, .cm-selectionBackground, .cm-content ::selection':
    {
      backgroundColor: 'var(--accent-soft)',
    },
  '.cm-activeLine': {
    backgroundColor: 'transparent',
  },
});

const highlightStyle = HighlightStyle.define([
  { tag: tags.heading1, fontWeight: '700' },
  { tag: tags.heading2, fontWeight: '700' },
  { tag: tags.heading3, fontWeight: '650' },
  { tag: tags.heading4, fontWeight: '650' },
  { tag: tags.heading5, fontWeight: '650' },
  { tag: tags.heading6, fontWeight: '600', color: 'var(--text-muted)' },
  { tag: tags.strong, fontWeight: '700' },
  { tag: tags.emphasis, fontStyle: 'italic' },
  { tag: tags.keyword, color: 'var(--syn-keyword)' },
  { tag: [tags.string, tags.special(tags.string)], color: 'var(--syn-string)' },
  { tag: tags.comment, color: 'var(--syn-comment)', fontStyle: 'italic' },
  { tag: [tags.number, tags.bool, tags.null, tags.atom], color: 'var(--syn-number)' },
  { tag: [tags.typeName, tags.className, tags.namespace], color: 'var(--syn-type)' },
  { tag: [tags.function(tags.variableName), tags.function(tags.propertyName)], color: 'var(--syn-func)' },
  { tag: [tags.propertyName, tags.attributeName], color: 'var(--syn-prop)' },
  { tag: [tags.operator, tags.punctuation], color: 'var(--syn-operator)' },
  {
    tag: tags.monospace,
    fontFamily: 'var(--font-mono)',
    fontSize: '0.88em',
    backgroundColor: 'var(--surface-muted)',
    borderRadius: '4px',
    padding: '0.1em 0.35em',
  },
  { tag: tags.quote, color: 'var(--text-muted)', fontStyle: 'italic' },
  { tag: tags.link, color: 'var(--accent)', textDecoration: 'underline' },
  { tag: tags.url, color: 'var(--text-muted)', fontFamily: 'var(--font-mono)', fontSize: '0.88em' },
  { tag: tags.processingInstruction, color: 'var(--text-faint)' },
  { tag: tags.meta, color: 'var(--text-faint)' },
]);

export const atriumMarkdownHighlight: Extension =
  syntaxHighlighting(highlightStyle);

