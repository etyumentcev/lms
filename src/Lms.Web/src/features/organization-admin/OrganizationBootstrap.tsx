import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { ApiError, createApiClient } from '../../api/client';
import { browserApiClient } from '../../api/browserClient';
import './organization-admin.css';
import { AccountsPanel } from './AccountAdministration';
import { CourseAdministrationPanel } from './CourseAdministration';
import { ProcessingContextPanel } from './ProcessingContextAdministration';

type BootstrapResult = { commandId: string; organizationId: string; organizationName: string;
  adminUserId: string; adminActorId: string; challengeId: string; created: boolean; activationCode: string | null };
type Overview = { organizationId: string; organizationName: string; state: string; actorId: string; administratorState: string };
type AuditItem = { auditId: string; action: string; subjectRef: string; resourceRef: string | null;
  result: string; authorityVersion: number | null; occurredAt: string };

function object(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object') throw new Error('Invalid response');
  return value as Record<string, unknown>;
}
function decodeBootstrap(value: unknown): BootstrapResult {
  const item = object(value);
  for (const key of ['commandId', 'organizationId', 'organizationName', 'adminUserId', 'adminActorId', 'challengeId'])
    if (typeof item[key] !== 'string') throw new Error('Invalid response');
  if (typeof item.created !== 'boolean' || (typeof item.activationCode !== 'string' && item.activationCode !== null))
    throw new Error('Invalid response');
  return item as BootstrapResult;
}
function decodeOverview(value: unknown): Overview {
  const item = object(value);
  for (const key of ['organizationId', 'organizationName', 'state', 'actorId', 'administratorState'])
    if (typeof item[key] !== 'string') throw new Error('Invalid response');
  return item as Overview;
}
function decodeAudit(value: unknown): AuditItem[] {
  const items = object(value).items;
  if (!Array.isArray(items)) throw new Error('Invalid response');
  return items.map(raw => {
    const item = object(raw);
    if (!['auditId', 'action', 'subjectRef', 'result', 'occurredAt'].every(key => typeof item[key] === 'string'))
      throw new Error('Invalid response');
    return item as AuditItem;
  });
}
function decodeCourseStats(value: unknown) {
  const items = object(value).items;
  if (!Array.isArray(items)) throw new Error('Invalid response');
  return { courses: items.length, groups: items.reduce((sum, raw) => {
    const count = object(raw).groupCount; if (typeof count !== 'number') throw new Error('Invalid response'); return sum + count;
  }, 0) };
}
function message(error: unknown) {
  if (!(error instanceof ApiError)) return 'Сервис вернул некорректный ответ.';
  if (error.status === 0 || error.status >= 500) return 'Сервис временно недоступен. Проверьте результат перед новой командой.';
  if (error.code === 'IdempotencyConflict') return 'Этот идентификатор команды уже использован с другими данными.';
  if (error.code === 'DuplicateIdentity') return 'Такой логин уже существует.';
  if (error.code === 'ActivationExpired') return 'Срок действия кода истёк.';
  if (error.code === 'ActivationLocked') return 'Код заблокирован после пяти неудачных попыток.';
  if (error.status === 401) return 'Токен оператора или код активации не подтверждён.';
  if (error.status === 403) return 'Недостаточно полномочий для этой операции.';
  return 'Проверьте заполнение формы и повторите безопасную операцию.';
}

