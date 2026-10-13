// J2: real UI decisions and protected photos. Only a dedicated disposable Compose project.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdir, writeFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.LMS_SMOKE_URL || 'http://127.0.0.1:8085';
const output = process.env.LMS_SMOKE_OUTPUT || '/tmp/lms-j2-browser';
await mkdir(output, { recursive: true });
const compose = (...args) => execFileSync('docker', ['compose', ...args], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
assert.equal(JSON.parse(compose('config', '--format', 'json')).name, 'lms-j2-smoke');
const student = compose('exec', '-T', 'api', 'dotnet', 'Lms.Api.dll', '--dev-token');
const teacher = compose('exec', '-T', 'api', 'dotnet', 'Lms.Api.dll', '--dev-token', 'Teacher');
const assignment = n => `40000000-0000-0000-0000-${String(n).padStart(12, '0')}`;
const route = n => `/problem-assignments/${assignment(n)}`;
async function request(path, { role = student, method = 'GET', body } = {}) {
  return fetch(`${base}/api/v1${path}`, { method, headers: { ...(role ? { Authorization: `Bearer ${role}` } : {}), ...(body ? { 'Content-Type': 'application/json' } : {}) }, body: body ? JSON.stringify(body) : undefined });
}
async function api(path, options) { const r = await request(path, options); assert(r.ok, `${path}: ${r.status}`); return r.json(); }
const workspace = () => api(`${route(1)}/workspace`);
const queue = () => api('/teacher/review-queue', { role: teacher });
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
async function original(id, role = teacher) { const r = await request(`/attachments/${id}/content`, { role }); assert.equal(r.status, 200); return hash(Buffer.from(await r.arrayBuffer())); }
const browser = await chromium.launch({ headless: true, channel: 'chrome' });
const results = [], errors = [];
let page;
try {
  page = await browser.newPage({ viewport: { width: 1440, height: 1100 } });
  page.on('pageerror', error => errors.push(error.message));
  async function login(token) { await page.goto(`${base}/local-login.html`); await page.getByLabel('Вставьте token').fill(token); await page.getByRole('button').click(); await page.waitForURL('**/courses/local-demo'); }
  async function image(label) { return Buffer.from(await page.evaluate(label => {
    const canvas = document.createElement('canvas'); canvas.width = 1190; canvas.height = 1684;
    const ctx = canvas.getContext('2d'); ctx.fillStyle = 'white'; ctx.fillRect(0, 0, canvas.width, canvas.height);
    ctx.fillStyle = '#123a25'; ctx.font = '32px serif'; ctx.fillText(label, 70, 80);
    for (let i = 1; i < 23; i++) ctx.fillText(`${i}. n = 3k + r;  n² − r² = 3k(3k + 2r)`, 70, 80 + i * 62);
    return canvas.toDataURL('image/png').split(',')[1];
  }, label), 'base64'); }
  await login(student);
  await page.getByRole('link', { name: /Открыть.*Делимость и остатки/ }).click();
  await page.getByRole('link', { name: 'Открыть задачу: Делимость суммы' }).click();
  assert.equal((await workspace()).work.problemWorkId, null, 'Run on a clean dedicated project');
  const photos = [await image('Attempt 1 / page 1'), await image('Attempt 1 / page 2'), await image('Attempt 2 / corrected page')];
  await page.getByLabel('Добавить фото решения').setInputFiles(photos.slice(0, 2).map((buffer, i) => ({ name: `page-${i + 1}.png`, mimeType: 'image/png', buffer })));
  await page.getByAltText('Страница 2 — фото решения').waitFor();
  await page.locator('textarea').fill('Пояснение первой попытки.');
  await page.getByRole('button', { name: 'Отправить решение' }).click();
  await page.getByText('Решение отправлено и ожидает проверки преподавателем.').waitFor();
  let data = await workspace(); const first = structuredClone(data.history.submissions[0]); const workId = data.work.problemWorkId;
  assert.equal((await request(`/problem-works/${workId}/reviews/accept`, { role: teacher, method: 'POST', body: { submissionId: first.id, expectedVersion: 1, feedbackMarkdown: 'x'.repeat(4 * 1024 * 1024) } })).status, 413);
  const workRoute = `/teacher/reviews/${workId}`;
  await login(teacher); await page.goto(`${base}/teacher/reviews?courseId=local-demo&pageSize=10`);
  await page.getByRole('link', { name: /Проверить:/ }).click(); await page.getByRole('heading', { name: 'Попытка №1', exact: true }).waitFor();
  assert.equal(new URL(page.url()).pathname, workRoute);
  assert(await page.getByRole('region', { name: 'Условие задачи' }).isVisible());
  assert.match(await page.getByRole('article', { name: 'Просмотр попытки №1' }).innerText(), /Пояснение первой попытки/);
  await page.getByAltText('Страница 1 — фото решения').waitFor();
  await page.getByRole('button', { name: 'Увеличить фото' }).click(); assert.equal(await page.getByLabel('Масштаб').innerText(), '150%');
  const originalLink = page.getByRole('link', { name: 'Открыть страницу 1 в полном размере (новая вкладка)' });
  await originalLink.focus(); const popupPromise = page.waitForEvent('popup'); await page.keyboard.press('Enter'); const popup = await popupPromise; assert.match(popup.url(), /^blob:/); await popup.close();
  await page.getByRole('button', { name: 'Следующая', exact: true }).click(); await page.getByAltText('Страница 2 — фото решения').waitFor();
  assert.equal(await page.getByLabel('Масштаб').innerText(), '100%');
  await page.getByRole('region', { name: 'Просмотр фото' }).focus(); await page.keyboard.press('Alt+ArrowLeft'); await page.getByText('Страница 1 из 2').waitFor();
  for (let i = 0; i < 2; i++) assert.equal(await original(first.attachmentIds[i]), hash(photos[i]));
  results.push('Student login → Course → Lesson → two A4 uploads → Submit #1 → Teacher login → filtered Queue → ProblemWork route; immutable statement, ordered protected originals, zoom and keyboard navigation');
  // Canvas coordinates must match the original's visible rectangle at every zoom.
  async function drawStroke() {
    const canvas = page.locator('canvas[aria-label^="Слой"]');
    await canvas.scrollIntoViewIfNeeded(); const box = await canvas.boundingBox();
    await page.mouse.move(box.x + box.width * .2, box.y + box.height * .3);
    await page.mouse.down(); await page.mouse.move(box.x + box.width * .4, box.y + box.height * .4, { steps: 12 }); await page.mouse.up();
    await page.getByText(/Графические пометки: 1 штр/).waitFor();
  }
  await page.getByRole('button', { name: 'Вписать страницу' }).click();
  await drawStroke();
  await page.getByRole('button', { name: 'Отмена', exact: true }).click(); await page.getByText(/Графические пометки: 0 штр/).waitFor();
  await page.getByRole('button', { name: 'Повтор', exact: true }).click(); await page.getByText(/Графические пометки: 1 штр/).waitFor();
  await page.getByRole('button', { name: 'Следующая', exact: true }).click(); await page.getByText(/Графические пометки: 0 штр/).waitFor();
  await page.getByRole('button', { name: 'Предыдущая', exact: true }).click(); await page.getByText(/Графические пометки: 1 штр/).waitFor();
  await page.getByRole('button', { name: 'На весь экран', exact: true }).click(); await page.getByRole('dialog').waitFor();
  await page.screenshot({ path: `${output}/fullscreen.png` });
  await page.getByRole('button', { name: 'Выйти из полного экрана', exact: true }).click();
  const feedback = 'Объясните второй переход.\nСохраните все вычисления.';
  await page.getByRole('button', { name: 'Вернуть на доработку' }).click(); await page.getByText('Укажите, что ученику нужно исправить.').waitFor();
  assert(await page.locator('textarea').evaluate(node => document.activeElement === node));
  await page.locator('textarea').fill(feedback);
  // Responsive geometry exercises real persisted data, including 300% photo scroll.
  for (const width of [320, 375, 768, 1024, 1440]) {
    await page.setViewportSize({ width, height: 1100 });
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, `overflow ${width}`);
    assert.equal(await page.getByRole('heading', { level: 1 }).count(), 1);
    assert.equal(await page.getByRole('main').count(), 1); assert.equal(await page.getByRole('complementary').count(), 1);
    assert(await page.getByRole('main').evaluate(node => !!(node.compareDocumentPosition(document.querySelector('aside.context-panel')) & Node.DOCUMENT_POSITION_FOLLOWING)));
    assert(await page.getByRole('img').evaluate(node => { const r = node.getBoundingClientRect(); const c = node.parentElement.querySelector('canvas').getBoundingClientRect(); return Math.abs(r.width / r.height - node.naturalWidth / node.naturalHeight) < .001 && Math.abs(c.width - r.width) < 1 && Math.abs(c.height - r.height) < 1; }), `image/canvas geometry ${width}`);
    await page.getByRole('button', { name: 'Увеличить фото' }).click();
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, `zoom overflow ${width}`);
    await page.getByRole('button', { name: 'Вписать страницу' }).click();
    await page.screenshot({ path: `${output}/review-${width}.png`, fullPage: true });
  }
  await page.locator('textarea').scrollIntoViewIfNeeded();
  const statement = await page.getByRole('region', { name: 'Условие задачи' }).boundingBox(); assert(statement.y >= 0 && statement.y < 200, 'desktop statement stays visible while reviewing');
  let reviewPosts = 0; page.on('request', r => { if (r.method() === 'POST' && r.url().includes('/reviews/')) reviewPosts++; });
  // Two same-tick clicks exercise the immediate ref lock as well as disabled state.
  await page.getByRole('button', { name: 'Вернуть на доработку' }).evaluate(button => { button.click(); button.click(); });
  await page.getByText('Проверка сохранена. Состояние работы обновлено.').waitFor();
  const persistedAnnotations = (await workspace()).history.reviews[0].annotations;
  assert.equal(persistedAnnotations.length, 1); assert.equal(persistedAnnotations[0].attachmentId, first.attachmentIds[0]);
  assert.equal(persistedAnnotations[0].strokes.length, 1);
  assert(Math.abs(persistedAnnotations[0].strokes[0].points[0].x - .2) < .01);
  assert.equal(reviewPosts, 1); assert.equal(await page.getByRole('button', { name: 'Принять решение' }).count(), 0);
  await page.getByRole('link', { name: 'К очереди проверок' }).click(); await page.getByText('По выбранным фильтрам работ нет').waitFor();
  assert.equal(new URL(page.url()).searchParams.get('courseId'), 'local-demo'); assert.equal(new URL(page.url()).searchParams.get('pageSize'), '10'); assert.equal((await queue()).total, 0);
  results.push('Five widths without horizontal overflow/crop; sticky desktop statement; blank feedback validation/focus; double-click sends one RequestRevision; confirmed result and fresh filtered queue excludes work');
  await login(student); await page.goto(`${base}${route(1)}`); await page.getByText(/Объясните второй переход/).waitFor();
  await page.getByLabel('Добавить фото решения').setInputFiles({ name: 'corrected.png', mimeType: 'image/png', buffer: photos[2] });
  await page.locator('.solution-form').getByAltText('Страница 1 — фото решения').waitFor(); await page.locator('textarea').fill('Вторая попытка: переход обоснован.');
  await page.getByRole('button', { name: 'Отправить решение' }).click(); await page.getByText('Последняя попытка №2').waitFor();
  data = await workspace(); const second = structuredClone(data.history.submissions[0]); assert.deepEqual(data.history.submissions[1], first);
  await login(teacher); await page.goto(`${base}/teacher/reviews`); await page.getByRole('link', { name: /Проверить:/ }).click(); await page.getByRole('heading', { name: 'Попытка №2', exact: true }).waitFor();
  await page.getByRole('img').waitFor(); assert.equal(await original(second.attachmentIds[0]), hash(photos[2])); assert.equal(await page.getByRole('img').count(), 1);
  assert.match(await page.getByRole('region', { name: 'Последнее замечание' }).innerText(), /Объясните второй переход/);
  await page.locator('.review-history summary').click(); await page.getByRole('button', { name: 'Посмотреть попытку №1' }).click();
  await page.getByRole('heading', { name: 'Попытка №1', exact: true }).waitFor(); assert.equal(await page.getByRole('button', { name: 'Принять решение' }).count(), 0);
  await page.getByRole('button', { name: 'К текущей попытке' }).click();
  // A real message increments version without a review. Stale command must stop and refresh.
  await api(`/problem-works/${workId}/messages`, { method: 'POST', body: { bodyMarkdown: 'Уточнение после отправки.', attachmentIds: [] } });
  await drawStroke();
  await page.locator('textarea').fill('Черновик после конфликта'); await page.getByRole('button', { name: 'Принять решение' }).click();
  await page.getByText('Работа уже изменилась или была проверена другим преподавателем. Обновите работу.').waitFor();
  assert(await page.getByRole('button', { name: 'Принять решение' }).isDisabled()); assert.equal(reviewPosts, 2);
  page.once('dialog', d => d.accept());
  await page.getByRole('button', { name: 'Обновить работу' }).click(); await page.getByRole('button', { name: 'Принять решение' }).waitFor();
  assert.equal(await page.locator('textarea').inputValue(), 'Черновик после конфликта'); await page.locator('textarea').fill('');
  await page.getByRole('button', { name: 'Принять решение' }).click(); await page.getByText('Проверка сохранена. Состояние работы обновлено.').waitFor();
  assert.equal((await workspace()).history.reviews[0].annotations[0].attachmentId, second.attachmentIds[0]);
  assert.equal((await workspace()).work.status, 'Accepted'); assert.equal((await queue()).total, 0);
  await page.screenshot({ path: `${output}/accepted-1440.png`, fullPage: true });
  results.push('Student reads feedback → Submit #2 with distinct photo → Teacher sees previous feedback and prior photos; real message concurrency conflict, no retry, draft survives refresh; empty-feedback Accept succeeds');
  await login(student); await page.goto(`${base}${route(1)}?tab=history`); await page.getByRole('article', { name: 'Попытка №2', exact: true }).waitFor();
  assert.equal(await page.locator('.history-attempt').count(), 2); assert.match(await page.getByRole('article', { name: 'Попытка №1', exact: true }).innerText(), /Объясните второй переход/);
  assert.equal(await page.getByRole('button', { name: 'Перо', exact: true }).count(), 0);
  assert.equal(await page.getByText(/Графические пометки: 1 штр/).count(), 2);
  await page.waitForFunction(() => [...document.querySelectorAll('.annotation-image img')].length === 3 && [...document.querySelectorAll('.annotation-image img')].every(i => i.naturalWidth > 0));
  for (const img of await page.locator('.annotation-image img').all()) assert(await img.evaluate(node => { const r = node.getBoundingClientRect(); const c = node.parentElement.querySelector('canvas').getBoundingClientRect(); return Math.abs(r.width / r.height - node.naturalWidth / node.naturalHeight) < .001 && Math.abs(r.height - c.height) < 1; }));
  data = await workspace(); assert.deepEqual(data.history.submissions, [second, first]); assert.equal(data.history.reviews.length, 2); assert.equal(data.history.reviews[0].messageId, null);
  const forbiddenBody = { submissionId: second.id, expectedVersion: data.work.version, feedbackMarkdown: null };
  assert.equal((await request(`/problem-works/${workId}/reviews/accept`, { method: 'POST', body: forbiddenBody })).status, 403);
  assert.equal((await request('/teacher/review-queue')).status, 403);
  assert.equal((await request(`/problem-works/${workId}`, { role: null })).status, 401);
  await page.goto(`${base}${workRoute}`); await page.getByText('Нет доступа к работе', { exact: true }).waitFor(); assert.equal(await page.locator('textarea').count(), 0);
  // Own-work GET is deliberately permitted by C4; foreign and teacher command access remain denied.
  results.push('Student History preserves both immutable attempts/reviews; Student queue/review POST forbidden, anonymous GET 401, teacher screen denied, general own-work API remains readable by C4 policy');
  for (let i = 0; i < 2; i++) assert.equal(await original(first.attachmentIds[i]), hash(photos[i]));
  compose('restart', 'api'); compose('up', '-d', '--wait'); assert.deepEqual((await workspace()).history, data.history);
  compose('down'); compose('up', '-d', '--wait'); assert.deepEqual((await workspace()).history, data.history);
  assert.equal(await original(second.attachmentIds[0]), hash(photos[2]));
  results.push('API restart and compose down/up preserve history and original hashes');
  // Only this script's dedicated disposable project is reset, after persistence assertions.
  compose('down', '-v'); compose('up', '--build', '-d', '--wait');
  assert.equal((await workspace()).work.problemWorkId, null);
  assert.equal((await request(`/attachments/${first.attachmentIds[0]}/content`)).status, 404);
  assert.equal((await queue()).total, 0);
  results.push('Dedicated lms-j2-smoke down -v/up --build resets database and attachments, migrations/auth/API/Web healthy');
  assert.deepEqual(errors, []);
  await writeFile(`${output}/results.json`, JSON.stringify({ results, errors }, null, 2)); console.log(JSON.stringify({ results, errors }, null, 2));
} catch (error) { if (page) await page.screenshot({ path: `${output}/failure.png`, fullPage: true }); throw error; }
finally { await browser.close(); }
