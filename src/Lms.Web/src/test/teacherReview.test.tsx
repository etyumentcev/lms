import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, useLocation, useNavigate } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { ApiError, createApiClient, type ApiClient, type RequestOptions } from '../api/client';
import { decodeProblemWork, type ProblemWorkDetails } from '../api/contracts/problemWork';
import lesson from './fixtures/lesson-student.json';
const uuid = (n: number) => `70000000-0000-0000-0000-${String(n).padStart(12, '0')}`;
const id = uuid(200), path = `/teacher/reviews/${id}`;
function fixture(): ProblemWorkDetails {
  const submissions = [2, 1].map(n => ({ id: uuid(n), attemptNumber: n, bodyMarkdown: `Пояснение попытки ${n}`, submittedAt: `2026-10-0${n}T10:00:00Z`, attachmentIds: [uuid(n * 10), uuid(n * 10 + 1)] }));
  return { id, problemAssignmentId: lesson.problems[0]!.problemAssignmentId, status: 'Submitted', version: 3, attemptCount: 2,
    student: { id: uuid(100), displayName: null, avatarUrl: null }, course: structuredClone(lesson.course), lesson: structuredClone(lesson.lesson),
    problem: { ...lesson.problems[0]!.problem, statementMarkdown: 'Историческое условие A' }, currentSubmission: submissions[0]!, submissions,
    reviews: [{ id: uuid(50), submissionId: uuid(1), teacherId: uuid(101), decision: 'NeedsRevision', messageId: uuid(30), createdAt: '2026-10-01T11:00:00Z' }],
    messages: [{ id: uuid(30), author: { id: uuid(101), displayName: null, avatarUrl: null }, authorRole: 'Teacher', bodyMarkdown: 'Исправьте второй переход.\nОбоснуйте вывод.', replyToMessageId: null, createdAt: '2026-10-01T11:00:00Z', attachmentIds: [] }],
    permissions: { canSubmit: false, canPostMessage: true, canAccept: true, canRequestRevision: true } };
}
function decided(decision: 'Accepted' | 'NeedsRevision', feedback: string | null = null) {
  const data = fixture(); data.status = decision; data.version = 4; data.permissions.canAccept = data.permissions.canRequestRevision = false;
  data.reviews.unshift({ id: uuid(51), submissionId: uuid(2), teacherId: uuid(101), decision, messageId: feedback ? uuid(31) : null, createdAt: '2026-10-02T11:00:00Z' });
  if (feedback) data.messages.push({ ...data.messages[0]!, id: uuid(31), bodyMarkdown: feedback });
  return data;
}
type Handler = (url: string, options: RequestOptions) => unknown | Promise<unknown>;
function Navigation() { const navigate = useNavigate(), location = useLocation(); return <><button onClick={() => navigate(`/teacher/reviews/${uuid(201)}`)}>Другая работа</button><output aria-label="URL">{location.pathname}{location.search}</output></>; }
function mount(data = fixture(), handler?: Handler, queueSearch?: string) {
  const request = vi.fn(async (url: string, decode: (value: unknown) => unknown, options: RequestOptions = {}) => decode(handler ? await handler(url, options) : content(url, data)));
  return { ...render(<MemoryRouter initialEntries={[{ pathname: path, state: { queueSearch } }]} future={{ v7_startTransition: true, v7_relativeSplatPath: true }}><Navigation /><App client={{ request } as ApiClient} /></MemoryRouter>), request };
}
const emptyReference = { contentVersionId: uuid(300), versionNumber: 1, courseId: 'olympiad-7', lessonId: 'lesson-2', problemId: 'problem-1', materials: [], assets: [] };
const content = (url: string, data: ProblemWorkDetails) => url.endsWith('/content') ? new Blob(['photo']) : url.endsWith('/reference') ? emptyReference : data;
const loaded = () => screen.findByRole('heading', { name: 'Попытка №2' });
const accept = () => screen.getByRole('button', { name: 'Принять решение' });
const revise = () => screen.getByRole('button', { name: 'Вернуть на доработку' });
const editor = () => screen.getByRole('textbox', { name: 'Комментарий преподавателя' });
const posts = (request: ReturnType<typeof mount>['request']) => request.mock.calls.filter(([, , options]) => options?.method === 'POST');
async function archive() { fireEvent.click(screen.getByText('Попытка №1 · Нужна доработка')); await userEvent.click(screen.getByRole('button', { name: 'Посмотреть попытку №1' })); }
beforeEach(() => { URL.createObjectURL = vi.fn(() => 'blob:protected-original'); URL.revokeObjectURL = vi.fn(); });

