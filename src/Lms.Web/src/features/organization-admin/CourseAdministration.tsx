import { useEffect, useState, type FormEvent, type ReactNode } from 'react';
import { ApiError } from '../../api/client';
import { browserApiClient } from '../../api/browserClient';
import { CourseScheduleEditor } from './CourseScheduleEditor';
import './course-administration.css';

type Course = { courseId: string; title: string; description: string; state: 'Draft'|'Published'; version: number;
  groupCount: number; studentCount: number; teacherCount: number };
type Group = { groupId: string; name: string; description: string; version: number; studentCount: number; teacherCount: number };
type Student = { actorId: string; displayName: string; externalRosterKey: string; groupId: string; groupName: string;
  enrollmentGeneration: number; version: number };
type Teacher = { actorId: string; displayName: string; externalRosterKey: string; grantId: string; groupId: string;
  groupName: string; generation: number; version: number };
type CourseEditor = { actorId: string; displayName: string; externalRosterKey: string; grantId: string;
  generation: number; version: number };
type Detail = { course: Course; groups: Group[]; students: Student[]; teachers: Teacher[]; editors: CourseEditor[] };
type Account = { actorId: string; displayName: string; externalRosterKey: string; role: 'Student' | 'Teacher'; state: string };

function object(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object') throw new Error('Invalid response');
  return value as Record<string, unknown>;
}
function course(value: unknown): Course {
  const item = object(value);
  if (typeof item.courseId !== 'string' || typeof item.title !== 'string' || typeof item.description !== 'string' ||
      !['Draft','Published'].includes(String(item.state)) || !['version','groupCount','studentCount','teacherCount'].every(x => typeof item[x] === 'number'))
    throw new Error('Invalid response');
  return item as Course;
}
function courses(value: unknown) { const item = object(value); if (!Array.isArray(item.items)) throw new Error('Invalid response'); return item.items.map(course); }
function detail(value: unknown): Detail {
  const item = object(value); if (!Array.isArray(item.groups) || !Array.isArray(item.students) ||
      !Array.isArray(item.teachers) || !Array.isArray(item.editors)) throw new Error('Invalid response');
  return { course: course(item.course), groups: item.groups as Group[], students: item.students as Student[],
    teachers: item.teachers as Teacher[], editors: item.editors as CourseEditor[] };
}
function accounts(value: unknown): Account[] { const item = object(value); if (!Array.isArray(item.items)) throw new Error('Invalid response'); return item.items.map(x => object(x) as Account); }
function message(error: unknown) {
  if (!(error instanceof ApiError)) return 'Сервис вернул некорректный ответ.';
  if (error.status === 409) return 'Данные уже изменились. Список обновлён — повторите действие при необходимости.';
  if (error.status === 403) return 'Недостаточно полномочий для этой организации.';
  if (error.status === 404) return 'Курс, группа или участник больше недоступны.';
  return error.status === 0 || error.status >= 500 ? 'Сервис временно недоступен. Результат команды неизвестен.' : 'Проверьте введённые данные.';
}

