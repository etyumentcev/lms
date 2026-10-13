# I — Teacher Review Queue

2026-10-08. Выполнен только I. Teacher Review J остаётся существующим placeholder.

## 1. Branch / base / commit

Ветка `codex/i-teacher-queue`, точная база `200ab25` (принятый H).
Работа последовательно в `/home/tea/github/lms`, без дополнительных worktrees.
Commit результата — commit, содержащий этот отчёт (`git log -1 --oneline`).

## 2. Files changed

- `src/Lms.Web/src/api/contracts/reviewQueue.ts`: точные C4 types/runtime decoder.
- `src/Lms.Web/src/features/review-queue/{query.ts,ReviewQueuePage.tsx,queue.css}`:
  cancellable GET, URL normalization, фильтры, очередь, pagination, context, responsive.
- `src/Lms.Web/src/App.tsx`: реальный queue route; return link в существующем placeholder.
- `src/Lms.Web/src/test/reviewQueue.test.tsx`: 43 новых cases.
- `src/Lms.Web/src/test/shell.test.tsx`: один D placeholder case заменён query loading case.
- `src/Lms.Web/scripts/review-queue-smoke.mjs`: воспроизводимый real API/browser workflow.
- `src/Lms.Web/scripts/smoke.mjs`: I route передан feature smoke вместо placeholder check.
- Root/frontend README, ExecPlan, этот отчёт.

Backend production/tests, migrations, package dependencies и Domain states не менялись.

## 3. API integration

`GET /api/v1/teacher/review-queue` через существующий bearer-aware ApiClient.
C4 DTO сверены с `ReadModels.cs`, projection — `PostgresReviewQueue.cs`, wire contract —
C3/C4 results/amendments. Decoder проверяет nested items/summary, nullable labels,
числа/даты, safe integer version, соответствие requested page/pageSize.
Ни mutable Queue aggregate, ни local business-state mirror, ни N+1 запросов нет.

## 4. Queue eligibility

Только backend `ProblemWork.Status == Submitted`, latest submission каждого work.
Frontend не вычисляет eligibility и не фильтрует rows по текстам/попытке.
DTO не имеет status: `StatusBadge Submitted` отображает invariant этого endpoint.
Live smoke доказал включение first/revision, исключение NeedsRevision/Accepted.

## 5. Queue item composition

Ученик → задача → курс/урок/группа при наличии → attemptNumber → submittedAt
(semantic time, локальное отображение) → server waitingDurationSeconds → статус →
«Проверить». При nullable labels — исходные student/course/lesson/problem/group IDs.
Если имя придёт, используется оно. Нет выдуманных ФИО, приоритетов, сложности или lookup.

## 6. Filters

Поддержаны все C3/C4 filters: courseId, groupId, studentId (UUID), attemptKind.
Одно form draft state в native controls и один applied state в URL; ContextPanel
показывает applied values. Применение кнопкой «Применить», сброс на page=1.
Нет справочников курсов/групп в contract: вводятся ID, а filtered byCourse не выдаётся
за полный каталог. Grade filter не добавлен. Группа не называется классом без источника.

## 7. Search

`search` до 200 символов, применяется явно вместе с фильтрами, не на каждый символ.
Backend ищет literal substring в titles/IDs курса, урока, задачи и UUID ученика.
UI объясняет доступный поиск; поиска по отсутствующим profile names не обещает.

## 8. Sorting

Сохранён server order: SubmittedAt ascending, затем ProblemWorkId ascending.
Сначала наиболее давно ожидающие работы. Сортировки текущей страницы на клиенте нет.

## 9. Pagination

Backend page/pageSize/total; default 1/50, допустимый pageSize 1–200.
Предыдущая/следующая сохраняют filters/search. Изменение pageSize сбрасывает page=1.
Out-of-range page не выдаётся за пустую очередь: есть переход на первую страницу.
Запросы отменяются, поздние responses/errors игнорируются. Старые rows/summary скрыты
при новом GET, чтобы данные старого фильтра не выглядели результатом нового.

