import { useCallback, useEffect, useState } from 'react';
import { ApiError } from '../../api/client';
import { browserApiClient } from '../../api/browserClient';
import './processing-context.css';

type Context = { contextId: string; version: number; effectiveFrom: string; approvalState: string;
  syntheticOnly: boolean; purposeRef: string; retentionPolicyRef: string; versionCount: number };
type Binding = { contextId: string; contextVersion: number; effectiveFrom: string };
type Course = { courseId: string; title: string; state: string; binding: Binding | null };
type State = { revision: number; currentContext: Context | null; hasActiveRestrictions: boolean; courses: Course[] };

function object(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object') throw new Error('Invalid response');
  return value as Record<string, unknown>;
}
function binding(value: unknown): Binding | null {
  if (value === null) return null;
  const item = object(value);
  if (typeof item.contextId !== 'string' || typeof item.contextVersion !== 'number' ||
      typeof item.effectiveFrom !== 'string') throw new Error('Invalid response');
  return item as Binding;
}
function decode(value: unknown): State {
  const item = object(value); const rawContext = item.currentContext;
  if (typeof item.revision !== 'number' || typeof item.hasActiveRestrictions !== 'boolean' ||
      !Array.isArray(item.courses)) throw new Error('Invalid response');
  let currentContext: Context | null = null;
  if (rawContext !== null) {
    const current = object(rawContext);
    if (typeof current.contextId !== 'string' || typeof current.version !== 'number' ||
        typeof current.effectiveFrom !== 'string' || typeof current.approvalState !== 'string' ||
        typeof current.syntheticOnly !== 'boolean' || typeof current.purposeRef !== 'string' ||
        typeof current.retentionPolicyRef !== 'string' || typeof current.versionCount !== 'number')
      throw new Error('Invalid response');
    currentContext = current as Context;
  }
  const courses = item.courses.map(raw => {
    const course = object(raw);
    if (typeof course.courseId !== 'string' || typeof course.title !== 'string' ||
        typeof course.state !== 'string') throw new Error('Invalid response');
    return { ...course, binding: binding(course.binding) } as Course;
  });
  return { revision: item.revision, hasActiveRestrictions: item.hasActiveRestrictions,
    currentContext, courses };
}
function result(value: unknown): State {
  const item = object(value);
  if (typeof item.changed !== 'boolean') throw new Error('Invalid response');
  return decode(item.state);
}
function message(error: unknown) {
  if (!(error instanceof ApiError)) return 'Сервис вернул некорректный ответ.';
  const messages: Record<string, string> = {
    ConcurrencyConflict: 'Данные уже изменились в другой вкладке. Состояние обновлено — повторите осознанно.',
    ProcessingContextConflict: 'Обнаружено неоднозначное состояние контекста. Изменение остановлено.',
    ProcessingContextMissing: 'Действующий синтетический контекст не найден.',
    ProcessingRestricted: 'Операция запрещена действующим ограничением обработки.',
    ValidationFailed: 'Команда не прошла проверку. Обновите страницу и повторите действие.',
  };
  return messages[error.code] ?? (error.status === 403 ? 'Недостаточно полномочий.' :
    error.status >= 500 || error.status === 0 ? 'Сервис временно недоступен. Результат команды неизвестен — сначала обновите состояние.' :
      'Не удалось выполнить команду. Обновите состояние и повторите действие.');
}

