# L1/S4 — JWT & Public API Authorization

2026-10-09. **S4 BLOCKED** at the G02 file-completion boundary. This is a blocker checkpoint, not an implemented JWT/API cutover or an acceptance request.

## Authorization and Git baseline

The explicit S4 owner task accepts S1, S2, S3, B01 and O01 and authorizes only S4, on synthetic data. Earlier documents' S4-not-authorized statements remain historical. S5–S8, M2/M3, production migration, real personal data and push/merge/deploy remain prohibited.

Canonical directory: `/home/tea/github/lms`. Initial working tree was clean; branch `codex/l1-s3-membership-scopes`, HEAD `4acd70deab65b92b2e293f19ceb9fa54b489d0a4`. Verified history: S1 `938599e`, S2 `388d27e` / results `26305af`, S3 `81520a7` / results `4acd70d`. Created the explicitly requested branch `codex/l1-s4-jwt-api-authorization` from that HEAD, without another worktree. The checkpoint commit contains this report, appended authorization checkpoints and the fault reproducer; locate it with `git log -1 --format='%h %s' -- codex/L1-S4-RESULTS.md`.

## Implementation checklist

- [x] Verify baseline, clean tree and S1–S3 history; create requested branch.
- [x] Review plan S4/S7 boundaries, execution stop conditions, decision register G02, ADR-L15–L18, security specification and S3 results/contracts.
- [x] Inspect learning API routes, frontend/domain contracts and file transport/storage.
- [x] Reproduce a cancellation/completion gap on synthetic storage; preserve evidence.
- [ ] Resolve G02 completion/fault-model boundary before continuing implementation.
- [ ] JWT issuer/validator: one org, distinct user/Actor/session, explicit concrete grants, conservative expiry and S2/S3 transaction linearization.
- [ ] HTTP login/refresh/switch/logout/session/CSRF, cookie security, exact Origin, O01 and browser coordinator.
- [ ] Replace all learning authorization paths using S3 resource ownership and signed grant policy; filter queries/counts before pagination.
- [ ] File scope/parent/context authorization and bounded transfer.
- [ ] Current E06 processing gates, transactional write recheck, SyntheticOnly enforcement.
- [ ] JWT/HTTP/PostgreSQL/browser S4 security matrix and final security/DDD review.
- [x] Run existing full backend regression plus the new reproducer (results below).
- [x] Review checkpoint diff; no runtime/schema changes.
- [ ] Owner decision on the blocker; implementation and S4 acceptance remain pending.

## G02 blocker: cancellation is not bounded completion

The owner's S4 task §7 says to record a blocker and stop if the existing file mechanism cannot safely be bounded within S4. ADR-L15 requires the full interval, including storage, cancellation, commit and last edge bytes, to fit within eight minutes. The decision register explicitly says timeout configuration is an engineering choice, but unproven bounds stop S4/S7/S8. No extra approval is being requested merely to configure a timeout.

Concrete findings:

1. `LocalFileAttachmentStorage.Open` checks cancellation only before synchronous `new FileStream(...)` (lines 36–40). An already-entered native open is not interrupted by canceling that token. Store also performs synchronous directory/create/move/delete operations; Delete uses synchronous `File.Delete`.
2. `AttachmentService.Upload` compensation uses `CancellationToken.None` for the database existence check and physical deletion (lines 42–43). Preserving potentially committed originals is necessary, but there is no overall completion budget on this path.
3. `AttachmentsController.Content` returns a stream to MVC with no full-operation deadline. The checked-in nginx configuration disables request buffering but specifies no absolute request-to-final-byte deadline and does not disable response buffering. This is a source inspection finding; no proxy fault test was run.
4. The repository does not establish an enforceable upper bound for stalled filesystem operations, their termination, or combined API/storage/edge completion. A timer plus `WaitAsync` could stop waiting while leaving work running; that would not establish the required bound. Closing the client connection alone would not prove completion of storage or database work.

### Executable evidence and limits

`tests/Lms.Infrastructure.Tests/AttachmentDeadlineBoundaryTests.cs` creates an isolated Linux FIFO under a fresh temporary directory, named with a valid storage key. It calls the actual local storage adapter, observes the open remaining pending, cancels the token, and observes that the open is still pending for another 300 ms. Opening the FIFO's writer side releases the native call; the test joins the worker and removes the fixture in `finally`.

The test **passes by reproducing the gap**, not by verifying T04. It does not wait eight minutes, simulate a production disk failure, prove an HTTP exploit, or claim that an ordinary uploaded file becomes a FIFO. The FIFO is a deterministic fault surrogate for a blocking filesystem open. It proves that cancellation alone is not a completion mechanism for this adapter. It does not prove that no redesigned S4 adapter can satisfy T04. Non-Linux runs explicitly skip this Linux-only probe.

### Decision needed / proposed continuation

The current architecture has no demonstrated completion guarantee for this failure case. Stop rather than substitute cooperative cancellation for the accepted hard bound. Proposed owner decision: confirm a narrowly scoped G02 continuation for S4 that establishes the supported synthetic deployment/failure model and a bounded storage/edge execution boundary, while retaining T04 unchanged. That continuation must cover stalled I/O, cancellation/termination, final-byte cutoff, database commit ambiguity and measured clock uncertainty; it must not treat killing a task, returning a timeout, or passing the FIFO probe as sufficient evidence. If a supported bounded execution boundary cannot be demonstrated, keep file operations fail-closed and S4 blocked; do not silently remove required attachment use cases from acceptance.

