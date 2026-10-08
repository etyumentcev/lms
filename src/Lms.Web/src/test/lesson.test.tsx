import { render, screen, within, waitFor, renderHook, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { createApiClient } from '../api/client';
import { decodeLessonWorkspace } from '../api/contracts/lessonWorkspace';
import { useLessonWorkspace } from '../features/lesson/query';
import { attempts } from '../features/lesson/LessonPage';
import fixtureJson from './fixtures/lesson-student.json';
import course from './fixtures/course-student.json';
const fixture = () => decodeLessonWorkspace(structuredClone(fixtureJson));
const response = (data: unknown, status = 200) => new Response(JSON.stringify(data), { status });
const path = '/courses/olympiad-7/lessons/lesson-2';
function mount(data = fixture(), fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(response(data))) {
  const onUnauthenticated = vi.fn();
  const client = createApiClient({ fetch, getAccessToken: () => 'test-token', onUnauthenticated });
  return { ...render(<MemoryRouter initialEntries={[path]} future={{ v7_startTransition: true, v7_relativeSplatPath: true }}><App client={client} /></MemoryRouter>), fetch, onUnauthenticated };
}
const loaded = () => screen.findByRole('heading', { level: 1, name: fixtureJson.lesson.title });
describe('Lesson C4 workspace', () => {
  it('keeps shell with header, row and context skeletons', () => {
    mount(fixture(), vi.fn<typeof fetch>().mockReturnValue(new Promise(() => {})));
    expect(screen.getByRole('status', { name: 'Загрузка задач' })).toBeInTheDocument();
    expect(screen.getByRole('status', { name: 'Загрузка контекста занятия' })).toBeInTheDocument();
    expect(screen.getByRole('main')).toBeInTheDocument(); expect(screen.getByRole('navigation', { name: 'Основная навигация' })).toBeInTheDocument();
  });
  it('uses authenticated exact GET and C4 data', async () => {
    const { fetch } = mount(); await loaded();
    expect(fetch.mock.calls[0]![0]).toBe('/api/v1/courses/olympiad-7/lessons/lesson-2/workspace');
    expect(new Headers(fetch.mock.calls[0]![1]?.headers).get('Authorization')).toBe('Bearer test-token');
    expect(within(screen.getByRole('region', { name: 'Задачи' })).getAllByRole('listitem')).toHaveLength(4);
  });
  it('has breadcrumbs with a non-linked current lesson', async () => {
    mount(); await loaded(); const nav = screen.getByRole('navigation', { name: 'Хлебные крошки' });
    expect(within(nav).getByRole('link', { name: 'Мои курсы' })).toHaveAttribute('href', '/courses');
    expect(within(nav).getByRole('link', { name: course.course.title })).toHaveAttribute('href', '/courses/olympiad-7');
    expect(within(nav).queryByRole('link', { name: fixtureJson.lesson.title })).not.toBeInTheDocument();
  });
  it('returns to Course with Router and focuses main', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValueOnce(response(fixture())).mockResolvedValueOnce(response(course));
    mount(fixture(), fetch); await loaded(); await userEvent.click(screen.getByRole('link', { name: course.course.title }));
    await screen.findByRole('heading', { level: 1, name: course.course.title }); expect(screen.getByRole('main')).toHaveFocus();
  });
  it('navigates by assignment with null work using keyboard and focuses workspace', async () => {
    mount(); await loaded(); const link = screen.getByRole('link', { name: 'Открыть задачу: Задача 1' });
    expect(link).toHaveAttribute('href', `/problem-assignments/${fixtureJson.problems[0]!.problemAssignmentId}`);
    link.focus(); await userEvent.keyboard('{Enter}'); expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Задача');
    expect(screen.getByRole('main')).toHaveFocus(); expect(screen.getByRole('complementary')).toBeInTheDocument();
  });
  it.each(['Не начата', 'На проверке', 'Нужна доработка', 'Принята'])('renders canonical status %s as text', async label => {
    mount(); await loaded(); expect(screen.getByText(label)).toBeInTheDocument();
  });
  it.each([[1,'1 попытка'],[2,'2 попытки'],[5,'5 попыток'],[11,'11 попыток'],[21,'21 попытка'],[22,'22 попытки']])('pluralizes %s attempts', (count, label) => expect(attempts(Number(count))).toBe(label));
  it('uses server attempt count without displaying zero', async () => {
    const data = fixture(); data.problems[1]!.work.attemptCount = 22; mount(data); await loaded();
    expect(screen.getByText('22 попытки')).toBeInTheDocument(); expect(screen.queryByText('0 попыток')).not.toBeInTheDocument();
  });
  it.each([0, 25, 100, 33.33])('uses server progress %s without recomputing from rows', async percent => {
    const data = fixture(); data.progress.percent = percent; mount(data); await loaded();
    expect(screen.getByRole('progressbar', { name: 'Завершение занятия' })).toHaveAttribute('aria-valuenow', String(percent));
    expect(screen.getByText('1 из 4 задач принято')).toBeInTheDocument();
  });
  it('accepts zero problems and zero denominator', async () => {
    const data = fixture(); data.problems = []; data.progress = { status: 'NotStarted', percent: 0, acceptedProblems: 0, totalAssignedProblems: 0 };
    mount(data); await loaded(); expect(screen.getByText('В занятии пока нет задач')).toBeInTheDocument(); expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
  it('renders materials only from contract with safe links', async () => {
    const data = fixture(); data.materials = [{ id: 'm', title: 'Конспект', description: 'Описание', kind: 'Document', url: '/notes.pdf' },
      { id: 'bad', title: 'Некорректный', description: null, kind: 'Link', url: 'javascript:alert(1)' }];
    mount(data); await loaded(); expect(screen.getByRole('link', { name: 'Конспект' })).toHaveAttribute('href', 'http://localhost:3000/notes.pdf');
    expect(screen.queryByRole('link', { name: /Некорректный/ })).not.toBeInTheDocument();
  });
  it('omits absent materials and optional metadata', async () => {
    const data = fixture(); data.lesson.summary = null; data.lesson.title = null; mount(data);
    await screen.findByRole('heading', { level: 1, name: 'Занятие 2' }); expect(screen.queryByText('Материалы занятия')).not.toBeInTheDocument();
  });
  it('redacts restricted success payload content, progress and materials', async () => {
    const data = fixture(); data.availability.isAvailable = false; data.availability.reason = 'NotPublished';
    data.materials = [{ id: 'secret', title: 'Protected material', kind: 'Link', url: '/secret', description: null }];
    mount(data); await loaded(); expect(screen.getByText('Занятие недоступно')).toBeInTheDocument();
    expect(screen.queryByText('Задача 1')).not.toBeInTheDocument(); expect(screen.queryByText(data.lesson.summary!)).not.toBeInTheDocument();
    expect(screen.queryByText('Protected material')).not.toBeInTheDocument(); expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
  });
  it('does not link unavailable assignments', async () => {
    const data = fixture(); data.problems[0]!.availability.isAvailable = false; mount(data); await loaded();
    expect(screen.queryByRole('link', { name: 'Открыть задачу: Задача 1' })).not.toBeInTheDocument();
  });
  it.each([[401,'Войдите, чтобы открыть занятие'],[403,'Нет доступа к занятию'],[404,'Занятие не найдено']])('handles HTTP %s', async (status, title) => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(response({ code: 'Forbidden', title: 'Untrusted text', status }, Number(status)));
    const { onUnauthenticated } = mount(fixture(), fetch); expect(await screen.findByRole('alert')).toHaveTextContent(String(title));
    expect(onUnauthenticated).toHaveBeenCalledTimes(status === 401 ? 1 : 0); expect(screen.queryByRole('button', { name: 'Повторить' })).not.toBeInTheDocument();
  });
  it.each(['network','server'])('retries GET after %s error', async kind => {
    const fetch = vi.fn<typeof globalThis.fetch>(); if (kind === 'network') fetch.mockRejectedValueOnce(new TypeError('offline')); else fetch.mockResolvedValueOnce(response({}, 500));
    fetch.mockResolvedValueOnce(response(fixture())); mount(fixture(), fetch); await screen.findByRole('alert');
    await userEvent.click(screen.getByRole('button', { name: 'Повторить' })); await loaded(); expect(fetch).toHaveBeenCalledTimes(2);
  });
  it('keeps one context after main, with named landmarks and one h1', async () => {
    mount(); await loaded(); expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1);
    expect(screen.getAllByRole('complementary')).toHaveLength(1); expect(screen.getByRole('main').compareDocumentPosition(screen.getByRole('complementary')) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(within(screen.getByRole('complementary')).getByText('О занятии')).toBeInTheDocument();
  });
  it('renders teacher-scoped rows without a fake student/enrollment identity', async () => {
    const data = fixture(); data.problems[1]!.permissions.canAccept = true; data.problems[1]!.permissions.canRequestRevision = true;
    data.problems.forEach(p => { p.permissions.canSubmit = false; }); mount(data); await loaded();
    expect(screen.queryByText('Ученик')).not.toBeInTheDocument(); expect(screen.getByText('По доступным вам назначенным задачам.')).toBeInTheDocument();
  });
  it('uses statement as a safe plain-text title fallback', async () => {
    const data = fixture(); data.problems[0]!.problem.title = null; data.problems[0]!.problem.statementMarkdown = '<script>alert(1)</script> Условие';
    mount(data); await loaded(); expect(screen.getByRole('heading', { name: '<script>alert(1)</script> Условие' })).toBeInTheDocument();
    expect(document.querySelector('main script')).toBeNull();
  });
  it('distinguishes multiple students and does not offer teacher revision editing', async () => {
    const data = fixture(); data.problems[1]!.studentId = 'other-student'; data.problems[2]!.permissions.canSubmit = false;
    mount(data); await loaded(); expect(screen.getByText('Ученик: other-student')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Посмотреть задачу: Задача 3' })).toHaveAttribute('href', `/problem-assignments/${data.problems[2]!.problemAssignmentId}`);
    expect(screen.queryByRole('link', { name: 'Исправить решение: Задача 3' })).not.toBeInTheDocument();
  });
  it('rejects noncanonical work status and mismatched identity', async () => {
    const data = structuredClone(fixtureJson); data.problems[0]!.work.status = 'Draft'; expect(() => decodeLessonWorkspace(data)).toThrow();
    const wrong = fixture(); wrong.lesson.id = 'other'; mount(wrong); expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось загрузить занятие');
  });
  it('cancels on unmount', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockReturnValue(new Promise(() => {})); const { unmount } = mount(fixture(), fetch);
    await waitFor(() => expect(fetch).toHaveBeenCalledOnce()); const signal = fetch.mock.calls[0]![1]?.signal; unmount(); expect(signal?.aborted).toBe(true);
  });
  it('excludes stale responses across lesson changes', async () => {
    let resolveOld!: (value: Response) => void;
    const fetch = vi.fn<typeof globalThis.fetch>().mockReturnValueOnce(new Promise(resolve => { resolveOld = resolve; })).mockResolvedValueOnce(response({ ...fixture(), lesson: { ...fixture().lesson, id: 'new' } }));
    const client = createApiClient({ fetch, getAccessToken: () => null }); const { result, rerender } = renderHook(({ id }) => useLessonWorkspace(client, 'olympiad-7', id), { initialProps: { id: 'lesson-2' } });
    await waitFor(() => expect(fetch).toHaveBeenCalledOnce()); rerender({ id: 'new' }); await waitFor(() => expect(result.current.state.phase).toBe('success'));
    await act(async () => resolveOld(response(fixture()))); expect(result.current.state.phase === 'success' && result.current.state.data.lesson.id).toBe('new');
  });
});
