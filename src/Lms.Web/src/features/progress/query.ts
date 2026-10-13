import { useEffect, useState } from 'react';
import { ApiError } from '../../api/client';
import type { ApiClient } from '../../api/client';
import { decodeReviewHistory, decodeStudentProgress, decodeTeacherProgress } from '../../api/contracts/progressHistory';
import type { ReviewHistory, StudentProgress, TeacherProgress } from '../../api/contracts/progressHistory';
import type { NavigationRole } from '../../layout/SidebarNavigation';
import { uuidPattern } from '../review-queue/query';

export function normalizeProgressQuery(source: URLSearchParams, history = false) {
  const result = new URLSearchParams();
  for (const key of ['courseId', 'groupId']) {
    const value = source.get(key)?.trim();
    if (value) result.set(key, value);
  }
  const student = source.get('studentId');
  if (student && new RegExp(`^${uuidPattern}$`).test(student)) result.set('studentId', student);
  const allowed = history ? ['NeedsRevision', 'Accepted'] : ['NotStarted', 'Submitted', 'NeedsRevision', 'Accepted'];
  const filter = source.get(history ? 'decision' : 'status');
  if (filter && allowed.includes(filter)) result.set(history ? 'decision' : 'status', filter);
  const sizeRaw = source.get('pageSize') ?? '';
  const size = /^\d+$/.test(sizeRaw) && Number(sizeRaw) >= 1 && Number(sizeRaw) <= 200 ? Number(sizeRaw) : 50;
  const pageRaw = source.get('page') ?? '';
  const maxPage = Math.min(2147483647, Math.floor(2147483647 / size) + 1);
  const page = /^\d+$/.test(pageRaw) && Number(pageRaw) >= 1 && Number(pageRaw) <= maxPage ? Number(pageRaw) : 1;
  result.set('page', String(page)); result.set('pageSize', String(size));
  return result;
}

type ProgressData = TeacherProgress | StudentProgress;
type State<T> = { phase: 'loading' } | { phase: 'success'; data: T } | { phase: 'error'; error: ApiError };
function useRead<T>(client: ApiClient, path: string, decode: (value: unknown) => T) {
  const [retry, setRetry] = useState(0);
  const identity = `${path}:${retry}`;
  const [result, setResult] = useState<{ identity: string; state: State<T> }>({ identity, state: { phase: 'loading' } });
  useEffect(() => {
    const controller = new AbortController();
    setResult({ identity, state: { phase: 'loading' } });
    void client.request(path, decode, { signal: controller.signal }).then(data => {
      if (!controller.signal.aborted) setResult({ identity, state: { phase: 'success', data } });
    }).catch((error: unknown) => {
      if (!controller.signal.aborted) setResult({ identity, state: { phase: 'error',
        error: error instanceof ApiError ? error : new ApiError(0, 'RequestFailed') } });
    });
    return () => controller.abort();
  }, [client, identity, path, decode]);
  return { state: result.identity === identity ? result.state : { phase: 'loading' } as State<T>,
    retry: () => setRetry(value => value + 1) };
}
export function useProgress(client: ApiClient, role: NavigationRole, query: string) {
  return useRead<ProgressData>(client,
    role === 'Teacher' ? `/teacher/group-progress?${query}` : `/student/progress-history?${query}`,
    role === 'Teacher' ? decodeTeacherProgress : decodeStudentProgress);
}
export function useReviewHistory(client: ApiClient, query: string) {
  return useRead<ReviewHistory>(client, `/teacher/review-history?${query}`, decodeReviewHistory);
}
