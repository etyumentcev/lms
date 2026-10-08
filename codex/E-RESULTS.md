# E — Course screen

2026-10-07. Только checkpoint E. F и последующие checkpoints не начаты.

## 1. Branch / base

`codex/e-course` создана строго от принятого `6b4df3a` (checkpoint D), без переноса
main merge в базу. До implementation проверены frontend build/49 tests и full backend
build/187 tests: всё зелёное, сборки без warnings/errors. Решения записаны в ExecPlan §18.

## 2. Files changed

- `src/Lms.Web/src/features/course/{CoursePage.tsx,query.ts,course.css}` — Course UI/query.
- `src/Lms.Web/src/api/contracts/courseWorkspace.ts` — единый C4 contract/runtime decoder.
- `src/Lms.Web/src/api/browserClient.ts` — trusted host bearer-provider integration.
- `src/Lms.Web/src/api/client.ts` — только export существующего ApiClient type.
- `src/Lms.Web/src/App.tsx` — Course route composition; остальные feature routes — placeholders.
- `src/Lms.Web/src/layout/{AppShell.tsx,SidebarNavigation.tsx}` — optional preview label;
  реальный Course больше не обозначается demo, структура D сохранена.
- `src/Lms.Web/vite.config.ts` — optional same-origin API proxy для dev/preview.
- `src/Lms.Web/tsconfig.json` — typed JSON fixture import.
- `src/Lms.Web/src/test/course.test.tsx`, `src/test/fixtures/course-student.json` — feature tests.
- `src/Lms.Web/src/test/shell.test.tsx`, `scripts/smoke.mjs` — адаптация двух D cases и
  placeholder smoke к тому, что Course теперь реальный screen, а не placeholder.
- `src/Lms.Web/scripts/course-smoke.mjs` — Course responsive/state/live-API browser smoke.
- `src/Lms.Web/README.md`, root `README.md`, ExecPlan и этот отчёт — setup/results/gaps.

Backend production, contracts, domain, persistence, migrations и backend tests не изменены.
Новых dependencies, UI-kit или global cache нет; package.json/lockfile не менялись.

## 3. CourseWorkspace integration

`/courses/:courseId` → `getCourseWorkspace` → D ApiClient →
`GET /api/v1/courses/{encodedCourseId}/workspace` → runtime decoder → CoursePage.
Decoder соответствует `src/Lms.Application/ReadModels.cs` и C4 wire assertions:
course/enrollment/teacherContext/progress/lessons/materials, nested lesson metadata,
courseVersionIds, nullable title/publishedVersionId/profile fields, string availability reason.
Успешный ответ другого courseId и malformed data дают InvalidResponse.

Текущий token запрашивается у `window.lmsAuth.getAccessToken` на каждый request;
401 вызывает `onUnauthenticated`. Это явная граница trusted host session integration.
Пользователь подтвердил отсутствие OIDC settings; без adapter request анонимный и UI
показывает login-required 401. Fake login, bundled token, role/actor spoof headers отсутствуют.
`LMS_API_TARGET` настраивает server-side Vite proxy, production использует reverse proxy.

## 4. Page composition

Переиспользованы D AppShell/SidebarNavigation/MainContent/ContextPanel/ContextSection,
Breadcrumbs/PageHeader, AvailabilityBadge, ProgressBar, EmptyState/ErrorState/LoadingSkeleton,
Button для retry через ErrorState, tokens/classes. Один h1 с course title; optional
plain-text description/gradeLabel и Teacher context. Семантический ordered lessons list.
Один progress block справа; main содержит course summary и total lessons, без второго
крупного progress блока. Course-specific components остаются внутри course feature.

## 5. Student / Teacher differences

Student принимает реальный enrollment с nullable composite-identity id; дополнительной
client enrollment model нет, личные IDs без необходимости не отображаются.
Teacher нормально работает с enrollment=null; teacherContext задаёт presentation role,
capability label и пояснение «По доступным вам работам курса и групп.» для progress.
Teacher ID/grants не выдаются за designated instructor profile. Authorization выполняет API.
Demo role selector не участвует в Course requests или определении прав.

## 6. Lessons behavior

