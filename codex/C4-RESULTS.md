# C4 — backend contract/integration stabilization

2026-10-07. Только C4. База: принятый C3 merge commit `16ee5c1`.
Ветка `codex/c4-contract` создана от этой базы. Имя `codex/c4` уже занято
worktree 88c7 на C2 `d843b97`; чужая ветка/worktree не изменялись.

## 1. Files changed

- `tests/Lms.Api.Tests/ContractApiTests.cs` — 9-route authentication, command validation,
  missing resources, student/teacher authorization, read error envelope; общие HTTP helpers.
- `tests/Lms.Api.Tests/ContractWorkflowTests.cs` — единый HTTP/PostgreSQL workflow,
  точные JSON property sets всех 9 response contracts, версии, history/attachment immutability,
  message capabilities во всех состояниях, concurrent messages.
- `tests/Lms.Api.Tests/ContractQueryTests.cs` — filtered pagination на неоднородном dataset,
  progress при revision/withdrawal, teacher capability → command, publication A → B,
  student/group isolation внутри одного курса.
- `tests/Lms.Api.Tests/ErrorMappingContractTests.cs` — 21 middleware scenario для стабильных
  domain/application error mappings, включая defensive SubmissionAlreadyReviewed.
- `docs/ui/LMS-FRONTEND-CONTRACT.md` — transport errors, submit version, PostMessage exception,
  history linkage, приоритет принятого C3 wire contract, deferred upload.
- `docs/features/FEATURE-SUBMISSION-REVIEW.md` — C4 HTTP clarification, aliases/statuses,
  version/error precedence и ограничения.
- `codex/EXECPLAN-LMS-SIX-SCREENS.md` — pre-implementation discrepancies, актуальные gaps,
  C4 results/exit checklist.
- `codex/C4-RESULTS.md` — этот отчёт.

Production files, Domain/Application semantics, persistence/schema/migrations и все
существующие тесты оставлены без изменений. Новых packages, routes или aggregates нет.

## 2. Contract discrepancies found

Записаны в ExecPlan §16 до implementation. Source of truth: принятые C2/C3 decisions,
Domain/Feature invariants и C3 amendment к Frontend contract.

| ID | Расхождение | Решение |
|---|---|---|
| D01 / G06 | Frontend §3.6 называл `{code,message}` transport envelope; C2 отдаёт Problem Details | Документировать действующий HTTP envelope; adapter остаётся будущим UI concern |
| D02 | SubmitSolutionRequest в Frontend §8.8 не содержал expectedVersion | Добавить nullable/optional expectedVersion, обязательный актуальный token для resubmit |
| D03 | Frontend §4.2 требовал version от любого teacher command, включая PostMessage | Уточнить исключение Feature UC-A06/C2: внутренний compare-and-save, GET для новой версии |
| D04 / G11 | History VM подразумевал messages на попытку без domain связи | Только Review.SubmissionId/MessageId связывают feedback; остальные messages — work discussion |
| D05 | Старые богатые DTO sketches расходятся с принятым C3 read amendment | Явно обозначить приоритет C3/C4 wire boundary; зафиксировать JSON names/nulls тестами |

Подтверждённых дефектов production contract, domain state machine, command semantics или
persistence invariants не найдено. Различия с ранними sketches не оправдывают новые поля
или вымышленные profile/storage данные.

## 3. Discrepancies fixed

Все D01–D05 уточнены в contracts. Регрессии: точный error envelope на HTTP boundary,
существующие stale/null resubmit tests C2 плюс новый full flow из read version,
6 capability/message/version cases, feedback-to-review linkage и JSON shape assertions.
C4 не реализует frontend adapter, draft, cache или renderer.

SubmissionAlreadyReviewed сохраняется как 409 mapping. На валидном persisted workflow
повторная review ранее отвергается version/state guards: stale version → ConcurrencyConflict;
актуальная version после review → WorkNotSubmitted. Для defensive guard проверен реальный
middleware с DomainException и UseCaseException; это не PostgreSQL workflow test и не
искусственно повреждённая работа. Порядок domain guards не менялся.

## 4. Final routes

Все требуют authentication. Base `/api/v1`.

