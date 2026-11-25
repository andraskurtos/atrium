import { getInitials } from '../data/mockData';
import type { AppView, ThemePreference, UserProfile } from '../types/atrium';
import {
  MoonIcon,
  SearchIcon,
  SunIcon,
  SystemThemeIcon,
} from './Icons';

interface TopBarProps {
  activeView: Exclude<AppView, 'welcome'>;
  profile: UserProfile;
  theme: ThemePreference;
  onCycleTheme: () => void;
  onOpenCommandPalette: () => void;
  onNavigate: (view: AppView) => void;
}

export function TopBar({
  activeView,
  profile,
  theme,
  onCycleTheme,
  onOpenCommandPalette,
  onNavigate,
}: TopBarProps) {
  const ThemeIcon =
    theme === 'light' ? SunIcon : theme === 'dark' ? MoonIcon : SystemThemeIcon;
  const themeLabel =
    theme === 'light' ? 'Light' : theme === 'dark' ? 'Dark' : 'System';

  const initials = getInitials(profile.displayName, profile.username);

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
          <span className="breadcrumb-current">
            {activeView === 'account' ? 'Account' : 'Today'}
          </span>
        </div>

        <nav className="topbar-actions" aria-label="Global actions">
          <button
            id="topbar-welcome-btn"
            type="button"
            className="btn btn-ghost btn-compact topbar-welcome-link"
            onClick={() => onNavigate('welcome')}
          >
            Welcome
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
        </nav>
      </div>
    </header>
  );
}
