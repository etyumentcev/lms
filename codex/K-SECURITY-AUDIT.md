# K — Security and integrity audit

Scope: base `47d6de3`, canonical repository, isolated PostgreSQL/Testcontainers and
`lms-k-smoke`. No real student data, production tables, external penetration testing,
or dependency vulnerability certification. Reproduction commands: `K-TEST-MATRIX.md`.
PASS below is limited to the named checks; deployment readiness is separate.

## Findings and disposition

| ID / severity | Exploit or failure / endpoint | Evidence | Disposition |
|---|---|---|---|
| K-S01 **P0** | Uploader downloads an attached original after enrollment/lesson/assignment or teacher grant is revoked; `GET /attachments/{id}/content` checked ownership before parent authorization | Four new `*rechecks*` API cases initially returned 200 instead of 404; `/tmp/lms-k-attachment-red.log` | **FIXED**. Pending files remain owner-only; attached Submission/Message files require current parent-work policy for every caller. All 27 attachment API cases pass in `attachment-green.log`; final full regression recorded in matrix. No original bytes or metadata modified. |
| K-S02 **P1**, externally exposed pilot/production | Any authenticated actor repeatedly uploads up to 10 MiB; no per-owner/global quota, request rate limit or automatic orphan retention. Can exhaust storage/CPU; `/uploads`, repeated work/message/review requests | `AttachmentService.Upload`, `Program.cs`, nginx config; bounded single uploads verified, bounded total consumption absent | **OPEN RELEASE BLOCKER** for exposed service. Before pilot: enforce gateway rate/concurrency limits, storage quota/alerts and approved pending/orphan cleanup; test exhaustion safely. No new scheduler/identity infrastructure added in K. |
| K-S03 **P2** | Student could open teacher screen for their own work, albeit read-only under C4; no foreign data or command access | Original J2 smoke and C4 own-work policy | **FIXED** route boundary using `GET /teacher/problem-works/{id}`. Backend checks Teacher role then existing scope; UI uses alias. General `/problem-works/{id}` remains authorized for owner. Two submitted/accepted regression cases + real browser 403. |
| K-S04 **P1 deployment gate** | Deploying public Compose signing key/demo identities would permit identity forgery | `docker-compose.yml`, `LocalDevelopment.cs` | **BLOCKED** for pilot/production: real IdP, UUID mapping, session/remount flow, HTTPS and private secrets not supplied or exercised. Compose is explicitly Development, loopback-bound. Production/Staging reject dev token in `LocalDevelopmentTests`; that does not prove deployed IdP integration. |
| K-S05 **P2 operational risk** | Crash between filesystem write and DB metadata can leave orphan; after tombstone before deletion can leave physical file | `AttachmentConsistencyTests`, U1 design | **OPEN**. Compensation only deletes when absent commit is confirmed; uncertain outcomes retain data. Approved grace-period reconciliation and retry/retention procedure needed, alongside S02. |
| K-S06 **P1 deployment gate** | DB-only restore loses photos; file-only restore loses associations/history | Two independent Compose volumes; no operational backup system or restore evidence | **NOT TESTED / BLOCKER**: coordinated DB+attachment backup, clean-host restoration, integrity hashes, RPO/RTO and operator ownership before real learners. Restart tests are not backups. |

## Authentication and authorization evidence

| Check | Result / evidence |
|---|---|
| Signed JWT, issuer, audience, expiry, not-before, valid actor claims | **PASS** `BearerAuthenticationTests.Real_bearer_handler_validates_token_before_actor_mapping`: real bearer handler, valid 201; bad signature/issuer/audience/time 401, invalid mapped subject/role 403. K adds unsigned `alg=none` case (401). Local key replaces discovery only in test. |
| Development login unavailable in production configuration | **PASS backend config boundary** `LocalDevelopmentTests.Local_signing_requires_explicit_development_and_preserves_access`, Production and Staging reject demo JWT even with Enabled=true. `--dev-token` guards Development + flag. Docker production target excludes local-login/auth files; actual production host **NOT TESTED**. |
| Tokens in app URLs/logs | **PASS source inspection of application-generated traffic**, ApiClient Authorization header, same-origin URLs, redirect:error; no app logging of bearer/header or `EnableSensitiveDataLogging`. Default server/proxy log configuration examined. Arbitrary client-supplied query secrets and external IdP/proxy log redaction **NOT TESTED**. No tokens stored in K artifacts. |
| Student A vs B work | **PASS** `Student_negative_access_blocks_assignment_work_and_locked_content`, `Shared_course_never_exposes_other_students_rows_or_other_teacher_groups`, `CommandApiTests` / `ContractApiTests`. Backend ownership/enrollment/availability mandatory. |
| Student queue/review commands/teacher screen | **PASS** existing command/queue contract cases, K `Teacher_screen_endpoint_requires_role_and_scope_including_terminal_state` (anonymous401/student403/foreign teacher403/authorized200) and `release-smoke.mjs`. |
| Teacher course/group scope | **PASS** `Teacher_work_and_queue_follow_C2_course_group_boundary`, `Inactive_group_membership_revokes_teacher_reads_and_queue`, `Submitted_content_honors_teacher_group_access`, K teacher-owned-message revocation. |
| Annotation reads/writes | **PASS** `Annotation_commands_are_authorized_atomic_and_readable_in_history`: foreign teacher/student POST403, wrong submission409, foreign attachment422, authorized persisted history. Reads are embedded in the authorized work/workspace DTO, no public annotation endpoint. |

