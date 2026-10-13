import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { ApiError, createApiClient } from '../../api/client';
import { browserApiClient } from '../../api/browserClient';
import './accounts.css';

type Role = 'Student' | 'Teacher';
type Account = { userId: string; actorId: string; externalRosterKey: string; displayName: string;
  userName: string; role: Role; state: 'PendingActivation' | 'Active' | 'Blocked'; createdAt: string; organizationCount: number };
type Page = { items: Account[]; total: number; offset: number; limit: number };
type ProvisionResult = Account & { organizationId: string; created: boolean; challengeId: string | null;
  activationCode: string | null; approvalRequestId: string | null };
type Secret = { code: string; title: string; note: string };

function object(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object') throw new Error('Invalid response');
  return value as Record<string, unknown>;
}
function account(value: unknown): Account {
  const item = object(value);
  if (!['userId','actorId','externalRosterKey','displayName','userName','role','state','createdAt']
      .every(key => typeof item[key] === 'string') || typeof item.organizationCount !== 'number') throw new Error('Invalid response');
  return item as Account;
}
function page(value: unknown): Page {
  const item = object(value); if (!Array.isArray(item.items) || typeof item.total !== 'number') throw new Error('Invalid response');
  return { items: item.items.map(account), total: item.total, offset: Number(item.offset), limit: Number(item.limit) };
}
function provision(value: unknown): ProvisionResult {
  const item = object(value); const base = account({ ...item, externalRosterKey: '', createdAt: new Date().toISOString(), organizationCount: 1 });
  if (typeof item.organizationId !== 'string' || typeof item.created !== 'boolean' ||
      (item.activationCode !== null && typeof item.activationCode !== 'string')) throw new Error('Invalid response');
  return { ...base, ...item } as ProvisionResult;
}
function friendly(error: unknown) {
  if (!(error instanceof ApiError)) return 'Сервис вернул некорректный ответ.';
  const messages: Record<string,string> = {
    IdempotencyConflict: 'Такая roster-запись уже использована с другими данными.',
    DuplicateMembership: 'Участник уже состоит в этой организации.',
    IdentityAmbiguous: 'Совпадение личности требует проверки оператором безопасности.',
    ActivationUnavailable: 'Новый код активации сейчас выдать нельзя.',
    RecoveryUnavailable: 'Восстановление для этой записи сейчас недоступно.',
    ChallengeCooldown: 'Новый код можно выдать через минуту.', ChallengeRateLimited: 'Лимит выдачи кодов исчерпан.',
  };
  return messages[error.code] ?? (error.status === 403 ? 'Недостаточно полномочий.' :
    error.status >= 500 || error.status === 0 ? 'Сервис временно недоступен. Результат команды неизвестен.' : 'Проверьте данные и повторите действие.');
}

