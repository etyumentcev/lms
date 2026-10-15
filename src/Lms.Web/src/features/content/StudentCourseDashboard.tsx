import { useEffect, useId, useState } from 'react';
import type { ReactNode } from 'react';
import { Link, useLocation, useSearchParams } from 'react-router-dom';
import { ApiError, type ApiClient } from '../../api/client';
import { browserApiClient } from '../../api/browserClient';
import { decodeStudentRecentProgress, type RecentSubmission } from '../../api/contracts/progressHistory';
import { StatusBadge } from '../../components/StatusBadge';
import type { PublishedStudentContent, StudentAssignment } from '../../api/contracts/publishedContent';
import { AvailabilityBadge } from '../../components/StatusBadge';
import { Breadcrumbs, EmptyState, PageHeader, ProgressBar } from '../../components/primitives';
import { AppShell } from '../../layout/AppShell';
import { CourseScheduleCalendar } from './CourseScheduleCalendar';
import { CourseScheduleAside, UpcomingCourseEvents } from './CourseScheduleSummary';
import './studentCourseDashboard.css';

type DashboardIconName = 'book' | 'check' | 'person' | 'calendar' | 'pin';

function DashboardIcon({ name }: { name: DashboardIconName }) {
  return <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"
    strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
    {name === 'book' && <><path d="M12 7v14"/><path d="M3 18V5a2 2 0 0 1 2-2h2a5 5 0 0 1 5 5 5 5 0 0 1 5-5h2a2 2 0 0 1 2 2v13a1 1 0 0 1-1 1h-3a5 5 0 0 0-5 2 5 5 0 0 0-5-2H4a1 1 0 0 1-1-1Z"/></>}
    {name === 'check' && <><circle cx="12" cy="12" r="9"/><path d="m8 12 2.7 2.7L16 9.5"/></>}
    {name === 'person' && <><circle cx="12" cy="7" r="3.5"/><path d="M5 20v-1a7 7 0 0 1 14 0v1H5Z"/></>}
    {name === 'calendar' && <><rect x="3" y="5" width="18" height="16" rx="2"/><path d="M7 3v4m10-4v4M3 10h18"/></>}
    {name === 'pin' && <><path d="M20 10c0 5-8 11-8 11S4 15 4 10a8 8 0 1 1 16 0Z"/><circle cx="12" cy="10" r="2.5"/></>}
  </svg>;
}

export function DashboardInfoCard({ title, icon, children }: { title: string; icon: DashboardIconName; children: ReactNode }) {
  const headingId = useId();
  return <section className="student-course-info-card" aria-labelledby={headingId}>
    <h3 id={headingId}><span className="student-course-info-icon"><DashboardIcon name={icon}/></span>{title}</h3>
    <div className="student-course-info-content">{children}</div>
  </section>;
}

type Row = {
  versionId: string;
  versionNumber: number;
  lesson: PublishedStudentContent['course']['lessons'][number];
  assignments: StudentAssignment[];
};

function uniqueAssignments(items: StudentAssignment[]) {
  const byId = new Map<string, StudentAssignment>();
  for (const item of items) {
    const previous = byId.get(item.assignmentId);
    if (!previous || previous.status !== 'Accepted') byId.set(item.assignmentId, item);
  }
  return [...byId.values()];
}

type RecentState =
  | { phase: 'loading' }
  | { phase: 'ready'; items: RecentSubmission[] }
  | { phase: 'error'; error: ApiError };

