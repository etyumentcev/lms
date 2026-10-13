// H only. Run against a clean dedicated lms-h-smoke Compose project.
// Reviews and messages use existing authorized APIs; no database writes.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdir, writeFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.LMS_SMOKE_URL || 'http://127.0.0.1:8082';
const output = process.env.LMS_SMOKE_OUTPUT || '/tmp/lms-h-browser';
await mkdir(output, { recursive: true });
const compose = (...args) => execFileSync('docker', ['compose', ...args], { encoding:'utf8', stdio:['ignore','pipe','pipe'] }).trim();
assert.equal(JSON.parse(compose('config','--format','json')).name,'lms-h-smoke');
const token = compose('exec','-T','api','dotnet','Lms.Api.dll','--dev-token');
const teacher = compose('exec','-T','api','dotnet','Lms.Api.dll','--dev-token','Teacher');
const assignment='40000000-0000-0000-0000-000000000001';
const route=`/problem-assignments/${assignment}`;
async function api(path,{method='GET',body,role=token}={}) {
  const response=await fetch(`${base}/api/v1${path}`,{method,headers:{Authorization:`Bearer ${role}`,...(body?{'Content-Type':'application/json'}:{})},body:body?JSON.stringify(body):undefined});
  assert(response.ok,`${path}: ${response.status}`);return response.json();
}
const workspace=()=>api(`${route}/workspace`);
const digest=data=>createHash('sha256').update(data).digest('hex');
async function original(id){const res=await fetch(`${base}/api/v1/attachments/${id}/content`,{headers:{Authorization:`Bearer ${token}`}});assert.equal(res.status,200);return digest(Buffer.from(await res.arrayBuffer()));}
const browser=await chromium.launch({headless:true,channel:'chrome'});
const results=[],errors=[];
try {
  const page=await browser.newPage({viewport:{width:1440,height:1000}});page.on('pageerror',e=>errors.push(e.message));
  const history=()=>page.getByRole('tab',{name:'История'}).click();
  const solution=()=>page.getByRole('tab',{name:'Решение'}).click();
  const attempt=n=>page.getByRole('article',{name:`Попытка №${n}`,exact:true});
  async function image(label){return Buffer.from(await page.evaluate(label=>{const c=document.createElement('canvas');c.width=850;c.height=1200;const x=c.getContext('2d');x.fillStyle='white';x.fillRect(0,0,c.width,c.height);x.fillStyle='#08733f';x.font='32px serif';x.fillText(label,50,80);for(let i=1;i<15;i++)x.fillText(`${i}. n = 3k + r`,50,80+i*60);return c.toDataURL('image/png').split(',')[1];},label),'base64');}
  await page.goto(`${base}/local-login.html`);await page.getByLabel('Вставьте token').fill(token);await page.getByRole('button').click();await page.waitForURL('**/courses/local-demo');
  await page.getByRole('link',{name:/Открыть.*Делимость и остатки/}).click();await page.getByRole('link',{name:'Открыть задачу: Делимость суммы'}).click();await page.getByRole('tab',{name:'История'}).waitFor();
  await history();await page.getByText('Вы ещё не отправляли решение').waitFor();assert.equal((await workspace()).work.problemWorkId,null);assert(await page.getByRole('region',{name:'Условие задачи'}).isVisible());await solution();
  const photos=[await image('Attempt 1 / page 1'),await image('Attempt 1 / page 2'),await image('Attempt 2 / new page')];
  await page.getByLabel('Добавить фото решения').setInputFiles(photos.slice(0,2).map((buffer,i)=>({name:`page-${i+1}.png`,mimeType:'image/png',buffer})));
  await page.getByAltText('Страница 2 — фото решения').waitFor();await page.getByRole('textbox').fill('Пояснение первой попытки.');await page.getByRole('button',{name:'Отправить решение'}).click();await page.getByText('Решение отправлено и ожидает проверки преподавателем.').waitFor();
  await history();await attempt(1).waitFor();assert.match(await attempt(1).innerText(),/Пояснение первой попытки/);let data=await workspace();const first=structuredClone(data.history.submissions[0]);
  for(let i=0;i<2;i++)assert.equal(await original(first.attachmentIds[i]),digest(photos[i]));
  results.push('Real login → Course → Lesson → NotStarted history (no work) → two photo Submit #1 → History #1 with snapshot');
  await api(`/problem-works/${data.work.problemWorkId}/reviews/needs-revision`,{method:'POST',role:teacher,body:{submissionId:first.id,expectedVersion:data.work.version,feedbackMarkdown:'Объясните второй переход.\nСохраните все вычисления.'}});
  await solution();await history();await attempt(1).getByLabel('Статус: Нужна доработка',{exact:true}).waitFor();assert.match(await attempt(1).innerText(),/Сохраните все вычисления/);
  await api(`/problem-works/${data.work.problemWorkId}/messages`,{method:'POST',body:{bodyMarkdown:'Общий вопрос ученика.',attachmentIds:[]}});
  await solution();await history();await page.getByRole('article',{name:'Сообщение · Ученик'}).waitFor();await solution();await page.getByLabel('Добавить фото решения').setInputFiles({name:'revision.png',mimeType:'image/png',buffer:photos[2]});await page.locator('.solution-form').getByAltText('Страница 1 — фото решения').waitFor();await page.getByRole('textbox').fill('Пояснение второй попытки.');await page.getByRole('button',{name:'Отправить решение'}).click();await page.getByText('Последняя попытка №2').waitFor();
  await history();await attempt(2).waitFor();assert.equal(await page.locator('.history-attempt').count(),2);data=await workspace();assert.deepEqual(data.history.submissions[1],first);
  await api(`/problem-works/${data.work.problemWorkId}/reviews/accept`,{method:'POST',role:teacher,body:{submissionId:data.work.latestSubmission.id,expectedVersion:data.work.version,feedbackMarkdown:'Теперь верно.\nРешение принято.'}});
  await solution();await history();await attempt(2).getByLabel('Статус: Принята',{exact:true}).waitFor();await page.getByRole('article',{name:'Сообщение · Ученик'}).waitFor();
  assert.deepEqual(await page.locator('.history-attempt h2').allTextContents(),['Попытка №2','Попытка №1']);assert.equal(await attempt(1).getByRole('img').count(),2);assert.equal(await attempt(2).getByRole('img').count(),1);
  assert.match(await attempt(1).innerText(),/Пояснение первой/);assert.doesNotMatch(await attempt(1).innerText(),/Пояснение второй/);
  for(let i=0;i<2;i++)assert.equal(await original(first.attachmentIds[i]),digest(photos[i]));assert.equal(await original(data.work.latestSubmission.attachmentIds[0]),digest(photos[2]));
  results.push('Valid RequestRevision → feedback + separate Message → new photo Submit #2 → Accept; both snapshots and original hashes remain distinct');
  // Keyboard full-size original viewer opens a native browser tab.
  const link=attempt(1).getByRole('link',{name:'Открыть страницу 1 в полном размере (новая вкладка)'});await link.focus();
  const popupPromise=page.waitForEvent('popup');await page.keyboard.press('Enter');const popup=await popupPromise;await popup.waitForLoadState();assert.match(popup.url(),/^blob:/);await popup.close();
  await page.getByRole('tab',{name:'История'}).focus();await page.keyboard.press('Home');assert(await page.getByRole('tab',{name:'Решение'}).evaluate(n=>document.activeElement===n));await page.goBack();await attempt(2).waitFor();await page.goForward();await page.getByText('Решение принято.',{exact:true}).waitFor();await history();await attempt(2).waitFor();
  for(const width of [320,375,768,1024,1440]){await page.setViewportSize({width,height:1000});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false,`overflow ${width}`);assert(await page.getByRole('region',{name:'Условие задачи'}).isVisible());assert.equal(await page.getByRole('main').count(),1);assert.equal(await page.getByRole('complementary').count(),1);assert(await attempt(1).getByRole('img').first().evaluate(n=>n.naturalHeight>n.naturalWidth&&getComputedStyle(n).objectFit==='contain'));assert(await page.getByRole('main').evaluate(n=>!!(n.compareDocumentPosition(document.querySelector('aside.context-panel'))&Node.DOCUMENT_POSITION_FOLLOWING)));await page.screenshot({path:`${output}/history-${width}.png`,fullPage:true});}
  results.push('320/375/768/1024/1440 geometry, no overflow/crop, context after main, keyboard tabs/Back/Forward and full-size original');
  await page.reload();await attempt(2).waitFor();data=await workspace();assert.deepEqual(data.history.submissions[1],first);
  compose('restart','api');compose('up','-d','--wait');assert.deepEqual((await workspace()).history,data.history);for(let i=0;i<2;i++)assert.equal(await original(first.attachmentIds[i]),digest(photos[i]));
  results.push('Browser reload + API restart persistence; both attempts/reviews/messages and original bytes unchanged');
  // Explicit controlled fixture: large layout, not a persisted backend workflow.
  const enriched=structuredClone(data);const latest=enriched.history.submissions[0];enriched.history.submissions=Array.from({length:12},(_,i)=>({...latest,id:`70000000-0000-0000-0000-${String(12-i).padStart(12,'0')}`,attemptNumber:12-i,bodyMarkdown:('ДлинноеПояснение'.repeat(30)+'\n').repeat(3)}));enriched.history.reviews=[{...enriched.history.reviews[0],submissionId:enriched.history.submissions[0].id}];enriched.history.messages=enriched.history.messages.filter(m=>m.id===enriched.history.reviews[0].messageId);enriched.history.messages[0].bodyMarkdown=('ДлинныйFeedback'.repeat(40)+'\n').repeat(4);
  await page.route(`**/api/v1${route}/workspace`,r=>r.fulfill({json:enriched}));await page.reload();await attempt(12).waitFor();
  for(const width of [320,375,768,1024,1440]){await page.setViewportSize({width,height:1000});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false,`long history overflow ${width}`);}
  await page.screenshot({path:`${output}/history-long-1440.png`,fullPage:true});await page.unroute(`**/api/v1${route}/workspace`);results.push('Controlled C4-shaped fixture: 12 attempts + long body/feedback, all five widths no horizontal overflow');
  assert.deepEqual(errors,[]);await writeFile(`${output}/results.json`,JSON.stringify({results,errors},null,2));console.log(JSON.stringify({results,errors},null,2));
} finally {await browser.close();}
