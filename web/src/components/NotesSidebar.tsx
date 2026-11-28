import {
  useMemo,
  useRef,
  useState,
  type ChangeEvent,
  type DragEvent,
  type FormEvent,
} from 'react';
import {
  downloadAttachment,
  isPdfAttachment,
  type UploadAttachmentOptions,
} from '../lib/attachmentsClient';
import type { NoteSearchMode } from '../lib/notesClient';
import type {
  AttachmentSummary,
  EventNotesGroup,
  NoteFolder,
  NoteSummary,
  NoteTagCount,
  UniversitySemesterNotesTree,
} from '../types/atrium';
import {
  ChevronRightIcon,
  CloseIcon,
  EyeIcon,
  EyeOffIcon,
  NoteIcon,
  PlusIcon,
  SearchIcon,
  UploadIcon,
} from './Icons';

const SHOW_UNI_KEY = 'atrium.notes.sidebar.showUni';
const SHOW_EVENTS_KEY = 'atrium.notes.sidebar.showEvents';
const SHOW_DAILY_KEY = 'atrium.notes.sidebar.showDaily';

function readBoolPref(key: string, fallback: boolean): boolean {
  if (typeof window === 'undefined') return fallback;
  try {
    const raw = window.localStorage.getItem(key);
    return raw === null ? fallback : raw === 'true';
  } catch {
    return fallback;
  }
}

function formatShortOccurrenceDate(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso.slice(0, 10);
  return d.toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  });
}

