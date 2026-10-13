# M1-09 — Teacher Review, Annotation & Submit → Review → Revise → Accept: результаты

Дата: 13.10.2026
Статус: **DONE (component acceptance, synthetic-only)**
Граница: только M1-09. Существующие ProblemWork/Submission/Review/Message, PostgreSQL CAS, Teacher Revocation HTTP cutover, protected attachment storage и annotation engine переиспользованы. M1-10, push и deploy не выполнялись.

## Git preflight

- canonical checkout: /home/tea/github/lms;
- branch: main, HEAD до M1-09: 41b3a6daa0881fda727529b86687412856c0f7ac;
- MERGE_HEAD отсутствовал; index был чистым;
- worktree содержал ровно 14 ранее изменённых PNG из M1-06/M1-07; они сохранены, не восстанавливались и не входят в M1-09 commit;
- незавершённого merge и конфликтующего index не было.

Указанный codex/SAPIENTUM-M1-PLAN.md отсутствует. Использован раздел M1-09 действующего единого плана codex/Sapientum_M1_Codex_Tasks_All.md. Файлы image-gen-1(1).png и Проверка решения квадратного уравнения.png также отсутствуют; другие изображения не подставлялись. UI реализован по явным требованиям задания и существующей визуальной системе.

Специализированные project skills model-domain, implement-use-case и implement-feature в доступном каталоге skills отсутствовали; их выполнение не заявляется.

## Контракт

- **Actor → Use Case:** преподаватель видит только scoped Submitted работы, открывает защищённую exact-version попытку, размечает каждую страницу, возвращает на доработку или принимает; ученик видит immutable review/annotations, отправляет новую попытку и получает Accepted.
- **Invariants:** authority только на сервере; queue counts/search/filter/page используют тот же scoped set; active queue содержит только Submitted; original bytes не меняются; annotations связаны с attachment ID и нормализованными координатами; старые Submission/Review неизменяемы; teacher-only solution/rubric/notes не попадают ученику; unknown outcome не порождает новый command.
- **Entities/VO:** существующие ProblemWork, Submission, Review, Message, AnnotatedAttachment/PenStroke, AttachmentMetadata, immutable AssignmentScope и content-version projection.
- **Transitions:** Submitted → NeedsRevision → Submitted (attempt 2) → Accepted. Accepted не возвращается в queue и не допускает новую review/submit mutation.
- **Authorization/data source:** assignment → immutable organization/course/group/student scope; teacher grant проверяется существующим Teacher Revocation cutover; queue/profile/reference/work/photo читаются из PostgreSQL и protected routes; student projection отдельно проверена на отсутствие teacher canary.
- **Write semantics:** PostgresTeacherCommands остаётся transaction owner. Existing lock order, version CAS, command/intent identity, commit/audit, retry и unknown-outcome rules не изменены. После потерянного response UI сохраняет local draft, выполняет authoritative GET и очищает его только когда свежая версия доказывает committed review.

## Реализация

### Очередь

ReviewQueue DTO и SQL projection теперь содержат attachmentCount и scoped studentDisplayName. Имя берётся только через immutable organization scope назначения. Поиск имени, счетчики, filters и pagination применяются к одному server-scoped набору в Repeatable Read transaction. Карточка показывает ученика, курс/занятие/задачу, попытку, дату, число страниц и действие Проверить. Accepted/NeedsRevision отсутствуют в active queue.

### Рабочее место и fullscreen

Существующий SubmissionViewer получил защищённую вертикальную ленту миниатюр с переключением страниц; каждая миниатюра использует тот же authenticated no-store fetch/blob lifecycle. AnnotationPhoto и текущий engine переиспользованы: pen, eraser, undo, zoom, fit и page switching сохраняют отдельный PageState по attachment ID.

Fullscreen на 768 px отдаёт максимум площади A4, скрывает вторичные color/width/touch controls и оставляет pen, eraser, undo, zoom, fit, page switching и exit. На узких экранах thumbnails становятся горизонтальными.

Условие, exact-version teacher solution/rubric/notes, последнее замечание, feedback и оба review actions остаются в существующих projections/components. Новые баллы, шкалы, permissions, schema или доменные статусы не добавлены.

