import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, useNavigate, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { ApiError, type ApiClient, type RequestOptions } from '../api/client';
import { decodeProblemWorkspace, type ProblemWorkspace } from '../api/contracts/problemWorkspace';
import lesson from './fixtures/lesson-student.json';
const id = lesson.problems[0]!.problemAssignmentId;
const path = `/problem-assignments/${id}`;
function fixture(status: 'NotStarted' | 'Submitted' | 'NeedsRevision' | 'Accepted' = 'NotStarted'): ProblemWorkspace {
  const row = lesson.problems[0]!;
  const data = { assignment: { id, isActive: true }, course: lesson.course, lesson: lesson.lesson,
    problem: { ...row.problem, statementMarkdown: 'Найдите **остаток** при делении $n^2$ на 3.' }, availability: row.availability,
    work: structuredClone(row.work), permissions: { ...row.permissions }, hints: [], usefulMaterials: [], history: { submissions: [], reviews: [], messages: [] } } as ProblemWorkspace;
  data.work.status = status;
  if (status !== 'NotStarted') {
    data.work.problemWorkId = '50000000-0000-0000-0000-000000000001'; data.work.version = 7; data.work.attemptCount = 1;
    data.work.latestSubmission = { id: 's1', attemptNumber: 1, bodyMarkdown: 'Моё решение', attachmentIds: ['60000000-0000-0000-0000-000000000001'], submittedAt: '2026-10-08T10:00:00Z' };
    data.permissions.canSubmit = status === 'NeedsRevision';
    if (status !== 'Submitted') data.work.latestTeacherFeedback = { id: 'm1', author: { id: 't1', displayName: null, avatarUrl: null }, authorRole: 'Teacher', bodyMarkdown: 'Объясните второй переход.', replyToMessageId: null, createdAt: '2026-10-08T11:00:00Z', attachmentIds: [] };
  }
  return data;
}
const uploaded = (n = 1) => ({ attachmentId: `60000000-0000-0000-0000-${String(n).padStart(12, '0')}`, fileName: `Лист ${n}.png`, mediaType: 'image/png', size: 100, createdAt: '2026-10-08T10:00:00Z', previewUrl: `/api/v1/attachments/60000000-0000-0000-0000-${String(n).padStart(12, '0')}/content` });
type Handler = (path: string, options: RequestOptions) => unknown | Promise<unknown>;
function Navigation() { const nav = useNavigate(); const location = useLocation(); return <><button onClick={() => nav(-1)}>Назад</button><button onClick={() => nav(1)}>Вперёд</button><output data-testid="location">{location.pathname}{location.search}</output></>; }
function mount(data = fixture(), handler?: Handler, search = '') {
  let uploadCount = 0;
  const request = vi.fn(async (url: string, decode: (value: unknown) => unknown, options: RequestOptions = {}) => {
    const value = handler ? await handler(url, options) : url === '/uploads' ? uploaded(++uploadCount) : url.endsWith('/content') ? new Blob(['png'], { type: 'image/png' }) : data;
    return decode(value);
  });
  const client = { request } as ApiClient;
  return { ...render(<MemoryRouter initialEntries={[path + search]} future={{ v7_startTransition: true, v7_relativeSplatPath: true }}><Navigation /><App client={client} /></MemoryRouter>), request };
}
const loaded = () => screen.findByRole('heading', { level: 1, name: 'Задача 1' });
const submit = () => screen.getByRole('button', { name: 'Отправить решение' });
const editor = () => screen.getByRole('textbox', { name: 'Пояснение к решению — необязательно' });
const upload = async (names = ['Лист 1.png']) => userEvent.upload(screen.getByLabelText('Сфотографировать / Выбрать фото'), names.map(name => new File(['png'], name, { type: 'image/png' })));
const posts = (request: ReturnType<typeof mount>['request']) => request.mock.calls.filter(([url]) => url.endsWith('/submissions'));
beforeEach(() => { vi.stubGlobal('URL', URL); URL.createObjectURL = vi.fn(() => 'blob:photo'); URL.revokeObjectURL = vi.fn(); });

