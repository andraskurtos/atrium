import {
  Children,
  cloneElement,
  isValidElement,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactElement,
  type ReactNode,
} from 'react';
import ReactMarkdown from 'react-markdown';
import {
  insertCallout,
  insertHorizontalRule,
  insertImage,
  insertLink,
  setHeading,
  toggleBlockquote,
  toggleBold,
  toggleItalic,
} from '../lib/markdownCommands';
import {
  countWordsAndChars,
  DEFAULT_SAMPLE_NOTE,
  formatCalloutDefaultTitle,
  loadNotesDraft,
  resolveCalloutTone,
  sanitizeMarkdownUrl,
  saveNotesDraft,
  type CalloutTone,
} from '../lib/markdownUtils';
import {
  MarkdownEditor,
  type MarkdownEditorHandle,
} from './MarkdownEditor';

export type NotesViewMode = 'live' | 'reading' | 'source';

interface NotesPageProps {
  onAnnounce: (message: string) => void;
}

const CALLOUT_GLYPHS: Record<CalloutTone, string> = {
  note: 'ℹ',
  tip: '✦',
  warning: '⚠',
  danger: '✕',
  quote: '❝',
};

function renderReadingBlockquote(children: ReactNode) {
  const childArray = Children.toArray(children).filter(
    (c) => !(typeof c === 'string' && c.trim() === ''),
  );
  const first = childArray[0];

  if (isValidElement<{ children?: ReactNode }>(first)) {
    const firstChildren = Children.toArray(first.props.children);
    const firstText = firstChildren[0];
    if (typeof firstText === 'string') {
      const match = firstText.match(
        /^\[!([a-zA-Z0-9_-]+)\][+-]?(?:[ \t]+([^\n]*))?(?:\n([\s\S]*))?$/,
      );
      if (match) {
        const rawType = match[1];
        const tone = resolveCalloutTone(rawType);
        const title =
          (match[2] ?? '').trim() || formatCalloutDefaultTitle(rawType);
        const remainderFirstLine = match[3] ?? '';
        const restOfFirstParagraph = firstChildren.slice(1);
        const bodyNodes: ReactNode[] = [];

        if (remainderFirstLine || restOfFirstParagraph.length > 0) {
          bodyNodes.push(
            cloneElement(first as ReactElement<{ children?: ReactNode }>, {
              key: 'callout-first-body',
              children: [
                ...(remainderFirstLine ? [remainderFirstLine] : []),
                ...restOfFirstParagraph,
              ],
            }),
          );
        }
        bodyNodes.push(...childArray.slice(1));

        return (
          <div className={`notes-callout is-${tone}`}>
            <div className={`notes-callout-header is-${tone}`}>
              <span className="cm-md-callout-icon" aria-hidden="true">
                {CALLOUT_GLYPHS[tone]}
              </span>
              <span>{title}</span>
            </div>
            {bodyNodes.length > 0 && (
              <div className="notes-callout-body">{bodyNodes}</div>
            )}
          </div>
        );
      }
    }
  }

  return <blockquote>{children}</blockquote>;
}

function SafeReadingImage({
  src,
  alt,
}: {
  src?: string;
  alt?: string;
}) {
  const [failed, setFailed] = useState(false);
  const safeSrc = src ? sanitizeMarkdownUrl(src, 'image') : null;

  if (!safeSrc || failed) {
    return (
      <span className="cm-md-image-fallback">
        {!safeSrc
          ? alt
            ? `Blocked or invalid image URL (${alt})`
            : 'Blocked or invalid image URL'
          : alt
            ? `Image failed to load: ${alt}`
            : 'Image failed to load'}
      </span>
    );
  }

  return (
    <figure className="cm-md-image-figure">
      <img
        className="cm-md-image"
        src={safeSrc}
        alt={alt ?? ''}
        loading="lazy"
        decoding="async"
        onError={() => setFailed(true)}
      />
      {alt && alt.trim().length > 0 && (
        <figcaption className="cm-md-image-caption">{alt}</figcaption>
      )}
    </figure>
  );
}