Номер, title/summary, availability и progress/status берутся только из nested C4 row.
Derived lesson labels: NotStarted → «Не начат», InProgress → «В работе», Completed →
«Завершён»; это не ProblemWork statuses. Используется AvailabilityBadge, а не ошибочная
подмена derived lesson status через ProblemWork StatusBadge.

При isAvailable=true CTA «Открыть»/«Продолжить»/«Посмотреть» ведёт через React Router
на `/courses/{courseId}/lessons/{lessonId}`. Lesson остаётся D placeholder. При false
нет ссылки, показываются текст «Недоступен» и server reason; scheduled timestamp
форматируется локально, но его наступление не вычисляет доступность. Protected summary
не рендерится для locked rows. Empty/nullable titles имеют нейтральные fallback labels.

## 7. Progress

completedLessons/availableLessons/totalLessons/percent отображаются из API без расчёта
по lesson statuses. D ProgressBar используется один раз и имеет accessible name/value.
Проверены 0%, partial, 100%, availableLessons=0, fractional server percentage и намеренное
расхождение с displayed rows для доказательства отсутствия пересчёта на frontend.
Teacher aggregate progress подписан согласно C3 semantics, не «ваш студенческий прогресс».

## 8. ContextPanel

Обязательный отдельный desktop rail с одним server progress section. Teacher section
только при course.teacher != null; materials — только при непустом materials[].
Используются реальные названия/description/URLs; HTTP(S)/relative links разрешены,
executable schemes остаются неактивным текстом. Unsourced avatar/contact не выдумываются.
Events field отсутствует в текущем DTO: секция не создаётся. Нет пустых декоративных cards.

## 9. Loading / error / empty

LoadingSkeleton для header description, 6 lesson rows и context progress, shell остаётся.
401 — login-required и foundation callback; 403 — понятный отказ доступа; 404 — курс
не найден/недоступен; network/5xx/InvalidResponse — ErrorState с manual GET retry.
ContentUnavailable распознаётся по machine code; human title/detail не анализируются.
Query abort при unmount/navigation, obsolete response не заменяет новый course; глобального
cache и command retry нет. Пустой курс и отсутствие доступных уроков — нормальные empty
states, отсутствующие optional sections скрываются без error.

## 10. Responsive behavior

D breakpoints сохранены: >=1200px three-column desktop; 768–1199px compact navigation
и collapsible context ниже main; <768px Menu и inline context после main.
Один DOM context tree, без отдельной mobile разметки. Переносы длинных course/lesson titles
и wrapping rows/metadata/CTA добавлены только course CSS с D tokens.
На 1440/1024/768/390/320px проверены normal/empty/30 lessons и длинные неразрывные названия:
horizontal overflow отсутствует. Tablet collapse не скрывает mobile context.

## 11. Accessibility

Один основной h1, semantic lesson section/list, labelled breadcrumbs/current page,
main/nav/aside landmarks D, текстовые locked/status labels, accessible progress value,
keyboard Router links и visible focus. После Course → Lesson focus в main. Динамическая
загрузка имеет labelled status skeletons и aria-busy, ошибки — role=alert, document title
обновляется после загрузки. Keyboard проверен component tests и браузером; D contrast
tokens и browser regression сохранены. Полная assistive-technology сертификация не заявляется.

## 12. Tests

**83 frontend tests passed, 0 failed/skipped**: исходные 49 cases D (два navigation cases
адаптированы к реальному Course route) + **34 Course cases**.
Покрыты loading, Student, Teacher/null enrollment, 401/callback, 403, 404, network/server/
ContentUnavailable retry, empty, zero availability, progress 0/50/100/fractional,
keyboard Lesson navigation, locked non-navigation/protected summary, availability reasons,
optional context absence/presence, safe links, nullable metadata, landmarks/breadcrumbs,
unmount cancellation, host adapter, malformed C4/old sketches, response identity и race.

Fixture использует C4 property names/nulls и UUID values, не альтернативный contract.
Test JSON не импортируется production code. DTO-compatible optional enrichment examples
отдельно проверяют рендеринг полей, для которых текущий backend пока не имеет источника.

## 13. Browser smoke

Course-only `scripts/course-smoke.mjs` — PASS. Existing Playwright + installed Chrome,
без новой visual regression infrastructure. Проверены responsive geometry, long/30/empty
lessons, locked rows, Teacher null enrollment, сохранность mobile context, held loading,
HTTP 401/403/404/503, network/retry, keyboard переход в Lesson placeholder. Нет uncaught
runtime errors; ожидаемые resource failures при error scenarios не скрываются как success.
Desktop/mobile screenshots просмотрены. D shell/primitives smoke также PASS.

