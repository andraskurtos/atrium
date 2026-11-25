import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import {
  formatMonthYear,
  formatShortDate,
  parseDateKey,
  toDateKey,
} from '../data/mockData';
import type { CalendarEvent } from '../types/atrium';
import {
  ChevronLeftIcon,
  ChevronRightIcon,
  CloseIcon,
  ExpandIcon,
  PlusIcon,
} from './Icons';

interface CalendarMockupCardProps {
  todayKey: string;
  selectedDateKey: string;
  events: CalendarEvent[];
  isExpandedOpen: boolean;
  onSelectDate: (dateKey: string) => void;
  onAddEvent: (event: Omit<CalendarEvent, 'id'>) => void;
  onDeleteEvent: (id: string) => void;
  onToggleExpanded: (open: boolean) => void;
}

const WEEKDAYS = ['Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su'];

interface DayCell {
  dateKey: string;
  dayNumber: number;
  inCurrentMonth: boolean;
}

function buildMonthGrid(viewYear: number, viewMonth: number): DayCell[] {
  const firstOfMonth = new Date(viewYear, viewMonth, 1);
  // Convert JS Sun=0..Sat=6 to Mon=0..Sun=6
  const startWeekday = (firstOfMonth.getDay() + 6) % 7;
  const daysInMonth = new Date(viewYear, viewMonth + 1, 0).getDate();
  const daysInPrevMonth = new Date(viewYear, viewMonth, 0).getDate();

  const cells: DayCell[] = [];

  for (let i = startWeekday - 1; i >= 0; i -= 1) {
    const day = daysInPrevMonth - i;
    const d = new Date(viewYear, viewMonth - 1, day);
    cells.push({
      dateKey: toDateKey(d),
      dayNumber: day,
      inCurrentMonth: false,
    });
  }

  for (let day = 1; day <= daysInMonth; day += 1) {
    const d = new Date(viewYear, viewMonth, day);
    cells.push({
      dateKey: toDateKey(d),
      dayNumber: day,
      inCurrentMonth: true,
    });
  }

  // Fill remaining cells to complete 5 or 6 crisp rows (35 or 42 cells)
  const targetCells = cells.length > 35 ? 42 : 35;
  let nextDay = 1;
  while (cells.length < targetCells) {
    const d = new Date(viewYear, viewMonth + 1, nextDay);
    cells.push({
      dateKey: toDateKey(d),
      dayNumber: nextDay,
      inCurrentMonth: false,
    });
    nextDay += 1;
  }

  return cells;
}