export function AccountsPanel({ onBack }: { onBack: () => void }) {
  const [items, setItems] = useState<Account[]>([]); const [total, setTotal] = useState(0);
  const [search, setSearch] = useState(''); const [role, setRole] = useState(''); const [state, setState] = useState('');
  const [creating, setCreating] = useState(false); const [selected, setSelected] = useState<Account | null>(null);
  const [secret, setSecret] = useState<Secret | null>(null); const [error, setError] = useState(''); const [loading, setLoading] = useState(false);
  async function load() {
    setLoading(true); setError('');
    const query = new URLSearchParams(); if (search) query.set('search', search); if (role) query.set('role', role); if (state) query.set('state', state);
    try { const result = await browserApiClient.request(`/admin/accounts?${query}`, page); setItems(result.items); setTotal(result.total); }
    catch (reason) { setError(friendly(reason)); } finally { setLoading(false); }
  }
  useEffect(() => { const id = window.setTimeout(() => void load(), 180); return () => clearTimeout(id); }, [search, role, state]);
  async function issueActivation(target: Account) {
    setError(''); try {
      const result = await browserApiClient.request(`/admin/accounts/${target.actorId}/activation`, object,
        { method: 'POST', csrf: true, body: { idempotencyKey: crypto.randomUUID() } });
      if (typeof result.activationCode === 'string') setSecret({ title: 'Код активации', code: result.activationCode,
        note: 'Показывается только сейчас. Передайте участнику лично или по проверенному индивидуальному каналу.' });
    } catch (reason) { setError(friendly(reason)); }
  }
  async function recover(target: Account) {
    setError(''); try {
      await browserApiClient.request(`/admin/accounts/${target.actorId}/recovery`, object, { method: 'POST', csrf: true,
        body: { verificationRef: `synthetic-roster:${target.externalRosterKey}`, idempotencyKey: crypto.randomUUID() } });
      setSecret({ title: 'Запрос восстановления отправлен', code: 'ОЖИДАЕТ ПОДТВЕРЖДЕНИЯ',
        note: 'Независимый уполномоченный оператор должен проверить получателя. Код появится только после подтверждения.' });
    } catch (reason) { setError(friendly(reason)); }
  }
  return <section className="accounts-page"><div className="accounts-heading"><div><button className="link-button" onClick={onBack}>← Главная</button>
    <span className="eyebrow">ПОЛЬЗОВАТЕЛИ</span><h1>Учётные записи</h1><p>Участники организации, активация и восстановление доступа.</p></div>
    <button className="button button--primary" onClick={() => setCreating(true)}>+ Создать пользователя</button></div>
    {error && <p className="admin-alert" role="alert">{error}</p>}
    <div className="account-filters"><label>Поиск<input aria-label="Поиск пользователей" value={search} onChange={e => setSearch(e.target.value)} placeholder="ФИО, логин или roster ID" /></label>
      <label>Роль<select value={role} onChange={e => setRole(e.target.value)}><option value="">Все роли</option><option value="Student">Ученики</option><option value="Teacher">Преподаватели</option></select></label>
      <label>Статус<select value={state} onChange={e => setState(e.target.value)}><option value="">Все статусы</option><option value="PendingActivation">Не активирован</option><option value="Active">Активен</option><option value="Blocked">Заблокирован</option></select></label></div>
    <div className="account-count">{loading ? 'Загружаем…' : `Найдено: ${total}`}</div>
    <div className="account-grid">{items.map(item => <article className="account-card" key={item.actorId}>
      <button onClick={() => setSelected(item)} aria-label={`Открыть карточку ${item.displayName}`}><span className="account-avatar">{item.displayName.split(/\s+/).slice(0,2).map(x => x[0]).join('').toUpperCase()}</span>
        <span><strong>{item.displayName}</strong><small>@{item.userName} · {item.externalRosterKey}</small></span>
        <span className={`status-pill status-${item.state.toLowerCase()}`}>{item.state === 'Active' ? 'Активен' : item.state === 'Blocked' ? 'Заблокирован' : 'Не активирован'}</span></button>
      <footer><span>{item.role === 'Student' ? 'Ученик' : 'Преподаватель'}</span><span>{item.organizationCount > 1 ? `Организаций: ${item.organizationCount}` : 'Эта организация'}</span></footer>
    </article>)}</div>
    {!loading && !items.length && <div className="account-empty"><strong>Пользователи не найдены</strong><p>Измените фильтры или создайте первую учётную запись.</p></div>}
    {creating && <CreateAccountDialog onClose={() => setCreating(false)} onCreated={result => { setCreating(false); void load();
      if (result.activationCode) setSecret({ title: 'Одноразовый код активации', code: result.activationCode, note: 'Код действует 24 часа и показывается только один раз.' });
      else if (result.approvalRequestId) setSecret({ title: 'Требуется независимое подтверждение', code: 'ОЖИДАЕТ ПОДТВЕРЖДЕНИЯ', note: 'Существующая личность не дублируется. Оператор безопасности должен подтвердить новую membership.' }); }} />}
    {selected && <AccountDialog item={selected} onClose={() => setSelected(null)} onActivation={() => void issueActivation(selected)} onRecovery={() => void recover(selected)} />}
    {secret && <SecretDialog value={secret} onClose={() => setSecret(null)} />}
  </section>;
}