Дополнительно выполнена **реальная связка frontend → same-origin proxy → Course API →
Bearer validation → PostgreSQL → C4 JSON → decoder/rendering**, без response interception:

| Actor | HTTP | Result |
|---|---|---|
| Student | 200 | Реальный enrollment, server progress/lessons |
| Teacher | 200 | enrollment=null, teacherContext, mobile context |
| Anonymous | 401 | Unauthenticated, login-required UI |
| Foreign Student | 403 | Forbidden, access-error UI |

Temporary test-only harness `/tmp/lms-e-live/Smoke.cs` использует существующий ApiFactory,
реальные migrations и isolated PostgreSQL/Testcontainers. Только IdP signing configuration
переопределена локальным test key, как в существующих BearerAuthenticationTests. HTTP bridge
forward-ит requests в реальный TestServer; fake workspace endpoint отсутствует. Production
backend не менялся; tokens/signing keys не добавлены в repository и не выводились.
Это Course integration smoke, не Submit/Review browser workflow E2E и не проверка live IdP login.

Evidence: `/tmp/lms-e-browser/results.json`, screenshots, `/tmp/lms-e-browser.log`,
`/tmp/lms-e-live/host.log`, `/tmp/lms-e-shell-browser/results.json`. Optional live mode
воспроизводится через LMS_LIVE_SESSION с external test-session JSON; инструкция в Web README.

## 14. Backend regression

Baseline и final: **24 Domain + 15 Application + 10 Infrastructure + 138 API = 187 passed**,
0 failed, 0 skipped. Backend source и tests имеют нулевой diff от 6b4df3a.
Logs: `/tmp/lms-e-baseline-tests.log`, `/tmp/lms-e-tests.log`.

## 15. Build results

Frontend strict TypeScript + Vite: **0 warnings/errors**. Full solution: **0 warnings/errors**.
Production JS 267.58 kB / gzip 84.58 kB; CSS 12.66 kB / gzip 3.03 kB. `git diff --check`: PASS.

```sh
pnpm --dir src/Lms.Web test
pnpm --dir src/Lms.Web build
/tmp/lms-dotnet/dotnet build Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false
/tmp/lms-dotnet/dotnet test Lms.sln --no-build --no-restore -m:1
```

Logs: `/tmp/lms-e-frontend-{build,tests}.log`, `/tmp/lms-e-{build,tests}.log`.
Approved escalated commands использованы из-за прежнего bubblewrap mountinfo failure;
код приложения не содержит обходов sandbox или suppressions.

## 16. Contract gaps discovered

Backend contract не нарушен, автоматических «исправлений» backend нет. Старый §6 sketch
не является C4 wire model: фактические nullable metadata, nested lesson, teacherContext,
courseVersionIds и materials учтены. Existing gaps подтверждены:

- upcoming events отсутствуют; course status/availability не предусмотрены DTO;
- designated teacher/profile/grade/material sources пока отсутствуют: null/[] нормальны;
- teacher grants не являются teacher profile; progress course/group-scoped;
- OIDC settings отсутствуют по ответу пользователя. Trusted host должен дать token/session
  adapter и remount App при смене пользователя; без этого корректный 401, не fake login;
- hosting должен проксировать `/api/*` до SPA fallback; реальный provider login/discovery
  нельзя подтвердить без его конфигурации. Signed test-token API boundary подтверждён;
- OpenAPI G18 остаётся deferred; frontend decoder сверяется с C4/C# contract вручную.

Эти deployment/source dependencies явно отделены от реализованного Course screen.

## 17. ExecPlan changes

§18 содержит baseline, pre-implementation decisions, contract/source/auth gaps, actual
reuse, все проверки и exit checklist. Current status/table отмечают E DONE; исторические
B–D результаты сохранены. Next checkpoint F остаётся deferred. README содержит реальные
команды, bearer adapter/proxy contract и границы test/live smoke.

**E завершён. Остановка; Lesson, Student Problem, upload, History, Teacher Queue/Review
и другие checkpoints не реализованы.**
