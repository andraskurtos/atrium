import { useEffect, useMemo, useRef, useState } from 'react';
import { AccountPage } from './components/AccountPage';
import { AgentProposalsCard } from './components/AgentProposalsCard';
import { CalendarMockupCard } from './components/CalendarMockupCard';
import { ChecklistCard } from './components/ChecklistCard';
import { CommandPalette, type CommandItem } from './components/CommandPalette';
import { LocalStatusCard } from './components/LocalStatusCard';
import { MobileTabBar } from './components/MobileTabBar';
import { NotesCard } from './components/NotesCard';
import { QuickCapture } from './components/QuickCapture';
import { TopBar } from './components/TopBar';
import { VaultCard } from './components/VaultCard';
import { WelcomeScreen } from './components/WelcomeScreen';
import {
  createDefaultPreferences,
  createDefaultProfile,
  createInitialAgentTokens,
  createInitialEvents,
  createInitialNotes,
  createInitialPasskeys,
  createInitialProposals,
  createInitialSessions,
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
import type {
  AccountPreferences,
  AgentProposal,
  AgentToken,
  AppView,
  CalendarEvent,
  CaptureKind,
  MobileTab,
  NoteItem,
  PasskeyItem,
  SessionItem,
  TaskItem,
  UserProfile,
} from './types/atrium';

const WELCOME_STORAGE_KEY = 'atrium.welcomed';
const PROFILE_STORAGE_KEY = 'atrium.profile';
const PREFS_STORAGE_KEY = 'atrium.preferences';
const LEGACY_OWNER_NAME_KEY = 'atrium.ownerName';

function readInitialView(): AppView {
  if (typeof window === 'undefined') return 'welcome';
  if (window.location.hash === '#account') return 'account';
  if (window.location.hash === '#welcome') return 'welcome';
  if (window.location.hash === '#dashboard') return 'dashboard';
  try {
    return window.localStorage.getItem(WELCOME_STORAGE_KEY) === 'true'
      ? 'dashboard'
      : 'welcome';
  } catch {
    return 'welcome';
  }
}

function readInitialProfile(): UserProfile {
  if (typeof window === 'undefined') return createDefaultProfile();
  try {
    const raw = window.localStorage.getItem(PROFILE_STORAGE_KEY);
    if (raw) {
      const parsed = JSON.parse(raw) as Partial<UserProfile>;
      return { ...createDefaultProfile(), ...parsed };
    }
    const legacyName = window.localStorage.getItem(LEGACY_OWNER_NAME_KEY) ?? '';
    return createDefaultProfile(legacyName);
  } catch {
    return createDefaultProfile();
  }
}

function readInitialPreferences(): AccountPreferences {
  if (typeof window === 'undefined') return createDefaultPreferences();
  try {
    const raw = window.localStorage.getItem(PREFS_STORAGE_KEY);
    if (raw) {
      const parsed = JSON.parse(raw) as Partial<AccountPreferences>;
      return { ...createDefaultPreferences(), ...parsed };
    }
  } catch {
    // Ignore storage errors
  }
  return createDefaultPreferences();
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

  const [activeView, setActiveView] = useState<AppView>(readInitialView);
  const [profile, setProfile] = useState<UserProfile>(readInitialProfile);
  const [preferences, setPreferences] = useState<AccountPreferences>(
    readInitialPreferences,
  );
  const [passkeys, setPasskeys] = useState<PasskeyItem[]>(createInitialPasskeys);
  const [agentTokens, setAgentTokens] = useState<AgentToken[]>(
    createInitialAgentTokens,
  );
  const [sessions, setSessions] = useState<SessionItem[]>(createInitialSessions);

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

  const announce = (message: string) => {
    setAnnouncement(message);
  };

  useEffect(() => {
    try {
      window.localStorage.setItem(PROFILE_STORAGE_KEY, JSON.stringify(profile));
    } catch {
      // Ignore quota/storage errors
    }
  }, [profile]);

  useEffect(() => {
    try {
      window.localStorage.setItem(PREFS_STORAGE_KEY, JSON.stringify(preferences));
    } catch {
      // Ignore storage errors
    }
  }, [preferences]);

  useEffect(() => {
    if (!announcement) return;
    const timer = window.setTimeout(() => setAnnouncement(''), 3600);
    return () => window.clearTimeout(timer);
  }, [announcement]);

  // Global ⌘K / Ctrl+K shortcut for Command Palette
  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        if (activeView !== 'welcome') {
          setIsCommandOpen((prev) => !prev);
        }
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [activeView]);

  const openTaskCount = tasks.filter((t) => !t.completed).length;
  const todayEventCount = events.filter((e) => e.dateKey === todayKey).length;
  const pendingProposalCount = proposals.length;

  const navigateTo = (view: AppView) => {
    setActiveView(view);
    if (view !== 'welcome') {
      try {
        window.localStorage.setItem(WELCOME_STORAGE_KEY, 'true');
      } catch {
        // Ignore storage errors
      }
    }
    window.scrollTo({ top: 0, behavior: 'auto' });
  };

  const handleEnterDashboard = (name: string, openCalendar = false) => {
    if (name !== profile.displayName) {
      setProfile((prev) => ({ ...prev, displayName: name }));
    }
    navigateTo('dashboard');
    if (openCalendar) {
      setIsCalendarExpanded(true);
      setMobileTab('calendar');
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
        {
          id,
          title: text,
          completed: false,
          category: 'Inbox',
        },
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
    setEvents((prev) => [
      ...prev,
      {
        ...eventData,
        id: `evt-${Date.now()}`,
      },
    ]);
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

  const handleAddPasskey = (deviceName: string) => {
    const addedDate = new Intl.DateTimeFormat('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    }).format(new Date());
    setPasskeys((prev) => [
      {
        id: `pk-${Date.now()}`,
        deviceName,
        addedAt: addedDate,
        lastUsed: 'Just added',
      },
      ...prev,
    ]);
    announce(`Registered passkey: ${deviceName}`);
  };

  const handleRevokePasskey = (id: string) => {
    setPasskeys((prev) => prev.filter((item) => item.id !== id));
    announce('Passkey revoked');
  };

  const handleCreateToken = (name: string, scopes: string[]): string => {
    const randomPart = Math.random().toString(36).slice(2, 10);
    const tail = Math.random().toString(36).slice(2, 10);
    const fullSecret = `atr_${randomPart}_${tail}`;
    const prefix = `atr_${randomPart.slice(0, 4)}••••`;

    setAgentTokens((prev) => [
      {
        id: `tok-${Date.now()}`,
        name,
        prefix,
        scopes,
        lastUsed: 'Never',
      },
      ...prev,
    ]);
    announce(`Created scoped token for ${name}`);
    return fullSecret;
  };

  const handleRevokeToken = (id: string) => {
    setAgentTokens((prev) => prev.filter((item) => item.id !== id));
    announce('MCP agent token revoked');
  };

  const handleRevokeSession = (id: string) => {
    setSessions((prev) => prev.filter((item) => item.id !== id));
    announce('Session signed out');
  };

  const handleSignOutOtherSessions = () => {
    setSessions((prev) => prev.filter((item) => item.isCurrent));
    announce('Signed out all other sessions');
  };

  const handleExportData = () => {
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

  const handleResetWorkspace = () => {
    try {
      window.localStorage.removeItem(PROFILE_STORAGE_KEY);
      window.localStorage.removeItem(PREFS_STORAGE_KEY);
      window.localStorage.removeItem(LEGACY_OWNER_NAME_KEY);
    } catch {
      // Ignore storage errors
    }
    setProfile(createDefaultProfile());
    setPreferences(createDefaultPreferences());
    setTasks(createInitialTasks());
    setEvents(createInitialEvents(today));
    setProposals(createInitialProposals(today));
    setNotes(createInitialNotes());
    setPasskeys(createInitialPasskeys());
    setAgentTokens(createInitialAgentTokens());
    setSessions(createInitialSessions());
    announce('Local workspace data reset to defaults');
  };

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
      id: 'cmd-welcome',
      label: 'View Welcome Screen',
      hint: 'Overview',
      group: 'Navigation',
      onSelect: () => navigateTo('welcome'),
    },
  ];

  const todayFormatted = formatLongDate(today);
  const selectedDateLabel =
    selectedDateKey === todayKey
      ? 'Today'
      : formatShortDate(parseDateKey(selectedDateKey));

  if (activeView === 'welcome') {
    return (
      <WelcomeScreen
        ownerName={profile.displayName}
        apiState={apiState}
        apiLabel={apiLabel}
        theme={theme}
        todayFormatted={todayFormatted}
        openTaskCount={openTaskCount}
        todayEventCount={todayEventCount}
        pendingProposalCount={pendingProposalCount}
        onCycleTheme={cycleTheme}
        onEnter={handleEnterDashboard}
      />
    );
  }

  const eyebrow = profile.displayName
    ? `${getGreeting(today).toUpperCase()}, ${profile.displayName.toUpperCase()} · ATRIUM`
    : `${getGreeting(today).toUpperCase()} · ATRIUM PERSONAL HUB`;

  return (
    <div className="app-shell">
      <TopBar
        activeView={activeView}
        profile={profile}
        theme={theme}
        onCycleTheme={cycleTheme}
        onOpenCommandPalette={() => setIsCommandOpen(true)}
        onNavigate={navigateTo}
      />

      {activeView === 'account' ? (
        <AccountPage
          profile={profile}
          preferences={preferences}
          theme={theme}
          passkeys={passkeys}
          agentTokens={agentTokens}
          sessions={sessions}
          apiState={apiState}
          apiLabel={apiLabel}
          lastChecked={lastChecked}
          onBackToDashboard={() => navigateTo('dashboard')}
          onUpdateProfile={setProfile}
          onUpdatePreferences={setPreferences}
          onSetTheme={setTheme}
          onAddPasskey={handleAddPasskey}
          onRevokePasskey={handleRevokePasskey}
          onCreateToken={handleCreateToken}
          onRevokeToken={handleRevokeToken}
          onRevokeSession={handleRevokeSession}
          onSignOutOtherSessions={handleSignOutOtherSessions}
          onExportData={handleExportData}
          onResetWorkspace={handleResetWorkspace}
          onRefreshHealth={refreshHealth}
          onAnnounce={announce}
        />
      ) : (
        <>
          <div className="dashboard-container" data-mobile-tab={mobileTab}>
            <main className="focus-stream">
              <header className="stream-header" data-mobile-section="stream">
                <p className="kicker">{eyebrow}</p>
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
