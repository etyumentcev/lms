import { createRequire } from 'node:module';
import { mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const { chromium } = require(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
const evidence = fileURLToPath(new URL('../../../codex/evidence/COURSE-SCHEDULE/', import.meta.url));
await mkdir(evidence, { recursive: true });

const organizationId = '00000000-0000-4000-8000-000000000002';
const actorId = '00000000-0000-4000-8000-000000000001';
const courseId = 'olympiad';
const contentVersionId = '10000000-0000-4000-8000-000000000001';
const ruleMonday = '20000000-0000-4000-8000-000000000001';
const ruleSaturday = '20000000-0000-4000-8000-000000000002';
const description = 'Курс для 6 класса, посвящённый решению олимпиадных задач.';
const workload = { courseId, totalAcademicHours: 72, scheduledAcademicHours: 48,
  independentAcademicHours: 24, academicHourMinutes: 45, calendarPlannedAcademicHours: 48,
  conductedAcademicHours: null, remainingPlannedAcademicHours: 0, calendarPlanMismatch: false, version: 1 };
const rules = [
  { ruleId: ruleMonday, courseId, isoDayOfWeek: 1, startTime: '17:00', endTime: '19:00',
    academicHours: 2, effectiveFrom: '2026-09-01', effectiveTo: '2027-05-31', timeZoneId: 'Asia/Omsk',
    deliveryMode: 'InPerson', location: 'Кабинет 305', onlineUrl: null, eventKind: 'Lesson',
    title: 'Занятие. Инварианты', description: 'Теория и задачи на инварианты.', lessonId: null,
    isActive: true, version: 1 },
  { ruleId: ruleSaturday, courseId, isoDayOfWeek: 6, startTime: '10:00', endTime: '13:00',
    academicHours: 3, effectiveFrom: '2026-09-01', effectiveTo: '2027-05-31', timeZoneId: 'Asia/Omsk',
    deliveryMode: 'InPerson', location: 'Кабинет 305', onlineUrl: null, eventKind: 'Lesson',
    title: 'Комбинаторика', description: 'Разбор олимпиадных методов.', lessonId: null,
    isActive: true, version: 1 }
];
const datePlus = (value, days) => { const date = new Date(`${value}T12:00:00Z`); date.setUTCDate(date.getUTCDate() + days); return date.toISOString().slice(0, 10); };
function event(date, index, { hour = 17, duration = 2, title = 'Занятие. Инварианты', kind = 'Lesson',
  location = 'Кабинет 305', mode = 'InPerson', hours = 2 } = {}) {
  const pad = value => String(value).padStart(2, '0');
  return { occurrenceId: `${index}:${date}`, ruleId: index % 2 ? ruleSaturday : ruleMonday, courseId,
    originalDate: date, startsAt: `${date}T${pad(hour)}:00:00+06:00`,
    endsAt: `${date}T${pad(hour + duration)}:00:00+06:00`, timeZoneId: 'Asia/Omsk',
    academicHours: hours, deliveryMode: mode, location, onlineUrl: mode === 'Online' ? 'https://meet.example/synthetic' : null,
    eventKind: kind, title, description: 'Synthetic-only демонстрационное занятие.', lessonId: null,
    status: 'Scheduled', originalStartsAt: null, exceptionVersion: null };
}
function periodEvents(url) {
  const request = new URL(url); const from = request.searchParams.get('from');
  return [event(datePlus(from, 1), 0), event(datePlus(from, 3), 1,
    { hour: 17, duration: 1, title: 'Консультация', kind: 'Consultation', location: null, mode: 'Online', hours: 1 }),
    event(datePlus(from, 5), 2, { hour: 10, duration: 3, title: 'Комбинаторика', hours: 3 })];
}
const lessons = ['Введение в комбинаторику', 'Принцип Дирихле'].map((title, index) => ({
  id: `lesson-${index + 1}`, title, order: index + 1, bodyMarkdown: 'Теория занятия',
  problems: [{ id: `problem-${index + 1}`, title: `Задача ${index + 1}`, order: 1, bodyMarkdown: 'Условие', hints: [] }]
}));
const assignments = lessons.map((lesson, index) => ({ assignmentId: `assignment-${index + 1}`,
  contentVersionId, versionNumber: 1, lessonId: lesson.id, lessonTitle: lesson.title,
  problemId: lesson.problems[0].id, problemTitle: lesson.problems[0].title, problemOrder: 1,
  status: index ? 'Submitted' : 'Accepted', attemptCount: 1 }));
function token(role) {
  return 'x.' + Buffer.from(JSON.stringify({ actor: actorId, exp: Math.floor(Date.now() / 1000) + 3600,
    scope: { Membership: { Role: role, OrganizationId: organizationId, Permissions: role ? 0 : 1 } } })).toString('base64url') + '.x';
}
const fulfill = (route, data, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(data) });
const browser = await chromium.launch({ headless: true,
  executablePath: process.env.LMS_BROWSER_EXECUTABLE || '/opt/google/chrome/chrome', args: ['--no-sandbox'] });
const unknown = [];
try {
  const student = await browser.newPage({ viewport: { width: 1512, height: 980 }, deviceScaleFactor: 1 });
  await student.route('**/api/v1/**', route => {
    const url = new URL(route.request().url()); const path = url.pathname;
    if (path === '/api/v1/auth/csrf') return fulfill(route, { token: 'synthetic-csrf' });
    if (path === '/api/v1/auth/refresh') return fulfill(route, { accessToken: token('Student'),
      hasEducationalScope: true, hasOrganizationAdminScope: false, organizationId,
      organizationName: 'Sapientum', displayName: 'Иванов И.', selectionRequired: false,
      organizations: [{ organizationId, name: 'Sapientum' }] });
    if (path === '/api/v1/my/courses') return fulfill(route, { items: [{ courseId,
      title: 'Олимпиадная математика, 6 класс', description }] });
    if (path === `/api/v1/my/courses/${courseId}/assignments`) return fulfill(route, { items: assignments });
    if (path === `/api/v1/my/courses/${courseId}/versions/${contentVersionId}/content`) return fulfill(route, {
      artifactSchema: 'student-content/v1', contractRevision: '1.1', studentProjectionHash: 'a'.repeat(64),
      course: { id: courseId, title: 'Олимпиадная математика, 6 класс', description,
        bodyMarkdown: 'Описание курса', lessons }, assets: [] });
    if (path === '/api/v1/student/progress-history') return fulfill(route, { items: [], page: 1,
      pageSize: 5, total: 0, summary: { studentCount: 1, assignedCount: 2, notStarted: 0,
        submitted: 1, needsRevision: 0, accepted: 1 } });
    if (path === `/api/v1/courses/${courseId}/schedule`) return fulfill(route, { workload, rules });
    if (path === `/api/v1/courses/${courseId}/schedule/upcoming`) return fulfill(route, { items: [
      event(datePlus(new Date().toISOString().slice(0, 10), 2), 4, { hour: 10, duration: 3, title: 'Комбинаторика', hours: 3 })] });
    if (path === `/api/v1/courses/${courseId}/schedule/events`) return fulfill(route, { items: periodEvents(url) });
    unknown.push(path); return fulfill(route, {}, 404);
  });
  const base = process.env.LMS_TEST_BASE_URL || 'http://127.0.0.1:5178';
  await student.goto(`${base}/study/courses/${courseId}?tab=schedule`);
  await student.locator('.schedule-week').waitFor();
  await student.screenshot({ path: `${evidence}/week.png`, fullPage: true });
  await student.getByRole('button', { name: 'Месяц' }).click();
  await student.locator('.schedule-month').waitFor();
  await student.screenshot({ path: `${evidence}/month.png`, fullPage: true });

  const admin = await browser.newPage({ viewport: { width: 1512, height: 1000 }, deviceScaleFactor: 1 });
  await admin.route('**/api/v1/**', route => {
    const url = new URL(route.request().url()); const path = url.pathname;
    if (path === '/api/v1/auth/csrf') return fulfill(route, { token: 'synthetic-csrf' });
    if (path === '/api/v1/auth/refresh') return fulfill(route, { accessToken: token(null),
      hasEducationalScope: false, hasOrganizationAdminScope: true, organizationId,
      organizationName: 'Sapientum', displayName: 'Администратор', selectionRequired: false,
      organizations: [{ organizationId, name: 'Sapientum' }] });
    if (path === '/api/v1/admin/organization') return fulfill(route, { organizationId,
      organizationName: 'Sapientum', state: 'Enabled', actorId, administratorState: 'Active' });
    if (path === '/api/v1/admin/audit') return fulfill(route, { items: [] });
    if (path === '/api/v1/admin/courses') return fulfill(route, { items: [{ courseId,
      title: 'Олимпиадная математика, 6 класс', description, state: 'Published', version: 1,
      groupCount: 1, studentCount: 12, teacherCount: 1 }] });
    if (path === `/api/v1/admin/courses/${courseId}`) return fulfill(route, { course: { courseId,
      title: 'Олимпиадная математика, 6 класс', description, state: 'Published', version: 1,
      groupCount: 1, studentCount: 12, teacherCount: 1 }, groups: [{ groupId: 'group-a', name: 'Группа А',
      description: '6 класс', version: 1, studentCount: 12, teacherCount: 1 }], students: [], teachers: [], editors: [] });
    if (path === `/api/v1/admin/courses/${courseId}/schedule`) return fulfill(route, { workload, rules });
    unknown.push(path); return fulfill(route, {}, 404);
  });
  await admin.goto(`${base}/courses`);
  await admin.locator('aside').getByRole('button', { name: /Курсы и группы/ }).click();
  await admin.getByRole('button', { name: /Олимпиадная математика, 6 класс/ }).click();
  await admin.getByRole('button', { name: 'Расписание и нагрузка' }).click();
  await admin.getByRole('dialog', { name: 'Расписание и учебная нагрузка' }).waitFor();
  await admin.screenshot({ path: `${evidence}/editor.png`, fullPage: true });
  if (unknown.length) throw Error(`Unexpected API requests: ${[...new Set(unknown)].join(', ')}`);
  process.stdout.write(JSON.stringify({ week: 'PASS', month: 'PASS', editor: 'PASS', unknown }) + '\n');
} finally {
  await browser.close();
}
