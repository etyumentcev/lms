import { act, render, renderHook, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { createApiClient } from '../api/client';
import { decodeCourseWorkspace } from '../api/contracts/courseWorkspace';
import { browserApiClient } from '../api/browserClient';
import { useCourseWorkspace } from '../features/course/query';
import studentFixture from './fixtures/course-student.json';

const fixture = () => decodeCourseWorkspace(structuredClone(studentFixture));
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status,
  headers: { 'Content-Type': status === 200 ? 'application/json' : 'application/problem+json' } });
const problem = (status: number, code: string) => response({ type: `urn:lms:error:${code}`, title: 'Do not parse this title',
  status, instance: '/api/v1/courses/olympiad-7/workspace', code, traceId: 'test-trace' }, status);
function mount(data = fixture(), fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(response(data)), path = '/courses/olympiad-7') {
  const onUnauthenticated = vi.fn();
  const getAccessToken = vi.fn().mockResolvedValue('test-only-bearer');
  const client = createApiClient({ fetch, getAccessToken, onUnauthenticated });
  const rendered = render(<MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }} initialEntries={[path]}>
    <App client={client} /></MemoryRouter>);
  return { ...rendered, fetch, getAccessToken, onUnauthenticated };
}
const loaded = () => screen.findByRole('heading', { level: 1, name: studentFixture.course.title });
afterEach(() => { vi.unstubAllGlobals(); delete window.lmsAuth; });

