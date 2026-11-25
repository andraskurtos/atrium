import { useEffect, useMemo, useRef, useState } from 'react';
import type { ThemePreference } from '../types/atrium';
import { CloseIcon, SearchIcon } from './Icons';

export interface CommandItem {
  id: string;
  label: string;
  hint: string;
  group: string;
  onSelect: () => void;
}

interface CommandPaletteProps {
  isOpen: boolean;
  theme: ThemePreference;
  commands: CommandItem[];
  onClose: () => void;
}

export function CommandPalette({
  isOpen,
  commands,
  onClose,
}: CommandPaletteProps) {
  const [query, setQuery] = useState('');
  const dialogRef = useRef<HTMLDialogElement | null>(null);
  const inputRef = useRef<HTMLInputElement | null>(null);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;

    if (isOpen && !dialog.open) {
      setQuery('');
      dialog.showModal();
      requestAnimationFrame(() => inputRef.current?.focus());
    } else if (!isOpen && dialog.open) {
      dialog.close();
    }
  }, [isOpen]);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return commands;
    return commands.filter(
      (cmd) =>
        cmd.label.toLowerCase().includes(q) ||
        cmd.group.toLowerCase().includes(q) ||
        cmd.hint.toLowerCase().includes(q),
    );
  }, [commands, query]);

  return (
    <dialog
      ref={dialogRef}
      className="atrium-dialog command-dialog"
      aria-label="Command palette"
      onCancel={(e) => {
        e.preventDefault();
        onClose();
      }}
      onClick={(e) => {
        if (e.target === dialogRef.current) {
          onClose();
        }
      }}
    >
      <div className="command-search-row">
        <SearchIcon className="command-search-icon" />
        <input
          ref={inputRef}
          id="command-palette-input"
          type="text"
          className="command-input"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="Type a command or jump to a section…"
        />
        <button
          type="button"
          className="btn btn-ghost btn-icon-sm"
          onClick={onClose}
          aria-label="Close command palette"
        >
          <CloseIcon />
        </button>
      </div>

      <div className="command-list" role="listbox" aria-label="Available commands">
        {filtered.length === 0 ? (
          <p className="empty-state meta-text">No matching commands.</p>
        ) : (
          filtered.map((cmd) => (
            <button
              key={cmd.id}
              type="button"
              className="command-row"
              onClick={() => {
                cmd.onSelect();
                onClose();
              }}
            >
              <span className="command-row-main">
                <span className="row-title">{cmd.label}</span>
                <span className="meta-text">{cmd.group}</span>
              </span>
              <span className="meta-text mono-num">{cmd.hint}</span>
            </button>
          ))
        )}
      </div>
    </dialog>
  );
}
