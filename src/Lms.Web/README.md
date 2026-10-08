# Lms.Web — checkpoints D/E

Shared UI foundation plus the Course screen. `/courses/:courseId` reads the real
CourseWorkspace endpoint; Lesson/assignment/review routes remain placeholders. `/` is an explicitly labelled shell demo;
its six links are navigation examples, not course data. Role selection changes
menu presentation only; it does not authenticate or grant access.

## Run

Requires Node 24 and pnpm 11.19.0. From this directory:

```sh
pnpm install --frozen-lockfile
pnpm dev
pnpm test
pnpm build
pnpm preview --port 4173 --strictPort
```

In the Codex environment, the supplied runtime can be used without a system install:

```sh
export PATH=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin:/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/bin/fallback:$PATH
```

React 19.3, TypeScript 7.0, Vite 7.3 and React Router DOM 6.30; plain CSS,
Vitest 4.0/Testing Library/jsdom. Exact versions and transitive integrity are pinned.
Router future flags enable transition updates and relative splat behavior explicitly.
Only esbuild's required install script is allowed by pnpm-workspace.yaml.
No UI framework or global data cache. Course has one cancellable feature query. Backend build remains the independent
`dotnet build Lms.sln`; run both builds for a complete repository check.

## Structure and composition

- `src/layout/`: AppShell, MainContent, SidebarNavigation, ContextPanel, ContextSection.
- `src/components/`: presentation primitives, central status map and accessible Tabs.
- `src/styles/`: shared tokens and responsive layout/component styling.
- `src/api/client.ts`: transport only, independent of React.
- `src/App.tsx`: static demonstration, Course route and remaining placeholders.
- `src/features/course/`: Course page, local query lifecycle, rendering and CSS.
- `src/api/contracts/courseWorkspace.ts`: single C3/C4 DTO definition/runtime decoder.
- `src/api/browserClient.ts`: trusted host session adapter to the D bearer client.
- `src/test/`: component/navigation and API boundary tests.

```tsx
<AppShell role="Student" context={
  <ContextSection title="О курсе">Context from the future screen model</ContextSection>
}>
  <Breadcrumbs items={[{ label: 'Мои курсы', to: '/courses' }, { label: 'Курс' }]} />
  <PageHeader title="Курс" />
</AppShell>
```

AppShell always renders one ContextPanel after MainContent in DOM order. Desktop
(>=1200px) uses 216px navigation / flexible workspace / 288px right rail. Tablet
(768–1199px) uses an 80px icon rail and collapsible context below main. Mobile
(<768px) uses an accessible Menu disclosure and always-visible inline context.
Tablet collapsed state never hides mobile or desktop context; there is no duplicate
context tree. No viewport JavaScript or state remounting is needed.

Tabs use controlled selection, automatic activation, roving tabIndex, ArrowLeft/Right,
Home/End, disabled-tab skipping and linked tabpanels. Use automatic activation only
for already-loaded content. AsyncButton takes pending state from its caller and
prevents additional clicks while pending; it does not implement a mutation.
ProgressBar accepts server-supplied percent and clamps only its visual representation.
AvailabilityBadge accepts availability/explanation and does not calculate eligibility.

## Routes

- `/courses/:courseId`
- `/courses/:courseId/lessons/:lessonId`
- `/problem-assignments/:assignmentId` and `?tab=history`
- `/teacher/reviews`
- `/teacher/reviews/:problemWorkId`

Course renders actual data; remaining sidebar destinations have generic placeholders. Unknown URLs stay inside the
shell with a not-found message. No separate submit/history endpoint or feature screen.
Production hosting must serve `index.html` for frontend deep links and route `/api/*`
to the existing backend before applying that fallback. Hosting is not configured in D.

## API boundary

`createApiClient({ getAccessToken, onUnauthenticated?, fetch?, basePath? })` creates a
same-origin client (default `/api/v1`). The integration supplies a current bearer token
per request. No token storage, actor/role headers, login UI or mock identity.
`request(path, decode, { method?, body?, signal? })` passes unknown JSON to a required
runtime decoder, returning its inferred type. Feature DTOs/decoders belong in one API/contracts layer, based on the actual C3/C4
records. CourseWorkspace is implemented there; other feature DTOs remain deferred.

