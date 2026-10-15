import { useEffect, useMemo, useState } from 'react';
import type { CSSProperties } from 'react';
import type { ApiClient } from '../../api/client';
import { ApiError } from '../../api/client';
import { decodeCourseScheduleEvents, type CourseScheduleEvent } from '../../api/contracts/courseSchedule';
import './courseScheduleCalendar.css';

type View = 'week' | 'month' | 'list';
const ruDays = ['Пн','Вт','Ср','Чт','Пт','Сб','Вс'];
const eventLabels = { Lesson: 'Занятие', Consultation: 'Консультация', ExtraLesson: 'Дополнительное занятие' } as const;
const modeLabels = { InPerson: 'Очно', Online: 'Онлайн', Hybrid: 'Гибридно' } as const;

function date(value: string) { return new Date(`${value}T12:00:00`); }
function iso(value: Date) {
  return `${value.getFullYear()}-${String(value.getMonth() + 1).padStart(2,'0')}-${String(value.getDate()).padStart(2,'0')}`;
}
function add(value: Date, days: number) { const result = new Date(value); result.setDate(result.getDate() + days); return result; }
function monday(value: Date) { const result = new Date(value); result.setDate(result.getDate() - ((result.getDay() + 6) % 7)); return result; }
function monthStart(value: Date) { return new Date(value.getFullYear(), value.getMonth(), 1, 12); }
function monthEnd(value: Date) { return new Date(value.getFullYear(), value.getMonth() + 1, 0, 12); }
function title(view: View, cursor: Date) {
  if (view === 'month') return new Intl.DateTimeFormat('ru', { month: 'long', year: 'numeric' }).format(cursor);
  const from = view === 'week' ? monday(cursor) : cursor; const to = add(from, view === 'week' ? 6 : 30);
  const sameMonth = from.getMonth() === to.getMonth();
  return sameMonth ? `${from.getDate()}–${to.getDate()} ${new Intl.DateTimeFormat('ru', {month:'long',year:'numeric'}).format(to)}`
    : `${new Intl.DateTimeFormat('ru', {day:'numeric',month:'short'}).format(from)} — ${new Intl.DateTimeFormat('ru', {day:'numeric',month:'short',year:'numeric'}).format(to)}`;
}
function period(view: View, cursor: Date) {
  const from = view === 'week' ? monday(cursor) : view === 'month' ? monthStart(cursor) : cursor;
  const to = view === 'week' ? add(from, 6) : view === 'month' ? monthEnd(cursor) : add(from, 30);
  return { from, to };
}
function zonedParts(value: string, timeZone: string) {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone, year:'numeric', month:'2-digit', day:'2-digit', hour:'2-digit', minute:'2-digit', hourCycle:'h23' })
    .formatToParts(new Date(value)).reduce<Record<string,string>>((result, part) => ({...result,[part.type]:part.value}),{});
  return { day: `${parts.year}-${parts.month}-${parts.day}`, hour: Number(parts.hour), minute: Number(parts.minute), time: `${parts.hour}:${parts.minute}` };
}
function eventDay(item: CourseScheduleEvent) { return zonedParts(item.startsAt, item.timeZoneId).day; }
function eventMinutes(item: CourseScheduleEvent, end = false) {
  const parts = zonedParts(end ? item.endsAt : item.startsAt, item.timeZoneId); return parts.hour * 60 + parts.minute;
}
function dayLabel(value: string) { return new Intl.DateTimeFormat('ru', {weekday:'short',day:'numeric',month:'short'}).format(date(value)); }

