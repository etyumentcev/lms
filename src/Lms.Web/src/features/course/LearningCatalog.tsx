import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApiError } from '../../api/client';
import type { ApiClient } from '../../api/client';
import { browserApiClient } from '../../api/browserClient';
import { decodeStudentAssignments, type StudentAssignment } from '../../api/contracts/publishedContent';
import { AppShell } from '../../layout/AppShell';
import { Breadcrumbs, EmptyState, ErrorState, LoadingSkeleton, PageHeader, ProgressBar } from '../../components/primitives';
import { CourseMaterialsPage } from '../content/CourseMaterialsPage';
import './learningCatalog.css';

type Role = 'Student' | 'Teacher';
type LearningGroup = { groupId: string; name: string; studentCount: number };
type LearningCourse = { courseId: string; title: string; description: string; state: 'Draft'|'Published'; role: Role; groups: LearningGroup[]; canEditContent: boolean };
type TeacherStudent = { actorId: string; displayName: string; externalRosterKey: string; groupName: string };
type TeacherDetail = { course: { courseId:string;title:string;description:string;state:'Draft'|'Published';version:number }; groups: LearningGroup[]; students: TeacherStudent[] };
type StudentProgress = { accepted: number; total: number; percent: number };
function object(value: unknown): Record<string, unknown> { if (!value || typeof value !== 'object') throw new Error('Invalid response'); return value as Record<string, unknown>; }
function list(value: unknown): LearningCourse[] { const root=object(value); if(!Array.isArray(root.items))throw new Error('Invalid response'); return root.items.map(row=>{const x=object(row);if(typeof x.courseId!=='string'||typeof x.title!=='string'||typeof x.description!=='string'||!['Draft','Published'].includes(String(x.state))||!['Student','Teacher'].includes(String(x.role))||!Array.isArray(x.groups)||typeof x.canEditContent!=='boolean')throw new Error('Invalid response');return x as LearningCourse;}); }
function teacherDetail(value:unknown):TeacherDetail{const x=object(value);if(!x.course||!Array.isArray(x.groups)||!Array.isArray(x.students))throw new Error('Invalid response');return x as TeacherDetail;}
export function studentProgress(assignments: StudentAssignment[]): StudentProgress {
  const statuses = new Map<string, StudentAssignment['status']>();
  for (const assignment of assignments) {
    if (statuses.get(assignment.assignmentId) !== 'Accepted') statuses.set(assignment.assignmentId, assignment.status);
  }
  const total = statuses.size;
  const accepted = [...statuses.values()].filter(status => status === 'Accepted').length;
  return { accepted, total, percent: total === 0 ? 0 : Math.round(accepted / total * 100) };
}

