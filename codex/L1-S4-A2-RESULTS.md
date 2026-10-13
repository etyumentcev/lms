# L1/S4-A2 — Security Contracts Hardening

2026-10-10. Owner authorization: **S4-A2 COMPONENT HARDENING AUTHORIZED — SYNTHETIC ONLY**. S4-A1 component scope was accepted; this report requests acceptance only for the bounded A2 changes below. T04/E03 are unchanged, G02 remains BLOCKED and public HTTP integration is not authorized.

## Baseline, preservation and commits

Canonical `/home/tea/github/lms`, existing branch `codex/l1-s4-jwt-api-authorization`. Initial HEAD verified exactly `6f9bf19c0f1f8968b09ea288b0a06cd625cacb17`, the accepted S4-A1 commit. Index was empty. Both S4 reports contained the owner acceptance append; A1 also had unrelated table formatting and S4 had older G02 checkpoint changes. Existing modified `AttachmentDeadlineBoundaryTests.cs`, untracked G02/A05/A06/evidence/architecture artifacts and `.obsidian/` were present.

Before implementation, all tracked/untracked nonignored file bytes, SHA-256 manifest, working/index patches and status were saved in `/tmp/lms-s4-a2-preservation`. Exact staging used the HEAD versions of the two reports plus only the acceptance append, leaving working-tree bytes intact. Local commit **`6b8cc2e`** contains only that acceptance (two reports). Existing formatting/G02/test/Obsidian changes were not included. No branch switch, new worktree, push, merge, rebase or deployment.

A2 source/tests and this report/selector decision document are the only intended A2 changes. Existing reports are retained unchanged in the working tree. `.obsidian/workspace.json` was never written or restored by this task. Final preservation check covered 359 baseline files. Outside the six intended tracked A2 code/test files, one external change was observed in `.obsidian/workspace.json`; this task did not edit or restore it. Both pre-existing report files, AttachmentDeadlineBoundaryTests and all other baseline files matched saved SHA-256 values. Whole-tree immutability is therefore not claimed. The first strict preservation check stopped on this external change; it was explicitly classified, not silently ignored. The original Obsidian bytes remain in the preservation directory.

The A2 commit includes only the following 14 files (its SHA is reported in the task completion message):

- `src/Lms.Application/ComponentAuthorization.cs`, `QualifiedAuthorityClock.cs`;
- `src/Lms.Infrastructure/ComponentJwt.cs`, `ComponentSigningKeys.cs`, `PostgresComponentTokenIssuer.cs`, `PostgresCurrentProcessingGate.cs`, `PostgresIdentitySessions.cs`;
- `tests/Lms.Infrastructure.Tests/ComponentJwtTests.cs`, `ComponentHardeningTests.cs`, `ComponentCommitAcknowledgementTests.cs`, `ComponentKeyContractTests.cs`, `ComponentRestrictionHardeningTests.cs`;
- `codex/L1-S4-A2-RESULTS.md`, `L1-S4-A2-OWNER-DECISION.md`.

`git diff --check`, local document links, TRX counters, unchanged branch and exact staging checks passed. Local machine-readable preservation/verification evidence: `/tmp/lms-s4-a2-preservation/a2-validation.json`. Existing unrelated changes remain outside both task commits.

## Implemented changes

### Transaction-safe JWT issuance

`PostgresIdentitySessions.WithIssuingSession` previously returned a signed token while its read-only transaction was disposed without an explicit commit. It now keeps the pending token internal, checks cancellation, awaits `CommitAsync`, checks cancellation again and completes transaction disposal before the caller receives the result. A null signing result exits without successful issuance. Read/sign/commit/disposal failures cannot yield a successful token return. No retry, replay, compensation or rollback inference is introduced for unknown outcomes.

The existing S2 account/user/mapping/session locks and S3 organization lock remain held through signing and commit. Fresh authority still comes from the existing S3 resolver; no detached grants or new stores. After commit/disposal, `PostgresComponentTokenIssuer` revalidates the token's lifetime, clock and signing-key trust before returning it, accounting for waits or qualification loss. E03 remains unchanged: credentials linearized before a subsequent revoke/block can remain locally valid until expiry; current processing checks are separate.

