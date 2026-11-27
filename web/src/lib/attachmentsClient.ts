import type { AttachmentSummary } from '../types/atrium';
import { ApiError, authenticatedRawFetch } from './authClient';

const INTERNAL_ATTACHMENT_URL_RE =
  /^\/api\/attachments\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$/i;

const blobUrlCache = new Map<string, string>();
const inFlightBlobLoads = new Map<string, Promise<string | null>>();

export interface UploadAttachmentOptions {
  kind: 'artifact' | 'inline_image';
  noteId?: string;
  universityCourseId?: string;
  universityActivityItemId?: string;
  calendarEventId?: string;
  calendarOccurrenceStartAt?: string | null;
}

export interface AttachmentFilter {
  noteId?: string;
  universityCourseId?: string;
  universityActivityItemId?: string;
  calendarEventId?: string;
  occurrenceStartAt?: string | null;
}

export function isInternalAttachmentUrl(url: string): boolean {
  return INTERNAL_ATTACHMENT_URL_RE.test(url.trim());
}

export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export async function uploadAttachment(
  file: File,
  options: UploadAttachmentOptions,
): Promise<AttachmentSummary> {
  const form = new FormData();
  form.append('file', file, file.name || 'attachment');
  form.append('kind', options.kind);
  if (options.noteId) form.append('noteId', options.noteId);
  if (options.universityCourseId) {
    form.append('universityCourseId', options.universityCourseId);
  }
  if (options.universityActivityItemId) {
    form.append('universityActivityItemId', options.universityActivityItemId);
  }
  if (options.calendarEventId) {
    form.append('calendarEventId', options.calendarEventId);
  }
  if (options.calendarOccurrenceStartAt) {
    form.append('calendarOccurrenceStartAt', options.calendarOccurrenceStartAt);
  }

  const res = await authenticatedRawFetch('/api/attachments', {
    method: 'POST',
    body: form,
  });

  if (!res.ok) {
    let message = `Upload failed (${res.status})`;
    try {
      const body = (await res.json()) as { error?: string };
      if (body?.error) message = body.error;
    } catch {
      // Ignore parse errors
    }
    throw new ApiError(res.status, message);
  }

  return (await res.json()) as AttachmentSummary;
}

export async function listAttachments(
  filter: AttachmentFilter,
): Promise<AttachmentSummary[]> {
  const params = new URLSearchParams();
  if (filter.noteId) params.set('noteId', filter.noteId);
  if (filter.universityCourseId) {
    params.set('universityCourseId', filter.universityCourseId);
  }
  if (filter.universityActivityItemId) {
    params.set('universityActivityItemId', filter.universityActivityItemId);
  }
  if (filter.calendarEventId) {
    params.set('calendarEventId', filter.calendarEventId);
  }
  if (filter.occurrenceStartAt) {
    params.set('occurrenceStartAt', filter.occurrenceStartAt);
  }

  const res = await authenticatedRawFetch(`/api/attachments?${params.toString()}`);
  if (!res.ok) {
    throw new ApiError(res.status, 'Could not load attachments.');
  }
  return (await res.json()) as AttachmentSummary[];
}

export async function deleteAttachment(id: string): Promise<void> {
  const res = await authenticatedRawFetch(`/api/attachments/${id}`, {
    method: 'DELETE',
  });
  if (!res.ok && res.status !== 204) {
    throw new ApiError(res.status, 'Could not delete attachment.');
  }
  const cached = blobUrlCache.get(`/api/attachments/${id}`);
  if (cached) {
    URL.revokeObjectURL(cached);
    blobUrlCache.delete(`/api/attachments/${id}`);
  }
}

export async function downloadAttachment(
  id: string,
  fileName: string,
): Promise<void> {
  const res = await authenticatedRawFetch(`/api/attachments/${id}`);
  if (!res.ok) {
    throw new ApiError(res.status, 'Could not download attachment.');
  }
  const blob = await res.blob();
  const objectUrl = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = objectUrl;
  link.download = fileName || 'attachment';
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(objectUrl), 10_000);
}

export function resolveAttachmentImageUrl(
  url: string,
): Promise<string | null> {
  const trimmed = url.trim();
  if (!isInternalAttachmentUrl(trimmed)) {
    return Promise.resolve(trimmed);
  }

  const cached = blobUrlCache.get(trimmed);
  if (cached) {
    return Promise.resolve(cached);
  }

  const pending = inFlightBlobLoads.get(trimmed);
  if (pending) {
    return pending;
  }

  const promise = (async () => {
    try {
      const res = await authenticatedRawFetch(trimmed);
      if (!res.ok) return null;
      const blob = await res.blob();
      const objectUrl = URL.createObjectURL(blob);
      blobUrlCache.set(trimmed, objectUrl);
      return objectUrl;
    } catch {
      return null;
    } finally {
      inFlightBlobLoads.delete(trimmed);
    }
  })();

  inFlightBlobLoads.set(trimmed, promise);
  return promise;
}


export function isPdfAttachment(a: {
  contentType: string;
  fileName: string;
}): boolean {
  return (
    a.contentType.toLowerCase().startsWith('application/pdf') ||
    a.fileName.toLowerCase().endsWith('.pdf')
  );
}
