import type {
  AccountPreferences,
  AgentProposal,
  AgentToken,
  CalendarEvent,
  NoteItem,
  PasskeyItem,
  SessionItem,
  TaskItem,
  UserProfile,
  VaultItem,
} from '../types/atrium';

export function getInitials(name: string, username: string): string {
  const source = name.trim() || username.replace(/^@/, '').trim() || 'AT';
  const parts = source.split(/\s+/).filter(Boolean);
  if (parts.length >= 2) {
    return `${parts[0][0]}${parts[1][0]}`.toUpperCase();
  }
  return source.slice(0, 2).toUpperCase();
}

export function detectTimezone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  } catch {
    return 'UTC';
  }
}

export function createDefaultProfile(displayName = ''): UserProfile {
  return {
    displayName,
    username: 'owner',
    email: 'owner@atrium.local',
    bio: 'Self-hosted Atrium workspace owner',
    timezone: detectTimezone(),
    avatarDataUrl: null,
    passwordUpdatedAt: '14 days ago',
  };
}

export function createDefaultPreferences(): AccountPreferences {
  return {
    weekStartsOn: 'monday',
    timeFormat: '24h',
    requireAgentApproval: true,
    morningBrief: true,
    eventReminders: true,
  };
}

export function createInitialPasskeys(): PasskeyItem[] {
  return [
    {
      id: 'pk-1',
      deviceName: 'Workstation · Platform Passkey',
      addedAt: 'Nov 12, 2026',
      lastUsed: 'Today',
    },
    {
      id: 'pk-2',
      deviceName: 'Mobile PWA · Face / Touch ID',
      addedAt: 'Nov 18, 2026',
      lastUsed: 'Yesterday',
    },
  ];
}

export function createInitialAgentTokens(): AgentToken[] {
  return [
    {
      id: 'tok-1',
      name: 'Calendar MCP',
      prefix: 'atr_cal_8f2a••••',
      scopes: ['calendar:read', 'calendar:propose'],
      lastUsed: '12m ago',
    },
    {
      id: 'tok-2',
      name: 'Home Assistant',
      prefix: 'atr_ha_3c91••••',
      scopes: ['tasks:propose'],
      lastUsed: '48m ago',
    },
    {
      id: 'tok-3',
      name: 'Local Vault CLI',
      prefix: 'atr_vlt_6d04••••',
      scopes: ['vault:read'],
      lastUsed: '2d ago',
    },
  ];
}

export function createInitialSessions(): SessionItem[] {
  return [
    {
      id: 'sess-1',
      device: 'Desktop Browser · Standalone PWA',
      location: 'Local network (127.0.0.1)',
      lastActive: 'Active now',
      isCurrent: true,
    },
    {
      id: 'sess-2',
      device: 'Mobile Safari · iOS PWA',
      location: 'Local network (Tailscale)',
      lastActive: '2h ago',
      isCurrent: false,
    },
  ];
}

