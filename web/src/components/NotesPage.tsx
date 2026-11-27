import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import ReactMarkdown from 'react-markdown';
import {
  parseFrontmatter,
  stripFrontmatterForReading,
  updateFrontmatterTags,
  updateFrontmatterTitleIfPresent,
} from '../lib/frontmatter';
import {
  insertCallout,
  insertHorizontalRule,
  insertImage,
  insertLink,
  setHeading,
  toggleBlockquote,
  toggleBold,
  toggleItalic,
} from '../lib/markdownCommands';
import { readingMarkdownComponents } from '../lib/markdownReading';
import {
  countWordsAndChars,
  DEFAULT_SAMPLE_NOTE,
  loadNotesDraft,
} from '../lib/markdownUtils';
import {
  createNote,
  createNoteFolder,
  deleteNote,
  deleteNoteFolder,
  getNote,
  getNotesTree,
  NoteConflictError,
  searchNotes,
  updateNote,
  updateNoteFolder,
  type NoteSearchMode,
} from '../lib/notesClient';
import type {
  NoteDetail,
  NoteFolder,
  NoteSummary,
  NoteTagCount,
} from '../types/atrium';
import {
  MarkdownEditor,
  type MarkdownEditorHandle,
} from './MarkdownEditor';
import { NotePropertiesBar } from './NotePropertiesBar';
import { NotesSidebar } from './NotesSidebar';

export type NotesViewMode = 'live' | 'reading' | 'source';

type SaveState = 'idle' | 'saving' | 'saved' | 'error' | 'conflict';

const SIDEBAR_STORAGE_KEY = 'atrium.notes.sidebar.open';
const AUTOSAVE_DELAY_MS = 700;

interface NotesPageProps {
  userId: string;
  onAnnounce: (message: string) => void;
}

function readInitialSidebarOpen(): boolean {
  if (typeof window === 'undefined') return true;
  if (window.innerWidth < 768) return false;
  try {
    const raw = window.localStorage.getItem(SIDEBAR_STORAGE_KEY);
    return raw === null ? true : raw === 'true';
  } catch {
    return true;
  }
}

