import { apiFetch } from './authClient';
import type {
  UniversityQuizDetail,
  UniversityQuizSummary,
} from '../types/atrium';

export async function listCourseQuizzes(
  courseId: string,
): Promise<UniversityQuizSummary[]> {
  return apiFetch<UniversityQuizSummary[]>(
    `/api/university/courses/${courseId}/quizzes`,
    { method: 'GET' },
  );
}

export async function getUniversityQuiz(
  quizId: string,
): Promise<UniversityQuizDetail> {
  return apiFetch<UniversityQuizDetail>(`/api/university/quizzes/${quizId}`, {
    method: 'GET',
  });
}

export async function recordQuizAttempt(
  quizId: string,
  score: number,
): Promise<UniversityQuizSummary> {
  return apiFetch<UniversityQuizSummary>(
    `/api/university/quizzes/${quizId}/attempt`,
    {
      method: 'POST',
      body: JSON.stringify({ score }),
    },
  );
}

export async function deleteUniversityQuiz(quizId: string): Promise<void> {
  await apiFetch<void>(`/api/university/quizzes/${quizId}`, {
    method: 'DELETE',
  });
}

