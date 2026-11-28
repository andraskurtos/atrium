import { useEffect, useMemo, useState } from 'react';
import ReactMarkdown from 'react-markdown';
import rehypeKatex from 'rehype-katex';
import remarkGfm from 'remark-gfm';
import remarkMath from 'remark-math';
import { normalizeBlockMathFences } from '../lib/markdownUtils';
import type { QuizQuestion, UniversityQuizDetail } from '../types/atrium';
import { CloseIcon } from './Icons';

type QuizPlayMode = 'study' | 'exam';

interface QuizPlayerModalProps {
  quiz: UniversityQuizDetail | null;
  loading: boolean;
  onClose: () => void;
  onRecordAttempt: (quizId: string, score: number) => Promise<void>;
  onDelete: (quizId: string) => Promise<void>;
}

const OPTION_LETTERS = ['A', 'B', 'C', 'D', 'E', 'F'];

function QuizMarkdown({ content }: { content: string }) {
  return (
    <ReactMarkdown
      remarkPlugins={[remarkGfm, remarkMath]}
      rehypePlugins={[rehypeKatex]}
    >
      {normalizeBlockMathFences(content)}
    </ReactMarkdown>
  );
}

export function QuizPlayerModal({
  quiz,
  loading,
  onClose,
  onRecordAttempt,
  onDelete,
}: QuizPlayerModalProps) {
  const [mode, setMode] = useState<QuizPlayMode>('study');
  const [questionOrder, setQuestionOrder] = useState<number[]>([]);
  const [currentIdx, setCurrentIdx] = useState(0);
  const [answers, setAnswers] = useState<Record<number, number>>({});
  const [finished, setFinished] = useState(false);
  const [savingScore, setSavingScore] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);

  useEffect(() => {
    if (!quiz) return;
    setQuestionOrder(quiz.questions.map((_, i) => i));
    setCurrentIdx(0);
    setAnswers({});
    setFinished(false);
    setConfirmDelete(false);
  }, [quiz]);

  const orderedQuestions: Array<{ origIndex: number; q: QuizQuestion }> =
    useMemo(() => {
      if (!quiz) return [];
      return questionOrder
        .filter((i) => i >= 0 && i < quiz.questions.length)
        .map((i) => ({ origIndex: i, q: quiz.questions[i] }));
    }, [quiz, questionOrder]);

  if (!quiz && !loading) return null;

  const total = orderedQuestions.length;
  const currentEntry = orderedQuestions[currentIdx] ?? null;
  const answeredCount = Object.keys(answers).length;

  const score = orderedQuestions.reduce((acc, entry) => {
    return answers[entry.origIndex] === entry.q.correctIndex ? acc + 1 : acc;
  }, 0);

  function resetAttempt(shuffle: boolean) {
    if (!quiz) return;
    const indices = quiz.questions.map((_, i) => i);
    if (shuffle) {
      for (let i = indices.length - 1; i > 0; i -= 1) {
        const j = Math.floor(Math.random() * (i + 1));
        [indices[i], indices[j]] = [indices[j], indices[i]];
      }
    }
    setQuestionOrder(indices);
    setCurrentIdx(0);
    setAnswers({});
    setFinished(false);
  }

  function handleSelectOption(origIndex: number, optionIndex: number) {
    if (finished) return;
    if (mode === 'study' && answers[origIndex] !== undefined) return;
    setAnswers((prev) => ({ ...prev, [origIndex]: optionIndex }));
  }

  async function handleFinishQuiz() {
    if (!quiz || finished) return;
    setFinished(true);
    setSavingScore(true);
    try {
      await onRecordAttempt(quiz.id, score);
    } finally {
      setSavingScore(false);
    }
  }

  function handleSwitchMode(nextMode: QuizPlayMode) {
    if (nextMode === mode) return;
    setMode(nextMode);
    resetAttempt(false);
  }

  const currentSelected =
    currentEntry !== null ? answers[currentEntry.origIndex] : undefined;
  const showStudyFeedback =
    mode === 'study' && currentSelected !== undefined && currentEntry !== null;

  return (
    <div
      className="modal-backdrop"
      role="dialog"
      aria-modal="true"
      aria-labelledby="quiz-player-title"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div className="modal-card quiz-player-modal">
        {loading || !quiz ? (
          <div className="quiz-player-loading">
            <p className="meta-text">Loading quiz…</p>
          </div>
        ) : (
          <>
            <div className="modal-header quiz-player-header">
              <div>
                <h2 id="quiz-player-title" className="card-title">
                  {quiz.title}
                </h2>
                {quiz.description && (
                  <p className="meta-text">{quiz.description}</p>
                )}
              </div>
              <div className="avatar-actions">
                <div className="quiz-mode-switch" role="group" aria-label="Quiz mode">
                  <button
                    type="button"
                    className={`mode-chip ${mode === 'study' ? 'is-active' : ''}`}
                    onClick={() => handleSwitchMode('study')}
                  >
                    Study
                  </button>
                  <button
                    type="button"
                    className={`mode-chip ${mode === 'exam' ? 'is-active' : ''}`}
                    onClick={() => handleSwitchMode('exam')}
                  >
                    Exam
                  </button>
                </div>
                <button
                  type="button"
                  className="btn btn-ghost btn-icon-sm"
                  onClick={onClose}
                  aria-label="Close quiz"
                >
                  <CloseIcon />
                </button>
              </div>
            </div>

            {/* Progress bar */}
            <div className="quiz-progress-row">
              <div className="quiz-progress-track" aria-hidden="true">
                <div
                  className="quiz-progress-fill"
                  style={{
                    width: `${total > 0 ? Math.round(((finished ? total : answeredCount) / total) * 100) : 0}%`,
                  }}
                />
              </div>
              <span className="meta-text mono-num">
                {finished
                  ? `Score: ${score}/${total}`
                  : `${currentIdx + 1} / ${total}`}
              </span>
            </div>

            {finished ? (
              <div className="quiz-results-view">
                <div className="card-soft quiz-score-banner">
                  <div className="quiz-score-big mono-num">
                    {score} / {total}{' '}
                    <span className="meta-text">
                      ({total > 0 ? Math.round((score / total) * 100) : 0}%)
                    </span>
                  </div>
                  <div className="meta-text">
                    {savingScore
                      ? 'Saving attempt…'
                      : `Best score: ${Math.max(quiz.bestScore ?? 0, score)}/${total}`}
                  </div>
                  <div className="avatar-actions">
                    <button
                      type="button"
                      className="btn btn-primary btn-xs"
                      onClick={() => resetAttempt(false)}
                    >
                      Retake
                    </button>
                    <button
                      type="button"
                      className="btn btn-secondary btn-xs"
                      onClick={() => resetAttempt(true)}
                    >
                      Shuffle &amp; Retake
                    </button>
                  </div>
                </div>

                <div className="quiz-review-list">
                  {orderedQuestions.map((entry, idx) => {
                    const chosen = answers[entry.origIndex];
                    const isCorrect = chosen === entry.q.correctIndex;
                    return (
                      <div
                        key={entry.origIndex}
                        className={`card-soft quiz-review-item ${isCorrect ? 'is-correct' : 'is-wrong'}`}
                      >
                        <div className="quiz-review-q-header">
                          <span className="mono-num meta-text">Q{idx + 1}</span>
                          <span
                            className={`quiz-badge ${isCorrect ? 'is-correct' : 'is-wrong'}`}
                          >
                            {isCorrect ? 'Correct' : 'Incorrect'}
                          </span>
                          {entry.q.sourceReference && (
                            <span className="meta-text">
                              · {entry.q.sourceReference}
                            </span>
                          )}
                        </div>
                        <div className="quiz-question-prompt">
                          <QuizMarkdown content={entry.q.prompt} />
                        </div>
                        <ul className="quiz-review-options">
                          {entry.q.options.map((opt, optIdx) => {
                            const isRightOpt = optIdx === entry.q.correctIndex;
                            const isChosenOpt = optIdx === chosen;
                            return (
                              <li
                                key={optIdx}
                                className={`quiz-review-opt ${
                                  isRightOpt
                                    ? 'is-correct'
                                    : isChosenOpt
                                      ? 'is-wrong'
                                      : ''
                                }`}
                              >
                                <span className="quiz-opt-letter mono-num">
                                  {OPTION_LETTERS[optIdx] ?? optIdx + 1}
                                </span>
                                <span className="quiz-opt-body">
                                  <QuizMarkdown content={opt} />
                                </span>
                              </li>
                            );
                          })}
                        </ul>
                        <div className="quiz-explanation-box">
                          <strong>Explanation:</strong>{' '}
                          <QuizMarkdown content={entry.q.explanation} />
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>
            ) : currentEntry ? (
              <div className="quiz-question-stage">
                <div className="quiz-question-meta">
                  <span className="meta-text">
                    Question {currentIdx + 1} of {total}
                  </span>
                  {currentEntry.q.sourceReference && (
                    <span className="meta-text">
                      Source: {currentEntry.q.sourceReference}
                    </span>
                  )}
                </div>

                <div className="quiz-question-prompt">
                  <QuizMarkdown content={currentEntry.q.prompt} />
                </div>

                <div className="quiz-options-grid">
                  {currentEntry.q.options.map((opt, optIdx) => {
                    const isSelected = currentSelected === optIdx;
                    const isCorrectOpt = optIdx === currentEntry.q.correctIndex;
                    let stateClass = '';
                    if (showStudyFeedback) {
                      if (isCorrectOpt) stateClass = 'is-correct';
                      else if (isSelected) stateClass = 'is-wrong';
                    } else if (isSelected) {
                      stateClass = 'is-selected';
                    }

                    return (
                      <button
                        key={optIdx}
                        type="button"
                        className={`quiz-option-btn ${stateClass}`}
                        onClick={() =>
                          handleSelectOption(currentEntry.origIndex, optIdx)
                        }
                      >
                        <span className="quiz-opt-letter mono-num">
                          {OPTION_LETTERS[optIdx] ?? optIdx + 1}
                        </span>
                        <span className="quiz-opt-body">
                          <QuizMarkdown content={opt} />
                        </span>
                      </button>
                    );
                  })}
                </div>

                {showStudyFeedback && (
                  <div
                    className={`quiz-explanation-box ${
                      currentSelected === currentEntry.q.correctIndex
                        ? 'is-correct'
                        : 'is-wrong'
                    }`}
                  >
                    <strong>
                      {currentSelected === currentEntry.q.correctIndex
                        ? 'Correct! '
                        : `Incorrect — correct answer is ${OPTION_LETTERS[currentEntry.q.correctIndex] ?? currentEntry.q.correctIndex + 1}. `}
                    </strong>
                    <QuizMarkdown content={currentEntry.q.explanation} />
                  </div>
                )}

                <div className="quiz-nav-footer">
                  <div className="avatar-actions">
                    <button
                      type="button"
                      className="btn btn-ghost btn-xs"
                      disabled={currentIdx === 0}
                      onClick={() => setCurrentIdx((i) => Math.max(0, i - 1))}
                    >
                      ← Prev
                    </button>
                    {currentIdx < total - 1 ? (
                      <button
                        type="button"
                        className="btn btn-secondary btn-xs"
                        onClick={() =>
                          setCurrentIdx((i) => Math.min(total - 1, i + 1))
                        }
                      >
                        Next →
                      </button>
                    ) : (
                      <button
                        type="button"
                        className="btn btn-primary btn-xs"
                        disabled={mode === 'exam' && answeredCount < total}
                        onClick={() => void handleFinishQuiz()}
                      >
                        {mode === 'exam' ? 'Submit Exam' : 'Finish & See Score'}
                      </button>
                    )}
                    {mode === 'exam' &&
                      currentIdx < total - 1 &&
                      answeredCount === total && (
                        <button
                          type="button"
                          className="btn btn-primary btn-xs"
                          onClick={() => void handleFinishQuiz()}
                        >
                          Submit Exam
                        </button>
                      )}
                  </div>

                  <div>
                    {confirmDelete ? (
                      <div className="avatar-actions">
                        <button
                          type="button"
                          className="btn btn-danger btn-xs"
                          onClick={() => void onDelete(quiz.id)}
                        >
                          Confirm delete
                        </button>
                        <button
                          type="button"
                          className="btn btn-ghost btn-xs"
                          onClick={() => setConfirmDelete(false)}
                        >
                          Cancel
                        </button>
                      </div>
                    ) : (
                      <button
                        type="button"
                        className="btn btn-ghost btn-xs"
                        onClick={() => setConfirmDelete(true)}
                      >
                        Delete quiz
                      </button>
                    )}
                  </div>
                </div>
              </div>
            ) : null}
          </>
        )}
      </div>
    </div>
  );
}

