# H — Student History

2026-10-08. Выполнен только H. I Teacher Queue и J Teacher Review не начаты.

## 1. Branch / base / commit

`codex/h-student-history`, точная база `6157592` (принятый G). Работа велась
последовательно в `/home/tea/github/lms`, без нового worktree. До переключения
основной checkout был чистым на `cce0f86`; main не переписан. Итоговый commit —
commit, содержащий этот отчёт (`git log -1 --oneline` после H).

## 2. Files changed

- `src/Lms.Web/src/features/problem-workspace/HistoryTimeline.tsx`: read-only история.
- `ProblemPage.tsx`, `query.ts`, `problem.css` в том же каталоге: вкладка, context,
  background workspace refresh, loading/errors/focus и responsive presentation.
- `src/Lms.Web/src/components/attachments/AttachmentPreview.tsx`: access/missing/network
  errors, failed image decode и retry существующего protected preview.
- `src/Lms.Web/src/test/history.test.tsx`: 37 H cases.
- `src/Lms.Web/src/test/problem.test.tsx`: два G placeholder expectations обновлены;
  все прежние 53 G cases сохранены.
- `src/Lms.Web/scripts/history-smoke.mjs`: реальный H workflow и responsive checks.
- `src/Lms.Web/scripts/problem-smoke.mjs`: ожидаемый NotStarted текст вместо placeholder.
- Root/frontend README, ExecPlan и этот отчёт.

Backend source/tests, migrations, dependencies и Domain states не менялись.

## 3. API integration

Используется существующий `GET /api/v1/problem-assignments/{assignmentId}/workspace`
через общий ApiClient, прежний nested C4 decoder, identity validation и AbortSignal.
Фактические `ReadModels.cs`, `PostgresWorkspaceQueries.History` и C3/C4 amendments
проверены: workspace уже содержит полную историю. Поэтому GET `/problem-works/{id}`
не дублируется; новый endpoint не нужен. На initial History — один workspace GET.
При повторном открытии History — новый authoritative GET без global cache framework.

## 4. History data model

Источник — `history.submissions/reviews/messages` и `work` одного server snapshot.
Pure presentation composition не изменяет массивы/объекты DTO. Нет events reconstruction,
нового mutable history store, persistence/browser history model или вычисления work status.
Context получает server status/count/latest, а не результат сканирования timeline.

## 5. Timeline composition

UI Flow §9 требует newest attempts first. Сохраняется порядок submissions сервера
(AttemptNumber descending, включая одинаковые timestamps). Review отображается внутри
своей попытки после submission. Общие messages размещаются отдельными sibling entries
по createdAt от новых к старым: reverse уже упорядоченного сервером CreatedAt/ID массива.
Attempt block позиционируется по submittedAt; его review остаётся внутри блока.
При равном времени message/attempt сообщение идёт первым; message/message tie сохраняет
обратный server ID order. Случайной сортировки, UUID generation и Domain Event карточек нет.
Это grouped chronology, а не глобальный audit log отдельных submit/review events.

## 6. Attempt grouping

Named article «Попытка №N»: submittedAt, исходные страницы, body snapshot, review.
Review join — только `submissionId`, feedback join — только `messageId`.
Messages не привязываются к попытке по времени. Отсутствие review не создаёт
фиктивного решения преподавателя или дополнительной попытки.

## 7. Review rendering

NeedsRevision/Accepted используют существующий текстовый StatusBadge и разные цвета
границы. Показываются timestamp, teacher identity и полный feedback через safe
Markdown/math renderer. Display name используется только от linked feedback author
с matching teacherId; иначе честный supplied teacher UUID. Feedback сохраняет переносы.
Accepted без feedback поддержан. Никаких teacher actions/annotation tools.

## 8. Messages

Linked feedback показан один раз внутри Review, исключён из общего discussion.
Остальные messages имеют отдельный article, Student/Teacher label, author/time,
безопасный Markdown и свои ordered attachments. Composer/PostMessage UI не добавлен.
В smoke PostMessage вызывается только как подготовка валидного backend workflow.

## 9. Attachment snapshots

Каждая попытка использует только собственный ordered `attachmentIds` array. Общий U1
adapter получает original через authenticated `/attachments/{id}/content`; Blob URL
не содержит token/path, abort/revoke сохраняются. Contain preview без crop; keyboard
link открывает original в новой вкладке. Есть loading, 401/403, 404, network и image
decode failure с retry. Edit/delete/reorder controls отсутствуют в исторических articles.
Live original hashes #1 остались прежними после #2, Accepted, reload и API restart.

## 10. Historical content

`workspace.problem.courseVersionId/statementMarkdown` разрешаются backend из immutable
assignment.Content. C3 гарантирует exact version и fail-closed при её отсутствии.
В History есть collapsed «Условие этой работы» с этой reference/statement, без запроса
latest publication. Постоянное условие над tabs остаётся из того же workspace.
Submission DTO не содержит отдельной per-attempt revision/snapshot; frontend её не
изобретает. В текущем domain все попытки назначения относятся к одной immutable версии.

## 11. NotStarted

Null work показывает «Вы ещё не отправляли решение». Нет `/problem-works/null`, start
command или создания work при чтении. Existing work с пустыми collections показывает
«История пока пуста»; message-only history отображается, а не считается ошибкой.

## 12. Tabs / URL