export function CourseAdministrationPanel({ onBack }: { onBack: () => void }) {
  const [items, setItems] = useState<Course[]>([]); const [selected, setSelected] = useState<Detail | null>(null);
  const [creating, setCreating] = useState(false);
  const [roster, setRoster] = useState<'students' | 'teachers' | 'editors'>('students'); const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  async function loadCatalog() { setLoading(true); setError(''); try { setItems(await browserApiClient.request('/admin/courses', courses)); }
    catch (reason) { setError(message(reason)); } finally { setLoading(false); } }
  async function loadDetail(id: string) { setLoading(true); setError(''); try { setSelected(await browserApiClient.request(`/admin/courses/${encodeURIComponent(id)}`, detail)); }
    catch (reason) { setError(message(reason)); } finally { setLoading(false); } }
  useEffect(() => { void loadCatalog(); }, []);
  if (selected) return <CourseDetail value={selected} mode={roster} setMode={setRoster} error={error} loading={loading}
    onBack={() => { setSelected(null); void loadCatalog(); }} onReload={() => void loadDetail(selected.course.courseId)} />;
  return <section className="course-admin-page"><div className="accounts-heading"><div><button className="link-button" onClick={onBack}>← Главная</button>
    <span className="eyebrow">КУРСЫ И ГРУППЫ</span><h1>Каталог курсов</h1><p>Черновые учебные пространства, группы и назначения.</p></div>
    <button className="button button--primary" onClick={() => setCreating(true)}>+ Создать курс</button></div>
    {error && <p className="admin-alert" role="alert">{error}</p>}
    <div className="catalog-summary"><span>Курсов <strong>{items.length}</strong></span><span>Групп <strong>{items.reduce((sum, x) => sum + x.groupCount, 0)}</strong></span>
      <span>Зачислений <strong>{items.reduce((sum, x) => sum + x.studentCount, 0)}</strong></span></div>
    {loading && <p role="status">Загружаем каталог…</p>}
    <div className="course-card-grid">{items.map(item => <button key={item.courseId} className="course-card" onClick={() => void loadDetail(item.courseId)}>
      <span className="course-icon">∑</span><span className="draft-pill">{item.state === 'Published' ? 'Опубликован' : 'Черновик'}</span><strong>{item.title}</strong>
      <small>{item.description || 'Описание пока не добавлено'}</small><footer><span>{item.groupCount} групп</span><span>{item.studentCount} учеников</span><span>{item.teacherCount} назначений</span></footer>
    </button>)}</div>
    {!loading && !items.length && <div className="account-empty"><strong>Курсов пока нет</strong><p>Создайте первый курс — публикация материалов появится на M1-06/M1-07.</p></div>}
    {creating && <CreateCourseDialog onClose={() => setCreating(false)} onCreated={id => { setCreating(false); void loadDetail(id); }} />}
  </section>;
}

