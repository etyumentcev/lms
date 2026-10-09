# L1/S3 — Scoped authority and B01 enrollment moves

2026-10-09. **B01 ACCEPTED** by the explicit owner continuation. S1/S2/O01 ACCEPTED; S3 AUTHORIZED; S4–S8 NOT AUTHORIZED. Synthetic data only. Implementation evidence is in [S3 results](../../codex/L1-S3-RESULTS.md).

## Authority boundary

`IScopedAuthorityAdministration` is a trusted internal system capability, not a request DTO or an HTTP authorization adapter. It changes organization membership separately from enrollment and teacher assignments. A future S5 adapter must authenticate and authorize the administrative initiator in the selected organization before calling it; possession of a learning role is not that capability. Neither this port nor the reader is registered at public ingress in S3.

`IScopedAuthorityReader` obtains current membership and educational grants from server rows and checks ownership through one repeatable-read snapshot. This is a foundation for fresh authority decisions, not mandatory per-request DB grant freshness or a JWT implementation. Role/permission flags alone never grant course/work access. Shared Actor membership in A and B does not combine transcripts or scopes.

For S4 issuance: acquire locks in the existing S2 account/user/mapping/session order, then acquire the selected Organizations row `FOR UPDATE` before reading grants and deciding token scope in the same transaction. S3 writers serialize on that row. A detached reader result cannot be signed later as if it were fresh authority. Admission, signed-claim validation, current processing restrictions, bounded HTTP completion and browser rotation remain S4/S7.

## B01 protocol and persistence

The new forward migration leaves S1 unchanged. It replaces only enrollment-specific GroupId stability checks with generation-aware transitions and deferred final-state consistency. StudentId, CourseId and OrganizationId remain stable. Teacher, identity, attachment and other S1 stability guards remain intact.

`public.s3_change_authority` serializes mutations by organization, validates expected version and, for grants/moves, the active membership generation and educational role. It verifies the course's explicit organization and the destination group's composite course/group key. Move requires an active enrollment in the current membership generation and two different non-null groups of the same course. Repeated requests with the old version fail; they do not create another generation.

An active predecessor is first retired with a non-extensible deadline, then an explicit grant/move creates the next generation. EnrollmentScopes and AccessEnrollments are updated in one transaction, with append-only AuthorityHistory snapshots and SecurityAudit. Deferred triggers verify both GroupId and active-state agreement at commit. Version and generation guards, retained keys, FK/unique/check constraints and limited-role privileges supplement the application protocol. Any audit, statement, constraint or commit failure rolls back the transaction. Callers receive a successful result only after commit; an ambiguous lost commit acknowledgement requires rereading state, not blind replay.

Membership and teacher mutations also capture immutable authority history. Membership regrant does not update dependent grants: old MembershipGeneration stays stale until an explicit enrollment/teacher regrant. A repeated revoke with the current version returns the existing deadline without extending it or appending duplicate audit. Revocation time is read from PostgreSQL after taking the organization lock, never from a caller parameter. Revocation time plus two hours is the upper bound recorded for the retired authority. Existing JWTs and in-flight operations remain governed by T04; this code does not invalidate or issue JWTs.

## Resource affiliation and history

Course ownership comes from Verified CourseRegistry rows, group ownership from CourseGroups. New assignments created with valid scoped enrollment capture an immutable AssignmentScopes sidecar under the same organization lock. The sidecar records the server-derived course, organization, student, group and membership/enrollment generation. No migration populates old assignments; no resolver guesses a missing group or organization.

Work resolves through assignment; submission through work; review through its submission and matching work. AttachmentScopes must match the private stored upload and verified course/context. Pending attachments require the uploader's own current course authority and eligible unexpired metadata. Attached files require the parent's immutable affiliation, including for the uploader. Parent attachment arrays and uploader identity are checked. Missing sidecar/context, quarantine, deleted files, unknown registry and contradictory route hints deny.

A move never rewrites AssignmentScopes, ProblemAssignments, ProblemWorks, submissions, reviews, messages, attachments, Actor/AuthorRole or history. Old work remains in its original group; newly created assignments capture the new group. A currently enrolled student can read their own earlier course work. A group teacher sees work bound to that group, regardless of the student's later group move; only an explicit whole-course teacher grant spans all groups. No automatic transfer of old work to a new teacher occurs. Resource metadata retains the captured membership/enrollment generation: an older student grant cannot gain work created under a later generation, while a current regrant can cover the student’s own earlier work. S4 must still enforce signed token expiry/action constraints independently.

SyntheticOnly is a processing boundary, not a legal basis or PII detector. S3 resolves ownership and checks referenced synthetic attachment context; it does not claim S4/S7 processing restriction admission or file transfer enforcement.

## Required database privileges

The migration owner is a trusted deployment capability. Never use that owner or a superuser as the runtime principal. The migration revokes PUBLIC execution of the security-definer mutation function and capture function; it grants no web role a mutation capability.

For a separately configured internal authority operator role:

- grant CONNECT to the synthetic database and USAGE on public;
- grant EXECUTE on `public.s3_change_authority(text,text,uuid,uuid,bigint,bigint,text,text,uuid,text,integer)` only to the trusted administrative capability;
- grant only required SELECT on resource/authority tables to read adapters;
- do not grant INSERT/UPDATE/DELETE/TRUNCATE on authority, registry, access-fact, history, audit or affiliation tables to that role;
- do not grant schema CREATE, object ownership, role membership in the owner, SUPERUSER, CREATEROLE or trigger-disabling capabilities;
- remove pre-existing broader grants, including inherited ones, before any future public cutover. M3/public deployment is not authorized here.

The SQL function uses a fixed search path and schema-qualified persistent tables. Database role separation protects the protocol from ordinary direct SQL; a DBA able to alter schema/disable triggers remains outside this boundary. The PostgreSQL tests use a fresh restricted NOLOGIN role with SET ROLE to prove successful protocol execution and denial of direct DML/DDL. They do not modify production credentials or claim that an existing deployment is already hardened.
