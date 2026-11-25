import { useEffect, useState } from 'react';

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

export function App() {
  const [apiState, setApiState] = useState<ApiState>('checking');

  useEffect(() => {
    const controller = new AbortController();

    fetch('/api/health', { signal: controller.signal })
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
  }, []);

  return (
    <main className="shell">
      <section className="card" aria-label="Atrium status">
        <img className="mark" src="/icon.svg" alt="Atrium emblem" width={56} height={56} />
        <h1 className="title">Atrium</h1>
        <p className="subtitle">Your private personal hub. Ready to build.</p>
        <div className="status" role="status" aria-live="polite">
          <span className="dot" data-state={apiState} aria-hidden="true" />
          <span>{STATUS_LABELS[apiState]}</span>
        </div>
      </section>
    </main>
  );
}
