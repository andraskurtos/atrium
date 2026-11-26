import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { AccountPage } from './components/AccountPage';
import { AgentProposalsCard } from './components/AgentProposalsCard';
import { AuthScreen } from './components/AuthScreen';
import { CalendarCard } from './components/CalendarCard';
import { ChecklistCard } from './components/ChecklistCard';
import { CommandPalette, type CommandItem } from './components/CommandPalette';
import { LocalStatusCard } from './components/LocalStatusCard';
import { MobileTabBar } from './components/MobileTabBar';
import { NotesCard } from './components/NotesCard';
import { QuickCapture } from './components/QuickCapture';
import { TopBar } from './components/TopBar';
import { VaultCard } from './components/VaultCard';
import {
  createInitialNotes,
  createInitialProposals,
  createInitialTasks,
  createInitialVault,
  formatLongDate,
  formatShortDate,
  getGreeting,
  parseDateKey,
  toDateKey,
} from './data/mockData';
import { useApiHealth } from './hooks/useApiHealth';
import { useTheme } from './hooks/useTheme';
import {
  changePassword,
  clearAccessToken,
  createInvite,
  createToken,
  deleteMember,
  getAccountMe,
  getAuthStatus,
  listInvites,
  listMembers,
  listSessions,
  listTokens,
  logout,
  refreshAccessToken,
  revokeInvite,
  revokeOtherSessions,
  revokeSession,
  revokeToken,
  setMemberActive,
  setUnauthorizedHandler,
  updatePreferences,
  updateProfile,
  type AuthSessionPayload,
  type CreateInvitePayload,
} from './lib/authClient';
import {
  createCalendar,
  createEvent,
  deleteCalendar,
  deleteEvent,
  downloadCalendarIcs,
  eventOccursOnDateKey,
  importIcsAsNewCalendar,
  importIcsToCalendar,
  listCalendars,
  listEvents,
  localDateTimeToIso,
  parseDurationMinutes,
  updateCalendar,
  updateEvent,
  type CreateCalendarInput,
  type CreateEventInput,
  type UpdateEventInput,
} from './lib/calendarClient';
import type {
  AccountPreferences,
  AgentProposal,
  AgentToken,
  AppView,
  CalendarEvent,
  CaptureKind,
  HouseholdMember,
  InviteItem,
  MobileTab,
  NoteItem,
  RecurrenceScope,
  SessionItem,
  TaskItem,
  UserCalendar,
  UserProfile,
} from './types/atrium';

