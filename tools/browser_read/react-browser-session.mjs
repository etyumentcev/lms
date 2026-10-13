import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import readline from 'node:readline';

const inputLines = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
const commands = inputLines[Symbol.asyncIterator]();
const first = await commands.next();
if (first.done) throw new Error('Missing session journey input');
const input = JSON.parse(first.value);
const { chromium } = await import(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
const browser = await chromium.launch({ executablePath: process.env.LMS_BROWSER_EXECUTABLE || '/opt/google/chrome/chrome', headless: true });
const events = [], stages = [];
await mkdir(input.evidence, { recursive: true });
async function control(command) {
  process.stdout.write(command + '\n');
  const response = await commands.next();
  if (response.done) throw new Error('Missing control response');
  JSON.parse(response.value || '{}');
}
function observe(page) {
  page.on('pageerror', error => events.push({ error: error.name, message: error.message }));
  page.on('response', response => {
    const url = new URL(response.url());
    if (url.pathname.startsWith('/api/')) events.push({ method: response.request().method(), path: url.pathname, status: response.status() });
  });
}
async function login(page) {
  await page.getByLabel('Логин').fill(input.userName);
  await page.getByLabel('Пароль', { exact: true }).fill(input.password);
  await page.getByLabel('Организация').fill(input.organizationA);
  await page.getByRole('button', { name: 'Войти' }).click();
  await page.getByRole('heading', { name: 'Мои курсы' }).waitFor();
  await page.getByText(input.courseA).waitFor();
}
try {
  const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
  const page = await context.newPage(); observe(page);
  await page.goto(`${input.origin}/courses`);
  await page.getByRole('heading', { name: 'Вход в Сапиентум' }).waitFor();
  await page.screenshot({ path: path.join(input.evidence, '01-login.png'), fullPage: true });

  await page.route('**/api/v1/auth/login', route => route.abort('failed'));
  await page.getByLabel('Логин').fill(input.userName);
  await page.getByLabel('Пароль', { exact: true }).fill(input.password);
  await page.getByLabel('Организация').fill(input.organizationA);
  await page.getByRole('button', { name: 'Войти' }).click();
  await page.getByRole('alert').waitFor();
  await page.screenshot({ path: path.join(input.evidence, '02-login-network-error.png'), fullPage: true });
  await page.unroute('**/api/v1/auth/login');
  await page.getByRole('button', { name: 'Войти' }).click();
  await page.getByRole('heading', { name: 'Мои курсы' }).waitFor();
  await page.getByText(input.courseA).waitFor();
  const storage = await page.evaluate(() => ({ local: { ...localStorage }, session: { ...sessionStorage } }));
  if (JSON.stringify(storage).includes('eyJ')) throw new Error('Access token reached browser storage');
  stages.push({ action: 'login', realReactForm: true, tokenStorage: 'memory-only', course: input.courseA });
  await page.screenshot({ path: path.join(input.evidence, '03-authenticated.png'), fullPage: true });

  await page.reload();
  await page.getByRole('heading', { name: 'Мои курсы' }).waitFor();
  await page.getByText(input.courseA).waitFor();
  stages.push({ action: 'reload-refresh', restored: true });

  const predecessor = (await context.cookies()).find(cookie => cookie.name === '__Host-LmsRefresh');
  if (!predecessor) throw new Error('Missing HttpOnly refresh cookie');
  const second = await context.newPage(); observe(second);
  await second.goto(`${input.origin}/courses`);
  await second.getByRole('heading', { name: 'Вход в Сапиентум' }).waitFor();
  await second.getByLabel('Организация').fill(input.organizationA);
  await second.getByRole('button', { name: 'Продолжить текущую сессию' }).click();
  await second.getByRole('heading', { name: 'Мои курсы' }).waitFor();
  await second.getByText(input.courseA).waitFor();
  stages.push({ action: 'two-tabs', sharedCookie: true, accessTokens: 'per-tab-memory' });

  const replayContext = await browser.newContext({ ignoreHTTPSErrors: true });
  await replayContext.addCookies([{ ...predecessor, sameSite: predecessor.sameSite }]);
  const replayPage = await replayContext.newPage();
  await replayPage.goto(`${input.origin}/courses`);
  const replay = await replayPage.evaluate(async organizationId => {
    const csrf = await fetch('/api/v1/auth/csrf', { credentials: 'same-origin', cache: 'no-store' }).then(r => r.json());
    const response = await fetch('/api/v1/auth/refresh', { method: 'POST', credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrf.token }, body: JSON.stringify({ organizationId }) });
    return response.status;
  }, input.organizationA);
  if (replay !== 409) throw new Error(`Refresh replay returned ${replay}`);
  await replayContext.close();
  stages.push({ action: 'refresh-replay', status: replay });

  const noCsrf = await page.evaluate(async organizationId => (await fetch('/api/v1/auth/switch-organization', {
    method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ organizationId })
  })).status, input.organizationA);
  if (noCsrf !== 403) throw new Error(`Missing CSRF returned ${noCsrf}`);
  const csrf = await page.evaluate(async () => (await fetch('/api/v1/auth/csrf', { credentials: 'same-origin' }).then(r => r.json())).token);
  const evil = await context.request.post(`${input.origin}/api/v1/auth/refresh`, { ignoreHTTPSErrors: true,
    headers: { Origin: 'https://evil.invalid', 'X-CSRF-TOKEN': csrf }, data: { organizationId: input.organizationA } });
  if (evil.status() !== 403) throw new Error(`Foreign Origin returned ${evil.status()}`);
  stages.push({ action: 'csrf-origin', missingCsrf: noCsrf, foreignOrigin: evil.status() });

  await control('expire');
  const expired = await page.evaluate(async () => {
    const token = await window.lmsAuth?.getAccessToken();
    return (await fetch('/api/v1/courses', { headers: token ? { Authorization: `Bearer ${token}` } : {} })).status;
  });
  if (expired !== 401) throw new Error(`Expired access token returned ${expired}`);
  await page.reload();
  await page.getByRole('heading', { name: 'Мои курсы' }).waitFor();
  await page.getByText(input.courseA).waitFor();
  stages.push({ action: 'access-expiry-refresh', expiredStatus: expired, recovered: true });

  await page.getByRole('complementary', { name: 'Управление сессией' }).getByText('Организация').click();
  await page.getByLabel('Идентификатор организации').fill(input.organizationB);
  await page.getByRole('button', { name: 'Переключить' }).click();
  await page.getByText(input.courseB).waitFor();
  if (await page.getByText(input.courseA).count()) throw new Error('Organization A course leaked after switch');
  stages.push({ action: 'switch-organization', isolated: true, course: input.courseB });

  await page.getByRole('complementary', { name: 'Управление сессией' }).getByText('Организация', { exact: true }).click();
  await page.getByLabel('Идентификатор организации').fill(input.foreignOrganization);
  await page.getByRole('button', { name: 'Переключить' }).click();
  await page.getByRole('heading', { name: 'Нет доступных курсов' }).waitFor();
  stages.push({ action: 'foreign-organization', deniedByNoScope: true });

  await page.getByRole('complementary', { name: 'Управление сессией' }).getByText('Организация', { exact: true }).click();
  await page.getByLabel('Идентификатор организации').fill(input.organizationA);
  await page.getByRole('button', { name: 'Переключить' }).click();
  await page.getByText(input.courseA).waitFor();
  await control('revoke-a');
  await page.getByRole('complementary', { name: 'Управление сессией' }).getByText('Организация', { exact: true }).click();
  await page.getByLabel('Идентификатор организации').fill(input.organizationA);
  // Force a fresh authority read even though the selected identifier is unchanged.
  await page.getByLabel('Идентификатор организации').fill(input.organizationB);
  await page.getByRole('button', { name: 'Переключить' }).click();
  await page.getByText(input.courseB).waitFor();
  await page.getByRole('complementary', { name: 'Управление сессией' }).getByText('Организация', { exact: true }).click();
  await page.getByLabel('Идентификатор организации').fill(input.organizationA);
  await page.getByRole('button', { name: 'Переключить' }).click();
  await page.getByRole('heading', { name: 'Нет доступных курсов' }).waitFor();
  stages.push({ action: 'revoked-authority', refreshIssuedNoScope: true });

  await page.getByRole('button', { name: 'Выйти' }).click();
  await page.getByRole('heading', { name: 'Вход в Сапиентум' }).waitFor();
  await second.getByRole('heading', { name: 'Вход в Сапиентум' }).waitFor();
  await page.screenshot({ path: path.join(input.evidence, '04-logged-out.png'), fullPage: true });
  stages.push({ action: 'logout', serverRequest: true, crossTab: true });

  const otherContext = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
  const other = await otherContext.newPage(); observe(other);
  await other.goto(`${input.origin}/courses`);
  await other.getByRole('heading', { name: 'Вход в Сапиентум' }).waitFor();
  await other.getByLabel('Логин').fill(input.secondUserName);
  await other.getByLabel('Пароль', { exact: true }).fill(input.secondPassword);
  await other.getByLabel('Организация').fill(input.secondOrganization);
  await other.getByRole('button', { name: 'Войти' }).click();
  await other.getByText(input.courseB).waitFor();
  if (await other.getByText(input.courseA).count()) throw new Error('Organization A course leaked to second user');
  await other.screenshot({ path: path.join(input.evidence, '05-second-user-authenticated.png'), fullPage: true });
  await other.getByRole('button', { name: 'Выйти' }).click();
  await other.getByRole('heading', { name: 'Вход в Сапиентум' }).waitFor();
  stages.push({ action: 'second-user-login-logout', realReactForm: true, independentBrowserContext: true, course: input.courseB });
  await otherContext.close();

  await writeFile(path.join(input.evidence, 'result.json'), JSON.stringify({ passed: true,
    scope: 'Production React login/session UI over HTTPS, restricted PostgreSQL runtime and real Chromium',
    preissuedTokens: false, credentialsTransport: 'React form to protected login endpoint', stages, apiEvents: events }, null, 2));
  inputLines.close();
  console.log('PASS');
} catch (error) {
  await writeFile(path.join(input.evidence, 'failure.json'), JSON.stringify({ message: error instanceof Error ? error.message : String(error), stages, apiEvents: events }, null, 2));
  throw error;
} finally { await browser.close(); }