export function ProcessingContextPanel({ onBack }: { onBack: () => void }) {
  const [state, setState] = useState<State | null>(null);
  const [pending, setPending] = useState(''); const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const load = useCallback(async () => {
    try { setState(await browserApiClient.request('/admin/processing-context', decode)); }
    catch (reason) { setError(message(reason)); }
  }, []);
  useEffect(() => { void load(); }, [load]);

  async function createVersion() {
    if (!state) return; setPending('context'); setError(''); setNotice('');
    try {
      const current = state.currentContext;
      setState(await browserApiClient.request('/admin/processing-context/versions', result,
        { method: 'POST', csrf: true, body: { operationId: crypto.randomUUID(),
          expectedRevision: state.revision, expectedContextId: current?.contextId ?? null,
          expectedContextVersion: current?.version ?? null, effectiveFrom: new Date().toISOString() } }));
      setNotice(current ? 'Новая immutable-версия синтетического контекста создана.' :
        'Синтетический контекст обработки создан.');
    } catch (reason) { setError(message(reason)); await load(); }
    finally { setPending(''); }
  }

  async function bind(course: Course) {
    if (!state?.currentContext) return; setPending(course.courseId); setError(''); setNotice('');
    try {
      const current = state.currentContext;
      setState(await browserApiClient.request(`/admin/courses/${encodeURIComponent(course.courseId)}/processing-context`, result,
        { method: 'POST', csrf: true, body: { operationId: crypto.randomUUID(), expectedRevision: state.revision,
          contextId: current.contextId, contextVersion: current.version,
          expectedBindingFrom: course.binding?.effectiveFrom ?? null,
          effectiveFrom: course.binding ? null : new Date().toISOString() } }));
      setNotice(`Курс «${course.title}» привязан к контексту v${current.version}.`);
    } catch (reason) { setError(message(reason)); await load(); }
    finally { setPending(''); }
  }

  const current = state?.currentContext;
  return <section className="processing-page"><div className="processing-heading"><div>
    <button className="link-button" onClick={onBack}>← Главная</button><span className="eyebrow">SYNTHETIC-ONLY</span>
    <h1>Контекст обработки</h1><p>Первоначальная настройка допускает только контролируемые синтетические данные.</p>
  </div>{state && <span className="revision-chip">Ревизия {state.revision}</span>}</div>
    <div className="synthetic-boundary" role="note"><strong>Не является юридическим основанием</strong>
      <p>Этот тестовый контекст нельзя использовать для реальных персональных данных или как подтверждение готовности пилота.</p></div>
    {error && <p className="admin-alert" role="alert">{error}</p>}
    {notice && <p className="processing-success" role="status">✓ {notice}</p>}
    {state?.hasActiveRestrictions && <p className="processing-restriction" role="alert"><strong>Обработка ограничена.</strong> Действующие ProcessingRestrictions имеют приоритет; импорт и публикация будут отклонены сервером.</p>}
    <section className="processing-card"><div><span className="processing-icon">◎</span><div><small>ДЕЙСТВУЮЩИЙ КОНТЕКСТ</small>
      {current ? <><h2>Синтетический контекст · v{current.version}</h2><p>{current.contextId}</p>
        <dl><div><dt>Версий в истории</dt><dd>{current.versionCount}</dd></div><div><dt>Действует с</dt><dd>{new Date(current.effectiveFrom).toLocaleString('ru-RU')}</dd></div><div><dt>Режим</dt><dd>{current.approvalState}</dd></div></dl></>
        : <><h2>Контекст ещё не создан</h2><p>Без него импорт остаётся закрыт по умолчанию.</p></>}</div></div>
      <button className="button button--primary" disabled={pending !== '' || !state} onClick={() => void createVersion()}>
        {pending === 'context' ? 'Сохраняем…' : current ? 'Создать новую версию' : 'Создать синтетический контекст'}</button></section>
    <div className="processing-section-title"><div><span className="eyebrow">КУРСЫ</span><h2>Привязка контекста</h2></div><p>Только курсы этой организации.</p></div>
    <section className="processing-courses">{state?.courses.map(course => {
      const bound = !!current && course.binding?.contextId === current.contextId && course.binding.contextVersion === current.version;
      return <article key={course.courseId}><div><strong>{course.title}</strong><small>{course.courseId}</small></div>
        <div className={course.binding ? 'binding-state bound' : 'binding-state'}><span>{course.binding ? '✓' : '○'}</span>
          <div><strong>{bound ? `Привязан к v${course.binding!.contextVersion}` : course.binding ? `Требует обновления с v${course.binding.contextVersion}` : 'Не привязан'}</strong>
            <small>{course.binding ? new Date(course.binding.effectiveFrom).toLocaleString('ru-RU') : 'Импорт закрыт'}</small></div></div>
        <button className={bound ? 'button button--secondary' : 'button button--primary'} disabled={!current || bound || pending !== ''}
          onClick={() => void bind(course)}>{pending === course.courseId ? 'Привязываем…' : bound ? 'Привязан' : course.binding ? 'Обновить привязку' : 'Привязать контекст'}</button></article>;
    })}</section>
    {state && !state.courses.length && <div className="account-empty"><strong>Курсов пока нет</strong><p>Сначала создайте курс в разделе «Курсы и группы».</p></div>}
  </section>;
}
