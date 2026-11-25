export type ApiState = 'checking' | 'ok' | 'degraded' | 'offline';

export type ThemePreference = 'system' | 'light' | 'dark';

export type CaptureKind = 'task' | 'event' | 'note';

export type MobileTab = 'stream' | 'calendar' | 'vault' | 'agents';

export interface TaskItem {
  id: string;
  title: string;
  completed: boolean;
  category: string;
  dueTime?: string;
  source?: string;
}

export interface CalendarEvent {
  id: string;
  /** Local ISO date key: YYYY-MM-DD */
  dateKey: string;
  /** 24h time string: HH:MM */
  startTime: string;
  duration: string;
  title: string;
  location?: string;
  category: string;
  source?: string;
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
