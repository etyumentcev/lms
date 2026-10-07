# F — Lesson screen + reproducible local Docker Compose

2026-10-07. Выполнен только checkpoint F. G, upload, History, Teacher Queue/Review и другие feature checkpoints не начаты.

## 1. Branch / base / commit

Ветка `codex/f-lesson`, точная база `959a4ef` (принятый E). Итоговый commit — commit,
содержащий этот отчёт (`git log -1 --oneline` в ветке). Рабочее дерево до начала было чистым.
Baseline до implementation: 83 frontend tests, 187 backend tests; обе сборки без warnings/errors.

## 2. Files changed

- `src/Lms.Web/src/features/lesson/{LessonPage.tsx,query.ts,lesson.css}` — Lesson feature.
- `src/Lms.Web/src/api/contracts/{lessonWorkspace.ts,validation.ts,courseWorkspace.ts}` — точный C4 DTO/decoder; E validators вынесены без изменения поведения.
- `src/Lms.Web/src/App.tsx` — Lesson route, прежний focus lifecycle; убран недостижимый Lesson placeholder.
- `src/Lms.Web/src/layout/{AppShell.tsx,SidebarNavigation.tsx}` — нейтральная navigation role для contract без actor role.
- `src/Lms.Web/src/test/{lesson.test.tsx,fixtures/lesson-student.json,course.test.tsx,shell.test.tsx}` — 37 Lesson cases и адаптация исходных D/E navigation cases.
- `src/Lms.Web/scripts/{lesson-smoke.mjs,course-smoke.mjs,smoke.mjs}` — browser coverage и актуальные route expectations.
- `docker-compose.yml`, `.dockerignore`, `.gitignore`, `src/Lms.Api/Dockerfile`, `src/Lms.Web/Dockerfile`, `docker/{nginx.conf,local-login.html,local-auth.js}` — local runtime.
- `src/Lms.Api/{Program.cs,LocalDevelopment.cs}`, `tests/Lms.Api.Tests/LocalDevelopmentTests.cs` — ограниченный infrastructure wiring и security regression.
- `README.md`, `src/Lms.Web/README.md`, ExecPlan и этот отчёт — инструкции/результаты.

## 3. LessonWorkspace integration

`/courses/:courseId/lessons/:lessonId` → Lesson feature query → общий D/E ApiClient →
`GET /api/v1/courses/{courseId}/lessons/{lessonId}/workspace` → runtime C4 decoder.
Проверяются обе response identities. Запрос получает текущий bearer от существующего adapter.
AbortController и stale-response guard, ручной retry GET; без global cache/framework/dependencies.

Source of truth — `Lms.Application/ReadModels.cs` и C4 amendment, а не ранний sketch §7:
`contentVersions`, nested `problem`, `studentId`, полный `work`, `permissions`, `availability`,
`progress.totalAssignedProblems/percent`, `materials`. Backend DTO не изменён.

## 4. Page composition

Breadcrumbs → один h1 с title → optional summary → semantic ordered problems list →
непустые материалы. ContextPanel содержит только реальные сведения о занятии и один progress block.
Использованы D AppShell/ContextPanel/ContextSection, Breadcrumbs/PageHeader, StatusBadge,
AvailabilityBadge, ProgressBar, EmptyState/ErrorState/LoadingSkeleton, Router links, Button retry
через ErrorState и существующие tokens. Нет новых layout/API/component patterns.

## 5. Problem list / statuses

Номер, title (либо безопасный plain-text statement preview до 160 символов), canonical badge,
attemptCount с русским склонением при >0, availability и CTA. NotStarted/Submitted/NeedsRevision/
Accepted отображаются через D StatusBadge; новых work statuses нет. Submitted не Accepted.
Статусы/попытки берутся с сервера; history не пересчитывается и не отображается.
При отсутствии canSubmit для NeedsRevision CTA — просмотр, а не обещание редактирования.

## 6. ProblemAssignment navigation

