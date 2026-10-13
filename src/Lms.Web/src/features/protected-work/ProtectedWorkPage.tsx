import './protectedWork.css';
import { ProtectedPhotos } from './ProtectedPhotos';
import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApiError, type ApiClient } from '../../api/client';
import { decodeWork, type ProtectedWork, type WorkList } from '../../api/contracts/protectedWork';
import { AppShell } from '../../layout/AppShell';
import { Breadcrumbs, Button, EmptyState, ErrorState, LoadingSkeleton, PageHeader } from '../../components/primitives';
import { MarkdownContent } from '../../components/MarkdownContent';

type State = { phase: 'loading' } | { phase: 'error'; status: number } | { phase: 'success'; data: ProtectedWork | WorkList };
export function ProtectedWorkPage({ path, detail, client }: { path: string; detail: boolean; client: ApiClient }) {
  const [state, setState] = useState<State>({ phase: 'loading' });
  const [reload, setReload] = useState(0);
  const [selected, setSelected] = useState<string>();
  const refresh = () => { setState({ phase: 'loading' }); setReload(n => n + 1); };
  useEffect(() => {
    const controller = new AbortController(); setState({ phase: 'loading' }); setSelected(undefined);
    void client.request(path.slice('/api/v1'.length), decodeWork(path, detail), { signal: controller.signal }).then(data => {
      if (!controller.signal.aborted) setState({ phase: 'success', data });
    }).catch((e: unknown) => { if (!controller.signal.aborted) setState({ phase: 'error', status: e instanceof ApiError ? e.status : 0 }); });
    return () => controller.abort();
  }, [client, path, detail, reload]);
  useEffect(() => {
    const restore = (e: PageTransitionEvent) => { if (e.persisted) refresh(); };
    window.addEventListener('pageshow', restore); return () => window.removeEventListener('pageshow', restore);
  }, []);
  const data = state.phase === 'success' ? state.data : null;
  const work = data && 'attempts' in data ? data : null;
  const attempt = work?.attempts.find(a => a.id === selected) ?? work?.attempts.at(-1);
  const lesson = path.split('/problems/')[0]!.split('/works')[0]!.slice('/api/v1'.length);
  return <AppShell role={null} preview={false} context={null}>
    <Breadcrumbs items={[{ label: 'Мои курсы', to: '/courses' }, { label: 'Занятие', to: lesson }, { label: detail ? 'Работа' : 'Доступные работы' }]} />
    <div className="workspace-content protected-work" aria-busy={state.phase === 'loading'}>
      <PageHeader title={detail ? 'Работа' : 'Доступные работы'} />
      {state.phase === 'loading' && <LoadingSkeleton label="Загрузка работы" />}
      {state.phase === 'error' && <ErrorState title={state.status === 401 ? 'Войдите, чтобы открыть работу' : state.status === 403 ? 'Нет доступа к работе' : 'Не удалось загрузить работу'}
        description="Проверьте доступ и соединение." onRetry={[401, 403, 404].includes(state.status) ? undefined : refresh} />}
      {data && 'items' in data && (data.items.length ? <ol className="workspace-list">{data.items.map((row, i) => <li key={row.id}>
        <Link to={row.href.slice('/api/v1'.length)}>Работа {i + 1} · {row.problemId}</Link></li>)}</ol> : <EmptyState title="Доступных работ пока нет" />)}
      {work && <>
        <Link to={work.statementHref.slice('/api/v1'.length)}>Условие задачи</Link>
        <section><h2>История попыток</h2>
          {work.attempts.length ? <><label htmlFor="work-attempt">Попытка</label><select id="work-attempt" value={attempt?.id} onChange={e => setSelected(e.target.value)}>
            {work.attempts.map(a => <option key={a.id} value={a.id}>Попытка {a.number}</option>)}</select>
            {attempt && <article key={attempt.id}><h3>Попытка {attempt.number}</h3>
              {attempt.bodyMarkdown && <MarkdownContent>{attempt.bodyMarkdown}</MarkdownContent>}
              <h3>Фотографии</h3>{attempt.photoCount ? attempt.photoAvailability === 'protected' ? <ProtectedPhotos key={attempt.id} path={`${path}/submissions/${attempt.id}/photos`} client={client} /> : <p>{`Фотографии (${attempt.photoCount}) сейчас недоступны.`}</p> : <p>В этой попытке нет фотографий.</p>}
              <h3>Замечания к попытке</h3>{attempt.reviews.length ? attempt.reviews.map(r => <div key={r.id}>
                <p>{r.decision === 'Accepted' ? 'Принято' : 'Требуется доработка'}</p>{r.feedbackMarkdown && <MarkdownContent>{r.feedbackMarkdown}</MarkdownContent>}</div>) : <p>Замечаний пока нет.</p>}
            </article>}</> : <EmptyState title="Попыток пока нет" />}
        </section>
        <section><h2>История сообщений</h2>{work.messages.map(m => <article key={m.id}><p>{m.role === 'Teacher' ? 'Преподаватель' : 'Ученик'}</p>
          <MarkdownContent>{m.bodyMarkdown}</MarkdownContent>{m.photoCount > 0 && <p>Фотографии сообщения сейчас недоступны.</p>}</article>)}</section>
      </>}
      {data && <Button variant="secondary" onClick={refresh}>Обновить работу</Button>}
    </div>
  </AppShell>;
}
