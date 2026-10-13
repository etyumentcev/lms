import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApiError, type ApiClient } from '../../api/client';
import { browserApiClient } from '../../api/browserClient';
import { decodePublishedStudentContent, decodeStudentAssignments, type PublishedStudentContent,
  type StudentAssignment } from '../../api/contracts/publishedContent';
import { AppShell, ContextSection } from '../../layout/AppShell';
import { AvailabilityBadge, StatusBadge } from '../../components/StatusBadge';
import { Breadcrumbs, EmptyState, ErrorState, LoadingSkeleton, PageHeader, ProgressBar } from '../../components/primitives';
import { MarkdownContent } from '../../components/MarkdownContent';
import './studentStudy.css';

type CatalogCourse = { courseId: string; title: string; description: string };
type State = { phase:'loading' } | { phase:'error'; error:ApiError } | { phase:'ready'; catalog?:CatalogCourse;
  assignments:StudentAssignment[]; versions:Map<string,PublishedStudentContent> };
function catalogCourse(value:unknown, courseId:string):CatalogCourse|undefined {
  const items=(value as {items?:unknown})?.items;if(!Array.isArray(items))throw new Error('Invalid course catalog');
  const row=items.find(x=>(x as {courseId?:unknown})?.courseId===courseId) as Record<string,unknown>|undefined;
  if(!row)return undefined;if(typeof row.title!=='string'||typeof row.description!=='string')throw new Error('Invalid course catalog');
  return {courseId,title:row.title,description:row.description};
}
function useStudy(client:ApiClient,courseId:string,onlyVersion?:string){
  const[state,setState]=useState<State>({phase:'loading'});const[revision,setRevision]=useState(0);
  useEffect(()=>{const controller=new AbortController();setState({phase:'loading'});void Promise.all([
    client.request('/my/courses',v=>catalogCourse(v,courseId),{signal:controller.signal}),
    client.request(`/my/courses/${encodeURIComponent(courseId)}/assignments`,decodeStudentAssignments,{signal:controller.signal}),
  ]).then(async([catalog,assignments])=>{const ids=[...new Set(assignments.map(x=>x.contentVersionId))].filter(x=>!onlyVersion||x===onlyVersion);
    const content=await Promise.all(ids.map(id=>client.request(`/my/courses/${encodeURIComponent(courseId)}/versions/${encodeURIComponent(id)}/content`,decodePublishedStudentContent,{signal:controller.signal}).then(value=>[id,value] as const)));
    if(!controller.signal.aborted)setState({phase:'ready',catalog,assignments,versions:new Map(content)});
  }).catch(reason=>{if(!controller.signal.aborted)setState({phase:'error',error:reason instanceof ApiError?reason:new ApiError(0,'InvalidResponse')});});return()=>controller.abort();},[client,courseId,onlyVersion,revision]);
  return{state,retry:()=>setRevision(x=>x+1)};
}
function percent(accepted:number,total:number){return total?Math.round(accepted/total*100):0;}
function assetLoader(client:ApiClient,courseId:string,versionId:string){return async(path:string)=>client.request(
  `/my/courses/${encodeURIComponent(courseId)}/versions/${encodeURIComponent(versionId)}/assets/${path.split('/').map(encodeURIComponent).join('/')}`,
  value=>value as Blob,{responseType:'blob'});}
function Failure({error,retry}:{error:ApiError;retry:()=>void}){return <ErrorState title={error.status===403?'Нет доступа к материалам':'Не удалось загрузить курс'}
  description="Проверьте сессию, доступ к группе и соединение." onRetry={[401,403,404].includes(error.status)?undefined:retry}/>;}

