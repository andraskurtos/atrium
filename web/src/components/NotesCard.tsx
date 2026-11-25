import type { NoteItem } from '../types/atrium';

interface NotesCardProps {
  notes: NoteItem[];
}

export function NotesCard({ notes }: NotesCardProps) {
  return (
    <section className="card" aria-labelledby="notes-heading">
      <div className="card-header">
        <h2 id="notes-heading" className="card-title">
          Recent Notes
        </h2>
        <span className="pill-neutral mono-num">{notes.length}</span>
      </div>

      {notes.length === 0 ? (
        <p className="empty-state meta-text">
          No notes captured yet. Switch Quick Capture to Note to jot something down.
        </p>
      ) : (
        <div className="stack-sm">
          {notes.map((note) => (
            <article key={note.id} className="card-soft note-card">
              <div className="note-header">
                <h3 className="row-title">{note.title}</h3>
                <span className="meta-text mono-num">{note.updatedAt}</span>
              </div>
              <p className="meta-text note-excerpt">{note.excerpt}</p>
              <div className="note-footer">
                <span className="pill-accent">{note.tag}</span>
              </div>
            </article>
          ))}
        </div>
      )}
    </section>
  );
}
