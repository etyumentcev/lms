import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { createApiClient } from '../api/client';
import { decodeReviewHistory, decodeTeacherProgress } from '../api/contracts/progressHistory';
import type { ReviewHistory, TeacherProgress } from '../api/contracts/progressHistory';
import { ProgressPage, ReviewHistoryPage } from '../features/progress/ProgressPage';
import { normalizeProgressQuery } from '../features/progress/query';

const studentId = '10000000-0000-0000-0000-000000000001';
const assignmentId = '20000000-0000-0000-0000-000000000001';
const workId = '30000000-0000-0000-0000-000000000001';
const versionId = '40000000-0000-0000-0000-000000000001';
const reviewId = '50000000-0000-0000-0000-000000000001';
const submissionId = '60000000-0000-0000-0000-000000000001';
const assignment = { assignmentId, problemWorkId: workId, courseVersionId: versionId, courseId: 'math',
  courseTitle: 'Олимпиадная математика', lessonId: 'numbers', lessonTitle: 'Теория чисел',
  problemId: 'divisibility', problemTitle: 'Делимость суммы', status: 'Accepted' as const,
  attemptCount: 2, isActive: true, lastActivityAt: '2026-10-13T10:00:00Z' };
const progress = (): TeacherProgress => ({ items: [{ studentId, studentDisplayName: 'Анна Иванова',
  courseId: 'math', courseTitle: 'Олимпиадная математика', groupId: 'G1', groupTitle: 'Группа 1',
  assignedCount: 5, assignments: [assignment] }], page: 1, pageSize: 50, total: 12,
  summary: { studentCount: 12, assignedCount: 60, notStarted: 51, submitted: 3, needsRevision: 3, accepted: 3 } });
const history = (): ReviewHistory => ({ items: [{ reviewId, problemWorkId: workId, submissionId, studentId,
  studentDisplayName: 'Анна Иванова', courseId: 'math', courseTitle: 'Олимпиадная математика',
  lessonId: 'numbers', lessonTitle: 'Теория чисел', groupId: 'G1', groupTitle: 'Группа 1',
  problemId: 'divisibility', problemTitle: 'Делимость суммы', courseVersionId: versionId,
  attemptNumber: 2, attachmentCount: 2, decision: 'Accepted', workStatus: 'Accepted',
  createdAt: '2026-10-13T10:00:00Z' }], page: 1, pageSize: 50, total: 1,
  summary: { total: 6, needsRevision: 3, accepted: 3 } });
const response = (body: unknown) => new Response(JSON.stringify(body),
  { status: 200, headers: { 'Content-Type': 'application/json' } });
function client(fetch: typeof globalThis.fetch) {
  return createApiClient({ fetch, getAccessToken: () => 'm1-10-token' });
}

describe('M1-10 progress and review history', () => {
  it('shows authoritative 12-student summary, all status counters and immutable work link', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(response(progress()));
    render(<MemoryRouter initialEntries={['/progress?courseId=math&groupId=G1']}>
      <ProgressPage client={client(fetch)} role="Teacher"/></MemoryRouter>);
    expect(await screen.findByText('Анна Иванова')).toBeInTheDocument();
    const context = within(screen.getByRole('complementary'));
    expect(context.getByText('12')).toBeInTheDocument();
    for (const value of ['60', '51', '3']) expect(context.getAllByText(value).length).toBeGreaterThan(0);
    expect(screen.getByLabelText('Статус: Принята')).toBeInTheDocument();
    expect(screen.getByText(new RegExp(versionId + "$"))).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Открыть задачу и историю' }))
      .toHaveAttribute('href', `/teacher/reviews/${workId}`);
    expect(String(fetch.mock.calls[0]![0])).toBe(
      '/api/v1/teacher/group-progress?courseId=math&groupId=G1&page=1&pageSize=50');
  });

  it('sends status filters only after submit and retries only a safe GET', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>()
      .mockRejectedValueOnce(new TypeError('lost response')).mockResolvedValue(response(progress()));
    render(<MemoryRouter initialEntries={['/progress']}><ProgressPage client={client(fetch)} role="Teacher"/></MemoryRouter>);
    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось');
    await userEvent.click(screen.getByRole('button', { name: 'Повторить' }));
    await screen.findByText('Анна Иванова');
    await userEvent.selectOptions(screen.getByLabelText('Статус'), 'Accepted');
    expect(fetch).toHaveBeenCalledTimes(2);
    await userEvent.click(screen.getByRole('button', { name: 'Применить' }));
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(3));
    expect(new URL(String(fetch.mock.calls[2]![0]), 'https://test').searchParams.get('status')).toBe('Accepted');
    expect(fetch.mock.calls.every(([, options]) => options?.method === 'GET')).toBe(true);
  });

  it('shows accepted review history with photos, attempt and return context', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(response(history()));
    render(<MemoryRouter initialEntries={['/teacher/review-history?courseId=math']}>
      <ReviewHistoryPage client={client(fetch)}/></MemoryRouter>);
    expect(await screen.findByText('Попытка №2')).toBeInTheDocument();
    expect(screen.getByText('Фотографий: 2')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Открыть прошлую проверку' })).toHaveAttribute('href',
      `/teacher/reviews/${workId}?attempt=${submissionId}`);
    expect(within(screen.getByRole('complementary')).getByText('Принято: 3')).toBeInTheDocument();
  });

  it('rejects malformed projections and drops unsupported client parameters', () => {
    const invalid = progress(); delete (invalid.summary as Partial<TeacherProgress['summary']>).accepted;
    expect(() => decodeTeacherProgress(invalid)).toThrow();
    const invalidHistory = history(); (invalidHistory.items[0] as unknown as { decision: string }).decision = 'Submitted';
    expect(() => decodeReviewHistory(invalidHistory)).toThrow();
    const query = normalizeProgressQuery(new URLSearchParams('status=forged&studentId=bad&sort=name&page=-1'));
    expect(query.toString()).toBe('page=1&pageSize=50');
  });
});
