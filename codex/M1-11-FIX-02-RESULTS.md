# M1-11-FIX-02 — Processing Context Bootstrap Results

Дата: 2026-10-14. Статус: **DONE — scoped implementation and target E2E PASS, SyntheticOnly**.

## Итог

Блокер M1-11-D05 устранён штатным путём:

Organization → ProcessingContextVersion v1 → CourseContextBinding → CourseEditor → ZIP Import → Publish v1 → Assign G1 Problems → Student G1.

Организация, участники, курс, контекст, привязка и CourseEditor создавались через production React и защищённый HTTPS API на чистой PostgreSQL test database. Direct SQL seed для положительного Chromium-сценария не применялся. PostgreSQL после UI-пути использовался только как consistency oracle.

Добавлены:

- GET /api/v1/admin/processing-context;
- POST /api/v1/admin/processing-context/versions;
- POST /api/v1/admin/courses/{courseId}/processing-context;
- минимальный зелёный org-admin React экран создания/просмотра immutable synthetic context и same-org привязки курса;
- Application contract и PostgreSQL adapter поверх существующих processing_lock, processing_find_command и processing_change;
- API/PostgreSQL negative tests и targeted Chromium HTTPS E2E;
- owner decision register, progress и evidence.

Существующая schema достаточна; новая migration не создавалась.

## Actor → Use Case → Invariants → State

- Actor: только активный Organization Administrator из validated component token.
- Use case: создать первую или следующую immutable synthetic-only версию; просмотреть текущую версию; CAS-привязать verified course той же организации.
- Invariants: tenant определяется сервером, context synthetic-only, LegalBasisRef отсутствует, historical rows immutable, одна актуальная привязка курса, ProcessingRestrictions имеют приоритет, CourseEditor не расширяет group/student/review authority, отсутствие current context/binding остаётся fail-closed.
- Entities/VO: существующие Organization, ProcessingContextVersion, CourseContextBinding, ProcessingRestriction, ProcessingGuard и ProcessingWriteJournal.
- States: no context → v1; vN → vN+1; unbound course → current version; bound old version → atomic close/open на current version.
- Authorization/data source: org/actor — token; administrator membership, context, course, binding, restriction и revision — PostgreSQL server-side queries. Client-supplied org/actor отсутствуют.

## Write semantics

Transaction owner — PostgresProcessingContextAdministration. Isolation — Serializable для writes, RepeatableRead для state read. Lock order: processing guard организации → exact organization membership FOR UPDATE → current context/course/binding validation → existing processing_change writer. CAS — ExpectedRevision плюс ExpectedContextId/Version или ExpectedBindingFrom. Idempotency — ProcessingWriteJournal с OperationId и canonical payload; exact replay возвращает changed=false, конфликт payload/revision — 409.

Commit/audit выполняет существующий processing_change атомарно. Serialization/deadlock/unique conflicts нормализуются в ConcurrencyConflict. После неизвестного результата UI сначала перечитывает authoritative state и не выполняет автоматический write replay.

## Positive Chromium evidence

Targeted command:

    env LMS_BROWSER_READ=1 LMS_BROWSER_NODE=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node LMS_PLAYWRIGHT_MODULE=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright/index.mjs LMS_BROWSER_EXECUTABLE=/opt/google/chrome/chrome LMS_M1_11_FIX_02_EVIDENCE=/home/tea/github/lms/codex/evidence/M1-11-FIX-02 /tmp/lms-dotnet/dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj --no-restore --filter FullyQualifiedName~CourseEditorAuthorityBrowserTests --logger "console;verbosity=minimal"

Result: **PASS 1/1**, Chromium HTTPS, production React bundle, duration 11s.

Evidence:

- evidence/M1-11-FIX-02/01-processing-context-bound-1440.png
- evidence/M1-11-FIX-02/02-admin-course-editor-1440.png
- evidence/M1-11-FIX-02/03-teacher-published-v1-g1-1440.png
- evidence/M1-11-FIX-02/04-student-g1-five-problems-1440.png
- evidence/M1-11-FIX-02/test-summary.json

Machine summary confirms cleanBootstrapViaUi=true, uiApiPostgresConsistent=true, contextVersions=1, activeBindings=1, publishedVersion=1, assignedProblems=5 and g2AccessExpanded=false.

## Negative-access and consistency matrix

| Check | Expected | Actual evidence |
|---|---|---|
| Student creates context | 403 | ProcessingContextAdministrationHttpTests PASS |
| Teacher creates/views context | 403 | ProcessingContextAdministrationHttpTests PASS |
| Admin views foreign org context | own tenant only | second admin initially saw null, then only its own context; first admin retained its own context |
| Bind foreign course | no disclosure/write | 404 and no CourseContextBinding |
| Forged/missing context | fail closed | 409 ProcessingContextMissing |
| Exact replay | no duplicate effect | 200, changed=false, journal unchanged |
| Stale/concurrent revision | no lost update | 409 ConcurrencyConflict |
| Context update | immutable history | v1 and v2 retained, one current binding after atomic rebind |
| Import without current context/binding | denied | full Infrastructure processing-gate tests PASS; no bypass added |
| Import under ProcessingRestrictions | 403 | ContentPublicationHttpTests.Import_requires_course_editor_and_active_processing_context PASS |
| CourseEditor access to G2 | absent | Chromium teacher projection contained only G1; PostgreSQL oracle found no G2 TeacherAssignmentScope |
| API/PostgreSQL consistency | exact | two focused tests plus Chromium oracle PASS |

