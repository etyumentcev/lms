import type { ProblemWorkDetails } from '../../api/contracts/problemWork';
import { ContextSection } from '../../layout/AppShell';
import { MarkdownContent } from '../../components/MarkdownContent';
import { StatusBadge } from '../../components/StatusBadge';
import { Button } from '../../components/primitives';
export function ReviewContext({ data, selectAttempt }: { data: ProblemWorkDetails; selectAttempt: (id: string) => void }) {
  const previous = data.submissions.filter(item => item.id !== data.currentSubmission?.id);
  const lastRevision = [...data.reviews].filter(review => review.decision === 'NeedsRevision' && review.submissionId !== data.currentSubmission?.id)
    .sort((a, b) => (data.submissions.find(s => s.id === b.submissionId)?.attemptNumber ?? 0) - (data.submissions.find(s => s.id === a.submissionId)?.attemptNumber ?? 0))[0];
  const lastFeedback = data.messages.find(message => message.id === lastRevision?.messageId);
  return <><ContextSection title="Информация о работе">
    <p>{data.student.displayName?.trim() || `Ученик ${data.student.id}`}</p>
    <p>{data.course.title || data.course.id}</p><p>{data.lesson.title || `Занятие ${data.lesson.order}`}</p>
    <StatusBadge status={data.status} /><p>Всего попыток: {data.attemptCount}</p>
  </ContextSection>
    {lastFeedback && <ContextSection title="Последнее замечание"><div id="last-revision-feedback" className="review-text">
      <MarkdownContent>{lastFeedback.bodyMarkdown}</MarkdownContent></div>
      <time dateTime={lastRevision!.createdAt}>{new Intl.DateTimeFormat('ru', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(lastRevision!.createdAt))}</time>
    </ContextSection>}
    {previous.length > 0 && <ContextSection title="Предыдущие попытки"><div className="review-history">{previous.map(attempt => {
      const review = data.reviews.find(item => item.submissionId === attempt.id);
      const feedback = data.messages.find(item => item.id === review?.messageId);
      return <details key={attempt.id}><summary>Попытка №{attempt.attemptNumber}{review ? review.decision === 'Accepted' ? ' · Принята' : ' · Нужна доработка' : ''}</summary>
        <p>Отправлена <time dateTime={attempt.submittedAt}>{new Intl.DateTimeFormat('ru', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(attempt.submittedAt))}</time></p>
        {review && <p>Проверена <time dateTime={review.createdAt}>{new Intl.DateTimeFormat('ru', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(review.createdAt))}</time></p>}
        {feedback && (feedback.id === lastFeedback?.id ? <a href="#last-revision-feedback">См. последнее замечание</a>
          : <div className="review-text"><MarkdownContent>{feedback.bodyMarkdown}</MarkdownContent></div>)}
        <Button variant="secondary" onClick={() => selectAttempt(attempt.id)}>Посмотреть попытку №{attempt.attemptNumber}</Button>
      </details>;
    })}</div></ContextSection>}
  </>;
}
