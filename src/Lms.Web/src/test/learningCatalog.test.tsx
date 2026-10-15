import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import type { ApiClient } from '../api/client';
import type { StudentAssignment } from '../api/contracts/publishedContent';
import { LearningCatalog, studentProgress } from '../features/course/LearningCatalog';

type CatalogCourse = {
  courseId: string; title: string; description: string; state: 'Draft'|'Published'; role: 'Student'|'Teacher';
  groups: { groupId: string; name: string; studentCount: number }[]; canEditContent: boolean;
};

const course: CatalogCourse = {
  courseId: 'olympiad-8',
  title: 'Олимпиадная математика · 8 класс',
  description: '72 часа · 24 занятия · очно · 1 × 3 ч/нед.',
  state: 'Published',
  role: 'Student',
  groups: [{ groupId: 'grade-8', name: '8 класс', studentCount: 0 }],
  canEditContent: false,
};
function assignment(index: number, status: StudentAssignment['status']): StudentAssignment {
  return {
    assignmentId: `assignment-${index}`,
    contentVersionId: '10000000-0000-0000-0000-000000000001',
    versionNumber: 1,
    lessonId: `lesson-${index}`,
    lessonTitle: `Занятие ${index}`,
    problemId: `problem-${index}`,
    problemTitle: `Задача ${index}`,
    problemOrder: index,
    status,
    attemptCount: status === 'Accepted' ? 3 : 1,
  };
}
function api(items: StudentAssignment[], item: CatalogCourse = course) {
  const request = vi.fn(async (url: string, decode: (value: unknown) => unknown) =>
    decode(url === '/my/courses' ? { items: [item] } : { items }));
  return { request, client: { request } as ApiClient };
}
function Location() {
  const location = useLocation();
  return <output aria-label="Текущий маршрут">{location.pathname}</output>;
}
function mount(client: ApiClient, role: 'Student'|'Teacher' = 'Student') {
  return render(<MemoryRouter initialEntries={['/']}><LearningCatalog role={role} client={client}/><Location/></MemoryRouter>);
}

describe('student course card', () => {
  it('shows personal accepted-task progress, deduplicates a task and opens the whole card with Enter', async () => {
    const rows = Array.from({ length: 24 }, (_, index) => assignment(index + 1, index < 7 ? 'Accepted' : index === 7 ? 'NeedsRevision' : 'Submitted'));
    rows.push({ ...rows[0]!, status: 'Submitted', attemptCount: 9 });
    const value = api(rows);
    mount(value.client);

    const link = await screen.findByRole('link', { name: /Олимпиадная математика/ });
    expect(link).toHaveAttribute('href', '/study/courses/olympiad-8');
    expect(screen.getByText(course.description)).toBeVisible();
    expect(screen.getByText('8 класс', { selector: '.group-chip' })).toBeVisible();
    expect(screen.getByRole('progressbar', { name: 'Сдано 7 из 24 задач' })).toHaveAttribute('aria-valuenow', '29');
    expect(screen.getByText('29%')).toBeVisible();
    expect(screen.queryByText('Опубликован')).toBeNull();
    expect(screen.queryByText('Доступна опубликованная версия.')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Открыть задания' })).toBeNull();
    expect(link.querySelector('.course-icon')).toBeNull();

    link.focus();
    await userEvent.keyboard('{Enter}');
    expect(screen.getByRole('status', { name: 'Текущий маршрут' })).toHaveTextContent('/study/courses/olympiad-8');
  });

  it('shows the no-task state without a percentage or progressbar', async () => {
    mount(api([]).client);
    expect(await screen.findByText('Пока нет задач')).toBeVisible();
    expect(screen.queryByRole('progressbar')).toBeNull();
    expect(screen.queryByText(/%/)).toBeNull();
  });

  it('keeps teacher cards and avoids loading student assignments', async () => {
    const teacher: CatalogCourse = { ...course, role: 'Teacher', groups: [{ ...course.groups[0]!, studentCount: 12 }], canEditContent: true };
    const value = api([], teacher);
    mount(value.client, 'Teacher');
    expect(await screen.findByText('Опубликован')).toBeVisible();
    expect(screen.getByText('∑')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Материалы и публикация' })).toBeVisible();
    expect(value.request).toHaveBeenCalledTimes(1);
  });

  it('rounds 0, partial and complete progress to an integer', () => {
    expect(studentProgress([assignment(1, 'Submitted')])).toEqual({ accepted: 0, total: 1, percent: 0 });
    expect(studentProgress([assignment(1, 'Accepted'), assignment(2, 'NeedsRevision'), assignment(3, 'Submitted')]))
      .toEqual({ accepted: 1, total: 3, percent: 33 });
    expect(studentProgress([assignment(1, 'Accepted')])).toEqual({ accepted: 1, total: 1, percent: 100 });
  });
});
