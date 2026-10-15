import { createRequire } from 'node:module';
import { mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const { chromium } = require(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
const evidence = fileURLToPath(new URL('../../../codex/evidence/STUDENT-COURSE-DASHBOARD/', import.meta.url));
await mkdir(evidence, { recursive: true });
const browser = await chromium.launch({ headless: true, executablePath: process.env.LMS_BROWSER_EXECUTABLE || '/opt/google/chrome/chrome', args: ['--no-sandbox'] });
try {
  const page = await browser.newPage({ viewport: { width: 1512, height: 900 }, deviceScaleFactor: 1 });
  const organizationId = '00000000-0000-0000-0000-000000000002';
  const version = '10000000-0000-0000-0000-000000000001';
  const token = 'x.' + Buffer.from(JSON.stringify({ actor: '00000000-0000-0000-0000-000000000001',
    exp: Math.floor(Date.now() / 1000) + 3600, scope: { Membership: { Role: 'Student', OrganizationId: organizationId, Permissions: 1 } } })).toString('base64url') + '.x';
  const names = ['Введение в комбинаторику', 'Принцип Дирихле', 'Размещения и сочетания',
    'Бином Ньютона', 'Цепочка неравенств', 'Графы: основы'];
  const lessons = names.map((title, index) => ({ id: `lesson-${index + 1}`, title, order: index + 1,
    bodyMarkdown: 'Теория занятия', problems: [{ id: `problem-${index + 1}`, title: `Задача ${index + 1}`,
      order: 1, bodyMarkdown: 'Условие', hints: [] }] }));
  const assignments = lessons.map((lesson, index) => ({ assignmentId: `assignment-${index + 1}`,
    contentVersionId: version, versionNumber: 1, lessonId: lesson.id, lessonTitle: lesson.title,
    problemId: lesson.problems[0].id, problemTitle: lesson.problems[0].title, problemOrder: 1,
    status: index < 3 ? 'Accepted' : index === 3 ? 'NeedsRevision' : 'Submitted', attemptCount: index < 3 ? 2 : 1 }));
  const description = 'Курс по олимпиадной математике для школьников. Разбираем нестандартные задачи и готовимся к соревнованиям.';
  const responses = {
    '/api/v1/auth/csrf': { token: 'synthetic-csrf' },
    '/api/v1/auth/refresh': { accessToken: token, hasEducationalScope: true, hasOrganizationAdminScope: false,
      organizationId, organizationName: 'Sapientum', displayName: 'Тестовый ученик', selectionRequired: false,
      organizations: [{ organizationId, name: 'Sapientum' }] },
    '/api/v1/my/courses': { items: [{ courseId: 'olympiad', title: 'Олимпиадная математика', description }] },
    '/api/v1/my/courses/olympiad/assignments': { items: assignments },
    '/api/v1/student/progress-history': {
      items: assignments.slice(1, 6).reverse().map((assignment, index) => ({
        assignmentId: assignment.assignmentId, problemWorkId: `work-${index}`,
        courseVersionId: version, courseId: 'olympiad', courseTitle: 'Олимпиадная математика',
        lessonId: assignment.lessonId, lessonTitle: assignment.lessonTitle,
        problemId: assignment.problemId, problemTitle: assignment.problemTitle,
        status: assignment.status, attemptCount: assignment.attemptCount, isActive: true,
        lastActivityAt: `2026-07-${20 - index}T10:00:00Z`, lastSubmittedAt: `2026-07-${20 - index}T10:00:00Z` })),
      page: 1, pageSize: 5, total: 5, summary: { studentCount: 1, assignedCount: 6,
        notStarted: 0, submitted: 2, needsRevision: 1, accepted: 3 } },
    [`/api/v1/my/courses/olympiad/versions/${version}/content`]: {
      artifactSchema: 'student-content/v1', contractRevision: '1.1', studentProjectionHash: 'a'.repeat(64),
      course: { id: 'olympiad', title: 'Олимпиадная математика', description,
        bodyMarkdown: 'Описание курса', lessons }, assets: []
    }
  };
  const unknown = [];
  await page.route('**/api/v1/**', route => {
    const path = new URL(route.request().url()).pathname;
    const data = responses[path];
    if (!data) { unknown.push(path); return route.fulfill({ status: 404, contentType: 'application/json', body: '{}' }); }
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(data) });
  });
  await page.goto(process.env.LMS_TEST_BASE_URL || 'http://127.0.0.1:5178/study/courses/olympiad');
  await page.locator('.student-course-submission-list li').first().waitFor();
  await page.getByRole('heading', { level: 1, name: 'Олимпиадная математика' }).waitFor();
  await page.screenshot({ path: `${evidence}/summary.png`, fullPage: true });
  await page.getByRole('link', { name: 'Занятия' }).click();
  await page.getByRole('heading', { level: 2, name: 'Занятия' }).waitFor();
  await page.screenshot({ path: `${evidence}/lessons.png`, fullPage: true });
  await page.goBack();
  if (await page.getByRole('link', { name: 'Сводка' }).getAttribute('aria-current') !== 'page') throw Error('Back failed');
  await page.goForward();
  if (await page.getByRole('link', { name: 'Занятия' }).getAttribute('aria-current') !== 'page') throw Error('Forward failed');
  await page.setViewportSize({ width: 1024, height: 768 });
  const tablet = await page.evaluate(() => {
    const panel = document.querySelector('.student-course-right-panel')?.getBoundingClientRect();
    const body = document.querySelector('.context-body')?.getBoundingClientRect();
    return { panel: panel?.width, body: body?.width, scroll: document.documentElement.scrollWidth, viewport: innerWidth };
  });
  if (!tablet.panel || !tablet.body || tablet.panel < tablet.body * .95 || tablet.scroll > tablet.viewport)
    throw Error('Tablet context layout failed');
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole('link', { name: 'Сводка' }).click();
  const mobile = await page.evaluate(() => ({ scroll: document.documentElement.scrollWidth, viewport: innerWidth }));
  if (mobile.scroll > mobile.viewport) throw Error('Horizontal overflow');
  await page.screenshot({ path: `${evidence}/summary-mobile.png`, fullPage: true });
  if (unknown.length) throw Error(`Unexpected API requests: ${unknown.join(', ')}`);
  process.stdout.write(JSON.stringify({ tabs: 'PASS', backForward: 'PASS', tablet, mobile, unknown }) + '\n');
} finally {
  await browser.close();
}
