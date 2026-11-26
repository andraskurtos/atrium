import { apiFetch } from './authClient';
import type {
  DailyCheckItem,
  DailyNote,
  DailyTrackerData,
} from '../types/atrium';

export function shiftDateKey(dateKey: string, deltaDays: number): string {
  const [y, m, d] = dateKey.split('-').map(Number);
  const shifted = new Date(y, m - 1, d + deltaDays);
  const yyyy = shifted.getFullYear();
  const mm = String(shifted.getMonth() + 1).padStart(2, '0');
  const dd = String(shifted.getDate()).padStart(2, '0');
  return `${yyyy}-${mm}-${dd}`;
}

export function isValidDateKey(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const [y, m, d] = value.split('-').map(Number);
  const check = new Date(y, m - 1, d);
  return (
    check.getFullYear() === y &&
    check.getMonth() === m - 1 &&
    check.getDate() === d
  );
}

export async function getDayTracker(dateKey: string): Promise<DailyTrackerData> {
  return apiFetch<DailyTrackerData>(`/api/days/${encodeURIComponent(dateKey)}`);
}

export async function createDailyCheckItem(
  dateKey: string,
  input: { title: string; dueTime?: string | null },
): Promise<DailyCheckItem> {
  return apiFetch<DailyCheckItem>(
    `/api/days/${encodeURIComponent(dateKey)}/checklist`,
    {
      method: 'POST',
      body: JSON.stringify({
        title: input.title,
        dueTime: input.dueTime ?? null,
      }),
    },
  );
}

export async function updateDailyCheckItem(
  id: string,
  input: {
    title?: string;
    isCompleted?: boolean;
    date?: string;
    dueTime?: string | null;
    clearDueTime?: boolean;
  },
): Promise<DailyCheckItem> {
  return apiFetch<DailyCheckItem>(
    `/api/days/checklist/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      body: JSON.stringify(input),
    },
  );
}

export async function deleteDailyCheckItem(id: string): Promise<void> {
  await apiFetch<void>(`/api/days/checklist/${encodeURIComponent(id)}`, {
    method: 'DELETE',
  });
}

export async function upsertDailyNote(
  dateKey: string,
  content: string,
): Promise<{ date: string; note: DailyNote | null }> {
  return apiFetch<{ date: string; note: DailyNote | null }>(
    `/api/days/${encodeURIComponent(dateKey)}/note`,
    {
      method: 'PUT',
      body: JSON.stringify({ content }),
    },
  );
}
