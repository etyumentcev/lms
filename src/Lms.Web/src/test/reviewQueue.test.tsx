import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, useLocation, useNavigate } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { createApiClient } from '../api/client';
import { decodeReviewQueue } from '../api/contracts/reviewQueue';
import type { ReviewQueue } from '../api/contracts/reviewQueue';
import { normalizeQueueQuery } from '../features/review-queue/query';
import { waitingTime } from '../features/review-queue/ReviewQueuePage';
const id = '50000000-0000-0000-0000-000000000001';
const studentId = '10000000-0000-0000-0000-000000000001';
const fixture = (): ReviewQueue => ({ items: [{ problemWorkId:id, submissionId:'60000000-0000-0000-0000-000000000001',
  studentId, studentDisplayName:null, courseId:'math', courseTitle:'Математика', lessonId:'division', lessonTitle:'Делимость',
  groupId:'group-a', groupTitle:null, problemId:'sum', problemTitle:'Делимость суммы', attemptNumber:2,
  submittedAt:'2026-10-07T10:15:00Z', waitingDurationSeconds:7200, version:3 }], page:1, pageSize:50, total:12,
  summary:{ total:12, firstAttempts:7, revisions:5, averageWaitingSeconds:3600, byCourse:[{ courseId:'math', courseTitle:'Математика', count:12 }] } });
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), {status, headers:{'Content-Type':'application/json'}});
function Controls() { const location=useLocation(), navigate=useNavigate(); return <><output aria-label="URL">{location.pathname}{location.search}</output><button onClick={()=>navigate(-1)}>Back</button><button onClick={()=>navigate(1)}>Forward</button></>; }
function mount(data = fixture(), fetch = vi.fn<typeof globalThis.fetch>().mockImplementation(async input => {
  const query = new URL(String(input),'https://test').searchParams;
  return response({...data,page:Number(query.get('page') ?? 1),pageSize:Number(query.get('pageSize') ?? 50)});
}), path = '/teacher/reviews') {
  const onUnauthenticated=vi.fn();
  const client=createApiClient({fetch,getAccessToken:()=> 'queue-test-token',onUnauthenticated});
  return {...render(<MemoryRouter future={{v7_startTransition:true,v7_relativeSplatPath:true}} initialEntries={[path]}><App client={client}/><Controls/></MemoryRouter>),fetch,onUnauthenticated};
}
const loaded=()=>screen.findByText(/Найдено работ:/);
const apply=()=>userEvent.click(screen.getByRole('button',{name:'Применить'}));
const requests=(fetch: ReturnType<typeof vi.fn<typeof globalThis.fetch>>) => fetch.mock.calls.map(([url])=>new URL(String(url),'https://test').searchParams);
const empty=()=>({...fixture(),items:[],total:0,summary:{total:0,firstAttempts:0,revisions:0,averageWaitingSeconds:null,byCourse:[]}});

