# D — Shared UI shell

2026-10-07. Выполнен только checkpoint D. E/F/G и остальные UI checkpoints не начаты.

## 1. Branch / commit base

Ветка `codex/d-ui-shell`, создана строго от `b12cc2b`
(`test: stabilize C4 backend HTTP contracts`). Изначальный detached HEAD worktree
был `c1e72bd` — C4 merge; implementation начата после переключения на указанную базу.
Baseline до кода: solution build 0 warnings/errors, 187 passed, 0 failed/skipped.

## 2. Frontend stack

Framework документацией не закреплён. Решение записано в ExecPlan §17 до реализации.
React/React DOM 19.3.0 + strict TypeScript 7.0.2 + Vite 7.3.7 + React Router DOM 6.30.3,
plain CSS. Node 24.19.0; pnpm 11.19.0. Все версии и transitive dependencies зафиксированы.
Runtime dependencies: только React, React DOM, router; без UI frameworks/design systems,
query cache, state-management или icon packages.

Tests: Vitest 4.0.18, Testing Library React 16.3.3, user-event 14.6.7, jest-dom 6.9.1,
jsdom 30.1.2. Initial newest versions имели несовместимые test typings/CSR bundler warning;
подобраны совместимые версии без отключения проверок и подавления warnings.

## 3. Files changed

- `src/Lms.Web/package.json`, `pnpm-lock.yaml`, `pnpm-workspace.yaml`, `tsconfig.json`,
  `vite.config.ts`, `index.html`, `.gitignore` — frontend project и воспроизводимая сборка.
- `src/Lms.Web/src/main.tsx`, `App.tsx` — entry, static shell demo, placeholder routes.
- `src/Lms.Web/src/layout/AppShell.tsx`, `SidebarNavigation.tsx` — общие layouts/navigation.
- `src/Lms.Web/src/components/primitives.tsx`, `StatusBadge.tsx`, `Tabs.tsx` — primitives.
- `src/Lms.Web/src/styles/tokens.css`, `ui.css` — общие tokens, CSS и responsive rules.
- `src/Lms.Web/src/api/client.ts` — общий transport и errors.
- `src/Lms.Web/src/test/setup.ts`, `shell.test.tsx`, `primitives.test.tsx`, `api.test.ts` — tests.
- `src/Lms.Web/scripts/smoke.mjs` — лёгкий browser smoke без visual-baseline infrastructure.
- `src/Lms.Web/README.md`, root `README.md` — setup, команды, границы D.
- `codex/EXECPLAN-LMS-SIX-SCREENS.md`, `codex/D-RESULTS.md` — решения и результаты.

Backend Domain/Application/Infrastructure/Api, contracts, migrations и backend tests
имеют **нулевой diff от b12cc2b**. Blockers, требующих backend изменений, не обнаружено.

## 4. Reusable components

AppShell, SidebarNavigation, MainContent, ContextPanel, ContextSection, Breadcrumbs,
PageHeader, Tabs, Button, AsyncButton, StatusBadge, AvailabilityBadge, ProgressBar,
EmptyState, ErrorState, LoadingSkeleton, InlineError; дополнительно Section.

ContextPanel обязателен через AppShell composition и содержит переданный context один
раз. Основное содержимое не дублируется. Demo справа показывает описание, выбор роли
для предварительного просмотра и помощь, без вымышленных progress/profile/course data.
AsyncButton принимает pending от caller; ProgressBar только отображает переданный percent;
AvailabilityBadge не вычисляет eligibility. Frontend domain state machine отсутствует.

## 5. Design tokens

Единый `tokens.css`: spacing, typography/line-height, surfaces, text/muted text, borders,
radii, focus, primary Sapientum green и hover/soft/on-primary варианты; error semantics.
Canonical status map в `StatusBadge.tsx`:

| Status | Label | Tokens |
|---|---|---|
| NotStarted | Не начата | neutral |
| Submitted | На проверке | info |
| NeedsRevision | Нужна доработка | warning |
| Accepted | Принята | success |

Hex values находятся в token layer; экранные status colors не хардкодятся.
Визуальная основа — просмотренный six-screen PNG: белая рабочая поверхность, тонкие
divider, зелёный accent, компактная навигация, крупный heading, горизонтальные tabs,
умеренные скругления и contextual rail. Demo не является реализацией экрана курса.

## 6. Routing

Пять запрошенных patterns:

```text
/courses/:courseId
/courses/:courseId/lessons/:lessonId
/problem-assignments/:assignmentId
/teacher/reviews
/teacher/reviews/:problemWorkId
```

Шестой view — assignment `?tab=history`, только заглушка. Root `/` — явно обозначенная
демонстрация shell с links к шести views и примерами primitives. Sidebar destinations
имеют generic placeholders; неизвестные paths — not-found внутри shell. Все paths
поддерживают direct navigation через SPA fallback. No separate submit route.
Student/Teacher menu содержит все семь пунктов по заданию; роль demo не является auth.

## 7. API foundation

`createApiClient`: default same-origin `/api/v1`, bearer provider вызывается при каждом
request; AbortSignal, JSON body, 401 callback, без token persistence/fake identity.
Обязательный decoder принимает unknown JSON и задаёт тип результата, включая runtime
validation. Feature-specific DTO/queries пока не добавлены и не копируются по folders.