export function CalendarMockupCard({
  todayKey,
  selectedDateKey,
  events,
  isExpandedOpen,
  onSelectDate,
  onAddEvent,
  onDeleteEvent,
  onToggleExpanded,
}: CalendarMockupCardProps) {
  const selectedDate = parseDateKey(selectedDateKey);
  const [viewYear, setViewYear] = useState(selectedDate.getFullYear());
  const [viewMonth, setViewMonth] = useState(selectedDate.getMonth());
  const [showInlineForm, setShowInlineForm] = useState(false);
  const [newTitle, setNewTitle] = useState('');
  const [newTime, setNewTime] = useState('11:30');
  const [newDuration, setNewDuration] = useState('45m');
  const [newLocation, setNewLocation] = useState('');

  const dialogRef = useRef<HTMLDialogElement | null>(null);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (isExpandedOpen && !dialog.open) {
      dialog.showModal();
    } else if (!isExpandedOpen && dialog.open) {
      dialog.close();
    }
  }, [isExpandedOpen]);

  // Keep calendar month synced when jumping to a date in another month
  useEffect(() => {
    const parsed = parseDateKey(selectedDateKey);
    setViewYear(parsed.getFullYear());
    setViewMonth(parsed.getMonth());
  }, [selectedDateKey]);

  const eventsByDate = useMemo(() => {
    const map = new Map<string, CalendarEvent[]>();
    for (const evt of events) {
      const list = map.get(evt.dateKey) ?? [];
      list.push(evt);
      map.set(evt.dateKey, list);
    }
    for (const list of map.values()) {
      list.sort((a, b) => a.startTime.localeCompare(b.startTime));
    }
    return map;
  }, [events]);

  const monthCells = useMemo(
    () => buildMonthGrid(viewYear, viewMonth),
    [viewYear, viewMonth],
  );

  const selectedDayEvents = eventsByDate.get(selectedDateKey) ?? [];
  const viewMonthDate = new Date(viewYear, viewMonth, 1);
  const todayDate = parseDateKey(todayKey);
  const isViewingTodayMonth =
    viewYear === todayDate.getFullYear() && viewMonth === todayDate.getMonth();

  const shiftMonth = (delta: number) => {
    const next = new Date(viewYear, viewMonth + delta, 1);
    setViewYear(next.getFullYear());
    setViewMonth(next.getMonth());
  };

  const handleJumpToday = () => {
    setViewYear(todayDate.getFullYear());
    setViewMonth(todayDate.getMonth());
    onSelectDate(todayKey);
  };

  const handleAddEventSubmit = (event: FormEvent) => {
    event.preventDefault();
    const title = newTitle.trim();
    if (!title) return;
    onAddEvent({
      dateKey: selectedDateKey,
      startTime: newTime || '09:00',
      duration: newDuration || '30m',
      title,
      location: newLocation.trim() || undefined,
      category: 'Personal',
    });
    setNewTitle('');
    setNewLocation('');
    setShowInlineForm(false);
  };

  const selectedHeaderLabel =
    selectedDateKey === todayKey
      ? 'Up Next · Today'
      : `Schedule · ${formatShortDate(selectedDate)}`;

  return (
    <>
      <section className="card calendar-card" aria-labelledby="calendar-heading">
        <div className="calendar-header">
          <h2 id="calendar-heading" className="card-title">
            {formatMonthYear(viewMonthDate)}
          </h2>
          <div className="calendar-nav">
            {(!isViewingTodayMonth || selectedDateKey !== todayKey) && (
              <button
                id="calendar-today-btn"
                type="button"
                className="btn btn-ghost btn-xs"
                onClick={handleJumpToday}
              >
                Today
              </button>
            )}
            <button
              id="calendar-prev-month"
              type="button"
              className="btn btn-ghost btn-icon-sm"
              onClick={() => shiftMonth(-1)}
              aria-label="Previous month"
            >
              <ChevronLeftIcon />
            </button>
            <button
              id="calendar-next-month"
              type="button"
              className="btn btn-ghost btn-icon-sm"
              onClick={() => shiftMonth(1)}
              aria-label="Next month"
            >
              <ChevronRightIcon />
            </button>
            <button
              id="calendar-expand-btn"
              type="button"
              className="btn btn-ghost btn-icon-sm"
              onClick={() => onToggleExpanded(true)}
              aria-label="Open expanded calendar view"
              title="Expand calendar"
            >
              <ExpandIcon />
            </button>
          </div>
        </div>

        <div className="calendar-weekdays" aria-hidden="true">
          {WEEKDAYS.map((day) => (
            <span key={day} className="calendar-weekday">
              {day}
            </span>
          ))}
        </div>

        <div
          className="calendar-grid"
          role="grid"
          aria-label={formatMonthYear(viewMonthDate)}
        >
          {monthCells.map((cell) => {
            const isToday = cell.dateKey === todayKey;
            const isSelected = cell.dateKey === selectedDateKey;
            const dayEvents = eventsByDate.get(cell.dateKey) ?? [];
            const hasEvents = dayEvents.length > 0;

            return (
              <button
                key={cell.dateKey}
                type="button"
                className={[
                  'calendar-day',
                  'mono-num',
                  !cell.inCurrentMonth ? 'is-outside' : '',
                  isToday ? 'is-today' : '',
                  isSelected ? 'is-selected' : '',
                ]
                  .filter(Boolean)
                  .join(' ')}
                aria-label={`${formatShortDate(parseDateKey(cell.dateKey))}${
                  hasEvents ? `, ${dayEvents.length} events` : ''
                }`}
                aria-pressed={isSelected}
                onClick={() => onSelectDate(cell.dateKey)}
              >
                <span>{cell.dayNumber}</span>
                {hasEvents && (
                  <span className="calendar-day-dot" aria-hidden="true" />
                )}
              </button>
            );
          })}
        </div>
      </section>

      <section className="card" aria-labelledby="schedule-heading">
        <div className="card-header">
          <h2 id="schedule-heading" className="card-title">
            {selectedHeaderLabel}
          </h2>
          <button
            id="schedule-add-toggle"
            type="button"
            className="btn btn-ghost btn-xs"
            onClick={() => setShowInlineForm((prev) => !prev)}
            aria-expanded={showInlineForm}
          >
            <PlusIcon />
            <span>{showInlineForm ? 'Close' : 'Add'}</span>
          </button>
        </div>

        {showInlineForm && (
          <form className="inline-event-form card-soft" onSubmit={handleAddEventSubmit}>
            <input
              id="inline-event-title"
              type="text"
              className="input"
              placeholder="Event title"
              value={newTitle}
              onChange={(e) => setNewTitle(e.target.value)}
              required
            />
            <div className="inline-event-row">
              <input
                id="inline-event-time"
                type="time"
                className="input mono-num"
                value={newTime}
                onChange={(e) => setNewTime(e.target.value)}
                aria-label="Start time"
              />
              <select
                id="inline-event-duration"
                className="input mono-num"
                value={newDuration}
                onChange={(e) => setNewDuration(e.target.value)}
                aria-label="Duration"
              >
                <option value="30m">30m</option>
                <option value="45m">45m</option>
                <option value="60m">60m</option>
                <option value="90m">90m</option>
              </select>
            </div>
            <input
              id="inline-event-location"
              type="text"
              className="input"
              placeholder="Location (optional)"
              value={newLocation}
              onChange={(e) => setNewLocation(e.target.value)}
            />
            <button
              id="inline-event-submit"
              type="submit"
              className="btn btn-primary btn-compact"
            >
              Add to {formatShortDate(selectedDate)}
            </button>
          </form>
        )}

        {selectedDayEvents.length === 0 ? (
          <p className="empty-state meta-text">
            Open schedule for {formatShortDate(selectedDate)}.
          </p>
        ) : (
          <div className="stack-sm">
            {selectedDayEvents.map((evt) => (
              <article key={evt.id} className="card-soft event-row">
                <div className="event-time-col">
                  <span className="mono-num event-start">{evt.startTime}</span>
                  <span className="mono-num event-duration">{evt.duration}</span>
                </div>
                <div className="event-body">
                  <h3 className="row-title">{evt.title}</h3>
                  <div className="event-meta">
                    {evt.location && (
                      <span className="meta-text">{evt.location}</span>
                    )}
                    {evt.source && (
                      <span className="pill-accent">{evt.source}</span>
                    )}
                  </div>
                </div>
                <button
                  type="button"
                  className="btn btn-ghost btn-icon-sm row-remove-btn"
                  onClick={() => onDeleteEvent(evt.id)}
                  aria-label={`Remove event: ${evt.title}`}
                  title="Remove event"
                >
                  <CloseIcon />
                </button>
              </article>
            ))}
          </div>
        )}
      </section>

      <dialog
        ref={dialogRef}
        className="atrium-dialog calendar-dialog"
        aria-labelledby="calendar-modal-title"
        onCancel={(e) => {
          e.preventDefault();
          onToggleExpanded(false);
        }}
        onClick={(e) => {
          if (e.target === dialogRef.current) {
            onToggleExpanded(false);
          }
        }}
      >
        <div className="dialog-header">
          <div>
            <p className="kicker">CALENDAR MOCKUP</p>
            <h2 id="calendar-modal-title" className="dialog-title">
              {formatMonthYear(viewMonthDate)}
            </h2>
          </div>
          <div className="dialog-header-actions">
            <button
              type="button"
              className="btn btn-secondary btn-compact"
              onClick={handleJumpToday}
            >
              Today
            </button>
            <button
              type="button"
              className="btn btn-ghost btn-icon-sm"
              onClick={() => shiftMonth(-1)}
              aria-label="Previous month"
            >
              <ChevronLeftIcon />
            </button>
            <button
              type="button"
              className="btn btn-ghost btn-icon-sm"
              onClick={() => shiftMonth(1)}
              aria-label="Next month"
            >
              <ChevronRightIcon />
            </button>
            <button
              id="calendar-modal-close"
              type="button"
              className="btn btn-ghost btn-icon-sm"
              onClick={() => onToggleExpanded(false)}
              aria-label="Close calendar dialog"
            >
              <CloseIcon />
            </button>
          </div>
        </div>

        <div className="calendar-modal-layout">
          <div className="calendar-modal-month">
            <div className="calendar-weekdays" aria-hidden="true">
              {WEEKDAYS.map((day) => (
                <span key={day} className="calendar-weekday">
                  {day}
                </span>
              ))}
            </div>

            <div className="calendar-modal-grid">
              {monthCells.map((cell) => {
                const isToday = cell.dateKey === todayKey;
                const isSelected = cell.dateKey === selectedDateKey;
                const dayEvents = eventsByDate.get(cell.dateKey) ?? [];

                return (
                  <button
                    key={cell.dateKey}
                    type="button"
                    className={[
                      'calendar-modal-cell',
                      !cell.inCurrentMonth ? 'is-outside' : '',
                      isToday ? 'is-today' : '',
                      isSelected ? 'is-selected' : '',
                    ]
                      .filter(Boolean)
                      .join(' ')}
                    onClick={() => onSelectDate(cell.dateKey)}
                  >
                    <span className="calendar-modal-day-num mono-num">
                      {cell.dayNumber}
                    </span>
                    <div className="calendar-modal-cell-events">
                      {dayEvents.slice(0, 2).map((evt) => (
                        <span key={evt.id} className="calendar-cell-chip" title={`${evt.startTime} · ${evt.title}`}>
                          {evt.title}
                        </span>
                      ))}
                      {dayEvents.length > 2 && (
                        <span className="meta-text mono-num calendar-cell-more">
                          +{dayEvents.length - 2} more
                        </span>
                      )}
                    </div>
                  </button>
                );
              })}
            </div>
          </div>

          <aside className="calendar-modal-detail">
            <div>
              <h3 className="card-title">{formatShortDate(selectedDate)}</h3>
              <p className="meta-text">
                {selectedDayEvents.length === 0
                  ? 'No scheduled blocks'
                  : `${selectedDayEvents.length} scheduled ${
                      selectedDayEvents.length === 1 ? 'block' : 'blocks'
                    }`}
              </p>
            </div>

            <div className="stack-sm">
              {selectedDayEvents.length === 0 ? (
                <p className="empty-state meta-text">
                  No events scheduled for this day. Use Quick Capture or + Add in the Context Rail to schedule one.
                </p>
              ) : (
                selectedDayEvents.map((evt) => (
                  <article key={evt.id} className="card-soft event-row">
                    <div className="event-time-col">
                      <span className="mono-num event-start">{evt.startTime}</span>
                      <span className="mono-num event-duration">{evt.duration}</span>
                    </div>
                    <div className="event-body">
                      <h4 className="row-title">{evt.title}</h4>
                      <div className="event-meta">
                        {evt.location && (
                          <span className="meta-text">{evt.location}</span>
                        )}
                        {evt.source && (
                          <span className="pill-accent">{evt.source}</span>
                        )}
                      </div>
                    </div>
                    <button
                      type="button"
                      className="btn btn-ghost btn-icon-sm row-remove-btn"
                      onClick={() => onDeleteEvent(evt.id)}
                      aria-label={`Remove event: ${evt.title}`}
                      title="Remove event"
                    >
                      <CloseIcon />
                    </button>
                  </article>
                ))
              )}
            </div>
          </aside>
        </div>
      </dialog>
    </>
  );
}
