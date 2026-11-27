import { useMemo, useState, type DragEvent, type FormEvent } from 'react';
import type { NoteSearchMode } from '../lib/notesClient';
import type { NoteFolder, NoteSummary, NoteTagCount } from '../types/atrium';
import {
  ChevronRightIcon,
  CloseIcon,
  EyeIcon,
  EyeOffIcon,
  NoteIcon,
  PlusIcon,
  SearchIcon,
} from './Icons';

interface NotesSidebarProps {
  isOpen: boolean;
  folders: NoteFolder[];
  notes: NoteSummary[];
  tags: NoteTagCount[];
  searchQuery: string;
  searchMode: NoteSearchMode;
  activeTag: string | null;
  searchResults: NoteSummary[] | null;
  searching: boolean;
  selectedNoteId: string | null;
  onCloseMobile: () => void;
  onSearchChange: (q: string) => void;
  onSearchModeChange: (mode: NoteSearchMode) => void;
  onSelectTag: (tag: string | null) => void;
  onSelectNote: (noteId: string) => void;
  onSelectSearchResult: (noteId: string) => void;
  onMoveNoteToFolder: (noteId: string, folderId: string | null) => Promise<void>;
  onCreateNote: (folderId: string | null) => Promise<void>;
  onCreateFolder: (name: string, parentFolderId: string | null) => Promise<void>;
  onRenameFolder: (folder: NoteFolder, name: string) => Promise<void>;
  onToggleFolderAgent: (folder: NoteFolder) => Promise<void>;
  onDeleteFolder: (folder: NoteFolder) => Promise<void>;
}

