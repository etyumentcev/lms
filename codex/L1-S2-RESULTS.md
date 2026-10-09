# L1/S2 — Identity Mapping & Session Foundations

2026-10-09. **S2 READY FOR OWNER ACCEPTANCE** for the authorized internal foundations. Owner acceptance of S2 is pending. S3–S8 are not authorized or started.

Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY. LEGAL SOURCE NOT VERIFIED. SYNTHETIC DATA ONLY.

## Authorization, branch and checklist

- Canonical repository: `/home/tea/github/lms`, sequential work, no agents/new worktrees.
- Verified clean base: branch `codex/l1-s1-schema-foundations`, commit `938599eb1e01736c8ff76107027f00db6b477a5a`.
- Requested working branch: `codex/l1-s2-identity-sessions`.
- Implementation commit: `388d27e88f93eff2cfffb4b637e63fba7e52dbc8`. The following documentation commit records this exact tested implementation; its hash is supplied in the final response.
- Owner task accepts S1 and O01 and authorizes only S2. Earlier S1/L0.4 checkpoints remain historical evidence.
- Completed: initial Git/migration/S1 review; normative plan/execution/security/ADR review; O01 recorded before rotation code; identity mapping; session primitives; unit/real PostgreSQL integration tests; full backend regression/build; security/DDD/migration/diff review.
- Public refresh/coordinator/cookies/JWT grants belong to S4 under plan §S2 and the explicit scope-priority instruction. Contract-level simulations below do not claim actual HTTP/browser acceptance.

## Implemented use cases

`IIdentityMapping` / `PostgresIdentityMapping` atomically create an inactive IdentityUser, stable explicit Actor association, Enabled security state and minimal audit. Existing explicit historical Actor IDs can be linked; no identity guessing or reassignment occurs. New credentials have no password and cannot log in until a separately authorized activation. No memberships, educational roles, enrollment, teacher grants, roster workflow or recovery is created. Lookup returns Identity user and stable Actor IDs separately. Database uniqueness, immutable keys and deferred reverse FK remain authoritative; duplicate/malformed mappings fail without partial records.

`IIdentitySessions` / `PostgresIdentitySessions` implement password authentication plus session creation in one transaction, read, single revoke, revoke-all and rotation. ASP.NET Core Identity PasswordHasher verifies credentials (and upgrades an old hash when requested); accepted input length is 12–128. Missing security state, inactive mapping, SecurityBlocked, active Identity lockout, missing/incorrect password deny. Credentials/mapping remain Infrastructure concerns; Application contracts have no Identity or EF dependency.

Session identities contain user/Actor/session/absolute expiry only, not organization or educational permissions. A shared user can have A/B memberships without receiving course authority. No existing ClaimsActorMapper, learning endpoint, Domain aggregate or browser flow is changed. These ports are trusted internal contracts and are not registered/exposed as HTTP services. IDs supplied to read/revoke must be established by a future trusted adapter.

## Refresh protocol and concurrency

- Token: 32 CSPRNG bytes, canonical uppercase hex transport, 256 bits of entropy, no embedded identity/org claims. Server stores only `SHA256("lms-refresh-v1:" + token)`; schema `KeyVersion=1` denotes this verifier format, not a secret HMAC key. ADR-L16 permits a cryptographic hash for high-entropy refresh secrets; challenge HMAC requirements remain unchanged for S6.
- A unique verifier index supports lookup without a full history scan. Only the digest reaches EF/SQL. A second fixed-time comparison checks the stored verifier after locks. Unknown/malformed tokens cannot revoke a known session.
- Dedicated clean DbContext per operation; tracked contexts are rejected to prevent stale security state from defeating a locked reread. One transaction locks account security → Identity user → mapping → session. Revoke-all locks sessions in stable order. This conservatively serializes different sessions of one account too, without revoking them.
- The clock is read after locks at PostgreSQL microsecond precision. Idle expires at `now >= last successful refresh + 60m`; absolute expires at `now >= created + 12h`. Reads/denials/conflicts do not extend activity. Clock regression fails closed; rotation cannot move absolute expiry.
- Success consumes the predecessor, flushes it to satisfy the one-live-verifier index, inserts exactly one successor, increments generation/version, updates activity and appends audit in the same transaction. Secret-bearing success is returned only after commit. Database failure returns no successor.
- Accepted O01: verified consumed predecessor within **30 seconds inclusive** → internal `RefreshAlreadyRotated`, error `refresh_already_rotated`, no identity/token. S4 must project this as **409 without JWT or any Set-Cookie**, including cookie deletion. Expired/revoked/disabled account guards take priority.
- Verified replay after the window revokes only that session and appends `refresh_replay` audit atomically. Other sessions and account state remain unchanged. No automatic SecurityBlocked.
- Lost committed response does not guarantee recovery. There is no stored plaintext successor, redisclosure or request-ID retry credential. Without the new cookie a retry gets conflict during the window, denial/revocation later; new login can be necessary.
- Existing JWTs are not invalidated by these primitives. T04/E03 ≤15-minute issuance and operation bounds must be implemented/verified in S4/S7, not claimed from the S2 lifetime constant.

