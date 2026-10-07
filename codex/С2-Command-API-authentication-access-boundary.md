C1 и C1.5 принимаю.

Выполни только checkpoint C2 — Command API + authentication/access boundary.

Не переходи к C3/C4, read models, workspace queries или frontend.

Перед implementation:
1. Прочитай актуальные:
   - codex/EXECPLAN-LMS-SIX-SCREENS.md
   - docs/domain/LMS-DOMAIN-SPEC.md
   - docs/features/FEATURE-SUBMISSION-REVIEW.md
   - codex/C1-RESULTS.md
2. Проверь, что результаты C1/C1.5 остаются зелёными.
3. Domain state machine и persistence semantics не менять без зафиксированного blocker.

## 1. Scope C2

Реализовать:

- ASP.NET Core HTTP host;
- authentication boundary;
- mapping authenticated principal -> trusted Application Actor;
- authorization/access adapters;
- command endpoints:
  POST /api/v1/problem-assignments/{assignmentId}/submissions
  POST /api/v1/problem-works/{problemWorkId}/messages
  POST /api/v1/problem-works/{problemWorkId}/reviews/needs-revision
  POST /api/v1/problem-works/{problemWorkId}/reviews/accept
- единый Problem Details/error contract;
- HTTP mapping optimistic concurrency;
- API integration tests с реальным PostgreSQL.

Не реализовывать:
- CourseWorkspace;
- LessonWorkspace;
- StudentProblemWorkspace;
- ReviewQueue read model;
- GetProblemWork read model;
- frontend;
- progress projections;
- UI-specific DTO;
- right panels;
- cache invalidation.

## 2. Authentication boundary

Actor нельзя принимать из request body/query/header как произвольный ID.

Actor строится только из authenticated principal.

Минимально поддержать:
- Student;
- Teacher.

Authentication implementation должна быть заменяемой и не зависеть от конкретного будущего IdP.

Для integration tests допустим test authentication scheme.

Не помещать authentication concerns в Domain.

## 3. Authorization/access

Использовать существующие Application ports.

Student:
- submit только свой active ProblemAssignment;
- post message только в свой ProblemWork;
- attachment должен принадлежать текущему student.

Teacher:
- review/message только ProblemWork, к которому есть course/group access.

Добавить negative tests:
- чужой assignment;
- чужой ProblemWork;
- teacher без доступа;
- inactive assignment;
- unavailable lesson;
- attachment другого пользователя.

Backend authorization authoritative.

## 4. SubmitSolution endpoint

POST /api/v1/problem-assignments/{assignmentId}/submissions

Request:

{
  "bodyMarkdown": "...",
  "attachmentIds": ["..."],
  "expectedVersion": null | number
}

Rules:

First submission:
- ProblemWork может отсутствовать;
- expectedVersion == null.

Resubmit после NeedsRevision:
- expectedVersion обязателен;
- должен совпадать с текущей persisted version.

Не создавать:
POST /start
POST /attempts/create
POST /set-in-progress

Success response должен содержать минимум:
- problemWorkId;
- submissionId;
- attemptNumber;
- status;
- version.

## 5. RequestRevision endpoint

POST /api/v1/problem-works/{problemWorkId}/reviews/needs-revision

Request:
- submissionId;
- feedbackMarkdown;
- expectedVersion.

Feedback обязателен.

Правило уже является domain invariant после C1.5.
API/Application validation может возвращать более удобную ошибку, но не заменяет Domain check.

Transition:
Submitted -> NeedsRevision.

## 6. AcceptSubmission endpoint

POST /api/v1/problem-works/{problemWorkId}/reviews/accept

Request:
- submissionId;
- feedbackMarkdown? optional;
- expectedVersion.

Transition:
Submitted -> Accepted.

Accepted остаётся terminal.

## 7. PostMessage endpoint

POST /api/v1/problem-works/{problemWorkId}/messages

Request:
- bodyMarkdown;
- replyToMessageId?;
- attachmentIds[].

Message:
- не создаёт Submission;
- не увеличивает AttemptNumber;
- не меняет ProblemWork.Status.

Reply target должен принадлежать тому же ProblemWork.

## 8. Error contract

Использовать единый RFC Problem Details-compatible response с machine-readable `code`.

