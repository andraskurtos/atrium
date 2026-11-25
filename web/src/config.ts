const API_BASE_URL_STORAGE_KEY = 'atrium.apiBaseUrl';

export function normalizeApiBaseUrl(raw: string): string {
  const trimmed = raw.trim();
  if (!trimmed) {
    return '';
  }

  let parsed: URL;
  try {
    parsed = new URL(trimmed);
  } catch {
    throw new Error('Enter a full URL starting with https:// or http://');
  }

  if (parsed.protocol !== 'https:' && parsed.protocol !== 'http:') {
    throw new Error('Server URL must use https:// or http://');
  }

  const pathname = parsed.pathname.replace(/\/+$/, '');
  return `${parsed.origin}${pathname}`;
}

export function getBuildApiBaseUrl(): string {
  const raw = import.meta.env.VITE_API_BASE_URL ?? '';
  try {
    return normalizeApiBaseUrl(raw);
  } catch {
    return '';
  }
}

export function getSavedApiBaseUrl(): string {
  try {
    const stored = window.localStorage.getItem(API_BASE_URL_STORAGE_KEY);
    return stored ? normalizeApiBaseUrl(stored) : '';
  } catch {
    return '';
  }
}

export function getApiBaseUrl(): string {
  return getSavedApiBaseUrl() || getBuildApiBaseUrl();
}

export function saveApiBaseUrl(raw: string): string {
  const normalized = normalizeApiBaseUrl(raw);
  try {
    if (normalized) {
      window.localStorage.setItem(API_BASE_URL_STORAGE_KEY, normalized);
    } else {
      window.localStorage.removeItem(API_BASE_URL_STORAGE_KEY);
    }
  } catch {
    // Ignore storage quota / privacy-mode failures; caller still gets normalized value.
  }
  return normalized;
}

export function clearSavedApiBaseUrl(): void {
  try {
    window.localStorage.removeItem(API_BASE_URL_STORAGE_KEY);
  } catch {
    // Ignore storage errors.
  }
}

export function buildApiUrl(path: string, baseUrl: string = getApiBaseUrl()): string {
  const normalizedPath = path.startsWith('/') ? path : `/${path}`;
  return baseUrl ? `${baseUrl}${normalizedPath}` : normalizedPath;
}
