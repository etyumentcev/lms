import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApiClient } from '../api/client';
import { ProtectedPhotos } from '../features/protected-work/ProtectedPhotos';
const id = '11111111-1111-1111-1111-111111111111', other = '22222222-2222-2222-2222-222222222222';
const path = `/api/v1/courses/own/versions/${id}/lessons/lesson/works/${id}/submissions/${id}/photos`;
const manifest = () => new Response(JSON.stringify({ items: [id, other].map(id => ({ id, href: `${path}/${id}` })) }));
const photo = () => { const response = new Response(); response.blob = async () => new Blob(['photo'], { type: 'image/png' }); return response; };
afterEach(() => vi.unstubAllGlobals());
describe('protected originals', () => {
  it('fetches pages with bearer and no-store, revokes old bytes when changing page and on unmount', async () => {
    const create = vi.fn().mockReturnValueOnce('blob:one').mockReturnValueOnce('blob:two'), revoke = vi.fn();
    vi.stubGlobal('URL', class extends URL { static createObjectURL = create; static revokeObjectURL = revoke; });
    const fetch = vi.fn().mockResolvedValueOnce(manifest()).mockResolvedValueOnce(photo())
      .mockResolvedValueOnce(photo());
    const client = createApiClient({ getAccessToken: () => 'synthetic-token', fetch });
    const view = render(<ProtectedPhotos path={path} client={client} />);
    await screen.findByRole('img', { name: 'Страница 1 решения' });
    await userEvent.selectOptions(screen.getByLabelText('Масштаб'), '200');
    expect(screen.getByRole('img')).toHaveStyle({ width: '200%' });
    await userEvent.click(screen.getByRole('button', { name: 'Страница 2' }));
    await screen.findByRole('img', { name: 'Страница 2 решения' }); expect(revoke).toHaveBeenCalledWith('blob:one');
    expect(fetch.mock.calls[2]![0]).toBe(`${path}/${other}`);
    for (const [, options] of fetch.mock.calls) {
      expect(options.cache).toBe('no-store'); expect(options.headers.get('Authorization')).toBe('Bearer synthetic-token');
    }
    view.unmount(); expect(revoke).toHaveBeenCalledWith('blob:two');
  });
  it.each([401, 403, 503])('does not render or automatically retry denied/unavailable bytes: %s', async status => {
    const fetch = vi.fn().mockResolvedValueOnce(manifest()).mockResolvedValueOnce(new Response('', { status }));
    render(<ProtectedPhotos path={path} client={createApiClient({ getAccessToken: () => 'token', fetch })} />);
    await screen.findByRole('alert'); expect(screen.queryByRole('img')).toBeNull(); expect(fetch).toHaveBeenCalledTimes(2);
  });
  it('rejects foreign photo links before sending a token or rendering an image', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ items: [{ id, href: 'https://foreign.invalid/photo' }] })));
    render(<ProtectedPhotos path={path} client={createApiClient({ getAccessToken: () => 'token', fetch })} />);
    await screen.findByRole('alert'); await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1)); expect(screen.queryByRole('img')).toBeNull();
  });
});
