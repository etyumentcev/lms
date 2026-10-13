import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import readline from 'node:readline';

const commandInput = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
const commands = commandInput[Symbol.asyncIterator]();
const first = await commands.next();
if (first.done) throw new Error('Missing journey input');
const input = JSON.parse(first.value);
const { chromium } = await import(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
const browser = await chromium.launch({
  executablePath: process.env.LMS_BROWSER_EXECUTABLE || '/opt/google/chrome/chrome',
  headless: true,
});

const events = [];
const stages = [];
await mkdir(input.evidence, { recursive: true });

async function control(command) {
  process.stdout.write(command + '\n');
  const response = await commands.next();
  if (response.done) throw new Error('Missing control response');
  JSON.parse(response.value || '{}');
}

try {
  const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1365, height: 900 } });
  await context.addInitScript(({ studentToken }) => {
    window.__journeyAuth = { token: localStorage.getItem('journey-token') || studentToken, unauthenticated: 0 };
    window.lmsAuth = {
      getAccessToken: () => window.__journeyAuth.token,
      onUnauthenticated: () => { window.__journeyAuth.unauthenticated += 1; },
    };
  }, { studentToken: input.studentToken });
  const page = await context.newPage();
  page.on('pageerror', error => events.push({ error: error.name, message: error.message }));
  page.on('requestfailed', request => events.push({ method: request.method(), path: new URL(request.url()).pathname,
    failure: request.failure()?.errorText }));
  page.on('response', response => {
    const url = new URL(response.url());
    if (url.pathname.startsWith('/api/')) events.push({ method: response.request().method(), path: url.pathname, status: response.status() });
  });

  const photo = { name: 'real-solution.png', mimeType: 'image/png', buffer: Buffer.from(input.pngBase64, 'base64') };
  const teacherPhoto = { name: 'real-remark.png', mimeType: 'image/png', buffer: Buffer.from(input.pngBase64, 'base64') };
  const setToken = token => page.evaluate(value => {
    localStorage.setItem('journey-token', value); window.__journeyAuth.token = value;
  }, token);
  const studentUrl = `${input.origin}/problem-assignments/${input.assignmentId}`;
  const teacherUrl = `${input.origin}/teacher/reviews/${input.workId}`;

  await page.goto(studentUrl);
  await page.getByRole('heading', { name: 'Journey problem' }).waitFor();
  await page.locator('section[aria-label="Фотографии решения"] input[type=file]').setInputFiles(photo);
  await page.getByText('Фото загружено').waitFor();
  await page.getByRole('textbox', { name: 'Пояснение к решению — необязательно' }).fill('First answer from the real React UI');
  await page.getByRole('button', { name: 'Отправить решение' }).click();
  await page.getByText('Решение отправлено и ожидает проверки преподавателем.').waitFor();
  await page.getByLabel('Статус: На проверке').first().waitFor();
  stages.push({ action: 'student-submit', ui: 'React', photo: true, state: 'Submitted' });

  await setToken(input.teacherToken);
  await page.goto(teacherUrl);
  await page.getByRole('heading', { name: 'Journey problem' }).waitFor();
  const firstCanvas = page.getByRole('img', { name: 'Страница 1' });
  await firstCanvas.waitFor();
  const layer = page.locator('canvas[aria-label="Слой пометок страницы 1"]');
  await layer.waitFor({ state: 'visible' });
  const box = await layer.boundingBox();
  if (!box || box.width < 10 || box.height < 10) throw new Error('Annotation canvas has no usable geometry');
  await page.mouse.move(box.x + box.width * .25, box.y + box.height * .25);
  await page.mouse.down();
  await page.mouse.move(box.x + box.width * .7, box.y + box.height * .65, { steps: 8 });
  await page.mouse.up();
  await page.getByText(/Графические пометки: 1 штр\./).waitFor();

  await page.getByRole('textbox', { name: 'Текст сообщения' }).fill('A photographed teacher remark');
  await page.locator('section[aria-label="Новые фотографии замечания"] input[type=file]').setInputFiles(teacherPhoto);
  await page.locator('section[aria-label="Новые фотографии замечания"]').getByText('Фото загружено').waitFor();
  await page.getByRole('button', { name: 'Отправить сообщение' }).click();
  await page.getByText('Проверка сохранена. Состояние работы обновлено.').waitFor();
  stages.push({ action: 'teacher-message', ui: 'React', photo: true });

  await page.getByRole('textbox', { name: 'Комментарий преподавателя' }).fill('Explain step two and revise the answer.');
  await page.getByRole('button', { name: 'Вернуть на доработку' }).click();
  await page.getByLabel('Статус: Нужна доработка').first().waitFor();
  await page.getByText('Работа возвращена на доработку. Ожидается новая попытка ученика.').waitFor();
  stages.push({ action: 'teacher-needs-revision', ui: 'React', annotationStrokes: 1, state: 'NeedsRevision' });

  await setToken(input.studentToken);
  await page.goto(studentUrl);
  await page.getByRole('heading', { name: 'Journey problem' }).waitFor();
  await page.getByRole('heading', { name: 'Новое решение' }).waitFor();
  await page.locator('section[aria-label="Фотографии решения"] input[type=file]').setInputFiles({ ...photo, name: 'real-revision.png' });
  await page.getByText('Фото загружено').waitFor();
  await page.getByRole('textbox', { name: 'Пояснение к решению — необязательно' }).fill('Revised answer from the real React UI');
  await page.getByRole('button', { name: 'Отправить решение' }).click();
  await page.getByText('Решение отправлено и ожидает проверки преподавателем.').waitFor();
  await page.getByLabel('Статус: На проверке').first().waitFor();
  stages.push({ action: 'student-revise', ui: 'React', photo: true, state: 'Submitted' });

  await setToken(input.teacherToken);
  await page.goto(teacherUrl);
  await page.getByRole('heading', { name: 'Journey problem' }).waitFor();
  await page.getByRole('button', { name: 'Принять решение' }).waitFor();
  await page.getByRole('textbox', { name: 'Комментарий преподавателя' }).fill('Accepted after the correction.');
  await page.getByRole('button', { name: 'Принять решение' }).click();
  await page.getByLabel('Статус: Принята').first().waitFor();
  await page.getByText('Решение принято. Работа доступна только для чтения.').waitFor();
  if (await page.getByRole('button', { name: 'Принять решение' }).count()) throw new Error('Accepted command remained repeatable in UI');
  stages.push({ action: 'teacher-accept', ui: 'React', state: 'Accepted', repeatedCommandButton: false });

  await setToken(input.studentToken);
  await page.goto(studentUrl);
  await page.getByLabel('Статус: Принята').first().waitFor();
  stages.push({ action: 'student-observes-accepted', ui: 'React' });

  await control('revoke');
  await setToken(input.teacherToken);
  await page.goto(teacherUrl);
  await page.getByRole('heading', { name: 'Нет доступа к работе' }).waitFor();
  stages.push({ action: 'teacher-revoked', ui: 'React', state: 'Forbidden' });

  await setToken(input.outsiderToken);
  await page.goto(teacherUrl);
  await page.getByRole('heading', { name: 'Нет доступа к работе' }).waitFor();
  stages.push({ action: 'cross-organization-isolation', ui: 'React', state: 'Forbidden' });

  await page.screenshot({ path: path.join(input.evidence, 'react-teacher-journey.png'), fullPage: true });
  await writeFile(path.join(input.evidence, 'result.json'), JSON.stringify({
    passed: true,
    scope: 'Production React bundle over real Chromium, HTTPS API and disposable PostgreSQL',
    authentication: 'Pre-issued scoped access tokens supplied through the documented window.lmsAuth host adapter; browser login/refresh/logout not covered',
    fixture: 'Course, group and assignment are pre-seeded; they are not created by a user in the browser',
    stages,
    apiEvents: events,
  }, null, 2));
  commandInput.close();
  console.log('PASS');
} catch (error) {
  await writeFile(path.join(input.evidence, 'failure.json'), JSON.stringify({
    message: error instanceof Error ? error.message : String(error), events, stages,
  }, null, 2));
  throw error;
} finally {
  await browser.close();
}
