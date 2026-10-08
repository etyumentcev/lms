// U1 only: real Compose/API/PostgreSQL/filesystem; no intercepted responses or G screens.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { createHmac } from 'node:crypto';
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.LMS_SMOKE_URL || 'http://127.0.0.1:8080';
const output = process.env.LMS_SMOKE_OUTPUT || '/tmp/lms-u1-browser';
await mkdir(output, { recursive: true });
const fixture = await readFile(new URL('../../../tests/Lms.Api.Tests/AttachmentImages.cs', import.meta.url), 'utf8');
const png = Buffer.from(fixture.match(/Png = Convert.FromBase64String\("([^"]+)"/)[1], 'base64');
const compose = (...args) => execFileSync('docker', ['compose', ...args], { encoding: 'utf8' }).trim();
const token = compose('exec', '-T', 'api', 'dotnet', 'Lms.Api.dll', '--dev-token');
const auth = { Authorization: `Bearer ${token}` };
const browser = await chromium.launch({ headless: true, channel: 'chrome' });
const errors = [], results = [];
try {
  const page = await browser.newPage(); page.on('pageerror', error => errors.push(error.message));
  await page.goto(`${base}/local-login.html`); await page.getByLabel('Вставьте token').fill(token); await page.getByRole('button').click();
  await page.waitForURL('**/courses/local-demo'); await page.goto(`${base}/dev/attachments`);
  await page.getByRole('heading', { name: 'Тест загрузки фотографий · U1' }).waitFor();
  const uploads = [];
  page.on('response', async response => { if (response.url().endsWith('/api/v1/uploads') && response.status() === 201) uploads.push(await response.json()); });
  await page.getByLabel('Добавить фото решения').setInputFiles({ name: 'Лист 1.png', mimeType: 'image/png', buffer: png });
  await page.getByAltText('Страница 1 — фото решения').waitFor();
  await page.getByLabel('Добавить фото решения').setInputFiles({ name: 'Лист 2.png', mimeType: 'image/png', buffer: png });
  await page.getByAltText('Страница 2 — фото решения').waitFor();
  await page.getByRole('button', { name: 'Страница 2: переместить выше' }).focus(); await page.keyboard.press('Enter');
  assert.match(await page.getByRole('listitem').first().innerText(), /Лист 2/);
  for (const width of [320, 375, 768, 1024, 1440]) {
    await page.setViewportSize({ width, height: 960 });
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
    assert(await page.getByAltText('Страница 1 — фото решения').evaluate(image => image.naturalWidth > 0));
    await page.screenshot({ path: `${output}/uploader-${width}.png`, fullPage: true });
  }
  results.push('login, two uploads, protected previews, keyboard reorder, 5 responsive widths');
  await page.getByRole('button', { name: 'Удалить страницу 2' }).click();
  await page.waitForFunction(() => document.querySelectorAll('.attachment-pages li').length === 1);
  assert.equal((await fetch(`${base}${uploads[0].previewUrl}`, { headers: auth })).status, 404);
  assert.equal((await fetch(`${base}${uploads[1].previewUrl}`, { headers: auth })).status, 200);
  assert.equal((await fetch(`${base}${uploads[1].previewUrl}`)).status, 401);
  const [header, payload] = token.split('.'); const claims = JSON.parse(Buffer.from(payload, 'base64url').toString());
  claims.sub = '10000000-0000-0000-0000-000000000099';
  const unsigned = `${header}.${Buffer.from(JSON.stringify(claims)).toString('base64url')}`;
  const foreign = `${unsigned}.${createHmac('sha256', 'local-compose-only-signing-key-never-use-in-production-2026').update(unsigned).digest('base64url')}`;
  for (const [path, method] of [[uploads[1].previewUrl, 'GET'], [`/api/v1/attachments/${uploads[1].attachmentId}`, 'DELETE']])
    assert.equal((await fetch(`${base}${path}`, { method, headers: { Authorization: `Bearer ${foreign}` } })).status, 404);
  results.push('pending delete, anonymous and foreign student content/delete denial');
  const workCount = compose('exec', '-T', 'postgres', 'psql', '-U', 'lms_local', '-d', 'lms', '-Atc', 'SELECT count(*) FROM "ProblemWorks"');
  assert.equal(workCount, '0'); results.push('uploads created no ProblemWork');
  await writeFile(`${output}/persist.json`, JSON.stringify({ attachment: uploads[1], token }));
  assert.deepEqual(errors, []); await writeFile(`${output}/results.json`, JSON.stringify({ results, errors }, null, 2));
  console.log(JSON.stringify({ results, errors }));
} finally { await browser.close(); }
