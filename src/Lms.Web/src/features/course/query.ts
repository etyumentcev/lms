import { useEffect, useState } from 'react';
import { ApiError } from '../../api/client';
import type { ApiClient } from '../../api/client';
import { decodeCourseWorkspace } from '../../api/contracts/courseWorkspace';
import type { CourseWorkspace } from '../../api/contracts/courseWorkspace';

export function getCourseWorkspace(client: ApiClient, courseId: string, signal: AbortSignal) {
  return client.request(`/courses/${encodeURIComponent(courseId)}/workspace`, value => {
    const result = decodeCourseWorkspace(value);
    if (result.course.id !== courseId) throw new Error('Course identity mismatch');
    return result;
  }, { signal });
}
type QueryState = { phase: 'loading' } | { phase: 'success'; data: CourseWorkspace } | { phase: 'error'; error: ApiError };
export function useCourseWorkspace(client: ApiClient, courseId: string) {
  const [attempt, setAttempt] = useState(0);
  const [result, setResult] = useState<{ courseId: string; state: QueryState }>({ courseId, state: { phase: 'loading' } });
  useEffect(() => {
    const controller = new AbortController();
    setResult({ courseId, state: { phase: 'loading' } });
    void getCourseWorkspace(client, courseId, controller.signal).then(data => {
      if (!controller.signal.aborted) setResult({ courseId, state: { phase: 'success', data } });
    }).catch((error: unknown) => {
      if (!controller.signal.aborted) setResult({ courseId, state: { phase: 'error',
        error: error instanceof ApiError ? error : new ApiError(0, 'RequestFailed') } });
    });
    return () => controller.abort();
  }, [client, courseId, attempt]);
  return { state: result.courseId === courseId ? result.state : { phase: 'loading' } as QueryState,
    retry: () => { setResult({ courseId, state: { phase: 'loading' } }); setAttempt(value => value + 1); } };
}
