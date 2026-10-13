import { useEffect, useState } from 'react';
import { ApiError } from '../../api/client';
import type { ApiClient } from '../../api/client';
import { decodeReviewQueue } from '../../api/contracts/reviewQueue';
import type { ReviewQueue } from '../../api/contracts/reviewQueue';
export const uuidPattern = '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}';
/** Normalize only documented query fields, including the backend's SQL offset bound. */
export function normalizeQueueQuery(source: URLSearchParams) {
  const result = new URLSearchParams();
  for (const key of ['courseId', 'groupId', 'search']) {
    const value = source.get(key)?.trim();
    if (value) result.set(key, key === 'search' ? value.slice(0, 200) : value);
  }
  const student = source.get('studentId');
  if (student && new RegExp(`^${uuidPattern}$`).test(student)) result.set('studentId', student);
  const kind = source.get('attemptKind');
  if (kind === 'first' || kind === 'revision') result.set('attemptKind', kind);
  const integer = (key: string, fallback: number, max: number) => {
    const raw = source.get(key) ?? '';
    const value = /^\d+$/.test(raw) ? Number(raw) : NaN;
    return Number.isSafeInteger(value) && value >= 1 && value <= max ? value : fallback;
  };
  const size = integer('pageSize', 50, 200);
  const page = integer('page', 1, Math.min(2147483647, Math.floor(2147483647 / size) + 1));
  result.set('page', String(page)); result.set('pageSize', String(size));
  return result;
}
type State = { phase: 'loading' } | { phase: 'success'; data: ReviewQueue } | { phase: 'error'; error: ApiError };
export function useReviewQueue(client: ApiClient, query: string) {
  const [attempt, setAttempt] = useState(0);
  const identity = `${query}:${attempt}`;
  const [result, setResult] = useState<{ identity: string; state: State }>({ identity, state: { phase: 'loading' } });
  useEffect(() => {
    const controller = new AbortController();
    setResult({ identity, state: { phase: 'loading' } });
    void client.request(`/teacher/review-queue?${query}`, value => {
      const data = decodeReviewQueue(value);
      const params = new URLSearchParams(query);
      if (data.page !== Number(params.get('page')) || data.pageSize !== Number(params.get('pageSize')))
        throw new Error('Queue page mismatch');
      return data;
    }, { signal: controller.signal }).then(data => {
      if (!controller.signal.aborted) setResult({ identity, state: { phase: 'success', data } });
    }).catch((error: unknown) => {
      if (!controller.signal.aborted) setResult({ identity, state: { phase: 'error',
        error: error instanceof ApiError ? error : new ApiError(0, 'RequestFailed') } });
    });
    return () => controller.abort();
  }, [client, query, identity]);
  return { state: result.identity === identity ? result.state : { phase: 'loading' } as State,
    retry: () => setAttempt(value => value + 1) };
}
