import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, useNavigate } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { ApiError, type ApiClient } from '../api/client';
import { decodeStudentRecentProgress } from '../api/contracts/progressHistory';
import type { ProgressAssignment } from '../api/contracts/progressHistory';
import type { StudentAssignment, PublishedStudentContent } from '../api/contracts/publishedContent';
import { StudentCourseDashboard } from '../features/content/StudentCourseDashboard';

const courseId = 'synthetic-course';
const versionId = '10000000-0000-0000-0000-000000000001';
function task(index: number, status: StudentAssignment['status']): StudentAssignment {
  return { assignmentId: `assignment-${index}`, contentVersionId: versionId, versionNumber: 1,
    lessonId: `lesson-${index}`, lessonTitle: `Занятие ${index}`, problemId: `problem-${index}`,
    problemTitle: `Задача ${index}`, problemOrder: index, status, attemptCount: 1 };
}
function row(index: number, assignments: StudentAssignment[] = []) {
  const lesson: PublishedStudentContent['course']['lessons'][number] = {
    id: `lesson-${index}`, title: `Занятие ${index}`, order: index, bodyMarkdown: '', problems: []
  };
  return { versionId, versionNumber: 1, lesson, assignments };
}
function HistoryControls() {
  const navigate = useNavigate();
  return <><button onClick={() => navigate(-1)}>Назад</button><button onClick={() => navigate(1)}>Вперёд</button></>;
}
const emptyProgress = { items: [], page: 1, pageSize: 5, total: 0,
  summary: { studentCount: 1, assignedCount: 0, notStarted: 0, submitted: 0,
    needsRevision: 0, accepted: 0 } };
const emptySchedule = { workload: null, rules: [] };
function scheduleAware(path: string, decode: (value: unknown) => unknown) {
  if (path.endsWith("/schedule")) return decode(emptySchedule);
  if (path.includes("/schedule/upcoming")) return decode({ items: [] });
  return decode(emptyProgress);
}
const emptyClient = { request: async (path: string, decode: (value: unknown) => unknown) =>
  scheduleAware(path, decode) } as ApiClient;
function mount(assignments: StudentAssignment[], rows = [row(1, assignments)],
  path = `/study/courses/${courseId}`, client = emptyClient) {
  return render(<MemoryRouter initialEntries={[path]}><StudentCourseDashboard courseId={courseId}
    title="Олимпиадная математика" description="Серверное описание" assignments={assignments} rows={rows} client={client}/>
    <HistoryControls/></MemoryRouter>);
}

