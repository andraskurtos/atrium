import { useState, type FormEvent, type RefObject } from 'react';
import type { CaptureKind } from '../types/atrium';
import { TimeInput } from './TimeInput';

interface QuickCaptureProps {
  inputRef: RefObject<HTMLInputElement | null>;
  selectedDateLabel: string;
  timeFormat?: '24h' | '12h';
  onCapture: (payload: {
    kind: CaptureKind;
    text: string;
    time?: string;
  }) => void;
}

export function QuickCapture({
  inputRef,
  selectedDateLabel,
  timeFormat = '24h',
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

  const placeholder =
    kind === 'task'
      ? `Add a checklist item for ${selectedDateLabel}…`
      : `Schedule an event for ${selectedDateLabel}…`;

  return (
    <section className="card composer-card" aria-label="Quick capture">
      <form className="composer-form" onSubmit={handleSubmit}>
        <div className="composer-modes" role="radiogroup" aria-label="Capture type">
          {(['task', 'event'] as const).map((option) => (
            <button
              key={option}
              id={`capture-mode-${option}`}
              type="button"
              role="radio"
              aria-checked={kind === option}
              className={`mode-chip ${kind === option ? 'is-active' : ''}`}
              onClick={() => setKind(option)}
            >
              {option === 'task' ? 'Task' : 'Event'}
            </button>
          ))}
          <span className="composer-date-hint meta-text">
            for {selectedDateLabel}
          </span>
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
            placeholder={placeholder}
          />
          {kind === 'event' && (
            <label className="sr-only-wrapper">
              <span className="sr-only">Event start time</span>
              <TimeInput
                id="quick-capture-time"
                className="input composer-time mono-num"
                timeFormat={timeFormat}
                value={eventTime}
                onChange={setEventTime}
                ariaLabel="Event start time"
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
