import { useState, type KeyboardEvent } from 'react';
import { normalizeTag } from '../lib/frontmatter';
import type { NoteFolder, NoteLinkKind } from '../types/atrium';
import { CloseIcon, EyeIcon, EyeOffIcon } from './Icons';

interface NotePropertiesBarProps {
  title: string;
  tags: string[];
  folderId: string | null;
  folders: NoteFolder[];
  linkKind?: NoteLinkKind;
  linkLabel?: string | null;
  universityCourseId?: string | null;
  allowAgentRead: boolean;
  effectiveAllowAgentRead: boolean;
  readOnly: boolean;
  onTitleChange: (nextTitle: string) => void;
  onAddTag: (tag: string) => void;
  onRemoveTag: (tag: string) => void;
  onFolderChange: (nextFolderId: string | null) => void;
  onToggleAgentRead: () => void;
  onJumpToUniversityCourse?: (courseId: string) => void;
}

export function NotePropertiesBar({
  title,
  tags,
  folderId,
  folders,
  linkKind = 'general',
  linkLabel = null,
  universityCourseId = null,
  allowAgentRead,
  effectiveAllowAgentRead,
  readOnly,
  onTitleChange,
  onAddTag,
  onRemoveTag,
  onFolderChange,
  onToggleAgentRead,
  onJumpToUniversityCourse,
}: NotePropertiesBarProps) {
  const [tagDraft, setTagDraft] = useState('');

  const inheritedHidden = allowAgentRead && !effectiveAllowAgentRead;

  const commitTag = () => {
    const normalized = normalizeTag(tagDraft);
    setTagDraft('');
    if (!normalized) return;
    if (!tags.includes(normalized)) {
      onAddTag(normalized);
    }
  };

  const handleTagKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter' || e.key === ',') {
      e.preventDefault();
      commitTag();
    } else if (e.key === 'Backspace' && !tagDraft && tags.length > 0) {
      onRemoveTag(tags[tags.length - 1]);
    }
  };

  return (
    <div className="notes-properties-bar">
      {linkKind !== 'general' && linkLabel && (
        <div className="notes-link-breadcrumb">
          <span className="notes-link-badge">
            {linkKind === 'calendar_event' ? 'Event' : 'University'}
          </span>
          <span className="meta-text notes-link-path">{linkLabel}</span>
          {universityCourseId && onJumpToUniversityCourse && (
            <button
              type="button"
              className="btn btn-ghost btn-xs"
              onClick={() => onJumpToUniversityCourse(universityCourseId)}
            >
              Open course →
            </button>
          )}
        </div>
      )}

      {readOnly ? (
        <h1 className="notes-title-heading">{title}</h1>
      ) : (
        <input
          id="note-title-input"
          type="text"
          className="notes-title-input"
          value={title}
          maxLength={200}
          placeholder="Untitled"
          aria-label="Note title"
          onChange={(e) => onTitleChange(e.target.value)}
        />
      )}

      <div className="notes-properties-meta-row">
        <div className="notes-properties-tags" aria-label="Note tags">
          {tags.map((tag) => (
            <span key={tag} className="notes-tag-chip">
              <span>#{tag}</span>
              {!readOnly && (
                <button
                  type="button"
                  className="notes-tag-chip-remove"
                  aria-label={`Remove tag ${tag}`}
                  onClick={() => onRemoveTag(tag)}
                >
                  <CloseIcon />
                </button>
              )}
            </span>
          ))}
          {!readOnly && (
            <input
              type="text"
              className="notes-tag-add-input"
              placeholder="+ Add tag"
              maxLength={40}
              aria-label="Add tag"
              value={tagDraft}
              onChange={(e) => setTagDraft(e.target.value)}
              onBlur={commitTag}
              onKeyDown={handleTagKeyDown}
            />
          )}
        </div>

        <div className="notes-properties-controls">
          {!readOnly && linkKind === 'general' && (
            <select
              className="input notes-folder-select"
              aria-label="Note folder"
              value={folderId ?? ''}
              onChange={(e) =>
                onFolderChange(e.target.value ? e.target.value : null)
              }
            >
              <option value="">Unfiled</option>
              {folders.map((folder) => (
                <option key={folder.id} value={folder.id}>
                  {folder.name}
                </option>
              ))}
            </select>
          )}

          <button
            type="button"
            className={`btn btn-ghost btn-xs notes-agent-toggle ${
              effectiveAllowAgentRead ? 'is-allowed' : 'is-hidden'
            }`}
            disabled={readOnly || inheritedHidden}
            title={
              inheritedHidden
                ? 'Hidden from AI agents by parent folder'
                : allowAgentRead
                  ? 'AI agents with notes:read can read this note (click to hide)'
                  : 'Hidden from AI agents (click to allow)'
            }
            onClick={onToggleAgentRead}
          >
            {effectiveAllowAgentRead ? <EyeIcon /> : <EyeOffIcon />}
            <span>
              {inheritedHidden
                ? 'Hidden by folder'
                : allowAgentRead
                  ? 'Agents: Allowed'
                  : 'Agents: Hidden'}
            </span>
          </button>
        </div>
      </div>
    </div>
  );
}

