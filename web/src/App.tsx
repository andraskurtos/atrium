import { type FormEvent, useEffect, useState } from 'react';
import {
  buildApiUrl,
  clearSavedApiBaseUrl,
  getApiBaseUrl,
  getBuildApiBaseUrl,
  getSavedApiBaseUrl,
  saveApiBaseUrl,
} from './config';

type ApiState = 'checking' | 'ok' | 'degraded' | 'offline';

interface HealthPayload {
  status?: string;
  database?: string;
}

const STATUS_LABELS: Record<ApiState, string> = {
  checking: 'Checking API…',
  ok: 'API & Postgres connected',
  degraded: 'API online · Postgres unreachable',
  offline: 'API offline',
};

function formatServerLabel(baseUrl: string): string {
  if (!baseUrl) return 'Same origin';
  try {
    return new URL(baseUrl).host;
  } catch {
    return baseUrl;
  }
}

export function App() {
  const [apiBaseUrl, setApiBaseUrl] = useState<string>(() => getApiBaseUrl());
  const [apiState, setApiState] = useState<ApiState>('checking');
  const [checkNonce, setCheckNonce] = useState(0);
  const [showServerForm, setShowServerForm] = useState(false);
  const [draftUrl, setDraftUrl] = useState<string>(() => getApiBaseUrl());
  const [formError, setFormError] = useState<string | null>(null);

  const hasSavedOverride = Boolean(getSavedApiBaseUrl());
  const iconSrc = `${import.meta.env.BASE_URL}icon.svg`;

  useEffect(() => {
    const controller = new AbortController();
    setApiState('checking');

    fetch(buildApiUrl('/api/health', apiBaseUrl), { signal: controller.signal })
      .then(async (res) => {
        const data = (await res.json().catch(() => ({}))) as HealthPayload;
        if (res.ok && data.status === 'ok') {
          setApiState('ok');
        } else if (res.status === 503 && data.status === 'degraded') {
          setApiState('degraded');
        } else {
          setApiState('offline');
        }
      })
      .catch((err: unknown) => {
        if (err instanceof DOMException && err.name === 'AbortError') return;
        setApiState('offline');
      });

    return () => controller.abort();
  }, [apiBaseUrl, checkNonce]);

  function handleSaveServer(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    try {
      const normalized = saveApiBaseUrl(draftUrl);
      setFormError(null);
      setApiBaseUrl(normalized || getBuildApiBaseUrl());
      setDraftUrl(normalized || getBuildApiBaseUrl());
      setShowServerForm(false);
      setCheckNonce((n) => n + 1);
    } catch (err: unknown) {
      setFormError(err instanceof Error ? err.message : 'Invalid server URL');
    }
  }

  function handleResetServer() {
    clearSavedApiBaseUrl();
    const fallback = getBuildApiBaseUrl();
    setFormError(null);
    setApiBaseUrl(fallback);
    setDraftUrl(fallback);
    setShowServerForm(false);
    setCheckNonce((n) => n + 1);
  }

  return (
    <main className="shell">
      <section className="card" aria-label="Atrium status">
        <img className="mark" src={iconSrc} alt="Atrium emblem" width={56} height={56} />
        <h1 className="title">Atrium</h1>
        <p className="subtitle">Your private personal hub. Ready to build.</p>
        <div className="status" role="status" aria-live="polite">
          <span className="dot" data-state={apiState} aria-hidden="true" />
          <span>{STATUS_LABELS[apiState]}</span>
        </div>

        <div className="server-meta">
          <span className="server-host" title={apiBaseUrl || window.location.origin}>
            Server: {formatServerLabel(apiBaseUrl)}
          </span>
          <button
            type="button"
            className="text-button"
            onClick={() => {
              setDraftUrl(apiBaseUrl);
              setFormError(null);
              setShowServerForm((open) => !open);
            }}
            aria-expanded={showServerForm}
          >
            {showServerForm ? 'Close' : 'Configure'}
          </button>
        </div>

        {showServerForm && (
          <form className="server-form" onSubmit={handleSaveServer} noValidate>
            <label className="sr-only" htmlFor="server-url-input">
              Backend server URL
            </label>
            <input
              id="server-url-input"
              className="server-input"
              type="url"
              inputMode="url"
              placeholder="https://api.example.com"
              value={draftUrl}
              onChange={(e) => setDraftUrl(e.target.value)}
            />
            {formError && (
              <p className="server-error" role="alert">
                {formError}
              </p>
            )}
            <div className="server-actions">
              <button type="submit" className="btn-primary">
                Connect
              </button>
              {hasSavedOverride && (
                <button type="button" className="btn-secondary" onClick={handleResetServer}>
                  Reset
                </button>
              )}
            </div>
          </form>
        )}
      </section>
    </main>
  );
}
