# L1/S4 — JWT & Public API Authorization

2026-10-09. **S4 BLOCKED** at the G02 file-completion boundary. This is a blocker checkpoint, not an implemented JWT/API cutover or an acceptance request.

## G02/A02 — bounded execution architecture package — 2026-10-09

**G02 ARCHITECTURE READY FOR OWNER DECISION. S4 remains BLOCKED; T04 NOT PROVEN.** The owner authorized DESIGN only and explicitly approved keeping this package on the current `codex/l1-s4-jwt-api-authorization` branch in canonical `/home/tea/github/lms`. Baseline HEAD `a60ee6c`; pre-existing uncommitted changes in this report and `AttachmentDeadlineBoundaryTests.cs` were preserved. No new worktree or runtime implementation.

This A02 section supersedes the request to authorize architecture investigation in the older continuation below; it does not supersede its blocker evidence or convert historical tests into acceptance. Architecture readiness means the options, recommendation and verification obligations are ready for review, not that a supported T04 deployment has been demonstrated.

- [Architecture and invariants](../docs/architecture/L1-G02-BOUNDED-EXECUTION.md)
- [Failure model and guarantee limits](../docs/architecture/L1-G02-FAILURE-MODEL.md)
- [Options and recommendation](L1-G02-OPTIONS.md)
- [Verification plan](L1-G02-VERIFICATION-PLAN.md)
- [Concrete owner decision](L1-G02-OWNER-DECISION.md)

Recommendation: a restricted storage gateway with immutable versions, isolated I/O workers, durable operation identity, storage-side fencing/quiescence, atomic PostgreSQL result state and independent supervisor/controlled edge. A process/container kill or PG generation alone cannot fence issued filesystem writes. Unknown commit preserves potentially committed bytes, prohibits replay/premature purge and requires authoritative serialized reconciliation. Recovery cannot extend an expired user operation.

T04 remains 60s / 8m / 10m reserve / access JWT up to 15m / ordinary revoke no later than 2h. Stage budgets are proposed research envelopes, not established bounds. No bounded termination contract for native I/O, PostgreSQL WAL/commit, scheduling or final edge egress exists on the current baseline; full T04 cannot presently be proved. Fail-closed publication is not proof of physical completion. The proposed failure model explicitly excludes arbitrary correlated OS/hardware/network failures and does not claim a universal guarantee.

A02 validation: documentation links, required coverage, diff and preservation of the existing test file; no new runtime tests or fault injections. Prior 355/355 evidence below belongs to the earlier continuation. No runtime/config/schema/test changes by A02. Separate explicit owner approval is required before implementation; S5–S8/full S7 lifecycle, M2/M3, production migration, real personal data and push/merge/deploy remain prohibited. Stop after this package; G02 acceptance and S4 completion remain pending.

## G02 authorized continuation — 2026-10-09

**Owner continuation AUTHORIZED; G02 NOT RESOLVED; S4 remains BLOCKED.** This section supersedes the historical request for G02 authorization below. Baseline verified: clean canonical `/home/tea/github/lms`, branch `codex/l1-s4-jwt-api-authorization`, HEAD `a60ee6c`. No new worktree. T04 is unchanged. This continuation performed design research, a candidate process-isolation fault probe and backend regression; it did not implement or certify a bounded production storage adapter.

### Deployment / failure model assessment

Inspected ADR-L03, ADR-L06, ADR-L15, L1 Security Spec, owner T04/G02 register, S4/S7 plan and execution stop conditions, actual storage/service/controller and Compose/nginx configuration. Existing deployment is ordinary Linux + Docker Compose + local persistent attachment volume + PostgreSQL 17 + nginx. No verified filesystem, device, hypervisor scheduling, clock-error, watchdog/fencing or recovery latency contract exists. The local development stack is not a certified deployment.

A **candidate restricted model**, not an accepted supported deployment, would require: synthetic data only; a pinned Linux/kernel/filesystem/device stack; no NFS/FUSE or arbitrary devices in attachment storage; bounded CPU scheduling and monotonic clock drift; bounded local filesystem and PostgreSQL/WAL completion or independently enforced fencing; a single controlled ingress with an absolute last-byte deadline; no uncontrolled proxy hop; no operation restart/replay after timeout. Multiple API instances would each have to satisfy the same contract. None of the missing numerical bounds can be inferred from a successful local test. Excluding every stalled filesystem/commit from the model would evade this task rather than resolve G02. Consequently **no deployment model satisfying T04 is certified by this continuation**.

### Alternatives and minimal candidate

