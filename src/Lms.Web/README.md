# Lms.Web — checkpoint D

Shared UI foundation only. All course/lesson/assignment/review routes render
placeholders inside the same shell. `/` is an explicitly labelled shell demo;
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
No UI framework, data cache or feature queries. Backend build remains the independent
`dotnet build Lms.sln`; run both builds for a complete repository check.

## Structure and composition

- `src/layout/`: AppShell, MainContent, SidebarNavigation, ContextPanel, ContextSection.
- `src/components/`: presentation primitives, central status map and accessible Tabs.
- `src/styles/`: shared tokens and responsive layout/component styling.
- `src/api/client.ts`: transport only, independent of React.
- `src/App.tsx`: static demonstration and route placeholders.
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

Sidebar destinations also have generic placeholders. Unknown URLs stay inside the
shell with a not-found message. No separate submit/history endpoint or feature screen.
Production hosting must serve `index.html` for frontend deep links and route `/api/*`
to the existing backend before applying that fallback. Hosting is not configured in D.

## API boundary

`createApiClient({ getAccessToken, onUnauthenticated?, fetch?, basePath? })` creates a
same-origin client (default `/api/v1`). The integration supplies a current bearer token
per request. No token storage, actor/role headers, login UI or mock identity.
`request(path, decode, { method?, body?, signal? })` passes unknown JSON to a required
runtime decoder, returning its inferred type. Future feature DTOs/decoders belong in
one API/contracts layer, based on the actual C3/C4 records; none are copied here.

C4 Problem Details retains type/title/status/instance/code/traceId in ApiError.problem.
Branch on ApiError.code, never human text. Network failures use NetworkError/status 0;
malformed error responses use HttpError; invalid success JSON/shapes use InvalidResponse.
401 invokes the supplied session callback; 403/409 propagate without refresh/retry.
AbortSignal is forwarded and cancellations remain cancellations. Relative paths and
redirect rejection keep credentials within the configured origin. No requests are
made by the shell demo itself. Deployment identity/proxy integration is still deferred.

## Optional browser smoke

No screenshot-baseline or visual regression service was introduced. `scripts/smoke.mjs`
uses an existing Playwright installation and installed Chrome, against a running preview:

```sh
LMS_PLAYWRIGHT_MODULE=/absolute/path/to/node_modules/playwright node scripts/smoke.mjs
```

`LMS_SMOKE_URL` defaults to `http://127.0.0.1:4173`;
`LMS_SMOKE_OUTPUT` defaults to ignored `.qa/`. The script checks real CSS geometry at
1440/1024/768/390/320px, all six placeholder views at desktop/mobile sizes, context
identity/state across breakpoints, compact navigation/Escape, skip link, keyboard tabs,
focus outline, semantic-token text contrast >=4.5:1, no feature requests and a clean
console. Screenshots and JSON evidence are generated for inspection; they are not baselines.

See `../../codex/D-RESULTS.md` for verified results and intentional remaining gaps.
