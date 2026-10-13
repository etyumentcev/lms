import { createContext, useContext, useEffect, useState, useSyncExternalStore, type FormEvent, type ReactNode } from 'react';
import { ApiError, createApiClient } from '../api/client';
import { resetSessionResources } from '../api/sessionResources';
import './auth.css';
import { FirstAdminActivationPanel, OrganizationAdminDashboard, PlatformBootstrapPanel } from '../features/organization-admin/OrganizationBootstrap';
import { AccountAccessPanel, SecurityOperatorPanel } from '../features/organization-admin/AccountAdministration';

type Role = 'Student' | 'Teacher';
type SessionView = { organizationId: string; actorId: string | null; role: Role | null; permissions: number; expiresAt: number | null; hasEducationalScope: boolean; hasOrganizationAdminScope: boolean; label: string };
export type SessionState =
  | { phase: 'starting' }
  | { phase: 'anonymous'; organizationId: string; message?: string; error?: boolean }
  | { phase: 'busy'; organizationId: string; action: 'login' | 'restore' | 'switch' }
  | { phase: 'authenticated'; session: SessionView; refreshing: boolean; message?: string };
type AuthResponse = { accessToken: string | null; hasEducationalScope: boolean; hasOrganizationAdminScope: boolean };
type TokenDetails = { actorId: string; organizationId: string; role: Role | null; permissions: number; expiresAt: number };
const organizationKey = 'sapientum.organization';
const coordinationKey = 'sapientum.auth.lock';
const channelName = 'sapientum.auth';

function decodeAuth(value: unknown): AuthResponse {
  if (!value || typeof value !== 'object') throw new Error('Invalid auth response');
  const candidate = value as Record<string, unknown>;
  if ((typeof candidate.accessToken !== 'string' && candidate.accessToken !== null) || typeof candidate.hasEducationalScope !== 'boolean' ||
      typeof candidate.hasOrganizationAdminScope !== 'boolean')
    throw new Error('Invalid auth response');
  return candidate as AuthResponse;
}
function tokenDetails(token: string): TokenDetails | null {
  try {
    const parts = token.split('.');
    if (parts.length !== 3) return null;
    const encoded = parts[1]!.replace(/-/g, '+').replace(/_/g, '/');
    const payload = JSON.parse(atob(encoded + '='.repeat((4 - encoded.length % 4) % 4))) as Record<string, unknown>;
    const membership = (payload.scope as { Membership?: Record<string, unknown> } | undefined)?.Membership;
    const encodedRole = membership?.Role;
    const role: Role | null = encodedRole === 0 || encodedRole === 'Student' ? 'Student'
      : encodedRole === 1 || encodedRole === 'Teacher' ? 'Teacher' : null;
    const organizationId = membership?.OrganizationId;
    const permissions = membership?.Permissions;
    if (typeof payload.actor !== 'string' || typeof payload.exp !== 'number' || typeof organizationId !== 'string' ||
        typeof permissions !== 'number' || (role === null && (permissions & 1) === 0)) return null;
    return { actorId: payload.actor, organizationId, role, permissions, expiresAt: payload.exp };
  } catch { return null; }
}
function storedOrganization() { try { return sessionStorage.getItem(organizationKey) ?? ''; } catch { return ''; } }
function storeOrganization(value: string) { try { if (value) sessionStorage.setItem(organizationKey, value); else sessionStorage.removeItem(organizationKey); } catch { /* optional reload hint */ } }
function failureMessage(error: unknown, fallback: string) {
  if (!(error instanceof ApiError)) return fallback;
  if (error.status === 0 || error.status >= 500) return 'Сервис временно недоступен. Результат запроса неизвестен; повторите восстановление сессии.';
  if (error.status === 409) return 'Сессия уже обновлена в другой вкладке. Повторите восстановление, если вход не появился.';
  return 'Сессия истекла или учётные данные и организация не подтверждены.';
}

export class BrowserSessionController {
  private state: SessionState = { phase: 'starting' };
  private token: string | null = null;
  private listeners = new Set<() => void>();
  private started = false;
  private refreshTask: Promise<void> | null = null;
  private timer: number | undefined;
  private channel?: BroadcastChannel;
  private readonly id = crypto.randomUUID();
  private readonly auth = createApiClient({ getAccessToken: () => null, sessionScoped: false });
  subscribe = (listener: () => void) => { this.listeners.add(listener); return () => this.listeners.delete(listener); };
  snapshot = () => this.state;
  getAccessToken = () => this.token;
  private publish(state: SessionState) { this.state = state; for (const listener of this.listeners) listener(); }

