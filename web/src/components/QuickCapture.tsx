import { useState, type FormEvent, type RefObject } from 'react';
import type { CaptureKind } from '../types/atrium';

interface QuickCaptureProps {
  inputRef: RefObject<HTMLInputElement | null>;
  selectedDateLabel: string;
  onCapture: (payload: {
    kind: CaptureKind;
    text: string;
    time?: string;
  }) => void;
}

const PLACEHOLDERS: Record<CaptureKind, string> = {
  task: 'Capture a task for today…',
  event: 'Schedule an event on selected calendar day…',
  note: 'Jot down a quick note or thought…',
};

export function QuickCapture({
  inputRef,
  selectedDateLabel,
  onCapture,
}: QuickCaptureProps) {
  const [kind, setKind] = useState<CaptureKind>('task');
  const [text, setText] = useState('');
  const [eventTime, setEventTime] = useState('15:30');

  const handleSubmit = (event: FormEvent) => {
    event.preventDefault();
    const trimmed = text.trim();
    if (!trimmed) return;
    onCapture({
      kind,
      text: trimmed,
      time: kind === 'event' ? eventTime : undefined,
    });
    setText('');
  };

  return (
    <section className="card composer-card" aria-label="Quick capture">
      <form className="composer-form" onSubmit={handleSubmit}>
        <div className="composer-modes" role="radiogroup" aria-label="Capture type">
          {(['task', 'event', 'note'] as const).map((option) => (
            <button
              key={option}
              id={`capture-mode-${option}`}
              type="button"
              role="radio"
              aria-checked={kind === option}
              className={`mode-chip ${kind === option ? 'is-active' : ''}`}
              onClick={() => setKind(option)}
            >
              {option === 'task' ? 'Task' : option === 'event' ? 'Event' : 'Note'}
            </button>
          ))}
          {kind === 'event' && (
            <span className="composer-date-hint meta-text">
              for {selectedDateLabel}
            </span>
          )}
        </div>

        <div className="composer-input-row">
          <label className="sr-only" htmlFor="quick-capture-input">
            Quick capture
          </label>
          <input
            ref={inputRef}
            id="quick-capture-input"
            type="text"
            className="input composer-input"
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder={PLACEHOLDERS[kind]}
          />
          {kind === 'event' && (
            <label className="sr-only-wrapper">
              <span className="sr-only">Event start time</span>
              <input
                id="quick-capture-time"
                type="time"
                className="input composer-time mono-num"
                value={eventTime}
                onChange={(e) => setEventTime(e.target.value)}
              />
            </label>
          )}
          <button
            id="quick-capture-submit"
            type="submit"
            className="btn btn-primary"
            disabled={!text.trim()}
          >
            Capture
          </button>
        </div>
      </form>
    </section>
  );
}
