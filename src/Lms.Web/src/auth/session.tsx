import { createContext, useContext, useEffect, useState, useSyncExternalStore, type FormEvent, type ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { ApiError, createApiClient } from '../api/client';
import { resetSessionResources } from '../api/sessionResources';
import './auth.css';
import { FirstAdminActivationPanel, OrganizationAdminDashboard, PlatformBootstrapPanel } from '../features/organization-admin/OrganizationBootstrap';
import { AccountAccessPanel, SecurityOperatorPanel } from '../features/organization-admin/AccountAdministration';

type Role = 'Student' | 'Teacher';
type OrganizationOption = { organizationId: string; name: string };
type SessionView = { organizationId: string; organizationName: string; organizations: OrganizationOption[]; actorId: string | null; role: Role | null; permissions: number; expiresAt: number | null; hasEducationalScope: boolean; hasOrganizationAdminScope: boolean; label: string };
export type SessionState =
  | { phase: 'starting' }
  | { phase: 'anonymous'; organizationId: string; message?: string; error?: boolean }
  | { phase: 'busy'; organizationId: string; action: 'login' | 'restore' | 'switch' }
  | { phase: 'selecting'; organizations: OrganizationOption[]; message?: string }
  | { phase: 'authenticated'; session: SessionView; refreshing: boolean; message?: string };
type AuthResponse = { accessToken: string | null; hasEducationalScope: boolean; hasOrganizationAdminScope: boolean; organizationId: string | null; organizationName: string | null; selectionRequired: boolean; organizations: OrganizationOption[] };
type TokenDetails = { actorId: string; organizationId: string; role: Role | null; permissions: number; expiresAt: number };
const organizationKey = 'sapientum.organization';
const coordinationKey = 'sapientum.auth.lock';
const channelName = 'sapientum.auth';

function decodeAuth(value: unknown): AuthResponse {
  if (!value || typeof value !== 'object') throw new Error('Invalid auth response');
  const candidate = value as Record<string, unknown>;
  const organizations = candidate.organizations;
  if ((typeof candidate.accessToken !== 'string' && candidate.accessToken !== null) ||
      typeof candidate.hasEducationalScope !== 'boolean' || typeof candidate.hasOrganizationAdminScope !== 'boolean' ||
      (typeof candidate.organizationId !== 'string' && candidate.organizationId !== null) ||
      (typeof candidate.organizationName !== 'string' && candidate.organizationName !== null) ||
      typeof candidate.selectionRequired !== 'boolean' || !Array.isArray(organizations) ||
      organizations.some(item => !item || typeof item !== 'object' ||
        typeof (item as Record<string, unknown>).organizationId !== 'string' || typeof (item as Record<string, unknown>).name !== 'string'))
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
function storedOrganization() {
  try {
    const stored = localStorage.getItem(organizationKey);
    const legacy = sessionStorage.getItem(organizationKey);
    sessionStorage.removeItem(organizationKey);
    if (stored) return stored;
    if (legacy) { localStorage.setItem(organizationKey, legacy); return legacy; }
  } catch { /* optional reload hint */ }
  return '';
}
function storeOrganization(value: string) { try { if (value) localStorage.setItem(organizationKey, value); else localStorage.removeItem(organizationKey); } catch { /* optional reload hint */ } }
function failureMessage(error: unknown, fallback: string) {
  if (!(error instanceof ApiError)) return fallback;
  if (error.status === 0 || error.status >= 500) return 'Сервис временно недоступен. Результат запроса неизвестен; повторите восстановление сессии.';
  if (error.code === 'no_available_organizations') return 'Нет доступных организаций. Обратитесь к администратору.';
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
    void this.restore(organizationId, true);
  }
  async login(userName: string, password: string) {
    this.replaceContext();
    this.publish({ phase: 'busy', organizationId: '', action: 'login' });
    try {
      const result = await this.auth.request('/auth/login', decodeAuth, { method: 'POST', csrf: true, body: { userName, password } });
      this.accept(result, userName);
      this.channel?.postMessage({ type: 'session-replaced', sender: this.id });
    } catch (error) {
      this.publish({ phase: 'anonymous', organizationId: '', message: failureMessage(error, 'Не удалось выполнить вход.'), error: true });
    }
  }
  async restore(organizationId = '', silent = false) {
    this.publish({ phase: 'busy', organizationId, action: 'restore' });
    await this.rotate('/auth/refresh', organizationId, false, silent);
  }
  async switchOrganization(organizationId: string) {
    if (!organizationId) return;
    this.replaceContext();
    this.publish({ phase: 'busy', organizationId, action: 'switch' });
    await this.rotate('/auth/switch-organization', organizationId, false);
  }
  recover = async () => {
    const organizationId = this.state.phase === 'authenticated' ? this.state.session.organizationId : storedOrganization();
    if (this.refreshTask) return this.refreshTask;
    resetSessionResources();
    this.token = null;
    this.publish({ phase: 'busy', organizationId, action: 'restore' });
    this.refreshTask = this.rotate('/auth/refresh', organizationId, true).finally(() => { this.refreshTask = null; });
    return this.refreshTask;
  };
  private async rotate(path: '/auth/refresh' | '/auth/switch-organization', organizationId: string, automatic: boolean, silent = false) {
    try {
      await this.exclusive(async () => {
        const result = await this.auth.request(path, decodeAuth, { method: 'POST', csrf: true, body: { organizationId: organizationId || null } });
        this.accept(result);
      });
    } catch (error) {
      this.token = null; this.cancelTimer();
      if (silent && error instanceof ApiError &&
          (error.status === 401 || (error.status === 403 && error.code !== 'no_available_organizations'))) {
        storeOrganization('');
        this.publish({ phase: 'anonymous', organizationId: '' });
        return;
      }
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
  private accept(result: AuthResponse, label = 'Пользователь') {
    const identifiers = result.organizations.map(item => item.organizationId);
    if (new Set(identifiers).size !== identifiers.length || result.organizations.some(item => !item.organizationId || !item.name.trim()))
      throw new ApiError(200, 'InvalidResponse');
    const selected = result.organizations.find(item => item.organizationId === result.organizationId);
    if (!result.accessToken) {
      if (result.hasEducationalScope || result.hasOrganizationAdminScope) throw new ApiError(200, 'InvalidResponse');
      if (result.organizationId || result.organizationName) {
        if (!result.organizationId || !result.organizationName || !selected || selected.name !== result.organizationName ||
            result.selectionRequired) throw new ApiError(200, 'InvalidResponse');
        this.token = null; storeOrganization(result.organizationId); this.cancelTimer();
        this.publish({ phase: 'authenticated', refreshing: false, session: { organizationId: result.organizationId,
          organizationName: result.organizationName, organizations: result.organizations, actorId: null, role: null,
          permissions: 0, expiresAt: null, hasEducationalScope: false, hasOrganizationAdminScope: false, label } });
        return;
      }
      if (result.selectionRequired !== (result.organizations.length > 0)) throw new ApiError(200, 'InvalidResponse');
      this.token = null; storeOrganization(''); this.cancelTimer();
      this.publish({ phase: 'selecting', organizations: result.organizations,
        message: result.organizations.length === 0 ? 'Нет доступных организаций. Обратитесь к администратору.' : undefined });
      return;
    }
    const details = tokenDetails(result.accessToken);
    if (!details || !result.organizationId || !result.organizationName || !selected || selected.name !== result.organizationName ||
        details.organizationId !== result.organizationId || result.selectionRequired) throw new ApiError(200, 'InvalidResponse');
    this.token = result.accessToken; storeOrganization(result.organizationId);
    const session: SessionView = { organizationId: result.organizationId, organizationName: result.organizationName, organizations: result.organizations,
      actorId: details.actorId, role: details.role, permissions: details.permissions, expiresAt: details.expiresAt,
      hasEducationalScope: result.hasEducationalScope, hasOrganizationAdminScope: result.hasOrganizationAdminScope, label };
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
  const location = useLocation();
  useEffect(() => { if (!isAdministrativeEntryPath(location.pathname)) controller.start(); }, [controller, location.pathname]);
  return <SessionContext.Provider value={controller}>{children}</SessionContext.Provider>;
}
export function useSession() {
  const controller = useContext(SessionContext);
  return { state: useSyncExternalStore(controller.subscribe, controller.snapshot), controller };
}
function Brand() { return <div className="auth-brand"><svg aria-hidden="true" viewBox="0 0 58 34" fill="none"><path d="M4 18h13V7h14v20h8" stroke="currentColor" strokeWidth="3.5" strokeLinecap="round" strokeLinejoin="round"/><circle cx="4" cy="18" r="4" fill="currentColor"/><circle cx="17" cy="7" r="4" fill="currentColor"/><circle cx="31" cy="7" r="4" fill="currentColor"/><circle cx="31" cy="27" r="4" fill="currentColor"/><rect x="38" y="12" width="17" height="17" rx="4" stroke="currentColor" strokeWidth="3.5"/><path d="M50 17h-6.5a3 3 0 0 0 0 6H50" stroke="currentColor" strokeWidth="3" strokeLinecap="round"/></svg><strong>сапиентум</strong></div>; }
function AuthIcon({ name }: { name: 'user' | 'lock' | 'eye' | 'eye-off' }) {
  const paths = name === 'user'
    ? <><circle cx="12" cy="8" r="4"/><path d="M4.5 21a7.5 7.5 0 0 1 15 0"/></>
    : name === 'lock'
      ? <><rect x="4" y="10" width="16" height="11" rx="2"/><path d="M8 10V7a4 4 0 0 1 8 0v3"/></>
      : name === 'eye'
        ? <><path d="M2.5 12s3.5-6 9.5-6 9.5 6 9.5 6-3.5 6-9.5 6-9.5-6-9.5-6Z"/><circle cx="12" cy="12" r="2.5"/></>
        : <><path d="m3 3 18 18"/><path d="M10.6 6.1A10.7 10.7 0 0 1 12 6c6 0 9.5 6 9.5 6a15 15 0 0 1-2.2 2.8M6.2 6.2C3.8 7.8 2.5 12 2.5 12s3.5 6 9.5 6c1.2 0 2.3-.2 3.3-.6"/></>;
  return <svg className="auth-icon" aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">{paths}</svg>;
}
type LoginState = Extract<SessionState, { phase: 'anonymous' | 'busy' }>;
function LoginScreen({ state, controller }: { state: LoginState; controller: BrowserSessionController }) {
  const [mode, setMode] = useState<'login' | 'account-activation' | 'recovery'>('login');
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [passwordVisible, setPasswordVisible] = useState(false);
  const busy = state.phase === 'busy';
  const notice = state.phase === 'anonymous' ? state.message : undefined;
  const isError = state.phase === 'anonymous' && state.error;
  async function submit(event: FormEvent) { event.preventDefault(); await controller.login(userName, password); }
  if (mode === 'account-activation') return <AccountAccessPanel mode="activation" onBack={() => setMode('login')} />;
  if (mode === 'recovery') return <AccountAccessPanel mode="recovery" onBack={() => setMode('login')} />;
  return <main className="auth-page"><div className="auth-shell">
    <section className="auth-hero" aria-labelledby="auth-hero-title">
      <Brand /><div className="auth-hero-copy"><h2 id="auth-hero-title">Олимпиадная<br/>математика</h2><p>Задачи, кружки, прогресс<br/>и обратная связь в одном месте</p></div>
    </section>
    <section className="auth-card" aria-labelledby="login-heading">
      <h1 id="login-heading">Вход</h1>
      <p className="auth-intro">Используйте учётные данные, выданные администратором вашей образовательной организации.</p>
      {notice && <p className={isError ? 'auth-message auth-message--error' : 'auth-message'} role={isError ? 'alert' : 'status'}>{notice}</p>}
      <form onSubmit={submit} className="auth-form">
        <label htmlFor="auth-user">Логин</label><div className="auth-input"><span aria-hidden="true"><AuthIcon name="user" /></span><input id="auth-user" name="username" autoComplete="username" required placeholder="Введите логин" value={userName} onChange={e => setUserName(e.target.value)} /></div>
        <label htmlFor="auth-password">Пароль</label><div className="auth-input"><span aria-hidden="true"><AuthIcon name="lock" /></span><input id="auth-password" name="password" type={passwordVisible ? 'text' : 'password'} autoComplete="current-password" required minLength={12} placeholder="Введите пароль" value={password} onChange={e => setPassword(e.target.value)} /><button type="button" aria-label={passwordVisible ? 'Скрыть пароль' : 'Показать пароль'} aria-pressed={passwordVisible} onClick={() => setPasswordVisible(value => !value)}><AuthIcon name={passwordVisible ? 'eye-off' : 'eye'} /></button></div>
        <button className="button button--primary auth-submit" disabled={busy}>{busy && state.action === 'login' ? 'Входим…' : 'Войти'}</button>
      </form>
      <div className="auth-help" id="auth-help"><span aria-hidden="true">i</span><p><strong>Нет учётных данных?</strong>Обратитесь к администратору вашей образовательной организации. Доступ предоставляется индивидуально, публичной регистрации нет.</p></div>
      <div className="bootstrap-links"><button type="button" onClick={() => setMode('account-activation')}>У меня есть код активации</button><button type="button" onClick={() => setMode('recovery')}>Восстановить доступ</button></div>
    </section>
  </div></main>;
}
function OrganizationSelectionScreen({ state, controller }: { state: Extract<SessionState, { phase: 'selecting' }>; controller: BrowserSessionController }) {
  return <main className="auth-page auth-loading"><section className="auth-card organization-picker" aria-labelledby="organization-heading"><Brand />
    <h1 id="organization-heading">{state.organizations.length ? 'Выберите организацию' : 'Нет доступных организаций'}</h1>
    <p>{state.message ?? 'Для этой учётной записи доступно несколько организаций.'}</p>
    {state.organizations.length > 0 && <div className="organization-options">{state.organizations.map(organization =>
      <button key={organization.organizationId} className="button button--secondary" onClick={() => void controller.switchOrganization(organization.organizationId)}>{organization.name}</button>)}</div>}
    <button className="button button--secondary" onClick={() => void controller.logout()}>Выйти</button>
  </section></main>;
}
function SessionToolbar({ session, controller }: { session: SessionView; controller: BrowserSessionController }) {
  const alternatives = session.organizations.filter(item => item.organizationId !== session.organizationId);
  return <aside className="session-toolbar" aria-label="Управление сессией"><div><strong>{session.organizationName}</strong><span>{session.role === 'Teacher' ? 'Преподаватель' : session.role === 'Student' ? 'Ученик' : 'Нет учебных полномочий'}</span></div>
    {alternatives.length > 0 && <details><summary>Сменить организацию</summary><div className="session-organization-options">{alternatives.map(organization =>
      <button key={organization.organizationId} className="button button--secondary" onClick={() => void controller.switchOrganization(organization.organizationId)}>{organization.name}</button>)}</div></details>}
    <button className="button button--secondary" onClick={() => void controller.logout()}>Выйти</button></aside>;
}
export function isAdministrativeEntryPath(value: string) {
  return /^\/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-4[0-9a-fA-F]{3}-[89aAbB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}$/.test(value);
}

type AdministrativeView = "home" | "bootstrap" | "activate" | "security";
function AdministrativeEntryScreen() {
  const [view, setView] = useState<AdministrativeView>("home");
  const [activation, setActivation] = useState({ organizationId: "", userName: "", code: "" });
  const menu = () => setView("home");
  if (view === "bootstrap") return <PlatformBootstrapPanel onBack={menu} onActivate={(organizationId, userName, code) => {
    setActivation({ organizationId, userName, code }); setView("activate");
  }} />;
  if (view === "activate") return <FirstAdminActivationPanel initialOrganization={activation.organizationId}
    initialUserName={activation.userName} initialCode={activation.code} onBack={menu} />;
  if (view === "security") return <SecurityOperatorPanel onBack={menu} />;
  return <main className="auth-page auth-loading"><section className="auth-card administrative-entry" aria-labelledby="administrative-entry-heading">
    <Brand /><span className="eyebrow">СЛУЖЕБНЫЙ ДОСТУП</span><h1 id="administrative-entry-heading">Административный вход</h1>
    <p>Выберите отдельный сценарий. Знание адреса не предоставляет полномочий: каждая операция требует собственного серверного подтверждения.</p>
    <div className="administrative-entry-actions"><button className="button button--primary" onClick={() => setView("bootstrap")}>Первый запуск</button>
      <button className="button button--secondary" onClick={() => setView("activate")}>Первый администратор</button>
      <button className="button button--secondary" onClick={() => setView("security")}>Оператор безопасности</button></div>
    <button className="link-button" onClick={() => window.location.assign("/login")}>Перейти к обычному входу</button>
  </section></main>;
}

export function SessionGate({ children }: { children: ReactNode }) {
  const { state, controller } = useSession();
  const location = useLocation();
  if (isAdministrativeEntryPath(location.pathname)) return <AdministrativeEntryScreen />;
  if (state.phase === 'starting' || state.phase === 'busy' && state.action !== 'login')
    return <main className="auth-page auth-loading"><Brand /><p role="status">Проверяем сессию…</p></main>;
  if (state.phase === 'selecting') {
    if (location.pathname !== '/select-organization')
      return <Navigate to="/select-organization" replace state={{ returnTo: safeReturnPath((location.state as { returnTo?: unknown } | null)?.returnTo) }} />;
    return <OrganizationSelectionScreen state={state} controller={controller} />;
  }
  if (state.phase !== 'authenticated') {
    if (location.pathname !== '/login') return <Navigate to="/login" replace state={{ returnTo: safeReturnPath(location.pathname + location.search + location.hash) }} />;
    return <LoginScreen state={state} controller={controller} />;
  }
  if (location.pathname === '/login' || location.pathname === '/select-organization')
    return <Navigate to={safeReturnPath((location.state as { returnTo?: unknown } | null)?.returnTo)} replace />;
  if (state.session.hasOrganizationAdminScope) return <><SessionToolbar session={state.session} controller={controller} /><OrganizationAdminDashboard /></>;
  if (!state.session.hasEducationalScope) return <main className="auth-page"><section className="auth-card"><Brand /><h1>Нет доступных курсов</h1>
    <p>Сессия подтверждена, но для выбранной организации нет действующих учебных полномочий.</p>
    <SessionToolbar session={state.session} controller={controller} /></section></main>;
  return <><SessionToolbar session={state.session} controller={controller} />{children}</>;
}

export function safeReturnPath(value: unknown) {
  if (typeof value !== 'string' || !value.startsWith('/') || value.startsWith('//')) return '/courses';
  try {
    const url = new URL(value, 'https://sapientum.invalid');
    if (url.origin !== 'https://sapientum.invalid' || url.pathname === '/login' || url.pathname === '/select-organization') return '/courses';
    const allowed = url.pathname === '/courses' || url.pathname === '/progress' ||
      url.pathname.startsWith('/courses/') || url.pathname.startsWith('/study/courses/') ||
      url.pathname.startsWith('/problem-assignments/') || url.pathname.startsWith('/teacher/reviews');
    return allowed ? url.pathname + url.search + url.hash : '/courses';
  } catch { return '/courses'; }
}

declare global { interface Window { lmsAuth?: { getAccessToken: () => string | null | Promise<string | null>; onUnauthenticated: () => void } } }
