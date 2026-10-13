# MVP-HTTP-WORKSPACE-READ-01 — isolated content reads

2026-10-12. Authorization: current explicit owner decision **HTTP-COURSES-01 ISOLATED ACCEPTED; HTTP-WORKSPACE-READ-01 ISOLATED IMPLEMENTATION AUTHORIZED**. Submitted for owner acceptance only. Work is sequential in `/home/tea/github/lms`; existing dirty branch is preserved instead of switching to main.

## Contract and dependencies

Student / assigned Teacher → My Courses → Course → Lesson → Problem statement. Invariants: server-issued authority, S3 course ownership, same-org current processing, qualified time, original request deadline, immutable publication parent chain, published/available lessons, minimal content only. Entities/VO: TokenAuthority, ResourceReference(Course), ContentSelection, PublishedContentVersion, LessonAccess, ApplicationDeadline. No domain state transitions or route writes. S2 issuer/validator, S3 resolver/policy, ADR-L15/L16 delayed revocation, ADR-L18 conservative restrictions, CLOCK-01 and DEADLINE-01 are reused. No new permission, grant, policy, migration, clock or JWT validator.

Repository guardrails and reconciliation apply. Specialized model-domain / implement-use-case / implement-feature skills are unavailable; no execution of those skills is claimed. Production Program, legacy controllers/auth and frontend remain unchanged. No Submit/Review, files, cookies/refresh, B08, H04/Ready, production cutover or deployment.

## Pre-implementation inventory and field disposition

Legacy routes in `QueriesController` invoke `IWorkspaceQueries` / `PostgresWorkspaceQueries` using legacy Actor and access facts. They are **not registered** in the isolated host:

- `GET /api/v1/courses/{courseId}/workspace` → CourseWorkspaceDto.
- `GET /api/v1/courses/{courseId}/lessons/{lessonId}/workspace` → LessonWorkspaceDto.
- `GET /api/v1/problem-assignments/{assignmentId}/workspace` → StudentProblemWorkspaceDto.
- `GET /api/v1/problem-works/{problemWorkId}` and teacher alias → ProblemWorkDetailsDto.
- Teacher review queue is an additional aggregate read, excluded.

