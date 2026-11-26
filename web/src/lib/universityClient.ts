import type {
  AttendanceSummary,
  UniversityActivityItem,
  UniversityActivityType,
  UniversityAttendanceStatus,
  UniversityCourse,
  UniversityCourseActivity,
  UniversityCourseStatus,
  UniversityWorkspace,
} from '../types/atrium';
import { apiFetch } from './authClient';

export interface ActivityTypeDescriptor {
  type: UniversityActivityType;
  label: string;
  shortLabel: string;
  supportsAttendance: boolean;
  attendedVerb: string;
  defaultDurationMinutes: number;
}

export const UNIVERSITY_ACTIVITY_TYPES: readonly ActivityTypeDescriptor[] = [
  {
    type: 'lecture',
    label: 'Lecture',
    shortLabel: 'Lecture',
    supportsAttendance: true,
    attendedVerb: 'Attended',
    defaultDurationMinutes: 90,
  },
  {
    type: 'practice',
    label: 'Practice',
    shortLabel: 'Practice',
    supportsAttendance: true,
    attendedVerb: 'Attended',
    defaultDurationMinutes: 90,
  },
  {
    type: 'laboratory',
    label: 'Laboratory',
    shortLabel: 'Lab',
    supportsAttendance: true,
    attendedVerb: 'Attended',
    defaultDurationMinutes: 120,
  },
  {
    type: 'homework_assignment',
    label: 'Homework Assignment',
    shortLabel: 'Homework',
    supportsAttendance: true,
    attendedVerb: 'Done',
    defaultDurationMinutes: 60,
  },
  {
    type: 'semester_assignment',
    label: 'Semester Assignment',
    shortLabel: 'Semester HW',
    supportsAttendance: false,
    attendedVerb: 'Done',
    defaultDurationMinutes: 60,
  },
  {
    type: 'midterm',
    label: 'Midterm',
    shortLabel: 'Midterm',
    supportsAttendance: false,
    attendedVerb: 'Completed',
    defaultDurationMinutes: 90,
  },
  {
    type: 'exam',
    label: 'Exam',
    shortLabel: 'Exam',
    supportsAttendance: false,
    attendedVerb: 'Completed',
    defaultDurationMinutes: 120,
  },
] as const;

export function getActivityDescriptor(
  type: UniversityActivityType,
): ActivityTypeDescriptor {
  return (
    UNIVERSITY_ACTIVITY_TYPES.find((item) => item.type === type) ??
    UNIVERSITY_ACTIVITY_TYPES[0]
  );
}

export function getActivityDisplayTitle(
  activity: Pick<UniversityCourseActivity, 'activityType' | 'title'>,
): string {
  const custom = activity.title?.trim();
  if (custom) return custom;
  return getActivityDescriptor(activity.activityType).label;
}

export function getAttendanceStatusLabel(
  status: UniversityAttendanceStatus,
  activityType: UniversityActivityType,
): string {
  if (status === 'attended') {
    return getActivityDescriptor(activityType).attendedVerb;
  }
  if (status === 'missed') return 'Missed';
  if (status === 'excused') return 'Excused';
  return 'Pending';
}

export interface CreateCourseInput {
  name: string;
  code?: string | null;
  semester?: string | null;
  description?: string | null;
  instructor?: string | null;
  credits?: number | null;
  color?: string | null;
  status?: UniversityCourseStatus;
  metadataJson?: string | null;
  activities?: CreateActivityInput[];
}

export interface UpdateCourseInput {
  name: string;
  code?: string | null;
  semester?: string | null;
  description?: string | null;
  instructor?: string | null;
  credits?: number | null;
  color?: string | null;
  status?: UniversityCourseStatus;
  metadataJson?: string | null;
}

export interface CreateActivityInput {
  activityType: UniversityActivityType;
  title?: string | null;
  description?: string | null;
  defaultLocation?: string | null;
  minAttendanceCount?: number | null;
  minAttendancePercent?: number | null;
  plannedTotalCount?: number | null;
  sortOrder?: number | null;
  metadataJson?: string | null;
}

export interface UpdateActivityInput {
  title?: string | null;
  description?: string | null;
  defaultLocation?: string | null;
  minAttendanceCount?: number | null;
  minAttendancePercent?: number | null;
  plannedTotalCount?: number | null;
  sortOrder?: number | null;
  metadataJson?: string | null;
}

