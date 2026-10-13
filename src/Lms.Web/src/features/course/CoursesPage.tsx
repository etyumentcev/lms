import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApiError, type ApiClient } from '../../api/client';
import type { CourseWorkspace } from '../../api/contracts/courseWorkspace';
import { course, oneOf } from '../../api/contracts/lessonWorkspace';
import { array, count, nullable, object, percent } from '../../api/contracts/validation';
import { AppShell } from '../../layout/AppShell';
import { Breadcrumbs, EmptyState, ErrorState, LoadingSkeleton, PageHeader, ProgressBar } from '../../components/primitives';

type CourseList = { items: { course: CourseWorkspace['course']; role: 'Student' | 'Teacher'; progress: CourseWorkspace['progress'] | null }[] };
const valid = object({ items: array(object({ course, role: oneOf('Student', 'Teacher'), progress: nullable(object({
  completedLessons: count, availableLessons: count, totalLessons: count, percent,
})) })) });
export function decodeCourses(value: unknown): CourseList {
  if (!valid(value)) throw new Error('Invalid CourseNavigation');
  return value as CourseList;
}
type State = { phase: 'loading' } | { phase: 'success'; data: CourseList } | { phase: 'error'; error: ApiError };
export function CoursesPage({ client }: { client: ApiClient }) {
  const [state, setState] = useState<State>({ phase: 'loading' });
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    document.title = 'Мои курсы · Sapientum';
    const controller = new AbortController();
    setState({ phase: 'loading' });
    void client.request('/courses', decodeCourses, { signal: controller.signal }).then(data => {
      if (!controller.signal.aborted) setState({ phase: 'success', data });
    }).catch((error: unknown) => {
      if (!controller.signal.aborted) setState({ phase: 'error', error: error instanceof ApiError ? error : new ApiError(0, 'RequestFailed') });
    });
    return () => controller.abort();
  }, [client, attempt]);
  useEffect(() => {
    const restored = (event: PageTransitionEvent) => { if (event.persisted) { setState({ phase: 'loading' }); setAttempt(n => n + 1); } };
    window.addEventListener('pageshow', restored);
    return () => window.removeEventListener('pageshow', restored);
  }, []);
  return <AppShell preview={false} role={null} context={null}>
    <Breadcrumbs items={[{ label: 'Мои курсы' }]} />
    <div className="workspace-content" aria-busy={state.phase === 'loading'}>
      <PageHeader title="Мои курсы" description="Доступные вам курсы и занятия." />
      {state.phase === 'loading' && <LoadingSkeleton label="Загрузка курсов" />}
      {state.phase === 'error' && <ErrorState title={state.error.status === 401 ? 'Войдите, чтобы открыть курсы'
        : state.error.status === 403 ? 'Нет доступа к курсам' : state.error.status === 404 || state.error.status === 503 ? 'Список курсов пока недоступен' : 'Не удалось загрузить курсы'}
        description="Проверьте доступ и повторите загрузку."
        onRetry={[401, 403, 404].includes(state.error.status) ? undefined : () => { setState({ phase: 'loading' }); setAttempt(n => n + 1); }} />}
      {state.phase === 'success' && (state.data.items.length ? <ol className="workspace-list">{state.data.items.map(item => <li key={item.course.id}>
        <Link to={`/courses/${encodeURIComponent(item.course.id)}`}><span className="row-text"><strong>{item.course.title || item.course.id}</strong>
          {item.course.description && <span className="muted">{item.course.description}</span>}
          <span>{item.role === 'Teacher' ? 'Преподаватель' : 'Ученик'}</span>
          {item.progress && <ProgressBar value={item.progress.percent} label={`Прогресс курса ${item.course.title || item.course.id}`} />}
        </span><span aria-hidden="true">→</span></Link>
      </li>)}</ol> : <EmptyState title="Пока нет доступных курсов" description="Здесь появятся назначенные вам курсы." />)}
    </div>
  </AppShell>;
}
