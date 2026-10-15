import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { ApiClient } from '../api/client';
import { decodeCourseSchedule, decodeCourseScheduleEvents } from '../api/contracts/courseSchedule';
import { CourseScheduleCalendar } from '../features/content/CourseScheduleCalendar';
import { CourseScheduleAside } from '../features/content/CourseScheduleSummary';

const courseId='course-a';
const workload={courseId,totalAcademicHours:72,scheduledAcademicHours:48,independentAcademicHours:24,
  academicHourMinutes:45,calendarPlannedAcademicHours:47,conductedAcademicHours:null,
  remainingPlannedAcademicHours:1,calendarPlanMismatch:true,version:1};
const rule={ruleId:'00000000-0000-0000-0000-000000000001',courseId,isoDayOfWeek:1,
  startTime:'17:00',endTime:'19:00',academicHours:2,effectiveFrom:'2026-01-01',effectiveTo:'2027-01-01',
  timeZoneId:'Asia/Omsk',deliveryMode:'InPerson',location:'Кабинет 305',onlineUrl:null,
  eventKind:'Lesson',title:'Графы',description:'Задачи Эйлера',lessonId:'graphs',isActive:true,version:1};
const event={occurrenceId:`${rule.ruleId.replaceAll('-','')}:2026-10-12`,ruleId:rule.ruleId,courseId,
  originalDate:'2026-10-12',startsAt:'2026-10-12T17:00:00+06:00',endsAt:'2026-10-12T19:00:00+06:00',
  timeZoneId:'Asia/Omsk',academicHours:2,deliveryMode:'InPerson',location:'Кабинет 305',onlineUrl:null,
  eventKind:'Lesson',title:'Графы',description:'Задачи Эйлера',lessonId:'graphs',status:'Scheduled',
  originalStartsAt:null,exceptionVersion:null};
function client():ApiClient{return {request:vi.fn(async(path:string,decode:(value:unknown)=>unknown)=>
  decode(path.includes('/events')?{items:[event]}:{workload,rules:[rule]}))} as unknown as ApiClient;}

describe('course schedule',()=>{
  it('validates workload identity, sums and event course ownership',()=>{
    expect(decodeCourseSchedule({workload,rules:[rule]},courseId).workload?.totalAcademicHours).toBe(72);
    expect(()=>decodeCourseSchedule({workload:{...workload,independentAcademicHours:23},rules:[]},courseId)).toThrow();
    expect(()=>decodeCourseScheduleEvents({items:[{...event,courseId:'foreign'}]},courseId)).toThrow();
  });
  it('renders week, month and list controls and opens protected event details',async()=>{
    render(<CourseScheduleCalendar courseId={courseId} client={client()}/>);
    const weekEvent=(await screen.findAllByText('Графы'))[0]!.closest('button')!;
    expect(weekEvent).toBeVisible();
    await userEvent.click(weekEvent);
    expect(screen.getByRole('dialog')).toHaveTextContent('2 ак. ч.');
    await userEvent.click(screen.getByRole('button',{name:'Закрыть'}));
    await userEvent.click(screen.getByRole('button',{name:'Месяц'}));
    expect(await screen.findByRole('button',{name:/17:00 Графы/})).toBeVisible();
    await userEvent.click(screen.getByRole('button',{name:'Список'}));
    expect(await screen.findByRole('button',{name:/Графы/})).toHaveTextContent('2 ак. ч.');
  });
  it('shows authoritative workload and recurring rules without inventing attendance',async()=>{
    render(<CourseScheduleAside courseId={courseId} description="Олимпиадная математика" client={client()}/>);
    expect(await screen.findByText('72 академических часа')).toBeVisible();
    expect(screen.getByText('Занятия 48 ч')).toBeVisible();
    expect(screen.getByText(/Понедельник/)).toBeVisible();
    expect(screen.queryByText(/посетил|посещение/i)).toBeNull();
  });
  it('shows the ordinary weekly slot once instead of repeating dated occurrences',async()=>{
    const saturday={...rule,isoDayOfWeek:6,startTime:'10:00',endTime:'11:30',effectiveFrom:'2026-10-17',effectiveTo:'2026-10-17'};
    const datedRules=[saturday,
      {...saturday,ruleId:'00000000-0000-0000-0000-000000000002',effectiveFrom:'2026-10-24',effectiveTo:'2026-10-24',title:'Остатки'},
      {...saturday,ruleId:'00000000-0000-0000-0000-000000000003',effectiveFrom:'2026-10-31',effectiveTo:'2026-10-31',title:'Комбинаторика'},
      {...rule,ruleId:'00000000-0000-0000-0000-000000000004',isoDayOfWeek:7,effectiveFrom:'2026-10-18',effectiveTo:'2026-10-18',title:'Разовая консультация'}];
    const api={request:vi.fn(async(_path:string,decode:(value:unknown)=>unknown)=>decode({workload,rules:datedRules}))} as unknown as ApiClient;
    render(<CourseScheduleAside courseId={courseId} client={api}/>);
    expect(await screen.findByText('Суббота')).toBeVisible();
    expect(screen.getAllByText('Суббота')).toHaveLength(1);
    expect(screen.getByText('10:00–11:30 · 2 ак. ч.')).toBeVisible();
    expect(screen.queryByText('Воскресенье')).toBeNull();
  });
});