export function PlatformBootstrapPanel({ onBack, onActivate }: { onBack: () => void; onActivate: (organizationId: string, userName: string, code: string) => void }) {
  const [token, setToken] = useState('');
  const [organizationName, setOrganizationName] = useState('');
  const [adminUserName, setAdminUserName] = useState('');
  const [commandId] = useState(() => crypto.randomUUID());
  const [pending, setPending] = useState(false);
  const [error, setError] = useState('');
  const [result, setResult] = useState<BootstrapResult | null>(null);
  const client = useMemo(() => createApiClient({ getAccessToken: () => token, sessionScoped: false }), [token]);
  async function submit(event: FormEvent) {
    event.preventDefault(); setPending(true); setError('');
    try {
      setResult(await client.request('/platform/organizations', decodeBootstrap,
        { method: 'POST', body: { commandId, organizationName, adminUserName } }));
    } catch (reason) { setError(message(reason)); }
    finally { setPending(false); }
  }
  return <main className="bootstrap-page"><aside className="bootstrap-sidebar"><strong><span>Σ</span>Sapientum</strong>
    <p>Системное управление</p><nav aria-label="Разделы первого запуска"><span aria-current="page">⌂ Главная</span><span>▣ Организации</span><span>◷ Журнал действий</span></nav>
    <button type="button" onClick={onBack}>← Вернуться ко входу</button></aside>
    <section className="bootstrap-main"><header><div><span className="eyebrow">ОРГАНИЗАЦИИ › ПЕРВЫЙ ЗАПУСК</span><h1>Создание организации</h1>
      <p>Организация и первый администратор создаются одной проверяемой командой.</p></div><span className="operator-chip">AC · Системный администратор</span></header>
      {result ? <section className="bootstrap-result" aria-live="polite"><span className="success-mark">✓</span><h2>Организация создана</h2>
        <p><strong>{result.organizationName}</strong></p><dl><div><dt>Идентификатор организации</dt><dd>{result.organizationId}</dd></div>
          <div><dt>Логин администратора</dt><dd>{adminUserName}</dd></div></dl>
        {result.activationCode ? <div className="activation-secret"><span>Одноразовый код активации</span><strong>{result.activationCode}</strong>
          <small>Показывается только сейчас. Передайте его администратору по проверенному каналу.</small></div>
          : <p className="admin-alert">Команда уже была зафиксирована. Код повторно не раскрывается.</p>}
        {result.activationCode && <button className="button button--primary" onClick={() => onActivate(result.organizationId, adminUserName, result.activationCode!)}>Активировать администратора</button>}
      </section> : <form className="bootstrap-form" onSubmit={submit}><div className="form-illustration" aria-hidden="true"><span>⌂</span></div>
        {error && <p className="admin-alert" role="alert">{error}</p>}
        <label>Название организации<span>*</span><input required minLength={2} maxLength={160} value={organizationName} onChange={e => setOrganizationName(e.target.value)} placeholder="Демо-кружок" /></label>
        <label>Логин первого администратора<span>*</span><input required minLength={3} maxLength={128} autoComplete="username" value={adminUserName} onChange={e => setAdminUserName(e.target.value)} placeholder="admin.demo" /></label>
        <label>Токен системного администратора<span>*</span><textarea required value={token} onChange={e => setToken(e.target.value)} autoComplete="off" spellCheck={false} placeholder="JWT с audience sapientum-platform" /></label>
        <p className="form-note">Токен используется только для этой команды, не сохраняется в браузере и не даёт доступа к учебным данным.</p>
        <div className="form-actions"><button type="button" className="button button--secondary" onClick={onBack}>Отмена</button><button className="button button--primary" disabled={pending}>{pending ? 'Создаём…' : 'Создать организацию'}</button></div>
      </form>}
    </section></main>;
}

export function FirstAdminActivationPanel({ initialOrganization = '', initialUserName = '', initialCode = '', onBack }:
  { initialOrganization?: string; initialUserName?: string; initialCode?: string; onBack: () => void }) {
  const [organizationId, setOrganizationId] = useState(initialOrganization);
  const [userName, setUserName] = useState(initialUserName);
  const [activationCode, setActivationCode] = useState(initialCode);
  const [password, setPassword] = useState('');
  const [pending, setPending] = useState(false); const [error, setError] = useState(''); const [done, setDone] = useState(false);
  const client = useMemo(() => createApiClient({ getAccessToken: () => null, sessionScoped: false }), []);
  async function submit(event: FormEvent) {
    event.preventDefault(); setPending(true); setError('');
    try {
      await client.request('/auth/activate-first-admin', value => { if (value !== undefined) throw new Error('Invalid response'); },
        { method: 'POST', csrf: true, body: { organizationId, userName, activationCode, password } });
      setDone(true); setActivationCode(''); setPassword('');
    } catch (reason) { setError(message(reason)); }
    finally { setPending(false); }
  }
  return <main className="auth-page"><section className="activation-card"><span className="eyebrow">ПЕРВЫЙ АДМИНИСТРАТОР</span><h1>{done ? 'Доступ активирован' : 'Активация доступа'}</h1>
    {done ? <><p>Пароль установлен. Теперь войдите через обычную форму Sapientum.</p><button className="button button--primary" onClick={onBack}>Перейти ко входу</button></>
      : <form className="activation-form" onSubmit={submit}>{error && <p className="admin-alert" role="alert">{error}</p>}
        <label>Организация<input required value={organizationId} onChange={e => setOrganizationId(e.target.value)} /></label>
        <label>Логин<input required autoComplete="username" value={userName} onChange={e => setUserName(e.target.value)} /></label>
        <label>Код активации<input required autoComplete="one-time-code" value={activationCode} onChange={e => setActivationCode(e.target.value)} placeholder="XXXX-XXXX-XXXX-XXXX" /></label>
        <label>Новый пароль<input required type="password" minLength={12} maxLength={128} autoComplete="new-password" value={password} onChange={e => setPassword(e.target.value)} /></label>
        <div className="form-actions"><button type="button" className="button button--secondary" onClick={onBack}>Назад</button><button className="button button--primary" disabled={pending}>{pending ? 'Активируем…' : 'Активировать'}</button></div>
      </form>}</section></main>;
}