Authorization is evaluated per request; revocation does not retract bytes already received.
Concurrent administrative revocation is not serialized with the full command transaction;
this inherited boundary needs an explicit operational policy for a future strict revocation requirement.

## Upload, annotation and API controls

- **PASS per-file protection**: JPEG/PNG only, configured 10 MiB, bounded multipart
  envelope, one form file, signatures/PNG CRC/size/JPEG structure and 60M-pixel area.
  `Invalid_uploads_leave_no_metadata_or_files`, multipart tests, real malformed upload E2E.
  Structural checking is not full JPEG decoding/malware certification. Compression CPU
  and disk consumption at concurrent load remain S02; filenames/extensions are not trusted paths.
- **PASS traversal/immutability/private storage**: `Storage_keys_reject_traversal_and_never_overwrite_an_original`,
  `Filename_is_metadata_only`, claim/delete races and original SHA-256 E2E.
  Random keys, dedicated non-web filesystem, bearer download with no-store/nosniff.
  Student now can also read an authorized teacher message attachment on their own work;
  a foreign student remains 404. No public static attachment directory.
- **PASS annotation bounds**: `AnnotationTests` validates normalized finite coordinates,
  pressure, tool=`pen`, color/width, unique page/stroke IDs, 20 pages/2000 strokes/
  4096 points per stroke/100000 total. Real Kestrel E2E confirms >4 MiB review body413.
  Ownership tied to the current submission; Domain defensive copy; DB trigger prevents UPDATE.
- **PASS error boundary**: `ErrorMappingContractTests` and `ContractApiTests.Error`
  enforce Problem Details without stack trace/detail or SQL values. Unexpected exceptions
  log server-side; public response exposes code and traceId only.
- **PASS same-origin development boundary**: no CORS allow-all policy configured;
  frontend forbids arbitrary-origin API base. Deployment origin/TLS/CSP/proxy policy is
  **NOT TESTED**. Auth is bearer, not an implicit cookie authorization scheme.
- **PASS legal transitions/duplicate guards**: workflow tests and browser terminal POST409,
  required revision feedback422, accept without feedback200; no client-side bypass of backend rules.

## Concurrency, transactions, persistence

**PASS**, real PostgreSQL tests (not in-memory mocks):

1. `Concurrent_first_submit_has_one_winner_and_no_loser_history_or_events`: one attempt,
   one work/event; losing save409. API attachment claim and delete/submit races also rerun.
2. `Concurrent_reviews_and_stale_review_cannot_overwrite_next_attempt`: one winning
   annotated Review; loser conflict; subsequent attempt/history/version remain consistent.
3. `Failure_after_all_writes_rolls_back_state_history_and_events` plus K
   `Commit_failure_rolls_back_and_retry_keeps_attempt_and_annotations`: first submit/review
   failure after writes or at commit entry, no partial messages/reviews/events; retry has
   the correct attempt number/version. Test barrier now synchronizes write transactions,
   excluding new RepeatableRead snapshots; races were not disabled.
4. `AttachmentConsistencyTests`: file failure before metadata, DB failure before/after
   metadata SaveChanges, uncertain commit compensation, claim rollback and delete retry.
   Review annotations are one JSONB column in the Review INSERT, so no independently
   committed annotation-write stage exists. Failure after INSERT/before commit covers both.
5. K `Split_history_load_keeps_one_snapshot_when_review_commits_between_queries`:
   concurrent committed review between collection reads cannot produce mixed history.
   Tested with caller-owned snapshot and store-owned snapshot.
6. `Legacy_review_survives_annotation_migration_with_empty_array`: predecessor schema
   upgrade keeps old Review with []; fixture migration/model-drift checks and clean Docker
   bootstrap pass. K adds **no migration** and does not rewrite historical rows.
7. `release-smoke.mjs`: restart, down/up, repeated migrate procedure preserve attempts,
   annotations and photo hashes. Disposable down-v/up proves clean reset only.

Process-kill/power-loss exactly during filesystem rename or lost DB commit acknowledgement
for Review is **NOT TESTED**; transactional rollback tests must not be represented as chaos
or durability certification. Browser uncertain responses stop commands until explicit refresh.
