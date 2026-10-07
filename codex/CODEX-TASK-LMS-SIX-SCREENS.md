# CODEX TASK — LMS: реализовать 6 основных экранов

**Status:** Ready for implementation  
**Scope:** one complete vertical slice  
**Project:** Sapientum LMS for olympiad mathematics

---

# 0. Repository preflight

Work inside the existing repository. Expected solution layout:

```text
src/Lms.Api
src/Lms.Application
src/Lms.Domain
src/Lms.Infrastructure
src/Lms.Web
```

These paths are expectations, not permission to create a parallel structure.

Before changing code:

1. Locate the actual solution/project files.
2. Confirm the real backend/frontend paths.
3. Locate existing tests.
4. Locate project-level instructions (`AGENTS.md`, `CLAUDE.md`, skills, README, coding conventions).
5. Locate reusable frontend components and design tokens.
6. Locate current Course/Lesson/Problem/Submission/Review code.
7. Locate build/test commands from the repository itself.

If actual structure differs, adapt to it. Do not duplicate projects.

---

# 1. Source of truth and precedence

Read these files before coding:

```text
docs/domain/LMS-DOMAIN-SPEC.md
docs/domain/LMS-MVP-ACTORS-USE-CASES.md
docs/content/COURSE-CONTENT-SPEC.md
docs/features/FEATURE-SUBMISSION-REVIEW.md
docs/ui/LMS-FRONTEND-CONTRACT.md
docs/ui/LMS-UI-FLOW-SPEC.md
docs/ui/mockups/lms-six-screens-right-panel.png
```

When documents disagree, use this precedence:

```text
1. LMS-DOMAIN-SPEC.md
2. FEATURE-SUBMISSION-REVIEW.md
3. LMS-FRONTEND-CONTRACT.md
4. LMS-UI-FLOW-SPEC.md
5. visual mockup
6. current implementation
```

Do not silently choose between conflicting contracts.

If a contradiction changes domain behavior, stop that part of implementation and record it as a blocker.

The visual mockup controls composition and visual hierarchy only. It must not override domain/API rules.

---

# 2. Required pre-implementation output

Before production code, create/update the project ExecPlan and record:

```text
A. Repository map
B. Existing use cases/endpoints relevant to the feature
C. Existing reusable UI components/design tokens
D. Contract gaps
E. Database/schema changes required? yes/no, with evidence
F. Build/test commands
G. Implementation checkpoints
```

For reusable UI, include a table:

```text
Need | Existing component | Decision: reuse/extend/create | Evidence/path
```

Then model the feature before coding:

```text
Actor
→ Use Case
→ Invariants
→ Aggregate
→ Entity / VO
→ Events
→ Evidence
→ State transitions
```

Do not invent a new aggregate for screen convenience.

---

# 3. Canonical domain workflow

`ProblemWork.Status` has exactly four MVP values:

```text
NotStarted
Submitted
NeedsRevision
Accepted
```

Allowed transitions:

```text
NotStarted    -> Submitted
Submitted     -> NeedsRevision
NeedsRevision -> Submitted
Submitted     -> Accepted
```

UI labels:

```text
NotStarted    -> Не начата
Submitted     -> На проверке
NeedsRevision -> Нужна доработка
Accepted      -> Принята
```

Never add domain/backend states:

```text
InProgress
Draft
Editing
Reviewing
```

A draft is technical UI/application state.

A `Submission`/attempt is created only by successful `SubmitSolution`.

Previous submissions are immutable.

---

# 4. Feature boundary

Implement the vertical flow:

```text
Course
→ Lesson
→ Student Problem / Solution
→ Submit attempt #1
→ Teacher Review Queue
→ Teacher Review
→ RequestRevision
→ Student sees feedback
→ Submit attempt #2
→ Teacher Accept
→ Student sees Accepted + immutable history
```

Do not expand the task into:
- calendar implementation;
- notification subsystem;
- rubric/scoring;
- AI grading;
- competency system;
- leaderboard;
- new server-side Draft aggregate;
- new design system;
- unrelated course authoring.

If a right-panel block depends on a subsystem that does not exist (for example upcoming events), render it only when data is already available from an existing read model. Do not build the subsystem as part of this task.