export function NotesSidebar({
  isOpen,
  folders,
  notes,
  tags,
  searchQuery,
  searchMode,
  activeTag,
  searchResults,
  searching,
  selectedNoteId,
  onCloseMobile,
  onSearchChange,
  onSearchModeChange,
  onSelectTag,
  onSelectNote,
  onSelectSearchResult,
  onMoveNoteToFolder,
  onCreateNote,
  onCreateFolder,
  onRenameFolder,
  onToggleFolderAgent,
  onDeleteFolder,
}: NotesSidebarProps) {
  const [expandedFolders, setExpandedFolders] = useState<Record<string, boolean>>({});
  const [creatingFolderParent, setCreatingFolderParent] = useState<string | null | undefined>(undefined);
  const [newFolderName, setNewFolderName] = useState('');
  const [renamingFolderId, setRenamingFolderId] = useState<string | null>(null);
  const [renameDraft, setRenameDraft] = useState('');
  const [confirmDeleteFolderId, setConfirmDeleteFolderId] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [draggingNoteId, setDraggingNoteId] = useState<string | null>(null);
  const [dropTargetFolderId, setDropTargetFolderId] = useState<string | 'root' | null>(null);

  const foldersByParent = useMemo(() => {
    const map = new Map<string | null, NoteFolder[]>();
    for (const folder of folders) {
      const key = folder.parentFolderId ?? null;
      const list = map.get(key) ?? [];
      list.push(folder);
      map.set(key, list);
    }
    return map;
  }, [folders]);

  const notesByFolder = useMemo(() => {
    const map = new Map<string | null, NoteSummary[]>();
    for (const note of notes) {
      const key = note.folderId ?? null;
      const list = map.get(key) ?? [];
      list.push(note);
      map.set(key, list);
    }
    return map;
  }, [notes]);

  const toggleExpand = (folderId: string) => {
    setExpandedFolders((prev) => ({
      ...prev,
      [folderId]: !(prev[folderId] ?? true),
    }));
  };

  const handleCreateFolderSubmit = async (e: FormEvent) => {
    e.preventDefault();
    const trimmed = newFolderName.trim();
    if (!trimmed || creatingFolderParent === undefined) return;
    const parentId = creatingFolderParent;
    setBusyId('new-folder');
    try {
      await onCreateFolder(trimmed, parentId);
      setNewFolderName('');
      setCreatingFolderParent(undefined);
      if (parentId) {
        setExpandedFolders((prev) => ({ ...prev, [parentId]: true }));
      }
    } finally {
      setBusyId(null);
    }
  };

  const handleRenameSubmit = async (folder: NoteFolder) => {
    const trimmed = renameDraft.trim();
    setRenamingFolderId(null);
    if (!trimmed || trimmed === folder.name) return;
    setBusyId(folder.id);
    try {
      await onRenameFolder(folder, trimmed);
    } finally {
      setBusyId(null);
    }
  };

  const isFiltering = Boolean(searchQuery.trim() || activeTag);
  const displayedSearchNotes = searchResults ?? [];

  const draggingNote = useMemo(
    () => (draggingNoteId ? notes.find((n) => n.id === draggingNoteId) ?? null : null),
    [draggingNoteId, notes],
  );

  const handleNoteDragStart = (e: DragEvent<HTMLButtonElement>, noteId: string) => {
    e.dataTransfer.setData('text/atrium-note-id', noteId);
    e.dataTransfer.effectAllowed = 'move';
    setDraggingNoteId(noteId);
  };

  const handleNoteDragEnd = () => {
    setDraggingNoteId(null);
    setDropTargetFolderId(null);
  };

  const handleFolderDragOver = (
    e: DragEvent<HTMLElement>,
    targetId: string | 'root',
  ) => {
    if (!e.dataTransfer.types.includes('text/atrium-note-id') && !draggingNoteId) {
      return;
    }
    e.preventDefault();
    e.dataTransfer.dropEffect = 'move';
    if (dropTargetFolderId !== targetId) {
      setDropTargetFolderId(targetId);
    }
  };

  const handleFolderDrop = (
    e: DragEvent<HTMLElement>,
    targetFolderId: string | null,
  ) => {
    e.preventDefault();
    const noteId =
      e.dataTransfer.getData('text/atrium-note-id') || draggingNoteId;
    setDraggingNoteId(null);
    setDropTargetFolderId(null);
    if (!noteId) return;

    const note = notes.find((n) => n.id === noteId);
    if (!note || note.folderId === targetFolderId) return;

    if (targetFolderId) {
      setExpandedFolders((prev) => ({ ...prev, [targetFolderId]: true }));
    }
    void onMoveNoteToFolder(noteId, targetFolderId);
  };

  const renderNoteButton = (note: NoteSummary, depth: number) => (
    <button
      key={note.id}
      type="button"
      draggable
      onDragStart={(e) => handleNoteDragStart(e, note.id)}
      onDragEnd={handleNoteDragEnd}
      className={`notes-tree-note ${selectedNoteId === note.id ? 'is-selected' : ''} ${
        draggingNoteId === note.id ? 'is-dragging' : ''
      }`}
      style={{ paddingLeft: `${12 + depth * 14}px` }}
      onClick={() => {
        onSelectNote(note.id);
        onCloseMobile();
      }}
      title={`${note.title} (drag into a folder to move)`}
    >
      <NoteIcon className="notes-tree-icon" />
      <span className="notes-tree-note-title">{note.title}</span>
      {!note.effectiveAllowAgentRead && (
        <span
          className="notes-tree-agent-hidden"
          title="Hidden from AI agents"
          aria-label="Hidden from AI agents"
        >
          <EyeOffIcon />
        </span>
      )}
    </button>
  );

  const renderFolderBranch = (folder: NoteFolder, depth: number) => {
    const isExpanded = expandedFolders[folder.id] ?? true;
    const childFolders = foldersByParent.get(folder.id) ?? [];
    const childNotes = notesByFolder.get(folder.id) ?? [];
    const isRenaming = renamingFolderId === folder.id;
    const isConfirmingDelete = confirmDeleteFolderId === folder.id;
    const isDropTarget = dropTargetFolderId === folder.id;

    return (
      <div key={folder.id} className="notes-tree-branch">
        <div
          className={`notes-tree-folder-row ${isDropTarget ? 'is-drop-target' : ''}`}
          style={{ paddingLeft: `${8 + depth * 14}px` }}
          onDragOver={(e) => handleFolderDragOver(e, folder.id)}
          onDragLeave={() => {
            if (dropTargetFolderId === folder.id) setDropTargetFolderId(null);
          }}
          onDrop={(e) => handleFolderDrop(e, folder.id)}
        >
          <button
            type="button"
            className={`notes-tree-chevron ${isExpanded ? 'is-expanded' : ''}`}
            aria-label={isExpanded ? `Collapse ${folder.name}` : `Expand ${folder.name}`}
            onClick={() => toggleExpand(folder.id)}
          >
            <ChevronRightIcon />
          </button>

          {isRenaming ? (
            <input
              type="text"
              className="input notes-tree-rename-input"
              value={renameDraft}
              maxLength={120}
              autoFocus
              aria-label="Rename folder"
              onChange={(e) => setRenameDraft(e.target.value)}
              onBlur={() => void handleRenameSubmit(folder)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') {
                  e.preventDefault();
                  void handleRenameSubmit(folder);
                } else if (e.key === 'Escape') {
                  e.preventDefault();
                  setRenamingFolderId(null);
                }
              }}
            />
          ) : (
            <button
              type="button"
              className="notes-tree-folder-name"
              onClick={() => toggleExpand(folder.id)}
              onDoubleClick={() => {
                setRenamingFolderId(folder.id);
                setRenameDraft(folder.name);
              }}
              title={`${folder.name} (double-click to rename)`}
            >
              <span>{folder.name}</span>
              {!folder.effectiveAllowAgentRead && (
                <span
                  className="notes-tree-agent-hidden"
                  title="Hidden from AI agents"
                >
                  <EyeOffIcon />
                </span>
              )}
            </button>
          )}

          <div className="notes-tree-folder-actions">
            <button
              type="button"
              className="btn btn-ghost btn-icon-sm"
              title={`New note in ${folder.name}`}
              aria-label={`New note in ${folder.name}`}
              disabled={busyId === folder.id}
              onClick={() => {
                setExpandedFolders((prev) => ({ ...prev, [folder.id]: true }));
                void onCreateNote(folder.id);
              }}
            >
              <PlusIcon />
            </button>
            <button
              type="button"
              className="btn btn-ghost btn-xs"
              title={`New subfolder in ${folder.name}`}
              aria-label={`New subfolder in ${folder.name}`}
              disabled={busyId === folder.id}
              onClick={() => {
                setExpandedFolders((prev) => ({ ...prev, [folder.id]: true }));
                setCreatingFolderParent(folder.id);
                setNewFolderName('');
              }}
            >
              +Sub
            </button>
            <button
              type="button"
              className="btn btn-ghost btn-icon-sm"
              title={
                folder.allowAgentRead
                  ? 'AI agents can read this folder (click to hide)'
                  : 'Hidden from AI agents (click to allow)'
              }
              aria-label={`Toggle AI agent access for ${folder.name}`}
              disabled={busyId === folder.id}
              onClick={() => {
                setBusyId(folder.id);
                void onToggleFolderAgent(folder).finally(() => setBusyId(null));
              }}
            >
              {folder.allowAgentRead ? <EyeIcon /> : <EyeOffIcon />}
            </button>
            <button
              type="button"
              className="btn btn-ghost btn-icon-sm"
              title={`Delete ${folder.name}`}
              aria-label={`Delete ${folder.name}`}
              disabled={busyId === folder.id}
              onClick={() =>
                setConfirmDeleteFolderId((prev) =>
                  prev === folder.id ? null : folder.id,
                )
              }
            >
              <CloseIcon />
            </button>
          </div>
        </div>

        {isConfirmingDelete && (
          <div className="notes-tree-confirm-bar">
            <span className="meta-text">Delete folder &amp; notes?</span>
            <button
              type="button"
              className="btn btn-danger btn-xs"
              disabled={busyId === folder.id}
              onClick={() => {
                setBusyId(folder.id);
                void onDeleteFolder(folder)
                  .then(() => setConfirmDeleteFolderId(null))
                  .finally(() => setBusyId(null));
              }}
            >
              Delete
            </button>
            <button
              type="button"
              className="btn btn-ghost btn-xs"
              onClick={() => setConfirmDeleteFolderId(null)}
            >
              Cancel
            </button>
          </div>
        )}

        {isExpanded && (
          <div className="notes-tree-children">
            {childFolders.map((sub) => renderFolderBranch(sub, depth + 1))}
            {childNotes.map((note) => renderNoteButton(note, depth + 1))}
          </div>
        )}
      </div>
    );
  };

  if (!isOpen) return null;

  const rootFolders = foldersByParent.get(null) ?? [];
  const rootNotes = notesByFolder.get(null) ?? [];

  return (
    <>
      <div
        className="notes-sidebar-backdrop"
        onClick={onCloseMobile}
        aria-hidden="true"
      />
      <aside className="notes-sidebar" aria-label="Notes vault navigation">
        <div className="notes-sidebar-top">
          <div className="notes-sidebar-actions">
            <button
              id="notes-new-note-btn"
              type="button"
              className="btn btn-primary btn-xs"
              onClick={() => void onCreateNote(null)}
            >
              <PlusIcon />
              <span>New note</span>
            </button>
            <button
              id="notes-new-folder-btn"
              type="button"
              className="btn btn-secondary btn-xs"
              onClick={() => {
                setCreatingFolderParent((prev) =>
                  prev === undefined ? null : undefined,
                );
                setNewFolderName('');
              }}
            >
              <span>New folder</span>
            </button>
          </div>

          {creatingFolderParent !== undefined && (
            <form
              className="notes-new-folder-form"
              onSubmit={(e) => void handleCreateFolderSubmit(e)}
            >
              <input
                type="text"
                className="input notes-new-folder-input"
                placeholder={
                  creatingFolderParent
                    ? `Subfolder in "${
                        folders.find((f) => f.id === creatingFolderParent)
                          ?.name ?? 'folder'
                      }"…`
                    : 'Folder name…'
                }
                maxLength={120}
                autoFocus
                value={newFolderName}
                onChange={(e) => setNewFolderName(e.target.value)}
              />
              <button
                type="submit"
                className="btn btn-primary btn-xs"
                disabled={!newFolderName.trim() || busyId === 'new-folder'}
              >
                Add
              </button>
              <button
                type="button"
                className="btn btn-ghost btn-xs"
                onClick={() => {
                  setCreatingFolderParent(undefined);
                  setNewFolderName('');
                }}
              >
                ×
              </button>
            </form>
          )}

          <div className="notes-search-wrap">
            <SearchIcon className="notes-search-icon" />
            <input
              id="notes-search-input"
              type="search"
              className="input notes-search-input"
              placeholder={
                searchMode === 'titles'
                  ? 'Search titles…'
                  : searchMode === 'content'
                    ? 'Search note content…'
                    : 'Search tags…'
              }
              value={searchQuery}
              onChange={(e) => onSearchChange(e.target.value)}
            />
          </div>

          <div
            className="notes-search-modes"
            role="group"
            aria-label="Search mode"
          >
            {(
              [
                { id: 'titles', label: 'Titles' },
                { id: 'content', label: 'Content' },
                { id: 'tags', label: 'Tags' },
              ] as const
            ).map((opt) => (
              <button
                key={opt.id}
                type="button"
                aria-pressed={searchMode === opt.id}
                className={`notes-search-mode-btn ${
                  searchMode === opt.id ? 'is-active' : ''
                }`}
                onClick={() => onSearchModeChange(opt.id)}
              >
                {opt.label}
              </button>
            ))}
          </div>

          {tags.length > 0 && (
            <div className="notes-sidebar-tags" aria-label="Filter by tag">
              {tags.slice(0, 12).map((t) => (
                <button
                  key={t.tag}
                  type="button"
                  className={`notes-tag-filter-chip ${
                    activeTag === t.tag ? 'is-active' : ''
                  }`}
                  onClick={() =>
                    onSelectTag(activeTag === t.tag ? null : t.tag)
                  }
                >
                  <span>#{t.tag}</span>
                  <span className="mono-num">{t.count}</span>
                </button>
              ))}
            </div>
          )}
        </div>

        <div className="notes-sidebar-scroll">
          {isFiltering ? (
            <div className="notes-search-results">
              <div className="notes-search-results-header">
                <span className="meta-text">
                  {searching
                    ? 'Searching…'
                    : `${displayedSearchNotes.length} ${
                        displayedSearchNotes.length === 1 ? 'result' : 'results'
                      }`}
                </span>
                <button
                  type="button"
                  className="btn btn-ghost btn-xs"
                  onClick={() => {
                    onSearchChange('');
                    onSelectTag(null);
                  }}
                >
                  Clear
                </button>
              </div>
              {displayedSearchNotes.length === 0 && !searching ? (
                <p className="meta-text empty-state">No matching notes.</p>
              ) : (
                displayedSearchNotes.map((note) => (
                  <button
                    key={note.id}
                    type="button"
                    className={`notes-search-result-card ${
                      selectedNoteId === note.id ? 'is-selected' : ''
                    }`}
                    onClick={() => {
                      onSelectSearchResult(note.id);
                      onCloseMobile();
                    }}
                  >
                    <span className="notes-tree-note-title">{note.title}</span>
                    {note.excerpt && (
                      <span className="meta-text notes-search-excerpt">
                        {note.excerpt}
                      </span>
                    )}
                  </button>
                ))
              )}
            </div>
          ) : folders.length === 0 && notes.length === 0 ? (
            <p className="meta-text empty-state">
              No notes yet. Create your first note above.
            </p>
          ) : (
            <div className="notes-tree-list">
              {rootFolders.map((folder) => renderFolderBranch(folder, 0))}
              {rootNotes.map((note) => renderNoteButton(note, 0))}
              {draggingNote && draggingNote.folderId !== null && (
                <div
                  className={`notes-root-drop-zone ${
                    dropTargetFolderId === 'root' ? 'is-drop-target' : ''
                  }`}
                  onDragOver={(e) => handleFolderDragOver(e, 'root')}
                  onDragLeave={() => {
                    if (dropTargetFolderId === 'root') setDropTargetFolderId(null);
                  }}
                  onDrop={(e) => handleFolderDrop(e, null)}
                >
                  Drop here to move to Unfiled
                </div>
              )}
            </div>
          )}
        </div>
      </aside>
    </>
  );
}