| Method / relative path | Success / result |
|---|---|
| POST `/problem-assignments/{assignmentId}/submissions` | 201 SubmissionResult |
| POST `/problem-works/{problemWorkId}/messages` | 200 MessageResult |
| POST `/problem-works/{problemWorkId}/reviews/needs-revision` | 200 ReviewResult |
| POST `/problem-works/{problemWorkId}/reviews/accept` | 200 ReviewResult |
| GET `/problem-works/{problemWorkId}` | 200 ProblemWorkDetailsDto |
| GET `/teacher/review-queue` | 200 ReviewQueueResponseDto |
| GET `/courses/{courseId}/workspace` | 200 CourseWorkspaceDto |
| GET `/courses/{courseId}/lessons/{lessonId}/workspace` | 200 LessonWorkspaceDto |
| GET `/problem-assignments/{assignmentId}/workspace` | 200 StudentProblemWorkspaceDto |

Upload, history-only и progress mutation routes отсутствуют.

## 5. Final request/response DTO

Идентификаторы entities — UUID strings; course/lesson/problem IDs — strings.
Version — JSON integer / Int64; status/decision — string enum; timestamps UTC ISO-8601.
Неизвестные JSON request properties отклоняются. Actor берётся только из principal.

| Command | Request | Response |
|---|---|---|
| Submit | bodyMarkdown?; required attachmentIds[]; expectedVersion? | problemWorkId, submissionId, attemptNumber, status, version |
| Message | required nonblank bodyMarkdown; replyToMessageId?; required attachmentIds[] | messageId, createdAt |
| NeedsRevision | required submissionId, expectedVersion; nonblank feedbackMarkdown | reviewId, messageId, status, version |
| Accept | required submissionId, expectedVersion; feedbackMarkdown? | reviewId, nullable messageId, status, version |

First submit: expectedVersion null/omitted; any non-null → 409. Existing work: current
read version required; stale/null → 409. Missing/null review version/submissionId → 400.
Message increments persisted version, returns no version per accepted UC-A06; subsequent
GET returns it. Concurrent message save can return 409; no automatic retry.

Read records — `src/Lms.Application/ReadModels.cs`; HTTP wire names asserted independently
of those record types in ContractWorkflowTests.

| Read DTO | Root fields |
|---|---|
| StudentProblemWorkspace | assignment, course, lesson, problem, availability, work, hints, history, usefulMaterials, permissions |
| ProblemWorkDetails | id, problemAssignmentId, status, version, attemptCount, student, course, lesson, problem, currentSubmission, submissions, reviews, messages, permissions |
| CourseWorkspace | course, enrollment, teacherContext, progress, lessons, materials |
| LessonWorkspace | course, lesson, contentVersions, availability, progress, problems, materials |
| ReviewQueueResponse | items, page, pageSize, total, summary |

Shared nested contracts:

- Course: id/title/publishedVersionId/description/gradeLabel/teacher; Lesson:
  id/courseVersionId/order/title/summary; Problem: id/courseVersionId/order/title/statementMarkdown.
- Assignment: id/isActive. Availability: isAvailable/availableFrom/reason.
- Work summary: nullable problemWorkId/version/latestSubmission/latestReview/latestTeacherFeedback,
  status, attemptCount. History: submissions/reviews/messages.
- Submission: id/attemptNumber/bodyMarkdown/submittedAt/attachmentIds.
- Review: id/submissionId/teacherId/decision/messageId/createdAt.
- Message: id/author/authorRole/bodyMarkdown/replyToMessageId/createdAt/attachmentIds.
  Author/user summary: id/displayName/avatarUrl; unsourced display data nullable.
- Permissions: canSubmit/canPostMessage/canAccept/canRequestRevision. Payload validation,
  attachment ownership and version remain necessary even when capability is true.
- Lesson progress: status/acceptedProblems/totalAssignedProblems/percent.
  Course progress: completedLessons/availableLessons/totalLessons/percent.
- Course lesson row: lesson/availability/progress/courseVersionIds. Lesson problem row:
  problemAssignmentId/studentId/problem/availability/work/permissions.
- Student enrollment: id(null for composite identity)/studentId/courseId/groupId/status.
  Teacher enrollment=null; teacherContext: teacherId/canReviewCourse/groupIds.
- Queue item: problemWorkId/submissionId/studentId/studentDisplayName/courseId/courseTitle/
  lessonId/lessonTitle/groupId/groupTitle/problemId/problemTitle/attemptNumber/submittedAt/
  waitingDurationSeconds/version. Summary: total/firstAttempts/revisions/averageWaitingSeconds/
  byCourse[{courseId,courseTitle,count}]. Empty average=null, empty byCourse=[].

