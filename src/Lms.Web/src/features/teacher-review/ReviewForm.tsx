import { validAnnotations, type AnnotatedAttachment } from '../../components/annotations/model';
import { useEffect, useRef, useState } from 'react';
import { ApiError, type ApiClient } from '../../api/client';
import type { ProblemWorkDetails } from '../../api/contracts/problemWork';
import { AsyncButton, Button, InlineError } from '../../components/primitives';
import { reviewSubmission } from './query';
function errorMessage(error: ApiError) {
  if (error.code === 'FeedbackRequired') return 'Укажите, что ученику нужно исправить.';
  if (error.code === 'ValidationFailed') return 'Проверьте комментарий. Сервер отклонил данные проверки.';
  if (error.code === 'ConcurrencyConflict') return 'Работа уже изменилась или была проверена другим преподавателем. Обновите работу.';
  if (error.status === 409) return 'Эту попытку уже нельзя проверить. Обновите работу.';
  if (error.status === 401) return 'Войдите снова, затем обновите работу.';
  if (error.status === 403) return 'Нет права проверять эту работу. Обновите работу.';
  if (error.status === 404) return 'Работа не найдена. Обновите работу.';
  return 'Не удалось подтвердить проверку. Решение могло сохраниться. Обновите работу перед следующим действием.';
}
export function ReviewForm({ data, client, feedback, changeFeedback, refresh, completed, annotations = [], busy }: {
  data: ProblemWorkDetails; client: ApiClient; feedback: string; changeFeedback: (value: string) => void;
  refresh: () => void; completed: () => void; annotations?: AnnotatedAttachment[]; busy?: (value: boolean) => void;
}) {
  const [pending, setPending] = useState<'accept' | 'needs-revision' | null>(null);
  const [failure, setFailure] = useState<{ text: string; blocked: boolean; field?: boolean } | null>(null);
  const locked = useRef(false), active = useRef(true);
  const commands = useRef<Record<'accept' | 'needs-revision', string>>({
    accept: crypto.randomUUID(), 'needs-revision': crypto.randomUUID(),
  });
  const errorRef = useRef<HTMLDivElement>(null), fieldRef = useRef<HTMLTextAreaElement>(null);
  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  useEffect(() => { if (failure?.field) fieldRef.current?.focus(); else if (failure) errorRef.current?.focus(); }, [failure]);
  async function decide(decision: 'accept' | 'needs-revision') {
    if (locked.current || data.status !== 'Submitted' || !data.currentSubmission ||
      !(decision === 'accept' ? data.permissions.canAccept : data.permissions.canRequestRevision)) return;
    if (decision === 'needs-revision' && !feedback.trim()) {
      setFailure({ text: 'Укажите, что ученику нужно исправить.', blocked: false, field: true }); return;
    }
    if (!validAnnotations(annotations) || new TextEncoder().encode(JSON.stringify(annotations)).length > 4000000) { setFailure({ text: 'Пометки превышают допустимый размер или содержат ошибку. Удалите лишние штрихи.', blocked: false }); return; }
    busy?.(true);
    locked.current = true; setPending(decision); setFailure(null);
    try {
      await reviewSubmission(client, data.id, decision, { commandId: commands.current[decision],
        submissionId: data.currentSubmission.id, expectedVersion: data.version,
        feedback: feedback.trim() ? feedback : null, ...(annotations.length ? { annotations } : {}) });
      if (active.current) completed();
    } catch (error) {
      if (!active.current) return;
      const failure = error instanceof ApiError ? error : new ApiError(0, 'NetworkError');
      const recoverable = [400, 422].includes(failure.status) && ['FeedbackRequired', 'ValidationFailed', 'InvalidAnnotations'].includes(failure.code);
      locked.current = !recoverable;
      setFailure({ text: errorMessage(failure), blocked: !recoverable, field: recoverable });
    } finally { busy?.(false); if (active.current) setPending(null); }
  }
  return <form className="solution-form review-form" aria-label="Проверка решения" onSubmit={event => event.preventDefault()}>
    <label htmlFor="review-feedback">Комментарий преподавателя</label>
    <textarea ref={fieldRef} id="review-feedback" rows={6} value={feedback} disabled={!!pending}
      aria-invalid={failure?.field || undefined} aria-describedby={`review-feedback-help${failure?.field ? ' review-error' : ''}`}
      onChange={event => { changeFeedback(event.target.value); if (failure?.field) setFailure(null); }} />
    <p className="small muted" id="review-feedback-help">Для доработки укажите, что нужно исправить. При принятии комментарий необязателен.</p>
    {failure && <div ref={errorRef} tabIndex={-1}><InlineError id="review-error">{failure.text}</InlineError>
      {failure.blocked && <Button variant="secondary" onClick={refresh}>Обновить работу</Button>}</div>}
    <div className="review-actions">
      {data.permissions.canRequestRevision && <AsyncButton variant="secondary" pending={pending === 'needs-revision'} pendingLabel="Сохранение…"
        disabled={!!pending || !!failure?.blocked} onClick={() => void decide('needs-revision')}>Вернуть на доработку</AsyncButton>}
      {data.permissions.canAccept && <AsyncButton pending={pending === 'accept'} pendingLabel="Сохранение…"
        disabled={!!pending || !!failure?.blocked} onClick={() => void decide('accept')}>Принять решение</AsyncButton>}
    </div>
    <span role="status">{pending ? 'Сохранение проверки. Дождитесь ответа сервера.' : ''}</span>
  </form>;
}
