import { useState, type FormEvent } from 'react';
import type { ApiState, ThemePreference } from '../types/atrium';
import {
  AgentIcon,
  ArrowRightIcon,
  CalendarIcon,
  MoonIcon,
  StreamIcon,
  SunIcon,
  SystemThemeIcon,
} from './Icons';

interface WelcomeScreenProps {
  ownerName: string;
  apiState: ApiState;
  apiLabel: string;
  theme: ThemePreference;
  todayFormatted: string;
  openTaskCount: number;
  todayEventCount: number;
  pendingProposalCount: number;
  onCycleTheme: () => void;
  onEnter: (name: string, openCalendar?: boolean) => void;
}

export function WelcomeScreen({
  ownerName,
  apiState,
  apiLabel,
  theme,
  todayFormatted,
  openTaskCount,
  todayEventCount,
  pendingProposalCount,
  onCycleTheme,
  onEnter,
}: WelcomeScreenProps) {
  const [draftName, setDraftName] = useState(ownerName);

  const handleSubmit = (event: FormEvent) => {
    event.preventDefault();
    onEnter(draftName.trim(), false);
  };

  const ThemeIcon =
    theme === 'light' ? SunIcon : theme === 'dark' ? MoonIcon : SystemThemeIcon;

  const themeLabel =
    theme === 'light' ? 'Light' : theme === 'dark' ? 'Dark' : 'System';

  return (
    <div className="welcome-shell">
      <header className="welcome-topbar">
        <span className="kicker">ATRIUM · PERSONAL HUB</span>
        <button
          id="welcome-theme-toggle"
          type="button"
          className="btn btn-ghost btn-compact"
          onClick={onCycleTheme}
          aria-label={`Color theme: ${themeLabel}. Click to cycle.`}
        >
          <ThemeIcon />
          <span>{themeLabel}</span>
        </button>
      </header>

      <main className="welcome-main">
        <section className="card welcome-card" aria-labelledby="welcome-heading">
          <div className="welcome-brand">
            <img
              className="welcome-mark"
              src="/icon.svg"
              alt="Atrium emblem"
              width={48}
              height={48}
            />
            <div>
              <p className="kicker">{todayFormatted.toUpperCase()}</p>
              <h1 id="welcome-heading" className="page-title">
                Welcome to Atrium
              </h1>
            </div>
          </div>

          <p className="welcome-lead">
            Your private, self-hosted personal instrument for daily focus, calendar
            clarity, and transparent AI agent collaboration.
          </p>

          <div className="welcome-summary-strip" aria-label="Today's snapshot">
            <div className="welcome-stat">
              <span className="mono-num welcome-stat-value">{openTaskCount}</span>
              <span className="meta-text">Active tasks</span>
            </div>
            <div className="welcome-stat">
              <span className="mono-num welcome-stat-value">{todayEventCount}</span>
              <span className="meta-text">Events today</span>
            </div>
            <div className="welcome-stat">
              <span className="mono-num welcome-stat-value">{pendingProposalCount}</span>
              <span className="meta-text">Agent proposals</span>
            </div>
          </div>

          <div className="welcome-pillars" aria-label="Core workspace pillars">
            <div className="card-soft welcome-pillar">
              <span className="welcome-pillar-icon">
                <StreamIcon />
              </span>
              <div>
                <h2 className="row-title">One Calm Focus Stream</h2>
                <p className="meta-text">
                  Capture tasks, notes, and schedules from a single composer without
                  widget clutter.
                </p>
              </div>
            </div>

            <div className="card-soft welcome-pillar">
              <span className="welcome-pillar-icon">
                <CalendarIcon />
              </span>
              <div>
                <h2 className="row-title">Context Rail &amp; Calendar</h2>
                <p className="meta-text">
                  Browse dates, inspect upcoming blocks, and add events right beside
                  your daily stream.
                </p>
              </div>
            </div>

            <div className="card-soft welcome-pillar">
              <span className="welcome-pillar-icon">
                <AgentIcon />
              </span>
              <div>
                <h2 className="row-title">Human-in-the-Loop MCP Agents</h2>
                <p className="meta-text">
                  Every agent suggestion is attributed and waits for your explicit
                  approval before changing records.
                </p>
              </div>
            </div>
          </div>

          <form className="welcome-form" onSubmit={handleSubmit}>
            <label className="field-label" htmlFor="welcome-name-input">
              Personalize your greeting <span className="text-faint">(optional)</span>
            </label>
            <div className="welcome-form-row">
              <input
                id="welcome-name-input"
                type="text"
                className="input"
                value={draftName}
                onChange={(e) => setDraftName(e.target.value)}
                placeholder="e.g. András"
                autoComplete="given-name"
                maxLength={40}
              />
              <button
                id="welcome-enter-btn"
                type="submit"
                className="btn btn-primary"
              >
                <span>Open Dashboard</span>
                <ArrowRightIcon />
              </button>
            </div>
          </form>

          <div className="welcome-footer">
            <div className="status-inline" role="status" aria-live="polite">
              <span className="status-dot" data-state={apiState} aria-hidden="true" />
              <span className="meta-text">{apiLabel}</span>
            </div>

            <button
              id="welcome-calendar-btn"
              type="button"
              className="btn btn-ghost btn-compact"
              onClick={() => onEnter(draftName.trim(), true)}
            >
              <CalendarIcon />
              <span>Jump to Calendar</span>
            </button>
          </div>
        </section>
      </main>
    </div>
  );
}
