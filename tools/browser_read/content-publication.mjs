import { createRequire } from 'node:module';
import readline from 'node:readline';

const require = createRequire(import.meta.url);
const { chromium } = require(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
const line = await new Promise(resolve => {
  const input = readline.createInterface({ input: process.stdin, terminal: false });
  input.once('line', value => { input.close(); resolve(value); });
});
const data = JSON.parse(line);
const browser = await chromium.launch({ headless: true,
  ...(process.env.LMS_BROWSER_EXECUTABLE ? { executablePath: process.env.LMS_BROWSER_EXECUTABLE } : {}),
  args: ['--ignore-certificate-errors'] });
const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 1050 }, locale: 'ru-RU' });
const page = await context.newPage(); page.setDefaultTimeout(30000);
const failures = []; let expectedNetworkAbort = false;
page.on('console', message => { if (message.type() === 'error' && !(expectedNetworkAbort && message.text().includes('ERR_CONNECTION_FAILED'))) failures.push(`console:${message.text()}`); });
page.on('pageerror', error => failures.push(`page:${error.message}`));
page.on('response', response => {
  if (response.status() >= 400)
    failures.push(`http:${response.status()}:${new URL(response.url()).pathname}`);
});

async function login(userName) {
  await page.getByRole('heading', { name: 'Вход в Сапиентум' }).waitFor();
  await page.getByLabel('Логин').fill(userName);
  await page.getByLabel('Пароль', { exact: true }).fill(data.password);
  await page.getByLabel('Организация').fill(data.organizationId);
  await page.getByRole('button', { name: 'Войти' }).click();
  await page.getByRole('heading', { name: 'Мои курсы' }).waitFor();
}
async function logout() {
  await page.getByRole('button', { name: 'Выйти' }).click();
  await page.getByRole('heading', { name: 'Вход в Сапиентум' }).waitFor();
}
async function openMaterials() {
  await page.getByRole('heading', { name: data.courseTitle }).waitFor();
  await page.getByRole('button', { name: 'Материалы и публикация' }).click();
  await page.getByRole('heading', { name: data.courseTitle }).waitFor();
  await page.getByRole('heading', { name: 'Импорт ZIP' }).waitFor();
}
async function upload(path) {
  await page.getByTestId('content-zip').setInputFiles(path);
  await page.getByRole('button', { name: 'Импортировать ZIP' }).click();
  await page.getByTestId('import-diagnostics').waitFor();
}

try {
  await page.goto(data.origin, { waitUntil: 'networkidle' });
  await login(data.teacherUser); await openMaterials();

  await upload(data.invalidZip);
  await page.getByText('lessons/number-theory/problems/congruence-1.md:2:1', { exact: true }).waitFor();
  await page.screenshot({ path: `${data.evidence}/01-import-diagnostics-invalid.png`, fullPage: true });

  await upload(data.validZip);
  await page.getByText('Пакет готов к публикации.', { exact: true }).waitFor();
  await page.getByTestId('content-preview').waitFor();
  await page.screenshot({ path: `${data.evidence}/02-import-ready.png`, fullPage: true });
  const studentPanel = page.getByTestId('student-preview');
  await studentPanel.getByText('Остатки и сравнения', { exact: true }).waitFor();
  if (await studentPanel.getByText(/M1_TEACHER_ONLY/).count()) throw new Error('teacher material rendered in student preview');
  await page.screenshot({ path: `${data.evidence}/03-preview-student.png`, fullPage: true });

  await page.getByRole('tab', { name: 'Преподаватель' }).click();
  await page.getByTestId('teacher-preview').getByText(/M1_TEACHER_ONLY_SOLUTION_v1/).first().waitFor();
  await page.screenshot({ path: `${data.evidence}/04-preview-teacher.png`, fullPage: true });
  await page.getByTestId('publish-content').click();
  await page.getByText('Опубликована неизменяемая версия v1.', { exact: true }).waitFor();
  await page.getByRole('button', { name: 'Открыть и назначить группе' }).click();
  await page.getByText(/Занятие №1 открыто группе G1; назначено 5 заданий/).waitFor();
  await page.screenshot({ path: `${data.evidence}/05-published-v1-assigned-g1.png`, fullPage: true });

  await upload(data.versionTwoZip);
  await page.getByTestId('publish-content').click();
  await page.getByText('Опубликована неизменяемая версия v2.', { exact: true }).waitFor();
  await page.screenshot({ path: `${data.evidence}/06-published-v2.png`, fullPage: true });

  await logout(); await login(data.studentUser);
  await page.getByRole('button', { name: 'Открыть задания' }).click();
  await page.getByRole('heading', { name: 'Занятие №1' }).waitFor();
  await page.waitForFunction(() => document.querySelectorAll('.student-assignment-list li').length === 5);
  if (await page.locator('.student-assignment-list small').allTextContents().then(values => values.some(x => x !== 'Неизменяемая версия v1')))
    throw new Error('historical assignment no longer points to v1');
  const studentHtml = await page.content();
  for (const secret of ['M1_TEACHER_ONLY', 'teacherArtifactHash', 'packageHash'])
    if (studentHtml.includes(secret)) throw new Error(`student HTML leaked ${secret}`);
  await page.screenshot({ path: `${data.evidence}/07-student-v1-assignments-after-v2.png`, fullPage: true });

  await logout(); await login(data.teacherUser); await openMaterials();
  let intercepted = 0;
  expectedNetworkAbort = true;
  await page.route('**/api/v1/teacher/courses/*/imports', async route => { intercepted++; await route.abort('connectionfailed'); });
  await page.getByTestId('content-zip').setInputFiles(data.validZip);
  await page.getByRole('button', { name: 'Импортировать ZIP' }).click();
  await page.getByRole('alert').getByText(/Результат запроса неизвестен/).waitFor();
  await page.waitForTimeout(750);
  if (intercepted !== 1) throw new Error(`unknown-result request was replayed automatically (${intercepted})`);
  await page.unroute('**/api/v1/teacher/courses/*/imports');
  expectedNetworkAbort = false;

  if (failures.length) throw new Error(failures.join('\n'));
  process.stdout.write('PASS\n');
} catch (error) {
  const body = await page.locator('body').innerText().catch(() => '');
  process.stderr.write(`${String(error?.stack || error)}\n${failures.join('\n')}\n${body}\n`.replaceAll(data.password, '[REDACTED]'));
  process.stdout.write('FAIL\n'); process.exitCode = 1;
} finally {
  await Promise.race([browser.close(), new Promise(resolve => setTimeout(resolve, 5000))]);
  process.exit(process.exitCode ?? 0);
}
