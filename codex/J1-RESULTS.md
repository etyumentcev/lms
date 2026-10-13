# J1 — Teacher Review Workspace

2026-10-08. Реализован только J1. J2 Image Annotation не начат.

## 1. Branch / base / commit

Ветка `codex/j1-teacher-review`, база `a388c6d`. Канонический checkout
`/home/tea/github/lms`, последовательная работа без дополнительных worktrees.
Commit результата — commit, содержащий этот отчёт (`git log -1 --oneline`).

## 2. Changed files

- `src/Lms.Web/src/api/contracts/problemWork.ts`: точный C4 DTO/runtime decoder.
- `src/Lms.Web/src/features/teacher-review/`: query, TeacherReviewPage, ReviewForm,
  SubmissionViewer, ReviewContext, responsive CSS.
- `src/Lms.Web/src/App.tsx`: реальный ProblemWorkId route вместо placeholder.
- `src/Lms.Web/src/test/teacherReview.test.tsx`: 55 новых test cases.
- `src/Lms.Web/src/test/{shell,reviewQueue}.test.tsx`: прежние cases обновлены для J1.
- `src/Lms.Web/scripts/teacher-review-smoke.mjs`: реальный workflow/lifecycle smoke.
- `src/Lms.Web/scripts/{smoke,review-queue-smoke}.mjs`: границы real J1 route.
- Root/frontend README, ExecPlan и этот отчёт.

Backend production/tests, Domain, migrations и dependencies не менялись.

## 3. API integration

GET `/api/v1/problem-works/{problemWorkId}` и POST
`/api/v1/problem-works/{problemWorkId}/reviews/{needs-revision|accept}` через общий
bearer-aware ApiClient. Payload строго `submissionId`, `feedbackMarkdown`,
`expectedVersion`; actor/submission DTO не придумываются. Проверяется response identity,
nested C4 shape и safe integer version. Student assignment route не менялся.

## 4. Page composition

AppShell → Breadcrumbs → PageHeader → queue link → immutable statement → current
submission/viewer → feedback/actions. Desktop ContextPanel справа; mobile после main.
Явный J1 prompt размещает действия в main и имеет приоритет над старым Frontend §29.7.
Макет просмотрен; используются прежние tokens, разделители и primitives.

## 5. Statement

Только `problem.statementMarkdown` и immutable `courseVersionId` из backend exact-version
resolver. Нет latest content fetch или подмены solution. Общий безопасный Markdown/Math
renderer. Desktop ≥1200px: sticky statement, длинный текст прокручивается в bounded
35vh region. На tablet/mobile условие находится перед решением без отдельной вкладки.

## 6. Current Submission

Только `currentSubmission` read model: attemptNumber, submittedAt, bodyMarkdown,
attachmentIds. Null current не реконструируется из массива истории. Status — server.
Текстовые решения без фотографий поддержаны.

## 7–8. Attachment viewer / navigation

Существующий U1 AttachmentPreview получает protected original через bearer GET,
создаёт/revokes Blob URL и отменяет stale downloads. Только активная страница;
порядок attachmentIds сохранён. Изображение пропорционально, без crop/изменения bytes.
Zoom 100–300%, уменьшение/по ширине, scroll увеличенного фото, native full-size link.
Previous/next, счётчик и Alt+Left/Right внутри focusable photo region. Feedback сохраняется.
Canvas renderer/annotations не добавлены; canvas в smoke только генерирует тестовые фото.

## 9–10. Previous attempts / feedback

Компактные раскрываемые attempts в ContextPanel: номер, решение, даты, feedback,
кнопка просмотра. Старые фото открываются в main; явно указан historical режим,
review actions скрыты до возврата к current. Форма остаётся mounted, сохраняя draft/lock.
Review join — submissionId, feedback join — messageId. Последнее NeedsRevision по
attemptNumber показано один раз в приоритетной секции; history содержит ссылку на него.
Общие messages не выдаются за feedback и не привязываются к попытке по времени.

## 11–12. Reference solutions / evaluation criteria

В действующем teacher DTO и immutable source нет этих полей. Секции скрыты.
Не создавались fake solution, критерии, scoring или fixtures вымышленного контракта.
Positive presence scenarios неприменимы до появления реального authorized source;
проверены отсутствие секций и игнорирование undeclared полей. Nonblocking contract gap.

## 13. Feedback editor

Обычный labelled textarea, shared form CSS, много абзацев и переносы строк. Draft
только в памяти страницы; сохраняется при paging/history и explicit state refresh,
не сохраняется в Domain до command и не переносится между work IDs.

## 14–15. Commands

NeedsRevision требует nonblank feedback: empty/whitespace дают inline error и focus
в textarea без POST. Accept допускает пустой комментарий (null). Непустой текст
передаётся без потери переносов. Existing backend повторно проверяет invariants.

## 16. Capabilities

Каждое действие следует своему server canAccept/canRequestRevision и Submitted.
Accepted/NeedsRevision read-only даже при inconsistent enabled capabilities.
GET своего work доступен Student по Feature UC-A03/C4; это не teacher-only endpoint.
Student queue и review commands возвращают 403; свой work отображается без редактора.
Role нельзя достоверно восстановить из capabilities после завершения review, поэтому
sidebar не утверждает Teacher без активного review capability. Host remount при смене
аккаунта остаётся существующей интеграционной обязанностью.

## 17–18. Concurrency / double-submit

