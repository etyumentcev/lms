# L1/S3 — Membership, Scopes & Resource Ownership

2026-10-09. **S3 READY FOR OWNER ACCEPTANCE** for the authorized internal foundations, including **B01 ACCEPTED — Generation-aware Enrollment Move**. S3 owner acceptance is pending. S4–S8 are not authorized or started.

SYNTHETIC DATA ONLY. Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY. LEGAL SOURCE NOT VERIFIED.

## Authorization, branch and commits

- Canonical repository: `/home/tea/github/lms`; sequential work, no agents or new worktrees.
- Verified original clean base: `codex/l1-s2-identity-sessions`, HEAD `26305af` (`docs: record L1 S2 acceptance evidence`), containing S1 `938599e` and S2 `388d27e`.
- Explicitly requested branch: `codex/l1-s3-membership-scopes`.
- Tested implementation and architecture checkpoint: **`81520a760c644302f641e64b0ced080ef4e8b960`**.
- The following documentation-only commit records this report; its exact hash is supplied in the final task response.
- Owner explicitly accepted S1/S2/O01 and authorized S3, then accepted B01 and authorized the forward change to enrollment-specific GroupId guards. Earlier slice-authorization statements remain historical evidence, superseded by the new checkpoints.

Checklist complete: verify base/documentation; preserve B01 reproducer; implement authority mutations and generations; implement current-grant and resource resolvers; add required forward migration/history/affiliation sidecars; test PostgreSQL races, constraints, restricted-role access and rollback; run full backend regression/build; review security/DDD/SQL/diff; commit implementation; record evidence. No public cutover.

## B01 resolution

The initial review found a real incompatibility: S3 required `move enrollment`, while S1 fixed GroupId in EnrollmentScopes and AccessEnrollments for the lifetime of the row. The primary key allowed only one sidecar per student/course, and deletion was prohibited. Work stopped and the owner explicitly approved B01 before the forward replacement was implemented.

The initial reproducer is retained as `Enrollment_move_is_blocked_even_after_revoke_and_new_generation`, pinned to the original S2 migration. It still proves the old guards reject a move even after revoke/new generation. `B01_forward_upgrade_preserves_S2_rows_and_does_not_backfill_history` proves the new migration preserves the existing rows, leaves new history/affiliation tables empty on upgrade, supports repeat application, and then permits a valid move.

## Implemented use cases

`IScopedAuthorityAdministration` / `PostgresScopedAuthorityAdministration` expose a trusted internal command for membership grant/regrant/revoke, enrollment grant/regrant/revoke/move and teacher assignment grant/regrant/revoke. Expected version zero means create only. Educational mutations verify the active membership generation and Student/Teacher role. Course ownership and course-local group identity are validated on server rows. An existing unclassified legacy enrollment or teacher grant is not silently adopted.

Mutations call a restricted PostgreSQL function under a dedicated transaction. Its organization-row lock serializes competing operations; expected version rejects stale/repeated requests. Active predecessors are retired before an explicit new generation; versions can advance twice for retire-plus-regrant. EnrollmentScopes and AccessEnrollments, append-only authority history and audit commit together. The API returns only after successful commit. Audit/statement/constraint/commit failure rolls back all rows. PostgreSQL sequences may have ordinary unused values after rollback; they are not authority generations.

Revoke uses PostgreSQL time read after taking the lock and fixes AccessValidUntil to request time plus two hours. Repeated revoke with the current version returns the original result without extending its deadline. Membership regrant never updates dependent MembershipGeneration: old grants remain ineffective until explicitly regranted. Permission flags and primary/global roles do not create educational grants.

`IScopedAuthorityReader` / `PostgresScopedAuthorityReader` resolve current membership and concrete grants, trusted course/group/resource ownership, and Read/Submit/Review checks. Standalone decisions use one repeatable-read snapshot. They use PostgreSQL state, not caller-supplied ownership or unvalidated claims. Submit additionally checks assignment status, available published lesson and work state; Review checks submitted/current/unreviewed submission and teacher scope. Domain workflow guards remain authoritative at mutation time.

## Resource affiliation and preservation

- Course → Verified CourseRegistry → enabled synthetic organization; group → composite course/group registry.
- New assignments created under current scoped enrollment receive immutable AssignmentScopes through a server trigger, under the organization lock. Captured fields: organization/course/student/group and membership/enrollment generation.
- Work → assignment; submission → work; review → its submission with matching work. Missing affiliation is denied, including old assignments in a known course.
- A move changes only authority/current enrollment. Existing work, submissions, reviews, messages, attachments, Actor/AuthorRole and assignment affiliation are not rewritten. New assignments capture the destination group. Old-group teachers retain only work bound to their group; destination-group teachers do not automatically receive old work.
- A current student enrollment covers the student's own earlier work in that course. An older grant cannot gain resources created under a later membership/enrollment generation. Explicit whole-course teacher grants span groups; group grants do not cover ungrouped work.
- Pending attachments require uploader ownership, eligible unexpired metadata, a valid nonquarantined AttachmentScope, verified course and synthetic context. Attached files resolve through their parent and validate parent attachment IDs/uploader, including when the requester is the uploader. Missing/deleted/quarantined/unclassified files deny.
- Conflicting course/group hints supplied alongside direct IDs are rejected. No assignment classification or owner inference occurs on read.

