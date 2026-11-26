import type {
  CalendarEvent,
  CalendarEventStatus,
  IcsImportSummary,
  UserCalendar,
} from '../types/atrium';
import { apiFetch, authenticatedRawFetch } from './authClient';

export const CALENDAR_COLOR_PALETTE = [
  '#c25438', // Terracotta
  '#2f6f55', // Pine
  '#3b6ea5', // Slate Blue
  '#b4691e', // Ochre
  '#7c4d9c', // Plum
  '#2d7d7d', // Teal
  '#a63f5b', // Berry
  '#5c6670', // Graphite
] as const;

export interface CreateCalendarInput {
  name: string;
  description?: string;
  color?: string;
  timezone?: string;
  isDefault?: boolean;
  isVisible?: boolean;
}

export interface UpdateCalendarInput {
  name: string;
  description?: string;
  color?: string;
  timezone?: string;
  isDefault?: boolean;
  isVisible?: boolean;
}

export interface CreateEventInput {
  calendarId?: string;
  title: string;
  startAt: string;
  endAt: string;
  isAllDay?: boolean;
  location?: string | null;
  description?: string | null;
  timezone?: string | null;
  status?: CalendarEventStatus;
  recurrenceRule?: string | null;
  source?: string | null;
}

export interface UpdateEventInput {
  calendarId: string;
  title: string;
  startAt: string;
  endAt: string;
  isAllDay?: boolean;
  location?: string | null;
  description?: string | null;
  timezone?: string | null;
  status?: CalendarEventStatus;
  recurrenceRule?: string | null;
}

export async function listCalendars(): Promise<UserCalendar[]> {
  return apiFetch<UserCalendar[]>('/api/calendars', { method: 'GET' });
}

export async function createCalendar(input: CreateCalendarInput): Promise<UserCalendar> {
  return apiFetch<UserCalendar>('/api/calendars', {
    method: 'POST',
    body: JSON.stringify(input),
  });
}

export async function updateCalendar(
  id: string,
  input: UpdateCalendarInput,
): Promise<UserCalendar> {
  return apiFetch<UserCalendar>(`/api/calendars/${id}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  });
}

export async function deleteCalendar(id: string): Promise<void> {
  return apiFetch<void>(`/api/calendars/${id}`, { method: 'DELETE' });
}

export async function listEvents(params?: {
  from?: string;
  to?: string;
  calendarIds?: string[];
  visibleOnly?: boolean;
}): Promise<CalendarEvent[]> {
  const search = new URLSearchParams();
  if (params?.from) search.set('from', params.from);
  if (params?.to) search.set('to', params.to);
  if (params?.visibleOnly) search.set('visibleOnly', 'true');
  if (params?.calendarIds) {
    for (const id of params.calendarIds) {
      search.append('calendarId', id);
    }
  }
  const query = search.toString();
  return apiFetch<CalendarEvent[]>(`/api/events${query ? `?${query}` : ''}`, {
    method: 'GET',
  });
}

export async function createEvent(input: CreateEventInput): Promise<CalendarEvent> {
  return apiFetch<CalendarEvent>('/api/events', {
    method: 'POST',
    body: JSON.stringify(input),
  });
}

export async function updateEvent(
  id: string,
  input: UpdateEventInput,
): Promise<CalendarEvent> {
  return apiFetch<CalendarEvent>(`/api/events/${id}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  });
}

export async function deleteEvent(id: string): Promise<void> {
  return apiFetch<void>(`/api/events/${id}`, { method: 'DELETE' });
}

export async function importIcsToCalendar(
  calendarId: string,
  file: File,
): Promise<IcsImportSummary> {
  const form = new FormData();
  form.append('file', file);
  return apiFetch<IcsImportSummary>(`/api/calendars/${calendarId}/import/ics`, {
    method: 'POST',
    body: form,
  });
}

export async function importIcsAsNewCalendar(
  file: File,
  options?: { name?: string; color?: string },
): Promise<IcsImportSummary> {
  const form = new FormData();
  form.append('file', file);
  if (options?.name?.trim()) form.append('name', options.name.trim());
  if (options?.color?.trim()) form.append('color', options.color.trim());
  return apiFetch<IcsImportSummary>('/api/calendars/import/ics', {
    method: 'POST',
    body: form,
  });
}

export async function downloadCalendarIcs(
  calendarId: string,
  calendarName: string,
): Promise<void> {
  const response = await authenticatedRawFetch(`/api/calendars/${calendarId}/export/ics`, {
    method: 'GET',
  });
  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  const safeName =
    calendarName
      .toLowerCase()
      .replace(/[^a-z0-9_-]+/g, '-')
      .replace(/^-+|-+$/g, '') || 'calendar';
  link.href = url;
  link.download = `${safeName}.ics`;
  link.click();
  URL.revokeObjectURL(url);
}

