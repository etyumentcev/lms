# L1/S1 — Schema Foundations (M1 only)

2026-10-09. **S1 READY FOR OWNER ACCEPTANCE**, within the owner's explicit M1-only authorization. Owner acceptance has not happened. S2 is not authorized or started.

Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY. LEGAL SOURCE NOT VERIFIED. SYNTHETIC DATA ONLY.

## Authorization, base and checklist

- Canonical repository `/home/tea/github/lms`; sequential work, no new worktrees or agents.
- Initial clean tree, branch `codex/l04-extended-decisions-l1-readiness`, exact HEAD `99c2c0600713ea17d0c96b0f38b2b4dae888f372` verified.
- Work branch `codex/l1-s1-schema-foundations`, created from that commit. The implementation/report commit is the HEAD containing this checkpoint (hash supplied in the final owner report).
- Read implementation plan, execution task, owner register, readiness, security specification, ADR-L01–L18, domain specification and existing persistence/migration/test code.
- Checklist completed: persistence foundations; Identity mappings; PostgreSQL constraints/guards; additive M1; isolated migration and constraint tests; backend regression; DDD/security review; diff review.
- The baseline plan's S1 includes M2/M3 rehearsal. The current owner request explicitly forbids M2/M3 and supersedes that execution scope. Neither is implemented or executed here. This is not a silent claim of full baseline M1→M2→M3 acceptance.
- The owner requires organization-bound ProcessingContext; the mandatory organization follows ADR-L18 and this request. No platform-purpose/null-org exception or legal approval workflow is introduced.

## Implementation

27 new tables: standard seven ASP.NET Core Identity tables plus 20 foundation records. Full keys, checks, indexes and trigger names are in [schema catalog](L1-S1-SCHEMA-CATALOG.md).

| Area | Tables |
|---|---|
| Credentials | AspNetUsers, AspNetRoles, AspNetUserClaims, AspNetRoleClaims, AspNetUserRoles, AspNetUserLogins, AspNetUserTokens |
| Stable identity and organization | Actors, ActorIdentities, Organizations, OrganizationMemberships |
| Course and educational scopes | CourseRegistry, CourseGroups, EnrollmentScopes, TeacherAssignmentScopes, AttachmentScopes |
| Processing context | ProcessingContextVersions, CourseContextBindings, ProcessingRestrictions |
| Provisioning/security foundations | ProvisioningRequests, AccountSecurityState, IdentitySessions, RefreshVerifiers, RecoveryRequests, AccessChallenges, ChallengeReceipts, SecurityAudit |

Infrastructure records follow existing AccessFacts conventions; `ActorRecord` avoids shadowing the existing Application `Actor` value. Identity remains in Infrastructure. No new learning aggregate or workflow is introduced. `ProblemWork`, commands, ports, API and UI are unchanged.

EF uses the standard `IdentityDbContext<IdentityUser<Guid>, IdentityRole<Guid>, Guid>` mappings and explicit RESTRICT relationships. Version fields are optimistic concurrency tokens. Membership, enrollment and teacher scopes have separate state/generation/deadline columns. Regrant does not update dependent MembershipGeneration automatically. Old generation/deadline changes are guarded; a new grant generation is required for explicit rebinding.

Database protection: 27 PKs, 3 alternate unique keys, 46 FKs, 29 CHECK constraints, 42 explicit indexes, 2 exclusion constraints and 27 new triggers. Standard Identity provider/user-role/token keys and normalized username uniqueness are preserved. Teacher active group/whole-course, live challenge and unconsumed refresh verifier uniqueness use partial indexes.

Nullable quarantine ownership uses a unique `(CourseId, OrganizationId)` index. PostgreSQL composite FKs for new sidecars/bindings are migration-owned because EF alternate keys would force a nullable quarantine column to NOT NULL. A deferred reverse user→ActorIdentity FK makes the 1:1 mapping mandatory at transaction commit; actor-only historical records remain possible. These SQL-owned features are catalogued and checked against real PostgreSQL, beyond EF's pending-model check.

## M1 and compatibility

Migration: `20261009094756_SchemaFoundationsM1`, following `20261008154119_ReviewAnnotations`.

