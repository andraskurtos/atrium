import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type ChangeEvent,
  type FormEvent,
} from 'react';
import {
  formatMonthYear,
  formatShortDate,
  parseDateKey,
  toDateKey,
} from '../data/mockData';
import {
  allDayDatesToIsoRange,
  buildRRuleString,
  CALENDAR_COLOR_PALETTE,
  createDefaultRecurrenceState,
  dateKeyToWeekdayCode,
  eventInstanceKey,
  eventOccursOnDateKey,
  eventsOverlap,
  eventToFormFields,
  formatEventTimeLabels,
  formatRecurrenceSummary,
  localDateTimeToIso,
  parseRRuleString,
  WEEKDAY_CODES,
  type CreateCalendarInput,
  type CreateEventInput,
  type RecurrenceEndMode,
  type RecurrenceFormState,
  type RecurrenceFrequency,
  type UpdateEventInput,
  type WeekdayCode,
} from '../lib/calendarClient';
import {
  deleteAttachment,
  downloadAttachment,
  formatFileSize,
  listAttachments,
  uploadAttachment,
} from '../lib/attachmentsClient';
import type {
  AttachmentSummary,
  CalendarEvent,
  PendingNoteTarget,
  RecurrenceScope,
  UserCalendar,
} from '../types/atrium';
import {
  ChevronLeftIcon,
  ChevronRightIcon,
  CloseIcon,
  DownloadIcon,
  ExpandIcon,
  PlusIcon,
  UploadIcon,
} from './Icons';
import { TimeInput } from './TimeInput';

interface CalendarCardProps {
  todayKey: string;
  selectedDateKey: string;
  calendars: UserCalendar[];
  events: CalendarEvent[];
  loading: boolean;
  isExpandedOpen: boolean;
  weekStartsOn?: 'monday' | 'sunday';
  timeFormat?: '24h' | '12h';
  onSelectDate: (dateKey: string) => void;
  onViewYearChange?: (year: number) => void;
  onCreateCalendar: (input: CreateCalendarInput) => Promise<void>;
  onToggleCalendarVisibility: (calendar: UserCalendar) => Promise<void>;
  onDeleteCalendar: (id: string) => Promise<void>;
  onCreateEvent: (input: CreateEventInput) => Promise<void>;
  onUpdateEvent: (id: string, input: UpdateEventInput) => Promise<void>;
  onDeleteEvent: (
    evt: CalendarEvent,
    scope: RecurrenceScope,
  ) => Promise<void>;
  onImportIcs: (file: File, targetCalendarId: string | 'new') => Promise<void>;
  onExportIcs: (calendar: UserCalendar) => Promise<void>;
  onToggleExpanded: (open: boolean) => void;
  onOpenLinkedNote?: (target: PendingNoteTarget) => void;
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