C4 Problem Details retains type/title/status/instance/code/traceId in ApiError.problem.
Branch on ApiError.code, never human text. Network failures use NetworkError/status 0;
malformed error responses use HttpError; invalid success JSON/shapes use InvalidResponse.
401 invokes the supplied session callback; 403/409 propagate without refresh/retry.
AbortSignal is forwarded and cancellations remain cancellations. Relative paths and
redirect rejection keep credentials within the configured origin. No requests are made by the shell demo itself; opening Course performs its GET. Deployment identity/proxy integration is still deferred.

## Optional browser smoke

No screenshot-baseline or visual regression service was introduced. `scripts/smoke.mjs`
uses an existing Playwright installation and installed Chrome, against a running preview:

```sh
LMS_PLAYWRIGHT_MODULE=/absolute/path/to/node_modules/playwright node scripts/smoke.mjs
```

`LMS_SMOKE_URL` defaults to `http://127.0.0.1:4173`;
`LMS_SMOKE_OUTPUT` defaults to ignored `.qa/`. The script checks real CSS geometry at
1440/1024/768/390/320px, the five remaining placeholder views at desktop/mobile sizes, context
identity/state across breakpoints, compact navigation/Escape, skip link, keyboard tabs,
focus outline, semantic-token text contrast >=4.5:1, no feature requests and a clean
console. Screenshots and JSON evidence are generated for inspection; they are not baselines.

See `../../codex/D-RESULTS.md` and `../../codex/E-RESULTS.md` for verified results and intentional remaining gaps.


## Course integration (E)

`/courses/:courseId` -> browserApiClient ->
`GET /api/v1/courses/{encodedCourseId}/workspace` -> validated C3/C4 contract -> CoursePage.
The decoder checks nested row shape, all required fields/nulls, enum/type/number validity
and query response course identity. It never calculates progress or availability.

Student uses actual enrollment; Teacher accepts enrollment=null and teacherContext.
Teacher progress is labelled as scoped to accessible course/group works. Optional teacher
and materials are rendered only if present; current backend returns null/[] for those.
Events/course status/course availability are absent in C4 and have no invented UI fields.
Course names/descriptions are plain text, not raw HTML. Only HTTP(S)/relative material
links are actionable; unsafe URLs remain text. Locked lessons have no navigation or
protected summary. Available lessons open the existing Lesson placeholder via Router.

A local query hook handles loading/success/error, cleanup cancellation, stale-result
exclusion, identity changes and explicit GET retry; there is no global cache. 401 invokes
D's session callback; 403/404 have distinct states; network/5xx/malformed responses allow
manual retry. Shell and a single responsive ContextPanel remain mounted across query states.

### Authentication and same-origin API hosting

The user confirmed that OIDC Authority/Client ID/scope are not configured yet. E does not
invent a login provider or ship test credentials. The trusted hosting application's session
integration must set `window.lmsAuth` before mounting App:

```ts
window.lmsAuth = {
  getAccessToken: () => existingSession.getCurrentAccessToken(),
  onUnauthenticated: () => existingSession.handleSessionExpired(),
};
```

`existingSession` here denotes the host's actual OIDC/session integration, not a provided
fake implementation. It supplies a current access token per request. Without an adapter,
the client sends an anonymous request and renders the API's 401 login-required state.
The host must remount App on account/session changes so previous-account UI is discarded;
there is no account-switcher or shared user-data cache in E. Never put bearer tokens in
Vite environment variables, HTML build artifacts, URL parameters or test fixtures used by
production. Role selection on the shell demo has no effect on Course authorization.

For local development/preview with the real backend on 5187:

```sh
LMS_API_TARGET=http://127.0.0.1:5187 pnpm dev
# Or build first, then:
LMS_API_TARGET=http://127.0.0.1:5187 pnpm preview --port 4174 --strictPort
```

