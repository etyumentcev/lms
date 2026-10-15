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
const context = await browser.newContext({ ignoreHTTPSErrors: true,
  viewport: { width: 1440, height: 1050 }, locale: 'ru-RU' });
const page = await context.newPage();
page.setDefaultTimeout(30000);

async function login(userName) {
  for (let attempt = 0; attempt < 3; attempt++) {
    try {
      await page.goto(data.origin, { waitUntil: 'networkidle' });
      break;
    } catch (error) {
      if (!String(error).includes('ERR_NETWORK_CHANGED') || attempt === 2) throw error;
      await page.waitForTimeout(1000);
    }
  }
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

async function token() {
  const value = await page.evaluate(() => window.lmsAuth?.getAccessToken());
  if (!value) throw new Error('missing browser access token');
  return value;
}

async function json(path) {
  const access = await token();
  const response = await context.request.get(`${data.origin}${path}`, {
    ignoreHTTPSErrors: true, headers: { Authorization: `Bearer ${access}` }
  });
  if (!response.ok()) throw new Error(`${path} returned ${response.status()}`);
  return response.json();
}

async function content(path) {
  const access = await token();
  const response = await context.request.get(`${data.origin}${path}`, {
    ignoreHTTPSErrors: true, headers: { Authorization: `Bearer ${access}` }
  });
  const bytes = await response.body();
  const digest = response.ok()
    ? await page.evaluate(async raw => {
        const hash = await crypto.subtle.digest('SHA-256', new Uint8Array(raw));
        return [...new Uint8Array(hash)].map(byte => byte.toString(16).padStart(2, '0')).join('');
      }, [...bytes])
    : null;
  return { status: response.status(), size: bytes.length, sha256: digest };
}

try {
  await login(data.studentUser);
  const workspace = await json(`/api/v1/problem-assignments/${data.assignmentId}/workspace`);
  if (workspace.work.status !== 'Accepted' || workspace.history.submissions.length !== 2 ||
      workspace.history.reviews.length !== 2)
    throw new Error('student restored history is incomplete');
  await page.goto(`${data.origin}/problem-assignments/${data.assignmentId}?tab=history`, { waitUntil: 'networkidle' });
  await page.getByRole('article', { name: 'Попытка №2' }).getByLabel('Статус: Принята').waitFor();
  await page.screenshot({ path: `${data.evidence}/${data.stage}-student-history-390.png`, fullPage: true });
  const fileResults = [];
  for (const expected of data.files) {
    const actual = await content(`/api/v1/attachments/${expected.attachmentId}/content`);
    fileResults.push({ attachmentId: expected.attachmentId, ...actual });
    if (data.expectMissingFiles) {
      if (actual.status >= 200 && actual.status < 300)
        throw new Error(`incomplete restore exposed ${expected.attachmentId}`);
    } else if (actual.status !== 200 || actual.sha256 !== expected.sha256 || actual.size !== expected.size) {
      throw new Error(`restored original mismatch ${expected.attachmentId}: ${JSON.stringify(actual)}`);
    }
  }
  if (data.expectMissingFiles) {
    process.stdout.write(JSON.stringify({ status: 'EXPECTED_FAILURE', stage: data.stage, fileResults }) + '\n');
    process.exit(0);
  }

  await logout();
  await login(data.teacherUser);
  const progress = await json(`/api/v1/teacher/group-progress?courseId=${data.courseId}&groupId=${data.groupId}`);
  if (progress.summary.studentCount !== 12 || progress.summary.assignedCount !== 60 ||
      progress.summary.notStarted !== 54 || progress.summary.submitted !== 2 ||
      progress.summary.needsRevision !== 2 || progress.summary.accepted !== 2)
    throw new Error(`restored teacher progress mismatch: ${JSON.stringify(progress.summary)}`);
  const history = await json(`/api/v1/teacher/review-history?courseId=${data.courseId}&groupId=${data.groupId}`);
  if (history.summary.total !== 5 || history.summary.needsRevision !== 3 || history.summary.accepted !== 2)
    throw new Error(`restored review history mismatch: ${JSON.stringify(history.summary)}`);
  const work = await json(`/api/v1/teacher/problem-works/${data.workId}`);
  const revision = work.reviews.find(item => item.decision === 'NeedsRevision');
  if (!revision || revision.annotations.length !== 2 ||
      revision.annotations.some(item => item.strokes.length !== 1))
    throw new Error('restored annotations are incomplete');
  await page.getByRole('link', { name: 'Прогресс' }).click();
  await page.getByRole('heading', { name: 'Прогресс группы' }).waitFor();
  await page.getByText('60 назначенных задач', { exact: true }).waitFor();
  await page.screenshot({ path: `${data.evidence}/${data.stage}-teacher-progress-1440.png`, fullPage: true });

  await logout();
  await login(data.otherTeacherUser);
  const denied = [];
  const access = await token();
  for (const path of [`/api/v1/teacher/problem-works/${data.workId}`,
      ...data.files.map(item => `/api/v1/attachments/${item.attachmentId}/content`)]) {
    const response = await context.request.get(`${data.origin}${path}`, {
      ignoreHTTPSErrors: true, headers: { Authorization: `Bearer ${access}` }
    });
    denied.push({ path, status: response.status(), size: (await response.body()).length });
    if (response.status() !== 403) throw new Error(`foreign group access was ${response.status()} for ${path}`);
  }
  process.stdout.write(JSON.stringify({ status: 'PASS', stage: data.stage, fileResults,
    progress: progress.summary, reviewHistory: history.summary, annotations: revision.annotations.length, denied }) + '\n');
} catch (error) {
  const body = await page.locator('body').innerText().catch(() => '');
  process.stderr.write(`${String(error?.stack || error)}\n${body}\n`.replaceAll(data.password, '[REDACTED]'));
  process.stdout.write(JSON.stringify({ status: 'FAIL', stage: data.stage }) + '\n');
  process.exitCode = 1;
} finally {
  await Promise.race([browser.close(), new Promise(resolve => setTimeout(resolve, 5000))]);
  process.exit(process.exitCode ?? 0);
}