describe('G read workspace', () => {
  it('keeps shell, statement/tabs/solution/context skeletons while loading', () => {
    mount(fixture(), () => new Promise(() => {}));
    for (const name of ['Загрузка условия задачи', 'Загрузка вкладок', 'Загрузка решения', 'Загрузка контекста задачи']) expect(screen.getByRole('status', { name })).toBeInTheDocument();
    expect(screen.getByRole('main')).toBeInTheDocument(); expect(screen.getByRole('complementary')).toBeInTheDocument();
  });
  it('reads by assignment, accepts null work/version and never writes on read', async () => {
    const { request } = mount(); await loaded(); expect(request.mock.calls[0]![0]).toBe(`/problem-assignments/${id}/workspace`);
    expect(request).toHaveBeenCalledOnce(); expect(screen.getByText('Не начата')).toBeInTheDocument(); expect(submit()).toBeDisabled();
    expect(screen.getByText('Решение ещё не отправлено')).toBeInTheDocument();
  });
  it('renders semantic immutable markdown/math above tabs with one h1', async () => {
    mount(); await loaded(); const statement = screen.getByRole('region', { name: 'Условие задачи' });
    expect(statement.querySelector('strong')).toHaveTextContent('остаток'); expect(statement.querySelector('math')?.textContent).toContain('n2');
    expect(statement.compareDocumentPosition(screen.getByRole('tablist')) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy(); expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1);
  });
  it('has Router course/lesson breadcrumbs and nonlinked current problem', async () => {
    mount(); await loaded(); const nav = within(screen.getByRole('navigation', { name: 'Хлебные крошки' }));
    expect(nav.getByRole('link', { name: 'Мои курсы' })).toHaveAttribute('href', '/courses');
    expect(nav.getByRole('link', { name: lesson.course.title })).toHaveAttribute('href', '/study/courses/olympiad-7');
    expect(nav.getByRole('link', { name: lesson.lesson.title })).toHaveAttribute('href', '/study/courses/olympiad-7/versions/b5eec63a-f2d8-4b31-bca4-e21cfd1d9baa/lessons/lesson-2');
    expect(nav.queryByRole('link', { name: 'Задача 1' })).not.toBeInTheDocument();
  });
  it.each([['hints','Подсказки','Подсказок пока нет'], ['history','История','Вы ещё не отправляли решение']])('supports deep link %s and keeps statement', async (tab, label, text) => {
    mount(fixture(), undefined, `?tab=${tab}`); await loaded(); expect(screen.getByRole('tab', { name: label })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByText(text)).toBeVisible(); expect(screen.getByRole('region', { name: 'Условие задачи' })).toBeVisible();
  });
  it('supports keyboard tabs with focus, query preservation and Back/Forward', async () => {
    mount(fixture(), undefined, '?source=lesson'); await loaded(); screen.getByRole('tab', { name: 'Решение' }).focus();
    await userEvent.keyboard('{ArrowRight}'); expect(screen.getByRole('tab', { name: 'Подсказки' })).toHaveFocus();
    expect(screen.getByTestId('location')).toHaveTextContent('source=lesson&tab=hints');
    await userEvent.click(screen.getByRole('button', { name: 'Назад' })); expect(screen.getByRole('tab', { name: 'Решение' })).toHaveAttribute('aria-selected', 'true');
    await userEvent.click(screen.getByRole('button', { name: 'Вперёд' })); expect(screen.getByRole('tab', { name: 'Подсказки' })).toHaveAttribute('aria-selected', 'true');
  });
  it('renders only supplied hints in server order', async () => {
    const data = fixture(); data.hints = [{ id: '2', order: 2, title: 'Второй шаг', bodyMarkdown: 'Разберите случаи.' }, { id: '1', order: 1, title: 'Первый шаг', bodyMarkdown: 'Начните с остатка.' }];
    mount(data, undefined, '?tab=hints'); await loaded(); expect(screen.getAllByRole('heading', { level: 2 }).map(n => n.textContent).slice(0,2)).toEqual(['Первый шаг','Второй шаг']);
  });
  it.each([[401,'Войдите, чтобы открыть задачу'],[403,'Нет доступа к задаче'],[404,'Задача не найдена']])('handles GET %s', async (status, title) => {
    mount(fixture(), () => { throw new ApiError(Number(status), 'Forbidden'); }); expect(await screen.findByRole('alert')).toHaveTextContent(String(title));
    expect(screen.queryByRole('button', { name: 'Повторить' })).not.toBeInTheDocument(); expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
  });
  it.each([0,500])('retries GET after failure %s', async status => {
    let n = 0; const { request } = mount(fixture(), () => { if (++n === 1) throw new ApiError(status, 'NetworkError'); return fixture(); });
    await screen.findByRole('alert'); await userEvent.click(screen.getByRole('button', { name: 'Повторить' })); await loaded(); expect(request).toHaveBeenCalledTimes(2);
  });
  it('cancels GET on unmount', async () => {
    const { request, unmount } = mount(fixture(), () => new Promise(() => {})); await waitFor(() => expect(request).toHaveBeenCalledOnce());
    const signal = request.mock.calls[0]![2]?.signal; unmount(); expect(signal?.aborted).toBe(true);
  });
  it.each(['Draft','Editing','Uploading','Reviewing','Rejected','InProgress'])('rejects noncanonical status %s', status => {
    const data = fixture(); (data.work as { status: string }).status = status; expect(() => decodeProblemWorkspace(data)).toThrow();
  });
  it('rejects identity mismatch and unsafe version', async () => {
    const data = fixture(); data.work.version = Number.MAX_SAFE_INTEGER + 1; expect(() => decodeProblemWorkspace(data)).toThrow();
    data.work.version = null; data.assignment.id = 'foreign'; mount(data); expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось загрузить задачу');
  });
  it('keeps HTML/scripts and unsafe Markdown links inert', async () => {
    const data = fixture(); data.problem.statementMarkdown = '# Heading\n\n<script>alert(1)</script>\n\n[bad](javascript:alert%281%29)\n\n![remote](https://invalid.test/image.png)'; mount(data); await loaded();
    expect(document.querySelector('main script, main img')).toBeNull(); expect(document.querySelector('a[href^="javascript:"]')).toBeNull(); expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1);
  });
});