Минимальные error codes:

ValidationFailed
Forbidden
NotFound
ConcurrencyConflict
WorkAlreadySubmitted
WorkAlreadyAccepted
WorkNotSubmitted
SubmissionIsNotCurrent
SubmissionAlreadyReviewed
EmptySubmission
FeedbackRequired
AttachmentNotOwned
InvalidReplyTarget

Frontend в будущем должен принимать решения по `code`, не по human-readable message.

Не создавать разные error formats для разных endpoints.

## 9. HTTP status mapping

Рекомендуемое mapping:

400 / 422:
- validation;
- empty solution;
- FeedbackRequired.

403:
- authenticated actor не имеет доступа.

404:
- entity отсутствует либо не должна раскрываться согласно access policy.

409:
- stale ExpectedVersion;
- invalid current submission;
- already reviewed;
- invalid state transition, относящийся к concurrency/current workflow.

Если существующий Feature contract задаёт иное — следовать ему и зафиксировать решение в ExecPlan.

## 10. Optimistic concurrency

На stale expectedVersion:
- HTTP 409;
- code = ConcurrencyConflict;
- server НЕ retry command автоматически.

Сохранить PostgreSQL compare-and-save semantics C1.

HTTP layer не должен реализовывать собственную state machine.

## 11. Upload decision

Не проектировать полноценную object-storage subsystem на C2.

Если текущему SubmitSolution уже нужны реальные AttachmentIds, выбери один из двух путей:

A. если temporary upload contract можно реализовать минимально и чисто:
   POST /api/v1/uploads
   + ownership metadata
   + integration tests;

B. если это требует отдельного storage architecture:
   зафиксировать upload как deferred dependency и не расширять C2.

Не использовать fake "production" attachment IDs без явного test-only boundary.

## 12. API architecture

Controllers/endpoints должны быть thin.

HTTP layer отвечает за:
- parsing;
- authenticated Actor;
- request DTO;
- mapping результата;
- status/error response.

Application отвечает за orchestration/access/use case.

Domain отвечает за invariants/state transitions.

Не переносить business rules в controller.

## 13. Tests

API integration tests должны использовать:
- real ASP.NET Core TestServer/WebApplicationFactory;
- real PostgreSQL/Testcontainers;
- real Infrastructure adapters;
- authenticated test principals.

Минимум проверить:

1. Student submit #1 succeeds.
2. First submit with non-null invalid version rejected.
3. Resubmit after NeedsRevision with correct version succeeds.
4. Resubmit without required version rejected.
5. Stale version -> 409 ConcurrencyConflict.
6. Student cannot submit another student's assignment.
7. Inactive assignment rejected.
8. Unavailable lesson rejected.
9. Attachment ownership enforced.
10. Teacher without course/group access cannot review.
11. RequestRevision without feedback rejected.
12. RequestRevision with feedback succeeds.
13. Accept without feedback succeeds.
14. Review non-current submission rejected.
15. Duplicate/stale review rejected.
16. Message does not change status/attemptCount.
17. Invalid reply target rejected.
18. Existing Domain/Application/Infrastructure tests remain green.

## 14. G03

G03 считается закрытым.

Не возвращаться к optional revision feedback.

Canonical rule:
RequestRevision requires non-empty feedback globally.

## 15. Exit criteria C2

C2 завершён только если:

- ASP.NET Core API реально запускается;
- Actor строится из authenticated principal;
- authorization negative paths покрыты;
- все 4 command endpoints работают end-to-end через PostgreSQL;
- concurrency переводится в стабильный 409 contract;
- единый Problem Details contract используется всеми endpoints;
- Domain/Application/Infrastructure tests остаются зелёными;
- API integration tests проходят;
- full solution build = 0 warnings/errors.

## 16. Final report

После C2 выдать:

1. files changed;
2. routes;
3. request/response DTO;
4. authentication strategy;
5. Actor mapping;
6. authorization strategy;
7. Problem Details/error codes;
8. HTTP status mapping;
9. concurrency mapping;
10. upload decision;
11. API integration test scenarios;
12. build/test results;
13. remaining open decisions/deviations;
14. изменения ExecPlan.

После отчёта остановиться.

C3 не начинать.