export function StudentPublishedCoursePage({courseId,client=browserApiClient}:{courseId:string;client?:ApiClient}){
  const{state,retry}=useStudy(client,courseId);const ready=state.phase==='ready'?state:null;
  const rows=useMemo(()=>ready?[...ready.versions.entries()].flatMap(([versionId,content])=>content.course.lessons.map(lesson=>{
    const assignments=ready.assignments.filter(x=>x.contentVersionId===versionId&&x.lessonId===lesson.id);
    return{versionId,versionNumber:assignments[0]?.versionNumber??0,content,lesson,assignments};
  })).sort((a,b)=>a.lesson.order-b.lesson.order||a.versionNumber-b.versionNumber):[],[ready]);
  const all=ready?.assignments??[];const accepted=all.filter(x=>x.status==='Accepted').length;const progress=percent(accepted,all.length);
  const first=rows[0];const title=first?.content.course.title||ready?.catalog?.title||'Курс';
  const load=useCallback((path:string)=>first?assetLoader(client,courseId,first.versionId)(path):Promise.reject(),[client,courseId,first]);
  useEffect(()=>{document.title=`${title} · Sapientum`;},[title]);
  return <AppShell role="Student" preview={false} context={ready?<><ContextSection title="Прогресс курса"><p>{accepted} из {all.length} задач принято</p><ProgressBar value={progress} label="Прогресс курса"/><p className="muted small">Только по реально назначенным задачам.</p></ContextSection><ContextSection title="Доступ"><p>{rows.length} занятий · {all.length} задач</p><p className="muted small">Каждое занятие привязано к опубликованной версии.</p></ContextSection></>:null}>
    <Breadcrumbs items={[{label:'Мои курсы',to:'/courses'},{label:title}]}/><div className="workspace-content student-study" aria-busy={state.phase==='loading'}>
      <PageHeader title={title} description={first?.content.course.description||ready?.catalog?.description}/>
      {state.phase==='loading'&&<><LoadingSkeleton label="Загрузка курса" rows={3}/><LoadingSkeleton label="Загрузка занятий" rows={4}/></>}
      {state.phase==='error'&&<Failure error={state.error} retry={retry}/>} {first&&<section className="study-theory" aria-label="Описание курса"><MarkdownContent assets={first.content.assets} loadAsset={load}>{first.content.course.bodyMarkdown}</MarkdownContent></section>}
      {ready&&<section aria-labelledby="published-lessons"><div className="section-intro"><h2 id="published-lessons">Занятия</h2><span className="muted">{rows.length}</span></div>{rows.length?<ol className="study-card-list">{rows.map(row=>{
        const done=row.assignments.filter(x=>x.status==='Accepted').length;return <li key={`${row.versionId}/${row.lesson.id}`}><Link to={`/study/courses/${encodeURIComponent(courseId)}/versions/${encodeURIComponent(row.versionId)}/lessons/${encodeURIComponent(row.lesson.id)}`}><span className="row-number">{row.lesson.order}</span><span className="row-text"><strong>{row.lesson.title}</strong><small><AvailabilityBadge isAvailable/> · {row.assignments.length} задач · {done} принято · версия v{row.versionNumber}</small></span><span aria-hidden="true">→</span></Link></li>;})}</ol>:<EmptyState title="Нет доступных занятий" description="Преподаватель ещё не открыл занятие вашей группе."/>}</section>}
    </div></AppShell>;
}

export function StudentPublishedLessonPage({courseId,versionId,lessonId,client=browserApiClient}:{courseId:string;versionId:string;lessonId:string;client?:ApiClient}){
  const{state,retry}=useStudy(client,courseId,versionId);const ready=state.phase==='ready'?state:null;const content=ready?.versions.get(versionId);const lesson=content?.course.lessons.find(x=>x.id===lessonId);
  const assignments=(ready?.assignments??[]).filter(x=>x.contentVersionId===versionId&&x.lessonId===lessonId);const accepted=assignments.filter(x=>x.status==='Accepted').length;
  const load=useCallback(assetLoader(client,courseId,versionId),[client,courseId,versionId]);const title=lesson?.title||'Занятие';
  useEffect(()=>{document.title=`${title} · Sapientum`;},[title]);
  return <AppShell role="Student" preview={false} context={lesson?<><ContextSection title="О занятии"><p>Занятие {lesson.order}</p><AvailabilityBadge isAvailable/><p>Опубликованная версия v{assignments[0]?.versionNumber??'—'}</p></ContextSection><ContextSection title="Прогресс занятия"><p>{accepted} из {assignments.length} задач принято</p><ProgressBar value={percent(accepted,assignments.length)} label="Прогресс занятия"/></ContextSection></>:null}>
    <Breadcrumbs items={[{label:'Мои курсы',to:'/courses'},{label:content?.course.title||'Курс',to:`/study/courses/${encodeURIComponent(courseId)}`},{label:title}]}/><div className="workspace-content student-study" aria-busy={state.phase==='loading'}>
      <PageHeader title={title} description={lesson?`Занятие ${lesson.order} · ${assignments.length} задач`:undefined}/>
      {state.phase==='loading'&&<><LoadingSkeleton label="Загрузка теории" rows={5}/><LoadingSkeleton label="Загрузка задач" rows={5}/></>}{state.phase==='error'&&<Failure error={state.error} retry={retry}/>} 
      {ready&&!lesson&&<ErrorState title="Занятие недоступно" description="Оно не открыто вашей группе или относится к другой версии."/>}
      {lesson&&content&&<><section className="study-theory" aria-labelledby="lesson-theory"><h2 id="lesson-theory">Теория</h2><MarkdownContent assets={content.assets} loadAsset={load}>{lesson.bodyMarkdown}</MarkdownContent></section><section aria-labelledby="lesson-problems"><div className="section-intro"><h2 id="lesson-problems">Задачи</h2><span className="muted">{assignments.length}</span></div>{assignments.length?<ol className="study-card-list">{lesson.problems.map(problem=>{const assignment=assignments.find(x=>x.problemId===problem.id);return assignment?<li key={assignment.assignmentId}><Link to={`/problem-assignments/${encodeURIComponent(assignment.assignmentId)}`}><span className="row-number">{problem.order}</span><span className="row-text"><strong>{problem.title||`Задача ${problem.order}`}</strong><small><StatusBadge status={assignment.status}/>{assignment.attemptCount?` · ${assignment.attemptCount} попыток`:''}</small></span><span aria-hidden="true">→</span></Link></li>:null;})}</ol>:<EmptyState title="В занятии пока нет назначенных задач"/>}</section></>}
    </div></AppShell>;
}
