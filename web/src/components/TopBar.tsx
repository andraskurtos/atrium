import { getInitials } from '../data/mockData';
import type { AppView, ThemePreference, UserProfile } from '../types/atrium';
import {
  GraduationCapIcon,
  MoonIcon,
  NoteIcon,
  SearchIcon,
  SunIcon,
  SystemThemeIcon,
} from './Icons';

interface TopBarProps {
  activeView: AppView;
  profile: UserProfile;
  theme: ThemePreference;
  onCycleTheme: () => void;
  onOpenCommandPalette: () => void;
  onNavigate: (view: AppView) => void;
  onSignOut: () => void;
}

export function TopBar({
  activeView,
  profile,
  theme,
  onCycleTheme,
  onOpenCommandPalette,
  onNavigate,
  onSignOut,
}: TopBarProps) {
  const ThemeIcon =
    theme === 'light' ? SunIcon : theme === 'dark' ? MoonIcon : SystemThemeIcon;
  const themeLabel =
    theme === 'light' ? 'Light' : theme === 'dark' ? 'Dark' : 'System';

  const initials = getInitials(profile.displayName, profile.username);
  const breadcrumbLabel =
    activeView === 'account'
      ? 'Account'
      : activeView === 'university'
        ? 'University'
        : activeView === 'notes'
          ? 'Notes'
          : 'Today';

  return (
    <header className="topbar">
      <div className="topbar-inner">
        <div className="topbar-left">
          <button
            id="topbar-brand-btn"
            type="button"
            className="brand-button"
            onClick={() => onNavigate('dashboard')}
            title="Go to Today's Dashboard"
          >
            <img
              src="/icon.svg"
              alt=""
              width={24}
              height={24}
              className="brand-mark"
            />
            <span className="brand-name">Atrium</span>
          </button>
          <span className="breadcrumb-sep" aria-hidden="true">
            /
          </span>
          <span className="breadcrumb-current">{breadcrumbLabel}</span>
        </div>

        <nav className="topbar-actions" aria-label="Global actions">
          <button
            id="topbar-university-btn"
            type="button"
            className={`btn btn-compact topbar-nav-pill ${
              activeView === 'university' ? 'is-active' : 'btn-ghost'
            }`}
            onClick={() =>
              onNavigate(activeView === 'university' ? 'dashboard' : 'university')
            }
            aria-current={activeView === 'university' ? 'page' : undefined}
            title="University Courses & Calendar"
          >
            <GraduationCapIcon />
            <span className="topbar-nav-pill-label">University</span>
          </button>

          <button
            id="topbar-notes-btn"
            type="button"
            className={`btn btn-compact topbar-nav-pill ${
              activeView === 'notes' ? 'is-active' : 'btn-ghost'
            }`}
            onClick={() =>
              onNavigate(activeView === 'notes' ? 'dashboard' : 'notes')
            }
            aria-current={activeView === 'notes' ? 'page' : undefined}
            title="Markdown Notes Editor"
          >
            <NoteIcon />
            <span className="topbar-nav-pill-label">Notes</span>
          </button>

          <button
            id="topbar-command-btn"
            type="button"
            className="btn btn-secondary btn-compact command-trigger"
            onClick={onOpenCommandPalette}
            aria-label="Open command palette"
          >
            <SearchIcon />
            <span className="command-trigger-label">Command</span>
            <kbd className="kbd mono-num">⌘K</kbd>
          </button>

          <button
            id="topbar-theme-btn"
            type="button"
            className="btn btn-ghost btn-icon"
            onClick={onCycleTheme}
            aria-label={`Theme: ${themeLabel}. Switch theme.`}
            title={`Theme: ${themeLabel}`}
          >
            <ThemeIcon />
          </button>

          <button
            id="topbar-account-btn"
            type="button"
            className={`account-pill-btn ${
              activeView === 'account' ? 'is-active' : ''
            }`}
            onClick={() =>
              onNavigate(activeView === 'account' ? 'dashboard' : 'account')
            }
            aria-current={activeView === 'account' ? 'page' : undefined}
            aria-label={
              activeView === 'account'
                ? 'Back to Today dashboard'
                : 'Open Account & Settings'
            }
            title="Account & Settings"
          >
            <span className="avatar-sm" aria-hidden="true">
              {profile.avatarDataUrl ? (
                <img src={profile.avatarDataUrl} alt="" className="avatar-img" />
              ) : (
                <span className="avatar-initials">{initials}</span>
              )}
            </span>
            <span className="account-pill-label">Account</span>
          </button>

          <button
            id="topbar-signout-btn"
            type="button"
            className="btn btn-ghost btn-compact topbar-welcome-link"
            onClick={onSignOut}
          >
            Sign out
          </button>
        </nav>
      </div>
    </header>
  );
}