describe('J1 teacher read workspace', () => {
  it('keeps shell and separate main/context loading representations', () => { mount(fixture(), () => new Promise(() => {})); expect(screen.getByRole('status', { name: 'Загрузка решения' })).toBeVisible(); expect(screen.getByRole('status', { name: 'Загрузка контекста проверки' })).toBeVisible(); expect(screen.getByRole('main')).toBeVisible(); });
  it('reads exact ProblemWorkId, decodes real DTO and keeps immutable statement visible', async () => { const { request } = mount(); await loaded(); expect(request.mock.calls[0]![0]).toBe(`/teacher/problem-works/${id}`); expect(request.mock.calls[0]![2]!.signal).toBeInstanceOf(AbortSignal); expect(screen.getByRole('region', { name: 'Условие задачи' })).toHaveTextContent('Историческое условие A'); expect(decodeProblemWork(fixture()).id).toBe(id); });
  it('shows server current submission, attempt number, timestamp and multiline body', async () => { const data = fixture(); data.currentSubmission!.bodyMarkdown = 'Первый абзац\n\nВторой абзац'; mount(data); await loaded(); const view = screen.getByRole('article', { name: 'Просмотр попытки №2' }); expect(view).toHaveTextContent('Первый абзац'); expect(view).toHaveTextContent('Второй абзац'); expect(view.querySelector('time')).toHaveAttribute('datetime', data.currentSubmission!.submittedAt); });
  it('does not reconstruct currentSubmission from submissions array', async () => { const data = fixture(); data.currentSubmission = null; mount(data); await screen.findByText('Отправленных попыток нет'); expect(screen.queryByRole('textbox', { name: 'Комментарий преподавателя' })).toBeNull(); });
  it('shows a single attachment and disables paging at both boundaries', async () => { const data = fixture(); data.currentSubmission!.attachmentIds = [uuid(20)]; mount(data); await loaded(); await screen.findByRole('img'); expect(screen.getByText('Страница 1 из 1')).toBeVisible(); expect(screen.getByRole('button', { name: 'Предыдущая' })).toBeDisabled(); expect(screen.getByRole('button', { name: 'Следующая' })).toBeDisabled(); });
  it('loads only current page in original attachment order', async () => { const { request } = mount(); await screen.findByRole('img'); await userEvent.click(screen.getByRole('button', { name: 'Следующая' })); await screen.findByAltText('Страница 2 — фото решения'); expect(request.mock.calls.filter(([url]) => url.endsWith('/content')).map(([url]) => url)).toEqual([`/attachments/${uuid(20)}/content`, `/attachments/${uuid(21)}/content`]); expect(screen.getByText('Страница 2 из 2')).toBeVisible(); });
  it('uses protected blob original links without public filesystem URLs', async () => { mount(); const link = await screen.findByRole('link', { name: 'Открыть страницу 1 в полном размере (новая вкладка)' }); expect(link).toHaveAttribute('href', 'blob:protected-original'); expect(link).toHaveAttribute('rel', 'noopener noreferrer'); });
  it('enlarges, reduces and resets photo without altering original', async () => { mount(); await loaded(); await userEvent.click(screen.getByRole('button', { name: 'Увеличить фото' })); expect(screen.getByLabelText('Масштаб')).toHaveTextContent('150%'); expect(document.querySelector('.annotation-image')).toHaveStyle({ transform: 'translate(0px, 0px) scale(1.5)' }); await userEvent.click(screen.getByRole('button', { name: 'Уменьшить фото' })); expect(screen.getByLabelText('Масштаб')).toHaveTextContent('100%'); await userEvent.click(screen.getByRole('button', { name: 'Увеличить фото' })); await userEvent.click(screen.getByRole('button', { name: 'Вписать страницу' })); expect(screen.getByLabelText('Масштаб')).toHaveTextContent('100%'); });
  it('keyboard page navigation preserves feedback and uses bounded pages', async () => { mount(); await loaded(); await userEvent.type(editor(), 'Черновик'); const photo = screen.getByRole('region', { name: 'Просмотр фото' }); photo.focus(); await userEvent.keyboard('{Alt>}{ArrowRight}{/Alt}'); expect(screen.getByText('Страница 2 из 2')).toBeVisible(); await waitFor(() => expect(screen.getByRole('region', { name: 'Просмотр фото' })).toHaveFocus()); await userEvent.keyboard('{Alt>}{ArrowLeft}{/Alt}'); expect(screen.getByText('Страница 1 из 2')).toBeVisible(); expect(editor()).toHaveValue('Черновик'); });
  it('previous attempt opens its own photos and focuses viewer; return retains draft', async () => { const { request } = mount(); await loaded(); await userEvent.type(editor(), 'Draft'); await archive(); expect(screen.getByRole('region', { name: 'Отправленное решение' })).toHaveFocus(); expect(screen.getByRole('article', { name: 'Просмотр попытки №1' })).toHaveTextContent('Пояснение попытки 1'); expect(request.mock.calls.some(([url]) => url === `/attachments/${uuid(10)}/content`)).toBe(true); expect(screen.queryByRole('button', { name: 'Принять решение' })).toBeNull(); await userEvent.click(screen.getByRole('button', { name: 'К текущей попытке' })); expect(editor()).toHaveValue('Draft'); });
  it('last actionable feedback is joined by messageId and displayed exactly once', async () => { const data = fixture(); data.messages.push({ ...data.messages[0]!, id: uuid(99), bodyMarkdown: 'Постороннее сообщение', createdAt: '2026-10-03T00:00:00Z' }); mount(data); await loaded(); expect(screen.getAllByText(/Исправьте второй переход/)).toHaveLength(1); expect(screen.getByRole('complementary')).toHaveTextContent('Обоснуйте вывод'); expect(screen.queryByText('Постороннее сообщение')).toBeNull(); });
  it('chooses latest revision by attempt even if review array order differs', async () => { const data = fixture(); data.currentSubmission = { ...data.currentSubmission!, id: uuid(3), attemptNumber: 3 }; data.submissions.unshift(data.currentSubmission); data.attemptCount = 3; data.reviews[0]!.submissionId = uuid(2); data.reviews.unshift({ ...data.reviews[0]!, id: uuid(55), submissionId: uuid(1), messageId: uuid(35) }); data.messages.push({ ...data.messages[0]!, id: uuid(35), bodyMarkdown: 'Старое замечание' }); mount(data); await screen.findByRole('heading', { name: 'Попытка №3' }); expect(screen.getByRole('region', { name: 'Последнее замечание' })).toHaveTextContent('Исправьте второй переход'); });
  it('omits unsourced reference solution, criteria and materials', async () => { mount(); await loaded(); for (const name of ['Эталонное решение', 'Критерии оценки', 'Материалы']) expect(screen.queryByRole('heading', { name })).toBeNull(); });
  it('loads exact teacher projection separately and keeps the student photo workspace mounted', async () => {
    const { request } = mount(fixture(), (url) => url.endsWith('/reference') ? { ...emptyReference, materials: [
      { id:'solution-1',kind:'solution',title:null,order:1,targetKind:'problem',targetId:'problem-1',bodyMarkdown:'Эталон версии v1' },
      { id:'rubric-1',kind:'rubric',title:null,order:2,targetKind:'problem',targetId:'problem-1',bodyMarkdown:'Идея — 3 балла' },
    ] } : content(url,fixture()));
    await loaded(); expect(await screen.findByText('Эталон версии v1')).toBeVisible();
    expect(screen.getByText('Неизменяемая версия v1')).toBeVisible(); expect(screen.getByRole('region',{name:'Просмотр фото'})).toBeVisible();
    expect(request.mock.calls.some(([url])=>url===`/teacher/problem-works/${id}/reference`)).toBe(true);
  });
  it('cannot display undeclared teacher-only fields injected outside C4 contract', async () => { mount({ ...fixture(), referenceSolution: 'SECRET SOLUTION', criteria: 'SECRET CRITERIA' } as ProblemWorkDetails); await loaded(); expect(screen.queryByText(/SECRET/)).toBeNull(); });
  it('uses supplied student name and real course/lesson data in ContextPanel', async () => { const data = fixture(); data.student.displayName = 'Анна'; mount(data); await loaded(); const context = screen.getByRole('complementary'); expect(context).toHaveTextContent('Анна'); expect(context).toHaveTextContent(data.course.title!); expect(context).toHaveTextContent('Всего попыток: 2'); });
  it('preserves mobile DOM order, semantic landmarks and labelled actions', async () => { mount(); await loaded(); expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1); expect(screen.getByRole('main').compareDocumentPosition(screen.getByRole('complementary')) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy(); expect(editor()).toHaveAccessibleDescription(/Для доработки/); expect(screen.getByRole('navigation', { name: 'Страницы решения' })).toBeVisible(); });
  it.each(['Accepted', 'NeedsRevision'] as const)('%s review is read-only even with inconsistent enabled capabilities', async status => { const data = decided(status); data.permissions.canAccept = data.permissions.canRequestRevision = true; mount(data); await loaded(); expect(screen.queryByRole('textbox', { name: 'Комментарий преподавателя' })).toBeNull(); expect(screen.getByRole('textbox', { name: 'Текст сообщения' })).toBeVisible(); expect(screen.queryByRole('button', { name: 'Принять решение' })).toBeNull(); expect(screen.getByRole('region', { name: 'Результат проверки' })).toHaveTextContent(status === 'Accepted' ? 'Принята' : 'Нужна доработка'); });
  it('uses distinct server capabilities independently', async () => { const data = fixture(); data.permissions.canAccept = false; mount(data); await loaded(); expect(screen.queryByRole('button', { name: 'Принять решение' })).toBeNull(); expect(revise()).toBeEnabled(); });
  it('work with no review capabilities has no review editor', async () => { const data = fixture(); data.permissions.canAccept = data.permissions.canRequestRevision = false; mount(data); await loaded(); expect(screen.queryByRole('textbox', { name: 'Комментарий преподавателя' })).toBeNull(); expect(screen.getByRole('textbox', { name: 'Текст сообщения' })).toBeVisible(); expect(screen.queryByRole('link', { name: 'Проверка' })).toBeNull(); });
  it('keeps unsafe Markdown inert', async () => { const data = fixture(); data.problem.statementMarkdown = '<script>alert(1)</script>\n[bad](javascript:alert%281%29)'; mount(data); await loaded(); expect(document.querySelector('script,a[href^="javascript:"]')).toBeNull(); });
});

