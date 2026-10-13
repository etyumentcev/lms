import { AnnotationPhoto } from '../../components/annotations/AnnotationPhoto';
import { newPage, type AnnotatedAttachment } from '../../components/annotations/model';
import { useState } from 'react';
import type { ProblemWorkspace } from '../../api/contracts/problemWorkspace';
import type { Message, Review, Submission } from '../../api/contracts/lessonWorkspace';
import type { AttachmentApi } from '../../components/attachments/api';
import { AttachmentPreview } from '../../components/attachments/AttachmentPreview';
import { MarkdownContent } from '../../components/MarkdownContent';
import { EmptyState } from '../../components/primitives';
import { StatusBadge } from '../../components/StatusBadge';

function DateLabel({ value }: { value: string }) {
  return <time dateTime={value}>{new Intl.DateTimeFormat('ru', { dateStyle: 'long', timeStyle: 'short' }).format(new Date(value))}</time>;
}
function ReadPhoto({ id, api, page, annotations }: { id: string; api: AttachmentApi; page: number; annotations: AnnotatedAttachment[] }) {
  const [state, setState] = useState(() => newPage(annotations.find(a => a.attachmentId === id)?.strokes));
  return <AnnotationPhoto api={api} id={id} page={page} state={state} change={setState} />;
}
function Pages({ ids, api, annotations }: { ids: readonly string[]; api: AttachmentApi; annotations?: AnnotatedAttachment[] }) {
  if (!ids.length) return null;
  return <ol className="attachment-pages" aria-label="Фотографии в исходном порядке">{ids.map((id, index) => <li key={id}>
    <h4>Страница {index + 1}</h4>{annotations ? <ReadPhoto id={id} api={api} page={index + 1} annotations={annotations} /> : <AttachmentPreview api={api} id={id} page={index + 1} />}
  </li>)}</ol>;
}
function ReviewResult({ review, feedback, api }: { review: Review; feedback?: Message; api: AttachmentApi }) {
  const teacher = feedback?.author.id === review.teacherId ? feedback.author.displayName : null;
  return <section className={`history-review history-review-${review.decision}`} aria-label="Проверка попытки">
    <h3>Проверка: <StatusBadge status={review.decision} /></h3>
    <p className="small muted">Преподаватель: {teacher || review.teacherId} · <DateLabel value={review.createdAt} /></p>
    {feedback && <><div className="history-text"><MarkdownContent>{feedback.bodyMarkdown}</MarkdownContent></div>
      <Pages ids={feedback.attachmentIds} api={api} /></>}
  </section>;
}
type Entry = { kind: 'attempt'; submission: Submission } | { kind: 'message'; message: Message };
/** Presentation only: retain server attempt order, reverse server message chronology for newest-first.
 * Feedback is joined only by IDs. Timestamp placement never associates discussion with an attempt.
 * Equal cross-kind timestamps put the message first; same-kind ties retain server order (attempt/id).
 */
function entries(history: ProblemWorkspace['history']): Entry[] {
  const feedbackIds = new Set(history.reviews.map(review => review.messageId));
  const discussion = history.messages.filter(message => !feedbackIds.has(message.id)).reverse();
  const result: Entry[] = [];
  let index = 0;
  for (const submission of history.submissions) {
    while (index < discussion.length && Date.parse(discussion[index]!.createdAt) >= Date.parse(submission.submittedAt)) {
      result.push({ kind: 'message', message: discussion[index++]! });
    }
    result.push({ kind: 'attempt', submission });
  }
  for (; index < discussion.length; index++) result.push({ kind: 'message', message: discussion[index]! });
  return result;
}
export function HistoryTimeline({ data, api }: { data: ProblemWorkspace; api: AttachmentApi }) {
  if (data.work.problemWorkId === null) return <EmptyState title="Вы ещё не отправляли решение" />;
  const timeline = entries(data.history);
  if (!timeline.length) return <EmptyState title="История пока пуста" />;
  const reviews = new Map(data.history.reviews.map(review => [review.submissionId, review]));
  const messages = new Map(data.history.messages.map(message => [message.id, message]));
  return <section aria-label="История работы" className="history">
    <h2>История работы</h2><p className="small muted">Новые попытки сверху. Проверка показана вместе с отправленным решением.</p>
    <details className="history-content"><summary>Условие этой работы</summary>
      <p className="small muted">Версия курса: {data.problem.courseVersionId}</p>
      <MarkdownContent>{data.problem.statementMarkdown}</MarkdownContent>
    </details>
    <ol className="history-timeline" aria-label="Попытки и сообщения">{timeline.map(entry => {
      if (entry.kind === 'message') {
        const message = entry.message;
        return <li key={`message-${message.id}`} className="history-message"><article aria-labelledby={`message-${message.id}`}>
          <h2 id={`message-${message.id}`}>Сообщение · {message.authorRole === 'Teacher' ? 'Преподаватель' : 'Ученик'}</h2>
          <p className="small muted">{message.author.displayName || message.author.id} · <DateLabel value={message.createdAt} /></p>
          <div className="history-text"><MarkdownContent>{message.bodyMarkdown}</MarkdownContent></div>
          <Pages ids={message.attachmentIds} api={api} />
        </article></li>;
      }
      const submission = entry.submission;
      const review = reviews.get(submission.id);
      return <li key={submission.id} className="history-attempt"><article aria-labelledby={`attempt-${submission.id}`}>
        <h2 id={`attempt-${submission.id}`}>Попытка №{submission.attemptNumber}</h2>
        <p className="history-submitted">Решение отправлено · <DateLabel value={submission.submittedAt} /></p>
        <Pages ids={submission.attachmentIds} api={api} annotations={review?.annotations} />
        {submission.bodyMarkdown && <div className="history-text"><MarkdownContent>{submission.bodyMarkdown}</MarkdownContent></div>}
        {review && <ReviewResult review={review} feedback={review.messageId ? messages.get(review.messageId) : undefined} api={api} />}
      </article></li>;
    })}</ol>
  </section>;
}
