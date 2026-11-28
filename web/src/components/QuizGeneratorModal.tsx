import { useEffect, useState, type FormEvent } from 'react';
import type { QuizGenerateOptions } from '../types/atrium';
import { CloseIcon } from './Icons';

interface QuizGeneratorModalProps {
  isOpen: boolean;
  targetLabel: string;
  scopeKind: 'lecture' | 'course';
  onClose: () => void;
  onSubmit: (options: QuizGenerateOptions) => void;
}

const DEFAULT_LECTURE_INSTRUCTIONS =
  'Test deep understanding of the key concepts, definitions, formulas, and practical examples from this lecture. Include plausible distractors and clear explanations for every answer.';

const DEFAULT_COURSE_INSTRUCTIONS =
  'Create a comprehensive exam-prep quiz spanning all major lectures, topics, and concepts in this course. Balance conceptual understanding, comparisons, and applied problem-solving.';

export function QuizGeneratorModal({
  isOpen,
  targetLabel,
  scopeKind,
  onClose,
  onSubmit,
}: QuizGeneratorModalProps) {
  const [questionCount, setQuestionCount] = useState<number>(
    scopeKind === 'course' ? 15 : 8,
  );
  const [difficulty, setDifficulty] =
    useState<QuizGenerateOptions['difficulty']>('mixed');
  const [customInstructions, setCustomInstructions] = useState<string>(
    scopeKind === 'course'
      ? DEFAULT_COURSE_INSTRUCTIONS
      : DEFAULT_LECTURE_INSTRUCTIONS,
  );

  useEffect(() => {
    if (!isOpen) return;
    setQuestionCount(scopeKind === 'course' ? 15 : 8);
    setDifficulty('mixed');
    setCustomInstructions(
      scopeKind === 'course'
        ? DEFAULT_COURSE_INSTRUCTIONS
        : DEFAULT_LECTURE_INSTRUCTIONS,
    );
  }, [isOpen, scopeKind, targetLabel]);

  if (!isOpen) return null;

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    onSubmit({
      questionCount,
      difficulty,
      customInstructions: customInstructions.trim(),
    });
    onClose();
  }

  return (
    <div
      className="modal-backdrop"
      role="dialog"
      aria-modal="true"
      aria-labelledby="quiz-gen-title"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div className="modal-card quiz-gen-modal">
        <div className="modal-header">
          <div>
            <h2 id="quiz-gen-title" className="card-title">
              Generate {scopeKind === 'course' ? 'Course' : 'Lecture'} Quiz
            </h2>
            <p className="meta-text">{targetLabel}</p>
          </div>
          <button
            type="button"
            className="btn btn-ghost btn-icon-sm"
            onClick={onClose}
            aria-label="Close quiz generator"
          >
            <CloseIcon />
          </button>
        </div>

        <form className="settings-form" onSubmit={handleSubmit}>
          <div className="form-grid-2">
            <div className="form-field">
              <label className="field-label" htmlFor="quiz-gen-count">
                Number of Questions
              </label>
              <select
                id="quiz-gen-count"
                className="input"
                value={questionCount}
                onChange={(e) => setQuestionCount(Number(e.target.value))}
              >
                <option value={5}>5 questions (quick check)</option>
                <option value={8}>8 questions</option>
                <option value={10}>10 questions</option>
                <option value={15}>15 questions</option>
                <option value={20}>20 questions (full exam)</option>
              </select>
            </div>

            <div className="form-field">
              <label className="field-label" htmlFor="quiz-gen-difficulty">
                Difficulty
              </label>
              <select
                id="quiz-gen-difficulty"
                className="input"
                value={difficulty}
                onChange={(e) =>
                  setDifficulty(
                    e.target.value as QuizGenerateOptions['difficulty'],
                  )
                }
              >
                <option value="mixed">Mixed (easy → hard)</option>
                <option value="easy">Easy (fundamentals)</option>
                <option value="medium">Medium (exam level)</option>
                <option value="hard">Hard (tricky & applied)</option>
              </select>
            </div>
          </div>

          <div className="form-field">
            <label className="field-label" htmlFor="quiz-gen-prompt">
              Content &amp; Focus Instructions{' '}
              <span className="meta-text">(editable)</span>
            </label>
            <textarea
              id="quiz-gen-prompt"
              className="input"
              rows={4}
              value={customInstructions}
              onChange={(e) => setCustomInstructions(e.target.value)}
              placeholder="e.g. Focus on algorithms and complexity, skip introductory history, write questions in Hungarian…"
              maxLength={2000}
            />
          </div>

          <p className="meta-text quiz-gen-locked-note">
            Structured multiple-choice JSON schema, answer explanations, and
            source reading tools are locked automatically so the quiz is always
            playable.
          </p>

          <div className="form-footer">
            <button
              type="button"
              className="btn btn-ghost"
              onClick={onClose}
            >
              Cancel
            </button>
            <button type="submit" className="btn btn-primary">
              Generate Quiz
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