export function CourseScheduleCalendar({ courseId, client }: { courseId: string; client: ApiClient }) {
  const [view,setView] = useState<View>('week'); const [cursor,setCursor] = useState(() => new Date());
  const [events,setEvents] = useState<CourseScheduleEvent[]>([]); const [loading,setLoading] = useState(true);
  const [error,setError] = useState<ApiError|null>(null); const [revision,setRevision] = useState(0);
  const [selected,setSelected] = useState<CourseScheduleEvent|null>(null);
  const selectedPeriod = useMemo(() => period(view,cursor),[view,cursor]);
  useEffect(() => { const controller = new AbortController(); setLoading(true); setError(null);
    const path = `/courses/${encodeURIComponent(courseId)}/schedule/events?from=${iso(selectedPeriod.from)}&to=${iso(selectedPeriod.to)}`;
    void client.request(path, value => decodeCourseScheduleEvents(value, courseId), {signal:controller.signal})
      .then(items => { if(!controller.signal.aborted)setEvents(items); })
      .catch(reason => { if(!controller.signal.aborted)setError(reason instanceof ApiError?reason:new ApiError(0,'InvalidResponse')); })
      .finally(() => { if(!controller.signal.aborted)setLoading(false); }); return()=>controller.abort();
  },[client,courseId,selectedPeriod.from.getTime(),selectedPeriod.to.getTime(),revision]);
  function move(direction:number){const next=new Date(cursor);view==='month'?next.setMonth(next.getMonth()+direction):next.setDate(next.getDate()+direction*(view==='week'?7:31));setCursor(next);}
  return <section className="course-schedule" aria-labelledby="course-schedule-title">
    <header className="course-schedule-toolbar"><div className="course-schedule-navigation">
      <button type="button" aria-label="Предыдущий период" onClick={()=>move(-1)}>‹</button>
      <button type="button" onClick={()=>setCursor(new Date())}>Сегодня</button>
      <button type="button" aria-label="Следующий период" onClick={()=>move(1)}>›</button></div>
      <h2 id="course-schedule-title">{title(view,cursor)}</h2>
      <div className="course-schedule-modes" aria-label="Вид календаря">{(['week','month','list'] as const).map(item=><button
        type="button" key={item} aria-pressed={view===item} onClick={()=>setView(item)}>{item==='week'?'Неделя':item==='month'?'Месяц':'Список'}</button>)}</div></header>
    {loading&&<div className="schedule-state" role="status">Загружаем расписание…</div>}
    {error&&<div className="schedule-state" role="alert"><strong>Не удалось загрузить расписание.</strong>
      {![401,403].includes(error.status)&&<button type="button" onClick={()=>setRevision(x=>x+1)}>Повторить</button>}</div>}
    {!loading&&!error&&!events.length&&<div className="schedule-state"><strong>Занятий в этом периоде нет</strong><span>Выберите другой период или вернитесь позже.</span></div>}
    {!loading&&!error&&events.length>0&&(view==='week'?<Week events={events} from={selectedPeriod.from} onSelect={setSelected}/>
      :view==='month'?<Month events={events} cursor={cursor} onSelect={setSelected}/>:<List events={events} onSelect={setSelected}/>)}
    {selected&&<EventDialog item={selected} onClose={()=>setSelected(null)}/>} </section>;
}