function CreateAccountDialog({ onClose, onCreated }: { onClose: () => void; onCreated: (value: ProvisionResult) => void }) {
  const [role, setRole] = useState<Role>('Student'); const [displayName, setDisplayName] = useState('');
  const [userName, setUserName] = useState(''); const [externalRosterKey, setExternalRosterKey] = useState('');
  const [pending, setPending] = useState(false); const [error, setError] = useState(''); const [id] = useState(() => crypto.randomUUID());
  async function submit(event: FormEvent) { event.preventDefault(); setPending(true); setError(''); try {
    onCreated(await browserApiClient.request('/admin/accounts', provision, { method: 'POST', csrf: true,
      body: { role, displayName, userName, externalRosterKey, idempotencyKey: id } }));
  } catch (reason) { setError(friendly(reason)); } finally { setPending(false); } }
  return <div className="admin-modal-backdrop" role="presentation"><section className="admin-modal" role="dialog" aria-modal="true" aria-labelledby="create-account-title">
    <button className="modal-close" onClick={onClose} aria-label="Закрыть">×</button><span className="eyebrow">НОВАЯ УЧЁТНАЯ ЗАПИСЬ</span><h2 id="create-account-title">Создание пользователя</h2>
    <form onSubmit={submit} className="account-form">{error && <p className="admin-alert" role="alert">{error}</p>}
      <div className="role-switch"><button type="button" aria-pressed={role === 'Student'} onClick={() => setRole('Student')}>Ученик</button><button type="button" aria-pressed={role === 'Teacher'} onClick={() => setRole('Teacher')}>Преподаватель</button></div>
      <label>ФИО / отображаемое имя<span>*</span><input required minLength={2} maxLength={160} value={displayName} onChange={e => setDisplayName(e.target.value)} /></label>
      <label>Логин<span>*</span><input required minLength={3} maxLength={128} autoComplete="off" value={userName} onChange={e => setUserName(e.target.value)} /></label>
      <label>Идентификатор roster<span>*</span><input required minLength={3} maxLength={128} value={externalRosterKey} onChange={e => setExternalRosterKey(e.target.value)} /><small>Стабильный синтетический идентификатор из доверенного списка организации.</small></label>
      <p className="form-note">Email и телефон не требуются. Курсы и группы здесь не назначаются.</p>
      <div className="form-actions"><button type="button" className="button button--secondary" onClick={onClose}>Отмена</button><button className="button button--primary" disabled={pending}>{pending ? 'Создаём…' : 'Создать и выдать код'}</button></div>
    </form></section></div>;
}

function AccountDialog({ item, onClose, onActivation, onRecovery }: { item: Account; onClose: () => void; onActivation: () => void; onRecovery: () => void }) {
  return <div className="admin-modal-backdrop"><section className="admin-modal account-detail" role="dialog" aria-modal="true" aria-labelledby="account-title">
    <button className="modal-close" onClick={onClose} aria-label="Закрыть">×</button><span className="account-avatar large">{item.displayName[0]}</span><h2 id="account-title">{item.displayName}</h2>
    <p>@{item.userName}</p><dl><div><dt>Роль</dt><dd>{item.role === 'Student' ? 'Ученик' : 'Преподаватель'}</dd></div><div><dt>Статус</dt><dd>{item.state}</dd></div><div><dt>Roster ID</dt><dd>{item.externalRosterKey}</dd></div><div><dt>Actor</dt><dd>{item.actorId}</dd></div></dl>
    <div className="dialog-actions">{item.state === 'PendingActivation' && <button className="button button--primary" onClick={onActivation}>Перевыпустить код активации</button>}
      {item.state === 'Active' && <button className="button button--secondary" onClick={onRecovery}>Запросить восстановление</button>}</div>
  </section></div>;
}
function SecretDialog({ value, onClose }: { value: Secret; onClose: () => void }) {
  return <div className="admin-modal-backdrop"><section className="admin-modal secret-dialog" role="dialog" aria-modal="true" aria-labelledby="secret-title"><span className="success-mark">✓</span><h2 id="secret-title">{value.title}</h2>
    <div className="activation-secret"><span>Результат</span><strong>{value.code}</strong><small>{value.note}</small></div>
    <button className="button button--primary" onClick={onClose}>Я сохранил результат</button></section></div>;
}