ApiError содержит HTTP status, machine code и проверенный C4 Problem Details:
`type/title/status/instance/code/traceId`. NetworkError, HttpError и InvalidResponse
отдельно обозначают transport/malformed errors; cancellation не превращается в ошибку сети.
Нет автоматических retry/refresh/refetch, включая 409. Human title не используется как code.
Relative URL validation/redirect rejection защищают границу bearer handling.
Shell делает **0 API requests**; login/IdP и production reverse-proxy wiring отложены.

## 8. Responsive strategy

- Desktop >=1200px: 216px sidebar | flexible main | 288px right ContextPanel.
- Tablet 768–1199px: 80px compact navigation, main приоритетен; context расположен
  ниже main, его можно свернуть с доступной кнопки.
- Mobile <768px: компактный Menu disclosure; все context sections inline после main.
  Tablet collapse не скрывает mobile/desktop context. Один DOM tree сохраняет state.

Реальный Chrome: 1440×1000, 1024×900, 768×1024, 390×844, 320×720. Во всех случаях
нет horizontal overflow. Tablet-collapse → mobile → desktop сохраняет тот же context
node и выбранную роль; mobile возвращает скрытые на tablet sections. Все шесть views
открыты напрямую на desktop/mobile: 12 route checks. Screenshots просмотрены.

## 9. Accessibility

Semantic main/nav/aside/sections, labels, один h1, skip link, `aria-current`, focus-visible;
на navigation меняются document title и focus main. Menu поддерживает Escape с возвратом
фокуса; collapsed content скрыт из keyboard navigation. Tabs имеют tablist/tab/tabpanel,
roving tabIndex, ArrowLeft/Right, wrap, Home/End, disabled skipping, aria linkage.
Pending buttons disabled + aria-busy, статусы содержат canonical text и symbol,
ошибки role=alert, loading role=status. Primary interactive controls минимум 44px высотой.

Девять измеренных foreground/background token pairs >=4.5:1, минимум **4.92:1**.
Keyboard/focus проверены и component tests, и браузером. Это shell-level QA, не заявление
о полной screen-reader/WCAG сертификации ещё не реализованных feature screens.

## 10. Frontend tests

`pnpm test`: **49 passed**, 0 failed/skipped:

- 14 shell/sidebar/context/route cases;
- 15 primitives/keyboard/status/async/errors/progress cases;
- 20 API transport/error/auth/cancellation/decoder/path-boundary cases.

`scripts/smoke.mjs`: PASS — responsive geometry, required desktop panel, mobile content
preservation, routes, focus, keyboard, contrast, clean console, no feature API calls.
Использован существующий bundled Playwright и installed Chrome; тяжёлая visual regression
infrastructure не добавлена. Script воспроизводим с внешним Playwright installation.
Результаты: `/tmp/lms-d-browser/results.json`, screenshots в том же каталоге;
`/tmp/lms-d-frontend-tests.log`, `/tmp/lms-d-browser.log`.

## 11. Backend regression

До и после D: **24 Domain + 15 Application + 10 Infrastructure + 138 API = 187 passed**,
0 failed, 0 skipped. Infrastructure/API fixtures используют реальный PostgreSQL/Testcontainers.
Существующие tests не изменялись. Logs: `/tmp/lms-d-baseline-tests.log`, `/tmp/lms-d-tests.log`.

## 12. Build results

- Full backend solution build: **0 warnings, 0 errors**.
- Frontend `tsc --noEmit && vite build`: **0 warnings, 0 errors**.
- Production output: JS 257.24 kB / gzip 81.33 kB; CSS 10.95 kB / gzip 2.77 kB.
- `git diff --check`: PASS.

```sh
# Repository root; this environment uses /tmp/lms-dotnet/dotnet
/tmp/lms-dotnet/dotnet build Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false
/tmp/lms-dotnet/dotnet test Lms.sln --no-build --no-restore -m:1
pnpm --dir src/Lms.Web install --frozen-lockfile
pnpm --dir src/Lms.Web test
pnpm --dir src/Lms.Web build
pnpm --dir src/Lms.Web preview --port 4173 --strictPort
# Another terminal, with an existing Playwright installation:
LMS_PLAYWRIGHT_MODULE=/absolute/path/to/playwright node src/Lms.Web/scripts/smoke.mjs
```

Build logs: `/tmp/lms-d-baseline-build.log`, `/tmp/lms-d-build.log`,
`/tmp/lms-d-frontend-build.log`. The ordinary environment sandbox fails with bubblewrap
mountinfo; approved escalated execution was used. No application workaround/suppression.

## 13. Remaining frontend gaps

Intentional future work: real Course/Lesson data, Student Solution, upload/claim/download,
History, Teacher Queue/Review, safe Markdown/LaTeX, drafts, feature DTO decoders,
query/cache invalidation, real session/IdP integration and complete workflow E2E.
SPA hosting must route `/api/*` to backend before frontend deep-link fallback.
C3/C4 nullable/unsourced profiles/materials and absent OpenAPI G18 remain unchanged.
No AttachmentUploader, SubmissionView, AttemptTimeline, MessageThread, review components,
backend duplication or new domain logic was introduced. No D exit blocker remains.

## 14. ExecPlan changes

§17 records baseline, visual/source review, stack, scope and responsive/API decisions
before implementation; verified results and actual component reuse appended afterward.
Current status/checkpoint table now mark D DONE; original historical B/C sections preserved.
Future feature components remain deferred. C4 contracts and G03/G07 decisions unchanged.

**D завершён. Остановка после отчёта; следующие checkpoints не начинать.**