export interface ActivityItemScheduleInput {
  startAt: string;
  endAt: string;
  isAllDay?: boolean;
  location?: string | null;
}

export interface CreateActivityItemInput {
  title: string;
  theme?: string | null;
  notes?: string | null;
  sequenceNumber?: number | null;
  attendanceStatus?: UniversityAttendanceStatus;
  schedule?: ActivityItemScheduleInput | null;
  metadataJson?: string | null;
}

export interface UpdateActivityItemInput {
  title: string;
  theme?: string | null;
  notes?: string | null;
  sequenceNumber?: number | null;
  attendanceStatus?: UniversityAttendanceStatus;
  schedule?: ActivityItemScheduleInput | null;
  removeCalendarEvent?: boolean;
  metadataJson?: string | null;
}

export interface GenerateSeriesInput {
  startAt: string;
  endAt: string;
  count: number;
  intervalWeeks?: number;
  titlePrefix?: string | null;
  startNumber?: number;
  isAllDay?: boolean;
  location?: string | null;
  defaultTheme?: string | null;
}

export interface UpdateAttendanceResponse {
  item: UniversityActivityItem;
  attendance: AttendanceSummary;
}

export interface GenerateSeriesResponse {
  createdItems: UniversityActivityItem[];
  attendance: AttendanceSummary;
}

export async function getUniversityWorkspace(params?: {
  semester?: string;
  status?: string;
}): Promise<UniversityWorkspace> {
  const search = new URLSearchParams();
  if (params?.semester) search.set('semester', params.semester);
  if (params?.status) search.set('status', params.status);
  const query = search.toString();
  return apiFetch<UniversityWorkspace>(
    `/api/university/workspace${query ? `?${query}` : ''}`,
    { method: 'GET' },
  );
}

export async function createUniversityCourse(
  input: CreateCourseInput,
): Promise<UniversityCourse> {
  return apiFetch<UniversityCourse>('/api/university/courses', {
    method: 'POST',
    body: JSON.stringify(input),
  });
}

export async function updateUniversityCourse(
  courseId: string,
  input: UpdateCourseInput,
): Promise<UniversityCourse> {
  return apiFetch<UniversityCourse>(`/api/university/courses/${courseId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  });
}

export async function deleteUniversityCourse(courseId: string): Promise<void> {
  return apiFetch<void>(`/api/university/courses/${courseId}`, {
    method: 'DELETE',
  });
}

export async function createUniversityActivity(
  courseId: string,
  input: CreateActivityInput,
): Promise<UniversityCourseActivity> {
  return apiFetch<UniversityCourseActivity>(
    `/api/university/courses/${courseId}/activities`,
    {
      method: 'POST',
      body: JSON.stringify(input),
    },
  );
}

export async function updateUniversityActivity(
  activityId: string,
  input: UpdateActivityInput,
): Promise<UniversityCourseActivity> {
  return apiFetch<UniversityCourseActivity>(
    `/api/university/activities/${activityId}`,
    {
      method: 'PUT',
      body: JSON.stringify(input),
    },
  );
}

export async function deleteUniversityActivity(activityId: string): Promise<void> {
  return apiFetch<void>(`/api/university/activities/${activityId}`, {
    method: 'DELETE',
  });
}

export async function createUniversityItem(
  activityId: string,
  input: CreateActivityItemInput,
): Promise<UniversityActivityItem> {
  return apiFetch<UniversityActivityItem>(
    `/api/university/activities/${activityId}/items`,
    {
      method: 'POST',
      body: JSON.stringify(input),
    },
  );
}

export async function updateUniversityItem(
  itemId: string,
  input: UpdateActivityItemInput,
): Promise<UniversityActivityItem> {
  return apiFetch<UniversityActivityItem>(`/api/university/items/${itemId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  });
}

export async function updateUniversityItemAttendance(
  itemId: string,
  attendanceStatus: UniversityAttendanceStatus,
): Promise<UpdateAttendanceResponse> {
  return apiFetch<UpdateAttendanceResponse>(
    `/api/university/items/${itemId}/attendance`,
    {
      method: 'PATCH',
      body: JSON.stringify({ attendanceStatus }),
    },
  );
}

