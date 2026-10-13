# M1-06 — Content Import & Publication: результаты

Дата: 13.10.2026  
Статус: **DONE (component acceptance, synthetic-only)**  
Граница: только M1-06; universal CMS/blob storage, WYSIWYG, статистика просмотров, M1-07, production cutover, commit/push/deploy не выполнялись.

## Итог

Преподаватель через production React UI открывает draft-only курс из M1-05, загружает ZIP с Markdown-программой, получает диагностику с исходным файлом/строкой, независимо просматривает student и teacher projections, явно публикует immutable v1, открывает занятие №1 группе G1 и назначает пять задач. После отдельного импорта и публикации v2 прежние пять заданий продолжают ссылаться на exact v1.

Реализация не создаёт второй compiler/importer: trusted `server_bridge.py` вызывает существующий B07b `import_zip`, который запускает существующий B07a внутри принятого Linux/systemd/bubblewrap sandbox. Готовые artifacts и BuildReceipt проверяются действующим B07b verifier.

## Owner decision и граница полномочий

До schema/API изменений были проверены `L03-OWNER-DECISION-REGISTER`, B07a/B07b reports, C2 design и действующие content contracts. Исходный register оставлял OD-C2-01/02, H04, server HTTP и B08 закрытыми. Владелец явным ответом «да» принял представленное точное SyntheticOnly решение M1-06: whole-course S3 publisher scope, H04 ImportAttempt writer, additive schema/API/React и test/local HTTPS evidence без production cutover. Решение записано в `L03-OWNER-DECISION-REGISTER.md`.

G02, production restricted DB capability/deployment qualification, реальные ПД и public cutover остаются blocked. Component acceptance ниже их не заменяет.

## Фактическая вертикальная цепочка

`LearningCatalog` → `CourseMaterialsPage` → HTTPS + exact Origin + antiforgery + component bearer → `ContentPublicationHttp` → `PostgresContentPublication` → S3 whole-course/group scope + current processing gate → private `ContentImportAttempt` → fixed `ContentImportRunner` → existing B07b/B07a → split artifacts/assets → explicit `PublishedContentVersion` → `LessonGroupOpening` → existing `ProblemAssignment` / auto-captured `AssignmentScope`.

Student path:

`StudentAssignmentsPage` → `/api/v1/my/courses/{courseId}/assignments` → current enrollment/group processing check → opening + exact assignment/version join. Published student content дополнительно обрезается до открытых lessons, назначенных problems и разрешённых assets.

## Синтетический пакет

Детерминированный generator `tools/content_import/create_m1_fixture.py` создаёт:

- два занятия: «Остатки и сравнения» и «Комбинаторные конструкции»;
- пять задач первого занятия и одну задачу второго;
- LaTeX inline formulas;
- student PNG asset;
- published hints и одну withheld hint;
- отдельные teacher-only solutions/rubrics с canaries;
- v1 и v2, где student projection одинаков, а teacher-only solution/package hashes различаются;
- невалидный вариант без обязательного `problem.id`.

Fixtures: `tests/fixtures/content/m1-06-olympiad.zip`, `m1-06-olympiad-v2.zip`, `m1-06-invalid.zip`.

Прямой прогон существующего importer дал `Ready`, 2 lessons, 6 problems, 1 student asset, teacher canary отсутствует в student artifact. Невалидный пакет дал `Invalid / E_FIELD_REQUIRED / lessons/number-theory/problems/congruence-1.md:2:1`.

## Persistence и immutable history

- `ContentImportAttempt` хранит owner org/course/actor, stable idempotency key, request digest, source revision, state/version/expiry, diagnostics и private split artifacts/receipt.
- Raw uploaded ZIP существует только в server-generated `0700` staging directory с `0400` file; B07b повторно делает собственный bounded snapshot/sandbox/cleanup. ZIP не становится published asset.
- Import admission (`Validating`) и terminal result (`Ready|Invalid|Failed`) — отдельные serializable transactions; только проверенный `Ready` можно публиковать.
- `PublishedContentArtifact` и audience-separated assets связаны с immutable existing `PublishedContentVersion`; существующий `immutable_content` trigger запрещает update/delete published version.
- Новые artifact/assets/opening/receipt rows защищены immutable triggers; import transition trigger допускает только `Validating → terminal` и одно присоединение published version.
- Existing `immutable_assignment_reference` сохраняет `CourseVersionId/LessonId/ProblemId`; публикация v2 не переписывает v1 assignments.
- Все новые FK используют `ON DELETE RESTRICT`; schema no-cascade invariant сохранён.

## Authorization, restrictions и приватность

- Import/preview/publish требуют существующий whole-course `TeacherAssignmentScope(GroupId=null)`; group-only teacher получает `403`.
- Open/assign принимает whole-course или exact G1 teacher grant; course/group и active G1 enrollments повторно читаются в mutation transaction.
- Org/actor берутся только из validated token; foreign selectors не создают authority.
- Каждая read/write проверяет qualified token time и authoritative current processing context/restrictions; applicable active restriction закрывает path.
- HTTP upload/publish/assign требуют HTTPS, exact Origin, CSRF, strict body/form shape, MIME/size limits и `no-store, private`.
- Student API никогда не читает teacher artifact/assets; student content отфильтрован до открытого занятия и пяти assigned problems. Teacher canaries и `teacherArtifactHash/packageHash` отсутствуют в student API, React HTML и cacheable responses.
- Teacher preview доступен только teacher token; student получает `403`.