  start() {
    if (this.started) return;
    this.started = true;
    window.lmsAuth = { getAccessToken: this.getAccessToken, onUnauthenticated: () => void this.recover() };
    if ('BroadcastChannel' in window) {
      this.channel = new BroadcastChannel(channelName);
      this.channel.onmessage = event => {
        if (event.data?.sender === this.id) return;
        if (event.data?.type === 'logout' || event.data?.type === 'session-replaced') this.clearLocal('Сессия завершена в другой вкладке.', true);
      };
    }
    const organizationId = storedOrganization();
    if (organizationId) void this.restore(organizationId);
    else this.publish({ phase: 'anonymous', organizationId: '' });
  }
  async login(userName: string, password: string, organizationId: string) {
    this.replaceContext();
    this.publish({ phase: 'busy', organizationId, action: 'login' });
    try {
      const result = await this.auth.request('/auth/login', decodeAuth, { method: 'POST', csrf: true, body: { userName, password, organizationId } });
      this.accept(result, organizationId, userName);
      this.channel?.postMessage({ type: 'session-replaced', sender: this.id });
    } catch (error) {
      this.publish({ phase: 'anonymous', organizationId, message: failureMessage(error, 'Не удалось выполнить вход.'), error: true });
    }
  }
  async restore(organizationId: string) {
    if (!organizationId) { this.publish({ phase: 'anonymous', organizationId: '' }); return; }
    this.publish({ phase: 'busy', organizationId, action: 'restore' });
    await this.rotate('/auth/refresh', organizationId, false);
  }
  async switchOrganization(organizationId: string) {
    if (!organizationId) return;
    this.replaceContext();
    this.publish({ phase: 'busy', organizationId, action: 'switch' });
    await this.rotate('/auth/switch-organization', organizationId, false);
  }
  recover = async () => {
    const organizationId = this.state.phase === 'authenticated' ? this.state.session.organizationId
      : this.state.phase === 'starting' ? storedOrganization() : this.state.organizationId;
    if (!organizationId || this.refreshTask) return this.refreshTask ?? undefined;
    resetSessionResources();
    this.token = null;
    this.publish({ phase: 'busy', organizationId, action: 'restore' });
    this.refreshTask = this.rotate('/auth/refresh', organizationId, true).finally(() => { this.refreshTask = null; });
    return this.refreshTask;
  };
  private async rotate(path: '/auth/refresh' | '/auth/switch-organization', organizationId: string, automatic: boolean) {
    try {
      await this.exclusive(async () => {
        const result = await this.auth.request(path, decodeAuth, { method: 'POST', csrf: true, body: { organizationId } });
        this.accept(result, organizationId);
      });
    } catch (error) {
      this.token = null; this.cancelTimer();
      this.publish({ phase: 'anonymous', organizationId, message: failureMessage(error, automatic ? 'Не удалось обновить истёкшую сессию.' : 'Не удалось восстановить сессию.'), error: true });
    }
  }
  async logout() {
    this.clearLocal(undefined, false);
    this.channel?.postMessage({ type: 'logout', sender: this.id });
    try {
      await this.auth.request('/auth/logout', value => { if (value !== undefined) throw new Error('Invalid logout response'); }, { method: 'POST', csrf: true });
    } catch (error) {
      this.publish({ phase: 'anonymous', organizationId: '', message: failureMessage(error, 'Локальные данные очищены, но серверный выход не подтверждён.'), error: true });
    }
  }
  private accept(result: AuthResponse, requestedOrganization: string, label = 'Пользователь') {
    const details = result.accessToken ? tokenDetails(result.accessToken) : null;
    if ((result.hasEducationalScope || result.hasOrganizationAdminScope) && (!result.accessToken || !details || details.organizationId !== requestedOrganization)) throw new ApiError(200, 'InvalidResponse');
    this.token = result.accessToken; storeOrganization(requestedOrganization);
    const session: SessionView = { organizationId: requestedOrganization, actorId: details?.actorId ?? null, role: details?.role ?? null,
      permissions: details?.permissions ?? 0, expiresAt: details?.expiresAt ?? null, hasEducationalScope: result.hasEducationalScope,
      hasOrganizationAdminScope: result.hasOrganizationAdminScope, label };
    this.publish({ phase: 'authenticated', session, refreshing: false });
    this.schedule(session);
  }
  private schedule(session: SessionView) {
    this.cancelTimer();
    if (!session.expiresAt || (!session.hasEducationalScope && !session.hasOrganizationAdminScope)) return;
    this.timer = window.setTimeout(() => void this.recover(), Math.max(1000, session.expiresAt * 1000 - Date.now() - 30_000));
  }
  private cancelTimer() { if (this.timer !== undefined) window.clearTimeout(this.timer); this.timer = undefined; }
  private replaceContext() { this.token = null; this.cancelTimer(); resetSessionResources(); }
  private clearLocal(text?: string, keepOrganization = false) {
    this.replaceContext();
    const organizationId = keepOrganization ? storedOrganization() : '';
    if (!keepOrganization) storeOrganization('');
    this.publish({ phase: 'anonymous', organizationId, message: text });
  }
  private async exclusive<T>(work: () => Promise<T>): Promise<T> {
    if (navigator.locks) return navigator.locks.request('sapientum-refresh-session', () => work());
    try { localStorage.getItem(coordinationKey); } catch { return work(); }
    const until = Date.now() + 5000;
    while (Date.now() < until) {
      const now = Date.now(); let current: { owner?: string; expires?: number } = {};
      try { current = JSON.parse(localStorage.getItem(coordinationKey) ?? '{}'); } catch { /* replace malformed coordination data */ }
      if (!current.owner || !current.expires || current.expires <= now) {
        localStorage.setItem(coordinationKey, JSON.stringify({ owner: this.id, expires: now + 5000 }));
        await new Promise(resolve => window.setTimeout(resolve, 20));
        const confirmed = JSON.parse(localStorage.getItem(coordinationKey) ?? '{}') as { owner?: string };
        if (confirmed.owner === this.id) {
          try { return await work(); }
          finally {
            const latest = JSON.parse(localStorage.getItem(coordinationKey) ?? '{}') as { owner?: string };
            if (latest.owner === this.id) localStorage.removeItem(coordinationKey);
          }
        }
      }
      await new Promise(resolve => window.setTimeout(resolve, 40));
    }
    throw new ApiError(0, 'CoordinationUnavailable');
  }
}

