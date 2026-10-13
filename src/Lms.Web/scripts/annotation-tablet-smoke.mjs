// Dedicated J2 stack: tablet emulation, native pen events, EXIF, DPR and sustained drawing.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { execFileSync } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.LMS_SMOKE_URL || 'http://127.0.0.1:8085';
const output = process.env.LMS_SMOKE_OUTPUT || '/tmp/lms-j2-tablet'; await mkdir(output, { recursive: true });
const compose = (...args) => execFileSync('docker', ['compose', ...args], { encoding: 'utf8' }).trim();
assert(['lms-j2-smoke', 'lms-k-smoke'].includes(JSON.parse(compose('config', '--format', 'json')).name));
const student = compose('exec', '-T', 'api', 'dotnet', 'Lms.Api.dll', '--dev-token');
const teacher = compose('exec', '-T', 'api', 'dotnet', 'Lms.Api.dll', '--dev-token', 'Teacher');
async function api(path, token, body) {
  const r = await fetch(base + '/api/v1' + path, { method: body ? 'POST' : 'GET', headers: { Authorization: `Bearer ${token}`, ...(body ? { 'Content-Type': 'application/json' } : {}) }, body: body && JSON.stringify(body) });
  assert(r.ok, await r.clone().text()); return r.json();
}
const browser = await chromium.launch({ headless: true, channel: 'chrome' });
const context = await browser.newContext({ viewport: { width: 768, height: 1024 }, deviceScaleFactor: 2, hasTouch: true, isMobile: true });
const page = await context.newPage(); const errors = []; page.on('pageerror', e => errors.push(e.message));
try {
  await page.goto(base + '/local-login.html');
  const jpeg = Buffer.from(await page.evaluate(() => { const c = document.createElement('canvas'); c.width = 4200; c.height = 3000; const x = c.getContext('2d'); x.fillStyle = 'white'; x.fillRect(0, 0, 4200, 3000); x.fillStyle = '#123a25'; x.font = '100px serif'; x.fillText('EXIF orientation 6 / 4200 × 3000', 100, 300); x.fillStyle = 'red'; x.fillRect(0, 0, 120, 120); return c.toDataURL('image/jpeg').split(',')[1]; }), 'base64');
  const exif = Buffer.from('ffe1002245786966000049492a0008000000010012010300010000000600000000000000', 'hex');
  const original = Buffer.concat([jpeg.subarray(0, 2), exif, jpeg.subarray(2)]);
  const form = new FormData(); form.append('file', new Blob([original], { type: 'image/jpeg' }), 'oriented.jpg');
  const uploaded = await fetch(base + '/api/v1/uploads', { method: 'POST', headers: { Authorization: `Bearer ${student}` }, body: form }); assert.equal(uploaded.status, 201);
  const attachmentId = (await uploaded.json()).attachmentId;
  const submitted = await api('/problem-assignments/40000000-0000-0000-0000-000000000002/submissions', student, { attachmentIds: [attachmentId] });
  await page.getByLabel('Вставьте token').fill(teacher); await page.getByRole('button').click(); await page.waitForURL('**/courses/local-demo');
  await page.goto(base + '/teacher/reviews/' + submitted.problemWorkId); await page.waitForFunction(() => document.querySelector('.annotation-image img')?.naturalWidth > 0);
  assert.deepEqual(await page.locator('.annotation-image img').evaluate(i => [i.naturalWidth, i.naturalHeight]), [3000, 4200]);
  const canvas = page.locator('canvas[aria-label^="Слой"]'); await canvas.scrollIntoViewIfNeeded();
  const box = await canvas.boundingBox(); const cdp = await context.newCDPSession(page);
  await cdp.send('Input.dispatchMouseEvent', { type: 'mousePressed', x: box.x + box.width * .2, y: box.y + box.height * .3, button: 'left', buttons: 1, clickCount: 1, pointerType: 'pen', force: .6 });
  await cdp.send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: box.x + box.width * .4, y: box.y + box.height * .4, button: 'left', buttons: 1, pointerType: 'pen', force: .8 });
  await cdp.send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: box.x + box.width * .4, y: box.y + box.height * .4, button: 'left', buttons: 0, clickCount: 1, pointerType: 'pen' });
  await page.getByText(/Графические пометки: 1 штр/).waitFor();
  for (const size of [{ width: 1024, height: 768 }, { width: 768, height: 1024 }]) {
    await page.setViewportSize(size);
    // ResizeObserver schedules backing-buffer resize on the next animation frame.
    // Await the same exact geometry invariant instead of racing that asynchronous frame.
    await page.waitForFunction(() => { const c = document.querySelector('canvas[aria-label^="Слой"]'); const i = c.parentElement.querySelector('img'); const r = i.getBoundingClientRect(); const s = c.getBoundingClientRect(); return Math.abs(r.width / r.height - 3000 / 4200) < .001 && Math.abs(s.height - r.height) < 1 && Math.abs(c.width - i.clientWidth * devicePixelRatio) <= 1; });
  }
  // Force Fullscreen API refusal to verify the tablet CSS fallback independently.
  await page.evaluate(() => { HTMLElement.prototype.requestFullscreen = async () => { throw Error('Fullscreen unavailable'); }; });
  await page.getByRole('button', { name: 'На весь экран' }).click(); await page.getByRole('dialog').waitFor();
  await page.screenshot({ path: output + '/tablet-fullscreen.png' });
  const elapsed = await canvas.evaluate(async c => {
    const start = performance.now(); const rect = c.getBoundingClientRect();
    // Synthetic stress events do not register native active pointers; capture is tested above with CDP.
    c.setPointerCapture = () => {};
    for (let stroke = 0; stroke < 200; stroke++) {
      for (let point = 0; point < 100; point++) c.dispatchEvent(new PointerEvent(point ? 'pointermove' : 'pointerdown', { bubbles: true, pointerType: 'pen', pointerId: 77, button: 0, pressure: .7,
        clientX: rect.left + rect.width * (.1 + point * .007), clientY: rect.top + rect.height * (.1 + (stroke % 80) * .01) }));
      c.dispatchEvent(new PointerEvent('pointerup', { bubbles: true, pointerType: 'pen', pointerId: 77 }));
      await new Promise(requestAnimationFrame);
    }
    return performance.now() - start;
  });
  await page.getByText(/Графические пометки: 201 штр/).waitFor(); assert(elapsed < 30000, `drawing took ${elapsed}ms`);
  await page.getByRole('button', { name: 'Выйти из полного экрана' }).click(); await page.getByRole('button', { name: 'Принять решение' }).click();
  await page.getByText('Проверка сохранена. Состояние работы обновлено.').waitFor();
  const work = await api('/problem-works/' + submitted.problemWorkId, teacher); const strokes = work.reviews[0].annotations[0].strokes;
  assert.equal(strokes.length, 201); assert(strokes.reduce((n, s) => n + s.points.length, 0) >= 20000); assert(Math.abs(strokes[0].points[0].pressure - .6) < .001); assert(Math.abs(strokes[0].points[0].x - .2) < .01);
  const content = await fetch(base + '/api/v1/attachments/' + attachmentId + '/content', { headers: { Authorization: `Bearer ${student}` } }); assert.deepEqual(Buffer.from(await content.arrayBuffer()), original);
  assert.deepEqual(errors, []); const result = { nativePen: true, orientation: 6, originalPixels: [4200, 3000], orientedPixels: [3000, 4200], dpr: 2, strokes: 201, points: strokes.reduce((n, s) => n + s.points.length, 0), drawingMs: elapsed, errors };
  await writeFile(output + '/results.json', JSON.stringify(result, null, 2)); console.log(result);
} catch (e) { await page.screenshot({ path: output + '/failure.png', fullPage: true }); throw e; }
finally { await browser.close(); }
