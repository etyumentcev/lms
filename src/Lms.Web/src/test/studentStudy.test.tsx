import { render,screen,waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import userEvent from '@testing-library/user-event';
import { beforeEach,describe,expect,it,vi } from 'vitest';
import type { ApiClient,RequestOptions } from '../api/client';
import { StudentPublishedCoursePage,StudentPublishedLessonPage } from '../features/content/StudentStudyPage';

const version='10000000-0000-0000-0000-000000000001';
const assignment='20000000-0000-0000-0000-000000000001';
const artifact={artifactSchema:'student-content/v1',contractRevision:'1.1',studentProjectionHash:'a'.repeat(64),course:{id:'euler',title:'Олимпиадная математика',description:'Опубликованный курс',bodyMarkdown:'Курс с формулой $V-E+F=2$.',lessons:[{id:'number-theory',title:'Остатки и сравнения',order:1,bodyMarkdown:'Теория: $a \\equiv b \\pmod m$.\n\n![Схема](./assets/euler-diagram.png)',problems:[{id:'p1',title:'Первая задача',order:1,bodyMarkdown:'Докажите сравнение.',hints:[{id:'h1',order:1,bodyMarkdown:'Начните с остатков.'}]}]}]},assets:[{id:'euler-diagram',ownerKind:'course',ownerId:'euler',visibility:'student',mediaType:'image/png',sizeBytes:100,sha256:'b'.repeat(64),artifactPath:'assets/euler-diagram.png'}]};
const assignments={items:[{assignmentId:assignment,contentVersionId:version,versionNumber:1,lessonId:'number-theory',lessonTitle:'Остатки и сравнения',problemId:'p1',problemTitle:'Первая задача',problemOrder:1,status:'NeedsRevision',attemptCount:2}]};
function client(){const request=vi.fn(async(url:string,decode:(value:unknown)=>unknown,_options:RequestOptions={})=>decode(url==='/my/courses'?{items:[{courseId:'course-1',title:'Каталог',description:'Описание'}]}:url.endsWith('/assignments')?assignments:url.startsWith('/student/progress-history')?{items:[],page:1,pageSize:5,total:0,summary:{studentCount:1,assignedCount:1,notStarted:0,submitted:0,needsRevision:1,accepted:0}}:url.endsWith('/schedule')?{workload:null,rules:[]}:url.includes('/schedule/upcoming')?{items:[]}:url.includes('/assets/')?new Blob(['png'],{type:'image/png'}):artifact));return{request,client:{request} as ApiClient};}
beforeEach(()=>{URL.createObjectURL=vi.fn(()=> 'blob:lesson-asset');URL.revokeObjectURL=vi.fn();});

describe('M1-07 student study views',()=>{
  it('shows the summary, own accepted-task progress and existing lessons on tab switch',async()=>{
    const c=client();
    render(<MemoryRouter initialEntries={['/study/courses/course-1']}><StudentPublishedCoursePage courseId="course-1" client={c.client}/></MemoryRouter>);
    expect(await screen.findByRole('heading',{level:1,name:'Олимпиадная математика'})).toBeVisible();
    expect(screen.getByRole('link',{name:'Сводка'})).toHaveAttribute('aria-current','page');
    expect(screen.getByRole('progressbar',{name:'Принято задач'})).toHaveAttribute('aria-valuenow','0');
    expect(await screen.findByText('Отправленных задач пока нет.')).toBeVisible();
    expect(screen.getByRole('complementary')).toHaveTextContent('Опубликованный курс');
    await userEvent.click(screen.getByRole('link',{name:'Занятия'}));
    expect(screen.getByText(/версия v1/)).toBeVisible();
    expect(screen.getByRole('link',{name:'Занятия'})).toHaveAttribute('aria-current','page');
    expect(c.request).toHaveBeenCalledTimes(6);
    expect(c.request.mock.calls.some(([url])=>String(url).endsWith('/schedule'))).toBe(true);
    expect(c.request.mock.calls.some(([url])=>String(url).includes('/schedule/upcoming'))).toBe(true);
  });
  it('renders lesson theory, KaTeX, protected image and published problem order',async()=>{const c=client();const{container}=render(<MemoryRouter><StudentPublishedLessonPage courseId="course-1" versionId={version} lessonId="number-theory" client={c.client}/></MemoryRouter>);expect(await screen.findByRole('heading',{name:'Остатки и сравнения'})).toBeVisible();await screen.findByAltText('Схема');expect(container.querySelector('.katex')).not.toBeNull();expect(screen.getByRole('link',{name:/Первая задача/})).toHaveAttribute('href',`/problem-assignments/${assignment}`);expect(screen.getByText('Нужна доработка')).toBeVisible();await waitFor(()=>expect(c.request.mock.calls.some(([url])=>String(url).includes('/assets/assets%2Feuler-diagram.png'))).toBe(false));expect(c.request.mock.calls.some(([url])=>String(url).includes('/assets/assets/euler-diagram.png'))).toBe(true);});
});
