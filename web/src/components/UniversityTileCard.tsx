import { useMemo } from 'react';
import { formatShortDate, parseDateKey, toDateKey } from '../data/mockData';
import {
  flattenScheduledUniversityItems,
  formatScheduledTimeLabel,
  getActivityDescriptor,
} from '../lib/universityClient';
import type { UniversityCourse } from '../types/atrium';
import { ArrowRightIcon, GraduationCapIcon } from './Icons';

interface UniversityTileCardProps {
  courses: UniversityCourse[];
  loading: boolean;
  timeFormat?: '24h' | '12h';
  onOpenUniversity: (courseId?: string) => void;
}

export function UniversityTileCard({
  courses,
  loading,
  timeFormat = '24h',
  onOpenUniversity,
}: UniversityTileCardProps) {
  const activeCourses = useMemo(
    () => courses.filter((c) => c.status === 'active'),
    [courses],
  );

  const upcomingEvents = useMemo(() => {
    const nowMs = Date.now();
    return flattenScheduledUniversityItems(activeCourses)
      .filter((entry) => {
        const endMs = new Date(
          entry.item.endAt ?? entry.item.startAt ?? 0,
        ).getTime();
        return endMs >= nowMs;
      })
      .slice(0, 3);
  }, [activeCourses]);

  const attendanceStats = useMemo(() => {
    let met = 0;
    let remainingActivities = 0;
    let atRisk = 0;
    let totalRemainingSessions = 0;

    for (const course of activeCourses) {
      for (const activity of course.activities) {
        const a = activity.attendance;
        if (!a.supportsAttendance || !a.hasRequirement) continue;
        if (a.status === 'met') {
          met += 1;
        } else {
          remainingActivities += 1;
          totalRemainingSessions += a.remainingRequired ?? 0;
          if (a.status === 'at_risk' || a.status === 'impossible') {
            atRisk += 1;
          }
        }
      }
    }

    return { met, remainingActivities, atRisk, totalRemainingSessions };
  }, [activeCourses]);

  const todayKey = toDateKey(new Date());

  return (
    <section
      className="card university-tile-card"
      aria-labelledby="university-tile-heading"
    >
      <div className="card-header">
        <div className="card-header-group">
          <GraduationCapIcon className="university-tile-icon" />
          <h2 id="university-tile-heading" className="card-title">
            University
          </h2>
          {activeCourses.length > 0 && (
            <span className="pill-neutral mono-num">{activeCourses.length}</span>
          )}
        </div>
        <button
          id="dashboard-open-university-btn"
          type="button"
          className="btn btn-ghost btn-compact"
          onClick={() => onOpenUniversity()}
        >
          <span>Open</span>
          <ArrowRightIcon />
        </button>
      </div>

      {loading && courses.length === 0 ? (
        <p className="meta-text">Loading university schedule…</p>
      ) : activeCourses.length === 0 ? (
        <div className="university-tile-empty">
          <p className="meta-text">
            Track courses, lectures, labs, assignments, attendance minimums, and
            your university calendar.
          </p>
          <button
            type="button"
            className="btn btn-secondary btn-compact"
            onClick={() => onOpenUniversity()}
          >
            Set up courses
          </button>
        </div>
      ) : (
        <div className="stack-sm">
          {(attendanceStats.met > 0 ||
            attendanceStats.remainingActivities > 0) && (
            <div className="university-tile-attendance-row">
              {attendanceStats.atRisk > 0 ? (
                <span className="uni-status-pill is-warn">
                  <span className="mono-num">{attendanceStats.atRisk}</span>{' '}
                  {attendanceStats.atRisk === 1 ? 'activity' : 'activities'} at
                  risk
                </span>
              ) : attendanceStats.remainingActivities > 0 ? (
                <span className="pill-neutral">
                  <span className="mono-num">
                    {attendanceStats.totalRemainingSessions}
                  </span>{' '}
                  required sessions left
                </span>
              ) : (
                <span className="uni-status-pill is-ok">
                  All {attendanceStats.met} requirements met
                </span>
              )}
            </div>
          )}

          {upcomingEvents.length > 0 ? (
            <div className="stack-sm">
              {upcomingEvents.map(({ item, activity, course }) => {
                const startDate = new Date(item.startAt ?? 0);
                const dateKey = toDateKey(startDate);
                const dateLabel =
                  dateKey === todayKey
                    ? 'Today'
                    : formatShortDate(parseDateKey(dateKey));
                const timeLabel = formatScheduledTimeLabel(
                  item.startAt ?? '',
                  item.endAt,
                  item.isAllDay,
                  timeFormat,
                );
                const descriptor = getActivityDescriptor(activity.activityType);

                return (
                  <button
                    key={item.id}
                    type="button"
                    className="card-soft university-tile-event"
                    onClick={() => onOpenUniversity(course.id)}
                  >
                    <span
                      className="uni-color-bar"
                      style={{ backgroundColor: course.color }}
                      aria-hidden="true"
                    />
                    <div className="university-tile-event-body">
                      <div className="university-tile-event-top">
                        <span className="row-title">
                          {course.code ? `${course.code} · ` : ''}
                          {item.title}
                        </span>
                        <span className="pill-neutral">
                          {descriptor.shortLabel}
                        </span>
                      </div>
                      <span className="meta-text mono-num">
                        {dateLabel} · {timeLabel}
                        {item.location ? ` · ${item.location}` : ''}
                      </span>
                      {item.theme && (
                        <span className="meta-text university-tile-theme">
                          {item.theme}
                        </span>
                      )}
                    </div>
                  </button>
                );
              })}
            </div>
          ) : (
            <p className="meta-text">
              <span className="mono-num">{activeCourses.length}</span> active{' '}
              {activeCourses.length === 1 ? 'course' : 'courses'} · No upcoming
              scheduled classes yet.
            </p>
          )}
        </div>
      )}
    </section>
  );
}
