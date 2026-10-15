// Child runner. Secrets arrive only over its anonymous stdin pipe and stay in memory.
import assert from 'node:assert/strict';
import { createInterface } from 'node:readline';
import { writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const lines = createInterface({ input: process.stdin })[Symbol.asyncIterator]();
const receive = async () => JSON.parse((await lines.next()).value);
const config = await receive();
const { chromium } = require(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
const command = async name => { process.stdout.write(`${name}\n`); return receive(); };
const browser = await chromium.launch(process.env.LMS_BROWSER_EXECUTABLE
  ? { executablePath: process.env.LMS_BROWSER_EXECUTABLE, headless: true }
  : { channel: 'chrome', headless: true });
const checks = [], consoleMessages = [], requestUrls = [], apiResponses = [], cacheResponses = [], servedFromCache = [], networkRequests = new Map();
let currentToken = config.token;
const tokens = [currentToken];
const course = '/api/v1/courses/http-own';
const lesson = `${course}/versions/${config.version}/lessons/lesson`;
const problem = `${lesson}/problems/problem`;
const ui = path => path.slice('/api/v1'.length);
let context, page;
async function newPage(token = currentToken) {
  context = await browser.newContext({ viewport: { width: 1280, height: 900 }, serviceWorkers: 'block' });
  // Existing frontend session contract; per-document/tab closure, no storage or cookie issuance.
  if (token) await context.addInitScript(({ origin, access }) => {
    if (location.origin !== origin) return;
    let value = access;
    window.lmsAuth = { getAccessToken: () => value, onUnauthenticated: () => { value = null; } };
  }, { origin: config.origin, access: token });
  page = await context.newPage();
  page.setDefaultTimeout(12000);
  page.on('console', msg => consoleMessages.push(msg.text()));
  page.on('pageerror', error => consoleMessages.push(error.message));
  page.on('request', request => requestUrls.push(request.url()));
  const cdp = await context.newCDPSession(page);
  await cdp.send('Network.enable'); // No Network.setCacheDisabled: real browser cache behavior.
  cdp.on('Network.requestWillBeSent', ({ requestId, request }) => networkRequests.set(requestId, request.url));
  cdp.on('Network.requestServedFromCache', ({ requestId }) => servedFromCache.push(requestId));
  cdp.on('Network.responseReceived', ({ response }) => {
    if (response.url.startsWith(config.origin + '/api/')) cacheResponses.push({
      status: response.status, disk: !!response.fromDiskCache, serviceWorker: !!response.fromServiceWorker,
      prefetch: !!response.fromPrefetchCache,
    });
  });
  page.on('response', response => {
    if (response.url().startsWith(config.origin + '/api/')) apiResponses.push(response);
  });
}
async function responseTo(path, action, status = 200) {
  const pending = page.waitForResponse(r => r.url() === config.origin + path);
  await action(); const response = await pending;
  assert.equal(response.status(), status, `HTTP status for ${path}`);
  assert.equal(response.headers()['cache-control'], 'no-store');
  const body = await response.text();
  if (status !== 200) assert.equal(body, '');
  assert.equal(body.includes('HIDDEN'), false);
  for (const marker of config.privateMarkers) assert.equal(body.includes(marker), false);
  return status === 200 ? JSON.parse(body) : null;
}
async function open(path, status = 200) { return responseTo(path, () => page.goto(config.origin + ui(path)), status); }
async function heading(text) { await page.getByRole('heading', { name: text, exact: true }).waitFor(); }
async function refresh(path, status = 200) {
  return responseTo(path, () => page.getByRole('button', { name: 'Обновить материалы', exact: true }).click(), status);
}
async function screenshot(name) { await page.screenshot({ path: join(config.evidence, name + '.png'), fullPage: true }); }
async function denied(path) {
  await open(path, 403); await page.getByRole('alert').waitFor();
  assert.equal((await page.locator('body').innerText()).includes(config.canary), false);
}
try {
  await newPage();
  const courses = await open('/api/v1/courses');
  await heading('Мои курсы');
  assert.equal(courses.items.length, 1);
  assert.equal(courses.items[0].role, config.role);
  assert.equal(courses.items[0].progress, null);
  assert.equal(await page.getByRole('link', { name: /http-other|http-foreign|HIDDEN/ }).count(), 0);
  await screenshot('01-courses');
  const coursePath = `/api/v1/courses/${encodeURIComponent(courses.items[0].course.id)}`;
  const courseData = await responseTo(coursePath, () => page.getByRole('link', { name: /Real course/ }).click());
  await heading('Real course');
  await screenshot('02-course');
  const lessonLink = courseData.lessons.find(l => l.id === 'lesson');
  assert.equal(lessonLink.href, lesson);
  const lessonData = await responseTo(lessonLink.href, () => page.locator(`a[href="${ui(lessonLink.href)}"]`).click());
  await heading('Real lesson');
  assert.equal(await page.locator('.markdown-content strong').innerText(), 'lesson');
  await screenshot('03-lesson');
  const problemLink = lessonData.problems[0];
  assert.equal(problemLink.href, problem);
  const statement = await responseTo(problemLink.href, () => page.locator(`a[href="${ui(problemLink.href)}"]`).click());
  await heading('Real problem'); await heading('Условие задачи');
  assert.equal(statement.statementMarkdown.includes(config.canary), true);
  await page.getByText(config.canary, { exact: true }).waitFor();
  assert.equal(await page.locator('.katex').count(), 2);
  assert.equal(await page.locator('.markdown-content img, .markdown-content script, .markdown-content a[href^="javascript:"]').count(), 0);
  assert.equal(await page.evaluate(() => window.pwned), undefined);
  assert.equal(await page.getByRole('button', { name: /Отправить|Принять решение|Загрузить/ }).count(), 0);
  await screenshot('04-statement');
  checks.push('API-backed courses → course → published lesson → problem/Markdown/LaTeX; no writes; XSS/image boundary');

  await responseTo(lesson, () => page.goBack()); await heading('Real lesson');
  await responseTo(problem, () => page.goForward()); await heading('Real problem');
  await responseTo(lesson, () => page.getByRole('link', { name: 'Занятие', exact: true }).click());
  await heading('Real lesson');
  await responseTo(course, () => page.getByRole('link', { name: 'Курс', exact: true }).click());
  await heading('Real course');
  const emptyLesson = courseData.lessons.find(l => l.id === 'other-lesson');
  await responseTo(emptyLesson.href, () => page.locator(`a[href="${ui(emptyLesson.href)}"]`).click());
  await page.getByText('В занятии пока нет задач', { exact: true }).waitFor();
  await command('hide-lessons'); await open(course);
  await page.getByText('В курсе пока нет доступных занятий', { exact: true }).waitFor();
  await screenshot('05-empty-course'); await command('show-lessons');
  checks.push('browser back/forward and parent links re-fetch; empty lesson and course from PostgreSQL');

  for (const path of [course, lesson, problem]) {
    await open(path); // independent positive before each access family
    for (const replacement of ['http-other', 'http-foreign', 'absent']) await denied(path.replace('http-own', replacement));
  }
  for (const path of [lesson, problem]) {
    await denied(path.replace(config.version, 'invalid'));
    await denied(path.replace('/lessons/lesson', '/lessons/locked'));
    await denied(path.replace('/lessons/lesson', '/lessons/absent'));
  }
  await denied(problem.replace('/lessons/lesson', '/lessons/other-lesson'));
  await screenshot('06-access-denied');
  checks.push('own positive; same-org ungranted, foreign-org, absent, malformed version, locked/missing/swapped parent uniformly 403');

  await open(problem); await heading('Real problem');
  // Prime using the browser default HTTP cache policy, independently of ApiClient.
  for (let i = 0; i < 2; i++) {
    const warm = await page.evaluate(async path => {
      const r = await fetch(path, { headers: { Authorization: `Bearer ${await window.lmsAuth.getAccessToken()}` } });
      return { status: r.status, statement: (await r.json()).statementMarkdown, cache: r.headers.get('cache-control') };
    }, problem);
    assert.equal(warm.status, 200); assert.equal(warm.statement.includes(config.canary), true);
    assert.equal(warm.cache, 'no-store');
  }
  await command('restrict'); // actual committed ProcessingRestrictions while the page is open
  await refresh(problem, 403); await page.getByRole('alert').waitFor();
  assert.equal((await page.locator('body').innerText()).includes(config.canary), false);
  await screenshot('07-restriction');
  // Direct default-cache requests: the server must deny even outside ApiClient's no-store mode.
  const deniedFetch = await page.evaluate(async path => {
    const r = await fetch(path, { headers: { Authorization: `Bearer ${await window.lmsAuth.getAccessToken()}` } });
    return { status: r.status, body: await r.text(), cache: r.headers.get('cache-control') };
  }, problem);
  assert.deepEqual(deniedFetch, { status: 403, body: '', cache: 'no-store' });
  const empty = await open('/api/v1/courses'); assert.deepEqual(empty, { items: [] });
  await page.getByText('Пока нет доступных курсов', { exact: true }).waitFor();
  await responseTo(problem, () => page.goBack(), 403); await page.getByRole('alert').waitFor();
  await command('withdraw'); await open(problem); await heading('Real problem');
  await command('restrict-after-read'); await refresh(problem, 403);
  await page.getByRole('alert').waitFor();
  assert.equal((await page.locator('body').innerText()).includes(config.canary), false);
  await command('withdraw');
  checks.push('current restriction while open; refresh, default-cache fetch, history and list deny; post-read pre-response restriction denies buffered content');

  await open(problem); await heading('Real problem');
  await command('database-down'); await refresh(problem, 503); await page.getByRole('alert').waitFor();
  await screenshot('08-server-error'); await command('database-up');
  await responseTo(problem, () => page.getByRole('button', { name: 'Повторить', exact: true }).click());
  await heading('Real problem');
  await context.setOffline(true);
  await page.getByRole('button', { name: 'Обновить материалы', exact: true }).click();
  await page.getByRole('alert').waitFor();
  assert.equal((await page.locator('body').innerText()).includes(config.canary), false);
  await context.setOffline(false);
  await responseTo(problem, () => page.getByRole('button', { name: 'Повторить', exact: true }).click());
  await heading('Real problem');
  checks.push('real DB permission failure →503, explicit GET retry; offline → error → reconnect');

  // API success/denial headers inspected before disposing contexts.
  for (const response of apiResponses) {
    assert.equal(response.headers()['cache-control'], 'no-store');
    assert.equal(response.headers()['set-cookie'], undefined);
  }
  const storage = await page.evaluate(async () => ({ local: localStorage.length, session: sessionStorage.length,
    caches: await caches.keys(), workers: (await navigator.serviceWorker.getRegistrations()).length,
    dbs: (await indexedDB.databases()).length }));
  assert.deepEqual(storage, { local: 0, session: 0, caches: [], workers: 0, dbs: 0 });
  assert.deepEqual(await context.cookies(), []);
  // Public build artifacts are fetched without Authorization, recursively including fonts/chunks.
  const publicClient = await browser.newContext();
  const resources = new Set(requestUrls.filter(url => url.startsWith(config.origin) && !url.includes('/api/')));
  for (const resource of resources) {
    const r = await publicClient.request.get(resource);
    const body = await r.body();
    assert.equal(body.includes(Buffer.from(config.canary)), false);
    assert.equal(body.includes(Buffer.from('HIDDEN STATEMENT')), false);
    for (const token of [...tokens, ...config.privateMarkers]) assert.equal(body.includes(Buffer.from(token)), false);
  }
  for (const path of ['/api/v1/auth/login', '/api/v1/auth/refresh', '/fixture', '/token', '/tests/Lms.Api.Tests/BrowserReadTests.cs', '/api/v1/attachments/forged/content'])
    assert.equal((await publicClient.request.get(config.origin + path)).status(), 404);
  for (const path of ['/api/v1/courses', course, lesson, problem]) {
    const r = await publicClient.request.get(config.origin + path);
    assert.equal(r.status(), 401); assert.equal(await r.text(), '');
    assert.equal(r.headers()['cache-control'], 'no-store');
  }
  await publicClient.close();
  checks.push('no token/content in public build/HTML; no public auth/fixture/file routes; unauthenticated all reads 401; memory-only auth, no cookies/storage/CacheStorage/IndexedDB/SW');

  await command('expire'); await refresh(problem, 401); await page.getByRole('alert').waitFor();
  assert.equal(await page.evaluate(() => window.lmsAuth.getAccessToken()), null);
  assert.equal(await page.getByRole('button', { name: 'Повторить', exact: true }).count(), 0);
  await screenshot('09-expired');
  checks.push('exact 15m JWT expiry →401; adapter clears per-tab token; no automatic retry/refresh');
  await context.close();
  const issued = await command('reissue'); currentToken = issued.token; tokens.push(currentToken);
  await newPage(currentToken + 'x'); await open(course, 401); await page.getByRole('alert').waitFor(); await context.close();
  checks.push('forged JWT signature →401 through real validator');
  await newPage(); await open(problem); await heading('Real problem');
  await page.setViewportSize({ width: 768, height: 1024 });
  await screenshot('10-tablet-statement');
  await command('clock-lost'); await refresh(problem, 401); await page.getByRole('alert').waitFor();
  await screenshot('11-clock-lost');
  checks.push('independent valid reissued JWT positive → synthetic clock qualification loss →401; tablet viewport render');

  assert.equal(requestUrls.some(url => !url.startsWith(config.origin)), false, 'Unexpected external request');
  for (const text of [...requestUrls, ...consoleMessages]) {
    assert.equal(text.includes(config.canary), false);
    assert.equal(/eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+/.test(text), false);
    for (const token of [...tokens, ...config.privateMarkers]) assert.equal(text.includes(token), false);
  }
  assert.equal(cacheResponses.length > 30, true);
  assert.equal(cacheResponses.some(r => r.disk || r.serviceWorker || r.prefetch || r.status === 304), false);
  assert.equal(servedFromCache.some(id => networkRequests.get(id)?.startsWith(config.origin + '/api/')), false);
  checks.push('no bearer/private canary in URLs or browser logs; CDP shows no API disk/SW/prefetch cache or 304, without disabling browser cache');
  await writeFile(join(config.evidence, 'results.json'), JSON.stringify({ role: config.role, browser: browser.version(),
    checks, apiResponseCount: apiResponses.length, cacheResponseCount: cacheResponses.length,
    cachedApiResponses: 0, publicResourceCount: resources.size,
    limitation: 'Synthetic local Chromium only; not retroactive erasure, production G02 or CLOCK-DEPLOY evidence.' }, null, 2) + '\n');
  process.stdout.write('PASS\n');
} catch (error) {
  let message = error?.stack || String(error);
  for (const token of tokens) message = message.replaceAll(token, '[REDACTED]');
  message = message.replace(/eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+/g, '[REDACTED]');
  process.stderr.write(message + '\n'); process.exitCode = 1;
} finally { await browser.close(); process.stdin.destroy(); }