React Router links всегда `/problem-assignments/{problemAssignmentId}`. Ключ строки — assignment ID.
`problemWorkId=null` не препятствует переходу. GET ничего не создаёт. Конечный экран — существующий
D Problem placeholder; редактор/отправка/попытки/History не реализованы.

## 7. Progress

Один ProgressBar справа: server percent, acceptedProblems, totalAssignedProblems и derived lesson status.
Слово «ваш» не приписывает teacher aggregate ученику. Проверены 0/25/100/33.33%, zero denominator,
а также расхождение server percent со списком: frontend не пересчитывает domain progress.

## 8. Locked content

Реальная student policy C3/C4: unavailable direct Lesson → 403/ErrorState. Дополнительно defensive
restricted success (`availability.isAvailable=false`) скрывает summary, problems, materials и progress.
Закрытая строка не имеет ссылки/statement preview. Данные Course не используются для обхода доступа.
Teacher policy gap описан в §28; backend authorization не менялся.

## 9. Materials

Секция появляется только для непустого contract materials[]. Реальные title/description/URL;
HTTP(S)/relative links разрешены, executable schemes остаются текстом. Текущий backend возвращает []:
наличие материалов проверено DTO-compatible fixtures, не выдаётся за live material subsystem.
Teacher solutions/hints/file upload не добавлялись.

## 10. Student / Teacher

Student получает свои rows; Teacher — реальные course/group-scoped assignment/work rows и progress.
Lesson DTO не содержит actor role/teacherContext/enrollment; роль не угадывается из canSubmit=false.
Поэтому Lesson использует нейтральный sidebar без подписи «Ученик» и без вымышленного enrollment.
При нескольких student IDs строки явно показывают исходный ID, поскольку имён в DTO нет.
Live Compose проверен для обеих ролей. Teacher StudentProblem endpoint по-прежнему запрещён;
assignment route на F только placeholder и не вызывает его.

## 11. ContextPanel

О занятии: номер, availability, availableFrom только если задан. Прогресс: server counts/percent/state.
Без вымышленного дедлайна, событий, профильных данных или копии problems list. Один DOM tree;
на mobile контекст после main, tablet collapse не удаляет mobile context.

## 12. Loading / error / empty

Shell сохраняется при загрузке, skeleton описания/rows/context. 401 → вход, 403 → отказ доступа,
404 → не найдено. Network/5xx/invalid response → ErrorState с retry; machine code ContentUnavailable
имеет отдельный текст. Human-readable Problem Details не анализируется. Zero problems — нормальный
EmptyState; отсутствующие materials/metadata не создают декоративные секции.

## 13. Responsive

Browser PASS на **320, 375, 768, 1024, 1440 px**: normal, длинные неразрывные названия,
30 задач, zero, материалы, restricted. Horizontal overflow отсутствует. Live container API отдельно
проверен на desktop 1440, tablet 1024 и mobile 375. Desktop/mobile screenshots просмотрены.

## 14. Accessibility

Один h1, semantic ordered list, текстовые status/availability, именованные main/nav/aside, accessible
ProgressBar. Keyboard links и focus main после Course → Lesson и Lesson → Problem проверены
в component/browser tests. Mobile сохраняет context landmark. Markdown preview — escaped text,
не HTML. Полная assistive-technology сертификация не заявляется.

## 15. Frontend tests

**120 passed, 0 failed/skipped** = 83 исходных D/E cases (navigation expectations адаптированы) + 37 Lesson.
Покрытие: loading, exact authenticated request, breadcrumbs/back, null-work assignment navigation,
четыре canonical статуса, склонение attempts, 0/partial/100/fractional progress, zero tasks,
materials present/absent/safe links, optional metadata, restricted/row availability, 401/403/404,
network/server retry, context DOM order, landmarks/h1, teacher/multiple-student rows, safe preview,
invalid status/identity, cancellation и stale response. Fixtures соответствуют C4 null/property shape.

## 16. Browser smoke

