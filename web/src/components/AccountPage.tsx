import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react';
import { getInitials } from '../data/mockData';
import type {
  AccountPreferences,
  AgentToken,
  ApiState,
  PasskeyItem,
  SessionItem,
  ThemePreference,
  UserProfile,
} from '../types/atrium';
import {
  ArrowLeftIcon,
  CloseIcon,
  CopyIcon,
  DownloadIcon,
  EyeIcon,
  EyeOffIcon,
  KeyIcon,
  PlusIcon,
  ShieldIcon,
  UploadIcon,
} from './Icons';
import { LocalStatusCard } from './LocalStatusCard';

interface AccountPageProps {
  profile: UserProfile;
  preferences: AccountPreferences;
  theme: ThemePreference;
  passkeys: PasskeyItem[];
  agentTokens: AgentToken[];
  sessions: SessionItem[];
  apiState: ApiState;
  apiLabel: string;
  lastChecked: string;
  onBackToDashboard: () => void;
  onUpdateProfile: (next: UserProfile) => void;
  onUpdatePreferences: (next: AccountPreferences) => void;
  onSetTheme: (theme: ThemePreference) => void;
  onAddPasskey: (deviceName: string) => void;
  onRevokePasskey: (id: string) => void;
  onCreateToken: (name: string, scopes: string[]) => string;
  onRevokeToken: (id: string) => void;
  onRevokeSession: (id: string) => void;
  onSignOutOtherSessions: () => void;
  onExportData: () => void;
  onResetWorkspace: () => void;
  onRefreshHealth: () => void;
  onAnnounce: (message: string) => void;
}

const COMMON_TIMEZONES = [
  'UTC',
  'America/Los_Angeles',
  'America/Denver',
  'America/Chicago',
  'America/New_York',
  'Europe/London',
  'Europe/Berlin',
  'Europe/Budapest',
  'Europe/Zurich',
  'Asia/Tokyo',
  'Asia/Singapore',
  'Australia/Sydney',
];

const AVAILABLE_SCOPES = [
  'calendar:read',
  'calendar:propose',
  'tasks:read',
  'tasks:propose',
  'vault:read',
];

