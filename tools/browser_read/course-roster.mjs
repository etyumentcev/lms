import { createRequire } from 'node:module';
import readline from 'node:readline';

const require = createRequire(import.meta.url);
const { chromium } = require(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
const line = await new Promise(resolve => { const input = readline.createInterface({ input: process.stdin, terminal: false }); input.once('line', value => { input.close(); resolve(value); }); });
const data = JSON.parse(line);
const browser = await chromium.launch({ headless: true,
  ...(process.env.LMS_BROWSER_EXECUTABLE ? { executablePath: process.env.LMS_BROWSER_EXECUTABLE } : {}),
  args: ['--ignore-certificate-errors'] });
const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 1000 }, locale: 'ru-RU' });
const page = await context.newPage(); page.setDefaultTimeout(25000);
const failures = [];
page.on('console', message => { if (message.type() === 'error') failures.push(`console:${message.text()}`); });
page.on('pageerror', error => failures.push(`page:${error.message}`));
page.on('response', response => { if (response.url().includes('/api/') && response.status() >= 500) failures.push(`http:${response.status()}:${new URL(response.url()).pathname}`); });
page.on('dialog', dialog => void dialog.accept());

async function login(userName, password, organizationId) {
  await page.getByRole('heading', { name: 'Вход в Сапиентум' }).waitFor();
  await page.getByLabel('Логин').fill(userName);
  await page.getByLabel('Пароль', { exact: true }).fill(password);
  await page.getByLabel('Организация').fill(organizationId);
  await page.getByRole('button', { name: 'Войти' }).click();
}
async function logout() { await page.getByRole('button', { name: 'Выйти' }).click(); await page.getByRole('heading', { name: 'Вход в Сапиентум' }).waitFor(); }
async function openAdminCourse() {
  await page.getByRole('button', { name: /Курсы и группы/ }).click();
  await page.getByRole('heading', { name: 'Каталог курсов' }).waitFor();
  await page.getByRole('button', { name: new RegExp(data.courseTitle) }).click();
  await page.getByRole('heading', { name: data.courseTitle }).waitFor();
}

