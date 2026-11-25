import type { ThemePreference } from '../types/atrium';
import {
  MoonIcon,
  SearchIcon,
  SunIcon,
  SystemThemeIcon,
} from './Icons';

interface TopBarProps {
  theme: ThemePreference;
  onCycleTheme: () => void;
  onOpenCommandPalette: () => void;
  onShowWelcome: () => void;
}

export function TopBar({
  theme,
  onCycleTheme,
  onOpenCommandPalette,
  onShowWelcome,
}: TopBarProps) {
  const ThemeIcon =
    theme === 'light' ? SunIcon : theme === 'dark' ? MoonIcon : SystemThemeIcon;
  const themeLabel =
    theme === 'light' ? 'Light' : theme === 'dark' ? 'Dark' : 'System';

  return (
    <header className="topbar">
      <div className="topbar-inner">
        <div className="topbar-left">
          <button
            id="topbar-brand-btn"
            type="button"
            className="brand-button"
            onClick={onShowWelcome}
            title="Return to Welcome screen"
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
          <span className="breadcrumb-current">Today</span>
        </div>

        <nav className="topbar-actions" aria-label="Global actions">
          <button
            id="topbar-welcome-btn"
            type="button"
            className="btn btn-ghost btn-compact topbar-welcome-link"
            onClick={onShowWelcome}
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
        </nav>
      </div>
    </header>
  );
}
