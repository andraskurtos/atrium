import type {
  AccountPreferences,
  AgentToken,
  HouseholdMember,
  InviteItem,
  SessionItem,
  UserProfile,
} from '../types/atrium';

export class ApiError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
}

export interface AuthSessionPayload {
  accessToken: string;
  expiresAt: string;
  user: UserProfile;
  preferences: AccountPreferences;
}

export interface AccountMePayload {
  user: UserProfile;
  preferences: AccountPreferences;
}

export interface CreateTokenPayload {
  token: AgentToken;
  secret: string;
}

export interface CreateInvitePayload {
  invite: InviteItem;
  code: string;
  invitePath: string;
}

// Access token is intentionally held in module memory only (never localStorage).
let memoryAccessToken: string | null = null;
let inFlightRefresh: Promise<boolean> | null = null;
let unauthorizedHandler: (() => void) | null = null;

export function setUnauthorizedHandler(handler: (() => void) | null): void {
  unauthorizedHandler = handler;
}

export function clearAccessToken(): void {
  memoryAccessToken = null;
}

async function parseErrorMessage(response: Response): Promise<string> {
  try {
    const body = (await response.json()) as { error?: string; title?: string };
    if (body.error) return body.error;
    if (body.title) return body.title;
  } catch {
    // Fall back to HTTP status text
  }
  return `Request failed (${response.status})`;
}

export async function refreshAccessToken(): Promise<boolean> {
  if (inFlightRefresh) return inFlightRefresh;

  inFlightRefresh = (async () => {
    try {
      const res = await fetch('/api/auth/refresh', {
        method: 'POST',
        credentials: 'same-origin',
        headers: {
          'X-Atrium-CSRF': '1',
        },
      });
      if (!res.ok) {
        memoryAccessToken = null;
        return false;
      }
      const data = (await res.json()) as { accessToken: string };
      memoryAccessToken = data.accessToken;
      return true;
    } catch {
      return false;
    } finally {
      inFlightRefresh = null;
    }
  })();

  return inFlightRefresh;
}