| Mechanism | Assessment |
|---|---|
| CancellationToken, Task.Run + WaitAsync, HTTP 504 | Reject: stops waiting or requests cancellation while native work may continue. |
| Dedicated thread, close stream from another thread | Reject: no safe forced managed-thread termination; native open has no returned handle to close; close/flush may themselves wait. |
| One child process per storage operation; private IPC; kill then reap | Smallest useful isolation candidate. Parent must never run native attachment I/O, including stat/cleanup, and must retain admission slot until exit is observed. It can terminate the interruptible FIFO surrogate. It supplies no upper bound for arbitrary uninterruptible kernel I/O or already issued device writes. Not selected as a sufficient T04 mechanism. |
| Container stop/kill or cgroup kill | Same kernel/device boundary; signal dispatch is not observed completion. Moving the same worker into a container does not close the proof. |
| External storage service with request timeout | Client timeout does not fence server mutation. Requires a server-side completion/fencing contract and evidence; adds a dependency, not justified as a silent fix. |
| Independent watchdog/fencing for the execution and persistence boundary | Candidate architecture extension requiring separate owner authorization and concrete infrastructure latency evidence. Power-off alone does not prove cancellation of issued writes or safe restart; edge egress and stale writers must also be fenced. Not implemented or deployed. |

Primary references checked on 2026-10-09: [Linux completion waits](https://docs.kernel.org/scheduler/completion.html) document default uninterruptible waits without a timeout; [Linux scheduler timeout semantics](https://www.kernel.org/doc/html/v5.10/driver-api/basics.html) distinguish interruptible waits and potentially unbounded scheduling waits; [PostgreSQL 17 transaction/statement timeouts](https://www.postgresql.org/docs/17/runtime-config-client.html) define useful session/query limits, not a device-failure termination SLA; [nginx proxy timeouts](https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_read_timeout) apply between reads, not to the whole response. The conclusion that these primitives do not supply the combined required bound is an architectural inference, not a claim that every Linux I/O wait ignores SIGKILL.

### Full-operation proof obligations and compensation audit

Let admission start an immutable monotonic budget B (60s ordinary, at most 480s explicitly long upload/download). A sufficient implementation must prove `execution cutoff + worst-case termination/rollback/edge-drain tail <= B`, counting queue, body, validation, storage, database, disposal and final edge send. Overlapping stages may share a budget; sequential compensation cannot receive a fresh one. Timer dispatch/scheduling error must also fit B. No numeric termination tail is established here, so assigning e.g. a 450s timeout would be an invented proof. The separate 120s T04 clock margin cannot pay for execution beyond 480s. Conservative issuer/admission clock bounds remain unverified.

- **Storage:** kill must be followed by observed process exit and a contract for outstanding I/O. Timed-out worker cannot be detached, replaced while still active, or publish a late result. Marking storage unhealthy blocks future admission but does not finish an existing writer.
- **API/body/edge:** an admission deadline must cover active trickle traffic, slow response consumers and every Range request. Current nginx buffering and inactivity timeouts do not establish an absolute last-byte cutoff. Disabling buffering alone is insufficient. No edge integration proof was run here.
- **Database:** statement/lock/transaction timeouts and provider cancellation are necessary controls, not evidence that commit/WAL/rollback/recovery always finish in the remaining budget. Killing the API does not terminate an independently executing PostgreSQL backend. Lost commit acknowledgement may mean committed data; never automatically replay a command or claim rollback of committed history.
- **Upload compensation:** current `CancellationToken.None` database lookup and delete have no shared budget. An absence read while the original commit is unresolved is not proof that it will never commit. Before unlink, prove the original transaction has ended and metadata is absent in an authoritative fresh view. Unknown outcome must preserve bytes and deny publication/claim, with no detached deletion. A failed Store can also leave temporary data before the service's compensation try block.
- **Delete:** existing tombstone-before-unlink protects eligibility when physical delete fails, but neither unlink nor transaction disposal has a proven completion bound. Never compensate by deleting an attached original.
- **Fail closed:** required for unavailable/unknown storage, unknown clock bounds, unresolved worker exit and ambiguous publication. Existing runtime is **not certified fail-closed for G02**. No flag that disables required attachment use cases is offered as acceptance. A quarantine/reconciliation/restart protocol beyond the existing S4 boundary would require the owner's separate S7-scope decision.

### Security and architecture review outcome

Self-review, no independent reviewer or delegated agent: **NOT APPROVED for G02 acceptance**. Critical unresolved findings are native I/O termination, DB commit/rollback termination, edge final-byte cutoff, unknown clock/scheduling bounds, and safe compensation after unresolved commit. A process worker solves only one fault class. Adding it to the public runtime now would leave the required guarantees unproved. No runtime/schema/auth cutover changes were made; existing file endpoints retain their pre-S4 limitations.

The explicit owner stop condition applies: a complete candidate would need a separately approved execution/persistence fencing boundary (or a concrete bounded platform contract), and potentially a narrowly scoped quarantine/restart recovery contract overlapping S7. This is not a request to approve timeout values or to repeat G02 authorization. Stop before implementing that architecture extension. Owner decision requested: authorize investigation/design of this boundary, retaining T04 and keeping deployment, M2/M3 and S5–S8 implementation prohibited. Such authorization alone would not constitute technical proof.

### Continuation evidence and completion matrix

New `Isolated_FIFO_reader_can_be_killed_and_reaped_without_releasing_IO` test launches only a synthetic FIFO reader, observes its kernel FIFO-open wait through `/proc/<pid>/wchan`, kills it, observes exit within a 5s test limit, verifies no output, and joins/cleans up in finally. No shell command string or user input is executed by the child. This is an OS isolation feasibility probe, **not the .NET storage adapter**, not a D-state device fault, not DB rollback, and not an eight-minute integration proof. The pre-existing actual-adapter cancellation reproducer remains intact. `/proc` wait-channel visibility is required on this Linux test host; no hidden passing fallback.

Regression command: `/tmp/lms-s1-dotnet/dotnet test Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/lms-g02-results/regression`. Results: Domain **43/43**, Application **24/24**, Infrastructure/PostgreSQL **102/102**, API/HTTP **186/186**; total **355 passed, 0 failed, 0 skipped**. Build output reported no warnings/errors. This regression checks existing behavior plus the new probe; it does not certify the unimplemented G02 boundary. TRX evidence is in the results directory above.

| G02 criterion | Result |
|---|---|
| Document supported failure model | Candidate and missing bounds documented; supported T04 deployment NOT ESTABLISHED |
| Implement bounded completion mechanism | NOT IMPLEMENTED; no sufficient mechanism selected within verified boundary |
| Blocking I/O / cancellation | Existing adapter gap reproduced; interruptible child-process termination probe added |
| Rollback / commit / edge fault integration | NOT RUN for proposed boundary; stop condition reached |
| Full <=480s proof | NOT ESTABLISHED; passing short probe is not proof |
| Full backend regression | PASS: 355/355, 0 failed, 0 skipped |
| Positive security review | NOT APPROVED |
| Continue S4 / final S4 acceptance | NO; S4 BLOCKED, S5 not started |

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


## G02/A03 — Infrastructure Evidence Package — 2026-10-09

**INFRASTRUCTURE EVIDENCE BLOCKED. T04 NOT PROVEN. S4 remains BLOCKED.** Owner accepted G02/A02 option C as the architectural direction and explicitly authorized A03 infrastructure research and isolated synthetic experiments. This supersedes A02's historical pending decision only for that direction/research scope; runtime implementation remains unauthorized.

Canonical `/home/tea/github/lms`, existing branch `codex/l1-s4-jwt-api-authorization`, HEAD `a60ee6c73c4400bd1bdf6c6df434358372745c41`. Existing S4 text, modified synthetic C# test, A02 documents and other working-tree content preserved; no branch switch/worktree created. A03 adds seven documents and a standalone evidence harness/results under `codex/evidence/L1-G02-A03`, outside LMS runtime/build/test integration.

[Implementation readiness / owner report](L1-G02-IMPLEMENTATION-READINESS.md) is the current A03 conclusion, with links to all seven mandatory artifacts. [Experiments](L1-G02-EXPERIMENT-RESULTS.md) distinguish contracts, measurements, assumptions and unverified claims. Real isolated probes covered FIFO open/read/write kill/reap, user-space hang, epoch TOCTOU, writable FD after surrogate seal, PostgreSQL lost application ACK/false absence/rollback/backend termination/clean restart, TCP buffered delivery, nginx trickle and buffering/cutoff. Final harness completed successfully and removed its containers/anonymous volumes. Two earlier harness setup failures and their fixes are recorded; no hidden PASS.

P03 Linux/ext4/NVMe/Docker/PG17.6/nginx1.28.3 has no established hard bounds for storage quiescence, WAL/rollback termination, controlled-egress queues or clock/scheduler dispatch; independent cross-kernel supervisor is absent. Successful finite probes do not supply these contracts. Device/WAL/failover faults, physical NIC cutoff, real-time 60/480s full LMS paths and V2/V3 integration remain NOT RUN/NOT ESTABLISHED. V0 covers A03 only; V1 FAIL, V2/V3 not satisfied. I1–I7/V0–V3 retain their A02 definitions; T04 is unchanged, including prohibition on spending the 120s clock reserve as execution time.

A03 validation: standalone fault probes, evidence JSON/log/checksum and local documentation-link checks, preservation of all pre-existing files except this append. No LMS runtime/config/schema/test-source changes; backend regression not rerun, historical 355/355 remains earlier evidence. No independent security review or T04 acceptance claimed. Proposed next owner decision is infrastructure contract investigation with a concrete alternative/device fault boundary, or rejection of P03; no further work auto-authorized. Work stopped after package, with S5–S8/full S7 lifecycle, M2/M3, production migrations, real personal data and push/merge/deploy excluded.
