import { newPage, type PageState } from '../../components/annotations/model';
import { useUnsavedAnnotations } from './useUnsavedAnnotations';
import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, useLocation, useSearchParams } from 'react-router-dom';
import type { ApiClient } from '../../api/client';
import { AppShell, ContextSection } from '../../layout/AppShell';
import { Breadcrumbs, Button, EmptyState, ErrorState, LoadingSkeleton, PageHeader } from '../../components/primitives';
import { MarkdownContent } from '../../components/MarkdownContent';
import { StatusBadge } from '../../components/StatusBadge';
import { AttachmentPreview } from '../../components/attachments/AttachmentPreview';
import { teacherAttachmentApi } from '../../components/attachments/api';
import { normalizeQueueQuery } from '../review-queue/query';
import { useProblemWork } from './query';
import { ReviewForm } from './ReviewForm';
import { ReviewContext } from './ReviewContext';
import { SubmissionViewer } from './SubmissionViewer';
import { TeacherMessageForm } from './TeacherMessageForm';
import '../problem-workspace/problem.css';
import './review.css';
export function TeacherReviewPage({ problemWorkId, client }: { problemWorkId: string; client: ApiClient }) {
  const location = useLocation();
  const queueSearch = typeof location.state?.queueSearch === 'string' ? normalizeQueueQuery(new URLSearchParams(location.state.queueSearch)).toString() : '';
  const queueUrl = `/teacher/reviews${queueSearch ? `?${queueSearch}` : ''}`;
  const { state, refresh } = useProblemWork(client, problemWorkId);
  const [pages, setPages] = useState<Record<string, PageState>>({});
  const [busy, setBusy] = useState(false);
  const dirty = Object.values(pages).some(p => p.strokes.length > 0 || p.undo.length > 0);
  useUnsavedAnnotations(dirty);
  const [feedback, setFeedback] = useState('');
  const [saved, setSaved] = useState(false);
  const [params, setParams] = useSearchParams();
  const selected = params.get('attempt');
  function setSelected(id: string | null) { const next = new URLSearchParams(params); if (id) next.set('attempt', id); else next.delete('attempt'); setParams(next, { state: location.state }); }
  const focusAfterRefresh = useRef(false), focusViewer = useRef(false);
  const viewerRef = useRef<HTMLElement>(null);
  const api = useMemo(() => teacherAttachmentApi(client, problemWorkId), [client, problemWorkId]);
  const data = state.phase === 'success' ? state.data : null;
  const current = data?.currentSubmission;
  const submission = selected ? data?.submissions.find(item => item.id === selected) ?? current : current;
  const archived = !!submission && submission.id !== current?.id;
  const review = data?.reviews.find(item => item.submissionId === submission?.id);
  const reviewedFeedback = data?.messages.find(item => item.id === review?.messageId);
  const title = data ? data.problem.title?.trim() || `Задача ${data.problem.order}` : 'Проверка решения';
  const reload = () => { if (dirty && !window.confirm('Пометки останутся локально. После обновления работа может оказаться уже проверенной. Обновить?')) return; focusAfterRefresh.current = true; setSelected(null); refresh(); };
  function selectAttempt(id: string | null) { focusViewer.current = true; setSelected(id); }
  useEffect(() => { document.title = `${title} · Sapientum`; }, [title]);
  useEffect(() => {
    if (state.phase !== 'loading' && focusAfterRefresh.current) { document.getElementById('main-content')?.focus(); focusAfterRefresh.current = false; }
  }, [state.phase]);
  useEffect(() => { if (focusViewer.current) { viewerRef.current?.focus(); focusViewer.current = false; } }, [selected]);
  return <AppShell role={data && (data.permissions.canViewAsTeacher || data.permissions.canAccept || data.permissions.canRequestRevision) ? 'Teacher' : null} preview={false}
    context={state.phase === 'loading' ? <ContextSection title="Информация о работе"><LoadingSkeleton label="Загрузка контекста проверки" /></ContextSection>
      : data ? <ReviewContext data={data} selectAttempt={selectAttempt} client={client} /> : null}>
    <Breadcrumbs items={[{ label: 'Мои курсы', to: '/courses' }, ...(data ? [
      { label: data.course.title || 'Курс', to: `/courses/${encodeURIComponent(data.course.id)}` },
      { label: data.lesson.title || 'Занятие', to: `/courses/${encodeURIComponent(data.course.id)}/lessons/${encodeURIComponent(data.lesson.id)}` },
    ] : []), { label: title }]} />
    <div className="workspace-content teacher-review" aria-busy={state.phase === 'loading'}>
      <PageHeader title={title} meta={data ? <StatusBadge status={data.status} /> : undefined} />
      <Link className="text-link review-queue-link" to={queueUrl}>К очереди проверок</Link>
      {data && <Link className="text-link" to={`/courses/${encodeURIComponent(data.course.id)}/lessons/${encodeURIComponent(data.lesson.id)}`}>Назад к занятию</Link>}
      {dirty && <p role="status">Есть несохранённые графические пометки. Они сохранятся вместе с решением проверки.</p>}
      {saved && <p role="status">{state.phase === 'success' ? 'Проверка сохранена. Состояние работы обновлено.' : 'Проверка сохранена. Ожидается обновление состояния работы.'}</p>}
      {state.phase === 'loading' && <><LoadingSkeleton label="Загрузка условия задачи" rows={3} /><LoadingSkeleton label="Загрузка решения" rows={6} /></>}
      {state.phase === 'error' && <ErrorState title={state.error.status === 401 ? 'Войдите, чтобы открыть работу' : state.error.status === 403 ? 'Нет доступа к работе'
        : state.error.status === 404 ? 'Работа не найдена' : 'Не удалось загрузить работу'}
        description={state.error.code === 'ContentUnavailable' ? 'Историческое условие временно недоступно. Повторите загрузку.'
          : 'Проверьте доступ и соединение. Повтор загрузки не отправляет решение проверки повторно.'}
        onRetry={[401, 403, 404].includes(state.error.status) ? undefined : reload} />}
      {data && <>
        <Link className="text-link" to={{ search: '?tab=history' }} state={location.state}>История работы</Link>
        {params.get('tab') === 'history' && <section aria-label="История работы"><h2>История</h2>
          <Link to={{ search: '' }} state={location.state}>Вернуться к текущему решению</Link>
          {data.submissions.length ? <ol>{data.submissions.map(attempt => <li key={attempt.id}>
            <Link to={{ search: `?attempt=${encodeURIComponent(attempt.id)}` }} state={location.state}>Попытка №{attempt.attemptNumber}</Link>
          </li>)}</ol> : <EmptyState title="Отправленных попыток нет" />}
        </section>}

        <section className="review-statement" aria-label="Условие задачи"><h2>Условие задачи</h2><MarkdownContent>{data.problem.statementMarkdown}</MarkdownContent></section>
        <section ref={viewerRef} tabIndex={-1} aria-label="Отправленное решение" className="review-work-area">
          {archived && <div className="review-archive-note"><p>Просмотр предыдущей попытки. Действия проверки доступны только для текущей попытки №{current?.attemptNumber}.</p>
            <Button onClick={() => selectAttempt(null)}>К текущей попытке</Button></div>}
          {submission ? <SubmissionViewer key={submission.id} submission={submission} api={api} annotations={review?.annotations ?? []}
            editable={!archived && !review && !busy && data.status === 'Submitted' && (data.permissions.canAccept || data.permissions.canRequestRevision)}
            pages={!archived && !review ? pages : undefined} changePages={!archived && !review ? setPages : undefined} /> : <EmptyState title="Отправленных попыток нет" />}
          {review && <section className="review-outcome" aria-label="Результат проверки"><h2>Результат проверки попытки №{submission?.attemptNumber}</h2><StatusBadge status={review.decision} />
            <p><time dateTime={review.createdAt}>{new Intl.DateTimeFormat('ru', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(review.createdAt))}</time></p>
            {reviewedFeedback && <><div className="review-text"><MarkdownContent>{reviewedFeedback.bodyMarkdown}</MarkdownContent></div>
              {reviewedFeedback.attachmentIds.length > 0 && <section aria-label="Фотографии замечания"><ol className="attachment-pages">
                {reviewedFeedback.attachmentIds.map((attachmentId, index) => <li key={attachmentId}>
                  <h3>Страница замечания {index + 1}</h3><AttachmentPreview api={api} id={attachmentId} page={index + 1} />
                </li>)}
              </ol></section>}</>}
          </section>}
        </section>
        {current && data.status === 'Submitted' && (data.permissions.canAccept || data.permissions.canRequestRevision)
          ? <div hidden={archived}><ReviewForm data={data} client={client} feedback={feedback} changeFeedback={setFeedback} refresh={reload} busy={setBusy} annotations={current.attachmentIds.flatMap(id => {
            const p = pages[id] ?? newPage(); return p.strokes.length ? [{ attachmentId: id, strokes: p.strokes }] : [];
          })} completed={() => { setPages({}); setSaved(true); focusAfterRefresh.current = true; setSelected(null); refresh(); }} /></div>
          : !archived && <p className="muted">{data.status === 'Accepted' ? 'Решение принято. Работа доступна только для чтения.'
            : data.status === 'NeedsRevision' ? 'Работа возвращена на доработку. Ожидается новая попытка ученика.' : 'Действия проверки недоступны.'}</p>}
        {!archived && data.permissions.canPostMessage && <TeacherMessageForm client={client} api={api}
          workId={data.id} refresh={reload} completed={() => { setSaved(true); focusAfterRefresh.current = true; refresh(); }} />}
      </>}
    </div>
  </AppShell>;
}
