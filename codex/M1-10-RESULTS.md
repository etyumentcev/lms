# M1-10 — Прогресс группы, история проверок и обработка сбоев: результаты

Дата: 13.10.2026
Статус: **DONE (component acceptance, synthetic-only)**
Граница: только M1-10. M1-11, production cutover, push и deploy не выполнялись.

## Git preflight и источники

- canonical checkout: `/home/tea/github/lms`; branch `main`, исходный HEAD `2ffb306`;
- index до работы был чистым; сохранены ровно 14 прежних unstaged PNG M1-06/M1-07 — они не восстанавливались, не перезаписывались E2E и не входят в M1-10 commit;
- указанный `codex/SAPIENTUM-M1-PLAN.md` отсутствует; использован M1-10 из `codex/Sapientum_M1_Codex_Tasks_All.md`, а также `M1-09-RESULTS.md`, `M1-PROGRESS.md`, guardrails/reconciliation и ADR-L10/L11/L12/L15;
- project skills `model-domain`, `implement-use-case`, `implement-feature` в доступном каталоге отсутствовали; их выполнение не заявляется.

Явное owner decision `M1-10 HTTP READ AUTHORIZATION` записано в `codex/L03-OWNER-DECISION-REGISTER.md`. Исключение из `S4 HTTP NOT AUTHORIZED` ограничено ровно маршрутами:

- `GET /api/v1/teacher/group-progress`;
- `GET /api/v1/student/progress-history`;
- `GET /api/v1/teacher/review-history`.

Новые write endpoints, schema, roles, permissions и grants не добавлены.

## Контракт

- **Actor → Use Case:** преподаватель читает scoped прогресс группы и собственную историю проверок, затем открывает конкретную разрешённую работу/попытку, включая Accepted; ученик читает свои назначения и существующую immutable историю попыток.
- **Invariants:** client не является authority; rows, counts и aggregates формируются только из server-owned scopes после S2/S3/S4 и ProcessingRestrictions; exact content version сохраняется; Accepted read-only; move не переносит старую assignment affiliation; unknown write outcome сверяется fresh GET без replay.
- **Entities/VO:** существующие `ProblemAssignment`, `AssignmentScope`, `ProblemWork`, `Submission`, `Review`, `EnrollmentScope`, `TeacherAssignmentScope`, published content versions и participant profiles.
- **Transitions:** `NotStarted → Submitted → NeedsRevision → Submitted → Accepted`; read models новых переходов не создают.
- **Authorization/data source:** immutable assignment scope задаёт organization/course/group/student; текущий token + current resource/processing gate проверяются до materialization и повторно перед egress; historical detail/photo продолжает использовать существующие protected parent-aware routes.
- **Write semantics:** M1-10 не владеет транзакциями записи. Existing CAS/idempotency/audit/lock order не изменены. После timeout/lost response UI делает только authoritative GET; новый command автоматически не создаётся.

## Реализация

### Server read models

`PostgresProgressHistoryQueries` переиспользует текущие scope/read models и выполняет candidate read в Repeatable Read. Каждое assignment/review отдельно авторизуется до profile/content/status projection. Status/decision filter применяется к уже server-authorized set до paging. Summary считается по тому же authorized set.

Для защиты counters/aggregates от revoke между DB read и HTTP enqueue DTO несёт несериализуемый authorization proof со всеми assignment/review IDs, участвовавшими в summary. `CanReturn` повторно проверяет весь proof, а не только текущую страницу. `ProtectedCoursesHttp` по-прежнему буферизует ответ, повторно проверяет bearer/deadline и отправляет `Cache-Control: no-store`.

Teacher progress объединяет current roster и immutable assignment scopes: после move старое назначение остаётся в исходной teacher group, а ученик с текущим course enrollment видит собственную старую работу. Content title/problem/lesson разрешаются по точному `CourseVersionId`, поэтому публикация v2 не меняет v1 history.

### React

- `/progress` выбирает teacher/student projection по authenticated session role;
- teacher получает `Прогресс группы`, 12-student/assignment/status summary, course/group/student/status filters и links в конкретный ProblemWork;
- `/teacher/review-history` показывает NeedsRevision/Accepted, attempt/photo counts и deep link `?attempt=<submissionId>`;
- student видит назначение, immutable version, status/attempt count и переходит в существующую вкладку истории с фотографиями, feedback и annotations;
- `TeacherReviewPage` принимает безопасные internal navigation state `returnTo`/`returnLabel`, поэтому historical review возвращает в исходную историю;
- network/server retry у progress/history повторяет только GET.

## Реальный Chromium HTTPS и UI/API/PostgreSQL

`ContentPublicationBrowserTests` поднял real HTTPS Kestrel, production Vite bundle, `/opt/google/chrome/chrome` и disposable PostgreSQL. В G1 создано 12 synthetic students, через существующий publication flow назначено 5 immutable v1 задач каждому — 60 assignment/AssignmentScope rows. Старые M1-06/M1-07 screenshot destinations для этого прогона были направлены в уникальный `/tmp`, поэтому 14 прежних PNG не менялись.

Chromium через обычный login проверил:

1. Submitted snapshot: 12 учеников, 60 назначений, 59 NotStarted, 1 Submitted;
2. lost submission response: двойной click дал один POST, fresh GET подтвердил committed state, replay отсутствовал;
3. NeedsRevision snapshot и teacher review history с attempt/photos/annotations;
4. lost review response: один POST, authoritative GET, immutable review и annotations;
5. Accepted snapshot, server status filter Accepted и history totals `2 = 1 NeedsRevision + 1 Accepted`;
6. переход из history к Accepted review и возврат в history;
7. student progress: 5 назначений, 4 NotStarted, 1 Accepted;
8. student attempt history: две попытки, оригинальные фото, feedback и annotations;
9. browser API summaries сравнены с UI; post-condition oracle подтвердил PostgreSQL: 12 students, 60 assignments, 60 exact G1 scopes, v1 после v2, 2 submissions/2 reviews, Accepted.

Evidence: `codex/evidence/M1-10-PROGRESS-HISTORY/`

- `01-teacher-submitted-1440.png`;
- `02-teacher-needs-revision-1440.png`;
- `03-review-history-needs-revision-1440.png`;
- `04-teacher-accepted-1440.png`;
- `05-teacher-accepted-filter-1440.png`;
- `06-review-history-accepted-1440.png`;
- `07-open-accepted-review-1440.png`;
- `08-student-progress-390.png`;
- `09-student-attempt-history-390.png`;
- `test-summary.json`.

Все 9 PNG существуют и ненулевые (64–870 KB), созданы финальным зелёным Chromium run. DOM assertions и browser/API/DB oracles прошли. Отдельный image-view helper в этом окружении снова недоступен из-за sandbox `mountinfo path is not absolute`; ручная pixel-level инспекция этим helper не заявляется.

## Reliability и negative access

| Путь/риск | Результат |
|---|---|
| positive teacher/student controls | **covered** — focused HTTP + Chromium PASS |
| wrong role | **covered** — student→teacher и teacher→student дают 403 |
| wrong course/group | **covered** — forged course и real G2 outside teacher scope дают 403 |
| cross-org | **covered** — существующий foreign organization/course даёт 403 без rows/counts |
| student move | **covered** — G1→G2: student видит v1+v2 own history, G1 teacher только immutable G1 assignment |
| Teacher Revocation | **covered** — accepted delayed old JWT сохраняет разрешённый read; fresh JWT после revoke не содержит grant и получает 403 |
| ProcessingRestrictions | **covered** — subject restriction блокирует teacher progress и review history |
| missing/forged parent, inaccessible attachment | **not applicable к list DTO** — list endpoints не читают attachment bytes и не принимают parent IDs; deep link/photo paths переиспользуют protected parent-aware routes, full API regression PASS |
| counts/pagination leakage | **covered** — authorize-before-summary/page + nonserialized all-row pre-egress proof; wrong scope получает 403, не нулевые counters |
| mutable history | **covered** — exact v1, move, v2 and accepted review remain linked; browser final chain 2 submissions/2 reviews |
| expiry/two tabs | **covered by reused session suites** — full API/React regression PASS; M1-10 не менял session controller |
| unknown result/duplicates | **covered** — browser submission/review lost-ACK, exactly one POST each, explicit GET reconciliation |
| secrets | **covered** — no new secret/token persistence/logging; response cache `no-store` |

## Проверки

| Команда | Результат |
|---|---:|
| `/tmp/lms-dotnet/dotnet build Lms.sln --no-restore` | PASS, 0 warnings / 0 errors |
| focused `ProgressHistoryHttpTests` | 1 PASS / 0 FAIL |
| focused `vitest run src/test/progressHistory.test.tsx` | 4 PASS / 0 FAIL |
| bundled-node `pnpm run build` | PASS |
| real Chromium HTTPS `ContentPublicationBrowserTests`, `LMS_BROWSER_READ=1` | 1 PASS / 0 FAIL / 0 SKIP |
| `/tmp/lms-dotnet/dotnet test Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false` | Domain 43 + Application 369 + Infrastructure 342 + API 382 = **1136 PASS / 0 FAIL / 10 opt-in SKIP** |
| bundled-node full `pnpm test` | **421 PASS / 0 FAIL**, 19/19 files |
| `node --check tools/browser_read/content-publication.mjs` | PASS |
| `git diff --check` | PASS |

Первые два Chromium iterations остановились на неоднозначных strict locators (`Мои проверки` h1/h3 и status select/badges). Локаторы уточнены до page h1 и `select[name=status]`; product code и server state не откатывались. Финальный полный E2E — PASS.

## Guardrails §7

- [x] Client authority отсутствует; S2/S3/S4/current processing проверяются сервером.
- [x] IDOR/cross-org/wrong role/course/group и revocation/move покрыты рядом с positive controls.
- [x] Rows/counts/aggregates авторизуются до paging и повторно перед egress.
- [x] File-parent bypass не добавлен; detail/photo остаются на существующих protected routes.
- [x] Stale authorization закрыт full-set authorization proof; denied response не содержит partial body.
- [x] Restriction narrowing отсутствует; ProcessingRestrictions только deny.
- [x] History/content versions immutable; schema/write endpoints не добавлены.
- [x] Duplicate effects/unknown outcome: никакого automatic write replay.
- [x] Секреты и unsupported readiness claims отсутствуют.

## Readiness и границы

- **M1-10 component acceptance:** DONE, synthetic-only.
- **UI COMPLETE:** да, только для M1-10 synthetic slice.
- **E2E ACCEPTED:** нет как общий product/release gate; component-level real Chromium HTTPS E2E выполнен и прошёл.
- **SYNTHETIC MVP ACCEPTED:** нет; M1-11 и общая приёмка не выполнялись.
- **PILOT AUTHORIZED:** нет.
- `PRS1 EXACT OFF · G02 BLOCKED` сохраняются. Production/public cutover, реальные ПД, push/deploy не выполнялись.