async function apiFetch<T>(
  path: string,
  options: RequestInit = {},
  { authenticated = true, retryOn401 = true } = {},
): Promise<T> {
  const headers = new Headers(options.headers);
  if (options.body && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }
  if (authenticated && memoryAccessToken) {
    headers.set('Authorization', `Bearer ${memoryAccessToken}`);
  }

  const response = await fetch(path, {
    ...options,
    headers,
    credentials: 'same-origin',
  });

  if (response.status === 401 && authenticated && retryOn401) {
    const refreshed = await refreshAccessToken();
    if (refreshed) {
      return apiFetch<T>(path, options, { authenticated, retryOn401: false });
    }
    unauthorizedHandler?.();
  }

  if (!response.ok) {
    throw new ApiError(response.status, await parseErrorMessage(response));
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export async function getAuthStatus(): Promise<{ initialized: boolean }> {
  return apiFetch<{ initialized: boolean }>(
    '/api/auth/status',
    { method: 'GET' },
    { authenticated: false },
  );
}

export async function setupOwner(payload: {
  username: string;
  email: string;
  displayName: string;
  password: string;
}): Promise<AuthSessionPayload> {
  const res = await apiFetch<AuthSessionPayload>(
    '/api/auth/setup',
    { method: 'POST', body: JSON.stringify(payload) },
    { authenticated: false },
  );
  memoryAccessToken = res.accessToken;
  return res;
}

export async function login(payload: {
  identifier: string;
  password: string;
}): Promise<AuthSessionPayload> {
  const res = await apiFetch<AuthSessionPayload>(
    '/api/auth/login',
    { method: 'POST', body: JSON.stringify(payload) },
    { authenticated: false },
  );
  memoryAccessToken = res.accessToken;
  return res;
}

export async function registerWithInvite(payload: {
  inviteCode: string;
  username: string;
  email: string;
  displayName: string;
  password: string;
}): Promise<AuthSessionPayload> {
  const res = await apiFetch<AuthSessionPayload>(
    '/api/auth/register',
    { method: 'POST', body: JSON.stringify(payload) },
    { authenticated: false },
  );
  memoryAccessToken = res.accessToken;
  return res;
}

export async function logout(): Promise<void> {
  try {
    await fetch('/api/auth/logout', {
      method: 'POST',
      credentials: 'same-origin',
      headers: {
        'X-Atrium-CSRF': '1',
      },
    });
  } finally {
    memoryAccessToken = null;
  }
}

export async function getAccountMe(): Promise<AccountMePayload> {
  return apiFetch<AccountMePayload>('/api/account/me', { method: 'GET' });
}

export async function updateProfile(payload: {
  displayName: string;
  username: string;
  email: string;
  bio: string;
  timezone: string;
  avatarDataUrl: string | null;
}): Promise<UserProfile> {
  return apiFetch<UserProfile>('/api/account/profile', {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function updatePreferences(
  payload: AccountPreferences,
): Promise<AccountPreferences> {
  return apiFetch<AccountPreferences>('/api/account/preferences', {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function changePassword(payload: {
  currentPassword: string;
  newPassword: string;
}): Promise<{ passwordUpdatedAt: string }> {
  return apiFetch<{ passwordUpdatedAt: string }>(
    '/api/account/password',
    {
      method: 'POST',
      body: JSON.stringify(payload),
    },
    { retryOn401: false },
  );
}

export async function listSessions(): Promise<SessionItem[]> {
  return apiFetch<SessionItem[]>('/api/account/sessions', { method: 'GET' });
}

export async function revokeSession(id: string): Promise<void> {
  return apiFetch<void>(`/api/account/sessions/${id}`, { method: 'DELETE' });
}

export async function revokeOtherSessions(): Promise<void> {
  return apiFetch<void>('/api/account/sessions/revoke-others', {
    method: 'POST',
  });
}

export async function listTokens(): Promise<AgentToken[]> {
  return apiFetch<AgentToken[]>('/api/account/tokens', { method: 'GET' });
}

export async function createToken(
  name: string,
  scopes: string[],
): Promise<CreateTokenPayload> {
  return apiFetch<CreateTokenPayload>('/api/account/tokens', {
    method: 'POST',
    body: JSON.stringify({ name, scopes }),
  });
}

export async function revokeToken(id: string): Promise<void> {
  return apiFetch<void>(`/api/account/tokens/${id}`, { method: 'DELETE' });
}

export async function listInvites(): Promise<InviteItem[]> {
  return apiFetch<InviteItem[]>('/api/account/invites', { method: 'GET' });
}

export async function createInvite(
  label: string | null,
  expiresInDays = 7,
): Promise<CreateInvitePayload> {
  return apiFetch<CreateInvitePayload>('/api/account/invites', {
    method: 'POST',
    body: JSON.stringify({ label, expiresInDays }),
  });
}

export async function revokeInvite(id: string): Promise<void> {
  return apiFetch<void>(`/api/account/invites/${id}`, { method: 'DELETE' });
}

export async function listMembers(): Promise<HouseholdMember[]> {
  return apiFetch<HouseholdMember[]>('/api/account/members', { method: 'GET' });
}

export async function setMemberActive(
  id: string,
  isActive: boolean,
): Promise<HouseholdMember> {
  return apiFetch<HouseholdMember>(`/api/account/members/${id}`, {
    method: 'PATCH',
    body: JSON.stringify({ isActive }),
  });
}

export async function deleteMember(id: string): Promise<void> {
  return apiFetch<void>(`/api/account/members/${id}`, { method: 'DELETE' });
}

export function formatRelativeTime(iso: string | null | undefined): string {
  if (!iso) return 'Never';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  const diffSec = Math.round((Date.now() - date.getTime()) / 1000);
  if (diffSec < 60) return 'Just now';
  if (diffSec < 3600) return `${Math.floor(diffSec / 60)}m ago`;
  if (diffSec < 86400) return `${Math.floor(diffSec / 3600)}h ago`;
  if (diffSec < 86400 * 7) return `${Math.floor(diffSec / 86400)}d ago`;
  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  }).format(date);
}

