import { useState, type FormEvent } from 'react';
import {
  ApiError,
  login,
  registerWithInvite,
  setupOwner,
  type AuthSessionPayload,
} from '../lib/authClient';
import type { ApiState, ThemePreference } from '../types/atrium';
import {
  ArrowRightIcon,
  EyeIcon,
  EyeOffIcon,
  MoonIcon,
  ShieldIcon,
  SunIcon,
  SystemThemeIcon,
} from './Icons';

type AuthMode = 'setup' | 'login' | 'invite';

interface AuthScreenProps {
  initialized: boolean;
  initialInviteCode: string;
  apiState: ApiState;
  apiLabel: string;
  theme: ThemePreference;
  onCycleTheme: () => void;
  onAuthenticated: (session: AuthSessionPayload) => void;
}

export function AuthScreen({
  initialized,
  initialInviteCode,
  apiState,
  apiLabel,
  theme,
  onCycleTheme,
  onAuthenticated,
}: AuthScreenProps) {
  const [mode, setMode] = useState<AuthMode>(() => {
    if (!initialized) return 'setup';
    if (initialInviteCode) return 'invite';
    return 'login';
  });

  const [identifier, setIdentifier] = useState('');
  const [inviteCode, setInviteCode] = useState(initialInviteCode);
  const [username, setUsername] = useState('');
  const [email, setEmail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');

  const ThemeIcon =
    theme === 'light' ? SunIcon : theme === 'dark' ? MoonIcon : SystemThemeIcon;
  const themeLabel =
    theme === 'light' ? 'Light' : theme === 'dark' ? 'Dark' : 'System';

  const hasMinLength = password.length >= 12;
  const hasMixedChars =
    /[a-z]/.test(password) &&
    /[A-Z]/.test(password) &&
    /[0-9!@#$%^&*()_+\-=[\]{};':"\\|,.<>/?]/.test(password);
  const passwordsMatch = password.length > 0 && password === confirmPassword;

  const switchMode = (next: AuthMode) => {
    setError('');
    setPassword('');
    setConfirmPassword('');
    setMode(next);
  };

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setError('');

    if (mode !== 'login') {
      if (!hasMinLength || !hasMixedChars) {
        setError(
          'Use at least 12 characters with uppercase, lowercase, and a number or symbol.',
        );
        return;
      }
      if (!passwordsMatch) {
        setError('Passwords do not match.');
        return;
      }
    }

    setSubmitting(true);
    try {
      if (mode === 'setup') {
        const session = await setupOwner({
          username,
          email,
          displayName,
          password,
        });
        onAuthenticated(session);
      } else if (mode === 'invite') {
        const session = await registerWithInvite({
          inviteCode,
          username,
          email,
          displayName,
          password,
        });
        onAuthenticated(session);
      } else {
        const session = await login({ identifier, password });
        onAuthenticated(session);
      }
    } catch (err: unknown) {
      if (err instanceof ApiError) {
        setError(err.message);
      } else {
        setError('Could not reach the Atrium API. Check that the server is running.');
      }
    } finally {
      setSubmitting(false);
    }
  };

  const heading =
    mode === 'setup'
      ? 'Initialize your Atrium'
      : mode === 'invite'
        ? 'Accept household invite'
        : 'Sign in to Atrium';

  const lead =
    mode === 'setup'
      ? 'Create the sole Owner account for this self-hosted instance. Once initialized, open registration locks automatically.'
      : mode === 'invite'
        ? 'Enter your one-time invite code to create a household member account.'
        : 'Sign in with your username or email to access your private Focus Stream.';

  return (
    <div className="welcome-shell">
      <header className="welcome-topbar">
        <span className="kicker">ATRIUM · PRIVATE INSTANCE</span>
        <button
          id="auth-theme-toggle"
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
        <section className="card auth-card" aria-labelledby="auth-heading">
          <div className="welcome-brand">
            <img
              className="welcome-mark"
              src="/icon.svg"
              alt="Atrium emblem"
              width={48}
              height={48}
            />
            <div>
              <p className="kicker">
                {mode === 'setup'
                  ? 'FIRST-RUN OWNER SETUP'
                  : mode === 'invite'
                    ? 'INVITE-ONLY ACCESS'
                    : 'AUTHENTICATION'}
              </p>
              <h1 id="auth-heading" className="page-title">
                {heading}
              </h1>
            </div>
          </div>

          <p className="welcome-lead">{lead}</p>

          <form className="settings-form" onSubmit={handleSubmit}>
            {mode === 'invite' && (
              <div className="form-field">
                <label className="field-label" htmlFor="auth-invite-code">
                  Invite code
                </label>
                <input
                  id="auth-invite-code"
                  type="text"
                  className="input mono-num"
                  value={inviteCode}
                  onChange={(e) => setInviteCode(e.target.value)}
                  placeholder="inv_..."
                  autoComplete="off"
                  required
                />
              </div>
            )}

            {mode === 'login' ? (
              <div className="form-field">
                <label className="field-label" htmlFor="auth-identifier">
                  Username or email
                </label>
                <input
                  id="auth-identifier"
                  type="text"
                  className="input"
                  value={identifier}
                  onChange={(e) => setIdentifier(e.target.value)}
                  placeholder="andras or andras@atrium.local"
                  autoComplete="username"
                  required
                />
              </div>
            ) : (
              <>
                <div className="form-grid-2">
                  <div className="form-field">
                    <label className="field-label" htmlFor="auth-display-name">
                      Display name
                    </label>
                    <input
                      id="auth-display-name"
                      type="text"
                      className="input"
                      value={displayName}
                      onChange={(e) => setDisplayName(e.target.value)}
                      placeholder="e.g. András"
                      autoComplete="name"
                      maxLength={50}
                      required
                    />
                  </div>

                  <div className="form-field">
                    <label className="field-label" htmlFor="auth-username">
                      Username
                    </label>
                    <div className="input-prefix-wrap">
                      <span className="input-prefix mono-num" aria-hidden="true">
                        @
                      </span>
                      <input
                        id="auth-username"
                        type="text"
                        className="input input-with-prefix mono-num"
                        value={username}
                        onChange={(e) => setUsername(e.target.value)}
                        placeholder="andras"
                        autoComplete="username"
                        required
                      />
                    </div>
                  </div>
                </div>

                <div className="form-field">
                  <label className="field-label" htmlFor="auth-email">
                    Email address
                  </label>
                  <input
                    id="auth-email"
                    type="email"
                    className="input"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    placeholder="you@atrium.local"
                    autoComplete="email"
                    required
                  />
                </div>
              </>
            )}

            <div className="form-field">
              <div className="field-label-row">
                <label className="field-label" htmlFor="auth-password">
                  Password
                </label>
                <button
                  type="button"
                  className="btn btn-ghost btn-xs"
                  onClick={() => setShowPassword((prev) => !prev)}
                >
                  {showPassword ? <EyeOffIcon /> : <EyeIcon />}
                  <span>{showPassword ? 'Hide' : 'Show'}</span>
                </button>
              </div>
              <input
                id="auth-password"
                type={showPassword ? 'text' : 'password'}
                className="input"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
                placeholder={
                  mode === 'login' ? 'Enter your password' : 'Minimum 12 characters'
                }
                required
              />
            </div>

            {mode !== 'login' && (
              <>
                <div className="form-field">
                  <label className="field-label" htmlFor="auth-confirm-password">
                    Confirm password
                  </label>
                  <input
                    id="auth-confirm-password"
                    type={showPassword ? 'text' : 'password'}
                    className="input"
                    value={confirmPassword}
                    onChange={(e) => setConfirmPassword(e.target.value)}
                    autoComplete="new-password"
                    placeholder="Repeat password"
                    required
                  />
                </div>

                <ul className="password-rules" aria-label="Password requirements">
                  <li className={hasMinLength ? 'is-met' : ''}>
                    <span
                      className="status-dot"
                      data-state={hasMinLength ? 'ok' : 'offline'}
                      aria-hidden="true"
                    />
                    <span>At least 12 characters</span>
                  </li>
                  <li className={hasMixedChars ? 'is-met' : ''}>
                    <span
                      className="status-dot"
                      data-state={hasMixedChars ? 'ok' : 'offline'}
                      aria-hidden="true"
                    />
                    <span>Uppercase, lowercase, and number or symbol</span>
                  </li>
                  <li className={passwordsMatch ? 'is-met' : ''}>
                    <span
                      className="status-dot"
                      data-state={passwordsMatch ? 'ok' : 'offline'}
                      aria-hidden="true"
                    />
                    <span>Matches confirmation</span>
                  </li>
                </ul>
              </>
            )}

            {error && (
              <p className="field-error" role="alert">
                {error}
              </p>
            )}

            <div className="form-footer">
              <span className="status-inline meta-text">
                <ShieldIcon className="meta-icon" />
                <span>HttpOnly rotating sessions</span>
              </span>

              <button
                id="auth-submit-btn"
                type="submit"
                className="btn btn-primary"
                disabled={submitting}
              >
                <span>
                  {submitting
                    ? 'Working…'
                    : mode === 'setup'
                      ? 'Create Owner Account'
                      : mode === 'invite'
                        ? 'Create Account'
                        : 'Sign In'}
                </span>
                <ArrowRightIcon />
              </button>
            </div>
          </form>

          <div className="welcome-footer">
            <div className="status-inline" role="status" aria-live="polite">
              <span className="status-dot" data-state={apiState} aria-hidden="true" />
              <span className="meta-text">{apiLabel}</span>
            </div>

            {initialized && (
              <button
                id="auth-switch-mode-btn"
                type="button"
                className="btn btn-ghost btn-compact"
                onClick={() => switchMode(mode === 'invite' ? 'login' : 'invite')}
              >
                {mode === 'invite' ? 'Back to Sign In' : 'Have an invite code?'}
              </button>
            )}
          </div>
        </section>
      </main>
    </div>
  );
}