describe('Course route through the shared client', () => {
  it('keeps shell/main/context visible while loading header and rows', () => {
    mount(fixture(), vi.fn<typeof globalThis.fetch>().mockReturnValue(new Promise(() => {})));
    expect(screen.getByRole('status', { name: 'Загрузка уроков' })).toBeInTheDocument();
    expect(screen.getByRole('status', { name: 'Загрузка контекста курса' })).toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Основная навигация' })).toBeInTheDocument();
    expect(screen.getByRole('main')).toBeInTheDocument();
    expect(screen.getByRole('complementary')).toBeInTheDocument();
  });
  it('renders Student C4 fields via the exact authenticated GET URL', async () => {
    const { fetch } = mount(); await loaded();
    expect(fetch).toHaveBeenCalledOnce();
    const [url, options] = fetch.mock.calls[0]!;
    expect(url).toBe('/api/v1/courses/olympiad-7/workspace');
    expect(options?.method).toBe('GET');
    expect(new Headers(options?.headers).get('Authorization')).toBe('Bearer test-only-bearer');
    expect(options?.signal).toBeInstanceOf(AbortSignal);
    expect(screen.getByText('Делимость и остатки')).toBeInTheDocument();
    expect(screen.queryByText('Демо-пространство')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Текущие задачи' })).toBeInTheDocument();
  });
  it('renders Teacher enrollment=null without a fake student identity or progress', async () => {
    const data = fixture(); data.enrollment = null;
    data.teacherContext = { teacherId: '3d1358b6-e4bc-4c9a-9bda-92f07a75d407', canReviewCourse: true, groupIds: [null, 'group-a'] };
    mount(data); await loaded();
    expect(screen.getByText(/Режим преподавателя/)).toBeInTheDocument();
    expect(screen.getByText('По доступным вам работам курса и групп.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Проверка' })).toBeInTheDocument();
    expect(screen.queryByText(data.course.teacher?.displayName ?? data.teacherContext.teacherId)).not.toBeInTheDocument();
  });
  it.each([[401, 'Unauthenticated', 'Войдите, чтобы открыть курс'], [403, 'Forbidden', 'Нет доступа к курсу'],
    [404, 'NotFound', 'Курс не найден']] as const)('handles %s without retry or human-text matching', async (status, code, title) => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(problem(status, code));
    const { onUnauthenticated } = mount(fixture(), fetch);
    expect(await screen.findByRole('alert')).toHaveTextContent(title);
    expect(onUnauthenticated).toHaveBeenCalledTimes(status === 401 ? 1 : 0);
    expect(screen.queryByRole('button', { name: 'Повторить' })).not.toBeInTheDocument();
    expect(screen.getByRole('complementary')).toBeInTheDocument();
    expect(screen.queryByText(studentFixture.course.title)).not.toBeInTheDocument();
  });
  it.each(['network', 'server', 'content'] as const)('allows manual GET retry after %s failure', async kind => {
    const fetch = vi.fn<typeof globalThis.fetch>();
    if (kind === 'network') fetch.mockRejectedValueOnce(new TypeError('offline'));
    else fetch.mockResolvedValueOnce(problem(kind === 'server' ? 500 : 503, kind === 'server' ? 'InternalError' : 'ContentUnavailable'));
    fetch.mockResolvedValueOnce(response(fixture()));
    mount(fixture(), fetch);
    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось загрузить курс');
    expect(fetch).toHaveBeenCalledOnce();
    await userEvent.click(screen.getByRole('button', { name: 'Повторить' }));
    await loaded(); expect(fetch).toHaveBeenCalledTimes(2);
  });
  it('renders a valid empty course and zero available lessons without an error', async () => {
    const data = fixture(); data.lessons = []; data.progress = { totalLessons: 0, availableLessons: 0, completedLessons: 0, percent: 0 };
    mount(data); await loaded();
    expect(screen.getByRole('heading', { name: 'В курсе пока нет уроков' })).toBeInTheDocument();
    expect(screen.getByText('0 из 0 доступных уроков завершено')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
  it.each([0, 50, 100])('renders %s%% from server progress', async percent => {
    const data = fixture(); data.progress.percent = percent;
    data.progress.completedLessons = percent === 0 ? 0 : percent === 50 ? 1 : 2;
    mount(data); await loaded();
    expect(screen.getAllByRole('progressbar')).toHaveLength(1);
    expect(screen.getByRole('progressbar', { name: 'Завершение курса' })).toHaveAttribute('aria-valuenow', String(percent));
    expect(screen.getByText(`${data.progress.completedLessons} из 2 доступных уроков завершено`)).toBeInTheDocument();
  });
  it('does not recalculate authoritative progress from lesson rows', async () => {
    const data = fixture(); data.progress.percent = 33.33; data.progress.availableLessons = 3;
    mount(data); await loaded();
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '33.33');
  });
  it('navigates to the Lesson query with Router and predictable focus', async () => {
    mount(); await loaded();
    const link = screen.getByRole('link', { name: 'Открыть: Делимость и остатки' });
    expect(link).toHaveAttribute('href', '/courses/olympiad-7/lessons/lesson-2');
    link.focus(); await userEvent.keyboard('{Enter}');
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Занятие');
    expect(screen.getByRole('main')).toHaveFocus();
    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось загрузить занятие');
  });
  it('locked lessons never expose navigation/protected summaries, regardless of progress', async () => {
    const data = fixture(); const locked = data.lessons[2]!;
    locked.lesson.summary = 'Must not show protected summary'; locked.progress.status = 'Completed';
    mount(data); await loaded();
    const row = screen.getByRole('heading', { name: 'Принцип Дирихле' }).closest('li')!;
    expect(within(row).queryByRole('link')).not.toBeInTheDocument();
    expect(within(row).getByText(/Откроется/)).toBeInTheDocument();
    expect(within(row).getByText('Недоступен')).toBeInTheDocument();
    expect(screen.queryByText(locked.lesson.summary)).not.toBeInTheDocument();
  });
  it('shows no available lessons without treating locked metadata as an error', async () => {
    const data = fixture(); data.lessons = [data.lessons[2]!];
    data.progress = { completedLessons: 0, availableLessons: 0, totalLessons: 1, percent: 0 };
    mount(data); await loaded();
    expect(screen.getByText('Пока нет доступных уроков.')).toBeInTheDocument();
    expect(screen.getByText('Принцип Дирихле')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
  it.each([['NotPublished', 'Урок ещё не опубликован'], ['EnrollmentInactive', 'Обучение на курсе неактивно'],
    ['Maintenance', 'Недоступен: Maintenance']] as const)('preserves availability reason %s', async (reason, label) => {
    const data = fixture(); data.lessons[2]!.availability.reason = reason;
    mount(data); await loaded(); expect(screen.getByText(label)).toBeInTheDocument();
  });
  it('hides unsupported events and absent teacher/materials sections', async () => {
    mount(); await loaded();
    const context = within(screen.getByRole('complementary'));
    expect(context.getByRole('heading', { name: 'Прогресс курса' })).toBeInTheDocument();
    expect(context.queryByRole('heading', { name: /Материалы|Преподаватель|События/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
  });
  it('renders actual optional context and safe material links once', async () => {
    const data = fixture();
    data.course.teacher = { id: '849c8cba-a239-4cf9-b622-aec73d1b5b61', displayName: 'Е. А. Тюменцев', avatarUrl: null };
    data.materials = [{ id: 'guide', title: 'Программа курса', kind: 'Document', description: null, url: '/materials/guide.pdf' }];
    mount(data); await loaded();
    const context = within(screen.getByRole('complementary'));
    expect(context.getByText('Е. А. Тюменцев')).toBeInTheDocument();
    expect(context.getByRole('link', { name: 'Программа курса' }).getAttribute('href')).toMatch(/\/materials\/guide.pdf$/);
    expect(within(screen.getByRole('main')).queryByText('Программа курса')).not.toBeInTheDocument();
  });
  it('rejects executable material URLs while preserving the text', async () => {
    const data = fixture(); data.materials = [{ id: 'bad', title: 'Материал', kind: 'Link', description: null, url: 'javascript:alert(1)' }];
    mount(data); await loaded();
    expect(screen.getByText('Ссылка недоступна')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Материал' })).not.toBeInTheDocument();
  });
  it('accepts nullable titles and mixed publishedVersionId', async () => {
    const data = fixture(); data.course.title = null; data.course.publishedVersionId = null; data.lessons[0]!.lesson.title = null;
    mount(data);
    expect(await screen.findByRole('heading', { level: 1, name: 'Курс без названия' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Урок 1' })).toBeInTheDocument();
  });
  it('has a single h1, semantic lesson list, labelled context and current breadcrumb', async () => {
    mount(); await loaded();
    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1);
    expect(within(screen.getByRole('region', { name: 'Уроки' })).getAllByRole('listitem')).toHaveLength(3);
    expect(screen.getByRole('complementary', { name: 'Контекст' })).toBeInTheDocument();
    expect(within(screen.getByRole('navigation', { name: 'Хлебные крошки' })).getByText(studentFixture.course.title)).toHaveAttribute('aria-current', 'page');
    expect(document.title).toBe(`${studentFixture.course.title} · Sapientum`);
  });
  it('aborts the transport on unmount', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockReturnValue(new Promise(() => {}));
    const { unmount } = mount(fixture(), fetch);
    await waitFor(() => expect(fetch).toHaveBeenCalledOnce());
    const signal = fetch.mock.calls[0]![1]?.signal;
    unmount(); expect(signal?.aborted).toBe(true);
  });
  it('uses the real browser auth adapter and invokes session handling on 401', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(problem(401, 'Unauthenticated'));
    vi.stubGlobal('fetch', fetch);
    const onUnauthenticated = vi.fn(); window.lmsAuth = { getAccessToken: () => 'host-session-token', onUnauthenticated };
    render(<MemoryRouter initialEntries={['/courses/olympiad-7']} future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <App client={browserApiClient} /></MemoryRouter>);
    expect(await screen.findByRole('alert')).toHaveTextContent('Войдите');
    expect(new Headers(fetch.mock.calls[0]![1]?.headers).get('Authorization')).toBe('Bearer host-session-token');
    expect(onUnauthenticated).toHaveBeenCalledOnce();
  });
});

describe('Course decoder and race protection', () => {
  it.each(['lessons', 'materials', 'teacherContext', 'enrollment'])('rejects missing required C4 property %s', key => {
    const data: Record<string, unknown> = fixture(); delete data[key];
    expect(() => decodeCourseWorkspace(data)).toThrow('Invalid CourseWorkspace');
  });
  it('rejects the old flat lesson sketch and malformed status', () => {
    const data = fixture();
    expect(() => decodeCourseWorkspace({ ...data, lessons: [{ lessonId: 'one', title: 'Old sketch' }] })).toThrow();
    const invalid = structuredClone(data) as unknown as { lessons: { progress: { status: unknown } }[] };
    invalid.lessons[0]!.progress.status = ['Completed'];
    expect(() => decodeCourseWorkspace(invalid)).toThrow();
  });
  it('shows InvalidResponse for wrong course identity', async () => {
    const data = fixture(); data.course.id = 'other-course';
    mount(data);
    expect(await screen.findByRole('alert')).toHaveTextContent('Получен некорректный ответ сервера');
  });
  it('ignores a previous course response even if its transport ignores abort', async () => {
    let finishFirst!: (value: Response) => void;
    const fetch = vi.fn<typeof globalThis.fetch>().mockReturnValueOnce(new Promise(resolve => { finishFirst = resolve; }))
      .mockResolvedValueOnce(response({ ...fixture(), course: { ...fixture().course, id: 'second', title: 'Второй курс' } }));
    const client = createApiClient({ fetch, getAccessToken: () => null });
    const { result, rerender } = renderHook(({ courseId }) => useCourseWorkspace(client, courseId), { initialProps: { courseId: 'olympiad-7' } });
    await waitFor(() => expect(fetch).toHaveBeenCalledOnce());
    rerender({ courseId: 'second' });
    await waitFor(() => expect(result.current.state.phase).toBe('success'));
    await act(async () => { finishFirst(response(fixture())); });
    expect(fetch.mock.calls[0]![1]?.signal?.aborted).toBe(true);
    expect(result.current.state).toMatchObject({ phase: 'success', data: { course: { id: 'second' } } });
  });
});