---

# 5. Existing feature API — inspect and reuse

Verify existing implementations before adding anything:

```http
POST /api/v1/uploads
POST /api/v1/problem-assignments/{assignmentId}/submissions
POST /api/v1/problem-works/{problemWorkId}/messages
POST /api/v1/problem-works/{problemWorkId}/reviews/needs-revision
POST /api/v1/problem-works/{problemWorkId}/reviews/accept

GET /api/v1/problem-works/{problemWorkId}
GET /api/v1/teacher/review-queue
```

Do not create duplicate commands/use cases if they already exist.

---

# 6. Required read-model gaps

If absent, implement these query/read-model endpoints exactly for screen composition:

```http
GET /api/v1/courses/{courseId}/workspace

GET /api/v1/courses/{courseId}/lessons/{lessonId}/workspace

GET /api/v1/problem-assignments/{assignmentId}/workspace
```

Enrich existing read models only as required by:

```text
docs/ui/LMS-FRONTEND-CONTRACT.md
```

These are query/read-model concerns.

Do not introduce a new aggregate or mutable progress entity.

---

# 7. Database/migration rule

Do not change persistence schema just because a screen needs a different DTO.

Prefer:
- projections;
- joins;
- query services;
- existing immutable content references.

A migration is allowed only if the approved domain/feature contract requires persisted data that the current schema objectively cannot represent.

If a migration is required, explain the evidence in the ExecPlan before adding it.

---

# 8. Global desktop layout

All six screens use:

```text
Left navigation | Main workspace | Right contextual panel
```

Use one reusable shell/context-panel implementation.

On mobile/tablet, context-panel sections move inline; they do not disappear semantically.

Do not duplicate main content in the right rail.

---

# 9. Screen 1 — Course

Route:

```text
/courses/{courseId}
```

Primary query:

```http
GET /api/v1/courses/{courseId}/workspace
```

Main:
- course title;
- teacher;
- course metadata;
- lesson list;
- availability;
- derived lesson progress.

Lesson action mapping:

```text
NotStarted  -> Открыть
InProgress  -> Продолжить
Completed   -> Посмотреть
Locked      -> disabled
```

`InProgress` is allowed only as derived lesson progress, never `ProblemWork.Status`.

Right panel:
- course progress;
- upcoming events if data already exists;
- teacher;
- course materials.

Do not implement a calendar subsystem for this panel.

---

# 10. Screen 2 — Lesson

Route:

```text
/courses/{courseId}/lessons/{lessonId}
```

Primary query:

```http
GET /api/v1/courses/{courseId}/lessons/{lessonId}/workspace
```

Main:
- lesson header;
- task list;
- materials tab/content;
- canonical status per task.

Task action mapping:

```text
NotStarted    -> Открыть
Submitted     -> Посмотреть
NeedsRevision -> Исправить
Accepted      -> Посмотреть
```

An unstarted task may have:

```text
ProblemWorkId = null
Status = NotStarted
AttemptCount = 0
```

Open task by `ProblemAssignmentId`.

Right panel:
- about lesson;
- progress;
- deadlines if already modeled;
- discussion summary if already modeled.

Do not build new deadline/discussion domains just for the panel.

---

# 11. Screen 3 — Student Problem / Solution

Route:

```text
/problem-assignments/{assignmentId}?tab=solution
```

Primary query:

```http
GET /api/v1/problem-assignments/{assignmentId}/workspace
```

The screen must work when no `ProblemWork` exists yet.

Problem statement is always visible above tabs.

Tabs:

```text
Решение
Подсказки
История
```

Do not create an `Условие` tab.

Do not create a separate submit route/page.

## Photo-first workflow

Visual priority:

```text
1. Add/capture solution photo
2. Attached photo gallery
3. Optional text solution
4. Optional student comment
5. Submit
```

Use:

```http
POST /api/v1/uploads
```

to get `AttachmentId`, then:

```http
POST /api/v1/problem-assignments/{assignmentId}/submissions
```

A submission is valid if:

```text
text OR attachment
```

## Status behavior