  // Always render 6 full weeks (42 cells) so month height stays consistent across 4/5/6-week months
  const targetCells = 42;
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

export function CalendarCard({
  todayKey,
  selectedDateKey,
  calendars,
  events,
  loading,
  isExpandedOpen,
  weekStartsOn = 'monday',
  timeFormat = '24h',
  onSelectDate,
  onViewYearChange,
  onCreateCalendar,
  onToggleCalendarVisibility,
  onDeleteCalendar,
  onCreateEvent,
  onUpdateEvent,
  onDeleteEvent,
  onImportIcs,
  onExportIcs,
  onToggleExpanded,
  onOpenLinkedNote,
}: CalendarCardProps) {
  const selectedDate = parseDateKey(selectedDateKey);
  const [viewYear, setViewYear] = useState(selectedDate.getFullYear());
  const [viewMonth, setViewMonth] = useState(selectedDate.getMonth());
  const [editingEventRefObj, setEditingEventRefObj] =
    useState<CalendarEvent | null>(null);
  const [editingEventArtifacts, setEditingEventArtifacts] = useState<
    AttachmentSummary[]
  >([]);
  const [uploadingEventArtifact, setUploadingEventArtifact] = useState(false);
  const eventArtifactInputRef = useRef<HTMLInputElement | null>(null);

  // Event form state (shared between create and edit)
  const [showEventForm, setShowEventForm] = useState(false);
  const [editingEventId, setEditingEventId] = useState<string | null>(null);
  const [editingIsRecurring, setEditingIsRecurring] = useState(false);
  const [editingOccurrenceStartAt, setEditingOccurrenceStartAt] = useState<string | null>(null);
  const [editScope, setEditScope] = useState<RecurrenceScope>('single');
  const [pendingDeleteKey, setPendingDeleteKey] = useState<string | null>(null);
  const [eventTitle, setEventTitle] = useState('');
  const [eventCalendarId, setEventCalendarId] = useState('');
  const [eventIsAllDay, setEventIsAllDay] = useState(false);
  const [eventStartDate, setEventStartDate] = useState(selectedDateKey);
  const [eventStartTime, setEventStartTime] = useState('09:30');
  const [eventEndDate, setEventEndDate] = useState(selectedDateKey);
  const [eventEndTime, setEventEndTime] = useState('10:15');
  const [eventLocation, setEventLocation] = useState('');
  const [eventDescription, setEventDescription] = useState('');
  const [recurrence, setRecurrence] = useState<RecurrenceFormState>(() =>
    createDefaultRecurrenceState(selectedDateKey),
  );
  const [eventError, setEventError] = useState<string | null>(null);
  const [eventSaving, setEventSaving] = useState(false);

  // Calendar creation form state
  const [showNewCalendarForm, setShowNewCalendarForm] = useState(false);
  const [pendingDeleteCalendarId, setPendingDeleteCalendarId] = useState<string | null>(null);
  const [calName, setCalName] = useState('');
  const [calDescription, setCalDescription] = useState('');
  const [calColor, setCalColor] = useState<string>(CALENDAR_COLOR_PALETTE[1]);
  const [calError, setCalError] = useState<string | null>(null);
  const [calSaving, setCalSaving] = useState(false);

  // ICS import state
  const [showImportPanel, setShowImportPanel] = useState(false);
  const [importTarget, setImportTarget] = useState<string>('new');
  const [importBusy, setImportBusy] = useState(false);
  const [importError, setImportError] = useState<string | null>(null);
  const icsInputRef = useRef<HTMLInputElement | null>(null);

  const dialogRef = useRef<HTMLDialogElement | null>(null);

  const defaultCalendarId = useMemo(
    () => calendars.find((c) => c.isDefault)?.id ?? calendars[0]?.id ?? '',
    [calendars],
  );

  useEffect(() => {
    if (!eventCalendarId && defaultCalendarId) {
      setEventCalendarId(defaultCalendarId);
    }
  }, [defaultCalendarId, eventCalendarId]);

  useEffect(() => {
    onViewYearChange?.(viewYear);
  }, [onViewYearChange, viewYear]);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (isExpandedOpen && !dialog.open) {
      dialog.showModal();
    } else if (!isExpandedOpen && dialog.open) {
      dialog.close();
    }
  }, [isExpandedOpen]);

  useEffect(() => {
    const parsed = parseDateKey(selectedDateKey);
    setViewYear(parsed.getFullYear());
    setViewMonth(parsed.getMonth());
    if (!editingEventId) {
      setEventStartDate(selectedDateKey);
      setEventEndDate(selectedDateKey);
      setRecurrence((prev) =>
        prev.freq === 'NONE'
          ? createDefaultRecurrenceState(selectedDateKey)
          : prev,
      );
    }
  }, [selectedDateKey, editingEventId]);

  const visibleCalendarIds = useMemo(
    () => new Set(calendars.filter((c) => c.isVisible).map((c) => c.id)),
    [calendars],
  );

  const visibleEvents = useMemo(
    () =>
      events
        .filter((evt) => visibleCalendarIds.has(evt.calendarId))
        .sort((a, b) => {
          if (a.isAllDay !== b.isAllDay) return a.isAllDay ? -1 : 1;
          return a.startAt.localeCompare(b.startAt) || a.title.localeCompare(b.title);
        }),
    [events, visibleCalendarIds],
  );

  const monthCells = useMemo(
    () => buildMonthGrid(viewYear, viewMonth, weekStartsOn),
    [viewYear, viewMonth, weekStartsOn],
  );
  const weekdays = weekStartsOn === 'monday' ? WEEKDAYS_MON : WEEKDAYS_SUN;

  const eventsByCellKey = useMemo(() => {
    const map = new Map<string, CalendarEvent[]>();
    for (const cell of monthCells) {
      const matching = visibleEvents.filter((evt) =>
        eventOccursOnDateKey(evt, cell.dateKey),
      );
      if (matching.length > 0) {
        map.set(cell.dateKey, matching);
      }
    }
    return map;
  }, [monthCells, visibleEvents]);

  const selectedDayEvents = useMemo(
    () => visibleEvents.filter((evt) => eventOccursOnDateKey(evt, selectedDateKey)),
    [visibleEvents, selectedDateKey],
  );

  const overlappingEventKeys = useMemo(() => {
    const keys = new Set<string>();
    for (let i = 0; i < selectedDayEvents.length; i += 1) {
      for (let j = i + 1; j < selectedDayEvents.length; j += 1) {
        if (eventsOverlap(selectedDayEvents[i], selectedDayEvents[j])) {
          keys.add(eventInstanceKey(selectedDayEvents[i]));
          keys.add(eventInstanceKey(selectedDayEvents[j]));
        }
      }
    }
    return keys;
  }, [selectedDayEvents]);

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

  const openNewEventForm = () => {
    setEditingEventId(null);
    setEditingIsRecurring(false);
    setEditingOccurrenceStartAt(null);
    setEditScope('series');
    setPendingDeleteKey(null);
    setEventTitle('');
    setEventCalendarId(defaultCalendarId);
    setEventIsAllDay(false);
    setEventStartDate(selectedDateKey);
    setEventStartTime('09:30');
    setEventEndDate(selectedDateKey);
    setEventEndTime('10:15');
    setEventLocation('');
    setEventDescription('');
    setRecurrence(createDefaultRecurrenceState(selectedDateKey));
    setEventError(null);
    setShowEventForm(true);
  };

  const openEditEventForm = (evt: CalendarEvent) => {
    const fields = eventToFormFields(evt);
    setEditingEventRefObj(evt);
    setEditingEventId(evt.id);
    setEditingIsRecurring(evt.isRecurring);
    setEditingOccurrenceStartAt(evt.occurrenceStartAt ?? evt.startAt);
    setEditScope(evt.isRecurring ? 'single' : 'series');
    setPendingDeleteKey(null);
    setEventTitle(evt.title);
    setEventCalendarId(evt.calendarId);
    setEventIsAllDay(evt.isAllDay);
    setEventStartDate(fields.startDate);
    setEventStartTime(fields.startTime);
    setEventEndDate(fields.endDate);
    setEventEndTime(fields.endTime);
    setEventLocation(evt.location ?? '');
    setEventDescription(evt.description ?? '');
    setRecurrence(parseRRuleString(evt.recurrenceRule, fields.startDate));
    setEventError(null);
    setShowEventForm(true);

    const cal = calendars.find((c) => c.id === evt.calendarId);
    const isUni = cal?.module === 'university' || Boolean(evt.universityActivityItemId);
    if (!isUni) {
      const targetEventId = evt.seriesMasterId ?? evt.id;
      const targetOcc = evt.isRecurring ? (evt.occurrenceStartAt ?? evt.startAt) : null;
      void listAttachments({
        calendarEventId: targetEventId,
        occurrenceStartAt: targetOcc,
      })
        .then(setEditingEventArtifacts)
        .catch(() => setEditingEventArtifacts([]));
    } else {
      setEditingEventArtifacts([]);
    }
  };

  const closeEventForm = () => {
    setShowEventForm(false);
    setEditingEventRefObj(null);
    setEditingEventArtifacts([]);
    setEditingEventId(null);
    setEditingIsRecurring(false);
    setEditingOccurrenceStartAt(null);
    setEventError(null);
  };

  const handleUploadEventArtifact = async (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = '';
    const evt = editingEventRefObj;
    if (!file || !evt) return;
    setUploadingEventArtifact(true);
    try {
      const targetEventId = evt.seriesMasterId ?? evt.id;
      const targetOcc = evt.isRecurring ? (evt.occurrenceStartAt ?? evt.startAt) : null;
      const created = await uploadAttachment(file, {
        kind: 'artifact',
        calendarEventId: targetEventId,
        calendarOccurrenceStartAt: targetOcc,
      });
      setEditingEventArtifacts((prev) => [...prev, created]);
    } catch (err) {
      setEventError(
        err instanceof Error ? err.message : 'Could not upload artifact.',
      );
    } finally {
      setUploadingEventArtifact(false);
    }
  };

  const handleDeleteEventArtifact = async (art: AttachmentSummary) => {
    try {
      await deleteAttachment(art.id);
      setEditingEventArtifacts((prev) => prev.filter((a) => a.id !== art.id));
    } catch (err) {
      setEventError(
        err instanceof Error ? err.message : 'Could not delete artifact.',
      );
    }
  };

  const toggleWeekday = (code: WeekdayCode) => {
    setRecurrence((prev) => {
      const exists = prev.byDays.includes(code);
      if (exists && prev.byDays.length === 1) {
        return prev; // Keep at least one weekday selected
      }
      return {
        ...prev,
        byDays: exists
          ? prev.byDays.filter((d) => d !== code)
          : [...prev.byDays, code],
      };
    });
  };

  const handleEventSubmit = async (e: FormEvent) => {
    e.preventDefault();
    const trimmedTitle = eventTitle.trim();
    if (!trimmedTitle) {
      setEventError('Please enter an event title.');
      return;
    }

    let startAt: string;
    let endAt: string;
    if (eventIsAllDay) {
      const range = allDayDatesToIsoRange(eventStartDate, eventEndDate);
      startAt = range.startAt;
      endAt = range.endAt;
    } else {
      startAt = localDateTimeToIso(eventStartDate, eventStartTime);
      endAt = localDateTimeToIso(eventEndDate || eventStartDate, eventEndTime);
      if (new Date(endAt).getTime() < new Date(startAt).getTime()) {
        setEventError('End date/time must be on or after start date/time.');
        return;
      }
    }

    const chosenCalId = eventCalendarId || defaultCalendarId;
    if (!chosenCalId) {
      setEventError('Create or select a calendar first.');
      return;
    }

    const showRepeatRule = !editingIsRecurring || editScope === 'series';
    if (showRepeatRule && recurrence.freq !== 'NONE' && recurrence.endMode === 'UNTIL' && !recurrence.untilDate) {
      setEventError('Choose an end date for the repeating series.');
      return;
    }

    const rrule = showRepeatRule ? buildRRuleString(recurrence) : null;

    setEventSaving(true);
    setEventError(null);
    try {
      if (editingEventId) {
        await onUpdateEvent(editingEventId, {
          calendarId: chosenCalId,
          title: trimmedTitle,
          startAt,
          endAt,
          isAllDay: eventIsAllDay,
          location: eventLocation.trim() || null,
          description: eventDescription.trim() || null,
          recurrenceRule: rrule,
          scope: editingIsRecurring ? editScope : 'series',
          occurrenceStartAt: editingIsRecurring ? editingOccurrenceStartAt : null,
        });
      } else {
        await onCreateEvent({
          calendarId: chosenCalId,
          title: trimmedTitle,
          startAt,
          endAt,
          isAllDay: eventIsAllDay,
          location: eventLocation.trim() || null,
          description: eventDescription.trim() || null,
          recurrenceRule: rrule,
        });
      }
      closeEventForm();
    } catch (err) {
      setEventError(err instanceof Error ? err.message : 'Could not save event.');
    } finally {
      setEventSaving(false);
    }
  };

  const handleCreateCalendarSubmit = async (e: FormEvent) => {
    e.preventDefault();
    const trimmed = calName.trim();
    if (!trimmed) {
      setCalError('Enter a calendar name.');
      return;
    }
    setCalSaving(true);
    setCalError(null);
    try {
      await onCreateCalendar({
        name: trimmed,
        description: calDescription.trim() || undefined,
        color: calColor,
      });
      setCalName('');
      setCalDescription('');
      setShowNewCalendarForm(false);
    } catch (err) {
      setCalError(err instanceof Error ? err.message : 'Could not create calendar.');
    } finally {
      setCalSaving(false);
    }
  };

  const handleIcsFileChange = async (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setImportBusy(true);
    setImportError(null);
    try {
      await onImportIcs(file, importTarget === 'new' ? 'new' : importTarget);
      setShowImportPanel(false);
    } catch (err) {
      setImportError(err instanceof Error ? err.message : 'Could not import .ics file.');
    } finally {
      setImportBusy(false);
      if (icsInputRef.current) {
        icsInputRef.current.value = '';
      }
    }
  };

  const selectedHeaderLabel =
    selectedDateKey === todayKey
      ? 'Up Next · Today'
      : `Schedule · ${formatShortDate(selectedDate)}`;

  const renderEventForm = (prefix: string) => {
    const showRepeatControls = !editingIsRecurring || editScope === 'series';
    const intervalUnit =
      recurrence.freq === 'DAILY'
        ? recurrence.interval === 1 ? 'day' : 'days'
        : recurrence.freq === 'WEEKLY'
          ? recurrence.interval === 1 ? 'week' : 'weeks'
          : recurrence.freq === 'MONTHLY'
            ? recurrence.interval === 1 ? 'month' : 'months'
            : recurrence.interval === 1 ? 'year' : 'years';

    return (
      <form className="inline-event-form card-soft" onSubmit={(e) => void handleEventSubmit(e)}>
        <div className="calendar-form-heading">
          <span className="meta-text">
            {editingEventId ? 'Edit event' : 'New event'}
          </span>
          <button
            type="button"
            className="btn btn-ghost btn-xs"
            onClick={closeEventForm}
          >
            Cancel
          </button>
        </div>

        {editingEventId && editingIsRecurring && (
          <div className="recurrence-scope-group" role="radiogroup" aria-label="Edit scope">
            <button
              type="button"
              role="radio"
              aria-checked={editScope === 'single'}
              className={`mode-chip ${editScope === 'single' ? 'is-active' : ''}`}
              onClick={() => setEditScope('single')}
            >
              This event only
            </button>
            <button
              type="button"
              role="radio"
              aria-checked={editScope === 'series'}
              className={`mode-chip ${editScope === 'series' ? 'is-active' : ''}`}
              onClick={() => setEditScope('series')}
            >
              All events in series
            </button>
          </div>
        )}

        <input
          id={`${prefix}-event-title`}
          type="text"
          className="input"
          placeholder="Event title"
          value={eventTitle}
          onChange={(e) => setEventTitle(e.target.value)}
          required
        />

        <div className="inline-event-meta-row">
          <select
            id={`${prefix}-event-calendar`}
            className="input"
            value={eventCalendarId || defaultCalendarId}
            onChange={(e) => setEventCalendarId(e.target.value)}
            aria-label="Calendar"
          >
            {calendars.map((cal) => (
              <option key={cal.id} value={cal.id}>
                {cal.name}
              </option>
            ))}
          </select>
          <label className="calendar-allday-toggle">
            <input
              id={`${prefix}-event-allday`}
              type="checkbox"
              checked={eventIsAllDay}
              onChange={(e) => setEventIsAllDay(e.target.checked)}
            />
            <span>All day</span>
          </label>
        </div>

        <div className="inline-event-datetime-grid">
          <label className="calendar-field-label">
            <span className="meta-text">Start</span>
            <div className="inline-event-dt-pair">
              <input
                id={`${prefix}-event-start-date`}
                type="date"
                className="input mono-num"
                value={eventStartDate}
                onChange={(e) => {
                  const nextDate = e.target.value;
                  setEventStartDate(nextDate);
                  if (nextDate > eventEndDate) {
                    setEventEndDate(nextDate);
                  }
                  if (recurrence.freq === 'NONE' && nextDate) {
                    setRecurrence((prev) => ({
                      ...prev,
                      byDays: [dateKeyToWeekdayCode(nextDate)],
                    }));
                  }
                }}
                required
              />
              {!eventIsAllDay && (
                <TimeInput
                  id={`${prefix}-event-start-time`}
                  className="input mono-num"
                  timeFormat={timeFormat}
                  value={eventStartTime}
                  onChange={setEventStartTime}
                  required
                  ariaLabel="Event start time"
                />
              )}
            </div>
          </label>

          <label className="calendar-field-label">
            <span className="meta-text">End</span>
            <div className="inline-event-dt-pair">
              <input
                id={`${prefix}-event-end-date`}
                type="date"
                className="input mono-num"
                value={eventEndDate}
                min={eventStartDate}
                onChange={(e) => setEventEndDate(e.target.value)}
                required
              />
              {!eventIsAllDay && (
                <TimeInput
                  id={`${prefix}-event-end-time`}
                  className="input mono-num"
                  timeFormat={timeFormat}
                  value={eventEndTime}
                  onChange={setEventEndTime}
                  required
                  ariaLabel="Event end time"
                />
              )}
            </div>
          </label>
        </div>

        {showRepeatControls && (
          <div className="recurrence-controls">
            <label className="calendar-field-label">
              <span className="meta-text">Repeat</span>
              <select
                id={`${prefix}-event-repeat-freq`}
                className="input"
                value={recurrence.freq}
                onChange={(e) => {
                  const nextFreq = e.target.value as RecurrenceFrequency;
                  setRecurrence((prev) => ({
                    ...prev,
                    freq: nextFreq,
                    byDays:
                      nextFreq === 'WEEKLY' && prev.byDays.length === 0
                        ? [dateKeyToWeekdayCode(eventStartDate)]
                        : prev.byDays,
                  }));
                }}
              >
                <option value="NONE">Does not repeat</option>
                <option value="DAILY">Daily</option>
                <option value="WEEKLY">Weekly</option>
                <option value="MONTHLY">Monthly</option>
                <option value="YEARLY">Yearly</option>
              </select>
            </label>

            {recurrence.freq !== 'NONE' && (
              <>
                <div className="recurrence-interval-row">
                  <span className="meta-text">Every</span>
                  <input
                    id={`${prefix}-event-repeat-interval`}
                    type="number"
                    min={1}
                    max={999}
                    className="input mono-num recurrence-num-input"
                    value={recurrence.interval}
                    onChange={(e) =>
                      setRecurrence((prev) => ({
                        ...prev,
                        interval: Math.max(1, Number.parseInt(e.target.value, 10) || 1),
                      }))
                    }
                    aria-label="Repeat interval"
                  />
                  <span className="meta-text">{intervalUnit}</span>
                </div>

                {recurrence.freq === 'WEEKLY' && (
                  <div className="recurrence-weekdays" role="group" aria-label="Repeat on weekdays">
                    {WEEKDAY_CODES.map((code) => {
                      const active = recurrence.byDays.includes(code);
                      return (
                        <button
                          key={code}
                          type="button"
                          className={`recurrence-day-btn ${active ? 'is-active' : ''}`}
                          aria-pressed={active}
                          onClick={() => toggleWeekday(code)}
                        >
                          {code[0] + code[1].toLowerCase()}
                        </button>
                      );
                    })}
                  </div>
                )}

                <label className="calendar-field-label">
                  <span className="meta-text">Ends</span>
                  <select
                    id={`${prefix}-event-repeat-end-mode`}
                    className="input"
                    value={recurrence.endMode}
                    onChange={(e) =>
                      setRecurrence((prev) => ({
                        ...prev,
                        endMode: e.target.value as RecurrenceEndMode,
                      }))
                    }
                  >
                    <option value="NEVER">Never</option>
                    <option value="UNTIL">On date</option>
                    <option value="COUNT">After occurrences</option>
                  </select>
                </label>

                {recurrence.endMode === 'UNTIL' && (
                  <input
                    id={`${prefix}-event-repeat-until`}
                    type="date"
                    className="input mono-num"
                    min={eventStartDate}
                    value={recurrence.untilDate}
                    onChange={(e) =>
                      setRecurrence((prev) => ({
                        ...prev,
                        untilDate: e.target.value,
                      }))
                    }
                    aria-label="Repeat until date"
                    required
                  />
                )}

                {recurrence.endMode === 'COUNT' && (
                  <div className="recurrence-interval-row">
                    <span className="meta-text">After</span>
                    <input
                      id={`${prefix}-event-repeat-count`}
                      type="number"
                      min={1}
                      max={5000}
                      className="input mono-num recurrence-num-input"
                      value={recurrence.count}
                      onChange={(e) =>
                        setRecurrence((prev) => ({
                          ...prev,
                          count: Math.max(1, Number.parseInt(e.target.value, 10) || 1),
                        }))
                      }
                      aria-label="Number of occurrences"
                    />
                    <span className="meta-text">occurrences</span>
                  </div>
                )}
              </>
            )}
          </div>
        )}

        <input
          id={`${prefix}-event-location`}
          type="text"
          className="input"
          placeholder="Location (room, address, or link)"
          value={eventLocation}
          onChange={(e) => setEventLocation(e.target.value)}
        />

        <textarea
          id={`${prefix}-event-description`}
          className="input calendar-textarea"
          placeholder="Notes / description (optional)"
          rows={2}
          value={eventDescription}
          onChange={(e) => setEventDescription(e.target.value)}
        />

        {editingEventRefObj && onOpenLinkedNote && (() => {
          const cal = calendars.find((c) => c.id === editingEventRefObj.calendarId);
          const isUni =
            cal?.module === 'university' ||
            Boolean(editingEventRefObj.universityActivityItemId);

          if (isUni) {
            return editingEventRefObj.universityActivityItemId ? (
              <div className="cal-event-notes-section">
                <button
                  type="button"
                  className="btn btn-secondary btn-xs"
                  onClick={() => {
                    closeEventForm();
                    onOpenLinkedNote({
                      kind: 'university_item',
                      universityActivityItemId:
                        editingEventRefObj.universityActivityItemId!,
                    });
                  }}
                >
                  Open University Item Note
                </button>
              </div>
            ) : null;
          }

          const masterId =
            editingEventRefObj.seriesMasterId ?? editingEventRefObj.id;
          const occStart =
            editingEventRefObj.occurrenceStartAt ?? editingEventRefObj.startAt;

          return (
            <div className="cal-event-notes-section">
              <div className="cal-event-notes-actions">
                {editingEventRefObj.isRecurring ? (
                  <>
                    <button
                      type="button"
                      className="btn btn-secondary btn-xs"
                      onClick={() => {
                        closeEventForm();
                        onOpenLinkedNote({
                          kind: 'calendar_event',
                          calendarEventId: masterId,
                          occurrenceStartAt: occStart,
                        });
                      }}
                    >
                      Open Note for this Date
                    </button>
                    <button
                      type="button"
                      className="btn btn-ghost btn-xs"
                      onClick={() => {
                        closeEventForm();
                        onOpenLinkedNote({
                          kind: 'calendar_event',
                          calendarEventId: masterId,
                          occurrenceStartAt: null,
                        });
                      }}
                    >
                      Open Shared Series Note
                    </button>
                  </>
                ) : (
                  <button
                    type="button"
                    className="btn btn-secondary btn-xs"
                    onClick={() => {
                      closeEventForm();
                      onOpenLinkedNote({
                        kind: 'calendar_event',
                        calendarEventId: masterId,
                        occurrenceStartAt: null,
                      });
                    }}
                  >
                    {editingEventRefObj.hasNote ? 'Open Note' : 'Create Note'}
                  </button>
                )}

                <button
                  type="button"
                  className="btn btn-ghost btn-xs"
                  disabled={uploadingEventArtifact}
                  onClick={() => eventArtifactInputRef.current?.click()}
                >
                  {uploadingEventArtifact ? 'Uploading…' : '+ Attach file'}
                </button>
                <input
                  ref={eventArtifactInputRef}
                  type="file"
                  hidden
                  onChange={(e) => void handleUploadEventArtifact(e)}
                />
              </div>

              {editingEventArtifacts.length > 0 && (
                <div className="uni-course-notes-list">
                  {editingEventArtifacts.map((art) => (
                    <span key={art.id} className="notes-artifact-chip">
                      <button
                        type="button"
                        className="notes-artifact-download"
                        onClick={() =>
                          void downloadAttachment(art.id, art.fileName)
                        }
                        title={`Download ${art.fileName} (${formatFileSize(art.sizeBytes)})`}
                      >
                        <span>📎</span>
                        <span className="notes-artifact-name">{art.fileName}</span>
                      </button>
                      <button
                        type="button"
                        className="notes-artifact-remove"
                        aria-label={`Delete ${art.fileName}`}
                        onClick={() => void handleDeleteEventArtifact(art)}
                      >
                        ×
                      </button>
                    </span>
                  ))}
                </div>
              )}
            </div>
          );
        })()}

        {eventError && <p className="calendar-inline-error">{eventError}</p>}

        <button
          id={`${prefix}-event-submit`}
          type="submit"
          className="btn btn-primary btn-compact"
          disabled={eventSaving}
        >
          {eventSaving
            ? 'Saving…'
            : editingEventId
              ? editingIsRecurring
                ? editScope === 'single'
                  ? 'Save this event'
                  : 'Save entire series'
                : 'Save changes'
              : `Add to ${formatShortDate(parseDateKey(eventStartDate || selectedDateKey))}`}
        </button>
      </form>
    );
  };

  const renderEventRow = (evt: CalendarEvent, headingLevel: 'h3' | 'h4') => {
    const instanceKey = eventInstanceKey(evt);
    const timeLabels = formatEventTimeLabels(evt, timeFormat);
    const hasOverlap = overlappingEventKeys.has(instanceKey);
    const isConfirmingDelete = pendingDeleteKey === instanceKey;
    const Heading = headingLevel;

    return (
      <article
        key={instanceKey}
        className="card-soft event-row-card"
        style={{ borderLeft: `3px solid ${evt.calendarColor}` }}
      >
        <div className="event-row">
          <div className="event-time-col">
            <span className="mono-num event-start">{timeLabels.primary}</span>
            {timeLabels.secondary && (
              <span className="mono-num event-duration">{timeLabels.secondary}</span>
            )}
          </div>
          <div className="event-body">
            <Heading className="row-title">{evt.title}</Heading>
            <div className="event-meta">
              <span className="calendar-name-tag">
                <span
                  className="calendar-swatch-dot"
                  style={{ backgroundColor: evt.calendarColor }}
                  aria-hidden="true"
                />
                {evt.calendarName}
              </span>
              {evt.isRecurring && (
                <span
                  className="pill-muted"
                  title={formatRecurrenceSummary(evt.recurrenceRule)}
                >
                  {evt.isRecurrenceException ? 'Series · Edited' : 'Repeats'}
                </span>
              )}
              {evt.location && <span className="meta-text">{evt.location}</span>}
              {hasOverlap && (
                <span className="pill-overlap" title="Overlaps another scheduled event">
                  Overlap
                </span>
              )}
              {evt.source && evt.source !== 'Manual' && (
                <span className="pill-accent">{evt.source}</span>
              )}
            </div>
            {evt.description && (
              <p className="meta-text event-description">{evt.description}</p>
            )}
          </div>
          <div className="event-row-actions">
            <button
              type="button"
              className="btn btn-ghost btn-xs"
              onClick={() => openEditEventForm(evt)}
              aria-label={`Edit event: ${evt.title}`}
            >
              Edit
            </button>
            <button
              type="button"
              className="btn btn-ghost btn-icon-sm row-remove-btn"
              onClick={() => {
                if (evt.isRecurring) {
                  setPendingDeleteKey((prev) =>
                    prev === instanceKey ? null : instanceKey,
                  );
                } else {
                  void onDeleteEvent(evt, 'series');
                }
              }}
              aria-label={`Remove event: ${evt.title}`}
              title="Remove event"
            >
              <CloseIcon />
            </button>
          </div>
        </div>

        {isConfirmingDelete && evt.isRecurring && (
          <div className="recurrence-delete-bar" role="group" aria-label="Delete recurring event options">
            <span className="meta-text">Delete:</span>
            <button
              type="button"
              className="btn btn-secondary btn-xs"
              onClick={() => {
                setPendingDeleteKey(null);
                void onDeleteEvent(evt, 'single');
              }}
            >
              This event
            </button>
            <button
              type="button"
              className="btn btn-danger btn-xs"
              onClick={() => {
                setPendingDeleteKey(null);
                void onDeleteEvent(evt, 'series');
              }}
            >
              Entire series
            </button>
            <button
              type="button"
              className="btn btn-ghost btn-xs"
              onClick={() => setPendingDeleteKey(null)}
            >
              Cancel
            </button>
          </div>
        )}
      </article>
    );
  };

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
              aria-label="Open expanded calendar manager"
              title="Expand calendar & manage calendars"
            >
              <ExpandIcon />
            </button>
          </div>
        </div>

        {/* Multi-calendar visibility chips */}
        <div className="calendar-chip-bar" aria-label="Calendars">
          {calendars.map((cal) => (
            <button
              key={cal.id}
              type="button"
              className={`calendar-filter-chip ${cal.isVisible ? 'is-visible' : 'is-hidden'}`}
              aria-pressed={cal.isVisible}
              onClick={() => void onToggleCalendarVisibility(cal)}
              title={
                cal.isVisible
                  ? `Hide "${cal.name}" calendar`
                  : `Show "${cal.name}" calendar`
              }
            >
              <span
                className="calendar-swatch-dot"
                style={{
                  backgroundColor: cal.isVisible ? cal.color : 'transparent',
                  borderColor: cal.color,
                }}
                aria-hidden="true"
              />
              <span>{cal.name}</span>
            </button>
          ))}
          <button
            id="calendar-manage-open-btn"
            type="button"
            className="calendar-filter-chip calendar-filter-action"
            onClick={() => {
              onToggleExpanded(true);
              setShowImportPanel(false);
              setShowNewCalendarForm(true);
            }}
            title="Create or import calendars"
          >
            <PlusIcon />
            <span>Calendar</span>
          </button>
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
          aria-label={formatMonthYear(viewMonthDate)}
        >
          {monthCells.map((cell) => {
            const isToday = cell.dateKey === todayKey;
            const isSelected = cell.dateKey === selectedDateKey;
            const dayEvents = eventsByCellKey.get(cell.dateKey) ?? [];
            const dotColors = Array.from(
              new Set(dayEvents.map((e) => e.calendarColor)),
            ).slice(0, 3);

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
                  dayEvents.length > 0 ? `, ${dayEvents.length} events` : ''
                }`}
                aria-pressed={isSelected}
                onClick={() => onSelectDate(cell.dateKey)}
              >
                <span>{cell.dayNumber}</span>
                {dotColors.length > 0 && (
                  <span className="calendar-day-dots" aria-hidden="true">
                    {dotColors.map((color) => (
                      <span
                        key={color}
                        className="calendar-day-dot-colored"
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
      </section>

      <section className="card" aria-labelledby="schedule-heading">
        <div className="card-header">
          <h2 id="schedule-heading" className="card-title">
            {selectedHeaderLabel}
          </h2>
          <div className="calendar-header-buttons">
            <button
              id="schedule-import-trigger"
              type="button"
              className="btn btn-ghost btn-xs"
              onClick={() => {
                onToggleExpanded(true);
                setShowNewCalendarForm(false);
                setShowImportPanel(true);
              }}
              title="Import standard .ics calendar file"
            >
              <UploadIcon />
              <span>.ics</span>
            </button>
            <button
              id="schedule-add-toggle"
              type="button"
              className="btn btn-ghost btn-xs"
              onClick={() =>
                showEventForm ? closeEventForm() : openNewEventForm()
              }
              aria-expanded={showEventForm}
            >
              <PlusIcon />
              <span>{showEventForm ? 'Close' : 'Add'}</span>
            </button>
          </div>
        </div>

        {showEventForm && !isExpandedOpen && renderEventForm('inline')}

        {loading ? (
          <p className="empty-state meta-text">Loading calendar…</p>
        ) : selectedDayEvents.length === 0 ? (
          <p className="empty-state meta-text">
            Open schedule for {formatShortDate(selectedDate)}.
          </p>
        ) : (
          <div className="stack-sm">
            {selectedDayEvents.map((evt) => renderEventRow(evt, 'h3'))}
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
            <p className="kicker">CALENDARS &amp; SCHEDULE</p>
            <h2 id="calendar-modal-title" className="dialog-title">
              {formatMonthYear(viewMonthDate)}
            </h2>
          </div>
          <div className="dialog-header-actions">
            <button
              id="calendar-modal-import-btn"
              type="button"
              className="btn btn-secondary btn-compact"
              onClick={() => {
                setShowImportPanel((prev) => {
                  const next = !prev;
                  if (next) setShowNewCalendarForm(false);
                  return next;
                });
              }}
            >
              <UploadIcon />
              <span>Import .ics</span>
            </button>
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

        {/* Multi-calendar manager & ICS import toolbar */}
        <div className="calendar-manager-strip card-soft">
          <div className="calendar-manager-header">
            <span className="kicker">YOUR CALENDARS ({calendars.length})</span>
            <button
              id="calendar-new-toggle"
              type="button"
              className="btn btn-ghost btn-xs"
              onClick={() => {
                setShowNewCalendarForm((prev) => {
                  const next = !prev;
                  if (next) setShowImportPanel(false);
                  return next;
                });
              }}
            >
              <PlusIcon />
              <span>{showNewCalendarForm ? 'Close' : 'New calendar'}</span>
            </button>
          </div>

          <div className="calendar-manager-list">
            {calendars.map((cal) => {
              const isConfirmingCalDelete = pendingDeleteCalendarId === cal.id;
              return (
                <div key={cal.id}>
                  <div className="calendar-manager-item">
                    <label className="calendar-manager-toggle">
                      <input
                        type="checkbox"
                        checked={cal.isVisible}
                        onChange={() => void onToggleCalendarVisibility(cal)}
                      />
                      <span
                        className="calendar-swatch-dot"
                        style={{ backgroundColor: cal.color }}
                        aria-hidden="true"
                      />
                      <span className="calendar-manager-name">{cal.name}</span>
                      {cal.isDefault && <span className="pill-muted">Default</span>}
                      {cal.module && <span className="pill-muted">Module</span>}
                    </label>
                    <div className="calendar-manager-item-actions">
                      <button
                        type="button"
                        className="btn btn-ghost btn-icon-sm"
                        onClick={() => void onExportIcs(cal)}
                        title={`Export "${cal.name}" as .ics`}
                        aria-label={`Export ${cal.name} as .ics`}
                      >
                        <DownloadIcon />
                      </button>
                      {calendars.length > 1 && !cal.module && (
                        <button
                          type="button"
                          className="btn btn-ghost btn-icon-sm"
                          onClick={() =>
                            setPendingDeleteCalendarId((prev) =>
                              prev === cal.id ? null : cal.id,
                            )
                          }
                          title={`Delete "${cal.name}" calendar`}
                          aria-label={`Delete ${cal.name} calendar`}
                          aria-expanded={isConfirmingCalDelete}
                        >
                          <CloseIcon />
                        </button>
                      )}
                    </div>
                  </div>
                  {isConfirmingCalDelete && (
                    <div
                      className="recurrence-delete-bar"
                      role="group"
                      aria-label={`Confirm delete ${cal.name}`}
                    >
                      <span className="meta-text">Delete calendar &amp; its events?</span>
                      <button
                        type="button"
                        className="btn btn-danger btn-xs"
                        onClick={() => {
                          setPendingDeleteCalendarId(null);
                          void onDeleteCalendar(cal.id);
                        }}
                      >
                        Delete
                      </button>
                      <button
                        type="button"
                        className="btn btn-ghost btn-xs"
                        onClick={() => setPendingDeleteCalendarId(null)}
                      >
                        Cancel
                      </button>
                    </div>
                  )}
                </div>
              );
            })}
          </div>

          {showNewCalendarForm && (
            <form
              className="calendar-create-form"
              onSubmit={(e) => void handleCreateCalendarSubmit(e)}
            >
              <div className="calendar-create-inputs">
                <input
                  id="new-calendar-name"
                  type="text"
                  className="input"
                  placeholder="Calendar name (e.g. Work, Family, Holidays)"
                  value={calName}
                  onChange={(e) => setCalName(e.target.value)}
                  required
                />
                <input
                  id="new-calendar-desc"
                  type="text"
                  className="input"
                  placeholder="Description (optional)"
                  value={calDescription}
                  onChange={(e) => setCalDescription(e.target.value)}
                />
              </div>
              <div className="calendar-create-footer">
                <div className="calendar-color-swatches" role="radiogroup" aria-label="Calendar color">
                  {CALENDAR_COLOR_PALETTE.map((hex) => (
                    <button
                      key={hex}
                      type="button"
                      role="radio"
                      className={`calendar-color-swatch ${calColor === hex ? 'is-selected' : ''}`}
                      style={{ backgroundColor: hex }}
                      aria-label={`Color ${hex}`}
                      aria-checked={calColor === hex}
                      onClick={() => setCalColor(hex)}
                    />
                  ))}
                </div>
                <button
                  id="new-calendar-submit"
                  type="submit"
                  className="btn btn-primary btn-compact"
                  disabled={calSaving}
                >
                  {calSaving ? 'Creating…' : 'Create calendar'}
                </button>
              </div>
              {calError && <p className="calendar-inline-error">{calError}</p>}
            </form>
          )}

          {showImportPanel && (
            <div className="calendar-import-panel">
              <div className="calendar-import-row">
                <label className="calendar-field-label">
                  <span className="meta-text">Import target</span>
                  <select
                    id="ics-import-target"
                    className="input"
                    value={importTarget}
                    onChange={(e) => setImportTarget(e.target.value)}
                  >
                    <option value="new">Create new calendar from .ics file</option>
                    {calendars.map((cal) => (
                      <option key={cal.id} value={cal.id}>
                        Into existing: {cal.name}
                      </option>
                    ))}
                  </select>
                </label>

                <label className="btn btn-primary btn-compact calendar-file-btn">
                  <UploadIcon />
                  <span>{importBusy ? 'Importing…' : 'Choose .ics file'}</span>
                  <input
                    ref={icsInputRef}
                    id="ics-file-input"
                    type="file"
                    accept=".ics,.ical,.ifb,.icalendar,text/calendar"
                    onChange={(e) => void handleIcsFileChange(e)}
                    disabled={importBusy}
                    hidden
                  />
                </label>
              </div>
              <p className="meta-text">
                Supports standard RFC 5545 iCalendar files exported from Google Calendar, Apple Calendar, Outlook, or Fastmail. Re-importing updates matching UIDs without duplicates.
              </p>
              {importError && <p className="calendar-inline-error">{importError}</p>}
            </div>
          )}
        </div>

        <div className="calendar-modal-layout">
          <div className="calendar-modal-month">
            <div className="calendar-weekdays" aria-hidden="true">
              {weekdays.map((day) => (
                <span key={day} className="calendar-weekday">
                  {day}
                </span>
              ))}
            </div>

            <div className="calendar-modal-grid">
              {monthCells.map((cell) => {
                const isToday = cell.dateKey === todayKey;
                const isSelected = cell.dateKey === selectedDateKey;
                const dayEvents = eventsByCellKey.get(cell.dateKey) ?? [];

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
                    <div className="calendar-modal-cell-head">
                      <span className="calendar-modal-day-num mono-num">
                        {cell.dayNumber}
                      </span>
                      {dayEvents.length > 2 && (
                        <span
                          className="meta-text mono-num calendar-cell-more"
                          title={`${dayEvents.length} events on this day`}
                        >
                          +{dayEvents.length - 2}
                        </span>
                      )}
                    </div>
                    <div className="calendar-modal-cell-events">
                      {dayEvents.slice(0, 2).map((evt) => {
                        const labels = formatEventTimeLabels(evt, timeFormat);
                        return (
                          <span
                            key={eventInstanceKey(evt)}
                            className="calendar-cell-chip"
                            style={{ borderLeft: `3px solid ${evt.calendarColor}` }}
                            title={`${labels.primary} · ${evt.title} (${evt.calendarName})`}
                          >
                            {evt.title}
                          </span>
                        );
                      })}
                    </div>
                  </button>
                );
              })}
            </div>
          </div>

          <aside className="calendar-modal-detail">
            <div className="calendar-modal-detail-head">
              <div>
                <h3 className="card-title">{formatShortDate(selectedDate)}</h3>
                <p className="meta-text">
                  {selectedDayEvents.length === 0
                    ? 'No scheduled events'
                    : `${selectedDayEvents.length} ${
                        selectedDayEvents.length === 1 ? 'event' : 'events'
                      }${
                        overlappingEventKeys.size > 0
                          ? ` · ${overlappingEventKeys.size} overlapping`
                          : ''
                      }`}
                </p>
              </div>
              <button
                id="modal-add-event-btn"
                type="button"
                className="btn btn-primary btn-xs"
                onClick={() =>
                  showEventForm ? closeEventForm() : openNewEventForm()
                }
              >
                <PlusIcon />
                <span>{showEventForm ? 'Close' : 'Add'}</span>
              </button>
            </div>

            <div className="calendar-modal-detail-body">
              {showEventForm && isExpandedOpen && renderEventForm('modal')}

              <div className="stack-sm">
                {selectedDayEvents.length === 0 ? (
                  <p className="empty-state meta-text">
                    No visible events scheduled for this day. Add an event or import an .ics file.
                  </p>
                ) : (
                  selectedDayEvents.map((evt) => renderEventRow(evt, 'h4'))
                )}
              </div>
            </div>
          </aside>
        </div>
      </dialog>
    </>
  );
}
