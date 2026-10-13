# Teacher operation context — mandatory enforcement

> **2026-10-12 HTTP/React migration follow-up.** The 34-failure and no-cutover snapshot below is preserved as the baseline for this task. See the [current cutover report](TEACHER-REVOCATION-CUTOVER-RESULTS.md) for the migrated routes, zero-failure final regression, real React evidence and remaining M1/AUTH blockers.

> **2026-10-12 owner supersession.** [Physical-completion proof is no longer a Teacher Revocation acceptance or production-readiness requirement](TEACHER-REVOCATION-OWNER-DECISION.md). Physical NOT PROVEN/BLOCKED statements below are retained as historical results, not current blockers and not PASS. Logical deadline/cancellation/no-late-success, replay/unknown-outcome, authorization, isolation, privacy, migration regressions and other gates remain.

2026-10-12. Owner authorized removal of legacy compatibility for teacher writes/files. Sources: `TEACHER-REVOCATION-INTEGRATION.md`, `docs/architecture/TEACHER-REVOCATION.md`, current explicit owner request. Canonical checkout `/home/tea/github/lms`; original branch/HEAD/index retained. No cutover, deployment, commit, new schema, JWT instant revocation, fs-verity or STORAGE-PROFILE-02.

## Contract and transaction

Actor → authenticated teacher; use cases → review, revision, message, upload, pending delete, private file read. Invariants → original qualified ingress, JWT ≤900s, operation ≤1800s including 30s reserve, fixed authority, current processing, parent ownership, one durable attempt. Entities → Work/Submission/Review/Message/Attachment and process-local `TeacherOperationContext`. Existing Submit → Review → Revise → Accept transitions remain unchanged.

The context is sealed, has an internal constructor, no client-bindable authority fields, no ambient state and no renewal API. Production writes create it only in `PostgresTeacherCommands`, after validated JWT, scope/processing checks and acknowledged first durable reservation. It binds actor/work/kind, permits one application entry and one persistence dispatch, and closes on executor completion/failure. Unit/persistence test assemblies have explicit internal visibility for synthetic component fixtures; those fixtures are not authorization/ledger evidence and have no runtime registration.

The executor retains READ COMMITTED ownership, organization/processing locks, fresh processing checks in the mutation transaction, aggregate CAS and atomic history/events. Reservation uses the existing separate transaction; unknown outcome never resumes. Review identity is work + natural expected version; messages/uploads retain stable intent distinct from transport command ID; delete uses attachment identity. No new retry or worker exists. Upload remains before the metadata transaction; delete retains the same context after acknowledged tombstone COMMIT, disposes the DB transaction and awaits file I/O before closing context. File/DB atomicity is not claimed.

## Current path matrix

| Path | Enforced boundary | Missing context / replay behavior | Scope |
|---|---|---|---|
| Legacy POST work reviews/needs-revision, reviews/accept | `ProblemWorkService` requires context before loading work/access policy | 403, no mutation/reservation/file effect | Registered controllers remain; no public cutover |
| Legacy POST work messages, Teacher | Same, actor/work/Message capability | 403 before dependency access | Student branch unchanged |
| Legacy POST uploads | Controller rejects teacher before multipart read; service independently rejects non-student | 403 even with an optional live deadline | No parent/intent invented for old upload contract |
| Legacy DELETE attachments/{id} | AttachmentService rejects teacher before DB/storage | 403 | Student branch retained |
| Legacy GET attachments/{id}/content, Teacher | AttachmentService rejects teacher before DB/storage | 403; no detached stream for teacher | Protected alternative below |
| Internal PostgresTeacherCommands Review/Message | JWT + resource/current processing + durable reservation → context → application → transaction store | Invalid JWT/deadline deny; repeated version/intent never gets another executor | Explicit isolated composition only |
| Internal Upload/Delete | Same context spans file and metadata lifecycle, including post-ACK delete | No second file prepare/cleanup after retry or unknown COMMIT | Existing SHA-256, provenance, owner/parent checks retained |
| Internal ReadAttachment | JWT + independent parent/current processing + original deadline → read context through buffered I/O | No write attempt needed; no late result | Isolated HTTP adapter retained |
| Protected submission photo list/original | Existing validated token/parent/current processing/deadline plus teacher read context around private I/O | Expired/unqualified ingress fails closed; student read remains unchanged | No activation change |
| Public PostgresWorkStore.Save | New teacher domain events require trusted internal transaction entry | Direct teacher history save fails before transaction/write | Student events retained; raw DB admin capability is not a teacher endpoint |
| Internal SaveInTransaction | Non-null context, bound work, one persistence dispatch, existing transaction and original deadline | Null/closed/reused/mismatched capability denied | Only runtime caller is private executor TransactionStore |
| Student SubmitSolution/PostMessage and student attachment operations | Existing student authorization/domain/file checks | No teacher context required | No teacher submission capability added |
| Course/material/work/history reads | Existing read policies | Unchanged | No writes or new permissions |
| Identity/security/admin operations | Existing dedicated administrative/session protocols | Outside educational teacher-grant model | JWT cap and refresh issuance unchanged |
| Content compiler/importer | Trusted isolated synthetic tools | Not exposed by teacher authorization | No new infrastructure |
| Background/resume/alternate handler | No registered educational background worker or resume endpoint found; application/store guards apply to direct callers | No automatic fresh context or replay after restart | Future exposed executors must use existing admission/reservation boundary |

