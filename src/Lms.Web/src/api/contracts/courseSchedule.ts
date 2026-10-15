type Json = Record<string, unknown>;

export type DeliveryMode = 'InPerson' | 'Online' | 'Hybrid';
export type EventKind = 'Lesson' | 'Consultation' | 'ExtraLesson';
export type OccurrenceStatus = 'Scheduled' | 'Moved' | 'Cancelled';

export type CourseWorkload = {
  courseId: string; totalAcademicHours: number; scheduledAcademicHours: number;
  independentAcademicHours: number; academicHourMinutes: number;
  calendarPlannedAcademicHours: number; conductedAcademicHours: number | null;
  remainingPlannedAcademicHours: number; calendarPlanMismatch: boolean; version: number;
};
export type CourseScheduleRule = {
  ruleId: string; courseId: string; isoDayOfWeek: number; startTime: string; endTime: string;
  academicHours: number; effectiveFrom: string; effectiveTo: string; timeZoneId: string;
  deliveryMode: DeliveryMode; location: string | null; onlineUrl: string | null;
  eventKind: EventKind; title: string; description: string | null; lessonId: string | null;
  isActive: boolean; version: number;
};
export type CourseScheduleEvent = {
  occurrenceId: string; ruleId: string; courseId: string; originalDate: string;
  startsAt: string; endsAt: string; timeZoneId: string; academicHours: number;
  deliveryMode: DeliveryMode; location: string | null; onlineUrl: string | null;
  eventKind: EventKind; title: string; description: string | null; lessonId: string | null;
  status: OccurrenceStatus; originalStartsAt: string | null; exceptionVersion: number | null;
};
export type CourseSchedule = { workload: CourseWorkload | null; rules: CourseScheduleRule[] };

function object(value: unknown): Json {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Invalid response');
  return value as Json;
}
function text(value: unknown, nullable = false) {
  if (nullable && value === null) return null;
  if (typeof value !== 'string') throw new Error('Invalid response');
  return value;
}
function integer(value: unknown, min = 0) {
  if (!Number.isSafeInteger(value) || (value as number) < min) throw new Error('Invalid response');
  return value as number;
}
function oneOf<T extends string>(value: unknown, values: readonly T[]): T {
  if (typeof value !== 'string' || !values.includes(value as T)) throw new Error('Invalid response');
  return value as T;
}
function isoDate(value: unknown) {
  const result = text(value) as string;
  if (!/^\d{4}-\d{2}-\d{2}$/.test(result)) throw new Error('Invalid response');
  return result;
}
function instant(value: unknown, nullable = false) {
  const result = text(value, nullable);
  if (result === null) return null;
  if (!Number.isFinite(Date.parse(result))) throw new Error('Invalid response');
  return result;
}

function workload(value: unknown, courseId: string): CourseWorkload {
  const x = object(value);
  const result: CourseWorkload = { courseId: text(x.courseId) as string,
    totalAcademicHours: integer(x.totalAcademicHours), scheduledAcademicHours: integer(x.scheduledAcademicHours),
    independentAcademicHours: integer(x.independentAcademicHours), academicHourMinutes: integer(x.academicHourMinutes, 1),
    calendarPlannedAcademicHours: integer(x.calendarPlannedAcademicHours),
    conductedAcademicHours: x.conductedAcademicHours === null ? null : integer(x.conductedAcademicHours),
    remainingPlannedAcademicHours: integer(x.remainingPlannedAcademicHours),
    calendarPlanMismatch: x.calendarPlanMismatch === true || x.calendarPlanMismatch === false
      ? x.calendarPlanMismatch : (() => { throw new Error('Invalid response'); })(),
    version: integer(x.version, 1) };
  if (result.courseId !== courseId || result.totalAcademicHours !==
    result.scheduledAcademicHours + result.independentAcademicHours) throw new Error('Invalid response');
  return result;
}
function rule(value: unknown, courseId: string): CourseScheduleRule {
  const x = object(value);
  const result: CourseScheduleRule = { ruleId: text(x.ruleId) as string,
    courseId: text(x.courseId) as string, isoDayOfWeek: integer(x.isoDayOfWeek, 1),
    startTime: text(x.startTime) as string, endTime: text(x.endTime) as string,
    academicHours: integer(x.academicHours, 1), effectiveFrom: isoDate(x.effectiveFrom),
    effectiveTo: isoDate(x.effectiveTo), timeZoneId: text(x.timeZoneId) as string,
    deliveryMode: oneOf(x.deliveryMode, ['InPerson','Online','Hybrid'] as const),
    location: text(x.location, true), onlineUrl: text(x.onlineUrl, true),
    eventKind: oneOf(x.eventKind, ['Lesson','Consultation','ExtraLesson'] as const),
    title: text(x.title) as string, description: text(x.description, true), lessonId: text(x.lessonId, true),
    isActive: x.isActive === true || x.isActive === false ? x.isActive : (() => { throw new Error('Invalid response'); })(),
    version: integer(x.version, 1) };
  if (result.courseId !== courseId || result.isoDayOfWeek > 7) throw new Error('Invalid response');
  return result;
}
function event(value: unknown, courseId: string): CourseScheduleEvent {
  const x = object(value);
  const result: CourseScheduleEvent = { occurrenceId: text(x.occurrenceId) as string,
    ruleId: text(x.ruleId) as string, courseId: text(x.courseId) as string,
    originalDate: isoDate(x.originalDate), startsAt: instant(x.startsAt)!, endsAt: instant(x.endsAt)!,
    timeZoneId: text(x.timeZoneId) as string, academicHours: integer(x.academicHours, 1),
    deliveryMode: oneOf(x.deliveryMode, ['InPerson','Online','Hybrid'] as const),
    location: text(x.location, true), onlineUrl: text(x.onlineUrl, true),
    eventKind: oneOf(x.eventKind, ['Lesson','Consultation','ExtraLesson'] as const),
    title: text(x.title) as string, description: text(x.description, true), lessonId: text(x.lessonId, true),
    status: oneOf(x.status, ['Scheduled','Moved','Cancelled'] as const),
    originalStartsAt: instant(x.originalStartsAt, true),
    exceptionVersion: x.exceptionVersion === null ? null : integer(x.exceptionVersion, 1) };
  if (result.courseId !== courseId || Date.parse(result.endsAt) <= Date.parse(result.startsAt))
    throw new Error('Invalid response');
  return result;
}

export function decodeCourseSchedule(value: unknown, courseId: string): CourseSchedule {
  const x = object(value);
  if (!Array.isArray(x.rules)) throw new Error('Invalid response');
  return { workload: x.workload === null ? null : workload(x.workload, courseId),
    rules: x.rules.map(item => rule(item, courseId)) };
}
export function decodeCourseScheduleEvents(value: unknown, courseId: string) {
  const x = object(value); if (!Array.isArray(x.items)) throw new Error('Invalid response');
  const items = x.items.map(item => event(item, courseId));
  if (items.some((item, index) => index > 0 && Date.parse(item.startsAt) < Date.parse(items[index - 1]!.startsAt)))
    throw new Error('Invalid response');
  return items;
}