export function NotesPage({ userId, onAnnounce }: NotesPageProps) {
  const [sidebarOpen, setSidebarOpen] = useState<boolean>(readInitialSidebarOpen);
  const [folders, setFolders] = useState<NoteFolder[]>([]);
  const [notes, setNotes] = useState<NoteSummary[]>([]);
  const [tags, setTags] = useState<NoteTagCount[]>([]);
  const [treeLoading, setTreeLoading] = useState(true);

  const [searchQuery, setSearchQuery] = useState('');
  const [searchMode, setSearchMode] = useState<NoteSearchMode>('titles');
  const [activeTag, setActiveTag] = useState<string | null>(null);
  const [searchResults, setSearchResults] = useState<NoteSummary[] | null>(null);
  const [searching, setSearching] = useState(false);
  const [searchRefreshTick, setSearchRefreshTick] = useState(0);

  // In-note Ctrl+F / Ctrl+H find & replace state
  const [findOpen, setFindOpen] = useState(false);
  const [replaceOpen, setReplaceOpen] = useState(false);
  const [findQuery, setFindQuery] = useState('');
  const [replaceValue, setReplaceValue] = useState('');
  const [findCaseSensitive, setFindCaseSensitive] = useState(false);
  const [findMatchIndex, setFindMatchIndex] = useState(0);
  const [findMatchCount, setFindMatchCount] = useState(0);
  const findInputRef = useRef<HTMLInputElement | null>(null);
  const replaceInputRef = useRef<HTMLInputElement | null>(null);

  const [selectedNoteId, setSelectedNoteId] = useState<string | null>(null);
  const [activeNote, setActiveNote] = useState<NoteDetail | null>(null);
  const [noteLoading, setNoteLoading] = useState(false);

  const [draftTitle, setDraftTitle] = useState('');
  const [draftContent, setDraftContent] = useState('');
  const [mode, setMode] = useState<NotesViewMode>('live');
  const [editorMode, setEditorMode] = useState<'live' | 'source'>('live');
  const [saveState, setSaveState] = useState<SaveState>('idle');
  const [saveError, setSaveError] = useState<string | null>(null);
  const [conflictNote, setConflictNote] = useState<NoteDetail | null>(null);
  const [confirmDeleteNote, setConfirmDeleteNote] = useState(false);

  const editorRef = useRef<MarkdownEditorHandle | null>(null);
  const modeRef = useRef<NotesViewMode>(mode);
  const selectedNoteIdRef = useRef<string | null>(selectedNoteId);
  const activeNoteRef = useRef<NoteDetail | null>(activeNote);
  const draftTitleRef = useRef(draftTitle);
  const draftContentRef = useRef(draftContent);
  const titleEditedInPropertiesRef = useRef(false);
  const dirtyRef = useRef(false);
  const conflictRef = useRef(false);
  const editSeqRef = useRef(0);
  const saveTimerRef = useRef<number | null>(null);
  const loadSeqRef = useRef(0);
  const pendingSavesRef = useRef<Map<string, Promise<void>>>(new Map());
  const latestVersionByNoteRef = useRef<Map<string, number>>(new Map());

  modeRef.current = mode;
  selectedNoteIdRef.current = selectedNoteId;
  activeNoteRef.current = activeNote;
  draftTitleRef.current = draftTitle;
  draftContentRef.current = draftContent;

  const localPr1Draft = useMemo(() => {
    const draft = loadNotesDraft(userId);
    return draft.trim() && draft !== DEFAULT_SAMPLE_NOTE ? draft : null;
  }, [userId]);

  const clearSaveTimer = useCallback(() => {
    if (saveTimerRef.current !== null) {
      window.clearTimeout(saveTimerRef.current);
      saveTimerRef.current = null;
    }
  }, []);

  const refreshTree = useCallback(async () => {
    const data = await getNotesTree();
    setFolders(data.folders);
    setNotes(data.notes);
    setTags(data.tags);
    setSearchRefreshTick((t) => t + 1);
    return data;
  }, []);

  const enqueueNoteTask = useCallback(
    (noteId: string, task: () => Promise<void>): Promise<void> => {
      const prev = pendingSavesRef.current.get(noteId) ?? Promise.resolve();
      const next = prev
        .catch(() => undefined)
        .then(task);
      pendingSavesRef.current.set(noteId, next);
      void next.finally(() => {
        if (pendingSavesRef.current.get(noteId) === next) {
          pendingSavesRef.current.delete(noteId);
        }
      });
      return next;
    },
    [],
  );

  const persistNote = useCallback(
    (
      noteId: string,
      payload: {
        title?: string;
        content: string;
        overrideExpectedVersion?: number;
      },
      editSeqAtStart: number,
      trackUiState: boolean,
    ) => {
      if (trackUiState) {
        setSaveState('saving');
        setSaveError(null);
      }

      return enqueueNoteTask(noteId, async () => {
        const expectedVersion =
          payload.overrideExpectedVersion ??
          latestVersionByNoteRef.current.get(noteId);
        try {
          const updated = await updateNote(noteId, {
            title: payload.title,
            content: payload.content,
            expectedVersion,
          });
          latestVersionByNoteRef.current.set(noteId, updated.version);

          if (selectedNoteIdRef.current === noteId) {
            setActiveNote(updated);
            activeNoteRef.current = updated;
            if (editSeqRef.current === editSeqAtStart) {
              dirtyRef.current = false;
              titleEditedInPropertiesRef.current = false;
              conflictRef.current = false;
              setDraftTitle(updated.title);
              if (trackUiState) {
                setSaveState('saved');
                setConflictNote(null);
              }
            }
          }
          void refreshTree().catch(() => undefined);
        } catch (err) {
          if (err instanceof NoteConflictError) {
            if (selectedNoteIdRef.current === noteId && trackUiState) {
              conflictRef.current = true;
              setSaveState('conflict');
              setConflictNote(err.currentNote);
            } else {
              onAnnounce('A note was modified in another session');
            }
            return;
          }
          if (selectedNoteIdRef.current === noteId && trackUiState) {
            setSaveState('error');
            setSaveError(
              err instanceof Error ? err.message : 'Could not save note.',
            );
          } else {
            onAnnounce('Could not save note changes');
          }
        }
      });
    },
    [enqueueNoteTask, onAnnounce, refreshTree],
  );

  const flushCurrentNote = useCallback(
    (trackUiState: boolean) => {
      clearSaveTimer();
      const note = activeNoteRef.current;
      if (!note || !dirtyRef.current || conflictRef.current) return;

      const capturedSeq = editSeqRef.current;
      const shouldSendTitle =
        modeRef.current !== 'source' || titleEditedInPropertiesRef.current;
      dirtyRef.current = false;

      void persistNote(
        note.id,
        {
          title: shouldSendTitle
            ? draftTitleRef.current.trim() || 'Untitled'
            : undefined,
          content: draftContentRef.current,
        },
        capturedSeq,
        trackUiState,
      );
    },
    [clearSaveTimer, persistNote],
  );

  const openNote = useCallback(
    async (noteId: string) => {
      if (selectedNoteIdRef.current && selectedNoteIdRef.current !== noteId) {
        flushCurrentNote(false);
      }

      const seq = ++loadSeqRef.current;
      setSelectedNoteId(noteId);
      setConfirmDeleteNote(false);
      setConflictNote(null);
      conflictRef.current = false;
      setSaveState('idle');
      setSaveError(null);
      setNoteLoading(true);

      try {
        const pending = pendingSavesRef.current.get(noteId);
        if (pending) {
          await pending.catch(() => undefined);
        }
        if (seq !== loadSeqRef.current) return;

        const detail = await getNote(noteId);
        if (seq !== loadSeqRef.current) return;

        latestVersionByNoteRef.current.set(detail.id, detail.version);
        setActiveNote(detail);
        activeNoteRef.current = detail;
        setDraftTitle(detail.title);
        draftTitleRef.current = detail.title;
        setDraftContent(detail.content);
        draftContentRef.current = detail.content;
        dirtyRef.current = false;
        titleEditedInPropertiesRef.current = false;
        editSeqRef.current += 1;
      } catch (err) {
        if (seq !== loadSeqRef.current) return;
        setSelectedNoteId(activeNoteRef.current?.id ?? null);
        onAnnounce(err instanceof Error ? err.message : 'Could not load note');
      } finally {
        if (seq === loadSeqRef.current) {
          setNoteLoading(false);
        }
      }
    },
    [flushCurrentNote, onAnnounce],
  );

  // Initial vault tree load
  useEffect(() => {
    let active = true;
    setTreeLoading(true);
    void refreshTree()
      .then((data) => {
        if (!active) return;
        if (data.notes.length > 0 && !selectedNoteIdRef.current) {
          void openNote(data.notes[0].id);
        }
      })
      .catch(() => {
        if (active) onAnnounce('Could not load notes vault');
      })
      .finally(() => {
        if (active) setTreeLoading(false);
      });

    return () => {
      active = false;
    };
  }, [onAnnounce, openNote, refreshTree]);

  // Flush pending edits on unmount
  useEffect(() => {
    return () => {
      clearSaveTimer();
      const note = activeNoteRef.current;
      if (note && dirtyRef.current && !conflictRef.current) {
        dirtyRef.current = false;
        const shouldSendTitle =
          modeRef.current !== 'source' || titleEditedInPropertiesRef.current;
        void enqueueNoteTask(note.id, async () => {
          await updateNote(note.id, {
            title: shouldSendTitle
              ? draftTitleRef.current.trim() || 'Untitled'
              : undefined,
            content: draftContentRef.current,
            expectedVersion: latestVersionByNoteRef.current.get(note.id),
          }).catch(() => undefined);
        });
      }
    };
  }, [clearSaveTimer, enqueueNoteTask]);

  // Debounced search & tag filter
  useEffect(() => {
    const trimmed = searchQuery.trim();
    if (!trimmed && !activeTag) {
      setSearchResults(null);
      setSearching(false);
      return;
    }

    let cancelled = false;
    setSearching(true);
    const timer = window.setTimeout(() => {
      void searchNotes({
        q: trimmed || undefined,
        searchIn: trimmed ? searchMode : undefined,
        tag: activeTag ?? undefined,
      })
        .then((res) => {
          if (!cancelled) setSearchResults(res);
        })
        .catch(() => {
          if (!cancelled) setSearchResults([]);
        })
        .finally(() => {
          if (!cancelled) setSearching(false);
        });
    }, 220);

    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, [activeTag, searchMode, searchQuery, searchRefreshTick]);

  const toggleSidebar = useCallback(() => {
    setSidebarOpen((prev) => {
      const next = !prev;
      try {
        window.localStorage.setItem(SIDEBAR_STORAGE_KEY, String(next));
      } catch {
        // Ignore storage errors
      }
      return next;
    });
  }, []);

  const switchMode = useCallback(
    (nextMode: NotesViewMode) => {
      // Flush before changing modeRef so Source-mode YAML title edits keep their precedence
      if (dirtyRef.current) {
        flushCurrentNote(true);
      }
      modeRef.current = nextMode;
      setMode(nextMode);
      if (nextMode !== 'reading') {
        setEditorMode(nextMode);
        requestAnimationFrame(() => editorRef.current?.focus());
      }
    },
    [flushCurrentNote],
  );

  // Keyboard shortcuts: Cmd/Ctrl+E (Reading toggle), Cmd/Ctrl+\ (Sidebar), Cmd/Ctrl+F (Find), Ctrl+H / Cmd+Alt+F (Replace)
  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (!(event.metaKey || event.ctrlKey) || event.shiftKey) {
        return;
      }
      if (document.querySelector('dialog[open]')) {
        return;
      }

      const key = event.key.toLowerCase();

      if (
        activeNoteRef.current &&
        ((event.ctrlKey && !event.metaKey && !event.altKey && key === 'h') ||
          (event.metaKey && event.altKey && key === 'f'))
      ) {
        event.preventDefault();
        if (modeRef.current === 'reading') {
          switchMode('live');
        }
        setFindOpen(true);
        setReplaceOpen(true);
        requestAnimationFrame(() => {
          if (findInputRef.current && !findInputRef.current.value) {
            findInputRef.current.focus();
          } else {
            replaceInputRef.current?.focus();
            replaceInputRef.current?.select();
          }
        });
        return;
      }

      if (event.altKey) {
        return;
      }

      if (key === 'e') {
        event.preventDefault();
        switchMode(modeRef.current === 'reading' ? 'live' : 'reading');
      } else if (event.key === '\\') {
        event.preventDefault();
        toggleSidebar();
      } else if (key === 'f' && activeNoteRef.current) {
        event.preventDefault();
        if (modeRef.current === 'reading') {
          switchMode('live');
        }
        setFindOpen(true);
        requestAnimationFrame(() => {
          findInputRef.current?.focus();
          findInputRef.current?.select();
        });
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [switchMode, toggleSidebar]);

  const scheduleAutoSave = useCallback(() => {
    clearSaveTimer();
    const note = activeNoteRef.current;
    if (!note || conflictRef.current) return;

    const capturedSeq = editSeqRef.current;
    const capturedNoteId = note.id;
    saveTimerRef.current = window.setTimeout(() => {
      saveTimerRef.current = null;
      if (
        !dirtyRef.current ||
        conflictRef.current ||
        selectedNoteIdRef.current !== capturedNoteId
      ) {
        return;
      }
      const shouldSendTitle =
        modeRef.current !== 'source' || titleEditedInPropertiesRef.current;
      void persistNote(
        capturedNoteId,
        {
          title: shouldSendTitle
            ? draftTitleRef.current.trim() || 'Untitled'
            : undefined,
          content: draftContentRef.current,
        },
        capturedSeq,
        true,
      );
    }, AUTOSAVE_DELAY_MS);
  }, [clearSaveTimer, persistNote]);

  const handleContentChange = useCallback(
    (nextContent: string) => {
      setDraftContent(nextContent);
      draftContentRef.current = nextContent;
      dirtyRef.current = true;
      editSeqRef.current += 1;
      if (!conflictRef.current) {
        setSaveState('idle');
        scheduleAutoSave();
      }
    },
    [scheduleAutoSave],
  );

  const handleTitleChange = (nextTitle: string) => {
    setDraftTitle(nextTitle);
    draftTitleRef.current = nextTitle;
    titleEditedInPropertiesRef.current = true;
    const syncedContent = updateFrontmatterTitleIfPresent(
      draftContentRef.current,
      nextTitle,
    );
    if (syncedContent !== draftContentRef.current) {
      setDraftContent(syncedContent);
      draftContentRef.current = syncedContent;
    }
    dirtyRef.current = true;
    editSeqRef.current += 1;
    if (!conflictRef.current) {
      setSaveState('idle');
      scheduleAutoSave();
    }
  };

  const parsedFm = useMemo(() => parseFrontmatter(draftContent), [draftContent]);

  const handleAddTag = (tag: string) => {
    const nextContent = updateFrontmatterTags(draftContentRef.current, [
      ...parsedFm.tags,
      tag,
    ]);
    handleContentChange(nextContent);
  };

  const handleRemoveTag = (tag: string) => {
    const nextContent = updateFrontmatterTags(
      draftContentRef.current,
      parsedFm.tags.filter((t) => t !== tag),
    );
    handleContentChange(nextContent);
  };

  const handleCreateNote = async (
    folderId: string | null,
    initialTitle = 'Untitled',
    initialContent = '',
  ) => {
    flushCurrentNote(false);
    try {
      const created = await createNote({
        title: initialTitle,
        folderId,
        content: initialContent,
      });
      latestVersionByNoteRef.current.set(created.id, created.version);
      await refreshTree();
      setSelectedNoteId(created.id);
      setActiveNote(created);
      activeNoteRef.current = created;
      setDraftTitle(created.title);
      draftTitleRef.current = created.title;
      setDraftContent(created.content);
      draftContentRef.current = created.content;
      dirtyRef.current = false;
      conflictRef.current = false;
      setSaveState('idle');
      setConflictNote(null);
      if (modeRef.current === 'reading') {
        switchMode('live');
      }
      onAnnounce(`Created note "${created.title}"`);
    } catch (err) {
      onAnnounce(err instanceof Error ? err.message : 'Could not create note');
    }
  };

  const handleDeleteCurrentNote = async () => {
    if (!activeNote) return;
    clearSaveTimer();
    dirtyRef.current = false;
    try {
      await deleteNote(activeNote.id);
      setConfirmDeleteNote(false);
      const nextTree = await refreshTree();
      if (nextTree.notes.length > 0) {
        await openNote(nextTree.notes[0].id);
      } else {
        setSelectedNoteId(null);
        setActiveNote(null);
        setDraftTitle('');
        setDraftContent('');
      }
      onAnnounce('Deleted note');
    } catch (err) {
      onAnnounce(err instanceof Error ? err.message : 'Could not delete note');
    }
  };

  const handleMoveAnyNoteToFolder = async (
    noteId: string,
    nextFolderId: string | null,
  ) => {
    if (activeNoteRef.current?.id === noteId) {
      flushCurrentNote(false);
    }
    await enqueueNoteTask(noteId, async () => {
      try {
        const updated = await updateNote(noteId, {
          folderId: nextFolderId,
          moveToRoot: nextFolderId === null,
        });
        latestVersionByNoteRef.current.set(updated.id, updated.version);
        if (selectedNoteIdRef.current === updated.id) {
          setActiveNote(updated);
          activeNoteRef.current = updated;
        }
        await refreshTree();
        const targetName = nextFolderId
          ? folders.find((f) => f.id === nextFolderId)?.name ?? 'folder'
          : 'Unfiled';
        onAnnounce(`Moved "${updated.title}" to ${targetName}`);
      } catch (err) {
        onAnnounce(err instanceof Error ? err.message : 'Could not move note');
      }
    });
  };

  const handleMoveNoteFolder = async (nextFolderId: string | null) => {
    const note = activeNoteRef.current;
    if (!note) return;
    await handleMoveAnyNoteToFolder(note.id, nextFolderId);
  };

  const handleSelectSearchResult = (noteId: string) => {
    const trimmed = searchQuery.trim();
    if (searchMode === 'content' && trimmed) {
      if (modeRef.current === 'reading') {
        switchMode('live');
      }
      setFindOpen(true);
      setFindQuery(trimmed);
      setFindMatchIndex(0);
    }
    void openNote(noteId);
  };

  const stepFindMatch = (delta: number) => {
    if (findMatchCount <= 0) return;
    setFindMatchIndex((prev) => {
      const next = ((prev + delta) % findMatchCount + findMatchCount) % findMatchCount;
      editorRef.current?.scrollToMatch(next);
      return next;
    });
  };

  const closeFindBar = () => {
    setFindOpen(false);
    setReplaceOpen(false);
    setFindQuery('');
    editorRef.current?.focus();
  };

  const handleReplaceCurrent = () => {
    if (findMatchCount <= 0 || !findQuery) return;
    const currentClamped =
      ((findMatchIndex % findMatchCount) + findMatchCount) % findMatchCount;
    const replaced = editorRef.current?.replaceCurrentMatch(
      replaceValue,
      currentClamped,
    );
    if (!replaced) return;

    const queryCmp = findCaseSensitive ? findQuery : findQuery.toLowerCase();
    const replCmp = findCaseSensitive
      ? replaceValue
      : replaceValue.toLowerCase();
    if (replCmp.includes(queryCmp)) {
      setFindMatchIndex(currentClamped + 1);
    } else if (findMatchCount > 1 && currentClamped >= findMatchCount - 1) {
      setFindMatchIndex(0);
    }
  };

  const handleReplaceAll = () => {
    if (findMatchCount <= 0 || !findQuery) return;
    const count = editorRef.current?.replaceAllMatches(replaceValue) ?? 0;
    if (count > 0) {
      setFindMatchIndex(0);
      onAnnounce(
        `Replaced ${count} ${count === 1 ? 'occurrence' : 'occurrences'}`,
      );
    }
  };

  const handleToggleNoteAgentRead = async () => {
    const note = activeNoteRef.current;
    if (!note) return;
    flushCurrentNote(false);
    await enqueueNoteTask(note.id, async () => {
      try {
        const latestAllowed = activeNoteRef.current?.allowAgentRead ?? note.allowAgentRead;
        const updated = await updateNote(note.id, {
          allowAgentRead: !latestAllowed,
        });
        latestVersionByNoteRef.current.set(updated.id, updated.version);
        if (selectedNoteIdRef.current === updated.id) {
          setActiveNote(updated);
          activeNoteRef.current = updated;
        }
        await refreshTree();
        onAnnounce(
          updated.allowAgentRead
            ? 'AI agents can now read this note'
            : 'Note hidden from AI agents',
        );
      } catch (err) {
        onAnnounce(
          err instanceof Error ? err.message : 'Could not update agent access',
        );
      }
    });
  };

  const handleCreateFolder = async (
    name: string,
    parentFolderId: string | null,
  ) => {
    try {
      await createNoteFolder({ name, parentFolderId });
      await refreshTree();
      onAnnounce(`Created folder "${name}"`);
    } catch (err) {
      onAnnounce(
        err instanceof Error ? err.message : 'Could not create folder',
      );
    }
  };

  const handleRenameFolder = async (folder: NoteFolder, name: string) => {
    try {
      await updateNoteFolder(folder.id, { name });
      await refreshTree();
      onAnnounce(`Renamed folder to "${name}"`);
    } catch (err) {
      onAnnounce(
        err instanceof Error ? err.message : 'Could not rename folder',
      );
    }
  };

  const handleToggleFolderAgent = async (folder: NoteFolder) => {
    try {
      const updated = await updateNoteFolder(folder.id, {
        allowAgentRead: !folder.allowAgentRead,
      });
      await refreshTree();
      if (activeNote) {
        const refreshedNote = await getNote(activeNote.id);
        setActiveNote(refreshedNote);
        activeNoteRef.current = refreshedNote;
      }
      onAnnounce(
        updated.allowAgentRead
          ? `AI agents can read "${updated.name}"`
          : `Hidden "${updated.name}" from AI agents`,
      );
    } catch (err) {
      onAnnounce(
        err instanceof Error ? err.message : 'Could not update folder access',
      );
    }
  };

  const handleDeleteFolder = async (folder: NoteFolder) => {
    try {
      await deleteNoteFolder(folder.id);
      const nextTree = await refreshTree();
      if (
        selectedNoteIdRef.current &&
        !nextTree.notes.some((n) => n.id === selectedNoteIdRef.current)
      ) {
        clearSaveTimer();
        dirtyRef.current = false;
        conflictRef.current = false;
        setConflictNote(null);
        setSaveState('idle');
        setSaveError(null);
        if (nextTree.notes.length > 0) {
          await openNote(nextTree.notes[0].id);
        } else {
          setSelectedNoteId(null);
          setActiveNote(null);
          activeNoteRef.current = null;
          setDraftTitle('');
          setDraftContent('');
        }
      }
      onAnnounce(`Deleted folder "${folder.name}"`);
    } catch (err) {
      onAnnounce(
        err instanceof Error ? err.message : 'Could not delete folder',
      );
    }
  };

  const handleReloadConflict = () => {
    if (!conflictNote) return;
    clearSaveTimer();
    latestVersionByNoteRef.current.set(conflictNote.id, conflictNote.version);
    setActiveNote(conflictNote);
    activeNoteRef.current = conflictNote;
    setDraftTitle(conflictNote.title);
    draftTitleRef.current = conflictNote.title;
    setDraftContent(conflictNote.content);
    draftContentRef.current = conflictNote.content;
    dirtyRef.current = false;
    titleEditedInPropertiesRef.current = false;
    conflictRef.current = false;
    editSeqRef.current += 1;
    setConflictNote(null);
    setSaveState('idle');
    setSaveError(null);
    onAnnounce('Loaded latest version from server');
  };

  const handleOverwriteConflict = () => {
    if (!conflictNote || !activeNote) return;
    clearSaveTimer();
    const serverVersion = conflictNote.version;
    const shouldSendTitle =
      modeRef.current !== 'source' || titleEditedInPropertiesRef.current;
    conflictRef.current = false;
    dirtyRef.current = false;
    setConflictNote(null);
    void persistNote(
      activeNote.id,
      {
        title: shouldSendTitle
          ? draftTitleRef.current.trim() || 'Untitled'
          : undefined,
        content: draftContentRef.current,
        overrideExpectedVersion: serverVersion,
      },
      editSeqRef.current,
      true,
    );
  };

  const readingBody = useMemo(
    () => stripFrontmatterForReading(draftContent),
    [draftContent],
  );
  const stats = useMemo(() => countWordsAndChars(readingBody), [readingBody]);

  return (
    <main className="notes-page-shell" aria-label="Markdown Notes">
      <div className="notes-toolbar-bar">
        <div className="notes-toolbar-inner">
          <button
            id="notes-sidebar-toggle"
            type="button"
            className="btn btn-ghost btn-xs"
            onClick={toggleSidebar}
            title="Toggle sidebar (Cmd/Ctrl+\)"
            aria-expanded={sidebarOpen}
          >
            {sidebarOpen ? 'Hide sidebar' : 'Sidebar'}
          </button>

          {activeNote && (
            <>
              <div
                className="notes-mode-switcher"
                role="group"
                aria-label="Editor view mode"
              >
                {(
                  [
                    { id: 'live', label: 'Live Preview' },
                    { id: 'reading', label: 'Reading' },
                    { id: 'source', label: 'Source' },
                  ] as const
                ).map((opt) => (
                  <button
                    key={opt.id}
                    id={`notes-mode-${opt.id}`}
                    type="button"
                    aria-pressed={mode === opt.id}
                    className={`mode-chip ${mode === opt.id ? 'is-active' : ''}`}
                    onClick={() => switchMode(opt.id)}
                  >
                    {opt.label}
                  </button>
                ))}
              </div>

              {mode !== 'reading' && (
                <div
                  className="notes-format-toolbar"
                  role="toolbar"
                  aria-label="Markdown formatting"
                >
                  <button
                    type="button"
                    className="btn btn-ghost btn-xs notes-fmt-btn"
                    title="Heading 1"
                    onClick={() => editorRef.current?.runCommand(setHeading(1))}
                  >
                    H1
                  </button>
                  <button
                    type="button"
                    className="btn btn-ghost btn-xs notes-fmt-btn"
                    title="Heading 2"
                    onClick={() => editorRef.current?.runCommand(setHeading(2))}
                  >
                    H2
                  </button>
                  <button
                    type="button"
                    className="btn btn-ghost btn-xs notes-fmt-btn"
                    title="Heading 3"
                    onClick={() => editorRef.current?.runCommand(setHeading(3))}
                  >
                    H3
                  </button>
                  <span className="notes-toolbar-sep" aria-hidden="true" />
                  <button
                    type="button"
                    className="btn btn-ghost btn-xs notes-fmt-btn"
                    title="Bold (Cmd/Ctrl+B)"
                    onClick={() => editorRef.current?.runCommand(toggleBold)}
                  >
                    <strong>B</strong>
                  </button>
                  <button
                    type="button"
                    className="btn btn-ghost btn-xs notes-fmt-btn"
                    title="Italic (Cmd/Ctrl+I)"
                    onClick={() => editorRef.current?.runCommand(toggleItalic)}
                  >
                    <em>I</em>
                  </button>
                  <button
                    type="button"
                    className="btn btn-ghost btn-xs notes-fmt-btn"
                    title="Blockquote"
                    onClick={() =>
                      editorRef.current?.runCommand(toggleBlockquote)
                    }
                  >
                    Quote
                  </button>
                  <button
                    type="button"
                    className="btn btn-ghost btn-xs notes-fmt-btn"
                    title="Insert callout (> [!note])"
                    onClick={() => editorRef.current?.runCommand(insertCallout)}
                  >
                    Callout
                  </button>
                  <span className="notes-toolbar-sep" aria-hidden="true" />
                  <button
                    type="button"
                    className="btn btn-ghost btn-xs notes-fmt-btn"
                    title="Insert link (Cmd/Ctrl+Shift+K)"
                    onClick={() => editorRef.current?.runCommand(insertLink)}
                  >
                    Link
                  </button>
                  <button
                    type="button"
                    className="btn btn-ghost btn-xs notes-fmt-btn"
                    title="Insert image URL"
                    onClick={() => editorRef.current?.runCommand(insertImage)}
                  >
                    Image
                  </button>
                  <button
                    type="button"
                    className="btn btn-ghost btn-xs notes-fmt-btn"
                    title="Horizontal rule"
                    onClick={() =>
                      editorRef.current?.runCommand(insertHorizontalRule)
                    }
                  >
                    HR
                  </button>
                </div>
              )}

              <div className="notes-toolbar-right">
                {saveState === 'saving' && (
                  <span className="pill-neutral daily-note-status">Saving…</span>
                )}
                {saveState === 'saved' && (
                  <span className="pill-neutral daily-note-status">Saved</span>
                )}
                {saveState === 'error' && (
                  <button
                    type="button"
                    className="btn btn-ghost btn-xs daily-note-retry"
                    onClick={() => flushCurrentNote(true)}
                  >
                    Retry save
                  </button>
                )}

                {confirmDeleteNote ? (
                  <div className="uni-inline-confirm">
                    <button
                      type="button"
                      className="btn btn-danger btn-xs"
                      onClick={() => void handleDeleteCurrentNote()}
                    >
                      Delete
                    </button>
                    <button
                      type="button"
                      className="btn btn-ghost btn-xs"
                      onClick={() => setConfirmDeleteNote(false)}
                    >
                      Cancel
                    </button>
                  </div>
                ) : (
                  <button
                    type="button"
                    className="btn btn-ghost btn-xs uni-danger-text"
                    onClick={() => setConfirmDeleteNote(true)}
                  >
                    Delete
                  </button>
                )}
              </div>
            </>
          )}
        </div>
      </div>

      <div className="notes-vault-layout">
        <NotesSidebar
          isOpen={sidebarOpen}
          folders={folders}
          notes={notes}
          tags={tags}
          searchQuery={searchQuery}
          searchMode={searchMode}
          activeTag={activeTag}
          searchResults={searchResults}
          searching={searching}
          selectedNoteId={selectedNoteId}
          onCloseMobile={() => {
            if (window.innerWidth < 768) setSidebarOpen(false);
          }}
          onSearchChange={setSearchQuery}
          onSearchModeChange={setSearchMode}
          onSelectTag={setActiveTag}
          onSelectNote={(id) => void openNote(id)}
          onSelectSearchResult={handleSelectSearchResult}
          onMoveNoteToFolder={handleMoveAnyNoteToFolder}
          onCreateNote={(folderId) => handleCreateNote(folderId)}
          onCreateFolder={handleCreateFolder}
          onRenameFolder={handleRenameFolder}
          onToggleFolderAgent={handleToggleFolderAgent}
          onDeleteFolder={handleDeleteFolder}
        />

        <div className="notes-canvas-scroll">
          <div className="notes-canvas">
            {saveState === 'conflict' && conflictNote && (
              <div className="notes-conflict-banner" role="alert">
                <span>
                  This note was updated in another session (v{conflictNote.version}).
                </span>
                <div className="notes-conflict-actions">
                  <button
                    type="button"
                    className="btn btn-secondary btn-xs"
                    onClick={handleReloadConflict}
                  >
                    Reload latest
                  </button>
                  <button
                    type="button"
                    className="btn btn-danger btn-xs"
                    onClick={handleOverwriteConflict}
                  >
                    Overwrite with mine
                  </button>
                </div>
              </div>
            )}

            {saveError && (
              <p className="calendar-inline-error" role="alert">
                {saveError}
              </p>
            )}

            {treeLoading && !activeNote ? (
              <p className="meta-text empty-state">Loading your notes vault…</p>
            ) : !activeNote ? (
              <div className="card notes-empty-state">
                <h2 className="card-title">Your Knowledge Base</h2>
                <p className="meta-text">
                  Create Markdown notes organized in folders with YAML frontmatter
                  tags and per-note AI agent access controls.
                </p>
                <div className="notes-empty-actions">
                  <button
                    type="button"
                    className="btn btn-primary"
                    onClick={() =>
                      void handleCreateNote(
                        null,
                        'Welcome to Atrium Notes',
                        DEFAULT_SAMPLE_NOTE,
                      )
                    }
                  >
                    Create first note
                  </button>
                  {localPr1Draft && (
                    <button
                      type="button"
                      className="btn btn-secondary"
                      onClick={() =>
                        void handleCreateNote(
                          null,
                          'Imported Draft',
                          localPr1Draft,
                        )
                      }
                    >
                      Import local draft
                    </button>
                  )}
                </div>
              </div>
            ) : noteLoading ? (
              <p className="meta-text empty-state">Loading note…</p>
            ) : (
              <>
                {findOpen && mode !== 'reading' && (
                  <div
                    className="notes-find-bar"
                    role="search"
                    aria-label="Find and replace in note"
                  >
                    <div className="notes-find-row">
                      <button
                        type="button"
                        className="btn btn-ghost btn-xs notes-find-toggle-replace"
                        aria-expanded={replaceOpen}
                        onClick={() => {
                          setReplaceOpen((prev) => {
                            const next = !prev;
                            if (next) {
                              requestAnimationFrame(() =>
                                replaceInputRef.current?.focus(),
                              );
                            }
                            return next;
                          });
                        }}
                        title="Toggle replace (Ctrl+H)"
                        aria-label="Toggle replace"
                      >
                        {replaceOpen ? '▾' : '▸'}
                      </button>
                      <input
                        ref={findInputRef}
                        type="text"
                        className="input notes-find-input"
                        placeholder="Find in note…"
                        value={findQuery}
                        onChange={(e) => {
                          setFindQuery(e.target.value);
                          setFindMatchIndex(0);
                        }}
                        onKeyDown={(e) => {
                          if (e.key === 'Enter') {
                            e.preventDefault();
                            stepFindMatch(e.shiftKey ? -1 : 1);
                          } else if (e.key === 'Escape') {
                            e.preventDefault();
                            closeFindBar();
                          }
                        }}
                      />
                      <button
                        type="button"
                        className={`btn btn-ghost btn-xs notes-find-case-btn ${findCaseSensitive ? 'is-active' : ''}`}
                        aria-pressed={findCaseSensitive}
                        onClick={() => {
                          setFindCaseSensitive((prev) => !prev);
                          setFindMatchIndex(0);
                        }}
                        title="Match case"
                        aria-label="Match case"
                      >
                        Aa
                      </button>
                      <span className="meta-text mono-num notes-find-count">
                        {findQuery
                          ? findMatchCount > 0
                            ? `${(findMatchIndex % findMatchCount) + 1} / ${findMatchCount}`
                            : '0 / 0'
                          : ''}
                      </span>
                      <button
                        type="button"
                        className="btn btn-ghost btn-xs"
                        disabled={findMatchCount === 0}
                        onClick={() => stepFindMatch(-1)}
                        title="Previous match (Shift+Enter)"
                        aria-label="Previous match"
                      >
                        ↑
                      </button>
                      <button
                        type="button"
                        className="btn btn-ghost btn-xs"
                        disabled={findMatchCount === 0}
                        onClick={() => stepFindMatch(1)}
                        title="Next match (Enter)"
                        aria-label="Next match"
                      >
                        ↓
                      </button>
                      <button
                        type="button"
                        className="btn btn-ghost btn-xs"
                        onClick={closeFindBar}
                        title="Close find bar (Esc)"
                        aria-label="Close find bar"
                      >
                        ×
                      </button>
                    </div>

                    {replaceOpen && (
                      <div className="notes-find-row notes-replace-row">
                        <span
                          className="notes-find-row-spacer"
                          aria-hidden="true"
                        />
                        <input
                          ref={replaceInputRef}
                          type="text"
                          className="input notes-find-input"
                          placeholder="Replace with…"
                          value={replaceValue}
                          onChange={(e) => setReplaceValue(e.target.value)}
                          onKeyDown={(e) => {
                            if (e.key === 'Enter') {
                              e.preventDefault();
                              if (e.ctrlKey || e.metaKey) {
                                handleReplaceAll();
                              } else {
                                handleReplaceCurrent();
                              }
                            } else if (e.key === 'Escape') {
                              e.preventDefault();
                              closeFindBar();
                            }
                          }}
                        />
                        <button
                          type="button"
                          className="btn btn-secondary btn-xs"
                          disabled={findMatchCount === 0}
                          onClick={handleReplaceCurrent}
                          title="Replace current match (Enter)"
                        >
                          Replace
                        </button>
                        <button
                          type="button"
                          className="btn btn-secondary btn-xs"
                          disabled={findMatchCount === 0}
                          onClick={handleReplaceAll}
                          title="Replace all matches (Ctrl+Enter)"
                        >
                          Replace all
                        </button>
                      </div>
                    )}
                  </div>
                )}

                <NotePropertiesBar
                  title={draftTitle}
                  tags={parsedFm.tags}
                  folderId={activeNote.folderId}
                  folders={folders}
                  allowAgentRead={activeNote.allowAgentRead}
                  effectiveAllowAgentRead={activeNote.effectiveAllowAgentRead}
                  readOnly={mode === 'reading'}
                  onTitleChange={handleTitleChange}
                  onAddTag={handleAddTag}
                  onRemoveTag={handleRemoveTag}
                  onFolderChange={(fid) => void handleMoveNoteFolder(fid)}
                  onToggleAgentRead={() => void handleToggleNoteAgentRead()}
                />

                {mode === 'reading' && (
                  <article
                    className="notes-reading-prose"
                    aria-label="Rendered Markdown note"
                  >
                    {readingBody.trim().length === 0 ? (
                      <p className="meta-text empty-state">
                        Nothing to preview yet. Switch to Live Preview or Source
                        to start writing.
                      </p>
                    ) : (
                      <ReactMarkdown components={readingMarkdownComponents}>
                        {readingBody}
                      </ReactMarkdown>
                    )}
                  </article>
                )}

                <div
                  className="notes-editor-wrap"
                  hidden={mode === 'reading'}
                  onBlur={() => flushCurrentNote(true)}
                >
                  <MarkdownEditor
                    key={activeNote.id}
                    ref={editorRef}
                    value={draftContent}
                    mode={editorMode}
                    highlightQuery={findOpen ? findQuery : ''}
                    caseSensitive={findCaseSensitive}
                    activeMatchIndex={findMatchIndex}
                    onMatchCountChange={setFindMatchCount}
                    onChange={handleContentChange}
                  />
                </div>
              </>
            )}
          </div>
        </div>
      </div>

      <footer className="notes-statusbar">
        <div className="notes-statusbar-inner">
          <span className="meta-text mono-num">
            {activeNote
              ? `${stats.words} ${stats.words === 1 ? 'word' : 'words'} · ${stats.chars} ${stats.chars === 1 ? 'char' : 'chars'} · v${activeNote.version}`
              : `${notes.length} ${notes.length === 1 ? 'note' : 'notes'} · ${folders.length} ${folders.length === 1 ? 'folder' : 'folders'}`}
          </span>
          <span className="meta-text">
            {mode === 'live'
              ? 'Live Preview · Cmd/Ctrl+E for Reading · Cmd/Ctrl+\\ Sidebar'
              : mode === 'reading'
                ? 'Reading mode · Cmd/Ctrl+E to edit'
                : 'Source mode · Cmd/Ctrl+E for Reading'}
          </span>
        </div>
      </footer>
    </main>
  );
}
