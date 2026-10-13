import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, useNavigate } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { ApiError, type ApiClient } from '../api/client';
import { decodeLessonWorkspace } from '../api/contracts/lessonWorkspace';
import { decodeCourses } from '../features/course/CoursesPage';
import course from './fixtures/course-student.json';
import lesson from './fixtures/lesson-student.json';
const lessonPath = `/courses/${course.course.id}/lessons/${lesson.lesson.id}`;
const list = { items: [{ course: course.course, role: 'Student', progress: course.progress }] };
function Controls() { const navigate = useNavigate(); return <><button onClick={() => navigate(-1)}>Browser back</button><button onClick={() => navigate(1)}>Browser forward</button></>; }
function mount(path: string, handler: (url: string) => unknown) {
  const request = vi.fn(async (url: string, decode: (value: unknown) => unknown) => decode(handler(url)));
  render(<MemoryRouter initialEntries={[path]} future={{ v7_startTransition: true, v7_relativeSplatPath: true }}><Controls /><App client={{ request } as ApiClient} /></MemoryRouter>);
  return request;
}
function teacherLesson() {
  const data = decodeLessonWorkspace(structuredClone(lesson));
  data.problems.forEach(row => { row.permissions = { canSubmit: false, canPostMessage: false, canAccept: false, canRequestRevision: false, canViewAsTeacher: true }; });
  return data;
}
describe('Course navigation', () => {
  it('loads only server rows, role and server progress, opens course and returns through history', async () => {
    const request = mount('/courses', url => url === '/courses' ? list : course);
    const link = await screen.findByRole('link', { name: new RegExp(course.course.title) });
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', String(course.progress.percent));
    expect(within(link).getByText('Ученик')).toBeInTheDocument();
    await userEvent.click(link); await screen.findByRole('heading', { level: 1, name: course.course.title });
    await userEvent.click(screen.getByRole('button', { name: 'Browser back' }));
    await screen.findByRole('link', { name: new RegExp(course.course.title) });
    expect(request.mock.calls.filter(([url]) => url === '/courses')).toHaveLength(2);
  });
  it('has real empty state and does not create sample course links', async () => {
    mount('/courses', () => ({ items: [] })); await screen.findByText('Пока нет доступных курсов');
    expect(document.querySelector('a[href*="demo-course"]')).toBeNull();
  });
  it.each([401, 403, 404, 503])('handles list HTTP %s without cached course rows', async status => {
    mount('/courses', () => { throw new ApiError(status, 'Forbidden'); });
    await screen.findByRole('alert'); expect(screen.queryByText(course.course.title)).toBeNull();
  });
  it('refetches after returning and removes courses after access withdrawal', async () => {
    let denied = false;
    mount('/courses', url => { if (url !== '/courses') return course; if (denied) throw new ApiError(403, 'Forbidden'); return list; });
    await userEvent.click(await screen.findByRole('link', { name: new RegExp(course.course.title) }));
    await screen.findByRole('heading', { level: 1, name: course.course.title }); denied = true;
    await userEvent.click(screen.getByRole('button', { name: 'Browser back' }));
    await screen.findByRole('alert'); expect(screen.queryByText(course.course.title)).toBeNull();
  });
  it('renders optional progress only when supplied and validates explicit role', async () => {
    mount('/courses', () => ({ items: [{ ...list.items[0], role: 'Teacher', progress: null }] }));
    await screen.findByText('Преподаватель'); expect(screen.queryByRole('progressbar')).toBeNull();
    expect(() => decodeCourses({ items: [{ ...list.items[0], role: 'Administrator' }] })).toThrow();
  });
  it('opens teacher statement without a work, no submit/review controls, and returns to lesson', async () => {
    const data = teacherLesson(); mount(lessonPath, () => data);
    await userEvent.click(await screen.findByRole('link', { name: 'Посмотреть задачу: Задача 1' }));
    await screen.findByText('Решение ещё не отправлено');
    expect(screen.getByRole('region', { name: 'Задание ученика' })).toHaveTextContent(data.problems[0]!.problem.statementMarkdown);
    expect(screen.queryByRole('button', { name: /Отправить решение|Принять решение/ })).toBeNull();
    expect(document.querySelector('a[href*="null"],a[href*="undefined"]')).toBeNull();
    await userEvent.click(screen.getByRole('link', { name: 'Назад к занятию' }));
    await screen.findByRole('heading', { name: 'Работы учеников' });
  });
  it.each(['Accepted', 'NeedsRevision'] as const)('links %s teacher work using explicit view capability', async status => {
    const data = teacherLesson(); data.problems = [data.problems.find(row => row.work.status === status)!];
    mount(lessonPath, () => data);
    expect(await screen.findByRole('link', { name: /Посмотреть решение:/ })).toHaveAttribute('href', `/teacher/reviews/${data.problems[0]!.work.problemWorkId}`);
  });
  it('does not infer teacher from canSubmit false and rejects a forged preview assignment', async () => {
    const data = teacherLesson(); data.problems[0]!.permissions.canViewAsTeacher = false;
    mount(lessonPath + '/assignments/' + data.problems[0]!.problemAssignmentId, () => data);
    expect(await screen.findByRole('alert')).toHaveTextContent('Задание недоступно');
    expect(screen.queryByRole('region', { name: 'Задание ученика' })).toBeNull();
  });
  it('rechecks lesson access on opening a teacher preview', async () => {
    const data = teacherLesson(); let denied = false;
    mount(lessonPath, () => { if (denied) throw new ApiError(403, 'Forbidden'); return data; });
    const link = await screen.findByRole('link', { name: 'Посмотреть задачу: Задача 1' }); denied = true;
    await userEvent.click(link); expect(await screen.findByRole('alert')).toHaveTextContent('Нет доступа к занятию');
    expect(screen.queryByRole('region', { name: 'Задание ученика' })).toBeNull();
  });
});

