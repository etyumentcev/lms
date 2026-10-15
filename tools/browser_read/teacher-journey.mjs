import { writeFile } from 'node:fs/promises';
import path from 'node:path';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
let raw = ''; for await (const chunk of process.stdin) raw += chunk;
const input = JSON.parse(raw);
const { chromium } = require(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
const browser = await chromium.launch({ executablePath: process.env.LMS_BROWSER_EXECUTABLE || '/opt/google/chrome/chrome', headless: true });
try {
  const context = await browser.newContext({ ignoreHTTPSErrors: true });
  const page = await context.newPage();
  await page.goto(input.origin);
  await page.evaluate(({ studentToken, teacherToken }) => { window.session = { studentToken, teacherToken }; }, input);
  const stages = []; const events = [];
  page.on('pageerror', error => events.push({ error: error.name, message: error.message }));
  page.on('requestfailed', request => events.push({ path: new URL(request.url()).pathname, failure: request.failure()?.errorText }));
  page.on('response', response => events.push({ method: response.request().method(), path: new URL(response.url()).pathname, status: response.status() }));
  for (const [id, state] of [['submit','Submitted'],['review','NeedsRevision'],['revise','Submitted'],['accept','Accepted'],['retry','HTTP 409']]) {
    await page.locator('#' + id).click();
    try { await page.waitForFunction(expected => document.querySelector('#state').textContent === expected, state, { timeout: 35000 }); }
    catch { await writeFile(path.join(input.evidence, 'failure.json'), JSON.stringify({ stage: id, events }, null, 2)); throw new Error('Stage ' + id + ' expected ' + state + ', observed ' + await page.locator('#state').innerText() + ', events ' + JSON.stringify(events)); }
    stages.push({ action: id, state });
  }
  await page.screenshot({ path: path.join(input.evidence, 'journey.png') });
  await writeFile(path.join(input.evidence, 'result.json'), JSON.stringify({ passed: true, stages, scope: 'Isolated HTTPS protocol harness; production UI not accepted' }, null, 2));
  console.log('PASS');
} finally { await browser.close(); }