export function LearningCatalog({ role, client=browserApiClient }: { role: Role; client?: ApiClient }) {
  const [items,setItems]=useState<LearningCourse[]>([]);const[selected,setSelected]=useState<{course:LearningCourse;detail?:TeacherDetail;view?:'materials'|'roster'}|null>(null);
  const[progress,setProgress]=useState<Record<string,StudentProgress>>({});
  const[loading,setLoading]=useState(true);const[error,setError]=useState<ApiError|null>(null);
  async function load(){setLoading(true);setError(null);try{const courses=await client.request('/my/courses',list);if(role==='Student'){const values=await Promise.all(courses.map(async course=>[course.courseId,studentProgress(await client.request(`/my/courses/${encodeURIComponent(course.courseId)}/assignments`,decodeStudentAssignments))] as const));setProgress(Object.fromEntries(values));}else setProgress({});setItems(courses);}catch(reason){setError(reason instanceof ApiError?reason:new ApiError(0,'InvalidResponse'));}finally{setLoading(false);}}
  useEffect(()=>{void load();},[client,role]);
  async function open(course:LearningCourse,view:'materials'|'roster'='materials'){setLoading(true);try{setSelected({course,view,detail:await client.request(`/teacher/courses/${encodeURIComponent(course.courseId)}`,teacherDetail)});}catch(reason){setError(reason instanceof ApiError?reason:new ApiError(0,'InvalidResponse'));}finally{setLoading(false);}}
  if(selected&&role==='Teacher'&&selected.detail&&selected.view==='roster')return <AppShell preview={false} role="Teacher" context={null}><Breadcrumbs items={[{label:'Мои курсы'},{label:selected.course.title},{label:'Ученики'}]}/><div className="workspace-content"><button className="link-button" onClick={()=>{setSelected(null);void load();}}>← Мои курсы</button><PageHeader title="Ученики" description={selected.course.title}/><section className="learning-groups"><h2>Назначенные группы</h2>{selected.detail.groups.map(group=><article key={group.groupId}><strong>{group.name}</strong><span>{group.studentCount} учеников</span></article>)}</section><section className="learning-roster"><h2>Состав групп</h2>{selected.detail.students.map(student=><article key={student.actorId}><div><strong>{student.displayName}</strong><small>{student.externalRosterKey} · {student.groupName}</small></div></article>)}</section></div></AppShell>;
  if(selected&&role==='Teacher'&&selected.detail)return <AppShell preview={false} role="Teacher" context={null}><Breadcrumbs items={[{label:'Мои курсы'},{label:selected.course.title},{label:'Материалы'}]}/><div className="workspace-content"><CourseMaterialsPage course={{courseId:selected.course.courseId,title:selected.course.title,description:selected.course.description,version:selected.detail.course.version}} groups={selected.detail.groups} onBack={()=>{setSelected(null);void load();}}/></div></AppShell>;
  return <AppShell preview={false} role={role} context={null}><Breadcrumbs items={[{label:'Мои курсы'}]}/><div className="workspace-content" aria-busy={loading}><PageHeader title="Мои курсы" description={role==='Teacher'?'Курсы, где вы преподаёте группе или управляете содержанием.':'Курсы, на которые вы зачислены.'}/>{loading&&<LoadingSkeleton label="Загрузка курсов"/>}{error&&<ErrorState title={error.status===403?'Нет доступа к каталогу':'Не удалось загрузить курсы'} description="Проверьте сессию и повторите загрузку." onRetry={()=>void load()}/>} {!loading&&!error&&(items.length?<div className={`learning-course-grid${role==='Student'?' learning-course-grid--student':''}`}>{items.map(item=>role==='Student'?<StudentCourseCard key={item.courseId} course={item} progress={progress[item.courseId]}/>:<article key={item.courseId}><span className="course-icon">∑</span><span className="draft-pill">{item.state==='Published'?'Опубликован':'Черновик'}</span><h2>{item.title}</h2><p>{item.description||'Описание пока не добавлено'}</p><div>{item.groups.map(group=><span className="group-chip" key={group.groupId}>{group.name} · {group.studentCount}</span>)}</div>{item.canEditContent&&<button className="button button--primary" onClick={()=>void open(item)}>Материалы и публикация</button>}{item.groups.length>0&&<button className="button button--secondary" onClick={()=>void open(item,'roster')}>Открыть группы и учеников</button>}<small>{item.state==='Published'?'Доступна опубликованная версия.':'Материалы появятся после публикации преподавателем.'}</small></article>)}</div>:<EmptyState title="Нет доступных курсов" description="Администратор ещё не добавил вас в группу или курс."/>)}</div></AppShell>;
}

function StudentCourseCard({course,progress}:{course:LearningCourse;progress?:StudentProgress}) {
  return <Link className="student-course-card" to={`/study/courses/${encodeURIComponent(course.courseId)}`}>
    <h2>{course.title}</h2>
    {course.description&&<p>{course.description}</p>}
    {course.groups.length>0&&<div className="student-course-groups">{course.groups.map(group=><span className="group-chip" key={group.groupId}>{group.name}</span>)}</div>}
    <div className="student-course-progress">{progress&&progress.total>0
      ?<ProgressBar value={progress.percent} label={`Сдано ${progress.accepted} из ${progress.total} задач`}/>
      :<span className="student-course-empty-progress">Пока нет задач</span>}</div>
  </Link>;
}