describe('Teacher Review Queue',()=>{
  it('keeps shell, header, filter labels, row and context skeletons while loading',()=>{
    mount(fixture(),vi.fn<typeof globalThis.fetch>().mockReturnValue(new Promise(()=>{})));
    expect(screen.getByRole('heading',{level:1})).toHaveTextContent('Проверка работ');
    expect(screen.getByRole('status',{name:'Загрузка работ'})).toBeInTheDocument();
    expect(screen.getByRole('status',{name:'Загрузка контекста очереди'})).toBeInTheDocument();
    expect(screen.getByRole('main')).toBeInTheDocument();expect(screen.getByLabelText('Поиск')).toBeInTheDocument();
    expect(screen.queryByRole('link',{name:'Проверка'})).not.toBeInTheDocument();
  });
  it('uses bearer-aware GET with AbortSignal and exact C4 decode',async()=>{
    const {fetch}=mount();await loaded();expect(fetch).toHaveBeenCalledOnce();
    const [url,options]=fetch.mock.calls[0]!;expect(url).toBe('/api/v1/teacher/review-queue?page=1&pageSize=50');
    expect(options?.method).toBe('GET');expect(options?.signal).toBeInstanceOf(AbortSignal);
    expect(new Headers(options?.headers).get('Authorization')).toBe('Bearer queue-test-token');
    expect(screen.getByRole('link',{name:'Проверка'})).toBeInTheDocument();
  });
  it('renders student UUID honestly without extra profile requests',async()=>{
    const {fetch}=mount();await loaded();expect(screen.getByText(`Ученик ${studentId}`)).toBeInTheDocument();expect(fetch).toHaveBeenCalledOnce();
  });
  it('uses supplied student name',async()=>{const data=fixture();data.items[0]!.studentDisplayName='Анна Иванова';mount(data);await loaded();expect(screen.getByText('Анна Иванова')).toBeInTheDocument();});
  it('renders problem identity and course/lesson/group context',async()=>{mount();await loaded();expect(screen.getByRole('heading',{level:2,name:'Делимость суммы'})).toBeInTheDocument();expect(screen.getByText('Математика · Делимость · group-a')).toBeInTheDocument();});
  it('uses nullable title fallbacks without fabricating labels',async()=>{const data=fixture();Object.assign(data.items[0]!,{problemTitle:null,courseTitle:null,lessonTitle:null,groupId:null});mount(data);await loaded();expect(screen.getByRole('heading',{name:'Задача sum'})).toBeInTheDocument();expect(screen.getByText('math · division')).toBeInTheDocument();});
  it('renders attempt, semantic timestamp, waiting and canonical status',async()=>{mount();await loaded();expect(screen.getByText('Попытка №2')).toBeInTheDocument();expect(document.querySelector('time')).toHaveAttribute('datetime','2026-10-07T10:15:00Z');expect(screen.getByText('Ожидает 2 ч 0 мин')).toBeInTheDocument();expect(screen.getByLabelText('Статус: На проверке')).toBeInTheDocument();});
  it('navigates using ProblemWorkId to placeholder J and focuses main',async()=>{mount();await loaded();const link=screen.getByRole('link',{name:/Проверить: Ученик/});expect(link).toHaveAttribute('href',`/teacher/reviews/${id}`);await userEvent.click(link);expect(screen.getByText('Раздел готовится')).toBeInTheDocument();expect(screen.getByRole('main')).toHaveFocus();expect(screen.queryByRole('button',{name:'Принять решение'})).not.toBeInTheDocument();});
  it('shows empty queue without network error',async()=>{mount(empty());await loaded();expect(screen.getByText('Все работы проверены')).toBeInTheDocument();expect(screen.queryByRole('alert')).not.toBeInTheDocument();});
  it('shows empty filtered result and resets all filters to page one',async()=>{const {fetch}=mount(empty(),undefined,'/teacher/reviews?courseId=math&search=abc&page=3');await loaded();expect(screen.getByText('По выбранным фильтрам работ нет')).toBeInTheDocument();await userEvent.click(screen.getAllByRole('button',{name:'Сбросить фильтры'})[1]!);await loaded();expect(requests(fetch).at(-1)?.get('page')).toBe('1');expect(requests(fetch).at(-1)?.has('courseId')).toBe(false);expect(requests(fetch).at(-1)?.has('search')).toBe(false);});
  it.each([['Курс · ID','courseId','math'],['Группа · ID','groupId','group-a'],['Ученик · UUID','studentId',studentId]])('sends documented %s filter only on submit',async(label,key,value)=>{const {fetch}=mount();await loaded();await userEvent.type(screen.getByLabelText(label),value);expect(fetch).toHaveBeenCalledOnce();await apply();await loaded();expect(requests(fetch).at(-1)?.get(key)).toBe(value);});
  it('sends attempt kind to server',async()=>{const {fetch}=mount();await loaded();await userEvent.selectOptions(screen.getByLabelText('Попытки'),'revision');await apply();await loaded();expect(requests(fetch).at(-1)?.get('attemptKind')).toBe('revision');});
  it('combines search with filters and avoids requests per keystroke',async()=>{const {fetch}=mount();await loaded();await userEvent.type(screen.getByLabelText('Курс · ID'),'math');await userEvent.type(screen.getByLabelText('Поиск'),'Делимость');expect(fetch).toHaveBeenCalledOnce();await apply();await loaded();expect(requests(fetch).at(-1)?.get('search')).toBe('Делимость');expect(requests(fetch).at(-1)?.get('courseId')).toBe('math');});
  it('preserves search/filters on pagination',async()=>{const {fetch}=mount(fixture(),undefined,'/teacher/reviews?courseId=math&search=sum&pageSize=1');await loaded();await userEvent.click(screen.getByRole('button',{name:'Следующая'}));await loaded();const q=requests(fetch).at(-1)!;expect(q.get('page')).toBe('2');expect(q.get('pageSize')).toBe('1');expect(q.get('courseId')).toBe('math');expect(q.get('search')).toBe('sum');});
  it('resets page on filter change',async()=>{const {fetch}=mount(fixture(),undefined,'/teacher/reviews?page=3&pageSize=1');await loaded();await userEvent.type(screen.getByLabelText('Курс · ID'),'math');await apply();await loaded();expect(requests(fetch).at(-1)?.get('page')).toBe('1');});
  it('hydrates URL fields and supports Back/Forward',async()=>{mount(fixture(),undefined,'/teacher/reviews?courseId=math&search=sum&pageSize=1');await loaded();expect(screen.getByLabelText('Поиск')).toHaveValue('sum');await userEvent.click(screen.getByRole('button',{name:'Следующая'}));await screen.findByText('Найдено работ: 12 · Страница 2');await userEvent.click(screen.getByRole('button',{name:'Back'}));await screen.findByText('Найдено работ: 12 · Страница 1');await userEvent.click(screen.getByRole('button',{name:'Forward'}));await screen.findByText('Найдено работ: 12 · Страница 2');expect(screen.getByLabelText('Поиск')).toHaveValue('sum');});
  it('displays server summary independent of page size',async()=>{mount(fixture(),undefined,'/teacher/reviews?pageSize=1');await loaded();const context=within(screen.getByRole('complementary'));expect(context.getByText('Первые попытки: 7')).toBeInTheDocument();expect(context.getByText('После доработки: 5')).toBeInTheDocument();expect(context.getAllByText('12')).toHaveLength(2);expect(context.queryByText('Делимость суммы')).not.toBeInTheDocument();});
  it('omits unavailable summary breakdown and average',async()=>{mount(empty());await loaded();const context=within(screen.getByRole('complementary'));expect(context.queryByText(/Среднее ожидание/)).not.toBeInTheDocument();expect(context.queryByRole('heading',{name:'По курсам'})).not.toBeInTheDocument();});
  it.each([401,403,404])('handles %s without text parsing or teacher navigation',async status=>{const {onUnauthenticated}=mount(fixture(),vi.fn<typeof globalThis.fetch>().mockResolvedValue(response({type:'urn:lms:error:test',title:'Misleading',status,instance:'/queue',code:status===401?'Unauthenticated':status===403?'Forbidden':'NotFound',traceId:'test'},status)));expect(await screen.findByRole('alert')).toHaveTextContent(status===401?'Войдите':status===403?'Нет доступа':'не найдена');expect(onUnauthenticated).toHaveBeenCalledTimes(status===401?1:0);expect(screen.queryByRole('button',{name:'Повторить'})).not.toBeInTheDocument();expect(screen.queryByRole('link',{name:'Проверка'})).not.toBeInTheDocument();});
  it.each(['network','server'])('retries %s GET explicitly',async kind=>{const fetch=vi.fn<typeof globalThis.fetch>();if(kind==='network')fetch.mockRejectedValueOnce(new TypeError('offline'));else fetch.mockResolvedValueOnce(response({},500));fetch.mockResolvedValueOnce(response(fixture()));mount(fixture(),fetch);await screen.findByRole('alert');expect(fetch).toHaveBeenCalledOnce();await userEvent.click(screen.getByRole('button',{name:'Повторить'}));await loaded();expect(fetch).toHaveBeenCalledTimes(2);});
  it('cancels on unmount',async()=>{const {fetch,unmount}=mount(fixture(),vi.fn<typeof globalThis.fetch>().mockReturnValue(new Promise(()=>{})));await waitFor(()=>expect(fetch).toHaveBeenCalledOnce());const signal=fetch.mock.calls[0]![1]!.signal!;unmount();expect(signal.aborted).toBe(true);});
  it('ignores stale response after filters change',async()=>{let resolve!:(value:Response)=>void;const fetch=vi.fn<typeof globalThis.fetch>().mockReturnValueOnce(new Promise(r=>{resolve=r;})).mockResolvedValueOnce(response(empty()));mount(fixture(),fetch);await waitFor(()=>expect(fetch).toHaveBeenCalledOnce());await userEvent.type(screen.getByLabelText('Поиск'),'absent');await apply();await screen.findByText('По выбранным фильтрам работ нет');expect(fetch.mock.calls[0]![1]!.signal!.aborted).toBe(true);await act(async()=>resolve(response(fixture())));expect(screen.queryByText('Делимость суммы')).not.toBeInTheDocument();});
  it('refreshes on return from placeholder with filters preserved',async()=>{const fetch=vi.fn<typeof globalThis.fetch>().mockResolvedValueOnce(response(fixture())).mockResolvedValueOnce(response(empty()));mount(fixture(),fetch,'/teacher/reviews?courseId=math');await loaded();await userEvent.click(screen.getByRole('link',{name:/Проверить: Ученик/}));await userEvent.click(screen.getByRole('link',{name:'Вернуться к очереди →'}));await screen.findByText('По выбранным фильтрам работ нет');expect(fetch).toHaveBeenCalledTimes(2);expect(screen.getByLabelText('Курс · ID')).toHaveValue('math');});
  it('preserves backend order even for equal or reversed timestamps',async()=>{const data=fixture();data.items.push({...data.items[0]!,problemWorkId:'50000000-0000-0000-0000-000000000002',problemTitle:'Вторая',submittedAt:'2026-01-01T00:00:00Z'});mount(data);await loaded();expect(screen.getAllByRole('link',{name:/Проверить:/}).map(n=>n.getAttribute('href'))).toEqual(data.items.map(i=>`/teacher/reviews/${i.problemWorkId}`));});
  it('offers recovery for out-of-range page without claiming queue empty',async()=>{mount({...fixture(),items:[]},undefined,'/teacher/reviews?page=99');await loaded();expect(screen.getByText('На этой странице работ нет')).toBeInTheDocument();await userEvent.click(screen.getByRole('button',{name:'На первую страницу'}));await screen.findByText('Найдено работ: 12 · Страница 1');});
  it('keeps context after main, one h1 and accessible controls',async()=>{mount();await loaded();expect(screen.getAllByRole('heading',{level:1})).toHaveLength(1);expect(screen.getByRole('main').compareDocumentPosition(screen.getByRole('complementary'))&Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();expect(screen.getByRole('navigation',{name:'Страницы очереди'})).toBeInTheDocument();screen.getByLabelText('Поиск').focus();await userEvent.keyboard('{Tab}');expect(screen.getByLabelText('Курс · ID')).toHaveFocus();});
  it('resets unapplied form input too',async()=>{mount();await loaded();await userEvent.type(screen.getByLabelText('Поиск'),'unsaved');await userEvent.click(screen.getByRole('button',{name:'Сбросить фильтры'}));expect(screen.getByLabelText('Поиск')).toHaveValue('');});
  it('changes page size server-side and resets page',async()=>{const {fetch}=mount(fixture(),undefined,'/teacher/reviews?page=2');await loaded();const input=screen.getByLabelText('Работ на странице');await userEvent.clear(input);await userEvent.type(input,'10');await userEvent.tab();await loaded();expect(requests(fetch).at(-1)?.get('pageSize')).toBe('10');expect(requests(fetch).at(-1)?.get('page')).toBe('1');});
  it('rejects malformed or mismatched response',async()=>{const fetch=vi.fn<typeof globalThis.fetch>().mockResolvedValue(response({...fixture(),page:2}));mount(fixture(),fetch);await screen.findByRole('alert');expect(screen.queryByText('Делимость суммы')).not.toBeInTheDocument();});
});
describe('C4 boundary and URL normalization',()=>{
  it.each(['page=-1&pageSize=201','page=NaN&pageSize=0','page=999999999999999999&pageSize=50','page=2.5&pageSize=Infinity'])('sanitizes invalid paging %s',query=>{expect(normalizeQueueQuery(new URLSearchParams(query)).toString()).toBe('page=1&pageSize=50');});
  it('drops unsupported parameters and invalid UUID/kind, limits search',()=>{const q=normalizeQueueQuery(new URLSearchParams({studentId:'fake',attemptKind:'Accepted',sort:'priority',search:'x'.repeat(201)}));expect(q.has('studentId')).toBe(false);expect(q.has('attemptKind')).toBe(false);expect(q.has('sort')).toBe(false);expect(q.get('search')).toHaveLength(200);});
  it.each(['studentDisplayName','waitingDurationSeconds','summary'])('rejects missing C4 field %s',key=>{const data=fixture();if(key==='summary')delete (data as Partial<ReviewQueue>).summary;else delete (data.items[0] as unknown as Record<string,unknown>)[key];expect(()=>decodeReviewQueue(data)).toThrow();});
  it('formats elapsed time safely',()=>{expect(waitingTime(-60)).toBe('меньше минуты');expect(waitingTime(120)).toBe('2 мин');expect(waitingTime(90000)).toBe('1 д 1 ч');});
});
