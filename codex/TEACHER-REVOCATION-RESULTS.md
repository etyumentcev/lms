# Teacher revocation — implementation results

> **2026-10-12 owner supersession.** [Physical-completion proof is no longer a Teacher Revocation acceptance or production-readiness requirement](TEACHER-REVOCATION-OWNER-DECISION.md). Physical NOT PROVEN/BLOCKED statements below are retained as historical results, not current blockers and not PASS. Logical deadline/cancellation/no-late-success, replay/unknown-outcome, authorization, isolation, privacy, migration regressions and other gates remain.

2026-10-12. Canonical checkout `/home/tea/github/lms`, existing dirty branch `codex/l1-s4-jwt-api-authorization` retained. No commit/push/merge/deploy, no operational database migration. Only disposable PostgreSQL fixtures were migrated. Authorization: owner «выполни план» and selection «2» (bounded failure model).

**Implemented component/backend boundaries; overall AUTH-01–AUTH-04 acceptance is NOT COMPLETE.** Public scoped refresh/write/file composition and production clock/DB/storage/edge qualification remain open. Green component tests are not a claim that every legacy endpoint has a durable operation or physical completion proof. [Effective contract](../docs/architecture/TEACHER-REVOCATION.md).

## Changes and invariants

- `ComponentJwt` conservative 15m semantics retained. Legacy `Program.cs` rejects missing-start/overlong JWT and uses zero positive skew. `LocalDevelopment.Token` uses one captured start and 15m lifetime instead of eight hours.
- `PostgresComponentTokenIssuer.Refresh` composes verifier rotation, current authority and signing in the same `PostgresIdentitySessions` transaction. New credentials leave only after commit ACK and a fresh deadline/lifetime check. A session can rotate with no educational JWT after teacher/membership revoke; other-org grants remain independent. No ordinary grant cache/denylist introduced.
- `RevocationPolicy`, `ApplicationDeadline` and `QualifiedAuthorityClock` preserve one monotonic deadline plus conservative absolute cutoff. Teacher operation budget 1800s; existing 60/480s budgets preserved. `DeadlineCancellation` requests cancellation 30s before cutoff, includes shutdown in the budget, invalidates the handle, and does not detach the executor. `CourseRequestIngress` carries it through isolated read handlers.
- Additive `20261012120000_BoundedTeacherRevocation` replaces only the S3 function's future revoke lease calculation; existing rows/history/ACLs/generations/CAS/audit remain. Legacy AccessValidUntil becomes a revoke marker for new mutations. Historic two-hour rows are not rewritten.
- Additive `20261012121000_TeacherOperationAttempts` plus `PostgresTeacherOperationAttempts` provide immutable at-most-once attempt identity on existing PostgreSQL. New request/context/key/restart cannot renew the same versioned intent. Payload mismatch conflicts; unknown registration commit is never replayed. This is a trusted internal component, not an authorizer/public endpoint/result resolver.
- `PostgresWorkStore` accepts the original deadline, checks before persistence and dispatch, tracks COMMIT Unknown→Acknowledged, rejects late success. Domain CAS/history/claim semantics unchanged. `AttachmentService` optionally carries the same context and prevents late eligibility/result; `LocalFileAttachmentStorage` checks cancellation before final rename, retaining cancelled private staging for separately bounded cleanup.
- Existing files and prior checkpoint bodies preserved. Effective notices plus an append-only owner register entry identify exact supersession; no historical FAIL/NO-GO was rewritten as PASS. `Submit → Review → Revise → Accept`, current processing, org/course/group/parent protections and the accepted photo-storage risk boundary remain.

## Transaction contract

Refresh: dedicated context / READ COMMITTED; security→user→mapping→session→organization locks; predecessor consume/generation/successor/activity/audit and fresh authority decision inside one transaction; no retry on unknown outcome. O01 <=30s conflict returns no credentials; later replay revokes only that session. No post-ACK token lifetime recalculation.

Attempt: dedicated READ COMMITTED transaction before effects; organization row lock then immutable identity insert; unique key and unique versioned resource intent. FirstAttempt only after timely ACK. Retry returns original deadline/identity with FirstAttempt=false. No automatic resume or deletion. Domain/result/audit atomic integration is NOT supplied merely by reserving an attempt.

WorkStore: owns existing transaction and aggregate CAS. Optional deadline checks before domain persistence and immediate COMMIT dispatch. Lost COMMIT ACK remains Unknown. A late confirmed commit remains committed and does not return success; it is out-of-envelope evidence, not a physical-bound PASS. No new workflow permissions.