function Week({events,from,onSelect}:{events:CourseScheduleEvent[];from:Date;onSelect:(x:CourseScheduleEvent)=>void}){
  const days=Array.from({length:7},(_,i)=>add(from,i));const today=iso(new Date());
  const startHour=Math.max(0,Math.min(8,...events.map(x=>Math.floor(eventMinutes(x)/60))));
  const endHour=Math.min(24,Math.max(21,...events.map(x=>Math.ceil(eventMinutes(x,true)/60))));
  const hours=Array.from({length:endHour-startHour+1},(_,i)=>startHour+i);
  return <><div className="schedule-week" style={{'--hour-count':endHour-startHour} as CSSProperties}>
    <div className="schedule-week-corner"/>{days.map((day,index)=><div key={iso(day)} className={`schedule-week-day${iso(day)===today?' is-today':''}`}><strong>{ruDays[index]}</strong><span>{day.getDate()}</span></div>)}
    <div className="schedule-week-times">{hours.map(hour=><span key={hour} style={{top:`${(hour-startHour)*4}rem`}}>{String(hour).padStart(2,'0')}:00</span>)}</div>
    {days.map(day=><div className="schedule-week-column" key={iso(day)}>{hours.slice(0,-1).map(hour=><span className="schedule-hour-line" key={hour} style={{top:`${(hour-startHour)*4}rem`}}/>)}
      {events.filter(item=>eventDay(item)===iso(day)).map(item=>{const top=(eventMinutes(item)-startHour*60)/60*4;const height=Math.max(2,(eventMinutes(item,true)-eventMinutes(item))/60*4);
        return <button type="button" key={item.occurrenceId} className={`schedule-event kind-${item.eventKind.toLowerCase()} status-${item.status.toLowerCase()}`}
          style={{top:`${top}rem`,height:`${height}rem`}} onClick={()=>onSelect(item)}><time>{zonedParts(item.startsAt,item.timeZoneId).time}–{zonedParts(item.endsAt,item.timeZoneId).time}</time><strong>{item.title}</strong><span>{item.location||modeLabels[item.deliveryMode]}</span></button>;})}</div>)}</div>
    <div className="schedule-mobile-week"><List events={events} onSelect={onSelect}/></div></>;
}
function Month({events,cursor,onSelect}:{events:CourseScheduleEvent[];cursor:Date;onSelect:(x:CourseScheduleEvent)=>void}){
  const start=monthStart(cursor);const first=monday(start);const cells=Array.from({length:42},(_,i)=>add(first,i));const today=iso(new Date());
  return <div className="schedule-month"><div className="schedule-month-head">{ruDays.map(x=><strong key={x}>{x}</strong>)}</div><div className="schedule-month-grid">{cells.map(day=>{const key=iso(day);const items=events.filter(x=>eventDay(x)===key);return <section key={key} className={`${day.getMonth()!==cursor.getMonth()?'is-outside ':''}${key===today?'is-today':''}`}><span>{day.getDate()}</span>{items.slice(0,3).map(item=><button type="button" key={item.occurrenceId} className={`kind-${item.eventKind.toLowerCase()} status-${item.status.toLowerCase()}`} onClick={()=>onSelect(item)}>{zonedParts(item.startsAt,item.timeZoneId).time} {item.title}</button>)}{items.length>3&&<small>Ещё {items.length-3}</small>}</section>;})}</div></div>;
}
function List({events,onSelect}:{events:CourseScheduleEvent[];onSelect:(x:CourseScheduleEvent)=>void}){
  const groups=new Map<string,CourseScheduleEvent[]>();for(const item of events){const key=eventDay(item);groups.set(key,[...(groups.get(key)??[]),item]);}
  return <div className="schedule-list">{[...groups].map(([day,items])=><section key={day}><h3>{dayLabel(day)}</h3>{items.map(item=><button type="button" key={item.occurrenceId} onClick={()=>onSelect(item)} className={`kind-${item.eventKind.toLowerCase()} status-${item.status.toLowerCase()}`}><time>{zonedParts(item.startsAt,item.timeZoneId).time}–{zonedParts(item.endsAt,item.timeZoneId).time}</time><span><strong>{item.title}</strong><small>{eventLabels[item.eventKind]} · {item.academicHours} ак. ч. · {item.location||modeLabels[item.deliveryMode]}</small></span><span aria-hidden="true">›</span></button>)}</section>)}</div>;
}
function EventDialog({item,onClose}:{item:CourseScheduleEvent;onClose:()=>void}){
  return <div className="schedule-dialog-backdrop" onMouseDown={event=>{if(event.target===event.currentTarget)onClose();}}><section role="dialog" aria-modal="true" aria-labelledby="schedule-dialog-title" className="schedule-dialog"><button type="button" aria-label="Закрыть" onClick={onClose}>×</button><span className={`schedule-dialog-kind kind-${item.eventKind.toLowerCase()}`}>{eventLabels[item.eventKind]}</span><h2 id="schedule-dialog-title">{item.title}</h2><dl><div><dt>Дата</dt><dd>{dayLabel(eventDay(item))}</dd></div><div><dt>Время</dt><dd>{zonedParts(item.startsAt,item.timeZoneId).time}–{zonedParts(item.endsAt,item.timeZoneId).time} · {item.timeZoneId}</dd></div><div><dt>Нагрузка</dt><dd>{item.academicHours} ак. ч.</dd></div><div><dt>Формат</dt><dd>{modeLabels[item.deliveryMode]}{item.location?` · ${item.location}`:''}</dd></div>{item.status!=='Scheduled'&&<div><dt>Статус</dt><dd>{item.status==='Moved'?'Перенесено':'Отменено'}</dd></div>}</dl>{item.description&&<p>{item.description}</p>}{item.onlineUrl&&<a href={item.onlineUrl} target="_blank" rel="noreferrer">Открыть ссылку на занятие</a>}</section></div>;
}
