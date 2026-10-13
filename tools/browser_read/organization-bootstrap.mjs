import { createRequire } from 'node:module';
import readline from 'node:readline';

const require = createRequire(import.meta.url);
const { chromium } = require(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
const line = await new Promise(resolve => {
  const input = readline.createInterface({ input: process.stdin, terminal: false });
  input.once('line', value => { input.close(); resolve(value); });
});
const { origin, platformToken, organizationName, adminUserName, password, evidence } = JSON.parse(line);
const browser = await chromium.launch({ headless: true,
  ...(process.env.LMS_BROWSER_EXECUTABLE ? { executablePath: process.env.LMS_BROWSER_EXECUTABLE } : {}),
  args: ['--ignore-certificate-errors'] });
const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 960 }, locale: 'ru-RU' });
const page = await context.newPage();
page.setDefaultTimeout(15000);
const failures = [];
page.on('console', message => { if (message.type() === 'error') failures.push(`console:${message.text()}`); });
page.on('pageerror', error => failures.push(`page:${error.message}`));
page.on('response', response => {
  if (response.url().includes('/api/') && response.status() >= 400)
    failures.push(`http:${response.status()}:${new URL(response.url()).pathname}`);
});

try {
  await page.goto(origin, { waitUntil: 'networkidle' });
  await page.getByRole('button', { name: 'Первый запуск организации' }).click();
  await page.getByLabel('Название организации').fill(organizationName);
  await page.getByLabel('Логин первого администратора').fill(adminUserName);
  await page.getByLabel('Токен системного администратора').fill(platformToken);
  await page.screenshot({ path: `${evidence}/01-create-organization.png`, fullPage: true });
  await page.getByRole('button', { name: 'Создать организацию' }).click();
  await page.getByRole('heading', { name: 'Организация создана' }).waitFor();
  const organizationId = (await page.locator('.bootstrap-result dd').first().textContent()).trim();
  const code = (await page.locator('.activation-secret strong').textContent()).trim();
  if (!/^[0-9A-HJKMNP-TV-Z]{4}(-[0-9A-HJKMNP-TV-Z]{4}){3}$/.test(code)) throw new Error('activation code profile mismatch');
  await page.screenshot({ path: `${evidence}/02-one-time-code.png`, fullPage: true });

  await page.getByRole('button', { name: 'Активировать администратора' }).click();
  await page.getByLabel('Новый пароль').fill(password);
  await page.screenshot({ path: `${evidence}/03-activate-admin.png`, fullPage: true });
  await page.getByRole('button', { name: 'Активировать', exact: true }).click();
  await page.getByRole('heading', { name: 'Доступ активирован' }).waitFor();
  await page.getByRole('button', { name: 'Перейти ко входу' }).click();
  await page.getByLabel('Логин').fill(adminUserName);
  await page.getByLabel('Пароль', { exact: true }).fill(password);
  await page.getByLabel('Организация').fill(organizationId);
  await page.getByRole('button', { name: 'Войти' }).click();
  await page.getByRole('heading', { name: 'Добро пожаловать, администратор!' }).waitFor();
  await page.getByText(organizationName, { exact: true }).waitFor();
  await page.getByText('Активирован первый администратор', { exact: true }).waitFor();
  await page.screenshot({ path: `${evidence}/04-admin-dashboard.png`, fullPage: true });
  if (await page.getByRole('button', { name: /Создать пользователя/ }).count() < 1) throw new Error('accounts action absent');
  if (failures.length) throw new Error(failures.join('\n'));
  process.stdout.write('PASS\n');
} catch (error) {
  const pageText = await page.locator('body').innerText().catch(() => '');
  process.stderr.write((String(error?.stack || error) + '\n' + failures.join('\n') + '\n' + pageText)
    .replaceAll(platformToken, '[REDACTED]').replaceAll(password, '[REDACTED]') + '\n');
  process.stdout.write('FAIL\n');
  process.exitCode = 1;
} finally {
  await Promise.race([browser.close(), new Promise(resolve => setTimeout(resolve, 5000))]);
  process.exit(process.exitCode ?? 0);
}