interface NotesSidebarProps {
  isOpen: boolean;
  folders: NoteFolder[];
  notes: NoteSummary[];
  university: UniversitySemesterNotesTree[];
  events: EventNotesGroup[];
  daily: NoteSummary[];
  tags: NoteTagCount[];
  searchQuery: string;
  searchMode: NoteSearchMode;
  selectedTags: string[];
  searchResults: NoteSummary[] | null;
  searching: boolean;
  selectedNoteId: string | null;
  onCloseMobile: () => void;
  onSearchChange: (q: string) => void;
  onSearchModeChange: (mode: NoteSearchMode) => void;
  onChangeSelectedTags: (tags: string[]) => void;
  onSelectNote: (noteId: string) => void;
  onSelectSearchResult: (noteId: string) => void;
  onCreateUniversityItemNote: (itemId: string) => Promise<void>;
  onViewPdf: (artifact: AttachmentSummary) => void;
  onOpenNoteSplit: (noteId: string) => void;
  onCreateCourseNote: (courseId: string) => Promise<void>;
  onUploadArtifact: (
    file: File,
    owner: Omit<UploadAttachmentOptions, 'kind'>,
  ) => Promise<void>;
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
  university,
  events,
  daily,
  tags,
  searchQuery,
  searchMode,
  selectedTags,
  searchResults,
  searching,
  selectedNoteId,
  onCloseMobile,
  onSearchChange,
  onSearchModeChange,
  onChangeSelectedTags,
  onSelectNote,
  onSelectSearchResult,
  onCreateUniversityItemNote,
  onViewPdf,
  onOpenNoteSplit,
  onCreateCourseNote,
  onUploadArtifact,
  onMoveNoteToFolder,
  onCreateNote,
  onCreateFolder,
  onRenameFolder,
  onToggleFolderAgent,
  onDeleteFolder,
}: NotesSidebarProps) {
  const [showUni, setShowUni] = useState<boolean>(() =>
    readBoolPref(SHOW_UNI_KEY, true),
  );
  const [showEvents, setShowEvents] = useState<boolean>(() =>
    readBoolPref(SHOW_EVENTS_KEY, false),
  );
  const [showDaily, setShowDaily] = useState<boolean>(() =>
    readBoolPref(SHOW_DAILY_KEY, true),
  );
  const [expandedNodes, setExpandedNodes] = useState<Record<string, boolean>>({});
  const [creatingFolderParent, setCreatingFolderParent] = useState<
    string | null | undefined
  >(undefined);
  const [newFolderName, setNewFolderName] = useState('');
  const [renamingFolderId, setRenamingFolderId] = useState<string | null>(null);
  const [renameDraft, setRenameDraft] = useState('');
  const [confirmDeleteFolderId, setConfirmDeleteFolderId] = useState<
    string | null
  >(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [draggingNoteId, setDraggingNoteId] = useState<string | null>(null);
  const [dropTargetFolderId, setDropTargetFolderId] = useState<
    string | 'root' | null
  >(null);
  const [fileDropTargetKey, setFileDropTargetKey] = useState<string | null>(
    null,
  );
  const [selectedContainer, setSelectedContainer] = useState<
    | { kind: 'folder'; folderId: string; label: string }
    | { kind: 'course'; courseId: string; label: string }
    | { kind: 'university_item'; itemId: string; label: string }
    | null
  >(null);

  const handleTopNewNote = () => {
    if (!selectedContainer) {
      void onCreateNote(null);
      return;
    }
    if (selectedContainer.kind === 'folder') {
      setExpandedNodes((prev) => ({ ...prev, [selectedContainer.folderId]: true }));
      void onCreateNote(selectedContainer.folderId);
    } else if (selectedContainer.kind === 'course') {
      setExpandedNodes((prev) => ({
        ...prev,
        [`uni:course:${selectedContainer.courseId}`]: true,
      }));
      void onCreateCourseNote(selectedContainer.courseId);
    } else {
      setExpandedNodes((prev) => ({
        ...prev,
        [`uni:item:${selectedContainer.itemId}`]: true,
      }));
      void onCreateUniversityItemNote(selectedContainer.itemId);
    }
  };

  const artifactUploadInputRef = useRef<HTMLInputElement | null>(null);
  const pendingUploadOwnerRef = useRef<Omit<
    UploadAttachmentOptions,
    'kind'
  > | null>(null);

  const toggleShowUni = () => {
    setShowUni((prev) => {
      const next = !prev;
      try {
        window.localStorage.setItem(SHOW_UNI_KEY, String(next));
      } catch {
        // ignore
      }
      return next;
    });
  };

  const toggleShowEvents = () => {
    setShowEvents((prev) => {
      const next = !prev;
      try {
        window.localStorage.setItem(SHOW_EVENTS_KEY, String(next));
      } catch {
        // ignore
      }
      return next;
    });
  };

  const toggleShowDaily = () => {
    setShowDaily((prev) => {
      const next = !prev;
      try {
        window.localStorage.setItem(SHOW_DAILY_KEY, String(next));
      } catch {
        // ignore
      }
      return next;
    });
  };

  const triggerArtifactUpload = (
    owner: Omit<UploadAttachmentOptions, 'kind'>,
  ) => {
    pendingUploadOwnerRef.current = owner;
    artifactUploadInputRef.current?.click();
  };

  const handleArtifactFilePicked = (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = '';
    const owner = pendingUploadOwnerRef.current;
    pendingUploadOwnerRef.current = null;
    if (!file || !owner) return;
    void onUploadArtifact(file, owner);
  };

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

  const isNodeExpanded = (key: string, defaultOpen = true): boolean =>
    expandedNodes[key] ?? defaultOpen;

  const toggleExpand = (key: string, defaultOpen = true) => {
    setExpandedNodes((prev) => ({
      ...prev,
      [key]: !(prev[key] ?? defaultOpen),
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
        setExpandedNodes((prev) => ({ ...prev, [parentId]: true }));
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

  const isFiltering = Boolean(searchQuery.trim() || selectedTags.length > 0);

  const MAX_VISIBLE_TAG_SUGGESTIONS = 8;
  const matchingTags = useMemo(() => {
    const q = searchQuery.trim().toLowerCase().replace(/^#/, '');
    if (!q) return tags;
    return tags.filter((t) => t.tag.toLowerCase().includes(q));
  }, [searchQuery, tags]);

  const selectedTagSet = useMemo(() => new Set(selectedTags), [selectedTags]);
  const allMatchingTagsSelected =
    matchingTags.length > 0 &&
    matchingTags.every((t) => selectedTagSet.has(t.tag));

  const toggleTagSelection = (tag: string) => {
    if (selectedTagSet.has(tag)) {
      onChangeSelectedTags(selectedTags.filter((t) => t !== tag));
    } else {
      onChangeSelectedTags([...selectedTags, tag]);
    }
  };

  const handleToggleAllMatchingTags = () => {
    if (matchingTags.length === 0) return;
    if (allMatchingTagsSelected) {
      const matchSet = new Set(matchingTags.map((t) => t.tag));
      onChangeSelectedTags(selectedTags.filter((t) => !matchSet.has(t)));
    } else {
      const next = new Set(selectedTags);
      for (const t of matchingTags) next.add(t.tag);
      onChangeSelectedTags(Array.from(next));
    }
  };
  const displayedSearchNotes = searchResults ?? [];

  const draggingNote = useMemo(
    () =>
      draggingNoteId ? notes.find((n) => n.id === draggingNoteId) ?? null : null,
    [draggingNoteId, notes],
  );

  const handleNoteDragStart = (
    e: DragEvent<HTMLButtonElement>,
    noteId: string,
  ) => {
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
    if (
      !e.dataTransfer.types.includes('text/atrium-note-id') &&
      !draggingNoteId
    ) {
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
      setExpandedNodes((prev) => ({ ...prev, [targetFolderId]: true }));
    }
    void onMoveNoteToFolder(noteId, targetFolderId);
  };

  const ownerForNote = (
    note: NoteSummary,
  ): Omit<UploadAttachmentOptions, 'kind'> => {
    if (note.universityActivityItemId) {
      return { universityActivityItemId: note.universityActivityItemId };
    }
    if (note.calendarEventId) {
      return {
        calendarEventId: note.calendarEventId,
        calendarOccurrenceStartAt: note.calendarOccurrenceStartAt ?? null,
      };
    }
    return { noteId: note.id };
  };

  const fileDropProps = (
    key: string,
    owner: Omit<UploadAttachmentOptions, 'kind'>,
  ) => ({
    'data-file-drop': fileDropTargetKey === key ? 'true' : undefined,
    onDragOver: (e: DragEvent<HTMLElement>) => {
      if (!e.dataTransfer.types.includes('Files')) return;
      e.preventDefault();
      e.stopPropagation();
      e.dataTransfer.dropEffect = 'copy';
      if (fileDropTargetKey !== key) setFileDropTargetKey(key);
    },
    onDragLeave: () => {
      if (fileDropTargetKey === key) setFileDropTargetKey(null);
    },
    onDrop: (e: DragEvent<HTMLElement>) => {
      if (e.dataTransfer.files.length === 0) return;
      e.preventDefault();
      e.stopPropagation();
      setFileDropTargetKey(null);
      const files = Array.from(e.dataTransfer.files);
      void (async () => {
        for (const file of files) {
          await onUploadArtifact(file, owner);
        }
      })();
    },
  });

  const renderArtifactButton = (artifact: AttachmentSummary, depth: number) => {
    const isPdf = isPdfAttachment(artifact);
    return (
      <div key={artifact.id} className="notes-tree-artifact-row">
        <button
          type="button"
          className="notes-tree-note notes-tree-artifact"
          style={{ paddingLeft: `${12 + depth * 12}px` }}
          onClick={() =>
            isPdf
              ? onViewPdf(artifact)
              : void downloadAttachment(artifact.id, artifact.fileName)
          }
          title={
            isPdf
              ? `View ${artifact.fileName} beside your note`
              : `Download ${artifact.fileName}`
          }
        >
          <span className="notes-tree-artifact-glyph" aria-hidden="true">
            📎
          </span>
          <span className="notes-tree-note-title">{artifact.fileName}</span>
        </button>
        {isPdf && (
          <button
            type="button"
            className="btn btn-ghost btn-icon-sm notes-tree-artifact-download"
            title={`Download ${artifact.fileName}`}
            aria-label={`Download ${artifact.fileName}`}
            onClick={() =>
              void downloadAttachment(artifact.id, artifact.fileName)
            }
          >
            ↓
          </button>
        )}
      </div>
    );
  };

  const renderNoteButton = (
    note: NoteSummary,
    depth: number,
    allowDrag = true,
  ) => (
    <button
      key={note.id}
      type="button"
      draggable={allowDrag}
      onDragStart={
        allowDrag ? (e) => handleNoteDragStart(e, note.id) : undefined
      }
      onDragEnd={allowDrag ? handleNoteDragEnd : undefined}
      className={`notes-tree-note ${selectedNoteId === note.id ? 'is-selected' : ''} ${
        draggingNoteId === note.id ? 'is-dragging' : ''
      }`}
      {...fileDropProps(`note:${note.id}`, ownerForNote(note))}
      style={{ paddingLeft: `${12 + depth * 12}px` }}
      onClick={(e) => {
        if (e.shiftKey) {
          onOpenNoteSplit(note.id);
          return;
        }
        onSelectNote(note.id);
        onCloseMobile();
      }}
      title={
        allowDrag
          ? `${note.title} (drag into a folder to move, Shift+click to open beside)`
          : note.title
      }
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
    const isExpanded = isNodeExpanded(folder.id, true);
    const childFolders = foldersByParent.get(folder.id) ?? [];
    const childNotes = notesByFolder.get(folder.id) ?? [];
    const isRenaming = renamingFolderId === folder.id;
    const isConfirmingDelete = confirmDeleteFolderId === folder.id;
    const isDropTarget = dropTargetFolderId === folder.id;
    const isContainerSelected =
      selectedContainer?.kind === 'folder' &&
      selectedContainer.folderId === folder.id;

    return (
      <div key={folder.id} className="notes-tree-branch">
        <div
          className={`notes-tree-folder-row ${isDropTarget ? 'is-drop-target' : ''} ${
            isContainerSelected ? 'is-selected-container' : ''
          }`}
          style={{ paddingLeft: `${8 + depth * 12}px` }}
          onDragOver={(e) => handleFolderDragOver(e, folder.id)}
          onDragLeave={() => {
            if (dropTargetFolderId === folder.id) setDropTargetFolderId(null);
          }}
          onDrop={(e) => handleFolderDrop(e, folder.id)}
        >
          <button
            type="button"
            className={`notes-tree-chevron ${isExpanded ? 'is-expanded' : ''}`}
            aria-label={
              isExpanded ? `Collapse ${folder.name}` : `Expand ${folder.name}`
            }
            onClick={() => toggleExpand(folder.id, true)}
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
              onClick={() => {
                setSelectedContainer({
                  kind: 'folder',
                  folderId: folder.id,
                  label: folder.name,
                });
                toggleExpand(folder.id, true);
              }}
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
                setExpandedNodes((prev) => ({ ...prev, [folder.id]: true }));
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
                setExpandedNodes((prev) => ({ ...prev, [folder.id]: true }));
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
            {childNotes.map((note) => renderNoteButton(note, depth + 1, true))}
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
        <input
          ref={artifactUploadInputRef}
          type="file"
          hidden
          onChange={handleArtifactFilePicked}
        />

        <div className="notes-sidebar-top">
          <div className="notes-sidebar-actions">
            <button
              id="notes-new-note-btn"
              type="button"
              className="btn btn-primary btn-xs"
              onClick={handleTopNewNote}
              title={
                selectedContainer
                  ? `New note in ${selectedContainer.label}`
                  : 'New note'
              }
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

          <div
            className="notes-section-toggles"
            role="group"
            aria-label="Sidebar sections"
          >
            <button
              type="button"
              aria-pressed={showUni}
              className={`notes-section-toggle-chip ${showUni ? 'is-active' : ''}`}
              onClick={toggleShowUni}
            >
              University
            </button>
            <button
              type="button"
              aria-pressed={showEvents}
              className={`notes-section-toggle-chip ${showEvents ? 'is-active' : ''}`}
              onClick={toggleShowEvents}
            >
              Events
            </button>
            <button
              type="button"
              aria-pressed={showDaily}
              className={`notes-section-toggle-chip ${showDaily ? 'is-active' : ''}`}
              onClick={toggleShowDaily}
            >
              Daily
            </button>
          </div>

          {searchMode === 'tags' && tags.length > 0 && (
            <div
              className="notes-tag-suggestions"
              aria-label="Tag search suggestions"
            >
              <div className="notes-tag-suggestions-header">
                <span className="meta-text">
                  {matchingTags.length === 0
                    ? 'No matching tags'
                    : selectedTags.length > 0
                      ? `${selectedTags.length} selected · ${matchingTags.length} matching`
                      : `${matchingTags.length} matching ${matchingTags.length === 1 ? 'tag' : 'tags'}`}
                </span>
                {matchingTags.length > 0 && (
                  <button
                    type="button"
                    className="btn btn-ghost btn-xs notes-tag-select-all-btn"
                    onClick={handleToggleAllMatchingTags}
                  >
                    {allMatchingTagsSelected ? 'Deselect all' : 'Select all'}
                  </button>
                )}
              </div>

              {matchingTags.length > 0 && (
                <div className="notes-tag-suggestions-list">
                  {matchingTags
                    .slice(0, MAX_VISIBLE_TAG_SUGGESTIONS)
                    .map((t) => {
                      const checked = selectedTagSet.has(t.tag);
                      return (
                        <label
                          key={t.tag}
                          className={`notes-tag-suggestion-row ${checked ? 'is-checked' : ''}`}
                        >
                          <input
                            type="checkbox"
                            checked={checked}
                            onChange={() => toggleTagSelection(t.tag)}
                          />
                          <span className="notes-tag-suggestion-name">
                            #{t.tag}
                          </span>
                          <span className="meta-text mono-num">{t.count}</span>
                        </label>
                      );
                    })}
                </div>
              )}

              {matchingTags.length > MAX_VISIBLE_TAG_SUGGESTIONS && (
                <div className="notes-tag-suggestions-more meta-text">
                  Showing {MAX_VISIBLE_TAG_SUGGESTIONS} of {matchingTags.length}{' '}
                  tags — &ldquo;{allMatchingTagsSelected ? 'Deselect all' : 'Select all'}&rdquo; applies to all {matchingTags.length}.
                </div>
              )}
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
                    onChangeSelectedTags([]);
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
                    {note.linkLabel && (
                      <span className="meta-text notes-search-link-label">
                        {note.linkLabel}
                      </span>
                    )}
                    {note.excerpt && (
                      <span className="meta-text notes-search-excerpt">
                        {note.excerpt}
                      </span>
                    )}
                  </button>
                ))
              )}
            </div>
          ) : (
            <div className="notes-sections-stack">
              {/* General Section */}
              <section className="notes-sidebar-section">
                <button
                  type="button"
                  className="notes-sidebar-section-header"
                  onClick={() => toggleExpand('section:general', true)}
                  aria-expanded={isNodeExpanded('section:general', true)}
                >
                  <ChevronRightIcon
                    className={`notes-tree-chevron ${
                      isNodeExpanded('section:general', true)
                        ? 'is-expanded'
                        : ''
                    }`}
                  />
                  <span>General</span>
                </button>

                {isNodeExpanded('section:general', true) && (
                  <div className="notes-tree-list">
                    {folders.length === 0 && notes.length === 0 ? (
                      <p className="meta-text empty-state">
                        No notes yet. Create your first note above.
                      </p>
                    ) : (
                      <>
                        {rootFolders.map((folder) =>
                          renderFolderBranch(folder, 0),
                        )}
                        {rootNotes.map((note) =>
                          renderNoteButton(note, 0, true),
                        )}
                        {draggingNote && draggingNote.folderId !== null && (
                          <div
                            className={`notes-root-drop-zone ${
                              dropTargetFolderId === 'root'
                                ? 'is-drop-target'
                                : ''
                            }`}
                            onDragOver={(e) => handleFolderDragOver(e, 'root')}
                            onDragLeave={() => {
                              if (dropTargetFolderId === 'root') {
                                setDropTargetFolderId(null);
                              }
                            }}
                            onDrop={(e) => handleFolderDrop(e, null)}
                          >
                            Drop here to move to Unfiled
                          </div>
                        )}
                      </>
                    )}
                  </div>
                )}
              </section>

              {/* University Section */}
              {showUni && (
                <section className="notes-sidebar-section">
                  <button
                    type="button"
                    className="notes-sidebar-section-header"
                    onClick={() => toggleExpand('section:university', true)}
                    aria-expanded={isNodeExpanded('section:university', true)}
                  >
                    <ChevronRightIcon
                      className={`notes-tree-chevron ${
                        isNodeExpanded('section:university', true)
                          ? 'is-expanded'
                          : ''
                      }`}
                    />
                    <span>University</span>
                  </button>

                  {isNodeExpanded('section:university', true) && (
                    <div className="notes-tree-list">
                      {university.length === 0 ? (
                        <p className="meta-text empty-state">
                          No university courses yet.
                        </p>
                      ) : (
                        university.map((sem) => {
                          const semKey = `uni:sem:${sem.semester}`;
                          const semOpen = isNodeExpanded(semKey, true);
                          return (
                            <div key={semKey} className="notes-tree-branch">
                              <div
                                className="notes-tree-folder-row"
                                style={{ paddingLeft: '8px' }}
                              >
                                <button
                                  type="button"
                                  className={`notes-tree-chevron ${semOpen ? 'is-expanded' : ''}`}
                                  onClick={() => toggleExpand(semKey, true)}
                                  aria-label={`Toggle ${sem.semester}`}
                                >
                                  <ChevronRightIcon />
                                </button>
                                <button
                                  type="button"
                                  className="notes-tree-folder-name"
                                  onClick={() => toggleExpand(semKey, true)}
                                >
                                  <span>{sem.semester}</span>
                                </button>
                              </div>

                              {semOpen &&
                                sem.courses.map((course) => {
                                  const cKey = `uni:course:${course.id}`;
                                  const cOpen = isNodeExpanded(cKey, false);
                                  return (
                                    <div key={cKey} className="notes-tree-branch">
                                      <div
                                        className={`notes-tree-folder-row ${
                                          selectedContainer?.kind === 'course' &&
                                          selectedContainer.courseId === course.id
                                            ? 'is-selected-container'
                                            : ''
                                        }`}
                                        style={{ paddingLeft: '20px' }}
                                        {...fileDropProps(`course:${course.id}`, { universityCourseId: course.id })}
                                      >
                                        <button
                                          type="button"
                                          className={`notes-tree-chevron ${cOpen ? 'is-expanded' : ''}`}
                                          onClick={() =>
                                            toggleExpand(cKey, false)
                                          }
                                          aria-label={`Toggle ${course.name}`}
                                        >
                                          <ChevronRightIcon />
                                        </button>
                                        <button
                                          type="button"
                                          className="notes-tree-folder-name"
                                          onClick={() => {
                                            setSelectedContainer({
                                              kind: 'course',
                                              courseId: course.id,
                                              label: course.name,
                                            });
                                            toggleExpand(cKey, false);
                                          }}
                                          title={course.name}
                                        >
                                          <span
                                            className="notes-course-dot"
                                            style={{
                                              backgroundColor: course.color,
                                            }}
                                            aria-hidden="true"
                                          />
                                          <span>
                                            {course.code
                                              ? `${course.code} · ${course.name}`
                                              : course.name}
                                          </span>
                                        </button>
                                        <div className="notes-tree-folder-actions">
                                          <button
                                            type="button"
                                            className="btn btn-ghost btn-icon-sm"
                                            title={`New course note in ${course.name}`}
                                            aria-label={`New course note in ${course.name}`}
                                            onClick={() => {
                                              setExpandedNodes((prev) => ({
                                                ...prev,
                                                [cKey]: true,
                                              }));
                                              void onCreateCourseNote(
                                                course.id,
                                              );
                                            }}
                                          >
                                            <PlusIcon />
                                          </button>
                                          <button
                                            type="button"
                                            className="btn btn-ghost btn-icon-sm"
                                            title={`Upload course artifact to ${course.name}`}
                                            aria-label={`Upload course artifact to ${course.name}`}
                                            onClick={() => {
                                              setExpandedNodes((prev) => ({
                                                ...prev,
                                                [cKey]: true,
                                              }));
                                              triggerArtifactUpload({
                                                universityCourseId: course.id,
                                              });
                                            }}
                                          >
                                            <UploadIcon />
                                          </button>
                                        </div>
                                      </div>

                                      {cOpen && (
                                        <div className="notes-tree-children">
                                          {course.notes.map((n) =>
                                            renderNoteButton(n, 2, false),
                                          )}
                                          {course.artifacts.map((a) =>
                                            renderArtifactButton(a, 2),
                                          )}
                                          {course.activities.map((act) => {
                                            const aKey = `uni:act:${act.id}`;
                                            const aOpen = isNodeExpanded(
                                              aKey,
                                              false,
                                            );
                                            return (
                                              <div
                                                key={aKey}
                                                className="notes-tree-branch"
                                              >
                                                <div
                                                  className="notes-tree-folder-row"
                                                  style={{
                                                    paddingLeft: '32px',
                                                  }}
                                                >
                                                  <button
                                                    type="button"
                                                    className={`notes-tree-chevron ${aOpen ? 'is-expanded' : ''}`}
                                                    onClick={() =>
                                                      toggleExpand(aKey, false)
                                                    }
                                                    aria-label={`Toggle ${act.title}`}
                                                  >
                                                    <ChevronRightIcon />
                                                  </button>
                                                  <button
                                                    type="button"
                                                    className="notes-tree-folder-name"
                                                    onClick={() =>
                                                      toggleExpand(aKey, false)
                                                    }
                                                  >
                                                    <span>{act.title}</span>
                                                  </button>
                                                </div>

                                                {aOpen &&
                                                  act.items.map((item) => {
                                                    const iKey = `uni:item:${item.id}`;
                                                    const itemNotes =
                                                      item.notes &&
                                                      item.notes.length > 0
                                                        ? item.notes
                                                        : item.note
                                                          ? [item.note]
                                                          : [];
                                                    const hasChildren =
                                                      itemNotes.length > 0 ||
                                                      item.artifacts.length > 0;
                                                    const iOpen =
                                                      isNodeExpanded(
                                                        iKey,
                                                        false,
                                                      );
                                                    const isItemContainerSelected =
                                                      selectedContainer?.kind ===
                                                        'university_item' &&
                                                      selectedContainer.itemId ===
                                                        item.id;
                                                    return (
                                                      <div
                                                        key={iKey}
                                                        className="notes-tree-branch"
                                                      >
                                                        <div
                                                          className={`notes-tree-folder-row ${
                                                            isItemContainerSelected
                                                              ? 'is-selected-container'
                                                              : ''
                                                          }`}
                                                          style={{
                                                            paddingLeft: '44px',
                                                          }}
                                                          {...fileDropProps(`item:${item.id}`, { universityActivityItemId: item.id })}
                                                        >
                                                          <button
                                                            type="button"
                                                            className={`notes-tree-chevron ${iOpen ? 'is-expanded' : ''}`}
                                                            onClick={() =>
                                                              toggleExpand(
                                                                iKey,
                                                                false,
                                                              )
                                                            }
                                                            aria-label={`Toggle ${item.title}`}
                                                          >
                                                            <ChevronRightIcon />
                                                          </button>
                                                          <button
                                                            type="button"
                                                            className="notes-tree-folder-name"
                                                            onClick={() => {
                                                              setSelectedContainer(
                                                                {
                                                                  kind: 'university_item',
                                                                  itemId:
                                                                    item.id,
                                                                  label:
                                                                    item.title,
                                                                },
                                                              );
                                                              toggleExpand(
                                                                iKey,
                                                                false,
                                                              );
                                                            }}
                                                            title={`${item.title} (${itemNotes.length} notes, ${item.artifacts.length} files)`}
                                                          >
                                                            <span>
                                                              {item.title}
                                                            </span>
                                                          </button>
                                                          <div className="notes-tree-folder-actions">
                                                            <button
                                                              type="button"
                                                              className="btn btn-ghost btn-icon-sm"
                                                              title={`New note in ${item.title}`}
                                                              aria-label={`New note in ${item.title}`}
                                                              onClick={() => {
                                                                setSelectedContainer(
                                                                  {
                                                                    kind: 'university_item',
                                                                    itemId:
                                                                      item.id,
                                                                    label:
                                                                      item.title,
                                                                  },
                                                                );
                                                                setExpandedNodes(
                                                                  (prev) => ({
                                                                    ...prev,
                                                                    [iKey]: true,
                                                                  }),
                                                                );
                                                                void onCreateUniversityItemNote(
                                                                  item.id,
                                                                );
                                                              }}
                                                            >
                                                              <PlusIcon />
                                                            </button>
                                                            <button
                                                              type="button"
                                                              className="btn btn-ghost btn-icon-sm"
                                                              title={`Upload artifact to ${item.title}`}
                                                              aria-label={`Upload artifact to ${item.title}`}
                                                              onClick={() => {
                                                                setExpandedNodes(
                                                                  (prev) => ({
                                                                    ...prev,
                                                                    [iKey]: true,
                                                                  }),
                                                                );
                                                                triggerArtifactUpload(
                                                                  {
                                                                    universityActivityItemId:
                                                                      item.id,
                                                                  },
                                                                );
                                                              }}
                                                            >
                                                              <UploadIcon />
                                                            </button>
                                                          </div>
                                                        </div>
                                                        {iOpen && hasChildren && (
                                                          <div className="notes-tree-children">
                                                            {itemNotes.map(
                                                              (n) =>
                                                                renderNoteButton(
                                                                  n,
                                                                  4,
                                                                  false,
                                                                ),
                                                            )}
                                                            {item.artifacts.map(
                                                              (art) =>
                                                                renderArtifactButton(
                                                                  art,
                                                                  4,
                                                                ),
                                                            )}
                                                          </div>
                                                        )}
                                                      </div>
                                                    );
                                                  })}
                                              </div>
                                            );
                                          })}
                                        </div>
                                      )}
                                    </div>
                                  );
                                })}
                            </div>
                          );
                        })
                      )}
                    </div>
                  )}
                </section>
              )}

              {/* Events Section */}
              {showEvents && (
                <section className="notes-sidebar-section">
                  <button
                    type="button"
                    className="notes-sidebar-section-header"
                    onClick={() => toggleExpand('section:events', true)}
                    aria-expanded={isNodeExpanded('section:events', true)}
                  >
                    <ChevronRightIcon
                      className={`notes-tree-chevron ${
                        isNodeExpanded('section:events', true)
                          ? 'is-expanded'
                          : ''
                      }`}
                    />
                    <span>Events</span>
                  </button>

                  {isNodeExpanded('section:events', true) && (
                    <div className="notes-tree-list">
                      {events.length === 0 ? (
                        <p className="meta-text empty-state">
                          No event notes yet. Open a note from any calendar
                          event to see it here.
                        </p>
                      ) : (
                        events.map((evt) => {
                          const eKey = `evt:${evt.calendarEventId}`;
                          const eOpen = isNodeExpanded(eKey, true);
                          if (
                            !evt.isRecurring &&
                            evt.occurrences.length === 0 &&
                            evt.sharedNote &&
                            evt.sharedArtifacts.length === 0
                          ) {
                            return renderNoteButton(evt.sharedNote, 0, false);
                          }
                          return (
                            <div key={eKey} className="notes-tree-branch">
                              <div
                                className="notes-tree-folder-row"
                                style={{ paddingLeft: '8px' }}
                              >
                                <button
                                  type="button"
                                  className={`notes-tree-chevron ${eOpen ? 'is-expanded' : ''}`}
                                  onClick={() => toggleExpand(eKey, true)}
                                  aria-label={`Toggle ${evt.title}`}
                                >
                                  <ChevronRightIcon />
                                </button>
                                <button
                                  type="button"
                                  className="notes-tree-folder-name"
                                  onClick={() => toggleExpand(eKey, true)}
                                >
                                  <span>{evt.title}</span>
                                </button>
                              </div>

                              {eOpen && (
                                <div className="notes-tree-children">
                                  {evt.sharedNote &&
                                    renderNoteButton(evt.sharedNote, 1, false)}
                                  {evt.sharedArtifacts.map((a) =>
                                    renderArtifactButton(a, 1),
                                  )}
                                  {evt.occurrences.map((occ) => (
                                    <div
                                      key={occ.occurrenceStartAt}
                                      className="notes-tree-branch"
                                    >
                                      {occ.note ? (
                                        renderNoteButton(occ.note, 1, false)
                                      ) : (
                                        <div
                                          className="meta-text notes-tree-occ-label"
                                          style={{ paddingLeft: '24px' }}
                                        >
                                          {formatShortOccurrenceDate(
                                            occ.occurrenceStartAt,
                                          )}
                                        </div>
                                      )}
                                      {occ.artifacts.map((a) =>
                                        renderArtifactButton(a, 2),
                                      )}
                                    </div>
                                  ))}
                                </div>
                              )}
                            </div>
                          );
                        })
                      )}
                    </div>
                  )}
                </section>
              )}

              {/* Daily Section */}
              {showDaily && (
                <section className="notes-sidebar-section">
                  <button
                    type="button"
                    className="notes-sidebar-section-header"
                    onClick={() => toggleExpand('section:daily', true)}
                    aria-expanded={isNodeExpanded('section:daily', true)}
                  >
                    <ChevronRightIcon
                      className={`notes-tree-chevron ${
                        isNodeExpanded('section:daily', true)
                          ? 'is-expanded'
                          : ''
                      }`}
                    />
                    <span>Daily</span>
                  </button>

                  {isNodeExpanded('section:daily', true) && (
                    <div className="notes-tree-list">
                      {daily.length === 0 ? (
                        <p className="meta-text empty-state">
                          No daily notes yet.
                        </p>
                      ) : (
                        daily.map((note) => renderNoteButton(note, 0, false))
                      )}
                    </div>
                  )}
                </section>
              )}
            </div>
          )}
        </div>
      </aside>
    </>
  );
}
