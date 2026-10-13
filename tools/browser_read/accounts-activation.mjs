import { createRequire } from 'node:module';
import readline from 'node:readline';

const require = createRequire(import.meta.url);
const { chromium } = require(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
const line = await new Promise(resolve => {
  const input = readline.createInterface({ input: process.stdin, terminal: false });
  input.once('line', value => { input.close(); resolve(value); });
});
const { origin, platformToken, organizationName, adminUserName, adminPassword,
  studentUserName, studentPassword, evidence } = JSON.parse(line);
const browser = await chromium.launch({ headless: true,
  ...(process.env.LMS_BROWSER_EXECUTABLE ? { executablePath: process.env.LMS_BROWSER_EXECUTABLE } : {}),
  args: ['--ignore-certificate-errors'] });
const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 960 }, locale: 'ru-RU' });
const page = await context.newPage(); page.setDefaultTimeout(20000);
const failures = [];
page.on('console', message => { if (message.type() === 'error') failures.push(`console:${message.text()}`); });
page.on('pageerror', error => failures.push(`page:${error.message}`));
page.on('response', response => { if (response.url().includes('/api/') && response.status() >= 400) failures.push(`http:${response.status()}:${new URL(response.url()).pathname}`); });

try {
  await page.goto(origin, { waitUntil: 'networkidle' });
  await page.getByRole('button', { name: 'Первый запуск' }).click();
  await page.getByLabel('Название организации').fill(organizationName);
  await page.getByLabel('Логин первого администратора').fill(adminUserName);
  await page.getByLabel('Токен системного администратора').fill(platformToken);
  await page.getByRole('button', { name: 'Создать организацию' }).click();
  await page.getByRole('heading', { name: 'Организация создана' }).waitFor();
  const organizationId = (await page.locator('.bootstrap-result dd').first().textContent()).trim();
  await page.getByRole('button', { name: 'Активировать администратора' }).click();
  await page.getByLabel('Новый пароль').fill(adminPassword);
  await page.getByRole('button', { name: 'Активировать', exact: true }).click();
  await page.getByRole('button', { name: 'Перейти ко входу' }).click();
  await page.getByLabel('Логин').fill(adminUserName);
  await page.getByLabel('Пароль', { exact: true }).fill(adminPassword);
  await page.getByLabel('Организация').fill(organizationId);
  await page.getByRole('button', { name: 'Войти' }).click();
  await page.getByRole('heading', { name: 'Добро пожаловать, администратор!' }).waitFor();

  await page.getByRole('button', { name: /Создать пользователя/ }).click();
  await page.getByRole('heading', { name: 'Учётные записи' }).waitFor();
  await page.screenshot({ path: `${evidence}/01-accounts-empty.png`, fullPage: true });
  await page.getByRole('button', { name: '+ Создать пользователя' }).click();
  await page.getByLabel('ФИО / отображаемое имя').fill('Синтетический Ученик');
  await page.getByLabel('Логин').fill(studentUserName);
  await page.getByLabel('Идентификатор roster').fill('browser-student-001');
  await page.screenshot({ path: `${evidence}/02-create-student.png`, fullPage: true });
  await page.getByRole('button', { name: 'Создать и выдать код' }).click();
  await page.getByRole('heading', { name: 'Одноразовый код активации' }).waitFor();
  const activationCode = (await page.locator('.secret-dialog .activation-secret strong').textContent()).trim();
  if (!/^[0-9A-HJKMNP-TV-Z]{4}(-[0-9A-HJKMNP-TV-Z]{4}){3}$/.test(activationCode)) throw new Error('student activation code mismatch');
  await page.screenshot({ path: `${evidence}/03-student-code.png`, fullPage: true });
  await page.getByRole('button', { name: 'Я сохранил результат' }).click();
  await page.getByText('Синтетический Ученик', { exact: true }).waitFor();
  await page.screenshot({ path: `${evidence}/04-accounts-list.png`, fullPage: true });

  await page.getByRole('button', { name: 'Выйти' }).click();
  await page.getByRole('heading', { name: 'Вход в Сапиентум' }).waitFor();
  await page.getByRole('button', { name: 'У меня есть код активации' }).click();
  await page.getByLabel('Организация').fill(organizationId);
  await page.getByLabel('Логин').fill(studentUserName);
  await page.getByLabel('Одноразовый код').fill(activationCode);
  await page.getByLabel('Новый пароль').fill(studentPassword);
  await page.getByLabel('Повторите пароль').fill(studentPassword);
  await page.screenshot({ path: `${evidence}/05-student-activation.png`, fullPage: true });
  await page.getByRole('button', { name: 'Продолжить' }).click();
  await page.getByRole('heading', { name: 'Пароль установлен' }).waitFor();
  await page.getByRole('button', { name: 'Перейти ко входу' }).click();
  await page.getByLabel('Логин').fill(studentUserName);
  await page.getByLabel('Пароль', { exact: true }).fill(studentPassword);
  await page.getByLabel('Организация').fill(organizationId);
  await page.getByRole('button', { name: 'Войти' }).click();
  await page.getByRole('heading', { name: 'Нет доступных курсов' }).waitFor();
  await page.screenshot({ path: `${evidence}/06-first-student-login.png`, fullPage: true });
  if (failures.length) throw new Error(failures.join('\n'));
  process.stdout.write('PASS\n');
} catch (error) {
  const pageText = await page.locator('body').innerText().catch(() => '');
  process.stderr.write((String(error?.stack || error) + '\n' + failures.join('\n') + '\n' + pageText)
    .replaceAll(platformToken, '[REDACTED]').replaceAll(adminPassword, '[REDACTED]').replaceAll(studentPassword, '[REDACTED]') + '\n');
  process.stdout.write('FAIL\n'); process.exitCode = 1;
} finally {
  await Promise.race([browser.close(), new Promise(resolve => setTimeout(resolve, 5000))]);
  process.exit(process.exitCode ?? 0);
}