```text
NotStarted
  editor enabled
  domain status remains NotStarted while drafting

Submitted
  read-only
  label На проверке

NeedsRevision
  label remains Нужна доработка while drafting
  show latest actionable teacher feedback
  next Submission created only on submit

Accepted
  read-only
  label Принята
```

## Draft persistence

First reuse an existing draft/autosave mechanism if present.

If none exists, browser/local draft persistence is acceptable for MVP.

Do not add a server-side Draft aggregate/use case without explicit approval.

Right panel:
- current status;
- latest/current attempt summary;
- latest actionable teacher feedback;
- useful materials;
- link to History.

Never show the full attempt timeline here.

---

# 12. Screen 4 — Student Problem / History

Route:

```text
/problem-assignments/{assignmentId}?tab=history
```

For MVP use the same student workspace query unless the existing architecture already has a dedicated paginated history query.

Build history from authoritative:

```text
Submissions
Reviews
Messages
```

Newest attempts first.

Each attempt:
- attempt number;
- submitted at;
- immutable text;
- attachments;
- review decision;
- teacher feedback;
- related messages.

Messages do not create attempts and do not mutate status.

Right panel:
- current status;
- latest attempt;
- current files summary;
- useful materials;
- `Вернуться к решению`.

Do not repeat the full timeline.

---

# 13. Screen 5 — Teacher Review Queue

Route:

```text
/teacher/reviews
```

Primary query:

```http
GET /api/v1/teacher/review-queue
```

Queue membership:

```text
ProblemWork.Status == Submitted
```

Filters:

```text
Все
Первые попытки
После доработки
```

Derived mapping:

```text
first    = attemptNumber == 1
revision = attemptNumber > 1
```

Filtering that affects totals/pagination must be server-side.

Supported query shape should follow the frontend contract:

```text
courseId?
groupId?
studentId?
attemptKind=all|first|revision
search?
page
pageSize
```

Main table:
- student;
- course / lesson;
- task;
- attempt number;
- submitted time / waiting time;
- attempt kind;
- `Проверить`.

Right panel:
- same authoritative filters;
- queue summary;
- distribution by course/grade when available from read model.

Do not client-filter only the current page.

---

# 14. Screen 6 — Teacher Review

Route:

```text
/teacher/reviews/{problemWorkId}
```

Primary query:

```http
GET /api/v1/problem-works/{problemWorkId}
```

Main:
- immutable problem statement;
- student;
- current submission;
- text;
- photos;
- discussion/history access.

Right panel:
- previous attempt summaries;
- course/lesson/student/submitted context;
- feedback editor;
- optional local quick-note helpers;
- `Принять решение`;
- `Вернуть на доработку`.

Do not add rubric/scoring.

## Request revision

```http
POST /api/v1/problem-works/{problemWorkId}/reviews/needs-revision
```

Feedback required.

## Accept

```http
POST /api/v1/problem-works/{problemWorkId}/reviews/accept
```

Feedback optional.

Both commands send authoritative current:

```text
ExpectedVersion
```

## Concurrency

On `409 ConcurrencyConflict`:

1. never auto-retry;
2. disable stale actions;
3. show:
   `Работа уже изменилась или была проверена другим преподавателем.`;
4. refetch ProblemWork;
5. update status/version/actions.

---

# 15. Forbidden endpoints/workflows

Do not create:

```text
POST /problem-works/{id}/start
POST /attempts/create
POST /problem-work/set-in-progress
POST /lessons/{id}/complete
POST /courses/{id}/update-progress
```

Do not persist derived lesson/course progress.

---

# 16. Design system requirement

Use the existing Sapientum visual language and reusable library.

Visual reference:

```text
docs/ui/mockups/lms-six-screens-right-panel.png
```

Characteristics:
- white workspace;
- Sapientum green as primary accent;
- light gray borders/dividers;
- compact left navigation;
- large content heading;
- horizontal tabs;
- narrow contextual right rail;
- small radii;
- restrained badges;
- information-dense working interface;
- avoid decorative dashboard-card overload.

Before creating components, search for existing:
- shell/sidebar;
- buttons;
- tabs;
- badges;
- tables/lists;
- progress;
- panels;
- upload;
- markdown/math renderer.

