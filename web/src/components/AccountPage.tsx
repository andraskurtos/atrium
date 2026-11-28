import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react';
import { getInitials } from '../data/mockData';
import {
  createAiModel,
  createAiProvider,
  deleteAiModel,
  deleteAiProvider,
  discoverProviderModels,
  getAiConfig,
  updateAiDefaults,
  updateAiModel,
  updateAiProvider,
} from '../lib/aiConfigClient';
import { ApiError, formatRelativeTime, type CreateInvitePayload } from '../lib/authClient';
import type {
  AccountPreferences,
  AgentToken,
  AiConfig,
  AiModel,
  AiProvider,
  ApiState,
  HouseholdMember,
  InviteItem,
  SessionItem,
  ThemePreference,
  UpdatePreferencesPayload,
  UserProfile,
  WebSearchProviderKind,
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
  aiConfig: AiConfig | null;
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
  onAiConfigChange: (next: AiConfig) => void;
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
  'calendar:write',
  'tasks:read',
  'tasks:write',
  'notes:read',
  'notes:write',
  'university:read',
  'university:write',
  'vault:read',
];

export function AccountPage({
  profile,
  preferences,
  aiConfig,
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
  onAiConfigChange,
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

  // Multi-provider AI & Web Search settings state
  const [showAddProvider, setShowAddProvider] = useState(false);
  const [editingProviderId, setEditingProviderId] = useState<string | null>(null);
  const [providerName, setProviderName] = useState('');
  const [providerBaseUrl, setProviderBaseUrl] = useState('');
  const [providerApiKey, setProviderApiKey] = useState('');
  const [clearProviderKey, setClearProviderKey] = useState(false);
  const [showProviderKey, setShowProviderKey] = useState(false);
  const [savingAiSettings, setSavingAiSettings] = useState(false);
  const [aiSettingsError, setAiSettingsError] = useState('');

  // Per-provider add-model state
  const [addingModelForProviderId, setAddingModelForProviderId] = useState<string | null>(null);
  const [newModelId, setNewModelId] = useState('');
  const [newModelDisplayName, setNewModelDisplayName] = useState('');
  const [newModelVision, setNewModelVision] = useState(false);
  const [discoveredByProvider, setDiscoveredByProvider] = useState<Record<string, string[]>>({});
  const [discoveringProviderId, setDiscoveringProviderId] = useState<string | null>(null);

  // Defaults + Web Search state
  const [defaultModelId, setDefaultModelId] = useState<string>(aiConfig?.defaultModelId ?? '');
  const [defaultVisionModelId, setDefaultVisionModelId] = useState<string>(
    aiConfig?.defaultVisionModelId ?? '',
  );
  const [webSearchProvider, setWebSearchProvider] = useState<WebSearchProviderKind>(
    aiConfig?.webSearch.provider ?? 'duckduckgo',
  );
  const [webSearchUrl, setWebSearchUrl] = useState<string>(aiConfig?.webSearch.url ?? '');
  const [webSearchApiKey, setWebSearchApiKey] = useState('');
  const [showWebSearchKey, setShowWebSearchKey] = useState(false);

  useEffect(() => {
    if (!aiConfig) return;
    setDefaultModelId(aiConfig.defaultModelId ?? '');
    setDefaultVisionModelId(aiConfig.defaultVisionModelId ?? '');
    setWebSearchProvider(aiConfig.webSearch.provider ?? 'duckduckgo');
    setWebSearchUrl(aiConfig.webSearch.url ?? '');
  }, [aiConfig]);

  async function refreshAiConfig() {
    const fresh = await getAiConfig();
    onAiConfigChange(fresh);
  }

  function startEditProvider(provider: AiProvider) {
    setEditingProviderId(provider.id);
    setShowAddProvider(true);
    setProviderName(provider.name);
    setProviderBaseUrl(provider.baseUrl);
    setProviderApiKey('');
    setClearProviderKey(false);
    setAiSettingsError('');
  }

  function resetProviderForm() {
    setShowAddProvider(false);
    setEditingProviderId(null);
    setProviderName('');
    setProviderBaseUrl('');
    setProviderApiKey('');
    setClearProviderKey(false);
  }

  async function handleSaveProvider(e: FormEvent) {
    e.preventDefault();
    setAiSettingsError('');
    setSavingAiSettings(true);
    try {
      if (editingProviderId) {
        await updateAiProvider(editingProviderId, {
          name: providerName.trim(),
          baseUrl: providerBaseUrl.trim(),
          apiKey: providerApiKey.trim() || undefined,
          clearApiKey: clearProviderKey,
        });
        onAnnounce('AI provider updated');
      } else {
        await createAiProvider({
          name: providerName.trim(),
          baseUrl: providerBaseUrl.trim(),
          apiKey: providerApiKey.trim() || undefined,
        });
        onAnnounce('AI provider added');
      }
      resetProviderForm();
      await refreshAiConfig();
    } catch (err) {
      setAiSettingsError(
        err instanceof ApiError ? err.message : 'Failed to save AI provider.',
      );
    } finally {
      setSavingAiSettings(false);
    }
  }

  async function handleDeleteProvider(provider: AiProvider) {
    setAiSettingsError('');
    setSavingAiSettings(true);
    try {
      await deleteAiProvider(provider.id);
      await refreshAiConfig();
      onAnnounce(`Removed provider ${provider.name}`);
    } catch (err) {
      setAiSettingsError(
        err instanceof ApiError ? err.message : 'Failed to remove provider.',
      );
    } finally {
      setSavingAiSettings(false);
    }
  }

  async function handleDiscoverModels(providerId: string) {
    setAiSettingsError('');
    setDiscoveringProviderId(providerId);
    try {
      const res = await discoverProviderModels(providerId);
      if (res.error) {
        setAiSettingsError(res.error);
      } else {
        setDiscoveredByProvider((prev) => ({ ...prev, [providerId]: res.models }));
        setAddingModelForProviderId(providerId);
        onAnnounce(`Found ${res.models.length} models`);
      }
    } catch (err) {
      setAiSettingsError(
        err instanceof ApiError ? err.message : 'Could not fetch models from provider.',
      );
    } finally {
      setDiscoveringProviderId(null);
    }
  }

  async function handleAddModel(e: FormEvent, providerId: string) {
    e.preventDefault();
    setAiSettingsError('');
    setSavingAiSettings(true);
    try {
      await createAiModel({
        providerId,
        modelId: newModelId.trim(),
        displayName: newModelDisplayName.trim() || undefined,
        supportsVision: newModelVision,
      });
      setNewModelId('');
      setNewModelDisplayName('');
      setNewModelVision(false);
      setAddingModelForProviderId(null);
      await refreshAiConfig();
      onAnnounce('Model added');
    } catch (err) {
      setAiSettingsError(
        err instanceof ApiError ? err.message : 'Failed to add model.',
      );
    } finally {
      setSavingAiSettings(false);
    }
  }

  async function handleToggleModelVision(model: AiModel) {
    setAiSettingsError('');
    setSavingAiSettings(true);
    try {
      await updateAiModel(model.id, {
        modelId: model.modelId,
        displayName: model.displayName,
        supportsVision: !model.supportsVision,
        sortOrder: model.sortOrder,
      });
      await refreshAiConfig();
    } catch (err) {
      setAiSettingsError(
        err instanceof ApiError ? err.message : 'Failed to update model.',
      );
    } finally {
      setSavingAiSettings(false);
    }
  }

  async function handleDeleteModel(model: AiModel) {
    setAiSettingsError('');
    setSavingAiSettings(true);
    try {
      await deleteAiModel(model.id);
      await refreshAiConfig();
      onAnnounce(`Removed model ${model.displayName}`);
    } catch (err) {
      setAiSettingsError(
        err instanceof ApiError ? err.message : 'Failed to delete model.',
      );
    } finally {
      setSavingAiSettings(false);
    }
  }

  async function handleSaveAiDefaults(e: FormEvent, clearWebKey = false) {
    e.preventDefault();
    setAiSettingsError('');
    setSavingAiSettings(true);
    try {
      const updated = await updateAiDefaults({
        defaultModelId: defaultModelId || null,
        defaultVisionModelId: defaultVisionModelId || null,
        webSearchProvider,
        webSearchUrl: webSearchUrl.trim() || null,
        webSearchApiKey: clearWebKey ? undefined : webSearchApiKey.trim() || undefined,
        clearWebSearchApiKey: clearWebKey,
      });
      onAiConfigChange(updated);
      setWebSearchApiKey('');
      onAnnounce(clearWebKey ? 'Web search API key cleared' : 'AI defaults & web search saved');
    } catch (err) {
      setAiSettingsError(
        err instanceof ApiError ? err.message : 'Failed to save AI defaults.',
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

        {/* 5. AI Assistant (Multi-Provider Models & Web Search) */}
        <section className="card" aria-labelledby="ai-assistant-heading">
          <div className="card-header">
            <div className="card-header-group">
              <h2 id="ai-assistant-heading" className="card-title">
                AI Assistant
              </h2>
              <span className="pill-accent">Multi-Provider</span>
            </div>
            <button
              type="button"
              className="btn btn-secondary btn-xs"
              onClick={() => {
                if (showAddProvider) {
                  resetProviderForm();
                } else {
                  setEditingProviderId(null);
                  setShowAddProvider(true);
                }
              }}
            >
              <PlusIcon />
              <span>{showAddProvider ? 'Cancel' : 'Add Provider'}</span>
            </button>
          </div>

          {aiSettingsError && (
            <p className="field-error" role="alert">
              {aiSettingsError}
            </p>
          )}

          {showAddProvider && (
            <form
              className="card-soft ai-provider-form"
              onSubmit={(e) => void handleSaveProvider(e)}
            >
              <div className="form-grid-2">
                <div className="form-field">
                  <label className="field-label" htmlFor="ai-provider-name">
                    Provider Name
                  </label>
                  <input
                    id="ai-provider-name"
                    type="text"
                    className="input"
                    value={providerName}
                    onChange={(e) => setProviderName(e.target.value)}
                    placeholder="OpenRouter, OpenAI, Local Ollama…"
                    maxLength={80}
                    required
                  />
                </div>
                <div className="form-field">
                  <label className="field-label" htmlFor="ai-provider-url">
                    Base URL
                  </label>
                  <input
                    id="ai-provider-url"
                    type="url"
                    className="input mono-num"
                    value={providerBaseUrl}
                    onChange={(e) => setProviderBaseUrl(e.target.value)}
                    placeholder="https://openrouter.ai/api/v1"
                    maxLength={500}
                    required
                  />
                </div>
              </div>
              <div className="form-field">
                <div className="field-label-row">
                  <label className="field-label" htmlFor="ai-provider-key">
                    API Key{' '}
                    <span className="meta-text">
                      (Encrypted at rest · optional for local servers)
                    </span>
                  </label>
                  <button
                    type="button"
                    className="btn btn-ghost btn-xs"
                    onClick={() => setShowProviderKey((p) => !p)}
                  >
                    {showProviderKey ? <EyeOffIcon /> : <EyeIcon />}
                    <span>{showProviderKey ? 'Hide' : 'Show'}</span>
                  </button>
                </div>
                <input
                  id="ai-provider-key"
                  type={showProviderKey ? 'text' : 'password'}
                  className="input mono-num"
                  value={providerApiKey}
                  onChange={(e) => {
                    setProviderApiKey(e.target.value);
                    setClearProviderKey(false);
                  }}
                  placeholder={
                    editingProviderId
                      ? 'Leave empty to keep existing key'
                      : 'sk-…'
                  }
                  autoComplete="off"
                />
                {editingProviderId && (
                  <label className="check-label-inline">
                    <input
                      type="checkbox"
                      checked={clearProviderKey}
                      onChange={(e) => setClearProviderKey(e.target.checked)}
                    />
                    <span className="meta-text">Clear saved API key</span>
                  </label>
                )}
              </div>
              <div className="form-footer">
                <span className="meta-text">
                  OpenAI-compatible `/chat/completions` endpoint.
                </span>
                <button
                  type="submit"
                  className="btn btn-primary btn-xs"
                  disabled={savingAiSettings}
                >
                  {editingProviderId ? 'Update Provider' : 'Save Provider'}
                </button>
              </div>
            </form>
          )}

          {/* Configured Providers & Models */}
          <div className="ai-providers-list">
            {(aiConfig?.providers ?? []).length === 0 ? (
              <p className="meta-text">
                No AI providers configured yet. Click <strong>Add Provider</strong> to connect OpenAI, OpenRouter, Ollama, or any OpenAI-compatible server.
              </p>
            ) : (
              (aiConfig?.providers ?? []).map((provider) => {
                const providerModels = (aiConfig?.models ?? []).filter(
                  (m) => m.providerId === provider.id,
                );
                const discovered = discoveredByProvider[provider.id] ?? [];
                const isAddingModel = addingModelForProviderId === provider.id;

                return (
                  <div key={provider.id} className="card-soft ai-provider-card">
                    <div className="ai-provider-header">
                      <div>
                        <div className="ai-provider-title-row">
                          <strong>{provider.name}</strong>
                          <span className="meta-text">
                            {provider.hasApiKey ? 'Key saved' : 'No key'}
                          </span>
                        </div>
                        <div className="meta-text mono-num">{provider.baseUrl}</div>
                      </div>
                      <div className="avatar-actions">
                        <button
                          type="button"
                          className="btn btn-ghost btn-xs"
                          disabled={discoveringProviderId === provider.id}
                          onClick={() => void handleDiscoverModels(provider.id)}
                        >
                          {discoveringProviderId === provider.id
                            ? 'Fetching…'
                            : 'Fetch models'}
                        </button>
                        <button
                          type="button"
                          className="btn btn-secondary btn-xs"
                          onClick={() => {
                            setAddingModelForProviderId(
                              isAddingModel ? null : provider.id,
                            );
                            setNewModelId('');
                            setNewModelDisplayName('');
                            setNewModelVision(false);
                          }}
                        >
                          + Model
                        </button>
                        <button
                          type="button"
                          className="btn btn-ghost btn-xs"
                          onClick={() => startEditProvider(provider)}
                        >
                          Edit
                        </button>
                        <button
                          type="button"
                          className="btn btn-ghost btn-xs"
                          disabled={savingAiSettings}
                          onClick={() => void handleDeleteProvider(provider)}
                        >
                          Remove
                        </button>
                      </div>
                    </div>

                    {providerModels.length > 0 && (
                      <ul className="ai-models-list">
                        {providerModels.map((model) => (
                          <li key={model.id} className="ai-model-row">
                            <div className="ai-model-info">
                              <span className="row-title">{model.displayName}</span>
                              {model.displayName !== model.modelId && (
                                <span className="meta-text mono-num">{model.modelId}</span>
                              )}
                            </div>
                            <div className="avatar-actions">
                              <button
                                type="button"
                                className={`mode-chip ${model.supportsVision ? 'is-active' : ''}`}
                                disabled={savingAiSettings}
                                onClick={() => void handleToggleModelVision(model)}
                                title="Toggle vision / image input support"
                              >
                                {model.supportsVision ? 'Vision ✓' : 'Text only'}
                              </button>
                              <button
                                type="button"
                                className="btn btn-ghost btn-xs"
                                disabled={savingAiSettings}
                                onClick={() => void handleDeleteModel(model)}
                                aria-label={`Remove ${model.displayName}`}
                              >
                                ×
                              </button>
                            </div>
                          </li>
                        ))}
                      </ul>
                    )}

                    {isAddingModel && (
                      <form
                        className="ai-model-add-form"
                        onSubmit={(e) => void handleAddModel(e, provider.id)}
                      >
                        <div className="form-grid-2">
                          <div className="form-field">
                            <label className="field-label">Model ID</label>
                            <input
                              type="text"
                              className="input mono-num"
                              list={`discovered-${provider.id}`}
                              value={newModelId}
                              onChange={(e) => setNewModelId(e.target.value)}
                              placeholder="gpt-4o, openai/gpt-4o-mini, qwen2.5…"
                              required
                            />
                            {discovered.length > 0 && (
                              <datalist id={`discovered-${provider.id}`}>
                                {discovered.map((id) => (
                                  <option key={id} value={id} />
                                ))}
                              </datalist>
                            )}
                          </div>
                          <div className="form-field">
                            <label className="field-label">Display Name (optional)</label>
                            <input
                              type="text"
                              className="input"
                              value={newModelDisplayName}
                              onChange={(e) => setNewModelDisplayName(e.target.value)}
                              placeholder="Defaults to Model ID"
                            />
                          </div>
                        </div>
                        <div className="form-footer">
                          <label className="check-label-inline">
                            <input
                              type="checkbox"
                              checked={newModelVision}
                              onChange={(e) => setNewModelVision(e.target.checked)}
                            />
                            <span className="meta-text">Supports vision (image inputs)</span>
                          </label>
                          <button
                            type="submit"
                            className="btn btn-primary btn-xs"
                            disabled={savingAiSettings}
                          >
                            Add Model
                          </button>
                        </div>
                      </form>
                    )}
                  </div>
                );
              })
            )}
          </div>

          {/* Defaults & Web Search */}
          <form
            className="settings-form ai-defaults-form"
            onSubmit={(e) => void handleSaveAiDefaults(e, false)}
          >
            <div className="form-grid-2">
              <div className="form-field">
                <label className="field-label" htmlFor="ai-default-model">
                  Default Model
                </label>
                <select
                  id="ai-default-model"
                  className="input"
                  value={defaultModelId}
                  onChange={(e) => setDefaultModelId(e.target.value)}
                >
                  <option value="">Auto (first configured)</option>
                  {(aiConfig?.models ?? []).map((m) => (
                    <option key={m.id} value={m.id}>
                      {m.displayName} ({m.providerName})
                    </option>
                  ))}
                </select>
              </div>
              <div className="form-field">
                <label className="field-label" htmlFor="ai-default-vision-model">
                  Default Vision Model
                </label>
                <select
                  id="ai-default-vision-model"
                  className="input"
                  value={defaultVisionModelId}
                  onChange={(e) => setDefaultVisionModelId(e.target.value)}
                >
                  <option value="">None configured</option>
                  {(aiConfig?.models ?? [])
                    .filter((m) => m.supportsVision)
                    .map((m) => (
                      <option key={m.id} value={m.id}>
                        {m.displayName} ({m.providerName})
                      </option>
                    ))}
                </select>
              </div>
            </div>

            <div className="form-grid-2">
              <div className="form-field">
                <label className="field-label" htmlFor="ai-web-search-provider">
                  Web Search Provider
                </label>
                <select
                  id="ai-web-search-provider"
                  className="input"
                  value={webSearchProvider}
                  onChange={(e) =>
                    setWebSearchProvider(e.target.value as WebSearchProviderKind)
                  }
                >
                  <option value="duckduckgo">DuckDuckGo (Built-in, no key)</option>
                  <option value="tavily">Tavily Search (API key)</option>
                  <option value="brave">Brave Search (API key)</option>
                  <option value="searxng">SearXNG (Self-hosted URL)</option>
                </select>
              </div>

              {webSearchProvider === 'searxng' && (
                <div className="form-field">
                  <label className="field-label" htmlFor="ai-web-search-url">
                    SearXNG Base URL
                  </label>
                  <input
                    id="ai-web-search-url"
                    type="url"
                    className="input mono-num"
                    value={webSearchUrl}
                    onChange={(e) => setWebSearchUrl(e.target.value)}
                    placeholder="http://localhost:8888"
                    required
                  />
                </div>
              )}

              {webSearchProvider !== 'duckduckgo' && (
                <div className="form-field">
                  <div className="field-label-row">
                    <label className="field-label" htmlFor="ai-web-search-key">
                      Search API Key{' '}
                      <span className="meta-text">
                        ({aiConfig?.webSearch.hasApiKey ? 'Saved · encrypted' : 'Optional for SearXNG'})
                      </span>
                    </label>
                    <div className="avatar-actions">
                      <button
                        type="button"
                        className="btn btn-ghost btn-xs"
                        onClick={() => setShowWebSearchKey((p) => !p)}
                      >
                        {showWebSearchKey ? <EyeOffIcon /> : <EyeIcon />}
                      </button>
                      {aiConfig?.webSearch.hasApiKey && (
                        <button
                          type="button"
                          className="btn btn-ghost btn-xs"
                          disabled={savingAiSettings}
                          onClick={(e) => void handleSaveAiDefaults(e, true)}
                        >
                          Clear
                        </button>
                      )}
                    </div>
                  </div>
                  <input
                    id="ai-web-search-key"
                    type={showWebSearchKey ? 'text' : 'password'}
                    className="input mono-num"
                    value={webSearchApiKey}
                    onChange={(e) => setWebSearchApiKey(e.target.value)}
                    placeholder={
                      aiConfig?.webSearch.hasApiKey
                        ? '•••••••• (enter new key to replace)'
                        : 'API key'
                    }
                    autoComplete="off"
                  />
                </div>
              )}
            </div>

            <div className="form-footer">
              <span className="meta-text">
                Image attachments auto-route to your Default Vision Model when a text-only model is selected.
              </span>
              <button
                id="account-save-ai-btn"
                type="submit"
                className="btn btn-primary"
                disabled={savingAiSettings}
              >
                {savingAiSettings ? 'Saving…' : 'Save Defaults & Search'}
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
