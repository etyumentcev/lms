// Real API + Chrome measurements. Run immediately after release-smoke on its clean disposable stack.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { execFileSync } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
const { chromium } = createRequire(import.meta.url)(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
const base = process.env.LMS_SMOKE_URL || 'http://127.0.0.1:8086';
assert.equal(base, 'http://127.0.0.1:8086');
const output = process.env.LMS_PERF_OUTPUT || '/tmp/lms-k-performance'; await mkdir(output, { recursive: true });
const compose = (...args) => execFileSync('docker', ['compose', ...args], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
assert.equal(JSON.parse(compose('config', '--format', 'json')).name, 'lms-k-smoke');
const student = compose('exec', '-T', 'api', 'dotnet', 'Lms.Api.dll', '--dev-token');
const teacher = compose('exec', '-T', 'api', 'dotnet', 'Lms.Api.dll', '--dev-token', 'Teacher');
const assignment = n => `40000000-0000-0000-0000-${String(n).padStart(12, '0')}`;
async function api(path, token, body) {
  const response = await fetch(base + '/api/v1' + path, { method: body ? 'POST' : 'GET', headers: { Authorization: `Bearer ${token}`, ...(body ? { 'Content-Type': 'application/json' } : {}) }, body: body && JSON.stringify(body) });
  assert(response.ok, `${path}: ${response.status} ${await response.clone().text()}`); return response.json();
}
const stats = a => { const v = [...a].sort((a,b) => a-b); return { n: v.length, p50: v[Math.ceil(v.length*.5)-1], p95: v[Math.ceil(v.length*.95)-1], max: v.at(-1) }; };
async function latency(path, token) {
  const times = []; let bytes;
  for (let i=0; i<21; i++) { const start = performance.now(); const data = await api(path, token); bytes = Buffer.byteLength(JSON.stringify(data)); if(i) times.push(performance.now()-start); }
  return { ms: stats(times), jsonBytes: bytes, warmups: 1 };
}
const browser = await chromium.launch({ headless: true, channel: 'chrome' });
const page = await browser.newPage({ viewport: { width: 1440, height: 1100 } });
await page.addInitScript(() => {
  const active = new Set(); const create = URL.createObjectURL.bind(URL), revoke = URL.revokeObjectURL.bind(URL);
  URL.createObjectURL = blob => { const id = create(blob); active.add(id); return id; };
  URL.revokeObjectURL = id => { active.delete(id); return revoke(id); };
  window.__activeOriginals = active;
});
const errors = []; page.on('pageerror', e => errors.push(e.message));
const results = { environment: { browser: browser.version(), viewport: [1440,1100], generatedPhoto: [1190,1684], concurrency: 1 }, profiles: [] };
const cdp = await page.context().newCDPSession(page); await cdp.send('Performance.enable');
async function memory() { await cdp.send('HeapProfiler.collectGarbage'); return Object.fromEntries((await cdp.send('Performance.getMetrics')).metrics.filter(m => ['JSHeapUsedSize','Nodes','JSEventListeners','Documents'].includes(m.name)).map(m => [m.name,m.value])); }
async function login(token) { await page.goto(base+'/local-login.html'); await page.getByLabel('Вставьте token').fill(token); await page.getByRole('button').click(); await page.waitForURL('**/courses/local-demo'); }
async function loadPhoto() { await page.waitForFunction(() => document.querySelector('.annotation-image img')?.naturalWidth > 0); }
try {
  await login(student);
  const photo = Buffer.from(await page.evaluate(() => { const c=document.createElement('canvas'); c.width=1190;c.height=1684;const x=c.getContext('2d');x.fillStyle='white';x.fillRect(0,0,c.width,c.height);x.fillStyle='black';x.font='24px serif';for(let i=0;i<40;i++)x.fillText(`${i}. A4 measured fixture: n² - r² = 3k(3k + 2r)`,60,70+i*38);return c.toDataURL('image/png').split(',')[1]; }), 'base64');
  results.environment.photoBytes = photo.length;
  async function upload() { const form=new FormData();form.append('file',new Blob([photo],{type:'image/png'}),'a4.png');const t=performance.now();const r=await fetch(base+'/api/v1/uploads',{method:'POST',headers:{Authorization:`Bearer ${student}`},body:form});assert.equal(r.status,201);return {id:(await r.json()).attachmentId,ms:performance.now()-t}; }
  const works=[];
  for(const [i,count] of [1,5,10].entries()) {
    const path=`/problem-assignments/${assignment(i+1)}`;assert.equal((await api(path+'/workspace',student)).work.problemWorkId,null);
    const uploads=[]; for(let j=0;j<count;j++) uploads.push(await upload());
    const t=performance.now(); const work=await api(path+'/submissions',student,{attachmentIds:uploads.map(u=>u.id)}); const submitMs=performance.now()-t; works.push(work);
    const row={profile:`P${i+1}`,photos:count,uploadMs:stats(uploads.map(u=>u.ms)),submitMs,read:await latency('/problem-works/'+work.problemWorkId,teacher)};
    await login(teacher); const start=performance.now(); await page.goto(base+'/teacher/reviews/'+work.problemWorkId);await loadPhoto();row.initialImageMs=performance.now()-start;
    row.memoryBefore=await memory(); const switches=[];
    for(let cycle=0;cycle<3 && count>1;cycle++) {
      for(const name of ['Следующая','Предыдущая']) for(let j=0;j<count-1;j++) { const start=performance.now();await page.getByRole('button',{name,exact:true}).click();await loadPhoto(); switches.push(performance.now()-start); }
    }
    row.pageSwitchMs = switches.length ? stats(switches) : null;row.memoryAfter=await memory();
    row.activeObjectUrls=await page.evaluate(()=>window.__activeOriginals.size);assert.equal(row.activeObjectUrls,1);
    await page.getByRole('link',{name:'К очереди проверок'}).click();await page.getByRole('heading',{name:'Проверка работ',exact:true}).waitFor();
    row.urlsAfterUnmount=await page.evaluate(()=>window.__activeOriginals.size);assert.equal(row.urlsAfterUnmount,0);
    results.profiles.push(row);
  }
  // 500 realistic strokes, 100 points each. Synchronous point dispatch isolates handler cost;
  // one animation frame per stroke measures rendering under sustained drawing, not physical pen latency.
  await page.goto(base+'/teacher/reviews/'+works[0].problemWorkId);await loadPhoto();
  const canvas=page.locator('canvas[aria-label^="Слой"]');await canvas.scrollIntoViewIfNeeded();
  const draw = await canvas.evaluate(async c => {
    c.setPointerCapture=()=>{};const r=c.getBoundingClientRect();const frames=[],handlers=[],longTasks=[];
    const observer=new PerformanceObserver(list=>longTasks.push(...list.getEntries().map(e=>e.duration)));observer.observe({type:'longtask',buffered:false});
    const start=performance.now();
    for(let stroke=0;stroke<500;stroke++) {
      const t=performance.now();for(let point=0;point<100;point++) c.dispatchEvent(new PointerEvent(point?'pointermove':'pointerdown',{bubbles:true,pointerType:'pen',pointerId:77,button:0,pressure:.6,clientX:r.left+r.width*(.1+point*.007),clientY:r.top+r.height*(.1+(stroke%75)*.01)}));
      c.dispatchEvent(new PointerEvent('pointerup',{bubbles:true,pointerType:'pen',pointerId:77}));handlers.push(performance.now()-t);
      const f=performance.now();await new Promise(requestAnimationFrame);frames.push(performance.now()-f);
    }
    await new Promise(resolve=>setTimeout(resolve,100));observer.disconnect();return {durationMs:performance.now()-start,frames,handlers,longTasks};
  });
  await page.getByText(/Графические пометки: 500 штр/).waitFor();
  const post=page.waitForRequest(r=>r.method()==='POST'&&r.url().includes('/reviews/accept'));
  const saveStart=performance.now();await page.getByRole('button',{name:'Принять решение'}).click();const req=await post;await page.getByText('Проверка сохранена. Состояние работы обновлено.').waitFor();
  const saved=await api('/problem-works/'+works[0].problemWorkId,teacher);assert.equal(saved.reviews[0].annotations[0].strokes.length,500);
  results.profiles.push({profile:'P4',strokes:500,points:50000,durationMs:draw.durationMs,frameWaitMs:stats(draw.frames),strokeHandlerMs:stats(draw.handlers),longTasks:draw.longTasks,saveAndRefreshMs:performance.now()-saveStart,requestBytes:Buffer.byteLength(req.postData()),read:await latency('/problem-works/'+works[0].problemWorkId,teacher),memory:await memory()});
  // P5: valid 20-attempt history, 10 strokes x 100 points per review, real upload per attempt.
  let version=null, historyWork;
  for(let attempt=1;attempt<=20;attempt++) {
    const file=await upload();const sub=await api(`/problem-assignments/${assignment(4)}/submissions`,student,{attachmentIds:[file.id],expectedVersion:version});historyWork=sub.problemWorkId;
    const annotations=[{attachmentId:file.id,strokes:Array.from({length:10},()=>({id:randomUUID(),tool:'pen',color:'#dc2626',width:.003,points:Array.from({length:100},(_,n)=>({x:.1+n*.007,y:.2,pressure:.6}))}))}];
    const review=await api(`/problem-works/${historyWork}/reviews/${attempt===20?'accept':'needs-revision'}`,teacher,{submissionId:sub.submissionId,expectedVersion:sub.version,feedbackMarkdown:'Explain next step '+attempt,annotations});version=review.version;
  }
  const row={profile:'P5',attempts:20,strokesPerReview:10,pointsPerStroke:100,teacherRead:await latency('/problem-works/'+historyWork,teacher),studentRead:await latency(`/problem-assignments/${assignment(4)}/workspace`,student)};
  await login(student);let start=performance.now();await page.goto(base+`/problem-assignments/${assignment(4)}?tab=history`);await page.waitForFunction(()=>document.querySelectorAll('.history-attempt').length===20&&[...document.querySelectorAll('.annotation-image img')].filter(i=>i.naturalWidth>0).length===20);row.historyImagesMs=performance.now()-start;row.memory=await memory();
  assert.equal((await api('/problem-works/'+historyWork,teacher)).reviews.length,20);results.profiles.push(row);results.errors=errors;assert.deepEqual(errors,[]);
  await page.screenshot({path:output+'/history.png'}); await writeFile(output+'/results.json',JSON.stringify(results,null,2));console.log(JSON.stringify(results,null,2));
} finally { await browser.close(); }