/** Format a local Date into YYYY-MM-DD */
export function dateToKey(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

/** Format a UTC Date into YYYY-MM-DD */
function utcDateToKey(date: Date): string {
  const year = date.getUTCFullYear();
  const month = String(date.getUTCMonth() + 1).padStart(2, '0');
  const day = String(date.getUTCDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

/** Convert local YYYY-MM-DD + HH:MM into UTC ISO-8601 string */
export function localDateTimeToIso(dateKey: string, timeHHMM: string): string {
  const [year, month, day] = dateKey.split('-').map(Number);
  const [hour, minute] = (timeHHMM || '09:00').split(':').map(Number);
  return new Date(year, month - 1, day, hour || 0, minute || 0, 0, 0).toISOString();
}

/** Convert inclusive all-day start/end YYYY-MM-DD keys to exclusive UTC midnight ISO range */
export function allDayDatesToIsoRange(
  startDateKey: string,
  endDateKeyInclusive: string,
): { startAt: string; endAt: string } {
  const [sy, sm, sd] = startDateKey.split('-').map(Number);
  const [ey, em, ed] = (endDateKeyInclusive || startDateKey).split('-').map(Number);
  const startUtc = new Date(Date.UTC(sy, sm - 1, sd, 0, 0, 0));
  const endInclusiveUtc = new Date(Date.UTC(ey, em - 1, ed, 0, 0, 0));
  const endExclusiveUtc = new Date(
    Math.max(startUtc.getTime(), endInclusiveUtc.getTime()) + 86_400_000,
  );
  return {
    startAt: startUtc.toISOString(),
    endAt: endExclusiveUtc.toISOString(),
  };
}

/** Extract local YYYY-MM-DD and HH:MM from an event for form editing */
export function eventToFormFields(evt: CalendarEvent): {
  startDate: string;
  startTime: string;
  endDate: string;
  endTime: string;
} {
  const start = new Date(evt.startAt);
  const end = new Date(evt.endAt);
  if (evt.isAllDay) {
    const startDate = utcDateToKey(start);
    const inclusiveEnd = new Date(Math.max(start.getTime(), end.getTime() - 86_400_000));
    return {
      startDate,
      startTime: '09:00',
      endDate: utcDateToKey(inclusiveEnd),
      endTime: '10:00',
    };
  }
  const pad = (n: number) => String(n).padStart(2, '0');
  return {
    startDate: dateToKey(start),
    startTime: `${pad(start.getHours())}:${pad(start.getMinutes())}`,
    endDate: dateToKey(end),
    endTime: `${pad(end.getHours())}:${pad(end.getMinutes())}`,
  };
}

/** Check if an event occurs on the given local YYYY-MM-DD date key */
export function eventOccursOnDateKey(evt: CalendarEvent, dateKey: string): boolean {
  if (evt.isAllDay) {
    const startKey = evt.startAt.slice(0, 10);
    const endDate = new Date(evt.endAt);
    const inclusiveEndDate = new Date(
      Math.max(new Date(evt.startAt).getTime(), endDate.getTime() - 86_400_000),
    );
    const endKey = utcDateToKey(inclusiveEndDate);
    return dateKey >= startKey && dateKey <= endKey;
  }

  const [year, month, day] = dateKey.split('-').map(Number);
  const dayStartMs = new Date(year, month - 1, day, 0, 0, 0, 0).getTime();
  const dayEndMs = new Date(year, month - 1, day + 1, 0, 0, 0, 0).getTime();
  const evtStartMs = new Date(evt.startAt).getTime();
  const evtEndMs = new Date(evt.endAt).getTime();

  if (evtStartMs === evtEndMs) {
    return evtStartMs >= dayStartMs && evtStartMs < dayEndMs;
  }
  return evtStartMs < dayEndMs && evtEndMs > dayStartMs;
}

/** True if two timed events have overlapping time intervals */
export function eventsOverlap(a: CalendarEvent, b: CalendarEvent): boolean {
  if (a.id === b.id || a.isAllDay || b.isAllDay) return false;
  const aStart = new Date(a.startAt).getTime();
  const aEnd = new Date(a.endAt).getTime();
  const bStart = new Date(b.startAt).getTime();
  const bEnd = new Date(b.endAt).getTime();
  if (aStart === aEnd || bStart === bEnd) return aStart === bStart;
  return aStart < bEnd && bStart < aEnd;
}

/** Format event start time and end/duration labels respecting 12h/24h preference */
export function formatEventTimeLabels(
  evt: CalendarEvent,
  timeFormat: '24h' | '12h' = '24h',
): { primary: string; secondary: string } {
  if (evt.isAllDay) {
    return { primary: 'All day', secondary: '' };
  }

  const start = new Date(evt.startAt);
  const end = new Date(evt.endAt);
  const formatter = new Intl.DateTimeFormat('en-US', {
    hour: '2-digit',
    minute: '2-digit',
    hour12: timeFormat === '12h',
  });

  const primary = formatter.format(start);
  const diffMinutes = Math.max(0, Math.round((end.getTime() - start.getTime()) / 60_000));
  if (diffMinutes === 0) {
    return { primary, secondary: '0m' };
  }
  if (diffMinutes < 60) {
    return { primary, secondary: `${diffMinutes}m` };
  }
  const hours = Math.floor(diffMinutes / 60);
  const rem = diffMinutes % 60;
  return {
    primary,
    secondary: rem > 0 ? `${hours}h ${rem}m` : `${hours}h`,
  };
}

/** Parse a duration string like "30m", "45m", "90m" into minutes */
export function parseDurationMinutes(duration: string): number {
  const match = duration.trim().match(/^(\d+)\s*m$/i);
  if (match) return Number(match[1]);
  const hrMatch = duration.trim().match(/^(\d+)\s*h$/i);
  if (hrMatch) return Number(hrMatch[1]) * 60;
  return 30;
}