describe('student course dashboard', () => {
  it('uses URL history for tabs and reuses the accessible lesson route', async () => {
    mount([task(1, 'Accepted')]);
    expect(screen.getByRole('link', { name: 'Сводка' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('complementary')).toHaveTextContent('Серверное описание');
    expect(screen.queryByText('Студент')).toBeNull();
    expect(screen.getByRole('complementary').querySelectorAll('.student-course-info-icon svg')).toHaveLength(4);
    expect(document.querySelectorAll('.student-course-stat-icon svg')).toHaveLength(2);
    await userEvent.click(screen.getByRole('link', { name: 'Занятия' }));
    expect(screen.getByRole('link', { name: 'Занятия' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('link', { name: /Занятие 1/ })).toHaveAttribute('href',
      `/study/courses/${courseId}/versions/${versionId}/lessons/lesson-1`);
    await userEvent.click(screen.getByRole('button', { name: 'Назад' }));
    expect(screen.getByRole('link', { name: 'Сводка' })).toHaveAttribute('aria-current', 'page');
    await userEvent.click(screen.getByRole('button', { name: 'Вперёд' }));
    expect(screen.getByRole('link', { name: 'Занятия' })).toHaveAttribute('aria-current', 'page');
  });

  it('counts an accepted assignment once and does not fabricate 0/0 or attendance', () => {
    const accepted = task(1, 'Accepted');
    const { unmount } = mount([accepted, { ...accepted, status: 'Submitted' }, task(2, 'NeedsRevision')]);
    expect(screen.getByRole('progressbar', { name: 'Принято задач' })).toHaveAttribute('aria-valuenow', '50');
    expect(screen.getByText('1', { selector: '.student-course-stat-value' })).toHaveTextContent('1 из 2');
    expect(screen.queryByText('Посещения занятий')).toBeNull();
    unmount();
    mount([], []);
    expect(screen.getByText('Назначенных задач пока нет.')).toBeVisible();
    expect(screen.queryByRole('progressbar')).toBeNull();
    expect(screen.queryByText(/NaN|100%/)).toBeNull();
  });

  it('shows five latest lessons in course order and an empty submission state', async () => {
    const rows = [2, 6, 1, 5, 3, 4].map(index => row(index, [task(index, index === 6 ? 'Accepted' : 'Submitted')]));
    mount(rows.flatMap(item => item.assignments), rows);
    const recent = screen.getByRole('heading', { name: 'Прогресс по последним урокам' }).closest('section')!;
    expect(Array.from(recent.querySelectorAll('ol li strong'), item => item.textContent))
      .toEqual(['Занятие 6', 'Занятие 5', 'Занятие 4', 'Занятие 3', 'Занятие 2']);
    expect(await screen.findByText('Отправленных задач пока нет.')).toBeVisible();
    expect(screen.queryByText(/часа назад|дня назад/)).toBeNull();
  });

  it('measures lesson progress only by unique accepted tasks and hides empty percentages', () => {
    const accepted = task(1, 'Accepted');
    const revision = { ...task(2, 'NeedsRevision'), lessonId: 'lesson-1' };
    const submitted = { ...task(3, 'Submitted'), lessonId: 'lesson-1' };
    const full = { ...task(4, 'Accepted'), lessonId: 'lesson-2' };
    const untouched = { ...task(5, 'Submitted'), lessonId: 'lesson-4' };
    const rows = [row(1, [accepted, { ...accepted, status: 'Submitted' }, revision, submitted]),
      row(2, [full]), row(3), row(4, [untouched])];
    mount([accepted, revision, submitted, full, untouched], rows);
    expect(screen.getByRole('progressbar', { name: 'Прогресс урока Занятие 1' }))
      .toHaveAttribute('aria-valuenow', '33');
    expect(screen.getByRole('progressbar', { name: 'Прогресс урока Занятие 1' }))
      .toHaveAttribute('aria-valuetext', 'Принято задач: 1 из 3');
    expect(screen.getByRole('progressbar', { name: 'Прогресс урока Занятие 2' }))
      .toHaveAttribute('aria-valuenow', '100');
    expect(screen.getByRole('progressbar', { name: 'Прогресс урока Занятие 4' }))
      .toHaveAttribute('aria-valuenow', '0');
    expect(screen.queryByRole('progressbar', { name: 'Прогресс урока Занятие 3' })).toBeNull();
    expect(screen.getByRole('link', { name: /Занятие 3/ })).toHaveTextContent('Назначенных задач нет');
    expect(screen.queryByText(/учёта просмотров материалов/)).toBeNull();
  });

  it('renders server-ordered submissions, current statuses and protected work links', async () => {
    const items = [1, 2, 3].map(index => ({
      assignmentId: `assignment-${index}`, problemWorkId: `work-${index}`, courseVersionId: versionId,
      courseId, courseTitle: 'Олимпиадная математика', lessonId: `lesson-${index}`,
      lessonTitle: `Занятие ${index}`, problemId: `problem-${index}`,
      problemTitle: `Задача ${index}`, status: (['Submitted', 'NeedsRevision', 'Accepted'] as const)[index - 1],
      attemptCount: index, isActive: true, lastActivityAt: '2026-07-20T11:00:00Z',
      lastSubmittedAt: `2026-07-${21 - index}T10:00:00Z`
    }));
    const request = vi.fn(async (_path: string, decode: (value: unknown) => unknown) =>
      decode({ items, page: 1, pageSize: 5, total: 3,
        summary: { studentCount: 1, assignedCount: 3, notStarted: 0,
          submitted: 1, needsRevision: 1, accepted: 1 } }));
    mount([], [], `/study/courses/${courseId}`, { request } as unknown as ApiClient);
    const list = (await screen.findByText('Задача 1')).closest('ol')!;
    expect(Array.from(list.querySelectorAll('li strong'), node => node.textContent))
      .toEqual(['Задача 1', 'Задача 2', 'Задача 3']);
    expect(Array.from(list.querySelectorAll('li .badge'), node => node.textContent))
      .toEqual(['◷На проверке', '!Нужна доработка', '✓Принята']);
    expect(list.querySelectorAll('time')).toHaveLength(3);
    expect(list.querySelector('time')).toHaveAttribute('datetime', '2026-07-20T10:00:00Z');
    expect(list.querySelector('li a')).toHaveAttribute('href', '/problem-assignments/assignment-1');
    expect(screen.getByRole('link', { name: /Все задачи/ })).toHaveAttribute('href',
      `/study/courses/${courseId}?tab=lessons`);
    expect(request).toHaveBeenCalledWith(
      `/student/progress-history?courseId=${courseId}&sort=lastSubmissionDesc&page=1&pageSize=5`,
      expect.any(Function), expect.objectContaining({ signal: expect.any(AbortSignal) }));
  });

  it('rejects cross-course, missing-time and unsorted payloads without showing task content', () => {
    const valid: ProgressAssignment & { lastSubmittedAt: string } = {
      assignmentId: 'one', problemWorkId: 'work', courseVersionId: versionId, courseId,
      courseTitle: 'Курс', lessonId: 'lesson', lessonTitle: 'Занятие', problemId: 'problem',
      problemTitle: 'Задача', status: 'Submitted', attemptCount: 1, isActive: true,
      lastActivityAt: '2026-07-20T10:00:00Z', lastSubmittedAt: '2026-07-20T10:00:00Z'
    };
    const envelope = { items: [valid], page: 1, pageSize: 5, total: 1,
      summary: { studentCount: 1, assignedCount: 1, notStarted: 0,
        submitted: 1, needsRevision: 0, accepted: 0 } };
    expect(() => decodeStudentRecentProgress({ ...envelope,
      items: [{ ...valid, courseId: 'other' }] }, courseId)).toThrow();
    expect(() => decodeStudentRecentProgress({ ...envelope,
      items: [{ ...valid, lastSubmittedAt: undefined }] }, courseId)).toThrow();
    expect(() => decodeStudentRecentProgress({ ...envelope,
      items: [{ ...valid, lastSubmittedAt: '2026-07-19T10:00:00Z' }, valid] }, courseId)).toThrow();
  });

  it('offers retry for transient failure but not for denied access', async () => {
    let progressAttempts = 0;
    const transient = vi.fn(async (path: string, decode: (value: unknown) => unknown) => {
      if (path.startsWith("/student/progress-history")) {
        if (progressAttempts++ === 0) throw new ApiError(0, "NetworkError");
        return decode(emptyProgress);
      }
      return scheduleAware(path, decode);
    });
    const { unmount } = mount([], [], `/study/courses/${courseId}`,
      { request: transient } as unknown as ApiClient);
    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось загрузить');
    await userEvent.click(screen.getByRole('button', { name: 'Повторить' }));
    expect(await screen.findByText('Отправленных задач пока нет.')).toBeVisible();
    expect(transient.mock.calls.filter(([path]) => String(path).startsWith("/student/progress-history"))).toHaveLength(2);
    unmount();
    const denied = vi.fn(async (path: string, decode: (value: unknown) => unknown) => {
      if (path.startsWith("/student/progress-history")) throw new ApiError(403, "Forbidden");
      return scheduleAware(path, decode);
    });
    const { unmount: unmountDenied } = mount([], [], `/study/courses/${courseId}`, { request: denied } as unknown as ApiClient);
    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось загрузить');
    expect(screen.queryByRole('button', { name: 'Повторить' })).toBeNull();
    unmountDenied();
    const expired = vi.fn(async (path: string, decode: (value: unknown) => unknown) => {
      if (path.startsWith("/student/progress-history")) throw new ApiError(401, "Unauthenticated");
      return scheduleAware(path, decode);
    });
    mount([], [], `/study/courses/${courseId}`, { request: expired } as unknown as ApiClient);
    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось загрузить');
    expect(screen.queryByRole('button', { name: 'Повторить' })).toBeNull();
  });

});