Reuse/extend before create.

---

# 17. Shared components

Prefer common components such as:

```text
AppShell
SidebarNavigation
ContextPanel
ContextSection
Breadcrumbs
PageHeader
Tabs
StatusBadge
AvailabilityBadge
ProgressBar
ProblemStatement
MarkdownMath
AttachmentUploader
AttachmentGrid
AttachmentPreview
SubmissionView
AttemptBadge
AttemptTimeline
TeacherFeedbackBanner
MessageThread
MessageComposer
EmptyState
ErrorState
LoadingSkeleton
AsyncButton
InlineError
```

Do not create screen-specific duplicates for generic behavior.

---

# 18. Cache/query behavior

Follow the exact contracts in:

```text
docs/ui/LMS-FRONTEND-CONTRACT.md
```

Key principles:
- student problem cache key uses `problemAssignmentId`, not nullable `problemWorkId`;
- mutation invalidates affected projections;
- no two manually-maintained copies of status;
- server capabilities/authorization are authoritative.

---

# 19. Error/loading/empty states

Implement all states specified in the frontend contract.

Minimum messages:

```text
Добавьте фотографию или текст решения.

Не удалось загрузить файл. Попробуйте ещё раз.

Вы ещё не отправляли решение этой задачи.

Все работы проверены.

По выбранным фильтрам работ нет.

Укажите, что ученику нужно исправить.

Работа уже изменилась или была проверена другим преподавателем.
```

Already uploaded photos must not disappear because final submit failed.

---

# 20. Responsive

Student Solution must support the real mobile flow of photographing a paper solution.

Mobile priority:

```text
Problem statement
Current feedback/status
Photos
Capture/add photo
Optional text/comment
Submit
Context sections
```

Teacher review can be desktop-first but must remain usable on tablet.

Right-panel information must become inline content rather than disappear.

---

# 21. Accessibility

At minimum:
- status not color-only;
- keyboard-accessible upload;
- keyboard tabs;
- focus first validation error;
- pending state on async buttons;
- accessible attachment labels;
- meaningful disabled-state explanations.

---

# 22. Tests

Keep existing feature tests and add/maintain tests required by the feature contract.

Backend/application coverage must include:
- NotStarted assignment opens without ProblemWork;
- draft does not create Submission;
- attempt increments only on submit;
- immutable Submission;
- immutable problem content resolution;
- queue contains only Submitted;
- server-side attempt-kind filtering;
- revision feedback required;
- accept feedback optional;
- optimistic concurrency conflict.

Frontend tests must include:
- status -> correct mode;
- Submitted read-only;
- NeedsRevision feedback + editable new draft;
- Accepted read-only;
- empty submit disabled;
- history not duplicated in right panel;
- review action permissions;
- revision disabled without feedback.

Required E2E happy path:

```text
Student opens Course
→ Lesson
→ NotStarted Problem
→ uploads photo
→ submits #1
→ Teacher sees queue item
→ requests revision
→ Student sees feedback
→ submits #2
→ Teacher accepts
→ Student sees Accepted
→ History contains both immutable submissions and review decisions
```

---

# 23. Implementation checkpoints

Implement in this order:

```text
A. Preflight + ExecPlan
B. Read-model/API gaps
C. Shared shell + design tokens + ContextPanel
D. Course + Lesson screens
E. Student Problem Solution + upload/submit
F. Student History/messages
G. Teacher Review Queue
H. Teacher Review + concurrency
I. E2E flow
J. DDD review + cleanup
```

After each checkpoint run the narrowest relevant build/tests.

Do not postpone all testing until the end.

---

# 24. Definition of Done

Before finishing, provide:

```text
1. Files changed.
2. Repository paths actually used.
3. Existing components reused/extended.
4. New reusable components and why needed.
5. Read models/endpoints added or changed.
6. Domain/application changes and evidence.
7. Migrations, if any, and why unavoidable.
8. Tests added.
9. Build/test commands and results.
10. E2E result.
11. Remaining deviations from contracts.
12. DDD review findings.
```

The task is not complete merely because six pages render.

The complete vertical workflow must function end-to-end.