- Creates new empty tables/indexes, `btree_gist` exclusion constraints and integrity guards. Requires a migration role able to create this PostgreSQL extension.
- No M1 data INSERT/backfill, no guessed course owner, credentials, memberships or grants. No rewrite of an applied migration, learning history, content or annotations.
- Only new sidecars/bindings point into the registry in M1. The five legacy→CourseRegistry FKs from the long-term schema plan require separately authorized inventory/backfill/validation. Installing validated FKs against an empty registry would reject existing legacy rows. Their omission here does not claim legacy organization isolation.
- Scope/legacy matching guards operate both ways. Inserting a scope takes a row-version write on its referenced access/attachment row without changing values, so concurrent READ COMMITTED and REPEATABLE READ writers cannot retain a stale resource association. M1 itself executes none of those data writes. Existing immutable-history triggers are not replaced or disabled.
- Existing legacy-only writes remain compatible while no sidecar exists. A record with a new sidecar cannot subsequently change its actor/course/group association through the legacy path. Attachment claims validate the actual parent course and uploader; cross-course claims are rejected even inside one organization.
- `Down` throws rather than deleting security/history foundations. Recovery is an explicitly reviewed forward fix or restore. An old annotation upgrade test now creates the prior schema directly, inserts the prior Review shape and upgrades, rather than downgrading through M1.
- `CourseContextBindings` use half-open `[from,to)` intervals, allow adjacent periods, reject overlaps concurrently and cannot start before the referenced context. Closed history cannot be rewritten; an open interval can be closed once. Context payloads and context organization are immutable across versions.
- Current restriction provenance survives new context versions. The table holds prohibitions, with context-wide lookup indexes; operation-time deny evaluation belongs to S4/S7 and is not implemented or claimed here.

All applications of M1 occurred in disposable `postgres:17.6-alpine` Testcontainers databases created by the tests. No ambient database configuration was used. No production, real or unknown dataset was inspected or migrated. A standalone idempotent SQL review artifact was generated at `/tmp/lms-s1-m1.sql`; the checked-in migration is authoritative.

## Verification evidence

Environment: Linux host, .NET SDK 10.0.401 (repository global.json), EF Core/Identity 10.0.12, Npgsql EF 10.0.3, Docker 29.8.1, PostgreSQL 17.6 Alpine. SDK restored into `/tmp/lms-s1-dotnet`; EF CLI 10.0.12 in `/tmp/lms-s1-tools` because the previous temporary SDK was absent.

| Check | Final result |
|---|---|
| Solution build | PASS, 0 warnings / 0 errors |
| Domain tests | PASS 43/43 |
| Application tests | PASS 16/16 |
| Infrastructure tests | PASS 32/32, including 17 S1 cases |
| API tests | PASS 186/186 |
| Final preservation/asset restore case after extra assertions | PASS 1/1 |
| Final security restore case after extra assertions | PASS 1/1 |
| Total distinct backend cases | 277, no skipped cases |
| EF model matches snapshot | PASS, checked on each newly created latest-schema DB |
| Idempotent migration SQL generation | PASS |
| Diff whitespace / applied migration preservation | PASS |

Commands (from canonical root; substitute a matching SDK path if needed):

```bash
/tmp/lms-s1-dotnet/dotnet test Lms.sln -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/lms-s1-results/full
/tmp/lms-s1-dotnet/dotnet test tests/Lms.Infrastructure.Tests --no-restore -m:1 -p:UseSharedCompilation=false --logger 'trx;LogFileName=infrastructure-final.trx' --results-directory /tmp/lms-s1-results
/tmp/lms-s1-dotnet/dotnet build Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false
```

The initial whole-solution run returned failure for the legacy annotation test's use of the new EF Review shape on the old schema. That test was corrected and the entire Infrastructure suite rerun successfully. Other suites passed in the initial run. Earlier development failures (Actor type-name collision, SQL test literal and invalid synthetic content fixture) were fixed before the final results; no failed run is represented as PASS. Final additions to asset/security restore assertions were checked with targeted reruns.

TRX files are under `/tmp/lms-s1-results/`: `infrastructure-final.trx`, `m1-preservation-final.trx`, `security-restore-final.trx`, and suite-specific files in `full/`. They contain synthetic test output; the persistent summary is this checkpoint and the tests themselves.

## Acceptance criteria for the authorized S1

