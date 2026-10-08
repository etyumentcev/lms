# G — Student Problem / Solution

2026-10-08. Выполнен только G. H / I / J не начаты.

## 1. Branch / base / commit

Ветка `codex/g-student-solution`, точная база `f35096d` (принятый U1).
Итоговый commit — commit, содержащий этот отчёт (`git log -1 --oneline`).
Начальный detached worktree был чистым на D; ветка создана явно от указанного U1.
Baseline до implementation: 143 frontend / 225 backend tests, обе сборки
0 warnings/errors, реальный `docker compose up --build -d --wait` healthy.

## 2. Files changed

- `src/Lms.Web/src/features/problem-workspace/`: `ProblemPage.tsx`, `SolutionForm.tsx`,
  `CurrentSubmission.tsx`, `query.ts`, `problem.css`.
- `src/Lms.Web/src/api/contracts/problemWorkspace.ts`: C4 DTO и runtime decoder.
- `src/Lms.Web/src/api/contracts/lessonWorkspace.ts`: извлечены переиспользуемые
  validators существующих nested DTO; безопасная целочисленная version.
- `src/Lms.Web/src/App.tsx`: assignment route и focus только при смене pathname.
- `src/Lms.Web/src/components/MarkdownContent.tsx`, `markdown.css`: общий безопасный
  Markdown/math renderer, raw HTML не исполняется, TeX trust выключен.
- `src/Lms.Web/src/components/attachments/AttachmentPreview.tsx`: protected original
  preview через существующий U1 content adapter; общий AttachmentImage для uploader.
- `AttachmentUploader.tsx`, `attachments.css`: заметный native photo picker,
  конкретная связь page/error, общий preview; upload/claim/delete lifecycle сохранён.
- `src/Lms.Web/src/test/problem.test.tsx`: 53 G cases;
  `lesson.test.tsx`, `shell.test.tsx`: прежние route cases проверяют G вместо placeholder.
- `src/Lms.Web/scripts/problem-smoke.mjs`: реальный Compose workflow, responsive,
  controlled enrichment, teacher 403 и изолированный persistence/reset.
- `scripts/lesson-smoke.mjs`, `scripts/smoke.mjs`: актуальные route boundaries.
- `package.json`, `pnpm-lock.yaml`, `vite.config.ts`: pinned renderer dependencies,
  общий KaTeX 0.16.47 для renderer/CSS, отдельный Markdown chunk без build warnings.
- Root/frontend README, ExecPlan и этот отчёт.

## 3. ProblemAssignmentWorkspace integration

`GET /api/v1/problem-assignments/{assignmentId}/workspace` через прежний ApiClient,
тот же bearer adapter, AbortSignal, исключение aborted responses, runtime nested DTO
validation и проверка response assignment identity. Глобальный cache не добавлен.
Course/Lesson перечитывают свои query при возвращении на route.

## 4. Route / identity semantics

Единственная route identity — ProblemAssignmentId. Nullable work ID/version допустимы.
Страница remount при смене assignment, поэтому draft/command предыдущей задачи не
попадают в другую. GET и upload не создают ProblemWork; это доказано live smoke.

## 5. Page composition

D AppShell → Breadcrumbs → PageHeader / один h1 → immutable statement → D Tabs.
Solution содержит feedback, форму при разрешённом действии и только последнюю
отправленную попытку. Desktop имеет отдельный ContextPanel справа.

## 6. Statement behavior

Statement всегда над tabs, без отдельного заголовка/таба «Условие». Источник только
workspace.problem.statementMarkdown; latest revision отдельно не запрашивается.
Markdown/LaTeX отображаются одним renderer; raw HTML и опасные ссылки не исполняются.
Заголовки контента не создают второй h1; длинные формулы прокручиваются внутри блока.

## 7. Tabs

Решение / Подсказки / История. `?tab=solution|hints|history`, посторонние query params
сохраняются. Back/Forward работают; неизвестная вкладка отображает Solution.
D roving tabIndex / ArrowLeft/Right / Home/End сохранены. App не крадёт focus при
изменении query. Hidden panels сохраняют форму и U1 uploads в памяти.

## 8. NotStarted

`problemWorkId=null`, `version=null`, `attemptCount=0`: первая форма сразу доступна
при canSubmit. Никаких start/create-attempt commands, frontend domain statuses или drafts API.

## 9. Photo-first flow

Заметная кнопка «Добавить фото решения», native JPEG/PNG multiple input без forced
capture. Страница N, preview, keyboard reorder/remove; desktop grid, mobile одна колонка.
В реальном браузере проверены два созданных тестовых A4 изображения 1190×1684.

