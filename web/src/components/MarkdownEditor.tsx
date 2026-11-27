import { defaultKeymap, history, historyKeymap } from '@codemirror/commands';
import { markdown } from '@codemirror/lang-markdown';
import { languages } from '@codemirror/language-data';
import {
  Annotation,
  Compartment,
  EditorSelection,
  EditorState,
  type Range,
} from '@codemirror/state';
import {
  Decoration,
  EditorView,
  keymap,
  placeholder as cmPlaceholder,
  type Command,
} from '@codemirror/view';
import {
  forwardRef,
  useEffect,
  useImperativeHandle,
  useRef,
} from 'react';
import { findFrontmatterRange } from '../lib/frontmatter';
import { markdownFormattingKeymap } from '../lib/markdownCommands';
import {
  atriumEditorTheme,
  atriumMarkdownHighlight,
  markdownLivePreviewExtension,
} from '../lib/markdownLivePreview';

export type EditorDisplayMode = 'live' | 'source';

export interface MarkdownEditorHandle {
  runCommand: (command: Command) => void;
  focus: () => void;
  scrollToMatch: (index: number) => void;
  replaceCurrentMatch: (replacement: string, index: number) => boolean;
  replaceAllMatches: (replacement: string) => number;
  insertImageMarkdown: (alt: string, url: string) => void;
}

interface MarkdownEditorProps {
  value: string;
  mode: EditorDisplayMode;
  highlightQuery?: string;
  caseSensitive?: boolean;
  activeMatchIndex?: number;
  onMatchCountChange?: (count: number) => void;
  onUploadInlineImage?: (
    file: File,
  ) => Promise<{ url: string; alt: string } | null>;
  onChange: (nextValue: string) => void;
}

const externalSyncAnnotation = Annotation.define<boolean>();

function clampInitialCursorForMode(doc: string, mode: EditorDisplayMode): number {
  if (mode !== 'live') return 0;
  const fm = findFrontmatterRange(doc);
  return fm ? Math.min(fm.bodyStart, doc.length) : 0;
}

function findMatchRanges(
  doc: string,
  query: string,
  mode: EditorDisplayMode,
  caseSensitive: boolean,
  maxMatches = 500,
): Array<{ from: number; to: number }> {
  if (!query) return [];

  const needle = caseSensitive ? query : query.toLowerCase();
  const startOffset =
    mode === 'live' ? (findFrontmatterRange(doc)?.bodyStart ?? 0) : 0;
  const haystack = caseSensitive ? doc : doc.toLowerCase();
  const matches: Array<{ from: number; to: number }> = [];
  let pos = startOffset;

  while (pos <= haystack.length - needle.length) {
    const found = haystack.indexOf(needle, pos);
    if (found === -1) break;
    matches.push({ from: found, to: found + needle.length });
    pos = found + needle.length;
    if (matches.length >= maxMatches) break;
  }

  return matches;
}

function buildFindHighlightExtension(
  matches: Array<{ from: number; to: number }>,
  activeIndex: number,
) {
  if (matches.length === 0) return [];
  const marks: Range<Decoration>[] = matches.map((m, idx) =>
    Decoration.mark({
      class: idx === activeIndex ? 'cm-find-match-active' : 'cm-find-match',
    }).range(m.from, m.to),
  );
  return EditorView.decorations.of(Decoration.set(marks, true));
}

export const MarkdownEditor = forwardRef<
  MarkdownEditorHandle,
  MarkdownEditorProps
