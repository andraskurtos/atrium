import {
  lazy,
  Suspense,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import { AccountPage } from './components/AccountPage';
import { AuthScreen } from './components/AuthScreen';
import { CalendarCard } from './components/CalendarCard';
import { ChatSidebar } from './components/ChatSidebar';
import { ChecklistCard } from './components/ChecklistCard';
import { CommandPalette, type CommandItem } from './components/CommandPalette';
import { DailyNoteCard } from './components/DailyNoteCard';
import { ChevronLeftIcon, ChevronRightIcon } from './components/Icons';
import { LocalStatusCard } from './components/LocalStatusCard';
import { MobileTabBar } from './components/MobileTabBar';
import { QuickCapture } from './components/QuickCapture';
import { TopBar } from './components/TopBar';
import { UniversityPage } from './components/UniversityPage';
import { UniversityTileCard } from './components/UniversityTileCard';
import {
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
  updateCalendar,
  updateEvent,
  type CreateCalendarInput,
  type CreateEventInput,
  type UpdateEventInput,
} from './lib/calendarClient';
import {
  createDailyCheckItem,
  deleteDailyCheckItem,
  getDayTracker,
  isValidDateKey,
  shiftDateKey,
  updateDailyCheckItem,
  upsertDailyNote,
} from './lib/dayClient';
import {
  createUniversityActivity,
  createUniversityCourse,
  createUniversityItem,
  deleteUniversityActivity,
  deleteUniversityCourse,
  deleteUniversityItem,
  generateUniversitySeries,
  getUniversityWorkspace,
  updateUniversityActivity,
  updateUniversityCourse,
  updateUniversityItem,
  updateUniversityItemAttendance,
  type CreateActivityInput,
  type CreateActivityItemInput,
  type CreateCourseInput,
  type GenerateSeriesInput,
  type UpdateActivityInput,
  type UpdateActivityItemInput,
  type UpdateCourseInput,
} from './lib/universityClient';
import { clearNotesDraft } from './lib/markdownUtils';
import {
  deleteChatThread,
  getChatThread,
  listChatThreads,
  streamChat,
} from './lib/chatClient';
import type {
  AccountPreferences,
  AgentToken,
  AppView,
  CalendarEvent,
  CaptureKind,
  ChatMessage,
  ChatScope,
  ChatThreadSummary,
  DailyCheckItem,
  DailyNote,
  HouseholdMember,
  InviteItem,
  MobileTab,
  PendingNoteTarget,
  RecurrenceScope,
  SessionItem,
  StudyGuideJob,
  UniversityAttendanceStatus,
  UniversityCourseActivity,
  UniversityActivityItem,
  UpdatePreferencesPayload,
  UniversityCourse,
  UserCalendar,
  UserProfile,
} from './types/atrium';

const NotesPage = lazy(() =>
  import('./components/NotesPage').then((m) => ({ default: m.NotesPage })),
);

function readInviteCodeFromHash(): string {
  if (typeof window === 'undefined') return '';
  const match = window.location.hash.match(/^#invite=([^&]+)/);
  return match ? decodeURIComponent(match[1]) : '';
}

function readInitialView(): AppView {
  if (typeof window === 'undefined') return 'dashboard';
  if (window.location.hash === '#account') return 'account';
  if (window.location.hash === '#university') return 'university';
  if (window.location.hash === '#notes') return 'notes';
  return 'dashboard';
}

function readDateKeyFromHash(): string | null {
  if (typeof window === 'undefined') return null;
  const match = window.location.hash.match(/^#day\/(\d{4}-\d{2}-\d{2})$/);
  if (!match) return null;
  return isValidDateKey(match[1]) ? match[1] : null;
}

function getRelativeDayLabel(selectedDateKey: string, todayKey: string): string {
  if (selectedDateKey === todayKey) return 'Today';
  if (selectedDateKey === shiftDateKey(todayKey, -1)) return 'Yesterday';
  if (selectedDateKey === shiftDateKey(todayKey, 1)) return 'Tomorrow';
  return selectedDateKey < todayKey ? 'Past day' : 'Upcoming';
}

function buildCalendarFetchRange(viewYear: number): {
  from: string;
  to: string;
} {
  return {
    from: new Date(Date.UTC(viewYear - 1, 11, 1, 0, 0, 0)).toISOString(),
    to: new Date(Date.UTC(viewYear + 1, 1, 1, 0, 0, 0)).toISOString(),
  };
}

function buildTodayFetchRange(today: Date): {
  from: string;
  to: string;
} {
  const y = today.getFullYear();
  const m = today.getMonth();
  const d = today.getDate();
  return {
    from: new Date(Date.UTC(y, m, d - 1, 0, 0, 0)).toISOString(),
    to: new Date(Date.UTC(y, m, d + 2, 0, 0, 0)).toISOString(),
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
  const [selectedDateKey, setSelectedDateKey] = useState<string>(
    () => readDateKeyFromHash() ?? todayKey,
  );
  const [dayItems, setDayItems] = useState<DailyCheckItem[]>([]);
  const [dayNote, setDayNote] = useState<DailyNote | null>(null);
  const [dayLoadedDateKey, setDayLoadedDateKey] = useState<string | null>(null);
  const [dayLoading, setDayLoading] = useState(false);
  const [dayLoadError, setDayLoadError] = useState<string | null>(null);
  const [calendars, setCalendars] = useState<UserCalendar[]>([]);
  const [events, setEvents] = useState<CalendarEvent[]>([]);
  const [todayEvents, setTodayEvents] = useState<CalendarEvent[]>([]);
  const [calendarLoading, setCalendarLoading] = useState(false);
  const [universityCalendar, setUniversityCalendar] = useState<UserCalendar | null>(null);
  const [universitySemesters, setUniversitySemesters] = useState<string[]>([]);
  const [universityCourses, setUniversityCourses] = useState<UniversityCourse[]>([]);
  const [universityLoading, setUniversityLoading] = useState(false);
  const [focusedUniversityCourseId, setFocusedUniversityCourseId] = useState<string | null>(null);
  const [pendingNoteTarget, setPendingNoteTarget] = useState<PendingNoteTarget | null>(null);
  const [nowMs, setNowMs] = useState<number>(() => Date.now());
  const [dismissedSuggestionKey, setDismissedSuggestionKey] = useState<string | null>(null);

  useEffect(() => {
    const id = window.setInterval(() => setNowMs(Date.now()), 30_000);
    return () => window.clearInterval(id);
  }, []);

  const [mobileTab, setMobileTab] = useState<MobileTab>('stream');
  const [isCommandOpen, setIsCommandOpen] = useState(false);
  const [isCalendarExpanded, setIsCalendarExpanded] = useState(false);
  const [announcement, setAnnouncement] = useState<string>('');

  const quickCaptureRef = useRef<HTMLInputElement | null>(null);
  const activeViewRef = useRef<AppView>(activeView);
  const selectedDateKeyRef = useRef<string>(selectedDateKey);
  const calendarViewYearRef = useRef<number>(parseDateKey(selectedDateKey).getFullYear());
  const eventsRequestSeqRef = useRef<number>(0);
  const dayRequestSeqRef = useRef<number>(0);
  const pendingNoteSavesRef = useRef<Map<string, Promise<void>>>(new Map());

  activeViewRef.current = activeView;
  selectedDateKeyRef.current = selectedDateKey;

  const announce = useCallback((message: string) => {
    setAnnouncement(message);
  }, []);

  const clearAuthState = useCallback(() => {
    clearAccessToken();
    dayRequestSeqRef.current += 1;
    pendingNoteSavesRef.current.clear();
    setProfile(null);
    setAgentTokens([]);
    setSessions([]);
    setInvites([]);
    setMembers([]);
    setDayItems([]);
    setDayNote(null);
    setDayLoadedDateKey(null);
    setDayLoading(false);
    setDayLoadError(null);
    setCalendars([]);
    setEvents([]);
    setTodayEvents([]);
    setUniversityCalendar(null);
    setUniversitySemesters([]);
    setUniversityCourses([]);
    setFocusedUniversityCourseId(null);
    setIsCommandOpen(false);
  }, []);

  const loadDayData = useCallback(async (targetDateKey: string) => {
    const seq = ++dayRequestSeqRef.current;
    setDayLoading(true);
    setDayLoadError(null);
    try {
      // Wait for any in-flight flush PUT for this date (e.g. rapid A -> B -> A switch)
      const pendingSave = pendingNoteSavesRef.current.get(targetDateKey);
      if (pendingSave) {
        await pendingSave.catch(() => undefined);
      }
      if (seq !== dayRequestSeqRef.current) return;

      const data = await getDayTracker(targetDateKey);
      if (seq !== dayRequestSeqRef.current) return;
      setDayItems(data.checklistItems);
      setDayNote(data.note);
      setDayLoadedDateKey(targetDateKey);
    } catch (err) {
      if (seq !== dayRequestSeqRef.current) return;
      setDayItems([]);
      setDayNote(null);
      setDayLoadedDateKey(null);
      setDayLoadError(
        err instanceof Error ? err.message : 'Could not load day tracker data.',
      );
    } finally {
      if (seq === dayRequestSeqRef.current) {
        setDayLoading(false);
      }
    }
  }, []);

  const selectDay = useCallback(
    (nextDateKey: string, syncHash = true) => {
      if (!isValidDateKey(nextDateKey)) return;
      if (nextDateKey !== selectedDateKeyRef.current) {
        selectedDateKeyRef.current = nextDateKey;
        setSelectedDateKey(nextDateKey);
        setDayItems([]);
        setDayNote(null);
        setDayLoadedDateKey(null);
        setDayLoadError(null);
        setDayLoading(true);
      }
      if (syncHash && typeof window !== 'undefined' && activeViewRef.current === 'dashboard') {
        const nextHash = nextDateKey === todayKey ? '' : `#day/${nextDateKey}`;
        const nextUrl = `${window.location.pathname}${nextHash}`;
        if (window.location.hash !== nextHash) {
          window.history.replaceState(null, '', nextUrl);
        }
      }
    },
    [todayKey],
  );

  const shiftSelectedDay = useCallback(
    (deltaDays: number) => {
      const nextKey = shiftDateKey(selectedDateKeyRef.current, deltaDays);
      selectDay(nextKey, true);
    },
    [selectDay],
  );

  const reloadEvents = useCallback(
    async (
      targetYear = calendarViewYearRef.current,
      refreshToday = true,
    ) => {
      const seq = ++eventsRequestSeqRef.current;
      const range = buildCalendarFetchRange(targetYear);
      const shouldFetchTodaySeparately = refreshToday && targetYear !== todayYear;
      const [nextEvents, nextTodayEvents] = await Promise.all([
        listEvents(range),
        shouldFetchTodaySeparately
          ? listEvents(buildTodayFetchRange(today))
          : Promise.resolve<CalendarEvent[] | null>(null),
      ]);
      if (seq !== eventsRequestSeqRef.current) return;
      setEvents(nextEvents);
      if (targetYear === todayYear) {
        setTodayEvents(nextEvents);
      } else if (nextTodayEvents !== null) {
        setTodayEvents(nextTodayEvents);
      }
    },
    [today, todayYear],
  );

  const loadCalendarData = useCallback(
    async (targetYear = calendarViewYearRef.current) => {
      const seq = ++eventsRequestSeqRef.current;
      setCalendarLoading(true);
      try {
        const range = buildCalendarFetchRange(targetYear);
        const shouldFetchTodaySeparately = targetYear !== todayYear;
        const [nextCalendars, nextEvents, nextTodayEvents] = await Promise.all([
          listCalendars(),
          listEvents(range),
          shouldFetchTodaySeparately
            ? listEvents(buildTodayFetchRange(today))
            : Promise.resolve<CalendarEvent[] | null>(null),
        ]);
        if (seq !== eventsRequestSeqRef.current) return;
        setCalendars(nextCalendars);
        setEvents(nextEvents);
        if (targetYear === todayYear) {
          setTodayEvents(nextEvents);
        } else if (nextTodayEvents !== null) {
          setTodayEvents(nextTodayEvents);
        }
      } finally {
        if (seq === eventsRequestSeqRef.current) {
          setCalendarLoading(false);
        }
      }
    },
    [today, todayYear],
  );

  const loadUniversityData = useCallback(async () => {
    setUniversityLoading(true);
    try {
      const workspace = await getUniversityWorkspace();
      setUniversityCalendar(workspace.calendar);
      setUniversitySemesters(workspace.semesters);
      setUniversityCourses(workspace.courses);
    } finally {
      setUniversityLoading(false);
    }
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
            await Promise.all([
              loadAccountSecurityData(me.user.role),
              loadUniversityData()
                .catch(() => undefined)
                .then(() => loadCalendarData()),
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
  }, [loadAccountSecurityData, loadCalendarData, loadUniversityData, setTheme]);

  useEffect(() => {
    if (!announcement) return;
    const timer = window.setTimeout(() => setAnnouncement(''), 3600);
    return () => window.clearTimeout(timer);
  }, [announcement]);

  useEffect(() => {
    if (!profile) return;
    void loadDayData(selectedDateKey);
  }, [loadDayData, profile, selectedDateKey]);

  useEffect(() => {
    const handleHashChange = () => {
      const hash = window.location.hash;
      if (hash === '#account') {
        setActiveView('account');
      } else if (hash === '#university') {
        setActiveView('university');
      } else if (hash === '#notes') {
        setActiveView('notes');
      } else {
        setActiveView('dashboard');
        const hashDate = readDateKeyFromHash();
        setSelectedDateKey(hashDate ?? todayKey);
      }
    };
    window.addEventListener('hashchange', handleHashChange);
    return () => window.removeEventListener('hashchange', handleHashChange);
  }, [todayKey]);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (
        (event.metaKey || event.ctrlKey) &&
        !event.shiftKey &&
        !event.altKey &&
        event.key.toLowerCase() === 'k'
      ) {
        event.preventDefault();
        if (profile) {
          setIsCommandOpen((prev) => !prev);
        }
        return;
      }

      if (!profile || activeView !== 'dashboard' || isCommandOpen || isCalendarExpanded) {
        return;
      }

      const target = event.target as HTMLElement | null;
      if (
        target &&
        (target.tagName === 'INPUT' ||
          target.tagName === 'TEXTAREA' ||
          target.tagName === 'SELECT' ||
          target.isContentEditable)
      ) {
        return;
      }

      if (document.querySelector('dialog[open]')) {
        return;
      }

      if (
        (event.altKey && !event.ctrlKey && !event.metaKey && event.key === 'ArrowLeft') ||
        (!event.altKey && !event.ctrlKey && !event.metaKey && event.key === '[')
      ) {
        event.preventDefault();
        shiftSelectedDay(-1);
      } else if (
        (event.altKey && !event.ctrlKey && !event.metaKey && event.key === 'ArrowRight') ||
        (!event.altKey && !event.ctrlKey && !event.metaKey && event.key === ']')
      ) {
        event.preventDefault();
        shiftSelectedDay(1);
      } else if (
        !event.altKey &&
        !event.ctrlKey &&
        !event.metaKey &&
        event.key.toLowerCase() === 't'
      ) {
        event.preventDefault();
        selectDay(todayKey, true);
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [activeView, isCalendarExpanded, isCommandOpen, profile, selectDay, shiftSelectedDay, todayKey]);

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
        loadUniversityData()
          .catch(() => undefined)
          .then(() => loadCalendarData()),
      ]);
    } catch {
      // Non-fatal; individual views can refresh
    }
    announce(`Signed in as ${session.user.displayName || session.user.username}`);
  };

  const signedOutNotesUserIdRef = useRef<string | null>(null);

  useEffect(() => {
    if (!profile && signedOutNotesUserIdRef.current !== null) {
      clearNotesDraft(signedOutNotesUserIdRef.current);
      signedOutNotesUserIdRef.current = null;
    }
  }, [profile]);

  const handleSignOut = async () => {
    const signingOutUserId = profile?.id ?? '';
    signedOutNotesUserIdRef.current = signingOutUserId;
    await logout();
    clearNotesDraft(signingOutUserId);
    clearAuthState();
    announce('Signed out');
  };

  const navigateTo = (view: AppView) => {
    activeViewRef.current = view;
    setActiveView(view);
    if (view === 'account' && profile) {
      void loadAccountSecurityData(profile.role);
    } else if (view === 'university' && profile) {
      void loadUniversityData();
    }
    if (typeof window !== 'undefined') {
      const nextHash =
        view === 'account'
          ? '#account'
          : view === 'university'
            ? '#university'
            : view === 'notes'
              ? '#notes'
              : selectedDateKeyRef.current === todayKey
                ? ''
                : `#day/${selectedDateKeyRef.current}`;
      window.history.replaceState(
        null,
        '',
        `${window.location.pathname}${nextHash}`,
      );
    }
    window.scrollTo({ top: 0, behavior: 'auto' });
  };

  const openUniversityView = (courseId?: string) => {
    setFocusedUniversityCourseId(courseId ?? null);
    navigateTo('university');
  };

  const handleSelectMobileTab = (tab: MobileTab) => {
    if (tab === 'university') {
      openUniversityView();
      return;
    }
    if (tab === 'notes') {
      navigateTo('notes');
      return;
    }
    setMobileTab(tab);
    if (activeView !== 'dashboard') {
      navigateTo('dashboard');
    }
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

  const handleUpdatePreferences = async (next: UpdatePreferencesPayload) => {
    const previous = preferences;
    setPreferences(next);
    try {
      const saved = await updatePreferences(next);
      setPreferences(saved);
    } catch (err) {
      setPreferences(previous);
      announce('Could not save preferences');
      throw err;
    }
  };

  // Collapsible & Fullscreenable AI Chat Sidebar with persisted history
  const [isChatOpen, setIsChatOpen] = useState(false);
  const [isChatFullscreen, setIsChatFullscreen] = useState(false);
  const [chatScope, setChatScope] = useState<ChatScope>({ courseId: null });
  const [chatMessages, setChatMessages] = useState<ChatMessage[]>([]);
  const [chatThreads, setChatThreads] = useState<ChatThreadSummary[]>([]);
  const [activeChatThreadId, setActiveChatThreadId] = useState<string | null>(null);
  const [loadingChatThread, setLoadingChatThread] = useState(false);
  const [chatStreaming, setChatStreaming] = useState(false);
  const [chatStatusText, setChatStatusText] = useState<string | null>(null);
  const [notesRefreshKey, setNotesRefreshKey] = useState(0);
  const [studyGuideJobs, setStudyGuideJobs] = useState<Record<string, StudyGuideJob>>({});
  const chatAbortRef = useRef<AbortController | null>(null);

  const isAiConfigured = Boolean(
    preferences.aiInferenceUrl?.trim() && preferences.aiModel?.trim(),
  );

  const refreshChatThreads = useCallback(async () => {
    try {
      const list = await listChatThreads();
      setChatThreads(list);
    } catch {
      // Ignore background history refresh failure
    }
  }, []);

  useEffect(() => {
    if (isChatOpen && profile) {
      void refreshChatThreads();
    }
  }, [isChatOpen, isChatFullscreen, profile, refreshChatThreads]);

  const handleStopChat = useCallback(() => {
    chatAbortRef.current?.abort();
    chatAbortRef.current = null;
    setChatStreaming(false);
    setChatStatusText(null);
  }, []);

  const handleNewChat = useCallback(() => {
    setActiveChatThreadId(null);
    setChatMessages([]);
  }, []);

  const handleOpenCourseChat = useCallback((course: UniversityCourse) => {
    const label = course.code ? `${course.code} · ${course.name}` : course.name;
    setChatScope({ courseId: course.id, courseLabel: label });
    setActiveChatThreadId(null);
    setChatMessages([]);
    setIsChatOpen(true);
  }, []);

  const handleSelectChatThread = useCallback(async (threadId: string) => {
    setLoadingChatThread(true);
    try {
      const detail = await getChatThread(threadId);
      setActiveChatThreadId(detail.id);
      setChatScope({
        courseId: detail.courseId,
        courseLabel: detail.courseLabel ?? null,
      });
      setChatMessages(detail.messages);
    } catch {
      announce('Could not load chat conversation');
    } finally {
      setLoadingChatThread(false);
    }
  }, [announce]);

  const handleDeleteChatThread = useCallback(
    async (threadId: string) => {
      try {
        await deleteChatThread(threadId);
        setChatThreads((prev) => prev.filter((t) => t.id !== threadId));
        if (activeChatThreadId === threadId) {
          setActiveChatThreadId(null);
          setChatMessages([]);
        }
      } catch {
        announce('Could not delete chat conversation');
      }
    },
    [activeChatThreadId, announce],
  );

  const handleSendChat = useCallback(
    async (prompt: string) => {
      if (chatStreaming) return;
      const scopeCourseId = chatScope.courseId;
      const targetThreadId = activeChatThreadId;
      const userMsg: ChatMessage = {
        id: `u_${Date.now()}_${Math.random().toString(36).slice(2, 7)}`,
        role: 'user',
        content: prompt,
      };
      const assistantId = `a_${Date.now()}_${Math.random().toString(36).slice(2, 7)}`;
      const assistantMsg: ChatMessage = {
        id: assistantId,
        role: 'assistant',
        content: '',
        reasoning: '',
      };

      const historyPayload = [
        ...chatMessages
          .filter((m) => !m.error && m.content.trim().length > 0)
          .map((m) => ({ role: m.role, content: m.content })),
        { role: 'user' as const, content: prompt },
      ];

      setChatMessages((prev) => [...prev, userMsg, assistantMsg]);
      setChatStreaming(true);
      setChatStatusText('Thinking…');

      const controller = new AbortController();
      chatAbortRef.current = controller;

      try {
        await streamChat({
          messages: historyPayload,
          courseId: scopeCourseId,
          threadId: targetThreadId,
          signal: controller.signal,
          onThread: (thread) => {
            setActiveChatThreadId(thread.id);
            setChatThreads((prev) => {
              const without = prev.filter((t) => t.id !== thread.id);
              return [thread, ...without];
            });
          },
          onStatus: (status) => setChatStatusText(status),
          onReasoning: (reasoningDelta) => {
            setChatMessages((prev) =>
              prev.map((m) =>
                m.id === assistantId
                  ? { ...m, reasoning: (m.reasoning ?? '') + reasoningDelta }
                  : m,
              ),
            );
          },
          onDelta: (delta) => {
            setChatStatusText(null);
            setChatMessages((prev) =>
              prev.map((m) =>
                m.id === assistantId ? { ...m, content: m.content + delta } : m,
              ),
            );
          },
          onCitations: (citations) => {
            setChatMessages((prev) =>
              prev.map((m) =>
                m.id === assistantId ? { ...m, citations } : m,
              ),
            );
          },
          onMutation: (mutation) => {
            if (mutation.domain === 'notes') {
              setNotesRefreshKey((k) => k + 1);
            } else if (mutation.domain === 'tasks') {
              void loadDayData(selectedDateKey);
            } else if (mutation.domain === 'calendar') {
              void loadCalendarData(calendarViewYearRef.current);
            } else if (mutation.domain === 'university') {
              void loadUniversityData();
              void loadCalendarData(calendarViewYearRef.current);
            }
          },
          onError: (message) => {
            setChatMessages((prev) =>
              prev.map((m) =>
                m.id === assistantId
                  ? { ...m, content: m.content || message, error: true }
                  : m,
              ),
            );
          },
        });
      } catch (err) {
        if ((err as Error)?.name !== 'AbortError') {
          const message =
            err instanceof Error ? err.message : 'Chat request failed.';
          setChatMessages((prev) =>
            prev.map((m) =>
              m.id === assistantId
                ? { ...m, content: m.content || message, error: true }
                : m,
            ),
          );
        }
      } finally {
        if (chatAbortRef.current === controller) {
          chatAbortRef.current = null;
        }
        setChatStreaming(false);
        setChatStatusText(null);
        void refreshChatThreads();
      }
    },
    [
      activeChatThreadId,
      chatMessages,
      chatScope.courseId,
      chatStreaming,
      loadCalendarData,
      loadDayData,
      loadUniversityData,
      refreshChatThreads,
      selectedDateKey,
    ],
  );

  const handleGenerateStudyGuide = useCallback(
    async (
      course: UniversityCourse,
      activity: UniversityCourseActivity,
      item: UniversityActivityItem,
    ) => {
      if (!isAiConfigured) {
        announce('Configure AI Assistant in Account settings first');
        return;
      }

      const courseLabel = course.code ? `${course.code} · ${course.name}` : course.name;
      const actLabel = activity.title || activity.activityType;
      const prompt = [
        `Create a comprehensive, rich Markdown study guide for the lecture "${item.title}" (${actLabel}) in course "${courseLabel}".`,
        `Target universityActivityItemId: ${item.id}`,
        '',
        'Steps:',
        `1. Inspect any existing notes and uploaded slides/PDFs for this lecture (call list_artifacts with universityActivityItemId="${item.id}", read_artifact_text on relevant files, and search_knowledge/read_note if helpful).`,
        `2. Create a new note attached to universityActivityItemId="${item.id}" using create_note titled "Study Guide — ${item.title}".`,
        '3. Format the note with rich Markdown: clear H1/H2/H3 headings, concise bullet summaries, Obsidian callouts (> [!note] Key Concept, > [!tip] Exam Tip, > [!warning] Common Pitfall), GFM comparison/summary tables, syntax-highlighted fenced code blocks, LaTeX math ($...$ / $$...$$) where relevant, and a short Self-Check Questions section.',
        '4. If any PDF artifact on this lecture contains useful diagrams or figures, call extract_pdf_images(attachmentId, noteId) on the newly created study guide note and call update_note to embed the most relevant diagrams (![caption](/api/attachments/...)) in the right sections.',
        '5. Finish with a brief summary of what you created.',
      ].join('\n');

      setStudyGuideJobs((prev) => ({
        ...prev,
        [item.id]: {
          threadId: null,
          status: 'generating',
          statusText: 'Starting study guide…',
        },
      }));
      announce(`Generating study guide for ${item.title} in background`);

      try {
        await streamChat({
          messages: [{ role: 'user', content: prompt }],
          courseId: course.id,
          onThread: (thread) => {
            setStudyGuideJobs((prev) => ({
              ...prev,
              [item.id]: {
                ...(prev[item.id] ?? { status: 'generating' }),
                threadId: thread.id,
              },
            }));
            setChatThreads((prev) => {
              const without = prev.filter((t) => t.id !== thread.id);
              return [thread, ...without];
            });
          },
          onStatus: (status) => {
            setStudyGuideJobs((prev) => ({
              ...prev,
              [item.id]: {
                ...(prev[item.id] ?? { threadId: null, status: 'generating' }),
                statusText: status,
              },
            }));
          },
          onDelta: () => {
            // Stream runs silently in background; persisted to thread for View progress / Follow up
          },
          onMutation: (mutation) => {
            if (mutation.domain === 'notes') {
              setNotesRefreshKey((k) => k + 1);
            }
          },
          onError: (message) => {
            setStudyGuideJobs((prev) => ({
              ...prev,
              [item.id]: {
                ...(prev[item.id] ?? { threadId: null }),
                status: 'error',
                error: message,
              },
            }));
            announce(`Study guide failed: ${message}`);
          },
          onDone: () => {
            setStudyGuideJobs((prev) => {
              const current = prev[item.id];
              if (current?.status === 'error') return prev;
              return {
                ...prev,
                [item.id]: {
                  ...(current ?? { threadId: null }),
                  status: 'done',
                  statusText: null,
                },
              };
            });
            setNotesRefreshKey((k) => k + 1);
            void refreshChatThreads();
            announce(`Study guide ready for ${item.title}`);
          },
        });
      } catch (err) {
        const message =
          err instanceof Error ? err.message : 'Study guide generation failed.';
        setStudyGuideJobs((prev) => ({
          ...prev,
          [item.id]: {
            ...(prev[item.id] ?? { threadId: null }),
            status: 'error',
            error: message,
          },
        }));
        announce(`Study guide failed: ${message}`);
      }
    },
    [announce, isAiConfigured, refreshChatThreads],
  );

  const handleOpenStudyGuideThread = useCallback(
    async (itemId: string) => {
      const job = studyGuideJobs[itemId];
      setIsChatOpen(true);
      if (job?.threadId) {
        await handleSelectChatThread(job.threadId);
      }
    },
    [handleSelectChatThread, studyGuideJobs],
  );

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
    if (universityCalendar?.id === calendar.id) {
      setUniversityCalendar({ ...universityCalendar, isVisible: nextVisible });
    }
    try {
      const updated = await updateCalendar(calendar.id, {
        name: calendar.name,
        description: calendar.description,
        color: calendar.color,
        timezone: calendar.timezone,
        isVisible: nextVisible,
      });
      setCalendars((prev) => prev.map((c) => (c.id === updated.id ? updated : c)));
      if (universityCalendar?.id === updated.id) {
        setUniversityCalendar(updated);
      }
    } catch {
      setCalendars((prev) => prev.map((c) => (c.id === calendar.id ? calendar : c)));
      if (universityCalendar?.id === calendar.id) {
        setUniversityCalendar(calendar);
      }
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
        void reloadEvents(nextYear, false);
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
    await Promise.all([
      reloadEvents(),
      universityCalendar && input.calendarId === universityCalendar.id
        ? loadUniversityData()
        : Promise.resolve(),
    ]);
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
    await Promise.all([
      reloadEvents(),
      universityCalendar && evt.calendarId === universityCalendar.id
        ? loadUniversityData()
        : Promise.resolve(),
    ]);
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

  const handleCreateUniversityCourse = async (
    input: CreateCourseInput,
  ): Promise<UniversityCourse> => {
    const created = await createUniversityCourse(input);
    await Promise.all([loadUniversityData(), loadCalendarData()]);
    announce(`Added course: ${created.name}`);
    return created;
  };

  const handleUpdateUniversityCourse = async (
    courseId: string,
    input: UpdateCourseInput,
  ): Promise<UniversityCourse> => {
    const updated = await updateUniversityCourse(courseId, input);
    await Promise.all([loadUniversityData(), reloadEvents()]);
    announce(`Updated course: ${updated.name}`);
    return updated;
  };

  const handleDeleteUniversityCourse = async (courseId: string) => {
    try {
      await deleteUniversityCourse(courseId);
      await Promise.all([loadUniversityData(), reloadEvents()]);
      announce('Deleted university course');
    } catch (err) {
      announce(
        err instanceof Error ? err.message : 'Could not delete university course',
      );
      throw err;
    }
  };

  const handleCreateUniversityActivity = async (
    courseId: string,
    input: CreateActivityInput,
  ) => {
    await createUniversityActivity(courseId, input);
    await loadUniversityData();
    announce('Added course activity');
  };

  const handleUpdateUniversityActivity = async (
    activityId: string,
    input: UpdateActivityInput,
  ) => {
    await updateUniversityActivity(activityId, input);
    await loadUniversityData();
    announce('Updated course activity');
  };

  const handleDeleteUniversityActivity = async (activityId: string) => {
    try {
      await deleteUniversityActivity(activityId);
      await Promise.all([loadUniversityData(), reloadEvents()]);
      announce('Removed course activity');
    } catch (err) {
      announce(
        err instanceof Error ? err.message : 'Could not remove course activity',
      );
      throw err;
    }
  };

  const handleCreateUniversityItem = async (
    activityId: string,
    input: CreateActivityItemInput,
  ) => {
    const created = await createUniversityItem(activityId, input);
    await Promise.all([
      loadUniversityData(),
      created.calendarEventId ? reloadEvents() : Promise.resolve(),
    ]);
    announce(`Added: ${created.title}`);
  };

  const handleUpdateUniversityItem = async (
    itemId: string,
    input: UpdateActivityItemInput,
  ) => {
    const updated = await updateUniversityItem(itemId, input);
    await Promise.all([loadUniversityData(), reloadEvents()]);
    announce(`Updated: ${updated.title}`);
  };

  const handleUpdateUniversityItemAttendance = async (
    itemId: string,
    status: UniversityAttendanceStatus,
  ) => {
    try {
      const result = await updateUniversityItemAttendance(itemId, status);
      setUniversityCourses((prev) =>
        prev.map((course) => {
          if (course.id !== result.item.courseId) return course;
          return {
            ...course,
            activities: course.activities.map((act) => {
              if (act.id !== result.item.activityId) return act;
              return {
                ...act,
                attendance: result.attendance,
                items: act.items.map((it) =>
                  it.id === result.item.id ? result.item : it,
                ),
              };
            }),
          };
        }),
      );
    } catch (err) {
      announce(
        err instanceof Error ? err.message : 'Could not update attendance',
      );
      throw err;
    }
  };

  const handleDeleteUniversityItem = async (itemId: string) => {
    try {
      await deleteUniversityItem(itemId);
      await Promise.all([loadUniversityData(), reloadEvents()]);
      announce('Removed item');
    } catch (err) {
      announce(err instanceof Error ? err.message : 'Could not remove item');
      throw err;
    }
  };

  const handleGenerateUniversitySeries = async (
    activityId: string,
    input: GenerateSeriesInput,
  ) => {
    const res = await generateUniversitySeries(activityId, input);
    await Promise.all([loadUniversityData(), reloadEvents()]);
    announce(`Generated ${res.createdItems.length} scheduled sessions`);
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
    const targetDateKey = selectedDateKeyRef.current;
    if (kind === 'task') {
      void createDailyCheckItem(targetDateKey, { title: text })
        .then((created) => {
          if (selectedDateKeyRef.current === targetDateKey) {
            setDayItems((prev) => [...prev, created]);
          }
          announce(
            `Added task for ${
              targetDateKey === todayKey
                ? 'Today'
                : formatShortDate(parseDateKey(targetDateKey))
            }`,
          );
        })
        .catch((err) => {
          announce(err instanceof Error ? err.message : 'Could not add task');
        });
    } else {
      const startIso = localDateTimeToIso(targetDateKey, time || '15:30');
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
          if (eventOccursOnDateKey(created, todayKey)) {
            setTodayEvents((prev) => [...prev, created]);
          }
          announce(
            `Scheduled "${text}" for ${formatShortDate(parseDateKey(targetDateKey))}`,
          );
        })
        .catch(() => {
          announce('Could not schedule event');
        });
    }
  };

  const handleToggleDailyItem = async (item: DailyCheckItem) => {
    try {
      const updated = await updateDailyCheckItem(item.id, {
        isCompleted: !item.isCompleted,
      });
      setDayItems((prev) =>
        prev.map((row) => (row.id === updated.id ? updated : row)),
      );
    } catch (err) {
      announce(err instanceof Error ? err.message : 'Could not update task');
      throw err;
    }
  };

  const handleEditDailyItemTitle = async (
    item: DailyCheckItem,
    title: string,
  ) => {
    try {
      const updated = await updateDailyCheckItem(item.id, { title });
      setDayItems((prev) =>
        prev.map((row) => (row.id === updated.id ? updated : row)),
      );
      announce('Updated task');
    } catch (err) {
      announce(err instanceof Error ? err.message : 'Could not update task');
      throw err;
    }
  };

  const handleMoveDailyItemToNextDay = async (item: DailyCheckItem) => {
    const nextDateKey = shiftDateKey(item.date, 1);
    try {
      await updateDailyCheckItem(item.id, { date: nextDateKey });
      setDayItems((prev) => prev.filter((row) => row.id !== item.id));
      announce(
        `Moved "${item.title}" to ${formatShortDate(parseDateKey(nextDateKey))}`,
      );
    } catch (err) {
      announce(err instanceof Error ? err.message : 'Could not move task');
      throw err;
    }
  };

  const handleDeleteDailyItem = async (item: DailyCheckItem) => {
    try {
      await deleteDailyCheckItem(item.id);
      setDayItems((prev) => prev.filter((row) => row.id !== item.id));
      announce('Removed task');
    } catch (err) {
      announce(err instanceof Error ? err.message : 'Could not remove task');
      throw err;
    }
  };

  const handleSaveDailyNote = useCallback(
    async (
      dateKey: string,
      content: string,
      options?: { background?: boolean },
    ) => {
      const savePromise = (async () => {
        try {
          const res = await upsertDailyNote(dateKey, content);
          if (selectedDateKeyRef.current === dateKey) {
            setDayNote(res.note);
          }
        } catch (err) {
          if (options?.background) {
            announce(
              `Could not save note for ${formatShortDate(parseDateKey(dateKey))}`,
            );
          }
          throw err;
        }
      })();

      pendingNoteSavesRef.current.set(dateKey, savePromise);
      try {
        await savePromise;
      } finally {
        if (pendingNoteSavesRef.current.get(dateKey) === savePromise) {
          pendingNoteSavesRef.current.delete(dateKey);
        }
      }
    },
    [announce],
  );

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
      events,
      selectedDay: {
        date: selectedDateKey,
        checklistItems: dayItems,
        note: dayNote,
      },
      universityCourses,
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
  const openTaskCount = dayItems.filter((t) => !t.isCompleted).length;
  const completedTaskCount = dayItems.length - openTaskCount;
  const selectedDayEventsSource =
    selectedDateKey === todayKey && todayEvents.length > 0 ? todayEvents : events;
  const selectedDayEventCount = selectedDayEventsSource.filter(
    (e) => visibleCalIds.has(e.calendarId) && eventOccursOnDateKey(e, selectedDateKey),
  ).length;

  const commands: CommandItem[] = [
    {
      id: 'cmd-dashboard',
      label: 'Go to Day Tracker',
      hint: 'Day',
      group: 'Navigation',
      onSelect: () => navigateTo('dashboard'),
    },
    {
      id: 'cmd-prev-day',
      label: 'Previous Day',
      hint: '[ or Alt+←',
      group: 'Day Tracker',
      onSelect: () => {
        navigateTo('dashboard');
        shiftSelectedDay(-1);
      },
    },
    {
      id: 'cmd-next-day',
      label: 'Next Day',
      hint: '] or Alt+→',
      group: 'Day Tracker',
      onSelect: () => {
        navigateTo('dashboard');
        shiftSelectedDay(1);
      },
    },
    {
      id: 'cmd-university',
      label: 'Open University Courses & Calendar',
      hint: `${universityCourses.filter((c) => c.status === 'active').length} courses`,
      group: 'Navigation',
      onSelect: () => openUniversityView(),
    },
    {
      id: 'cmd-notes',
      label: 'Open Markdown Notes Editor',
      hint: 'Notes',
      group: 'Navigation',
      onSelect: () => navigateTo('notes'),
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
      hint: 'Day',
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
      label: 'Jump to Today',
      hint: `T · ${formatShortDate(today)}`,
      group: 'Day Tracker',
      onSelect: () => {
        navigateTo('dashboard');
        selectDay(todayKey, true);
        announce('Jumped to today');
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

  const selectedDate = parseDateKey(selectedDateKey);
  const selectedDateFormatted = formatLongDate(selectedDate);
  const isViewingToday = selectedDateKey === todayKey;
  const relativeDayLabel = getRelativeDayLabel(selectedDateKey, todayKey);
  const selectedDateLabel = isViewingToday
    ? 'Today'
    : formatShortDate(selectedDate);

  const greetingText = `${getGreeting(today)}, ${profile.displayName || profile.username}`;

  return (
    <div className="app-shell">
      <TopBar
        activeView={activeView}
        profile={profile}
        theme={theme}
        isChatOpen={isChatOpen}
        onToggleChat={() => setIsChatOpen((prev) => !prev)}
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
      ) : activeView === 'university' ? (
        <>
          <UniversityPage
            todayKey={todayKey}
            selectedDateKey={selectedDateKey}
            calendar={universityCalendar}
            semesters={universitySemesters}
            courses={universityCourses}
            loading={universityLoading}
            initialCourseId={focusedUniversityCourseId}
            weekStartsOn={preferences.weekStartsOn}
            timeFormat={preferences.timeFormat}
            onBackToDashboard={() => navigateTo('dashboard')}
            onSelectDate={(nextDate) => selectDay(nextDate, false)}
            onCreateCourse={handleCreateUniversityCourse}
            onUpdateCourse={handleUpdateUniversityCourse}
            onDeleteCourse={handleDeleteUniversityCourse}
            onCreateActivity={handleCreateUniversityActivity}
            onUpdateActivity={handleUpdateUniversityActivity}
            onDeleteActivity={handleDeleteUniversityActivity}
            onCreateItem={handleCreateUniversityItem}
            onUpdateItem={handleUpdateUniversityItem}
            onUpdateItemAttendance={handleUpdateUniversityItemAttendance}
            onDeleteItem={handleDeleteUniversityItem}
            onGenerateSeries={handleGenerateUniversitySeries}
            onToggleCalendarVisibility={handleToggleCalendarVisibility}
            onExportIcs={handleExportIcs}
            onOpenLinkedNote={(target) => {
              setPendingNoteTarget(target);
              navigateTo('notes');
            }}
            onAskAboutCourse={handleOpenCourseChat}
            notesRefreshKey={notesRefreshKey}
            isAiConfigured={isAiConfigured}
            studyGuideJobs={studyGuideJobs}
            onGenerateStudyGuide={(course, activity, item) =>
              void handleGenerateStudyGuide(course, activity, item)
            }
            onOpenStudyGuideThread={(itemId) =>
              void handleOpenStudyGuideThread(itemId)
            }
            onAnnounce={announce}
          />

          <MobileTabBar
            activeTab="university"
            isChatOpen={isChatOpen}
            onSelectTab={handleSelectMobileTab}
            onToggleChat={() => setIsChatOpen((prev) => !prev)}
          />
        </>
      ) : activeView === 'notes' ? (
        <>
          <Suspense
            fallback={
              <main
                className="notes-page-shell"
                role="status"
                aria-live="polite"
              >
                <div className="notes-canvas">
                  <p className="meta-text">Loading Markdown editor…</p>
                </div>
              </main>
            }
          >
            <NotesPage
              key={profile.id}
              userId={profile.id}
              initialTarget={pendingNoteTarget}
              onConsumeInitialTarget={() => setPendingNoteTarget(null)}
              onJumpToUniversityCourse={(courseId) => {
                setFocusedUniversityCourseId(courseId);
                navigateTo('university');
              }}
              onAnnounce={announce}
            />
          </Suspense>

          <MobileTabBar
            activeTab="notes"
            isChatOpen={isChatOpen}
            onSelectTab={handleSelectMobileTab}
            onToggleChat={() => setIsChatOpen((prev) => !prev)}
          />
        </>
      ) : (
        <>
          <div className="dashboard-container" data-mobile-tab={mobileTab}>
            <main className="focus-stream">
              <header className="stream-header" data-mobile-section="stream">
                <div className="welcome-badge-row">
                  <span
                    className={
                      isViewingToday
                        ? 'pill-accent welcome-badge'
                        : 'pill-neutral day-relative-badge'
                    }
                  >
                    {isViewingToday
                      ? `Today · ${greetingText}`
                      : relativeDayLabel}
                  </span>
                </div>

                <div className="day-header-row">
                  <h1 className="page-title">{selectedDateFormatted}</h1>
                  <div className="day-nav-bar" role="group" aria-label="Day navigation">
                    <button
                      id="day-prev-btn"
                      type="button"
                      className="btn btn-secondary btn-compact"
                      onClick={() => shiftSelectedDay(-1)}
                      title="Previous day ([ or Alt+←)"
                      aria-label="Previous day"
                    >
                      <ChevronLeftIcon />
                      <span>Prev</span>
                    </button>
                    {!isViewingToday && (
                      <button
                        id="day-today-btn"
                        type="button"
                        className="btn btn-ghost btn-compact"
                        onClick={() => selectDay(todayKey, true)}
                        title="Jump to today (T)"
                      >
                        Today
                      </button>
                    )}
                    <button
                      id="day-next-btn"
                      type="button"
                      className="btn btn-secondary btn-compact"
                      onClick={() => shiftSelectedDay(1)}
                      title="Next day (] or Alt+→)"
                      aria-label="Next day"
                    >
                      <span>Next</span>
                      <ChevronRightIcon />
                    </button>
                  </div>
                </div>

                {preferences.morningBrief && (
                  <p className="stream-summary">
                    <span className="mono-num">{openTaskCount}</span> open ·{' '}
                    <span className="mono-num">{completedTaskCount}</span> done ·{' '}
                    <span className="mono-num">{selectedDayEventCount}</span>{' '}
                    {selectedDayEventCount === 1 ? 'event' : 'events'}
                  </p>
                )}

                {isViewingToday && (() => {
                  const SOON_WINDOW_MS = 10 * 60_000;
                  type LiveSuggestion = {
                    key: string;
                    status: 'live' | 'soon';
                    label: string;
                    priority: number;
                    target: PendingNoteTarget;
                  };

                  const candidates: LiveSuggestion[] = [];

                  for (const course of universityCourses) {
                    const courseLabel = course.code || course.name;
                    for (const act of course.activities) {
                      for (const item of act.items) {
                        if (!item.startAt || !item.endAt || item.isAllDay) continue;
                        const s = Date.parse(item.startAt);
                        const e = Date.parse(item.endAt);
                        if (Number.isNaN(s) || Number.isNaN(e)) continue;
                        if (s <= nowMs && nowMs < e) {
                          candidates.push({
                            key: `uni:${item.id}`,
                            status: 'live',
                            label: `${courseLabel} · ${item.title}`,
                            priority: 0,
                            target: {
                              kind: 'university_item',
                              universityActivityItemId: item.id,
                            },
                          });
                        } else if (s > nowMs && s - nowMs <= SOON_WINDOW_MS) {
                          candidates.push({
                            key: `uni:${item.id}`,
                            status: 'soon',
                            label: `${courseLabel} · ${item.title}`,
                            priority: 2,
                            target: {
                              kind: 'university_item',
                              universityActivityItemId: item.id,
                            },
                          });
                        }
                      }
                    }
                  }

                  const uniCalIds = new Set(
                    calendars
                      .filter((c) => c.module === 'university')
                      .map((c) => c.id),
                  );

                  for (const evt of todayEvents) {
                    if (
                      evt.isAllDay ||
                      evt.status === 'cancelled' ||
                      uniCalIds.has(evt.calendarId) ||
                      evt.universityActivityItemId
                    ) {
                      continue;
                    }
                    const s = Date.parse(evt.startAt);
                    const e = Date.parse(evt.endAt);
                    if (Number.isNaN(s) || Number.isNaN(e)) continue;
                    const masterId = evt.seriesMasterId ?? evt.id;
                    const occStart = evt.isRecurring
                      ? evt.occurrenceStartAt ?? evt.startAt
                      : null;
                    if (s <= nowMs && nowMs < e) {
                      candidates.push({
                        key: `evt:${masterId}:${occStart ?? 'single'}`,
                        status: 'live',
                        label: evt.title,
                        priority: 1,
                        target: {
                          kind: 'calendar_event',
                          calendarEventId: masterId,
                          occurrenceStartAt: occStart,
                        },
                      });
                    } else if (s > nowMs && s - nowMs <= SOON_WINDOW_MS) {
                      candidates.push({
                        key: `evt:${masterId}:${occStart ?? 'single'}`,
                        status: 'soon',
                        label: evt.title,
                        priority: 3,
                        target: {
                          kind: 'calendar_event',
                          calendarEventId: masterId,
                          occurrenceStartAt: occStart,
                        },
                      });
                    }
                  }

                  candidates.sort((a, b) => a.priority - b.priority);
                  const best = candidates.find(
                    (c) => c.key !== dismissedSuggestionKey,
                  );
                  if (!best) return null;

                  return (
                    <div className="today-live-suggestion" role="status">
                      <button
                        type="button"
                        className="today-live-suggestion-btn"
                        onClick={() => {
                          setPendingNoteTarget(best.target);
                          navigateTo('notes');
                        }}
                      >
                        <span
                          className={`today-live-dot ${best.status === 'live' ? 'is-live' : ''}`}
                          aria-hidden="true"
                        />
                        <span>
                          {best.status === 'live'
                            ? `Open notes for ${best.label}?`
                            : `Starting soon: Open notes for ${best.label}?`}
                        </span>
                      </button>
                      <button
                        type="button"
                        className="today-live-suggestion-dismiss"
                        aria-label="Dismiss suggestion"
                        title="Dismiss"
                        onClick={() => setDismissedSuggestionKey(best.key)}
                      >
                        ×
                      </button>
                    </div>
                  );
                })()}
              </header>

              <div data-mobile-section="stream">
                <QuickCapture
                  inputRef={quickCaptureRef}
                  selectedDateLabel={selectedDateLabel}
                  timeFormat={preferences.timeFormat}
                  onCapture={handleCapture}
                />
              </div>

              <div data-mobile-section="stream">
                <ChecklistCard
                  dateLabel={selectedDateLabel}
                  isToday={isViewingToday}
                  items={
                    dayLoadedDateKey === selectedDateKey ? dayItems : []
                  }
                  loading={dayLoading || dayLoadedDateKey !== selectedDateKey}
                  onToggleItem={handleToggleDailyItem}
                  onEditItemTitle={handleEditDailyItemTitle}
                  onMoveItemToNextDay={handleMoveDailyItemToNextDay}
                  onDeleteItem={handleDeleteDailyItem}
                />
              </div>

              <div data-mobile-section="stream">
                <DailyNoteCard
                  dateKey={selectedDateKey}
                  dateLabel={selectedDateLabel}
                  initialContent={
                    dayNote?.date === selectedDateKey ? dayNote.content : ''
                  }
                  loading={dayLoading || dayLoadedDateKey !== selectedDateKey}
                  loadError={dayLoadError}
                  onRetryLoad={() => void loadDayData(selectedDateKey)}
                  onOpenInNotes={(dateKey) => {
                    setPendingNoteTarget({ kind: 'daily', dailyDate: dateKey });
                    navigateTo('notes');
                  }}
                  onSaveNote={handleSaveDailyNote}
                />
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
                  onSelectDate={(nextDate) => selectDay(nextDate, true)}
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
                  onOpenLinkedNote={(target) => {
                    setPendingNoteTarget(target);
                    navigateTo('notes');
                  }}
                />
              </div>

              <div className="rail-group" data-mobile-section="stream">
                <UniversityTileCard
                  courses={universityCourses}
                  loading={universityLoading}
                  timeFormat={preferences.timeFormat}
                  onOpenUniversity={openUniversityView}
                />
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
            isChatOpen={isChatOpen}
            onSelectTab={handleSelectMobileTab}
            onToggleChat={() => setIsChatOpen((prev) => !prev)}
          />
        </>
      )}

      <ChatSidebar
        isOpen={isChatOpen}
        isFullscreen={isChatFullscreen}
        scope={chatScope}
        isConfigured={isAiConfigured}
        modelLabel={preferences.aiModel}
        messages={chatMessages}
        threads={chatThreads}
        activeThreadId={activeChatThreadId}
        loadingThread={loadingChatThread}
        streaming={chatStreaming}
        statusText={chatStatusText}
        onClose={() => {
          setIsChatOpen(false);
          setIsChatFullscreen(false);
        }}
        onToggleFullscreen={() => setIsChatFullscreen((prev) => !prev)}
        onClearScope={() => {
          setChatScope({ courseId: null });
          setActiveChatThreadId(null);
          setChatMessages([]);
        }}
        onNewChat={handleNewChat}
        onSelectThread={(id) => void handleSelectChatThread(id)}
        onDeleteThread={(id) => void handleDeleteChatThread(id)}
        onSend={(prompt) => void handleSendChat(prompt)}
        onStop={handleStopChat}
        onOpenSettings={() => {
          setIsChatOpen(false);
          setIsChatFullscreen(false);
          navigateTo('account');
        }}
        onOpenNote={(noteId) => {
          setIsChatFullscreen(false);
          setPendingNoteTarget({ kind: 'note_id', noteId });
          navigateTo('notes');
        }}
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
