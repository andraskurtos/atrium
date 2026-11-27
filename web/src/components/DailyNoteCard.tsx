import {
  lazy,
  Suspense,
  useCallback,
  useEffect,
  useRef,
  useState,
  type MouseEvent,
} from 'react';

const MarkdownEditor = lazy(() =>
  import('./MarkdownEditor').then((m) => ({ default: m.MarkdownEditor })),
);

const MAX_NOTE_LENGTH = 10_000;
const AUTOSAVE_DELAY_MS = 600;

interface DailyNoteCardProps {
  dateKey: string;
  dateLabel: string;
  initialContent: string;
  loading: boolean;
  loadError: string | null;
  onRetryLoad: () => void;
  onOpenInNotes?: (dateKey: string) => void;
  onSaveNote: (
    dateKey: string,
    content: string,
    options?: { background?: boolean },
  ) => Promise<void>;
}

type SaveState = 'idle' | 'saving' | 'saved' | 'error';

export function DailyNoteCard({
  dateKey,
  dateLabel,
  initialContent,
  loading,
  loadError,
  onRetryLoad,
  onOpenInNotes,
  onSaveNote,
}: DailyNoteCardProps) {
  const [draft, setDraft] = useState(initialContent);
  const [saveState, setSaveState] = useState<SaveState>('idle');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const dirtyRef = useRef(false);
  const draftRef = useRef(initialContent);
  const dateKeyRef = useRef(dateKey);
  const timerRef = useRef<number | null>(null);
  const editSeqRef = useRef(0);
  const saveSeqRef = useRef(0);
  const onSaveNoteRef = useRef(onSaveNote);

  onSaveNoteRef.current = onSaveNote;

  const clearTimer = useCallback(() => {
    if (timerRef.current !== null) {
      window.clearTimeout(timerRef.current);
      timerRef.current = null;
    }
  }, []);

  const persist = useCallback(
    async (
      targetDateKey: string,
      content: string,
      editSeqAtStart: number,
      trackUiState: boolean,
    ) => {
      const seq = ++saveSeqRef.current;
      if (trackUiState) {
        setSaveState('saving');
        setErrorMessage(null);
      }
      try {
        await onSaveNoteRef.current(targetDateKey, content, {
          background: !trackUiState,
        });
        if (
          trackUiState &&
          seq === saveSeqRef.current &&
          dateKeyRef.current === targetDateKey &&
          editSeqRef.current === editSeqAtStart
        ) {
          dirtyRef.current = false;
          setSaveState('saved');
        }
      } catch (err) {
        if (
          trackUiState &&
          seq === saveSeqRef.current &&
          dateKeyRef.current === targetDateKey
        ) {
          setSaveState('error');
          setErrorMessage(
            err instanceof Error ? err.message : 'Could not save daily note.',
          );
        }
      }
    },
    [],
  );

  // Flush dirty note to the previous date when navigating to a new day
  useEffect(() => {
    const previousDateKey = dateKeyRef.current;
    if (previousDateKey !== dateKey) {
      clearTimer();
      if (dirtyRef.current) {
        const previousDraft = draftRef.current;
        const prevEditSeq = editSeqRef.current;
        dirtyRef.current = false;
        void persist(previousDateKey, previousDraft, prevEditSeq, false);
      }
      dateKeyRef.current = dateKey;
      editSeqRef.current += 1;
      draftRef.current = initialContent;
      setDraft(initialContent);
      setSaveState('idle');
      setErrorMessage(null);
    } else if (!dirtyRef.current && draftRef.current !== initialContent) {
      draftRef.current = initialContent;
      setDraft(initialContent);
    }
  }, [clearTimer, dateKey, initialContent, persist]);

  // Flush pending dirty edit on unmount
  useEffect(() => {
    return () => {
      clearTimer();
      if (dirtyRef.current) {
        const pendingDate = dateKeyRef.current;
        const pendingDraft = draftRef.current;
        dirtyRef.current = false;
        void onSaveNoteRef
          .current(pendingDate, pendingDraft, { background: true })
          .catch(() => undefined);
      }
    };
  }, [clearTimer]);

  const handleChange = (nextValue: string) => {
    const clamped =
      nextValue.length > MAX_NOTE_LENGTH
        ? nextValue.slice(0, MAX_NOTE_LENGTH)
        : nextValue;
    setDraft(clamped);
    draftRef.current = clamped;
    dirtyRef.current = true;
    editSeqRef.current += 1;
    setSaveState('idle');
    setErrorMessage(null);
    clearTimer();
    const capturedEditSeq = editSeqRef.current;
    timerRef.current = window.setTimeout(() => {
      timerRef.current = null;
      if (dirtyRef.current) {
        void persist(
          dateKeyRef.current,
          draftRef.current,
          capturedEditSeq,
          true,
        );
      }
    }, AUTOSAVE_DELAY_MS);
  };

  const handleBlur = () => {
    clearTimer();
    if (dirtyRef.current) {
      void persist(
        dateKeyRef.current,
        draftRef.current,
        editSeqRef.current,
        true,
      );
    }
  };

  const handleRetry = () => {
    void persist(
      dateKeyRef.current,
      draftRef.current,
      editSeqRef.current,
      true,
    );
  };

  const handleCardDoubleClick = (e: MouseEvent<HTMLElement>) => {
    if (!onOpenInNotes || loading || Boolean(loadError)) return;
    const target = e.target as HTMLElement | null;
    if (target?.closest('.daily-note-content, button')) {
      return;
    }

    clearTimer();
    if (dirtyRef.current) {
      const pendingDate = dateKeyRef.current;
      const pendingDraft = draftRef.current;
      dirtyRef.current = false;
      void onSaveNoteRef
        .current(pendingDate, pendingDraft, { background: true })
        .catch(() => undefined)
        .finally(() => onOpenInNotes(pendingDate));
      return;
    }

    onOpenInNotes(dateKeyRef.current);
  };

  const isDisabled = loading || Boolean(loadError);

  return (
    <section
      className="card daily-note-card"
      aria-labelledby="daily-note-heading"
      title={
        onOpenInNotes
          ? 'Double-click the card border to open full Markdown note'
          : undefined
      }
      onDoubleClick={handleCardDoubleClick}
    >
      <div className="card-header">
        <h2 id="daily-note-heading" className="card-title">
          Daily Note
        </h2>
        <div className="daily-note-header-meta">
          {draft.length > 8500 && (
            <span className="meta-text mono-num">
              {draft.length} / {MAX_NOTE_LENGTH}
            </span>
          )}
          {loading && (
            <span className="pill-neutral daily-note-status">Loading…</span>
          )}
          {!loading && saveState === 'saving' && (
            <span className="pill-neutral daily-note-status">Saving…</span>
          )}
          {!loading && saveState === 'saved' && (
            <span className="pill-neutral daily-note-status">Saved</span>
          )}
          {!loading && saveState === 'error' && (
            <button
              type="button"
              className="btn btn-ghost btn-xs daily-note-retry"
              onClick={handleRetry}
            >
              Retry save
            </button>
          )}
          {loadError && (
            <button
              type="button"
              className="btn btn-ghost btn-xs daily-note-retry"
              onClick={onRetryLoad}
            >
              Retry load
            </button>
          )}
        </div>
      </div>

      <div
        className="daily-note-content"
        aria-label={`Daily note for ${dateLabel}`}
        onBlur={handleBlur}
      >
        {isDisabled ? (
          <p className="meta-text daily-note-placeholder">
            {loading
              ? `Loading note for ${dateLabel}…`
              : 'Could not load this day’s note.'}
          </p>
        ) : (
          <Suspense
            fallback={
              <p className="meta-text daily-note-placeholder">
                Loading editor…
              </p>
            }
          >
            <MarkdownEditor
              key={dateKey}
              value={draft}
              mode="live"
              onChange={handleChange}
            />
          </Suspense>
        )}
      </div>

      {(loadError || errorMessage) && (
        <p className="calendar-inline-error" role="alert">
          {loadError ?? errorMessage}
        </p>
      )}
    </section>
  );
}
