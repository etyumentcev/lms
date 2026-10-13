import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { createApiClient } from '../api/client';
import { decodeWork } from '../api/contracts/protectedWork';
import { ProtectedWorkPage } from '../features/protected-work/ProtectedWorkPage';
const id = '11111111-1111-1111-1111-111111111111', second = '22222222-2222-2222-2222-222222222222';
const root = `/api/v1/courses/own/versions/${id}/lessons/lesson`, path = `${root}/works/${id}`;
const data = () => ({ id, status: 'Submitted', statementHref: `${root}/problems/problem`, attempts: [
  { id, number: 1, bodyMarkdown: 'First solution', submittedAt: '2026-10-12T00:00:00Z', photoCount: 2, photoAvailability: 'unavailable', reviews: [
    { id, decision: 'NeedsRevision', feedbackMarkdown: 'First feedback', createdAt: '2026-10-12T00:01:00Z' }] },
  { id: second, number: 2, bodyMarkdown: 'Second solution', submittedAt: '2026-10-12T00:02:00Z', photoCount: 0, photoAvailability: 'unavailable', reviews: [] },
], messages: [] });
function setup(fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(data())))) {
  const client = createApiClient({ getAccessToken: () => 'test', fetch });
  render(<MemoryRouter><ProtectedWorkPage path={path} detail client={client} /></MemoryRouter>);
  return fetch;
}
describe('isolated protected work', () => {
  it('selects old attempt and its feedback without issuing writes or loading blocked files', async () => {
    const fetch = setup(); await screen.findByText('Second solution');
    expect(screen.queryByText('First feedback')).not.toBeInTheDocument();
    await userEvent.selectOptions(screen.getByLabelText('Попытка'), id);
    expect(screen.getByText('First feedback')).toBeVisible(); expect(screen.getByText('Фотографии (2) сейчас недоступны.')).toBeVisible();
    expect(fetch).toHaveBeenCalledTimes(1); expect(document.querySelector('img')).toBeNull();
    expect(screen.getByRole('link', { name: 'Условие задачи' })).toHaveAttribute('href', `${root.slice(7)}/problems/problem`);
  });
  it.each([401, 403])('clears prior private content after denial %s', async status => {
    const fetch = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify(data()))).mockResolvedValueOnce(new Response('', { status }));
    setup(fetch); await screen.findByText('Second solution'); await userEvent.click(screen.getByRole('button', { name: 'Обновить работу' }));
    await screen.findByText(status === 401 ? 'Войдите, чтобы открыть работу' : 'Нет доступа к работе');
    expect(screen.queryByText('Second solution')).not.toBeInTheDocument(); expect(fetch).toHaveBeenCalledTimes(2);
  });
  it('re-reads restored pages and removes stale data on network error', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify(data()))).mockRejectedValueOnce(new Error('offline'));
    setup(fetch); await screen.findByText('Second solution'); fireEvent(window, new PageTransitionEvent('pageshow', { persisted: true }));
    await screen.findByText('Не удалось загрузить работу'); expect(screen.queryByText('Second solution')).not.toBeInTheDocument();
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2));
  });
  it('rejects extra private fields, external links, swapped parents and invalid history order', () => {
    for (const value of [{ ...data(), storageKey: 'secret' }, { ...data(), id: second }, { ...data(), statementHref: 'https://other.invalid' },
      { ...data(), statementHref: `${root}/problems/problem?token=secret` }, { ...data(), attempts: data().attempts.reverse() }])
      expect(() => decodeWork(path, true)(value)).toThrow();
    expect(() => decodeWork(`${root}/works`, false)({ items: [{ id, problemId: 'p', status: 'Submitted', href: '/api/v1/other' }] })).toThrow();
  });
});
