import { useState } from 'react';
import type { DailyCheckItem } from '../types/atrium';
import { ArrowRightIcon, CloseIcon } from './Icons';

interface ChecklistCardProps {
  dateLabel: string;
  isToday: boolean;
  items: DailyCheckItem[];
  loading: boolean;
  onToggleItem: (item: DailyCheckItem) => Promise<void>;
  onEditItemTitle: (item: DailyCheckItem, title: string) => Promise<void>;
  onMoveItemToNextDay: (item: DailyCheckItem) => Promise<void>;
  onDeleteItem: (item: DailyCheckItem) => Promise<void>;
}

export function ChecklistCard({
  dateLabel,
  isToday,
  items,
  loading,
  onToggleItem,
  onEditItemTitle,
  onMoveItemToNextDay,
  onDeleteItem,
}: ChecklistCardProps) {
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editDraft, setEditDraft] = useState('');
  const [busyIds, setBusyIds] = useState<Record<string, boolean>>({});

  const completedCount = items.filter((item) => item.isCompleted).length;

  const runBusy = (id: string, action: () => Promise<void>) => {
    if (busyIds[id]) return;
    setBusyIds((prev) => ({ ...prev, [id]: true }));
    void action()
      .catch(() => undefined)
      .finally(() => {
        setBusyIds((prev) => {
          const next = { ...prev };
          delete next[id];
          return next;
        });
      });
  };

  const startEditing = (item: DailyCheckItem) => {
    setEditingId(item.id);
    setEditDraft(item.title);
  };

  const cancelEditing = () => {
    setEditingId(null);
    setEditDraft('');
  };

  const commitEditing = (item: DailyCheckItem) => {
    const trimmed = editDraft.trim();
    setEditingId(null);
    if (!trimmed || trimmed === item.title) {
      return;
    }
    runBusy(item.id, () => onEditItemTitle(item, trimmed));
  };

  return (
    <section className="card" aria-labelledby="checklist-heading">
      <div className="card-header">
        <h2 id="checklist-heading" className="card-title">
          {isToday ? "Today's Checklist" : `${dateLabel} Checklist`}
        </h2>
        <span className="pill-neutral mono-num">
          {completedCount} / {items.length}
        </span>
      </div>

      {loading && items.length === 0 ? (
        <p className="empty-state meta-text">Loading checklist…</p>
      ) : items.length === 0 ? (
        <p className="empty-state meta-text">
          Nothing on the list for {dateLabel}. Use Quick Capture above to add a task.
        </p>
      ) : (
        <ul className="hairline-list">
          {items.map((item) => {
            const isBusy = Boolean(busyIds[item.id]);
            const isEditing = editingId === item.id;

            return (
              <li
                key={item.id}
                className={`checklist-row ${item.isCompleted ? 'is-completed' : ''}`}
              >
                <div className="checklist-label">
                  <input
                    id={`task-check-${item.id}`}
                    type="checkbox"
                    className="native-checkbox"
                    checked={item.isCompleted}
                    disabled={isBusy}
                    aria-label={`Mark "${item.title}" as ${item.isCompleted ? 'incomplete' : 'complete'}`}
                    onChange={() => runBusy(item.id, () => onToggleItem(item))}
                  />
                  <span className="checklist-content">
                    {isEditing ? (
                      <input
                        type="text"
                        className="input checklist-edit-input"
                        value={editDraft}
                        maxLength={280}
                        autoFocus
                        aria-label="Edit checklist item title"
                        onChange={(e) => setEditDraft(e.target.value)}
                        onBlur={() => commitEditing(item)}
                        onKeyDown={(e) => {
                          if (e.key === 'Enter') {
                            e.preventDefault();
                            commitEditing(item);
                          } else if (e.key === 'Escape') {
                            e.preventDefault();
                            cancelEditing();
                          }
                        }}
                      />
                    ) : (
                      <button
                        type="button"
                        className="checklist-title-btn"
                        disabled={isBusy}
                        onClick={() => startEditing(item)}
                        title="Click to edit title"
                      >
                        <span className="checklist-title">{item.title}</span>
                      </button>
                    )}
                  </span>
                </div>

                <div className="checklist-right">
                  {item.dueTime && (
                    <span className="mono-num time-label">{item.dueTime}</span>
                  )}
                  {!item.isCompleted && (
                    <button
                      type="button"
                      className="btn btn-ghost btn-icon-sm checklist-move-btn"
                      disabled={isBusy}
                      onClick={() =>
                        runBusy(item.id, () => onMoveItemToNextDay(item))
                      }
                      aria-label={`Move "${item.title}" to next day`}
                      title="Move to next day"
                    >
                      <ArrowRightIcon />
                    </button>
                  )}
                  <button
                    type="button"
                    className="btn btn-ghost btn-icon-sm row-remove-btn"
                    disabled={isBusy}
                    onClick={() => runBusy(item.id, () => onDeleteItem(item))}
                    aria-label={`Remove task: ${item.title}`}
                    title="Remove task"
                  >
                    <CloseIcon />
                  </button>
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}
