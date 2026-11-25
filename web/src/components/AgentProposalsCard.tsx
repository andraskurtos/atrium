import { useState } from 'react';
import type { AgentProposal } from '../types/atrium';

interface AgentProposalsCardProps {
  proposals: AgentProposal[];
  onApprove: (proposal: AgentProposal) => void;
  onDismiss: (id: string) => void;
}

export function AgentProposalsCard({
  proposals,
  onApprove,
  onDismiss,
}: AgentProposalsCardProps) {
  const [expandedId, setExpandedId] = useState<string | null>(null);

  return (
    <section className="card" aria-labelledby="proposals-heading">
      <div className="card-header">
        <div className="card-header-group">
          <h2 id="proposals-heading" className="card-title">
            Agent Proposals
          </h2>
          <span className="pill-accent">MCP</span>
        </div>

        <span className="status-inline">
          <span
            className="status-dot"
            data-state={proposals.length > 0 ? 'degraded' : 'ok'}
            aria-hidden="true"
          />
          <span className="meta-text mono-num">
            {proposals.length > 0 ? `${proposals.length} pending` : 'All reviewed'}
          </span>
        </span>
      </div>

      {proposals.length === 0 ? (
        <p className="empty-state meta-text">
          No pending agent proposals. Incoming MCP drafts will appear here for your
          review.
        </p>
      ) : (
        <div className="stack-sm">
          {proposals.map((proposal) => {
            const isExpanded = expandedId === proposal.id;
            return (
              <article key={proposal.id} className="card-soft proposal-item">
                <div className="proposal-meta">
                  <span className="status-inline">
                    <span
                      className="status-dot"
                      data-state="degraded"
                      aria-hidden="true"
                    />
                    <span className="proposal-source">{proposal.source}</span>
                  </span>
                  <span className="meta-text mono-num">{proposal.relativeTime}</span>
                </div>

                <h3 className="row-title">{proposal.title}</h3>
                <p className="meta-text proposal-summary">{proposal.summary}</p>

                {isExpanded && (
                  <p className="proposal-details mono-num">{proposal.details}</p>
                )}

                <div className="proposal-actions">
                  <button
                    id={`proposal-approve-${proposal.id}`}
                    type="button"
                    className="btn btn-primary btn-compact"
                    onClick={() => onApprove(proposal)}
                  >
                    Approve
                  </button>
                  <button
                    id={`proposal-review-${proposal.id}`}
                    type="button"
                    className="btn btn-secondary btn-compact"
                    aria-expanded={isExpanded}
                    onClick={() =>
                      setExpandedId(isExpanded ? null : proposal.id)
                    }
                  >
                    {isExpanded ? 'Hide Details' : 'Review'}
                  </button>
                  <button
                    id={`proposal-dismiss-${proposal.id}`}
                    type="button"
                    className="btn btn-ghost btn-compact"
                    onClick={() => onDismiss(proposal.id)}
                  >
                    Dismiss
                  </button>
                </div>
              </article>
            );
          })}
        </div>
      )}
    </section>
  );
}