function CourseDetail({ value, mode, setMode, onBack, onReload, error, loading }: {
  value: Detail; mode: 'students'|'teachers'|'editors'; setMode: (value:'students'|'teachers'|'editors') => void; onBack:()=>void; onReload:()=>void; error:string; loading:boolean }) {
  const [adding, setAdding] = useState<'students'|'teacher'|'editor'|null>(null);
  const [addingGroup, setAddingGroup] = useState(false);
  const [editingSchedule, setEditingSchedule] = useState(false);
  const [actionError, setActionError] = useState('');
  async function mutate(path: string, body: Record<string, unknown>, prompt: string) {
    if (!window.confirm(prompt)) return;
    setActionError('');
    try {
      await browserApiClient.request(path, object, { method: 'POST', csrf: true, body });
      onReload();
    } catch (reason) {
      setActionError(message(reason));
      if (reason instanceof ApiError && reason.status === 409) onReload();
    }
  }
  return <section className="course-admin-page"><div className="course-detail-heading"><div><button className="link-button" onClick={onBack}>← Все курсы</button>
    <span className="eyebrow">УПРАВЛЕНИЕ КУРСОМ</span><h1>{value.course.title}</h1><p>{value.course.description}</p></div><span className="draft-pill">{value.course.state === 'Published' ? 'Опубликован' : 'Не опубликован'}</span></div>
    {(actionError || error) && <p className="admin-alert" role="alert">{actionError || error}</p>}{loading && <p role="status">Обновляем…</p>}
    <div className="course-notice">Редактор курса управляет импортом и публикацией отдельно от преподавательских назначений по группам.</div>
    <div className="course-actions"><button className="button button--secondary" onClick={() => setAddingGroup(true)}>+ Добавить группу</button>
      <button className="button button--primary" disabled={!value.groups.length} onClick={() => setAdding('students')}>+ Зачислить учеников</button>
      <button className="button button--secondary" disabled={!value.groups.length} onClick={() => setAdding('teacher')}>Назначить преподавателя</button>
      <button className="button button--secondary" onClick={() => setAdding('editor')}>Назначить редактора курса</button>
      <button className="button button--secondary" onClick={() => setEditingSchedule(true)}>Расписание и нагрузка</button></div>
    <div className="group-grid">{value.groups.map(group => <article key={group.groupId}><span className="group-symbol">◎</span><div><strong>{group.name}</strong><small>{group.description || 'Без описания'}</small></div><dl><div><dt>Ученики</dt><dd>{group.studentCount}</dd></div><div><dt>Преподаватели</dt><dd>{group.teacherCount}</dd></div></dl></article>)}</div>
    {!value.groups.length && <div className="account-empty"><strong>Добавьте две группы</strong><p>Зачисление и назначения станут доступны после создания структуры.</p></div>}
    <div className="roster-tabs"><button aria-pressed={mode === 'students'} onClick={() => setMode('students')}>Ученики ({value.students.length})</button><button aria-pressed={mode === 'teachers'} onClick={() => setMode('teachers')}>Преподаватели ({value.teachers.length})</button><button aria-pressed={mode === 'editors'} onClick={() => setMode('editors')}>Редакторы курса ({value.editors.length})</button></div>
    {mode === 'students' ? <div className="roster-table"><div className="roster-head"><span>Ученик</span><span>Группа</span><span>Действия</span></div>{value.students.map(student => <div className="roster-row" key={student.actorId}>
      <span><strong>{student.displayName}</strong><small>{student.externalRosterKey}</small></span><span>{student.groupName}</span><span className="row-actions"><select aria-label={`Новая группа для ${student.displayName}`} defaultValue=""><option value="">Перевести…</option>{value.groups.filter(x => x.groupId !== student.groupId).map(x => <option key={x.groupId} value={x.groupId}>{x.name}</option>)}</select>
        <button onClick={event => { const select = event.currentTarget.previousElementSibling as HTMLSelectElement; if (select.value) void mutate(`/admin/courses/${value.course.courseId}/students/${student.actorId}/move`, { targetGroupId: select.value, expectedVersion: student.version, idempotencyKey: crypto.randomUUID() }, `Перевести ${student.displayName} в выбранную группу?`); }}>Перевести</button>
        <button className="danger-link" onClick={() => void mutate(`/admin/courses/${value.course.courseId}/students/${student.actorId}/remove`, { expectedVersion: student.version, idempotencyKey: crypto.randomUUID() }, `Исключить ${student.displayName} из курса? Ранее выданные задания сохранят исходную группу.`)}>Исключить</button></span></div>)}</div>
      : mode === 'teachers' ? <div className="roster-table"><div className="roster-head"><span>Преподаватель</span><span>Группа</span><span>Действия</span></div>{value.teachers.map(teacher => <div className="roster-row" key={teacher.grantId}>
        <span><strong>{teacher.displayName}</strong><small>{teacher.externalRosterKey}</small></span><span>{teacher.groupName}</span><span><button className="danger-link" onClick={() => void mutate(`/admin/courses/${value.course.courseId}/groups/${teacher.groupId}/teachers/${teacher.actorId}/remove`, { grantId: teacher.grantId, expectedVersion: teacher.version, idempotencyKey: crypto.randomUUID() }, `Снять назначение ${teacher.displayName}?`)}>Снять назначение</button></span></div>)}</div>
      : <div className="roster-table"><div className="roster-head"><span>Редактор курса</span><span>Полномочие</span><span>Действия</span></div>{value.editors.map(editor => <div className="roster-row" key={editor.grantId}>
        <span><strong>{editor.displayName}</strong><small>{editor.externalRosterKey}</small></span><span>Импорт и публикация</span><span><button className="danger-link" onClick={() => void mutate(`/admin/courses/${value.course.courseId}/editors/${editor.actorId}/remove`, { grantId: editor.grantId, expectedVersion: editor.version, idempotencyKey: crypto.randomUUID() }, `Отозвать полномочие редактора у ${editor.displayName}?`)}>Отозвать полномочие</button></span></div>)}</div>}
    {adding === 'students' && <EnrollmentDialog detail={value} onClose={() => setAdding(null)} onDone={() => { setAdding(null); onReload(); }} />}
    {adding === 'teacher' && <TeacherDialog detail={value} onClose={() => setAdding(null)} onDone={() => { setAdding(null); onReload(); }} />}
    {adding === 'editor' && <CourseEditorDialog detail={value} onClose={() => setAdding(null)} onDone={() => { setAdding(null); onReload(); }} />}
    {addingGroup && <CreateGroupDialog courseId={value.course.courseId} onClose={() => setAddingGroup(false)} onCreated={() => { setAddingGroup(false); onReload(); }} />}
    {editingSchedule && <CourseScheduleEditor courseId={value.course.courseId} onClose={() => setEditingSchedule(false)}/>}
  </section>;
}