Hints/materials/usefulMaterials remain []; no authoritative hasDraft, fake filenames/URLs,
unsourced grade facets or duplicated rightPanel. Missing exact content fails closed.

## 6. Final Problem Details/error codes

```json
{
  "type": "urn:lms:error:ConcurrencyConflict",
  "title": "ConcurrencyConflict",
  "status": 409,
  "instance": "/api/v1/problem-works/.../reviews/accept",
  "code": "ConcurrencyConflict",
  "traceId": "..."
}
```

Content-Type `application/problem+json`. Required code; clients never parse human text.
No SQL, stack trace or token details. Mapping unchanged from accepted C2/C3:

| HTTP | code |
|---|---|
| 400 | ValidationFailed (binding/query/required fields; invalid attachment ID set at access boundary) |
| 401 | Unauthenticated |
| 403 | Forbidden, AttachmentNotOwned |
| 404 | NotFound |
| 405 | MethodNotAllowed |
| 409 | ConcurrencyConflict, WorkAlreadySubmitted, WorkAlreadyAccepted, WorkNotSubmitted, SubmissionIsNotCurrent, SubmissionAlreadyReviewed |
| 415 | ValidationFailed |
| 422 | EmptySubmission, FeedbackRequired, InvalidReplyTarget; ValidationFailed for other domain input errors |
| 503 | ContentUnavailable |
| 500 | InternalError for unexpected failures |

Not-found aliases AssignmentNotFound/ProblemWorkNotFound → NotFound. Access aliases
AssignmentInactive/AssignmentDoesNotBelongToStudent/StudentNotAuthorized/TeacherNotAuthorized
→ Forbidden. No mechanical renaming in Domain/Application. Known workflow/input failures
are not 500. Defensive mapping coverage is distinguished from reachable HTTP cases in §3.

## 7. Authorization matrix

| Actor/resource | Read | Commands |
|---|---|---|
| Anonymous, any of 9 endpoints | 401 | 401 |
| Student, own active enrollment/assignment/available lesson | Course/Lesson/Assignment/Work allowed | Submit only NotStarted/NeedsRevision; message on existing work in any state |
| Student, another student's resource, including same enrolled course | Only own assignment rows/progress; foreign Assignment/Work 403 | Foreign submit/message 403 |
| Student, inactive enrollment | Course/Lesson/Assignment/Work 403 | Submit/message 403 |
| Student, withdrawn assignment | Excluded from list/progress; direct Assignment/Work 403 | Submit/message 403 |
| Student, unavailable/scheduled lesson | Safe course metadata; direct Lesson/Assignment/Work 403 | Submit/message 403 |
| Student on teacher APIs | Queue 403 | Both reviews 403 |
| Teacher, course grant or matching active group | Authorized Course/Lesson/Work/queue scope only | Both reviews on Submitted; message allowed |
| Teacher, absent grant/wrong group/inactive group membership | Work 403; queue/rows exclude work; absent course grant denies Course/Lesson | Both reviews/message 403 |
| Teacher on student-only APIs | Assignment workspace 403 | Submit 403 |
| Teacher, whole-course grant, withdrawn/unavailable student resource | Historical Work readable; withdrawn excluded from active lists/progress | Review/message remain permitted by accepted C2 access policy |

Teacher access policy is not tightened to student availability. Course-level metadata can be
read with a course/group grant even if no assignments match; rows and progress remain scoped.
Existing Bearer tests (signature/issuer/audience/lifetime/principal mapping) remain green.
Attachment ownership/eligibility/expiry checks and fake-ID denial remain green.

## 8. Full backend workflow test

`Complete_HTTP_workflow_preserves_versions_content_history_queue_and_progress`:
GET NotStarted (no work/submission/events in PostgreSQL) → POST submit #1/version 1 →
GET queue/current submission → POST revision/version 2 → GET NeedsRevision + linked feedback →
POST submit #2 using workspace version/version 3 → GET queue attempt 2 → POST accept/version 4 →
GET Accepted/history → queue empty, course/lesson complete.

