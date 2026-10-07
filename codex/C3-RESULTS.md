# C3 — immutable content resolver + read models/workspace queries

2026-10-07. C3 завершён в `codex/c3`, созданной от принятого C2 merge commit `0b29654`.
Scope: только read side и immutable content resolution. C4 и frontend не начинались.

## 1. Files changed

- `src/Lms.Application/ReadModels.cs` — explicit read DTOs, query/resolver ports.
- `src/Lms.Infrastructure/PublishedContent.cs` — compiled snapshot format, exact-version resolver.
- `src/Lms.Infrastructure/PostgresWorkspaceQueries.cs` — four workspace/details projections, access, capabilities, derived progress/history.
- `src/Lms.Infrastructure/PostgresReviewQueue.cs` — unmapped SQL projection, all filters/search/pagination/summary.
- `src/Lms.Infrastructure/LmsDbContext.cs` — one content snapshot DbSet/mapping.
- `src/Lms.Infrastructure/Migrations/20261007103352_ImmutableContentSnapshots.cs` — additive snapshot table and immutable/validation triggers.
- `src/Lms.Infrastructure/Migrations/20261007103352_ImmutableContentSnapshots.Designer.cs` and `LmsDbContextModelSnapshot.cs` — generated EF metadata.
- `src/Lms.Api/QueriesController.cs` — five authenticated GET routes.
- `src/Lms.Api/Program.cs` — read services DI.
- `src/Lms.Api/ApiErrors.cs` — 503 ContentUnavailable for missing exact historical content.
- `tests/Lms.Api.Tests/ReadApiTests.cs` — 28 new API/PostgreSQL test cases.
- `docs/ui/LMS-FRONTEND-CONTRACT.md`, `docs/features/FEATURE-SUBMISSION-REVIEW.md` — minimal C3 read decisions, no authoritative hasDraft.
- `codex/EXECPLAN-LMS-SIX-SCREENS.md`, `README.md`, this report — decisions, operational boundary and verification.

No changes to Domain, command Application service/ports, PostgresWorkStore, PostgresAccessPolicy,
C1/C2 migrations or existing tests. No new packages, UI-specific tables or mutable progress.

## 2. Routes

All require the existing C2 authentication/principal mapping:

| GET /api/v1 route | Result |
|---|---|
| `/problem-assignments/{assignmentId}/workspace` | StudentProblemWorkspaceDto; student only, supports absent work |
| `/problem-works/{problemWorkId}` | ProblemWorkDetailsDto; authorized student or teacher |
| `/courses/{courseId}/workspace` | CourseWorkspaceDto; actual student enrollment / null for teacher |
| `/courses/{courseId}/lessons/{lessonId}/workspace` | LessonWorkspaceDto; assignment-keyed rows |
| `/teacher/review-queue` | ReviewQueueResponseDto; teacher only |

Success 200. C2 errors remain: 401 unauthenticated, 403 Forbidden, 404 missing assignment/work/lesson,
400 invalid query. Missing exact published content: 503 ContentUnavailable, no fallback.
Course without access returns 403 before revealing whether content exists.

## 3. DTOs/read models

Explicit Application records; no EF entities/aggregate internals serialized. Shared course/lesson/problem,
availability, permissions, enrollment, teacher context, work summary, submission/review/message/history,
progress and queue DTOs. Read queries use no-tracking and repeatable-read transactions, providing one
consistent DB snapshot per response (including queue items/counts/summary).

Student no-work response: nullable work ID/version/latest submission/latest review/feedback, NotStarted,
attemptCount 0; history.submissions/reviews/messages empty. GET creates no work, attempt or event.
Hints/usefulMaterials/materials are empty. G08: no server hasDraft, table, aggregate or endpoint.
G07: bodyMarkdown belongs to Submission with attachmentIds; no separate solution comment.

Teacher details explicitly identify currentSubmission as greatest AttemptNumber. Submissions/reviews
are newest attempt first; messages sort CreatedAt ascending, then ID. Review links preserve submissionId
and messageId. LatestTeacherFeedback follows the latest review's MessageId; it does not resurrect an
older revision feedback after an Accept without feedback.

## 4. Immutable content resolver strategy