export const browserSession = new BrowserSessionController();
const SessionContext = createContext(browserSession);
export function SessionProvider({ children, controller = browserSession }: { children: ReactNode; controller?: BrowserSessionController }) {
  useEffect(() => controller.start(), [controller]);
  return <SessionContext.Provider value={controller}>{children}</SessionContext.Provider>;
}
export function useSession() {
  const controller = useContext(SessionContext);
  return { state: useSyncExternalStore(controller.subscribe, controller.snapshot), controller };
}
function Brand() { return <div className="auth-brand"><svg aria-hidden="true" viewBox="0 0 58 34" fill="none"><path d="M4 18h13V7h14v20h8" stroke="currentColor" strokeWidth="3.5" strokeLinecap="round" strokeLinejoin="round"/><circle cx="4" cy="18" r="4" fill="currentColor"/><circle cx="17" cy="7" r="4" fill="currentColor"/><circle cx="31" cy="7" r="4" fill="currentColor"/><circle cx="31" cy="27" r="4" fill="currentColor"/><rect x="38" y="12" width="17" height="17" rx="4" stroke="currentColor" strokeWidth="3.5"/><path d="M50 17h-6.5a3 3 0 0 0 0 6H50" stroke="currentColor" strokeWidth="3" strokeLinecap="round"/></svg><strong>сапиентум</strong></div>; }
type LoginState = Extract<SessionState, { phase: 'anonymous' | 'busy' }>;
function LoginScreen({ state, controller }: { state: LoginState; controller: BrowserSessionController }) {
  const [mode, setMode] = useState<'login' | 'bootstrap' | 'activate' | 'account-activation' | 'recovery' | 'security'>('login');
  const [activation, setActivation] = useState({ organizationId: '', userName: '', code: '' });
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [organizationId, setOrganizationId] = useState(state.organizationId);
  const [passwordVisible, setPasswordVisible] = useState(false);
  const busy = state.phase === 'busy';
  const notice = state.phase === 'anonymous' ? state.message : undefined;
  const isError = state.phase === 'anonymous' && state.error;
  async function submit(event: FormEvent) { event.preventDefault(); await controller.login(userName, password, organizationId.trim()); }
  if (mode === 'bootstrap') return <PlatformBootstrapPanel onBack={() => setMode('login')} onActivate={(organizationId, userName, code) => { setActivation({ organizationId, userName, code }); setMode('activate'); }} />;
  if (mode === 'activate') return <FirstAdminActivationPanel initialOrganization={activation.organizationId} initialUserName={activation.userName} initialCode={activation.code} onBack={() => setMode('login')} />;
  if (mode === 'account-activation') return <AccountAccessPanel mode="activation" onBack={() => setMode('login')} />;
  if (mode === 'recovery') return <AccountAccessPanel mode="recovery" onBack={() => setMode('login')} />;
  if (mode === 'security') return <SecurityOperatorPanel onBack={() => setMode('login')} />;
  return <main className="auth-page"><div className="auth-shell">
    <header className="auth-utility" aria-label="Справка и язык"><a href="#auth-help">?<span>Помощь</span></a><span aria-hidden="true"/><button type="button">RU⌄</button></header>
    <section className="auth-hero" aria-labelledby="auth-hero-title">
      <Brand /><div className="auth-hero-copy"><h2 id="auth-hero-title">Олимпиадная<br/>математика</h2><p>Задачи, кружки, прогресс<br/>и обратная связь в одном месте</p></div>
    </section>
    <section className="auth-card" aria-labelledby="login-heading">
      <h1 id="login-heading">Вход в Сапиентум</h1>
      <p className="auth-intro">Используйте учётные данные, выданные администратором вашей образовательной организации.</p>
      {notice && <p className={isError ? 'auth-message auth-message--error' : 'auth-message'} role={isError ? 'alert' : 'status'}>{notice}</p>}
      <form onSubmit={submit} className="auth-form">
        <label htmlFor="auth-user">Логин</label><div className="auth-input"><span aria-hidden="true">♙</span><input id="auth-user" name="username" autoComplete="username" required placeholder="Введите логин" value={userName} onChange={e => setUserName(e.target.value)} /></div>
        <label htmlFor="auth-password">Пароль</label><div className="auth-input"><span aria-hidden="true">▢</span><input id="auth-password" name="password" type={passwordVisible ? 'text' : 'password'} autoComplete="current-password" required minLength={12} placeholder="Введите пароль" value={password} onChange={e => setPassword(e.target.value)} /><button type="button" aria-label={passwordVisible ? 'Скрыть пароль' : 'Показать пароль'} onClick={() => setPasswordVisible(value => !value)}>{passwordVisible ? '◉' : '◌'}</button></div>
        <label htmlFor="auth-org">Организация</label><div className="auth-input"><span aria-hidden="true">◇</span><input id="auth-org" name="organization" required placeholder="Идентификатор организации" value={organizationId} onChange={e => setOrganizationId(e.target.value)} /></div>
        <button className="button button--primary auth-submit" disabled={busy}>{busy && state.action === 'login' ? 'Входим…' : 'Войти'}</button>
      </form>
      <div className="auth-divider"><span>или</span></div>
      <button className="button button--secondary auth-continue" disabled={busy || !organizationId.trim()} onClick={() => void controller.restore(organizationId.trim())}>
        {busy && state.action === 'restore' ? 'Восстанавливаем…' : 'Продолжить текущую сессию'}</button>
      <div className="auth-help" id="auth-help"><span aria-hidden="true">i</span><p><strong>Нет учётных данных?</strong>Обратитесь к администратору вашей образовательной организации. Доступ предоставляется индивидуально, публичной регистрации нет.</p></div>
      <div className="bootstrap-links"><button type="button" onClick={() => setMode('account-activation')}>У меня есть код активации</button><button type="button" onClick={() => setMode('recovery')}>Восстановить доступ</button><button type="button" onClick={() => setMode('activate')}>Первый администратор</button><button type="button" onClick={() => setMode('bootstrap')}>Первый запуск</button><button type="button" onClick={() => setMode('security')}>Оператор безопасности</button></div>
    </section>
  </div></main>;
}
function SessionToolbar({ session, controller }: { session: SessionView; controller: BrowserSessionController }) {
  const [organizationId, setOrganizationId] = useState(session.organizationId);
  return <aside className="session-toolbar" aria-label="Управление сессией"><div><strong>{session.label}</strong><span>{session.role === 'Teacher' ? 'Преподаватель' : session.role === 'Student' ? 'Ученик' : 'Нет учебных полномочий'}</span></div>
    <details><summary>Организация</summary><label htmlFor="session-org" className="sr-only">Идентификатор организации</label>
      <input id="session-org" value={organizationId} onChange={e => setOrganizationId(e.target.value)} />
      <button className="button button--secondary" disabled={!organizationId || organizationId === session.organizationId} onClick={() => void controller.switchOrganization(organizationId)}>Переключить</button></details>
    <button className="button button--secondary" onClick={() => void controller.logout()}>Выйти</button></aside>;
}
export function SessionGate({ children }: { children: ReactNode }) {
  const { state, controller } = useSession();
  if (state.phase === 'starting') return <main className="auth-page"><p role="status">Проверяем сессию…</p></main>;
  if (state.phase !== 'authenticated') return <LoginScreen state={state} controller={controller} />;
  if (state.session.hasOrganizationAdminScope) return <><SessionToolbar session={state.session} controller={controller} /><OrganizationAdminDashboard /></>;
  if (!state.session.hasEducationalScope) return <main className="auth-page"><section className="auth-card"><Brand /><h1>Нет доступных курсов</h1>
    <p>Сессия подтверждена, но для выбранной организации нет действующих учебных полномочий.</p>
    <SessionToolbar session={state.session} controller={controller} /></section></main>;
  return <><SessionToolbar session={state.session} controller={controller} />{children}</>;
}

declare global { interface Window { lmsAuth?: { getAccessToken: () => string | null | Promise<string | null>; onUnauthenticated: () => void } } }