## Idempotency, CAS и неопределённый результат

- Import key привязан к actor + ZIP digest + course + source revision; same replay возвращает прежний attempt, другой payload — `409`.
- Publish и OpenAndAssign имеют immutable receipt с payload digest/result; replay возвращает `created=false` и не повторяет side effect.
- Course/import expected versions проверяются под row lock; stale version — `409 ConcurrencyConflict`.
- Parallel publish regression с двумя ключами и одинаковым expected version даёт ровно один success, один `409`, одну version/artifact.
- PostgreSQL serialization/deadlock/unique и EF concurrency outcomes нормализованы в стабильный `409`, не в 500.
- React не делает automatic retry. При network abort показывает «результат запроса неизвестен», сохраняет idempotency key и требует явной проверки/повтора; Chromium доказал ровно один исходный request.

## Реальный Chromium HTTPS

`ContentPublicationBrowserTests` поднял real HTTPS Kestrel, PostgreSQL, production Vite bundle и `/opt/google/chrome/chrome`. API не mock/fulfill; единственный Playwright route используется в самом конце только для доказательства неизвестного сетевого результата и отсутствия automatic replay.

Проверенная цепочка:

1. teacher login и draft-only курс M1-05;
2. invalid ZIP → file/line diagnostic;
3. valid ZIP → Ready и структура 2 lessons / 6 problems;
4. student preview без teacher canary и teacher preview с solution/rubric;
5. explicit v1;
6. lesson №1 opened, five tasks assigned G1;
7. separate v2 import/publication;
8. student login → ровно пять cards, каждая `Неизменяемая версия v1`;
9. student DOM не содержит teacher canaries/hashes;
10. forced connection abort → unknown-result message и no automatic replay.

Screenshots:

- `codex/evidence/M1-06-CONTENT-PUBLICATION/01-import-diagnostics-invalid.png`;
- `02-import-ready.png`;
- `03-preview-student.png`;
- `04-preview-teacher.png`;
- `05-published-v1-assigned-g1.png`;
- `06-published-v2.png`;
- `07-student-v1-assignments-after-v2.png`;
- machine-readable `test-summary.json`.

## Проверки

| Проверка | Результат |
|---|---:|
| Solution build | PASS, 0 warnings / 0 errors |
| M1-06 protected API/PostgreSQL targeted | 3 PASS / 0 FAIL |
| M1-06 real Chromium HTTPS opt-in | 1 PASS / 0 FAIL |
| B07a compiler full, pinned runtime | 74 PASS / 0 FAIL |
| B07b importer/sandbox full | 49 PASS / 0 FAIL |
| Frontend Vitest full | 411 PASS / 0 FAIL |
| Frontend TypeScript + production Vite build | PASS |
| Domain full | 43 PASS / 0 FAIL |
| Application full | 369 PASS / 0 FAIL |
| Infrastructure full | 342 PASS / 0 FAIL |
| API full (opt-in browsers отдельно) | 381 PASS / 10 SKIP / 0 FAIL |

Первый диагностический B07a запуск без обязательных `SAPIENTUM_CONTENT_NODE/NODE_PATH` корректно fail-closed с `E_CONFIG`; итоговый полный запуск использовал закреплённые Node 24/KaTeX paths и прошёл 74/74. Первый full .NET run обнаружил три M1-06 composite FK с default `NO ACTION`; миграция была исправлена на явный `ON DELETE RESTRICT`, точный schema test и повторная полная regression прошли.

## Guardrails §7

- [x] Actor/use cases/entities/states и граница только M1-06 зафиксированы.
- [x] Owner approval проверен и записан до принятия schema/API результата.
- [x] Existing B07a/B07b, S3 grants, published version/resolver и assignment reference переиспользованы.
- [x] Org/course/group/processing checks выполняются до private/published rows и внутри writes.
- [x] Explicit publish; invalid import не создаёт published content.
- [x] Idempotency, stale CAS, parallel publish, duplicate assign, unknown outcome и no replay проверены.
- [x] v2 не переписывает v1 assignment history.
- [x] Student API/HTML/cache не содержат teacher solutions/rubrics/hashes и не открывают lesson №2.
- [x] Real PostgreSQL/HTTPS/React/Chromium evidence и screenshots записаны.
- [x] Additive schema only; CMS/WYSIWYG/analytics/backend vanity metrics не добавлены.

## Открытые границы

- component acceptance не является production readiness, deploy approval, G02 closure или legal/compliance acceptance;
- production restricted DB capability/role, backup/retention/operator cleanup и deployment-specific runtime paths требуют отдельной qualification;
- importer сохраняет действующие B07b narrow Linux x86_64/systemd/bubblewrap assumptions и ранее записанные control-plane limitations;
- полный ученический материал, navigation/reference presentation и отдельный teacher reference относятся к M1-07 и не реализованы;
- commit, push и deploy не выполнялись.