C2 had no published content storage. Before implementation the gap was recorded in ExecPlan §15.
One `PublishedContentVersions` table stores complete immutable, compiled student-safe JSONB artifacts:
Id/CourseId/VersionNumber/SourceRevision/PublishedAt/ContentJson. Unique CourseId+VersionNumber;
positive version; database triggers reject UPDATE/DELETE, malformed core snapshot shape and duplicate
lesson/problem IDs. This stores content, not a UI projection; no authoring or publication API is added.

Trusted provisioning supplies a whole `PublishedCourseContent` snapshot with nested lessons/problems
using camelCase JSON. The operational compiler/importer remains outside C3. Teacher solutions must
never be included in this student-safe artifact. The typed artifact does not define a solution field.

Resolver reads CourseId + assignment.Content.CourseVersionId, then exact LessonId + ProblemId.
No latest/current fallback. Both student and teacher endpoints resolve through this port.
A newly provisioned version gets a new UUID; old assignments retain old statement. Tests publish a
second artifact in real PostgreSQL and verify old/new assignments, teacher details and lesson rows.

## 5. Progress formula

- Problem completed iff canonical status == Accepted.
- All active assignments are required in MVP; required/optional is not modeled.
- Lesson: Accepted / active assigned × 100, rounded to two decimals.
- Lesson NotStarted: all works absent/NotStarted, including zero assignments.
- Lesson Completed: nonempty assignment set and every assignment Accepted; otherwise InProgress.
- Withdrawn assignments do not enter lists/progress.
- Course: completed / available logical lessons × 100; zero denominator gives 0.
- Logical lesson identity is LessonId; multiple publications do not duplicate lesson counts.
- Student projections include available versions only. Teacher projections include all authorized
  active assignments, including unavailable versions; Course/Lesson teacher progress agree.

No progress persistence or new domain status. Teacher progress is course/group-scoped aggregate
progress, not a fabricated student enrollment.

## 6. Locked content policy

Student requires active enrollment. Assignment/work reads also require ownership, active assignment
and exact-version lesson published with elapsed AvailableFrom, reusing C2 access policy.
Unavailable direct ProblemWorkspace/work reads return 403; LessonWorkspace returns 403 if no version
is available. In a mixed-version lesson only available versions and their assignments are disclosed.
Course rows may expose safe title/order/version/availability; locked summary is null. Statements,
hints/materials and teacher-only content are never included in locked student responses.

## 7. Queue filtering/summary semantics

Source: Submitted works only; latest submission selected by MAX(AttemptNumber). Same teacher
course/group grant predicates as C2, applied in SQL before filters. Whole-course grants allow all
works in that course; group grants require matching active enrollment. No extra active-assignment
filter is invented for teacher review: C2 review policy permits historical withdrawn work.

Optional courseId, groupId, studentId; attemptKind all/first/revision (==1/>1); literal case-insensitive
substring search over existing course/lesson/problem titles and IDs, plus student UUID. Wildcards are
escaped. No profile names exist to search yet. Everything is parameterized and evaluated by PostgreSQL.
Page >=1, pageSize 1..200 (default 50), search <=200 characters; overflow rejected with 400.
Order is SubmittedAt ascending then work ID; Skip/Take executes in SQL.

summary.total/firstAttempts/revisions/averageWaitingSeconds/byCourse are calculated over exactly the
same filtered dataset, before pagination, in the same DB snapshot. Empty average is null; byCourse [].
Out-of-range page returns empty items but preserves full filtered summary. Grade section omitted
because no grade source exists. byCourse groups stable CourseId; title uses deterministic MIN when
historical titles differ. Missing content does not silently remove a Submitted work: queue metadata
uses left joins and nullable labels; opening the work fails closed if its exact artifact is absent.

## 8. Permissions strategy

Authenticated Actor comes only from C2 mapper. Access checked before returning any workspace content.
Server-derived capabilities reflect canonical status and current access:
student canSubmit only NotStarted/NeedsRevision; canPostMessage only with an existing authorized work;
teacher canAccept/canRequestRevision only Submitted; teacher canPostMessage with authorized work.
Commands remain authoritative and independently recheck access/state/version. No command semantics changed.
Teacher CourseWorkspace has enrollment=null and separate TeacherContext (teacher ID, grants).
Student enrollment projects the actual AccessEnrollments row, with nullable Id because its existing
identity is composite StudentId/CourseId; no synthetic enrollment UUID.

