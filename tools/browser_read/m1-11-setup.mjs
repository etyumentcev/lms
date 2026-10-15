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
page.on('console', message => { if (message.type() === 'error') failures.push(`console:${message.text()}`); });
page.on('pageerror', error => failures.push(`page:${error.message}`));
page.on('response', response => {
  if (response.url().includes('/api/') && response.status() >= 500)
    failures.push(`http:${response.status()}:${new URL(response.url()).pathname}`);
});
page.on('dialog', dialog => void dialog.accept());

async function login(target, userName, password, organizationId) {
  await target.getByRole('heading', { name: 'Вход в Сапиентум' }).waitFor();
  await target.getByLabel('Логин').fill(userName);
  await target.getByLabel('Пароль', { exact: true }).fill(password);
  await target.getByLabel('Организация').fill(organizationId);
  await target.getByRole('button', { name: 'Войти' }).click();
}

async function logout(target) {
  await target.getByRole('button', { name: 'Выйти' }).click();
  await target.getByRole('heading', { name: 'Вход в Сапиентум' }).waitFor();
}

async function bootstrap(organizationName, adminUser, screenshotPrefix) {
  await page.goto(data.origin, { waitUntil: 'networkidle' });
  await page.getByRole('button', { name: /Первый запуск/ }).click();
  await page.getByLabel('Название организации').fill(organizationName);
  await page.getByLabel('Логин первого администратора').fill(adminUser);
  await page.getByLabel('Токен системного администратора').fill(data.platformToken);
  await page.getByRole('button', { name: 'Создать организацию' }).click();
  await page.getByRole('heading', { name: 'Организация создана' }).waitFor();
  const organizationId = (await page.locator('.bootstrap-result dd').first().textContent()).trim();
  const code = (await page.locator('.activation-secret strong').textContent()).trim();
  if (!/^[0-9A-HJKMNP-TV-Z]{4}(-[0-9A-HJKMNP-TV-Z]{4}){3}$/.test(code))
    throw new Error('first-admin activation code profile mismatch');
  if (screenshotPrefix) await page.screenshot({ path: `${data.screenshots}/${screenshotPrefix}-bootstrap-1440.png`, fullPage: true });
  await page.getByRole('button', { name: 'Активировать администратора' }).click();
  await page.getByLabel('Новый пароль').fill(data.adminPassword);
  await page.getByRole('button', { name: 'Активировать', exact: true }).click();
  await page.getByRole('button', { name: 'Перейти ко входу' }).click();
  await login(page, adminUser, data.adminPassword, organizationId);
  await page.getByRole('heading', { name: 'Добро пожаловать, администратор!' }).waitFor();
  await page.getByText(organizationName, { exact: true }).waitFor();
  if (screenshotPrefix) await page.screenshot({ path: `${data.screenshots}/${screenshotPrefix}-admin-1440.png`, fullPage: true });
  await logout(page);
  return organizationId;
}

async function activateParticipant(participant, code, organizationId, screenshotName) {
  const participantContext = await browser.newContext({
    ignoreHTTPSErrors: true, viewport: { width: 390, height: 844 }, locale: 'ru-RU'
  });
  const participantPage = await participantContext.newPage();
  participantPage.setDefaultTimeout(30000);
  try {
    await participantPage.goto(data.origin, { waitUntil: 'networkidle' });
    await participantPage.getByRole('button', { name: 'У меня есть код активации' }).click();
    await participantPage.getByRole('heading', { name: 'Активируйте аккаунт' }).waitFor();
    if (screenshotName)
      await participantPage.screenshot({ path: `${data.screenshots}/${screenshotName}`, fullPage: true });
    await participantPage.getByLabel('Организация').fill(organizationId);
    await participantPage.getByLabel('Логин').fill(participant.userName);
    await participantPage.getByLabel('Одноразовый код').fill(code);
    await participantPage.getByLabel('Новый пароль').fill(data.participantPassword);
    await participantPage.getByLabel('Повторите пароль').fill(data.participantPassword);
    await participantPage.getByRole('button', { name: 'Продолжить' }).click();
    await participantPage.getByRole('heading', { name: 'Пароль установлен' }).waitFor();
    if (screenshotName)
      await participantPage.screenshot({ path: `${data.screenshots}/${screenshotName.replace('.png', '-success.png')}`, fullPage: true });
    await participantPage.getByRole('button', { name: 'Перейти ко входу' }).click();
    await login(participantPage, participant.userName, data.participantPassword, organizationId);
    await participantPage.getByRole('heading', { name: 'Нет доступных курсов' }).waitFor();
  } finally {
    await participantContext.close();
  }
}

