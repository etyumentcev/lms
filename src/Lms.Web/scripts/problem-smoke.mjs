// G: real student workflow against a clean, isolated local Compose project.
// Teacher commands below prepare valid review states through existing APIs, not a teacher UI.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdir, writeFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.LMS_SMOKE_URL || 'http://127.0.0.1:8081';
const output = process.env.LMS_SMOKE_OUTPUT || '/tmp/lms-g-browser';
await mkdir(output, { recursive: true });
const compose = (...args) => execFileSync('docker', ['compose', ...args], { encoding: 'utf8', stdio: ['ignore','pipe','pipe'] }).trim();
const token = compose('exec','-T','api','dotnet','Lms.Api.dll','--dev-token');
const teacher = compose('exec','-T','api','dotnet','Lms.Api.dll','--dev-token','Teacher');
const assignment = '40000000-0000-0000-0000-000000000001';
const route = `/problem-assignments/${assignment}`;
async function api(path, { method = 'GET', body, role = token } = {}) {
  const response = await fetch(`${base}/api/v1${path}`, { method, headers: { Authorization: `Bearer ${role}`, ...(body ? {'Content-Type':'application/json'} : {}) }, body: body ? JSON.stringify(body) : undefined });
  assert(response.ok, `${method} ${path}: ${response.status}`); return response.json();
}
const workspace = () => api(`${route}/workspace`);
const bytes = async id => { const res = await fetch(`${base}/api/v1/attachments/${id}/content`, { headers: { Authorization: `Bearer ${token}` } }); assert.equal(res.status,200); return Buffer.from(await res.arrayBuffer()); };
const digest = data => createHash('sha256').update(data).digest('hex');
const results = [], errors = [];
const browser = await chromium.launch({ headless: true, channel: 'chrome' });
try {
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
  page.on('pageerror', e => errors.push(e.message));
  const uploadedPages = [];
  page.on('response', async response => { if (response.url().endsWith('/uploads') && response.status()===201) uploadedPages.push(await response.json()); });
  const requests = []; page.on('request', request => { if (request.url().endsWith('/submissions')) requests.push(request.postDataJSON()); });
  await page.goto(`${base}/local-login.html`); await page.getByLabel('Вставьте token').fill(token); await page.getByRole('button').click();
  await page.waitForURL('**/courses/local-demo');
  await page.getByRole('link', { name: /Открыть.*Делимость и остатки/ }).click();
  await page.getByRole('link', { name: 'Открыть задачу: Делимость суммы' }).click();
  await page.getByRole('heading', { level: 1, name: 'Делимость суммы' }).waitFor();
  const first = await workspace(); assert.equal(first.work.problemWorkId, null); assert.equal(first.work.status, 'NotStarted'); assert.equal(first.work.attemptCount, 0); assert.equal(first.work.version,null);
  const png = Buffer.from(await page.evaluate(() => {
    const canvas = document.createElement('canvas'); canvas.width=1190; canvas.height=1684;
    const context = canvas.getContext('2d'); context.fillStyle='#fff'; context.fillRect(0,0,1190,1684); context.fillStyle='#183c70'; context.font='40px serif';
    context.fillText('Solution — page A4',80,100);
    for(let i=1;i<20;i++) { context.fillStyle='#e5e9ed'; context.fillRect(50,120+i*65,1090,1); context.fillStyle='#183c70'; context.fillText(`${i}.  n = 3k + r`,80,110+i*65); }
    return canvas.toDataURL('image/png').split(',')[1];
  }), 'base64');
  await page.getByLabel('Добавить фото решения').setInputFiles({ name:'Лист 1.png',mimeType:'image/png',buffer:png });
  await page.getByAltText('Страница 1 — фото решения').waitFor();
  await page.getByLabel('Добавить фото решения').setInputFiles({ name:'Лист 2.png',mimeType:'image/png',buffer:png });
  await page.getByAltText('Страница 2 — фото решения').waitFor();
  assert.equal((await workspace()).work.problemWorkId,null);
  await page.getByRole('button', { name:'Страница 2: переместить выше' }).focus(); await page.keyboard.press('Enter');
  assert.match(await page.locator('.attachment-pages li').first().innerText(), /Лист 2/);
  await page.getByRole('textbox').fill('Рассмотрел все остатки.');
  await page.getByRole('tab', { name:'Решение' }).focus(); await page.keyboard.press('ArrowRight');
  assert.equal(await page.getByRole('tab', { name:'Подсказки' }).evaluate(node => document.activeElement === node),true);
  assert(await page.getByRole('region',{ name:'Условие задачи' }).isVisible()); await page.keyboard.press('End');
  await page.getByText('Вы ещё не отправляли решение').waitFor(); assert(await page.getByRole('region',{ name:'Условие задачи' }).isVisible());
  await page.goBack(); await page.getByText('Подсказок пока нет').waitFor(); await page.goForward(); await page.getByText('Вы ещё не отправляли решение').waitFor();
  await page.getByRole('tab', { name:'Решение' }).click(); assert.equal(await page.getByRole('textbox').inputValue(),'Рассмотрел все остатки.');
  for (const width of [320,375,768,1024,1440]) {
    await page.setViewportSize({width,height:1000});
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth),false,`overflow ${width}`);
    assert.equal(await page.locator('h1').count(),1); assert.equal(await page.getByRole('complementary').count(),1);
    assert(await page.getByAltText('Страница 1 — фото решения').evaluate(img => img.naturalHeight>img.naturalWidth && getComputedStyle(img).objectFit==='contain'));
    assert(await page.getByRole('button',{name:'Отправить решение'}).isVisible());
    if(width===768) await page.getByRole('button',{name:'Свернуть контекст'}).click();
    if(width===1024) { await page.setViewportSize({width:375,height:1000}); assert(await page.getByRole('complementary').getByText('Не начата').isVisible()); await page.setViewportSize({width,height:1000}); await page.getByRole('button',{name:'Развернуть контекст'}).click(); }
    await page.screenshot({path:`${output}/solution-${width}.png`,fullPage:true});
  }
  results.push('Student login → Course → Lesson → NotStarted; GET/upload create no work; two A4 pages; keyboard reorder/tabs/back/forward; draft survives tabs; 5 widths and context preservation');
  const sentResponse = page.waitForResponse(r => r.url().endsWith('/submissions') && r.request().method()==='POST');
  await page.getByRole('button',{name:'Отправить решение'}).click(); assert.equal((await sentResponse).status(),201);
  await page.getByText('Решение отправлено и ожидает проверки преподавателем.').waitFor();
  let state = await workspace(); assert.equal(state.work.status,'Submitted'); assert.equal(state.work.attemptCount,1);
  assert.deepEqual(state.work.latestSubmission.attachmentIds,requests[0].attachmentIds); assert.equal(state.work.latestSubmission.bodyMarkdown,'Рассмотрел все остатки.');
  assert.deepEqual(requests[0].attachmentIds,[uploadedPages[1].attachmentId,uploadedPages[0].attachmentId]);
  assert.equal(requests.length,1); assert.equal(await page.getByRole('textbox').count(),0);
  const attachmentIds = state.work.latestSubmission.attachmentIds;
  for(const id of attachmentIds) assert.equal(digest(await bytes(id)),digest(png));
  await page.reload(); await page.getByText('Решение отправлено и ожидает проверки преподавателем.').waitFor(); await page.getByAltText('Страница 2 — фото решения').waitFor();
  await page.getByRole('navigation',{name:'Хлебные крошки'}).getByRole('link',{name:'Делимость и остатки'}).click();
  const row = page.locator('.lesson-problem').filter({has:page.getByRole('heading',{name:'Делимость суммы'})}); await row.getByText('На проверке').waitFor();
  await row.getByRole('link').click(); await page.getByText('Решение отправлено и ожидает проверки преподавателем.').waitFor();
  results.push('Real SubmitSolution 201, exact attachment order/body, authoritative Submitted/attempt 1, originals hash matches, browser reload and Lesson status updated');
  await api(`/problem-works/${state.work.problemWorkId}/reviews/needs-revision`,{method:'POST',role:teacher,body:{submissionId:state.work.latestSubmission.id,expectedVersion:state.work.version,feedbackMarkdown:'Объясните второй переход.'}});
  await page.reload(); await page.getByRole('heading',{name:'Что нужно исправить'}).waitFor(); assert.equal(await page.getByText('Объясните второй переход.',{exact:true}).count(),1);
  assert.equal(await page.getByRole('textbox').inputValue(),''); await page.screenshot({path:`${output}/revision-1440.png`,fullPage:true});
  await page.getByLabel('Добавить фото решения').setInputFiles({name:'Исправленный лист.png',mimeType:'image/png',buffer:png});
  await page.locator('.solution-form').getByAltText('Страница 1 — фото решения').waitFor();
  await page.getByRole('button',{name:'Отправить решение'}).click(); await page.getByText('Последняя попытка №2').waitFor();
  state=await workspace(); assert.equal(state.work.attemptCount,2); assert.equal(state.work.status,'Submitted'); assert.equal(state.work.latestSubmission.bodyMarkdown,null);
  assert(!attachmentIds.includes(state.work.latestSubmission.attachmentIds[0])); assert.equal(requests.length,2);
  await api(`/problem-works/${state.work.problemWorkId}/reviews/accept`,{method:'POST',role:teacher,body:{submissionId:state.work.latestSubmission.id,expectedVersion:state.work.version,feedbackMarkdown:'Теперь верно.'}});
  await page.reload(); await page.getByText('Решение принято.',{exact:true}).waitFor(); assert.equal(await page.getByRole('textbox').count(),0); await page.getByText('Теперь верно.',{exact:true}).waitFor();
  await page.screenshot({path:`${output}/accepted-1440.png`,fullPage:true});
  for(const id of attachmentIds) assert.equal(digest(await bytes(id)),digest(png));
  results.push('Valid API teacher review fixture → NeedsRevision feedback/new upload/attempt 2 → Accepted terminal with feedback; previous originals remain readable');
  // Controlled DTO enrichment exercises currently unsourced fields without inserting database rows.
  const enriched=await workspace(); enriched.problem.statementMarkdown=('ДлинноеУсловие'.repeat(20)+'\n\n').repeat(8)+'$$'+Array(50).fill('x^2').join('+')+'$$';
  enriched.usefulMaterials=[{id:'m1',title:'Конспект',kind:'Document',description:null,url:'/notes'}]; enriched.hints=[{id:'h1',order:1,title:'Начните здесь',bodyMarkdown:'Разберите три остатка.'}];
  await page.route(`**/api/v1${route}/workspace`,r=>r.fulfill({json:enriched})); await page.reload(); await page.getByRole('heading',{level:1,name:'Делимость суммы'}).waitFor();
  for(const width of [320,375,768,1024,1440]) { await page.setViewportSize({width,height:1000}); assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false,`long statement overflow ${width}`); }
  await page.getByRole('tab',{name:'Подсказки'}).click(); await page.getByText('Разберите три остатка.').waitFor(); await page.getByRole('complementary').getByRole('link',{name:'Конспект'}).waitFor();
  await page.unroute(`**/api/v1${route}/workspace`); results.push('Controlled enrichment: long unbroken statement/math no page overflow at all 5 widths, available hints/materials');
  // Student-only route rejects teacher through the real backend.
  const other=await browser.newPage(); await other.goto(`${base}/local-login.html`); await other.getByLabel('Вставьте token').fill(teacher); await other.getByRole('button').click(); await other.goto(`${base}${route}`); await other.getByText('Нет доступа к задаче',{exact:true}).waitFor(); await other.close();
  results.push('Real teacher student-route 403 ErrorState');
  if(process.env.LMS_G_LIFECYCLE==='1') {
    const config=JSON.parse(compose('config','--format','json')); assert.equal(config.name,'lms-g-smoke','Destructive lifecycle smoke is restricted to its dedicated project');
    const before=await workspace(); const contentHash=digest(await bytes(attachmentIds[0]));
    compose('restart','api'); compose('up','-d','--wait'); assert.equal(digest(await bytes(attachmentIds[0])),contentHash);
    compose('down'); compose('up','-d','--wait'); assert.deepEqual((await workspace()).work,before.work); assert.equal(digest(await bytes(attachmentIds[0])),contentHash);
    compose('down','-v'); compose('up','--build','-d','--wait'); const reset=await workspace(); assert.equal(reset.work.problemWorkId,null); assert.equal(reset.work.attemptCount,0);
    const removed=await fetch(`${base}/api/v1/attachments/${attachmentIds[0]}/content`,{headers:{Authorization:`Bearer ${token}`}}); assert.equal(removed.status,404);
    results.push('Isolated Docker: API restart and down/up preserve Accepted/work/version/original hash; down -v + clean build resets DB and uploads');
  }
  assert.deepEqual(errors,[]); await writeFile(`${output}/results.json`,JSON.stringify({results,errors},null,2)); console.log(JSON.stringify({results,errors},null,2));
} finally { await browser.close(); }
