import {
  useEffect,
  useRef,
  useState,
  type FormEvent,
  type KeyboardEvent,
} from 'react';
import ReactMarkdown from 'react-markdown';
import rehypeKatex from 'rehype-katex';
import remarkMath from 'remark-math';
import { downloadAttachment } from '../lib/attachmentsClient';
import type { ChatCitation, ChatMessage, ChatScope } from '../types/atrium';
import { ChatIcon, CloseIcon, PlusIcon } from './Icons';

interface ChatSidebarProps {
  isOpen: boolean;
  scope: ChatScope;
  isConfigured: boolean;
  modelLabel?: string | null;
  messages: ChatMessage[];
  streaming: boolean;
  statusText: string | null;
  onClose: () => void;
  onClearScope: () => void;
  onNewChat: () => void;
  onSend: (prompt: string) => void;
  onStop: () => void;
  onOpenSettings: () => void;
  onOpenNote: (noteId: string) => void;
}

export function ChatSidebar({
  isOpen,
  scope,
  isConfigured,
  modelLabel,
  messages,
  streaming,
  statusText,
  onClose,
  onClearScope,
  onNewChat,
  onSend,
  onStop,
  onOpenSettings,
  onOpenNote,
}: ChatSidebarProps) {
  const [draft, setDraft] = useState('');
  const endRef = useRef<HTMLDivElement | null>(null);
  const inputRef = useRef<HTMLTextAreaElement | null>(null);

  useEffect(() => {
    if (isOpen) {
      endRef.current?.scrollIntoView({ behavior: 'smooth', block: 'end' });
    }
  }, [isOpen, messages, statusText]);

  useEffect(() => {
    if (isOpen && isConfigured) {
      inputRef.current?.focus();
    }
  }, [isOpen, isConfigured, scope.courseId]);

  useEffect(() => {
    if (!isOpen) return;
    const onKeyDown = (e: globalThis.KeyboardEvent) => {
      if (e.key === 'Escape') {
        onClose();
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [isOpen, onClose]);

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    const trimmed = draft.trim();
    if (!trimmed || streaming || !isConfigured) return;
    setDraft('');
    onSend(trimmed);
  }

  function handleKeyDown(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      const trimmed = draft.trim();
      if (!trimmed || streaming || !isConfigured) return;
      setDraft('');
      onSend(trimmed);
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
      <div
        className="chat-sidebar-backdrop"
        aria-hidden="true"
        onClick={onClose}
      />
      <aside
        className="chat-sidebar-drawer"
        role="complementary"
        aria-label="Atrium AI Assistant"
      >
        <header className="chat-sidebar-header">
          <div className="chat-sidebar-title-group">
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
            {messages.length > 0 && (
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
              className="btn btn-ghost btn-icon-sm"
              onClick={onClose}
              aria-label="Close AI Assistant"
              title="Close (Esc)"
            >
              <CloseIcon />
            </button>
          </div>
        </header>

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
          {!isConfigured ? (
            <div className="chat-empty-state">
              <p className="row-title">Connect your AI model</p>
              <p className="meta-text">
                Add an OpenAI-compatible Inference URL and Model name (plus optional
                API key) in Account settings to start chatting with your notes,
                courses, and schedule.
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
                    <div className="chat-markdown-body">
                      {msg.content ? (
                        <ReactMarkdown
                          remarkPlugins={[remarkMath]}
                          rehypePlugins={[rehypeKatex]}
                        >
                          {msg.content}
                        </ReactMarkdown>
                      ) : (
                        <span className="meta-text">…</span>
                      )}
                    </div>
                  ) : (
                    <p className="chat-user-text">{msg.content}</p>
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
                          <span className="chat-citation-title">{c.title}</span>
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
              <span className="status-dot" data-state="checking" aria-hidden="true" />
              <span>{statusText}</span>
            </div>
          )}

          <div ref={endRef} />
        </div>

        <form className="chat-sidebar-composer" onSubmit={handleSubmit}>
          <textarea
            ref={inputRef}
            className="input chat-composer-input"
            rows={2}
            value={draft}
            disabled={!isConfigured || streaming}
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder={
              !isConfigured
                ? 'Configure AI settings in Account first…'
                : isCourseScoped
                  ? `Ask about ${scope.courseLabel || 'this course'}…`
                  : 'Ask about your day, notes, or slides…'
            }
          />
          <div className="chat-composer-footer">
            <span className="meta-text">Enter to send · Shift+Enter newline</span>
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
                disabled={!isConfigured || !draft.trim()}
              >
                Send
              </button>
            )}
          </div>
        </form>
      </aside>
    </>
  );
}

