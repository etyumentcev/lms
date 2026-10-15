import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import readline from 'node:readline';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);

const inputLines = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
const commands = inputLines[Symbol.asyncIterator]();
const first = await commands.next();
if (first.done) throw new Error('Missing session journey input');
const input = JSON.parse(first.value);
const { chromium } = require(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
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
  await page.getByRole('button', { name: 'Войти' }).click();
  await page.getByRole('heading', { name: 'Выберите организацию' }).waitFor();
  await page.getByRole('button', { name: input.organizationNameA }).click();
  await page.getByRole('heading', { name: 'Мои курсы' }).waitFor();
  await page.getByRole('heading', { name: input.courseA, exact: true }).waitFor();
}
try {
  const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
  const page = await context.newPage(); observe(page);
  await page.goto(`${input.origin}/courses`);
  await page.getByRole('heading', { name: 'Вход' }).waitFor();
  await page.screenshot({ path: path.join(input.evidence, '01-login.png'), fullPage: true });

  await page.route('**/api/v1/auth/login', route => route.abort('failed'));
  await page.getByLabel('Логин').fill(input.userName);
  await page.getByLabel('Пароль', { exact: true }).fill(input.password);
  await page.getByRole('button', { name: 'Войти' }).click();
  await page.getByRole('alert').waitFor();
  await page.screenshot({ path: path.join(input.evidence, '02-login-network-error.png'), fullPage: true });
  await page.unroute('**/api/v1/auth/login');
  await page.getByLabel('Пароль', { exact: true }).fill(input.password + '-invalid');
  const deniedLogin = page.waitForResponse(response => new URL(response.url()).pathname === '/api/v1/auth/login' && response.status() === 401);
  await page.getByRole('button', { name: 'Войти' }).click();
  await deniedLogin;
  await page.getByRole('alert').waitFor();
  const deniedText = await page.locator('body').innerText();
  if ([input.organizationNameA, input.organizationNameB, input.organizationA, input.organizationB].some(value => deniedText.includes(value)))
    throw new Error('Invalid credentials leaked organization data');
  if ((await context.cookies()).some(cookie => cookie.name === '__Host-LmsRefresh'))
    throw new Error('Invalid credentials issued a refresh cookie');
  stages.push({ action: 'invalid-credentials', status: 401, organizationDisclosure: false, refreshCookie: false });
  await page.getByLabel('Пароль', { exact: true }).fill(input.password);
  await page.getByRole('button', { name: 'Войти' }).click();
  await page.getByRole('heading', { name: 'Выберите организацию' }).waitFor();
  await page.getByRole('button', { name: input.organizationNameA }).waitFor();
  await page.getByRole('button', { name: input.organizationNameB }).waitFor();
  if (await page.getByText(input.organizationA).count() || await page.getByText(input.organizationB).count())
    throw new Error('Organization GUID leaked into selection UI');
  await page.screenshot({ path: path.join(input.evidence, '03-organization-selection.png'), fullPage: true });
  await page.getByRole('button', { name: input.organizationNameA }).click();
  await page.getByRole('heading', { name: 'Мои курсы' }).waitFor();
  await page.getByRole('heading', { name: input.courseA, exact: true }).waitFor();
  const storage = await page.evaluate(() => ({ local: { ...localStorage }, session: { ...sessionStorage } }));
  if (JSON.stringify(storage).includes('eyJ')) throw new Error('Access token reached browser storage');
  stages.push({ action: 'login', realReactForm: true, tokenStorage: 'memory-only', course: input.courseA });
  await page.screenshot({ path: path.join(input.evidence, '03-authenticated.png'), fullPage: true });

  await page.reload();
  await page.getByRole('heading', { name: 'Мои курсы' }).waitFor();
  await page.getByRole('heading', { name: input.courseA, exact: true }).waitFor();
  stages.push({ action: 'reload-refresh', restored: true });

  const predecessor = (await context.cookies()).find(cookie => cookie.name === '__Host-LmsRefresh');
  if (!predecessor) throw new Error('Missing HttpOnly refresh cookie');
  const second = await context.newPage(); observe(second);
  await second.goto(`${input.origin}/courses`);
  await second.getByRole('heading', { name: 'Мои курсы' }).waitFor();
  await second.getByRole('heading', { name: input.courseA, exact: true }).waitFor();
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
    return (await fetch('/api/v1/my/courses', { headers: token ? { Authorization: `Bearer ${token}` } : {} })).status;
  });
  if (expired !== 401) throw new Error(`Expired access token returned ${expired}`);
  await page.reload();
  await page.getByRole('heading', { name: 'Мои курсы' }).waitFor();
  await page.getByRole('heading', { name: input.courseA, exact: true }).waitFor();
  stages.push({ action: 'access-expiry-refresh', expiredStatus: expired, recovered: true });

  let toolbar = page.getByRole('complementary', { name: 'Управление сессией' });
  await toolbar.getByText('Сменить организацию').click();
  await toolbar.getByRole('button', { name: input.organizationNameB }).click();
  await page.getByRole('heading', { name: input.courseB, exact: true }).waitFor();
  if (await page.getByRole('heading', { name: input.courseA, exact: true }).count()) throw new Error('Organization A course leaked after switch');
  stages.push({ action: 'switch-organization', isolated: true, course: input.courseB });

  const forged = await page.evaluate(async ({ organizationId, token }) => {
    const response = await fetch('/api/v1/auth/switch-organization', { method: 'POST', credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token }, body: JSON.stringify({ organizationId }) });
    return { status: response.status, body: await response.json() };
  }, { organizationId: input.foreignOrganization, token: csrf });
  if (forged.status !== 200 || forged.body.accessToken !== null || forged.body.organizationId !== null || !forged.body.selectionRequired)
    throw new Error('Forged organization selector was accepted as authority');
  stages.push({ action: 'foreign-organization', selectorOnly: true, educationalToken: false });

  toolbar = page.getByRole('complementary', { name: 'Управление сессией' });
  await toolbar.getByText('Сменить организацию').click();
  await toolbar.getByRole('button', { name: input.organizationNameA }).click();
  await page.getByRole('heading', { name: input.courseA, exact: true }).waitFor();
  await control('revoke-a');
  toolbar = page.getByRole('complementary', { name: 'Управление сессией' });
  await toolbar.getByText('Сменить организацию').click();
  await toolbar.getByRole('button', { name: input.organizationNameB }).click();
  await page.getByRole('heading', { name: input.courseB, exact: true }).waitFor();
  if (await page.getByRole('button', { name: input.organizationNameA }).count())
    throw new Error('Revoked organization remained selectable');
  const revoked = await page.evaluate(async ({ organizationId, token }) => {
    const response = await fetch('/api/v1/auth/switch-organization', { method: 'POST', credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token }, body: JSON.stringify({ organizationId }) });
    return { status: response.status, body: await response.json() };
  }, { organizationId: input.organizationA, token: csrf });
  if (revoked.status !== 200 || revoked.body.organizationId !== input.organizationB)
    throw new Error('Revoked organization selector escaped server revalidation');
  stages.push({ action: 'revoked-authority', removedFromOptions: true, selectedOrganization: input.organizationNameB });

  await page.getByRole('button', { name: 'Выйти' }).click();
  await page.getByRole('heading', { name: 'Вход' }).waitFor();
  await second.getByRole('heading', { name: 'Вход' }).waitFor();
  await page.screenshot({ path: path.join(input.evidence, '04-logged-out.png'), fullPage: true });
  stages.push({ action: 'logout', serverRequest: true, crossTab: true });

  const otherContext = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
  const other = await otherContext.newPage(); observe(other);
  await other.goto(`${input.origin}/courses`);
  await other.getByRole('heading', { name: 'Вход' }).waitFor();
  await other.getByLabel('Логин').fill(input.secondUserName);
  await other.getByLabel('Пароль', { exact: true }).fill(input.secondPassword);
  await other.getByRole('button', { name: 'Войти' }).click();
  await other.getByRole('heading', { name: input.courseB, exact: true }).waitFor();
  if (await other.getByRole('heading', { name: input.courseA, exact: true }).count()) throw new Error('Organization A course leaked to second user');
  await other.screenshot({ path: path.join(input.evidence, '05-second-user-authenticated.png'), fullPage: true });
  await other.getByRole('button', { name: 'Выйти' }).click();
  await other.getByRole('heading', { name: 'Вход' }).waitFor();
  stages.push({ action: 'second-user-login-logout', realReactForm: true, independentBrowserContext: true, singleOrganizationAutoSelected: true, course: input.courseB });
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
