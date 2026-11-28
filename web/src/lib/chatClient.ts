import { ApiError, apiFetch, authenticatedRawFetch } from './authClient';
import type {
  ChatAttachment,
  ChatCitation,
  ChatMutationEvent,
  ChatThreadDetail,
  ChatThreadSummary,
} from '../types/atrium';

export interface StreamChatOptions {
  messages: Array<{ role: 'user' | 'assistant'; content: string }>;
  courseId?: string | null;
  threadId?: string | null;
  modelId?: string | null;
  attachmentIds?: string[];
  enableWebSearch?: boolean;
  allowWriteTools?: boolean;
  signal?: AbortSignal;
  onThread?: (thread: ChatThreadSummary) => void;
  onStatus?: (message: string) => void;
  onReasoning?: (text: string) => void;
  onDelta: (text: string) => void;
  onCitations?: (citations: ChatCitation[]) => void;
  onMutation?: (mutation: ChatMutationEvent) => void;
  onError?: (message: string) => void;
  onDone?: () => void;
}

type ChatSseEvent =
  | { type: 'thread'; thread: ChatThreadSummary }
  | { type: 'status'; message: string }
  | { type: 'reasoning'; text: string }
  | { type: 'delta'; text: string }
  | { type: 'citations'; citations: ChatCitation[] }
  | ({ type: 'mutation' } & ChatMutationEvent)
  | { type: 'error'; message: string }
  | { type: 'done' };

export async function listChatThreads(): Promise<ChatThreadSummary[]> {
  return apiFetch<ChatThreadSummary[]>('/api/chat/threads', { method: 'GET' });
}

export async function getChatThread(id: string): Promise<ChatThreadDetail> {
  return apiFetch<ChatThreadDetail>(`/api/chat/threads/${id}`, { method: 'GET' });
}

export async function deleteChatThread(id: string): Promise<void> {
  await apiFetch<void>(`/api/chat/threads/${id}`, { method: 'DELETE' });
}

export async function uploadChatAttachment(file: File): Promise<ChatAttachment> {
  const form = new FormData();
  form.append('file', file);
  const response = await authenticatedRawFetch('/api/chat/attachments', {
    method: 'POST',
    body: form,
  });
  if (!response.ok) {
    let message = `Attachment upload failed (${response.status}).`;
    try {
      const body = (await response.json()) as { error?: string };
      if (body?.error) message = body.error;
    } catch {
      // Ignore non-JSON error
    }
    throw new ApiError(response.status, message);
  }
  return (await response.json()) as ChatAttachment;
}

export async function fetchChatAttachmentObjectUrl(id: string): Promise<string> {
  const response = await authenticatedRawFetch(`/api/chat/attachments/${id}`, {
    method: 'GET',
  });
  if (!response.ok) {
    throw new ApiError(response.status, 'Could not load attachment.');
  }
  const blob = await response.blob();
  return URL.createObjectURL(blob);
}

export async function downloadChatAttachment(
  id: string,
  fileName: string,
): Promise<void> {
  const objectUrl = await fetchChatAttachmentObjectUrl(id);
  try {
    const a = document.createElement('a');
    a.href = objectUrl;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
  } finally {
    window.setTimeout(() => URL.revokeObjectURL(objectUrl), 1000);
  }
}

export async function streamChat(options: StreamChatOptions): Promise<void> {
  const response = await authenticatedRawFetch('/api/chat/stream', {
    method: 'POST',
    body: JSON.stringify({
      messages: options.messages,
      courseId: options.courseId ?? null,
      threadId: options.threadId ?? null,
      modelId: options.modelId ?? null,
      attachmentIds:
        options.attachmentIds && options.attachmentIds.length > 0
          ? options.attachmentIds
          : null,
      enableWebSearch: options.enableWebSearch ?? true,
      allowWriteTools: options.allowWriteTools ?? true,
    }),
    signal: options.signal,
  });

  if (!response.ok) {
    let message = `Chat request failed (${response.status}).`;
    try {
      const body = (await response.json()) as { error?: string };
      if (body?.error) message = body.error;
    } catch {
      // Ignore non-JSON error body
    }
    throw new ApiError(response.status, message);
  }

  if (!response.body) {
    throw new ApiError(500, 'Streaming response body was unavailable.');
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  try {
    while (true) {
      const { value, done } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });

      const lines = buffer.split('\n');
      buffer = lines.pop() ?? '';

      for (const rawLine of lines) {
        const line = rawLine.trim();
        if (!line.startsWith('data:')) continue;
        const payload = line.slice(5).trim();
        if (!payload) continue;

        let evt: ChatSseEvent;
        try {
          evt = JSON.parse(payload) as ChatSseEvent;
        } catch {
          continue;
        }

        switch (evt.type) {
          case 'thread':
            options.onThread?.(evt.thread);
            break;
          case 'status':
            options.onStatus?.(evt.message);
            break;
          case 'reasoning':
            options.onReasoning?.(evt.text);
            break;
          case 'delta':
            options.onDelta(evt.text);
            break;
          case 'citations':
            options.onCitations?.(evt.citations);
            break;
          case 'mutation':
            options.onMutation?.({
              domain: evt.domain,
              id: evt.id,
              title: evt.title,
            });
            break;
          case 'error':
            options.onError?.(evt.message);
            break;
          case 'done':
            options.onDone?.();
            break;
        }
      }
    }
  } finally {
    reader.releaseLock();
  }
}
