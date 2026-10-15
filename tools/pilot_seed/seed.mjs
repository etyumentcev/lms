import { createHash, randomUUID } from 'node:crypto';
import { chmod, mkdir, readFile, rename, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const { chromium } = require(process.env.LMS_PLAYWRIGHT_MODULE || 'playwright');
const root = path.dirname(new URL(import.meta.url).pathname);
const localRoot = path.join(root, '.local');
const manifest = JSON.parse(await readFile(path.join(root, 'manifest.json'), 'utf8'));
const password = process.env.PILOT_PASSWORD;
const platformToken = process.env.LMS_PLATFORM_TOKEN;
if (!password || password.length < 12) throw new Error('PILOT_PASSWORD must contain at least 12 characters');
if (!platformToken) throw new Error('LMS_PLATFORM_TOKEN is required');

await mkdir(localRoot, { recursive: true });
const statePath = path.join(localRoot, 'state.json');
let state = await readFile(statePath, 'utf8').then(JSON.parse).catch(() => ({
  fixtureVersion: manifest.fixtureVersion, organizations: {}, courses: {},
}));
if (state.fixtureVersion !== manifest.fixtureVersion)
  throw new Error(`Unsupported local state version ${state.fixtureVersion}`);

function stableUuid(label) {
  const bytes = Buffer.from(createHash('sha256').update(`sapientum-pilot-v${manifest.fixtureVersion}:${label}`).digest().subarray(0, 16));
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = bytes.toString('hex');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
function stableKey(label) { return `pilot-v${manifest.fixtureVersion}-${stableUuid(label)}`; }
async function saveState() {
  const temporary = `${statePath}.${process.pid}.tmp`;
  await writeFile(temporary, JSON.stringify(state, null, 2) + '\n', { mode: 0o600 });
  await rename(temporary, statePath); await chmod(statePath, 0o600);
}
function log(message) { process.stdout.write(`[pilot-seed] ${message}\n`); }
function students(course) {
  return Array.from({ length: course.studentCount }, (_, index) => {
    const number = String(index + 1).padStart(2, '0');
    return { userName: `${course.studentPrefix}.${number}`, rosterKey: `${course.studentRosterPrefix}-${number}`,
      displayName: `${course.groupName} · ученик ${number}`, role: 'Student' };
  });
}

const browser = await chromium.launch({ headless: true,
  executablePath: process.env.LMS_BROWSER_EXECUTABLE || '/usr/bin/google-chrome',
  args: ['--ignore-certificate-errors'] });
const context = await browser.newContext({ ignoreHTTPSErrors: true, locale: 'ru-RU' });
const page = await context.newPage(); page.setDefaultTimeout(30_000);
await page.goto(manifest.origin, { waitUntil: 'domcontentloaded' });

class ApiFailure extends Error {
  constructor(pathName, response) {
    super(`${response.status} ${pathName}: ${response.data?.code || response.data?.title || 'HTTP error'}`);
    this.status = response.status; this.code = response.data?.code; this.retryAfter = response.retryAfter;
  }
}
async function request(pathName, options = {}) {
  const response = await page.evaluate(async ({ pathName, options }) => {
    const headers = new Headers({ Accept: 'application/json, application/problem+json' });
    if (options.bearer) headers.set('Authorization', `Bearer ${options.bearer}`);
    if (options.csrf) {
      const csrfHeaders = new Headers({ Accept: 'application/json' });
      if (options.bearer) csrfHeaders.set('Authorization', `Bearer ${options.bearer}`);
      const csrfResponse = await fetch('/api/v1/auth/csrf', { headers: csrfHeaders,
        credentials: 'same-origin', cache: 'no-store', redirect: 'error' });
      if (!csrfResponse.ok) return { status: csrfResponse.status, data: await csrfResponse.json().catch(() => ({})), retryAfter: null };
      headers.set('X-CSRF-TOKEN', (await csrfResponse.json()).token);
    }
    let body;
    if (options.form) {
      const bytes = Uint8Array.from(atob(options.form.base64), character => character.charCodeAt(0));
      body = new FormData();
      body.append('file', new File([bytes], options.form.fileName, { type: 'application/zip' }));
      for (const [key, value] of Object.entries(options.form.fields)) body.append(key, value);
    } else if (options.body !== undefined) {
      headers.set('Content-Type', 'application/json'); body = JSON.stringify(options.body);
    }
    const result = await fetch(`/api/v1${pathName}`, { method: options.method || 'GET', headers, body,
      credentials: 'same-origin', cache: 'no-store', redirect: 'error' });
    const data = result.status === 204 ? null : await result.json().catch(() => ({}));
    return { status: result.status, data, retryAfter: result.headers.get('retry-after') };
  }, { pathName, options });
  if (response.status < 200 || response.status >= 300) throw new ApiFailure(pathName, response);
  return response.data;
}
async function clearSession() { await context.clearCookies(); await page.goto(manifest.origin, { waitUntil: 'domcontentloaded' }); }
async function login(userName, organizationId) {
  await clearSession();
  return request('/auth/login', { method: 'POST', csrf: true,
    body: { userName, password, organizationId } });
}
let lastActivation = 0;
async function activate(pathName, body) {
  const remaining = 150 - (Date.now() - lastActivation);
  if (remaining > 0) await new Promise(resolve => setTimeout(resolve, remaining));
  for (;;) {
    try {
      const result = await request(pathName, { method: 'POST', csrf: true, body });
      lastActivation = Date.now(); return result;
    } catch (error) {
      if (!(error instanceof ApiFailure) || error.status !== 429) throw error;
      const seconds = Number(error.retryAfter || 7);
      log(`лимит активации; повтор через ${seconds} с`);
      await new Promise(resolve => setTimeout(resolve, Math.max(7, seconds) * 1000));
    }
  }
}
async function bootstrap(organization) {
  const saved = state.organizations[organization.key];
  if (saved?.organizationId) {
    try {
      const session = await login(organization.adminUserName, saved.organizationId);
      if (session.hasOrganizationAdminScope) return { ...saved, adminToken: session.accessToken };
    } catch { log(`${organization.name}: сохранённый bootstrap недоступен, создаю заново`); }
    delete state.organizations[organization.key];
    for (const course of organization.courses) delete state.courses[course.key];
    await saveState();
  }
  await clearSession();
  const created = await request('/platform/organizations', { method: 'POST', bearer: platformToken,
    body: { commandId: stableUuid(`bootstrap:${organization.key}`), organizationName: organization.name,
      adminUserName: organization.adminUserName } });
  if (created.activationCode) await activate('/auth/activate-first-admin', {
    organizationId: created.organizationId, userName: organization.adminUserName,
    activationCode: created.activationCode, password,
  });
  const session = await login(organization.adminUserName, created.organizationId);
  if (!session.hasOrganizationAdminScope || !session.accessToken)
    throw new Error(`${organization.name}: administrator scope was not issued`);
  const savedOrganization = { organizationId: created.organizationId, adminUserName: organization.adminUserName };
  state.organizations[organization.key] = savedOrganization; await saveState();
  log(`${organization.name}: организация и администратор готовы`);
  return { ...savedOrganization, adminToken: session.accessToken };
}
async function accounts(token) {
  const result = await request('/admin/accounts?offset=0&limit=100', { bearer: token });
  return result.items;
}
async function ensureAccounts(organization, runtime) {
  let items = await accounts(runtime.adminToken);
  const expected = [
    ...organization.teachers.map(teacher => ({ ...teacher, role: 'Teacher' })),
    ...organization.courses.flatMap(students),
  ];
  let activated = 0;
  for (const participant of expected) {
    let account = items.find(item => item.externalRosterKey === participant.rosterKey);
    let code;
    if (!account) {
      const provisioned = await request('/admin/accounts', { method: 'POST', csrf: true, bearer: runtime.adminToken,
        body: { externalRosterKey: participant.rosterKey, displayName: participant.displayName,
          userName: participant.userName, role: participant.role,
          idempotencyKey: stableKey(`account:${organization.key}:${participant.rosterKey}`) } });
      account = provisioned; code = provisioned.activationCode;
    }
    if (account.state !== 'Active') {
      if (!code) {
        const issued = await request(`/admin/accounts/${account.actorId}/activation`, {
          method: 'POST', csrf: true, bearer: runtime.adminToken, body: { idempotencyKey: randomUUID() } });
        code = issued.activationCode;
      }
      if (!code) throw new Error(`${participant.userName}: activation code was not returned`);
      await activate('/auth/activate', { organizationId: runtime.organizationId,
        userName: participant.userName, code, password });
      activated++;
      if (activated % 5 === 0) log(`${organization.name}: активировано ${activated} новых участников`);
    }
  }
  runtime.adminToken = (await login(organization.adminUserName, runtime.organizationId)).accessToken;
  items = await accounts(runtime.adminToken);
  for (const participant of expected) {
    const account = items.find(item => item.externalRosterKey === participant.rosterKey);
    if (!account || account.state !== 'Active') throw new Error(`${participant.userName}: account is not Active`);
  }
  log(`${organization.name}: ${expected.length} synthetic accounts готовы`);
  return new Map(items.map(item => [item.userName, item]));
}
async function detail(token, courseId) { return request(`/admin/courses/${encodeURIComponent(courseId)}`, { bearer: token }); }
async function ensureCourses(organization, runtime, accountMap) {
  const catalogResponse = await request('/admin/courses', { bearer: runtime.adminToken });
  const catalog = catalogResponse.items;
  const results = [];
  for (const course of organization.courses) {
    let item = catalog.find(value => value.title === course.title);
    if (!item) item = await request('/admin/courses', { method: 'POST', csrf: true, bearer: runtime.adminToken,
      body: { title: course.title, description: course.description,
        idempotencyKey: stableKey(`course:${organization.key}:${course.key}`) } });
    let current = await detail(runtime.adminToken, item.courseId);
    let group = current.groups.find(value => value.name === course.groupName);
    if (!group) {
      group = await request(`/admin/courses/${item.courseId}/groups`, { method: 'POST', csrf: true, bearer: runtime.adminToken,
        body: { name: course.groupName, description: course.groupDescription,
          idempotencyKey: stableKey(`group:${organization.key}:${course.key}`) } });
      current = await detail(runtime.adminToken, item.courseId);
      group = current.groups.find(value => value.name === course.groupName);
    }
    const wantedStudents = students(course).map(value => accountMap.get(value.userName));
    const enrolled = new Set(current.students.map(value => value.actorId));
    const missing = wantedStudents.filter(value => value && !enrolled.has(value.actorId));
    if (missing.length) {
      await request(`/admin/courses/${item.courseId}/groups/${group.groupId}/students`, {
        method: 'POST', csrf: true, bearer: runtime.adminToken,
        body: { studentIds: missing.map(value => value.actorId),
          idempotencyKey: stableKey(`enroll:${course.key}:${missing.map(value => value.actorId).sort().join(':')}`) } });
      current = await detail(runtime.adminToken, item.courseId);
    }
    const teacher = accountMap.get(course.teacherUserName);
    if (!current.teachers.some(value => value.actorId === teacher.actorId && value.groupId === group.groupId)) {
      await request(`/admin/courses/${item.courseId}/groups/${group.groupId}/teachers`, {
        method: 'POST', csrf: true, bearer: runtime.adminToken,
        body: { teacherId: teacher.actorId, idempotencyKey: stableKey(`teacher:${course.key}:${teacher.actorId}`) } });
      current = await detail(runtime.adminToken, item.courseId);
    }
    if (!current.editors.some(value => value.actorId === teacher.actorId)) {
      await request(`/admin/courses/${item.courseId}/editors`, { method: 'POST', csrf: true, bearer: runtime.adminToken,
        body: { editorId: teacher.actorId, idempotencyKey: stableKey(`editor:${course.key}:${teacher.actorId}`) } });
      current = await detail(runtime.adminToken, item.courseId);
    }
    if (current.students.length !== course.studentCount)
      throw new Error(`${course.title}: expected ${course.studentCount} enrollments, got ${current.students.length}`);
    results.push({ spec: course, courseId: item.courseId, groupId: group.groupId,
      courseVersion: current.course.version, state: current.course.state });
    state.courses[course.key] = { ...(state.courses[course.key] || {}), courseId: item.courseId, groupId: group.groupId };
    await saveState(); log(`${course.title}: группа и ${course.studentCount} зачислений готовы`);
  }
  return results;
}
async function ensureProcessing(organization, runtime, courses) {
  let processing = await request('/admin/processing-context', { bearer: runtime.adminToken });
  const effectiveFrom = new Date(Date.now() - 60_000).toISOString();
  if (!processing.currentContext) {
    const created = await request('/admin/processing-context/versions', { method: 'POST', csrf: true, bearer: runtime.adminToken,
      body: { operationId: stableUuid(`processing:${organization.key}`), expectedRevision: processing.revision,
        expectedContextId: null, expectedContextVersion: null, effectiveFrom } });
    processing = created.state;
  }
  for (const course of courses) {
    const summary = processing.courses.find(value => value.courseId === course.courseId);
    if (!summary?.binding) {
      const bound = await request(`/admin/courses/${course.courseId}/processing-context`, {
        method: 'POST', csrf: true, bearer: runtime.adminToken,
        body: { operationId: stableUuid(`processing-bind:${course.spec.key}`), expectedRevision: processing.revision,
          contextId: processing.currentContext.contextId, contextVersion: processing.currentContext.version,
          expectedBindingFrom: null, effectiveFrom } });
      processing = bound.state;
    }
  }
  log(`${organization.name}: synthetic processing context привязан`);
}
async function publishCourses(organization, runtime, courses) {
  const byTeacher = Map.groupBy(courses, course => course.spec.teacherUserName);
  for (const [teacherUserName, teacherCourses] of byTeacher) {
    const session = await login(teacherUserName, runtime.organizationId);
    if (!session.hasEducationalScope || !session.accessToken) throw new Error(`${teacherUserName}: educational scope missing`);
    for (const course of teacherCourses) {
      if (state.courses[course.spec.key]?.assigned) continue;
      const bytes = await readFile(path.join(localRoot, 'packages', `${course.spec.key}.zip`));
      const imported = await request(`/teacher/courses/${course.courseId}/imports`, {
        method: 'POST', csrf: true, bearer: session.accessToken,
        form: { base64: bytes.toString('base64'), fileName: `${course.spec.key}.zip`, fields: {
          idempotencyKey: stableKey(`import:${course.spec.key}:local-s4-v1`), sourceRevision: `pilot-v${manifest.fixtureVersion}-${course.spec.key}` } } });
      if (imported.state !== 'Ready') throw new Error(`${course.spec.title}: import ${imported.state}`);
      const published = await request(`/teacher/courses/${course.courseId}/imports/${imported.importId}/publish`, {
        method: 'POST', csrf: true, bearer: session.accessToken,
        body: { expectedImportVersion: imported.version, expectedCourseVersion: course.courseVersion,
          idempotencyKey: stableKey(`publish:${course.spec.key}`) } });
      const problemIds = Array.from({ length: 5 }, (_, index) => `lesson-01-problem-${index + 1}`);
      const assigned = await request(`/teacher/courses/${course.courseId}/open-and-assign`, {
        method: 'POST', csrf: true, bearer: session.accessToken,
        body: { contentVersionId: published.contentVersionId, lessonId: 'lesson-01', groupId: course.groupId,
          problemIds, expectedCourseVersion: published.courseVersion,
          idempotencyKey: stableKey(`assign:${course.spec.key}`) } });
      if (assigned.assignmentCount !== course.spec.studentCount * 5)
        throw new Error(`${course.spec.title}: expected ${course.spec.studentCount * 5} assignments, got ${assigned.assignmentCount}`);
      state.courses[course.spec.key] = { ...state.courses[course.spec.key], assigned: true,
        contentVersionId: published.contentVersionId };
      await saveState(); log(`${course.spec.title}: опубликовано и назначено ${assigned.assignmentCount} задач`);
    }
  }
}
async function validate(organization, runtime, accountMap, courses) {
  runtime.adminToken = (await login(organization.adminUserName, runtime.organizationId)).accessToken;
  for (const course of courses) {
    const current = await detail(runtime.adminToken, course.courseId);
    const expectedCourseState = manifest.contentPublication === 'enabled' ? 'Published' : 'Draft';
    if (current.course.state !== expectedCourseState || current.groups.length !== 1 || current.students.length !== course.spec.studentCount)
      throw new Error(`${course.spec.title}: final roster/state mismatch`);
    const student = students(course.spec)[0];
    const studentSession = await login(student.userName, runtime.organizationId);
    if (!studentSession.hasEducationalScope) throw new Error(`${student.userName}: educational scope missing`);
    if (manifest.contentPublication === 'enabled') {
      const assignments = await request(`/my/courses/${course.courseId}/assignments`, { bearer: studentSession.accessToken });
      if (assignments.items.length !== 5) throw new Error(`${student.userName}: expected 5 assignments, got ${assignments.items.length}`);
      const published = state.courses[course.spec.key];
      if (!published?.contentVersionId) throw new Error(`${course.spec.title}: published content version missing from state`);
      const content = await request(
        `/my/courses/${course.courseId}/versions/${published.contentVersionId}/content`,
        { bearer: studentSession.accessToken });
      const openedLesson = content.course?.lessons?.[0];
      if (content.artifactSchema !== 'student-content/v1' || content.course?.lessons?.length !== 1
        || openedLesson?.id !== 'lesson-01' || openedLesson.problems?.length !== 5)
        throw new Error(`${student.userName}: student content projection is incomplete`);
      const studentProjection = JSON.stringify(content);
      if (studentProjection.includes('Синтетическое методическое решение')
        || studentProjection.includes('teacherMaterials') || studentProjection.includes('rubric'))
        throw new Error(`${student.userName}: teacher-only content leaked into student projection`);
    }
  }
  const unassigned = organization.teachers.find(value => value.unassigned);
  if (unassigned) {
    const session = await login(unassigned.userName, runtime.organizationId);
    if (session.hasEducationalScope) throw new Error(`${unassigned.userName}: unassigned teacher received educational scope`);
  }
  const expected = organization.teachers.length + organization.courses.reduce((sum, course) => sum + course.studentCount, 0);
  if (accountMap.size < expected) throw new Error(`${organization.name}: account count mismatch`);
  log(`${organization.name}: role and access validation PASS`);
}
async function validateCrossOrgDenial() {
  const ownStudent = `${manifest.organizations[0].courses[0].studentPrefix}.01`;
  const foreignOrganizationId = state.organizations[manifest.organizations[1].key].organizationId;
  let session;
  try {
    session = await login(ownStudent, foreignOrganizationId);
  } catch (error) {
    if (error instanceof ApiFailure && [401, 403, 404].includes(error.status)) {
      log(`${ownStudent}: cross-organization login denial PASS`);
      return;
    }
    throw error;
  }
  if (session.accessToken || session.hasEducationalScope || session.hasOrganizationAdminScope)
    throw new Error(`${ownStudent}: cross-organization authority unexpectedly issued`);
  log(`${ownStudent}: cross-organization authority denial PASS`);
}


const credentials = { fixtureVersion: manifest.fixtureVersion, password,
  contentPublication: manifest.contentPublication, organizations: [] };
try {
  for (const organization of manifest.organizations) {
    const runtime = await bootstrap(organization);
    const accountMap = await ensureAccounts(organization, runtime);
    const courses = await ensureCourses(organization, runtime, accountMap);
    await ensureProcessing(organization, runtime, courses);
    if (manifest.contentPublication === 'enabled') await publishCourses(organization, runtime, courses);
    else log(`${organization.name}: публикация контента пропущена — ${manifest.contentPublication}`);
    await validate(organization, runtime, accountMap, courses);
    credentials.organizations.push({ name: organization.name, organizationId: runtime.organizationId,
      admin: organization.adminUserName,
      teachers: organization.teachers.map(value => ({ userName: value.userName, assigned: !value.unassigned })),
      students: organization.courses.map(course => ({ course: course.title,
        firstUserName: `${course.studentPrefix}.01`, lastUserName: `${course.studentPrefix}.${String(course.studentCount).padStart(2, '0')}` })) });
  }
  await validateCrossOrgDenial();
  const credentialsPath = path.join(localRoot, 'credentials.json');
  await writeFile(credentialsPath, JSON.stringify(credentials, null, 2) + '\n', { mode: 0o600 });
  await chmod(credentialsPath, 0o600);
  log(`PASS: credentials saved to ${credentialsPath}`);
} finally {
  await browser.close();
}