Focused API command:

    /tmp/lms-dotnet/dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj --no-restore --filter FullyQualifiedName~ProcessingContextAdministrationHttpTests --logger "console;verbosity=minimal"

Result: **PASS 2/2**.

## Four prior browser failures

1. AccountAdministrationBrowserTests: test host did not register/map CourseRoster after the dashboard began loading /api/v1/admin/courses. This was fixture drift, not D05. Host composition fixed; isolated Chromium rerun PASS.
2. OrganizationBootstrapBrowserTests: stale accessible-name locator expected «Первый запуск организации», while current UI exposes «Первый запуск»; fixture also lacked CourseRoster mapping. Locator/host fixed; isolated Chromium rerun PASS.
3. IsolatedProcessingDeploymentTests two-tab browser: current React requests /api/v1/my/courses, while the legacy restricted host exposes /api/v1/courses and its profile lacks the current CourseCatalog read capability. Exact rerun still FAIL at 404. Not D05; adding a capability/profile change is outside FIX-02. Evidence: evidence/M1-11-FIX-02/prior-browser-reruns/M1-01-BROWSER-AUTH/failure.json.
4. ReactTeacherJourneyTests: the legacy harness injects a pre-issued window.lmsAuth token, but current SessionProvider owns/overwrites auth state and keeps the page at the login gate. Exact rerun still FAIL before the teacher journey. Not D05; evidence: evidence/M1-11-FIX-02/prior-browser-reruns/TEACHER-REVOCATION-REACT-E2E/failure.json.

The two remaining failures are recorded, not relabelled PASS, and do not invalidate the dedicated FIX-02 production-login Chromium path.

## Regression

| Layer | Exact result |
|---|---|
| Domain | 43 PASS |
| Application | 369 PASS |
| Infrastructure | 343 PASS |
| API final full rerun | 385 PASS / 12 opt-in browser SKIP / 0 FAIL |
| React/Vitest | 421 PASS / 19 files |
| Production build | PASS, TypeScript + Vite, 302 modules |
| Content compiler | 74 PASS |
| ZIP importer | 49 PASS |
| Target Chromium HTTPS | 1 PASS |

The first combined Lms.sln run produced one ContentPublicationHttpTests ConcurrencyConflict while projects ran together: API 384 PASS / 1 FAIL / 12 SKIP. The exact failed test passed in isolation (1/1), and the final complete API-project rerun passed 385/385 with 0 failures. This transient result is retained here rather than hidden.

## Guardrails §7

- covered — actor/use case/invariants/states documented above.
- covered — accepted S2/S3/S4 reused; the owner explicitly authorized only FIX-02 HTTP; PRS1 exact remains OFF and G02 remains BLOCKED.
- covered — org and course checked server-side; group authority independently checked by the Chromium/API oracle.
- covered — affected context/course subjects use PostgreSQL sources; unknown/missing state denies.
- covered — course list is prefiltered by OrganizationId before serialization; no post-pagination filtering or cross-org counts.
- not applicable — attachments/history paths are unchanged; existing independent protections remain.
- covered — transaction, lock order, CAS, idempotency, retry and unknown-outcome behavior documented/tested.
- covered — strict JSON, IDs, revision/version, effective-time limits, HTTPS exact Origin, CSRF and safe error mapping tested.
- covered — unit/integration/negative-access/target E2E results recorded with exact commands/results.
- covered — no schema migration, new role, new grant, policy bypass or unrelated refactor.
- covered — SyntheticOnly constants are server-owned; UI explicitly says the context is not a legal basis; no real data/secrets/unsafe logs.
- covered — actual runs and the remaining two unrelated browser gaps are explicitly recorded.

Additional threat review:

- client authority/IDOR: covered by token-derived org/actor and foreign-course/context negatives;
- cross-org/list/count leakage: covered by tenant-prefiltered queries and second-admin test;
- stale authorization across transactions: covered by membership recheck under the same serializable write transaction;
- restriction narrowing: covered — no restriction mutation endpoint; active restrictions remain authoritative;
- mutable history/duplicate effects: covered by existing immutable triggers, version history, CAS and journal replay;
- file-parent bypass and inaccessible attachments: not applicable — no file route changed;
- missing/forged parent: covered for course/context references;
- secrets: covered — platform/activation/password values are redacted from browser diagnostics and absent from summary.

## Scope boundary and readiness

FIX-02 is complete. No full M1-11 acceptance run, push or deploy was performed. The system is **READY FOR THE NEXT SEPARATELY AUTHORIZED FULL M1-11 RERUN** because D05 now has a clean UI/API path and its targeted end-to-end chain passes.

This is not SYNTHETIC MVP ACCEPTED and not PILOT AUTHORIZED. PRS1 exact remains OFF; G02, production qualification, real-data/compliance and pilot gates remain blocked.
