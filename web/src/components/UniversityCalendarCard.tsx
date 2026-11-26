import { useEffect, useMemo, useRef, useState } from 'react';
import {
  formatMonthYear,
  formatShortDate,
  parseDateKey,
  toDateKey,
} from '../data/mockData';
import {
  flattenScheduledUniversityItems,
  formatScheduledTimeLabel,
  getActivityDescriptor,
  getAttendanceStatusLabel,
  UNIVERSITY_ACTIVITY_TYPES,
  universityItemOccursOnDateKey,
  type FlattenedUniversityEvent,
} from '../lib/universityClient';
import type {
  UniversityActivityType,
  UniversityAttendanceStatus,
  UniversityCourse,
  UserCalendar,
} from '../types/atrium';
import {
  ChevronLeftIcon,
  ChevronRightIcon,
  CloseIcon,
  DownloadIcon,
  ExpandIcon,
} from './Icons';

interface UniversityCalendarCardProps {
  todayKey: string;
  selectedDateKey: string;
  calendar: UserCalendar | null;
  courses: UniversityCourse[];
  weekStartsOn?: 'monday' | 'sunday';
  timeFormat?: '24h' | '12h';
  onSelectDate: (dateKey: string) => void;
  onSelectCourse: (courseId: string) => void;
  onToggleDashboardVisibility: (calendar: UserCalendar) => Promise<void>;
  onExportIcs: (calendar: UserCalendar) => Promise<void>;
  onQuickAttendance: (
    itemId: string,
    status: UniversityAttendanceStatus,
  ) => Promise<void>;
}

const WEEKDAYS_MON = ['Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su'];
const WEEKDAYS_SUN = ['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa'];

interface DayCell {
  dateKey: string;
  dayNumber: number;
  inCurrentMonth: boolean;
}

function buildMonthGrid(
  viewYear: number,
  viewMonth: number,
  weekStartsOn: 'monday' | 'sunday',
): DayCell[] {
  const firstOfMonth = new Date(viewYear, viewMonth, 1);
  const rawDay = firstOfMonth.getDay();
  const startWeekday = weekStartsOn === 'monday' ? (rawDay + 6) % 7 : rawDay;
  const daysInMonth = new Date(viewYear, viewMonth + 1, 0).getDate();
  const daysInPrevMonth = new Date(viewYear, viewMonth, 0).getDate();

  const cells: DayCell[] = [];
  for (let i = startWeekday - 1; i >= 0; i -= 1) {
    const day = daysInPrevMonth - i;
    cells.push({
      dateKey: toDateKey(new Date(viewYear, viewMonth - 1, day)),
      dayNumber: day,
      inCurrentMonth: false,
    });
  }
  for (let day = 1; day <= daysInMonth; day += 1) {
    cells.push({
      dateKey: toDateKey(new Date(viewYear, viewMonth, day)),
      dayNumber: day,
      inCurrentMonth: true,
    });
  }
  let nextDay = 1;
  while (cells.length < 42) {
    cells.push({
      dateKey: toDateKey(new Date(viewYear, viewMonth + 1, nextDay)),
      dayNumber: nextDay,
      inCurrentMonth: false,
    });
    nextDay += 1;
  }
  return cells;
}

const NEXT_STATUS_CYCLE: Record<
  UniversityAttendanceStatus,
  UniversityAttendanceStatus
> = {
  pending: 'attended',
  attended: 'missed',
  missed: 'excused',
  excused: 'pending',
};