Real synthetic PostgreSQL tests cover rollback after signing, injected pre-commit exception, backend termination, post-commit application acknowledgement loss, cancellation after commit, blocked commit (no early return), expiry/clock loss/key distrust between commit and return. A separate TCP relay test allows real COMMIT to execute, suppresses PostgreSQL `CommandComplete(COMMIT)` and closes before the issuing client receives it. A marker in the same transaction is visible via an independent connection while issuance throws and returns no JWT. This distinguishes committed-but-unacknowledged from rollback. The marker is test-only data in the existing AccountSecurityState fixture, not a runtime issuance write or schema change. Existing A1 lock/revoke/regrant/SecurityBlocked tests remain regression evidence.

### Qualified clock and monotonic deadline contract

`QualifiedAuthorityClock` implements the existing clock port over an explicit trusted `IQualifiedClockSource`. A reading supplies a conservative UTC interval, conservative monotonic elapsed interval, qualification status and epoch ID. Explicit host-provided uncertainty limits bound both interval widths. Invalid/missing readings, source exception, qualification loss, epoch change, negative/inverted/regressing monotonic readings, contradictory UTC/monotonic advancement or uncertainty overflow latch the adapter closed. Requalification requires a new qualified instance; there is no system-clock or stale-reading fallback.

Deadlines use the initial monotonic earliest value plus one budget; remaining time uses the latest conservative monotonic value. Waiting consumes the same budget, UTC correction does not reset it, exact cutoff is expired and qualification loss yields no remaining-time evidence. This is an internal accounting primitive, not cancellation, worker termination, a timer-dispatch bound or HTTP middleware. It is not advertised as enforcing T04 by itself.

Expiry calculation retains earliest UTC + 900s, session cutoff, supplied AccessValidUntil − 600s, downward whole-second rounding, no positive leeway and conservative nbf/exp checks. Extreme DateTimeOffset addition now denies instead of overflowing. S3's existing stronger exclusion of deadline-bearing rows is unchanged; no scheduled-grant semantics were added.

Tests use synthetic clocks only. `IAuthorityClock` remains a trusted internal injection port; no production source or health monitor is registered. Numerical qualification inputs and the assertion that real time lies within the intervals require independent infrastructure evidence.

### Signing-key lifecycle

`ComponentSigningKeys` encapsulates a synchronized in-process trust set and current signer. Key identity is SHA-256 of the public SPKI; JWT headers require exactly `alg=RS256`, `typ=JWT` and `kid`. RSA ≥2048 bits, SHA-256 and PKCS#1 signatures remain fixed; no algorithm negotiation, network key discovery or fallback. `TrustVerificationKey` can supply a public-only RSA handle to a validator without enabling signing. Rotation verifies signing capability, activates the new key and retains old verification trust until explicitly removed. `StopSigning` prevents issuance while allowing existing trusted signatures to validate. `RemoveTrust` stops validation/signing for the key and prevents reintroduction in that ring instance.

Signing/verification recheck the public-key fingerprint so mutation of an externally supplied RSA handle cannot silently bind a different key under an old ID. Disposed/unavailable keys fail closed. The trusted host owns RSA handle lifetimes; there is no private-key export, JSON exposure or token logging in this component. Synthetic tests create ephemeral keys. Key custody, secure destruction, persistence of distrust across restart, distributed rotation, production KMS and incident runbooks are not implemented or qualified.

The internal synthetic wire profile now requires `kid`; old A1 tokens without it are intentionally rejected. No public token rollout or backwards compatibility for a deployed client is claimed. The legacy single-RSA constructor builds the same ring/profile, with a derived ID, for internal callers.

### Current ProcessingRestrictions

[Selector owner-decision request](L1-S4-A2-OWNER-DECISION.md) records the inspected accepted sources and missing grammar/bit mapping/propagation rules. No exact selector interpretation is invented. The existing conservative policy continues to deny Active restrictions for the applicable context and caller/resource student regardless of narrower opaque resource/operation selectors.

The gate additionally rechecks clock qualification and the binding/context time interval after DB waits. Current authoritative no-tracking PostgreSQL reads, dedicated fresh snapshot, same-org course/context checks, missing/ambiguous-context denial and absence of positive cross-request cache remain. Tests use the same gate/policy instance before and after a newly committed restriction, across assignment/work/submission/review, all individual operation bits, malformed/opaque/null/empty selectors, withdrawal, foreign org/course and unknown child. Student restrictions also deny teacher access to the student's children. File access remains denied by component authorization.