## Tests and evidence

See `evidence/TEACHER-CONTEXT-ENFORCEMENT/test-summary.json` for exact commands, counts, failures and final-state runs. All evidence is synthetic. Initial failures and old checkpoints are retained; no skipped/failed run is labelled PASS.

The new browser test drives real Chromium clicks over loopback HTTPS into PostgreSQL: Submit → Review → Revise → Accept → retry with a new command key (409), then independently checks two submissions, two reviews, two messages, six domain events and two durable attempts. It is an **isolated protocol harness**, not the production React workflow or production readiness.

## Guardrails §7 / limits

- Covered: actor/use case/invariants/states, accepted scoped authority and conservative processing, independent work/attachment parent checks, immutable history, CAS, original deadline, unknown outcome and at-most-once attempt. Negative tests assert missing/invalid context before dependency access and no DB/file effects; existing scoped tests cover org/group/role/processing/parent denial.
- Covered: no new list query/filter/count path; existing protected read/browser suites are rerun. No client authority fields, new scopes, migration, public registration or credentials in evidence.
- Component fixtures: historical read data are explicitly seeded outside teacher routes; storage-only tests use a synthetic context and must not be treated as ledger/admission proof.
- BLOCKED until recorded final tests pass: full regression and complete current UI workflow acceptance. Registered legacy teacher operations intentionally fail closed; React/legacy positive HTTP contracts need migration to the reviewed secure protocol before an authorized future cutover. No fallback is enabled to make an obsolete success expectation pass.
- NOT PROVEN: deployment clock/DB/kernel/storage/edge completion guarantee and bounded cleanup/capacity. Conditional ≤15m + ≤30m arithmetic does not prove physical termination. AUTH-01–AUTH-04 remain unaccepted, production readiness is not claimed.

## Changed files

Exact manifest: `evidence/TEACHER-CONTEXT-ENFORCEMENT/changed-files.json` (24 source/test/checkpoint paths; evidence files listed separately in test-summary).

| Files | Change |
|---|---|
| `src/Lms.Application/TeacherOperationContext.cs` | Opaque actor/work/kind capability, original deadline, single entry/persistence dispatch and closure |
| `src/Lms.Application/ProblemWorkService.cs` | Mandatory context for teacher reviews/messages before dependency access; student branches retained |
| `src/Lms.Infrastructure/PostgresTeacherCommands.cs` | Capability issuance after admission/reservation and propagation; context spans files/metadata; result gates after awaited file/DB disposal |
| `src/Lms.Infrastructure/PostgresWorkStore.cs` | Deny direct teacher events; internal store requires bound capability and identical original deadline |
| `src/Lms.Infrastructure/AttachmentService.cs`; `src/Lms.Api/AttachmentsController.cs` | Independently close teacher legacy file calls; deny upload before multipart read |
| `src/Lms.Infrastructure/PostgresProtectedPhotoQueries.cs` | Teacher-only read capability around existing authorized private photo I/O |
| `tests/Lms.Application.Tests/TeacherOperationContextTests.cs`; `ProblemWorkServiceTests.cs` | Missing/invalid/reused/closed context checks and explicit unit fixture context |
| `tests/Lms.Infrastructure.Tests/MandatoryTeacherContextTests.cs` | Pre-effect file/store denial, deadline substitution and late file-disposal tests |
| `tests/Lms.Infrastructure.Tests/TeacherPersistenceFixture.cs`; `PersistenceTests.cs`; `BoundedWriteBoundaryTests.cs`; `SchemaFoundationTests.cs`; `ScopedAuthorityTests.cs` | Explicit synthetic storage/read fixtures; retain CAS/history/rollback/late-ACK assertions without reopening a production legacy port |
| `tests/Lms.Api.Tests/MandatoryTeacherContextTests.cs` | Six registered legacy teacher routes deny with DB and student positive controls |
| `tests/Lms.Api.Tests/TeacherOperationJourneyTests.cs`; `tools/browser_read/teacher-journey.mjs` | Real HTTPS/Chromium/PostgreSQL journey and duplicate command-key retry |
| `tests/Lms.Api.Tests/ProtectedPhotoHttpTests.cs`; `ProtectedWorkHttpTests.cs`; `ReadApiTests.cs` | Seed historical read data explicitly instead of executing unsupported legacy teacher commands |
| This report; `codex/TEACHER-REVOCATION-INTEGRATION.md`; `docs/architecture/TEACHER-REVOCATION.md` | Current matrix and dated supersession notices preserving prior checkpoint text |

