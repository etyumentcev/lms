import { useEffect, useRef, useState } from 'react';
import { ApiError, type ApiClient } from '../../api/client';
import { decodeProblemWorkspace, type ProblemWorkspace, type SubmitSolutionRequest } from '../../api/contracts/problemWorkspace';
export function getProblemWorkspace(client: ApiClient, assignmentId: string, signal: AbortSignal) {
  return client.request(`/problem-assignments/${encodeURIComponent(assignmentId)}/workspace`, value => {
    const data = decodeProblemWorkspace(value);
    if (data.assignment.id !== assignmentId) throw Error('Assignment identity mismatch');
    return data;
  }, { signal });
}
// A fulfilled HTTP command is acknowledged even if its optional response body is incomplete.
// The workspace is always refetched; there is no retry or invented local submission DTO.
export function submitSolution(client: ApiClient, assignmentId: string, body: SubmitSolutionRequest) {
  return client.request(`/problem-assignments/${encodeURIComponent(assignmentId)}/submissions`, () => undefined, { method: 'POST', body });
}
type State = { phase: 'loading' } | { phase: 'error'; error: ApiError } | { phase: 'success'; data: ProblemWorkspace; refreshing?: boolean; historyError?: ApiError };
export function useProblemWorkspace(client: ApiClient, assignmentId: string, historyOpen = false) {
  const previouslyOpen = useRef(historyOpen);
  const [revision, setRevision] = useState(0);
  const [state, setState] = useState<State>({ phase: 'loading' });
  useEffect(() => {
    if (historyOpen && !previouslyOpen.current) {
      setState(current => current.phase === 'success' ? { phase: 'success', data: current.data, refreshing: true } : current);
      setRevision(x => x + 1);
    }
    previouslyOpen.current = historyOpen;
  }, [historyOpen]);
  useEffect(() => {
    const controller = new AbortController();
    setState(current => current.phase === 'success' && current.data.assignment.id === assignmentId
      ? { phase: 'success', data: current.data, refreshing: true } : { phase: 'loading' });
    void getProblemWorkspace(client, assignmentId, controller.signal).then(data => {
      if (!controller.signal.aborted) setState({ phase: 'success', data });
    }).catch((error: unknown) => {
      if (!controller.signal.aborted) {
        const failure = error instanceof ApiError ? error : new ApiError(0, 'RequestFailed');
        setState(current => current.phase === 'success'
          ? { phase: 'success', data: current.data, historyError: failure } : { phase: 'error', error: failure });
      }
    });
    return () => controller.abort();
  }, [client, assignmentId, revision]);
  return { state, retryHistory: () => setRevision(x => x + 1), refresh: () => { setState({ phase: 'loading' }); setRevision(x => x + 1); } };
}
