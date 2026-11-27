import { ApiError, apiFetch } from './authClient';
import type {
  NoteDetail,
  NoteFolder,
  NoteSummary,
  NotesTreeResponse,
} from '../types/atrium';

export class NoteConflictError extends Error {
  readonly currentNote: NoteDetail;

  constructor(message: string, currentNote: NoteDetail) {
    super(message);
    this.name = 'NoteConflictError';
    this.currentNote = currentNote;
  }
}

export async function getNotesTree(): Promise<NotesTreeResponse> {
  return apiFetch<NotesTreeResponse>('/api/notes/tree');
}

export type NoteSearchMode = 'titles' | 'content' | 'tags';

export async function searchNotes(params: {
  q?: string;
  searchIn?: 'all' | NoteSearchMode;
  tag?: string;
  folderId?: string;
  includeSubfolders?: boolean;
}): Promise<NoteSummary[]> {
  const qs = new URLSearchParams();
  if (params.q && params.q.trim()) qs.set('q', params.q.trim());
  if (params.searchIn && params.searchIn !== 'all') {
    qs.set('searchIn', params.searchIn);
  }
  if (params.tag && params.tag.trim()) qs.set('tag', params.tag.trim());
  if (params.folderId) qs.set('folderId', params.folderId);
  if (params.includeSubfolders === false) qs.set('includeSubfolders', 'false');
  const suffix = qs.toString() ? `?${qs.toString()}` : '';
  return apiFetch<NoteSummary[]>(`/api/notes${suffix}`);
}

export async function getNote(id: string): Promise<NoteDetail> {
  return apiFetch<NoteDetail>(`/api/notes/${encodeURIComponent(id)}`);
}

export async function createNoteFolder(input: {
  name: string;
  parentFolderId?: string | null;
  allowAgentRead?: boolean;
}): Promise<NoteFolder> {
  return apiFetch<NoteFolder>('/api/notes/folders', {
    method: 'POST',
    body: JSON.stringify({
      name: input.name,
      parentFolderId: input.parentFolderId ?? null,
      allowAgentRead: input.allowAgentRead ?? true,
    }),
  });
}

export async function updateNoteFolder(
  id: string,
  input: {
    name?: string;
    parentFolderId?: string | null;
    moveToRoot?: boolean;
    allowAgentRead?: boolean;
  },
): Promise<NoteFolder> {
  return apiFetch<NoteFolder>(`/api/notes/folders/${encodeURIComponent(id)}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  });
}

export async function deleteNoteFolder(id: string): Promise<void> {
  await apiFetch<void>(`/api/notes/folders/${encodeURIComponent(id)}`, {
    method: 'DELETE',
  });
}

export async function createNote(input: {
  title?: string;
  folderId?: string | null;
  universityCourseId?: string | null;
  content?: string;
  allowAgentRead?: boolean;
}): Promise<NoteDetail> {
  return apiFetch<NoteDetail>('/api/notes', {
    method: 'POST',
    body: JSON.stringify({
      title: input.title ?? null,
      folderId: input.folderId ?? null,
      universityCourseId: input.universityCourseId ?? null,
      content: input.content ?? '',
      allowAgentRead: input.allowAgentRead ?? true,
    }),
  });
}

export async function openLinkedNote(
  input:
    | { kind: 'university_item'; universityActivityItemId: string }
    | {
        kind: 'calendar_event';
        calendarEventId: string;
        occurrenceStartAt?: string | null;
      }
    | { kind: 'daily'; dailyDate: string },
): Promise<NoteDetail> {
  return apiFetch<NoteDetail>('/api/notes/linked', {
    method: 'POST',
    body: JSON.stringify(input),
  });
}

export async function updateNote(
  id: string,
  input: {
    title?: string;
    content?: string;
    folderId?: string | null;
    moveToRoot?: boolean;
    allowAgentRead?: boolean;
    expectedVersion?: number;
  },
): Promise<NoteDetail> {
  try {
    return await apiFetch<NoteDetail>(`/api/notes/${encodeURIComponent(id)}`, {
      method: 'PUT',
      body: JSON.stringify(input),
    });
  } catch (err) {
    if (err instanceof ApiError && err.status === 409) {
      const payload = err.body as
        | { error?: string; currentNote?: NoteDetail }
        | undefined;
      if (payload?.currentNote) {
        throw new NoteConflictError(
          payload.error ?? 'Note was modified elsewhere.',
          payload.currentNote,
        );
      }
    }
    throw err;
  }
}

export async function deleteNote(id: string): Promise<void> {
  await apiFetch<void>(`/api/notes/${encodeURIComponent(id)}`, {
    method: 'DELETE',
  });
}