`lesson-smoke.mjs`: **32 reported checks, no page errors**, плюс assertions navigation/focus,
mobile context, retry, Student/Teacher, refresh и API errors. 29 controlled checks проверяют UI states,
3 live viewport checks используют настоящий containerized API/PostgreSQL без response interception.
В live режиме token получен API CLI, введён в local login форму; цепочка Course → Lesson → Problem,
refresh Course/Lesson/Problem и null work проверены. Ноль ProblemWork после всех GET.

D shell/primitives smoke PASS; E Course responsive/error/navigation smoke PASS.
Исправлены только устаревшие expectations Lesson placeholder, ожидание hydration/focus effect и
изоляция fixture adapter от Compose-only local adapter. Browser Submit/Review workflow отсутствует.

## 17. Docker Compose architecture

Одна команда из root: `docker compose up --build`. Только Docker Engine/Compose обязательны на host.
PostgreSQL healthy → one-shot migrate success → API healthy → nginx web. Без Redis/broker/cloud/CI.
Внутренние обращения используют `postgres` и `api`, не host localhost. Единственный exposed port:
`127.0.0.1:8080` nginx. Readiness проверяет реальное соединение API с БД, без sensitive details.

## 18. Docker services

- postgres: `postgres:17.6-alpine`, pg_isready, named volume.
- migrate: published API runtime, `--migrate-only`, exit 0, restart=no.
- api: SDK 10.0.401 build → ASP.NET 10.0.12 runtime, non-root APP_UID, порт 8080, curl healthcheck.
- web: Node 24.19/pnpm 11.19 build → nginx 1.28 Alpine static runtime, wget healthcheck.

`pnpm-workspace.yaml` копируется до install (esbuild allowBuilds). Runtime API не содержит SDK;
web runtime не содержит node_modules/source/tests. Проверено командами внутри контейнеров.
Production web target отдельно собран и проверен: local login/auth files отсутствуют.

## 19. Migration strategy

Существующий DbContext.Database.MigrateAsync, штатная EF history, все три checked-in migrations:
`20261007035847_InitialPersistence`, `20261007090442_CommandAccessFacts`,
`20261007103352_ImmutableContentSnapshots`. Нет EnsureCreated/самодельной схемы.
Повторный migrate сообщает «database is already up to date». API service не стартует при ошибке migrate.

## 20. Web → API routing

Nginx `/api/` proxy_pass сохраняет URI, HTTP status и Problem Details; proxy_intercept_errors off.
Все остальные browser routes имеют SPA fallback. `/api/nonexistent` возвращает API JSON 404,
а не index.html. Live 401/403/404 подтверждены. Feature URLs остаются same-origin через общий client.

## 21. Development authentication

`docker compose exec api dotnet Lms.Api.dll --dev-token` выдаёт Student token; суффикс `Teacher` — Teacher.
Вставить на `http://localhost:8080/local-login.html`; redirect в `/courses/local-demo`.
Восьмичасовой JWT подписан public local-only key; existing bearer проверяет signature/issuer/audience/
lifetime, existing mapper/access policy проверяет resource access. Anonymous не становится Student.

И validation config, и token CLI, и seed требуют **Development + LocalDemo:Enabled=true**.
Production/Staging guard проверен integration tests; реальный Production CLI отказал с сообщением
`Local tokens require Development and LocalDemo:Enabled.`. OIDC production boundary сохранён.
Compose-only host adapter использует tab sessionStorage; generic application client не менялся.
Production web target исключает этот adapter. README описывает вход, выход, expiration и local-only defaults.

## 22. Seed/demo strategy

Atomic deterministic provisioning через существующие PublishedCourseContent, ProblemAssignment и
access facts. Course `local-demo`, lesson `divisibility`, Student/Teacher UUIDs, четыре active assignments.
Отсутствие publication marker → seed; его наличие → no-op без перезаписи learner work.
Seed не создаёт fake profiles, ProblemWork, submissions, attachments или invalid workflow rows.
Live seed намеренно NotStarted-only; остальные статусы проверены feature/backend tests.

## 23. Clean Docker bootstrap