export function NotesPage({ onAnnounce }: NotesPageProps) {
  const [content, setContent] = useState<string>(loadNotesDraft);
  const [mode, setMode] = useState<NotesViewMode>('live');
  const [editorMode, setEditorMode] = useState<'live' | 'source'>('live');
  const [confirmReset, setConfirmReset] = useState(false);

  const editorRef = useRef<MarkdownEditorHandle | null>(null);

  const switchMode = (nextMode: NotesViewMode) => {
    setMode(nextMode);
    if (nextMode !== 'reading') {
      setEditorMode(nextMode);
      requestAnimationFrame(() => editorRef.current?.focus());
    }
  };

  useEffect(() => {
    saveNotesDraft(content);
  }, [content]);

  // Cmd/Ctrl+E toggles between Live Preview and Reading mode (Obsidian convention)
  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (
        (event.metaKey || event.ctrlKey) &&
        !event.altKey &&
        !event.shiftKey &&
        event.key.toLowerCase() === 'e'
      ) {
        if (document.querySelector('dialog[open]')) {
          return;
        }
        event.preventDefault();
        setMode((prev) => {
          const next = prev === 'reading' ? 'live' : 'reading';
          if (next === 'live') {
            setEditorMode('live');
            requestAnimationFrame(() => editorRef.current?.focus());
          }
          return next;
        });
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, []);

  const stats = useMemo(() => countWordsAndChars(content), [content]);

  const handleResetSample = () => {
    setContent(DEFAULT_SAMPLE_NOTE);
    setConfirmReset(false);
    onAnnounce('Restored sample Markdown note');
  };

  return (
    <main className="notes-page-shell" aria-label="Markdown Notes">
      <div className="notes-toolbar-bar">
        <div className="notes-toolbar-inner">
          <div
            className="notes-mode-switcher"
            role="radiogroup"
            aria-label="Editor view mode"
          >
            {(
              [
                { id: 'live', label: 'Live Preview' },
                { id: 'reading', label: 'Reading' },
                { id: 'source', label: 'Source' },
              ] as const
            ).map((opt) => (
              <button
                key={opt.id}
                id={`notes-mode-${opt.id}`}
                type="button"
                role="radio"
                aria-checked={mode === opt.id}
                className={`mode-chip ${mode === opt.id ? 'is-active' : ''}`}
                onClick={() => switchMode(opt.id)}
              >
                {opt.label}
              </button>
            ))}
          </div>

          {mode !== 'reading' && (
            <div
              className="notes-format-toolbar"
              role="toolbar"
              aria-label="Markdown formatting"
            >
              <button
                type="button"
                className="btn btn-ghost btn-xs notes-fmt-btn"
                title="Heading 1"
                onClick={() => editorRef.current?.runCommand(setHeading(1))}
              >
                H1
              </button>
              <button
                type="button"
                className="btn btn-ghost btn-xs notes-fmt-btn"
                title="Heading 2"
                onClick={() => editorRef.current?.runCommand(setHeading(2))}
              >
                H2
              </button>
              <button
                type="button"
                className="btn btn-ghost btn-xs notes-fmt-btn"
                title="Heading 3"
                onClick={() => editorRef.current?.runCommand(setHeading(3))}
              >
                H3
              </button>
              <span className="notes-toolbar-sep" aria-hidden="true" />
              <button
                type="button"
                className="btn btn-ghost btn-xs notes-fmt-btn"
                title="Bold (Cmd/Ctrl+B)"
                onClick={() => editorRef.current?.runCommand(toggleBold)}
              >
                <strong>B</strong>
              </button>
              <button
                type="button"
                className="btn btn-ghost btn-xs notes-fmt-btn"
                title="Italic (Cmd/Ctrl+I)"
                onClick={() => editorRef.current?.runCommand(toggleItalic)}
              >
                <em>I</em>
              </button>
              <button
                type="button"
                className="btn btn-ghost btn-xs notes-fmt-btn"
                title="Blockquote"
                onClick={() => editorRef.current?.runCommand(toggleBlockquote)}
              >
                Quote
              </button>
              <button
                type="button"
                className="btn btn-ghost btn-xs notes-fmt-btn"
                title="Insert callout (> [!note])"
                onClick={() => editorRef.current?.runCommand(insertCallout)}
              >
                Callout
              </button>
              <span className="notes-toolbar-sep" aria-hidden="true" />
              <button
                type="button"
                className="btn btn-ghost btn-xs notes-fmt-btn"
                title="Insert link (Cmd/Ctrl+Shift+K)"
                onClick={() => editorRef.current?.runCommand(insertLink)}
              >
                Link
              </button>
              <button
                type="button"
                className="btn btn-ghost btn-xs notes-fmt-btn"
                title="Insert image URL"
                onClick={() => editorRef.current?.runCommand(insertImage)}
              >
                Image
              </button>
              <button
                type="button"
                className="btn btn-ghost btn-xs notes-fmt-btn"
                title="Horizontal rule"
                onClick={() =>
                  editorRef.current?.runCommand(insertHorizontalRule)
                }
              >
                HR
              </button>
            </div>
          )}

          <div className="notes-toolbar-right">
            {content !== DEFAULT_SAMPLE_NOTE &&
              (confirmReset ? (
                <div className="uni-inline-confirm">
                  <button
                    type="button"
                    className="btn btn-danger btn-xs"
                    onClick={handleResetSample}
                  >
                    Reset
                  </button>
                  <button
                    type="button"
                    className="btn btn-ghost btn-xs"
                    onClick={() => setConfirmReset(false)}
                  >
                    Cancel
                  </button>
                </div>
              ) : (
                <button
                  type="button"
                  className="btn btn-ghost btn-xs"
                  onClick={() => setConfirmReset(true)}
                  title="Restore starter sample note"
                >
                  Reset sample
                </button>
              ))}
          </div>
        </div>
      </div>

      <div className="notes-canvas-scroll">
        <div className="notes-canvas">
          {mode === 'reading' && (
            <article
              className="notes-reading-prose"
              aria-label="Rendered Markdown note"
            >
              {content.trim().length === 0 ? (
                <p className="meta-text empty-state">
                  Nothing to preview yet. Switch to Live Preview or Source to
                  start writing.
                </p>
              ) : (
                <ReactMarkdown
                  components={{
                    blockquote({ children }) {
                      return renderReadingBlockquote(children);
                    },
                    a({ href, children }) {
                      const safeHref = href
                        ? sanitizeMarkdownUrl(href, 'link')
                        : null;
                      if (!safeHref) {
                        return (
                          <span
                            className="cm-md-link-invalid"
                            title="Blocked unsafe or invalid URL"
                          >
                            {children}
                          </span>
                        );
                      }
                      return (
                        <a
                          href={safeHref}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="notes-reading-link"
                        >
                          {children}
                        </a>
                      );
                    },
                    img({ src, alt }) {
                      return (
                        <SafeReadingImage
                          key={src ?? ''}
                          src={src}
                          alt={alt}
                        />
                      );
                    },
                  }}
                >
                  {content}
                </ReactMarkdown>
              )}
            </article>
          )}
          <div className="notes-editor-wrap" hidden={mode === 'reading'}>
            <MarkdownEditor
              ref={editorRef}
              value={content}
              mode={editorMode}
              onChange={setContent}
            />
          </div>
        </div>
      </div>

      <footer className="notes-statusbar">
        <div className="notes-statusbar-inner">
          <span className="meta-text mono-num">
            {stats.words} {stats.words === 1 ? 'word' : 'words'} · {stats.chars}{' '}
            {stats.chars === 1 ? 'char' : 'chars'}
          </span>
          <span className="meta-text">
            {mode === 'live'
              ? 'Live Preview · Cmd/Ctrl+E for Reading · Local draft'
              : mode === 'reading'
                ? 'Reading mode · Cmd/Ctrl+E to edit'
                : 'Source mode · Cmd/Ctrl+E for Reading'}
          </span>
        </div>
      </footer>
    </main>
  );
}