function RecentSubmissions({ client, courseId, lessonsUrl }: {
  client: ApiClient; courseId: string; lessonsUrl: string;
}) {
  const [state, setState] = useState<RecentState>({ phase: 'loading' });
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    setState({ phase: 'loading' });
    void client.request(
      `/student/progress-history?courseId=${encodeURIComponent(courseId)}&sort=lastSubmissionDesc&page=1&pageSize=5`,
      value => decodeStudentRecentProgress(value, courseId), { signal: controller.signal }
    ).then(data => {
      if (!controller.signal.aborted) setState({ phase: 'ready', items: data.items });
    }).catch(reason => {
      if (!controller.signal.aborted) setState({ phase: 'error',
        error: reason instanceof ApiError ? reason : new ApiError(0, 'InvalidResponse') });
    });
    return () => controller.abort();
  }, [client, courseId, revision]);
  return <section className="student-course-card student-course-submissions" aria-labelledby="student-recent-submissions">
    <header><h2 id="student-recent-submissions">Последние сданные задачи на проверку</h2>
      <Link to={lessonsUrl}>Все задачи <span aria-hidden="true">→</span></Link></header>
    {state.phase === 'loading' && <p className="muted" role="status">Загрузка последних задач…</p>}
    {state.phase === 'error' && <div role="alert"><p>Не удалось загрузить последние задачи.</p>
      {![401, 403].includes(state.error.status) && <button type="button"
        onClick={() => setRevision(value => value + 1)}>Повторить</button>}</div>}
    {state.phase === 'ready' && (state.items.length
      ? <ol className="student-course-submission-list">{state.items.map(item =>
        <li key={item.assignmentId}><Link to={`/problem-assignments/${encodeURIComponent(item.assignmentId)}`}>
          <span className="student-course-submission-title"><strong>{item.problemTitle || item.problemId}</strong>
            <small className="muted">{item.lessonTitle || item.lessonId}</small></span>
          <StatusBadge status={item.status}/>
          <time dateTime={item.lastSubmittedAt}>{new Intl.DateTimeFormat('ru', {
            dateStyle: 'medium', timeStyle: 'short'
          }).format(new Date(item.lastSubmittedAt))}</time><span aria-hidden="true">→</span>
        </Link></li>)}</ol>
      : <p className="muted">Отправленных задач пока нет.</p>)}
  </section>;
}

function Lessons({ rows, courseId }: { rows: Row[]; courseId: string }) {
  return <section aria-labelledby="published-lessons">
    <div className="section-intro"><h2 id="published-lessons">Занятия</h2><span className="muted">{rows.length}</span></div>
    {rows.length ? <ol className="study-card-list">{rows.map(row => {
      const assignments = uniqueAssignments(row.assignments);
      const done = assignments.filter(item => item.status === 'Accepted').length;
      return <li key={`${row.versionId}/${row.lesson.id}`}><Link
        to={`/study/courses/${encodeURIComponent(courseId)}/versions/${encodeURIComponent(row.versionId)}/lessons/${encodeURIComponent(row.lesson.id)}`}>
        <span className="row-number">{row.lesson.order}</span>
        <span className="row-text"><strong>{row.lesson.title}</strong><small><AvailabilityBadge isAvailable/> · {assignments.length} задач · {done} принято · версия v{row.versionNumber}</small></span>
        <span aria-hidden="true">→</span>
      </Link></li>;
    })}</ol> : <EmptyState title="Нет доступных занятий" description="Преподаватель ещё не открыл занятие вашей группе."/>}
  </section>;
}