describe('G submit and U1 integration', () => {
  it('photo-first native multiple input, optional body and accessible order controls', async () => {
    mount(); await loaded(); const input = screen.getByLabelText('Сфотографировать / Выбрать фото'); expect(input).toHaveAttribute('multiple'); expect(input).toHaveAttribute('accept','image/jpeg,image/png');
    expect(input).not.toHaveAttribute('capture'); await upload(); await screen.findByAltText('Страница 1 — фото решения'); expect(submit()).toBeEnabled(); expect(editor()).toHaveValue('');
    expect(screen.getByRole('button', { name: 'Удалить страницу 1' })).toBeEnabled(); expect(screen.getByRole('link', { name: /Открыть страницу 1/ })).toHaveAttribute('href','blob:photo');
  });
  it.each([1,2])('submits %s photos in visual order with null optional body', async count => {
    let n = 0; let sent = false; const next = fixture('Submitted'); const { request } = mount(fixture(), (url, opts) => {
      if (url === '/uploads') return uploaded(++n); if (url.endsWith('/content')) return new Blob(['png']);
      if (opts.method === 'POST') { sent = true; return {}; } return sent ? next : fixture();
    }); await loaded(); await upload(Array.from({ length: count }, (_, i) => `Лист ${i+1}.png`)); await screen.findByAltText(`Страница ${count} — фото решения`);
    if (count === 2) await userEvent.click(screen.getByRole('button', { name: 'Страница 2: переместить выше' }));
    await userEvent.click(submit()); await screen.findByText('Решение отправлено и ожидает проверки преподавателем.');
    expect(posts(request)[0]![2]?.body).toEqual({ bodyMarkdown: null, expectedVersion: null, attachmentIds: count === 1 ? [uploaded(1).attachmentId] : [uploaded(2).attachmentId,uploaded(1).attachmentId] });
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
  });
  it('allows text only; protects double click and waits for authoritative count/version/context', async () => {
    let complete!: () => void; let sent = false; const next = fixture('Submitted'); next.work.attemptCount = 3; next.work.latestSubmission!.attemptNumber = 3;
    const { request } = mount(fixture(), async (url, opts) => {
      if (opts.method === 'POST') { await new Promise<void>(r => { complete = r; }); sent = true; return {}; }
      return url.endsWith('/content') ? new Blob(['png']) : sent ? next : fixture();
    }); await loaded(); await userEvent.type(editor(), 'Текст решения'); const button = submit(); fireEvent.click(button); fireEvent.click(button);
    expect(posts(request)).toHaveLength(1); expect(screen.getByRole('button', { name: 'Отправка…' })).toBeDisabled(); expect(screen.getByText('Не начата')).toBeInTheDocument();
    await act(async () => complete()); await screen.findByText('Попыток: 3'); expect(screen.getByText('Последняя попытка №3')).toBeInTheDocument(); expect(screen.getByRole('main')).toHaveFocus();
    expect(posts(request)[0]![2]?.body).toEqual({ bodyMarkdown: 'Текст решения', attachmentIds: [], expectedVersion: null });
  });
  it('disables submit during unfinished uploads even when body exists', async () => {
    mount(fixture(), url => url === '/uploads' ? new Promise(() => {}) : fixture()); await loaded(); await userEvent.type(editor(), 'Текст'); await upload();
    expect(submit()).toBeDisabled(); expect(screen.getByText('Не начата')).toBeInTheDocument();
  });
  it('preserves uploaded pages and text across tabs without delete or reupload', async () => {
    const { request } = mount(); await loaded(); await upload(); await screen.findByAltText('Страница 1 — фото решения'); await userEvent.type(editor(), 'Пояснение');
    await userEvent.click(screen.getByRole('tab', { name: 'Подсказки' })); await userEvent.click(screen.getByRole('tab', { name: 'История' })); await userEvent.click(screen.getByRole('tab', { name: 'Решение' }));
    expect(editor()).toHaveValue('Пояснение'); expect(screen.getByAltText('Страница 1 — фото решения')).toBeVisible();
    expect(request.mock.calls.filter(([url]) => url === '/uploads')).toHaveLength(1); expect(request.mock.calls.some(([, , opts]) => opts?.method === 'DELETE')).toBe(false);
  });
  it('uses U1 delete for a removed pending page', async () => {
    const { request } = mount(); await loaded(); await upload(); await screen.findByAltText('Страница 1 — фото решения'); await userEvent.click(screen.getByRole('button', { name: 'Удалить страницу 1' }));
    await waitFor(() => expect(screen.queryByAltText('Страница 1 — фото решения')).not.toBeInTheDocument()); expect(submit()).toBeDisabled();
    expect(request.mock.calls.some(([url,,opts]) => url === `/attachments/${uploaded().attachmentId}` && opts?.method === 'DELETE')).toBe(true);
  });
  it('retries failed upload only and keeps canonical state unchanged', async () => {
    let n = 0; const { request } = mount(fixture(), url => { if (url === '/uploads') { if (++n === 1) throw new ApiError(0,'NetworkError'); return uploaded(); } return url.endsWith('/content') ? new Blob(['png']) : fixture(); });
    await loaded(); await upload(); await screen.findByRole('alert'); expect(submit()).toBeDisabled(); await userEvent.click(screen.getByRole('button', { name: 'Повторить загрузку страницы 1' }));
    await screen.findByAltText('Страница 1 — фото решения'); expect(submit()).toBeEnabled(); expect(screen.getByText('Не начата')).toBeInTheDocument(); expect(posts(request)).toHaveLength(0);
  });
  it.each(['EmptySubmission','ValidationFailed','AttachmentNotOwned','InvalidAttachment','AttachmentAlreadyAttached'])('preserves draft on %s without automatic retry', async code => {
    const { request } = mount(fixture(), (_url, opts) => { if (opts.method === 'POST') throw new ApiError(422,code); return fixture(); }); await loaded(); await userEvent.type(editor(),'Текст'); await userEvent.click(submit());
    expect(await screen.findByRole('alert')).not.toHaveTextContent(code); expect(editor()).toHaveValue('Текст'); expect(posts(request)).toHaveLength(1); expect(submit()).toBeEnabled(); expect(screen.getByRole('alert').parentElement).toHaveFocus();
  });
  it.each(['ConcurrencyConflict','NetworkError','InternalError','WorkAlreadySubmitted'])('locks %s until explicit authoritative refresh, no command retry', async code => {
    let after = false; const { request } = mount(fixture(), (url, opts) => { if (opts.method === 'POST') { after = true; throw new ApiError(code === 'ConcurrencyConflict' ? 409 : 0, code); } return url.endsWith('/content') ? new Blob(['png']) : after ? fixture('Submitted') : fixture(); });
    await loaded(); await userEvent.type(editor(),'Текст'); await userEvent.click(submit()); await screen.findByRole('alert'); expect(submit()).toBeDisabled(); expect(editor()).toHaveValue('Текст'); expect(posts(request)).toHaveLength(1);
    await userEvent.click(screen.getByRole('button', { name: 'Обновить состояние' })); await screen.findByText('Решение отправлено и ожидает проверки преподавателем.'); expect(posts(request)).toHaveLength(1);
  });
  it('after acknowledged POST and failed refresh retries only GET', async () => {
    let sent = false; let failed = false; const { request } = mount(fixture(), (url, opts) => {
      if (opts.method === 'POST') { sent = true; return {}; } if (sent && !failed) { failed = true; throw new ApiError(500,'InternalError'); }
      return url.endsWith('/content') ? new Blob(['png']) : sent ? fixture('Submitted') : fixture();
    }); await loaded(); await userEvent.type(editor(),'Текст'); await userEvent.click(submit()); await screen.findByRole('alert'); expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Повторить' })); await screen.findByText('Решение отправлено и ожидает проверки преподавателем.'); expect(posts(request)).toHaveLength(1);
  });
  it('keeps uploaded originals and ordering after rejected Submit', async () => {
    let n = 0;
    const { request } = mount(fixture(), (url, opts) => {
      if (url === '/uploads') return uploaded(++n);
      if (url.endsWith('/content')) return new Blob(['png']);
      if (opts.method === 'POST') throw new ApiError(403, 'AttachmentNotOwned');
      return fixture();
    });
    await loaded(); await upload(['first.png','second.png']); await screen.findByAltText('Страница 2 — фото решения');
    await userEvent.click(screen.getByRole('button', { name: 'Страница 2: переместить выше' }));
    await userEvent.type(editor(), 'Пояснение'); await userEvent.click(submit()); await screen.findByRole('alert');
    expect(editor()).toHaveValue('Пояснение'); expect(screen.getByAltText('Страница 2 — фото решения')).toBeVisible();
    expect(screen.getByRole('region', { name: 'Фотографии решения' }).querySelector('li')).toHaveTextContent('second.png');
    expect(request.mock.calls.filter(([url]) => url === '/uploads')).toHaveLength(2);
    expect(request.mock.calls.some(([, , opts]) => opts?.method === 'DELETE')).toBe(false);
  });
  it('retries protected current preview through U1 content only and revokes URL on unmount', async () => {
    let calls = 0;
    const { request, unmount } = mount(fixture('Submitted'), url => {
      if (url.endsWith('/content')) { if (++calls === 1) throw new ApiError(0,'NetworkError'); return new Blob(['png']); }
      return fixture('Submitted');
    });
    await loaded(); await screen.findByRole('alert'); await userEvent.click(screen.getByRole('button', { name: 'Повторить предпросмотр страницы 1' }));
    await screen.findByAltText('Страница 1 — фото решения'); expect(calls).toBe(2); expect(posts(request)).toHaveLength(0);
    unmount(); expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:photo');
  });
  it('leaving with pending resources never DELETEs server uploads', async () => {
    const { request, unmount } = mount(); await loaded(); await upload(); await screen.findByAltText('Страница 1 — фото решения'); unmount();
    expect(request.mock.calls.some(([, , opts]) => opts?.method === 'DELETE')).toBe(false); expect(URL.revokeObjectURL).toHaveBeenCalled();
  });
});