describe('J1 command invariants', () => {
  it.each(['', '   \n\t'])('rejects blank revision feedback %j and focuses labelled editor', async value => { const { request } = mount(); await loaded(); fireEvent.change(editor(), { target: { value } }); await userEvent.click(revise()); expect(screen.getByRole('alert')).toHaveTextContent('Укажите, что ученику нужно исправить'); expect(editor()).toHaveFocus(); expect(editor()).toHaveAttribute('aria-invalid', 'true'); expect(posts(request)).toHaveLength(0); });
  it.each([['accept', 'Accepted'], ['needs-revision', 'NeedsRevision']] as const)('sends exact %s payload, waits for authoritative GET and removes review actions', async (action, status) => {
    let sent = false; const { request } = mount(fixture(), (url, options) => { if (options.method === 'POST') { sent = true; return {}; } return content(url, sent ? decided(status, action === 'accept' ? null : 'Первая строка\nВторая строка') : fixture()); });
    await loaded(); if (action === 'needs-revision') fireEvent.change(editor(), { target: { value: 'Первая строка\nВторая строка' } }); await userEvent.click(action === 'accept' ? accept() : revise()); await screen.findByText('Проверка сохранена. Состояние работы обновлено.');
    expect(posts(request)).toHaveLength(1); expect(posts(request)[0]![0]).toBe(`/problem-works/${id}/reviews/${action}`);
    expect(posts(request)[0]![2]!.body).toMatchObject({ submissionId: uuid(2), expectedVersion: 3,
      feedback: action === 'accept' ? null : 'Первая строка\nВторая строка' });
    expect((posts(request)[0]![2]!.body as { commandId: string }).commandId).toMatch(/^[\da-f-]{36}$/i);
    expect(posts(request)[0]![2]!.csrf).toBe(true);
    expect(request.mock.calls.filter(([url]) => url === `/teacher/problem-works/${id}`)).toHaveLength(2); expect(screen.queryByRole('button', { name: 'Принять решение' })).toBeNull();
  });
  it('preserves nonblank optional acceptance feedback', async () => { const { request } = mount(fixture(), (url, options) => options?.method === 'POST' ? new Promise(() => {}) : content(url, fixture())); await loaded(); await userEvent.type(editor(), 'Верно'); await userEvent.click(accept()); expect(posts(request)[0]![2]!.body).toMatchObject({ feedback: 'Верно' }); });
  it('normalizes whitespace acceptance feedback to null', async () => { const { request } = mount(fixture(), (url, options) => options?.method === 'POST' ? new Promise(() => {}) : content(url, fixture())); await loaded(); fireEvent.change(editor(), { target: { value: ' \n ' } }); await userEvent.click(accept()); expect(posts(request)[0]![2]!.body).toMatchObject({ feedback: null }); });
  it('blocks double click and both decisions during pending without optimistic success', async () => { const { request } = mount(fixture(), (url, options) => options?.method === 'POST' ? new Promise(() => {}) : content(url, fixture())); await loaded(); const yes = accept(), no = revise(); fireEvent.click(yes); fireEvent.click(yes); fireEvent.click(no); await waitFor(() => expect(posts(request)).toHaveLength(1)); expect(yes).toBeDisabled(); expect(no).toBeDisabled(); expect(editor()).toBeDisabled(); expect(screen.queryByText(/Проверка сохранена/)).toBeNull(); expect(screen.getByText('Сохранение проверки. Дождитесь ответа сервера.')).toBeVisible(); });
  it('pending lock survives opening and closing historical attempt', async () => { const { request } = mount(fixture(), (url, options) => options?.method === 'POST' ? new Promise(() => {}) : content(url, fixture())); await loaded(); fireEvent.click(accept()); await archive(); await userEvent.click(screen.getByRole('button', { name: 'К текущей попытке' })); expect(screen.getByRole('button', { name: 'Сохранение…' })).toBeDisabled(); expect(posts(request)).toHaveLength(1); });
  it('keeps draft through explicit concurrency refresh and reads new version', async () => { let conflict = true; const { request } = mount(fixture(), (url, options) => { if (options.method === 'POST') throw new ApiError(409, 'ConcurrencyConflict'); const data = fixture(); if (!conflict) data.version = 5; return content(url, data); }); await loaded(); await userEvent.type(editor(), 'Мой комментарий'); await userEvent.click(revise()); expect(await screen.findByRole('alert')).toHaveTextContent('Работа уже изменилась'); expect(accept()).toBeDisabled(); expect(editor()).toHaveValue('Мой комментарий'); expect(posts(request)).toHaveLength(1); conflict = false; await userEvent.click(screen.getByRole('button', { name: 'Обновить работу' })); await loaded(); expect(editor()).toHaveValue('Мой комментарий'); expect(screen.getByRole('main')).toHaveFocus(); await userEvent.click(revise()); expect(posts(request)[1]![2]!.body).toMatchObject({ expectedVersion: 5 }); });
  it.each([[422, 'FeedbackRequired'], [400, 'ValidationFailed']] as const)('allows corrected input after %s %s', async (status, code) => { const { request } = mount(fixture(), (url, options) => { if (options.method === 'POST') throw new ApiError(status, code); return content(url, fixture()); }); await loaded(); await userEvent.type(editor(), 'Text'); await userEvent.click(revise()); expect(await screen.findByRole('alert')).toBeVisible(); expect(editor()).toHaveFocus(); await userEvent.type(editor(), ' fixed'); expect(accept()).toBeEnabled(); expect(posts(request)).toHaveLength(1); });
  it.each([[0, 'NetworkError'], [500, 'InternalError'], [401, 'Unauthenticated'], [403, 'Forbidden'], [404, 'NotFound'], [409, 'WorkNotSubmitted']] as const)('handles command %s with explicit refresh, preserved draft and no retry', async (status, code) => { const { request } = mount(fixture(), (url, options) => { if (options.method === 'POST') throw new ApiError(status, code); return content(url, fixture()); }); await loaded(); await userEvent.type(editor(), 'Сохранить текст'); await userEvent.click(accept()); await screen.findByRole('alert'); expect(accept()).toBeDisabled(); expect(revise()).toBeDisabled(); expect(editor()).toHaveValue('Сохранить текст'); expect(screen.getByRole('button', { name: 'Обновить работу' })).toBeVisible(); expect(posts(request)).toHaveLength(1); });
  it('failed authoritative refresh retries only GET after acknowledged command', async () => { let reads = 0, fail = true; const { request } = mount(fixture(), (url, options) => { if (options.method === 'POST') return {}; if (url.endsWith('/content')) return new Blob(); if (++reads > 1 && fail) throw new ApiError(500, 'InternalError'); return reads > 1 ? decided('Accepted') : fixture(); }); await loaded(); await userEvent.click(accept()); await screen.findByRole('alert'); expect(screen.queryByRole('button', { name: 'Принять решение' })).toBeNull(); fail = false; await userEvent.click(screen.getByRole('button', { name: 'Повторить' })); await screen.findByText('Проверка сохранена. Состояние работы обновлено.'); expect(posts(request)).toHaveLength(1); });
  it('does not invent Accepted when authoritative state is a newer Submitted attempt', async () => { let sent = false; const next = fixture(); next.version = 6; next.currentSubmission = { ...next.currentSubmission!, id: uuid(3), attemptNumber: 3 }; const { request } = mount(fixture(), (url, options) => { if (options.method === 'POST') { sent = true; return { status: 'Accepted' }; } return content(url, sent ? next : fixture()); }); await loaded(); await userEvent.click(accept()); await screen.findByRole('heading', { name: 'Попытка №3' }); expect(accept()).toBeEnabled(); expect(posts(request)).toHaveLength(1); });
});

