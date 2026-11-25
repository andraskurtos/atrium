import { useEffect, useRef, useState } from 'react';
import type { VaultItem } from '../types/atrium';
import { CloseIcon, VaultIcon } from './Icons';

interface VaultCardProps {
  items: VaultItem[];
}

export function VaultCard({ items }: VaultCardProps) {
  const [activeItem, setActiveItem] = useState<VaultItem | null>(null);
  const dialogRef = useRef<HTMLDialogElement | null>(null);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (activeItem && !dialog.open) {
      dialog.showModal();
    } else if (!activeItem && dialog.open) {
      dialog.close();
    }
  }, [activeItem]);

  return (
    <>
      <section className="card" aria-labelledby="vault-heading">
        <div className="card-header">
          <h2 id="vault-heading" className="card-title">
            Pinned Vault
          </h2>
          <span className="pill-neutral mono-num">{items.length}</span>
        </div>

        <div className="stack-sm">
          {items.map((item) => (
            <button
              key={item.id}
              id={`vault-item-${item.id}`}
              type="button"
              className="card-soft vault-row"
              onClick={() => setActiveItem(item)}
            >
              <span className="vault-icon">
                <VaultIcon />
              </span>
              <span className="vault-body">
                <span className="row-title">{item.title}</span>
                <span className="meta-text">
                  {item.format} · {item.updatedAt}
                </span>
              </span>
            </button>
          ))}
        </div>
      </section>

      <dialog
        ref={dialogRef}
        className="atrium-dialog vault-dialog"
        aria-labelledby="vault-dialog-title"
        onCancel={(e) => {
          e.preventDefault();
          setActiveItem(null);
        }}
        onClick={(e) => {
          if (e.target === dialogRef.current) {
            setActiveItem(null);
          }
        }}
      >
        {activeItem && (
          <>
            <div className="dialog-header">
              <div>
                <p className="kicker">
                  VAULT · {activeItem.format.toUpperCase()} · {activeItem.updatedAt.toUpperCase()}
                </p>
                <h2 id="vault-dialog-title" className="dialog-title">
                  {activeItem.title}
                </h2>
              </div>
              <button
                type="button"
                className="btn btn-ghost btn-icon-sm"
                onClick={() => setActiveItem(null)}
                aria-label="Close vault document"
              >
                <CloseIcon />
              </button>
            </div>
            <p className="meta-text vault-dialog-summary">{activeItem.summary}</p>
            <div className="vault-dialog-body">
              {activeItem.body.map((paragraph, index) => (
                <p key={index}>{paragraph}</p>
              ))}
            </div>
          </>
        )}
      </dialog>
    </>
  );
}
