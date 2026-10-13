import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { expect, it, vi } from 'vitest';
import { App } from '../App';
import { createApiClient } from '../api/client';
import { decodeContent } from '../api/contracts/workspaceContent';
const versionId = '10000000-0000-0000-0000-000000000001';
const coursePath = '/api/v1/courses/test';
const lessonPath = `${coursePath}/versions/${versionId}/lessons/lesson`;
const problemPath = `${lessonPath}/problems/problem`;
const course = { id: 'test', title: 'Protected course', description: null, lessons: [{ id: 'lesson', versionId, order: 1, title: 'Protected lesson', href: lessonPath }] };
const lesson = { courseId: 'test', versionId, id: 'lesson', order: 1, title: 'Protected lesson', summary: null, problems: [{ id: 'problem', versionId, order: 1, title: 'Protected problem', href: problemPath }] };
const problem = { courseId: 'test', versionId, lessonId: 'lesson', id: 'problem', order: 1, title: 'Protected problem', statementMarkdown: 'Private statement $x=1$' };
const values: Record<string, unknown> = { [coursePath]: course, [lessonPath]: lesson, [problemPath]: problem };
function mount(path: string, response: (path: string) => Response = path => Response.json(values[path])) {
  const unauthenticated = vi.fn();
  const fetch = vi.fn<typeof globalThis.fetch>(async path => response(String(path)));
  const client = createApiClient({ getAccessToken: () => 'memory-token', onUnauthenticated: unauthenticated, fetch });
  render(<MemoryRouter initialEntries={[path.slice('/api/v1'.length)]} future={{ v7_startTransition: true, v7_relativeSplatPath: true }}><App contentReads client={client} /></MemoryRouter>);
  return { fetch, unauthenticated };
}
it('follows exact API links through existing page modes without legacy permissions or write UI', async () => {
  const { fetch } = mount(coursePath);
  await userEvent.click(await screen.findByRole('link', { name: /Protected lesson/ }));
  await userEvent.click(await screen.findByRole('link', { name: /Protected problem/ }));
  await screen.findByRole('heading', { name: 'Условие задачи' });
  expect(document.querySelector('.katex')).not.toBeNull();
  expect(screen.queryByRole('button', { name: /Отправить|Загрузить|Принять решение/ })).toBeNull();
  expect(fetch.mock.calls.map(call => call[0])).toEqual([coursePath, lessonPath, problemPath]);
  for (const [, init] of fetch.mock.calls) {
    expect(init?.cache).toBe('no-store');
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer memory-token');
  }
});
it.each([401, 403, 503])('removes displayed content on an empty HTTP %s response', async status => {
  let denied = false;
  const { unauthenticated } = mount(problemPath, () => denied ? new Response(null, { status }) : Response.json(problem));
  await screen.findByRole('heading', { name: 'Условие задачи' }); denied = true;
  await userEvent.click(screen.getByRole('button', { name: 'Обновить материалы' }));
  await screen.findByRole('alert');
  expect(screen.queryByText(/Private statement/)).toBeNull();
  expect(unauthenticated).toHaveBeenCalledTimes(status === 401 ? 1 : 0);
  expect(screen.queryByRole('button', { name: 'Повторить' }) !== null).toBe(status === 503);
});
it.each(['https://evil.invalid/steal', '//evil.invalid', '/api/v1/courses/foreign/versions/x/lessons/lesson', `${lessonPath}?token=secret`, `${lessonPath}/../other`])('rejects unsafe or mismatched returned link %s', href => {
  expect(() => decodeContent('course', coursePath)({ ...course, lessons: [{ ...course.lessons[0], href }] })).toThrow();
});
it('rejects mismatched content parents and mixed personal/work DTOs', () => {
  expect(() => decodeContent('problem', problemPath)({ ...problem, courseId: 'foreign' })).toThrow();
  expect(() => decodeContent('problem', problemPath)({ ...problem, permissions: { canSubmit: true } })).toThrow();
  expect(() => decodeContent('lesson', lessonPath)({ ...lesson, problems: [{ ...lesson.problems[0], versionId: '20000000-0000-0000-0000-000000000002' }] })).toThrow();
});
it('revalidates a restored document instead of keeping the old statement', async () => {
  let denied = false;
  mount(problemPath, () => denied ? new Response(null, { status: 403 }) : Response.json(problem));
  await screen.findByRole('heading', { name: 'Условие задачи' }); denied = true;
  window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }));
  await screen.findByRole('alert'); expect(screen.queryByText(/Private statement/)).toBeNull();
});
