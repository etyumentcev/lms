import { createRequire } from 'node:module';
import readline from 'node:readline';

const require = createRequire(import.meta.url);
const { chromium } = require(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
const line = await new Promise(resolve => {
  const input = readline.createInterface({ input: process.stdin, terminal: false });
  input.once('line', value => { input.close(); resolve(value); });
});
const data = JSON.parse(line);
const browser = await chromium.launch({
  headless: true,
  ...(process.env.LMS_BROWSER_EXECUTABLE ? { executablePath: process.env.LMS_BROWSER_EXECUTABLE } : {}),
  args: ['--ignore-certificate-errors']
});
const context = await browser.newContext({
  ignoreHTTPSErrors: true, viewport: { width: 1440, height: 1000 }, locale: 'ru-RU'
});
const page = await context.newPage();
page.setDefaultTimeout(30000);
const failures = [];
const ignoredNotFoundPaths = [];
page.on('console', message => {
  if (message.type() === 'error' && !message.text().includes('server responded with a status of 404'))
    failures.push(`console:${message.text()}`);
});
page.on('pageerror', error => failures.push(`page:${error.message}`));
page.on('response', response => {
  if (!response.url().includes('/api/')) return;
  const path = new URL(response.url()).pathname;
  if (response.status() === 404) ignoredNotFoundPaths.push(path);
  if (response.status() >= 500) failures.push(`http:${response.status()}:${path}`);
});

async function login(userName, password) {
  await page.getByRole('heading', { name: 'Вход в Сапиентум' }).waitFor();
  await page.getByLabel('Логин').fill(userName);
  await page.getByLabel('Пароль', { exact: true }).fill(password);
  await page.getByLabel('Организация').fill(data.organizationId);
  await page.getByRole('button', { name: 'Войти' }).click();
}
async function logout() {
  const myCourses = page.getByRole('link', { name: 'Мои курсы' }).first();
  if (await myCourses.count()) await myCourses.click();
  await page.getByRole('button', { name: 'Выйти' }).click();
  await page.getByRole('heading', { name: 'Вход в Сапиентум' }).waitFor();
}
async function protectedJson(path) {
  return page.evaluate(async resource => {
    const token = await window.lmsAuth?.getAccessToken();
    if (!token) throw new Error('missing browser access token');
    const response = await fetch(resource, {
      credentials: 'same-origin', cache: 'no-store',
      headers: { Authorization: `Bearer ${token}` }
    });
    if (!response.ok) throw new Error(`protected JSON returned ${response.status}`);
    return response.json();
  }, path);
}

try {
  await page.goto(data.origin, { waitUntil: 'networkidle' });
  await login(data.adminUser, data.adminPassword);
  await page.getByRole('heading', { name: 'Добро пожаловать, администратор!' }).waitFor();
  await page.getByRole('button', { name: /Создать курс/ }).click();
  await page.getByRole('heading', { name: 'Каталог курсов' }).waitFor();
  await page.getByRole('button', { name: new RegExp(data.courseTitle) }).click();
  await page.getByRole('heading', { name: data.courseTitle }).waitFor();
  await page.getByRole('button', { name: 'Назначить редактора курса' }).click();
  const dialog = page.getByRole('dialog', { name: 'Редактор курса' });
  await dialog.getByLabel('Редактор').selectOption({ label: data.teacherDisplayName });
  await dialog.getByRole('button', { name: 'Назначить редактора' }).click();
  await dialog.waitFor({ state: 'detached' });
  await page.getByRole('button', { name: 'Редакторы курса (1)' }).click();
  await page.getByText(data.teacherDisplayName, { exact: true }).waitFor();
  await page.screenshot({ path: `${data.evidence}/01-admin-course-editor-1440.png`, fullPage: true });
  await logout();

  await login(data.teacherUser, data.participantPassword);
  await page.getByRole('heading', { name: 'Мои курсы' }).waitFor();
  const teacherView = await protectedJson(`/api/v1/teacher/courses/${data.courseId}`);
  if (teacherView.groups.length !== 1 || teacherView.groups[0].groupId !== data.groupId)
    throw new Error(`teacher group isolation failed: ${JSON.stringify(teacherView.groups)}`);
  if (!teacherView.canEditContent) throw new Error('CourseEditor capability missing from teacher view');
  await page.getByRole('heading', { name: data.courseTitle }).waitFor();
  await page.getByRole('button', { name: 'Материалы и публикация' }).click();
  await page.getByRole('heading', { name: 'Импорт ZIP' }).waitFor();
  await page.getByTestId('content-zip').setInputFiles(data.validZip);
  await page.getByRole('button', { name: 'Импортировать ZIP' }).click();
  await page.getByText('Пакет готов к публикации.', { exact: true }).waitFor();
  await page.getByTestId('student-preview').getByText('Остатки и сравнения', { exact: true }).waitFor();
  await page.getByRole('tab', { name: 'Преподаватель' }).click();
  await page.getByTestId('teacher-preview').getByText(/M1_TEACHER_ONLY_SOLUTION_v1/).first().waitFor();
  await page.getByTestId('publish-content').click();
  await page.getByText('Опубликована неизменяемая версия v1.', { exact: true }).waitFor();
  await page.getByRole('button', { name: 'Открыть и назначить группе' }).click();
  await page.getByText(/Занятие №1 открыто группе G1; назначено 5 заданий/).waitFor();
  await page.screenshot({ path: `${data.evidence}/02-teacher-published-v1-g1-1440.png`, fullPage: true });
  await logout();

  await login(data.studentUser, data.participantPassword);
  await page.getByRole('heading', { name: 'Мои курсы' }).waitFor();
  await page.getByRole('button', { name: 'Открыть задания' }).click();
  await page.getByRole('heading', { name: data.courseTitle }).waitFor();
  await page.waitForFunction(() =>
    document.querySelector('.study-card-list li')?.textContent?.includes('5 задач · 0 принято · версия v1'));
  const html = await page.content();
  for (const secret of ['M1_TEACHER_ONLY', 'teacherArtifactHash', 'packageHash'])
    if (html.includes(secret)) throw new Error(`student HTML leaked ${secret}`);
  await page.screenshot({ path: `${data.evidence}/03-student-g1-five-problems-1440.png`, fullPage: true });

  if (failures.length) throw new Error(failures.join('\n'));
  process.stdout.write(JSON.stringify({
    status: 'PASS', editorAssigned: true, publishedVersion: 1,
    assignedProblems: 5, teacherGroups: teacherView.groups.map(group => group.groupId),
    ignoredNotFoundPaths: [...new Set(ignoredNotFoundPaths)]
  }) + '\n');
} catch (error) {
  const body = await page.locator('body').innerText().catch(() => '');
  let diagnostic = String(error?.stack || error) + '\n' + failures.join('\n') + '\n' + body;
  for (const secret of [data.adminPassword, data.participantPassword])
    diagnostic = diagnostic.replaceAll(secret, '[REDACTED]');
  process.stderr.write(diagnostic + '\n');
  process.stdout.write(JSON.stringify({ status: 'FAIL' }) + '\n');
  process.exitCode = 1;
} finally {
  await Promise.race([browser.close(), new Promise(resolve => setTimeout(resolve, 5000))]);
  process.exit(process.exitCode ?? 0);
}
