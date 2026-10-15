import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import readline from 'node:readline';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);

const commandInput = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
const commands = commandInput[Symbol.asyncIterator]();
const first = await commands.next();
if (first.done) throw new Error('Missing journey input');
const input = JSON.parse(first.value);
const { chromium } = require(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
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
  const page = await context.newPage();
  page.setDefaultTimeout(30000);
  page.on('pageerror', error => events.push({ error: error.name, message: error.message }));
  page.on('requestfailed', request => events.push({ method: request.method(), path: new URL(request.url()).pathname,
    failure: request.failure()?.errorText }));
  page.on('response', response => {
    const url = new URL(response.url());
    if (url.pathname.startsWith('/api/')) events.push({ method: response.request().method(), path: url.pathname, status: response.status() });
  });

  const photo = { name: 'real-solution.png', mimeType: 'image/png', buffer: Buffer.from(input.pngBase64, 'base64') };
  const teacherPhoto = { name: 'real-remark.png', mimeType: 'image/png', buffer: Buffer.from(input.pngBase64, 'base64') };
  const studentUrl = `${input.origin}/problem-assignments/${input.assignmentId}`;
  const teacherUrl = `${input.origin}/teacher/reviews/${input.workId}`;
  async function login(userName, password, organizationId) {
    await page.goto(`${input.origin}/courses`);
    await page.getByRole('heading', { name: 'Вход в Сапиентум' }).waitFor();
    await page.getByLabel('Логин').fill(userName);
    await page.getByLabel('Пароль', { exact: true }).fill(password);
    await page.getByLabel('Организация').fill(organizationId);
    await page.getByRole('button', { name: 'Войти' }).click();
    await page.getByRole('button', { name: 'Выйти' }).waitFor();
  }
  async function logout() {
    await page.getByRole('button', { name: 'Выйти' }).click();
    await page.getByRole('heading', { name: 'Вход в Сапиентум' }).waitFor();
  }

  await login(input.studentUserName, input.studentPassword, input.organizationId);
  await page.goto(studentUrl);
  await page.getByRole('heading', { name: 'Journey problem' }).waitFor();
  await page.locator('section[aria-label="Фотографии решения"] input[type=file]').setInputFiles(photo);
  await page.getByText('Фото загружено').waitFor();
  await page.getByRole('textbox', { name: 'Пояснение к решению — необязательно' }).fill('First answer from the real React UI');
  await page.getByRole('button', { name: 'Отправить решение' }).click();
  await page.getByText('Решение отправлено и ожидает проверки преподавателем.').waitFor();
  await page.getByLabel('Статус: На проверке').first().waitFor();
  stages.push({ action: 'student-submit', ui: 'React', photo: true, state: 'Submitted' });

  await logout();
  await login(input.teacherUserName, input.teacherPassword, input.organizationId);
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

  await logout();
  await login(input.studentUserName, input.studentPassword, input.organizationId);
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

  await logout();
  await login(input.teacherUserName, input.teacherPassword, input.organizationId);
  await page.goto(teacherUrl);
  await page.getByRole('heading', { name: 'Journey problem' }).waitFor();
  await page.getByRole('button', { name: 'Принять решение' }).waitFor();
  await page.getByRole('textbox', { name: 'Комментарий преподавателя' }).fill('Accepted after the correction.');
  await page.getByRole('button', { name: 'Принять решение' }).click();
  await page.getByLabel('Статус: Принята').first().waitFor();
  await page.getByText('Решение принято. Работа доступна только для чтения.').waitFor();
  if (await page.getByRole('button', { name: 'Принять решение' }).count()) throw new Error('Accepted command remained repeatable in UI');
  stages.push({ action: 'teacher-accept', ui: 'React', state: 'Accepted', repeatedCommandButton: false });

  await logout();
  await login(input.studentUserName, input.studentPassword, input.organizationId);
  await page.goto(studentUrl);
  await page.getByLabel('Статус: Принята').first().waitFor();
  stages.push({ action: 'student-observes-accepted', ui: 'React' });

  await control('revoke');
  await logout();
  await login(input.teacherUserName, input.teacherPassword, input.organizationId);
  await page.goto(teacherUrl);
  await page.getByRole('heading', { name: /Нет доступных курсов|Нет учебных полномочий|Нет доступа к работе/ }).waitFor();
  stages.push({ action: 'teacher-revoked', ui: 'React', state: 'Forbidden',
    publicSession: 'fresh login issued without revoked educational scope' });

  await logout();
  await login(input.outsiderUserName, input.outsiderPassword, input.outsiderOrganizationId);
  await page.goto(teacherUrl);
  await page.getByRole('heading', { name: 'Нет доступа к работе' }).waitFor();
  stages.push({ action: 'cross-organization-isolation', ui: 'React', state: 'Forbidden' });

  await page.screenshot({ path: path.join(input.evidence, 'react-teacher-journey.png'), fullPage: true });
  await writeFile(path.join(input.evidence, 'result.json'), JSON.stringify({
    passed: true,
    scope: 'Production React bundle over real Chromium, HTTPS API and disposable PostgreSQL',
    authentication: 'Production React login/logout forms for every actor; no pre-issued browser token',
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