LMS_API_TARGET is a Vite server-side proxy setting, not a browser API origin or credential.
Deployed hosting must proxy `/api/*` before applying the SPA index.html fallback. Backend
JWT issuer/audience/signature/lifetime and course access checks remain authoritative.
A deployed IdP login has not been tested without provider settings.

### Course tests and browser smoke

`pnpm test`: 83 tests, including 34 Course cases. The JSON fixture under src/test/fixtures
mirrors C4 wire names/nullability; it is test-only and is never imported by production code.
Optional enrichment fixtures exercise DTO-supported data that today's backend does not source.
Two D tests now check the courses landing placeholder / Lesson navigation, since the Course
route is covered by actual feature tests. The D browser script similarly leaves Course to:

```sh
LMS_PLAYWRIGHT_MODULE=/absolute/path/to/playwright \
LMS_SMOKE_URL=http://127.0.0.1:4174 \
node scripts/course-smoke.mjs
```

The script intercepts only the Course HTTP response for deterministic loading/success/empty/
error/race-free screenshot checks: 1440/1024/768/390/320px, long unbroken names, 30 rows,
locked rows, Teacher, tablet collapse -> mobile context, 401/403/404/503/network/retry,
and keyboard navigation to the Lesson placeholder. No submission/review workflow E2E.

For optional real API smoke, set LMS_LIVE_SESSION to an external JSON file containing
courseId/studentToken/teacherToken/foreignToken from an isolated test environment. Tokens
are never logged or committed. Production preview must proxy to that API. During E this
used the existing ApiFactory, real JWT Bearer handler and PostgreSQL/Testcontainers via a
temporary local HTTP bridge; no backend source changes. Both roles returned 200, anonymous
401, foreign Student 403. This verifies Course transport/auth/access/rendering, not a
selected provider's discovery/login flow. Screenshots/results go to LMS_SMOKE_OUTPUT.


## Lesson workspace (F)

Lesson consumes C4 `GET /api/v1/courses/{courseId}/lessons/{lessonId}/workspace` through the
existing client/auth adapter. Problems navigate by assignment ID, including absent work.
Progress and canonical statuses come from the server. Materials appear only when supplied.
Teacher rows retain server work states; the contract has no role marker, so Lesson uses neutral
shell navigation. For multiple students the row shows the actual student ID; no names are invented.

For a complete local API/PostgreSQL setup, use the root README Docker Compose instructions.
No frontend Vite server or host SDK is required for that scenario.

Optional F browser smoke (same existing Playwright/Chrome setup as Course smoke):

```sh
LMS_PLAYWRIGHT_MODULE=/absolute/path/to/playwright \
LMS_COMPOSE_LIVE=1 node src/Lms.Web/scripts/lesson-smoke.mjs
```

Run from the repository root after Compose is healthy. Default URL: http://127.0.0.1:8080;
`LMS_SMOKE_URL` overrides it, `LMS_SMOKE_OUTPUT` selects the report/screenshot directory
(default `/tmp/lms-f-browser`). Controlled API responses test all statuses, materials, errors,
and layouts at 320/375/768/1024/1440px. Live mode separately uses real Compose bearer tokens,
Course → Lesson → placeholder, Student/Teacher, direct refresh, anonymous 401 and API 404.
Tokens are never printed by the test or stored in repository artifacts. No workflow mutations.


### U1 attachment component smoke

The reusable `AttachmentUploader` lives in `src/components/attachments`. Its parent
receives ordered confirmed attachments plus readiness; it never submits a solution.
Use `/dev/attachments` in Vite development or the explicit Compose local-development
host adapter after local login. Generic production hosting does not enable this route.

```sh
LMS_PLAYWRIGHT_MODULE=/path/to/playwright node scripts/attachments-smoke.mjs
```

Run from repository root with `node src/Lms.Web/scripts/attachments-smoke.mjs` when
using Compose. The script uses the real API/storage and a generated tiny test image,
checks login, two uploads, protected previews, keyboard reorder, delete, foreign access,
no work creation and 320/375/768/1024/1440 layouts. It requires the clean local demo
(no submitted work); outputs default to `/tmp/lms-u1-browser`. No user photos are used.
