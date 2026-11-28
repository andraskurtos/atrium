import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type ChangeEvent,
  type FormEvent,
} from 'react';
import { formatShortDate, parseDateKey, toDateKey } from '../data/mockData';
import {
  deleteAttachment,
  downloadAttachment,
  formatFileSize,
  uploadAttachment,
} from '../lib/attachmentsClient';
import {
  allDayDatesToIsoRange,
  CALENDAR_COLOR_PALETTE,
  dateToKey,
  localDateTimeToIso,
} from '../lib/calendarClient';
import { getNotesTree } from '../lib/notesClient';
import {
  formatAttendanceCompact,
  formatScheduledTimeLabel,
  getActivityDescriptor,
  getActivityDisplayTitle,
  getAttendanceStatusLabel,
  UNIVERSITY_ACTIVITY_TYPES,
  type CreateActivityInput,
  type CreateActivityItemInput,
  type CreateCourseInput,
  type GenerateSeriesInput,
  type UpdateActivityInput,
  type UpdateActivityItemInput,
  type UpdateCourseInput,
} from '../lib/universityClient';
import {
  deleteUniversityQuiz,
  getUniversityQuiz,
  listCourseQuizzes,
  recordQuizAttempt,
} from '../lib/quizClient';
import type {
  AttachmentSummary,
  AttendanceSummary,
  NoteSummary,
  PendingNoteTarget,
  QuizGenerateOptions,
  StudyGuideJob,
  UniversityActivityItem,
  UniversityActivityType,
  UniversityAttendanceStatus,
  UniversityCourse,
  UniversityCourseActivity,
  UniversityCourseStatus,
  UniversityQuizDetail,
  UniversityQuizSummary,
  UserCalendar,
} from '../types/atrium';
import { ArrowLeftIcon, ChatIcon, CloseIcon, GraduationCapIcon, PlusIcon } from './Icons';
import { QuizGeneratorModal } from './QuizGeneratorModal';
import { QuizPlayerModal } from './QuizPlayerModal';
import { TimeInput } from './TimeInput';
import { UniversityCalendarCard } from './UniversityCalendarCard';

interface UniversityPageProps {
  todayKey: string;
  selectedDateKey: string;
  calendar: UserCalendar | null;
  semesters: string[];
  courses: UniversityCourse[];
  loading: boolean;
  initialCourseId: string | null;
  weekStartsOn?: 'monday' | 'sunday';
  timeFormat?: '24h' | '12h';
  onBackToDashboard: () => void;
  onSelectDate: (dateKey: string) => void;
  onCreateCourse: (input: CreateCourseInput) => Promise<UniversityCourse>;
  onUpdateCourse: (
    courseId: string,
    input: UpdateCourseInput,
  ) => Promise<UniversityCourse>;
  onDeleteCourse: (courseId: string) => Promise<void>;
  onCreateActivity: (
    courseId: string,
    input: CreateActivityInput,
  ) => Promise<void>;
  onUpdateActivity: (
    activityId: string,
    input: UpdateActivityInput,
  ) => Promise<void>;
  onDeleteActivity: (activityId: string) => Promise<void>;
  onCreateItem: (
    activityId: string,
    input: CreateActivityItemInput,
  ) => Promise<void>;
  onUpdateItem: (
    itemId: string,
    input: UpdateActivityItemInput,
  ) => Promise<void>;
  onUpdateItemAttendance: (
    itemId: string,
    status: UniversityAttendanceStatus,
  ) => Promise<void>;
  onDeleteItem: (itemId: string) => Promise<void>;
  onGenerateSeries: (
    activityId: string,
    input: GenerateSeriesInput,
  ) => Promise<void>;
  onToggleCalendarVisibility: (calendar: UserCalendar) => Promise<void>;
  onExportIcs: (calendar: UserCalendar) => Promise<void>;
  onOpenLinkedNote?: (target: PendingNoteTarget) => void;
  onAskAboutCourse?: (course: UniversityCourse) => void;
  notesRefreshKey?: number;
  isAiConfigured?: boolean;
  studyGuideJobs?: Record<string, StudyGuideJob>;
  onGenerateStudyGuide?: (
    course: UniversityCourse,
    activity: UniversityCourseActivity,
    item: UniversityActivityItem,
  ) => void;
  onOpenStudyGuideThread?: (itemId: string) => void;
  quizRefreshKey?: number;
  quizJobs?: Record<string, StudyGuideJob>;
  onGenerateQuiz?: (
    course: UniversityCourse,
    activity: UniversityCourseActivity | null,
    item: UniversityActivityItem | null,
    options: QuizGenerateOptions,
  ) => void;
  onOpenQuizThread?: (jobKey: string) => void;
  onAnnounce?: (message: string) => void;
}

const ATTENDANCE_STATUSES: readonly UniversityAttendanceStatus[] = [
  'pending',
  'attended',
  'missed',
  'excused',
];

function pad2(n: number): string {
  return String(n).padStart(2, '0');
}

function addMinutesToTime(timeHHMM: string, minutesToAdd: number): string {
  const [h, m] = timeHHMM.split(':').map(Number);
  const total = ((h || 0) * 60 + (m || 0) + minutesToAdd) % (24 * 60);
  const nextH = Math.floor(total / 60);
  const nextM = total % 60;
  return `${pad2(nextH)}:${pad2(nextM)}`;
}

function itemScheduleToForm(
  item: UniversityActivityItem,
  fallbackDateKey: string,
): {
  hasSchedule: boolean;
  isAllDay: boolean;
  startDate: string;
  startTime: string;
  endDate: string;
  endTime: string;
  location: string;
} {
  if (!item.startAt) {
    return {
      hasSchedule: false,
      isAllDay: false,
      startDate: fallbackDateKey,
      startTime: '10:00',
      endDate: fallbackDateKey,
      endTime: '11:30',
      location: item.location ?? '',
    };
  }
  const start = new Date(item.startAt);
  const end = new Date(item.endAt ?? item.startAt);
  if (item.isAllDay) {
    const startDate = item.startAt.slice(0, 10);
    const incEnd = new Date(
      Math.max(start.getTime(), end.getTime() - 86_400_000),
    );
    const endDate = `${incEnd.getUTCFullYear()}-${pad2(incEnd.getUTCMonth() + 1)}-${pad2(incEnd.getUTCDate())}`;
    return {
      hasSchedule: true,
      isAllDay: true,
      startDate,
      startTime: '10:00',
      endDate,
      endTime: '11:30',
      location: item.location ?? '',
    };
  }
  return {
    hasSchedule: true,
    isAllDay: false,
    startDate: dateToKey(start),
    startTime: `${pad2(start.getHours())}:${pad2(start.getMinutes())}`,
    endDate: dateToKey(end),
    endTime: `${pad2(end.getHours())}:${pad2(end.getMinutes())}`,
    location: item.location ?? '',
  };
}

function renderAttendanceProgress(
  activity: UniversityCourseActivity,
  onConfigure: () => void,
) {
  const summary: AttendanceSummary = activity.attendance;
  if (!summary.supportsAttendance) {
    return null;
  }

  if (!summary.hasRequirement) {
    return (
      <div className="uni-attendance-box is-unconfigured">
        <span className="meta-text">
          Attendance requirement not configured
          {summary.totalItemsCount > 0 && (
            <>
              {' '}
              ·{' '}
              <span className="mono-num">
                {summary.attendedCount}/{summary.effectiveTotalCount}
              </span>{' '}
              {getActivityDescriptor(activity.activityType).attendedVerb.toLowerCase()}
            </>
          )}
        </span>
        <button
          type="button"
          className="btn btn-ghost btn-compact"
          onClick={onConfigure}
        >
          Set minimum
        </button>
      </div>
    );
  }

  if (summary.status === 'no_data' || summary.requiredCount === null) {
    return (
      <div className="uni-attendance-box is-unconfigured">
        <span className="meta-text">
          Minimum{' '}
          <strong className="mono-num">{summary.minAttendancePercent}%</strong>{' '}
          required — add sessions or set a planned total to calculate how many
          you need.
        </span>
        <button
          type="button"
          className="btn btn-ghost btn-compact"
          onClick={onConfigure}
        >
          Set total
        </button>
      </div>
    );
  }

  const required = summary.requiredCount;
  const attended = summary.attendedCount;
  const remaining = summary.remainingRequired ?? 0;
  const progressPct =
    required === 0 ? 100 : Math.min(100, Math.round((attended / required) * 100));

  const toneClass =
    summary.status === 'met'
      ? 'is-ok'
      : summary.status === 'impossible'
        ? 'is-danger'
        : summary.status === 'at_risk'
          ? 'is-warn'
          : 'is-accent';

  const ruleParts: string[] = [];
  if (summary.minAttendanceCount !== null) {
    ruleParts.push(`min ${summary.minAttendanceCount}`);
  }
  if (summary.minAttendancePercent !== null) {
    ruleParts.push(`min ${summary.minAttendancePercent}%`);
  }
  if (summary.plannedTotalCount !== null) {
    ruleParts.push(`${summary.plannedTotalCount} planned`);
  }

  return (
    <div className={`uni-attendance-box ${toneClass}`}>
      <div className="uni-attendance-top">
        <div className="uni-attendance-headline">
          <span className="mono-num uni-attendance-fraction">
            {attended} / {required}
          </span>
          {remaining === 0 ? (
            <span className="uni-status-pill is-ok">Requirement met</span>
          ) : summary.status === 'impossible' ? (
            <span className="uni-status-pill is-danger">
              <span className="mono-num">{remaining}</span>
              <span>more needed · cannot reach with current plan</span>
            </span>
          ) : summary.status === 'at_risk' ? (
            <span className="uni-status-pill is-warn">
              <span className="mono-num">{remaining}</span>
              <span>
                more needed
                {summary.maxAllowedMissesLeft === 0
                  ? ' · no misses left'
                  : ' · add sessions or check plan'}
              </span>
            </span>
          ) : (
            <span className="pill-accent uni-pill-spaced">
              <span className="mono-num">{remaining}</span>
              <span>more needed</span>
            </span>
          )}
        </div>
        <span className="meta-text mono-num">
          {ruleParts.join(' · ')}
          {summary.maxAllowedMissesLeft !== null && remaining > 0
            ? ` · ${summary.maxAllowedMissesLeft} ${summary.maxAllowedMissesLeft === 1 ? 'miss' : 'misses'} left`
            : ''}
        </span>
      </div>

      <div
        className="uni-progress-track"
        role="progressbar"
        aria-valuenow={progressPct}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-label={`Attendance progress: ${attended} of ${required} required`}
      >
        <div
          className={`uni-progress-fill ${toneClass}`}
          style={{ width: `${progressPct}%` }}
        />
      </div>

      <div className="uni-attendance-meta">
        <span className="meta-text mono-num">
          {summary.attendedCount} done · {summary.missedCount} missed ·{' '}
          {summary.pendingCount} pending
          {summary.excusedCount > 0 ? ` · ${summary.excusedCount} excused` : ''}
        </span>
        {summary.currentRatePercent !== null && (
          <span className="meta-text mono-num">
            {summary.currentRatePercent}% of {summary.effectiveTotalCount}
          </span>
        )}
      </div>
    </div>
  );
}

