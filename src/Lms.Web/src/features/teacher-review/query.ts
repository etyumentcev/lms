import { useEffect, useState } from 'react';
import { ApiError, type ApiClient } from '../../api/client';
import { decodeProblemWork, type ProblemWorkDetails, type ReviewRequest } from '../../api/contracts/problemWork';
export function getProblemWork(client: ApiClient, id: string, signal: AbortSignal) {
  return client.request(`/teacher/problem-works/${encodeURIComponent(id)}`, value => {
    const data = decodeProblemWork(value);
    if (data.id !== id) throw Error('ProblemWork identity mismatch');
    return data;
  }, { signal });
}
export function reviewSubmission(client: ApiClient, id: string, decision: 'accept' | 'needs-revision', body: ReviewRequest) {
  return client.request(`/problem-works/${encodeURIComponent(id)}/reviews/${decision}`, () => undefined,
    { method: 'POST', body, csrf: true });
}
type State = { phase: 'loading' } | { phase: 'error'; error: ApiError } | { phase: 'success'; data: ProblemWorkDetails };
export function useProblemWork(client: ApiClient, id: string) {
  const [state, setState] = useState<State>({ phase: 'loading' });
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    setState({ phase: 'loading' });
    void getProblemWork(client, id, controller.signal).then(data => {
      if (!controller.signal.aborted) setState({ phase: 'success', data });
    }).catch((error: unknown) => {
      if (!controller.signal.aborted) setState({ phase: 'error', error: error instanceof ApiError ? error : new ApiError(0, 'RequestFailed') });
    });
    return () => controller.abort();
  }, [client, id, revision]);
  return { state, refresh: () => { setState({ phase: 'loading' }); setRevision(value => value + 1); } };
}
