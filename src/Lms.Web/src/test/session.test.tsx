import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { BrowserSessionController, SessionGate, SessionProvider } from '../auth/session';
import { createSessionObjectUrl } from '../api/sessionResources';

const org = '11111111-1111-1111-1111-111111111111';
const actor = '22222222-2222-2222-2222-222222222222';
function token() {
  const encode = (value: unknown) => btoa(JSON.stringify(value)).replace(/=/g, '').replace(/\+/g, '-').replace(/\//g, '_');
  return `${encode({ alg: 'RS256', typ: 'JWT', kid: 'x'.repeat(64) })}.${encode({ actor, exp: Math.floor(Date.now() / 1000) + 900,
    scope: { Membership: { OrganizationId: org, Role: 0, Permissions: 0 } } })}.signature`;
}
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
function transport() {
  return vi.fn<typeof globalThis.fetch>(async input => {
    const path = String(input);
    if (path.endsWith('/auth/csrf')) return json({ token: 'csrf' });
    if (path.endsWith('/auth/logout')) return new Response(null, { status: 204 });
    return json({ accessToken: token(), hasEducationalScope: true, hasOrganizationAdminScope: false });
  });
}
function mount(controller: BrowserSessionController) {
  return render(<SessionProvider controller={controller}><SessionGate><div>Защищённое приложение</div></SessionGate></SessionProvider>);
}
afterEach(() => {
  sessionStorage.clear(); localStorage.clear(); delete window.lmsAuth; vi.unstubAllGlobals();
});

describe('browser session lifecycle', () => {
  it('shows issued-credential login without public registration or mandatory email', async () => {
    vi.stubGlobal('fetch', transport());
    mount(new BrowserSessionController());
    expect(await screen.findByRole('heading', { name: 'Вход в Сапиентум' })).toBeVisible();
    expect(screen.getByLabelText('Логин')).toHaveAttribute('autocomplete', 'username');
    expect(screen.getByLabelText('Пароль')).toHaveAttribute('autocomplete', 'current-password');
    expect(screen.queryByLabelText(/email/i)).toBeNull();
    expect(screen.queryByText(/зарегистрироваться/i)).toBeNull();
  });

  it('logs in with an in-memory token, clears Blob URLs and revokes server session on logout', async () => {
    const fetch = transport(); vi.stubGlobal('fetch', fetch);
    const revoke = vi.fn(); URL.createObjectURL = vi.fn(() => 'blob:session-photo'); URL.revokeObjectURL = revoke;
    const controller = new BrowserSessionController(); mount(controller);
    await userEvent.type(await screen.findByLabelText('Логин'), 'synthetic-student');
    await userEvent.type(screen.getByLabelText('Пароль'), 'Synthetic-password-123');
    await userEvent.type(screen.getByLabelText('Организация'), org);
    await userEvent.click(screen.getByRole('button', { name: 'Войти' }));
    expect(await screen.findByText('Защищённое приложение')).toBeVisible();
    expect(await window.lmsAuth?.getAccessToken()).toContain('.');
    expect(sessionStorage.getItem('sapientum.organization')).toBe(org);
    expect(Object.values(localStorage).join('')).not.toContain('signature');
    createSessionObjectUrl(new Blob(['photo']));
    await userEvent.click(screen.getByRole('button', { name: 'Выйти' }));
    expect(await screen.findByRole('heading', { name: 'Вход в Сапиентум' })).toBeVisible();
    expect(window.lmsAuth?.getAccessToken()).toBeNull();
    expect(revoke).toHaveBeenCalledWith('blob:session-photo');
    expect(fetch.mock.calls.some(([url]) => String(url).endsWith('/auth/logout'))).toBe(true);
    expect(sessionStorage.getItem('sapientum.organization')).toBeNull();
  });

  it('restores after reload through refresh and reports a network failure without a loop', async () => {
    sessionStorage.setItem('sapientum.organization', org);
    const fetch = transport(); vi.stubGlobal('fetch', fetch);
    mount(new BrowserSessionController());
    expect(await screen.findByText('Защищённое приложение')).toBeVisible();
    expect(fetch.mock.calls.filter(([url]) => String(url).endsWith('/auth/refresh'))).toHaveLength(1);

    sessionStorage.setItem('sapientum.organization', org);
    const offline = vi.fn<typeof globalThis.fetch>(async input => {
      if (String(input).endsWith('/auth/csrf')) return json({ token: 'csrf' });
      throw new TypeError('offline');
    });
    vi.stubGlobal('fetch', offline);
    const second = new BrowserSessionController(); mount(second);
    expect(await screen.findByRole('alert')).toHaveTextContent('временно недоступен');
    await waitFor(() => expect(offline.mock.calls.filter(([url]) => String(url).endsWith('/auth/refresh'))).toHaveLength(1));
  });
});