export function toDateKey(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

export function parseDateKey(dateKey: string): Date {
  const [year, month, day] = dateKey.split('-').map(Number);
  return new Date(year, month - 1, day);
}

export function addDays(base: Date, offset: number): Date {
  const next = new Date(base.getFullYear(), base.getMonth(), base.getDate());
  next.setDate(next.getDate() + offset);
  return next;
}

export function formatLongDate(date: Date): string {
  return new Intl.DateTimeFormat('en-US', {
    weekday: 'long',
    month: 'short',
    day: 'numeric',
  }).format(date);
}

export function formatShortDate(date: Date): string {
  return new Intl.DateTimeFormat('en-US', {
    weekday: 'short',
    month: 'short',
    day: 'numeric',
  }).format(date);
}

export function formatMonthYear(date: Date): string {
  return new Intl.DateTimeFormat('en-US', {
    month: 'long',
    year: 'numeric',
  }).format(date);
}

export function getGreeting(date: Date = new Date()): string {
  const hour = date.getHours();
  if (hour < 12) return 'Good morning';
  if (hour < 18) return 'Good afternoon';
  return 'Good evening';
}

export function createInitialTasks(): TaskItem[] {
  return [
    {
      id: 'task-1',
      title: 'Review monthly household budget & recurring bills',
      completed: false,
      category: 'Personal',
      dueTime: '11:00',
    },
    {
      id: 'task-2',
      title: 'Verify nightly Postgres snapshot retention on home server',
      completed: true,
      category: 'Self-Host',
      dueTime: '08:30',
    },
    {
      id: 'task-3',
      title: 'Confirm dental check-up slot for Friday morning',
      completed: false,
      category: 'Health',
      dueTime: '15:00',
    },
    {
      id: 'task-4',
      title: 'Draft MCP permission boundaries for Calendar & Vault tools',
      completed: false,
      category: 'Atrium',
      dueTime: '17:00',
    },
  ];
}

export function createInitialEvents(today: Date): CalendarEvent[] {
  const todayKey = toDateKey(today);
  const tomorrowKey = toDateKey(addDays(today, 1));
  const plusTwoKey = toDateKey(addDays(today, 2));
  const plusFourKey = toDateKey(addDays(today, 4));
  const minusOneKey = toDateKey(addDays(today, -1));

  return [
    {
      id: 'evt-1',
      dateKey: todayKey,
      startTime: '09:30',
      duration: '45m',
      title: 'Deep work: Atrium schema & auth notes',
      location: 'Desk',
      category: 'Focus',
    },
    {
      id: 'evt-2',
      dateKey: todayKey,
      startTime: '14:00',
      duration: '30m',
      title: 'Architecture catch-up with Sam',
      location: 'Video call',
      category: 'Meeting',
    },
    {
      id: 'evt-3',
      dateKey: todayKey,
      startTime: '18:15',
      duration: '60m',
      title: 'Strength session & stretch',
      location: 'Neighborhood gym',
      category: 'Health',
    },
    {
      id: 'evt-4',
      dateKey: tomorrowKey,
      startTime: '10:00',
      duration: '90m',
      title: 'Quarterly reading synthesis',
      location: 'Library',
      category: 'Focus',
    },
    {
      id: 'evt-5',
      dateKey: tomorrowKey,
      startTime: '19:00',
      duration: '90m',
      title: 'Dinner with Maya & Leo',
      location: 'Osteria Verde',
      category: 'Personal',
    },
    {
      id: 'evt-6',
      dateKey: plusTwoKey,
      startTime: '08:45',
      duration: '45m',
      title: 'Dental check-up',
      location: 'Midtown Clinic',
      category: 'Health',
      source: 'Calendar MCP',
    },
    {
      id: 'evt-7',
      dateKey: plusFourKey,
      startTime: '11:00',
      duration: '60m',
      title: 'Weekly review & calendar reset',
      location: 'Atrium',
      category: 'Routine',
    },
    {
      id: 'evt-8',
      dateKey: minusOneKey,
      startTime: '16:00',
      duration: '30m',
      title: 'Home NAS firmware update',
      location: 'Homelab',
      category: 'Self-Host',
    },
  ];
}

export function createInitialProposals(today: Date): AgentProposal[] {
  const todayKey = toDateKey(today);

  return [
    {
      id: 'prop-1',
      source: 'Calendar MCP',
      relativeTime: '12m ago',
      title: 'Hold 30m prep block before Architecture catch-up',
      summary: 'Adds a quiet buffer from 13:15 to 13:45 today with notes linked from Vault.',
      details:
        'Tool: calendar.create_event · Target date: Today at 13:15 (30m) · Location: Desk · Conflict check: Clear.',
      kind: 'event',
      payload: {
        kind: 'event',
        dateKey: todayKey,
        startTime: '13:15',
        duration: '30m',
        title: 'Prep: Architecture catch-up notes',
        location: 'Desk',
        category: 'Focus',
      },
    },
    {
      id: 'prop-2',
      source: 'Home Assistant',
      relativeTime: '48m ago',
      title: 'Schedule air purifier HEPA filter replacement',
      summary: 'Filter runtime crossed 92% threshold in the study. Proposes a checklist task.',
      details:
        'Tool: tasks.propose_item · Sensor: study_air_purifier_filter (92%) · Suggested category: Home.',
      kind: 'task',
      payload: {
        kind: 'task',
        title: 'Replace study air purifier HEPA filter',
        category: 'Home',
        dueTime: '18:00',
      },
    },
  ];
}

export function createInitialNotes(): NoteItem[] {
  return [
    {
      id: 'note-1',
      title: 'Passkey & local session notes',
      excerpt:
        'Keep WebAuthn resident keys scoped to the local Atrium origin with short-lived refresh tokens and revocable per-agent MCP tokens.',
      tag: 'Security',
      updatedAt: '08:15',
    },
    {
      id: 'note-2',
      title: 'Autumn kitchen pantry list',
      excerpt:
        'Whole bean Ethiopian roast, flaky sea salt, Arborio rice, and fresh rosemary for Sunday risotto.',
      tag: 'Personal',
      updatedAt: 'Yesterday',
    },
  ];
}

export function createInitialVault(): VaultItem[] {
  return [
    {
      id: 'vault-1',
      title: 'Home Server & Postgres Runbook',
      format: 'Markdown',
      updatedAt: '2d ago',
      summary: 'Local Docker Compose ports, pg_dump schedule, and recovery commands.',
      body: [
        'PostgreSQL 17 binds strictly to 127.0.0.1:5433 on the host machine with persistent volume atrium_pgdata.',
        'Nightly logical dumps are written to encrypted local storage and verified every Sunday at 04:00.',
        'Health check endpoint: GET /api/health returns 200 OK when the database pool accepts connections.',
      ],
    },
    {
      id: 'vault-2',
      title: 'MCP Agent Permission Matrix',
      format: 'Policy',
      updatedAt: '4d ago',
      summary: 'Read/propose/write scopes for Calendar, Vault, and Home Assistant integrations.',
      body: [
        'Default stance: agents may read explicitly shared scopes and submit proposals into the Focus Stream.',
        'Mutating calendar events or deleting vault entries always requires explicit human approval in Atrium.',
        'All agent-authored items carry source attribution and timestamp metadata.',
      ],
    },
    {
      id: 'vault-3',
      title: 'Weekly Review Ritual',
      format: 'Template',
      updatedAt: '1w ago',
      summary: 'Ten-minute Sunday reset across calendar horizons, open tasks, and inbox notes.',
      body: [
        '1. Clear quick-capture items into tasks, scheduled blocks, or archived vault notes.',
        '2. Scan the next 14 days in the calendar for travel buffers and focus blocks.',
        '3. Resolve any lingering agent proposals so Monday starts with a calm stream.',
      ],
    },
  ];
}
