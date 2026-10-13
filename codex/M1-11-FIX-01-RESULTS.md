# M1-11-FIX-01 — Course Editor Authority — Results

Дата: 2026-10-13
Owner decision: **APPROVED, вариант C**
Итог среза: **FIX-01 COMPLETE — SYNTHETIC-ONLY COMPONENT + HTTPS E2E VERIFIED**
Не объявляется: `SYNTHETIC MVP ACCEPTED`, `PILOT AUTHORIZED`, production readiness.

## Outcome и границы

Реализовано отдельное course-scoped полномочие `CourseEditor`, независимое от `TeacherAssignmentScope`.

- `CourseEditor(C)` разрешает ZIP/Markdown import, status/diagnostics, student/teacher preview и публикацию immutable content version курса C.
- `TeacherAssignmentScope(C,G1)` по-прежнему разрешает только преподавательские операции G1.
- Editor-only пользователь не получает группы, учеников, работы, фотографии, review queue или историю попыток.
- Публикация не открывает занятие и не создаёт assignments. `open-and-assign` сохранил teacher/group authorization.
- Администратор организации выдаёт и отзывает полномочие через защищённый React/API flow.
- Другой курс и другая организация не принимают grant.
- S2/S3/S4, ProcessingRestrictions, session/JWT temporal model, immutable content/history и существующий compiler/import adapter не изменены.

Не реализовывались PRS1 exact, production/public cutover, реальные ПД, новые authentication/session механизмы, полный повтор M1-11, push или deploy.

## Actor → Use Case → invariants → state

| Actor | Use case | Проверяемые инварианты | Состояние |
|---|---|---|---|
| Org Admin | Assign/Revoke `CourseEditor(C)` | active same-org Teacher membership; exact course; org/course row lock; CAS revoke; idempotency receipt; audit | Covered |
| CourseEditor | Import/status/preview/publish | exact-course editor grant; current membership generation; JWT time; current ProcessingRestrictions; import owner; serializable publish/CAS | Covered |
| Group Teacher | Open lesson / assign five problems | existing exact course/group teacher grant; editor grant не участвует | Covered |
| Student G1 | Read assigned v1 tasks | existing enrollment/assignment parent scopes; teacher projection не раскрывается | Covered |
| Foreign org / wrong course | Any editor operation | server-derived organization + exact course grant; generic deny/not-found without counts/content | Covered |

Entity `CourseEditorScope` хранит stable `GrantId`, organization/editor/course binding, membership/grant generations, `Active|Revoked`, version и revoke timestamps. Ключевые поля и история не удаляются/не перепривязываются; revoke увеличивает version, regrant увеличивает generation и version.

## Write semantics

Владелец транзакции assign/revoke — `PostgresCourseRosterAdministration.Mutation`.

- Isolation: PostgreSQL `Serializable`.
- Lock order: idempotency advisory lock → administrator membership `FOR UPDATE` → course row `FOR UPDATE` → target membership/read → editor grant `FOR UPDATE`.
- CAS: revoke требует exact `GrantId + EditorId + CourseId + OrganizationId + ExpectedVersion`; stale version → `409 ConcurrencyConflict`.
- Commit: scope mutation, command receipt и secret-free security audit сохраняются в одной транзакции.
- Replay: одинаковый actor/key/kind/payload возвращает сохранённый результат с `Changed=false`; другой payload → conflict.
- Unknown outcome: автоматический повтор не выполняется; явный replay с тем же ключом даёт authoritative receipt.
- Publish сохранил существующие serializable transaction, advisory idempotency, course/import CAS, immutable version и atomic audit.

## Schema/API/UI

Добавлена additive migration `CourseEditorAuthorityM111Fix01`:

- PK `GrantId`;
- FK к membership и verified course/org;
- unique editor/course;
- state/generation/deadline checks;
- immutable key/history + version/state-transition trigger;
- restricted runtime получил только требуемый `SELECT` для fresh token issuance.

HTTP:

- `POST /api/v1/admin/courses/{courseId}/editors`;
- `POST /api/v1/admin/courses/{courseId}/editors/{editorId}/remove`.

Оба route используют существующие bearer, org-admin permission, HTTPS, Origin, CSRF, strict DTO, idempotency и audit. Content import/status/preview/assets/publish переключены с whole-course Teacher на exact `CourseEditor`; assignment/reference остались на Teacher.

React:

- отдельная вкладка и диалог «Редакторы курса»;
- editor capability показывается отдельно от групп;
- editor-only видит «Материалы и публикация», но не roster и не assignment control;
- teacher-only видит группы/учеников, но не content-control;
- admin course decoder принимает Draft/Published.

## Обязательная acceptance matrix

| Сценарий | Evidence | Результат |
|---|---|---|
| Admin → CourseEditor → T-A import/publish v1 → open/assign five G1 → P01 | real Chromium HTTPS + API + PostgreSQL oracle | PASS |
| G1 teacher без CourseEditor не импортирует/не публикует | `ContentPublicationHttpTests` revoke + group-only paths | PASS, 403 |
| Editor-only не проверяет работы | fresh editor-only token → teacher work/reference HTTP | PASS, 403/no teacher leak |
| Editor C не изменяет другой курс | exact-course import request | PASS, 403/no title leak |
| T-A/G1 не видит G2 rows/counts | teacher projection + Chromium protected JSON + DB oracle | PASS |
| Revoke CourseEditor блокирует новые import/publish, teaching G1 остаётся | fresh issue после revoke | PASS |
| Revoke teaching G1 сохраняет editor capability | fresh editor-only issue/import + empty roster | PASS |
| Другая организация не использует editor C | admin route selector | PASS, 404/no counts |
| Запреты не раскрывают list/count/API body | response assertions | PASS |
| Parallel publish, stale CAS, replay/unknown outcome | parallel publication + stale editor version + receipt replay | PASS |
| Organization switch/session isolation | existing full regression + cross-org editor cases | PASS |

