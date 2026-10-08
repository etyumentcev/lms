import { useEffect, useMemo, useRef } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import type { ApiClient, ApiError } from '../../api/client';
import type { ProblemWorkspace } from '../../api/contracts/problemWorkspace';
import { AppShell, ContextSection } from '../../layout/AppShell';
import { Breadcrumbs, EmptyState, ErrorState, LoadingSkeleton, PageHeader } from '../../components/primitives';
import { StatusBadge } from '../../components/StatusBadge';
import { Tabs } from '../../components/Tabs';
import { MarkdownContent } from '../../components/MarkdownContent';
import { attachmentApi } from '../../components/attachments/api';
import { CurrentSubmission } from './CurrentSubmission';
import { SolutionForm } from './SolutionForm';
import { useProblemWorkspace } from './query';
import './problem.css';

function Failure({ error, retry }: { error: ApiError; retry: () => void }) {
  const title = error.status === 401 ? 'Войдите, чтобы открыть задачу' : error.status === 403 ? 'Нет доступа к задаче'
    : error.status === 404 ? 'Задача не найдена' : 'Не удалось загрузить задачу';
  return <ErrorState title={title} description={error.status === 401 ? 'Требуется вход в учебную систему.'
    : error.status === 403 ? 'Задача недоступна вашей учётной записи.' : error.status === 404 ? 'Проверьте ссылку на задачу.'
    : 'Проверьте соединение и повторите загрузку. Отправка решения не повторяется.'}
    onRetry={[401, 403, 404].includes(error.status) ? undefined : retry} />;
}
function Materials({ materials }: { materials: ProblemWorkspace['usefulMaterials'] }) {
  if (!materials.length) return null;
  return <ContextSection title="Полезные материалы"><ul>{materials.map(material => {
    let href: string | undefined;
    try { const url = new URL(material.url, window.location.origin); if (['http:', 'https:'].includes(url.protocol)) href = url.href; } catch { /* Text only for unsafe URLs. */ }
    return <li key={material.id}>{href ? <a href={href}>{material.title}</a> : <span>{material.title}</span>}
      {material.description && <p className="small muted">{material.description}</p>}</li>;
  })}</ul></ContextSection>;
}
export function ProblemPage({ assignmentId, client }: { assignmentId: string; client: ApiClient }) {
  const { state, refresh } = useProblemWorkspace(client, assignmentId);
  const [params, setParams] = useSearchParams();
  const api = useMemo(() => attachmentApi(client), [client]);
  const afterRefresh = useRef(false);
  const data = state.phase === 'success' ? state.data : null;
  const title = data ? data.problem.title?.trim() || `Задача ${data.problem.order}` : state.phase === 'loading' ? 'Загрузка задачи' : 'Задача';
  const tab = ['solution', 'hints', 'history'].includes(params.get('tab') ?? '') ? params.get('tab')! : 'solution';
  function tabParams(value: string) { const next = new URLSearchParams(params); next.set('tab', value); return next; }
  const reload = () => { afterRefresh.current = true; refresh(); };
  useEffect(() => { document.title = `${title} · Sapientum`; }, [title]);
  useEffect(() => {
    if (state.phase !== 'loading' && afterRefresh.current) {
      document.getElementById('main-content')?.focus(); afterRefresh.current = false;
    }
  }, [state.phase]);
  const feedback = data?.work.latestTeacherFeedback;
  return <AppShell preview={false} role="Student" context={state.phase === 'loading'
    ? <ContextSection title="Текущая задача"><LoadingSkeleton label="Загрузка контекста задачи" /></ContextSection>
    : data ? <><ContextSection title="Текущая задача"><StatusBadge status={data.work.status} />
      <p>{data.work.attemptCount === 0 ? 'Решение ещё не отправлено' : `Попыток: ${data.work.attemptCount}`}</p>
      {data.work.latestSubmission && <p>Последняя попытка №{data.work.latestSubmission.attemptNumber}</p>}
      <Link to={{ search: `?${tabParams('history')}` }}>Перейти к истории →</Link>
    </ContextSection>{feedback && <ContextSection title="Обратная связь"><p>Есть замечание преподавателя к последней проверке.</p>
      <Link to={{ search: `?${tabParams('solution')}`, hash: '#teacher-feedback' }}>Прочитать замечание →</Link></ContextSection>}
      <Materials materials={data.usefulMaterials} /></> : null}>
    <Breadcrumbs items={[{ label: 'Мои курсы', to: '/courses' }, ...(data ? [
      { label: data.course.title || 'Курс', to: `/courses/${encodeURIComponent(data.course.id)}` },
      { label: data.lesson.title || `Занятие ${data.lesson.order}`, to: `/courses/${encodeURIComponent(data.course.id)}/lessons/${encodeURIComponent(data.lesson.id)}` },
    ] : []), { label: title }]} />
    <div className="workspace-content problem-workspace" aria-busy={state.phase === 'loading'}>
      <PageHeader title={title} meta={data ? <span>Задача {data.problem.order}</span> : undefined} />
      {state.phase === 'loading' && <><LoadingSkeleton label="Загрузка условия задачи" rows={4} />
        <LoadingSkeleton label="Загрузка вкладок" rows={1} /><LoadingSkeleton label="Загрузка решения" rows={4} /></>}
      {state.phase === 'error' && <Failure error={state.error} retry={reload} />}
      {data && <>
        <section className="problem-statement" aria-label="Условие задачи"><MarkdownContent>{data.problem.statementMarkdown}</MarkdownContent></section>
        <Tabs label="Задача" value={tab} onChange={value => setParams(tabParams(value))} items={[
          { id: 'solution', label: 'Решение', content: <>
            {feedback && <section id="teacher-feedback" className="revision-feedback" aria-labelledby="feedback-heading">
              <h2 id="feedback-heading">{data.work.status === 'NeedsRevision' ? 'Что нужно исправить' : 'Замечание преподавателя'}</h2>
              <MarkdownContent>{feedback.bodyMarkdown}</MarkdownContent>
            </section>}
            {data.work.status === 'Submitted' && <p role="status">Решение отправлено и ожидает проверки преподавателем.</p>}
            {data.work.status === 'Accepted' && <p role="status">Решение принято.</p>}
            {data.permissions.canSubmit && data.availability.isAvailable && data.work.status !== 'Accepted'
              && <SolutionForm data={data} client={client} api={api} refresh={reload} />}
            {data.work.latestSubmission && <CurrentSubmission submission={data.work.latestSubmission} api={api} />}
          </> },
          { id: 'hints', label: 'Подсказки', content: data.hints.length ? <ol className="problem-hints">{[...data.hints].sort((a, b) => a.order - b.order).map(hint => <li key={hint.id}>
            <h2>{hint.title || `Подсказка ${hint.order}`}</h2><MarkdownContent>{hint.bodyMarkdown}</MarkdownContent>
          </li>)}</ol> : <EmptyState title="Подсказок пока нет" /> },
          { id: 'history', label: 'История', content: <EmptyState title="История будет доступна здесь" /> },
        ]} />
      </>}
    </div>
  </AppShell>;
}