## Schema and migration

New migration: **`20261009153218_ScopedAuthorityS3`** after S2. Applied S1/S2 migrations are unchanged. EF designer and model snapshot agree; latest-schema tests verify no pending model changes.

Added two tables:

1. `AuthorityHistory`: append-only snapshots of membership/enrollment/teacher state transitions, including the retired predecessor and deadlines. History capture starts on new mutations; migration does not backfill old history.
2. `AssignmentScopes`: immutable server-captured affiliation for newly created scoped assignments, with RESTRICT FKs to assignment/student, membership and course/group, plus a migration-owned composite course/organization FK and positive-generation CHECK.

The forward migration replaces only the enrollment-specific GroupId protocol. Stable StudentId/CourseId/OrganizationId remain protected. Group changes require a retired predecessor and next generation. Deferred constraints check final GroupId and active-state agreement of both enrollment representations. Other identity/teacher/attachment/history protections are retained. Down refuses destructive removal of authority/affiliation history.

The security-definer mutation function has a fixed search path, schema-qualified persistent tables and PUBLIC execution revoked. Deferred consistency checks run with their own narrowly defined definer capability, so the calling runtime role does not need direct table write privileges. The capture trigger also has PUBLIC execution revoked. No web role is granted mutation privileges by the migration.

Restricted-role tests prove successful protocol execution and denied direct DML, history/affiliation writes, trigger disabling and schema CREATE. A role without the explicit function grant cannot execute it. Actual deployment must use the documented restricted roles, not migration-owner/superuser credentials; S3 neither provisions production roles nor claims an existing deployment already has those privileges. See [authority design and privilege contract](../docs/architecture/L1-S3-SCOPED-AUTHORITY.md).

Migration targets were exclusively disposable `postgres:17.6-alpine` Testcontainers databases. No ambient/production database was inspected or migrated. No M2/M3, legacy backfill or real personal data. Idempotent SQL was generated and reviewed at `/tmp/lms-s3-migration.sql`; schema changes and function definitions do not execute a data backfill.

## Verification

Environment: .NET SDK 10.0.401, EF Core/Identity 10.0.12, Npgsql EF 10.0.3, PostgreSQL 17.6 Alpine in isolated Testcontainers.

| Final check | Result |
|---|---|
| Solution build | PASS, **0 warnings / 0 errors** |
| Domain tests | PASS **43/43** |
| Application tests | PASS **24/24** |
| Infrastructure tests | PASS **100/100** |
| API regression | PASS **186/186** |
| Total distinct backend cases | **353 passed, 0 failed, 0 skipped** |
| Existing S1/S2 tests, including backup/restore and constraints | PASS in full suite |
| Original B01 reproducer on S2 and S2→S3 upgrade | PASS |
| PostgreSQL concurrency, rollback and restricted-role tests | PASS |
| EF model/snapshot, empty migration, repeated upgrade | PASS |
| Idempotent SQL generation/review; staged diff whitespace | PASS |
| S1 migration / Domain / public API preservation | PASS: no changes |

Commands:

```bash
/tmp/lms-s1-dotnet/dotnet test Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/lms-s3-results/final
/tmp/lms-s1-dotnet/dotnet build Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false
PATH=/tmp/lms-s1-dotnet:$PATH DOTNET_ROOT=/tmp/lms-s1-dotnet /tmp/lms-s1-tools/dotnet-ef migrations script 20261009101331_SessionVerifierLookupS2 20261009153218_ScopedAuthorityS3 --idempotent --no-build --project src/Lms.Infrastructure --startup-project src/Lms.Infrastructure --output /tmp/lms-s3-migration.sql
```

Final TRX evidence: `/tmp/lms-s3-results/final/`. The persistent evidence is this summary plus executable tests. The final suite adds 8 application cases and 31 infrastructure cases to the S2 baseline, including the preserved B01 reproducer. Earlier development attempts found a raw SQL literal typo in the original reproducer, an EF raw-SQL analyzer warning in the snapshot helper, and insufficient privileges for the deferred consistency trigger. Each was corrected before the passing final runs. A prior 352-case full pass was followed by the final 353-case run after strengthening resource-generation matching. Failed/intermediate runs are not counted as final passes.

## Required acceptance matrix

