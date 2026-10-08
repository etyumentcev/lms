import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import type { ApiClient } from '../../api/client';
import { AppShell, ContextSection } from '../../layout/AppShell';
import { Breadcrumbs, Button, EmptyState, ErrorState, LoadingSkeleton, PageHeader } from '../../components/primitives';
import { MarkdownContent } from '../../components/MarkdownContent';
import { StatusBadge } from '../../components/StatusBadge';
import { attachmentApi } from '../../components/attachments/api';
import { normalizeQueueQuery } from '../review-queue/query';
import { useProblemWork } from './query';
import { ReviewForm } from './ReviewForm';
import { ReviewContext } from './ReviewContext';
import { SubmissionViewer } from './SubmissionViewer';
import '../problem-workspace/problem.css';
import './review.css';
export function TeacherReviewPage({ problemWorkId, client }: { problemWorkId: string; client: ApiClient }) {
  const location = useLocation();
  const queueSearch = typeof location.state?.queueSearch === 'string' ? normalizeQueueQuery(new URLSearchParams(location.state.queueSearch)).toString() : '';
  const queueUrl = `/teacher/reviews${queueSearch ? `?${queueSearch}` : ''}`;
  const { state, refresh } = useProblemWork(client, problemWorkId);
  const [feedback, setFeedback] = useState('');
  const [saved, setSaved] = useState(false);
  const [selected, setSelected] = useState<string | null>(null);
  const focusAfterRefresh = useRef(false), focusViewer = useRef(false);
  const viewerRef = useRef<HTMLElement>(null);
  const api = useMemo(() => attachmentApi(client), [client]);
  const data = state.phase === 'success' ? state.data : null;
  const current = data?.currentSubmission;
  const submission = selected ? data?.submissions.find(item => item.id === selected) ?? current : current;
  const archived = !!submission && submission.id !== current?.id;
  const review = data?.reviews.find(item => item.submissionId === submission?.id);
  const reviewedFeedback = data?.messages.find(item => item.id === review?.messageId);
  const title = data ? data.problem.title?.trim() || `Задача ${data.problem.order}` : 'Проверка решения';
  const reload = () => { focusAfterRefresh.current = true; setSelected(null); refresh(); };
  function selectAttempt(id: string | null) { focusViewer.current = true; setSelected(id); }
  useEffect(() => { document.title = `${title} · Sapientum`; }, [title]);
  useEffect(() => {
    if (state.phase !== 'loading' && focusAfterRefresh.current) { document.getElementById('main-content')?.focus(); focusAfterRefresh.current = false; }
  }, [state.phase]);
  useEffect(() => { if (focusViewer.current) { viewerRef.current?.focus(); focusViewer.current = false; } }, [selected]);
  return <AppShell role={data && (data.permissions.canAccept || data.permissions.canRequestRevision) ? 'Teacher' : null} preview={false}
    context={state.phase === 'loading' ? <ContextSection title="Информация о работе"><LoadingSkeleton label="Загрузка контекста проверки" /></ContextSection>
      : data ? <ReviewContext data={data} selectAttempt={selectAttempt} /> : null}>
    <Breadcrumbs items={[{ label: 'Проверка работ', to: queueUrl }, { label: title }]} />
    <div className="workspace-content teacher-review" aria-busy={state.phase === 'loading'}>
      <PageHeader title={title} meta={data ? <StatusBadge status={data.status} /> : undefined} />
      <Link className="text-link review-queue-link" to={queueUrl}>К очереди проверок</Link>
      {saved && <p role="status">{state.phase === 'success' ? 'Проверка сохранена. Состояние работы обновлено.' : 'Проверка сохранена. Ожидается обновление состояния работы.'}</p>}
      {state.phase === 'loading' && <><LoadingSkeleton label="Загрузка условия задачи" rows={3} /><LoadingSkeleton label="Загрузка решения" rows={6} /></>}
      {state.phase === 'error' && <ErrorState title={state.error.status === 401 ? 'Войдите, чтобы открыть работу' : state.error.status === 403 ? 'Нет доступа к работе'
        : state.error.status === 404 ? 'Работа не найдена' : 'Не удалось загрузить работу'}
        description={state.error.code === 'ContentUnavailable' ? 'Историческое условие временно недоступно. Повторите загрузку.'
          : 'Проверьте доступ и соединение. Повтор загрузки не отправляет решение проверки повторно.'}
        onRetry={[401, 403, 404].includes(state.error.status) ? undefined : reload} />}
      {data && <>
        <section className="review-statement" aria-label="Условие задачи"><h2>Условие задачи</h2><MarkdownContent>{data.problem.statementMarkdown}</MarkdownContent></section>
        <section ref={viewerRef} tabIndex={-1} aria-label="Отправленное решение" className="review-work-area">
          {archived && <div className="review-archive-note"><p>Просмотр предыдущей попытки. Проверка относится к текущей попытке №{current?.attemptNumber}.</p>
            <Button onClick={() => selectAttempt(null)}>К текущей попытке</Button></div>}
          {submission ? <SubmissionViewer key={submission.id} submission={submission} api={api} /> : <EmptyState title="Отправленных попыток нет" />}
          {review && !archived && <section className="review-outcome" aria-label="Результат проверки"><h2>Результат проверки</h2><StatusBadge status={review.decision} />
            <p><time dateTime={review.createdAt}>{new Intl.DateTimeFormat('ru', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(review.createdAt))}</time></p>
            {reviewedFeedback && <div className="review-text"><MarkdownContent>{reviewedFeedback.bodyMarkdown}</MarkdownContent></div>}
          </section>}
        </section>
        {current && data.status === 'Submitted' && (data.permissions.canAccept || data.permissions.canRequestRevision)
          ? <div hidden={archived}><ReviewForm data={data} client={client} feedback={feedback} changeFeedback={setFeedback} refresh={reload} completed={() => { setSaved(true); reload(); }} /></div>
          : !archived && <p className="muted">{data.status === 'Accepted' ? 'Решение принято. Работа доступна только для чтения.'
            : data.status === 'NeedsRevision' ? 'Работа возвращена на доработку. Ожидается новая попытка ученика.' : 'Действия проверки недоступны.'}</p>}
      </>}
    </div>
  </AppShell>;
}