Начато с `docker compose down -v`, затем build/up. После исправления build-context pnpm policy
образы успешно собраны, PostgreSQL/API/web healthy, migrate exit 0. History содержит три миграции,
assignments=4, works=0. Browser открывает реальную LMS, bearer Course/Lesson 200; API 401/403/404
не маскируются. Никакой зависимости containers от host SDK/Node/PostgreSQL paths.

## 24. Persistence/reset verification

Снимок publication PublishedAt, assignment count, work count и migration count полностью совпал
до/после `docker compose down` → `docker compose up -d --wait`.
`docker compose down -v` удалил `lms-local_postgres-data` (volume inspect подтвердил отсутствие).
Следующий `up --build -d --wait` снова healthy: 3 migrations, 4 assignments, 0 works;
новый PublishedAt подтвердил новый seed. Затем повторный startup подтвердил migration/seed no-op.

## 25. Backend production changes

Только API host infrastructure: `Program.cs` — migration CLI, Development-gated signing/token command,
readiness; `LocalDevelopment.cs` — local signing/token и atomic seed через существующую модель.
**Domain, Application, Infrastructure persistence/query/access, DTOs, migrations и command semantics
имеют нулевой diff от 959a4ef.** Contract defect не найден; исправлений backend business logic нет.

## 26. Backend regression

**195 passed, 0 failed/skipped**: 24 Domain + 15 Application + 10 Infrastructure + 146 API.
Все прежние 187 сохранены; 8 новых cases проверяют Development enable/disable, Production/Staging,
wrong signature, expired token, anonymous и foreign actor. Health проверен в каждом case.
Original security/workflow/concurrency/content/progress tests не изменены и не ослаблены.

## 27. Build results

Frontend strict TypeScript/Vite: 0 warnings/errors. Full solution: 0 warnings/errors.
Docker API/migrate/web images и отдельный production web target: PASS. `git diff --check`: PASS.
Итоговый JS 275.36 kB (gzip 86.23), CSS 13.43 kB (gzip 3.10).

Evidence в текущем workspace session:
`/tmp/lms-f-{baseline-web-tests,baseline-web-build,baseline-build,baseline-tests,web-tests,web-build,build,tests}.log`,
`/tmp/lms-f-compose-{build,final-build}.log`, `/tmp/lms-f-lifecycle.{log,json}`,
`/tmp/lms-f-{lesson,course,shell}-browser/`, соответствующие `.log`,
`/tmp/lms-f-production-{token,web-build}.log`. Команды повторения — root/Web README.

## 28. Contract / infrastructure gaps

Нет blocker для проверенной Course → available Lesson → Problem placeholder вертикали.

- Lesson DTO не содержит role marker; нейтральная navigation и server row statuses не выдают teacher
  aggregate за личную работу. Dedicated Teacher Lesson navigation/profile остаётся contract enrichment.
- Availability — student publication availability; teacher backend может вернуть недоступные студенту
  версии. F консервативно скрывает `isAvailable=false` content для любого viewer. Чтобы отдельно
  показать разрешённый teacher-only closed content, нужен явный trusted role/view capability source;
  frontend не выводит его из статусов/UUIDs. Backend policy/DTO ради UI не менялись.
- Profiles/material sources/deadlines/hints отсутствуют; материалы проверены только fixture shape.
- Production OIDC/deployment не реализованы; public local credentials и local-development web target
  предназначены только для loopback Compose. Не добавлены object storage/upload/CI/CD/notifications.
- Live демонстрация четырёх workflow states не seed-илась: реальная seed вертикаль NotStarted;
  остальные states покрыты UI fixtures и backend HTTP/PostgreSQL regression.

## 29. ExecPlan changes

§19 фиксирует baseline, C4 источник, reuse, teacher/source gaps, Docker/auth/seed решения и verified
exit checklist. Current status/table обновлены на F complete; исторические B–E результаты сохранены.

**F завершён. Остановка; G, upload и последующие checkpoints не начаты.**
