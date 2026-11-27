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
import { sanitizeMarkdownUrl } from './markdownUtils';

class HorizontalRuleWidget extends WidgetType {
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
    img.src = this.safeUrl;
    img.alt = this.alt;
    img.loading = 'lazy';
    img.decoding = 'async';

    img.addEventListener('error', () => {
      figure.innerHTML = '';
      const fallback = document.createElement('div');
      fallback.className = 'cm-md-image-fallback';
      fallback.textContent = this.alt
        ? `Image failed to load: ${this.alt}`
        : `Image failed to load (${this.rawUrl})`;
      figure.appendChild(fallback);
    });

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

  const isInsideHidden = (from: number, to: number): boolean =>
    hiddenRanges.some((r) => from >= r.from && to <= r.to);

  const pushHidden = (from: number, to: number) => {
    if (to <= from) return;
    if (isInsideHidden(from, to)) return;
    hiddenRanges.push({ from, to });
    decorations.push(Decoration.replace({}).range(from, to));
  };

  syntaxTree(state).iterate({
    enter(nodeRef) {
      const { name, from, to, node } = nodeRef;

      if (isInsideHidden(from, to)) {
        return false;
      }

      // 1. Headings (ATXHeading1 .. ATXHeading6)
      if (name.startsWith('ATXHeading')) {
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
                  pushHidden(
                    child.from,
                    nextChar === ' ' ? child.to + 1 : child.to,
                  );
                } else {
                  const prevChar =
                    child.from > line.from
                      ? doc.sliceString(child.from - 1, child.from)
                      : '';
                  pushHidden(
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
        decorations.push(
          Decoration.mark({ class: 'cm-md-strong' }).range(from, to),
        );
        if (!isRangeActive(state, from, to)) {
          let child = node.firstChild;
          while (child) {
            if (child.name === 'EmphasisMark') {
              pushHidden(child.from, child.to);
            }
            child = child.nextSibling;
          }
        }
        return;
      }

      // 3. Italic (Emphasis)
      if (name === 'Emphasis') {
        decorations.push(
          Decoration.mark({ class: 'cm-md-em' }).range(from, to),
        );
        if (!isRangeActive(state, from, to)) {
          let child = node.firstChild;
          while (child) {
            if (child.name === 'EmphasisMark') {
              pushHidden(child.from, child.to);
            }
            child = child.nextSibling;
          }
        }
        return;
      }

      // 4. Blockquotes
      if (name === 'Blockquote') {
        const startLine = doc.lineAt(from).number;
        const endLine = doc.lineAt(to).number;
        for (let lineNo = startLine; lineNo <= endLine; lineNo += 1) {
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
        const line = doc.lineAt(from);
        if (!isRangeActive(state, line.from, line.to)) {
          const nextChar =
            to < line.to ? doc.sliceString(to, to + 1) : '';
          pushHidden(from, nextChar === ' ' ? to + 1 : to);
        }
        return;
      }

      // 5. Horizontal Rule
      if (name === 'HorizontalRule') {
        const line = doc.lineAt(from);
        if (!isRangeActive(state, line.from, line.to)) {
          hiddenRanges.push({ from, to });
          decorations.push(
            Decoration.replace({
              widget: new HorizontalRuleWidget(),
            }).range(from, to),
          );
          return false;
        }
        return;
      }

      // 6. Images: ![alt](url)
      if (name === 'Image') {
        const startLine = doc.lineAt(from);
        const endLine = doc.lineAt(to);
        const urlNode = node.getChild('URL');
        const rawUrl = urlNode ? doc.sliceString(urlNode.from, urlNode.to) : '';
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
          hiddenRanges.push({ from, to });
          decorations.push(
            Decoration.replace({
              widget: new ImageWidget(alt, safeUrl, rawUrl),
            }).range(from, to),
          );
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
            pushHidden(from, openBracketEnd);
            pushHidden(closeBracketStart, to);
          }
        }
        return;
      }
    },
  });

  return Decoration.set(decorations, true);
}

const livePreviewField = StateField.define<DecorationSet>({
  create(state) {
    return buildLivePreviewDecorations(state);
  },
  update(decorations, tr) {
    if (tr.docChanged || tr.selection) {
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

export const markdownLivePreviewExtension: Extension = [
  livePreviewField,
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
  { tag: tags.quote, color: 'var(--text-muted)', fontStyle: 'italic' },
  { tag: tags.link, color: 'var(--accent)', textDecoration: 'underline' },
  { tag: tags.url, color: 'var(--text-muted)', fontFamily: 'var(--font-mono)', fontSize: '0.88em' },
  { tag: tags.processingInstruction, color: 'var(--text-faint)' },
  { tag: tags.meta, color: 'var(--text-faint)' },
]);

export const atriumMarkdownHighlight: Extension =
  syntaxHighlighting(highlightStyle);

