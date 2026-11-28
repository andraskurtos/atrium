import {
  useEffect,
  useRef,
  useState,
  type ChangeEvent,
  type ClipboardEvent,
  type DragEvent,
  type FormEvent,
  type KeyboardEvent,
} from 'react';
import ReactMarkdown from 'react-markdown';
import rehypeKatex from 'rehype-katex';
import remarkGfm from 'remark-gfm';
import remarkMath from 'remark-math';
import { downloadAttachment } from '../lib/attachmentsClient';
import { formatRelativeTime } from '../lib/authClient';
import {
  downloadChatAttachment,
  fetchChatAttachmentObjectUrl,
  uploadChatAttachment,
} from '../lib/chatClient';
import { normalizeBlockMathFences } from '../lib/markdownUtils';
import type {
  AiModel,
  ChatAttachment,
  ChatCitation,
  ChatMessage,
  ChatScope,
  ChatThreadSummary,
} from '../types/atrium';
import { ChatIcon, CloseIcon, PlusIcon } from './Icons';

function ChatImageThumb({ attachment }: { attachment: ChatAttachment }) {
  const [src, setSrc] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    let url: string | null = null;
    void fetchChatAttachmentObjectUrl(attachment.id)
      .then((objectUrl) => {
        if (!active) {
          URL.revokeObjectURL(objectUrl);
          return;
        }
        url = objectUrl;
        setSrc(objectUrl);
      })
      .catch(() => undefined);

    return () => {
      active = false;
      if (url) URL.revokeObjectURL(url);
    };
  }, [attachment.id]);

  if (!src) {
    return (
      <span className="chat-attachment-chip">
        <span aria-hidden="true">🖼️</span>
        <span>{attachment.fileName}</span>
      </span>
    );
  }

  return (
    <button
      type="button"
      className="chat-attachment-thumb-btn"
      onClick={() => void downloadChatAttachment(attachment.id, attachment.fileName)}
      title={`Download ${attachment.fileName}`}
    >
      <img
        src={src}
        alt={attachment.fileName}
        className="chat-attachment-thumb"
      />
    </button>
  );
}

interface ChatSidebarProps {
  isOpen: boolean;
  isFullscreen: boolean;
  scope: ChatScope;
  isConfigured: boolean;
  modelLabel?: string | null;
  models: AiModel[];
  selectedModelId: string | null;
  defaultVisionModelId: string | null;
  enableWebSearch: boolean;
  allowWriteTools: boolean;
  messages: ChatMessage[];
  threads: ChatThreadSummary[];
  activeThreadId: string | null;
  loadingThread: boolean;
  streaming: boolean;
  statusText: string | null;
  onClose: () => void;
  onToggleFullscreen: () => void;
  onClearScope: () => void;
  onNewChat: () => void;
  onSelectThread: (threadId: string) => void;
  onDeleteThread: (threadId: string) => void;
  onSelectModel: (modelId: string) => void;
  onToggleWebSearch: () => void;
  onToggleWriteTools: () => void;
  onSend: (prompt: string, attachments: ChatAttachment[]) => void;
  onStop: () => void;
  onOpenSettings: () => void;
  onOpenNote: (noteId: string) => void;
}