## Remaining work / acceptance blockers

1. Full API regression is still not green: retained legacy positive write/file expectations conflict with the new fail-closed contract. Several broader workflows still use these calls as setup. Those tests and their supported positive replacements require completion; existing failures are evidence of unfinished work, not acceptance.
2. Actual React teacher calls still target the legacy contract. No public secure-route registration or frontend cutover was performed. The passing protocol harness does not establish the requested full application browser journey.
3. Browser harness network failures were traced to Chromium `ERR_NETWORK_CHANGED` while other Testcontainers fixtures changed Docker networks. The new browser class now runs in a collection with parallelization disabled; isolated revalidation is recorded separately. All failed and successful attempts remain in evidence.
4. Operational clock/DB/filesystem/transport timing and cleanup/capacity qualification remain unconfirmed. No production readiness or global AUTH-01–AUTH-04 acceptance.

**Mandatory fail-closed enforcement implemented; the overall requested task is not fully complete.** No claim that all acceptance criteria pass.

## Measured results

| Suite | Result |
|---|---|
| Full Domain | 43 passed, 0 failed |
| Full Application | 369 passed, 0 failed |
| Full Infrastructure | 333 passed, 0 failed |
| Full API, browsers enabled | 346 passed, **36 failed**, 0 skipped |
| Final affected infrastructure after awaited-disposal correction | 23 passed, 0 failed |
| Final isolated API after disposal/read-fixture/browser-isolation corrections | 14 passed, 0 failed, including actual Chromium Submit → Review → Revise → Accept → duplicate 409 |
| Protected read/work/photo Chromium cases in full run | 6 passed (Student and Teacher for each) |
| Frontend | 408 passed; build passed |
| Content compiler / importer | 74 / 49 passed |

The full solution run preceded the final disposal correction and the last two test-harness corrections. The two failures covered by those follow-ups are retained in the full-run evidence. **34 legacy HTTP/workflow failures remain unresolved**; their names and actual errors are in [remaining-api-failures.json](evidence/TEACHER-CONTEXT-ENFORCEMENT/remaining-api-failures.json). They include teacher review/message/upload/file expectations and student/progress scenarios whose setup depends on legacy teacher writes. They were not skipped, weakened or relabelled PASS.

Exact commands and TRX/log artifacts: [test-summary.json](evidence/TEACHER-CONTEXT-ENFORCEMENT/test-summary.json). The synthetic journey screenshot/result are under `isolated-browser/mandatory-teacher-journey/`; previous network failures remain alongside them. This is component/protocol evidence only, not full application E2E acceptance.

Preservation check: original branch `codex/l1-s4-jwt-api-authorization`, HEAD `492e30aef65bbddd24f107903bfaa54a563abe39`, and index SHA-256 `2a0e936a9f7a3395633ba3ee7084970af663a4412cec6f92f72b765a264f56a4` unchanged. No unexpected baseline file changed. Changed-file whitespace and new checkpoint links pass. Whole-checkout `git diff --check` still reports the pre-existing blank line at EOF in `docs/content/COURSE-CONTENT-SPEC.md`; that unrelated file was preserved.