## 10. AttachmentUploader integration

Использован прежний U1 AttachmentUploader и attachmentApi: progress, validation,
failed-page retry, DELETE pending, reorder, защищённый content. Подтверждённые IDs
передаются в текущем визуальном порядке. Upload errors связаны с конкретной page
через aria-describedby. Preview открывает исходный Blob в новой вкладке браузера;
есть contain thumbnail, retry content GET и revokeObjectURL/abort при unmount.
Нового upload transport или альтернативного lifecycle нет.

## 11. bodyMarkdown

Обычный textarea «Пояснение к решению — необязательно». Пустой/whitespace текст
отправляется как null. Body-only, photo-only и mixed payload допустимы по invariant
BodyMarkdown not blank OR AttachmentIds.Count > 0. Скрытого PostMessage нет.

## 12. SubmitSolution integration

POST существующего endpoint с bodyMarkdown, ordered attachmentIds, expectedVersion.
Pending uploads / failed pages / pending removal блокируют submit. После HTTP success
выполняется workspace GET; status/attempt/version/capabilities не конструируются локально.
Если POST подтверждён, но GET упал, доступен только retry GET, а не повтор command.

## 13. Concurrency / double-submit

Synchronous ref guard плюс AsyncButton/disabled form предотвращают двойной POST.
Version берётся только с сервера. 409 ConcurrencyConflict не повторяется; показан
«Обновить состояние». Network/5xx/неподтверждённый response блокируют отправку до
явного refresh, поскольку command мог сохраниться. Автоматического retry нигде нет.
Validation failures сохраняют body, фото и порядок для исправления.

## 14. Submitted

Показаны ожидание проверки, server status/count, номер/время/current body/attachments.
При canSubmit=false формы нет. Refresh браузера сохраняет подтверждённый сервером результат.

## 15. NeedsRevision

Один заметный блок «Что нужно исправить» использует latestTeacherFeedback projection.
Предыдущая отправка доступна ниже, новая форма начинается пустой. Предыдущие
attachments автоматически не переиспользуются. Live новая попытка создала attempt 2
только после успешного POST, с новым upload ID и server version.

## 16. Accepted

Terminal read-only: принятая последняя попытка, защищённые фото, body и supplied
teacher feedback. Submit отсутствует, включая защиту от inconsistent canSubmit=true.

## 17. Capabilities

`permissions.canSubmit` и `availability.isAvailable` определяют доступность формы.
Status используется для presentation, дополнительно соблюдается terminal Accepted.
Teacher controls не добавлены; реальный Teacher GET student route дал 403 ErrorState.

## 18. Latest feedback

Источник только `work.latestTeacherFeedback`, без timeline scan и History query.
Полный текст один раз в main. ContextPanel содержит компактное уведомление и ссылку
на замечание вместо второй большой копии feedback.

## 19. Hints

Только список workspace.hints; порядок по server order. Текущий backend возвращает [].
EmptyState «Подсказок пока нет». Supplied hints проверены unit и controlled browser
fixture. Hidden/reveal engine, новые запросы и teacher solutions отсутствуют.

## 20. History boundary

Вкладка и context link существуют; текст «История будет доступна здесь».
Условие остаётся видимым. DTO history валидируется как часть существующего workspace,
но timeline, grouping, old attempts UI и discussion не реализованы.

## 21. ContextPanel

Один D landmark: canonical status, server attemptCount/latest attempt, ссылка к истории,
компактная ссылка к projected feedback, реальные usefulMaterials при наличии.
Нет uploader, statement, полной истории или teacher controls. Unsafe material URLs
не становятся ссылками. На mobile этот же DOM остаётся после main.

## 22. Loading / errors

Shell сохраняется; statement/tabs/solution/context skeletons. GET 401/403/404 имеют
понятные states, network/server/invalid-response допускают ручной retry.
Command error mapping использует только ApiError.code/status: EmptySubmission,
ValidationFailed, AttachmentNotOwned, InvalidAttachment, AttachmentAlreadyAttached,
ConcurrencyConflict, WorkAlreadySubmitted/Accepted, access и uncertain outcome.
Human-readable backend text не анализируется.

## 23. Responsive / mobile

Реальная Chrome geometry на 320 / 375 / 768 / 1024 / 1440 px: no page horizontal overflow,
два portrait previews без crop, доступные controls/textarea/submit, context preservation
при tablet collapse → mobile. Long unbroken statement и длинная math formula проверены
на всех пяти ширинах. Desktop/mobile и NeedsRevision screenshots просмотрены вручную.

## 24. Accessibility