export function ChatSidebar({
  isOpen,
  isFullscreen,
  scope,
  isConfigured,
  modelLabel,
  models,
  selectedModelId,
  defaultVisionModelId,
  enableWebSearch,
  allowWriteTools,
  messages,
  threads,
  activeThreadId,
  loadingThread,
  streaming,
  statusText,
  onClose,
  onToggleFullscreen,
  onClearScope,
  onNewChat,
  onSelectThread,
  onDeleteThread,
  onSelectModel,
  onToggleWebSearch,
  onToggleWriteTools,
  onSend,
  onStop,
  onOpenSettings,
  onOpenNote,
}: ChatSidebarProps) {
  const [draft, setDraft] = useState('');
  const [mobileHistoryOpen, setMobileHistoryOpen] = useState(false);
  const [pendingAttachments, setPendingAttachments] = useState<ChatAttachment[]>([]);
  const [uploadingFiles, setUploadingFiles] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const [isDragOver, setIsDragOver] = useState(false);
  const endRef = useRef<HTMLDivElement | null>(null);
  const inputRef = useRef<HTMLTextAreaElement | null>(null);
  const fileInputRef = useRef<HTMLInputElement | null>(null);

  useEffect(() => {
    if (isOpen) {
      endRef.current?.scrollIntoView({ behavior: 'smooth', block: 'end' });
    }
  }, [isOpen, messages, statusText]);

  useEffect(() => {
    if (isOpen && isConfigured) {
      inputRef.current?.focus();
    }
  }, [isOpen, isConfigured, scope.courseId, activeThreadId]);

  useEffect(() => {
    if (!isFullscreen) {
      setMobileHistoryOpen(false);
    }
  }, [isFullscreen]);

  useEffect(() => {
    if (!isOpen) return;
    const onKeyDown = (e: globalThis.KeyboardEvent) => {
      if (e.key === 'Escape') {
        if (isFullscreen) {
          onToggleFullscreen();
        } else {
          onClose();
        }
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [isOpen, isFullscreen, onClose, onToggleFullscreen]);

  async function handleUploadFiles(fileList: FileList | File[]) {
    const files = Array.from(fileList);
    if (files.length === 0) return;

    const remainingSlots = 5 - pendingAttachments.length;
    if (remainingSlots <= 0) {
      setUploadError('Maximum 5 attachments per message.');
      return;
    }

    const toUpload = files.slice(0, remainingSlots);
    setUploadError(null);
    setUploadingFiles(true);
    try {
      const uploaded: ChatAttachment[] = [];
      for (const file of toUpload) {
        const dto = await uploadChatAttachment(file);
        uploaded.push(dto);
      }
      setPendingAttachments((prev) => [...prev, ...uploaded]);
    } catch (err) {
      setUploadError(err instanceof Error ? err.message : 'File upload failed.');
    } finally {
      setUploadingFiles(false);
    }
  }

  function handleFileInputChange(e: ChangeEvent<HTMLInputElement>) {
    if (e.target.files) {
      void handleUploadFiles(e.target.files);
    }
    e.target.value = '';
  }

  function handlePaste(e: ClipboardEvent<HTMLTextAreaElement>) {
    const items = e.clipboardData?.items;
    if (!items) return;
    const files: File[] = [];
    for (let i = 0; i < items.length; i += 1) {
      if (items[i].kind === 'file') {
        const f = items[i].getAsFile();
        if (f) files.push(f);
      }
    }
    if (files.length > 0) {
      e.preventDefault();
      void handleUploadFiles(files);
    }
  }

  function handleDragOver(e: DragEvent<HTMLElement>) {
    e.preventDefault();
    if (!isDragOver) setIsDragOver(true);
  }

  function handleDragLeave(e: DragEvent<HTMLElement>) {
    if (e.currentTarget.contains(e.relatedTarget as Node | null)) return;
    setIsDragOver(false);
  }

  function handleDrop(e: DragEvent<HTMLElement>) {
    e.preventDefault();
    setIsDragOver(false);
    if (e.dataTransfer?.files && e.dataTransfer.files.length > 0) {
      void handleUploadFiles(e.dataTransfer.files);
    }
  }

  function submitDraft() {
    const trimmed = draft.trim();
    if ((!trimmed && pendingAttachments.length === 0) || streaming || !isConfigured || uploadingFiles) {
      return;
    }
    const prompt = trimmed || 'Analyze the attached file(s).';
    const atts = pendingAttachments;
    setDraft('');
    setPendingAttachments([]);
    setUploadError(null);
    onSend(prompt, atts);
  }

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    submitDraft();
  }

  function handleKeyDown(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      submitDraft();
    }
  }

  function handleCitationClick(citation: ChatCitation) {
    if (citation.sourceType === 'note') {
      onOpenNote(citation.id);
    } else {
      void downloadAttachment(citation.id, citation.title);
    }
  }

  if (!isOpen) return null;

  const isCourseScoped = Boolean(scope.courseId);

  return (
    <>
      {!isFullscreen && (
        <div
          className="chat-sidebar-backdrop"
          aria-hidden="true"
          onClick={onClose}
        />
      )}
      <aside
        className={`chat-sidebar-drawer ${isFullscreen ? 'is-fullscreen' : ''} ${isDragOver ? 'is-drag-over' : ''}`}
        role="complementary"
        aria-label="Atrium AI Assistant"
        onDragOver={handleDragOver}
        onDragLeave={handleDragLeave}
        onDrop={handleDrop}
      >
        {isDragOver && (
          <div className="chat-drop-overlay" aria-hidden="true">
            <span>Drop files (.ics, PDF, DOCX, images…) to attach</span>
          </div>
        )}
        <header className="chat-sidebar-header">
          <div className="chat-sidebar-title-group">
            {isFullscreen && (
              <button
                type="button"
                className="btn btn-ghost btn-xs chat-history-mobile-toggle"
                onClick={() => setMobileHistoryOpen((prev) => !prev)}
                aria-expanded={mobileHistoryOpen}
              >
                History ({threads.length})
              </button>
            )}
            <ChatIcon />
            <div className="chat-sidebar-title-text">
              <h2 className="chat-sidebar-title">Assistant</h2>
              {modelLabel && (
                <span className="meta-text mono-num chat-sidebar-model">
                  {modelLabel}
                </span>
              )}
            </div>
          </div>
          <div className="chat-sidebar-header-actions">
            {(messages.length > 0 || activeThreadId) && (
              <button
                type="button"
                className="btn btn-ghost btn-xs"
                onClick={onNewChat}
                disabled={streaming}
                title="Start a new chat thread"
              >
                <PlusIcon />
                <span>New</span>
              </button>
            )}
            <button
              type="button"
              className="btn btn-ghost btn-xs"
              onClick={onToggleFullscreen}
              aria-pressed={isFullscreen}
              title={isFullscreen ? 'Exit fullscreen' : 'Fullscreen with history'}
            >
              <span>{isFullscreen ? 'Compact' : 'Fullscreen'}</span>
            </button>
            <button
              type="button"
              className="btn btn-ghost btn-icon-sm"
              onClick={onClose}
              aria-label="Close AI Assistant"
              title="Close (Esc)"
            >
              <CloseIcon />
            </button>
          </div>
        </header>

        <div className={`chat-body-layout ${isFullscreen ? 'is-fullscreen' : ''}`}>
          {isFullscreen && (
            <nav
              className={`chat-history-rail ${mobileHistoryOpen ? 'is-mobile-open' : ''}`}
              aria-label="Saved chat history"
            >
              <div className="chat-history-rail-top">
                <button
                  type="button"
                  className="btn btn-primary btn-compact chat-history-new-btn"
                  disabled={streaming}
                  onClick={() => {
                    onNewChat();
                    setMobileHistoryOpen(false);
                  }}
                >
                  <PlusIcon />
                  <span>New chat</span>
                </button>
              </div>
              <div className="chat-history-list">
                {threads.length === 0 ? (
                  <p className="meta-text chat-history-empty">
                    No saved chats yet. Start a conversation and it will appear here.
                  </p>
                ) : (
                  threads.map((thread) => {
                    const active = thread.id === activeThreadId;
                    return (
                      <div
                        key={thread.id}
                        className={`chat-history-item ${active ? 'is-active' : ''}`}
                      >
                        <button
                          type="button"
                          className="chat-history-item-btn"
                          disabled={streaming}
                          onClick={() => {
                            onSelectThread(thread.id);
                            setMobileHistoryOpen(false);
                          }}
                        >
                          <span className="chat-history-item-title">
                            {thread.title}
                          </span>
                          <span className="chat-history-item-meta meta-text">
                            {thread.courseLabel ? `${thread.courseLabel} · ` : ''}
                            {formatRelativeTime(thread.updatedAt)}
                          </span>
                        </button>
                        <button
                          type="button"
                          className="chat-history-item-delete"
                          disabled={streaming}
                          aria-label={`Delete chat ${thread.title}`}
                          title="Delete chat"
                          onClick={(e) => {
                            e.stopPropagation();
                            onDeleteThread(thread.id);
                          }}
                        >
                          ×
                        </button>
                      </div>
                    );
                  })
                )}
              </div>
            </nav>
          )}

          <div className="chat-main-pane">
            <div className="chat-sidebar-scope-bar">
              {isCourseScoped ? (
                <span className="pill-accent chat-scope-pill">
                  <span>Course: {scope.courseLabel || 'Selected course'}</span>
                  <button
                    type="button"
                    className="chat-scope-clear"
                    onClick={onClearScope}
                    disabled={streaming}
                    aria-label="Switch back to All Atrium scope"
                    title="Switch to All Atrium"
                  >
                    ×
                  </button>
                </span>
              ) : (
                <span className="pill-neutral chat-scope-pill">
                  All Atrium (Today, Calendar, Courses &amp; Notes)
                </span>
              )}
            </div>

            <div className="chat-sidebar-messages" aria-live="polite">
              <div className="chat-messages-inner">
                {loadingThread ? (
                  <div className="chat-empty-state">
                    <p className="meta-text">Loading conversation…</p>
                  </div>
                ) : !isConfigured ? (
                  <div className="chat-empty-state">
                    <p className="row-title">Connect your AI model</p>
                    <p className="meta-text">
                      Add an OpenAI-compatible Inference URL and Model name (plus
                      optional API key) in Account settings to start chatting with
                      your notes, courses, and schedule.
                    </p>
                    <button
                      type="button"
                      className="btn btn-primary btn-compact"
                      onClick={() => {
                        onClose();
                        onOpenSettings();
                      }}
                    >
                      Open AI Settings
                    </button>
                  </div>
                ) : messages.length === 0 ? (
                  <div className="chat-empty-state">
                    <p className="row-title">
                      {isCourseScoped
                        ? `Ask about ${scope.courseLabel || 'this course'}`
                        : 'Ask anything about your Atrium'}
                    </p>
                    <p className="meta-text">
                      {isCourseScoped
                        ? 'Questions are strictly scoped to this course’s lectures, notes, and uploaded files.'
                        : 'Ask about today’s schedule, upcoming deadlines, university lectures, Markdown notes, or uploaded PDFs & slides.'}
                    </p>
                  </div>
                ) : (
                  messages.map((msg) => (
                    <div
                      key={msg.id}
                      className={`chat-bubble-row is-${msg.role} ${msg.error ? 'is-error' : ''}`}
                    >
                      <div className="chat-bubble">
                        {msg.role === 'assistant' ? (
                          <>
                            {msg.reasoning && msg.reasoning.trim().length > 0 && (
                              <details className="chat-reasoning-box">
                                <summary className="chat-reasoning-summary">
                                  <span>Reasoning</span>
                                </summary>
                                <div className="chat-reasoning-body">
                                  {msg.reasoning}
                                </div>
                              </details>
                            )}
                            <div className="chat-markdown-body">
                              {msg.content ? (
                                <ReactMarkdown
                                  remarkPlugins={[remarkGfm, remarkMath]}
                                  rehypePlugins={[rehypeKatex]}
                                  components={{
                                    table: ({ children }) => (
                                      <div className="chat-table-scroll">
                                        <table>{children}</table>
                                      </div>
                                    ),
                                  }}
                                >
                                  {normalizeBlockMathFences(msg.content)}
                                </ReactMarkdown>
                              ) : (
                                <span className="meta-text">
                                  {msg.reasoning ? 'Thinking…' : '…'}
                                </span>
                              )}
                            </div>
                          </>
                        ) : (
                          <>
                            {msg.attachments && msg.attachments.length > 0 && (
                              <div className="chat-msg-attachments">
                                {msg.attachments.map((att) =>
                                  att.kind === 'image' ? (
                                    <ChatImageThumb key={att.id} attachment={att} />
                                  ) : (
                                    <button
                                      key={att.id}
                                      type="button"
                                      className="chat-attachment-chip"
                                      onClick={() =>
                                        void downloadChatAttachment(att.id, att.fileName)
                                      }
                                      title={att.summary ?? `Download ${att.fileName}`}
                                    >
                                      <span aria-hidden="true">
                                        {att.kind === 'ics' ? '📅' : '📎'}
                                      </span>
                                      <span>{att.fileName}</span>
                                    </button>
                                  ),
                                )}
                              </div>
                            )}
                            <p className="chat-user-text">{msg.content}</p>
                          </>
                        )}

                        {msg.citations && msg.citations.length > 0 && (
                          <div className="chat-citations" aria-label="Sources">
                            {msg.citations.map((c) => (
                              <button
                                key={`${c.sourceType}:${c.id}`}
                                type="button"
                                className="chat-citation-chip"
                                onClick={() => handleCitationClick(c)}
                                title={
                                  c.sourceType === 'note'
                                    ? `Open note: ${c.title}`
                                    : `Download artifact: ${c.title}`
                                }
                              >
                                <span aria-hidden="true">
                                  {c.sourceType === 'note' ? '📄' : '📎'}
                                </span>
                                <span className="chat-citation-title">
                                  {c.title}
                                </span>
                                {c.pageLabel && (
                                  <span className="meta-text">· {c.pageLabel}</span>
                                )}
                              </button>
                            ))}
                          </div>
                        )}
                      </div>
                    </div>
                  ))
                )}

                {streaming && statusText && (
                  <div className="chat-status-indicator meta-text">
                    <span
                      className="status-dot"
                      data-state="checking"
                      aria-hidden="true"
                    />
                    <span>{statusText}</span>
                  </div>
                )}

                <div ref={endRef} />
              </div>
            </div>

            <form className="chat-sidebar-composer" onSubmit={handleSubmit}>
              <div className="chat-composer-inner">
                <input
                  ref={fileInputRef}
                  type="file"
                  multiple
                  hidden
                  accept=".ics,.pdf,.docx,.pptx,.txt,.md,.csv,.json,.png,.jpg,.jpeg,.webp"
                  onChange={handleFileInputChange}
                />

                {pendingAttachments.length > 0 && (
                  <div className="chat-pending-attachments">
                    {pendingAttachments.map((att) => (
                      <span key={att.id} className="chat-attachment-chip is-pending">
                        <span aria-hidden="true">
                          {att.kind === 'image'
                            ? '🖼️'
                            : att.kind === 'ics'
                              ? '📅'
                              : '📎'}
                        </span>
                        <span className="chat-attachment-name">{att.fileName}</span>
                        <button
                          type="button"
                          className="chat-attachment-remove"
                          onClick={() =>
                            setPendingAttachments((prev) =>
                              prev.filter((x) => x.id !== att.id),
                            )
                          }
                          aria-label={`Remove ${att.fileName}`}
                        >
                          ×
                        </button>
                      </span>
                    ))}
                  </div>
                )}

                {uploadError && (
                  <p className="field-error chat-upload-error" role="alert">
                    {uploadError}
                  </p>
                )}

                {(() => {
                  const hasPendingImage = pendingAttachments.some(
                    (a) => a.kind === 'image',
                  );
                  if (!hasPendingImage) return null;
                  const activeModel = models.find((m) => m.id === selectedModelId);
                  if (activeModel?.supportsVision) return null;
                  const visionFallback = models.find(
                    (m) => m.id === defaultVisionModelId && m.supportsVision,
                  );
                  return (
                    <p className="meta-text chat-vision-hint">
                      {visionFallback
                        ? `Images will auto-route to ${visionFallback.displayName}.`
                        : 'No vision model configured — enable a vision model in Account → AI Assistant.'}
                    </p>
                  );
                })()}

                <textarea
                  ref={inputRef}
                  className="input chat-composer-input"
                  rows={2}
                  value={draft}
                  disabled={!isConfigured || streaming || loadingThread}
                  onChange={(e) => setDraft(e.target.value)}
                  onKeyDown={handleKeyDown}
                  onPaste={handlePaste}
                  placeholder={
                    !isConfigured
                      ? 'Configure AI settings in Account first…'
                      : isCourseScoped
                        ? `Ask about ${scope.courseLabel || 'this course'}…`
                        : 'Ask anything, paste a syllabus link, or drop a Neptun .ics…'
                  }
                />
                <div className="chat-composer-footer">
                  <div className="chat-composer-tools">
                    <button
                      type="button"
                      className="btn btn-ghost btn-xs chat-attach-btn"
                      disabled={!isConfigured || streaming || uploadingFiles}
                      onClick={() => fileInputRef.current?.click()}
                      title="Attach .ics, PDF, DOCX, PPTX, text, or image"
                      aria-label="Attach file"
                    >
                      {uploadingFiles ? 'Uploading…' : '📎'}
                    </button>
                    <button
                      type="button"
                      className={`chat-mode-toggle ${enableWebSearch ? 'is-active' : ''}`}
                      disabled={streaming}
                      aria-pressed={enableWebSearch}
                      onClick={onToggleWebSearch}
                      title={
                        enableWebSearch
                          ? 'Web search enabled (click to disable)'
                          : 'Web search disabled (click to enable)'
                      }
                    >
                      🌐 Web
                    </button>
                    <button
                      type="button"
                      className={`chat-mode-toggle ${allowWriteTools ? 'is-active' : ''}`}
                      disabled={streaming}
                      aria-pressed={allowWriteTools}
                      onClick={onToggleWriteTools}
                      title={
                        allowWriteTools
                          ? 'Assistant can read and create/edit items (click for Read-only)'
                          : 'Assistant is Read-only (click to allow Read + Write)'
                      }
                    >
                      {allowWriteTools ? 'Read + Write' : 'Read-only'}
                    </button>
                    {models.length > 0 && (
                      <select
                        className="chat-model-picker"
                        aria-label="Select AI model"
                        value={selectedModelId ?? models[0]?.id ?? ''}
                        disabled={streaming}
                        onChange={(e) => onSelectModel(e.target.value)}
                      >
                        {models.map((m) => (
                          <option key={m.id} value={m.id}>
                            {m.displayName}
                            {m.supportsVision ? ' · Vision' : ''}
                          </option>
                        ))}
                      </select>
                    )}
                  </div>
                  {streaming ? (
                    <button
                      type="button"
                      className="btn btn-secondary btn-xs"
                      onClick={onStop}
                    >
                      Stop
                    </button>
                  ) : (
                    <button
                      type="submit"
                      className="btn btn-primary btn-xs"
                      disabled={
                        !isConfigured ||
                        loadingThread ||
                        uploadingFiles ||
                        (!draft.trim() && pendingAttachments.length === 0)
                      }
                    >
                      Send
                    </button>
                  )}
                </div>
              </div>
            </form>
          </div>
        </div>
      </aside>
    </>
  );
}