## 10. URL state

Документированные API names используются в URL; direct link/Back/Forward проверены.
Malformed pages/pageSize, UUID, attemptKind нормализуются; search ограничен 200;
SQL offset/int bound соблюдён; неизвестные параметры удаляются replace navigation.
Filter/search changes добавляют history entries. Queue URL передаётся Router state
в placeholder: return link и browser Back восстанавливают фильтры/страницу.

## 11. Summary

Только `summary.total/firstAttempts/revisions/averageWaitingSeconds/byCourse`.
Никаких totals, вычисленных из items. Live pageSize=1 на двух Submitted works сохраняет
summary.total=2 на обеих страницах. Summary scope — все активные фильтры, все страницы.
Null average и пустой breakdown скрываются. Unfiltered global total contract не возвращает.

## 12. ContextPanel

Переиспользованы AppShell/ContextPanel/ContextSection: ожидают проверки, первые/повторные,
среднее ожидание, распределение по курсам, applied filters. Полных rows справа нет.
Распределения по группам backend не предоставляет; секция не изобретена.
Mobile использует тот же context DOM после main, desktop — отдельный правый rail.

## 13. Navigation / refresh

CTA `/teacher/reviews/{problemWorkId}`, никогда не assignment/submission ID.
J не реализован: «Раздел готовится», return link в очередь. При каждом mount — новый GET,
поэтому возврат с J/Back перечитывает сервер. Live принятие работы существующим API,
пока открыт placeholder, удалило её после возвращения. Polling/global cache отсутствуют.

## 14. Authentication / access

Bearer получает существующий hosting AuthAdapter, без нового auth subsystem.
Teacher navigation на queue появляется только после успешного teacher-only endpoint;
на loading/401/403 роль неизвестна, teacher item не показывается. Смена аккаунта следует
существующему требованию host remount App. Server access остаётся authoritative.
Live anonymous API = 401, Student API/UI = 403; 401/403/404 presentation проверено unit.
404 queue не является штатным empty response; UI обрабатывает его как ошибку endpoint.

## 15. Loading / errors / empty

Известные header и labels формы остаются стабильными и доступны сразу; skeletons
показывают загружаемые summary/rows/context. Main results aria-busy, skeleton role=status.
ApiError.status/code, без разбора human-readable messages; network/server GET retry
только явно, AbortController cleanup и stale identity guards. На errors старых rows нет.
Без фильтров total=0: «Все работы проверены»; с фильтрами: «По выбранным фильтрам работ нет»
и «Сбросить фильтры». Второй текст не утверждает наличие работ вне текущих фильтров:
такого total сервер не возвращает. Reset очищает также ещё не применённый ввод.

## 16. Responsive / visual

Реальный Chrome 320/375/768/1024/1440: no horizontal overflow, видимы student identity,
problem title, CTA. Дополнительная metadata переносится; обязательного table scroll нет.
Проверены также длинные unbroken name/title в отдельно обозначенном C4 HTTP fixture.
Desktop/mobile screenshots просмотрены. Sapientum tokens, белый фон, зелёные CTA,
тонкие разделители, компактные rows, без декоративных dashboard cards.

## 17. Accessibility

Один h1/main/context, labels search/filters/pageSize, named pagination/results,
semantic time/list, текстовый StatusBadge, доступные ссылки с student/problem context.
Navigation фокусирует main; после применения filters/paging/retry — results region.
Keyboard Tab/Enter, Browser Back/Forward и responsive landmarks проверены в браузере.
Screen reader certification не заявляется. Shared-shell smoke проверил прежние contrast
ratios, keyboard navigation и responsive shell.

## 18. Frontend tests