const actionNames: Record<string, string> = { organization_bootstrapped: 'Создана организация', first_admin_activated: 'Активирован первый администратор',
  session_created: 'Создана сессия', session_rotated: 'Обновлена сессия', session_revoked: 'Завершена сессия', first_admin_activation_denied: 'Отклонена активация' };
export function OrganizationAdminDashboard() {
  const [view, setView] = useState<'home' | 'accounts' | 'courses' | 'processing'>('home');
  const [overview, setOverview] = useState<Overview | null>(null); const [audit, setAudit] = useState<AuditItem[]>([]); const [stats, setStats] = useState({ courses: 0, groups: 0 }); const [error, setError] = useState('');
  useEffect(() => { const abort = new AbortController(); Promise.all([
    browserApiClient.request('/admin/organization', decodeOverview, { signal: abort.signal }),
    browserApiClient.request('/admin/audit?limit=12', decodeAudit, { signal: abort.signal }),
    browserApiClient.request('/admin/courses', decodeCourseStats, { signal: abort.signal })
  ]).then(([organization, items, catalog]) => { setOverview(organization); setAudit(items); setStats(catalog); }).catch(reason => { if (!abort.signal.aborted) setError(message(reason)); });
    return () => abort.abort(); }, [view]);
  return <div className="admin-dashboard"><aside><strong><span>Σ</span>Sapientum</strong><p>Веста</p><nav><button type="button" aria-current={view === 'home' ? 'page' : undefined} onClick={() => setView('home')}>⌂ Главная</button><span>▣ Организация</span><button type="button" aria-current={view === 'accounts' ? 'page' : undefined} onClick={() => setView('accounts')}>♙ Пользователи</button><button type="button" aria-current={view === 'courses' ? 'page' : undefined} onClick={() => setView('courses')}>▤ Курсы и группы</button><button type="button" aria-current={view === 'processing' ? 'page' : undefined} onClick={() => setView('processing')}>◎ Контекст обработки</button><span>◷ Журнал действий</span><span>⚙ Настройки</span></nav></aside>
    <main>{view === 'accounts' ? <AccountsPanel onBack={() => setView('home')} /> : view === 'courses' ? <CourseAdministrationPanel onBack={() => setView('home')} /> : view === 'processing' ? <ProcessingContextPanel onBack={() => setView('home')} /> : <><div className="dashboard-heading"><div><span className="eyebrow">АДМИНИСТРАТИВНАЯ ПАНЕЛЬ</span><h1>Добро пожаловать, администратор!</h1><p>Управление организацией, пользователями, курсами и доступом.</p></div><span className="operator-chip">AC · Администратор организации</span></div>
      {error && <p className="admin-alert" role="alert">{error}</p>}
      <section className="metric-grid"><article><span>▣</span><div><small>Организация</small><strong>{overview ? '1' : '—'}</strong></div></article><article><span>♙</span><div><small>Администраторы</small><strong>{overview ? '1' : '—'}</strong></div></article><article><span>▤</span><div><small>Курсы</small><strong>{stats.courses}</strong></div></article><article><span>◎</span><div><small>Группы</small><strong>{stats.groups}</strong></div></article></section>
      <h2>Организация</h2><section className="organization-card">{overview ? <><div><strong>{overview.organizationName}</strong><span className="status-pill">{overview.state === 'Enabled' ? 'Активна' : overview.state}</span></div><p>{overview.organizationId}</p><small>Администратор: {overview.administratorState === 'Active' ? 'активирован' : overview.administratorState}</small></> : <p>Загружаем данные…</p>}</section>
      <h2>Быстрые действия</h2><section className="quick-grid"><button type="button" onClick={() => setView('accounts')}><span>♙</span><strong>Создать пользователя</strong><small>Ученики и преподаватели</small></button><button type="button" onClick={() => setView('courses')}><span>▤</span><strong>Создать курс</strong><small>Курс и учебные группы</small></button><button type="button" onClick={() => setView('processing')}><span>◎</span><strong>Контекст обработки</strong><small>Synthetic-only и привязки курсов</small></button><button type="button" onClick={() => setView('courses')}><span>◎</span><strong>Управление группами</strong><small>Зачисления и назначения</small></button></section>
      <div className="audit-title"><h2>Последние действия</h2><span>Проверяемый журнал</span></div><section className="audit-list">{audit.length ? audit.map(item => <article key={item.auditId}><span>✓</span><div><strong>{actionNames[item.action] ?? item.action}</strong><small>{item.result} · {new Date(item.occurredAt).toLocaleString('ru-RU')}</small></div><code>{item.auditId.slice(0, 8)}</code></article>) : <p>Записи журнала загружаются…</p>}</section></>}</main></div>;
}
