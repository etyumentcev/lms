import { useEffect, useRef, useState } from 'react';
import { Link, useLocation, useSearchParams } from 'react-router-dom';
import type { ApiClient } from '../../api/client';
import type { ProgressAssignment, ProgressSummary, StudentProgress, TeacherProgress } from '../../api/contracts/progressHistory';
import { AppShell, ContextSection } from '../../layout/AppShell';
import type { NavigationRole } from '../../layout/SidebarNavigation';
import { Breadcrumbs, Button, EmptyState, ErrorState, LoadingSkeleton, PageHeader } from '../../components/primitives';
import { StatusBadge } from '../../components/StatusBadge';
import { normalizeProgressQuery, useProgress, useReviewHistory } from './query';
import './progress.css';

const labels = { NotStarted: 'Не начата', Submitted: 'На проверке',
  NeedsRevision: 'Нужна доработка', Accepted: 'Принята' } as const;
function Summary({ value }: { value: ProgressSummary }) {
  return <><ContextSection title="Сводка"><p><strong>{value.studentCount}</strong> учеников</p>
    <p><strong>{value.assignedCount}</strong> назначенных задач</p></ContextSection>
    <ContextSection title="Статусы"><ul className="progress-counts">
      {Object.entries(labels).map(([key, label]) => <li key={key}><span>{label}</span>
        <strong>{value[key.charAt(0).toLowerCase() + key.slice(1) as keyof ProgressSummary]}</strong></li>)}
    </ul></ContextSection></>;
}
function Filters({ source, role, apply, reset }: { source: URLSearchParams; role: NavigationRole;
  apply: (value: URLSearchParams) => void; reset: () => void }) {
  return <form className="progress-filters" aria-label="Фильтры прогресса" onSubmit={event => {
    event.preventDefault(); const data = new FormData(event.currentTarget); const next = new URLSearchParams();
    data.forEach((value, key) => { if (String(value)) next.set(key, String(value)); });
    next.set('pageSize', source.get('pageSize')!); next.set('page', '1'); apply(next);
  }}>
    <label>Курс · ID<input name="courseId" defaultValue={source.get('courseId') ?? ''}/></label>
    {role === 'Teacher' && <><label>Группа · ID<input name="groupId" defaultValue={source.get('groupId') ?? ''}/></label>
      <label>Ученик · UUID<input name="studentId" defaultValue={source.get('studentId') ?? ''}/></label></>}
    <label>Статус<select name="status" defaultValue={source.get('status') ?? ''}><option value="">Все статусы</option>
      {Object.entries(labels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
    <div><Button type="submit">Применить</Button> <Button variant="secondary" type="button" onClick={reset}>Сбросить</Button></div>
  </form>;
}
function Task({ item, role }: { item: ProgressAssignment; role: NavigationRole }) {
  const target = role === 'Teacher' && item.problemWorkId ? `/teacher/reviews/${encodeURIComponent(item.problemWorkId)}`
    : `/problem-assignments/${encodeURIComponent(item.assignmentId)}?tab=history`;
  return <li className="progress-task"><div><h3>{item.problemTitle?.trim() || item.problemId}</h3>
    <p className="muted">{item.lessonTitle?.trim() || item.lessonId} · версия {item.courseVersionId}</p>
    <div className="inline-group"><StatusBadge status={item.status}/><span>Попыток: {item.attemptCount}</span>
      {!item.isActive && <span>Назначение завершено</span>}</div></div>
    {(item.problemWorkId || role === 'Student') && <Link className="text-link" to={target}
      state={role === 'Teacher' ? { returnTo: window.location.pathname + window.location.search,
        returnLabel: 'К прогрессу группы' } : undefined}>Открыть задачу и историю</Link>}</li>;
}
export function ProgressPage({ client, role }: { client: ApiClient; role: NavigationRole }) {
  const [params, setParams] = useSearchParams(); const [resetKey, setResetKey] = useState(0);
  const query = normalizeProgressQuery(params); const key = query.toString();
  const { state, retry } = useProgress(client, role, key); const result = useRef<HTMLElement>(null);
  const apply = (next: URLSearchParams) => setParams(normalizeProgressQuery(next));
  const reset = () => { setResetKey(value => value + 1); apply(new URLSearchParams({ pageSize: query.get('pageSize')! })); };
  useEffect(() => { document.title = `Прогресс · Sapientum`; }, []);
  const data = state.phase === 'success' ? state.data : null;
  return <AppShell role={role} preview={false} context={data ? <Summary value={data.summary}/>
    : <ContextSection title="Сводка"><LoadingSkeleton label="Загрузка сводки"/></ContextSection>}>
    <Breadcrumbs items={[{ label: 'Главная', to: '/' }, { label: 'Прогресс' }]}/>
    <div className="workspace-content progress-workspace"><PageHeader title={role === 'Teacher' ? 'Прогресс группы' : 'Мой прогресс'}
      description="Статусы и история берутся из текущего серверного состояния."/>
      {role === 'Teacher' && <nav className="progress-tabs" aria-label="Прогресс и история">
        <Link aria-current="page" to={{ pathname: '/progress', search: params.toString() }}>Прогресс группы</Link>
        <Link to="/teacher/review-history">Мои проверки</Link></nav>}
      <Filters key={resetKey} source={query} role={role} apply={apply} reset={reset}/>
      <section ref={result} tabIndex={-1} aria-label="Результаты прогресса" aria-busy={state.phase === 'loading'}>
        {state.phase === 'loading' && <LoadingSkeleton label="Загрузка прогресса" rows={7}/>}
        {state.phase === 'error' && <ErrorState title={state.error.status === 403 ? 'Нет доступа к прогрессу' : 'Не удалось загрузить прогресс'}
          description="Повтор выполняет только безопасное чтение актуального состояния." onRetry={state.error.status === 403 ? undefined : retry}/>}
        {data && <ProgressResults data={data} role={role}/>}
      </section>
    </div></AppShell>;
}
function ProgressResults({ data, role }: { data: TeacherProgress | StudentProgress; role: NavigationRole }) {
  if (data.items.length === 0) return <EmptyState title="По выбранным фильтрам данных нет"/>;
  if (role === 'Student') return <ol className="progress-list">{(data as StudentProgress).items.map(item =>
    <Task key={item.assignmentId} item={item} role={role}/>)}</ol>;
  return <ol className="progress-list">{(data as TeacherProgress).items.map(row => <li key={`${row.courseId}:${row.groupId}:${row.studentId}`}
    className="progress-student"><header><h2>{row.studentDisplayName?.trim() || `Ученик ${row.studentId}`}</h2>
      <p>{row.courseTitle?.trim() || row.courseId}{row.groupId && <> · {row.groupTitle?.trim() || row.groupId}</>} · назначено {row.assignedCount}</p></header>
    {row.assignments.length ? <ol>{row.assignments.map(item => <Task key={item.assignmentId} item={item} role={role}/>)}</ol>
      : <p className="muted">В выбранном статусе задач нет.</p>}</li>)}</ol>;
}

export function ReviewHistoryPage({ client }: { client: ApiClient }) {
  const [params, setParams] = useSearchParams(); const location = useLocation();
  const query = normalizeProgressQuery(params, true); const key = query.toString();
  const { state, retry } = useReviewHistory(client, key); const data = state.phase === 'success' ? state.data : null;
  const apply = (next: URLSearchParams) => setParams(normalizeProgressQuery(next, true));
  return <AppShell role="Teacher" preview={false} context={<ContextSection title="Мои проверки">
    {data ? <><p><strong>{data.summary.total}</strong> всего</p><p>Доработка: {data.summary.needsRevision}</p>
      <p>Принято: {data.summary.accepted}</p></> : <LoadingSkeleton label="Загрузка статистики"/>}</ContextSection>}>
    <Breadcrumbs items={[{ label: 'Главная', to: '/' }, { label: 'Мои проверки' }]}/>
    <div className="workspace-content progress-workspace"><PageHeader title="Мои проверки"
      description="Включая завершённые проверки и принятые решения."/>
      <nav className="progress-tabs" aria-label="Прогресс и история"><Link to="/progress">Прогресс группы</Link>
        <Link aria-current="page" to={{ pathname: location.pathname, search: params.toString() }}>Мои проверки</Link></nav>
      <form className="progress-filters" aria-label="Фильтры истории проверок" onSubmit={event => {
        event.preventDefault(); const values = new FormData(event.currentTarget); const next = new URLSearchParams();
        values.forEach((value, name) => { if (String(value)) next.set(name, String(value)); });
        next.set('page', '1'); next.set('pageSize', query.get('pageSize')!); apply(next);
      }}><label>Курс · ID<input name="courseId" defaultValue={query.get('courseId') ?? ''}/></label>
        <label>Группа · ID<input name="groupId" defaultValue={query.get('groupId') ?? ''}/></label>
        <label>Ученик · UUID<input name="studentId" defaultValue={query.get('studentId') ?? ''}/></label>
        <label>Решение<select name="decision" defaultValue={query.get('decision') ?? ''}><option value="">Все решения</option>
          <option value="NeedsRevision">Нужна доработка</option><option value="Accepted">Принята</option></select></label>
        <Button type="submit">Применить</Button></form>
      {state.phase === 'loading' && <LoadingSkeleton label="Загрузка истории проверок" rows={7}/>}
      {state.phase === 'error' && <ErrorState title={state.error.status === 403 ? 'Нет доступа к истории' : 'Не удалось загрузить историю'}
        description="Повтор выполняет только чтение." onRetry={state.error.status === 403 ? undefined : retry}/>}
      {data && (data.items.length ? <ol className="progress-list">{data.items.map(item => <li key={item.reviewId} className="progress-task">
        <div><h2>{item.studentDisplayName?.trim() || item.studentId} · {item.problemTitle?.trim() || item.problemId}</h2>
          <p className="muted">{item.courseTitle?.trim() || item.courseId} · {item.lessonTitle?.trim() || item.lessonId}
            {item.groupId && <> · {item.groupTitle?.trim() || item.groupId}</>}</p>
          <div className="inline-group"><StatusBadge status={item.decision}/><span>Попытка №{item.attemptNumber}</span>
            <span>Фотографий: {item.attachmentCount}</span></div></div>
        <Link className="text-link" to={`/teacher/reviews/${encodeURIComponent(item.problemWorkId)}?attempt=${encodeURIComponent(item.submissionId)}`}
          state={{ returnTo: location.pathname + location.search, returnLabel: 'К истории проверок' }}>Открыть прошлую проверку</Link>
      </li>)}</ol> : <EmptyState title="По выбранным фильтрам проверок нет"/>)}
    </div></AppShell>;
}
