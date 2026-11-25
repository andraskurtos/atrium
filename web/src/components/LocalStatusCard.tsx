import type { ApiState } from '../types/atrium';
import { RefreshIcon } from './Icons';

interface LocalStatusCardProps {
  apiState: ApiState;
  label: string;
  lastChecked: string;
  onRefresh: () => void;
}

export function LocalStatusCard({
  apiState,
  label,
  lastChecked,
  onRefresh,
}: LocalStatusCardProps) {
  return (
    <section className="card" aria-labelledby="local-status-heading">
      <div className="card-header">
        <h2 id="local-status-heading" className="card-title">
          Local Status
        </h2>
        <button
          id="local-status-refresh"
          type="button"
          className="btn btn-ghost btn-icon-sm"
          onClick={onRefresh}
          aria-label="Refresh local API status"
          title="Refresh status"
        >
          <RefreshIcon />
        </button>
      </div>

      <div className="card-soft status-card-body" role="status" aria-live="polite">
        <div className="status-inline">
          <span className="status-dot" data-state={apiState} aria-hidden="true" />
          <span className="row-title">{label}</span>
        </div>
        <div className="status-meta-row">
          <span className="meta-text">Endpoint /api/health</span>
          <span className="meta-text mono-num">{lastChecked}</span>
        </div>
      </div>
    </section>
  );
}