| Criterion | Status / evidence |
|---|---|
| Empty PostgreSQL schema | PASS: all 27 tables empty, PKs present, no pending model changes |
| PK/FK/unique/check integrity | PASS: validated DB metadata, RESTRICT-only FKs and negative inserts/updates |
| Stable User↔Actor 1:1 | PASS: unique ActorId, mandatory deferred reverse FK, reassignment/delete denied |
| Multiple organizations per user | PASS: shared actor A/B memberships; no automatically created enrollment or teacher scope |
| Course owner and cross-org references | PASS for new schema: composite course/org, membership and context FKs; unknown registry cannot own active scopes. Legacy validation deferred to M2/M3 |
| Membership/enrollment/teacher separation | PASS: separate tables and explicit scopes, enrollment does not create teacher access |
| Regrant does not revive grants | PASS schema: old dependent generation remains unchanged; generation rollback/deadline extension rejected. Issuer checks are future S3/S4 |
| Historical data preserved | PASS: before/after SHA-256 of canonical rows for content, assignments, works, submissions, messages, reviews/annotations, events, attachment metadata; old mutation guards still reject writes |
| Immutable ProcessingContextVersions | PASS: UPDATE/DELETE rejected, organization cannot differ across versions |
| Correct CourseContextBinding intervals | PASS: ordering, adjacency, same-org and concurrent overlap tests; closed-history guard |
| Required SyntheticOnly | PASS: organization/context checks reject false marker and fake LegalApproved/basis; only generated test assets used. This is not a PII detector |
| Current restrictions retained over versions | PASS schema/provenance: new policy version does not remove current prohibition. Runtime processing gate intentionally outside S1 |
| Synthetic legacy M1 upgrade / no automatic backfill | PASS: unchanged history hashes, empty registry/Actors/Users/scopes after upgrade, repeat application produces no data |
| Backup/restore | PASS: pre-M1 dump restored and upgraded; expanded schema restored with guards; synthetic original/key bytes preserved; security state checked separately |
| Existing backend compatibility | PASS: latest results for all 277 cases; no skipped test |
| No applied migration rewrite / no destructive Down | PASS: only new migration/designer and snapshot; Down refuses destruction |
| No later-slice operations/API/UI | PASS: no runtime handlers/endpoints/UI changes; O01 unchanged |
| Original plan M2/M3 rehearsal | NOT AUTHORIZED / NOT RUN, explicitly excluded by this owner's request |

## DDD/security review and limitations

- PASS: credentials and security lifecycle remain outside learning aggregates; no additional LearningSpace, consent/legal-basis aggregates, or workflow states.
- PASS: mandatory stable identity mapping, independent membership/grants, organization-scoped new references, immutable policy/audit and retained history; no Identity/Organization cascade.
- PASS: challenges store a 32-byte verifier/MAC plus key version; refresh history stores verifiers only. Terminal challenge/session/verifier and recovery history cannot be revived. Independent approver CHECK is distinct from future capability/identity verification. No issuance/consume/approval handlers or secret generation are implemented.
- PASS: no backfill, runtime cutover, push, merge or deploy. No real personal data or fake legal approval was used.
- Runtime multi-organization authorization, missing-context/unknown-course deny, current restriction evaluation, JWT clocks, idle timeout enforcement, rate limiting, atomic audit with business/security actions and operator authority checks remain subsequent slices. Schema tests are not evidence that existing legacy endpoints implement those new policies.
- S1 guards intentionally keep existing scoped actor/course/group associations stable. Any future group reassignment protocol must preserve generation and matching invariants; no such business operation is exposed here.
- Restore evidence is a closed synthetic rehearsal of a coherent snapshot. It is not production RPO/RTO or post-backup revocation reconciliation evidence; ingress remains outside this work.
- O01 remains OPEN before S4. Legal/operations/real-data gates remain open. No new blocking architecture decision was required for the authorized M1 scope.

## Changed files

- `src/Lms.Infrastructure/Lms.Infrastructure.csproj` — Identity EF dependency.
- `src/Lms.Infrastructure/LmsDbContext.cs` — Identity context and foundation mapping registration.
- `src/Lms.Infrastructure/SchemaFoundations.cs` — 20 persistence records.
- `src/Lms.Infrastructure/SchemaFoundationMappings.cs` — EF keys, relationships, checks and indexes.
- `src/Lms.Infrastructure/Migrations/20261009094756_SchemaFoundationsM1.cs` — additive schema and SQL guards; protected Down.
- `src/Lms.Infrastructure/Migrations/20261009094756_SchemaFoundationsM1.Designer.cs` — generated target model.
- `src/Lms.Infrastructure/Migrations/LmsDbContextModelSnapshot.cs` — generated snapshot.
- `tests/Lms.Infrastructure.Tests/SchemaFoundationTests.cs` — 17 S1 constraint/migration/security cases.
- `tests/Lms.Infrastructure.Tests/PostgresFixture.cs` — target migration and isolated backup/restore helpers.
- `tests/Lms.Infrastructure.Tests/PersistenceTests.cs` — safe pre-Annotations upgrade regression.
- `codex/L1-S1-SCHEMA-CATALOG.md` — complete constraints/indexes/guards inventory.
- `codex/L1-S1-RESULTS.md` — this checkpoint, evidence and acceptance matrix.
- `codex/L03-L1-READINESS.md` — link to the new execution checkpoint while preserving the L0.4 assessment.

**STOP after this S1 report/commit. S2 requires separate owner acceptance and authorization.**
