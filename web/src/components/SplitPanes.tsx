import { useEffect, useState, type ReactNode } from 'react';
import ReactMarkdown from 'react-markdown';
import {
  downloadAttachment,
  formatFileSize,
  resolveAttachmentImageUrl,
} from '../lib/attachmentsClient';
import { stripFrontmatterForReading } from '../lib/frontmatter';
import {
  readingMarkdownComponents,
  readingRehypePlugins,
  readingRemarkPlugins,
} from '../lib/markdownReading';
import { normalizeBlockMathFences } from '../lib/markdownUtils';
import { getNote } from '../lib/notesClient';
import type { AttachmentSummary, NoteDetail } from '../types/atrium';

interface PaneChromeProps {
  title: string;
  subtitle?: string;
  actions?: ReactNode;
  onSwap: () => void;
  onClose: () => void;
  children: ReactNode;
}

function PaneChrome({
  title,
  subtitle,
  actions,
  onSwap,
  onClose,
  children,
}: PaneChromeProps) {
  return (
    <section className="notes-split-pane" aria-label={title}>
      <header className="notes-split-pane-header">
        <div className="notes-split-pane-title">
          <span className="notes-split-pane-name" title={title}>
            {title}
          </span>
          {subtitle && <span className="meta-text">{subtitle}</span>}
        </div>
        <div className="notes-split-pane-actions">
          {actions}
          <button
            type="button"
            className="btn btn-ghost btn-xs"
            onClick={onSwap}
            title="Swap sides"
            aria-label="Swap sides"
          >
            ⇄
          </button>
          <button
            type="button"
            className="btn btn-ghost btn-xs"
            onClick={onClose}
            title="Close split view"
            aria-label="Close split view"
          >
            ×
          </button>
        </div>
      </header>
      <div className="notes-split-pane-body">{children}</div>
    </section>
  );
}

export function PdfViewerPane({
  attachment,
  onSwap,
  onClose,
}: {
  attachment: AttachmentSummary;
  onSwap: () => void;
  onClose: () => void;
}) {
  const [blobUrl, setBlobUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setBlobUrl(null);
    setFailed(false);
    void resolveAttachmentImageUrl(attachment.url).then((url) => {
      if (cancelled) return;
      if (url) setBlobUrl(url);
      else setFailed(true);
    });
    return () => {
      cancelled = true;
    };
  }, [attachment.url]);

  return (
    <PaneChrome
      title={attachment.fileName}
      subtitle={`PDF · ${formatFileSize(attachment.sizeBytes)}`}
      onSwap={onSwap}
      onClose={onClose}
      actions={
        <button
          type="button"
          className="btn btn-ghost btn-xs"
          onClick={() =>
            void downloadAttachment(attachment.id, attachment.fileName)
          }
        >
          Download
        </button>
      }
    >
      {failed ? (
        <p className="meta-text empty-state">
          Could not load this PDF. Try downloading it instead.
        </p>
      ) : !blobUrl ? (
        <p className="meta-text empty-state">Loading PDF…</p>
      ) : (
        <iframe
          className="notes-split-pdf-frame"
          src={blobUrl}
          title={attachment.fileName}
        />
      )}
    </PaneChrome>
  );
}

export function NoteReadPane({
  noteId,
  refreshKey,
  onOpenInEditor,
  onSwap,
  onClose,
}: {
  noteId: string;
  refreshKey: number;
  onOpenInEditor: (noteId: string) => void;
  onSwap: () => void;
  onClose: () => void;
}) {
  const [note, setNote] = useState<NoteDetail | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setError(null);
    void getNote(noteId)
      .then((detail) => {
        if (!cancelled) setNote(detail);
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          setNote(null);
          setError(err instanceof Error ? err.message : 'Could not load note.');
        }
      });
    return () => {
      cancelled = true;
    };
  }, [noteId, refreshKey]);

  const body = note ? stripFrontmatterForReading(note.content) : '';

  return (
    <PaneChrome
      title={note?.title ?? 'Note'}
      subtitle={note?.linkLabel ?? 'Read-only'}
      onSwap={onSwap}
      onClose={onClose}
      actions={
        <button
          type="button"
          className="btn btn-secondary btn-xs"
          onClick={() => onOpenInEditor(noteId)}
          title="Edit this note and move the current one here"
        >
          Open in editor
        </button>
      }
    >
      {error ? (
        <p className="meta-text empty-state">{error}</p>
      ) : !note ? (
        <p className="meta-text empty-state">Loading note…</p>
      ) : body.trim().length === 0 ? (
        <p className="meta-text empty-state">This note is empty.</p>
      ) : (
        <article className="notes-reading-prose">
          <ReactMarkdown
            remarkPlugins={readingRemarkPlugins}
            rehypePlugins={readingRehypePlugins}
            components={readingMarkdownComponents}
          >
            {normalizeBlockMathFences(body)}
          </ReactMarkdown>
        </article>
      )}
    </PaneChrome>
  );
}