POST использует currentSubmission.id и version authoritative snapshot. Ref lock
срабатывает синхронно до await; обе кнопки disabled при pending. Lock сохраняется
при открытии истории. 409 не повторяется автоматически; explicit refresh получает
новое состояние/version. Network/5xx/401/403/404 тоже требуют refresh до следующего
решения, поскольку результат может быть неопределённым. Validation допускает правку.

## 19–20. Authoritative refresh / queue

После подтверждённого POST выполняется новый GET. UI не изобретает Accepted DTO.
При failed GET остаётся подтверждение сохранения и GET retry, без повторного POST.
Новый server status/review отображается после refresh. Queue URL-state I сохраняется
через Router state; возврат монтирует очередь с новым server GET. Live smoke доказал
исключение NeedsRevision/Accepted из очереди и сохранение course/pageSize filters.

## 21. ContextPanel

Real student name либо UUID, course/lesson, status/count; последнее замечание;
previous attempts. Reference/criteria/materials отсутствуют в wire DTO и не выдуманы.
Нет дублирования полноценного Student History или набора всех фото справа.

## 22. Loading / errors

AppShell/header остаются при initial GET, отдельные main/context skeletons. ApiError
status/code, без parsing human text. GET retry для network/server/content unavailable;
401/403/404 отображаются явно. AbortController и stale guards при смене ID/unmount.
Image loading локален. Ошибки и pending имеют announcements, refresh возвращает focus.

## 23–24. Responsive / accessibility

Real Chrome 320/375/768/1024/1440: no horizontal overflow, contain image, zoom scroll,
main/context order, один h1/main/aside. Desktop sticky statement проверен при scroll
к feedback. Desktop/mobile screenshots просмотрены; mobile page counter вынесен
на отдельную строку после визуального осмотра. Labels, semantic headings/time,
keyboard paging/enlargement, validation focus, focus на historical viewer/refresh,
текстовые statuses и доступные disabled actions. Shared shell contrast smoke PASS.
Screen reader certification не заявляется.

## 25. Frontend tests

**331 passed = 276 baseline + 55 J1**, 0 failed/skipped. Сохранены все baseline cases;
placeholder expectations заменены реальным loading/return query. Coverage: C4 shape,
immutable statement/current identity, photos/order/protected retrieval/zoom/paging,
history/linkage/draft, capabilities/read-only, payload/version/validation/pending,
double-submit/history lock, success/refetch/failure, 401/403/404/409/5xx/network,
GET retry/cancellation/stale identity, URL filters, semantic/mobile DOM/accessibility.
Optional reference/criteria presence не симулируется несуществующими DTO fields.

## 26. Backend regression

**225 passed**, 0 failed/skipped: Domain 24, Application 15, Infrastructure 10, API 176.
Реальные PostgreSQL/Testcontainers; backend не изменялся. Существующие authorization,
exact historical content, transaction races, immutable history и storage tests сохранены.

## 27. Docker / browser smoke

Dedicated `lms-j1-smoke` на 8084 со своими DB/uploads volumes. Real Student login →
Course → Lesson → два A4 uploads → Submit #1 → Teacher login → filtered Queue → Review
→ blank validation → feedback → double-click RequestRevision (один POST) → queue empty
→ Student feedback → distinct-photo Submit #2 → Teacher previous feedback/attempt
→ реальный concurrent Message меняет version → conflict → explicit refresh сохраняет
feedback → Accept без feedback → Accepted → Student History с двумя immutable attempts.
Original SHA256 сохранены; anonymous GET=401; Student queue/review=403. Browser errors=[]
API restart и compose down/up сохранили history/original bytes. Dedicated down -v и
up --build дали null work и 404 старого attachment. `lms-local` volumes не сбрасывались.
Также штатный Compose build/start и shared shell browser smoke проверены.

## 28. Builds

Frontend TypeScript/Vite и full solution SDK 10.0.401: 0 warnings/errors.
`git diff --check` PASS. Во время разработки strict TS выявил optional mock arguments;
исправлены до финальной сборки. Sandbox launcher имеет прежнюю mountinfo error,
использован разрешённый escalated execution. Один запуск был заблокирован сбоем
approval service/usage limit; после пользовательского «продолжай» работа продолжена.

Evidence: `/tmp/lms-j1-{baseline-web,web-tests,web-build,backend-tests,build}.log`,
`/tmp/lms-j1-compose{,-default}.log`, `/tmp/lms-j1-browser.log`,
`/tmp/lms-j1-browser/results.json` и screenshots, `/tmp/lms-j1-shell-browser.log`.

## 29–30. Contract gaps / limitations

Нет reference solutions/criteria/materials, trusted profile enrichment и role field в
ProblemWorkDetails. Existing C4 policy допускает own Student GET; review permissions
не подменяют role. Полная история уже в payload, без нового pagination endpoint.
Draft не переживает navigation/reload. Оригиналы открываются read-only, без annotation.
Длинный statement имеет собственный scroll desktop. Никакого нового Domain lifecycle,
production OIDC, global notifications, AI/OCR/scoring/object storage.

## 31. ExecPlan

Добавлен §24 с расхождениями до implementation и результатами/checklist. Исторические
sections сохранены; G03 CLOSED. Текущий статус J1 completed, J2 не начат.

## 32. Prerequisites for J2

Отдельно согласовать annotation contract, authorization, immutable original association,
coordinate/rotation/zoom semantics и persistence/versioning. J1 не создаёт API/aggregate
для будущих annotations. Эталонные материалы требуют отдельного trusted source contract.
После commit и проверки clean working tree — остановка. **J2 не начинать.**