## Schema and migration review

Additive migration `20261009101331_SessionVerifierLookupS2` creates only unique `IX_RefreshVerifiers_Verifier`. No columns/tables, data backfill, update/delete, changed historical migration or new cascade. Model snapshot/designer match. Down drops only this index; it deletes no history. S1 protected Down remains intact.

Migration targets were exclusively disposable PostgreSQL 17.6 Alpine Testcontainers databases. Explicit S1→S2 upgrade with live synthetic sessions/verifier/audit history preserves the complete snapshot, supports repeat application, and continues rotating. Existing S1 empty-schema, constraint, synthetic preservation and restore tests pass again. No M2/M3, ambient database access, production migration or unknown-data inspection occurred. Idempotent SQL reviewed at `/tmp/lms-s2-index.sql` contains only index creation and EF migration bookkeeping.

## Verification

Environment: .NET SDK 10.0.401 (`/tmp/lms-s1-dotnet`), EF Core/Identity 10.0.12, Npgsql EF 10.0.3; isolated `postgres:17.6-alpine` containers.

| Check | Final evidence |
|---|---|
| Solution build | PASS, 0 warnings / 0 errors |
| Domain | PASS 43/43 |
| Application | PASS 16/16 |
| Infrastructure | PASS 69/69: existing 32 plus 37 S2 cases |
| API regression | PASS 186/186 |
| Total distinct backend cases | **314 passed, 0 failed, 0 skipped** |
| EF model/snapshot, empty and S1→S2 migration | PASS |
| Idempotent SQL generation and diff whitespace | PASS |

Commands:

```bash
/tmp/lms-s1-dotnet/dotnet test Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/lms-s2-results/full
/tmp/lms-s1-dotnet/dotnet build Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false
PATH=/tmp/lms-s1-dotnet:$PATH DOTNET_ROOT=/tmp/lms-s1-dotnet /tmp/lms-s1-tools/dotnet-ef migrations script 20261009094756_SchemaFoundationsM1 20261009101331_SessionVerifierLookupS2 --idempotent --no-build --project src/Lms.Infrastructure --startup-project src/Lms.Infrastructure --output /tmp/lms-s2-index.sql
```

TRX evidence: `/tmp/lms-s2-results/full/`. Initial test compilation caught xUnit analyzer violations (filtered Assert.Single and blocking Task.Result); corrected before successful tests. The first 33-case targeted run passed; the final full run includes four additional audit/lock/migration cases. Initial EF invocation found the host dotnet shim; explicit SDK PATH fixed tooling. These development attempts are not represented as passing test runs.

## Required scenario / acceptance matrix

| Owner scenario | Evidence and status |
|---|---|
| 1. Create/read identity link | PASS: inactive stable mapping, explicit IDs, lookup/missing lookup, duplicate user/actor and malformed input |
| 2. One user in multiple organizations | PASS: A/B memberships retain one user/Actor/session identity |
| 3. No automatic grants | PASS: creation and login leave membership/grant tables empty unless explicitly seeded; no org authority in session result |
| 4. SecurityBlocked | PASS: login/read/refresh deny; waiting login/refresh observe committed block on real PostgreSQL |
| 5. Create/revoke session | PASS: password verification, single and all-session revoke, idempotency, permanent denial |
| 6. Idle/absolute | PASS: exact60m/exact12h, repeated rotations, read does not refresh, clock rollback fail-closed |
| 7. Successful rotation | PASS: one successor, retained consumed history, immutable absolute expiry |
| 8. Two concurrent refresh requests | PASS: separate PostgreSQL connections/transactions synchronized by barrier, one winner and one conflict |
| 9. Replay within30s | PASS: 0/29/30s, no secret/identity/activity update/revocation |
| 10. Replay outside30s | PASS: 30.001s, targeted revocation with security audit |
| 11. Other session isolation | PASS: another session continues rotating, account stays Enabled |
| 12. Parallel tabs | PASS internal simulation of shared cookie/session and sequential/concurrent clients; actual browser coordinator NOT RUN, S4 |
| 13. Lost successful response | PASS: dropped result cannot redisclose successor; eventual denial/new login |
| 14. Cookie response order | PASS internal projection of both result orders; conflict cannot supply replacement token. Actual HTTP Set-Cookie ordering NOT RUN, S4 |
| 15. No tokens/passwords in logs | PASS: raw-secret canaries checked against EF logs, actual SQL parameters, persisted audit/session/verifiers, default JSON and ToString |
| 16. Transaction commit | PASS: create/rotation/replay/revoke commit failure preserves exact snapshot; audit failure after consume rolls back; identity/audit commit failure leaves no partial records |
| Internal lifecycle acceptance | PASS: no two descendants, no timeout resurrection; real PostgreSQL concurrency and rollback |
| CSRF/Origin/HttpOnly/Secure/access memory | Contract documented; no S2 cookie endpoint exists. HTTP/browser enforcement NOT IMPLEMENTED/NOT RUN, belongs to S4 |