export function UniversityPage({
  todayKey,
  selectedDateKey,
  calendar,
  semesters,
  courses,
  loading,
  initialCourseId,
  weekStartsOn = 'monday',
  timeFormat = '24h',
  onBackToDashboard,
  onSelectDate,
  onCreateCourse,
  onUpdateCourse,
  onDeleteCourse,
  onCreateActivity,
  onUpdateActivity,
  onDeleteActivity,
  onCreateItem,
  onUpdateItem,
  onUpdateItemAttendance,
  onDeleteItem,
  onGenerateSeries,
  onToggleCalendarVisibility,
  onExportIcs,
  onOpenLinkedNote,
  onAskAboutCourse,
  notesRefreshKey = 0,
  isAiConfigured = false,
  studyGuideJobs = {},
  onGenerateStudyGuide,
  onOpenStudyGuideThread,
  quizRefreshKey = 0,
  quizJobs = {},
  onGenerateQuiz,
  onOpenQuizThread,
  onAnnounce,
}: UniversityPageProps) {
  const [selectedCourseId, setSelectedCourseId] = useState<string | null>(
    initialCourseId,
  );
  const [courseNotes, setCourseNotes] = useState<NoteSummary[]>([]);
  const [courseArtifacts, setCourseArtifacts] = useState<AttachmentSummary[]>(
    [],
  );
  const [itemNotesMap, setItemNotesMap] = useState<
    Record<string, NoteSummary[]>
  >({});
  const [itemArtifactsMap, setItemArtifactsMap] = useState<
    Record<string, AttachmentSummary[]>
  >({});
  const [uploadingTargetId, setUploadingTargetId] = useState<string | null>(
    null,
  );
  const courseArtifactInputRef = useRef<HTMLInputElement | null>(null);
  const itemArtifactInputRef = useRef<HTMLInputElement | null>(null);
  const pendingItemUploadIdRef = useRef<string | null>(null);

  useEffect(() => {
    if (!selectedCourseId) {
      setCourseNotes([]);
      setCourseArtifacts([]);
      setItemNotesMap({});
      setItemArtifactsMap({});
      return;
    }
    let cancelled = false;
    void getNotesTree()
      .then((tree) => {
        if (cancelled) return;
        for (const sem of tree.university ?? []) {
          const found = sem.courses.find((c) => c.id === selectedCourseId);
          if (found) {
            setCourseNotes(found.notes);
            setCourseArtifacts(found.artifacts);
            const notesMap: Record<string, NoteSummary[]> = {};
            const map: Record<string, AttachmentSummary[]> = {};
            for (const act of found.activities) {
              for (const item of act.items) {
                notesMap[item.id] =
                  item.notes && item.notes.length > 0
                    ? item.notes
                    : item.note
                      ? [item.note]
                      : [];
                map[item.id] = item.artifacts;
              }
            }
            setItemNotesMap(notesMap);
            setItemArtifactsMap(map);
            return;
          }
        }
        setCourseNotes([]);
        setCourseArtifacts([]);
        setItemNotesMap({});
        setItemArtifactsMap({});
      })
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, [courses, selectedCourseId, notesRefreshKey]);

  const handleUploadCourseArtifact = async (
    e: ChangeEvent<HTMLInputElement>,
  ) => {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file || !selectedCourseId) return;
    setUploadingTargetId(selectedCourseId);
    try {
      const created = await uploadAttachment(file, {
        kind: 'artifact',
        universityCourseId: selectedCourseId,
      });
      setCourseArtifacts((prev) => [...prev, created]);
      onAnnounce?.(`Uploaded "${file.name}"`);
    } catch (err) {
      onAnnounce?.(
        err instanceof Error ? err.message : 'Could not upload course file',
      );
    } finally {
      setUploadingTargetId(null);
    }
  };

  const handleDeleteCourseArtifact = async (art: AttachmentSummary) => {
    try {
      await deleteAttachment(art.id);
      setCourseArtifacts((prev) => prev.filter((a) => a.id !== art.id));
      onAnnounce?.(`Removed "${art.fileName}"`);
    } catch (err) {
      onAnnounce?.(
        err instanceof Error ? err.message : 'Could not delete artifact',
      );
    }
  };

  const handleUploadItemArtifact = async (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = '';
    const itemId = pendingItemUploadIdRef.current;
    pendingItemUploadIdRef.current = null;
    if (!file || !itemId) return;
    setUploadingTargetId(itemId);
    try {
      const created = await uploadAttachment(file, {
        kind: 'artifact',
        universityActivityItemId: itemId,
      });
      setItemArtifactsMap((prev) => ({
        ...prev,
        [itemId]: [...(prev[itemId] ?? []), created],
      }));
      onAnnounce?.(`Uploaded "${file.name}"`);
    } catch (err) {
      onAnnounce?.(
        err instanceof Error ? err.message : 'Could not upload file',
      );
    } finally {
      setUploadingTargetId(null);
    }
  };

  const handleDeleteItemArtifact = async (
    itemId: string,
    art: AttachmentSummary,
  ) => {
    try {
      await deleteAttachment(art.id);
      setItemArtifactsMap((prev) => ({
        ...prev,
        [itemId]: (prev[itemId] ?? []).filter((a) => a.id !== art.id),
      }));
      onAnnounce?.(`Removed "${art.fileName}"`);
    } catch (err) {
      onAnnounce?.(
        err instanceof Error ? err.message : 'Could not delete artifact',
      );
    }
  };
  const [semesterFilter, setSemesterFilter] = useState<string>('all');
  const [statusFilter, setStatusFilter] = useState<string>('active');
  const [pendingDeleteCourseId, setPendingDeleteCourseId] = useState<
    string | null
  >(null);
  const [pendingDeleteActivityId, setPendingDeleteActivityId] = useState<
    string | null
  >(null);
  const [pendingDeleteItemId, setPendingDeleteItemId] = useState<string | null>(
    null,
  );
  const [courseQuizzes, setCourseQuizzes] = useState<UniversityQuizSummary[]>(
    [],
  );
  const [quizGenTarget, setQuizGenTarget] = useState<{
    course: UniversityCourse;
    activity: UniversityCourseActivity | null;
    item: UniversityActivityItem | null;
    label: string;
    scopeKind: 'lecture' | 'course';
  } | null>(null);
  const [activeQuiz, setActiveQuiz] = useState<UniversityQuizDetail | null>(
    null,
  );
  const [loadingQuiz, setLoadingQuiz] = useState(false);
  const [busyActionIds, setBusyActionIds] = useState<Record<string, boolean>>(
    {},
  );

  // Course modal state
  const [courseModalOpen, setCourseModalOpen] = useState(false);
  const [editingCourse, setEditingCourse] = useState<UniversityCourse | null>(
    null,
  );
  const [courseName, setCourseName] = useState('');
  const [courseCode, setCourseCode] = useState('');
  const [courseSemester, setCourseSemester] = useState('');
  const [courseInstructor, setCourseInstructor] = useState('');
  const [courseCredits, setCourseCredits] = useState('');
  const [courseColor, setCourseColor] = useState<string>(
    CALENDAR_COLOR_PALETTE[2],
  );
  const [courseStatus, setCourseStatus] =
    useState<UniversityCourseStatus>('active');
  const [courseDescription, setCourseDescription] = useState('');
  const [courseError, setCourseError] = useState<string | null>(null);
  const [courseSaving, setCourseSaving] = useState(false);

  // Activity modal state
  const [activityModalOpen, setActivityModalOpen] = useState(false);
  const [editingActivity, setEditingActivity] =
    useState<UniversityCourseActivity | null>(null);
  const [actType, setActType] = useState<UniversityActivityType>('lecture');
  const [actTitle, setActTitle] = useState('');
  const [actLocation, setActLocation] = useState('');
  const [actDescription, setActDescription] = useState('');
  const [actMinCount, setActMinCount] = useState('');
  const [actMinPercent, setActMinPercent] = useState('');
  const [actPlannedTotal, setActPlannedTotal] = useState('');
  const [actError, setActError] = useState<string | null>(null);
  const [actSaving, setActSaving] = useState(false);

  // Item modal state
  const [itemModalOpen, setItemModalOpen] = useState(false);
  const [itemTargetActivity, setItemTargetActivity] =
    useState<UniversityCourseActivity | null>(null);
  const [editingItem, setEditingItem] = useState<UniversityActivityItem | null>(
    null,
  );
  const [itemTitle, setItemTitle] = useState('');
  const [itemTheme, setItemTheme] = useState('');
  const [itemNotes, setItemNotes] = useState('');
  const [itemSequence, setItemSequence] = useState('');
  const [itemStatus, setItemStatus] =
    useState<UniversityAttendanceStatus>('pending');
  const [itemHasSchedule, setItemHasSchedule] = useState(false);
  const [itemIsAllDay, setItemIsAllDay] = useState(false);
  const [itemStartDate, setItemStartDate] = useState(selectedDateKey);
  const [itemStartTime, setItemStartTime] = useState('10:00');
  const [itemEndDate, setItemEndDate] = useState(selectedDateKey);
  const [itemEndTime, setItemEndTime] = useState('11:30');
  const [itemLocation, setItemLocation] = useState('');
  const [itemError, setItemError] = useState<string | null>(null);
  const [itemSaving, setItemSaving] = useState(false);

  // Series modal state
  const [seriesModalOpen, setSeriesModalOpen] = useState(false);
  const [seriesTargetActivity, setSeriesTargetActivity] =
    useState<UniversityCourseActivity | null>(null);
  const [seriesPrefix, setSeriesPrefix] = useState('');
  const [seriesStartNum, setSeriesStartNum] = useState('1');
  const [seriesCount, setSeriesCount] = useState('12');
  const [seriesIntervalWeeks, setSeriesIntervalWeeks] = useState('1');
  const [seriesIsAllDay, setSeriesIsAllDay] = useState(false);
  const [seriesStartDate, setSeriesStartDate] = useState(selectedDateKey);
  const [seriesStartTime, setSeriesStartTime] = useState('10:00');
  const [seriesEndTime, setSeriesEndTime] = useState('11:30');
  const [seriesLocation, setSeriesLocation] = useState('');
  const [seriesDefaultTheme, setSeriesDefaultTheme] = useState('');
  const [seriesError, setSeriesError] = useState<string | null>(null);
  const [seriesSaving, setSeriesSaving] = useState(false);

  const courseDialogRef = useRef<HTMLDialogElement | null>(null);
  const activityDialogRef = useRef<HTMLDialogElement | null>(null);
  const itemDialogRef = useRef<HTMLDialogElement | null>(null);
  const seriesDialogRef = useRef<HTMLDialogElement | null>(null);

  useEffect(() => {
    if (initialCourseId) {
      setSelectedCourseId(initialCourseId);
    }
  }, [initialCourseId]);

  useEffect(() => {
    if (!selectedCourseId) {
      setCourseQuizzes([]);
      return;
    }
    let active = true;
    void listCourseQuizzes(selectedCourseId)
      .then((list) => {
        if (active) setCourseQuizzes(list);
      })
      .catch(() => {
        if (active) setCourseQuizzes([]);
      });
    return () => {
      active = false;
    };
  }, [selectedCourseId, quizRefreshKey]);

  const courseWideQuizzes = useMemo(
    () => courseQuizzes.filter((q) => !q.universityActivityItemId),
    [courseQuizzes],
  );

  const itemQuizzesMap = useMemo(() => {
    const map: Record<string, UniversityQuizSummary[]> = {};
    for (const q of courseQuizzes) {
      if (!q.universityActivityItemId) continue;
      if (!map[q.universityActivityItemId]) map[q.universityActivityItemId] = [];
      map[q.universityActivityItemId].push(q);
    }
    return map;
  }, [courseQuizzes]);

  const handleOpenQuiz = async (quizId: string) => {
    setLoadingQuiz(true);
    try {
      const detail = await getUniversityQuiz(quizId);
      setActiveQuiz(detail);
    } catch (err) {
      onAnnounce?.(
        err instanceof Error ? err.message : 'Could not load quiz',
      );
    } finally {
      setLoadingQuiz(false);
    }
  };

  const handleRecordQuizAttempt = async (quizId: string, score: number) => {
    const updated = await recordQuizAttempt(quizId, score);
    setCourseQuizzes((prev) =>
      prev.map((q) => (q.id === updated.id ? updated : q)),
    );
    setActiveQuiz((prev) =>
      prev && prev.id === updated.id ? { ...prev, ...updated } : prev,
    );
  };

  const handleDeleteQuiz = async (quizId: string) => {
    await deleteUniversityQuiz(quizId);
    setCourseQuizzes((prev) => prev.filter((q) => q.id !== quizId));
    setActiveQuiz(null);
    onAnnounce?.('Quiz deleted');
  };

  useEffect(() => {
    const syncDialog = (
      dialog: HTMLDialogElement | null,
      open: boolean,
    ) => {
      if (!dialog) return;
      if (open && !dialog.open) dialog.showModal();
      else if (!open && dialog.open) dialog.close();
    };
    syncDialog(courseDialogRef.current, courseModalOpen);
    syncDialog(activityDialogRef.current, activityModalOpen);
    syncDialog(itemDialogRef.current, itemModalOpen);
    syncDialog(seriesDialogRef.current, seriesModalOpen);
  }, [courseModalOpen, activityModalOpen, itemModalOpen, seriesModalOpen]);

  const hasUnassignedSemester = useMemo(
    () => courses.some((c) => !c.semester || !c.semester.trim()),
    [courses],
  );

  const filteredCourses = useMemo(() => {
    return courses.filter((course) => {
      if (statusFilter !== 'all' && course.status !== statusFilter) {
        return false;
      }
      if (semesterFilter === '__unassigned__') {
        return !course.semester || !course.semester.trim();
      }
      if (semesterFilter !== 'all') {
        return (
          (course.semester ?? '').toLowerCase() === semesterFilter.toLowerCase()
        );
      }
      return true;
    });
  }, [courses, semesterFilter, statusFilter]);

  const selectedCourse = useMemo(
    () => courses.find((c) => c.id === selectedCourseId) ?? null,
    [courses, selectedCourseId],
  );

  useEffect(() => {
    if (selectedCourseId && !selectedCourse) {
      setSelectedCourseId(null);
    }
  }, [selectedCourseId, selectedCourse]);

  const summaryCounts = useMemo(() => {
    const active = courses.filter((c) => c.status === 'active');
    let activitiesCount = 0;
    let metCount = 0;
    let trackedReqCount = 0;
    for (const c of active) {
      activitiesCount += c.activities.length;
      for (const a of c.activities) {
        if (a.attendance.supportsAttendance && a.attendance.hasRequirement) {
          trackedReqCount += 1;
          if (a.attendance.status === 'met') metCount += 1;
        }
      }
    }
    return {
      activeCourses: active.length,
      activitiesCount,
      metCount,
      trackedReqCount,
    };
  }, [courses]);

  // Open Course Modal
  const openCreateCourse = () => {
    setEditingCourse(null);
    setCourseName('');
    setCourseCode('');
    setCourseSemester(
      semesterFilter !== 'all' && semesterFilter !== '__unassigned__'
        ? semesterFilter
        : '',
    );
    setCourseInstructor('');
    setCourseCredits('');
    setCourseColor(
      CALENDAR_COLOR_PALETTE[courses.length % CALENDAR_COLOR_PALETTE.length],
    );
    setCourseStatus('active');
    setCourseDescription('');
    setCourseError(null);
    setCourseModalOpen(true);
  };

  const openEditCourse = (course: UniversityCourse) => {
    setEditingCourse(course);
    setCourseName(course.name);
    setCourseCode(course.code ?? '');
    setCourseSemester(course.semester ?? '');
    setCourseInstructor(course.instructor ?? '');
    setCourseCredits(course.credits !== null ? String(course.credits) : '');
    setCourseColor(course.color);
    setCourseStatus(course.status);
    setCourseDescription(course.description ?? '');
    setCourseError(null);
    setCourseModalOpen(true);
  };

  const handleSubmitCourse = async (e: FormEvent) => {
    e.preventDefault();
    setCourseError(null);
    const trimmedName = courseName.trim();
    if (!trimmedName) {
      setCourseError('Course name is required.');
      return;
    }

    let parsedCredits: number | null = null;
    if (courseCredits.trim()) {
      const val = Number(courseCredits.trim());
      if (!Number.isFinite(val) || val < 0 || val > 100) {
        setCourseError('Credits must be a number between 0 and 100.');
        return;
      }
      parsedCredits = val;
    }

    const payload = {
      name: trimmedName,
      code: courseCode.trim() || null,
      semester: courseSemester.trim() || null,
      instructor: courseInstructor.trim() || null,
      credits: parsedCredits,
      color: courseColor,
      status: courseStatus,
      description: courseDescription.trim() || null,
    };

    setCourseSaving(true);
    try {
      if (editingCourse) {
        await onUpdateCourse(editingCourse.id, payload);
      } else {
        const created = await onCreateCourse(payload);
        setSelectedCourseId(created.id);
      }
      setCourseModalOpen(false);
    } catch (err) {
      setCourseError(
        err instanceof Error ? err.message : 'Could not save course.',
      );
    } finally {
      setCourseSaving(false);
    }
  };

  // Open Activity Modal
  const openCreateActivity = () => {
    setEditingActivity(null);
    setActType('lecture');
    setActTitle('');
    setActLocation('');
    setActDescription('');
    setActMinCount('');
    setActMinPercent('');
    setActPlannedTotal('');
    setActError(null);
    setActivityModalOpen(true);
  };

  const openEditActivity = (activity: UniversityCourseActivity) => {
    setEditingActivity(activity);
    setActType(activity.activityType);
    setActTitle(activity.title ?? '');
    setActLocation(activity.defaultLocation ?? '');
    setActDescription(activity.description ?? '');
    setActMinCount(
      activity.minAttendanceCount !== null
        ? String(activity.minAttendanceCount)
        : '',
    );
    setActMinPercent(
      activity.minAttendancePercent !== null
        ? String(activity.minAttendancePercent)
        : '',
    );
    setActPlannedTotal(
      activity.plannedTotalCount !== null
        ? String(activity.plannedTotalCount)
        : '',
    );
    setActError(null);
    setActivityModalOpen(true);
  };

  const handleSubmitActivity = async (e: FormEvent) => {
    e.preventDefault();
    if (!selectedCourse) return;
    setActError(null);

    const descriptor = getActivityDescriptor(actType);
    let minCount: number | null = null;
    let minPercent: number | null = null;
    let plannedTotal: number | null = null;

    if (descriptor.supportsAttendance) {
      if (actMinCount.trim()) {
        const parsed = Number.parseInt(actMinCount.trim(), 10);
        if (!Number.isFinite(parsed) || parsed < 0 || parsed > 1000) {
          setActError('Minimum attendance count must be 0–1000.');
          return;
        }
        minCount = parsed;
      }
      if (actMinPercent.trim()) {
        const parsed = Number(actMinPercent.trim());
        if (!Number.isFinite(parsed) || parsed < 0 || parsed > 100) {
          setActError('Minimum attendance percentage must be 0–100.');
          return;
        }
        minPercent = parsed;
      }
      if (actPlannedTotal.trim()) {
        const parsed = Number.parseInt(actPlannedTotal.trim(), 10);
        if (!Number.isFinite(parsed) || parsed < 1 || parsed > 1000) {
          setActError('Planned total must be 1–1000.');
          return;
        }
        plannedTotal = parsed;
      }
    }

    setActSaving(true);
    try {
      if (editingActivity) {
        await onUpdateActivity(editingActivity.id, {
          title: actTitle.trim() || null,
          description: actDescription.trim() || null,
          defaultLocation: actLocation.trim() || null,
          minAttendanceCount: minCount,
          minAttendancePercent: minPercent,
          plannedTotalCount: plannedTotal,
        });
      } else {
        await onCreateActivity(selectedCourse.id, {
          activityType: actType,
          title: actTitle.trim() || null,
          description: actDescription.trim() || null,
          defaultLocation: actLocation.trim() || null,
          minAttendanceCount: minCount,
          minAttendancePercent: minPercent,
          plannedTotalCount: plannedTotal,
        });
      }
      setActivityModalOpen(false);
    } catch (err) {
      setActError(
        err instanceof Error ? err.message : 'Could not save activity.',
      );
    } finally {
      setActSaving(false);
    }
  };

  // Open Item Modal
  const openCreateItem = (activity: UniversityCourseActivity) => {
    const descriptor = getActivityDescriptor(activity.activityType);
    const nextSeq =
      activity.items.reduce(
        (max, item) => Math.max(max, item.sequenceNumber ?? 0),
        activity.items.length,
      ) + 1;
    setItemTargetActivity(activity);
    setEditingItem(null);
    setItemTitle(`${getActivityDisplayTitle(activity)} ${nextSeq}`);
    setItemTheme('');
    setItemNotes('');
    setItemSequence(String(nextSeq));
    setItemStatus('pending');
    setItemHasSchedule(false);
    setItemIsAllDay(false);
    setItemStartDate(selectedDateKey);
    setItemStartTime('10:00');
    setItemEndDate(selectedDateKey);
    setItemEndTime(addMinutesToTime('10:00', descriptor.defaultDurationMinutes));
    setItemLocation(activity.defaultLocation ?? '');
    setItemError(null);
    setItemModalOpen(true);
  };

  const openEditItem = (
    activity: UniversityCourseActivity,
    item: UniversityActivityItem,
  ) => {
    const form = itemScheduleToForm(item, selectedDateKey);
    setItemTargetActivity(activity);
    setEditingItem(item);
    setItemTitle(item.title);
    setItemTheme(item.theme ?? '');
    setItemNotes(item.notes ?? '');
    setItemSequence(
      item.sequenceNumber !== null ? String(item.sequenceNumber) : '',
    );
    setItemStatus(item.attendanceStatus);
    setItemHasSchedule(form.hasSchedule);
    setItemIsAllDay(form.isAllDay);
    setItemStartDate(form.startDate);
    setItemStartTime(form.startTime);
    setItemEndDate(form.endDate);
    setItemEndTime(form.endTime);
    setItemLocation(form.hasSchedule ? form.location : (activity.defaultLocation ?? ''));
    setItemError(null);
    setItemModalOpen(true);
  };

  const handleSubmitItem = async (e: FormEvent) => {
    e.preventDefault();
    if (!itemTargetActivity) return;
    setItemError(null);

    const trimmedTitle = itemTitle.trim();
    if (!trimmedTitle) {
      setItemError('Title is required.');
      return;
    }

    let seqNum: number | null = null;
    if (itemSequence.trim()) {
      const parsed = Number.parseInt(itemSequence.trim(), 10);
      if (!Number.isFinite(parsed) || parsed < 0 || parsed > 10_000) {
        setItemError('Number must be 0–10000.');
        return;
      }
      seqNum = parsed;
    }

    let schedule: CreateActivityItemInput['schedule'] = null;
    if (itemHasSchedule) {
      if (itemIsAllDay) {
        const { startAt, endAt } = allDayDatesToIsoRange(
          itemStartDate,
          itemEndDate || itemStartDate,
        );
        schedule = {
          startAt,
          endAt,
          isAllDay: true,
          location: itemLocation.trim() || null,
        };
      } else {
        const startIso = localDateTimeToIso(itemStartDate, itemStartTime);
        const endIso = localDateTimeToIso(
          itemEndDate || itemStartDate,
          itemEndTime,
        );
        if (new Date(endIso).getTime() < new Date(startIso).getTime()) {
          setItemError('End time must be on or after start time.');
          return;
        }
        schedule = {
          startAt: startIso,
          endAt: endIso,
          isAllDay: false,
          location: itemLocation.trim() || null,
        };
      }
    }

    setItemSaving(true);
    try {
      if (editingItem) {
        await onUpdateItem(editingItem.id, {
          title: trimmedTitle,
          theme: itemTheme.trim() || null,
          notes: itemNotes.trim() || null,
          sequenceNumber: seqNum,
          attendanceStatus: itemStatus,
          schedule,
          removeCalendarEvent: !itemHasSchedule && !!editingItem.calendarEventId,
        });
      } else {
        await onCreateItem(itemTargetActivity.id, {
          title: trimmedTitle,
          theme: itemTheme.trim() || null,
          notes: itemNotes.trim() || null,
          sequenceNumber: seqNum,
          attendanceStatus: itemStatus,
          schedule,
        });
      }
      setItemModalOpen(false);
    } catch (err) {
      setItemError(err instanceof Error ? err.message : 'Could not save item.');
    } finally {
      setItemSaving(false);
    }
  };

  // Open Series Modal
  const openGenerateSeries = (activity: UniversityCourseActivity) => {
    const descriptor = getActivityDescriptor(activity.activityType);
    const nextSeq =
      activity.items.reduce(
        (max, item) => Math.max(max, item.sequenceNumber ?? 0),
        activity.items.length,
      ) + 1;
    setSeriesTargetActivity(activity);
    setSeriesPrefix(getActivityDisplayTitle(activity));
    setSeriesStartNum(String(nextSeq));
    setSeriesCount(
      activity.plannedTotalCount
        ? String(Math.max(1, activity.plannedTotalCount - activity.items.length))
        : '12',
    );
    setSeriesIntervalWeeks('1');
    setSeriesIsAllDay(false);
    setSeriesStartDate(selectedDateKey);
    setSeriesStartTime('10:00');
    setSeriesEndTime(
      addMinutesToTime('10:00', descriptor.defaultDurationMinutes),
    );
    setSeriesLocation(activity.defaultLocation ?? '');
    setSeriesDefaultTheme('');
    setSeriesError(null);
    setSeriesModalOpen(true);
  };

  const handleSubmitSeries = async (e: FormEvent) => {
    e.preventDefault();
    if (!seriesTargetActivity) return;
    setSeriesError(null);

    const count = Number.parseInt(seriesCount, 10);
    const intervalWeeks = Number.parseInt(seriesIntervalWeeks, 10);
    const startNumber = Number.parseInt(seriesStartNum, 10);
    if (!Number.isFinite(count) || count < 1 || count > 52) {
      setSeriesError('Session count must be between 1 and 52.');
      return;
    }
    if (!Number.isFinite(intervalWeeks) || intervalWeeks < 1 || intervalWeeks > 8) {
      setSeriesError('Repeat interval must be between 1 and 8 weeks.');
      return;
    }

    let startAt: string;
    let endAt: string;
    if (seriesIsAllDay) {
      const range = allDayDatesToIsoRange(seriesStartDate, seriesStartDate);
      startAt = range.startAt;
      endAt = range.endAt;
    } else {
      startAt = localDateTimeToIso(seriesStartDate, seriesStartTime);
      endAt = localDateTimeToIso(seriesStartDate, seriesEndTime);
      if (new Date(endAt).getTime() < new Date(startAt).getTime()) {
        setSeriesError('End time must be on or after start time.');
        return;
      }
    }

    setSeriesSaving(true);
    try {
      await onGenerateSeries(seriesTargetActivity.id, {
        startAt,
        endAt,
        count,
        intervalWeeks,
        titlePrefix: seriesPrefix.trim() || null,
        startNumber: Number.isFinite(startNumber) ? startNumber : 1,
        isAllDay: seriesIsAllDay,
        location: seriesLocation.trim() || null,
        defaultTheme: seriesDefaultTheme.trim() || null,
      });
      setSeriesModalOpen(false);
    } catch (err) {
      setSeriesError(
        err instanceof Error ? err.message : 'Could not generate series.',
      );
    } finally {
      setSeriesSaving(false);
    }
  };

  const actSupportsAttendance =
    getActivityDescriptor(actType).supportsAttendance;

  return (
    <div className="dashboard-container university-page-container">
      <main className="focus-stream university-main">
        <header className="stream-header uni-page-header">
          <div className="uni-header-top">
            <button
              type="button"
              className="btn btn-ghost btn-compact"
              onClick={
                selectedCourse
                  ? () => setSelectedCourseId(null)
                  : onBackToDashboard
              }
            >
              <ArrowLeftIcon />
              <span>{selectedCourse ? 'All courses' : 'Today'}</span>
            </button>

            <button
              id="uni-new-course-btn"
              type="button"
              className="btn btn-primary btn-compact"
              onClick={openCreateCourse}
            >
              <PlusIcon />
              <span>New course</span>
            </button>
          </div>

          {!selectedCourse && (
            <>
              <div className="uni-title-row">
                <GraduationCapIcon className="uni-page-icon" />
                <h1 className="page-title">University</h1>
              </div>
              <p className="stream-summary">
                <span className="mono-num">{summaryCounts.activeCourses}</span>{' '}
                active {summaryCounts.activeCourses === 1 ? 'course' : 'courses'}{' '}
                · <span className="mono-num">{summaryCounts.activitiesCount}</span>{' '}
                {summaryCounts.activitiesCount === 1
                  ? 'activity'
                  : 'activities'}
                {summaryCounts.trackedReqCount > 0 && (
                  <>
                    {' '}
                    ·{' '}
                    <span className="mono-num">
                      {summaryCounts.metCount}/{summaryCounts.trackedReqCount}
                    </span>{' '}
                    attendance requirements met
                  </>
                )}
              </p>

              <div className="uni-filter-bar" aria-label="Course filters">
                <div className="uni-semester-pills" role="group" aria-label="Semester filter">
                  <button
                    type="button"
                    className={`uni-filter-pill ${semesterFilter === 'all' ? 'is-active' : ''}`}
                    onClick={() => setSemesterFilter('all')}
                  >
                    All semesters
                  </button>
                  {semesters.map((sem) => (
                    <button
                      key={sem}
                      type="button"
                      className={`uni-filter-pill ${semesterFilter === sem ? 'is-active' : ''}`}
                      onClick={() => setSemesterFilter(sem)}
                    >
                      {sem}
                    </button>
                  ))}
                  {hasUnassignedSemester && (
                    <button
                      type="button"
                      className={`uni-filter-pill ${semesterFilter === '__unassigned__' ? 'is-active' : ''}`}
                      onClick={() => setSemesterFilter('__unassigned__')}
                    >
                      Unassigned
                    </button>
                  )}
                </div>

                <select
                  aria-label="Filter courses by status"
                  className="input-control uni-status-select"
                  value={statusFilter}
                  onChange={(e) => setStatusFilter(e.target.value)}
                >
                  <option value="active">Active</option>
                  <option value="completed">Completed</option>
                  <option value="archived">Archived</option>
                  <option value="all">All statuses</option>
                </select>
              </div>
            </>
          )}
        </header>

        {loading && courses.length === 0 ? (
          <section className="card">
            <p className="meta-text">Loading university courses…</p>
          </section>
        ) : !selectedCourse ? (
          filteredCourses.length === 0 ? (
            <section className="card uni-empty-card">
              <h2 className="card-title">
                {courses.length === 0
                  ? 'No university courses yet'
                  : 'No courses match this filter'}
              </h2>
              <p className="meta-text">
                {courses.length === 0
                  ? 'Create a course to organize lectures, practices, labs, homeworks, semester assignments, midterms, and exams. Everything except the course name is optional.'
                  : 'Try switching semesters or status filters, or add a new course.'}
              </p>
              <div className="uni-empty-actions">
                <button
                  type="button"
                  className="btn btn-primary btn-compact"
                  onClick={openCreateCourse}
                >
                  <PlusIcon />
                  <span>Add your first course</span>
                </button>
                {courses.length > 0 && (
                  <button
                    type="button"
                    className="btn btn-secondary btn-compact"
                    onClick={() => {
                      setSemesterFilter('all');
                      setStatusFilter('all');
                    }}
                  >
                    Reset filters
                  </button>
                )}
              </div>
            </section>
          ) : (
            <div className="uni-course-list">
              {filteredCourses.map((course) => (
                <article
                  key={course.id}
                  className="card uni-course-card"
                  onClick={() => setSelectedCourseId(course.id)}
                >
                  <div className="uni-course-card-head">
                    <div className="uni-course-title-block">
                      <span
                        className="uni-color-dot uni-color-dot-lg"
                        style={{ backgroundColor: course.color }}
                        aria-hidden="true"
                      />
                      <div>
                        <div className="uni-course-badges">
                          {course.code && (
                            <span className="pill-neutral mono-num">
                              {course.code}
                            </span>
                          )}
                          {course.semester && (
                            <span className="pill-neutral">
                              {course.semester}
                            </span>
                          )}
                          {course.credits !== null && (
                            <span className="pill-neutral mono-num">
                              {course.credits} cr
                            </span>
                          )}
                          {course.status !== 'active' && (
                            <span className="pill-neutral">
                              {course.status}
                            </span>
                          )}
                        </div>
                        <h2 className="card-title uni-course-name">
                          {course.name}
                        </h2>
                        {course.instructor && (
                          <p className="meta-text">{course.instructor}</p>
                        )}
                      </div>
                    </div>

                    <button
                      type="button"
                      className="btn btn-secondary btn-compact"
                      onClick={(e) => {
                        e.stopPropagation();
                        setSelectedCourseId(course.id);
                      }}
                    >
                      Open
                    </button>
                  </div>

                  {course.description && (
                    <p className="meta-text uni-course-desc">
                      {course.description}
                    </p>
                  )}

                  {course.activities.length > 0 ? (
                    <div className="uni-activity-chips">
                      {course.activities.map((act) => {
                        const label = getActivityDisplayTitle(act);
                        const compact = formatAttendanceCompact(act.attendance);
                        const statusClass =
                          act.attendance.status === 'met'
                            ? 'is-ok'
                            : act.attendance.status === 'impossible'
                              ? 'is-danger'
                              : act.attendance.status === 'at_risk'
                                ? 'is-warn'
                                : '';
                        return (
                          <span
                            key={act.id}
                            className={`uni-activity-chip ${statusClass}`}
                          >
                            <strong>{label}</strong>
                            <span className="mono-num">{compact}</span>
                          </span>
                        );
                      })}
                    </div>
                  ) : (
                    <p className="meta-text text-faint">
                      No activities added yet — open to add lectures, labs,
                      homeworks, or exams.
                    </p>
                  )}
                </article>
              ))}
            </div>
          )
        ) : (
          /* Course Detail Workspace */
          <div className="stack-md">
            <section className="card uni-detail-header-card">
              <div className="uni-course-card-head">
                <div className="uni-course-title-block">
                  <span
                    className="uni-color-dot uni-color-dot-lg"
                    style={{ backgroundColor: selectedCourse.color }}
                    aria-hidden="true"
                  />
                  <div>
                    <div className="uni-course-badges">
                      {selectedCourse.code && (
                        <span className="pill-neutral mono-num">
                          {selectedCourse.code}
                        </span>
                      )}
                      {selectedCourse.semester && (
                        <span className="pill-neutral">
                          {selectedCourse.semester}
                        </span>
                      )}
                      {selectedCourse.credits !== null && (
                        <span className="pill-neutral mono-num">
                          {selectedCourse.credits} credits
                        </span>
                      )}
                      <span className="pill-neutral">
                        {selectedCourse.status}
                      </span>
                    </div>
                    <h1 className="page-title">{selectedCourse.name}</h1>
                    {selectedCourse.instructor && (
                      <p className="meta-text">
                        Instructor: {selectedCourse.instructor}
                      </p>
                    )}
                  </div>
                </div>

                <div className="uni-detail-actions">
                  {onAskAboutCourse && (
                    <button
                      type="button"
                      className="btn btn-primary btn-compact"
                      onClick={() => onAskAboutCourse(selectedCourse)}
                    >
                      <ChatIcon />
                      <span>Ask about this course</span>
                    </button>
                  )}
                  <button
                    type="button"
                    className="btn btn-secondary btn-compact"
                    onClick={() => openEditCourse(selectedCourse)}
                  >
                    Edit course
                  </button>
                  {pendingDeleteCourseId === selectedCourse.id ? (
                    <div className="uni-inline-confirm">
                      <button
                        type="button"
                        className="btn btn-danger btn-compact"
                        disabled={Boolean(busyActionIds[selectedCourse.id])}
                        onClick={async () => {
                          if (busyActionIds[selectedCourse.id]) return;
                          setBusyActionIds((prev) => ({
                            ...prev,
                            [selectedCourse.id]: true,
                          }));
                          try {
                            await onDeleteCourse(selectedCourse.id);
                            setPendingDeleteCourseId(null);
                            setSelectedCourseId(null);
                          } catch {
                            // Error announced by App handler
                          } finally {
                            setBusyActionIds((prev) => {
                              const next = { ...prev };
                              delete next[selectedCourse.id];
                              return next;
                            });
                          }
                        }}
                      >
                        {busyActionIds[selectedCourse.id]
                          ? 'Deleting…'
                          : 'Confirm delete'}
                      </button>
                      <button
                        type="button"
                        className="btn btn-ghost btn-compact"
                        disabled={Boolean(busyActionIds[selectedCourse.id])}
                        onClick={() => setPendingDeleteCourseId(null)}
                      >
                        Cancel
                      </button>
                    </div>
                  ) : (
                    <button
                      type="button"
                      className="btn btn-ghost btn-compact uni-danger-text"
                      onClick={() =>
                        setPendingDeleteCourseId(selectedCourse.id)
                      }
                    >
                      Delete
                    </button>
                  )}
                </div>
              </div>

              {selectedCourse.description && (
                <p className="uni-detail-description">
                  {selectedCourse.description}
                </p>
              )}

              <div className="uni-course-notes-panel">
                <input
                  ref={courseArtifactInputRef}
                  type="file"
                  hidden
                  onChange={(e) => void handleUploadCourseArtifact(e)}
                />
                <input
                  ref={itemArtifactInputRef}
                  type="file"
                  hidden
                  onChange={(e) => void handleUploadItemArtifact(e)}
                />
                <div className="uni-course-notes-header">
                  <span className="meta-text">
                    <strong>Course Notes &amp; Artifacts</strong>
                  </span>
                  <div className="uni-detail-actions">
                    {onGenerateQuiz && (() => {
                      const cJobKey = `course:${selectedCourse.id}`;
                      const cJob = quizJobs[cJobKey];
                      if (cJob?.status === 'generating') {
                        return (
                          <button
                            type="button"
                            className="btn btn-secondary btn-xs"
                            onClick={() => onOpenQuizThread?.(cJobKey)}
                          >
                            <span
                              className="status-dot"
                              data-state="checking"
                              aria-hidden="true"
                            />
                            <span>Quiz progress</span>
                          </button>
                        );
                      }
                      return (
                        <button
                          type="button"
                          className="btn btn-secondary btn-xs"
                          disabled={!isAiConfigured}
                          onClick={() =>
                            setQuizGenTarget({
                              course: selectedCourse,
                              activity: null,
                              item: null,
                              label: selectedCourse.code
                                ? `${selectedCourse.code} · ${selectedCourse.name}`
                                : selectedCourse.name,
                              scopeKind: 'course',
                            })
                          }
                          title={
                            isAiConfigured
                              ? 'Generate a comprehensive quiz across this entire course'
                              : 'Configure AI Assistant in Account settings first'
                          }
                        >
                          ✦ Course quiz
                        </button>
                      );
                    })()}
                    {onOpenLinkedNote && (
                      <button
                        type="button"
                        className="btn btn-secondary btn-xs"
                        onClick={() =>
                          onOpenLinkedNote({
                            kind: 'new_course_note',
                            universityCourseId: selectedCourse.id,
                          })
                        }
                      >
                        + Course note
                      </button>
                    )}
                    <button
                      type="button"
                      className="btn btn-ghost btn-xs"
                      disabled={uploadingTargetId === selectedCourse.id}
                      onClick={() => courseArtifactInputRef.current?.click()}
                    >
                      {uploadingTargetId === selectedCourse.id
                        ? 'Uploading…'
                        : '+ Upload file'}
                    </button>
                  </div>
                </div>

                {(courseNotes.length > 0 ||
                  courseArtifacts.length > 0 ||
                  courseWideQuizzes.length > 0) && (
                  <div className="uni-course-notes-list">
                    {courseWideQuizzes.map((quiz) => (
                      <button
                        key={quiz.id}
                        type="button"
                        className="btn btn-ghost btn-xs quiz-chip"
                        onClick={() => void handleOpenQuiz(quiz.id)}
                        title={
                          quiz.description ??
                          `Play quiz (${quiz.questionCount} questions)`
                        }
                      >
                        <span>📝 {quiz.title}</span>
                        <span className="meta-text mono-num">
                          {quiz.bestScore !== null
                            ? `${quiz.bestScore}/${quiz.questionCount}`
                            : `${quiz.questionCount}Q`}
                        </span>
                      </button>
                    ))}
                    {courseNotes.map((note) => (
                      <button
                        key={note.id}
                        type="button"
                        className="btn btn-ghost btn-xs"
                        onClick={() =>
                          onOpenLinkedNote?.({
                            kind: 'note_id',
                            noteId: note.id,
                          })
                        }
                      >
                        📝 {note.title}
                      </button>
                    ))}
                    {courseArtifacts.map((art) => (
                      <span key={art.id} className="notes-artifact-chip">
                        <button
                          type="button"
                          className="notes-artifact-download"
                          onClick={() =>
                            void downloadAttachment(art.id, art.fileName)
                          }
                          title={`Download ${art.fileName} (${formatFileSize(art.sizeBytes)})`}
                        >
                          <span>📎</span>
                          <span className="notes-artifact-name">
                            {art.fileName}
                          </span>
                        </button>
                        <button
                          type="button"
                          className="notes-artifact-remove"
                          aria-label={`Delete ${art.fileName}`}
                          onClick={() => void handleDeleteCourseArtifact(art)}
                        >
                          ×
                        </button>
                      </span>
                    ))}
                  </div>
                )}
              </div>
            </section>

            <section className="uni-activities-section">
              <div className="uni-section-toolbar">
                <div>
                  <h2 className="card-title">Course Activities</h2>
                  <p className="meta-text">
                    Add only the activities this course uses. Attendance tracking
                    and calendar dates are completely optional.
                  </p>
                </div>
                <button
                  id="uni-add-activity-btn"
                  type="button"
                  className="btn btn-primary btn-compact"
                  onClick={openCreateActivity}
                >
                  <PlusIcon />
                  <span>Add activity</span>
                </button>
              </div>

              {selectedCourse.activities.length === 0 ? (
                <div className="card uni-empty-card">
                  <p className="meta-text">
                    No activities configured for {selectedCourse.name} yet. Add
                    a lecture, practice, laboratory, homework assignment,
                    semester assignment, midterm, or exam.
                  </p>
                  <button
                    type="button"
                    className="btn btn-secondary btn-compact"
                    onClick={openCreateActivity}
                  >
                    <PlusIcon />
                    <span>Add first activity</span>
                  </button>
                </div>
              ) : (
                <div className="stack-md">
                  {selectedCourse.activities.map((activity) => {
                    const descriptor = getActivityDescriptor(
                      activity.activityType,
                    );
                    const displayTitle = getActivityDisplayTitle(activity);

                    return (
                      <article
                        key={activity.id}
                        className="card uni-activity-card"
                      >
                        <div className="uni-activity-header">
                          <div>
                            <div className="uni-course-badges">
                              <span className="pill-accent">
                                {descriptor.label}
                              </span>
                              {activity.defaultLocation && (
                                <span className="pill-neutral">
                                  {activity.defaultLocation}
                                </span>
                              )}
                            </div>
                            {activity.title &&
                              activity.title.toLowerCase() !==
                                descriptor.label.toLowerCase() && (
                                <h3 className="card-title uni-activity-custom-title">
                                  {displayTitle}
                                </h3>
                              )}
                            {activity.description && (
                              <p className="meta-text">
                                {activity.description}
                              </p>
                            )}
                          </div>

                          <div className="uni-activity-actions">
                            <button
                              type="button"
                              className="btn btn-secondary btn-compact"
                              onClick={() => openCreateItem(activity)}
                            >
                              <PlusIcon />
                              <span>Add item</span>
                            </button>
                            {descriptor.supportsAttendance && (
                              <button
                                type="button"
                                className="btn btn-secondary btn-compact"
                                onClick={() => openGenerateSeries(activity)}
                              >
                                Weekly series
                              </button>
                            )}
                            <button
                              type="button"
                              className="btn btn-ghost btn-compact"
                              onClick={() => openEditActivity(activity)}
                            >
                              Edit
                            </button>
                            {pendingDeleteActivityId === activity.id ? (
                              <div className="uni-inline-confirm">
                                <button
                                  type="button"
                                  className="btn btn-danger btn-compact"
                                  disabled={Boolean(busyActionIds[activity.id])}
                                  onClick={async () => {
                                    if (busyActionIds[activity.id]) return;
                                    setBusyActionIds((prev) => ({
                                      ...prev,
                                      [activity.id]: true,
                                    }));
                                    try {
                                      await onDeleteActivity(activity.id);
                                      setPendingDeleteActivityId(null);
                                    } catch {
                                      // Error announced by App handler
                                    } finally {
                                      setBusyActionIds((prev) => {
                                        const next = { ...prev };
                                        delete next[activity.id];
                                        return next;
                                      });
                                    }
                                  }}
                                >
                                  {busyActionIds[activity.id]
                                    ? 'Deleting…'
                                    : 'Delete'}
                                </button>
                                <button
                                  type="button"
                                  className="btn btn-ghost btn-compact"
                                  disabled={Boolean(busyActionIds[activity.id])}
                                  onClick={() =>
                                    setPendingDeleteActivityId(null)
                                  }
                                >
                                  Cancel
                                </button>
                              </div>
                            ) : (
                              <button
                                type="button"
                                className="btn btn-ghost btn-compact uni-danger-text"
                                onClick={() =>
                                  setPendingDeleteActivityId(activity.id)
                                }
                              >
                                Remove
                              </button>
                            )}
                          </div>
                        </div>

                        {renderAttendanceProgress(activity, () =>
                          openEditActivity(activity),
                        )}

                        {activity.items.length === 0 ? (
                          <p className="meta-text text-faint uni-no-items">
                            No {descriptor.label.toLowerCase()} items yet. Add
                            an individual item or generate a weekly schedule.
                          </p>
                        ) : (
                          <div className="uni-items-list">
                            {activity.items.map((item) => {
                              const dateLabel = item.startAt
                                ? (() => {
                                    const dk = item.isAllDay
                                      ? item.startAt.slice(0, 10)
                                      : toDateKey(new Date(item.startAt));
                                    const formattedDate =
                                      dk === todayKey
                                        ? 'Today'
                                        : formatShortDate(parseDateKey(dk));
                                    const timeStr = formatScheduledTimeLabel(
                                      item.startAt,
                                      item.endAt,
                                      item.isAllDay,
                                      timeFormat,
                                    );
                                    return `${formattedDate} · ${timeStr}`;
                                  })()
                                : null;

                              return (
                                <div key={item.id} className="uni-item-row">
                                  <div className="uni-item-main">
                                    <div className="uni-item-top">
                                      <span className="row-title">
                                        {item.title}
                                      </span>
                                      {dateLabel ? (
                                        <span className="pill-neutral mono-num">
                                          {dateLabel}
                                        </span>
                                      ) : (
                                        <span className="pill-neutral text-faint">
                                          Unscheduled
                                        </span>
                                      )}
                                      {item.location && (
                                        <span className="meta-text">
                                          {item.location}
                                        </span>
                                      )}
                                    </div>

                                    {item.theme && (
                                      <p className="uni-item-theme">
                                        <span className="uni-theme-label">
                                          Theme:
                                        </span>{' '}
                                        {item.theme}
                                      </p>
                                    )}

                                    {item.notes && (
                                      <p className="meta-text">{item.notes}</p>
                                    )}

                                    {((itemNotesMap[item.id]?.length ?? 0) > 0 ||
                                      (itemArtifactsMap[item.id]?.length ?? 0) > 0 ||
                                      (itemQuizzesMap[item.id]?.length ?? 0) > 0) && (
                                      <div className="uni-course-notes-list">
                                        {(itemQuizzesMap[item.id] ?? []).map(
                                          (quiz) => (
                                            <button
                                              key={quiz.id}
                                              type="button"
                                              className="btn btn-ghost btn-xs quiz-chip"
                                              onClick={() =>
                                                void handleOpenQuiz(quiz.id)
                                              }
                                              title={
                                                quiz.description ??
                                                `Play quiz (${quiz.questionCount} questions)`
                                              }
                                            >
                                              <span>📝 {quiz.title}</span>
                                              <span className="meta-text mono-num">
                                                {quiz.bestScore !== null
                                                  ? `${quiz.bestScore}/${quiz.questionCount}`
                                                  : `${quiz.questionCount}Q`}
                                              </span>
                                            </button>
                                          ),
                                        )}
                                        {(itemNotesMap[item.id] ?? []).map(
                                          (note) => (
                                            <button
                                              key={note.id}
                                              type="button"
                                              className="btn btn-ghost btn-xs"
                                              onClick={() =>
                                                onOpenLinkedNote?.({
                                                  kind: 'note_id',
                                                  noteId: note.id,
                                                })
                                              }
                                              title={`Open note: ${note.title}`}
                                            >
                                              📄 {note.title}
                                            </button>
                                          ),
                                        )}
                                        {(itemArtifactsMap[item.id] ?? []).map((art) => (
                                          <span
                                            key={art.id}
                                            className="notes-artifact-chip"
                                          >
                                            <button
                                              type="button"
                                              className="notes-artifact-download"
                                              onClick={() =>
                                                void downloadAttachment(
                                                  art.id,
                                                  art.fileName,
                                                )
                                              }
                                              title={`Download ${art.fileName} (${formatFileSize(art.sizeBytes)})`}
                                            >
                                              <span>📎</span>
                                              <span className="notes-artifact-name">
                                                {art.fileName}
                                              </span>
                                            </button>
                                            <button
                                              type="button"
                                              className="notes-artifact-remove"
                                              aria-label={`Delete ${art.fileName}`}
                                              onClick={() =>
                                                void handleDeleteItemArtifact(
                                                  item.id,
                                                  art,
                                                )
                                              }
                                            >
                                              ×
                                            </button>
                                          </span>
                                        ))}
                                      </div>
                                    )}
                                  </div>

                                  <div className="uni-item-controls">
                                    <div
                                      className="uni-attendance-segment"
                                      role="group"
                                      aria-label={`Attendance for ${item.title}`}
                                    >
                                      {ATTENDANCE_STATUSES.map((st) => (
                                        <button
                                          key={st}
                                          type="button"
                                          className={`uni-seg-btn is-${st} ${
                                            item.attendanceStatus === st
                                              ? 'is-active'
                                              : ''
                                          }`}
                                          aria-pressed={
                                            item.attendanceStatus === st
                                          }
                                          disabled={Boolean(busyActionIds[item.id])}
                                          onClick={() => {
                                            if (busyActionIds[item.id]) return;
                                            setBusyActionIds((prev) => ({
                                              ...prev,
                                              [item.id]: true,
                                            }));
                                            void onUpdateItemAttendance(
                                              item.id,
                                              st,
                                            )
                                              .catch(() => undefined)
                                              .finally(() => {
                                                setBusyActionIds((prev) => {
                                                  const next = { ...prev };
                                                  delete next[item.id];
                                                  return next;
                                                });
                                              });
                                          }}
                                        >
                                          {getAttendanceStatusLabel(
                                            st,
                                            activity.activityType,
                                          )}
                                        </button>
                                      ))}
                                    </div>

                                    <div className="uni-item-row-actions">
                                      {onGenerateQuiz && (() => {
                                        const qJob = quizJobs[item.id];
                                        if (qJob?.status === 'generating') {
                                          return (
                                            <button
                                              type="button"
                                              className="btn btn-secondary btn-compact"
                                              onClick={() =>
                                                onOpenQuizThread?.(item.id)
                                              }
                                              title={
                                                qJob.statusText ||
                                                'Generating quiz in background'
                                              }
                                            >
                                              <span
                                                className="status-dot"
                                                data-state="checking"
                                                aria-hidden="true"
                                              />
                                              <span>Quiz progress</span>
                                            </button>
                                          );
                                        }
                                        return (
                                          <button
                                            type="button"
                                            className="btn btn-ghost btn-compact"
                                            disabled={!isAiConfigured}
                                            onClick={() =>
                                              setQuizGenTarget({
                                                course: selectedCourse,
                                                activity,
                                                item,
                                                label: `${selectedCourse.code || selectedCourse.name} · ${item.title}`,
                                                scopeKind: 'lecture',
                                              })
                                            }
                                            title={
                                              isAiConfigured
                                                ? 'Customize and generate a playable quiz for this lecture'
                                                : 'Configure AI Assistant in Account settings first'
                                            }
                                          >
                                            📝 Quiz
                                          </button>
                                        );
                                      })()}
                                      {onGenerateStudyGuide && (() => {
                                        const job = studyGuideJobs[item.id];
                                        if (job?.status === 'generating') {
                                          return (
                                            <button
                                              type="button"
                                              className="btn btn-secondary btn-compact"
                                              onClick={() =>
                                                onOpenStudyGuideThread?.(item.id)
                                              }
                                              title={
                                                job.statusText ||
                                                'Generating study guide in background'
                                              }
                                            >
                                              <span
                                                className="status-dot"
                                                data-state="checking"
                                                aria-hidden="true"
                                              />
                                              <span>View progress</span>
                                            </button>
                                          );
                                        }
                                        return (
                                          <>
                                            {job?.status === 'done' &&
                                              job.threadId && (
                                                <button
                                                  type="button"
                                                  className="btn btn-secondary btn-compact"
                                                  onClick={() =>
                                                    onOpenStudyGuideThread?.(
                                                      item.id,
                                                    )
                                                  }
                                                  title="Open the study guide chat thread to refine or ask follow-up questions"
                                                >
                                                  <ChatIcon />
                                                  <span>Follow up</span>
                                                </button>
                                              )}
                                            <button
                                              type="button"
                                              className="btn btn-ghost btn-compact"
                                              disabled={!isAiConfigured}
                                              onClick={() =>
                                                onGenerateStudyGuide(
                                                  selectedCourse,
                                                  activity,
                                                  item,
                                                )
                                              }
                                              title={
                                                isAiConfigured
                                                  ? `Generate a rich Markdown study guide for ${item.title}`
                                                  : 'Configure AI Assistant in Account settings first'
                                              }
                                            >
                                              <span>Study Guide</span>
                                            </button>
                                          </>
                                        );
                                      })()}
                                      {onOpenLinkedNote && (
                                        <button
                                          type="button"
                                          className={`btn btn-compact ${
                                            item.linkedNoteId
                                              ? 'btn-secondary'
                                              : 'btn-ghost'
                                          }`}
                                          onClick={() =>
                                            onOpenLinkedNote({
                                              kind: 'university_item',
                                              universityActivityItemId: item.id,
                                            })
                                          }
                                        >
                                          {item.linkedNoteId ? 'Note' : '+ Note'}
                                        </button>
                                      )}
                                      <button
                                        type="button"
                                        className="btn btn-ghost btn-compact"
                                        disabled={uploadingTargetId === item.id}
                                        onClick={() => {
                                          pendingItemUploadIdRef.current =
                                            item.id;
                                          itemArtifactInputRef.current?.click();
                                        }}
                                      >
                                        {uploadingTargetId === item.id
                                          ? 'Uploading…'
                                          : '+ File'}
                                      </button>
                                      <button
                                        type="button"
                                        className="btn btn-ghost btn-compact"
                                        disabled={Boolean(busyActionIds[item.id])}
                                        onClick={() =>
                                          openEditItem(activity, item)
                                        }
                                      >
                                        Edit
                                      </button>
                                      {pendingDeleteItemId === item.id ? (
                                        <div className="uni-inline-confirm">
                                          <button
                                            type="button"
                                            className="btn btn-danger btn-compact"
                                            disabled={Boolean(busyActionIds[item.id])}
                                            onClick={async () => {
                                              if (busyActionIds[item.id]) return;
                                              setBusyActionIds((prev) => ({
                                                ...prev,
                                                [item.id]: true,
                                              }));
                                              try {
                                                await onDeleteItem(item.id);
                                                setPendingDeleteItemId(null);
                                              } catch {
                                                // Error announced by App handler
                                              } finally {
                                                setBusyActionIds((prev) => {
                                                  const next = { ...prev };
                                                  delete next[item.id];
                                                  return next;
                                                });
                                              }
                                            }}
                                          >
                                            {busyActionIds[item.id]
                                              ? 'Deleting…'
                                              : 'Confirm'}
                                          </button>
                                          <button
                                            type="button"
                                            className="btn btn-ghost btn-compact"
                                            disabled={Boolean(busyActionIds[item.id])}
                                            onClick={() =>
                                              setPendingDeleteItemId(null)
                                            }
                                          >
                                            Cancel
                                          </button>
                                        </div>
                                      ) : (
                                        <button
                                          type="button"
                                          className="btn btn-ghost btn-compact uni-danger-text"
                                          disabled={Boolean(busyActionIds[item.id])}
                                          onClick={() =>
                                            setPendingDeleteItemId(item.id)
                                          }
                                        >
                                          Delete
                                        </button>
                                      )}
                                    </div>
                                  </div>
                                </div>
                              );
                            })}
                          </div>
                        )}
                      </article>
                    );
                  })}
                </div>
              )}
            </section>
          </div>
        )}
      </main>

      <aside className="context-rail" aria-label="University calendar rail">
        <UniversityCalendarCard
          todayKey={todayKey}
          selectedDateKey={selectedDateKey}
          calendar={calendar}
          courses={courses}
          weekStartsOn={weekStartsOn}
          timeFormat={timeFormat}
          onSelectDate={onSelectDate}
          onSelectCourse={(courseId) => setSelectedCourseId(courseId)}
          onToggleDashboardVisibility={onToggleCalendarVisibility}
          onExportIcs={onExportIcs}
          onQuickAttendance={onUpdateItemAttendance}
        />
      </aside>

      {/* Course Create / Edit Modal */}
      <dialog
        ref={courseDialogRef}
        className="atrium-dialog"
        onClose={() => setCourseModalOpen(false)}
        aria-labelledby="uni-course-dialog-title"
      >
        <div className="dialog-header">
          <div>
            <p className="kicker">University Course</p>
            <h2 id="uni-course-dialog-title" className="dialog-title">
              {editingCourse ? 'Edit course' : 'New course'}
            </h2>
          </div>
          <button
            type="button"
            className="btn btn-ghost btn-icon"
            aria-label="Close dialog"
            onClick={() => setCourseModalOpen(false)}
          >
            <CloseIcon />
          </button>
        </div>

        <form className="uni-form" onSubmit={handleSubmitCourse}>
          <label className="field-group">
            <span className="field-label">Course name *</span>
            <input
              type="text"
              className="input-control"
              value={courseName}
              onChange={(e) => setCourseName(e.target.value)}
              placeholder="e.g. Algorithms & Data Structures"
              maxLength={140}
              required
            />
          </label>

          <div className="uni-form-grid-2">
            <label className="field-group">
              <span className="field-label">Course code (optional)</span>
              <input
                type="text"
                className="input-control"
                value={courseCode}
                onChange={(e) => setCourseCode(e.target.value)}
                placeholder="e.g. CS-201"
                maxLength={32}
              />
            </label>

            <label className="field-group">
              <span className="field-label">Semester / Term (optional)</span>
              <input
                type="text"
                list="uni-semester-suggestions"
                className="input-control"
                value={courseSemester}
                onChange={(e) => setCourseSemester(e.target.value)}
                placeholder="e.g. 2026/27 Fall"
                maxLength={64}
              />
              <datalist id="uni-semester-suggestions">
                {semesters.map((s) => (
                  <option key={s} value={s} />
                ))}
              </datalist>
            </label>
          </div>

          <div className="uni-form-grid-3">
            <label className="field-group">
              <span className="field-label">Instructor (optional)</span>
              <input
                type="text"
                className="input-control"
                value={courseInstructor}
                onChange={(e) => setCourseInstructor(e.target.value)}
                placeholder="Prof. Name"
                maxLength={120}
              />
            </label>

            <label className="field-group">
              <span className="field-label">Credits (optional)</span>
              <input
                type="number"
                step="0.5"
                min="0"
                max="100"
                className="input-control mono-num"
                value={courseCredits}
                onChange={(e) => setCourseCredits(e.target.value)}
                placeholder="5"
              />
            </label>

            <label className="field-group">
              <span className="field-label">Status</span>
              <select
                className="input-control"
                value={courseStatus}
                onChange={(e) =>
                  setCourseStatus(e.target.value as UniversityCourseStatus)
                }
              >
                <option value="active">Active</option>
                <option value="completed">Completed</option>
                <option value="archived">Archived</option>
              </select>
            </label>
          </div>

          <div className="field-group">
            <span className="field-label">Color</span>
            <div className="uni-color-swatches">
              {CALENDAR_COLOR_PALETTE.map((hex) => (
                <button
                  key={hex}
                  type="button"
                  className={`uni-color-swatch ${
                    courseColor.toLowerCase() === hex ? 'is-selected' : ''
                  }`}
                  style={{ backgroundColor: hex }}
                  aria-label={`Select color ${hex}`}
                  aria-pressed={courseColor.toLowerCase() === hex}
                  onClick={() => setCourseColor(hex)}
                />
              ))}
            </div>
          </div>

          <label className="field-group">
            <span className="field-label">Description (optional)</span>
            <textarea
              className="input-control uni-textarea"
              rows={3}
              value={courseDescription}
              onChange={(e) => setCourseDescription(e.target.value)}
              placeholder="Course notes, grading policy, syllabus overview, links…"
            />
          </label>

          {courseError && (
            <p className="form-error" role="alert">
              {courseError}
            </p>
          )}

          <div className="uni-dialog-footer">
            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => setCourseModalOpen(false)}
            >
              Cancel
            </button>
            <button
              type="submit"
              className="btn btn-primary"
              disabled={courseSaving}
            >
              {courseSaving
                ? 'Saving…'
                : editingCourse
                  ? 'Save changes'
                  : 'Create course'}
            </button>
          </div>
        </form>
      </dialog>

      {/* Activity Create / Edit Modal */}
      <dialog
        ref={activityDialogRef}
        className="atrium-dialog"
        onClose={() => setActivityModalOpen(false)}
        aria-labelledby="uni-activity-dialog-title"
      >
        <div className="dialog-header">
          <div>
            <p className="kicker">Course Activity</p>
            <h2 id="uni-activity-dialog-title" className="dialog-title">
              {editingActivity ? 'Edit activity' : 'Add activity'}
            </h2>
          </div>
          <button
            type="button"
            className="btn btn-ghost btn-icon"
            aria-label="Close dialog"
            onClick={() => setActivityModalOpen(false)}
          >
            <CloseIcon />
          </button>
        </div>

        <form className="uni-form" onSubmit={handleSubmitActivity}>
          <label className="field-group">
            <span className="field-label">Activity type</span>
            <select
              className="input-control"
              value={actType}
              disabled={editingActivity !== null}
              onChange={(e) =>
                setActType(e.target.value as UniversityActivityType)
              }
            >
              {UNIVERSITY_ACTIVITY_TYPES.map((t) => (
                <option key={t.type} value={t.type}>
                  {t.label}
                </option>
              ))}
            </select>
          </label>

          <div className="uni-form-grid-2">
            <label className="field-group">
              <span className="field-label">Custom title (optional)</span>
              <input
                type="text"
                className="input-control"
                value={actTitle}
                onChange={(e) => setActTitle(e.target.value)}
                placeholder={getActivityDescriptor(actType).label}
                maxLength={120}
              />
            </label>

            <label className="field-group">
              <span className="field-label">Default location (optional)</span>
              <input
                type="text"
                className="input-control"
                value={actLocation}
                onChange={(e) => setActLocation(e.target.value)}
                placeholder="e.g. Building Q, Room 102"
                maxLength={280}
              />
            </label>
          </div>

          <label className="field-group">
            <span className="field-label">Description (optional)</span>
            <textarea
              className="input-control uni-textarea"
              rows={2}
              value={actDescription}
              onChange={(e) => setActDescription(e.target.value)}
              placeholder="Requirements, submission rules, materials…"
            />
          </label>

          {actSupportsAttendance && (
            <fieldset className="uni-fieldset">
              <legend className="kicker">
                Attendance / Completion Minimum (Optional)
              </legend>
              <p className="meta-text">
                Leave blank if this activity has no attendance requirement yet.
              </p>
              <div className="uni-form-grid-3">
                <label className="field-group">
                  <span className="field-label">Min count</span>
                  <input
                    type="number"
                    min="0"
                    max="1000"
                    className="input-control mono-num"
                    value={actMinCount}
                    onChange={(e) => setActMinCount(e.target.value)}
                    placeholder="e.g. 10"
                  />
                </label>

                <label className="field-group">
                  <span className="field-label">Min rate (%)</span>
                  <input
                    type="number"
                    min="0"
                    max="100"
                    step="0.5"
                    className="input-control mono-num"
                    value={actMinPercent}
                    onChange={(e) => setActMinPercent(e.target.value)}
                    placeholder="e.g. 70"
                  />
                </label>

                <label className="field-group">
                  <span className="field-label">Planned total</span>
                  <input
                    type="number"
                    min="1"
                    max="1000"
                    className="input-control mono-num"
                    value={actPlannedTotal}
                    onChange={(e) => setActPlannedTotal(e.target.value)}
                    placeholder="e.g. 14"
                  />
                </label>
              </div>
            </fieldset>
          )}

          {actError && (
            <p className="form-error" role="alert">
              {actError}
            </p>
          )}

          <div className="uni-dialog-footer">
            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => setActivityModalOpen(false)}
            >
              Cancel
            </button>
            <button
              type="submit"
              className="btn btn-primary"
              disabled={actSaving}
            >
              {actSaving
                ? 'Saving…'
                : editingActivity
                  ? 'Save activity'
                  : 'Add activity'}
            </button>
          </div>
        </form>
      </dialog>

      {/* Item Create / Edit Modal */}
      <dialog
        ref={itemDialogRef}
        className="atrium-dialog"
        onClose={() => setItemModalOpen(false)}
        aria-labelledby="uni-item-dialog-title"
      >
        <div className="dialog-header">
          <div>
            <p className="kicker">
              {itemTargetActivity
                ? getActivityDisplayTitle(itemTargetActivity)
                : 'Activity Item'}
            </p>
            <h2 id="uni-item-dialog-title" className="dialog-title">
              {editingItem ? 'Edit item' : 'Add item'}
            </h2>
          </div>
          <button
            type="button"
            className="btn btn-ghost btn-icon"
            aria-label="Close dialog"
            onClick={() => setItemModalOpen(false)}
          >
            <CloseIcon />
          </button>
        </div>

        <form className="uni-form" onSubmit={handleSubmitItem}>
          <div className="uni-form-grid-title-seq">
            <label className="field-group">
              <span className="field-label">Title *</span>
              <input
                type="text"
                className="input-control"
                value={itemTitle}
                onChange={(e) => setItemTitle(e.target.value)}
                placeholder="e.g. Lecture 3 or Homework 1"
                maxLength={200}
                required
              />
            </label>

            <label className="field-group">
              <span className="field-label"># (optional)</span>
              <input
                type="number"
                min="0"
                max="10000"
                className="input-control mono-num"
                value={itemSequence}
                onChange={(e) => setItemSequence(e.target.value)}
                placeholder="1"
              />
            </label>
          </div>

          <label className="field-group">
            <span className="field-label">
              Theme / Topic description (optional)
            </span>
            <textarea
              className="input-control uni-textarea"
              rows={2}
              value={itemTheme}
              onChange={(e) => setItemTheme(e.target.value)}
              placeholder="Topic covered in this lecture, homework brief, exam chapters…"
            />
          </label>

          <div className="uni-form-grid-2">
            <label className="field-group">
              <span className="field-label">Attendance / Status</span>
              <select
                className="input-control"
                value={itemStatus}
                onChange={(e) =>
                  setItemStatus(e.target.value as UniversityAttendanceStatus)
                }
              >
                {ATTENDANCE_STATUSES.map((st) => (
                  <option key={st} value={st}>
                    {getAttendanceStatusLabel(
                      st,
                      itemTargetActivity?.activityType ?? 'lecture',
                    )}
                  </option>
                ))}
              </select>
            </label>

            <label className="field-group">
              <span className="field-label">Notes (optional)</span>
              <input
                type="text"
                className="input-control"
                value={itemNotes}
                onChange={(e) => setItemNotes(e.target.value)}
                placeholder="Prep checklist, reminders…"
              />
            </label>
          </div>

          <fieldset className="uni-fieldset">
            <label className="uni-checkbox-label">
              <input
                type="checkbox"
                checked={itemHasSchedule}
                onChange={(e) => setItemHasSchedule(e.target.checked)}
              />
              <span>Tie to University Calendar event</span>
            </label>

            {itemHasSchedule && (
              <div className="uni-schedule-fields">
                <label className="uni-checkbox-label">
                  <input
                    type="checkbox"
                    checked={itemIsAllDay}
                    onChange={(e) => setItemIsAllDay(e.target.checked)}
                  />
                  <span>All-day event / deadline</span>
                </label>

                <div className="uni-form-grid-2">
                  <label className="field-group">
                    <span className="field-label">Start date</span>
                    <input
                      type="date"
                      className="input-control mono-num"
                      value={itemStartDate}
                      onChange={(e) => {
                        setItemStartDate(e.target.value);
                        if (itemEndDate < e.target.value) {
                          setItemEndDate(e.target.value);
                        }
                      }}
                      required
                    />
                  </label>

                  {!itemIsAllDay && (
                    <label className="field-group">
                      <span className="field-label">Start time</span>
                      <TimeInput
                        className="input-control mono-num"
                        timeFormat={timeFormat}
                        value={itemStartTime}
                        onChange={setItemStartTime}
                        required
                      />
                    </label>
                  )}
                </div>

                <div className="uni-form-grid-2">
                  <label className="field-group">
                    <span className="field-label">End date</span>
                    <input
                      type="date"
                      className="input-control mono-num"
                      value={itemEndDate}
                      onChange={(e) => setItemEndDate(e.target.value)}
                      required
                    />
                  </label>

                  {!itemIsAllDay && (
                    <label className="field-group">
                      <span className="field-label">End time</span>
                      <TimeInput
                        className="input-control mono-num"
                        timeFormat={timeFormat}
                        value={itemEndTime}
                        onChange={setItemEndTime}
                        required
                      />
                    </label>
                  )}
                </div>

                <label className="field-group">
                  <span className="field-label">Location (optional)</span>
                  <input
                    type="text"
                    className="input-control"
                    value={itemLocation}
                    onChange={(e) => setItemLocation(e.target.value)}
                    placeholder="Room / building / link"
                    maxLength={280}
                  />
                </label>
              </div>
            )}
          </fieldset>

          {itemError && (
            <p className="form-error" role="alert">
              {itemError}
            </p>
          )}

          <div className="uni-dialog-footer">
            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => setItemModalOpen(false)}
            >
              Cancel
            </button>
            <button
              type="submit"
              className="btn btn-primary"
              disabled={itemSaving}
            >
              {itemSaving
                ? 'Saving…'
                : editingItem
                  ? 'Save changes'
                  : 'Add item'}
            </button>
          </div>
        </form>
      </dialog>

      {/* Generate Weekly Series Modal */}
      <dialog
        ref={seriesDialogRef}
        className="atrium-dialog"
        onClose={() => setSeriesModalOpen(false)}
        aria-labelledby="uni-series-dialog-title"
      >
        <div className="dialog-header">
          <div>
            <p className="kicker">Bulk Schedule Generator</p>
            <h2 id="uni-series-dialog-title" className="dialog-title">
              Generate weekly series
            </h2>
          </div>
          <button
            type="button"
            className="btn btn-ghost btn-icon"
            aria-label="Close dialog"
            onClick={() => setSeriesModalOpen(false)}
          >
            <CloseIcon />
          </button>
        </div>

        <form className="uni-form" onSubmit={handleSubmitSeries}>
          <p className="meta-text">
            Creates individual linked sessions in the University calendar so
            each week can have its own topic/theme and attendance status.
          </p>

          <div className="uni-form-grid-title-seq">
            <label className="field-group">
              <span className="field-label">Title prefix</span>
              <input
                type="text"
                className="input-control"
                value={seriesPrefix}
                onChange={(e) => setSeriesPrefix(e.target.value)}
                placeholder="Lecture"
                maxLength={140}
              />
            </label>

            <label className="field-group">
              <span className="field-label">Start #</span>
              <input
                type="number"
                min="0"
                max="10000"
                className="input-control mono-num"
                value={seriesStartNum}
                onChange={(e) => setSeriesStartNum(e.target.value)}
              />
            </label>
          </div>

          <div className="uni-form-grid-2">
            <label className="field-group">
              <span className="field-label">Number of sessions (1–52)</span>
              <input
                type="number"
                min="1"
                max="52"
                className="input-control mono-num"
                value={seriesCount}
                onChange={(e) => setSeriesCount(e.target.value)}
                required
              />
            </label>

            <label className="field-group">
              <span className="field-label">Every N weeks</span>
              <input
                type="number"
                min="1"
                max="8"
                className="input-control mono-num"
                value={seriesIntervalWeeks}
                onChange={(e) => setSeriesIntervalWeeks(e.target.value)}
                required
              />
            </label>
          </div>

          <label className="uni-checkbox-label">
            <input
              type="checkbox"
              checked={seriesIsAllDay}
              onChange={(e) => setSeriesIsAllDay(e.target.checked)}
            />
            <span>All-day deadlines</span>
          </label>

          <div className="uni-form-grid-3">
            <label className="field-group">
              <span className="field-label">First date</span>
              <input
                type="date"
                className="input-control mono-num"
                value={seriesStartDate}
                onChange={(e) => setSeriesStartDate(e.target.value)}
                required
              />
            </label>

            {!seriesIsAllDay && (
              <>
                <label className="field-group">
                  <span className="field-label">Start time</span>
                  <TimeInput
                    className="input-control mono-num"
                    timeFormat={timeFormat}
                    value={seriesStartTime}
                    onChange={setSeriesStartTime}
                    required
                  />
                </label>

                <label className="field-group">
                  <span className="field-label">End time</span>
                  <TimeInput
                    className="input-control mono-num"
                    timeFormat={timeFormat}
                    value={seriesEndTime}
                    onChange={setSeriesEndTime}
                    required
                  />
                </label>
              </>
            )}
          </div>

          <label className="field-group">
            <span className="field-label">Location (optional)</span>
            <input
              type="text"
              className="input-control"
              value={seriesLocation}
              onChange={(e) => setSeriesLocation(e.target.value)}
              placeholder="Room / building"
              maxLength={280}
            />
          </label>

          <label className="field-group">
            <span className="field-label">Default theme (optional)</span>
            <input
              type="text"
              className="input-control"
              value={seriesDefaultTheme}
              onChange={(e) => setSeriesDefaultTheme(e.target.value)}
              placeholder="You can customize each week's theme individually afterward"
            />
          </label>

          {seriesError && (
            <p className="form-error" role="alert">
              {seriesError}
            </p>
          )}

          <div className="uni-dialog-footer">
            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => setSeriesModalOpen(false)}
            >
              Cancel
            </button>
            <button
              type="submit"
              className="btn btn-primary"
              disabled={seriesSaving}
            >
              {seriesSaving ? 'Generating…' : 'Generate sessions'}
            </button>
          </div>
        </form>
      </dialog>

      <QuizGeneratorModal
        isOpen={Boolean(quizGenTarget)}
        targetLabel={quizGenTarget?.label ?? ''}
        scopeKind={quizGenTarget?.scopeKind ?? 'lecture'}
        onClose={() => setQuizGenTarget(null)}
        onSubmit={(opts) => {
          if (!quizGenTarget || !onGenerateQuiz) return;
          onGenerateQuiz(
            quizGenTarget.course,
            quizGenTarget.activity,
            quizGenTarget.item,
            opts,
          );
        }}
      />

      <QuizPlayerModal
        quiz={activeQuiz}
        loading={loadingQuiz}
        onClose={() => setActiveQuiz(null)}
        onRecordAttempt={handleRecordQuizAttempt}
        onDelete={handleDeleteQuiz}
      />
    </div>
  );
}