Один основной h1; named statement section; D tabs keyboard/focus/query history;
native labelled input, page order в ol + «Страница N», конкретные reorder/remove labels,
page-associated upload errors, labelled textarea, pending announcement, text status,
feedback heading, один context landmark после main. Focus на inline command error,
после authoritative refresh — main. Это автоматические и keyboard/browser checks,
не сертификация screen-reader accessibility.

## 25. Frontend tests

**196 passed = 143 baseline + 53 G**, 0 failed/skipped. Все 143 исходных cases сохранены;
3 D/F expectations адаптированы с placeholder на реальную assignment page.
Покрыты read states/errors/retry/cancellation/identity, null work, statement/tabs/history,
Markdown safety/math, breadcrumbs, optional body, 1/2 фото и порядок, upload pending,
duplicate guard, server refresh/count, command errors/no retry, refresh failure,
revision/version/new attempt, terminal/capabilities, U1 remove/retry/preservation,
protected preview retry/revoke, context/materials. CSS geometry — browser smoke (§23).

## 26. Backend regression

**225 passed**, 0 failed/skipped: 24 Domain + 15 Application + 10 Infrastructure +
176 API. Real PostgreSQL/Testcontainers, checked-in migrations, U1 storage tests.
Baseline и final полные suites выполнены. Backend tests/source не менялись.

## 27. Browser smoke

`problem-smoke.mjs`: local development login → Course → Lesson → NotStarted →
2 real photo uploads → preview → reorder → optional body → Submit → authoritative
Submitted → browser reload → protected originals → Lesson updated status.
Teacher fixtures через штатные API создали valid NeedsRevision и Accepted; никаких
прямых DB writes. Checked retry/negative paths в unit tests, 403 также live.
Long statement/hints/materials — отдельно помеченные intercepted DTO enrichments.
D shell smoke PASS, E Course smoke PASS, F Lesson smoke **32 browser checks PASS**;
везде no page errors. U1 component/transport tests остаются зелёными.

## 28. Docker regression

Основной `docker compose up --build -d --wait` проверен до и после G.
PostgreSQL → migrate exit 0 → API/storage → Web healthy.
Для destructive lifecycle использован отдельный project `lms-g-smoke`, порт 8081,
со своими DB/uploads volumes. API restart и down/up сохранили Accepted/work/version
и SHA-256 original. down -v + up --build дали чистые миграции/demo, null work и 404
для старого attachment. Основной lms-local DB/uploads не сбрасывались.

## 29. Build results

Frontend TypeScript/Vite: **0 warnings / 0 errors**; renderer выделен в отдельный chunk.
Full solution SDK 10.0.401: **0 warnings / 0 errors**. `git diff --check` PASS.
Transient evidence: `/tmp/lms-g-baseline-*.log`, `/tmp/lms-g-{web-tests,web-build,build,tests}.log`,
`/tmp/lms-g-compose-final.log`, `/tmp/lms-g-browser-final.log`, `/tmp/lms-g-browser/results.json`,
`/tmp/lms-g-{shell,course,lesson}-browser*`. Screenshots — `/tmp/lms-g-browser/`.
Обычный sandbox launcher неисправен (`mountinfo path is not absolute`); использован
разрешённый escalated execution, без application workaround.

## 30. Backend changes

Отсутствуют: production backend, migrations, domain/application invariants и backend
tests не менялись. Подтверждённых backend contract defects не найдено.

## 31. Contract gaps

Неблокирующие существующие C3/U1 gaps: hints/materials пока не provisioned; workspace
содержит attachment IDs без filenames — UI использует honest page numbers, не выдумывает
metadata. Version за пределами JS safe integer отклоняется вместо округлённого token.
Immutable asset resolver для встроенных Markdown images отсутствует: такие изображения
показывают alt text, внешние/неразрешённые images не загружаются. G не добавляет asset API.

## 32. Remaining limitations

Draft только in-memory: сохраняется между tabs, теряется при уходе/reload/manual refresh;
UI явно предупреждает. Pending uploads не удаляются в unmount/beforeunload и остаются
для future U1 cleanup policy; ручное удаление использует DELETE. JPEG/PNG only и
existing U1 size/validation limits. Production OIDC по-прежнему отдельная интеграция.
Preview full-size использует браузерную вкладку, без annotation/crop/viewer J.
History H, Teacher Queue I, Teacher Review J и все прочие запрещённые функции отсутствуют.

## 33. ExecPlan changes

Обновлён актуальный статус; добавлен §21: baseline, decisions, G implementation,
проверенные exit criteria и evidence. Исторические B–U1 sections сохранены.
G03 CLOSED, G07 body+attachments, G08 browser-only state не пересматривались.
**Остановка после G. H / I / J не начинать.**