## Security / DDD review and limitations

- PASS within S2: credentials/security tables stay outside learning Domain; no new aggregate, no role-based educational bypass, no organization selection in shared session, no history mutation.
- PASS: audit uses fixed action/result codes, generated trace IDs and UUID references only. No raw caller reason/URL/roster/password/refresh token is copied into audit. RefreshSecret is explicitly revealed only at the trusted return boundary; default JSON/ToString redact it. Sensitive EF logging stays off.
- PASS: transaction/lock protocol and schema guards retain one live successor, stable identity, fixed absolute expiry and terminal revocation. Revocation and audit are atomic. Failed audit/commit is fail-closed, including after predecessor SaveChanges.
- Ports require a fresh dedicated DbContext per operation, and do not implement public login rate limiting, attempt accounting, CSRF, TLS, cookie output or browser coordination. Public exposure before S4 is prohibited. Password verification is an internal primitive, not a production SignInManager/activation/recovery workflow.
- Future S4 issuance must read grants and commit its token decision under the same security transaction; the returned S2 identity is not authority to sign a JWT later without rechecking. S6 block/reset/mapping lifecycle writers must use the same lock order and atomic revoke-all semantics, not compose independent committed transactions. No block/unblock/recovery administrative handler is implemented here.
- Fake clocks prove boundary logic, not distributed wall-clock uncertainty, transport timeout or proxy completion guarantees. These remain G02/S4/S7 evidence. Unknown commit outcome/lost transport response can require re-login.
- Verifier/audit history is retained; cleanup/retention/key custody/real-data pilot gates remain separate. No real PII or legal approval is introduced. S1 restore tests are synthetic rehearsals, not production RPO/RTO guarantees.

## Changed files

- `src/Lms.Application/IdentitySessions.cs`: internal contracts, redacted secret/result and numerical session policy.
- `src/Lms.Infrastructure/PostgresIdentityMapping.cs`: atomic inactive identity mapping/audit.
- `src/Lms.Infrastructure/PostgresIdentitySessions.cs`: credential verification and session transactions.
- `src/Lms.Infrastructure/SchemaFoundationMappings.cs`: unique verifier lookup index.
- `src/Lms.Infrastructure/Migrations/20261009101331_SessionVerifierLookupS2.cs` and `.Designer.cs`: additive index migration/model.
- `src/Lms.Infrastructure/Migrations/LmsDbContextModelSnapshot.cs`: index snapshot.
- `tests/Lms.Infrastructure.Tests/IdentitySessionTests.cs`: 37 S2 unit/integration cases and fault/lock/secret fixtures.
- `codex/L03-OWNER-DECISION-REGISTER.md`, `codex/L03-L1-IMPLEMENTATION-PLAN.md`, `codex/CODEX-TASK-L1-EXECUTION.md`, `codex/L03-L1-READINESS.md`: accepted O01, S2 authorization/boundary/checkpoint.
- `docs/adr/ADR-L01-identity-integration.md`, `ADR-L14-cross-organization-identity.md`, `ADR-L15-jwt-delayed-revocation.md`, `ADR-L16-organization-jwt-browser-session.md`: O01 reconciliation without changing T04/E02/E03.
- `docs/architecture/L1-SECURITY-SPEC.md`: accepted replay boundary and S2 scope.
- `codex/L1-S2-RESULTS.md`: this acceptance/evidence checkpoint.

## Dependencies for S3 / stop

S3 requires owner acceptance of S2 and separate explicit authorization. It can consume stable user→Actor mapping while keeping memberships, enrollment/teacher scopes, generations/deadlines and trusted resource ownership separate from authentication. It must not interpret a session or primary role as an educational grant. O01 is resolved, but public JWT/refresh/cookie/browser work still belongs to separately authorized S4.

No push, merge or deploy. **STOP after S2; do not begin S3 without separate owner authorization.**