function CreateCourseDialog({ onClose, onCreated }: { onClose:()=>void; onCreated:(id:string)=>void }) {
  const [title,setTitle]=useState(''); const [description,setDescription]=useState(''); const [pending,setPending]=useState(false); const [error,setError]=useState(''); const [key]=useState(()=>crypto.randomUUID());
  async function submit(event:FormEvent){event.preventDefault();setPending(true);setError('');try{const result=object(await browserApiClient.request('/admin/courses',object,{method:'POST',csrf:true,body:{title,description,idempotencyKey:key}}));onCreated(String(result.courseId));}catch(reason){setError(message(reason));}finally{setPending(false);}}
  return <Modal title="Создание курса" onClose={onClose}><form className="account-form" onSubmit={submit}>{error&&<p className="admin-alert" role="alert">{error}</p>}<label>Название курса<span>*</span><input required minLength={2} maxLength={160} value={title} onChange={e=>setTitle(e.target.value)}/></label><label>Описание<textarea maxLength={500} value={description} onChange={e=>setDescription(e.target.value)}/></label><p className="form-note">Курс создаётся как черновик без опубликованных материалов.</p><div className="form-actions"><button type="button" className="button button--secondary" onClick={onClose}>Отмена</button><button className="button button--primary" disabled={pending}>{pending?'Создаём…':'Создать курс'}</button></div></form></Modal>;
}
function CreateGroupDialog({courseId,onClose,onCreated}:{courseId:string;onClose:()=>void;onCreated:()=>void}){
  const[name,setName]=useState('');const[description,setDescription]=useState('');const[pending,setPending]=useState(false);const[error,setError]=useState('');const[key]=useState(()=>crypto.randomUUID());
  async function submit(event:FormEvent){event.preventDefault();setPending(true);try{await browserApiClient.request(`/admin/courses/${courseId}/groups`,object,{method:'POST',csrf:true,body:{name,description,idempotencyKey:key}});onCreated();}catch(reason){setError(message(reason));}finally{setPending(false);}}
  return <Modal title="Новая группа" onClose={onClose}><form className="account-form" onSubmit={submit}>{error&&<p className="admin-alert" role="alert">{error}</p>}<label>Название группы<span>*</span><input required minLength={2} maxLength={120} value={name} onChange={e=>setName(e.target.value)}/></label><label>Описание<textarea maxLength={300} value={description} onChange={e=>setDescription(e.target.value)}/></label><div className="form-actions"><button type="button" className="button button--secondary" onClick={onClose}>Отмена</button><button className="button button--primary" disabled={pending}>{pending?'Добавляем…':'Добавить группу'}</button></div></form></Modal>;
}
function EnrollmentDialog({detail,onClose,onDone}:{detail:Detail;onClose:()=>void;onDone:()=>void}){
  const[items,setItems]=useState<Account[]>([]);const[selected,setSelected]=useState<string[]>([]);const[groupId,setGroupId]=useState(detail.groups[0]?.groupId??'');const[error,setError]=useState('');const[pending,setPending]=useState(false);
  useEffect(()=>{void browserApiClient.request('/admin/accounts?role=Student&state=Active&limit=100',accounts).then(rows=>setItems(rows.filter(x=>!detail.students.some(y=>y.actorId===x.actorId)))).catch(reason=>setError(message(reason)));},[detail]);
  async function submit(){setPending(true);try{await browserApiClient.request(`/admin/courses/${detail.course.courseId}/groups/${groupId}/students`,object,{method:'POST',csrf:true,body:{studentIds:selected,idempotencyKey:crypto.randomUUID()}});onDone();}catch(reason){setError(message(reason));}finally{setPending(false);}}
  return <Modal title="Зачисление учеников" onClose={onClose}>{error&&<p className="admin-alert" role="alert">{error}</p>}<label className="modal-field">Группа<select value={groupId} onChange={e=>setGroupId(e.target.value)}>{detail.groups.map(x=><option key={x.groupId} value={x.groupId}>{x.name}</option>)}</select></label><div className="picker-list">{items.map(item=><label key={item.actorId}><input type="checkbox" checked={selected.includes(item.actorId)} onChange={e=>setSelected(e.target.checked?[...selected,item.actorId]:selected.filter(x=>x!==item.actorId))}/><span><strong>{item.displayName}</strong><small>{item.externalRosterKey}</small></span></label>)}</div><div className="form-actions"><button className="button button--secondary" onClick={onClose}>Отмена</button><button className="button button--primary" disabled={pending||!selected.length} onClick={()=>void submit()}>Добавить выбранных ({selected.length})</button></div></Modal>;
}
function TeacherDialog({detail,onClose,onDone}:{detail:Detail;onClose:()=>void;onDone:()=>void}){
  const[items,setItems]=useState<Account[]>([]);const[teacherId,setTeacherId]=useState('');const[groupId,setGroupId]=useState(detail.groups[0]?.groupId??'');const[error,setError]=useState('');const[pending,setPending]=useState(false);
  useEffect(()=>{void browserApiClient.request('/admin/accounts?role=Teacher&state=Active&limit=100',accounts).then(rows=>{setItems(rows);setTeacherId(rows[0]?.actorId??'');}).catch(reason=>setError(message(reason)));},[]);
  async function submit(){setPending(true);try{await browserApiClient.request(`/admin/courses/${detail.course.courseId}/groups/${groupId}/teachers`,object,{method:'POST',csrf:true,body:{teacherId,idempotencyKey:crypto.randomUUID()}});onDone();}catch(reason){setError(message(reason));}finally{setPending(false);}}
  return <Modal title="Назначение преподавателя" onClose={onClose}>{error&&<p className="admin-alert" role="alert">{error}</p>}<label className="modal-field">Преподаватель<select value={teacherId} onChange={e=>setTeacherId(e.target.value)}>{items.map(x=><option key={x.actorId} value={x.actorId}>{x.displayName}</option>)}</select></label><label className="modal-field">Группа<select value={groupId} onChange={e=>setGroupId(e.target.value)}>{detail.groups.map(x=><option key={x.groupId} value={x.groupId}>{x.name}</option>)}</select></label><div className="form-actions"><button className="button button--secondary" onClick={onClose}>Отмена</button><button className="button button--primary" disabled={pending||!teacherId} onClick={()=>void submit()}>Назначить</button></div></Modal>;
}
function CourseEditorDialog({detail,onClose,onDone}:{detail:Detail;onClose:()=>void;onDone:()=>void}){
  const[items,setItems]=useState<Account[]>([]);const[editorId,setEditorId]=useState('');const[error,setError]=useState('');const[pending,setPending]=useState(false);
  useEffect(()=>{void browserApiClient.request('/admin/accounts?role=Teacher&state=Active&limit=100',accounts).then(rows=>{const available=rows.filter(x=>!detail.editors.some(y=>y.actorId===x.actorId));setItems(available);setEditorId(available[0]?.actorId??'');}).catch(reason=>setError(message(reason)));},[detail.editors]);
  async function submit(){setPending(true);try{await browserApiClient.request(`/admin/courses/${detail.course.courseId}/editors`,object,{method:'POST',csrf:true,body:{editorId,idempotencyKey:crypto.randomUUID()}});onDone();}catch(reason){setError(message(reason));}finally{setPending(false);}}
  return <Modal title="Редактор курса" onClose={onClose}>{error&&<p className="admin-alert" role="alert">{error}</p>}<p className="form-note">Полномочие разрешает импорт, предпросмотр и публикацию. Доступ к группам и работам не добавляется.</p><label className="modal-field">Редактор<select value={editorId} onChange={e=>setEditorId(e.target.value)}>{items.map(x=><option key={x.actorId} value={x.actorId}>{x.displayName}</option>)}</select></label><div className="form-actions"><button className="button button--secondary" onClick={onClose}>Отмена</button><button className="button button--primary" disabled={pending||!editorId} onClick={()=>void submit()}>Назначить редактора</button></div></Modal>;
}
function Modal({title,onClose,children}:{title:string;onClose:()=>void;children:ReactNode}){return <div className="admin-modal-backdrop"><section className="admin-modal" role="dialog" aria-modal="true" aria-label={title}><button className="modal-close" onClick={onClose} aria-label="Закрыть">×</button><span className="eyebrow">КУРСЫ И ГРУППЫ</span><h2>{title}</h2>{children}</section></div>;}
