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
const cdp = await context.newCDPSession(page);
const failures = []; const studentPayloads = []; let expectedNetworkAbort = false; let captureStudentPayloads = false;
page.on('console', message => { if (message.type() === 'error' && !(expectedNetworkAbort && message.text().includes('ERR_CONNECTION_FAILED'))) failures.push(`console:${message.text()}`); });
page.on('pageerror', error => failures.push(`page:${error.message}`));
page.on('response', response => {
  if (response.status() >= 400)
    failures.push(`http:${response.status()}:${new URL(response.url()).pathname}`);
});
page.on('response', async response => {
  if (!captureStudentPayloads || !response.ok()) return;
  const path = new URL(response.url()).pathname;
  if (!path.startsWith('/api/v1/my/') && !/^\/api\/v1\/problem-assignments\/[^/]+\/workspace$/.test(path)) return;
  const text = await response.text().catch(() => '');
  studentPayloads.push({ path, text, cacheControl: response.headers()['cache-control'] || '' });
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
  const path = new URL(page.url()).pathname;
  if (path.startsWith('/problem-assignments/') || path.startsWith('/teacher/')) {
    await page.getByRole('link', { name: 'Мои курсы' }).first().click();
    await page.getByRole('heading', { name: 'Мои курсы' }).waitFor();
  }
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
async function protectedSha256(url) {
  return page.evaluate(async resource => {
    const token = await window.lmsAuth?.getAccessToken();
    if (!token) throw new Error('missing browser access token');
    const response = await fetch(resource, { credentials: 'same-origin', cache: 'no-store',
      headers: { Authorization: `Bearer ${token}` } });
    if (!response.ok) throw new Error(`protected original returned ${response.status}`);
    const digest = await crypto.subtle.digest('SHA-256', await response.arrayBuffer());
    return [...new Uint8Array(digest)].map(byte => byte.toString(16).padStart(2, '0')).join('');
  }, url);
}
async function protectedJson(path) {
  return page.evaluate(async resource => {
    const token = await window.lmsAuth?.getAccessToken();
    if (!token) throw new Error('missing browser access token');
    const response = await fetch(`/api/v1${resource}`, { credentials: 'same-origin', cache: 'no-store',
      headers: { Authorization: `Bearer ${token}` } });
    if (!response.ok) throw new Error(`protected JSON returned ${response.status}`);
    return response.json();
  }, path);
}
async function drawOnCurrentPage(pointerType = 'mouse') {
  const canvas = page.locator('canvas[aria-label^="Слой пометок страницы"]').first();
  await canvas.scrollIntoViewIfNeeded(); const box = await canvas.boundingBox();
  if (!box) throw new Error('annotation canvas has no geometry');
  const start = { x: box.x + box.width * .2, y: box.y + box.height * .3 };
  const end = { x: box.x + box.width * .48, y: box.y + box.height * .42 };
  if (pointerType === 'pen') {
    await cdp.send('Input.dispatchMouseEvent', { type: 'mousePressed', ...start, button: 'left', buttons: 1, pointerType: 'pen', force: .7 });
    await cdp.send('Input.dispatchMouseEvent', { type: 'mouseMoved', ...end, button: 'left', buttons: 1, pointerType: 'pen', force: .8 });
    await cdp.send('Input.dispatchMouseEvent', { type: 'mouseReleased', ...end, button: 'left', buttons: 0, pointerType: 'pen', force: 0 });
  } else {
    await page.mouse.move(start.x, start.y); await page.mouse.down(); await page.mouse.move(end.x, end.y, { steps: 12 }); await page.mouse.up();
  }
  await page.getByText(/Графические пометки: 1 штр/).waitFor();
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
  await page.getByText(/Занятие №1 открыто группе G1; назначено 60 заданий/).waitFor();
  await page.screenshot({ path: `${data.evidence}/05-published-v1-assigned-g1.png`, fullPage: true });

  await upload(data.versionTwoZip);
  await page.getByTestId('publish-content').click();
  await page.getByText('Опубликована неизменяемая версия v2.', { exact: true }).waitFor();
  await page.screenshot({ path: `${data.evidence}/06-published-v2.png`, fullPage: true });

  await logout(); await login(data.studentUser); captureStudentPayloads = true;
  await page.getByRole('button', { name: 'Открыть задания' }).click();
  await page.getByRole('heading', { name: 'Подготовка к олимпиаде Эйлера' }).waitFor();
  await page.waitForFunction(() => document.querySelector('.study-card-list li')?.textContent?.includes('5 задач · 0 принято · версия v1'));
  const studentHtml = await page.content();
  for (const secret of ['M1_TEACHER_ONLY', 'teacherArtifactHash', 'packageHash'])
    if (studentHtml.includes(secret)) throw new Error(`student HTML leaked ${secret}`);
  await page.screenshot({ path: `${data.evidence}/07-student-v1-assignments-after-v2.png`, fullPage: true });
  await page.screenshot({ path: `${data.materialsEvidence}/01-course-1440.png`, fullPage: true });

  await page.getByRole('link').filter({ hasText: 'Остатки и сравнения' }).click();
  await page.getByRole('heading', { name: 'Теория' }).waitFor();
  await page.locator('.katex').first().waitFor();
  await page.getByRole('img', { name: 'Схема сравнения' }).waitFor();
  await page.getByText('Задача 1', { exact: true }).waitFor();
  await page.screenshot({ path: `${data.materialsEvidence}/02-lesson-1440.png`, fullPage: true });
  await page.setViewportSize({ width: 768, height: 1024 });
  await page.screenshot({ path: `${data.materialsEvidence}/03-lesson-768.png`, fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: `${data.materialsEvidence}/04-lesson-390.png`, fullPage: true });
  await page.setViewportSize({ width: 1440, height: 1050 });

  await page.locator('a[href^="/problem-assignments/"]').first().click();
  await page.getByRole('heading', { name: 'Задача 1' }).waitFor();
  const assignmentPath = new URL(page.url()).pathname;
  await page.getByRole('region', { name: 'Условие задачи' }).getByText(/вычислите остаток/).waitFor();
  await page.screenshot({ path: `${data.materialsEvidence}/05-problem-1440.png`, fullPage: true });
  await page.getByRole('tab', { name: 'Подсказки' }).click();
  await page.getByText('Рассмотрите классы вычетов и небольшой пример.', { exact: true }).waitFor();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: `${data.materialsEvidence}/06-hints-390.png`, fullPage: true });
  await page.getByRole('tab', { name: 'История' }).click();
  await page.getByText('Вы ещё не отправляли решение', { exact: true }).waitFor();
  await page.getByRole('tab', { name: 'Решение' }).click();
  await page.setViewportSize({ width: 1440, height: 1050 });

  await page.setViewportSize({ width: 390, height: 844 });
  const picker = page.getByLabel('Сфотографировать / Выбрать фото');
  await picker.setInputFiles([data.photoOne, data.photoTwo]);
  await page.waitForFunction(() => document.querySelectorAll('.attachment-pages [role="status"]').length === 2 &&
    [...document.querySelectorAll('.attachment-pages [role="status"]')].every(node => node.textContent === 'Фото загружено'));
  await page.locator('.attachment-pages img').nth(1).waitFor();
  await page.screenshot({ path: `${data.photoEvidence}/01-upload-preview-390.png`, fullPage: true });

  await page.getByRole('button', { name: 'Страница 2: переместить выше' }).click();
  const draftNames = await page.locator('.attachment-pages .attachment-filename').allTextContents();
  if (draftNames.join('|') !== 'a4-2.png|a4-1.png') throw new Error(`unexpected reordered draft: ${draftNames.join('|')}`);
  await page.setViewportSize({ width: 768, height: 1024 });
  await page.screenshot({ path: `${data.photoEvidence}/02-reordered-preview-768.png`, fullPage: true });

  await page.getByRole('button', { name: 'Удалить страницу 2' }).click();
  await page.waitForFunction(() => document.querySelectorAll('.attachment-pages > li').length === 1);
  await picker.setInputFiles(data.photoOne);
  await page.waitForFunction(() => document.querySelectorAll('.attachment-pages [role="status"]').length === 2 &&
    [...document.querySelectorAll('.attachment-pages [role="status"]')].every(node => node.textContent === 'Фото загружено'));
  await page.setViewportSize({ width: 1440, height: 1050 });
  await page.screenshot({ path: `${data.photoEvidence}/03-delete-readd-preview-1440.png`, fullPage: true });

  let submissionRequests = 0;
  expectedNetworkAbort = true;
  await page.route('**/api/v1/problem-assignments/*/submissions', async route => {
    submissionRequests++;
    const response = await route.fetch();
    if (!response.ok()) throw new Error(`submission returned ${response.status()}`);
    await route.abort('connectionfailed');
  });
  const submitButton = page.getByRole('button', { name: 'Отправить решение' });
  await submitButton.evaluate(button => { button.click(); button.click(); });
  await page.getByRole('alert').getByText(/Решение могло сохраниться/).waitFor();
  await page.waitForTimeout(500);
  await page.unroute('**/api/v1/problem-assignments/*/submissions');
  expectedNetworkAbort = false;
  if (submissionRequests !== 1) throw new Error(`double submit created ${submissionRequests} requests`);
  await page.getByRole('button', { name: 'Обновить состояние' }).click();
  await page.getByText('Решение отправлено и ожидает проверки преподавателем.', { exact: true }).waitFor();
  await page.reload({ waitUntil: 'networkidle' });
  await page.getByRole('heading', { name: 'Задача 1' }).waitFor();
  await page.getByText('Решение отправлено и ожидает проверки преподавателем.', { exact: true }).waitFor();
  await page.getByRole('img', { name: 'Страница 1 — фото решения' }).waitFor();
  await page.getByRole('img', { name: 'Страница 2 — фото решения' }).waitFor();
  await page.screenshot({ path: `${data.photoEvidence}/04-submitted-reload-390.png`, fullPage: true });
  captureStudentPayloads = false;
  if (!studentPayloads.length) throw new Error('student responses were not captured');
  for (const payload of studentPayloads) {
    for (const secret of ['M1_TEACHER_ONLY', 'teacherArtifactHash', 'packageHash'])
      if (payload.text.includes(secret)) throw new Error(`student response ${payload.path} leaked ${secret}`);
    if (!payload.cacheControl.includes('no-store')) throw new Error(`student response ${payload.path} is cacheable`);
  }

  await logout(); await login(data.teacherUser);
  await page.getByRole('link', { name: 'Прогресс' }).click();
  await page.getByRole('heading', { name: 'Прогресс группы' }).waitFor();
  const submittedProgress = await protectedJson(`/teacher/group-progress?courseId=${data.courseId}&groupId=${data.groupId}`);
  if (submittedProgress.summary.studentCount !== 12 || submittedProgress.summary.assignedCount !== 60 ||
      submittedProgress.summary.notStarted !== 59 || submittedProgress.summary.submitted !== 1)
    throw new Error(`submitted progress mismatch: ${JSON.stringify(submittedProgress.summary)}`);
  await page.getByText('Ученик 01', { exact: true }).waitFor();
  await page.screenshot({ path: `${data.progressEvidence}/01-teacher-submitted-1440.png`, fullPage: true });
  await page.getByRole('link', { name: 'Проверка' }).click();
  await page.getByRole('heading', { name: 'Проверка работ' }).waitFor();
  await page.setViewportSize({ width: 1440, height: 1050 });
  await page.getByText('2 страниц', { exact: true }).waitFor();
  await page.screenshot({ path: `${data.m1Evidence}/01-review-queue-1440.png`, fullPage: true });
  await page.getByRole('link', { name: /Проверить:/ }).first().click();
  await page.getByRole('heading', { name: 'Задача 1' }).waitFor();
  const workId = new URL(page.url()).pathname.split('/').at(-1);
  const firstTeacherWork = await protectedJson(`/teacher/problem-works/${workId}`);
  if (firstTeacherWork.currentSubmission.attachmentIds.length !== 2) throw new Error('teacher current submission lost pages');
  const reference = page.getByTestId('teacher-reference');
  await reference.getByText('Неизменяемая версия v1', { exact: true }).waitFor();
  await reference.getByText(/M1_TEACHER_ONLY_SOLUTION_v1/).waitFor();
  await reference.getByText('Критерии оценки', { exact: true }).click();
  await reference.getByText(/M1_TEACHER_ONLY_RUBRIC/).waitFor();
  await reference.getByText('Методические рекомендации', { exact: true }).click();
  await reference.getByText(/M1_TEACHER_ONLY_NOTES/).waitFor();
  if (await reference.getByText(/M1_TEACHER_ONLY_SOLUTION_v2/).count()) throw new Error('historical review resolved the latest v2 reference');
  await page.getByRole('img', { name: 'Страница 1 — фото решения' }).waitFor();
  const firstPhotoHash = await protectedSha256(`${data.origin}/api/v1/attachments/${firstTeacherWork.currentSubmission.attachmentIds[0]}/content`);
  if (firstPhotoHash !== data.photoTwoHash) throw new Error(`teacher page 1 hash mismatch: ${firstPhotoHash}`);
  await page.screenshot({ path: `${data.materialsEvidence}/07-teacher-reference-1440.png`, fullPage: true });
  await page.screenshot({ path: `${data.photoEvidence}/05-teacher-page-1-1440.png`, fullPage: true });
  await page.screenshot({ path: `${data.m1Evidence}/02-desktop-review-1440.png`, fullPage: true });

  await drawOnCurrentPage('pen');
  await page.screenshot({ path: `${data.m1Evidence}/03-page-1-annotated-1440.png`, fullPage: true });
  await page.setViewportSize({ width: 768, height: 1024 });
  await page.getByRole('button', { name: 'На весь экран' }).click();
  await page.getByRole('dialog', { name: 'Фотографии решения' }).waitFor();
  for (const name of ['Перо', 'Ластик', 'Отмена', 'Увеличить фото', 'Выйти из полного экрана'])
    if (!await page.getByRole('button', { name }).isVisible()) throw new Error(`fullscreen control missing: ${name}`);
  for (const name of ['Цвет пера', 'Толщина пера'])
    if (!await page.getByLabel(name).isHidden()) throw new Error(`fullscreen secondary control visible: ${name}`);
  const fitsViewport = await page.getByAltText('Страница 1 — фото решения').evaluate(node => {
    const rect = node.getBoundingClientRect(); return rect.top >= 0 && rect.left >= 0 && rect.bottom <= innerHeight + 1 && rect.right <= innerWidth + 1;
  });
  if (!fitsViewport) throw new Error('fullscreen A4 page does not initially fit');
  await page.screenshot({ path: `${data.m1Evidence}/04-fullscreen-768.png` });
  await page.getByRole('button', { name: 'Выйти из полного экрана' }).click();
  await page.setViewportSize({ width: 1440, height: 1050 });

  await page.getByRole('button', { name: 'Показать страницу 2' }).click();
  await page.getByRole('img', { name: 'Страница 2 — фото решения' }).waitFor();
  const secondPhotoHash = await protectedSha256(`${data.origin}/api/v1/attachments/${firstTeacherWork.currentSubmission.attachmentIds[1]}/content`);
  if (secondPhotoHash !== data.photoOneHash) throw new Error(`teacher page 2 hash mismatch: ${secondPhotoHash}`);
  await page.screenshot({ path: `${data.photoEvidence}/06-teacher-page-2-1440.png`, fullPage: true });
  await drawOnCurrentPage('mouse');
  await page.screenshot({ path: `${data.m1Evidence}/05-page-2-annotated-1440.png`, fullPage: true });
  await page.getByRole('button', { name: 'Показать страницу 1' }).click(); await page.getByText(/Графические пометки: 1 штр/).waitFor();
  await page.getByRole('button', { name: 'Показать страницу 2' }).click(); await page.getByText(/Графические пометки: 1 штр/).waitFor();

  await page.getByRole('textbox', { name: 'Комментарий преподавателя' }).fill('Исправьте переход и сохраните вычисления.');
  let reviewRequests = 0; expectedNetworkAbort = true;
  await page.route('**/api/v1/problem-works/*/reviews/needs-revision', async route => {
    reviewRequests++; const response = await route.fetch();
    if (!response.ok()) throw new Error(`review returned ${response.status()}`);
    await route.abort('connectionfailed');
  });
  await page.getByRole('button', { name: 'Вернуть на доработку' }).evaluate(button => { button.click(); button.click(); });
  await page.getByRole('alert').getByText(/Не удалось подтвердить проверку/).waitFor();
  await page.waitForTimeout(500); await page.unroute('**/api/v1/problem-works/*/reviews/needs-revision'); expectedNetworkAbort = false;
  if (reviewRequests !== 1) throw new Error(`unknown review outcome replayed ${reviewRequests} times`);
  page.once('dialog', dialog => dialog.accept());
  await page.getByRole('button', { name: 'Обновить работу' }).click();
  await page.getByRole('region', { name: 'Результат проверки' }).waitFor();
  if (await page.getByText(/Есть несохранённые графические пометки/).count()) throw new Error('committed review kept a dirty draft');
  await page.reload({ waitUntil: 'networkidle' });
  await page.getByRole('region', { name: 'Результат проверки' }).waitFor();
  await page.getByText(/Графические пометки: 1 штр/).waitFor();
  await page.getByRole('button', { name: 'Показать страницу 2' }).click(); await page.getByText(/Графические пометки: 1 штр/).waitFor();
  await page.getByRole('link', { name: 'Прогресс' }).click();
  await page.getByRole('heading', { name: 'Прогресс группы' }).waitFor();
  const revisionProgress = await protectedJson(`/teacher/group-progress?courseId=${data.courseId}&groupId=${data.groupId}`);
  if (revisionProgress.summary.studentCount !== 12 || revisionProgress.summary.assignedCount !== 60 ||
      revisionProgress.summary.notStarted !== 59 || revisionProgress.summary.needsRevision !== 1)
    throw new Error(`revision progress mismatch: ${JSON.stringify(revisionProgress.summary)}`);
  await page.screenshot({ path: `${data.progressEvidence}/02-teacher-needs-revision-1440.png`, fullPage: true });
  await page.getByRole('link', { name: 'Мои проверки' }).click();
  await page.getByRole('heading', { name: 'Мои проверки', level: 1 }).waitFor();
  await page.getByLabel('Статус: Нужна доработка').waitFor();
  await page.screenshot({ path: `${data.progressEvidence}/03-review-history-needs-revision-1440.png`, fullPage: true });

  await logout(); await login(data.studentUser);
  await page.goto(`${data.origin}${assignmentPath}?tab=history`, { waitUntil: 'networkidle' });
  await page.getByRole('article', { name: 'Попытка №1' }).waitFor();
  await page.getByRole('article', { name: 'Попытка №1' }).getByText('Исправьте переход и сохраните вычисления.', { exact: true }).waitFor();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: `${data.m1Evidence}/06-student-feedback-390.png`, fullPage: true });
  const revisionState = await protectedJson(`${assignmentPath}/workspace`);
  if (revisionState.work.status !== 'NeedsRevision' || revisionState.history.reviews.length !== 1) throw new Error('student did not receive revision');
  const immutableFirstReview = JSON.stringify(revisionState.history.reviews[0]);
  const firstAnnotations = revisionState.history.reviews[0].annotations;
  if (firstAnnotations.length !== 2 || firstAnnotations.some(item => item.strokes.length !== 1)) throw new Error('two-page annotations were not persisted');
  if (Math.abs(firstAnnotations[0].strokes[0].points[0].x - .2) > .02 || Math.abs(firstAnnotations[0].strokes[0].points[0].y - .3) > .02)
    throw new Error('normalized page-one coordinates changed');
  if (!firstAnnotations[0].strokes[0].points.some(point => point.pressure > 0)) throw new Error('emulated pen pressure was not persisted');
  const firstSubmission = revisionState.history.submissions.find(item => item.attemptNumber === 1);
  const firstExpectedHashes = [data.photoTwoHash, data.photoOneHash];
  for (let index = 0; index < firstSubmission.attachmentIds.length; index++)
    if (await protectedSha256(`${data.origin}/api/v1/attachments/${firstSubmission.attachmentIds[index]}/content`) !== firstExpectedHashes[index])
      throw new Error(`first attempt original changed at page ${index + 1}`);

  await page.getByRole('tab', { name: 'Решение' }).click();
  const revisionPicker = page.getByLabel('Сфотографировать / Выбрать фото');
  await revisionPicker.setInputFiles([data.photoOne, data.photoTwo]);
  await page.waitForFunction(() => document.querySelectorAll('.solution-form .attachment-pages [role="status"]').length === 2 &&
    [...document.querySelectorAll('.solution-form .attachment-pages [role="status"]')].every(node => node.textContent === 'Фото загружено'));
  await page.getByRole('textbox', { name: 'Пояснение к решению — необязательно' }).fill('Исправленная попытка с полным обоснованием.');
  await page.getByRole('button', { name: 'Отправить решение' }).click();
  await page.getByText('Решение отправлено и ожидает проверки преподавателем.', { exact: true }).waitFor();
  const submittedRevision = await protectedJson(`${assignmentPath}/workspace`);
  const secondSubmission = submittedRevision.history.submissions.find(item => item.attemptNumber === 2);
  if (!secondSubmission || secondSubmission.attachmentIds.length !== 2) throw new Error('second attempt lost pages');

  await page.setViewportSize({ width: 1440, height: 1050 }); await logout(); await login(data.teacherUser); await page.getByRole('link', { name: 'Проверка' }).click();
  await page.getByRole('heading', { name: 'Проверка работ' }).waitFor();
  await page.getByText('Попытка №2', { exact: true }).waitFor(); await page.getByText('2 страниц', { exact: true }).waitFor();
  await page.setViewportSize({ width: 768, height: 1024 });
  await page.screenshot({ path: `${data.m1Evidence}/07-revision-queue-768.png`, fullPage: true });
  await page.getByRole('link', { name: /Проверить:/ }).first().click();
  await page.getByRole('heading', { name: 'Попытка №2', exact: true }).waitFor();
  await page.getByRole('region', { name: 'Последнее замечание' }).getByText(/Исправьте переход/).waitFor();
  await page.setViewportSize({ width: 1440, height: 1050 });
  await page.getByRole('button', { name: 'Принять решение' }).click();
  await page.getByText('Проверка сохранена. Состояние работы обновлено.', { exact: true }).waitFor();
  await page.getByRole('region', { name: 'Результат проверки' }).getByLabel('Статус: Принята').waitFor();
  await page.screenshot({ path: `${data.m1Evidence}/08-teacher-accepted-1440.png`, fullPage: true });
  await page.getByRole('link', { name: 'Прогресс' }).click();
  await page.getByRole('heading', { name: 'Прогресс группы' }).waitFor();
  const acceptedProgress = await protectedJson(`/teacher/group-progress?courseId=${data.courseId}&groupId=${data.groupId}`);
  if (acceptedProgress.summary.studentCount !== 12 || acceptedProgress.summary.assignedCount !== 60 ||
      acceptedProgress.summary.notStarted !== 59 || acceptedProgress.summary.accepted !== 1)
    throw new Error(`accepted progress mismatch: ${JSON.stringify(acceptedProgress.summary)}`);
  await page.screenshot({ path: `${data.progressEvidence}/04-teacher-accepted-1440.png`, fullPage: true });
  await page.locator('select[name="status"]').selectOption('Accepted');
  await page.getByRole('button', { name: 'Применить' }).click();
  await page.getByLabel('Статус: Принята').waitFor();
  await page.screenshot({ path: `${data.progressEvidence}/05-teacher-accepted-filter-1440.png`, fullPage: true });
  await page.getByRole('link', { name: 'Мои проверки' }).click();
  await page.getByRole('heading', { name: 'Мои проверки', level: 1 }).waitFor();
  const acceptedHistory = await protectedJson(`/teacher/review-history?courseId=${data.courseId}&groupId=${data.groupId}`);
  if (acceptedHistory.summary.total !== 2 || acceptedHistory.summary.needsRevision !== 1 || acceptedHistory.summary.accepted !== 1)
    throw new Error(`review history mismatch: ${JSON.stringify(acceptedHistory.summary)}`);
  await page.screenshot({ path: `${data.progressEvidence}/06-review-history-accepted-1440.png`, fullPage: true });
  await page.getByRole('link', { name: 'Открыть прошлую проверку' }).first().click();
  await page.getByRole('region', { name: 'Результат проверки' }).getByLabel('Статус: Принята').waitFor();
  await page.screenshot({ path: `${data.progressEvidence}/07-open-accepted-review-1440.png`, fullPage: true });
  await page.getByRole('link', { name: 'К истории проверок' }).click();
  await page.getByRole('heading', { name: 'Мои проверки', level: 1 }).waitFor();
  await page.getByRole('link', { name: 'Проверка' }).click(); await page.getByText('Все работы проверены', { exact: true }).waitFor();

  await logout(); await login(data.studentUser);
  await page.getByRole('link', { name: 'Прогресс' }).click();
  await page.getByRole('heading', { name: 'Мой прогресс' }).waitFor();
  const studentProgress = await protectedJson(`/student/progress-history?courseId=${data.courseId}`);
  if (studentProgress.summary.assignedCount !== 5 || studentProgress.summary.notStarted !== 4 ||
      studentProgress.summary.accepted !== 1 || studentProgress.items.length !== 5)
    throw new Error(`student progress mismatch: ${JSON.stringify(studentProgress.summary)}`);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: `${data.progressEvidence}/08-student-progress-390.png`, fullPage: true }); await page.goto(`${data.origin}${assignmentPath}?tab=history`, { waitUntil: 'networkidle' });
  await page.getByRole('article', { name: 'Попытка №2' }).getByLabel('Статус: Принята').waitFor();
  await page.getByRole('article', { name: 'Попытка №2' }).waitFor();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: `${data.m1Evidence}/09-student-accepted-390.png`, fullPage: true });
  await page.screenshot({ path: `${data.progressEvidence}/09-student-attempt-history-390.png`, fullPage: true });
  const acceptedState = await protectedJson(`${assignmentPath}/workspace`);
  if (acceptedState.work.status !== 'Accepted' || acceptedState.history.submissions.length !== 2 || acceptedState.history.reviews.length !== 2)
    throw new Error('final immutable attempt chain is incomplete');
  const preservedRevision = acceptedState.history.reviews.find(item => item.decision === 'NeedsRevision');
  if (JSON.stringify(preservedRevision) !== immutableFirstReview) throw new Error('old review or annotations mutated');
  const secondExpectedHashes = [data.photoOneHash, data.photoTwoHash];
  for (let index = 0; index < secondSubmission.attachmentIds.length; index++)
    if (await protectedSha256(`${data.origin}/api/v1/attachments/${secondSubmission.attachmentIds[index]}/content`) !== secondExpectedHashes[index])
      throw new Error(`second attempt original changed at page ${index + 1}`);
  if (JSON.stringify(acceptedState).includes('M1_TEACHER_ONLY')) throw new Error('teacher reference leaked into student work projection');

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
