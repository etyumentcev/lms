import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { BrowserSessionController, isAdministrativeEntryPath, safeReturnPath, SessionGate, SessionProvider } from '../auth/session';
import { createSessionObjectUrl } from '../api/sessionResources';
import { AppShell } from '../layout/AppShell';

const org = '11111111-1111-1111-1111-111111111111';
const org2 = '33333333-3333-3333-3333-333333333333';
const actor = '22222222-2222-2222-2222-222222222222';
function token(organizationId = org) {
  const encode = (value: unknown) => btoa(JSON.stringify(value)).replace(/=/g, '').replace(/\+/g, '-').replace(/\//g, '_');
  return `${encode({ alg: 'RS256', typ: 'JWT', kid: 'x'.repeat(64) })}.${encode({ actor, exp: Math.floor(Date.now() / 1000) + 900,
    scope: { Membership: { OrganizationId: organizationId, Role: 0, Permissions: 0 } } })}.signature`;
}
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
function transport() {
  return vi.fn<typeof globalThis.fetch>(async input => {
    const path = String(input);
    if (path.endsWith('/auth/csrf')) return json({ token: 'csrf' });
    if (path.endsWith('/auth/logout')) return new Response(null, { status: 204 });
    if (path.endsWith('/auth/refresh') && !localStorage.getItem('sapientum.organization')) return json({}, 401);
    return json({ accessToken: token(), hasEducationalScope: true, hasOrganizationAdminScope: false,
      organizationId: org, organizationName: 'Лицей Эйлера', displayName: 'Тюменцев Евгений', selectionRequired: false,
      organizations: [{ organizationId: org, name: 'Лицей Эйлера' }] });
  });
}
function mount(controller: BrowserSessionController, path = '/login') {
  return render(<MemoryRouter initialEntries={[path]}><SessionProvider controller={controller}><SessionGate><AppShell role="Student" context={null}><div>Защищённое приложение</div></AppShell></SessionGate></SessionProvider></MemoryRouter>);
}
afterEach(() => {
  sessionStorage.clear(); localStorage.clear(); delete window.lmsAuth; vi.unstubAllGlobals();
});

describe('browser session lifecycle', () => {
  it('shows issued-credential login without public registration or mandatory email', async () => {
    vi.stubGlobal('fetch', transport());
    mount(new BrowserSessionController());
    expect(await screen.findByRole('heading', { name: 'Вход' })).toBeVisible();
    expect(screen.getByLabelText('Логин')).toHaveAttribute('autocomplete', 'username');
    expect(screen.getByLabelText('Пароль')).toHaveAttribute('autocomplete', 'current-password');
    expect(screen.queryByLabelText('Организация')).toBeNull();
    expect(screen.queryByLabelText(/email/i)).toBeNull();
    expect(screen.queryByText(/зарегистрироваться/i)).toBeNull();
    expect(screen.queryByText('Помощь')).toBeNull();
    expect(screen.queryByText('RU')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Первый запуск' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Первый администратор' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Оператор безопасности' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Продолжить текущую сессию' })).toBeNull();
    expect(screen.getByRole('button', { name: 'Показать пароль' })).toHaveAttribute('aria-pressed', 'false');
  });

  it('logs in with an in-memory token, clears Blob URLs and revokes server session on logout', async () => {
    const fetch = transport(); vi.stubGlobal('fetch', fetch);
    const revoke = vi.fn(); URL.createObjectURL = vi.fn(() => 'blob:session-photo'); URL.revokeObjectURL = revoke;
    const controller = new BrowserSessionController(); mount(controller);
    await userEvent.type(await screen.findByLabelText('Логин'), 'synthetic-student');
    await userEvent.type(screen.getByLabelText('Пароль'), 'Synthetic-password-123');
    await userEvent.click(screen.getByRole('button', { name: 'Войти' }));
    expect(await screen.findByText('Защищённое приложение')).toBeVisible();
    expect(await window.lmsAuth?.getAccessToken()).toContain('.');
    expect(localStorage.getItem('sapientum.organization')).toBe(org);
    expect(Object.values(localStorage).join('')).not.toContain('signature');
    createSessionObjectUrl(new Blob(['photo']));
    expect(screen.getByRole('link', { name: 'Лицей Эйлера' })).toHaveAttribute('href', '/');
    await userEvent.click(screen.getByRole('button', { name: /Тюменцев Евгений.*Ученик/ }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Выйти' }));
    expect(await screen.findByRole('heading', { name: 'Вход' })).toBeVisible();
    expect(window.lmsAuth?.getAccessToken()).toBeNull();
    expect(revoke).toHaveBeenCalledWith('blob:session-photo');
    expect(fetch.mock.calls.some(([url]) => String(url).endsWith('/auth/logout'))).toBe(true);
    expect(localStorage.getItem('sapientum.organization')).toBeNull();
  });

  it('restores after reload through refresh and reports a network failure without a loop', async () => {
    localStorage.setItem('sapientum.organization', org);
    const fetch = transport(); vi.stubGlobal('fetch', fetch);
    mount(new BrowserSessionController());
    expect(await screen.findByText('Защищённое приложение')).toBeVisible();
    expect(fetch.mock.calls.filter(([url]) => String(url).endsWith('/auth/refresh'))).toHaveLength(1);

    localStorage.setItem('sapientum.organization', org);
    const offline = vi.fn<typeof globalThis.fetch>(async input => {
      if (String(input).endsWith('/auth/csrf')) return json({ token: 'csrf' });
      throw new TypeError('offline');
    });
    vi.stubGlobal('fetch', offline);
    const second = new BrowserSessionController(); mount(second);
    expect(await screen.findByRole('alert')).toHaveTextContent('временно недоступен');
    await waitFor(() => expect(offline.mock.calls.filter(([url]) => String(url).endsWith('/auth/refresh'))).toHaveLength(1));
  });

  it('shows zero-organization message during automatic cookie restore', async () => {
    localStorage.setItem('sapientum.organization', org);
    vi.stubGlobal('fetch', vi.fn<typeof globalThis.fetch>(async input => {
      if (String(input).endsWith('/auth/csrf')) return json({ token: 'csrf' });
      return json({ type: 'about:blank', title: 'Forbidden', status: 403, instance: '/api/v1/auth/refresh',
        code: 'no_available_organizations', traceId: 'trace' }, 403);
    }));
    mount(new BrowserSessionController());
    expect(await screen.findByRole('alert')).toHaveTextContent('Нет доступных организаций. Обратитесь к администратору.');
  });

  it("redirects protected routes to login and returns only to an allowed internal deep link", async () => {
    vi.stubGlobal("fetch", transport());
    mount(new BrowserSessionController(), "/courses/algebra?tab=lessons");
    await userEvent.type(await screen.findByLabelText("Логин"), "synthetic-student");
    await userEvent.type(screen.getByLabelText("Пароль"), "Synthetic-password-123");
    await userEvent.click(screen.getByRole("button", { name: "Войти" }));
    expect(await screen.findByText("Защищённое приложение")).toBeVisible();
    expect(safeReturnPath("https://evil.example/steal")).toBe("/courses");
    expect(safeReturnPath("//evil.example/steal")).toBe("/courses");
    expect(safeReturnPath("/login")).toBe("/courses");
    expect(safeReturnPath("/courses/algebra?tab=lessons")).toBe("/courses/algebra?tab=lessons");
  });

  it('automatically enters a single organization even when no educational JWT can be issued', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof globalThis.fetch>(async input => {
      const path = String(input);
      if (path.endsWith('/auth/csrf')) return json({ token: 'csrf' });
      if (path.endsWith('/auth/refresh')) return json({}, 401);
      return json({ accessToken: null, hasEducationalScope: false, hasOrganizationAdminScope: false,
        organizationId: org, organizationName: 'Лицей Эйлера', displayName: 'Тюменцев Евгений', selectionRequired: false,
        organizations: [{ organizationId: org, name: 'Лицей Эйлера' }] });
    }));
    mount(new BrowserSessionController());
    await userEvent.type(await screen.findByLabelText('Логин'), 'without-course');
    await userEvent.type(screen.getByLabelText('Пароль'), 'Synthetic-password-123');
    await userEvent.click(screen.getByRole('button', { name: 'Войти' }));
    expect(await screen.findByRole('heading', { name: 'Нет доступных курсов' })).toBeVisible();
    expect(screen.getByText('Лицей Эйлера')).toBeVisible();
    expect(screen.queryByRole('heading', { name: 'Выберите организацию' })).toBeNull();
    expect(window.lmsAuth?.getAccessToken()).toBeNull();
  });

  it('selects one of multiple organizations by name without exposing GUIDs', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(async (input) => {
      const path = String(input);
      if (path.endsWith('/auth/csrf')) return json({ token: 'csrf' });
      if (path.endsWith('/auth/refresh')) return json({}, 401);
      if (path.endsWith('/auth/login')) return json({ accessToken: null, hasEducationalScope: false, hasOrganizationAdminScope: false,
        organizationId: null, organizationName: null, displayName: null, selectionRequired: true, organizations: [
          { organizationId: org, name: 'Лицей Эйлера' }, { organizationId: org2, name: 'Школа Лобачевского' }] });
      if (path.endsWith('/auth/switch-organization')) return json({ accessToken: token(org2), hasEducationalScope: true, hasOrganizationAdminScope: false,
        organizationId: org2, organizationName: 'Школа Лобачевского', displayName: 'Тюменцев Евгений', selectionRequired: false, organizations: [
          { organizationId: org, name: 'Лицей Эйлера' }, { organizationId: org2, name: 'Школа Лобачевского' }] });
      if (path.endsWith('/auth/logout')) return new Response(null, { status: 204 });
      return json({}, 404);
    });
    vi.stubGlobal('fetch', fetch); mount(new BrowserSessionController());
    await userEvent.type(await screen.findByLabelText('Логин'), 'multi-user');
    await userEvent.type(screen.getByLabelText('Пароль'), 'Synthetic-password-123');
    await userEvent.click(screen.getByRole('button', { name: 'Войти' }));
    expect(await screen.findByRole('heading', { name: 'Выберите организацию' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Лицей Эйлера' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Школа Лобачевского' })).toBeVisible();
    expect(screen.queryByText(org)).toBeNull(); expect(screen.queryByText(org2)).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Школа Лобачевского' }));
    expect(await screen.findByText('Защищённое приложение')).toBeVisible();
    expect(localStorage.getItem('sapientum.organization')).toBe(org2);
    const loginCall = fetch.mock.calls.find(([url]) => String(url).endsWith('/auth/login'));
    expect(JSON.parse(String(loginCall?.[1]?.body))).toEqual({ userName: 'multi-user', password: 'Synthetic-password-123' });
  });

  it('shows the dedicated zero-organization message after verified credentials', async () => {
    const problem = { type: 'about:blank', title: 'Forbidden', status: 403, instance: '/api/v1/auth/login',
      code: 'no_available_organizations', traceId: 'trace' };
    vi.stubGlobal('fetch', vi.fn<typeof globalThis.fetch>(async input => {
      const path = String(input);
      if (path.endsWith('/auth/csrf')) return json({ token: 'csrf' });
      if (path.endsWith('/auth/refresh')) return json({}, 401);
      return json(problem, 403);
    }));
    mount(new BrowserSessionController());
    await userEvent.type(await screen.findByLabelText('Логин'), 'without-org');
    await userEvent.type(screen.getByLabelText('Пароль'), 'Synthetic-password-123');
    await userEvent.click(screen.getByRole('button', { name: 'Войти' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Нет доступных организаций. Обратитесь к администратору.');
    expect(window.lmsAuth?.getAccessToken()).toBeNull();
  });

  it('serves administrative workflows only on a UUIDv4-shaped server-approved entry route', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(); vi.stubGlobal('fetch', fetch);
    const path = '/12345678-1234-4abc-8def-1234567890ab';
    const rendered = mount(new BrowserSessionController(), path);
    expect(await screen.findByRole('heading', { name: 'Административный вход' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Первый запуск' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Первый администратор' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Оператор безопасности' })).toBeVisible();
    expect(rendered.container).not.toHaveTextContent(path.slice(1));
    expect(fetch).not.toHaveBeenCalled();
    expect(isAdministrativeEntryPath(path)).toBe(true);
    expect(isAdministrativeEntryPath('/12345678-1234-1abc-8def-1234567890ab')).toBe(false);
    expect(isAdministrativeEntryPath(path + "/extra")).toBe(false);
    expect(safeReturnPath(path)).toBe('/courses');
    await userEvent.click(screen.getByRole('button', { name: 'Оператор безопасности' }));
    expect(await screen.findByRole('heading', { name: 'Запросы доступа' })).toBeVisible();
  });
});