function readInviteCodeFromHash(): string {
  if (typeof window === 'undefined') return '';
  const match = window.location.hash.match(/^#invite=([^&]+)/);
  return match ? decodeURIComponent(match[1]) : '';
}

function readInitialView(): AppView {
  if (typeof window === 'undefined') return 'dashboard';
  return window.location.hash === '#account' ? 'account' : 'dashboard';
}

function buildCalendarFetchRange(todayYear: number, viewYear: number): {
  from: string;
  to: string;
} {
  const minYear = Math.min(todayYear, viewYear);
  const maxYear = Math.max(todayYear, viewYear);
  return {
    from: new Date(Date.UTC(minYear - 1, 11, 1, 0, 0, 0)).toISOString(),
    to: new Date(Date.UTC(maxYear + 1, 1, 1, 0, 0, 0)).toISOString(),
  };
}

export function App() {
  const today = useMemo(() => new Date(), []);
  const todayKey = useMemo(() => toDateKey(today), [today]);
  const todayYear = today.getFullYear();

  const {
    apiState,
    label: apiLabel,
    lastChecked,
    refresh: refreshHealth,
  } = useApiHealth();
  const { theme, setTheme, cycleTheme } = useTheme();

  const [authBooting, setAuthBooting] = useState(true);
  const [initialized, setInitialized] = useState(true);
  const [initialInviteCode] = useState(readInviteCodeFromHash);
  const [profile, setProfile] = useState<UserProfile | null>(null);
  const [preferences, setPreferences] = useState<AccountPreferences>({
    theme: 'system',
    weekStartsOn: 'monday',
    timeFormat: '24h',
    requireAgentApproval: true,
    morningBrief: true,
    eventReminders: true,
  });
  const [agentTokens, setAgentTokens] = useState<AgentToken[]>([]);
  const [sessions, setSessions] = useState<SessionItem[]>([]);
  const [invites, setInvites] = useState<InviteItem[]>([]);
  const [members, setMembers] = useState<HouseholdMember[]>([]);

  const [activeView, setActiveView] = useState<AppView>(readInitialView);
  const [selectedDateKey, setSelectedDateKey] = useState<string>(todayKey);
  const [tasks, setTasks] = useState<TaskItem[]>(createInitialTasks);
  const [calendars, setCalendars] = useState<UserCalendar[]>([]);
  const [events, setEvents] = useState<CalendarEvent[]>([]);
  const [calendarLoading, setCalendarLoading] = useState(false);
  const [proposals, setProposals] = useState<AgentProposal[]>(() =>
    createInitialProposals(today),
  );
  const [notes, setNotes] = useState<NoteItem[]>(createInitialNotes);
  const vaultItems = useMemo(() => createInitialVault(), []);

  const [mobileTab, setMobileTab] = useState<MobileTab>('stream');
  const [isCommandOpen, setIsCommandOpen] = useState(false);
  const [isCalendarExpanded, setIsCalendarExpanded] = useState(false);
  const [announcement, setAnnouncement] = useState<string>('');

  const quickCaptureRef = useRef<HTMLInputElement | null>(null);
  const calendarViewYearRef = useRef<number>(todayYear);

  const announce = useCallback((message: string) => {
    setAnnouncement(message);
  }, []);

  const clearAuthState = useCallback(() => {
    clearAccessToken();
    setProfile(null);
    setAgentTokens([]);
    setSessions([]);
    setInvites([]);
    setMembers([]);
    setCalendars([]);
    setEvents([]);
    setIsCommandOpen(false);
  }, []);

  const reloadEvents = useCallback(
    async (targetYear = calendarViewYearRef.current) => {
      const range = buildCalendarFetchRange(todayYear, targetYear);
      const nextEvents = await listEvents(range);
      setEvents(nextEvents);
    },
    [todayYear],
  );

  const loadCalendarData = useCallback(
    async (targetYear = calendarViewYearRef.current) => {
      setCalendarLoading(true);
      try {
        const range = buildCalendarFetchRange(todayYear, targetYear);
        const [nextCalendars, nextEvents] = await Promise.all([
          listCalendars(),
          listEvents(range),
        ]);
        setCalendars(nextCalendars);
        setEvents(nextEvents);
      } finally {
        setCalendarLoading(false);
      }
    },
    [todayYear],
  );

  const loadAccountSecurityData = useCallback(async (role: UserProfile['role']) => {
    const [nextSessions, nextTokens] = await Promise.all([
      listSessions(),
      listTokens(),
    ]);
    setSessions(nextSessions);
    setAgentTokens(nextTokens);

    if (role === 'Owner') {
      const [nextInvites, nextMembers] = await Promise.all([
        listInvites(),
        listMembers(),
      ]);
      setInvites(nextInvites);
      setMembers(nextMembers);
    } else {
      setInvites([]);
      setMembers([]);
    }
  }, []);

  useEffect(() => {
    setUnauthorizedHandler(clearAuthState);
    return () => setUnauthorizedHandler(null);
  }, [clearAuthState]);

  useEffect(() => {
    let active = true;
    (async () => {
      try {
        const status = await getAuthStatus();
        if (!active) return;
        setInitialized(status.initialized);

        if (status.initialized) {
          const refreshed = await refreshAccessToken();
          if (refreshed && active) {
            const me = await getAccountMe();
            if (!active) return;
            setProfile(me.user);
            setPreferences(me.preferences);
            setTheme(me.preferences.theme);
            await Promise.all([
              loadAccountSecurityData(me.user.role),
              loadCalendarData(),
            ]);
          }
        }
      } catch {
        // API offline or unauthenticated; AuthScreen handles retry
      } finally {
        if (active) setAuthBooting(false);
      }
    })();

    return () => {
      active = false;
    };
  }, [loadAccountSecurityData, loadCalendarData, setTheme]);

  useEffect(() => {
    if (!announcement) return;
    const timer = window.setTimeout(() => setAnnouncement(''), 3600);
    return () => window.clearTimeout(timer);
  }, [announcement]);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        if (profile) {
          setIsCommandOpen((prev) => !prev);
        }
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [profile]);

  const handleAuthenticated = async (session: AuthSessionPayload) => {
    setInitialized(true);
    setProfile(session.user);
    setPreferences(session.preferences);
    setTheme(session.preferences.theme);
    setActiveView('dashboard');
    if (window.location.hash.startsWith('#invite=')) {
      window.history.replaceState(null, '', window.location.pathname);
    }
    try {
      await Promise.all([
        loadAccountSecurityData(session.user.role),
        loadCalendarData(),
      ]);
    } catch {
      // Non-fatal; individual views can refresh
    }
    announce(`Signed in as ${session.user.displayName || session.user.username}`);
  };

  const handleSignOut = async () => {
    await logout();
    clearAuthState();
    announce('Signed out');
  };

  const navigateTo = (view: AppView) => {
    setActiveView(view);
    if (view === 'account' && profile) {
      void loadAccountSecurityData(profile.role);
    }
    window.scrollTo({ top: 0, behavior: 'auto' });
  };

  const handleUpdateProfile = async (next: {
    displayName: string;
    username: string;
    email: string;
    bio: string;
    timezone: string;
    avatarDataUrl: string | null;
  }) => {
    const updated = await updateProfile(next);
    setProfile(updated);
  };

  const handleUpdatePreferences = async (next: AccountPreferences) => {
    setPreferences(next);
    try {
      const saved = await updatePreferences(next);
      setPreferences(saved);
    } catch {
      announce('Could not save preferences');
    }
  };

  const handleChangePassword = async (
    currentPassword: string,
    newPassword: string,
  ) => {
    const res = await changePassword({ currentPassword, newPassword });
    setProfile((prev) =>
      prev ? { ...prev, passwordUpdatedAt: res.passwordUpdatedAt } : prev,
    );
    const refreshedSessions = await listSessions();
    setSessions(refreshedSessions);
  };

  const handleCreateToken = async (
    name: string,
    scopes: string[],
  ): Promise<string> => {
    const created = await createToken(name, scopes);
    setAgentTokens((prev) => [created.token, ...prev]);
    announce(`Created scoped token for ${name}`);
    return created.secret;
  };

  const handleRevokeToken = async (id: string) => {
    await revokeToken(id);
    setAgentTokens((prev) => prev.filter((item) => item.id !== id));
    announce('MCP agent token revoked');
  };

  const handleRevokeSession = async (id: string) => {
    await revokeSession(id);
    setSessions((prev) => prev.filter((item) => item.id !== id));
    announce('Session signed out');
  };

  const handleSignOutOtherSessions = async () => {
    await revokeOtherSessions();
    setSessions((prev) => prev.filter((item) => item.isCurrent));
    announce('Signed out all other sessions');
  };

  const handleCreateInvite = async (
    label: string | null,
    expiresInDays: number,
  ): Promise<CreateInvitePayload> => {
    const created = await createInvite(label, expiresInDays);
    setInvites((prev) => [created.invite, ...prev]);
    announce('Created household invite');
    return created;
  };

  const handleRevokeInvite = async (id: string) => {
    await revokeInvite(id);
    setInvites((prev) =>
      prev.map((item) => (item.id === id ? { ...item, status: 'revoked' } : item)),
    );
    announce('Invite revoked');
  };

  const handleToggleMemberActive = async (id: string, isActive: boolean) => {
    const updated = await setMemberActive(id, isActive);
    setMembers((prev) => prev.map((m) => (m.id === id ? updated : m)));
    announce(isActive ? 'Member reactivated' : 'Member deactivated and sessions revoked');
  };

  const handleDeleteMember = async (id: string) => {
    await deleteMember(id);
    setMembers((prev) => prev.filter((m) => m.id !== id));
    announce('Household member removed');
  };

  const handleCreateCalendar = async (input: CreateCalendarInput) => {
    const created = await createCalendar(input);
    setCalendars((prev) =>
      created.isDefault
        ? [created, ...prev.map((c) => ({ ...c, isDefault: false }))]
        : [...prev, created],
    );
    announce(`Created calendar "${created.name}"`);
  };

  const handleToggleCalendarVisibility = async (calendar: UserCalendar) => {
    const nextVisible = !calendar.isVisible;
    setCalendars((prev) =>
      prev.map((c) => (c.id === calendar.id ? { ...c, isVisible: nextVisible } : c)),
    );
    try {
      const updated = await updateCalendar(calendar.id, {
        name: calendar.name,
        description: calendar.description,
        color: calendar.color,
        timezone: calendar.timezone,
        isVisible: nextVisible,
      });
      setCalendars((prev) => prev.map((c) => (c.id === updated.id ? updated : c)));
    } catch {
      setCalendars((prev) => prev.map((c) => (c.id === calendar.id ? calendar : c)));
      announce('Could not update calendar visibility');
    }
  };

  const handleDeleteCalendar = async (id: string) => {
    try {
      await deleteCalendar(id);
      await loadCalendarData();
      announce('Deleted calendar');
    } catch (err) {
      announce(err instanceof Error ? err.message : 'Could not delete calendar');
    }
  };

  const handleViewYearChange = useCallback(
    (nextYear: number) => {
      if (nextYear === calendarViewYearRef.current) return;
      calendarViewYearRef.current = nextYear;
      if (profile) {
        void reloadEvents(nextYear);
      }
    },
    [profile, reloadEvents],
  );

  const handleCreateEvent = async (input: CreateEventInput) => {
    const created = await createEvent(input);
    await reloadEvents();
    announce(
      created.isRecurring
        ? `Added repeating series: ${created.title}`
        : `Added event: ${created.title}`,
    );
  };

  const handleUpdateEvent = async (id: string, input: UpdateEventInput) => {
    const updated = await updateEvent(id, input);
    await reloadEvents();
    if (input.scope === 'single') {
      announce(`Updated single occurrence: ${updated.title}`);
    } else if (updated.isRecurring) {
      announce(`Updated repeating series: ${updated.title}`);
    } else {
      announce(`Updated event: ${updated.title}`);
    }
  };

  const handleDeleteEvent = async (
    evt: CalendarEvent,
    scope: RecurrenceScope,
  ) => {
    await deleteEvent(evt.id, {
      scope: evt.isRecurring ? scope : 'series',
      occurrenceStartAt:
        evt.isRecurring && scope === 'single'
          ? (evt.occurrenceStartAt ?? evt.startAt)
          : null,
    });
    await reloadEvents();
    if (evt.isRecurring) {
      announce(
        scope === 'single'
          ? `Removed single occurrence of "${evt.title}"`
          : `Removed entire series "${evt.title}"`,
      );
    } else {
      announce('Removed event');
    }
  };

  const handleImportIcs = async (file: File, targetCalendarId: string | 'new') => {
    const summary =
      targetCalendarId === 'new'
        ? await importIcsAsNewCalendar(file)
        : await importIcsToCalendar(targetCalendarId, file);
    await loadCalendarData();
    const parts = [`${summary.importedCount} imported`];
    if (summary.updatedCount > 0) parts.push(`${summary.updatedCount} updated`);
    if (summary.skippedCount > 0) parts.push(`${summary.skippedCount} skipped`);
    announce(`Imported into "${summary.calendar.name}" (${parts.join(', ')})`);
  };

  const handleExportIcs = async (calendar: UserCalendar) => {
    try {
      await downloadCalendarIcs(calendar.id, calendar.name);
      announce(`Exported "${calendar.name}.ics"`);
    } catch {
      announce('Could not export calendar');
    }
  };

  const handleCapture = ({
    kind,
    text,
    time,
  }: {
    kind: CaptureKind;
    text: string;
    time?: string;
  }) => {
    const id = `${kind}-${Date.now()}`;
    if (kind === 'task') {
      setTasks((prev) => [
        { id, title: text, completed: false, category: 'Inbox' },
        ...prev,
      ]);
      announce(`Added task: ${text}`);
    } else if (kind === 'event') {
      const startIso = localDateTimeToIso(selectedDateKey, time || '15:30');
      const endIso = new Date(new Date(startIso).getTime() + 30 * 60_000).toISOString();
      void createEvent({
        title: text,
        startAt: startIso,
        endAt: endIso,
        isAllDay: false,
        source: 'Quick Capture',
      })
        .then((created) => {
          setEvents((prev) => [...prev, created]);
          announce(
            `Scheduled "${text}" for ${formatShortDate(parseDateKey(selectedDateKey))}`,
          );
        })
        .catch(() => {
          announce('Could not schedule event');
        });
    } else {
      const nowLabel = new Intl.DateTimeFormat('en-US', {
        hour: '2-digit',
        minute: '2-digit',
        hour12: false,
      }).format(new Date());
      setNotes((prev) => [
        {
          id,
          title: text.length > 48 ? `${text.slice(0, 48)}…` : text,
          excerpt: text,
          tag: 'Quick Note',
          updatedAt: nowLabel,
        },
        ...prev,
      ]);
      announce(`Saved note: ${text}`);
    }
  };

  const handleToggleTask = (id: string) => {
    setTasks((prev) =>
      prev.map((task) =>
        task.id === id ? { ...task, completed: !task.completed } : task,
      ),
    );
  };

  const handleDeleteTask = (id: string) => {
    setTasks((prev) => prev.filter((task) => task.id !== id));
    announce('Removed task');
  };

  const handleApproveProposal = (proposal: AgentProposal) => {
    if (proposal.payload.kind === 'event') {
      const { dateKey, startTime, duration, title, location } = proposal.payload;
      const startIso = localDateTimeToIso(dateKey, startTime);
      const durationMinutes = parseDurationMinutes(duration);
      const endIso = new Date(
        new Date(startIso).getTime() + durationMinutes * 60_000,
      ).toISOString();

      void createEvent({
        title,
        startAt: startIso,
        endAt: endIso,
        isAllDay: false,
        location: location ?? null,
        source: proposal.source,
      })
        .then((created) => {
          setEvents((prev) => [...prev, created]);
          setSelectedDateKey(dateKey);
          setProposals((prev) => prev.filter((item) => item.id !== proposal.id));
          announce(`Approved & scheduled: ${title}`);
        })
        .catch(() => {
          announce('Could not schedule approved proposal');
        });
      return;
    }

    const { title, category, dueTime } = proposal.payload;
    setTasks((prev) => [
      {
        id: `task-${Date.now()}`,
        title,
        completed: false,
        category,
        dueTime,
        source: proposal.source,
      },
      ...prev,
    ]);
    announce(`Approved & added task: ${title}`);
    setProposals((prev) => prev.filter((item) => item.id !== proposal.id));
  };

  const handleDismissProposal = (id: string) => {
    setProposals((prev) => prev.filter((item) => item.id !== id));
    announce('Dismissed agent proposal');
  };

  const handleExportData = () => {
    if (!profile) return;
    const archive = {
      exportedAt: new Date().toISOString(),
      profile: {
        ...profile,
        avatarDataUrl: profile.avatarDataUrl ? '[embedded image]' : null,
      },
      preferences,
      calendars,
      tasks,
      events,
      notes,
    };
    const blob = new Blob([JSON.stringify(archive, null, 2)], {
      type: 'application/json',
    });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `atrium-export-${todayKey}.json`;
    link.click();
    URL.revokeObjectURL(url);
    announce('Exported Atrium JSON archive');
  };

  if (authBooting) {
    return (
      <main className="auth-loading-shell" role="status" aria-live="polite">
        <div className="card auth-loading-card">
          <img src="/icon.svg" alt="" width={40} height={40} className="welcome-mark" />
          <p className="meta-text">Connecting to your Atrium instance…</p>
        </div>
      </main>
    );
  }

  if (!profile) {
    return (
      <AuthScreen
        initialized={initialized}
        initialInviteCode={initialInviteCode}
        apiState={apiState}
        apiLabel={apiLabel}
        theme={theme}
        onCycleTheme={cycleTheme}
        onAuthenticated={handleAuthenticated}
      />
    );
  }

  const visibleCalIds = new Set(calendars.filter((c) => c.isVisible).map((c) => c.id));
  const openTaskCount = tasks.filter((t) => !t.completed).length;
  const todayEventCount = events.filter(
    (e) => visibleCalIds.has(e.calendarId) && eventOccursOnDateKey(e, todayKey),
  ).length;
  const pendingProposalCount = proposals.length;

  const commands: CommandItem[] = [
    {
      id: 'cmd-dashboard',
      label: "Go to Today's Dashboard",
      hint: 'Stream',
      group: 'Navigation',
      onSelect: () => navigateTo('dashboard'),
    },
    {
      id: 'cmd-account',
      label: 'Open Account & Settings',
      hint: 'Profile & Security',
      group: 'Navigation',
      onSelect: () => navigateTo('account'),
    },
    {
      id: 'cmd-capture',
      label: 'Focus Quick Capture',
      hint: 'Stream',
      group: 'Navigation',
      onSelect: () => {
        navigateTo('dashboard');
        setMobileTab('stream');
        requestAnimationFrame(() => quickCaptureRef.current?.focus());
      },
    },
    {
      id: 'cmd-calendar',
      label: 'Open Expanded Calendar',
      hint: 'Month View',
      group: 'Calendar',
      onSelect: () => {
        navigateTo('dashboard');
        setIsCalendarExpanded(true);
      },
    },
    {
      id: 'cmd-today',
      label: 'Jump Calendar to Today',
      hint: formatShortDate(today),
      group: 'Calendar',
      onSelect: () => {
        navigateTo('dashboard');
        setSelectedDateKey(todayKey);
        announce('Calendar set to today');
      },
    },
    {
      id: 'cmd-theme',
      label: `Switch Color Theme (currently ${theme})`,
      hint: 'Appearance',
      group: 'Preferences',
      onSelect: cycleTheme,
    },
    {
      id: 'cmd-health',
      label: 'Refresh Local API Health',
      hint: '/api/health',
      group: 'System',
      onSelect: () => {
        refreshHealth();
        announce('Refreshing local API status');
      },
    },
    {
      id: 'cmd-signout',
      label: 'Sign out of Atrium',
      hint: 'Session',
      group: 'Account',
      onSelect: () => void handleSignOut(),
    },
  ];

  const todayFormatted = formatLongDate(today);
  const selectedDateLabel =
    selectedDateKey === todayKey
      ? 'Today'
      : formatShortDate(parseDateKey(selectedDateKey));

  const greetingText = `${getGreeting(today)}, ${profile.displayName || profile.username}`;

  return (
    <div className="app-shell">
      <TopBar
        activeView={activeView}
        profile={profile}
        theme={theme}
        onCycleTheme={cycleTheme}
        onOpenCommandPalette={() => setIsCommandOpen(true)}
        onNavigate={navigateTo}
        onSignOut={() => void handleSignOut()}
      />

      {activeView === 'account' ? (
        <AccountPage
          profile={profile}
          preferences={preferences}
          theme={theme}
          agentTokens={agentTokens}
          sessions={sessions}
          invites={invites}
          members={members}
          apiState={apiState}
          apiLabel={apiLabel}
          lastChecked={lastChecked}
          onBackToDashboard={() => navigateTo('dashboard')}
          onUpdateProfile={handleUpdateProfile}
          onUpdatePreferences={handleUpdatePreferences}
          onSetTheme={setTheme}
          onChangePassword={handleChangePassword}
          onCreateToken={handleCreateToken}
          onRevokeToken={handleRevokeToken}
          onRevokeSession={handleRevokeSession}
          onSignOutOtherSessions={handleSignOutOtherSessions}
          onCreateInvite={handleCreateInvite}
          onRevokeInvite={handleRevokeInvite}
          onToggleMemberActive={handleToggleMemberActive}
          onDeleteMember={handleDeleteMember}
          onExportData={handleExportData}
          onSignOut={() => void handleSignOut()}
          onRefreshHealth={refreshHealth}
          onAnnounce={announce}
        />
      ) : (
        <>
          <div className="dashboard-container" data-mobile-tab={mobileTab}>
            <main className="focus-stream">
              <header className="stream-header" data-mobile-section="stream">
                <div className="welcome-badge-row">
                  <span className="pill-accent welcome-badge">
                    Welcome back · {greetingText}
                  </span>
                </div>
                <h1 className="page-title">{todayFormatted}</h1>
                {preferences.morningBrief && (
                  <p className="stream-summary">
                    <span className="mono-num">{openTaskCount}</span>{' '}
                    {openTaskCount === 1 ? 'task' : 'tasks'} open ·{' '}
                    <span className="mono-num">{todayEventCount}</span>{' '}
                    {todayEventCount === 1 ? 'event' : 'events'} today ·{' '}
                    <span className="mono-num">{pendingProposalCount}</span> agent{' '}
                    {pendingProposalCount === 1 ? 'proposal' : 'proposals'}
                  </p>
                )}
              </header>

              <div data-mobile-section="stream">
                <QuickCapture
                  inputRef={quickCaptureRef}
                  selectedDateLabel={selectedDateLabel}
                  onCapture={handleCapture}
                />
              </div>

              <div data-mobile-section="stream">
                <ChecklistCard
                  tasks={tasks}
                  onToggleTask={handleToggleTask}
                  onDeleteTask={handleDeleteTask}
                />
              </div>

              <div data-mobile-section="agents">
                <AgentProposalsCard
                  proposals={proposals}
                  onApprove={handleApproveProposal}
                  onDismiss={handleDismissProposal}
                />
              </div>

              <div data-mobile-section="stream">
                <NotesCard notes={notes} />
              </div>
            </main>

            <aside className="context-rail" aria-label="Context utility rail">
              <div className="rail-group" data-mobile-section="calendar">
                <CalendarCard
                  todayKey={todayKey}
                  selectedDateKey={selectedDateKey}
                  calendars={calendars}
                  events={events}
                  loading={calendarLoading}
                  isExpandedOpen={isCalendarExpanded}
                  weekStartsOn={preferences.weekStartsOn}
                  timeFormat={preferences.timeFormat}
                  onSelectDate={setSelectedDateKey}
                  onViewYearChange={handleViewYearChange}
                  onCreateCalendar={handleCreateCalendar}
                  onToggleCalendarVisibility={handleToggleCalendarVisibility}
                  onDeleteCalendar={handleDeleteCalendar}
                  onCreateEvent={handleCreateEvent}
                  onUpdateEvent={handleUpdateEvent}
                  onDeleteEvent={handleDeleteEvent}
                  onImportIcs={handleImportIcs}
                  onExportIcs={handleExportIcs}
                  onToggleExpanded={setIsCalendarExpanded}
                />
              </div>

              <div className="rail-group" data-mobile-section="vault">
                <VaultCard items={vaultItems} />
                <LocalStatusCard
                  apiState={apiState}
                  label={apiLabel}
                  lastChecked={lastChecked}
                  onRefresh={refreshHealth}
                />
              </div>
            </aside>
          </div>

          <MobileTabBar
            activeTab={mobileTab}
            pendingProposalCount={pendingProposalCount}
            onSelectTab={setMobileTab}
          />
        </>
      )}

      <CommandPalette
        isOpen={isCommandOpen}
        theme={theme}
        commands={commands}
        onClose={() => setIsCommandOpen(false)}
      />

      {announcement && (
        <div className="toast-status" role="status" aria-live="polite">
          {announcement}
        </div>
      )}
    </div>
  );
}