describe('J1 errors, identity and navigation', () => {
  it.each([401, 403, 404])('handles GET %s without exposing old content', async status => { mount(fixture(), () => { throw new ApiError(status, 'NotFound'); }); await screen.findByRole('alert'); expect(screen.queryByText('Историческое условие A')).toBeNull(); expect(screen.queryByRole('button', { name: 'Повторить' })).toBeNull(); });
  it.each([0, 500, 503])('retries GET %s explicitly', async status => { let fail = true; const { request } = mount(fixture(), url => { if (fail) throw new ApiError(status, status === 503 ? 'ContentUnavailable' : 'RequestFailed'); return content(url, fixture()); }); await screen.findByRole('alert'); expect(request).toHaveBeenCalledOnce(); fail = false; await userEvent.click(screen.getByRole('button', { name: 'Повторить' })); await loaded(); });
  it('rejects mismatched work identity', async () => { mount({ ...fixture(), id: uuid(999) }); await screen.findByRole('alert'); expect(screen.queryByRole('textbox')).toBeNull(); });
  it('cancels pending GET on unmount', () => { const { request, unmount } = mount(fixture(), () => new Promise(() => {})); unmount(); expect(request.mock.calls[0]![2]!.signal?.aborted).toBe(true); });
  it('ignores stale response after route identity changes', async () => { let resolve!: (value: unknown) => void; const { request } = mount(fixture(), url => url === `/teacher/problem-works/${id}` ? new Promise(r => { resolve = r; }) : content(url, { ...fixture(), id: uuid(201), problem: { ...fixture().problem, title: 'Другая задача' } })); await userEvent.click(screen.getByRole('button', { name: 'Другая работа' })); await screen.findByRole('heading', { name: 'Другая задача' }); await act(async () => resolve(fixture())); expect(request.mock.calls[0]![2]!.signal?.aborted).toBe(true); expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Другая задача'); });
  it('does not carry draft or late command result into another work', async () => { let resolve!: (value: unknown) => void; mount(fixture(), (url, options) => options?.method === 'POST' ? new Promise(r => { resolve = r; }) : content(url, { ...fixture(), id: url.includes(uuid(201)) ? uuid(201) : id })); await loaded(); await userEvent.type(editor(), 'Old draft'); await userEvent.click(accept()); await userEvent.click(screen.getByRole('button', { name: 'Другая работа' })); await loaded(); await act(async () => resolve({})); expect(editor()).toHaveValue(''); expect(screen.queryByText(/Проверка сохранена/)).toBeNull(); });
  it('returns to queue with normalized original URL filters', async () => { mount(fixture(), undefined, '?courseId=math&attemptKind=revision&page=2&pageSize=10'); await loaded(); expect(screen.getByRole('link', { name: 'К очереди проверок' })).toHaveAttribute('href', '/teacher/reviews?courseId=math&attemptKind=revision&page=2&pageSize=10'); });
  it('direct work link returns to base queue', async () => { mount(); await loaded(); expect(screen.getByRole('link', { name: 'К очереди проверок' })).toHaveAttribute('href', '/teacher/reviews'); });
  it('safe integer version and required capabilities are enforced', () => { expect(() => decodeProblemWork({ ...fixture(), version: Number.MAX_SAFE_INTEGER + 1 })).toThrow(); expect(() => decodeProblemWork({ ...fixture(), permissions: {} })).toThrow(); });
  it('real client transports bearer on protected photo GET', async () => { const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue({ ok: true, status: 200, blob: async () => new Blob(['photo']) } as Response); const client = createApiClient({ fetch, getAccessToken: () => 'test-token' }); const { attachmentApi } = await import('../components/attachments/api'); await attachmentApi(client).content(uuid(20), new AbortController().signal); expect(fetch.mock.calls[0]![0]).toBe(`/api/v1/attachments/${uuid(20)}/content`); expect(new Headers(fetch.mock.calls[0]![1]?.headers).get('Authorization')).toBe('Bearer test-token'); });
  it('revokes photo URLs when changing page and unmounting', async () => { const { unmount } = mount(); await screen.findByRole('img'); await userEvent.click(screen.getByRole('button', { name: 'Следующая' })); await screen.findByAltText('Страница 2 — фото решения'); expect(URL.revokeObjectURL).toHaveBeenCalledOnce(); unmount(); expect(URL.revokeObjectURL).toHaveBeenCalledTimes(2); });
});

describe('J2 annotation integration', () => {
  async function drawAnnotation() {
    await screen.findByAltText('Страница 1 — фото решения');
    Object.defineProperties(screen.getByAltText('Страница 1 — фото решения'), { naturalWidth: { value: 100, configurable: true }, naturalHeight: { value: 100, configurable: true } });
    fireEvent.load(screen.getByAltText('Страница 1 — фото решения'));
    const layer = screen.getByLabelText('Слой пометок страницы 1');
    vi.spyOn(layer, 'getBoundingClientRect').mockReturnValue({ left: 0, top: 0, width: 100, height: 100 } as DOMRect);
    const event = (name: string, x: number) => {
      const e = new MouseEvent(name, { bubbles: true, clientX: x, clientY: 30, button: 0 });
      Object.defineProperties(e, { pointerId: { value: 1 }, pointerType: { value: 'pen' }, pressure: { value: .7 } });
      fireEvent(layer, e);
    };
    event('pointerdown', 20); event('pointermove', 40); event('pointerup', 40);
    expect(screen.getByText(/Графические пометки: 1/)).toBeVisible();
  }
  it.each(['accept', 'needs-revision'] as const)('sends %s with normalized current attachment strokes atomically', async decision => {
    const data = fixture(); const { request } = mount(data, (url, options) => options.method === 'POST' ? {} : content(url, data));
    await drawAnnotation(); await userEvent.type(editor(), 'Комментарий'); await userEvent.click(decision === 'accept' ? accept() : revise());
    const body = posts(request)[0]![2]!.body as { annotations: { attachmentId: string; strokes: { points: { x: number; pressure: number }[] }[] }[] };
    expect(body.annotations[0]!.attachmentId).toBe(uuid(20)); expect(body.annotations[0]!.strokes[0]!.points[0]).toMatchObject({ x: .2, pressure: .7 });
    await waitFor(() => expect(screen.queryByText(/Есть несохранённые графические/)).toBeNull());
  });
  it.each([409, 500])('preserves strokes after failed command %s and never retries', async status => {
    const data = fixture(); const { request } = mount(data, (url, options) => { if (options.method === 'POST') throw new ApiError(status, status === 409 ? 'ConcurrencyConflict' : 'InternalError'); return content(url, data); });
    await drawAnnotation(); await userEvent.click(accept()); expect(screen.getByText(/Графические пометки: 1/)).toBeVisible(); expect(accept()).toBeDisabled(); expect(posts(request)).toHaveLength(1);
  });
  it('warns on SPA navigation and browser reload while retaining rejected navigation draft', async () => {
    mount(); await drawAnnotation(); const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    await userEvent.click(screen.getByText('Другая работа')); expect(confirm).toHaveBeenCalled(); expect(screen.getByLabelText('URL')).toHaveTextContent(path);
    const e = new Event('beforeunload', { cancelable: true }); window.dispatchEvent(e); expect(e.defaultPrevented).toBe(true);
    expect(screen.getByText(/Графические пометки: 1/)).toBeVisible(); confirm.mockRestore();
  });
  it('historical view does not move draft strokes to the old attempt', async () => {
    mount(); await drawAnnotation(); await archive(); expect(screen.getByText(/Графические пометки: 0/)).toBeVisible(); expect(screen.queryByText('Перо')).toBeNull();
    await userEvent.click(screen.getByText('К текущей попытке')); expect(screen.getByText(/Графические пометки: 1/)).toBeVisible();
  });
  it('completed review renders only strokes matching its attachment', async () => {
    const data = decided('Accepted'); data.reviews[0]!.annotations = [{ attachmentId: uuid(999), strokes: [{ id: uuid(90), tool: 'pen', color: '#dc2626', width: .003, points: [{ x: .2, y: .3 }] }] }];
    mount(data); await loaded(); expect(screen.getByText(/Графические пометки: 0/)).toBeVisible(); expect(screen.queryByText('Перо')).toBeNull();
  });
});


describe('MVP selected attempt feedback', () => {
  it('shows historical feedback with its photo and restores only the current outcome', async () => {
    const data = decided('Accepted', 'Исправление принято');
    data.messages.find(m => m.id === uuid(30))!.attachmentIds = [uuid(300)];
    const { request } = mount(data);
    await loaded();
    expect(screen.getByRole('region', { name: 'Результат проверки' })).toHaveTextContent('Исправление принято');
    await archive();
    const outcome = screen.getByRole('region', { name: 'Результат проверки' });
    expect(outcome).toHaveTextContent('Результат проверки попытки №1');
    expect(outcome).toHaveTextContent('Исправьте второй переход.');
    expect(outcome).not.toHaveTextContent('Исправление принято');
    await within(outcome).findByRole('img');
    expect(request.mock.calls.some(([url]) => url === `/attachments/${uuid(300)}/content`)).toBe(true);
    expect(screen.queryByRole('textbox')).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'К текущей попытке' }));
    expect(screen.getByRole('region', { name: 'Результат проверки' })).toHaveTextContent('Исправление принято');
    expect(screen.queryByRole('region', { name: 'Фотографии замечания' })).toBeNull();
    expect(posts(request)).toHaveLength(0);
  });
  it('keeps historical feedback visible when its protected photo is denied', async () => {
    const data = fixture(); data.messages[0]!.attachmentIds = [uuid(300)];
    mount(data, (url) => { if (url === `/attachments/${uuid(300)}/content`) throw new ApiError(404, 'NotFound'); return content(url, data); });
    await loaded(); await archive();
    const outcome = screen.getByRole('region', { name: 'Результат проверки' });
    expect(await within(outcome).findByRole('alert')).toHaveTextContent('Изображение не найдено или недоступно');
    expect(outcome).toHaveTextContent('Исправьте второй переход.');
    expect(within(outcome).queryByRole('img')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Принять решение' })).toBeNull();
  });
});