function Summary({ rows, assignments, courseId, lessonsUrl, client }: {
  rows: Row[]; assignments: StudentAssignment[]; courseId: string; lessonsUrl: string; client: ApiClient;
}) {
  const own = uniqueAssignments(assignments);
  const accepted = own.filter(item => item.status === 'Accepted').length;
  const percent = own.length ? Math.round(accepted / own.length * 100) : 0;
  return <section className="student-course-summary" aria-label="Сводка курса">
    <div className="student-course-stats">
      <article className="student-course-stat"><span className="student-course-stat-icon"><DashboardIcon name="book"/></span><div>
        <h2>Просмотр материалов</h2><p className="muted">Учёт просмотров пока недоступен.</p>
      </div></article>
      <article className="student-course-stat"><span className="student-course-stat-icon"><DashboardIcon name="check"/></span><div>
        <h2>Решённые задачи</h2>
        {own.length ? <><p className="student-course-stat-value">{accepted} <span>из {own.length}</span></p>
          <ProgressBar value={percent} label="Принято задач"/></> : <p className="muted">Назначенных задач пока нет.</p>}
      </div></article>
    </div>
    <div className="student-course-summary-grid">
      <UpcomingCourseEvents courseId={courseId} client={client}/>
      <RecentSubmissions client={client} courseId={courseId} lessonsUrl={lessonsUrl}/>
      <section className="student-course-card"><header><h2>Прогресс по последним урокам</h2>
        <Link to={lessonsUrl}>Все уроки <span aria-hidden="true">→</span></Link></header>
        {rows.length ? <ol className="student-course-recent-lessons">{[...rows]
          .sort((a, b) => b.lesson.order - a.lesson.order || b.versionNumber - a.versionNumber).slice(0, 5).map(row => {
            const tasks = uniqueAssignments(row.assignments);
            const done = tasks.filter(item => item.status === 'Accepted').length;
            const percent = tasks.length ? Math.round(done / tasks.length * 100) : null;
            return <li key={`${row.versionId}/${row.lesson.id}`}><Link
              to={`/study/courses/${encodeURIComponent(courseId)}/versions/${encodeURIComponent(row.versionId)}/lessons/${encodeURIComponent(row.lesson.id)}`}>
              <span className="row-number">{row.lesson.order}</span>
              <span className="student-course-lesson-copy"><strong>{row.lesson.title}</strong>
                <small className="muted">{tasks.length ? `Принято задач: ${done} из ${tasks.length}` : 'Назначенных задач нет'}</small>
              </span>
              {percent === null ? <span className="student-course-lesson-unavailable muted" aria-hidden="true">—</span> :
                <span className="student-course-lesson-progress" role="progressbar"
                  aria-label={`Прогресс урока ${row.lesson.title}`} aria-valuenow={percent}
                  aria-valuemin={0} aria-valuemax={100} aria-valuetext={`Принято задач: ${done} из ${tasks.length}`}>
                  <span className="student-course-lesson-track"><span style={{ width: `${percent}%` }}/></span>
                  <span className="student-course-lesson-percent">{percent}%</span>
                </span>}
              <span aria-hidden="true">→</span>
            </Link></li>;
          })}</ol> : <p className="muted">Доступных занятий пока нет.</p>}
      </section>
      <section className="student-course-card"><header><h2>Последние материалы</h2></header>
        <p className="muted">Отдельный список опубликованных материалов пока недоступен. Теория доступна в занятиях.</p></section>
    </div>
  </section>;
}

export function StudentCourseDashboard({ courseId, title, description, assignments, rows,
  client = browserApiClient }: {
  courseId: string; title: string; description?: string; assignments: StudentAssignment[]; rows: Row[]; client?: ApiClient;
}) {
  const [params] = useSearchParams();
  const location = useLocation();
  const tab = params.get('tab') === 'lessons' ? 'lessons' : params.get('tab') === 'schedule' ? 'schedule' : 'summary';
  const lessonsUrl = `${location.pathname}?tab=lessons`;
  const scheduleUrl = `${location.pathname}?tab=schedule`;
  return <AppShell role="Student" preview={false} context={<CourseScheduleAside courseId={courseId} description={description} client={client}/> }>
    <Breadcrumbs items={[{ label: 'Мои курсы', to: '/courses' }, { label: title }]}/>
    <div className="workspace-content student-study student-course-dashboard">
      <PageHeader title={title}/>
      <nav className="student-course-tabs" aria-label="Разделы курса">
        <Link to={location.pathname} aria-current={tab === 'summary' ? 'page' : undefined}>Сводка</Link>
        <Link to={lessonsUrl} aria-current={tab === 'lessons' ? 'page' : undefined}>Занятия</Link>
        <Link to={scheduleUrl} aria-current={tab === 'schedule' ? 'page' : undefined}>Расписание</Link>
      </nav>
      {tab === 'schedule' ? <CourseScheduleCalendar courseId={courseId} client={client}/> :
        tab === 'lessons' ? <Lessons rows={rows} courseId={courseId}/> :
        <Summary rows={rows} assignments={assignments} courseId={courseId} lessonsUrl={lessonsUrl} client={client}/>}
    </div>
  </AppShell>;
}
