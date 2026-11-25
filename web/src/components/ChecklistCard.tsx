import type { TaskItem } from '../types/atrium';
import { CloseIcon } from './Icons';

interface ChecklistCardProps {
  tasks: TaskItem[];
  onToggleTask: (id: string) => void;
  onDeleteTask: (id: string) => void;
}

export function ChecklistCard({
  tasks,
  onToggleTask,
  onDeleteTask,
}: ChecklistCardProps) {
  const completedCount = tasks.filter((t) => t.completed).length;

  return (
    <section className="card" aria-labelledby="checklist-heading">
      <div className="card-header">
        <h2 id="checklist-heading" className="card-title">
          Today&apos;s Checklist
        </h2>
        <span className="pill-neutral mono-num">
          {completedCount} / {tasks.length}
        </span>
      </div>

      {tasks.length === 0 ? (
        <p className="empty-state meta-text">
          All clear for now. Use Quick Capture above to add a task.
        </p>
      ) : (
        <ul className="hairline-list">
          {tasks.map((task) => (
            <li
              key={task.id}
              className={`checklist-row ${task.completed ? 'is-completed' : ''}`}
            >
              <label className="checklist-label">
                <input
                  id={`task-check-${task.id}`}
                  type="checkbox"
                  className="native-checkbox"
                  checked={task.completed}
                  onChange={() => onToggleTask(task.id)}
                />
                <span className="checklist-content">
                  <span className="checklist-title">{task.title}</span>
                  <span className="checklist-meta">
                    <span className="pill-accent">{task.category}</span>
                    {task.source && (
                      <span className="meta-text">via {task.source}</span>
                    )}
                  </span>
                </span>
              </label>

              <div className="checklist-right">
                {task.dueTime && (
                  <span className="mono-num time-label">{task.dueTime}</span>
                )}
                <button
                  type="button"
                  className="btn btn-ghost btn-icon-sm row-remove-btn"
                  onClick={() => onDeleteTask(task.id)}
                  aria-label={`Remove task: ${task.title}`}
                  title="Remove task"
                >
                  <CloseIcon />
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
