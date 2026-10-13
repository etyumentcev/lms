import { useCallback, useEffect, useState } from 'react';
import type { ApiClient, ApiError } from '../../api/client';
import { decodeTeacherReference, type TeacherReference } from '../../api/contracts/teacherReference';
import { MarkdownContent } from '../../components/MarkdownContent';
import { Button } from '../../components/primitives';

const labels={solution:'Эталонное решение',rubric:'Критерии оценки',notes:'Методические рекомендации'} as const;
type State={phase:'loading'}|{phase:'error';error:ApiError}|{phase:'ready';data:TeacherReference};
export function TeacherReferencePanel({client,problemWorkId}:{client:ApiClient;problemWorkId:string}){
  const[state,setState]=useState<State>({phase:'loading'});const[revision,setRevision]=useState(0);
  useEffect(()=>{const controller=new AbortController();setState({phase:'loading'});void client.request(`/teacher/problem-works/${encodeURIComponent(problemWorkId)}/reference`,decodeTeacherReference,{signal:controller.signal})
    .then(data=>{if(!controller.signal.aborted)setState({phase:'ready',data});}).catch(reason=>{if(!controller.signal.aborted)setState({phase:'error',error:reason as ApiError});});return()=>controller.abort();},[client,problemWorkId,revision]);
  const data=state.phase==='ready'?state.data:null;
  const load=useCallback((path:string)=>client.request(`/teacher/problem-works/${encodeURIComponent(problemWorkId)}/reference/assets/${path.split('/').map(encodeURIComponent).join('/')}`,value=>value as Blob,{responseType:'blob'}),[client,problemWorkId]);
  if(state.phase==='loading')return <p className="muted small">Загрузка эталона…</p>;
  if(state.phase==='error')return <><p className="muted small">Эталон недоступен.</p>{![401,403,404].includes(state.error?.status)&&<Button variant="secondary" onClick={()=>setRevision(x=>x+1)}>Повторить</Button>}</>;
  return <div className="teacher-reference" data-testid="teacher-reference"><p className="reference-version">Неизменяемая версия v{data!.versionNumber}</p>
    {data!.materials.length?data!.materials.map((item,index)=><details key={item.id} open={index===0}><summary>{item.title||labels[item.kind]}</summary><MarkdownContent assets={data!.assets} loadAsset={load}>{item.bodyMarkdown}</MarkdownContent></details>):<p className="muted small">Для этой задачи эталонные материалы не опубликованы.</p>}
  </div>;
}
