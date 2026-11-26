export type ApiState = 'checking' | 'ok' | 'degraded' | 'offline';

export type ThemePreference = 'system' | 'light' | 'dark';

export type AppView = 'dashboard' | 'university' | 'account';

export type UserRole = 'Owner' | 'Member';

export type CaptureKind = 'task' | 'event' | 'note';

export type MobileTab = 'stream' | 'calendar' | 'university' | 'vault' | 'agents';

export type UniversityActivityType =
  | 'lecture'
  | 'practice'
  | 'laboratory'
  | 'homework_assignment'
  | 'semester_assignment'
  | 'midterm'
  | 'exam';

export type UniversityCourseStatus = 'active' | 'completed' | 'archived';

export type UniversityAttendanceStatus =
  | 'pending'
  | 'attended'
  | 'missed'
  | 'excused';

export type AttendanceSummaryStatus =
  | 'not_applicable'
  | 'not_configured'
  | 'no_data'
  | 'met'
  | 'on_track'
  | 'at_risk'
  | 'impossible';

export interface AttendanceSummary {
  supportsAttendance: boolean;
  hasRequirement: boolean;
  attendedCount: number;
  missedCount: number;
  excusedCount: number;
  pendingCount: number;
  totalItemsCount: number;
  effectiveTotalCount: number;
  minAttendanceCount: number | null;
  minAttendancePercent: number | null;
  plannedTotalCount: number | null;
  requiredCount: number | null;
  remainingRequired: number | null;
  remainingOpportunities: number | null;
  maxAllowedMissesLeft: number | null;
  currentRatePercent: number | null;
  recordedRatePercent: number | null;
  status: AttendanceSummaryStatus;
}

export interface UniversityActivityItem {
  id: string;
  activityId: string;
  courseId: string;
  calendarEventId: string | null;
  title: string;
  theme: string | null;
  notes: string | null;
  sequenceNumber: number | null;
  attendanceStatus: UniversityAttendanceStatus;
  startAt: string | null;
  endAt: string | null;
  isAllDay: boolean;
  location: string | null;
  metadataJson: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface UniversityCourseActivity {
  id: string;
  courseId: string;
  activityType: UniversityActivityType;
  title: string | null;
  description: string | null;
  defaultLocation: string | null;
  minAttendanceCount: number | null;
  minAttendancePercent: number | null;
  plannedTotalCount: number | null;
  sortOrder: number;
  metadataJson: string | null;
  attendance: AttendanceSummary;
  items: UniversityActivityItem[];
  createdAt: string;
  updatedAt: string;
}

export interface UniversityCourse {
  id: string;
  name: string;
  code: string | null;
  semester: string | null;
  description: string | null;
  instructor: string | null;
  credits: number | null;
  color: string;
  status: UniversityCourseStatus;
  metadataJson: string | null;
  activities: UniversityCourseActivity[];
  createdAt: string;
  updatedAt: string;
}

export interface UniversityWorkspace {
  calendar: UserCalendar;
  semesters: string[];
  courses: UniversityCourse[];
}

export interface UserProfile {
  id: string;
  displayName: string;
  username: string;
  email: string;
  bio: string;
  timezone: string;
  avatarDataUrl: string | null;
  role: UserRole;
  passwordUpdatedAt: string;
  createdAt: string;
}

export interface AccountPreferences {
  theme: ThemePreference;
  weekStartsOn: 'monday' | 'sunday';
  timeFormat: '24h' | '12h';
  requireAgentApproval: boolean;
  morningBrief: boolean;
  eventReminders: boolean;
}

export interface AgentToken {
  id: string;
  name: string;
  prefix: string;
  scopes: string[];
  createdAt: string;
  lastUsedAt: string | null;
}

export interface SessionItem {
  id: string;
  device: string;
  location: string;
  lastActiveAt: string;
  expiresAt: string;
  isCurrent: boolean;
}

export interface InviteItem {
  id: string;
  codePrefix: string;
  label: string | null;
  createdAt: string;
  expiresAt: string;
  usedAt: string | null;
  revokedAt: string | null;
  status: 'pending' | 'used' | 'revoked' | 'expired';
}

export interface HouseholdMember {
  id: string;
  username: string;
  displayName: string;
  email: string;
  role: UserRole;
  isActive: boolean;
  createdAt: string;
  lastActiveAt: string | null;
  activeSessionCount: number;
  activeTokenCount: number;
}

export interface TaskItem {
  id: string;
  title: string;
  completed: boolean;
  category: string;
  dueTime?: string;
  source?: string;
}

export type CalendarEventStatus = 'confirmed' | 'tentative' | 'cancelled';

export type RecurrenceScope = 'single' | 'series';

export interface UserCalendar {
  id: string;
  name: string;
  description: string;
  color: string;
  timezone: string;
  isDefault: boolean;
  isVisible: boolean;
  module?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CalendarEvent {
  id: string;
  calendarId: string;
  calendarName: string;
  calendarColor: string;
  title: string;
  description: string | null;
  location: string | null;
  /** UTC ISO-8601 timestamp */
  startAt: string;
  /** UTC ISO-8601 timestamp */
  endAt: string;
  isAllDay: boolean;
  timezone: string | null;
  status: CalendarEventStatus;
  externalUid: string | null;
  recurrenceRule: string | null;
  source: string | null;
  createdAt: string;
  updatedAt: string;
  seriesMasterId: string | null;
  occurrenceStartAt: string | null;
  isRecurring: boolean;
  isRecurrenceException: boolean;
}

export interface IcsImportSummary {
  calendar: UserCalendar;
  importedCount: number;
  updatedCount: number;
  skippedCount: number;
  warnings: string[];
  events: CalendarEvent[];
}

export interface AgentProposal {
  id: string;
  source: string;
  relativeTime: string;
  title: string;
  summary: string;
  details: string;
  kind: 'event' | 'task';
  payload:
    | {
        kind: 'event';
        dateKey: string;
        startTime: string;
        duration: string;
        title: string;
        location?: string;
        category: string;
      }
    | {
        kind: 'task';
        title: string;
        category: string;
        dueTime?: string;
      };
}

export interface NoteItem {
  id: string;
  title: string;
  excerpt: string;
  tag: string;
  updatedAt: string;
}

export interface VaultItem {
  id: string;
  title: string;
  format: string;
  updatedAt: string;
  summary: string;
  body: string[];
}