try {
  await page.goto(data.origin, { waitUntil: 'networkidle' });
  await login(data.adminUser, data.adminPassword, data.organizationId);
  await page.getByRole('heading', { name: 'Добро пожаловать, администратор!' }).waitFor();
  await page.getByRole('button', { name: /Создать курс/ }).click();
  await page.getByRole('heading', { name: 'Каталог курсов' }).waitFor();
  await page.screenshot({ path: `${data.evidence}/01-admin-course-catalog-empty.png`, fullPage: true });
  await page.getByRole('button', { name: '+ Создать курс' }).click();
  await page.getByLabel('Название курса').fill(data.courseTitle);
  await page.getByLabel('Описание').fill('Синтетический курс без опубликованных материалов');
  await page.getByRole('button', { name: 'Создать курс', exact: true }).click();
  await page.getByRole('heading', { name: data.courseTitle }).waitFor();

  for (const [name, description] of [['Эйлер — основная', 'Основная группа'], ['Эйлер — резерв', 'Группа для переводов']]) {
    await page.getByRole('button', { name: '+ Добавить группу' }).click();
    const dialog = page.getByRole('dialog', { name: 'Новая группа' });
    await dialog.getByLabel('Название группы').fill(name);
    await dialog.getByLabel('Описание').fill(description);
    await dialog.getByRole('button', { name: 'Добавить группу' }).click();
    await dialog.waitFor({ state: 'detached' });
  }
  await page.screenshot({ path: `${data.evidence}/02-admin-course-two-groups.png`, fullPage: true });

  await page.getByRole('button', { name: '+ Зачислить учеников' }).click();
  const enrollment = page.getByRole('dialog', { name: 'Зачисление учеников' });
  const checks = enrollment.locator('input[type=checkbox]');
  await page.waitForFunction(() => document.querySelectorAll('.picker-list input[type=checkbox]').length === 12);
  if (await checks.count() !== 12) throw new Error(`expected 12 student checkboxes, got ${await checks.count()}`);
  for (let index = 0; index < 12; index++) await checks.nth(index).check();
  await enrollment.getByRole('button', { name: 'Добавить выбранных (12)' }).click();
  await enrollment.waitFor({ state: 'detached' });
  await page.getByText('Ученики (12)', { exact: true }).waitFor();

  await page.getByRole('button', { name: 'Назначить преподавателя' }).click();
  const teacherDialog = page.getByRole('dialog', { name: 'Назначение преподавателя' });
  await page.waitForFunction(() => document.querySelectorAll('[aria-label="Назначение преподавателя"] select option').length >= 2);
  await teacherDialog.getByLabel('Преподаватель').selectOption({ label: data.teacherDisplayName });
  await teacherDialog.getByLabel('Группа').selectOption({ label: 'Эйлер — основная' });
  await teacherDialog.getByRole('button', { name: 'Назначить' }).click();
  await teacherDialog.waitFor({ state: 'detached' });
  await page.screenshot({ path: `${data.evidence}/03-admin-roster-12.png`, fullPage: true });

  await logout(); await login(data.teacherUser, data.participantPassword, data.organizationId);
  await page.getByRole('heading', { name: 'Мои курсы' }).waitFor();
  await page.getByRole('heading', { name: data.courseTitle }).waitFor();
  await page.screenshot({ path: `${data.evidence}/04-teacher-course-catalog.png`, fullPage: true });
  await page.getByRole('button', { name: 'Открыть группы и учеников' }).click();
  await page.getByRole('heading', { name: 'Ученики' }).waitFor();
  if (await page.locator('.learning-roster article').count() !== 12) throw new Error('teacher must see 12 assigned students');
  await page.screenshot({ path: `${data.evidence}/05-teacher-assigned-roster.png`, fullPage: true });

  await logout(); await login(data.studentUsers[0], data.participantPassword, data.organizationId);
  await page.getByRole('heading', { name: 'Мои курсы' }).waitFor();
  await page.getByRole('heading', { name: data.courseTitle }).waitFor();
  await page.screenshot({ path: `${data.evidence}/06-student-my-courses.png`, fullPage: true });

  const session = page.getByRole('complementary', { name: 'Управление сессией' });
  await session.getByText('Организация', { exact: true }).click();
  await page.getByLabel('Идентификатор организации').fill(data.foreignOrganizationId);
  await page.getByRole('button', { name: 'Переключить' }).click();
  await page.getByRole('heading', { name: 'Нет доступных курсов' }).waitFor();
  if (await page.getByText(data.courseTitle, { exact: true }).count()) throw new Error('course leaked after organization switch');
  await page.screenshot({ path: `${data.evidence}/06a-student-after-organization-switch.png`, fullPage: true });

  await logout(); await login(data.adminUser, data.adminPassword, data.organizationId);
  await page.getByRole('heading', { name: 'Добро пожаловать, администратор!' }).waitFor();
  await openAdminCourse();
  const firstStudent = data.studentDisplayNames[0];
  const firstRow = page.locator('.roster-row').filter({ hasText: firstStudent });
  await firstRow.getByLabel(`Новая группа для ${firstStudent}`).selectOption({ label: 'Эйлер — резерв' });
  await firstRow.getByRole('button', { name: 'Перевести', exact: true }).click();
  await page.locator('.roster-row').filter({ hasText: firstStudent }).locator(':scope > span').nth(1).filter({ hasText: 'Эйлер — резерв' }).waitFor();
  const secondStudent = data.studentDisplayNames[1];
  const secondRow = page.locator('.roster-row').filter({ hasText: secondStudent });
  await secondRow.getByRole('button', { name: 'Исключить' }).click();
  await page.getByText('Ученики (11)', { exact: true }).waitFor();
  await page.screenshot({ path: `${data.evidence}/07-admin-after-move-remove.png`, fullPage: true });

  await logout(); await login(data.studentUsers[1], data.participantPassword, data.organizationId);
  await page.getByRole('heading', { name: 'Нет доступных курсов' }).waitFor();
  await page.screenshot({ path: `${data.evidence}/08-removed-student-denied.png`, fullPage: true });

  await logout(); await login(data.teacherUser, data.participantPassword, data.organizationId);
  await page.getByRole('button', { name: 'Открыть группы и учеников' }).click();
  await page.waitForFunction(() => document.querySelectorAll('.learning-roster article').length === 10);
  if (await page.locator('.learning-roster article').count() !== 10) throw new Error('teacher must only see 10 students remaining in assigned group');

  await logout(); await login(data.unassignedTeacherUser, data.participantPassword, data.organizationId);
  await page.getByRole('heading', { name: 'Нет доступных курсов' }).waitFor();

  await logout(); await login(data.foreignAdminUser, data.adminPassword, data.foreignOrganizationId);
  await page.getByRole('heading', { name: 'Добро пожаловать, администратор!' }).waitFor();
  await page.getByRole('button', { name: /Курсы и группы/ }).click();
  await page.getByRole('heading', { name: 'Каталог курсов' }).waitFor();
  await page.getByText('Загружаем каталог…', { exact: true }).waitFor({ state: 'detached' });
  await page.getByText('Курсов 0', { exact: true }).waitFor();
  if (await page.getByText(data.courseTitle, { exact: true }).count()) throw new Error('foreign organization course leaked');
  await page.screenshot({ path: `${data.evidence}/09-foreign-organization-empty.png`, fullPage: true });

  if (failures.length) throw new Error(failures.join('\n'));
  process.stdout.write('PASS\n');
} catch (error) {
  const body = await page.locator('body').innerText().catch(() => '');
  process.stderr.write(`${String(error?.stack || error)}\n${failures.join('\n')}\n${body}\n`);
  process.stdout.write('FAIL\n'); process.exitCode = 1;
} finally {
  await Promise.race([browser.close(), new Promise(resolve => setTimeout(resolve, 5000))]);
  process.exit(process.exitCode ?? 0);
}
