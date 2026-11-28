import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react';
import { getInitials } from '../data/mockData';
import { ApiError, formatRelativeTime, type CreateInvitePayload } from '../lib/authClient';
import type {
  AccountPreferences,
  AgentToken,
  ApiState,
  HouseholdMember,
  InviteItem,
  SessionItem,
  ThemePreference,
  UpdatePreferencesPayload,
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
  agentTokens: AgentToken[];
  sessions: SessionItem[];
  invites: InviteItem[];
  members: HouseholdMember[];
  apiState: ApiState;
  apiLabel: string;
  lastChecked: string;
  onBackToDashboard: () => void;
  onUpdateProfile: (next: {
    displayName: string;
    username: string;
    email: string;
    bio: string;
    timezone: string;
    avatarDataUrl: string | null;
  }) => Promise<void>;
  onUpdatePreferences: (next: UpdatePreferencesPayload) => Promise<void>;
  onSetTheme: (theme: ThemePreference) => void;
  onChangePassword: (currentPassword: string, newPassword: string) => Promise<void>;
  onCreateToken: (name: string, scopes: string[]) => Promise<string>;
  onRevokeToken: (id: string) => Promise<void>;
  onRevokeSession: (id: string) => Promise<void>;
  onSignOutOtherSessions: () => Promise<void>;
  onCreateInvite: (label: string | null, expiresInDays: number) => Promise<CreateInvitePayload>;
  onRevokeInvite: (id: string) => Promise<void>;
  onToggleMemberActive: (id: string, isActive: boolean) => Promise<void>;
  onDeleteMember: (id: string) => Promise<void>;
  onExportData: () => void;
  onSignOut: () => void;
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
  'notes:read',
  'university:read',
  'university:propose',
];

