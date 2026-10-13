import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApiError, type ApiClient } from '../../api/client';
import { decodeContent, contentUiPath, type Content, type ContentKind } from '../../api/contracts/workspaceContent';
import { AppShell } from '../../layout/AppShell';
import { Breadcrumbs, Button, EmptyState, ErrorState, LoadingSkeleton, PageHeader } from '../../components/primitives';
import { MarkdownContent } from '../../components/MarkdownContent';

type State = { phase: 'loading' } | { phase: 'success'; data: Content } | { phase: 'error'; error: ApiError };
const names = { course: 'Курс', lesson: 'Занятие', problem: 'Задача' };
/** Read-only mode of the existing pages. No invented progress, permissions or assignment identity. */
export function ContentPage({ kind, path, client }: { kind: ContentKind; path: string; client: ApiClient }) {
  const [state, setState] = useState<State>({ phase: 'loading' });
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    setState({ phase: 'loading' });
    void client.request(path.slice('/api/v1'.length), decodeContent(kind, path), { signal: controller.signal }).then(data => {
      if (!controller.signal.aborted) setState({ phase: 'success', data });
    }).catch((error: unknown) => {
      if (!controller.signal.aborted) setState({ phase: 'error', error: error instanceof ApiError ? error : new ApiError(0, 'RequestFailed') });
    });
    return () => controller.abort();
  }, [path, kind, client, attempt]);
  // Full document restoration must re-read too; never treat a bfcache snapshot as current authority.
  useEffect(() => {
    const restored = (event: PageTransitionEvent) => { if (event.persisted) { setState({ phase: 'loading' }); setAttempt(n => n + 1); } };
    window.addEventListener('pageshow', restored);
    return () => window.removeEventListener('pageshow', restored);
  }, []);
  const data = state.phase === 'success' ? state.data : null;
  const title = data?.title || names[kind];
  useEffect(() => { document.title = `${title} · Sapientum`; }, [title]);
  const crumbs: { label: string; to?: string }[] = [{ label: 'Мои курсы', to: '/courses' }];
  if (data && 'courseId' in data) crumbs.push({ label: 'Курс', to: `/courses/${encodeURIComponent(data.courseId)}` });
  if (data && 'lessonId' in data) crumbs.push({ label: 'Занятие', to: contentUiPath(path.slice(0, path.lastIndexOf('/problems/'))) });
  crumbs.push({ label: title });
  const links = data && ('lessons' in data ? data.lessons : 'problems' in data ? data.problems : null);
  return <AppShell role={null} preview={false} context={null}>
    <Breadcrumbs items={crumbs} />
    <div className="workspace-content" aria-busy={state.phase === 'loading'}>
      <PageHeader title={title} description={data && 'description' in data ? data.description ?? undefined : undefined} />
      {state.phase === 'loading' && <LoadingSkeleton label="Загрузка материалов" />}
      {state.phase === 'error' && <ErrorState title={state.error.status === 401 ? 'Войдите, чтобы открыть материалы'
        : state.error.status === 403 ? 'Нет доступа к материалам' : 'Не удалось загрузить материалы'}
        description={state.error.status === 401 ? 'Сессия завершилась или недоступна. Требуется повторный вход.' : 'Проверьте доступ и соединение.'}
        onRetry={[401, 403, 404].includes(state.error.status) ? undefined : () => { setState({ phase: 'loading' }); setAttempt(n => n + 1); }} />}
      {data && 'summary' in data && data.summary && <MarkdownContent>{data.summary}</MarkdownContent>}
      {links && <section><h2>{kind === 'course' ? 'Занятия' : 'Задачи'}</h2>
        {links.length ? <ol className="workspace-list">{links.map(item => <li key={`${item.versionId}/${item.id}`}>
          <Link to={contentUiPath(item.href)}><span className="row-number">{item.order}</span>
            <span className="row-text"><strong>{item.title || `${kind === 'course' ? 'Занятие' : 'Задача'} ${item.order}`}</strong></span><span aria-hidden="true">→</span></Link>
        </li>)}</ol> : <EmptyState title={kind === 'course' ? 'В курсе пока нет доступных занятий' : 'В занятии пока нет задач'} />}</section>}
      {data && 'statementMarkdown' in data && <section><h2>Условие задачи</h2><MarkdownContent>{data.statementMarkdown}</MarkdownContent></section>}
      {data && kind !== 'course' && <p><Link to={contentUiPath(path) + '/works'}>{kind === 'problem' ? 'Моя работа / доступные работы' : 'Работы учеников'}</Link></p>}
      {data && <Button variant="secondary" onClick={() => { setState({ phase: 'loading' }); setAttempt(n => n + 1); }}>Обновить материалы</Button>}
    </div>
  </AppShell>;
}
