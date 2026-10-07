import { useEffect, useState } from 'react';
import { ApiError } from '../../api/client';
import type { ApiClient } from '../../api/client';
import { decodeLessonWorkspace } from '../../api/contracts/lessonWorkspace';
import type { LessonWorkspace } from '../../api/contracts/lessonWorkspace';

export function getLessonWorkspace(client: ApiClient, courseId: string, lessonId: string, signal: AbortSignal) {
  return client.request(`/courses/${encodeURIComponent(courseId)}/lessons/${encodeURIComponent(lessonId)}/workspace`, value => {
    const result = decodeLessonWorkspace(value);
    if (result.course.id !== courseId || result.lesson.id !== lessonId) throw new Error('Lesson identity mismatch');
    return result;
  }, { signal });
}
type QueryState = { phase: 'loading' } | { phase: 'success'; data: LessonWorkspace } | { phase: 'error'; error: ApiError };
export function useLessonWorkspace(client: ApiClient, courseId: string, lessonId: string) {
  const identity = JSON.stringify([courseId, lessonId]);
  const [attempt, setAttempt] = useState(0);
  const [result, setResult] = useState<{ identity: string; state: QueryState }>({ identity, state: { phase: 'loading' } });
  useEffect(() => {
    const controller = new AbortController();
    setResult({ identity, state: { phase: 'loading' } });
    void getLessonWorkspace(client, courseId, lessonId, controller.signal).then(data => {
      if (!controller.signal.aborted) setResult({ identity, state: { phase: 'success', data } });
    }).catch((error: unknown) => {
      if (!controller.signal.aborted) setResult({ identity, state: { phase: 'error',
        error: error instanceof ApiError ? error : new ApiError(0, 'RequestFailed') } });
    });
    return () => controller.abort();
  }, [client, courseId, lessonId, identity, attempt]);
  return { state: result.identity === identity ? result.state : { phase: 'loading' } as QueryState,
    retry: () => { setResult({ identity, state: { phase: 'loading' } }); setAttempt(value => value + 1); } };
}