### Unknown outcome

После server commit response NeedsRevision намеренно теряется. Двойное нажатие отправляет ровно один POST. Fresh GET показывает immutable review и server annotations; только тогда local dirty state очищается. Пока authoritative состояние остается Submitted, local draft сохраняется и automatic replay не выполняется.

## Реальный Chromium HTTPS

ContentPublicationBrowserTests поднял real HTTPS Kestrel, disposable PostgreSQL, production Vite bundle и /opt/google/chrome/chrome. Пользовательские операции выполнялись через обычный React login и защищённые HTTP routes; SQL использовался только для seed и post-condition oracle.

Проверено:

1. существующая двухстраничная попытка открыта из scoped teacher queue; queue показывает 2 страницы;
2. teacher reference v1/canary прочитан только из protected exact-version projection;
3. page 1 размечена CDP pointerType=pen с pressure/force, page 2 — мышью;
4. thumbnails, normal/fullscreen switching и сохранение PageState между страницами проверены;
5. A4 при входе в fullscreen целиком помещается в viewport 768 px, обязательные controls видимы, вторичные panels скрыты;
6. response после committed NeedsRevision потерян; один POST, без replay, затем authoritative refresh;
7. reload восстановил обе annotations на правильных attachment IDs;
8. ученик на 390 px увидел feedback и обе размеченные страницы;
9. ученик через UI загрузил вторую попытку из двух оригинальных PNG;
10. teacher увидел revision в queue на 768 px, previous feedback и принял attempt 2;
11. queue стала пустой; ученик увидел Accepted и обе immutable attempts/reviews;
12. JSON первого review до/после accept совпал byte-for-byte;
13. обе попытки сверены с исходными bytes и SHA-256, student payload проверен на отсутствие M1_TEACHER_ONLY.

Original order:

- attempt 1: a4-2.png, a4-1.png;
- attempt 2: a4-1.png, a4-2.png;
- a4-2.png: 35437474fbf2306a1ca05c8da520233e01a29444bf93fe448949f499150c0745;
- a4-1.png: d93fc9aada5ba8db936463c3e514236b51f8071fdc7f1a2c838077f1241b57b8.

Evidence: codex/evidence/M1-09-TEACHER-REVIEW/

- 01-review-queue-1440.png;
- 02-desktop-review-1440.png;
- 03-page-1-annotated-1440.png;
- 04-fullscreen-768.png;
- 05-page-2-annotated-1440.png;
- 06-student-feedback-390.png;
- 07-revision-queue-768.png;
- 08-teacher-accepted-1440.png;
- 09-student-accepted-390.png;
- test-summary.json.

Все 9 PNG существуют, ненулевые (48–254 KB) и созданы зелёным Chromium-сценарием. DOM/layout assertions проверили controls, fullscreen fit, pages, annotations и statuses. Встроенный image-view helper недоступен из-за sandbox error mountinfo path is not absolute; browser policy блокирует file://. Поэтому отдельная ручная визуальная инспекция PNG инструментом не заявляется.

## Reliability и negative access

| Проверка | Статус и evidence |
|---|---|
| чужой teacher / wrong organization | **covered** — scoped queue/work/attachment/API matrix denies; full API PASS |
| wrong course/group/assignment/role | **covered** — ReadApiTests, ContractApiTests и protected work/photo tests; positive controls рядом |
| missing/forged parent и inaccessible attachment | **covered** — attachment consistency/annotation authorization tests deny |
| revoked/moved membership, expired session | **covered** — TeacherRevocationHttp, scoped authority/session/deadline suites PASS |
| processing evidence | **covered** — existing protected routes/processing restrictions unchanged; full Infrastructure/API PASS |
| concurrent reviews | **covered** — one winner, one 409, no partial writes |
| stale expectedVersion | **covered** — annotation/review command returns HTTP 409 ConcurrencyConflict |
| double click | **covered** — browser oracle needsRevisionRequests=1 |
| response lost after commit | **covered** — one committed POST, fresh GET, no automatic replay/new command |
| stale/reused IntentId | **covered** — TeacherOperationAttempt and UnversionedTeacherIntent suites PASS |
| mutation after Accepted | **covered** — WorkAlreadyAccepted/ConcurrencyConflict and immutable DB triggers PASS |
| teacher solution leak | **covered** — browser student payload canary false plus contract tests |
| counts/pagination leakage | **covered** — filtering and summary before pagination inside same scoped SQL transaction; ContractQuery/ReadApi PASS |
| post-pagination filtering | **not applicable —** authorization/filtering occur in SQL before Skip/Take; no in-memory security filtering |
| mutable history / duplicate effects | **covered** — two submissions/two reviews, exact old-review equality, one request |
| secrets | **covered** — no new secret, token logging, public attachment URL or persisted browser credential |

