import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
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
import { readingMarkdownComponents } from '../lib/markdownReading';
import {
  countWordsAndChars,
  DEFAULT_SAMPLE_NOTE,
  loadNotesDraft,
  saveNotesDraft,
} from '../lib/markdownUtils';
import {
  MarkdownEditor,
  type MarkdownEditorHandle,
} from './MarkdownEditor';

export type NotesViewMode = 'live' | 'reading' | 'source';

interface NotesPageProps {
  userId: string;
  onAnnounce: (message: string) => void;
}

export function NotesPage({ userId, onAnnounce }: NotesPageProps) {
  const [content, setContent] = useState<string>(() => loadNotesDraft(userId));
  const [mode, setMode] = useState<NotesViewMode>('live');
  const [editorMode, setEditorMode] = useState<'live' | 'source'>('live');
  const [confirmReset, setConfirmReset] = useState(false);

  const editorRef = useRef<MarkdownEditorHandle | null>(null);
  const modeRef = useRef<NotesViewMode>(mode);
  modeRef.current = mode;

  const pendingSaveRef = useRef<{ userId: string; content: string } | null>(
    null,
  );
  const saveTimerRef = useRef<number | null>(null);

  const switchMode = useCallback((nextMode: NotesViewMode) => {
    modeRef.current = nextMode;
    setMode(nextMode);
    if (nextMode !== 'reading') {
      setEditorMode(nextMode);
      requestAnimationFrame(() => editorRef.current?.focus());
    }
  }, []);

  const handleContentChange = useCallback(
    (nextContent: string) => {
      setContent(nextContent);
      pendingSaveRef.current = { userId, content: nextContent };
      if (saveTimerRef.current !== null) {
        window.clearTimeout(saveTimerRef.current);
      }
      saveTimerRef.current = window.setTimeout(() => {
        saveTimerRef.current = null;
        const pending = pendingSaveRef.current;
        pendingSaveRef.current = null;
        if (pending) {
          saveNotesDraft(pending.content, pending.userId);
        }
      }, 250);
    },
    [userId],
  );

  useEffect(() => {
    const flushPending = () => {
      if (saveTimerRef.current !== null) {
        window.clearTimeout(saveTimerRef.current);
        saveTimerRef.current = null;
      }
      const pending = pendingSaveRef.current;
      pendingSaveRef.current = null;
      if (pending) {
        saveNotesDraft(pending.content, pending.userId);
      }
    };

    window.addEventListener('pagehide', flushPending);
    return () => {
      window.removeEventListener('pagehide', flushPending);
      flushPending();
    };
  }, []);

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
        switchMode(modeRef.current === 'reading' ? 'live' : 'reading');
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [switchMode]);

  const stats = useMemo(() => countWordsAndChars(content), [content]);

  const handleResetSample = () => {
    if (saveTimerRef.current !== null) {
      window.clearTimeout(saveTimerRef.current);
      saveTimerRef.current = null;
    }
    pendingSaveRef.current = null;
    setContent(DEFAULT_SAMPLE_NOTE);
    saveNotesDraft(DEFAULT_SAMPLE_NOTE, userId);
    setConfirmReset(false);
    onAnnounce('Restored sample Markdown note');
  };

  return (
    <main className="notes-page-shell" aria-label="Markdown Notes">
      <div className="notes-toolbar-bar">
        <div className="notes-toolbar-inner">
          <div
            className="notes-mode-switcher"
            role="group"
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
                aria-pressed={mode === opt.id}
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
                <ReactMarkdown components={readingMarkdownComponents}>
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
              onChange={handleContentChange}
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