Files: private staging, full completed write before final unique no-overwrite key, eligibility only before deadline. FS and PostgreSQL remain separate; unknown originals are retained. Returned streams require the caller's original lifetime across delivery; returning a Stream does not establish bounded completion.

## Tests

SDK: `/tmp/b07c2g/sdk/dotnet` 10.0.401, net10.0. Commands from canonical repository; logs/TRX are in `/tmp/lms-revocation-20261012` and [final TRX/summary evidence](evidence/TEACHER-REVOCATION/test-summary.json) is retained in the repository. Repeated targeted results are not added to full-run counts.

| Run | Result |
|---|---|
| Domain (unchanged, initial solution regression) | 43 passed |
| Application final | 360 passed, 0 failed/skipped |
| Infrastructure final | 302 passed, 0 failed/skipped |
| API diagnostic/full | 365 passed, 3 browser cases skipped, 0 failed (before final optional write/file guards) |
| API final affected paths after write guards | 191 passed, 0 failed/skipped |
| Bounded scenarios after UTC-instant fix | 18 passed, 0 failed/skipped; includes one additional timezone regression |
| Browser/physical tablet/E2E/deployment qualification | NOT RUN |

```sh
/tmp/b07c2g/sdk/dotnet test Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/lms-revocation-20261012/regression
/tmp/b07c2g/sdk/dotnet test tests/Lms.Application.Tests/Lms.Application.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/lms-revocation-20261012/final-application
/tmp/b07c2g/sdk/dotnet test tests/Lms.Infrastructure.Tests/Lms.Infrastructure.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/lms-revocation-20261012/final-infrastructure
/tmp/b07c2g/sdk/dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --blame-hang-timeout 60s --blame-hang-dump-type none --logger 'console;verbosity=normal' --logger trx --results-directory /tmp/lms-revocation-20261012/api-diagnostic
/tmp/b07c2g/sdk/dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName!~IsolatedProcessingDeploymentTests' --logger trx --results-directory /tmp/lms-revocation-20261012/final-api-affected
/tmp/b07c2g/sdk/dotnet test tests/Lms.Infrastructure.Tests/Lms.Infrastructure.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~Bounded_' --logger trx --results-directory /tmp/lms-revocation-20261012/final-bounded
```

The final self-review also fixed non-zero-offset deadline persistence: convert to UTC before rounding PostgreSQL microseconds. The 302-test Infrastructure run preceded this narrow fix; all 18 affected bounded cases passed afterwards, including the new UTC+06:00 regression. Browser skips are exactly `Browser_work_real_student_teacher_history_and_denials`, `Browser_read_real_http_postgres_navigation_and_denials`, and `Browser_photo_real_student_teacher_A4_and_denials`.

Initial solution run: Domain43 PASS; Application359 PASS at that revision; Infrastructure294 PASS/1 FAIL because the old C2G rollback test assumed C2G was the final migration. Fixed by pinning that migration test to its actual target, preserving the retained-history refusal assertion; all 302 Infrastructure tests now pass. API portion was interrupted while diagnosing its long duration; subsequent verbose run shows per-test PostgreSQL container setup rather than proven deadlock. This interrupted run is not counted as full PASS.

Earlier intermediate failures retained in temporary evidence: new TeacherOperation cases exposed an old two-kind budget assumption in the boundary test; updated explicit 1800/60/480 expected values. ACK-loss test needed an explicitly held connection because its synthetic single-connection relay otherwise ended after verifier discovery; corrected and rerun. No expected denial was removed to obtain a pass.

### Required scenarios and independent oracles

| Scenario | Evidence and limit |
|---|---|
| Token older than15m/overlong/no start | ComponentJwt expiry + BearerAuthenticationTests; signature/issuer/audience and negative controls retained |
| Refresh after teacher/membership revoke | `Bounded_refresh_excludes_revoked_teacher_or_membership_and_keeps_other_org`: AccessToken=null for A, B remains scoped; old JWT expires |
| Concurrent issue/refresh and revoke | `Bounded_refresh_waiting_for_organization_observes_committed_revoke` and existing issuance races |
| Operation before revoke / start just before exp | `Bounded_old_token_operation_can_finish_after_revoke_but_refresh_does_not_extend_it`; fake-time old credential, 45m cutoff, no handle reset |
|30m exceeded / late publication | `BoundedExecutionTests`, `Bounded_work_deadline_before_commit_rolls_back_domain_history_together`, `Bounded_upload_finishing_after_deadline_never_publishes_eligible_metadata` |
| Refresh doesn't extend deadline | Original absolute cutoff asserted unchanged after real DB rotation/no-scope outcome |
| Retry/new key/restart | `Bounded_retry_new_request_restart_new_key_and_refresh_never_create_second_attempt`; concurrent first-attempt uniqueness; immutable SQL deadline |
| Lost/late COMMIT | Actual PostgreSQL relay drops ACK for refresh and attempt registration; no credentials/second executor. Late work ACK preserves committed history but no success |
| Other-org file | Existing `Http_photo_originals_and_independent_parent_denials`: own positive control; foreign org/course/group/parent denied before storage OpenCount grows |
|60m bound | Mathematical15+30=45 plus fake-time deadline and cooperative join tests; actual deployment envelope NOT QUALIFIED |