export function AccountPage({
  profile,
  preferences,
  theme,
  agentTokens,
  sessions,
  invites,
  members,
  apiState,
  apiLabel,
  lastChecked,
  onBackToDashboard,
  onUpdateProfile,
  onUpdatePreferences,
  onSetTheme,
  onChangePassword,
  onCreateToken,
  onRevokeToken,
  onRevokeSession,
  onSignOutOtherSessions,
  onCreateInvite,
  onRevokeInvite,
  onToggleMemberActive,
  onDeleteMember,
  onExportData,
  onSignOut,
  onRefreshHealth,
  onAnnounce,
}: AccountPageProps) {
  const isOwner = profile.role === 'Owner';

  // Profile draft state
  const [displayName, setDisplayName] = useState(profile.displayName);
  const [username, setUsername] = useState(profile.username);
  const [email, setEmail] = useState(profile.email);
  const [bio, setBio] = useState(profile.bio);
  const [timezone, setTimezone] = useState(profile.timezone);
  const [avatarError, setAvatarError] = useState('');
  const [profileError, setProfileError] = useState('');
  const [savingProfile, setSavingProfile] = useState(false);
  const fileInputRef = useRef<HTMLInputElement | null>(null);

  useEffect(() => {
    setDisplayName(profile.displayName);
    setUsername(profile.username);
    setEmail(profile.email);
    setBio(profile.bio);
    setTimezone(profile.timezone);
  }, [profile]);

  // AI Inference settings state
  const [aiInferenceUrl, setAiInferenceUrl] = useState(
    preferences.aiInferenceUrl ?? '',
  );
  const [aiModel, setAiModel] = useState(preferences.aiModel ?? '');
  const [aiApiKeyInput, setAiApiKeyInput] = useState('');
  const [showAiApiKey, setShowAiApiKey] = useState(false);
  const [savingAiSettings, setSavingAiSettings] = useState(false);
  const [aiSettingsError, setAiSettingsError] = useState('');

  useEffect(() => {
    setAiInferenceUrl(preferences.aiInferenceUrl ?? '');
    setAiModel(preferences.aiModel ?? '');
  }, [preferences.aiInferenceUrl, preferences.aiModel]);

  async function handleSaveAiSettings(e: FormEvent) {
    e.preventDefault();
    setAiSettingsError('');
    setSavingAiSettings(true);
    try {
      await onUpdatePreferences({
        ...preferences,
        aiInferenceUrl: aiInferenceUrl.trim(),
        aiModel: aiModel.trim(),
        aiApiKey: aiApiKeyInput.trim() || undefined,
      });
      setAiApiKeyInput('');
      onAnnounce('AI Assistant settings saved');
    } catch (err) {
      setAiSettingsError(
        err instanceof ApiError ? err.message : 'Failed to save AI settings.',
      );
    } finally {
      setSavingAiSettings(false);
    }
  }

  async function handleClearAiApiKey() {
    setAiSettingsError('');
    setSavingAiSettings(true);
    try {
      await onUpdatePreferences({
        ...preferences,
        clearAiApiKey: true,
      });
      setAiApiKeyInput('');
      onAnnounce('AI API key cleared');
    } catch (err) {
      setAiSettingsError(
        err instanceof ApiError ? err.message : 'Failed to clear API key.',
      );
    } finally {
      setSavingAiSettings(false);
    }
  }

  // Password form state
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [showPasswords, setShowPasswords] = useState(false);
  const [passwordError, setPasswordError] = useState('');
  const [savingPassword, setSavingPassword] = useState(false);

  // Scoped MCP token inline form
  const [showTokenForm, setShowTokenForm] = useState(false);
  const [newTokenName, setNewTokenName] = useState('');
  const [selectedScopes, setSelectedScopes] = useState<string[]>([
    'calendar:read',
    'calendar:propose',
  ]);
  const [createdSecret, setCreatedSecret] = useState<string | null>(null);
  const [tokenError, setTokenError] = useState('');

  // Household invites inline form (Owner-only)
  const [showInviteForm, setShowInviteForm] = useState(false);
  const [inviteLabel, setInviteLabel] = useState('');
  const [inviteDays, setInviteDays] = useState(7);
  const [createdInvite, setCreatedInvite] = useState<CreateInvitePayload | null>(null);
  const [householdError, setHouseholdError] = useState('');

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
    reader.onload = async () => {
      if (typeof reader.result === 'string') {
        try {
          await onUpdateProfile({
            displayName: profile.displayName,
            username: profile.username,
            email: profile.email,
            bio: profile.bio,
            timezone: profile.timezone,
            avatarDataUrl: reader.result,
          });
          onAnnounce('Profile photo updated');
        } catch (err: unknown) {
          setAvatarError(err instanceof ApiError ? err.message : 'Could not upload avatar.');
        }
      }
    };
    reader.readAsDataURL(file);
    event.target.value = '';
  };

  const handleRemoveAvatar = async () => {
    setAvatarError('');
    try {
      await onUpdateProfile({
        displayName: profile.displayName,
        username: profile.username,
        email: profile.email,
        bio: profile.bio,
        timezone: profile.timezone,
        avatarDataUrl: null,
      });
      onAnnounce('Profile photo removed');
    } catch (err: unknown) {
      setAvatarError(err instanceof ApiError ? err.message : 'Could not remove avatar.');
    }
  };

  const handleProfileSubmit = async (event: FormEvent) => {
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

    setSavingProfile(true);
    try {
      await onUpdateProfile({
        displayName: displayName.trim(),
        username: cleanUsername,
        email: cleanEmail,
        bio: bio.trim(),
        timezone,
        avatarDataUrl: profile.avatarDataUrl,
      });
      onAnnounce('Account profile saved');
    } catch (err: unknown) {
      setProfileError(err instanceof ApiError ? err.message : 'Could not save profile.');
    } finally {
      setSavingProfile(false);
    }
  };

  const handlePasswordSubmit = async (event: FormEvent) => {
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

    setSavingPassword(true);
    try {
      await onChangePassword(currentPassword, newPassword);
      setCurrentPassword('');
      setNewPassword('');
      setConfirmPassword('');
      onAnnounce('Password updated and other sessions signed out');
    } catch (err: unknown) {
      setPasswordError(err instanceof ApiError ? err.message : 'Could not update password.');
    } finally {
      setSavingPassword(false);
    }
  };

  const toggleScope = (scope: string) => {
    setSelectedScopes((prev) =>
      prev.includes(scope)
        ? prev.filter((item) => item !== scope)
        : [...prev, scope],
    );
  };

  const handleCreateTokenSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setTokenError('');
    const trimmed = newTokenName.trim();
    if (!trimmed || selectedScopes.length === 0) return;
    try {
      const secret = await onCreateToken(trimmed, selectedScopes);
      setCreatedSecret(secret);
      setNewTokenName('');
      setShowTokenForm(false);
    } catch (err: unknown) {
      setTokenError(err instanceof ApiError ? err.message : 'Could not create token.');
    }
  };

  const handleCreateInviteSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setHouseholdError('');
    try {
      const result = await onCreateInvite(inviteLabel.trim() || null, inviteDays);
      setCreatedInvite(result);
      setInviteLabel('');
      setShowInviteForm(false);
    } catch (err: unknown) {
      setHouseholdError(err instanceof ApiError ? err.message : 'Could not create invite.');
    }
  };

  const handleCopyText = async (value: string, label: string) => {
    try {
      await navigator.clipboard.writeText(value);
      onAnnounce(`${label} copied to clipboard`);
    } catch {
      onAnnounce(`Copy ${label.toLowerCase()} manually from the box`);
    }
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
            Manage your profile identity, password, workspace preferences, active
            sessions, and scoped MCP agent credentials.
          </p>
        </header>

        {/* 1. Profile & Identity */}
        <section className="card" aria-labelledby="profile-heading">
          <div className="card-header">
            <h2 id="profile-heading" className="card-title">
              Profile &amp; Identity
            </h2>
            <span className="pill-accent">{profile.role}</span>
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
                Stored in your Atrium PostgreSQL instance. PNG, JPG, or WebP up to 2 MB.
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
                disabled={!isProfileDirty || savingProfile}
              >
                {savingProfile ? 'Saving…' : 'Save Profile'}
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
              Updated {formatRelativeTime(profile.passwordUpdatedAt)}
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
                Changing your password signs out all other active sessions.
              </span>
              <button
                id="account-update-password-btn"
                type="submit"
                className="btn btn-primary"
                disabled={savingPassword}
              >
                {savingPassword ? 'Updating…' : 'Update Password'}
              </button>
            </div>
          </form>

          <div className="subsection-divider">
            <div className="card-header">
              <div className="card-header-group">
                <ShieldIcon className="meta-icon" />
                <h3 className="row-title">Passkeys (WebAuthn)</h3>
              </div>
              <span className="pill-neutral">Coming next</span>
            </div>
            <p className="meta-text">
              Hardware and platform WebAuthn passkeys will be enabled in the dedicated
              Passkeys follow-up release.
            </p>
          </div>
        </section>

        {/* 3. Household Invites & Members (Owner-only) */}
        {isOwner && (
          <section className="card" aria-labelledby="household-heading">
            <div className="card-header">
              <div className="card-header-group">
                <h2 id="household-heading" className="card-title">
                  Household Invites &amp; Members
                </h2>
                <span className="pill-accent">Owner Only</span>
              </div>
              <button
                id="account-create-invite-toggle"
                type="button"
                className="btn btn-secondary btn-xs"
                onClick={() => setShowInviteForm((prev) => !prev)}
                aria-expanded={showInviteForm}
              >
                <PlusIcon />
                <span>{showInviteForm ? 'Cancel' : 'New Invite'}</span>
              </button>
            </div>

            {householdError && (
              <p className="field-error" role="alert">
                {householdError}
              </p>
            )}

            {createdInvite && (
              <div className="card-soft token-reveal-box" role="status">
                <div className="token-reveal-header">
                  <span className="status-inline">
                    <span className="status-dot" data-state="ok" aria-hidden="true" />
                    <span className="row-title">One-time invite created — copy now</span>
                  </span>
                  <button
                    type="button"
                    className="btn btn-ghost btn-icon-sm"
                    onClick={() => setCreatedInvite(null)}
                    aria-label="Dismiss invite banner"
                  >
                    <CloseIcon />
                  </button>
                </div>
                <div className="token-reveal-value">
                  <code className="mono-num">
                    {`${window.location.origin}${createdInvite.invitePath}`}
                  </code>
                  <button
                    type="button"
                    className="btn btn-secondary btn-xs"
                    onClick={() =>
                      handleCopyText(
                        `${window.location.origin}${createdInvite.invitePath}`,
                        'Invite link',
                      )
                    }
                  >
                    <CopyIcon />
                    <span>Copy Link</span>
                  </button>
                </div>
              </div>
            )}

            {showInviteForm && (
              <form
                className="card-soft token-create-form"
                onSubmit={handleCreateInviteSubmit}
              >
                <div className="form-grid-2">
                  <div className="form-field">
                    <label className="field-label" htmlFor="invite-label-input">
                      Label <span className="text-faint">(optional)</span>
                    </label>
                    <input
                      id="invite-label-input"
                      type="text"
                      className="input"
                      value={inviteLabel}
                      onChange={(e) => setInviteLabel(e.target.value)}
                      placeholder="e.g. Partner's laptop"
                      maxLength={80}
                    />
                  </div>
                  <div className="form-field">
                    <label className="field-label" htmlFor="invite-days-select">
                      Expires in
                    </label>
                    <select
                      id="invite-days-select"
                      className="input"
                      value={inviteDays}
                      onChange={(e) => setInviteDays(Number(e.target.value))}
                    >
                      <option value={1}>1 day</option>
                      <option value={7}>7 days</option>
                      <option value={14}>14 days</option>
                      <option value={30}>30 days</option>
                    </select>
                  </div>
                </div>
                <div className="form-footer">
                  <span className="meta-text">
                    Invites always create a household Member account.
                  </span>
                  <button type="submit" className="btn btn-primary btn-compact">
                    Generate Invite
                  </button>
                </div>
              </form>
            )}

            <div className="stack-sm">
              <p className="field-label">Household Members ({members.length})</p>
              {members.map((member) => (
                <div key={member.id} className="card-soft setting-item-row">
                  <div className="setting-item-body">
                    <div className="card-header-group">
                      <span className="row-title">{member.displayName}</span>
                      <span className="meta-text mono-num">@{member.username}</span>
                      <span className="pill-neutral">{member.role}</span>
                      {!member.isActive && <span className="pill-neutral">Deactivated</span>}
                    </div>
                    <span className="meta-text">
                      {member.email} · {member.activeSessionCount} sessions ·{' '}
                      {member.activeTokenCount} tokens
                    </span>
                  </div>
                  {member.role !== 'Owner' && (
                    <div className="avatar-actions">
                      <button
                        type="button"
                        className="btn btn-ghost btn-xs"
                        onClick={() => onToggleMemberActive(member.id, !member.isActive)}
                      >
                        {member.isActive ? 'Deactivate' : 'Reactivate'}
                      </button>
                      <button
                        type="button"
                        className="btn btn-danger-outline btn-xs"
                        onClick={() => onDeleteMember(member.id)}
                      >
                        Remove
                      </button>
                    </div>
                  )}
                </div>
              ))}
            </div>

            {invites.length > 0 && (
              <div className="subsection-divider stack-sm">
                <p className="field-label">Invites ({invites.length})</p>
                {invites.map((invite) => (
                  <div key={invite.id} className="card-soft setting-item-row">
                    <div className="setting-item-body">
                      <div className="card-header-group">
                        <span className="row-title">
                          {invite.label || 'Household invite'}
                        </span>
                        <span className="meta-text mono-num">{invite.codePrefix}••••</span>
                        <span className="pill-neutral">{invite.status}</span>
                      </div>
                      <span className="meta-text">
                        Created {formatRelativeTime(invite.createdAt)} · Expires{' '}
                        {new Date(invite.expiresAt).toLocaleDateString()}
                      </span>
                    </div>
                    {invite.status === 'pending' && (
                      <button
                        type="button"
                        className="btn btn-ghost btn-xs"
                        onClick={() => onRevokeInvite(invite.id)}
                      >
                        Revoke
                      </button>
                    )}
                  </div>
                ))}
              </div>
            )}
          </section>
        )}

        {/* 4. Preferences & Notifications */}
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
                      void onUpdatePreferences({ ...preferences, theme: option.id });
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
                    void onUpdatePreferences({ ...preferences, weekStartsOn: value });
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
                    void onUpdatePreferences({ ...preferences, timeFormat: value });
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
                      void onUpdatePreferences({
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
                      void onUpdatePreferences({
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
                      void onUpdatePreferences({
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

        {/* 5. AI Assistant (OpenAI-Compatible Inference) */}
        <section className="card" aria-labelledby="ai-assistant-heading">
          <div className="card-header">
            <div className="card-header-group">
              <h2 id="ai-assistant-heading" className="card-title">
                AI Assistant
              </h2>
              <span className="pill-accent">OpenAI API</span>
            </div>
            <span className="meta-text">
              {preferences.aiInferenceUrl && preferences.aiModel
                ? 'Configured'
                : 'Not configured'}
            </span>
          </div>

          <form className="settings-form" onSubmit={(e) => void handleSaveAiSettings(e)}>
            <div className="form-grid-2">
              <div className="form-field">
                <label className="field-label" htmlFor="account-ai-url">
                  Inference Base URL
                </label>
                <input
                  id="account-ai-url"
                  type="url"
                  className="input mono-num"
                  value={aiInferenceUrl}
                  onChange={(e) => setAiInferenceUrl(e.target.value)}
                  placeholder="https://api.openai.com/v1 or http://localhost:11434/v1"
                  maxLength={500}
                />
              </div>

              <div className="form-field">
                <label className="field-label" htmlFor="account-ai-model">
                  Model Name
                </label>
                <input
                  id="account-ai-model"
                  type="text"
                  className="input mono-num"
                  value={aiModel}
                  onChange={(e) => setAiModel(e.target.value)}
                  placeholder="gpt-4o-mini, llama3.2, qwen2.5…"
                  maxLength={120}
                />
              </div>
            </div>

            <div className="form-field">
              <div className="field-label-row">
                <label className="field-label" htmlFor="account-ai-key">
                  API Key{' '}
                  <span className="meta-text">
                    ({preferences.hasAiApiKey ? 'Saved · encrypted' : 'Optional for local servers'})
                  </span>
                </label>
                <div className="avatar-actions">
                  <button
                    type="button"
                    className="btn btn-ghost btn-xs"
                    onClick={() => setShowAiApiKey((prev) => !prev)}
                  >
                    {showAiApiKey ? <EyeOffIcon /> : <EyeIcon />}
                    <span>{showAiApiKey ? 'Hide' : 'Show'}</span>
                  </button>
                  {preferences.hasAiApiKey && (
                    <button
                      type="button"
                      className="btn btn-ghost btn-xs"
                      disabled={savingAiSettings}
                      onClick={() => void handleClearAiApiKey()}
                    >
                      Clear key
                    </button>
                  )}
                </div>
              </div>
              <input
                id="account-ai-key"
                type={showAiApiKey ? 'text' : 'password'}
                className="input mono-num"
                value={aiApiKeyInput}
                onChange={(e) => setAiApiKeyInput(e.target.value)}
                placeholder={
                  preferences.hasAiApiKey
                    ? '•••••••••••••••• (enter new key to replace)'
                    : 'sk-… (leave empty for local Ollama / LM Studio)'
                }
                autoComplete="off"
                maxLength={512}
              />
            </div>

            {aiSettingsError && (
              <p className="field-error" role="alert">
                {aiSettingsError}
              </p>
            )}

            <div className="form-footer">
              <span className="meta-text">
                Used by the right-hand chat sidebar via OpenAI Chat Completions.
              </span>
              <button
                id="account-save-ai-btn"
                type="submit"
                className="btn btn-primary"
                disabled={savingAiSettings}
              >
                {savingAiSettings ? 'Saving…' : 'Save AI Settings'}
              </button>
            </div>
          </form>
        </section>

        {/* 6. Scoped MCP Agent Tokens */}
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

          {tokenError && (
            <p className="field-error" role="alert">
              {tokenError}
            </p>
          )}

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
                  onClick={() => handleCopyText(createdSecret, 'Scoped token')}
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
                  Tokens are hashed with SHA-256 and revocable at any time.
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
                      <span className="meta-text mono-num">{token.prefix}••••</span>
                    </div>
                    <div className="token-scope-list">
                      {token.scopes.map((scope) => (
                        <span key={scope} className="pill-neutral mono-num">
                          {scope}
                        </span>
                      ))}
                      <span className="meta-text">
                        · Last used {formatRelativeTime(token.lastUsedAt)}
                      </span>
                    </div>
                  </div>
                  <button
                    type="button"
                    className="btn btn-ghost btn-xs"
                    onClick={() => void onRevokeToken(token.id)}
                  >
                    Revoke
                  </button>
                </article>
              ))}
            </div>
          )}

          {(() => {
            const mcpUrl = `${window.location.origin}/mcp`;
            const mcpConfigSnippet = JSON.stringify(
              {
                mcpServers: {
                  atrium: {
                    url: mcpUrl,
                    headers: {
                      Authorization: `Bearer ${createdSecret ?? '<your-agent-token>'}`,
                    },
                  },
                },
              },
              null,
              2,
            );
            return (
              <details className="card-soft mcp-connect-box">
                <summary className="mcp-connect-summary">
                  <span className="row-title">Connect via MCP</span>
                  <span className="meta-text">Streamable HTTP · Read-only tools</span>
                </summary>
                <div className="mcp-connect-body">
                  <div className="mcp-connect-url-row">
                    <span className="meta-text">Endpoint:</span>
                    <code className="mono-num mcp-connect-url">{mcpUrl}</code>
                    <button
                      type="button"
                      className="btn btn-ghost btn-xs"
                      onClick={() => handleCopyText(mcpUrl, 'MCP endpoint URL')}
                    >
                      <CopyIcon />
                      <span>Copy URL</span>
                    </button>
                  </div>
                  <div className="mcp-connect-snippet-wrap">
                    <pre className="mcp-connect-pre">
                      <code>{mcpConfigSnippet}</code>
                    </pre>
                    <button
                      type="button"
                      className="btn btn-secondary btn-xs mcp-connect-copy-btn"
                      onClick={() => handleCopyText(mcpConfigSnippet, 'MCP config snippet')}
                    >
                      <CopyIcon />
                      <span>Copy config</span>
                    </button>
                  </div>
                  <p className="meta-text">
                    Requires an Agent PAT (<code>atr_...</code>). Private notes and folders
                    with agent read disabled are always excluded.
                  </p>
                </div>
              </details>
            );
          })()}
        </section>

        {/* 6. Data Export */}
        <section className="card" aria-labelledby="export-heading">
          <div className="card-header">
            <h2 id="export-heading" className="card-title">
              Data Export
            </h2>
          </div>

          <div className="card-soft setting-item-row">
            <div className="setting-item-body">
              <span className="row-title">Export personal archive</span>
              <span className="meta-text">
                Download your profile, preferences, tasks, calendar events, and notes as
                JSON.
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
                {profile.displayName || profile.username}
              </h2>
              <p className="meta-text mono-num">@{profile.username}</p>
            </div>
          </div>

          <dl className="account-meta-list">
            <div className="account-meta-row">
              <dt className="meta-text">Role</dt>
              <dd className="account-meta-val">{profile.role}</dd>
            </div>
            <div className="account-meta-row">
              <dt className="meta-text">Email</dt>
              <dd className="account-meta-val">{profile.email}</dd>
            </div>
            <div className="account-meta-row">
              <dt className="meta-text">Timezone</dt>
              <dd className="account-meta-val mono-num">{profile.timezone}</dd>
            </div>
            <div className="account-meta-row">
              <dt className="meta-text">MCP Tokens</dt>
              <dd className="account-meta-val mono-num">{agentTokens.length} active</dd>
            </div>
          </dl>

          <button
            id="account-signout-btn"
            type="button"
            className="btn btn-secondary btn-compact"
            onClick={onSignOut}
          >
            Sign out of this device
          </button>
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
                onClick={() => void onSignOutOtherSessions()}
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
                    {session.location} · {formatRelativeTime(session.lastActiveAt)}
                  </span>
                  {!session.isCurrent && (
                    <button
                      type="button"
                      className="btn btn-ghost btn-xs"
                      onClick={() => void onRevokeSession(session.id)}
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
    </div>
  );
}