async function createParticipant(participant, organizationId, screenshotName) {
  await page.getByRole('button', { name: '+ Создать пользователя' }).click();
  const dialog = page.getByRole('dialog', { name: 'Создание пользователя' });
  if (participant.role === 'Teacher')
    await dialog.getByRole('button', { name: 'Преподаватель' }).click();
  await dialog.getByLabel('ФИО / отображаемое имя').fill(participant.displayName);
  await dialog.getByLabel('Логин').fill(participant.userName);
  await dialog.getByLabel('Идентификатор roster').fill(participant.rosterKey);
  await dialog.getByRole('button', { name: 'Создать и выдать код' }).click();
  await page.getByRole('heading', { name: 'Одноразовый код активации' }).waitFor();
  const code = (await page.locator('.secret-dialog .activation-secret strong').textContent()).trim();
  if (!/^[0-9A-HJKMNP-TV-Z]{4}(-[0-9A-HJKMNP-TV-Z]{4}){3}$/.test(code))
    throw new Error(`activation code profile mismatch for ${participant.userName}`);
  await page.getByRole('button', { name: 'Я сохранил результат' }).click();
  await page.getByText(participant.displayName, { exact: true }).waitFor();
  await activateParticipant(participant, code, organizationId, screenshotName);
}

try {
  const organizationId = await bootstrap(data.organizationName, data.adminUser, 'E01');
  const foreignOrganizationId = await bootstrap(data.foreignOrganizationName, data.foreignAdminUser, null);

  await page.goto(data.origin, { waitUntil: 'networkidle' });
  await login(page, data.adminUser, data.adminPassword, organizationId);
  await page.getByRole('heading', { name: 'Добро пожаловать, администратор!' }).waitFor();
  await page.getByRole('button', { name: /Создать пользователя/ }).click();
  await page.getByRole('heading', { name: 'Учётные записи' }).waitFor();
  for (let index = 0; index < data.participants.length; index++) {
    const participant = data.participants[index];
    const screenshotName = index === 0 ? 'E02-activation-390.png'
      : participant.userName === data.teacherUser ? 'E02-teacher-activation-390.png' : null;
    await createParticipant(participant, organizationId, screenshotName);
  }
  await page.getByText('Найдено: 14', { exact: true }).waitFor();
  await page.screenshot({ path: `${data.screenshots}/E02-accounts-1440.png`, fullPage: true });

  await page.goto(data.origin, { waitUntil: 'networkidle' });
  await page.getByRole('heading', { name: 'Добро пожаловать, администратор!' }).waitFor();
  await page.getByRole('button', { name: /Создать курс/ }).click();
  await page.getByRole('heading', { name: 'Каталог курсов' }).waitFor();
  await page.getByRole('button', { name: '+ Создать курс' }).click();
  await page.getByLabel('Название курса').fill(data.courseTitle);
  await page.getByLabel('Описание').fill('Синтетический курс M1-11');
  await page.getByRole('button', { name: 'Создать курс', exact: true }).click();
  await page.getByRole('heading', { name: data.courseTitle }).waitFor();
  for (const [name, description] of [['G1', 'Основная группа'], ['G2', 'Контрольная группа']]) {
    await page.getByRole('button', { name: '+ Добавить группу' }).click();
    const group = page.getByRole('dialog', { name: 'Новая группа' });
    await group.getByLabel('Название группы').fill(name);
    await group.getByLabel('Описание').fill(description);
    await group.getByRole('button', { name: 'Добавить группу' }).click();
    await group.waitFor({ state: 'detached' });
  }
  await page.getByRole('button', { name: '+ Зачислить учеников' }).click();
  const enrollment = page.getByRole('dialog', { name: 'Зачисление учеников' });
  const students = enrollment.locator('input[type=checkbox]');
  await page.waitForFunction(() => document.querySelectorAll('.picker-list input[type=checkbox]').length === 12);
  if (await students.count() !== 12) throw new Error(`expected 12 student checkboxes, got ${await students.count()}`);
  for (let index = 0; index < 12; index++) await students.nth(index).check();
  await enrollment.getByRole('button', { name: 'Добавить выбранных (12)' }).click();
  await enrollment.waitFor({ state: 'detached' });
  for (const [teacher, group] of [[data.teacherDisplayName, 'G1'], [data.unassignedTeacherDisplayName, 'G2']]) {
    await page.getByRole('button', { name: 'Назначить преподавателя' }).click();
    const assignment = page.getByRole('dialog', { name: 'Назначение преподавателя' });
    await assignment.getByLabel('Преподаватель').selectOption({ label: teacher });
    await assignment.getByLabel('Группа').selectOption({ label: group });
    await assignment.getByRole('button', { name: 'Назначить' }).click();
    await assignment.waitFor({ state: 'detached' });
  }

  await page.getByRole('button', { name: /Контекст обработки/ }).first().click();
  await page.getByRole('heading', { name: 'Контекст обработки' }).waitFor();
  await page.getByRole('button', { name: 'Создать синтетический контекст' }).click();
  await page.getByRole('status').filter({ hasText: 'Синтетический контекст обработки создан.' }).waitFor();
  await page.getByRole('button', { name: 'Привязать контекст' }).click();
  await page.getByRole('status').filter({
    hasText: new RegExp(`Курс «${data.courseTitle}» привязан к контексту v1`)
  }).waitFor();
  await page.getByText('Синтетический контекст · v1', { exact: true }).waitFor();
  await page.getByText('Привязан к v1', { exact: true }).waitFor();
  await page.screenshot({ path: `${data.screenshots}/E03-processing-context-1440.png`, fullPage: true });

  await page.getByRole('button', { name: /Курсы и группы/ }).click();
  await page.getByRole('heading', { name: 'Каталог курсов' }).waitFor();
  await page.getByRole('button', { name: new RegExp(data.courseTitle) }).click();
  await page.getByRole('button', { name: 'Назначить редактора курса' }).click();
  const editor = page.getByRole('dialog', { name: 'Редактор курса' });
  await editor.getByLabel('Редактор').selectOption({ label: data.teacherDisplayName });
  await editor.getByRole('button', { name: 'Назначить редактора' }).click();
  await editor.waitFor({ state: 'detached' });
  await page.getByRole('button', { name: 'Редакторы курса (1)' }).click();
  await page.getByText(data.teacherDisplayName, { exact: true }).waitFor();
  await page.getByRole('button', { name: 'Ученики (12)' }).click();
  await page.getByText('Ученики (12)', { exact: true }).waitFor();
  await page.screenshot({ path: `${data.screenshots}/E03-roster-1440.png`, fullPage: true });
  await logout(page);

  if (failures.length) throw new Error(failures.join('\n'));
  process.stdout.write(JSON.stringify({ status: 'PASS', organizationId, foreignOrganizationId }) + '\n');
} catch (error) {
  const body = await page.locator('body').innerText().catch(() => '');
  process.stderr.write((String(error?.stack || error) + '\n' + failures.join('\n') + '\n' + body)
    .replaceAll(data.platformToken, '[REDACTED]')
    .replaceAll(data.adminPassword, '[REDACTED]')
    .replaceAll(data.participantPassword, '[REDACTED]') + '\n');
  process.stdout.write(JSON.stringify({ status: 'FAIL' }) + '\n');
  process.exitCode = 1;
} finally {
  await Promise.race([browser.close(), new Promise(resolve => setTimeout(resolve, 5000))]);
  process.exit(process.exitCode ?? 0);
}