| Requirement | Evidence / status |
|---|---|
| One Identity/Actor in A+B; independent memberships; no implicit courses | PASS: identity mapping, separate current authority, admin/no-grant denial |
| A membership revoke preserves B | PASS: current A denied, B authority remains |
| Course and group enrollment | PASS: explicit null course enrollment and named-group grants |
| Teacher grant/revoke/regrant; whole-course distinction | PASS: exact group checks, explicit null whole-course, no role/admin fallback |
| Foreign course/org/group and unknown registry | PASS: application denials plus real PostgreSQL composite FK rejection |
| Successful same-course move | PASS: generation 1→2, both current groups updated, history retained |
| Stale version and repeated request | PASS: conflict with unchanged row snapshots |
| Revoked/stale membership or revoked enrollment move | PASS: deny; explicit regrant required |
| move/move, move/revoke, move/regrant | PASS: independent connections, synchronized transactions, one version winner |
| Membership and teacher revoke/regrant races | PASS: one version winner; history preserved |
| Membership revoke versus move; assignment capture versus move | PASS: no effective stale membership, immutable original affiliation |
| Rollback on audit or commit failure | PASS: exact authority/access/history/audit snapshots unchanged |
| Direct SQL bypass / role enforcement | PASS: DML/DDL refused; only granted protocol function works |
| Student own/foreign work; teacher group isolation | PASS across assignment/work/submission/review and attached file |
| New resources cannot be acquired by old grant generation | PASS in policy and PostgreSQL-backed ownership scenario |
| Attachment pending/parent/expiry/quarantine/missing scope | PASS; uploader-only pending, parent authorization after claim |
| Legacy unknown affiliation and no implicit classification | PASS before/after new enrollment and repeated resolution |
| Immutable learning/history preservation | PASS: hashes of assignment/work/submission/review/message/event/file/affiliation tables across move; direct rewrites rejected |
| SyntheticOnly and organization suspension | PASS: existing S1 marker constraints and new fail-closed resolver tests |
| Regression and build | PASS: 353/353; 0 warnings/errors |

## Security / DDD review and limits

PASS within S3: identity, membership and educational scope remain distinct; authority state/history stay outside learning aggregates; ownership traversal is centralized; explicit org/course/group/owner matching rejects direct-ID substitution; no Teacher/Admin blanket branch; no learning history mutation or destructive cascade; atomic expected-version/generation transitions and audit; no client timestamp for revoke; immutable affiliation prevents historical work moving to a new group automatically.

The organization lock is deliberately conservative and serializes authority writers in one organization. It is not a per-request JWT freshness lock or a file-stream lock. Read results are snapshots, not perpetual authority. An issuer must use the same organization lock within the S2 security/session transaction before deciding new token scopes. No signing occurs here.

The administrative port is an internal system capability; ActorId in these mutation audit records identifies the affected actor. S5 must authenticate/authorize the administrative initiator and record that caller's provenance in its adapter transaction; these command fields must not be directly bound to public requests. A DBA/schema owner can alter functions/disable guards and is outside the restricted application-role threat boundary.

Existing legacy API/query/file adapters are unchanged and do not automatically adopt S3. These results do not certify organization-scoped HTTP admission, queue/count filtering, cookie/CSRF/browser coordination, stream/job limits or live JWT invalidation. The public-adapter cutover remains S4/S5. Current ProcessingRestrictions evaluation and bounded transfer enforcement remain S4/S7; synthetic attachment-context ownership is not a substitute for that processing gate. T04 issuance/expiry/completion bounds remain required and are not weakened or claimed as newly verified here.

No recovery/provisioning UI, production migration, real-data classification, push, merge or deploy. No new owner blocker remains within the authorized S3 scope.

## Changed files

- Application: `src/Lms.Application/ScopedAuthority.cs`.
- Infrastructure: `PostgresScopedAuthorityAdministration.cs`, `PostgresScopedAuthorityReader.cs`, `SchemaFoundations.cs`, `SchemaFoundationMappings.cs` under `src/Lms.Infrastructure/`.
- Migration: `20261009153218_ScopedAuthorityS3.cs`, its `.Designer.cs` and `LmsDbContextModelSnapshot.cs` under `src/Lms.Infrastructure/Migrations/`.
- Tests: `tests/Lms.Application.Tests/ScopePolicyTests.cs`; `SchemaFoundationTests.cs`, `ScopedAuthorityTests.cs`, `ScopedAuthoritySecurityTests.cs`, `ScopedAuthorityAdditionalTests.cs` under `tests/Lms.Infrastructure.Tests/`.
- Architecture: `docs/architecture/L1-S3-SCOPED-AUTHORITY.md`, `L1-SECURITY-SPEC.md`, `IDENTITY-ORGANIZATION-SCOPES.md`, `CROSS-ORGANIZATION-ISOLATION.md`; `docs/domain/LMS-DOMAIN-SPEC.md`; `docs/adr/ADR-L16-organization-jwt-browser-session.md`.
- Checkpoints: `codex/L03-OWNER-DECISION-REGISTER.md`, `L03-L1-IMPLEMENTATION-PLAN.md`, `CODEX-TASK-L1-EXECUTION.md`, and this `L1-S3-RESULTS.md`.

## Dependencies for S4 / stop

S4 requires separate owner acceptance of S3 and explicit authorization. It can consume current membership/concrete grant and trusted ownership contracts, including captured resource generations, without changing the learning Domain. It must integrate issuer/revoke serialization with S2, enforce signed scoped claims and processing gates on every public path, filter lists/counts before aggregation, and verify T04/O01 HTTP/browser timing semantics. Deployment role separation must be configured and verified before public ingress.

**STOP after S3. Do not begin S4 without separate owner acceptance and authorization.**