export async function deleteUniversityItem(itemId: string): Promise<void> {
  return apiFetch<void>(`/api/university/items/${itemId}`, {
    method: 'DELETE',
  });
}

export async function generateUniversitySeries(
  activityId: string,
  input: GenerateSeriesInput,
): Promise<GenerateSeriesResponse> {
  return apiFetch<GenerateSeriesResponse>(
    `/api/university/activities/${activityId}/items/generate-series`,
    {
      method: 'POST',
      body: JSON.stringify(input),
    },
  );
}

export interface FlattenedUniversityEvent {
  item: UniversityActivityItem;
  activity: UniversityCourseActivity;
  course: UniversityCourse;
}

/** Collect all scheduled university items across courses, sorted by start time */
export function flattenScheduledUniversityItems(
  courses: UniversityCourse[],
): FlattenedUniversityEvent[] {
  const result: FlattenedUniversityEvent[] = [];
  for (const course of courses) {
    for (const activity of course.activities) {
      for (const item of activity.items) {
        if (item.startAt) {
          result.push({ item, activity, course });
        }
      }
    }
  }
  return result.sort((a, b) => {
    const ta = new Date(a.item.startAt ?? 0).getTime();
    const tb = new Date(b.item.startAt ?? 0).getTime();
    return ta - tb;
  });
}

/** Check if a scheduled university item falls on a local YYYY-MM-DD date key */
export function universityItemOccursOnDateKey(
  item: UniversityActivityItem,
  dateKey: string,
): boolean {
  if (!item.startAt) return false;
  const start = new Date(item.startAt);
  const end = new Date(item.endAt ?? item.startAt);
  if (item.isAllDay) {
    const startKey = item.startAt.slice(0, 10);
    const inclusiveEnd = new Date(
      Math.max(start.getTime(), end.getTime() - 86_400_000),
    );
    const ey = inclusiveEnd.getUTCFullYear();
    const em = String(inclusiveEnd.getUTCMonth() + 1).padStart(2, '0');
    const ed = String(inclusiveEnd.getUTCDate()).padStart(2, '0');
    const endKey = `${ey}-${em}-${ed}`;
    return dateKey >= startKey && dateKey <= endKey;
  }

  const [year, month, day] = dateKey.split('-').map(Number);
  const dayStartMs = new Date(year, month - 1, day, 0, 0, 0, 0).getTime();
  const dayEndMs = new Date(year, month - 1, day + 1, 0, 0, 0, 0).getTime();
  const sMs = start.getTime();
  const eMs = end.getTime();
  if (sMs === eMs) return sMs >= dayStartMs && sMs < dayEndMs;
  return sMs < dayEndMs && eMs > dayStartMs;
}

export function formatAttendanceCompact(summary: AttendanceSummary): string {
  if (!summary.supportsAttendance) {
    return `${summary.totalItemsCount} ${summary.totalItemsCount === 1 ? 'item' : 'items'}`;
  }
  if (!summary.hasRequirement) {
    if (summary.totalItemsCount === 0) return 'No requirement';
    return `${summary.attendedCount}/${summary.effectiveTotalCount} tracked`;
  }
  if (summary.status === 'no_data' || summary.requiredCount === null) {
    return `Min ${summary.minAttendancePercent}% · add items`;
  }
  if (summary.status === 'met') {
    return `${summary.attendedCount}/${summary.requiredCount} · Met`;
  }
  const rem = summary.remainingRequired ?? 0;
  return `${summary.attendedCount}/${summary.requiredCount} · ${rem} left`;
}

export function formatScheduledTimeLabel(
  startAt: string,
  endAt: string | null,
  isAllDay: boolean,
  timeFormat: '24h' | '12h' = '24h',
): string {
  if (isAllDay) return 'All day';
  const start = new Date(startAt);
  const fmt = new Intl.DateTimeFormat('en-US', {
    hour: '2-digit',
    minute: '2-digit',
    hour12: timeFormat === '12h',
  });
  if (!endAt) return fmt.format(start);
  const end = new Date(endAt);
  return `${fmt.format(start)}–${fmt.format(end)}`;
}
