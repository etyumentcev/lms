import { ContentPage } from '../content/ContentPage';
import { useEffect } from 'react';
import { Link } from 'react-router-dom';
import type { ApiClient, ApiError } from '../../api/client';
import type { LessonWorkspace } from '../../api/contracts/lessonWorkspace';
import { AppShell, ContextSection } from '../../layout/AppShell';
import { StatusBadge, AvailabilityBadge } from '../../components/StatusBadge';
import { Breadcrumbs, EmptyState, ErrorState, LoadingSkeleton, PageHeader, ProgressBar } from '../../components/primitives';
import { useLessonWorkspace } from './query';
import './lesson.css';
import { MarkdownContent } from '../../components/MarkdownContent';

const lessonLabels = { NotStarted: 'Не начат', InProgress: 'В работе', Completed: 'Завершён' };
const actions = { NotStarted: 'Открыть задачу', Submitted: 'Посмотреть решение', NeedsRevision: 'Исправить решение', Accepted: 'Посмотреть решение' };
function problemTitle(row: LessonWorkspace['problems'][number]) {
  return row.problem.title?.trim() || row.problem.statementMarkdown.trim().slice(0, 160) || `Задача ${row.problem.order}`;
}
function canOpenReview(row: LessonWorkspace['problems'][number]) {
  return !!row.work.problemWorkId && (row.permissions.canViewAsTeacher || row.permissions.canAccept || row.permissions.canRequestRevision);
}
function problemAction(row: LessonWorkspace['problems'][number]) {
  if (canOpenReview(row)) return row.permissions.canAccept || row.permissions.canRequestRevision ? 'Проверить решение' : 'Посмотреть решение';
  if (row.permissions.canViewAsTeacher) return 'Посмотреть задачу';
  return row.work.status === 'NeedsRevision' && !row.permissions.canSubmit ? 'Посмотреть задачу' : actions[row.work.status];
}
export function attempts(count: number) {
  const form = new Intl.PluralRules('ru').select(count);
  return `${count} ${form === 'one' ? 'попытка' : form === 'few' ? 'попытки' : 'попыток'}`;
}
function Materials({ data }: { data: LessonWorkspace }) {
  if (!data.materials.length) return null;
  return <section aria-labelledby="lesson-materials"><h2 id="lesson-materials">Материалы занятия</h2><ul>
    {data.materials.map(material => {
      let href: string | undefined;
      try { const url = new URL(material.url, window.location.origin); if (['http:', 'https:'].includes(url.protocol)) href = url.href; } catch { /* Invalid source URL remains text. */ }
      return <li key={material.id}>{href ? <a href={href}>{material.title}</a> : <span>{material.title} · Ссылка недоступна</span>}
        {material.description && <p className="muted">{material.description}</p>}</li>;
    })}
  </ul></section>;
}
function Problems({ data }: { data: LessonWorkspace }) {
  const multipleStudents = new Set(data.problems.map(row => row.studentId)).size > 1;
  return <section aria-labelledby="lesson-problems"><h2 id="lesson-problems">{data.problems.some(row => row.permissions.canViewAsTeacher) ? 'Работы учеников' : 'Задачи'}</h2>
    {data.problems.length === 0 ? <EmptyState title="В занятии пока нет задач" /> : <ol className="lesson-problems">
      {data.problems.map(row => <li key={row.problemAssignmentId} className="lesson-problem">
        <span className="row-number" aria-label={`Задача ${row.problem.order}`}>{row.problem.order}</span>
        <div className="lesson-problem-content"><h3>{row.availability.isAvailable ? problemTitle(row) : `Задача ${row.problem.order}`}</h3>
          <div className="lesson-meta">{multipleStudents && <span className="small muted">Ученик: {row.studentId}</span>}<StatusBadge status={row.work.status} />
            {row.work.attemptCount > 0 && <span className="muted small">{attempts(row.work.attemptCount)}</span>}
            <AvailabilityBadge isAvailable={row.availability.isAvailable} explanation={row.availability.reason} />
          </div>
        </div>
        {row.availability.isAvailable && <Link className="lesson-action" to={canOpenReview(row) ? `/teacher/reviews/${encodeURIComponent(row.work.problemWorkId!)}`
          : row.permissions.canViewAsTeacher ? `/courses/${encodeURIComponent(data.course.id)}/lessons/${encodeURIComponent(data.lesson.id)}/assignments/${encodeURIComponent(row.problemAssignmentId)}`
          : `/problem-assignments/${encodeURIComponent(row.problemAssignmentId)}?source=lesson`}
          aria-label={`${problemAction(row)}: ${problemTitle(row)}`}>{problemAction(row)} →</Link>}
      </li>)}
    </ol>}
  </section>;
}
function Failure({ error, retry }: { error: ApiError; retry: () => void }) {
  const title = error.status === 401 ? 'Войдите, чтобы открыть занятие' : error.status === 403 ? 'Нет доступа к занятию'
    : error.status === 404 ? 'Занятие не найдено' : 'Не удалось загрузить занятие';
  return <ErrorState title={title} description={error.code === 'ContentUnavailable' ? 'Материалы временно недоступны.'
    : error.status === 401 ? 'Требуется вход в учебную систему.' : error.status === 403 ? 'Занятие недоступно вашей учётной записи.'
    : error.status === 404 ? 'Проверьте ссылку на занятие.' : 'Проверьте соединение и повторите попытку.'}
    onRetry={[401, 403, 404].includes(error.status) ? undefined : retry} />;
}
export function LessonPage({ courseId, lessonId, client, assignmentId }: { courseId: string; lessonId: string; client: ApiClient; assignmentId?: string }) {
  const { state, retry } = useLessonWorkspace(client, courseId, lessonId);
  const data = state.phase === 'success' ? state.data : null;
  const available = data?.availability.isAvailable;
  const selected = data?.problems.find(row => row.problemAssignmentId === assignmentId && row.permissions.canViewAsTeacher && row.availability.isAvailable);
  const lessonUrl = `/courses/${encodeURIComponent(courseId)}/lessons/${encodeURIComponent(lessonId)}`;
  const title = data?.lesson.title?.trim() || (data ? `Занятие ${data.lesson.order}` : state.phase === 'loading' ? 'Загрузка занятия' : 'Занятие');
  useEffect(() => { document.title = `${title} · Sapientum`; }, [title]);
  return <AppShell preview={false} role={null} context={state.phase === 'loading'
    ? <ContextSection title="О занятии"><LoadingSkeleton label="Загрузка контекста занятия" rows={3} /></ContextSection>
    : data ? <><ContextSection title="О занятии"><p>Занятие {data.lesson.order}</p>
      <AvailabilityBadge isAvailable={!!available} explanation={data.availability.reason} />
      {data.availability.availableFrom && <p>Доступ с {new Intl.DateTimeFormat('ru', { dateStyle: 'long', timeStyle: 'short' }).format(new Date(data.availability.availableFrom))}</p>}
    </ContextSection>{available && <ContextSection title="Прогресс занятия"><p>{data.progress.acceptedProblems} из {data.progress.totalAssignedProblems} задач принято</p>
      <ProgressBar value={data.progress.percent} label="Завершение занятия" /><p>{lessonLabels[data.progress.status]}</p>
      <p className="muted small">По доступным вам назначенным задачам.</p></ContextSection>}</> : null}>
    <Breadcrumbs items={[{ label: 'Мои курсы', to: '/courses' }, { label: data?.course.title || 'Курс', to: `/courses/${encodeURIComponent(courseId)}` }, { label: title, to: assignmentId ? lessonUrl : undefined }, ...(assignmentId ? [{ label: 'Задание' }] : [])]} />
    <div className="workspace-content lesson-workspace" aria-busy={state.phase === 'loading'}>
      <PageHeader title={title} description={available ? data?.lesson.summary ?? undefined : undefined} />
      {state.phase === 'loading' && <><LoadingSkeleton label="Загрузка описания занятия" rows={2} /><LoadingSkeleton label="Загрузка задач" rows={5} /></>}
      {state.phase === 'error' && <Failure error={state.error} retry={retry} />}
      <Link className="text-link" to={assignmentId ? lessonUrl : `/courses/${encodeURIComponent(courseId)}`}>{assignmentId ? 'Назад к занятию' : 'Назад к курсу'}</Link>
      {data && (available ? assignmentId ? selected ? <section aria-label="Задание ученика"><h2>{problemTitle(selected)}</h2><MarkdownContent>{selected.problem.statementMarkdown}</MarkdownContent>
        {selected.work.problemWorkId ? <Link to={`/teacher/reviews/${encodeURIComponent(selected.work.problemWorkId)}`}>Открыть работу и историю</Link> : <EmptyState title="Решение ещё не отправлено" description="Проверка станет доступна после отправки решения учеником." />}</section>
        : <ErrorState title="Задание недоступно" description="Назначение отсутствует или доступ отозван." /> : <><Problems data={data} /><Materials data={data} /></> : <EmptyState title="Занятие недоступно" description={data.availability.reason} />)}
    </div>
  </AppShell>;
}

export function LessonContentPage({ path, client }: { path: string; client: ApiClient }) {
  return <ContentPage kind="lesson" path={path} client={client} />;
}
