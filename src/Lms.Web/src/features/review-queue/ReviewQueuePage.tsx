import { useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import type { ApiClient } from '../../api/client';
import type { ReviewQueue } from '../../api/contracts/reviewQueue';
import { AppShell, ContextSection } from '../../layout/AppShell';
import { Breadcrumbs, Button, EmptyState, ErrorState, LoadingSkeleton, PageHeader } from '../../components/primitives';
import { StatusBadge } from '../../components/StatusBadge';
import { normalizeQueueQuery, useReviewQueue, uuidPattern } from './query';
import './queue.css';

export function waitingTime(seconds: number) {
  const minutes = Math.floor(Math.max(0, seconds) / 60);
  if (minutes < 1) return 'меньше минуты';
  if (minutes < 60) return `${minutes} мин`;
  const hours = Math.floor(minutes / 60);
  return hours < 24 ? `${hours} ч ${minutes % 60} мин` : `${Math.floor(hours / 24)} д ${hours % 24} ч`;
}
const fields = [['courseId', 'Курс · ID'], ['groupId', 'Группа · ID'], ['studentId', 'Ученик · UUID']] as const;
function Filters({ query, apply, reset }: { query: URLSearchParams; apply: (value: URLSearchParams) => void; reset: () => void }) {
  return <form className="queue-filters" aria-label="Фильтры очереди" onSubmit={event => {
    event.preventDefault();
    const values = new FormData(event.currentTarget);
    const next = new URLSearchParams();
    values.forEach((value, key) => next.set(key, String(value)));
    next.set('pageSize', query.get('pageSize')!); next.set('page', '1'); apply(next);
  }}>
    <label className="queue-search">Поиск<input name="search" type="search" maxLength={200} defaultValue={query.get('search') ?? ''}
      placeholder="Задача, курс, урок или UUID ученика" aria-describedby="queue-search-help" /></label>
    <p id="queue-search-help" className="muted small queue-search">Поиск по названиям, именам учеников и ID.</p>
    {fields.map(([key, label]) => <label key={key}>{label}<input name={key} defaultValue={query.get(key) ?? ''}
      pattern={key === 'studentId' ? uuidPattern : undefined} /></label>)}
    <label>Попытки<select aria-label="Попытки" name="attemptKind" defaultValue={query.get('attemptKind') ?? 'all'}>
      <option value="all">Все</option><option value="first">Первые попытки</option><option value="revision">После доработки</option>
    </select></label>
    <div className="queue-filter-actions"><Button type="submit">Применить</Button><Button variant="secondary" onClick={reset}>Сбросить фильтры</Button></div>
  </form>;
}
function QueueContext({ data, query }: { data: ReviewQueue; query: URLSearchParams }) {
  const summary = data.summary;
  return <>
    <ContextSection title="Ожидают проверки"><p className="queue-total">{summary.total}</p>
      <p>Первые попытки: {summary.firstAttempts}</p><p>После доработки: {summary.revisions}</p>
      {summary.averageWaitingSeconds !== null && <p>Среднее ожидание: {waitingTime(summary.averageWaitingSeconds)}</p>}
      <p className="muted small">По текущим фильтрам, на всех страницах.</p>
    </ContextSection>
    {summary.byCourse.length > 0 && <ContextSection title="По курсам"><ul className="queue-breakdown">
      {summary.byCourse.map(course => <li key={course.courseId}><span>{course.courseTitle?.trim() || course.courseId}</span><strong>{course.count}</strong></li>)}
    </ul></ContextSection>}
    <ContextSection title="Активные фильтры"><ul className="queue-active">
      {fields.map(([key, label]) => query.has(key) && <li key={key}>{label}: {query.get(key)}</li>)}
      {query.has('search') && <li>Поиск: {query.get('search')}</li>}
      <li>Попытки: {query.get('attemptKind') === 'first' ? 'Первые' : query.get('attemptKind') === 'revision' ? 'После доработки' : 'Все'}</li>
    </ul></ContextSection>
  </>;
}
export function ReviewQueuePage({ client }: { client: ApiClient }) {
  const resultsRef = useRef<HTMLElement>(null);
  const focusResults = useRef(false);
  const [resetVersion, setResetVersion] = useState(0);
  const [params, setParams] = useSearchParams();
  const query = normalizeQueueQuery(params);
  const key = query.toString();
  const { state, retry } = useReviewQueue(client, key);
  useEffect(() => { document.title = 'Проверка работ · Sapientum'; }, []);
  useEffect(() => { if (params.toString() !== key) setParams(key, { replace: true }); }, [params, key, setParams]);
  useEffect(() => {
    if (state.phase !== 'loading' && focusResults.current) { resultsRef.current?.focus(); focusResults.current = false; }
  }, [state]);
  const apply = (next: URLSearchParams) => { focusResults.current = true; setParams(normalizeQueueQuery(next)); };
  const reset = () => { setResetVersion(value => value + 1); apply(new URLSearchParams({ pageSize: query.get('pageSize')! })); };
  const page = Number(query.get('page'));
  const filtered = ['courseId', 'groupId', 'studentId', 'attemptKind', 'search'].some(name => query.has(name));
  const data = state.phase === 'success' ? state.data : null;
  const error = state.phase === 'error' ? state.error : null;
  const move = (target: number) => { const next = new URLSearchParams(query); next.set('page', String(target)); apply(next); };
  return <AppShell role={data ? 'Teacher' : null} preview={false} context={state.phase === 'loading'
    ? <ContextSection title="Статистика очереди"><LoadingSkeleton label="Загрузка контекста очереди" /></ContextSection>
    : data ? <QueueContext data={data} query={query} /> : null}>
    <Breadcrumbs items={[{ label: 'Главная', to: '/' }, { label: 'Проверка работ' }]} />
    <div className="workspace-content queue-workspace">
      <PageHeader title="Проверка работ" description="Сначала работы, которые дольше ожидают проверки." />
      <Filters key={`${key}:${resetVersion}`} query={query} apply={apply} reset={reset} />
      <section ref={resultsRef} tabIndex={-1} aria-label="Работы на проверку" aria-busy={state.phase === 'loading'} className="queue-results">
        {state.phase === 'loading' && <><LoadingSkeleton label="Загрузка сводки очереди" rows={2} /><LoadingSkeleton label="Загрузка работ" rows={6} /></>}
        {error && <ErrorState title={error.status === 401 ? 'Войдите, чтобы открыть очередь' : error.status === 403 ? 'Нет доступа к очереди' : error.status === 404 ? 'Очередь не найдена' : 'Не удалось загрузить очередь'}
          description={error.status === 403 ? 'Проверка работ доступна преподавателям.' : error.status === 401 ? 'Войдите в учебную систему повторно.' : 'Не удалось получить данные. Проверьте соединение и повторите попытку.'}
          onRetry={[401, 403, 404].includes(error.status) ? undefined : () => { focusResults.current = true; retry(); }} />}
        {data && <>
          <p role="status">Найдено работ: {data.total} · Страница {data.page}</p>
          {data.items.length === 0 ? <EmptyState title={data.total > 0 ? 'На этой странице работ нет' : filtered ? 'По выбранным фильтрам работ нет' : 'Все работы проверены'}
            action={data.total > 0 ? <Button onClick={() => move(1)}>На первую страницу</Button> : filtered ? <Button onClick={reset}>Сбросить фильтры</Button> : undefined} />
            : <ol className="queue-list">{data.items.map(item => {
              const student = item.studentDisplayName?.trim() || `Ученик ${item.studentId}`;
              const problem = item.problemTitle?.trim() || `Задача ${item.problemId}`;
              return <li key={item.problemWorkId} className="queue-row"><div className="queue-row-content">
                <p className="queue-student">{student}</p><h2>{problem}</h2>
                <p className="muted">{item.courseTitle?.trim() || item.courseId} · {item.lessonTitle?.trim() || item.lessonId}
                  {item.groupId && <> · {item.groupTitle?.trim() || item.groupId}</>}</p>
                <div className="queue-metadata"><strong>Попытка №{item.attemptNumber}</strong>
                  <StatusBadge status="Submitted" />
                  <span>{item.attachmentCount === 1 ? '1 страница' : `${item.attachmentCount} страниц`}</span>
                  <time dateTime={item.submittedAt}>{new Intl.DateTimeFormat('ru', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(item.submittedAt))}</time>
                  <span>Ожидает {waitingTime(item.waitingDurationSeconds)}</span></div>
              </div><Link className="button button--primary" aria-label={`Проверить: ${student} — ${problem}`}
                to={`/teacher/reviews/${encodeURIComponent(item.problemWorkId)}`} state={{ queueSearch: `?${key}` }}>Проверить</Link></li>;
            })}</ol>}
          <nav className="queue-pagination" aria-label="Страницы очереди">
            <Button variant="secondary" disabled={page <= 1} onClick={() => move(page - 1)}>Предыдущая</Button>
            <span>Страница {data.page} из {Math.max(1, Math.ceil(data.total / data.pageSize))}</span>
            <Button variant="secondary" disabled={page * data.pageSize >= data.total} onClick={() => move(page + 1)}>Следующая</Button>
            <label>Работ на странице<input type="number" min="1" max="200" key={key} defaultValue={data.pageSize} onBlur={event => {
              if (!event.currentTarget.validity.valid || !event.currentTarget.value) { event.currentTarget.value = String(data.pageSize); return; }
              if (Number(event.currentTarget.value) === data.pageSize) return;
              const next = new URLSearchParams(query); next.set('pageSize', event.currentTarget.value); next.set('page', '1'); apply(next);
            }} /></label>
          </nav>
        </>}
      </section>
    </div>
  </AppShell>;
}