export function AccountPage({
  profile,
  preferences,
  theme,
  passkeys,
  agentTokens,
  sessions,
  apiState,
  apiLabel,
  lastChecked,
  onBackToDashboard,
  onUpdateProfile,
  onUpdatePreferences,
  onSetTheme,
  onAddPasskey,
  onRevokePasskey,
  onCreateToken,
  onRevokeToken,
  onRevokeSession,
  onSignOutOtherSessions,
  onExportData,
  onResetWorkspace,
  onRefreshHealth,
  onAnnounce,
}: AccountPageProps) {
  // Profile draft state
  const [displayName, setDisplayName] = useState(profile.displayName);
  const [username, setUsername] = useState(profile.username);
  const [email, setEmail] = useState(profile.email);
  const [bio, setBio] = useState(profile.bio);
  const [timezone, setTimezone] = useState(profile.timezone);
  const [avatarError, setAvatarError] = useState('');
  const [profileError, setProfileError] = useState('');
  const fileInputRef = useRef<HTMLInputElement | null>(null);

  // Sync draft when profile resets externally
  useEffect(() => {
    setDisplayName(profile.displayName);
    setUsername(profile.username);
    setEmail(profile.email);
    setBio(profile.bio);
    setTimezone(profile.timezone);
  }, [profile]);

  // Password form state
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [showPasswords, setShowPasswords] = useState(false);
  const [passwordError, setPasswordError] = useState('');

  // Passkey inline form
  const [showPasskeyForm, setShowPasskeyForm] = useState(false);
  const [newPasskeyName, setNewPasskeyName] = useState('');

  // Scoped MCP token inline form
  const [showTokenForm, setShowTokenForm] = useState(false);
  const [newTokenName, setNewTokenName] = useState('');
  const [selectedScopes, setSelectedScopes] = useState<string[]>([
    'calendar:read',
    'calendar:propose',
  ]);
  const [createdSecret, setCreatedSecret] = useState<string | null>(null);

  // Destructive confirmation dialog
  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false);
  const [deletePhrase, setDeletePhrase] = useState('');
  const deleteDialogRef = useRef<HTMLDialogElement | null>(null);

  useEffect(() => {
    const dialog = deleteDialogRef.current;
    if (!dialog) return;
    if (confirmDeleteOpen && !dialog.open) {
      setDeletePhrase('');
      dialog.showModal();
    } else if (!confirmDeleteOpen && dialog.open) {
      dialog.close();
    }
  }, [confirmDeleteOpen]);

  const timezoneOptions = COMMON_TIMEZONES.includes(timezone)
    ? COMMON_TIMEZONES
    : [timezone, ...COMMON_TIMEZONES];

  const initials = getInitials(
    displayName || profile.displayName,
    username || profile.username,
  );

  const isProfileDirty =
    displayName !== profile.displayName ||
    username !== profile.username ||
    email !== profile.email ||
    bio !== profile.bio ||
    timezone !== profile.timezone;

  const hasMinLength = newPassword.length >= 12;
  const hasMixedChars =
    /[a-z]/.test(newPassword) &&
    /[A-Z]/.test(newPassword) &&
    /[0-9!@#$%^&*()_+\-=[\]{};':"\\|,.<>/?]/.test(newPassword);
  const passwordsMatch =
    newPassword.length > 0 && newPassword === confirmPassword;

  const handleAvatarChange = (event: ChangeEvent<HTMLInputElement>) => {
    setAvatarError('');
    const file = event.target.files?.[0];
    if (!file) return;

    if (!file.type.startsWith('image/')) {
      setAvatarError('Please choose a PNG, JPG, WebP, or SVG image.');
      return;
    }
    if (file.size > 2 * 1024 * 1024) {
      setAvatarError('Image must be 2 MB or smaller.');
      return;
    }

    const reader = new FileReader();
    reader.onload = () => {
      if (typeof reader.result === 'string') {
        onUpdateProfile({
          ...profile,
          avatarDataUrl: reader.result,
        });
        onAnnounce('Profile photo updated');
      }
    };
    reader.readAsDataURL(file);
    event.target.value = '';
  };

  const handleRemoveAvatar = () => {
    setAvatarError('');
    onUpdateProfile({
      ...profile,
      avatarDataUrl: null,
    });
    onAnnounce('Profile photo removed');
  };

  const handleProfileSubmit = (event: FormEvent) => {
    event.preventDefault();
    setProfileError('');

    const cleanUsername = username.trim().replace(/^@/, '').toLowerCase();
    const cleanEmail = email.trim();

    if (!/^[a-z0-9._-]{2,32}$/.test(cleanUsername)) {
      setProfileError(
        'Username must be 2–32 characters using letters, numbers, dots, underscores, or hyphens.',
      );
      return;
    }
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(cleanEmail)) {
      setProfileError('Please enter a valid email address.');
      return;
    }

    onUpdateProfile({
      ...profile,
      displayName: displayName.trim(),
      username: cleanUsername,
      email: cleanEmail,
      bio: bio.trim(),
      timezone,
    });
    onAnnounce('Account profile saved');
  };

  const handlePasswordSubmit = (event: FormEvent) => {
    event.preventDefault();
    setPasswordError('');

    if (!currentPassword.trim()) {
      setPasswordError('Enter your current password to continue.');
      return;
    }
    if (!hasMinLength || !hasMixedChars) {
      setPasswordError(
        'Use at least 12 characters with uppercase, lowercase, and a number or symbol.',
      );
      return;
    }
    if (!passwordsMatch) {
      setPasswordError('New password and confirmation do not match.');
      return;
    }

    onUpdateProfile({
      ...profile,
      passwordUpdatedAt: 'Just now',
    });
    setCurrentPassword('');
    setNewPassword('');
    setConfirmPassword('');
    onAnnounce('Password updated');
  };

  const handlePasskeySubmit = (event: FormEvent) => {
    event.preventDefault();
    const trimmed = newPasskeyName.trim();
    if (!trimmed) return;
    onAddPasskey(trimmed);
    setNewPasskeyName('');
    setShowPasskeyForm(false);
  };

  const toggleScope = (scope: string) => {
    setSelectedScopes((prev) =>
      prev.includes(scope)
        ? prev.filter((item) => item !== scope)
        : [...prev, scope],
    );
  };

  const handleCreateTokenSubmit = (event: FormEvent) => {
    event.preventDefault();
    const trimmed = newTokenName.trim();
    if (!trimmed || selectedScopes.length === 0) return;
    const secret = onCreateToken(trimmed, selectedScopes);
    setCreatedSecret(secret);
    setNewTokenName('');
    setShowTokenForm(false);
  };

  const handleCopySecret = async () => {
    if (!createdSecret) return;
    try {
      await navigator.clipboard.writeText(createdSecret);
      onAnnounce('Scoped token copied to clipboard');
    } catch {
      onAnnounce('Copy manually from the token box');
    }
  };

  const handleConfirmDelete = (event: FormEvent) => {
    event.preventDefault();
    if (deletePhrase.trim().toLowerCase() !== 'reset atrium') return;
    setConfirmDeleteOpen(false);
    onResetWorkspace();
  };

  const otherSessionCount = sessions.filter((s) => !s.isCurrent).length;

  return (
    <div className="dashboard-container account-container">
      <main className="focus-stream">
        <header className="stream-header">
          <div className="account-back-row">
            <button
              id="account-back-btn"
              type="button"
              className="btn btn-ghost btn-compact"
              onClick={onBackToDashboard}
            >
              <ArrowLeftIcon />
              <span>Back to Today</span>
            </button>
          </div>
          <p className="kicker">ATRIUM · ACCOUNT &amp; SECURITY</p>
          <h1 className="page-title">Account &amp; Settings</h1>
          <p className="stream-summary">
            Manage your profile identity, password, passkeys, workspace preferences,
            and scoped MCP agent credentials.
          </p>
        </header>

        {/* 1. Profile & Identity */}
        <section className="card" aria-labelledby="profile-heading">
          <div className="card-header">
            <h2 id="profile-heading" className="card-title">
              Profile &amp; Identity
            </h2>
            <span className="pill-accent">Owner</span>
          </div>

          <div className="avatar-row">
            <div className="avatar-lg" aria-hidden="true">
              {profile.avatarDataUrl ? (
                <img src={profile.avatarDataUrl} alt="" className="avatar-img" />
              ) : (
                <span className="avatar-initials">{initials}</span>
              )}
            </div>

            <div className="avatar-controls">
              <p className="row-title">Profile picture</p>
              <p className="meta-text">
                Stored locally in your Atrium instance. PNG, JPG, or WebP up to 2 MB.
              </p>
              <div className="avatar-actions">
                <input
                  ref={fileInputRef}
                  id="account-avatar-input"
                  type="file"
                  accept="image/*"
                  className="sr-only"
                  onChange={handleAvatarChange}
                />
                <button
                  id="account-upload-avatar-btn"
                  type="button"
                  className="btn btn-secondary btn-compact"
                  onClick={() => fileInputRef.current?.click()}
                >
                  <UploadIcon />
                  <span>Upload photo</span>
                </button>
                {profile.avatarDataUrl && (
                  <button
                    id="account-remove-avatar-btn"
                    type="button"
                    className="btn btn-ghost btn-compact"
                    onClick={handleRemoveAvatar}
                  >
                    Remove
                  </button>
                )}
              </div>
              {avatarError && (
                <p className="field-error" role="alert">
                  {avatarError}
                </p>
              )}
            </div>
          </div>

          <form className="settings-form" onSubmit={handleProfileSubmit}>
            <div className="form-grid-2">
              <div className="form-field">
                <label className="field-label" htmlFor="account-display-name">
                  Display name
                </label>
                <input
                  id="account-display-name"
                  type="text"
                  className="input"
                  value={displayName}
                  onChange={(e) => setDisplayName(e.target.value)}
                  placeholder="e.g. András"
                  maxLength={50}
                />
              </div>

              <div className="form-field">
                <label className="field-label" htmlFor="account-username">
                  Username
                </label>
                <div className="input-prefix-wrap">
                  <span className="input-prefix mono-num" aria-hidden="true">
                    @
                  </span>
                  <input
                    id="account-username"
                    type="text"
                    className="input input-with-prefix mono-num"
                    value={username}
                    onChange={(e) => setUsername(e.target.value)}
                    placeholder="owner"
                    autoComplete="username"
                    required
                  />
                </div>
              </div>

              <div className="form-field">
                <label className="field-label" htmlFor="account-email">
                  Email address
                </label>
                <input
                  id="account-email"
                  type="email"
                  className="input"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="you@atrium.local"
                  autoComplete="email"
                  required
                />
              </div>

              <div className="form-field">
                <label className="field-label" htmlFor="account-timezone">
                  Timezone
                </label>
                <select
                  id="account-timezone"
                  className="input"
                  value={timezone}
                  onChange={(e) => setTimezone(e.target.value)}
                >
                  {timezoneOptions.map((tz) => (
                    <option key={tz} value={tz}>
                      {tz}
                    </option>
                  ))}
                </select>
              </div>
            </div>

            <div className="form-field">
              <label className="field-label" htmlFor="account-bio">
                Short bio / workspace role
              </label>
              <input
                id="account-bio"
                type="text"
                className="input"
                value={bio}
                onChange={(e) => setBio(e.target.value)}
                placeholder="Personal workspace owner"
                maxLength={120}
              />
            </div>

            {profileError && (
              <p className="field-error" role="alert">
                {profileError}
              </p>
            )}

            <div className="form-footer">
              <span className="meta-text">
                {isProfileDirty ? 'Unsaved profile changes' : 'All changes saved'}
              </span>
              <button
                id="account-save-profile-btn"
                type="submit"
                className="btn btn-primary"
                disabled={!isProfileDirty}
              >
                Save Profile
              </button>
            </div>
          </form>
        </section>

        {/* 2. Password & Passkeys */}
        <section className="card" aria-labelledby="security-heading">
          <div className="card-header">
            <div className="card-header-group">
              <h2 id="security-heading" className="card-title">
                Password &amp; Passkeys
              </h2>
            </div>
            <span className="meta-text mono-num">
              Updated {profile.passwordUpdatedAt}
            </span>
          </div>

          <form className="settings-form" onSubmit={handlePasswordSubmit}>
            <div className="form-field">
              <div className="field-label-row">
                <label className="field-label" htmlFor="account-current-password">
                  Current password
                </label>
                <button
                  id="account-toggle-password-visibility"
                  type="button"
                  className="btn btn-ghost btn-xs"
                  onClick={() => setShowPasswords((prev) => !prev)}
                >
                  {showPasswords ? <EyeOffIcon /> : <EyeIcon />}
                  <span>{showPasswords ? 'Hide' : 'Show'}</span>
                </button>
              </div>
              <input
                id="account-current-password"
                type={showPasswords ? 'text' : 'password'}
                className="input"
                value={currentPassword}
                onChange={(e) => setCurrentPassword(e.target.value)}
                autoComplete="current-password"
                placeholder="Enter current password"
              />
            </div>

            <div className="form-grid-2">
              <div className="form-field">
                <label className="field-label" htmlFor="account-new-password">
                  New password
                </label>
                <input
                  id="account-new-password"
                  type={showPasswords ? 'text' : 'password'}
                  className="input"
                  value={newPassword}
                  onChange={(e) => setNewPassword(e.target.value)}
                  autoComplete="new-password"
                  placeholder="Minimum 12 characters"
                />
              </div>

              <div className="form-field">
                <label className="field-label" htmlFor="account-confirm-password">
                  Confirm new password
                </label>
                <input
                  id="account-confirm-password"
                  type={showPasswords ? 'text' : 'password'}
                  className="input"
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)}
                  autoComplete="new-password"
                  placeholder="Repeat new password"
                />
              </div>
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

            {passwordError && (
              <p className="field-error" role="alert">
                {passwordError}
              </p>
            )}

            <div className="form-footer">
              <span className="meta-text">
                Use a passphrase or password manager for local vault security.
              </span>
              <button
                id="account-update-password-btn"
                type="submit"
                className="btn btn-primary"
              >
                Update Password
              </button>
            </div>
          </form>

          <div className="subsection-divider">
            <div className="card-header">
              <div className="card-header-group">
                <ShieldIcon className="meta-icon" />
                <h3 className="row-title">Passkeys (WebAuthn)</h3>
              </div>
              <button
                id="account-add-passkey-toggle"
                type="button"
                className="btn btn-secondary btn-xs"
                onClick={() => setShowPasskeyForm((prev) => !prev)}
                aria-expanded={showPasskeyForm}
              >
                <PlusIcon />
                <span>{showPasskeyForm ? 'Cancel' : 'Register Passkey'}</span>
              </button>
            </div>

            {showPasskeyForm && (
              <form
                className="card-soft inline-create-row"
                onSubmit={handlePasskeySubmit}
              >
                <input
                  id="account-passkey-name-input"
                  type="text"
                  className="input"
                  value={newPasskeyName}
                  onChange={(e) => setNewPasskeyName(e.target.value)}
                  placeholder="Device label (e.g. YubiKey 5C)"
                  required
                />
                <button
                  id="account-passkey-submit"
                  type="submit"
                  className="btn btn-primary btn-compact"
                >
                  Add
                </button>
              </form>
            )}

            <div className="stack-sm">
              {passkeys.map((pk) => (
                <div key={pk.id} className="card-soft setting-item-row">
                  <div className="setting-item-body">
                    <span className="row-title">{pk.deviceName}</span>
                    <span className="meta-text">
                      Added {pk.addedAt} · Last used {pk.lastUsed}
                    </span>
                  </div>
                  <button
                    type="button"
                    className="btn btn-ghost btn-xs"
                    onClick={() => onRevokePasskey(pk.id)}
                    disabled={passkeys.length <= 1}
                    title={
                      passkeys.length <= 1
                        ? 'Keep at least one passkey registered'
                        : 'Revoke passkey'
                    }
                  >
                    Revoke
                  </button>
                </div>
              ))}
            </div>
          </div>
        </section>

        {/* 3. Preferences & Notifications */}
        <section className="card" aria-labelledby="preferences-heading">
          <div className="card-header">
            <h2 id="preferences-heading" className="card-title">
              Preferences &amp; Notifications
            </h2>
          </div>

          <div className="preferences-stack">
            <div className="form-field">
              <span className="field-label">Color theme</span>
              <div
                className="segmented-group"
                role="radiogroup"
                aria-label="Color theme"
              >
                {(
                  [
                    { id: 'system', label: 'System' },
                    { id: 'light', label: 'Light · Warm Linen' },
                    { id: 'dark', label: 'Dark · Warm Obsidian' },
                  ] as const
                ).map((option) => (
                  <button
                    key={option.id}
                    id={`pref-theme-${option.id}`}
                    type="button"
                    role="radio"
                    aria-checked={theme === option.id}
                    className={`segmented-btn ${
                      theme === option.id ? 'is-active' : ''
                    }`}
                    onClick={() => {
                      onSetTheme(option.id);
                      onAnnounce(`Theme set to ${option.label}`);
                    }}
                  >
                    {option.label}
                  </button>
                ))}
              </div>
            </div>

            <div className="form-grid-2">
              <div className="form-field">
                <label className="field-label" htmlFor="pref-week-start">
                  Calendar week starts on
                </label>
                <select
                  id="pref-week-start"
                  className="input"
                  value={preferences.weekStartsOn}
                  onChange={(e) => {
                    const value = e.target.value as 'monday' | 'sunday';
                    onUpdatePreferences({ ...preferences, weekStartsOn: value });
                    onAnnounce(
                      `Calendar week now starts on ${
                        value === 'monday' ? 'Monday' : 'Sunday'
                      }`,
                    );
                  }}
                >
                  <option value="monday">Monday (Swiss / ISO)</option>
                  <option value="sunday">Sunday</option>
                </select>
              </div>

              <div className="form-field">
                <label className="field-label" htmlFor="pref-time-format">
                  Time display format
                </label>
                <select
                  id="pref-time-format"
                  className="input"
                  value={preferences.timeFormat}
                  onChange={(e) => {
                    const value = e.target.value as '24h' | '12h';
                    onUpdatePreferences({ ...preferences, timeFormat: value });
                    onAnnounce(`Time format set to ${value}`);
                  }}
                >
                  <option value="24h">24-hour (14:30)</option>
                  <option value="12h">12-hour (2:30 PM)</option>
                </select>
              </div>
            </div>

            <ul className="hairline-list">
              <li className="checklist-row">
                <label className="checklist-label">
                  <input
                    id="pref-agent-approval"
                    type="checkbox"
                    className="native-checkbox"
                    checked={preferences.requireAgentApproval}
                    onChange={(e) =>
                      onUpdatePreferences({
                        ...preferences,
                        requireAgentApproval: e.target.checked,
                      })
                    }
                  />
                  <span className="checklist-content">
                    <span className="checklist-title">
                      Require manual approval for MCP agent mutations
                    </span>
                    <span className="meta-text">
                      Keep agent-proposed calendar blocks and tasks in Agent Proposals
                      until explicitly approved.
                    </span>
                  </span>
                </label>
              </li>

              <li className="checklist-row">
                <label className="checklist-label">
                  <input
                    id="pref-morning-brief"
                    type="checkbox"
                    className="native-checkbox"
                    checked={preferences.morningBrief}
                    onChange={(e) =>
                      onUpdatePreferences({
                        ...preferences,
                        morningBrief: e.target.checked,
                      })
                    }
                  />
                  <span className="checklist-content">
                    <span className="checklist-title">Daily Focus Stream brief</span>
                    <span className="meta-text">
                      Summarize open tasks, today&apos;s schedule, and pending proposals
                      at the top of your stream.
                    </span>
                  </span>
                </label>
              </li>

              <li className="checklist-row">
                <label className="checklist-label">
                  <input
                    id="pref-event-reminders"
                    type="checkbox"
                    className="native-checkbox"
                    checked={preferences.eventReminders}
                    onChange={(e) =>
                      onUpdatePreferences({
                        ...preferences,
                        eventReminders: e.target.checked,
                      })
                    }
                  />
                  <span className="checklist-content">
                    <span className="checklist-title">
                      Calendar reminders (15 minutes prior)
                    </span>
                    <span className="meta-text">
                      Show native PWA notifications before scheduled focus blocks and
                      meetings.
                    </span>
                  </span>
                </label>
              </li>
            </ul>
          </div>
        </section>

        {/* 4. Scoped MCP Agent Tokens */}
        <section className="card" aria-labelledby="tokens-heading">
          <div className="card-header">
            <div className="card-header-group">
              <h2 id="tokens-heading" className="card-title">
                Scoped MCP Agent Tokens
              </h2>
              <span className="pill-accent">MCP</span>
            </div>
            <button
              id="account-create-token-toggle"
              type="button"
              className="btn btn-secondary btn-xs"
              onClick={() => setShowTokenForm((prev) => !prev)}
              aria-expanded={showTokenForm}
            >
              <PlusIcon />
              <span>{showTokenForm ? 'Cancel' : 'Create Token'}</span>
            </button>
          </div>

          {createdSecret && (
            <div className="card-soft token-reveal-box" role="status">
              <div className="token-reveal-header">
                <span className="status-inline">
                  <span className="status-dot" data-state="ok" aria-hidden="true" />
                  <span className="row-title">New token created — copy now</span>
                </span>
                <button
                  type="button"
                  className="btn btn-ghost btn-icon-sm"
                  onClick={() => setCreatedSecret(null)}
                  aria-label="Dismiss new token banner"
                >
                  <CloseIcon />
                </button>
              </div>
              <div className="token-reveal-value">
                <code className="mono-num">{createdSecret}</code>
                <button
                  type="button"
                  className="btn btn-secondary btn-xs"
                  onClick={handleCopySecret}
                >
                  <CopyIcon />
                  <span>Copy</span>
                </button>
              </div>
            </div>
          )}

          {showTokenForm && (
            <form
              className="card-soft token-create-form"
              onSubmit={handleCreateTokenSubmit}
            >
              <div className="form-field">
                <label className="field-label" htmlFor="account-token-name">
                  Agent / client label
                </label>
                <input
                  id="account-token-name"
                  type="text"
                  className="input"
                  value={newTokenName}
                  onChange={(e) => setNewTokenName(e.target.value)}
                  placeholder="e.g. Claude Desktop MCP"
                  required
                />
              </div>

              <div className="form-field">
                <span className="field-label">Permitted scopes</span>
                <div className="scope-chip-group">
                  {AVAILABLE_SCOPES.map((scope) => {
                    const active = selectedScopes.includes(scope);
                    return (
                      <button
                        key={scope}
                        type="button"
                        className={`mode-chip mono-num ${active ? 'is-active' : ''}`}
                        aria-pressed={active}
                        onClick={() => toggleScope(scope)}
                      >
                        {scope}
                      </button>
                    );
                  })}
                </div>
              </div>

              <div className="form-footer">
                <span className="meta-text">
                  Tokens are least-privilege and revocable at any time.
                </span>
                <button
                  id="account-token-submit"
                  type="submit"
                  className="btn btn-primary btn-compact"
                  disabled={!newTokenName.trim() || selectedScopes.length === 0}
                >
                  Generate Token
                </button>
              </div>
            </form>
          )}

          {agentTokens.length === 0 ? (
            <p className="empty-state meta-text">
              No active MCP agent tokens. Create one to connect an external agent.
            </p>
          ) : (
            <div className="stack-sm">
              {agentTokens.map((token) => (
                <article key={token.id} className="card-soft setting-item-row">
                  <div className="setting-item-body">
                    <div className="card-header-group">
                      <KeyIcon className="meta-icon" />
                      <span className="row-title">{token.name}</span>
                      <span className="meta-text mono-num">{token.prefix}</span>
                    </div>
                    <div className="token-scope-list">
                      {token.scopes.map((scope) => (
                        <span key={scope} className="pill-neutral mono-num">
                          {scope}
                        </span>
                      ))}
                      <span className="meta-text">· Last used {token.lastUsed}</span>
                    </div>
                  </div>
                  <button
                    type="button"
                    className="btn btn-ghost btn-xs"
                    onClick={() => onRevokeToken(token.id)}
                  >
                    Revoke
                  </button>
                </article>
              ))}
            </div>
          )}
        </section>

        {/* 5. Data & Danger Zone */}
        <section className="card" aria-labelledby="danger-heading">
          <div className="card-header">
            <h2 id="danger-heading" className="card-title">
              Data &amp; Danger Zone
            </h2>
          </div>

          <div className="stack-sm">
            <div className="card-soft setting-item-row">
              <div className="setting-item-body">
                <span className="row-title">Export personal archive</span>
                <span className="meta-text">
                  Download your profile, tasks, calendar events, and notes as JSON.
                </span>
              </div>
              <button
                id="account-export-btn"
                type="button"
                className="btn btn-secondary btn-compact"
                onClick={onExportData}
              >
                <DownloadIcon />
                <span>Export JSON</span>
              </button>
            </div>

            <div className="card-soft setting-item-row">
              <div className="setting-item-body">
                <span className="row-title">Reset local workspace data</span>
                <span className="meta-text">
                  Clears locally stored profile overrides and restores default Atrium
                  demo records.
                </span>
              </div>
              <button
                id="account-reset-btn"
                type="button"
                className="btn btn-danger-outline btn-compact"
                onClick={() => setConfirmDeleteOpen(true)}
              >
                Reset Account
              </button>
            </div>
          </div>
        </section>
      </main>

      <aside className="context-rail" aria-label="Account summary and sessions">
        <section className="card account-summary-card" aria-labelledby="account-card-heading">
          <div className="account-summary-top">
            <div className="avatar-md" aria-hidden="true">
              {profile.avatarDataUrl ? (
                <img src={profile.avatarDataUrl} alt="" className="avatar-img" />
              ) : (
                <span className="avatar-initials">{initials}</span>
              )}
            </div>
            <div className="account-summary-meta">
              <h2 id="account-card-heading" className="card-title">
                {profile.displayName || 'Atrium Owner'}
              </h2>
              <p className="meta-text mono-num">@{profile.username}</p>
            </div>
          </div>

          <dl className="account-meta-list">
            <div className="account-meta-row">
              <dt className="meta-text">Email</dt>
              <dd className="account-meta-val">{profile.email}</dd>
            </div>
            <div className="account-meta-row">
              <dt className="meta-text">Timezone</dt>
              <dd className="account-meta-val mono-num">{profile.timezone}</dd>
            </div>
            <div className="account-meta-row">
              <dt className="meta-text">Instance</dt>
              <dd className="account-meta-val">Self-Hosted Local</dd>
            </div>
            <div className="account-meta-row">
              <dt className="meta-text">MCP Tokens</dt>
              <dd className="account-meta-val mono-num">{agentTokens.length} active</dd>
            </div>
          </dl>
        </section>

        <section className="card" aria-labelledby="sessions-heading">
          <div className="card-header">
            <h2 id="sessions-heading" className="card-title">
              Active Sessions
            </h2>
            {otherSessionCount > 0 && (
              <button
                id="account-signout-others-btn"
                type="button"
                className="btn btn-ghost btn-xs"
                onClick={onSignOutOtherSessions}
              >
                Sign out others
              </button>
            )}
          </div>

          <div className="stack-sm">
            {sessions.map((session) => (
              <div key={session.id} className="card-soft session-item">
                <div className="session-top">
                  <span className="status-inline">
                    <span
                      className="status-dot"
                      data-state={session.isCurrent ? 'ok' : 'offline'}
                      aria-hidden="true"
                    />
                    <span className="row-title">{session.device}</span>
                  </span>
                </div>
                <div className="session-bottom">
                  <span className="meta-text">
                    {session.location} · {session.lastActive}
                  </span>
                  {!session.isCurrent && (
                    <button
                      type="button"
                      className="btn btn-ghost btn-xs"
                      onClick={() => onRevokeSession(session.id)}
                    >
                      Revoke
                    </button>
                  )}
                </div>
              </div>
            ))}
          </div>
        </section>

        <LocalStatusCard
          apiState={apiState}
          label={apiLabel}
          lastChecked={lastChecked}
          onRefresh={onRefreshHealth}
        />
      </aside>

      <dialog
        ref={deleteDialogRef}
        className="atrium-dialog"
        aria-labelledby="reset-dialog-title"
        onCancel={(e) => {
          e.preventDefault();
          setConfirmDeleteOpen(false);
        }}
        onClick={(e) => {
          if (e.target === deleteDialogRef.current) {
            setConfirmDeleteOpen(false);
          }
        }}
      >
        <div className="dialog-header">
          <div>
            <p className="kicker">DANGER ZONE</p>
            <h2 id="reset-dialog-title" className="dialog-title">
              Reset local Atrium account data?
            </h2>
          </div>
          <button
            type="button"
            className="btn btn-ghost btn-icon-sm"
            onClick={() => setConfirmDeleteOpen(false)}
            aria-label="Close confirmation dialog"
          >
            <CloseIcon />
          </button>
        </div>

        <form className="settings-form" onSubmit={handleConfirmDelete}>
          <p className="meta-text">
            This clears your locally saved profile photo, custom settings, and
            restores initial mock items. Type{' '}
            <strong className="mono-num">reset atrium</strong> to confirm.
          </p>

          <input
            id="account-reset-confirm-input"
            type="text"
            className="input mono-num"
            value={deletePhrase}
            onChange={(e) => setDeletePhrase(e.target.value)}
            placeholder="reset atrium"
            autoComplete="off"
          />

          <div className="dialog-actions-end">
            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => setConfirmDeleteOpen(false)}
            >
              Cancel
            </button>
            <button
              id="account-reset-confirm-btn"
              type="submit"
              className="btn btn-danger-solid"
              disabled={deletePhrase.trim().toLowerCase() !== 'reset atrium'}
            >
              Reset Local Data
            </button>
          </div>
        </form>
      </dialog>
    </div>
  );
}