This is not a proposal to implement S5–S8, general jobs, full file lifecycle or cleanup/reconciliation infrastructure without authorization. If those become necessary, the owner must explicitly resolve the slice boundary. No weakening of 60s/8m/10m/15m/2h is proposed.

## Runtime use cases and endpoint inventory

No S4 runtime use case or new protected endpoint has been implemented at this checkpoint. Existing routes are unchanged and are **not certified for S4**. All paths below must be integrated after the blocker is resolved (prefix `/api/v1`):

| Method | Route | Required boundary |
|---|---|---|
| GET | `/courses/{courseId}/workspace` | Verified org/course; authorized lesson/problem list and counts |
| GET | `/courses/{courseId}/lessons/{lessonId}/workspace` | Server lesson/course match, filtered assignments/counts |
| GET | `/problem-assignments/{assignmentId}/workspace` | Immutable assignment affiliation and student ownership/group grant |
| GET | `/problem-works/{problemWorkId}` | Work → assignment; own work and scoped history |
| GET | `/teacher/problem-works/{problemWorkId}` | Same root; explicit teacher grant, no role-only fallback |
| GET | `/teacher/review-queue` | Scope and current processing filters before count/paging |
| POST | `/problem-assignments/{assignmentId}/submissions` | Submit action, assignment affiliation, atomic attachment eligibility/claim |
| POST | `/problem-works/{problemWorkId}/messages` | Parent work, actor action and attachment scope |
| POST | `/problem-works/{problemWorkId}/reviews/needs-revision` | Review action and submission/work match |
| POST | `/problem-works/{problemWorkId}/reviews/accept` | Same review boundary and domain workflow |
| POST | `/uploads` | Trusted eligible course/org/context, uploader and bounded storage |
| GET | `/attachments/{id}/content` | Pending ownership or attached parent, current processing, quarantine/deleted/unknown deny |
| DELETE | `/attachments/{id}` | Pending ownership/course/context and bounded deletion |

The two work GET routes must both be guarded; no legacy alias exemption. `/api/health` remains an existing health endpoint. Groups, submissions, reviews and history are also embedded resources; their scope checks cannot be reduced to the route parameter alone.

## JWT and session contract status

Planned claims remain ADR-L16: `iss`, `aud`, `iat`, `nbf`, `exp`, `jti`, Identity `sub`, stable `actor_id`, `sid`, one `org_id`, concrete grants and explicit group scope/actions/generations/deadlines. No claim encoding or signing configuration was introduced. Issue/refresh must use S2 security/session locking then the S3 organization lock; detached authority snapshots are not issuance authorization. No per-request current-grant lookup is proposed.

Planned refresh protocol remains O01: opaque secret, HttpOnly/Secure/host-only SameSite cookie; access in memory only; idle60m/absolute12h; rotation and grant decision atomic; consumed predecessor ≤30s returns 409 without JWT or any Set-Cookie; later replay revokes only that session. Lost successor response may require login. Exact Origin and CSRF are mandatory. These HTTP/browser properties are **NOT IMPLEMENTED / NOT VERIFIED** here. Internal S2 regression is not evidence for a public protocol.

## Verification

Synthetic local test environment only; no ambient database migration or real records. PostgreSQL tests use the existing disposable Testcontainers fixtures. Commands:

```sh
/tmp/lms-s1-dotnet/dotnet test tests/Lms.Infrastructure.Tests/Lms.Infrastructure.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~AttachmentDeadlineBoundaryTests --logger trx --results-directory /tmp/lms-s4-results/probe
/tmp/lms-s1-dotnet/dotnet test Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/lms-s4-results/regression
```

| Check | Result |
|---|---|
| Domain regression | PASS 43/43 |
| Application regression | PASS 24/24 |
| Infrastructure / PostgreSQL regression, including new probe | PASS 101/101 |
| Existing API / HTTP integration regression | PASS 186/186 |
| Total distinct backend cases | **354 passed, 0 failed, 0 skipped** |
| Final Linux probe after explicit non-Linux skip annotation | PASS 1/1 (already counted above) |

TRX evidence: `/tmp/lms-s4-results/regression/` and `/tmp/lms-s4-results/final-probe/`. Test builds reported no warnings/errors. The full regression preceded the platform skip annotation; the final targeted rerun verifies that annotation and the unchanged test body on Linux. Existing tests validate predecessor behavior, not unimplemented S4 acceptance.

| S4 acceptance area | Status |
|---|---|
| T04 full bounded file completion / G02 | BLOCKED; reproducer confirms cancellation gap |
| JWT signature/issuer/audience/expiry/org/grant/regrant/switch | NOT RUN for S4 |
| HTTP cookies, CSRF/Origin, refresh races/replay/lost response | NOT RUN for S4 |
| S4 IDOR, all aliases, scoped lists/counts/history/files | NOT RUN for S4 |
| E03 HTTP SecurityBlocked integration | NOT RUN for S4 |
| E06 current restrictions, transactional races, SyntheticOnly gates | NOT RUN for S4 |
| Browser tabs/coordinator and physical devices | NOT RUN: all browsers and devices unverified |
| Schema/migrations | No changes; no S4 migration required by this checkpoint |
| Security/DDD review | Preliminary file-boundary review only; full S4 review pending |

## Stop and downstream dependency

S5 remains NOT AUTHORIZED and cannot rely on this checkpoint as an S4 acceptance. No JWT/API authorization readiness claim, push, merge or deploy. The only code change is the synthetic test. The owner must resolve G02 before continuation; **S4 BLOCKED**.