it('student traverses list → course → lesson → assignment → history and returns to the same lesson', async () => {
  const row = lesson.problems[0]!;
  const problem = { assignment: { id: row.problemAssignmentId, isActive: true }, course: lesson.course, lesson: lesson.lesson,
    problem: row.problem, work: row.work, availability: row.availability, permissions: row.permissions,
    hints: [], usefulMaterials: [], history: { submissions: [], reviews: [], messages: [] } };
  mount('/courses', url => url === '/courses' ? list : url.startsWith('/problem-assignments/') ? problem : url.includes('/lessons/') ? lesson : course);
  await userEvent.click(await screen.findByRole('link', { name: new RegExp(course.course.title) }));
  await screen.findByRole('heading', { level: 1, name: course.course.title });
  const link = document.querySelector(`a[href="${lessonPath}"]`) as HTMLAnchorElement;
  expect(link).not.toBeNull(); await userEvent.click(link);
  await userEvent.click(await screen.findByRole('link', { name: 'Открыть задачу: Задача 1' }));
  await screen.findByRole('heading', { level: 1, name: 'Задача 1' });
  await userEvent.click(screen.getByRole('tab', { name: 'История' }));
  expect(screen.getByRole('tab', { name: 'История' })).toHaveAttribute('aria-selected', 'true');
  await userEvent.click(screen.getByRole('button', { name: 'Browser back' }));
  expect(screen.getByRole('tab', { name: 'Решение' })).toHaveAttribute('aria-selected', 'true');
  await userEvent.click(screen.getByRole('button', { name: 'Browser forward' }));
  expect(screen.getByRole('tab', { name: 'История' })).toHaveAttribute('aria-selected', 'true');
  await userEvent.click(screen.getByRole('link', { name: 'Назад к занятию' }));
  await screen.findByRole('heading', { level: 1, name: lesson.lesson.title });
});
