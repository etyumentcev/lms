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
page.on('console', message => {
  if (message.type() === 'error' && !message.text().includes('server responded with a status of 404'))
    failures.push(`console:${message.text()}`);
});
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

async function protectedJson(target, path) {
  return target.evaluate(async resource => {
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

async function activateParticipant(participant, code, organizationId) {
  const participantContext = await browser.newContext({
    ignoreHTTPSErrors: true, viewport: { width: 390, height: 844 }, locale: 'ru-RU'
  });
  const participantPage = await participantContext.newPage();
  participantPage.setDefaultTimeout(30000);
  try {
    await participantPage.goto(data.origin, { waitUntil: 'networkidle' });
    await participantPage.getByRole('button', { name: 'У меня есть код активации' }).click();
    await participantPage.getByLabel('Организация').fill(organizationId);
    await participantPage.getByLabel('Логин').fill(participant.userName);
    await participantPage.getByLabel('Одноразовый код').fill(code);
    await participantPage.getByLabel('Новый пароль').fill(data.participantPassword);
    await participantPage.getByLabel('Повторите пароль').fill(data.participantPassword);
    await participantPage.getByRole('button', { name: 'Продолжить' }).click();
    await participantPage.getByRole('heading', { name: 'Пароль установлен' }).waitFor();
    await participantPage.getByRole('button', { name: 'Перейти ко входу' }).click();
    await login(participantPage, participant.userName, data.participantPassword, organizationId);
    await participantPage.getByRole('heading', { name: 'Нет доступных курсов' }).waitFor();
  } finally {
    await participantContext.close();
  }
}

async function createParticipant(participant, organizationId) {
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
  await activateParticipant(participant, code, organizationId);
}

try {
  await page.goto(data.origin, { waitUntil: 'networkidle' });
  await page.getByRole('button', { name: /Первый запуск/ }).click();
  await page.getByLabel('Название организации').fill(data.organizationName);
  await page.getByLabel('Логин первого администратора').fill(data.adminUser);
  await page.getByLabel('Токен системного администратора').fill(data.platformToken);
  await page.getByRole('button', { name: 'Создать организацию' }).click();
  await page.getByRole('heading', { name: 'Организация создана' }).waitFor();
  const organizationId = (await page.locator('.bootstrap-result dd').first().textContent()).trim();
  const adminCode = (await page.locator('.activation-secret strong').textContent()).trim();
  await page.getByRole('button', { name: 'Активировать администратора' }).click();
  await page.getByLabel('Новый пароль').fill(data.adminPassword);
  await page.getByRole('button', { name: 'Активировать', exact: true }).click();
  await page.getByRole('button', { name: 'Перейти ко входу' }).click();
  await login(page, data.adminUser, data.adminPassword, organizationId);
  await page.getByRole('heading', { name: 'Добро пожаловать, администратор!' }).waitFor();
  if (!adminCode) throw new Error('first administrator activation code missing');

  await page.getByRole('button', { name: /Создать пользователя/ }).click();
  await page.getByRole('heading', { name: 'Учётные записи' }).waitFor();
  await createParticipant({
    userName: data.teacherUser, displayName: data.teacherDisplayName,
    rosterKey: data.teacherUser, role: 'Teacher'
  }, organizationId);
  await createParticipant({
    userName: data.studentUser, displayName: data.studentDisplayName,
    rosterKey: data.studentUser, role: 'Student'
  }, organizationId);

  await page.getByRole('button', { name: '← Главная' }).click();
  await page.getByRole('button', { name: /Создать курс/ }).click();
  await page.getByRole('heading', { name: 'Каталог курсов' }).waitFor();
  await page.getByRole('button', { name: '+ Создать курс' }).click();
  await page.getByLabel('Название курса').fill(data.courseTitle);
  await page.getByLabel('Описание').fill('Синтетический курс FIX-02');
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
  await enrollment.locator('input[type=checkbox]').first().check();
  await enrollment.getByRole('button', { name: 'Добавить выбранных (1)' }).click();
  await enrollment.waitFor({ state: 'detached' });
  await page.getByRole('button', { name: 'Назначить преподавателя' }).click();
  const assignment = page.getByRole('dialog', { name: 'Назначение преподавателя' });
  await assignment.getByLabel('Преподаватель').selectOption({ label: data.teacherDisplayName });
  await assignment.getByLabel('Группа').selectOption({ label: 'G1' });
  await assignment.getByRole('button', { name: 'Назначить' }).click();
  await assignment.waitFor({ state: 'detached' });
  const courseDetailBeforeEditor = await protectedJson(page,
    `/api/v1/admin/courses/${(await protectedJson(page, '/api/v1/admin/courses')).items[0].courseId}`);
  const courseId = courseDetailBeforeEditor.course.courseId;
  const groupId = courseDetailBeforeEditor.groups.find(group => group.name === 'G1').groupId;
  const g2Id = courseDetailBeforeEditor.groups.find(group => group.name === 'G2').groupId;

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
  await page.screenshot({ path: `${data.evidence}/01-processing-context-bound-1440.png`, fullPage: true });

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
  await page.screenshot({ path: `${data.evidence}/02-admin-course-editor-1440.png`, fullPage: true });
  await logout(page);

  await login(page, data.teacherUser, data.participantPassword, organizationId);
  await page.getByRole('heading', { name: 'Мои курсы' }).waitFor();
  const teacherView = await protectedJson(page, `/api/v1/teacher/courses/${courseId}`);
  if (teacherView.groups.length !== 1 || teacherView.groups[0].groupId !== groupId)
    throw new Error(`CourseEditor expanded G2 access: ${JSON.stringify(teacherView.groups)}`);
  if (!teacherView.canEditContent) throw new Error('CourseEditor capability missing from teacher view');
  await page.getByRole('heading', { name: data.courseTitle }).waitFor();
  await page.getByRole('button', { name: 'Материалы и публикация' }).click();
  await page.getByRole('heading', { name: 'Импорт ZIP' }).waitFor();
  await page.getByTestId('content-zip').setInputFiles(data.validZip);
  await page.getByRole('button', { name: 'Импортировать ZIP' }).click();
  await page.getByText('Пакет готов к публикации.', { exact: true }).waitFor();
  await page.getByTestId('publish-content').click();
  await page.getByText('Опубликована неизменяемая версия v1.', { exact: true }).waitFor();
  await page.getByRole('button', { name: 'Открыть и назначить группе' }).click();
  await page.getByText(/Занятие №1 открыто группе G1; назначено 5 заданий/).waitFor();
  await page.screenshot({ path: `${data.evidence}/03-teacher-published-v1-g1-1440.png`, fullPage: true });
  await logout(page);

  await login(page, data.studentUser, data.participantPassword, organizationId);
  await page.getByRole('heading', { name: 'Мои курсы' }).waitFor();
  await page.getByRole('button', { name: 'Открыть задания' }).click();
  await page.getByRole('heading', { name: data.courseTitle }).waitFor();
  await page.waitForFunction(() =>
    document.querySelector('.study-card-list li')?.textContent?.includes('5 задач · 0 принято · версия v1'));
  const html = await page.content();
  for (const secret of ['M1_TEACHER_ONLY', 'teacherArtifactHash', 'packageHash'])
    if (html.includes(secret)) throw new Error(`student HTML leaked ${secret}`);
  await page.screenshot({ path: `${data.evidence}/04-student-g1-five-problems-1440.png`, fullPage: true });

  if (failures.length) throw new Error(failures.join('\n'));
  process.stdout.write(JSON.stringify({
    status: 'PASS', organizationId, courseId, groupId, g2Id,
    contextVersion: 1, editorAssigned: true, publishedVersion: 1,
    assignedProblems: 5, teacherGroups: teacherView.groups.map(group => group.groupId)
  }) + '\n');
} catch (error) {
  const body = await page.locator('body').innerText().catch(() => '');
  let diagnostic = String(error?.stack || error) + '\n' + failures.join('\n') + '\n' + body;
  for (const secret of [data.platformToken, data.adminPassword, data.participantPassword])
    diagnostic = diagnostic.replaceAll(secret, '[REDACTED]');
  process.stderr.write(diagnostic + '\n');
  process.stdout.write(JSON.stringify({ status: 'FAIL' }) + '\n');
  process.exitCode = 1;
} finally {
  await Promise.race([browser.close(), new Promise(resolve => setTimeout(resolve, 5000))]);
  process.exit(process.exitCode ?? 0);
}
