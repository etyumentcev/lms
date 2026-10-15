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
async function openUserMenu(page) {
  const trigger = page.locator('.user-menu-trigger');
  await trigger.click();
  const logout = page.getByRole('menuitem', { name: 'Выйти' });
  await logout.waitFor();
  const [triggerBox, menuBox] = await Promise.all([trigger.boundingBox(), page.locator('.user-menu-popover').boundingBox()]);
  if (!triggerBox || !menuBox || menuBox.y < 0 || menuBox.y + menuBox.height > triggerBox.y ||
      menuBox.x < 0 || menuBox.x + menuBox.width > page.viewportSize().width)
    throw new Error('User menu is outside the viewport or overlaps its trigger');
  return logout;
}
async function logout(page) {
  await (await openUserMenu(page)).click();
  await page.getByRole('heading', { name: 'Вход' }).waitFor();
}
async function switchOrganization(page, organizationId) {
  return page.evaluate(async id => {
    const csrf = await fetch('/api/v1/auth/csrf', { credentials: 'same-origin', cache: 'no-store' }).then(response => response.json());
    const response = await fetch('/api/v1/auth/switch-organization', { method: 'POST', credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrf.token }, body: JSON.stringify({ organizationId: id }) });
    return { status: response.status, body: await response.json() };
  }, organizationId);
}
async function adoptOrganization(page, organizationId, organizationName, courseName) {
  await page.evaluate(id => localStorage.setItem('sapientum.organization', id), organizationId);
  await page.reload();
  await page.getByRole('link', { name: organizationName }).waitFor();
  await page.getByRole('heading', { name: courseName, exact: true }).waitFor();
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
  await page.getByRole('link', { name: input.organizationNameA }).waitFor();
  await page.getByRole('button', { name: new RegExp(input.displayName + '.*Ученик') }).waitFor();
  if (await page.locator('.sidebar-nav a[href="/"] .nav-icon svg').count() !== 1)
    throw new Error('Organization navigation does not use the home SVG icon');
  const nameMetrics = await page.locator('.user-menu-copy strong').evaluate(element => {
    const style = getComputedStyle(element);
    return { height: element.getBoundingClientRect().height, lineHeight: Number.parseFloat(style.lineHeight),
      scrollHeight: element.scrollHeight, clamp: style.webkitLineClamp };
  });
  if (nameMetrics.clamp !== '2' || nameMetrics.height < nameMetrics.lineHeight * 1.5 ||
      nameMetrics.height > nameMetrics.lineHeight * 2.1 || nameMetrics.scrollHeight <= nameMetrics.height)
    throw new Error('Long user name is not clamped after two visible lines');
  if (await page.getByRole('complementary', { name: 'Управление сессией' }).count())
    throw new Error('Former floating session toolbar is still visible');
  if (await page.getByText(input.organizationA, { exact: true }).count())
    throw new Error('Organization GUID leaked into authenticated UI');
  await openUserMenu(page);
  if (await page.getByText('Сменить организацию', { exact: true }).count())
    throw new Error('Organization switch leaked into user menu');
  if (!await page.getByRole('menuitem', { name: 'Выйти' }).evaluate(element => element === document.activeElement))
    throw new Error('User menu did not move keyboard focus to its action');
  await page.screenshot({ path: path.join(input.evidence, '03-authenticated-user-menu-open-1440.png'), fullPage: true });
  await page.keyboard.press('Escape');
  if (await page.locator('.user-menu-trigger').getAttribute('aria-expanded') !== 'false')
    throw new Error('Escape did not close user menu');
  await page.setViewportSize({ width: 390, height: 844 });
  await openUserMenu(page);
  const narrowOverflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth);
  if (narrowOverflow) throw new Error('Narrow user menu introduced horizontal scrolling');
  await page.screenshot({ path: path.join(input.evidence, '03-authenticated-user-menu-open-390.png'), fullPage: true });
  await page.keyboard.press('Escape');
  await page.setViewportSize({ width: 1440, height: 900 });
  stages.push({ action: 'user-menu', desktop: true, narrow: true, outsideViewport: false,
    overlapsTrigger: false, horizontalScroll: false, logoutOnly: true });

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

  const switched = await switchOrganization(page, input.organizationB);
  if (switched.status !== 200 || switched.body.organizationId !== input.organizationB)
    throw new Error('Server organization switch failed');
  await adoptOrganization(page, input.organizationB, input.organizationNameB, input.courseB);
  if (await page.getByRole('heading', { name: input.courseA, exact: true }).count()) throw new Error('Organization A course leaked after switch');
  stages.push({ action: 'switch-organization', isolated: true, sidebarUpdated: true, course: input.courseB });

  const forged = await switchOrganization(page, input.foreignOrganization);
  if (forged.status !== 200 || forged.body.accessToken !== null || forged.body.organizationId !== null || !forged.body.selectionRequired)
    throw new Error('Forged organization selector was accepted as authority');
  stages.push({ action: 'foreign-organization', selectorOnly: true, educationalToken: false });

  const switchedBack = await switchOrganization(page, input.organizationA);
  if (switchedBack.status !== 200 || switchedBack.body.organizationId !== input.organizationA)
    throw new Error('Server switch back to organization A failed');
  await adoptOrganization(page, input.organizationA, input.organizationNameA, input.courseA);
  await control('revoke-a');
  const switchedAfterRevoke = await switchOrganization(page, input.organizationB);
  if (switchedAfterRevoke.status !== 200 || switchedAfterRevoke.body.organizationId !== input.organizationB)
    throw new Error('Server switch after revocation failed');
  if (switchedAfterRevoke.body.organizations.some(option => option.organizationId === input.organizationA))
    throw new Error('Revoked organization remained selectable');
  await adoptOrganization(page, input.organizationB, input.organizationNameB, input.courseB);
  const revoked = await switchOrganization(page, input.organizationA);
  if (revoked.status !== 200 || revoked.body.organizationId !== input.organizationB)
    throw new Error('Revoked organization selector escaped server revalidation');
  stages.push({ action: 'revoked-authority', removedFromOptions: true, selectedOrganization: input.organizationNameB });

  await logout(page);
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
  await other.getByRole('button', { name: new RegExp(input.secondDisplayName + '.*Ученик') }).waitFor();
  if (await other.getByRole('heading', { name: input.courseA, exact: true }).count()) throw new Error('Organization A course leaked to second user');
  await other.screenshot({ path: path.join(input.evidence, '05-second-user-authenticated.png'), fullPage: true });
  await logout(other);
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
