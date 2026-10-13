import { useCallback, useRef, useState } from 'react';
import { ApiError, type ApiClient } from '../../api/client';
import { AttachmentUploader } from '../../components/attachments/AttachmentUploader';
import type { AttachmentApi, UploadedAttachment } from '../../components/attachments/api';
import { AsyncButton, Button, InlineError } from '../../components/primitives';

function failureText(error: ApiError) {
  if (error.status === 401) return 'Сеанс истёк. Войдите снова и обновите работу.';
  if (error.status === 403) return 'Полномочия изменились. Обновите работу.';
  if (error.status === 409 || error.status === 0 || error.status === 503 ||
    error.code === 'OperationOutcomeUnknown' || error.code === 'OperationAlreadyAttempted')
    return 'Результат отправки неизвестен. Не отправляйте сообщение повторно с новым идентификатором; обновите работу и проверьте историю.';
  return 'Не удалось подтвердить отправку сообщения. Обновите работу перед следующим действием.';
}

/** One mounted draft is one durable teacher intent; its identifiers never change after a request starts. */
export function TeacherMessageForm({ client, api, workId, refresh, completed }: {
  client: ApiClient; api: AttachmentApi; workId: string; refresh: () => void; completed: () => void;
}) {
  const identity = useRef({ commandId: crypto.randomUUID(), intentId: crypto.randomUUID() });
  const [body, setBody] = useState('');
  const [attachments, setAttachments] = useState<UploadedAttachment[]>([]);
  const [ready, setReady] = useState(true);
  const [pending, setPending] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);
  const [blocked, setBlocked] = useState(false);
  const changeAttachments = useCallback((items: UploadedAttachment[], nextReady: boolean) => {
    setAttachments(items); setReady(nextReady);
  }, []);
  async function send() {
    if (pending || blocked) return;
    if (!body.trim()) { setFailure('Введите сообщение преподавателя.'); return; }
    if (!ready) { setFailure('Дождитесь завершения загрузки фотографий.'); return; }
    setPending(true); setFailure(null);
    try {
      await client.request(`/problem-works/${encodeURIComponent(workId)}/messages`, () => undefined, {
        method: 'POST', csrf: true, body: { ...identity.current, body: body.trim(), replyTo: null,
          attachments: attachments.map(item => item.attachmentId) },
      });
      completed();
    } catch (error) {
      const apiError = error instanceof ApiError ? error : new ApiError(0, 'NetworkError');
      setBlocked(true); setFailure(failureText(apiError));
    } finally { setPending(false); }
  }
  return <section className="solution-form teacher-message-form" aria-label="Сообщение ученику">
    <h2>Сообщение ученику</h2>
    <fieldset disabled={pending || blocked}>
      <label htmlFor="teacher-message">Текст сообщения</label>
      <textarea id="teacher-message" rows={4} value={body}
        onChange={event => { setBody(event.target.value); if (!blocked) setFailure(null); }} />
      <AttachmentUploader api={api} subject="замечания" blockUnknownOutcome onChange={changeAttachments} />
      <AsyncButton pending={pending} pendingLabel="Отправка…" disabled={!ready}
        onClick={() => void send()}>Отправить сообщение</AsyncButton>
    </fieldset>
    {failure && <><InlineError>{failure}</InlineError>
      {blocked && <Button variant="secondary" onClick={refresh}>Обновить работу</Button>}</>}
    <span role="status">{pending ? 'Отправка сообщения. Дождитесь ответа сервера.' : ''}</span>
  </section>;
}