export function UniversityCalendarCard({
  todayKey,
  selectedDateKey,
  calendar,
  courses,
  weekStartsOn = 'monday',
  timeFormat = '24h',
  onSelectDate,
  onSelectCourse,
  onToggleDashboardVisibility,
  onExportIcs,
  onQuickAttendance,
}: UniversityCalendarCardProps) {
  const selectedDate = parseDateKey(selectedDateKey);
  const [viewYear, setViewYear] = useState(selectedDate.getFullYear());
  const [viewMonth, setViewMonth] = useState(selectedDate.getMonth());
  const [courseFilterId, setCourseFilterId] = useState<string>('all');
  const [typeFilter, setTypeFilter] = useState<string>('all');
  const [isExpanded, setIsExpanded] = useState(false);
  const [togglingVisibility, setTogglingVisibility] = useState(false);
  const [busyItemIds, setBusyItemIds] = useState<Record<string, boolean>>({});

  const dialogRef = useRef<HTMLDialogElement | null>(null);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (isExpanded && !dialog.open) {
      dialog.showModal();
    } else if (!isExpanded && dialog.open) {
      dialog.close();
    }
  }, [isExpanded]);

  useEffect(() => {
    const parsed = parseDateKey(selectedDateKey);
    setViewYear(parsed.getFullYear());
    setViewMonth(parsed.getMonth());
  }, [selectedDateKey]);

  const allScheduled = useMemo(
    () => flattenScheduledUniversityItems(courses),
    [courses],
  );

  const filteredEvents = useMemo(() => {
    return allScheduled.filter((entry) => {
      if (courseFilterId !== 'all' && entry.course.id !== courseFilterId) {
        return false;
      }
      if (
        typeFilter !== 'all' &&
        entry.activity.activityType !== (typeFilter as UniversityActivityType)
      ) {
        return false;
      }
      return true;
    });
  }, [allScheduled, courseFilterId, typeFilter]);

  const monthCells = useMemo(
    () => buildMonthGrid(viewYear, viewMonth, weekStartsOn),
    [viewYear, viewMonth, weekStartsOn],
  );

  const eventsByDateKey = useMemo(() => {
    const map = new Map<string, FlattenedUniversityEvent[]>();
    for (const cell of monthCells) {
      const dayItems = filteredEvents.filter((entry) =>
        universityItemOccursOnDateKey(entry.item, cell.dateKey),
      );
      if (dayItems.length > 0) {
        map.set(cell.dateKey, dayItems);
      }
    }
    return map;
  }, [monthCells, filteredEvents]);

  const selectedDayEvents = useMemo(
    () =>
      filteredEvents.filter((entry) =>
        universityItemOccursOnDateKey(entry.item, selectedDateKey),
      ),
    [filteredEvents, selectedDateKey],
  );

  const weekdays = weekStartsOn === 'monday' ? WEEKDAYS_MON : WEEKDAYS_SUN;
  const monthLabel = formatMonthYear(new Date(viewYear, viewMonth, 1));
  const selectedLabel =
    selectedDateKey === todayKey
      ? 'Today'
      : formatShortDate(parseDateKey(selectedDateKey));

  const shiftMonth = (delta: number) => {
    const next = new Date(viewYear, viewMonth + delta, 1);
    setViewYear(next.getFullYear());
    setViewMonth(next.getMonth());
  };

  const handleToggleVisibility = async () => {
    if (!calendar || togglingVisibility) return;
    setTogglingVisibility(true);
    try {
      await onToggleDashboardVisibility(calendar);
    } finally {
      setTogglingVisibility(false);
    }
  };

  const renderAgendaList = (entries: FlattenedUniversityEvent[]) => {
    if (entries.length === 0) {
      return (
        <p className="meta-text empty-state">
          No university events scheduled for {selectedLabel.toLowerCase()}.
        </p>
      );
    }

    return (
      <div className="stack-sm">
        {entries.map(({ item, activity, course }) => {
          const descriptor = getActivityDescriptor(activity.activityType);
          const timeLabel = formatScheduledTimeLabel(
            item.startAt ?? '',
            item.endAt,
            item.isAllDay,
            timeFormat,
          );
          const statusLabel = getAttendanceStatusLabel(
            item.attendanceStatus,
            activity.activityType,
          );
          const nextStatus = NEXT_STATUS_CYCLE[item.attendanceStatus];

          return (
            <div key={item.id} className="card-soft uni-agenda-item">
              <div className="uni-agenda-item-header">
                <button
                  type="button"
                  className="uni-agenda-course-btn"
                  onClick={() => {
                    onSelectCourse(course.id);
                    setIsExpanded(false);
                  }}
                >
                  <span
                    className="uni-color-dot"
                    style={{ backgroundColor: course.color }}
                    aria-hidden="true"
                  />
                  <span className="uni-agenda-course-name">
                    {course.code || course.name}
                  </span>
                </button>
                <span className="pill-neutral">{descriptor.shortLabel}</span>
              </div>

              <div className="uni-agenda-title-row">
                <p className="row-title">{item.title}</p>
                <button
                  type="button"
                  className={`uni-status-pill is-${item.attendanceStatus}`}
                  title={`Status: ${statusLabel}. Click to mark as ${getAttendanceStatusLabel(nextStatus, activity.activityType)}.`}
                  disabled={Boolean(busyItemIds[item.id])}
                  onClick={() => {
                    if (busyItemIds[item.id]) return;
                    setBusyItemIds((prev) => ({ ...prev, [item.id]: true }));
                    void onQuickAttendance(item.id, nextStatus)
                      .catch(() => undefined)
                      .finally(() => {
                        setBusyItemIds((prev) => {
                          const next = { ...prev };
                          delete next[item.id];
                          return next;
                        });
                      });
                  }}
                >
                  {statusLabel}
                </button>
              </div>

              <p className="meta-text mono-num">
                {timeLabel}
                {item.location ? ` · ${item.location}` : ''}
              </p>

              {item.theme && (
                <p className="meta-text uni-agenda-theme">{item.theme}</p>
              )}
            </div>
          );
        })}
      </div>
    );
  };

  return (
    <>
      <section
        className="card calendar-card uni-calendar-card"
        aria-labelledby="uni-calendar-heading"
      >
        <div className="calendar-header">
          <div>
            <p className="kicker">University Calendar</p>
            <h2 id="uni-calendar-heading" className="card-title">
              {monthLabel}
            </h2>
          </div>
          <div className="calendar-nav">
            <button
              type="button"
              className="btn btn-ghost btn-icon"
              aria-label="Previous month"
              onClick={() => shiftMonth(-1)}
            >
              <ChevronLeftIcon />
            </button>
            <button
              type="button"
              className="btn btn-ghost btn-icon"
              aria-label="Next month"
              onClick={() => shiftMonth(1)}
            >
              <ChevronRightIcon />
            </button>
            <button
              type="button"
              className="btn btn-ghost btn-icon"
              aria-label="Expand university calendar"
              title="Expand calendar"
              onClick={() => setIsExpanded(true)}
            >
              <ExpandIcon />
            </button>
          </div>
        </div>

        <div className="uni-calendar-filters">
          <select
            aria-label="Filter calendar by course"
            className="input-control uni-filter-select"
            value={courseFilterId}
            onChange={(e) => setCourseFilterId(e.target.value)}
          >
            <option value="all">All courses</option>
            {courses.map((c) => (
              <option key={c.id} value={c.id}>
                {c.code ? `${c.code} · ${c.name}` : c.name}
              </option>
            ))}
          </select>
          <select
            aria-label="Filter calendar by activity type"
            className="input-control uni-filter-select"
            value={typeFilter}
            onChange={(e) => setTypeFilter(e.target.value)}
          >
            <option value="all">All activities</option>
            {UNIVERSITY_ACTIVITY_TYPES.map((t) => (
              <option key={t.type} value={t.type}>
                {t.label}
              </option>
            ))}
          </select>
        </div>

        <div className="calendar-weekdays" aria-hidden="true">
          {weekdays.map((day) => (
            <span key={day} className="calendar-weekday">
              {day}
            </span>
          ))}
        </div>

        <div
          className="calendar-grid"
          role="grid"
          aria-label={`University calendar ${monthLabel}`}
        >
          {monthCells.map((cell) => {
            const dayEntries = eventsByDateKey.get(cell.dateKey) ?? [];
            const isToday = cell.dateKey === todayKey;
            const isSelected = cell.dateKey === selectedDateKey;
            const dotColors = Array.from(
              new Set(dayEntries.map((e) => e.course.color)),
            ).slice(0, 3);

            return (
              <button
                key={cell.dateKey}
                type="button"
                className={`calendar-day mono-num ${
                  !cell.inCurrentMonth ? 'is-outside' : ''
                } ${isToday ? 'is-today' : ''} ${
                  isSelected && !isToday ? 'is-selected' : ''
                }`}
                onClick={() => onSelectDate(cell.dateKey)}
                aria-label={`${cell.dateKey}, ${dayEntries.length} university events`}
                aria-pressed={isSelected}
              >
                <span>{cell.dayNumber}</span>
                {dotColors.length > 0 && (
                  <span className="uni-day-dots" aria-hidden="true">
                    {dotColors.map((color) => (
                      <span
                        key={color}
                        className="uni-day-dot"
                        style={{
                          backgroundColor: isToday ? 'var(--accent-fg)' : color,
                        }}
                      />
                    ))}
                  </span>
                )}
              </button>
            );
          })}
        </div>

        {calendar && (
          <div className="uni-calendar-sync-bar">
            <label className="uni-checkbox-label">
              <input
                type="checkbox"
                checked={calendar.isVisible}
                disabled={togglingVisibility}
                onChange={() => void handleToggleVisibility()}
              />
              <span>Show on Today calendar</span>
            </label>
            <button
              type="button"
              className="btn btn-ghost btn-compact"
              onClick={() => void onExportIcs(calendar)}
              title="Export University calendar as .ics"
            >
              <DownloadIcon />
              <span>.ics</span>
            </button>
          </div>
        )}

        <div className="uni-calendar-agenda">
          <div className="uni-calendar-agenda-header">
            <span className="kicker">{selectedLabel}</span>
            {selectedDateKey !== todayKey && (
              <button
                type="button"
                className="btn btn-ghost btn-compact"
                onClick={() => onSelectDate(todayKey)}
              >
                Today
              </button>
            )}
          </div>
          {renderAgendaList(selectedDayEvents)}
        </div>
      </section>

      <dialog
        ref={dialogRef}
        className="atrium-dialog calendar-dialog"
        onClose={() => setIsExpanded(false)}
        aria-labelledby="uni-calendar-modal-title"
      >
        <div className="dialog-header">
          <div>
            <p className="kicker">University Calendar</p>
            <h2 id="uni-calendar-modal-title" className="dialog-title">
              {monthLabel}
            </h2>
          </div>
          <div className="dialog-header-actions">
            <button
              type="button"
              className="btn btn-ghost btn-icon"
              aria-label="Previous month"
              onClick={() => shiftMonth(-1)}
            >
              <ChevronLeftIcon />
            </button>
            <button
              type="button"
              className="btn btn-secondary btn-compact"
              onClick={() => onSelectDate(todayKey)}
            >
              Today
            </button>
            <button
              type="button"
              className="btn btn-ghost btn-icon"
              aria-label="Next month"
              onClick={() => shiftMonth(1)}
            >
              <ChevronRightIcon />
            </button>
            <button
              type="button"
              className="btn btn-ghost btn-icon"
              aria-label="Close expanded university calendar"
              onClick={() => setIsExpanded(false)}
            >
              <CloseIcon />
            </button>
          </div>
        </div>

        <div className="calendar-modal-layout">
          <div>
            <div className="calendar-weekdays" aria-hidden="true">
              {weekdays.map((day) => (
                <span key={day} className="calendar-weekday">
                  {day}
                </span>
              ))}
            </div>
            <div className="calendar-modal-grid">
              {monthCells.map((cell) => {
                const dayEntries = eventsByDateKey.get(cell.dateKey) ?? [];
                const isToday = cell.dateKey === todayKey;
                const isSelected = cell.dateKey === selectedDateKey;
                return (
                  <button
                    key={cell.dateKey}
                    type="button"
                    className={`calendar-modal-cell ${
                      !cell.inCurrentMonth ? 'is-outside' : ''
                    } ${isToday ? 'is-today' : ''} ${
                      isSelected ? 'is-selected' : ''
                    }`}
                    onClick={() => onSelectDate(cell.dateKey)}
                  >
                    <span className="calendar-modal-day-num mono-num">
                      {cell.dayNumber}
                    </span>
                    <div className="calendar-modal-cell-events">
                      {dayEntries.slice(0, 3).map(({ item, course }) => (
                        <span
                          key={item.id}
                          className="uni-modal-event-chip"
                          style={{ borderLeftColor: course.color }}
                        >
                          {course.code ? `${course.code} ` : ''}
                          {item.title}
                        </span>
                      ))}
                      {dayEntries.length > 3 && (
                        <span className="meta-text mono-num">
                          +{dayEntries.length - 3} more
                        </span>
                      )}
                    </div>
                  </button>
                );
              })}
            </div>
          </div>

          <div className="uni-modal-sidebar">
            <h3 className="card-title">{selectedLabel}</h3>
            {renderAgendaList(selectedDayEvents)}
          </div>
        </div>
      </dialog>
    </>
  );
}
