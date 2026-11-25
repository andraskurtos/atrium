import { useCallback, useEffect, useState } from 'react';
import type { ApiState } from '../types/atrium';

interface HealthPayload {
  status?: string;
  database?: string;
}

export const API_STATUS_LABELS: Record<ApiState, string> = {
  checking: 'Checking local API…',
  ok: 'API & Postgres connected',
  degraded: 'API online · Postgres unreachable',
  offline: 'API offline · Local preview mode',
};

export function useApiHealth() {
  const [apiState, setApiState] = useState<ApiState>('checking');
  const [lastChecked, setLastChecked] = useState<string>('--:--');

  const checkHealth = useCallback((signal?: AbortSignal) => {
    setApiState('checking');

    fetch('/api/health', { signal })
      .then(async (res) => {
        const data = (await res.json().catch(() => ({}))) as HealthPayload;
        if (res.ok && data.status === 'ok') {
          setApiState('ok');
        } else if (res.status === 503 && data.status === 'degraded') {
          setApiState('degraded');
        } else {
          setApiState('offline');
        }
        setLastChecked(
          new Intl.DateTimeFormat('en-US', {
            hour: '2-digit',
            minute: '2-digit',
            hour12: false,
          }).format(new Date()),
        );
      })
      .catch((err: unknown) => {
        if (err instanceof DOMException && err.name === 'AbortError') return;
        setApiState('offline');
        setLastChecked(
          new Intl.DateTimeFormat('en-US', {
            hour: '2-digit',
            minute: '2-digit',
            hour12: false,
          }).format(new Date()),
        );
      });
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    checkHealth(controller.signal);
    return () => controller.abort();
  }, [checkHealth]);

  return {
    apiState,
    label: API_STATUS_LABELS[apiState],
    lastChecked,
    refresh: () => checkHealth(),
  };
}