## Проверки

| Команда | Результат |
|---|---:|
| /tmp/lms-dotnet/dotnet build Lms.sln --no-restore | PASS, 0 warnings / 0 errors |
| bundled node vitest targeted reviewQueue/teacherReview/annotations | 140 PASS / 0 FAIL |
| bundled node vite build | PASS |
| real Chromium HTTPS ContentPublicationBrowserTests opt-in | 1 PASS / 0 FAIL / 0 SKIP |
| /tmp/lms-dotnet/dotnet test Lms.sln --no-build --no-restore -m:1 | Domain 43 + Application 369 + Infrastructure 342 + API 381 = **1135 PASS / 0 FAIL / 10 opt-in SKIP** |
| bundled node vitest full --maxWorkers=1 --reporter=json | **417 PASS / 0 FAIL**, 43/43 suites |
| bundled node --check tools/browser_read/content-publication.mjs | PASS |
| git diff --check | PASS |

Первый full frontend run с parallel file workers дал 416 PASS / 1 FAIL: старый attachments.test assertion прочитал passive onChange effect сразу после появления Фото загружено. Исследование подтвердило scheduling race теста: production state уже был отрисован, следующий effect ещё не попал в mock.lastCall. Код M1-08 не менялся. Полный последовательный JSON run завершился 417/417 PASS. M1-06 publish-concurrency test в финальном serial .NET regression прошёл и не повторил transient failure из M1-08.

Первые итерации нового browser script останавливались на неоднозначных strict locators и сохранённом mobile viewport при смене роли; selectors/helper были исправлены. Финальный полный E2E прошёл 1/1, а server operations во время промежуточных прогонов не выявили product failure.

## Guardrails §7

- [x] Client не является authority; server revalidates actor/org/course/group/assignment/submission/attachment.
- [x] IDOR, cross-org, wrong role/group/assignment, forged parent, revoked/expired и inaccessible attachment denials покрыты независимо от positive controls.
- [x] Scope/search/counts/page применены до pagination; leakage через summary/byCourse не добавлена.
- [x] Exact-version teacher projection отделена от student projection; canary leak отсутствует.
- [x] Reviews/annotations/history immutable; originals совпали по bytes/SHA-256.
- [x] Existing transaction/CAS/lock/audit/restriction semantics не ослаблены.
- [x] Unknown outcome не создаёт новый command; double click не дублирует effect.
- [x] Protected thumbnails/content используют authenticated no-store routes и revoke object URLs.
- [x] 1440/768/390, mouse и emulated pen, normal/fullscreen, reload и full cycle проверены.
- [x] Новые schema/status/permissions, M1-10 aggregate statistics, fs-verity, push/deploy не выполнялись.

## Readiness и открытые границы

- **M1-09 component acceptance:** DONE, synthetic-only.
- **UI COMPLETE:** да, только для запрошенного M1-09 synthetic slice.
- **E2E ACCEPTED:** нет как общий product/release gate; выполнен и прошёл component-level real Chromium HTTPS E2E.
- **SYNTHETIC MVP ACCEPTED:** нет; M1-10 и иные MVP gates не закрывались.
- **PILOT AUTHORIZED:** нет.
- Физический планшет не использовался; заявлены mouse, pointer events и browser pen emulation, не hardware validation.
- Отсутствующие reference PNG не заменялись; pixel-match не заявляется.
- Generic boundary PRS1 EXACT OFF · G02 BLOCKED сохраняется. Продолжение текущего accepted Teacher Revocation HTTP/React migration не расширяло production authorization.