This is read-side component evidence. It does not implement list/count materialization filters, mutation/restriction serialization, file/jobs integration or real-data freshness guarantees.

## Verification

Initial full regression: **428 passed, 0 failed, 0 skipped**. **Final full backend regression: 429 passed, 0 failed, 0 skipped** — Domain 43; Application 24; Infrastructure 176; API 186. There are 38 new A2 cases relative to A1’s 391. Final command exited 0; build/test output contains no warnings or errors. Self-review also preserved the wrong-signature negative by using the trusted kid with a wrong signing key, so it cannot pass merely through an unknown-kid rejection. SDK `/tmp/lms-s1-dotnet` 10.0.401, existing dependencies and synthetic `postgres:17.6-alpine` Testcontainers fixtures. No dependencies, schema/migrations, API authorization middleware, endpoints, deployment configuration or normative ADR/register files changed.

```sh
/tmp/lms-s1-dotnet/dotnet test Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/lms-s4-a2-results/final
```

Targeted stages passed: initial 49 tests; expanded clock/restriction stage 60 tests; wire ACK-loss/key negative stage 2 tests. These overlap and must not be summed as distinct test totals. Local logs/TRX are under `/tmp/lms-s4-a2-results/` and `/tmp/lms-s4-a2-*.log`.

## Security review

Self-review by the implementing agent; **not an independent security review**. No delegated reviewer was used. Independent security review remains OPEN.

| Category                           | Evidence and limit                                                                                                                                                                                                                                                                                                                                        |
| ---------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Established component control flow | No successful Issue result before acknowledged commit/disposal; failures propagate without token; post-commit lifetime/trust recheck; fixed algorithm and exact kid lookup; no private-key export/logging path; qualification loss denies. These conclusions are bounded by trusted host/source/RSA/EF-provider contracts.                                |
| Synthetic verification only        | Real local PostgreSQL transaction ordering and rollback/commit faults, transport ACK loss, marker visibility, revoke/regrant/block regressions; ephemeral key rotation/distrust; synthetic UTC/monotonic intervals and boundary conditions; conservative current restrictions and child ownership. Finite tests do not qualify production infrastructure. |
| Infrastructure NOT PROVEN          | Bounded PostgreSQL commit/rollback/WAL/failover completion; production UTC inclusion/error/drift/dispatch; production key custody/rotation/distributed distrust/restart behavior; ingress-to-last-egress bounds; storage isolation/fencing/publication/recovery. No G02 closure or unconditional wall-clock guarantee.                                    |
| Normative OPEN                     | ResourceRef grammar/equality, operation-bit meanings/mapping, exact selector propagation and subject/list behavior; real-data freshness/propagation/in-flight semantics. A06 T04/E03 proposals are not adopted.                                                                                                                                           |

Review considered parsing/defaults, kid substitution and untrusted algorithm/key URL, transaction exceptions before/after actual commit, cancellation, stale authority under locks, clock regressions/uncertainty, key trust loss and parent ownership. The unknown-commit test deliberately demonstrates persisted state despite no issued JWT. No automatic replay or claim of rollback follows. Token validation requires current local key trust but retains ordinary E03 JWT semantics; no per-request grant/session lookup, Redis or denylist was added.

## Remaining S4 HTTP dependencies and next minimal stage

Public HTTP remains unauthorized. It still requires a separately approved scope, applicable G02 PostgreSQL/egress/clock proof obligations, qualified key/clock hosts, accepted selector semantics, independent security review and appropriate read/mutation/stream integration contracts. Storage/fence/publication/recovery obligations remain open for file paths. Login/refresh/logout endpoints, cookies/CSRF/browser coordination, middleware and domain publication are untouched; passing existing API tests is compatibility evidence only.

Next minimal proposal: owner acceptance of A2, then a separately authorized **documentation-only selector-contract decision package and independent security review**. Exact selector matching can follow only an accepted contract and separate implementation authorization. No next stage is started by this report. No S5–S8, real personal data, production clock/KMS, push/merge/deploy.

**S4-A2 READY FOR OWNER ACCEPTANCE · T04 UNCHANGED · E03 UNCHANGED · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**