Baseline **233 passed**. Final **276 passed = 233 прежних + 43 I**, 0 failed/skipped.
D queue placeholder expectation заменён real loading expectation без удаления case.
Покрыты loading/GET/empty/reset, IDs/nullable labels/context/attempt/time/status,
ProblemWorkId/J, все filters и search, combined queries/paging/reset/pageSize/URL/Back,
server summary, 401/403/404/network/server/retry/cancel/stale, accessibility/mobile DOM,
return refresh, server order, out-of-range, malformed URL/DTO и waiting presentation.
Геометрия проверена browser smoke, не симуляцией jsdom.

## 19. Backend regression

Baseline и final **225 passed**: Domain 24, Application 15, Infrastructure 10, API 176;
0 failed/skipped. Real PostgreSQL/Testcontainers, existing C3/C4 contract/authorization
и U1 attachment regressions. Подтверждённых backend contract defects не найдено.

## 20. Docker / browser smoke

Dedicated `lms-i-smoke`, port 8083, собственные PostgreSQL/uploads volumes.
Student dev login → Course → Lesson → photo upload/Submit → History → Teacher dev login
→ очередь. Штатными commands подготовлены first/revision/NeedsRevision/Accepted;
невалидных/прямо записанных DB rows нет. Filters/search/group nonmatch/reset/attempt,
real two-page pagination, summary scope, URL Back/Forward, ProblemWorkId/J/return,
Student denial, responsive, keyboard, zero browser page errors — PASS.
API restart сохранил очередь и original photo hash. Отдельный down/up сохранил history
и photo hash; down -v/up --build только dedicated project дал null work и 404 старого
attachment. Основной `lms-local` не сбрасывался. Default Compose startup также PASS:
PostgreSQL/API/Web healthy, migrate exit 0. Shared shell browser smoke PASS.

## 21. Builds

Frontend TypeScript/Vite и full solution SDK 10.0.401 — 0 warnings/errors.
`git diff --check` PASS. Runtime Node 24 найден в bundled dependencies; первый запуск
без PATH не выполнил tests, затем baseline успешно повторён. Обычный sandbox launcher
имеет существующую mountinfo error, использован разрешённый escalated execution.

Evidence: `/tmp/lms-i-baseline-*.log`, `/tmp/lms-i-web-{tests,build}.log`,
`/tmp/lms-i-backend-tests.log`, `/tmp/lms-i-build.log`, `/tmp/lms-i-compose*.log`,
`/tmp/lms-i-browser.log`, `/tmp/lms-i-browser/results.json` и screenshots,
`/tmp/lms-i-shell-browser.log`, `/tmp/lms-i-lifecycle.json`.

## 22. Contract gaps

- Trusted student display names/group labels отсутствуют; UUID/ID fallback, без N+1.
- Нет полного course/group/student filter catalog: ID inputs вместо ложных facets.
- Нет grade filter или group breakdown: не отображаются.
- Нет global unfiltered total alongside filtered summary: filtered empty нейтрален.
- status не является полем QueueItem: Submitted badge следует endpoint invariant.

Это принятые C3/C4 gaps, не production backend defects и не повод менять backend в I.

## 23. Limitations

ID-based фильтры и UUID student search до появления trusted enrichment/catalog.
Waiting time — snapshot сервера; обновляется при следующем GET, без таймера/polling.
Pagination управляется previous/next и pageSize, без infinite scroll/global cache.
Полный J, решения преподавателя в UI, annotation/reference solutions, новый auth,
уведомления и остальные запрещённые функции не добавлены. Smoke review commands
используются только для подготовки/проверки backend workflow.

## 24. ExecPlan changes

Текущий статус обновлён: I завершён, J не начат; добавлен §23 с decisions, gaps,
implementation, evidence и checklist. Исторические B–H sections сохранены.
G03 CLOSED и domain/source-of-truth precedence не менялись. После итогового commit
проверено чистое рабочее дерево. **Остановка после I. J не начинать.**