Temporal note: ordinary revoke следует принятой Teacher Revocation модели: новые issue/refresh немедленно исключают grant; ранее выпущенный JWT живёт не более 15 минут. Модель не расширялась и не объявляется instant denylist.

## Выполненные проверки

- Targeted content API: **4 PASS / 0 FAIL**.
- Course roster HTTP authority/idempotency/CAS/cross-org: **1 PASS / 0 FAIL**.
- Component JWT/policy: **27 PASS / 0 FAIL**.
- Restricted runtime regressions после ACL correction: **4 PASS / 0 FAIL**.
- Frontend Vitest: **421 PASS / 0 FAIL**, 19 files.
- Production React build: **PASS**, 300 modules.
- Dedicated Chromium HTTPS E2E: **1 PASS / 0 FAIL / 0 SKIP**.
- Full .NET solution regression: **1138 PASS / 0 FAIL / 12 opt-in browser SKIP** (43 Domain + 369 Application + 343 Infrastructure + 383 API). Dedicated FIX-01 Chromium test was run separately and passed.

Точные команды:

```bash
/tmp/lms-dotnet/dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj --no-restore --filter FullyQualifiedName~ContentPublicationHttpTests -m:1 -p:UseSharedCompilation=false --logger "console;verbosity=minimal"
/tmp/lms-dotnet/dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj --no-restore --filter FullyQualifiedName~CourseRosterHttpTests -m:1 -p:UseSharedCompilation=false --logger "console;verbosity=minimal"
/tmp/lms-dotnet/dotnet test tests/Lms.Infrastructure.Tests/Lms.Infrastructure.Tests.csproj --no-restore --filter FullyQualifiedName~ComponentJwtTests -m:1 -p:UseSharedCompilation=false --logger "console;verbosity=minimal"
LMS_BROWSER_READ=1 LMS_BROWSER_NODE=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node LMS_PLAYWRIGHT_MODULE=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright/index.mjs LMS_BROWSER_EXECUTABLE=/opt/google/chrome/chrome LMS_M1_11_FIX_01_EVIDENCE=/home/tea/github/lms/codex/evidence/M1-11-FIX-01 /tmp/lms-dotnet/dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~CourseEditorAuthorityBrowserTests -m:1 -p:UseSharedCompilation=false --logger "console;verbosity=minimal"
PATH=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin:$PATH /home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/bin/fallback/pnpm test
PATH=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin:$PATH /home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/bin/fallback/pnpm build
/tmp/lms-dotnet/dotnet test Lms.sln --no-build --no-restore -m:1 -p:UseSharedCompilation=false --logger "console;verbosity=minimal"
```

Evidence:

- `codex/evidence/M1-11-FIX-01/01-admin-course-editor-1440.png`
- `codex/evidence/M1-11-FIX-01/02-teacher-published-v1-g1-1440.png`
- `codex/evidence/M1-11-FIX-01/03-student-g1-five-problems-1440.png`
- `codex/evidence/M1-11-FIX-01/test-summary.json`

## Guardrails §§2–3, §6–7 self-review

| Check | Статус и evidence |
|---|---|
| Client authority / IDOR | covered — organization и course берутся/проверяются сервером; forged editor/course/org selectors denied |
| Cross-org access | covered — 404/no rows/counts; composite course/org FK |
| Post-pagination filtering | not applicable — authorization выполняется до roster/content queries; candidate account list admin-only и не является grant |
| File-parent bypass | covered — import owner + exact course/editor + current processing; preview asset parent unchanged |
| Stale authorization across transactions | covered under accepted ≤15m JWT model; fresh issue/revoke tests; publish rechecks processing inside transaction |
| Unsafe restriction narrowing | covered — ProcessingRestrictions path unchanged and regression PASS |
| Mutable history | covered — trigger rejects delete/key mutation/invalid transition; published versions/assignments unchanged |
| Duplicate effects / unknown result | covered — receipts, payload digest, serializable transaction, publish CAS, parallel/replay tests |
| Secrets | covered — no new credentials/secrets; audits and summaries contain identifiers/status only |
| Unsupported readiness claims | covered — synthetic-only; G02/production/real data/pilot remain blocked |
| Wrong org/course/group/role | covered — independent positive controls and denial tests |
| Missing/forged parent | covered by unchanged full regression; new routes use exact course/editor/grant/version |
| Revoked/moved membership | covered — membership generation filtering, fresh issue after independent revokes; existing move/revocation regression |
| Missing processing evidence | covered — import/publish deny under active ProcessingRestriction |
| Inaccessible attachments/photos | not applicable to editor capability; existing attachment/photo negative regression remains PASS |
| Counts/list leakage | covered — foreign/wrong-course bodies checked for title/count/editor/student leakage |
| UI expiry/retry/duplicate | covered by existing session regression and command receipt/CAS; no automatic write replay |

## Open risks / dependencies / owner decisions

- Existing accepted JWT revoke window (≤15 minutes) remains; no new instant denylist was authorized.
- Production database capability/cutover and G02 remain blocked.
- PRS1 exact remains off.
- Full M1-11 acceptance was intentionally not restarted. Historical M1-11 FAIL remains until a separate authorized rerun.
- No push or deploy performed.

## Readiness label

**M1-11-FIX-01 COMPLETE · SYNTHETIC-ONLY VERIFIED · FULL M1-11 NOT RERUN · SYNTHETIC MVP NOT ACCEPTED · PILOT NOT AUTHORIZED.**
