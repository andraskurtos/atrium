import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { AccountPage } from './components/AccountPage';
import { AgentProposalsCard } from './components/AgentProposalsCard';
import { AuthScreen } from './components/AuthScreen';
import { CalendarMockupCard } from './components/CalendarMockupCard';
import { ChecklistCard } from './components/ChecklistCard';
import { CommandPalette, type CommandItem } from './components/CommandPalette';
import { LocalStatusCard } from './components/LocalStatusCard';
import { MobileTabBar } from './components/MobileTabBar';
import { NotesCard } from './components/NotesCard';
import { QuickCapture } from './components/QuickCapture';
import { TopBar } from './components/TopBar';
import { VaultCard } from './components/VaultCard';
import {
  createInitialEvents,
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
  SessionItem,
  TaskItem,
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

export function App() {
  const today = useMemo(() => new Date(), []);
  const todayKey = useMemo(() => toDateKey(today), [today]);

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
  const [events, setEvents] = useState<CalendarEvent[]>(() =>
    createInitialEvents(today),
  );
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
    setIsCommandOpen(false);
  }, []);

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
            await loadAccountSecurityData(me.user.role);
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
  }, [loadAccountSecurityData, setTheme]);

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
      await loadAccountSecurityData(session.user.role);
    } catch {
      // Non-fatal; AccountPage refreshes when opened
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
      setEvents((prev) => [
        ...prev,
        {
          id,
          dateKey: selectedDateKey,
          startTime: time || '15:30',
          duration: '30m',
          title: text,
          category: 'Personal',
        },
      ]);
      announce(
        `Scheduled "${text}" for ${formatShortDate(parseDateKey(selectedDateKey))}`,
      );
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

  const handleAddEvent = (eventData: Omit<CalendarEvent, 'id'>) => {
    setEvents((prev) => [...prev, { ...eventData, id: `evt-${Date.now()}` }]);
    announce(`Added event: ${eventData.title}`);
  };

  const handleDeleteEvent = (id: string) => {
    setEvents((prev) => prev.filter((evt) => evt.id !== id));
    announce('Removed event');
  };

  const handleApproveProposal = (proposal: AgentProposal) => {
    if (proposal.payload.kind === 'event') {
      const { dateKey, startTime, duration, title, location, category } =
        proposal.payload;
      setEvents((prev) => [
        ...prev,
        {
          id: `evt-${Date.now()}`,
          dateKey,
          startTime,
          duration,
          title,
          location,
          category,
          source: proposal.source,
        },
      ]);
      setSelectedDateKey(dateKey);
      announce(`Approved & scheduled: ${title}`);
    } else {
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
    }
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

  const openTaskCount = tasks.filter((t) => !t.completed).length;
  const todayEventCount = events.filter((e) => e.dateKey === todayKey).length;
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
                <CalendarMockupCard
                  todayKey={todayKey}
                  selectedDateKey={selectedDateKey}
                  events={events}
                  isExpandedOpen={isCalendarExpanded}
                  weekStartsOn={preferences.weekStartsOn}
                  onSelectDate={setSelectedDateKey}
                  onAddEvent={handleAddEvent}
                  onDeleteEvent={handleDeleteEvent}
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