## 9. Authorization tests

401 on every GET; student forbidden from queue; foreign assignment/work forbidden; inactive enrollment;
withdrawn assignment; unpublished/scheduled lessons; locked metadata redaction; mixed-version locked
content exclusion. Teacher missing course grant/wrong group denied; matching group/whole-course allowed;
inactive group enrollment revokes work and queue access. Unauthorized work never contributes to queue
summary or teacher lesson rows. Existing JWT and command boundary tests remain unchanged and green.

## 10. New integration tests

28 cases in ReadApiTests, WebApplicationFactory + PostgreSQL 17.6/Testcontainers, fresh database per
case, all checked-in migrations, no pending EF model changes. Cover no-work/no writes, assignment
navigation, historical publication, database UPDATE/DELETE rejection, missing-version fail-closed,
student and teacher access cases, enrollment role behavior, derived lesson/course progress (including
50%, empty and withdrawn), current submission, immutable attempt ordering, review/message linkage,
capabilities across full flow, attachment ID roundtrip, Submitted-only queue, first/revision filters,
search and combined filters before pagination, full totals/summary, empty/out-of-range pages and
invalid query validation. Multi-version tests prevent publication double-counting and locked leakage.

## 11. Build/test results

Baseline before implementation: build 0 warnings/errors; Domain 24 + Application 15 + Infrastructure 10
+ API 50 = 99 passed. Final verification uses SDK 10.0.401:

```sh
/tmp/lms-dotnet/dotnet build Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false
/tmp/lms-dotnet/dotnet test Lms.sln --no-build --no-restore -m:1
git diff --check
```

Final: **0 warnings / 0 errors; Domain 24, Application 15, Infrastructure 10, API 78 = 127 passed,
0 failed, 0 skipped**. Includes all 99 existing C2 tests and 28 new C3 cases.
Transient logs: `/tmp/lms-c3-baseline-build.log`, `/tmp/lms-c3-baseline-tests.log`,
`/tmp/lms-c3-build.log`, `/tmp/lms-c3-tests.log`.
The environment's ordinary sandbox launcher fails before execution (bubblewrap mountinfo);
approved escalated execution used. EF tool required PATH/DOTNET_ROOT pointed at the same SDK.

## 12. Remaining decisions / deviations

- Publication/compiler/provisioning remains an integration dependency, as access facts already were in C2.
  C3 introduces the minimal immutable artifact source, not a Content authoring workflow.
- Enrollment has no CourseVersion binding. Course catalog uses provisioned AccessLessons; logical rows
  retain courseVersionIds. LessonWorkspace returns primary lesson plus contentVersions and per-assignment
  exact problem content. Primary lesson metadata uses earliest available version by VersionNumber;
  course metadata uses earliest provisioned lesson version (or earliest publication when no lesson exists).
  Mixed-version course PublishedVersionId=null, instead of pretending all assignments refer to latest content.
- No trusted profile/grade/designated-teacher/group-label source: names/teacher/grade nullable, grade facet
  omitted; student search supports UUID. Course teacher grants are context, not an invented instructor profile.
- Hints/materials/events not sourced: [] or absent optional section. No hints engine or calendar.
- Storage remains C2 dependency B: return exact attachmentIds, no fabricated filenames/download URLs.
- Markdown is raw source text in JSON; safe rendering/sanitization belongs to later renderer work.
- C3 exposes honest nullable/structured fields where draft frontend examples assumed richer models.
  No broad C4 cleanup, cache invalidation or frontend implementation performed.
- No C3 blockers remain; deploy migration and trusted artifact provisioning before using read endpoints.

## 13. ExecPlan changes

Added §15 pre-implementation evidence and decisions; updated current status, G08/G09/G16/summary/locked
policy, minimal content-source justification and mixed-version treatment; recorded final exit criteria
and test counts. Historical checkpoint sections remain historical. G03 and command semantics unchanged.

**Stop after C3. C4, UI shell and frontend not started.**
