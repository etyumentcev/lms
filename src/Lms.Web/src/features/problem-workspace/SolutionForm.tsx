import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError, type ApiClient } from '../../api/client';
import type { ProblemWorkspace } from '../../api/contracts/problemWorkspace';
import type { AttachmentApi, UploadedAttachment } from '../../components/attachments/api';
import { AttachmentUploader } from '../../components/attachments/AttachmentUploader';
import { AsyncButton, Button, InlineError } from '../../components/primitives';
import { submitSolution } from './query';

function commandError(error: ApiError) {
  switch (error.code) {
    case 'EmptySubmission': return 'Добавьте фотографию или текст решения.';
    case 'ValidationFailed': return 'Проверьте фотографии и пояснение к решению.';
    case 'AttachmentNotOwned': return 'Нет доступа к одной из фотографий. Удалите её и загрузите заново.';
    case 'InvalidAttachment': return 'Одна из фотографий недоступна или повреждена. Загрузите её заново.';
    case 'AttachmentAlreadyAttached': return 'Одна из фотографий уже отправлена. Для новой попытки загрузите новые фото.';
    case 'ConcurrencyConflict': return 'Работа уже изменилась. Обновите состояние перед отправкой.';
    case 'WorkAlreadySubmitted': return 'Решение уже отправлено. Обновите состояние.';
    case 'WorkAlreadyAccepted': return 'Решение уже принято. Обновите состояние.';
  }
  if (error.status === 401) return 'Войдите снова и обновите состояние задачи.';
  if (error.status === 403) return 'Отправка недоступна вашей учётной записи. Обновите состояние.';
  if (error.status === 404) return 'Задача не найдена. Обновите состояние.';
  return 'Не удалось подтвердить отправку. Решение могло сохраниться. Обновите состояние, прежде чем отправлять снова.';
}
export function SolutionForm({ data, client, api, refresh }: { data: ProblemWorkspace; client: ApiClient; api: AttachmentApi; refresh: () => void }) {
  const [body, setBody] = useState('');
  const [uploads, setUploads] = useState<{ attachments: UploadedAttachment[]; ready: boolean }>({ attachments: [], ready: true });
  const [pending, setPending] = useState(false);
  const [failure, setFailure] = useState<{ message: string; refreshRequired: boolean } | null>(null);
  const locked = useRef(false); const active = useRef(true); const errorRef = useRef<HTMLDivElement>(null);
  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  useEffect(() => { if (failure) errorRef.current?.focus(); }, [failure]);
  const changed = useCallback((attachments: UploadedAttachment[], ready: boolean) => setUploads({ attachments, ready }), []);
  const valid = uploads.ready && (body.trim().length > 0 || uploads.attachments.length > 0);
  async function submit() {
    if (locked.current || !valid || !data.permissions.canSubmit || !data.availability.isAvailable) return;
    locked.current = true; setPending(true); setFailure(null);
    try {
      await submitSolution(client, data.assignment.id, { bodyMarkdown: body.trim() ? body : null,
        attachmentIds: uploads.attachments.map(a => a.attachmentId), expectedVersion: data.work.version });
      if (active.current) refresh();
    } catch (error) {
      if (!active.current) return;
      const apiError = error instanceof ApiError ? error : new ApiError(0, 'NetworkError');
      const recoverable = ['EmptySubmission', 'ValidationFailed', 'AttachmentNotOwned', 'InvalidAttachment', 'AttachmentAlreadyAttached'].includes(apiError.code);
      locked.current = !recoverable;
      setFailure({ message: commandError(apiError), refreshRequired: !recoverable });
    } finally { if (active.current) setPending(false); }
  }
  return <form className="solution-form" onSubmit={event => { event.preventDefault(); void submit(); }}>
    {data.work.status === 'NeedsRevision' && <h2>Новое решение</h2>}
    <fieldset disabled={pending || !!failure?.refreshRequired}>
      <legend className="sr-only">Подготовка решения</legend>
      <AttachmentUploader api={api} onChange={changed} />
      <label htmlFor="solution-body">Пояснение к решению — необязательно</label>
      <textarea id="solution-body" rows={5} value={body} onChange={event => setBody(event.target.value)} aria-describedby="solution-draft-note" />
      <p id="solution-draft-note" className="small muted">До отправки изменения хранятся только на этой странице. При уходе текст и список фото не сохранятся.</p>
    </fieldset>
    {!valid && <p className="small muted">{uploads.ready ? 'Добавьте фотографию или текст решения.' : 'Дождитесь загрузки всех страниц или удалите страницы с ошибкой.'}</p>}
    {failure && <div ref={errorRef} tabIndex={-1}><InlineError>{failure.message}</InlineError>
      {failure.refreshRequired && <Button variant="secondary" onClick={refresh}>Обновить состояние</Button>}</div>}
    <AsyncButton type="submit" pending={pending} pendingLabel="Отправка…" disabled={!valid || !!failure?.refreshRequired}>Отправить решение</AsyncButton>
    <span role="status" className="sr-only">{pending ? 'Отправка решения. Дождитесь ответа сервера.' : ''}</span>
  </form>;
}