Тот же `/problem-assignments/{id}?tab=history|solution|hints`, прежний D Tabs, roving
keyboard navigation и сохранение других query params. Back/Forward работает. История
монтируется только на активной вкладке, поэтому старые фото не скачиваются на Solution.
Форма G остаётся mounted между tabs. После успешного Submit authoritative refresh G
сохранён; следующее открытие History также перечитывает server snapshot.

## 13. ContextPanel

Один прежний ContextPanel: current status, attemptCount, latest attempt, useful materials,
«Вернуться к решению», компактная ссылка к latest feedback. Полной timeline справа нет.
На mobile context остаётся после main. Обновление History обновляет и server context.

## 14. Loading / errors

Initial workspace loading/error — прежние G states. При повторном открытии History
background GET сохраняет условие, tabs и форму; skeleton/error находится только в
History. 401/403/404 не показывают старую timeline; network/server допускают explicit GET
retry. После успешного retry focus переходит на History panel. На tab navigation focus
не перехватывается. При assignment change/unmount запрос отменяется, поздний ответ
игнорируется; identity mismatch отклоняется. Нет automatic command retry.

## 15. Responsive / accessibility

Реальный Chrome: 320, 375, 768, 1024, 1440 px, no horizontal overflow; несколько фото,
long body/feedback и 12 attempts. Large-history проверка отдельно обозначена controlled
C4-shaped HTTP fixture, не persisted production data. Desktop/mobile screenshots
просмотрены. Есть named timeline ol/articles, текстовые review states, semantic time,
один h1/main/context, keyboard tabs и enlargement link, focus после retry. Full-size
original открылся с Enter в отдельной browser tab. Screen reader certification не заявляется.

## 16. Frontend tests

**233 passed = 196 baseline + 37 H**, 0 failed/skipped. Baseline перед implementation
был 196/196. Все прежние cases сохранены; два G expectations теперь проверяют реальную
empty History. H проверяет 28 запрошенных категорий: null/empty/loading/success,
attempt snapshots/order/ties/ownership, reviews/feedback/messages, immutable content,
statement/URL/tabs/Back/Forward, errors/retry/cancel/stale, context/mobile semantics,
accessibility/focus, refresh after G Submit, frozen read model/no mutations, safe Markdown
и protected attachment errors/decode/retry. Geometry проверена в browser, не только jsdom.

## 17. Backend regression

**225 passed**, 0 failed/skipped: 24 Domain + 15 Application + 10 Infrastructure +
176 API. Real PostgreSQL/Testcontainers, прежние ownership/foreign work authorization,
C4 history immutability/exact content и U1 attachment regressions. Backend files не
менялись; подтверждённого contract defect нет.

## 18. Docker / browser smoke

`history-smoke.mjs` на отдельном `lms-h-smoke`, port 8082, собственные DB/uploads:
login → Course → Lesson → Problem → null history → two uploads → Submit #1 → History;
штатный RequestRevision → linked feedback → отдельный student Message → новый upload
и Submit #2 → две попытки → Accept → два review. Проверены текст, attachment order,
original hashes, reload, API restart persistence, desktop/mobile, keyboard/Back/Forward,
original enlargement; browser page errors = []. Невалидные DB rows не создавались.
Перед повтором сбрасывались только временные H volumes. Основной lms-local не сбрасывался.

## 19. Builds

Frontend TypeScript/Vite и full solution SDK 10.0.401: **0 warnings, 0 errors**.
Стандартный `docker compose up --build -d --wait` и isolated H Compose PASS: PostgreSQL,
API/storage, Web healthy, migrate exit 0. `git diff --check` PASS.
Evidence: `/tmp/lms-h-{baseline-web-tests,baseline-web-build,web-tests,web-build,build,tests}.log`,
`/tmp/lms-h-compose{,-default}.log`, `/tmp/lms-h-browser.log`,
`/tmp/lms-h-browser/results.json` и screenshots.

## 20. Contract gaps

- Workspace уже безусловно включает полную history в payload. H не делает дополнительный
  history/details GET на Solution и не рендерит старые фото там; исключить bytes истории
  из обязательного workspace без backend contract change нельзя. Pagination/lazy payload
  остаётся отдельной будущей задачей, не дефектом принятого C3/C4.
- Отдельной per-attempt content reference нет; available immutable assignment reference
  достаточна для текущего domain. Нельзя заявлять поддержку смены revision между попытками.
- Общего cross-kind event sequence нет: deterministic display ties описаны в §5.
- Unsourced teacher display names/hints/materials, attachment filenames и Markdown asset
  resolver остаются существующими C3/U1/G gaps. Поля не выдумываются.

## 21. Limitations

Весь timeline без pagination, photos грузятся при открытии History. Enlargement — native
browser tab, без annotations. Глобальный cache, новый editor/upload subsystem, teacher
screens, notifications, production OIDC и discussion composer не добавлены. G in-memory
form/долговечность pending uploads не менялись. Smoke требует отдельный clean H project;
его reset не должен применяться к пользовательской базе. Обычный sandbox launcher
по-прежнему даёт mountinfo error; использован разрешённый escalated execution.

## 22. ExecPlan changes

Обновлён текущий статус, добавлен §22 с source/contract decisions, implementation,
проверенными exit criteria и evidence. Исторические B–G sections сохранены, G03 CLOSED.
Рабочее дерево должно быть чистым после итогового commit.
**Остановка после H. I и J не начинать.**
