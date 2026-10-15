import assert from 'node:assert/strict';
import { createInterface } from 'node:readline';
import { writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const lines = createInterface({ input: process.stdin })[Symbol.asyncIterator]();
const receive = async () => JSON.parse((await lines.next()).value);
const config = await receive();
const command = async name => { process.stdout.write(`${name}\n`); return receive(); };
const { chromium } = require(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
const browser = await chromium.launch(process.env.LMS_BROWSER_EXECUTABLE ? { executablePath: process.env.LMS_BROWSER_EXECUTABLE, headless: true } : { channel: 'chrome', headless: true });
const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, serviceWorkers: 'block' });
await context.addInitScript(({ origin, token }) => {
  if (location.origin !== origin) return;
  let access = token;
  window.lmsAuth = { getAccessToken: () => access, onUnauthenticated: () => { access = null; } };
}, { origin: config.origin, token: config.token });
const page = await context.newPage(); page.setDefaultTimeout(12000);
const logs = [], urls = [], responses = [], cache = [];
page.on('console', m => logs.push(m.text())); page.on('pageerror', e => logs.push(e.message));
page.on('request', r => urls.push(r.url()));
page.on('response', r => { if (r.url().includes('/api/')) responses.push(r); });
const cdp = await context.newCDPSession(page); await cdp.send('Network.enable');
cdp.on('Network.responseReceived', ({ response: r }) => { if (r.url.includes('/api/')) cache.push(r.fromDiskCache || r.fromServiceWorker || r.fromPrefetchCache || r.status === 304); });
const lesson = `/courses/http-own/versions/${config.version}/lessons/lesson`;
const problem = `${lesson}/problems/problem`, list = `${lesson}/works`, work = `${list}/${config.work}`;
const screenshot = name => page.screenshot({ path: join(config.evidence, name), fullPage: true });
async function get(path, expected) {
  const r = await page.evaluate(async path => {
    const token = await window.lmsAuth.getAccessToken();
    const response = await fetch('/api/v1' + path, { headers: { Authorization: 'Bearer ' + token } });
    return { status: response.status, body: await response.text(), cache: response.headers.get('cache-control') };
  }, path);
  assert.equal(r.status, expected); assert.equal(r.cache, 'no-store'); if (expected !== 200) assert.equal(r.body, '');
  return r.body;
}
try {
  await page.goto(config.origin + (config.role === 'Student' ? problem : lesson));
  await page.getByRole('link', { name: config.role === 'Student' ? 'Моя работа / доступные работы' : 'Работы учеников', exact: true }).click();
  await page.getByRole('link', { name: 'Работа 1 · problem', exact: true }).click();
  await page.getByRole('heading', { name: 'Попытка 2', exact: true }).waitFor();
  await screenshot('01-current-attempt.png');
  await page.getByLabel('Попытка', { exact: true }).selectOption({ label: 'Попытка 1' });
  await page.getByRole('heading', { name: 'Попытка 1', exact: true }).waitFor();
  assert.equal(await page.getByText('First attempt: explain the calculation', { exact: true }).count(), 2);
  await page.getByText('Фотографии (2) сейчас недоступны.', { exact: true }).waitFor();
  assert.equal(await page.locator('img').count(), 0);
  await screenshot('02-history-files-blocked.png');
  await page.getByRole('link', { name: 'Условие задачи', exact: true }).click();
  await page.getByRole('heading', { name: 'Условие задачи', exact: true }).waitFor();
  assert.equal(await page.locator('.katex').count(), 1);
  await screenshot('03-statement.png');
  const back = page.waitForResponse(r => r.url() === config.origin + '/api/v1' + work);
  await page.goBack(); assert.equal((await back).status(), 200);
  await page.getByRole('heading', { name: 'Попытка 2', exact: true }).waitFor();
  await get(`${list}/${config.hidden}`, 403);
  for (const course of ['http-other', 'http-foreign']) await get(work.replace('http-own', course), 403);
  await get(work.replace('/lessons/lesson', '/lessons/other-lesson'), 403);
  await get(`/attachments/${config.attachment}/content`, 404);
  await get(work, 200); await get(work, 200); // Browser default cache policy, no route mocking/cache disabling.
  await command('restrict'); await get(work, 403);
  await page.getByRole('button', { name: 'Обновить работу' }).click();
  await page.getByText('Нет доступа к работе', { exact: true }).waitFor();
  assert.equal(await page.getByText('Second synthetic attempt').count(), 0);
  await screenshot('04-restricted.png');
  await command('withdraw'); await page.goto(config.origin + work);
  await page.getByRole('heading', { name: 'Попытка 2', exact: true }).waitFor();
  await page.setViewportSize({ width: 768, height: 1024 }); await screenshot('05-tablet-history.png');
  await command('expire');
  await page.getByRole('button', { name: 'Обновить работу' }).click();
  await page.getByText('Войдите, чтобы открыть работу', { exact: true }).waitFor();
  assert.equal(await page.evaluate(() => window.lmsAuth.getAccessToken()), null);
  await screenshot('06-expired.png');
  for (const r of responses) { assert.equal(r.headers()['cache-control'], 'no-store'); assert.equal(r.headers()['set-cookie'], undefined); }
  assert.equal(cache.some(Boolean), false);
  assert.equal(logs.join('\n').includes(config.token), false); assert.equal(urls.some(u => u.includes(config.token)), false);
  assert.equal(urls.every(u => u.startsWith(config.origin)), true);
  assert.deepEqual(await context.cookies(), []);
  assert.deepEqual(await page.evaluate(async () => ({ local: localStorage.length, session: sessionStorage.length, caches: await caches.keys(), db: await indexedDB.databases() })), { local: 0, session: 0, caches: [], db: [] });
  await writeFile(join(config.evidence, 'results.json'), JSON.stringify({ role: config.role, browser: await browser.version(), status: 'PASS',
    covered: ['real-http-postgres', 'own-assigned-work', 'historical-review-binding', 'statement', 'foreign-work-denial', 'parent-denial', 'current-restriction', 'jwt-expiry', 'no-store', 'tablet-history'],
    blocked: ['photo-metadata', 'originals', 'photo-order', 'A4-zoom-fullscreen', 'Range-preview'], apiRequests: responses.length }, null, 2));
  process.stdout.write('PASS\n');
} catch (error) {
  let message = error?.stack || String(error);
  message = message.replaceAll(config.token, '[REDACTED]').replace(/eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+/g, '[REDACTED]');
  process.stderr.write(message + '\n'); process.exitCode = 1;
} finally { await context.close(); await browser.close(); process.stdin.destroy(); }