describe('G statuses and context', () => {
  it.each(['Submitted','Accepted'] as const)('%s shows protected current submission and no form', async status => {
    mount(fixture(status)); await loaded(); expect(screen.getByText('Моё решение')).toBeVisible(); await screen.findByAltText('Страница 1 — фото решения'); expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Отправленная попытка №1' })).toBeInTheDocument();
  });
  it('NeedsRevision prominently shows projected feedback, previous context and new form', async () => {
    mount(fixture('NeedsRevision')); await loaded(); expect(screen.getByRole('heading', { name: 'Что нужно исправить' })).toBeVisible();
    expect(screen.getAllByText('Объясните второй переход.')).toHaveLength(1); expect(editor()).toHaveValue(''); expect(screen.getByText('Моё решение')).toBeInTheDocument();
    expect(screen.getByRole('complementary')).toHaveTextContent('Есть замечание преподавателя');
  });
  it('revision submits new content with server version and updates attempt from GET', async () => {
    let sent = false; const next = fixture('Submitted'); next.work.attemptCount = 2; next.work.latestSubmission!.attemptNumber = 2;
    const { request } = mount(fixture('NeedsRevision'), (url, opts) => { if (opts.method === 'POST') { sent = true; return {}; } return url.endsWith('/content') ? new Blob(['png']) : sent ? next : fixture('NeedsRevision'); });
    await loaded(); await userEvent.type(editor(),'Исправление'); await userEvent.click(submit()); await screen.findByText('Попыток: 2');
    expect(posts(request)[0]![2]?.body).toEqual({ bodyMarkdown: 'Исправление', attachmentIds: [], expectedVersion: 7 });
  });
  it.each(['NotStarted','NeedsRevision'] as const)('canSubmit=false suppresses %s form', async status => {
    const data = fixture(status); data.permissions.canSubmit = false; mount(data); await loaded(); expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
  });
  it('Accepted remains terminal even for an inconsistent capability', async () => {
    const data = fixture('Accepted'); data.permissions.canSubmit = true; mount(data); await loaded(); expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
  });
  it('context remains one landmark after main with status and history navigation', async () => {
    mount(fixture('Submitted')); await loaded(); const context = screen.getByRole('complementary'); expect(context).toHaveTextContent('На проверке'); expect(context).toHaveTextContent('Последняя попытка №1');
    expect(screen.getAllByRole('complementary')).toHaveLength(1); expect(screen.getByRole('main').compareDocumentPosition(context) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    await userEvent.click(screen.getByRole('link', { name: 'Перейти к истории →' })); expect(await screen.findByText('История пока пуста')).toBeVisible(); expect(screen.getByRole('region', { name: 'Условие задачи' })).toBeVisible();
  });
  it('omits absent materials and renders only safe contract-supported links', async () => {
    const data = fixture(); data.usefulMaterials = [{ id: 'm', title: 'Конспект', kind: 'Document', description: null, url: '/notes' },{ id: 'bad', title: 'Опасная ссылка', kind: 'Link', description: null, url: 'javascript:alert(1)' }];
    mount(data); await loaded(); expect(within(screen.getByRole('complementary')).getByRole('link', { name: 'Конспект' })).toHaveAttribute('href','http://localhost:3000/notes'); expect(screen.queryByRole('link', { name: 'Опасная ссылка' })).not.toBeInTheDocument();
  });
  it('has no fabricated hints/materials/teacher actions/history timeline', async () => {
    mount(); await loaded(); expect(screen.queryByText('Полезные материалы')).not.toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Принять решение' })).not.toBeInTheDocument();
    expect(screen.queryByText(/Предыдущие попытки/)).not.toBeInTheDocument();
  });
});