Checks original body/UTC timestamp/ID/attachment IDs unchanged, ordered attempts 2/1,
linked revision and accept reviews, no stale old feedback after accept without feedback,
exact JSON property sets and server-derived capabilities. Fixture uses real PostgreSQL 17.6,
production adapters and migrations; HTTP uses WebApplicationFactory/TestServer. Ownership
records are test-only metadata, not file uploads.

## 9. Queue/progress/content tests

- Queue dataset has 7 works, pageSize=1, earlier search nonmatch, 2 first attempts,
  2 current revision attempts, Accepted and NeedsRevision exclusions. Both filtered pages,
  full totals/summary/course counts and latest submission checked. Existing C3 tests retain
  combined course/group/student/search filters, escaped wildcards, empty/out-of-range pages.
- Progress: Submitted and NeedsRevision each remain incomplete; one Accepted of two = 50%;
  withdrawing the Accepted assignment removes it from numerator and denominator without
  deleting history. Existing C3 coverage retains zero assignments/lessons, all Accepted,
  unavailable lessons, logical lessons across publications, teacher scope and 50% course case.
- Publication A is read before B exists; after B publication new assignment shows B,
  original assignment and teacher work still match complete original Problem DTO A.
  Existing database UPDATE/DELETE immutability and missing-content fail-closed tests retained.
- Same-course two-student dataset verifies own rows/enrollment/progress and foreign denial;
  teacher group A excludes group B from queue items/totals/summary and lesson rows.
- Capabilities: full flow executes canSubmit/canRequestRevision/canAccept; 6 message cases
  execute canPostMessage for both roles across Submitted/NeedsRevision/Accepted. Four
  teacher cases verify matching-group and historical-resource capabilities against commands.
- Existing first-submit/review transaction races unchanged; new message race proves one
  winner and 409 loser with one message/event and no status/attempt change.

## 10. Total test count

Baseline C3: 24 Domain + 15 Application + 10 Infrastructure + 78 API = **127 passed**.
Final C4: 24 Domain + 15 Application + 10 Infrastructure + 138 API = **187 passed**,
0 failed, 0 skipped. **60 added cases**: 39 HTTP/PostgreSQL cases + 21 middleware mappings.
All original 127 cases and their source files retained without weakening.

## 11. Build results

SDK 10.0.401, full solution build: **0 warnings, 0 errors**.

```sh
/tmp/lms-dotnet/dotnet build Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false
/tmp/lms-dotnet/dotnet test Lms.sln --no-build --no-restore -m:1
git diff --check
```

Baseline restore/build and all four suites ran before implementation. Final logs:
`/tmp/lms-c4-build.log`, `/tmp/lms-c4-tests.log`; baseline logs use `baseline-` prefix.
The environment's ordinary sandbox launcher fails with bubblewrap mountinfo error;
approved escalated execution was used. No application workaround or test suppression.

## 12. Remaining backend gaps

No C4 exit blocker. Existing deferred integration dependencies remain:

- Upload/storage/file validation/atomic claiming/cleanup and authenticated download metadata.
- Trusted content compiler/publication provisioning and access-fact provisioning.
- Deployment IdP configuration/internal UUID mapping; no production identity seed.
- Trusted profile/group labels/grade/designated teacher/material sources, if required by product.
- OpenAPI generation is not configured (no package/registration/document route): separate
  technical gap G18, not a new documentation subsystem in C4.
- Concurrent administrative revoke synchronization remains the existing C2 limitation;
  access is checked during the use case, no new transaction semantics introduced.

## 13. Frontend prerequisites

Before Student photo submit flow: real upload/claim/download integration and ownership source.
Before deployed end-to-end usage: migrations, trusted immutable snapshots/access facts, configured
IdP. Frontend must consume C4 wire names/nullability/errors, read versions and server capabilities;
profile/material enrichment needs sources if required. Markdown remains untrusted source text:
safe renderer is a later UI responsibility. No frontend, browser E2E, cache or D work started.

## 14. ExecPlan changes

Current state updated to C4 complete; §16 records discrepancies before code and their source
of truth; result/checklist appended. Gap table now reflects actual backend closure:
G06 Problem Details, G08 no server hasDraft, G09 derived formulas, G11 work discussion/review
linkage, G15 filtered dataset summary, G16 teacher enrollment=null. G18 OpenAPI tracked.
G03 remains CLOSED; G07 remains bodyMarkdown + attachments with no hidden PostMessage.
Historical B/C1/C2/C3 sections remain historical. **Stop after C4; D/frontend not started.**