export function AccountAccessPanel({ mode, onBack }: { mode: 'activation' | 'recovery'; onBack: () => void }) {
  const [organizationId, setOrganizationId] = useState(''); const [userName, setUserName] = useState('');
  const [code, setCode] = useState(''); const [password, setPassword] = useState(''); const [confirm, setConfirm] = useState('');
  const [error, setError] = useState(''); const [done, setDone] = useState(false); const [pending, setPending] = useState(false);
  const client = useMemo(() => createApiClient({ getAccessToken: () => null, sessionScoped: false }), []);
  async function submit(event: FormEvent) { event.preventDefault(); if (password !== confirm) { setError('Пароли не совпадают.'); return; }
    setPending(true); setError(''); try { await client.request(mode === 'activation' ? '/auth/activate' : '/auth/recover',
      value => { if (value !== undefined) throw new Error('Invalid response'); }, { method: 'POST', csrf: true,
        body: mode === 'activation' ? { organizationId, userName, code, password } : { organizationId, userName, code, newPassword: password } });
      setDone(true); setCode(''); setPassword(''); setConfirm('');
    } catch (reason) { const e = reason as ApiError; const messages: Record<string,string> = {
      ActivationInvalid: 'Код активации неверен.', ActivationUsed: 'Код активации уже использован.', ActivationExpired: 'Срок действия кода активации истёк.', ActivationLocked: 'Код заблокирован после пяти ошибок.',
      RecoveryInvalid: 'Код восстановления неверен.', RecoveryUsed: 'Код восстановления уже использован.', RecoveryExpired: 'Срок действия кода восстановления истёк.', RecoveryLocked: 'Код заблокирован после пяти ошибок.' };
      setError(messages[e?.code] ?? friendly(reason));
    } finally { setPending(false); } }
  return <main className="auth-page access-flow"><section className="activation-card"><button className="link-button" onClick={onBack}>← Назад</button><span className="eyebrow">{mode === 'activation' ? 'АКТИВАЦИЯ' : 'ВОССТАНОВЛЕНИЕ'}</span>
    <h1>{done ? 'Пароль установлен' : mode === 'activation' ? 'Активируйте аккаунт' : 'Восстановите доступ'}</h1>
    {done ? <><p>Теперь можно войти через обычную форму Sapientum.</p><button className="button button--primary" onClick={onBack}>Перейти ко входу</button></>
      : <form className="activation-form" onSubmit={submit}>{error && <p className="admin-alert" role="alert">{error}</p>}
        <label>Организация<input required value={organizationId} onChange={e => setOrganizationId(e.target.value)} /></label>
        <label>Логин<input required value={userName} onChange={e => setUserName(e.target.value)} autoComplete="username" /></label>
        <label>Одноразовый код<input required value={code} onChange={e => setCode(e.target.value.toUpperCase())} autoComplete="one-time-code" placeholder="XXXX-XXXX-XXXX-XXXX" /></label>
        <label>Новый пароль<input required minLength={12} maxLength={128} type="password" autoComplete="new-password" value={password} onChange={e => setPassword(e.target.value)} /></label>
        <label>Повторите пароль<input required minLength={12} maxLength={128} type="password" autoComplete="new-password" value={confirm} onChange={e => setConfirm(e.target.value)} /></label>
        <p className="form-note">Код одноразовый. Пароль не менее 12 символов.</p><button className="button button--primary" disabled={pending}>{pending ? 'Проверяем…' : 'Продолжить'}</button>
      </form>}</section></main>;
}

export function SecurityOperatorPanel({ onBack }: { onBack: () => void }) {
  const [token, setToken] = useState(''); const [items, setItems] = useState<Record<string,unknown>[]>([]); const [error, setError] = useState(''); const [secret, setSecret] = useState<Secret|null>(null);
  const client = useMemo(() => createApiClient({ getAccessToken: () => token, sessionScoped: false }), [token]);
  async function load() { setError(''); try { const result = await client.request('/platform/account-security/requests', object); setItems(Array.isArray(result.items) ? result.items.map(object) : []); } catch (reason) { setError(friendly(reason)); } }
  async function decide(id: string, approve: boolean) { setError(''); try { const result = await client.request(`/platform/account-security/requests/${id}`, object,
      { method: 'POST', body: { approve, verificationRef: `synthetic-independent-check:${crypto.randomUUID()}` } });
    if (typeof result.recoveryCode === 'string') setSecret({ title: 'Код восстановления', code: result.recoveryCode, note: 'Передайте только проверенному получателю. Код действует 15 минут и больше не показывается.' });
    await load();
  } catch (reason) { setError(friendly(reason)); } }
  return <main className="bootstrap-page"><aside className="bootstrap-sidebar"><strong><span>Σ</span>Sapientum</strong><p>Безопасность аккаунтов</p><button onClick={onBack}>← Вернуться ко входу</button></aside><section className="bootstrap-main">
    <header><div><span className="eyebrow">НЕЗАВИСИМОЕ ПОДТВЕРЖДЕНИЕ</span><h1>Запросы доступа</h1><p>Оператор не получает доступ к курсам, работам и оценкам.</p></div></header>
    {error && <p className="admin-alert" role="alert">{error}</p>}<div className="security-token"><label>JWT account-security operator<input value={token} onChange={e => setToken(e.target.value)} autoComplete="off" /></label><button className="button button--primary" onClick={() => void load()}>Загрузить запросы</button></div>
    <div className="security-requests">{items.map(item => <article key={String(item.requestId)}><div><strong>{item.kind === 'Recovery' ? 'Восстановление доступа' : 'Связь общей личности'}</strong><small>{String(item.organizationId)} · {String(item.requestedAt)}</small></div><button className="button button--secondary" onClick={() => void decide(String(item.requestId), false)}>Отклонить</button><button className="button button--primary" onClick={() => void decide(String(item.requestId), true)}>Подтвердить</button></article>)}</div>
    {secret && <SecretDialog value={secret} onClose={() => setSecret(null)} />}
  </section></main>;
}
