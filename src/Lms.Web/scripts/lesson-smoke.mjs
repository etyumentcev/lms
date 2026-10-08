// F: controlled responsive/state coverage plus optional real Compose vertical slice.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.LMS_SMOKE_URL || 'http://127.0.0.1:8080';
const output = process.env.LMS_SMOKE_OUTPUT || '/tmp/lms-f-browser';
await mkdir(output, { recursive: true });
const fixture = JSON.parse(await readFile(new URL('../src/test/fixtures/lesson-student.json', import.meta.url)));
const course = JSON.parse(await readFile(new URL('../src/test/fixtures/course-student.json', import.meta.url)));
const browser = await chromium.launch({ headless: true, channel: 'chrome' });
const results = [], errors = [];
async function geometry(page, width) {
  const data = await page.evaluate(() => {
    const main = document.querySelector('main').getBoundingClientRect(), aside = document.querySelector('aside').getBoundingClientRect();
    return { overflow: document.documentElement.scrollWidth > innerWidth, count: document.querySelectorAll('aside').length,
      main: { right: main.right, bottom: main.bottom }, aside: { x: aside.x, y: aside.y } };
  });
  assert.equal(data.overflow, false, `overflow at ${width}`); assert.equal(data.count, 1);
  assert(width >= 1200 ? data.aside.x >= data.main.right - 1 : data.aside.y >= data.main.bottom - 1);
  return data;
}
try {
  const context = await browser.newContext();
  const page = await context.newPage(); page.on('pageerror', error => errors.push(error.message));
  let data = structuredClone(fixture), status = 200, offline = false;
  await page.route('**/api/v1/courses/*/lessons/*/workspace', async route => {
    if (offline) return route.abort('failed');
    await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(data) });
  });
  await page.route('**/api/v1/courses/olympiad-7/workspace', route => route.fulfill({ json: course }));
  const row = fixture.problems[0];
  await page.route('**/api/v1/problem-assignments/*/workspace', route => route.fulfill({ json: {
    assignment: { id: row.problemAssignmentId, isActive: true }, course: fixture.course, lesson: fixture.lesson,
    problem: row.problem, availability: row.availability, work: row.work, permissions: row.permissions,
    hints: [], usefulMaterials: [], history: { submissions: [], reviews: [], messages: [] },
  } }));
  const path = `${base}/courses/olympiad-7/lessons/lesson-2`;
  for (const width of [320, 375, 768, 1024, 1440]) {
    await page.setViewportSize({ width, height: 960 });
    for (const state of ['normal', 'long-many', 'zero', 'materials', 'restricted']) {
      data = structuredClone(fixture);
      if (state === 'long-many') {
        data.lesson.title = 'ДлинноеНазваниеЗанятия'.repeat(12);
        data.problems = Array.from({ length: 30 }, (_, i) => ({ ...structuredClone(fixture.problems[i % 4]), problemAssignmentId: `assignment-${i}`,
          problem: { ...fixture.problems[i % 4].problem, title: 'ДлинноеНазваниеЗадачи'.repeat(15) } }));
      }
      if (state === 'zero') { data.problems = []; data.progress = { status: 'NotStarted', acceptedProblems: 0, totalAssignedProblems: 0, percent: 0 }; }
      if (state === 'materials') data.materials = [{ id: 'notes', title: 'Конспект'.repeat(15), kind: 'Document', description: 'Материал занятия', url: '/notes.pdf' }];
      if (state === 'restricted') { data.availability.isAvailable = false; data.availability.reason = 'NotPublished'; }
      await page.goto(path); await page.getByRole('heading', { level: 1, name: data.lesson.title }).waitFor();
      results.push({ width, state, ...await geometry(page, width) });
      if (state === 'normal') {
        for (const label of ['Не начата', 'На проверке', 'Нужна доработка', 'Принята']) assert(await page.getByLabel(`Статус: ${label}`, { exact: true }).isVisible());
        assert.equal(await page.getByRole('progressbar').getAttribute('aria-valuenow'), '25');
        await page.screenshot({ path: `${output}/lesson-${width}.png`, fullPage: true });
      }
      if (state === 'restricted') { assert.equal(await page.getByRole('link', { name: /Открыть задачу/ }).count(), 0); assert.equal(await page.getByRole('progressbar').count(), 0); }
    }
  }
  data = structuredClone(fixture);
  await page.goto(path); await page.getByRole('heading', { level: 1, name: data.lesson.title }).waitFor();
  await page.setViewportSize({ width: 1024, height: 900 }); await page.getByRole('button', { name: 'Свернуть контекст' }).click();
  await page.setViewportSize({ width: 375, height: 900 }); assert(await page.getByRole('progressbar').isVisible());
  const link = page.getByRole('link', { name: 'Открыть задачу: Задача 1', exact: true });
  await link.focus(); await page.keyboard.press('Enter'); await page.getByRole('heading', { level: 1, name: 'Задача 1' }).waitFor();
  assert(page.url().endsWith(`/problem-assignments/${fixture.problems[0].problemAssignmentId}`));
  await page.waitForFunction(() => document.activeElement?.id === 'main-content');
  for (const code of [401,403,404,500]) {
    status = code; await page.goto(path); await page.getByRole('alert').waitFor();
    results.push({ errorStatus: code, shell: await page.getByRole('main').count() });
  }
  status = 200; offline = true; await page.goto(path); await page.getByRole('alert').waitFor(); offline = false;
  await page.getByRole('button', { name: 'Повторить' }).click(); await page.getByRole('heading', { level: 1, name: fixture.lesson.title }).waitFor();
  await context.close();
  if (process.env.LMS_COMPOSE_LIVE === '1') {
    const live = await browser.newContext(); const page = await live.newPage(); page.on('pageerror', error => errors.push(error.message));
    const token = execFileSync('docker', ['compose','exec','-T','api','dotnet','Lms.Api.dll','--dev-token'], { encoding: 'utf8' }).trim();
    await page.goto(`${base}/local-login.html`); await page.getByLabel('Вставьте token').fill(token); await page.getByRole('button').click();
    await page.getByRole('heading', { level: 1, name: 'Математический кружок' }).waitFor();
    for (const width of [1440,1024,375]) {
      await page.setViewportSize({ width, height: 960 }); await page.goto(`${base}/courses/local-demo`);
      await page.getByRole('heading', { level: 1, name: 'Математический кружок' }).waitFor();
      await page.reload(); await page.getByRole('heading', { level: 1, name: 'Математический кружок' }).waitFor();
      await page.getByRole('link', { name: 'Открыть: Делимость и остатки' }).click();
      await page.getByRole('heading', { level: 1, name: 'Делимость и остатки' }).waitFor();
      await page.waitForFunction(() => document.activeElement?.id === 'main-content');
      results.push({ live: true, width, ...await geometry(page, width) });
      await page.screenshot({ path: `${output}/live-lesson-${width}.png`, fullPage: true });
      await page.reload(); await page.getByRole('heading', { level: 1, name: 'Делимость и остатки' }).waitFor();
      const response = await page.evaluate(async () => (await fetch('/api/v1/courses/local-demo/lessons/divisibility/workspace', { headers: { Authorization: `Bearer ${await window.lmsAuth.getAccessToken()}` } })).json());
      assert.equal(response.problems.length, 4); assert(response.problems.every(p => p.work.problemWorkId === null));
      const problem = page.getByRole('link', { name: 'Открыть задачу: Делимость суммы', exact: true });
      await problem.focus(); await page.keyboard.press('Enter'); await page.getByRole('heading', { level: 1, name: 'Делимость суммы' }).waitFor();
      assert(page.url().endsWith('/problem-assignments/40000000-0000-0000-0000-000000000001'));
      await page.waitForFunction(() => document.activeElement?.id === 'main-content');
      await page.reload(); await page.getByRole('heading', { level: 1, name: 'Делимость суммы' }).waitFor();
    }
    const teacher = execFileSync('docker', ['compose','exec','-T','api','dotnet','Lms.Api.dll','--dev-token','Teacher'], { encoding: 'utf8' }).trim();
    await page.goto(`${base}/local-login.html`); await page.getByLabel('Вставьте token').fill(teacher); await page.getByRole('button').click();
    await page.getByText(/Режим преподавателя/).waitFor(); await page.getByRole('link', { name: 'Открыть: Делимость и остатки' }).click();
    await page.getByRole('heading', { level: 1, name: 'Делимость и остатки' }).waitFor(); assert.equal(await page.getByText('Ученик', { exact: true }).count(), 0);
    const anon = await live.request.get(`${base}/api/v1/courses/local-demo/workspace`); assert.equal(anon.status(),401);
    assert.match(anon.headers()['content-type'], /problem\+json/);
    const missing = await live.request.get(`${base}/api/nonexistent`); assert.equal(missing.status(),404); assert.match(missing.headers()['content-type'], /problem\+json/);
    const denied = await live.request.get(`${base}/api/v1/problem-assignments/40000000-0000-0000-0000-000000000001/workspace`, { headers: { Authorization: `Bearer ${teacher}` } });
    assert.equal(denied.status(), 403); assert.match(denied.headers()['content-type'], /problem\+json/);
    const absent = await live.request.get(`${base}/api/v1/courses/local-demo/lessons/absent/workspace`, { headers: { Authorization: `Bearer ${teacher}` } });
    assert.equal(absent.status(), 404); assert.match(absent.headers()['content-type'], /problem\+json/);
    await live.close();
  }
  assert.deepEqual(errors, []); await writeFile(`${output}/results.json`, JSON.stringify({ results, errors }, null, 2));
  console.log(`PASS ${results.length} browser checks; no page errors`);
} finally { await browser.close(); }