No real15–60 minute sleeps added. Tests that intentionally keep noncooperative work alive join it in cleanup and label it counterevidence to equating cancellation with termination.

## Guardrails §7 / self-review

| Check | Status |
|---|---|
| Actor/use case/invariants/states | covered — effective contract + above |
| S2/S3/PRS1/release boundaries | covered — no new HTTP credentials/routes, no exact processing bypass; production blocked |
| Client authority/IDOR/org/course/group | covered for unchanged protected paths and fresh issuer; attempt input is trusted/internal, never proof of authority |
| Affected subjects/sources/current processing | covered — gates unchanged; no narrower interpretation of restrictions |
| List/count/pagination disclosure | not applicable to new components; existing protected read regression retained |
| File parent/history | covered — independent negative controls; domain/parent relations unchanged |
| Transaction/locks/CAS/retry/unknown | covered for implemented ports; all-route domain/result/audit integration blocked/pending |
| Validation/limits/errors | covered — JWT maximum, immutable deadline, intent validation, no late success; operational accounting/replay retention pending |
| Unit/integration/negative/E2E | component and backend results above; browser E2E/physical envelope NOT RUN |
| Unapproved/unrelated edits | no UI/domain workflow changes, no operational migrations, dirty checkout preserved |
| Real data/secrets/readiness | synthetic fixtures only; no secret output; no overall production/acceptance claim |
| Evidence/limitations | exact commands + TRX; history preserved; optional deadline arguments do NOT prove global enforcement |

Repository-referenced `model-domain`, `implement-use-case`, `implement-feature` skills remain unavailable; execution not claimed. Global `git diff --check` reports a pre-existing EOF blank line in `docs/content/COURSE-CONTENT-SPEC.md`; that unrelated file was not changed. Targeted changed-path whitespace check passes.

## Remaining plan work / readiness

1. Supply one mandatory trusted operation context and attempt composition to every enabled teacher write/file route, including safe result/audit/status handling; current legacy callers can omit the optional deadline. Upload/message intents without natural expectedVersion need their explicit durable identity contract, not an invented version=0.
2. Finish public scoped login/refresh/switch/commands composition with cookies/CSRF/Origin and no legacy alias bypass. No routes were enabled by this change.
3. Qualify the chosen bounded execution envelope (clock, total scheduler/cancellation/I/O/COMMIT/egress <=30s reserve), finite pending capacity and safe cleanup. The machine's existing clock verifier remains unqualified; no production clock override was invented.

**BACKEND COMPONENT CHANGES TESTED · OVERALL AUTH ACCEPTANCE INCOMPLETE · PRS1 EXACT OFF · G02 PRODUCTION BLOCKED · NO PUBLIC HTTP CUTOVER · SYNTHETIC ONLY.**


## Follow-up — internal integration, 2026-10-12

[Coverage matrix, internal adapters, final test evidence and unresolved qualification](TEACHER-REVOCATION-INTEGRATION.md) extend this checkpoint. Historical results above are preserved. Internal scoped command/file and login/refresh/switch composition has been added; Program.cs/public cutover remains unchanged. Global mandatory-context enforcement for legacy routes and physical deployment qualification are still open. AUTH-01–AUTH-04 are not declared fully accepted.

## Follow-up — HTTP and React cutover, 2026-10-12

The historical no-cutover statement above is superseded by the [current route, React, regression and M1 report](TEACHER-REVOCATION-CUTOVER-RESULTS.md). The feature-gated real `Program.cs` composition is connected and legacy teacher write/file bypasses remain fail-closed. Browser session completion, the runtime database profile and operational qualification remain open, so AUTH-01–AUTH-04 and production readiness are still not accepted.