describe('Course navigation history context', () => {
  it('opens explicit history for accepted work, selects attempt and returns with course breadcrumbs', async () => {
    const data = decided('Accepted'); data.permissions.canViewAsTeacher = true; mount(data); await loaded();
    await userEvent.click(screen.getByRole('link', { name: 'История работы' }));
    const history = screen.getByRole('region', { name: 'История работы' });
    await userEvent.click(within(history).getByRole('link', { name: 'Попытка №1' }));
    expect(screen.getByLabelText('URL')).toHaveTextContent(`?attempt=${uuid(1)}`);
    expect(screen.getByRole('heading', { name: 'Попытка №1' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'К текущей попытке' }));
    await loaded(); expect(screen.queryByRole('button', { name: 'Принять решение' })).toBeNull();
    expect(screen.getByRole('link', { name: 'Назад к занятию' })).toHaveAttribute('href', `/courses/${data.course.id}/lessons/${data.lesson.id}`);
    const crumbs = screen.getByRole('navigation', { name: 'Хлебные крошки' });
    expect(within(crumbs).getByRole('link', { name: data.course.title! })).toHaveAttribute('href', `/courses/${data.course.id}`);
  });
  it('restores selected historical attempt with browser back and forward', async () => {
    function BrowserButtons() { const nav = useNavigate(); return <><button onClick={() => nav(-1)}>Browser back</button><button onClick={() => nav(1)}>Browser forward</button></>; }
    const data = fixture();
    const request = vi.fn(async (_url: string, decode: (v: unknown) => unknown) => decode(data));
    data.submissions.forEach(s => { s.attachmentIds = []; });
    render(<MemoryRouter initialEntries={[path]} future={{ v7_startTransition: true, v7_relativeSplatPath: true }}><BrowserButtons /><App client={{ request } as ApiClient} /></MemoryRouter>);
    await loaded(); await archive(); expect(screen.getByRole('heading', { name: 'Попытка №1' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Browser back' })); await loaded();
    await userEvent.click(screen.getByRole('button', { name: 'Browser forward' }));
    expect(screen.getByRole('heading', { name: 'Попытка №1' })).toBeInTheDocument();
  });
});
