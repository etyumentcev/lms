// I only. Dedicated clean lms-i-smoke Compose project; valid existing commands only.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdir, writeFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.LMS_SMOKE_URL || 'http://127.0.0.1:8083';
const output = process.env.LMS_SMOKE_OUTPUT || '/tmp/lms-i-browser';
await mkdir(output,{recursive:true});
const compose=(...args)=>execFileSync('docker',['compose',...args],{encoding:'utf8',stdio:['ignore','pipe','pipe']}).trim();
assert.equal(JSON.parse(compose('config','--format','json')).name,'lms-i-smoke');
const student=compose('exec','-T','api','dotnet','Lms.Api.dll','--dev-token');
const teacher=compose('exec','-T','api','dotnet','Lms.Api.dll','--dev-token','Teacher');
const assignment=n=>`40000000-0000-0000-0000-${String(n).padStart(12,'0')}`;
const route=n=>`/problem-assignments/${assignment(n)}`;
async function request(path,{role=student,method='GET',body}={}) {
  return fetch(`${base}/api/v1${path}`,{method,headers:{...(role?{Authorization:`Bearer ${role}`} : {}),...(body?{'Content-Type':'application/json'}:{})},body:body?JSON.stringify(body):undefined});
}
async function api(path,options){const r=await request(path,options);assert(r.ok,`${path}: ${r.status}`);return r.json();}
const workspace=n=>api(`${route(n)}/workspace`);
const queue=query=>api(`/teacher/review-queue${query||''}`,{role:teacher});
const submit=(n,version=null)=>api(`${route(n)}/submissions`,{method:'POST',body:{bodyMarkdown:`Решение задачи ${n}`,attachmentIds:[],expectedVersion:version}});
async function review(n,kind){const w=(await workspace(n)).work;return api(`/problem-works/${w.problemWorkId}/reviews/${kind}`,{role:teacher,method:'POST',body:{submissionId:w.latestSubmission.id,expectedVersion:w.version,feedbackMarkdown:'Объясните переход.'}});}
const browser=await chromium.launch({headless:true,channel:'chrome'});
const results=[],errors=[];
try {
  const page=await browser.newPage({viewport:{width:1440,height:1100}});page.on('pageerror',e=>errors.push(e.message));
  const login=async token=>{await page.goto(`${base}/local-login.html`);await page.getByLabel('Вставьте token').fill(token);await page.getByRole('button').click();await page.waitForURL('**/courses/local-demo');};
  await login(student);
  await page.getByRole('link',{name:/Открыть.*Делимость и остатки/}).click();await page.getByRole('link',{name:'Открыть задачу: Делимость суммы'}).click();
  assert.equal((await workspace(1)).work.problemWorkId,null,'Requires clean dedicated I project');
  const photo=Buffer.from(await page.evaluate(()=>{const c=document.createElement('canvas');c.width=600;c.height=800;const x=c.getContext('2d');x.fillStyle='white';x.fillRect(0,0,600,800);x.fillStyle='black';x.font='30px sans-serif';x.fillText('Checkpoint I: 3n + 3 = 3(n + 1)',20,80);return c.toDataURL('image/png').split(',')[1];}),'base64');
  await page.getByLabel('Добавить фото решения').setInputFiles({name:'solution.png',mimeType:'image/png',buffer:photo});
  await page.getByAltText('Страница 1 — фото решения').waitFor();await page.getByRole('button',{name:'Отправить решение'}).click();await page.getByText('Решение отправлено и ожидает проверки преподавателем.').waitFor();
  await page.getByRole('tab',{name:'История'}).click();await page.getByRole('article',{name:'Попытка №1'}).waitFor();
  const first=(await workspace(1)).work;
  await submit(2);await review(2,'needs-revision');await submit(2,(await workspace(2)).work.version);
  await submit(3);await review(3,'needs-revision');await submit(4);await review(4,'accept');
  const initial=await queue();assert.equal(initial.total,2);assert.equal(initial.summary.firstAttempts,1);assert.equal(initial.summary.revisions,1);assert.equal(initial.items[0].problemWorkId,first.problemWorkId);
  results.push('Student development login → Course → Lesson → photo submit → History; valid first/revision queue, NeedsRevision/Accepted excluded');
  assert.equal((await request('/teacher/review-queue')).status,403);assert.equal((await request('/teacher/review-queue',{role:null})).status,401);
  await page.goto(`${base}/teacher/reviews`);await page.getByRole('heading',{name:'Нет доступа к очереди'}).waitFor();assert.equal(await page.getByRole('link',{name:'Проверка',exact:true}).count(),0);
  await login(teacher);await page.getByRole('link',{name:'Проверка',exact:true}).click();await page.getByText('Найдено работ: 2 · Страница 1').waitFor();
  assert.equal(await page.locator('.queue-row').count(),2);assert.equal(await page.getByLabel('Статус: На проверке',{exact:true}).count(),2);
  results.push('Teacher development login → real queue; direct Student API/UI 403 and anonymous API 401');
  await page.getByLabel('Курс · ID').fill('local-demo');await page.getByLabel('Поиск',{exact:true}).fill('Делимость суммы');await page.getByRole('button',{name:'Применить',exact:true}).click();await page.getByText('Найдено работ: 1 · Страница 1').waitFor();assert.match(page.url(),/courseId=local-demo/);assert.equal(await page.locator('.queue-row').count(),1);
  await page.getByLabel('Группа · ID').fill('missing-group');await page.getByRole('button',{name:'Применить',exact:true}).click();await page.getByText('По выбранным фильтрам работ нет').waitFor();await page.getByRole('button',{name:'Сбросить фильтры'}).last().click();await page.getByText('Найдено работ: 2 · Страница 1').waitFor();
  await page.getByLabel('Попытки',{exact:true}).selectOption('revision');await page.getByRole('button',{name:'Применить',exact:true}).click();await page.getByText('Найдено работ: 1 · Страница 1').waitFor();assert(await page.getByText('Попытка №2').isVisible());
  await page.goto(`${base}/teacher/reviews?courseId=local-demo&pageSize=1`);await page.getByText('Найдено работ: 2 · Страница 1').waitFor();assert.equal(await page.locator('.queue-row').count(),1);assert.equal(await page.locator('.queue-total').innerText(),'2');await page.getByRole('button',{name:'Следующая'}).click();await page.getByText('Найдено работ: 2 · Страница 2').waitFor();assert(await page.getByText('Попытка №2').isVisible());assert.equal(await page.locator('.queue-total').innerText(),'2');
  await page.goBack();await page.getByText('Найдено работ: 2 · Страница 1').waitFor();await page.goForward();await page.getByText('Найдено работ: 2 · Страница 2').waitFor();
  const target=(await queue('?courseId=local-demo&page=2&pageSize=1')).items[0].problemWorkId;
  const reviewLink=page.getByRole('link',{name:/Проверить:/});assert.equal(await reviewLink.getAttribute('href'),`/teacher/reviews/${target}`);await reviewLink.focus();await page.keyboard.press('Enter');await page.getByText('Раздел готовится').waitFor();assert(page.url().endsWith(`/teacher/reviews/${target}`));assert(await page.getByRole('main').evaluate(n=>document.activeElement===n));
  await page.getByRole('link',{name:'Вернуться к очереди →'}).click();await page.getByText('Найдено работ: 2 · Страница 2').waitFor();assert.match(page.url(),/courseId=local-demo/);
  results.push('Server course/group/attempt/search filters, combined search, reset, real pageSize=1 pagination and all-pages summary; URL Back/Forward; ProblemWorkId → placeholder J → preserved queue URL');
  await page.goto(`${base}/teacher/reviews`);await page.getByText('Найдено работ: 2 · Страница 1').waitFor();
  for(const width of [320,375,768,1024,1440]){
    await page.setViewportSize({width,height:1100});
    assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false,`overflow ${width}`);
    assert.equal(await page.getByRole('heading',{level:1}).count(),1);assert.equal(await page.getByRole('main').count(),1);assert.equal(await page.getByRole('complementary').count(),1);
    assert(await page.locator('.queue-student').first().isVisible());assert(await page.getByRole('heading',{name:'Делимость суммы',exact:true}).isVisible());assert(await page.getByRole('link',{name:/Проверить:/}).first().isVisible());
    assert(await page.getByRole('main').evaluate(n=>!!(n.compareDocumentPosition(document.querySelector('aside.context-panel'))&Node.DOCUMENT_POSITION_FOLLOWING)));
    await page.screenshot({path:`${output}/queue-${width}.png`,fullPage:true});
  }
  await page.getByLabel('Поиск',{exact:true}).focus();await page.keyboard.press('Tab');assert(await page.getByLabel('Курс · ID').evaluate(n=>document.activeElement===n));
  results.push('320/375/768/1024/1440: no horizontal overflow; student/problem/CTA visible; context after main; one h1, labelled filters and keyboard focus');
  const hash=buffer=>createHash('sha256').update(buffer).digest('hex');
  const attachment=first.latestSubmission.attachmentIds[0];const original=async()=>{const r=await request(`/attachments/${attachment}/content`);assert.equal(r.status,200);return hash(Buffer.from(await r.arrayBuffer()));};
  assert.equal(await original(),hash(photo));compose('restart','api');compose('up','-d','--wait');await page.reload();await page.getByText('Найдено работ: 2 · Страница 1').waitFor();assert.equal((await queue()).total,2);assert.equal(await original(),hash(photo));
  // Existing backend command acts as a future J completion while the user is away.
  await page.getByRole('link',{name:/Проверить:/}).first().click();await page.getByText('Раздел готовится').waitFor();await review(1,'accept');await page.getByRole('link',{name:'Вернуться к очереди →'}).click();await page.getByText('Найдено работ: 1 · Страница 1').waitFor();assert.equal(await page.getByRole('heading',{name:'Делимость суммы',exact:true}).count(),0);
  results.push('API restart preserves queue and uploaded photo hash; return from placeholder fetches fresh queue after valid acceptance command');
  // Explicit C4-shaped HTTP fixture for long names; real source has nullable profile names.
  const enriched=await queue();enriched.items[0].studentDisplayName='ОченьДлинноеИмя'.repeat(15);enriched.items[0].problemTitle='ОченьДлинноеНазваниеЗадачи'.repeat(20);
  await page.route('**/api/v1/teacher/review-queue?*',r=>r.fulfill({json:enriched}));await page.reload();await page.getByText('Найдено работ: 1 · Страница 1').waitFor();
  for(const width of [320,375,768,1024,1440]){await page.setViewportSize({width,height:1100});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false,`long fixture overflow ${width}`);}
  assert.deepEqual(errors,[]);results.push('Controlled long-name/title HTTP fixture stays in bounds; zero browser page errors');
  await writeFile(`${output}/results.json`,JSON.stringify({results,errors},null,2));console.log(JSON.stringify({results,errors},null,2));
} finally {await browser.close();}