>(function MarkdownEditor(
  {
    value,
    mode,
    highlightQuery = '',
    caseSensitive = false,
    activeMatchIndex = 0,
    onMatchCountChange,
    onUploadInlineImage,
    onChange,
  },
  ref,
) {
  const hostRef = useRef<HTMLDivElement | null>(null);
  const viewRef = useRef<EditorView | null>(null);
  const liveCompartmentRef = useRef(new Compartment());
  const findCompartmentRef = useRef(new Compartment());
  const matchesRef = useRef<Array<{ from: number; to: number }>>([]);
  const onChangeRef = useRef(onChange);
  const onMatchCountChangeRef = useRef(onMatchCountChange);
  const onUploadInlineImageRef = useRef(onUploadInlineImage);
  const initialValueRef = useRef(value);
  const initialModeRef = useRef(mode);
  const modeRef = useRef(mode);
  const highlightQueryRef = useRef(highlightQuery);
  const caseSensitiveRef = useRef(caseSensitive);

  onChangeRef.current = onChange;
  onMatchCountChangeRef.current = onMatchCountChange;
  onUploadInlineImageRef.current = onUploadInlineImage;
  modeRef.current = mode;
  highlightQueryRef.current = highlightQuery;
  caseSensitiveRef.current = caseSensitive;

  const scrollToMatchAt = (index: number) => {
    const view = viewRef.current;
    const matches = matchesRef.current;
    if (!view || matches.length === 0) return;
    const clamped =
      ((index % matches.length) + matches.length) % matches.length;
    const target = matches[clamped];
    view.dispatch({
      effects: EditorView.scrollIntoView(target.from, { y: 'center' }),
    });
  };

  useImperativeHandle(
    ref,
    () => ({
      runCommand(command: Command) {
        const view = viewRef.current;
        if (!view) return;
        command(view);
      },
      focus() {
        viewRef.current?.focus();
      },
      scrollToMatch(index: number) {
        scrollToMatchAt(index);
      },
      replaceCurrentMatch(replacement: string, index: number): boolean {
        const view = viewRef.current;
        const matches = matchesRef.current;
        if (!view || matches.length === 0) return false;
        const clamped =
          ((index % matches.length) + matches.length) % matches.length;
        const target = matches[clamped];
        view.dispatch({
          changes: { from: target.from, to: target.to, insert: replacement },
          selection: EditorSelection.cursor(target.from + replacement.length),
          effects: EditorView.scrollIntoView(target.from, { y: 'center' }),
        });
        return true;
      },
      replaceAllMatches(replacement: string): number {
        const view = viewRef.current;
        const query = highlightQueryRef.current;
        if (!view || !query) return 0;
        const doc = view.state.doc.toString();
        const allMatches = findMatchRanges(
          doc,
          query,
          modeRef.current,
          caseSensitiveRef.current,
          Number.POSITIVE_INFINITY,
        );
        if (allMatches.length === 0) return 0;
        view.dispatch({
          changes: allMatches.map((m) => ({
            from: m.from,
            to: m.to,
            insert: replacement,
          })),
        });
        return allMatches.length;
      },
      insertImageMarkdown(alt: string, url: string) {
        const view = viewRef.current;
        if (!view) return;
        const safeAlt = alt.replace(/[\[\]\r\n]/g, '').trim() || 'image';
        const snippet = `![${safeAlt}](${url})`;
        const { from, to } = view.state.selection.main;
        view.dispatch({
          changes: { from, to, insert: snippet },
          selection: EditorSelection.cursor(from + snippet.length),
          scrollIntoView: true,
        });
        view.focus();
      },
    }),
    [],
  );

  // Mount CodeMirror once and preserve undo history / cursor across Live Preview <-> Source toggles
  useEffect(() => {
    const host = hostRef.current;
    if (!host) return;

    const liveCompartment = liveCompartmentRef.current;
    const findCompartment = findCompartmentRef.current;
    const initialCursor = clampInitialCursorForMode(
      initialValueRef.current,
      initialModeRef.current,
    );

    const state = EditorState.create({
      doc: initialValueRef.current,
      selection: EditorSelection.cursor(initialCursor),
      extensions: [
        history(),
        markdownFormattingKeymap,
        keymap.of([...defaultKeymap, ...historyKeymap]),
        markdown({ codeLanguages: languages }),
        EditorView.lineWrapping,
        atriumEditorTheme,
        atriumMarkdownHighlight,
        cmPlaceholder('Start writing in Markdown…'),
        liveCompartment.of(
          initialModeRef.current === 'live' ? markdownLivePreviewExtension : [],
        ),
        findCompartment.of([]),
        EditorView.domEventHandlers({
          paste(event, view) {
            const uploader = onUploadInlineImageRef.current;
            if (!uploader) return false;
            const files = Array.from(event.clipboardData?.files ?? []).filter(
              (f) => /^image\/(png|jpeg|webp|gif)$/i.test(f.type),
            );
            if (files.length === 0) return false;
            event.preventDefault();
            const insertAt = view.state.selection.main.from;
            void (async () => {
              let offset = insertAt;
              for (const file of files) {
                const uploaded = await uploader(file);
                if (!uploaded) continue;
                const safeAlt =
                  uploaded.alt.replace(/[\[\]\r\n]/g, '').trim() || 'image';
                const snippet = `![${safeAlt}](${uploaded.url})\n`;
                const docLen = view.state.doc.length;
                const pos = Math.min(offset, docLen);
                view.dispatch({
                  changes: { from: pos, to: pos, insert: snippet },
                  selection: EditorSelection.cursor(pos + snippet.length),
                  scrollIntoView: true,
                });
                offset = pos + snippet.length;
              }
            })();
            return true;
          },
          drop(event, view) {
            const uploader = onUploadInlineImageRef.current;
            if (!uploader) return false;
            const files = Array.from(event.dataTransfer?.files ?? []).filter(
              (f) => /^image\/(png|jpeg|webp|gif)$/i.test(f.type),
            );
            if (files.length === 0) return false;
            event.preventDefault();
            const coords = view.posAtCoords({
              x: event.clientX,
              y: event.clientY,
            });
            const insertAt = coords ?? view.state.selection.main.from;
            void (async () => {
              let offset = insertAt;
              for (const file of files) {
                const uploaded = await uploader(file);
                if (!uploaded) continue;
                const safeAlt =
                  uploaded.alt.replace(/[\[\]\r\n]/g, '').trim() || 'image';
                const snippet = `![${safeAlt}](${uploaded.url})\n`;
                const docLen = view.state.doc.length;
                const pos = Math.min(offset, docLen);
                view.dispatch({
                  changes: { from: pos, to: pos, insert: snippet },
                  selection: EditorSelection.cursor(pos + snippet.length),
                  scrollIntoView: true,
                });
                offset = pos + snippet.length;
              }
            })();
            return true;
          },
        }),
        EditorView.updateListener.of((update) => {
          if (
            update.docChanged &&
            !update.transactions.every((tr) =>
              Boolean(tr.annotation(externalSyncAnnotation)),
            )
          ) {
            onChangeRef.current(update.state.doc.toString());
          }
        }),
      ],
    });

    const view = new EditorView({ state, parent: host });
    viewRef.current = view;

    return () => {
      view.destroy();
      viewRef.current = null;
    };
  }, []);

  // Reconfigure Live Preview compartment dynamically on mode switch
  useEffect(() => {
    const view = viewRef.current;
    if (!view) return;
    const doc = view.state.doc.toString();
    const minCursor = clampInitialCursorForMode(doc, mode);
    const currentHead = view.state.selection.main.head;
    const needsCursorClamp = mode === 'live' && currentHead < minCursor;

    view.dispatch({
      effects: liveCompartmentRef.current.reconfigure(
        mode === 'live' ? markdownLivePreviewExtension : [],
      ),
      selection: needsCursorClamp
        ? EditorSelection.cursor(minCursor)
        : undefined,
    });
  }, [mode]);

  // Sync external document replacements (e.g. Properties tag/title update, conflict reload)
  useEffect(() => {
    const view = viewRef.current;
    if (!view) return;
    const current = view.state.doc.toString();
    if (value !== current) {
      const oldFm = findFrontmatterRange(current);
      const newFm = findFrontmatterRange(value);
      const oldBodyStart = oldFm ? oldFm.bodyStart : 0;
      const newBodyStart = newFm ? newFm.bodyStart : 0;
      const oldHead = view.state.selection.main.head;
      const relativeInBody = Math.max(0, oldHead - oldBodyStart);
      const nextCursor =
        modeRef.current === 'live'
          ? Math.min(value.length, newBodyStart + relativeInBody)
          : Math.min(value.length, oldHead);

      view.dispatch({
        changes: { from: 0, to: current.length, insert: value },
        selection: EditorSelection.cursor(nextCursor),
        annotations: externalSyncAnnotation.of(true),
      });
    }
  }, [value]);

  // Recompute search highlights and scroll active match into view
  useEffect(() => {
    const view = viewRef.current;
    if (!view) return;

    const doc = view.state.doc.toString();
    const matches = findMatchRanges(doc, highlightQuery, mode, caseSensitive);
    matchesRef.current = matches;
    onMatchCountChangeRef.current?.(matches.length);

    const clampedIndex =
      matches.length > 0
        ? ((activeMatchIndex % matches.length) + matches.length) % matches.length
        : 0;

    view.dispatch({
      effects: [
        findCompartmentRef.current.reconfigure(
          buildFindHighlightExtension(matches, clampedIndex),
        ),
        ...(matches.length > 0
          ? [EditorView.scrollIntoView(matches[clampedIndex].from, { y: 'center' })]
          : []),
      ],
    });
  }, [activeMatchIndex, caseSensitive, highlightQuery, mode, value]);

  return (
    <div
      ref={hostRef}
      className={`notes-cm-host is-${mode}`}
      aria-label="Markdown editor"
    />
  );
});
