// Course-only browser smoke. No workflow mutations and no visual-regression framework.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
const fixture = JSON.parse(await readFile(new URL('../src/test/fixtures/course-student.json', import.meta.url), 'utf8'));
const output = resolve(process.env.LMS_SMOKE_OUTPUT || '.qa/course');
await mkdir(output, { recursive: true });
const base = process.env.LMS_SMOKE_URL || 'http://127.0.0.1:4174';
const browser = await chromium.launch({ headless: true, channel: 'chrome' });
const results = [];
const errors = [];
async function geometry(page, width) {
  const value = await page.evaluate(() => {
    const rect = selector => {
      const { x, y, width, height } = document.querySelector(selector).getBoundingClientRect();
      return { x, y, width, height };
    };
    return { main: rect('main'), context: rect('aside'), overflow: document.documentElement.scrollWidth > innerWidth,
      count: document.querySelectorAll('aside').length };
  });
  assert.equal(value.overflow, false, `overflow at ${width}`);
  assert.equal(value.count, 1);
  assert(await page.getByRole('progressbar', { name: 'Завершение курса' }).isVisible());
  if (width >= 1200) assert(value.context.x >= value.main.x + value.main.width - 1);
  else assert(value.context.y >= value.main.y + value.main.height - 1);
  return value;
}
try {
  const context = await browser.newContext();
  await context.addInitScript(() => { window.lmsAuth = { getAccessToken: () => 'fixture-session',
    onUnauthenticated: () => { document.documentElement.dataset.sessionExpired = 'true'; } }; });
  const page = await context.newPage();
  await page.route('**/local-auth.js', route => route.fulfill({ contentType: 'application/javascript', body: '' }));
  page.on('pageerror', error => errors.push(error.message));
  let data = structuredClone(fixture), status = 200, requests = 0, network = false;
  let hold = false, release;
  await page.route('**/api/v1/courses/*/workspace', async route => {
    requests++;
    assert.equal(route.request().headers().authorization, 'Bearer fixture-session');
    if (hold) await new Promise(resolve => { release = resolve; });
    if (network) { await route.abort('failed'); return; }
    await route.fulfill({ status, contentType: status === 200 ? 'application/json' : 'application/problem+json', body: JSON.stringify(data) });
  });
  for (const [width, height] of [[1440, 1000], [1024, 900], [768, 1024], [390, 844], [320, 720]]) {
    await page.setViewportSize({ width, height });
    data = structuredClone(fixture); status = 200;
    await page.goto(`${base}/courses/${fixture.course.id}`);
    await page.getByRole('heading', { level: 1, name: fixture.course.title }).waitFor();
    assert.equal(await page.getByRole('progressbar').getAttribute('aria-valuenow'), '50');
    const locked = page.getByRole('heading', { name: 'Принцип Дирихле' }).locator('..').locator('..');
    assert.equal(await locked.getByRole('link').count(), 0);
    results.push({ width, state: 'student', ...await geometry(page, width) });
    await page.screenshot({ path: `${output}/course-${width}.png`, fullPage: true });
    // Long titles and many lessons test wrapping without a separate mobile component tree.
    data.course.title = `${fixture.course.title} — ${'ОченьДлинноеНазвание'.repeat(7)}`;
    data.lessons = Array.from({ length: 30 }, (_, i) => ({ ...structuredClone(fixture.lessons[i % 3]),
      lesson: { ...fixture.lessons[i % 3].lesson, id: `long-${i}`, order: i + 1, title: 'ОченьДлинноеНазваниеУрока'.repeat(8) } }));
    data.progress = { completedLessons: 10, availableLessons: 20, totalLessons: 30, percent: 50 };
    await page.reload(); await page.getByRole('heading', { level: 1, name: data.course.title }).waitFor();
    await geometry(page, width);
    assert.equal(await page.getByRole('region', { name: 'Уроки', exact: true }).getByRole('listitem').count(), 30);
    results.push({ width, state: 'long titles / 30 lessons', overflow: false });
    data.lessons = []; data.progress = { completedLessons: 0, availableLessons: 0, totalLessons: 0, percent: 0 };
    await page.reload(); await page.getByRole('heading', { name: 'В курсе пока нет уроков' }).waitFor();
    await geometry(page, width); results.push({ width, state: 'empty' });
  }
  data = structuredClone(fixture); data.enrollment = null;
  data.teacherContext = { teacherId: '3d1358b6-e4bc-4c9a-9bda-92f07a75d407', canReviewCourse: true, groupIds: [null] };
  await page.setViewportSize({ width: 1024, height: 900 }); await page.reload();
  await page.getByText('По доступным вам работам курса и групп.').waitFor();
  await page.locator('.context-body').evaluate(node => { node.dataset.identity = 'same'; });
  await page.getByRole('button', { name: 'Свернуть контекст' }).click();
  assert.equal(await page.locator('.context-body').isVisible(), false);
  await page.setViewportSize({ width: 390, height: 844 });
  assert.equal(await page.locator('.context-body').getAttribute('data-identity'), 'same');
  await geometry(page, 390);
  assert(await page.getByText('По доступным вам работам курса и групп.').isVisible());
  results.push({ state: 'Teacher null enrollment / tablet collapse / mobile context preserved' });
  data = structuredClone(fixture); hold = true;
  await page.reload(); await page.getByRole('status', { name: 'Загрузка уроков' }).waitFor();
  assert(await page.getByRole('complementary').isVisible());
  assert(await page.getByRole('status', { name: 'Загрузка контекста курса' }).isVisible());
  // Wait until the intercepted request has actually reached the held response boundary.
  await page.waitForFunction(() => document.querySelector('[aria-busy="true"]') !== null);
  for (let i = 0; !release && i < 100; i++) await new Promise(resolve => setTimeout(resolve, 10));
  assert(release); hold = false; release();
  await page.getByRole('heading', { level: 1, name: fixture.course.title }).waitFor();
  results.push({ state: 'loading with shell retained' });
  for (const [http, code, title] of [[401, 'Unauthenticated', 'Войдите, чтобы открыть курс'],
    [403, 'Forbidden', 'Нет доступа к курсу'], [404, 'NotFound', 'Курс не найден'], [503, 'ContentUnavailable', 'Не удалось загрузить курс']]) {
    status = http; data = { type: `urn:lms:error:${code}`, title: 'Do not parse human text', status, instance: '/api/v1/courses/x/workspace', code, traceId: 'browser-smoke' };
    await page.reload(); await page.getByRole('heading', { name: title }).waitFor();
    assert(await page.getByRole('main').isVisible()); assert(await page.getByRole('complementary').isVisible());
    if (http === 401) assert.equal(await page.locator('html').getAttribute('data-session-expired'), 'true');
    results.push({ state: `HTTP ${http}` });
  }
  status = 200; data = structuredClone(fixture);
  const beforeRetry = requests; await page.getByRole('button', { name: 'Повторить' }).click();
  await page.getByRole('heading', { level: 1, name: fixture.course.title }).waitFor();
  assert.equal(requests, beforeRetry + 1);
  network = true; await page.reload(); await page.getByRole('heading', { name: 'Не удалось загрузить курс' }).waitFor();
  network = false; await page.getByRole('button', { name: 'Повторить' }).click();
  await page.getByRole('heading', { level: 1, name: fixture.course.title }).waitFor();
  results.push({ state: 'GET server/network manual retry' });
  const lessonFixture = JSON.parse(await readFile(new URL('../src/test/fixtures/lesson-student.json', import.meta.url), 'utf8'));
  await page.route('**/api/v1/courses/*/lessons/*/workspace', route => route.fulfill({ json: lessonFixture }));
  const lessonLink = page.getByRole('link', { name: 'Открыть: Делимость и остатки' });
  await lessonLink.focus(); await page.keyboard.press('Enter');
  await page.getByRole('heading', { level: 1, name: lessonFixture.lesson.title, exact: true }).waitFor();
  assert.equal(await page.getByRole('main').evaluate(node => node === document.activeElement), true);
  assert(page.url().endsWith('/courses/olympiad-7/lessons/lesson-2'));
  results.push({ state: 'keyboard Course → Lesson, focus main' });
  await context.close();

  // Optional true backend smoke: temporary test-host session file, never production fixtures.
  if (process.env.LMS_LIVE_SESSION) {
    const session = JSON.parse(await readFile(process.env.LMS_LIVE_SESSION, 'utf8'));
    for (const [role, token, expected] of [['Student', session.studentToken, 200], ['Teacher', session.teacherToken, 200],
      ['Anonymous', null, 401], ['ForeignStudent', session.foreignToken, 403]]) {
      const live = await browser.newContext({ viewport: { width: role === 'Teacher' ? 390 : 1440, height: 1000 } });
      await live.addInitScript(token => { window.lmsAuth = { getAccessToken: () => token, onUnauthenticated: () => {} }; }, token);
      const page = await live.newPage(); page.on('pageerror', error => errors.push(error.message));
      const read = page.waitForResponse(response => response.url().includes(`/api/v1/courses/${session.courseId}/workspace`));
      await page.goto(`${base}/courses/${session.courseId}`);
      const actual = await read; assert.equal(actual.status(), expected);
      const body = await actual.json();
      if (expected === 200) {
        await page.getByRole('heading', { level: 1, name: body.course.title }).waitFor();
        assert.equal(await page.getByRole('progressbar').getAttribute('aria-valuenow'), String(body.progress.percent));
        assert.equal(body.enrollment === null, role === 'Teacher');
        await geometry(page, role === 'Teacher' ? 390 : 1440);
        assert.equal(await page.getByText('Protected summary').count(), 0);
        await page.screenshot({ path: `${output}/live-${role}.png`, fullPage: true });
      } else await page.getByRole('alert').waitFor();
      results.push({ liveBackend: true, role, status: actual.status(), backendCode: body.code ?? null });
      await live.close();
    }
  }
  assert.deepEqual(errors, []);
  await writeFile(`${output}/results.json`, JSON.stringify({ passed: true, results, errors }, null, 2));
  console.log(JSON.stringify({ passed: true, results, errors }, null, 2));
} finally { await browser.close(); }
