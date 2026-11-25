import { useEffect, useMemo, useRef, useState } from 'react';
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
import type {
  AgentProposal,
  CalendarEvent,
  CaptureKind,
  MobileTab,
  NoteItem,
  TaskItem,
} from './types/atrium';

const WELCOME_STORAGE_KEY = 'atrium.welcomed';
const OWNER_NAME_KEY = 'atrium.ownerName';

function readInitialWelcomeState(): boolean {
  if (typeof window === 'undefined') return true;
  if (window.location.hash === '#welcome') return true;
  if (window.location.hash === '#dashboard') return false;
  try {
    return window.localStorage.getItem(WELCOME_STORAGE_KEY) !== 'true';
  } catch {
    return true;
  }
}

function readInitialOwnerName(): string {
  if (typeof window === 'undefined') return '';
  try {
    return window.localStorage.getItem(OWNER_NAME_KEY) ?? '';
  } catch {
    return '';
  }
}

export function App() {
  const today = useMemo(() => new Date(), []);
  const todayKey = useMemo(() => toDateKey(today), [today]);

  const { apiState, label: apiLabel, lastChecked, refresh: refreshHealth } = useApiHealth();
  const { theme, cycleTheme } = useTheme();

  const [showWelcome, setShowWelcome] = useState<boolean>(readInitialWelcomeState);
  const [ownerName, setOwnerName] = useState<string>(readInitialOwnerName);
  const [selectedDateKey, setSelectedDateKey] = useState<string>(todayKey);
  const [tasks, setTasks] = useState<TaskItem[]>(createInitialTasks);
  const [events, setEvents] = useState<CalendarEvent[]>(() => createInitialEvents(today));
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
    if (!announcement) return;
    const timer = window.setTimeout(() => setAnnouncement(''), 3600);
    return () => window.clearTimeout(timer);
  }, [announcement]);

  // Global ⌘K / Ctrl+K shortcut for Command Palette
  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        if (!showWelcome) {
          setIsCommandOpen((prev) => !prev);
        }
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [showWelcome]);

  const openTaskCount = tasks.filter((t) => !t.completed).length;
  const todayEventCount = events.filter((e) => e.dateKey === todayKey).length;
  const pendingProposalCount = proposals.length;

  const handleEnterDashboard = (name: string, openCalendar = false) => {
    setOwnerName(name);
    setShowWelcome(false);
    try {
      window.localStorage.setItem(WELCOME_STORAGE_KEY, 'true');
      window.localStorage.setItem(OWNER_NAME_KEY, name);
    } catch {
      // Ignore storage errors
    }
    if (openCalendar) {
      setIsCalendarExpanded(true);
      setMobileTab('calendar');
    }
  };

  const handleShowWelcome = () => {
    setShowWelcome(true);
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

  const commands: CommandItem[] = [
    {
      id: 'cmd-capture',
      label: 'Focus Quick Capture',
      hint: 'Stream',
      group: 'Navigation',
      onSelect: () => {
        setMobileTab('stream');
        requestAnimationFrame(() => quickCaptureRef.current?.focus());
      },
    },
    {
      id: 'cmd-calendar',
      label: 'Open Expanded Calendar',
      hint: 'Month View',
      group: 'Calendar',
      onSelect: () => setIsCalendarExpanded(true),
    },
    {
      id: 'cmd-today',
      label: 'Jump Calendar to Today',
      hint: formatShortDate(today),
      group: 'Calendar',
      onSelect: () => {
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
      onSelect: handleShowWelcome,
    },
  ];

  const todayFormatted = formatLongDate(today);
  const selectedDateLabel =
    selectedDateKey === todayKey
      ? 'Today'
      : formatShortDate(parseDateKey(selectedDateKey));

  if (showWelcome) {
    return (
      <WelcomeScreen
        ownerName={ownerName}
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

  const eyebrow = ownerName
    ? `${getGreeting(today).toUpperCase()}, ${ownerName.toUpperCase()} · ATRIUM`
    : `${getGreeting(today).toUpperCase()} · ATRIUM PERSONAL HUB`;

  return (
    <div className="app-shell">
      <TopBar
        theme={theme}
        onCycleTheme={cycleTheme}
        onOpenCommandPalette={() => setIsCommandOpen(true)}
        onShowWelcome={handleShowWelcome}
      />

      <div className="dashboard-container" data-mobile-tab={mobileTab}>
        <main className="focus-stream">
          <header className="stream-header" data-mobile-section="stream">
            <p className="kicker">{eyebrow}</p>
            <h1 className="page-title">{todayFormatted}</h1>
            <p className="stream-summary">
              <span className="mono-num">{openTaskCount}</span>{' '}
              {openTaskCount === 1 ? 'task' : 'tasks'} open ·{' '}
              <span className="mono-num">{todayEventCount}</span>{' '}
              {todayEventCount === 1 ? 'event' : 'events'} today ·{' '}
              <span className="mono-num">{pendingProposalCount}</span> agent{' '}
              {pendingProposalCount === 1 ? 'proposal' : 'proposals'}
            </p>
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