| Fields/category inspected | Source / immutable? | Subject and parent dependencies | Disposition / required check |
|---|---|---|---|
| Course `Id`, `Title`, `PublishedVersionId`, `Description` | Course registry + immutable PublishedContentVersions JSON | Course → org; content has no student/teacher profile | Safe values after S3 course + current actor/context checks; dedicated DTO, latest publication convention matches My Courses |
| Course `GradeLabel` | Optional existing DTO field, not supplied by resolver | No qualified source in current read model | Omit |
| Course `Teacher`; UserSummary `Id`, `DisplayName`, `AvatarUrl` | Profile/actor data, not immutable student-safe content | Teacher/student subject and organization | Omit; course scope alone is insufficient |
| Lesson `Id`, `CourseVersionId`, `Order`, `Title`, `Summary`; `ContentVersions`, `CourseVersionIds` | Immutable publication; list membership uses mutable LessonAccess | Exact course → publication version → lesson | Return only allowed published/available versions; omit locked versions entirely; verify exact parent before reading title/summary |
| Problem `Id`, `CourseVersionId`, `Order`, `Title`, `StatementMarkdown` | Immutable student-safe compiled publication | Exact course → version → lesson → problem | Safe projection after course/current processing + lesson availability; no assignment required for common content |
| Availability `IsAvailable`, `AvailableFrom`, `Reason` | Mutable AccessLessons; not immutable content | Course/version/lesson | Internal filter only; no locked title, schedule or reason disclosed; both roles require publication and availability |
| Enrollment `Id`, `StudentId`, `CourseId`, `GroupId`, `Status` | AccessEnrollments; personal mutable data | Student, org/course/group and generation | Omit; would require separate subject/ownership processing |
| TeacherContext `TeacherId`, `CanReviewCourse`, `GroupIds` | AccessTeachers/grants | Teacher and assignment/group | Omit; scope authority is not response content |
| Course/Lesson progress: status, completed/accepted, available/assigned/total, percent | Assignments + works aggregate | Every underlying student/assignment, group, context | Omit all fields, lists/counts; no legacy aggregate query |
| Assignment `Id`, `IsActive`; lesson rows `ProblemAssignmentId`, `StudentId` | ProblemAssignments, mutable status | Student + assignment sidecar/generations/course/group | Omit; content ID is not assignment ID |
| Work `ProblemWorkId`/`Id`, status, attempt count, version, current/latest submission/review/feedback | ProblemWorks + history | Assignment/student/group, affected subjects | Omit all; content grant cannot grant work access |
| Submission IDs, attempt/body/time/AttachmentIds | Immutable personal history | Work/assignment/student and files | Omit |
| Review IDs, submission/teacher IDs, decision/message/time/annotations | Immutable personal history | Work/submission/student/teacher and files | Omit |
| Message IDs, author/role/body/reply/time/attachments | Immutable personal history | Work/student/teacher and files | Omit |
| Permissions: submit/post/accept/revise/view-as-teacher | Derived mutable workflow/role | Assignment/work/actor | Omit; read DTO grants no write capability |
| Hints `Id`, order/title/body; Materials `Id`, title/kind/description/URL`; UsefulMaterials | Legacy empty placeholders; no qualified immutable provenance | Potential teacher-only source or attachment/URL parent | Omit, do not infer public availability |
| New `Href`, parent IDs, version/order/title link fields | Derived from verified immutable parent IDs; URL-escaped | Same authorized content tree | Safe navigation selectors, never authority |

No existing mixed DTO is returned. The accepted `PostgresContentResolver.Version` is reused only for immutable compiled student-safe publication after authorization; no teacher solutions belong in that artifact by its existing contract. Unknown/unqualified private source is not added. A runtime test removes SELECT access to assignment/work/submission/review/message/attachment tables and still reads all content levels.

## Routes and safe DTOs

| GET route | Response |
|---|---|
| Existing `/api/v1/courses` | Existing CourseNavigationDto, unchanged contract |
| `/api/v1/courses/{courseId}` | CourseContentDto: id, title, description, lessons |
| `/api/v1/courses/{courseId}/versions/{versionId}/lessons/{lessonId}` | LessonContentDto: courseId, versionId, id, order, title, summary, problems |
| `/api/v1/courses/{courseId}/versions/{versionId}/lessons/{lessonId}/problems/{problemId}` | ProblemContentDto: courseId, versionId, lessonId, id, order, title, statementMarkdown |

ContentLinkDto contains only id, versionId, order, title, href. My Courses supplies course.id; course GET returns ready lesson hrefs, lesson GET ready problem hrefs. Navigation tests follow those returned values without manually supplying IDs. Explicit versions preserve unambiguous publication identity; no mutable current-version shortcut. Lists have no counts/cursors/pagination or personal state. Empty allowed courses may have no lessons; unavailable direct resources uniformly return empty 403.

## Resource → parent → scope → processing

| Resource | Resolved parent | Scope | Current processing |
|---|---|---|---|
| Course | Verified CourseRegistry → Enabled SyntheticOnly org | Existing S3 Read, signed course grant matching org/actor/role | Current same-org binding/context; actor or context-wide restriction denies |
| Lesson link / lesson | AccessLessons exact course+version+lesson, then PublishedContentVersions exact course+version and nested lesson | Parent Course Read; published and available for both roles | Same course/actor gate before materializing content and before response; returned lesson availability rechecked |
| Problem link / statement | Exact immutable nested problem under verified lesson/version/course | Parent Course Read + lesson availability | Same course/actor checks, no student-derived subject introduced |
| Work/assignment/submission/review/file | Not exposed or read | No new grant | Not applicable; separate protected workflow required |

S3 has no Lesson/Problem ResourceKind. The adapter does not invent one or equate content to assignments: real S3 resolves the course root, and the persistence query verifies immutable child membership. Group-limited student/teacher grants permit **common course content** under accepted ScopePolicy; denying such course content as a wrong group would change that policy. Invalid group grants cannot be issued; client group/role/student query values cannot extend authority. Assignment/group-specific private data remains inaccessible. Revoking an ordinary grant blocks new issuance while an existing JWT retains its accepted lifetime; no immediate global revoke added.

## HTTP composition and failure behavior

Same disposable PostgreSQL TestServer and restricted request-scoped resource/processing/content connections as HTTP-COURSES-01. Real PostgresComponentTokenIssuer issues tokens; CourseBearerHandler uses ComponentJwt; CourseRequestIngress captures one ordinary 60-second deadline before authentication. Controlled HttpClock lives only in the test assembly and feeds real qualification/clock components. No production verifier bypass.

Concrete duplicate buffering/final-gate/error lifecycle was extracted into `ProtectedCoursesHttp.ExecuteBuffered`; courses retain their course-list response check, workspace has its own returned-parent/availability check. Workspace additionally checks the original deadline before content stages. Authorization occurs before immutable content materialization, with fresh processing per candidate; a private buffer is serialized then all returned resource gates are repeated before JWT trust/lifetime and ResponseEnqueue gates. Cancellation passes to DB and write calls. No partial successful list is returned after a failed final gate.

Invalid JWT: empty 401. Missing/foreign/mismatched/locked resource or processing denial: empty 403. Deadline/invalidation/cancellation or database failure: empty 503. `no-store` applies at ingress. Authenticated malformed version selectors also produce the same empty 403; no Guid route-constraint 404 existence distinction. Tests assert exact empty denial bodies and exact DTO property allowlists. No timing-side-channel resistance is claimed.

## Tests and evidence

Final commands/results are appended after execution. Test families:

- Both Student and group-assigned Teacher read actual PostgreSQL title/summary/statement by following returned navigation, with exact DTO allowlists and hidden lesson exclusion.
- Both roles: existing foreign org, same-org ungranted course, nonexistent course, forged version, malformed version, forged lesson, locked/scheduled lesson, problem from another lesson and swapped parent; same empty 403, no existence/count/body disclosure.
- Each of three routes, before read and after read: subject restriction, context-wide restriction, closed binding, suspended org, unknown clock, exact JWT expiry, exact original deadline. Independent positive request precedes mutation.
- Each route: missing processing binding and publication withdrawal between read and response deny.
- Invalid group grants refused; unassigned Student/Teacher cannot obtain token or read routes. Authorized content succeeds with private-table SELECT privileges removed.
- Both roles: delayed ordinary grant revocation permits existing tokens on each route, prevents issuance, then denies at expiry.
- Existing HTTP-COURSES suite also validates signature/issuer/audience/trust loss, cancellation, DB outage, unproven clock, split 30+30 deadline, pre/exact/post deadline, session revocation and no-scope roles.

## Guardrails §7 / self-review

| Check | Result |
|---|---|
| Actor/use case/invariants/states | covered — read-only contract above |
| Accepted S2/S3/PRS1 and gates | covered — reused; PRS1 exact OFF, production unchanged |
| Org/course/group/ownership, IDOR | covered — S3 root + exact immutable child chain; group content semantics above |
| Subjects/embedded sources | covered — only existing compiled student-safe content; no student/teacher profiles/history |
| Lists/counts/aggregates | covered — availability/authorization before metadata; no counts/aggregates; full late-denial buffer |
| Attachments/history | covered by exclusion and denied private-table SELECT test; no file route |
| Write transaction/locks/CAS/retry/unknown outcome | not applicable — no writes or retry in these routes; issuer/processing fixtures reuse existing protocols |
| Validation/limits/safe errors | covered — fixed JWT validator limits, server deadline, uniform empty error bodies |
| Unit/integration/negative/browser | recorded tests; browser E2E NOT RUN |
| Unapproved changes | no schema, production Program, frontend, legacy authorization changes |
| Secrets/real data/logging | synthetic-only fixtures, memory-only generated credentials, no token logging |
| Actual results / remaining gaps | see execution results and blockers |

## Remaining blockers and browser E2E conditions

No separate protected browser host exists in this slice; Browser E2E **NOT RUN**, not accepted. Frontend regression does not demonstrate integration with these new DTOs. Next browser work requires separately authorized protected host and browser auth/session flow, explicit frontend DTO adapter/navigation integration, qualified deployment clock or explicitly synthetic-only test qualification, origin/CSRF/cookie decisions where applicable, and browser negative-access/cache/expiry tests. Production cutover remains separately blocked.

CLOCK-DEPLOY-01 OPEN; G02 BLOCKED; H04/READY BLOCKED; PRS1 EXACT OFF. TestServer proves application-controlled gates, not external ingress/header queue, physical socket drain, proxy buffering, deployment clocks, DB cancellation latency, hard completion, or multi-node behavior. Last processing check and enqueue are not atomic with later restriction commits; accepted in-flight/delayed semantics remain. No immediate global revocation or physical delivery bound is claimed. No real-data/legal/pilot authorization.

## Changed files

New: `src/Lms.Application/WorkspaceContent.cs`, `src/Lms.Infrastructure/PostgresWorkspaceContentQueries.cs`, `src/Lms.Api/ProtectedWorkspaceHttp.cs`, `tests/Lms.Api.Tests/ProtectedWorkspaceHttpTests.cs`, this report and `codex/evidence/MVP-HTTP-WORKSPACE-READ-01/`.

Updated narrowly: `src/Lms.Api/ProtectedCoursesHttp.cs` (shared buffered lifecycle), `tests/Lms.Api.Tests/ProtectedCoursesHttpTests.cs` (same isolated host registers content query/routes, optional after-read hook). Existing checkpoint documentation is preserved.

No commit, merge, rebase, push, deploy, branch switch or new worktree. Stop at owner acceptance.

**HTTP-WORKSPACE-READ-01 ISOLATED IMPLEMENTED — OWNER ACCEPTANCE PENDING · CLOCK-DEPLOY-01 OPEN · G02 BLOCKED · H04/READY BLOCKED · PRS1 EXACT OFF · S4 HTTP CUTOVER NOT AUTHORIZED.**

## Execution results

```sh
/tmp/b07c2g/sdk/dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~Http_workspace|FullyQualifiedName~Http_courses' --logger trx --results-directory /tmp/workspace-read/targeted
/tmp/b07c2g/sdk/dotnet test Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/workspace-read/regression
/tmp/b07c2g/sdk/dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~Http_workspace' --logger trx --results-directory /tmp/workspace-read/final-workspace
PATH=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin:$PATH pnpm --dir src/Lms.Web test
PATH=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin:$PATH pnpm --dir src/Lms.Web build
```

- Full backend regression: **944/944 PASS**, failed 0, skipped 0. Domain 43; Application 340; Infrastructure 258; API 303, including existing HTTP courses and 51 workspace cases. [Log](evidence/MVP-HTTP-WORKSPACE-READ-01/regression.log).
- Final workspace run: **52/52 PASS**, failed 0, skipped 0. After the full regression API assembly was built, only tests were strengthened: missing-context positive controls and one independent-request-deadline case. All runtime code is identical between both successful runs. These runs overlap and must not be added together. [Log](evidence/MVP-HTTP-WORKSPACE-READ-01/final-workspace.log).
- Frontend regression: **387/387 PASS**, 13 files. TypeScript/Vite build **PASS**. [Test log](evidence/MVP-HTTP-WORKSPACE-READ-01/frontend-test.log), [build log](evidence/MVP-HTTP-WORKSPACE-READ-01/frontend-build.log).
- Initial targeted run: 77 passed, 3 failed due to fixture attempting to delete protected binding history (PostgreSQL 23514). Corrected to a distinct granted course without binding; no trigger, invariant or production check weakened. The final controls publish actual content in that unbound course, assert denial, open a binding with the existing processing capability, and assert success for the same resources. Initial failure evidence retained in [log](evidence/MVP-HTTP-WORKSPACE-READ-01/targeted.log).
- Changed-file whitespace: PASS. Browser E2E: **NOT RUN**; no protected browser host.
- TRX counters and frontend results: [test-summary.json](evidence/MVP-HTTP-WORKSPACE-READ-01/test-summary.json); full TRX files retained in corresponding evidence folders.

Preservation: **632/634 baseline files unchanged byte-for-byte**; the only two intentionally changed baseline files are the shared HTTP adapter and test-host file listed above. Baseline excludes `.obsidian/` and ignored build output. Production Program, existing frontend/domain/security components and checkpoint documentation are among the unchanged files. Branch `codex/l1-s4-jwt-api-authorization`, HEAD `492e30aef65bbddd24f107903bfaa54a563abe39` and index SHA-256 are unchanged. [Preservation evidence](evidence/MVP-HTTP-WORKSPACE-READ-01/preservation.json).
