# LMS-FRONTEND-CONTRACT.md

**Status:** Draft for implementation  
**Version:** 1.0  
**Scope:** 6 основных экранов LMS  
**Aligned with:** `LMS-DOMAIN-SPEC.md`, `FEATURE-SUBMISSION-REVIEW.md`, `LMS-UI-FLOW-SPEC-v2.md`

---

# 1. Назначение

Этот документ является единым frontend contract для шести экранов MVP:

1. Курс — список уроков.
2. Урок — список задач.
3. Задача — вкладка `Решение`.
4. Задача — вкладка `История`.
5. Очередь проверки преподавателя.
6. Проверка конкретного решения преподавателем.

Для каждого экрана фиксируются:

- route;
- query;
- command;
- DTO;
- обязательные UI-компоненты;
- UI-state;
- loading / empty / error states;
- правила доступности действий;
- cache invalidation;
- переходы после command.

Frontend MUST использовать application/API contracts и MUST NOT восстанавливать доменные правила только по визуальному состоянию.

---

# 2. Канонический workflow

`ProblemWork.Status`:

```text
NotStarted
Submitted
NeedsRevision
Accepted
```

UI:

```text
NotStarted    -> Не начата
Submitted     -> На проверке
NeedsRevision -> Нужна доработка
Accepted      -> Принята
```

Разрешённые domain transitions:

```text
NotStarted    -> Submitted
Submitted     -> NeedsRevision
NeedsRevision -> Submitted
Submitted     -> Accepted
```

Черновик не является status.

---

# 3. Общие frontend-типы

## 3.1. ProblemWorkStatusDto

```ts
type ProblemWorkStatusDto =
  | "NotStarted"
  | "Submitted"
  | "NeedsRevision"
  | "Accepted";
```

## 3.2. LessonProgressStatusDto

Presentation/read-model status:

```ts
type LessonProgressStatusDto =
  | "NotStarted"
  | "InProgress"
  | "Completed";
```

`InProgress` здесь допустим, потому что это derived lesson progress, а не `ProblemWork.Status`.

## 3.3. AvailabilityDto

```ts
type AvailabilityDto = {
  isAvailable: boolean;
  availableFrom: string | null;
  reason: "Available" | "NotPublished" | "Scheduled" | "EnrollmentInactive" | null;
};
```

## 3.4. AttachmentDto

```ts
type AttachmentDto = {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  previewUrl: string | null;
  downloadUrl: string;
};
```

## 3.5. UserSummaryDto

```ts
type UserSummaryDto = {
  id: string;
  displayName: string;
  avatarUrl: string | null;
};
```

## 3.6. ApiErrorDto

Используется единый error envelope:

```ts
type ApiErrorDto = {
  code: string;
  message: string;
  fieldErrors?: Record<string, string[]>;
  traceId?: string;
};
```

Frontend MUST ветвиться по `code`, а не по тексту `message`.

---

# 4. Общие правила запросов

## 4.1. Query ownership

- Screen data читается только query endpoint-ами.
- Commands не должны использоваться как способ перечитать состояние.
- После успешного command frontend либо:
  - применяет response локально, если contract содержит достаточные данные;
  - либо invalidate/refetch соответствующий query.

## 4.2. Version token

Любой teacher command, изменяющий `ProblemWork`, MUST отправлять актуальный `version`.

```ts
expectedVersion: number
```

Frontend не генерирует version сам.

## 4.3. Date/time

API отдаёт UTC ISO-8601.

Frontend отображает локальное время пользователя.

## 4.4. Markdown

Поля:

```text
bodyMarkdown
feedbackMarkdown
```

рендерятся через единый безопасный Markdown/LaTeX renderer.

Raw HTML не доверяется.

---

# 5. Общий gap текущего feature

`FEATURE-SUBMISSION-REVIEW.md` уже определяет:

```http
POST /api/v1/problem-assignments/{assignmentId}/submissions
GET  /api/v1/problem-works/{problemWorkId}
GET  /api/v1/teacher/review-queue
POST /api/v1/problem-works/{problemWorkId}/reviews/needs-revision
POST /api/v1/problem-works/{problemWorkId}/reviews/accept
POST /api/v1/problem-works/{problemWorkId}/messages
POST /api/v1/uploads
```

Но для UI не хватает read endpoints:

1. Course workspace.
2. Lesson workspace.
3. Student problem workspace по `ProblemAssignmentId`, когда `ProblemWork` ещё не существует.

Эти endpoints являются **read model/API additions**, а не изменением domain model.

---

# 6. Screen 1 — Course

## 6.1. Route

```text
/courses/{courseId}
```

## 6.2. Query

### Новый endpoint

```http
GET /api/v1/courses/{courseId}/workspace
```

Actor определяется authenticated principal.

### Response

```ts
type CourseWorkspaceDto = {
  course: {
    id: string;
    title: string;
    description: string | null;
    gradeLabel: string | null;
    teacher: UserSummaryDto | null;
    publishedVersionId: string;
  };

  enrollment: {
    id: string | null; // C2 source has composite studentId/courseId identity.
    studentId: string;
    courseId: string;
    groupId: string | null;
    status: "Active" | "Inactive";
  } | null; // Real enrollment for Student; null for Teacher (G16).

  progress: {
    availableLessons: number;
    completedLessons: number;
    totalLessons: number;
    percent: number;
  };

  lessons: CourseLessonRowDto[];

  nextEvent?: {
    id: string;
    title: string;
    startsAt: string;
    kind: string;
  } | null;
};
```

```ts
type CourseLessonRowDto = {
  lessonId: string;
  order: number;
  title: string;
  summary: string | null;

  availability: AvailabilityDto;

  progress: {
    status: LessonProgressStatusDto;
    acceptedProblems: number;
    totalAssignedProblems: number;
  };
};
```

## 6.3. Components

```text
CourseHeader
CourseMeta
CourseProgressCard
CourseTabs
LessonList
LessonRow
LessonAvailabilityBadge
LessonProgressBadge
RightSidebar
NextEventCard?   // если календарь подключён
```

## 6.4. Rendering rules

### Lesson CTA

```text
!availability.isAvailable -> disabled
NotStarted                -> Открыть
InProgress                -> Продолжить
Completed                 -> Посмотреть
```

### Locked lesson

Показывать причину только из DTO:

```text
Откроется {availableFrom}
```

или:

```text
Недоступен
```

Frontend не вычисляет дату доступности самостоятельно.

## 6.5. Loading

Показывать skeleton:

- header;
- progress card;
- 5–6 lesson rows.

## 6.6. Empty

```text
В курсе пока нет уроков.
```

## 6.7. Error

- `403` → нет доступа к курсу.
- `404` → курс не найден.
- network → retry.

## 6.8. Commands

На этом экране нет domain commands MVP.

## 6.9. Navigation

```text
LessonRow click
-> /courses/{courseId}/lessons/{lessonId}
```

---

# 7. Screen 2 — Lesson

## 7.1. Route

```text
/courses/{courseId}/lessons/{lessonId}
```

## 7.2. Query

### Новый endpoint

```http
GET /api/v1/courses/{courseId}/lessons/{lessonId}/workspace
```

### Response

```ts
type LessonWorkspaceDto = {
  course: {
    id: string;
    title: string;
  };

  lesson: {
    id: string;
    order: number;
    title: string;
    summary: string | null;
    availability: AvailabilityDto;
  };

  progress: {
    acceptedProblems: number;
    totalAssignedProblems: number;
    percent: number;
  };

  problems: LessonProblemRowDto[];

  materials: LessonMaterialDto[];
};
```

```ts
type LessonProblemRowDto = {
  problemAssignmentId: string;

  problem: {
    id: string;
    order: number;
    title: string | null;
    shortStatement: string | null;
    difficultyLabel: string | null;
    estimatedMinutes: number | null;
    tags: string[];
  };

  availability: {
    isAvailable: boolean;
    reason: string | null;
  };

  work: {
    problemWorkId: string | null;
    status: ProblemWorkStatusDto;
    attemptCount: number;
  };
};
```

```ts
type LessonMaterialDto = {
  id: string;
  title: string;
  kind: "Document" | "Video" | "Link" | "Other";
  description: string | null;
  url: string;
};
```

## 7.3. Important contract rule

Для ещё не начатой задачи:

```text
problemWorkId = null
status = NotStarted
attemptCount = 0
```

Это допустимое состояние.

Frontend MUST открывать задачу по `problemAssignmentId`, а не требовать существующий `problemWorkId`.

## 7.4. Components

```text
LessonHeader
LessonTags
LessonTabs
ProblemList
ProblemRow
ProblemStatusBadge
ProblemPrimaryAction
LessonProgressSidebar
LessonMaterialsSidebar
```

## 7.5. CTA mapping

```text
NotStarted    -> Открыть
Submitted     -> Посмотреть
NeedsRevision -> Исправить
Accepted      -> Посмотреть
```

Если:

```text
status == NotStarted && localDraftExists
```

дополнительная подпись:

```text
Черновик сохранён
```

Но badge остаётся:

```text
Не начата
```

## 7.6. Loading / empty / error

Loading: skeleton problem rows.

Empty:

```text
В урок пока не добавлены задачи.
```

Unavailable lesson:

- C3 policy: Student LessonWorkspace/ProblemWorkspace returns `403 Forbidden`;
- course list may show only safe lesson title/order/version and availability metadata;
- no statement, materials, hints or teacher-only content is disclosed.

## 7.7. Navigation

```text
ProblemRow
-> /problem-assignments/{problemAssignmentId}
```

---

# 8. Screen 3 — Problem / Solution

## 8.1. Route

```text
/problem-assignments/{problemAssignmentId}
```

Optional tab:

```text
?tab=solution
```

## 8.2. Почему query должен быть по assignment

Текущий endpoint:

```http
GET /api/v1/problem-works/{problemWorkId}
```

не может открыть задачу в `NotStarted`, потому что `ProblemWork` MAY отсутствовать.

Поэтому нужен student workspace query.

## 8.3. Query

### Новый endpoint

```http
GET /api/v1/problem-assignments/{assignmentId}/workspace
```

### Response

```ts
type StudentProblemWorkspaceDto = {
  assignment: {
    id: string;
    isActive: boolean;
  };

  course: {
    id: string;
    title: string;
  };

  lesson: {
    id: string;
    order: number;
    title: string;
  };

  problem: {
    id: string;
    order: number;
    title: string | null;
    statementMarkdown: string;
    courseVersionId: string;
    tags: string[];
  };

  availability: AvailabilityDto;

  work: {
    problemWorkId: string | null;
    status: ProblemWorkStatusDto;
    attemptCount: number;
    version: number | null;
    latestSubmission: SubmissionDto | null;
    latestReview: ReviewDto | null;
    latestTeacherFeedback: MessageDto | null;
  };

  hints: ProblemHintDto[];

  permissions: {
    canSubmit: boolean;
    canPostMessage: boolean;
  };
};
```

```ts
type SubmissionDto = {
  id: string;
  attemptNumber: number;
  bodyMarkdown: string | null;
  submittedAt: string;
  attachments: AttachmentDto[];
};
```

```ts
type ReviewDto = {
  id: string;
  submissionId: string;
  decision: "Accepted" | "NeedsRevision";
  createdAt: string;
  messageId: string | null;
};
```

```ts
type MessageDto = {
  id: string;
  author: UserSummaryDto | null;
  authorRole: "Student" | "Teacher";
  bodyMarkdown: string;
  replyToMessageId: string | null;
  createdAt: string;
  editedAt: string | null;
  attachments: AttachmentDto[];
};
```

```ts
type ProblemHintDto = {
  id: string;
  order: number;
  title: string | null;
  bodyMarkdown: string;
};
```

## 8.4. Components

Persistent:

```text
ProblemBreadcrumbs
ProblemHeader
ProblemStatusBadge
ProblemStatement
ProblemTabs
```

Tab `Решение`:

```text
LatestTeacherFeedbackBanner
SolutionPhotoUploader
SolutionPhotoGrid
OptionalTextSolutionEditor
OptionalStudentCommentField
DraftSaveIndicator
SubmitSolutionButton
CurrentSubmissionReadOnly
CurrentAttemptSummary
```

Right panel:

```text
ProblemWorkSummary
CurrentAttemptSummary
AllAttemptsLink
```

Do NOT render full attempt history here.

## 8.5. UI mode

Frontend derives editor mode:

```ts
type SolutionWorkspaceMode =
  | "CreateFirstSubmission"
  | "WaitingForReview"
  | "CreateRevision"
  | "AcceptedReadOnly";
```

Mapping:

```text
NotStarted    -> CreateFirstSubmission
Submitted     -> WaitingForReview
NeedsRevision -> CreateRevision
Accepted      -> AcceptedReadOnly
```

This enum is frontend-only and MUST NOT be persisted as domain status.

## 8.6. Local draft state

```ts
type SolutionDraftState = {
  bodyMarkdown: string;
  attachmentIds: string[];
  attachments: TemporaryAttachmentDto[];
  savedAt: string | null;
  saveState: "Clean" | "Dirty" | "Saving" | "Saved" | "Error";
};
```

Draft identity SHOULD be:

```text
studentId + problemAssignmentId + nextAttemptNumber
```

MVP decision G08: draft is browser-local. Server DTOs MUST NOT expose authoritative `hasDraft`.
No draft persistence or draft endpoint is part of C3. `localDraftExists` is frontend-local state.

## 8.7. Upload command

Existing assumption:

```http
POST /api/v1/uploads
```

Input:

```text
multipart/form-data
file
```

Recommended response contract:

```ts
type UploadResponseDto = {
  attachmentId: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  previewUrl: string | null;
};
```

Upload does not change `ProblemWork.Status`.

## 8.8. Submit command

Existing endpoint:

```http
POST /api/v1/problem-assignments/{assignmentId}/submissions
```

Request:

```ts
type SubmitSolutionRequest = {
  bodyMarkdown: string | null;
  attachmentIds: string[];
};
```

Response:

```ts
type SubmitSolutionResponse = {
  problemWorkId: string;
  submissionId: string;
  attemptNumber: number;
  status: "Submitted";
  version: number;
};
```

## 8.9. Submit button enabled rule

```text
permissions.canSubmit
AND availability.isAvailable
AND (
  bodyMarkdown.trim() != ""
  OR attachmentIds.length > 0
)
AND !submitPending
```

## 8.10. Submit success

After `201`:

1. clear current local draft;
2. invalidate:
   - `StudentProblemWorkspace(assignmentId)`;
   - `LessonWorkspace(courseId, lessonId)`;
   - `CourseWorkspace(courseId)`;
3. refetch problem workspace;
4. render `На проверке`.

Do NOT optimistically render an invented full Submission object if API response does not contain it.

## 8.11. Submit errors

```text
422 EmptySubmission
-> inline: Добавьте фотографию или текст решения.

409 WorkAlreadySubmitted
-> refetch workspace.

409 WorkAlreadyAccepted
-> refetch workspace.

409 ConcurrencyConflict
-> refetch workspace.

403 / 404
-> access/not-found screen.
```

## 8.12. Hints tab

Uses hints already returned by workspace DTO.

Opening a hint does not mutate domain state in MVP.

If hint analytics are added later, they MUST be a separate use case.

---

# 9. Screen 4 — Problem / History

## 9.1. Route

Same workspace:

```text
/problem-assignments/{problemAssignmentId}?tab=history
```

## 9.2. Query choice

Two acceptable implementations:

### Option A — preferred for MVP

Use the same:

```http
GET /api/v1/problem-assignments/{assignmentId}/workspace
```

but response MUST include complete history.

This gives one screen model and simpler cache consistency.

### Option B — if history grows

Add:

```http
GET /api/v1/problem-assignments/{assignmentId}/history
```

with pagination.

For MVP use **Option A**.

## 9.3. Expanded workspace history

Student workspace DTO therefore SHOULD include:

```ts
history: {
  submissions: SubmissionDto[];
  reviews: ReviewDto[];
  messages: MessageDto[];
};
```

Alternatively, `submissions/reviews/messages` may live at DTO root.

## 9.4. History view model

Frontend may compose:

```ts
type AttemptHistoryItemVm = {
  attemptNumber: number;
  submission: SubmissionDto;
  review: ReviewDto | null;
  feedback: MessageDto | null;
  messages: MessageDto[];
};
```

This is derived frontend data, not API/domain entity.

## 9.5. Components

```text
ProblemHeader
ProblemStatement
ProblemTabs
AttemptTimeline
AttemptCard
SubmissionAttachments
ReviewDecisionBadge
TeacherFeedback
DiscussionMessage
ReplyComposer?   // только если messages UI включён в MVP
RightSidebarCurrentState
BackToSolutionLink
```

## 9.6. Post message command

Existing endpoint:

```http
POST /api/v1/problem-works/{problemWorkId}/messages
```

Request:

```ts
type PostMessageRequest = {
  bodyMarkdown: string;
  replyToMessageId: string | null;
  attachmentIds: string[];
};
```

Response:

```ts
type PostMessageResponse = {
  messageId: string;
  createdAt: string;
};
```

Available only when:

```text
problemWorkId != null
AND permissions.canPostMessage
```

## 9.7. Post message success

Invalidate:

```text
StudentProblemWorkspace(assignmentId)
TeacherProblemWork(problemWorkId), if cached
```

Message MUST NOT modify problem status.

## 9.8. Empty

```text
Вы ещё не отправляли решение этой задачи.
```

## 9.9. Right panel rule

Do not render attempt list again.

Allowed:

```text
Текущий статус
Последняя попытка
Вернуться к решению
```

---

# 10. Screen 5 — Teacher Review Queue

## 10.1. Route

```text
/teacher/reviews
```

## 10.2. Query

Existing endpoint:

```http
GET /api/v1/teacher/review-queue
```

Query params:

```text
courseId?
groupId?
studentId?
page
pageSize
```

## 10.3. Current feature DTO

Current contract:

```ts
type ReviewQueueResponseDto = {
  items: ReviewQueueItemDto[];
  page: number;
  pageSize: number;
  total: number;
};
```

```ts
type ReviewQueueItemDto = {
  problemWorkId: string;
  submissionId: string;
  studentId: string;
  studentDisplayName: string;
  courseId: string;
  lessonId: string;
  problemId: string;
  problemTitle: string | null;
  attemptNumber: number;
  submittedAt: string;
  version: number;
};
```

## 10.4. Required DTO additions for current design

The mockup needs readable course/lesson labels and waiting duration.

Recommended additions:

```ts
courseTitle: string;
lessonTitle: string;
groupId: string | null;
groupTitle: string | null;
waitingDurationSeconds: number;
```

This remains a projection/read-model change only.

## 10.5. Derived filters

UI tabs:

```text
Все
Первые попытки
После доработки
```

Definition:

```text
Первые попытки  = attemptNumber == 1
После доработки = attemptNumber > 1
```

Best implementation:

Add query param:

```text
attemptKind=all|first|revision
```

because filtering only the current page client-side gives incorrect totals.

Recommended endpoint:

```http
GET /api/v1/teacher/review-queue?attemptKind=revision&page=1&pageSize=50
```

## 10.6. Components

```text
ReviewQueueHeader
ReviewQueueFilters
ReviewQueueSearch
ReviewQueueTable
ReviewQueueRow
WaitingTimeCell
AttemptKindBadge
ReviewButton
ReviewSummarySidebar
CourseQueueBreakdown
```

## 10.7. Search

Current backend query supports IDs but not free-text search.

The design contains search by student/problem/course.

Either:

1. remove free-text search from MVP; or
2. add read-model query:

```text
search?
```

Recommendation: add `search?` to queue projection, because it is natural for teacher workflow.

Search MUST operate server-side.

## 10.8. Loading

Table skeleton preserving column widths.

## 10.9. Empty

No filters:

```text
Все работы проверены.
```

With filters:

```text
По выбранным фильтрам работ нет.
```

## 10.10. Navigation

```text
Проверить
-> /teacher/reviews/{problemWorkId}
```

Optionally include `submissionId` in query for diagnostics, but current submission must still be resolved from authoritative ProblemWork.

---

# 11. Screen 6 — Teacher Review Workspace

## 11.1. Route

```text
/teacher/reviews/{problemWorkId}
```

## 11.2. Query

Existing endpoint:

```http
GET /api/v1/problem-works/{problemWorkId}
```

## 11.3. Existing response

Feature currently provides:

```text
id
status
attemptCount
version
problem
submissions[]
reviews[]
messages[]
```

## 11.4. Required response expansion

The current UI also needs:

- student display data;
- immutable full problem statement;
- attachment metadata;
- current submission explicitly identified;
- course/lesson titles.

Recommended normalized contract:

```ts
type TeacherProblemWorkDto = {
  id: string;
  problemAssignmentId: string;
  status: ProblemWorkStatusDto;
  attemptCount: number;
  version: number;

  student: UserSummaryDto;

  course: {
    id: string;
    title: string;
  };

  lesson: {
    id: string;
    order: number;
    title: string;
  };

  problem: {
    id: string;
    order: number;
    title: string | null;
    statementMarkdown: string;
    courseVersionId: string;
  };

  currentSubmission: SubmissionDto | null;
  submissions: SubmissionDto[];
  reviews: ReviewDto[];
  messages: MessageDto[];

  permissions: {
    canAccept: boolean;
    canRequestRevision: boolean;
    canPostMessage: boolean;
  };
};
```

Backend MUST resolve statement from immutable content reference.

Frontend MUST NOT request "latest problem.md" independently.

## 11.5. Components

Main column:

```text
ReviewBreadcrumbs
ProblemStatement
StudentIdentity
CurrentSubmissionCard
SubmissionText
SubmissionPhotoGallery
PreviousAttemptsLink/List
DiscussionThread
```

Right panel:

```text
TeacherFeedbackEditor
AcceptButton
RequestRevisionButton
ConcurrencyState
PreviousAttemptSummary?  // compact only
QuickNoteTemplates?      // only local UI helper, not domain
```

No grading rubric in MVP unless a real rubric domain/read model is added.

## 11.6. Accept command

Existing endpoint:

```http
POST /api/v1/problem-works/{problemWorkId}/reviews/accept
```

Request:

```ts
type AcceptSubmissionRequest = {
  submissionId: string;
  feedbackMarkdown: string | null;
  expectedVersion: number;
};
```

Response:

```ts
type AcceptSubmissionResponse = {
  reviewId: string;
  messageId: string | null;
  status: "Accepted";
  version: number;
};
```

Enable when:

```text
status == Submitted
AND permissions.canAccept
AND currentSubmission != null
AND !commandPending
```

Feedback optional.

## 11.7. Request revision command

Existing endpoint:

```http
POST /api/v1/problem-works/{problemWorkId}/reviews/needs-revision
```

Request:

```ts
type RequestRevisionRequest = {
  submissionId: string;
  feedbackMarkdown: string;
  expectedVersion: number;
};
```

Response:

```ts
type RequestRevisionResponse = {
  reviewId: string;
  messageId: string;
  status: "NeedsRevision";
  version: number;
};
```

Enable when:

```text
status == Submitted
AND permissions.canRequestRevision
AND feedbackMarkdown.trim() != ""
AND currentSubmission != null
AND !commandPending
```

## 11.8. Command success

After either command:

Invalidate:

```text
TeacherProblemWork(problemWorkId)
TeacherReviewQueue(all active variants)
StudentProblemWorkspace(problemAssignmentId), if shared cache layer exists
LessonWorkspace(courseId, lessonId)
CourseWorkspace(courseId)
```

Then either:

### Preferred teacher flow

Navigate to next queue item:

```text
/teacher/reviews
```

and remove processed item.

or provide:

```text
Проверено. Следующая работа →
```

Do not leave stale actionable buttons on the reviewed submission.

## 11.9. Concurrency conflict

On:

```text
409 ConcurrencyConflict
```

UI MUST:

1. disable both review commands;
2. show:
   `Работа уже изменилась или была проверена другим преподавателем.`;
3. refetch `GET /problem-works/{id}`;
4. never auto-repeat command.

## 11.10. Stale current submission

Also handle:

```text
409 SubmissionAlreadyReviewed
409 SubmissionIsNotCurrent
409 WorkNotSubmitted
```

All three:

```text
-> refetch ProblemWork
-> show non-destructive stale-data message
```

---

# 12. Screen-to-query matrix

| Screen | Route | Query |
|---|---|---|
| 1. Курс | `/courses/{courseId}` | `GET /courses/{courseId}/workspace` |
| 2. Урок | `/courses/{courseId}/lessons/{lessonId}` | `GET /courses/{courseId}/lessons/{lessonId}/workspace` |
| 3. Решение | `/problem-assignments/{assignmentId}` | `GET /problem-assignments/{assignmentId}/workspace` |
| 4. История | same + `?tab=history` | same workspace query |
| 5. Очередь | `/teacher/reviews` | `GET /teacher/review-queue` |
| 6. Проверка | `/teacher/reviews/{problemWorkId}` | `GET /problem-works/{problemWorkId}` |

All API paths above use `/api/v1`.

---

# 13. Command matrix

| Action | Command/API | Screens |
|---|---|---|
| Upload photo | `POST /uploads` | 3, 4 optional, 6 optional |
| Submit solution | `POST /problem-assignments/{id}/submissions` | 3 |
| Post message | `POST /problem-works/{id}/messages` | 4, 6 |
| Request revision | `POST /problem-works/{id}/reviews/needs-revision` | 6 |
| Accept submission | `POST /problem-works/{id}/reviews/accept` | 6 |

There is no command on screens 1, 2, or 5 in MVP.

---

# 14. Query-key recommendation

For React Query/TanStack Query-like cache:

```ts
courseWorkspace(courseId)

lessonWorkspace(courseId, lessonId)

studentProblemWorkspace(problemAssignmentId)

teacherReviewQueue({
  courseId,
  groupId,
  studentId,
  attemptKind,
  search,
  page,
  pageSize
})

teacherProblemWork(problemWorkId)
```

Do not key ProblemWorkspace by `problemWorkId` on student side because it may be null before first submit.

---

# 15. Cache invalidation map

## SubmitSolution

Invalidate:

```text
studentProblemWorkspace(assignmentId)
lessonWorkspace(courseId, lessonId)
courseWorkspace(courseId)
teacherReviewQueue(*)  // if same browser/role environment needs freshness
```

## PostMessage

Invalidate:

```text
studentProblemWorkspace(assignmentId)
teacherProblemWork(problemWorkId)
```

## RequestRevision

Invalidate:

```text
teacherProblemWork(problemWorkId)
teacherReviewQueue(*)
studentProblemWorkspace(assignmentId)
lessonWorkspace(courseId, lessonId)
courseWorkspace(courseId)
```

## AcceptSubmission

Invalidate same as RequestRevision.

---

# 16. Server-derived vs frontend-derived values

## MUST come from server

```text
authorization
availability
ProblemWork.Status
AttemptNumber
Version
current submission
content version
problem statement
progress counts
queue membership
canSubmit
canAccept
canRequestRevision
```

## MAY be frontend-derived

```text
status label text
status color
"Попытка №N"
"Следующая попытка №N+1"
elapsed display from submittedAt
grouping submissions/reviews/messages into timeline
editor mode
responsive layout
```

Security or domain eligibility MUST NEVER depend only on frontend-derived values.

---

# 17. Components shared across screens

Recommended reusable component set:

```text
<AppShell />
<SidebarNavigation />

<Breadcrumbs />
<PageHeader />
<Tabs />

<StatusBadge />
<AvailabilityBadge />
<ProgressBar />

<ProblemStatement />
<MarkdownMath />

<AttachmentUploader />
<AttachmentGrid />
<AttachmentPreview />

<SubmissionView />
<AttemptBadge />
<AttemptTimeline />

<TeacherFeedbackBanner />
<MessageThread />
<MessageComposer />

<EmptyState />
<ErrorState />
<LoadingSkeleton />

<ConfirmActionButton />
<AsyncButton />
<InlineError />
```

Screen-specific components SHOULD be compositions of these primitives rather than independent visual implementations.

---

# 18. Status visual contract

One status → one visual treatment everywhere.

Recommended semantic tokens:

```text
NotStarted
  neutral

Submitted
  info / blue-neutral

NeedsRevision
  warning

Accepted
  success / green
```

Do not assign status semantics directly to hardcoded hex values inside screens.

Use design tokens:

```text
--status-neutral-*
--status-info-*
--status-warning-*
--status-success-*
```

---

# 19. Role-specific navigation

## Student

```text
Главная
Мои курсы
Расписание
Текущие задачи
Архив задач
Прогресс
Сообщения
```

## Teacher

```text
Главная
Мои курсы
Расписание
Проверка
Архив задач
Прогресс
Сообщения
```

Exact sidebar IA may evolve, but screen routes MUST not depend on sidebar text.

---

# 20. Responsive priority

Student solution screen has highest mobile priority because photo submission is a primary real-world use case.

On narrow viewport:

```text
Problem statement
Status / latest feedback
Photos
Add photo
Optional text/comment
Submit
Tabs
```

Right sidebar collapses into inline cards.

Teacher review is desktop-first but MUST remain usable on tablet.

---

# 21. Accessibility

Required:

- all status meaning duplicated in text, not color only;
- upload area keyboard accessible;
- photos have file-name alt/label;
- tabs support keyboard navigation;
- focus moves to first validation error;
- buttons expose pending state;
- disabled actions have an explanatory reason when meaningful.

---

# 22. Analytics events (optional, non-domain)

If product analytics is enabled:

```text
course_opened
lesson_opened
problem_opened
solution_photo_added
solution_submitted
history_opened
review_queue_opened
review_opened
revision_requested
submission_accepted
```

These events MUST NOT affect domain behavior.

---

# 23. Backend additions required before frontend completion

The existing feature is sufficient for submit/review commands, but the complete six-screen frontend needs the following additions.

## Required read endpoints

```http
GET /api/v1/courses/{courseId}/workspace

GET /api/v1/courses/{courseId}/lessons/{lessonId}/workspace

GET /api/v1/problem-assignments/{assignmentId}/workspace
```

## Required enrichments

### `GET /problem-works/{id}`

Add:

```text
problemAssignmentId
student
course title
lesson title/order
problem statementMarkdown
attachment metadata
currentSubmission
permissions
```

### Review queue

Add:

```text
courseTitle
lessonTitle
groupId/groupTitle
waitingDurationSeconds
attemptKind filter
search? (recommended)
```

No new aggregate or domain state is required.

---

# 24. Backend endpoints NOT required

Do NOT create:

```text
POST /problem-works/{id}/start
POST /attempts/create
POST /problem-work/set-in-progress
POST /lessons/{id}/complete
POST /courses/{id}/update-progress
```

These would duplicate or violate the existing domain model.

---

# 25. Acceptance criteria for the six-screen integration

## Contract

1. Every screen has exactly one primary query model.
2. A student can open an unstarted task without `ProblemWorkId`.
3. Frontend never persists `InProgress` as `ProblemWork.Status`.
4. Attempt is created only on submit.
5. Previous submissions are immutable.
6. Student cannot edit while `Submitted`.
7. Student can edit a revision while status remains `NeedsRevision`.
8. Full history appears only in the History tab.
9. Teacher queue contains only `Submitted`.
10. Queue filters do not invent domain statuses.
11. Teacher review sends current `ExpectedVersion`.
12. `409` is refetched, never auto-retried.
13. `RequestRevision` requires feedback.
14. `AcceptSubmission` allows empty feedback.
15. Photo upload completes before submit and yields `AttachmentId`.
16. Course/Lesson progress comes from read models.
17. Problem content is resolved from immutable course version.
18. Successful commands invalidate all affected projections.
19. UI permission checks mirror backend capability flags but never replace server authorization.
20. No separate submit screen or submit route exists.

---

# 26. Recommended implementation slices

Implement frontend in this order:

```text
1. Shared contracts + API client
2. Status tokens + common components
3. Course workspace
4. Lesson workspace
5. Student Problem Workspace
6. Upload + SubmitSolution
7. History + messages
8. Teacher Review Queue
9. Teacher Review Workspace
10. Accept / RequestRevision + concurrency handling
11. Cross-screen cache invalidation
12. Responsive student photo workflow
13. Integration tests
```

This order produces a usable vertical path early:

```text
Course
→ Lesson
→ Problem
→ Submit
→ Queue
→ Review
→ Revision
→ Submit again
→ Accept
```

---

# 27. Minimal TypeScript contract index

```ts
// read DTOs
CourseWorkspaceDto
CourseLessonRowDto
LessonWorkspaceDto
LessonProblemRowDto
StudentProblemWorkspaceDto
TeacherProblemWorkDto
ReviewQueueResponseDto
ReviewQueueItemDto

// shared
ProblemWorkStatusDto
LessonProgressStatusDto
AvailabilityDto
AttachmentDto
UserSummaryDto
SubmissionDto
ReviewDto
MessageDto
ProblemHintDto
ApiErrorDto

// commands
UploadResponseDto
SubmitSolutionRequest
SubmitSolutionResponse
PostMessageRequest
PostMessageResponse
RequestRevisionRequest
RequestRevisionResponse
AcceptSubmissionRequest
AcceptSubmissionResponse
```

This set SHOULD live in the frontend API/contracts layer and SHOULD NOT reuse backend domain entities directly.

---

# 28. Architectural rule

The frontend contract is an anti-corruption boundary between UI and domain.

Frontend knows:

```text
DTO
capabilities
status codes
commands
queries
```

Frontend MUST NOT know:

```text
EF Core entities
aggregate internals
database schema
domain event persistence
repository implementation
```

Likewise backend MUST NOT shape domain entities solely to make a particular screen convenient.

Use dedicated read models for the six screens.


---

# 29. Right contextual panel contract

All six screens MUST implement a three-column desktop layout:

```text
App navigation | Main content | Context panel
```

The right panel is part of the screen contract, not optional decoration.

## 29.1. Shared component

Recommended abstraction:

```tsx
<ContextPanel>
  <ContextSection />
  ...
</ContextPanel>
```

Responsive behavior:
- desktop: fixed right rail;
- tablet: collapsible rail;
- mobile: sections move below main content.

## 29.2. Screen 1 — Course panel data

`CourseWorkspaceDto` SHOULD expose:

```ts
rightPanel: {
  progress: {
    completedLessons: number;
    totalLessons: number;
    percent: number;
  };

  nextEvents: Array<{
    id: string;
    title: string;
    startsAt: string;
    kind: string;
  }>;

  teacher: UserSummaryDto | null;

  materials: Array<{
    id: string;
    title: string;
    kind: string;
    url: string;
  }>;
};
```

If the existing DTO already exposes equivalent data at root, frontend may compose the panel locally. Do not duplicate API data unnecessarily.

## 29.3. Screen 2 — Lesson panel data

`LessonWorkspaceDto` SHOULD expose or allow deriving:

```ts
rightPanel: {
  about: string | null;

  progress: {
    acceptedProblems: number;
    totalAssignedProblems: number;
    percent: number;
  };

  deadlines: Array<{
    id: string;
    title: string;
    dueAt: string;
  }>;

  discussion: {
    threadId: string | null;
    messageCount: number;
  };
};
```

## 29.4. Screen 3 — Student solution panel

`StudentProblemWorkspaceDto` MUST support:

```ts
rightPanel: {
  status: ProblemWorkStatusDto;
  latestAttemptNumber: number | null;
  latestTeacherFeedback: MessageDto | null;
  usefulMaterials: LessonMaterialDto[];
};
```

This may be composed from existing fields.

The panel MUST NOT contain the full `history`.

## 29.5. Screen 4 — History panel

The same problem workspace query may compose:

```ts
rightPanel: {
  status: ProblemWorkStatusDto;
  latestAttemptNumber: number | null;
  currentAttachments: AttachmentDto[];
  usefulMaterials: LessonMaterialDto[];
};
```

Full attempts stay in `AttemptTimeline`.

## 29.6. Screen 5 — Review queue panel

`GET /teacher/review-queue` SHOULD also return aggregate metadata:

```ts
type ReviewQueueSummaryDto = {
  total: number;
  firstAttempts: number;
  revisions: number;
  averageWaitingSeconds: number | null;

  byCourse: Array<{
    courseId: string;
    courseTitle: string;
    count: number;
  }>;

  byGrade?: Array<{
    gradeLabel: string;
    count: number;
  }>;
};
```

Recommended response:

```ts
type ReviewQueueResponseDto = {
  items: ReviewQueueItemDto[];
  page: number;
  pageSize: number;
  total: number;
  summary: ReviewQueueSummaryDto;
};
```

The context panel renders:
- quick filters;
- summary;
- course/grade breakdown.

Server-side filtering remains authoritative.

## 29.7. Screen 6 — Teacher review panel

`TeacherProblemWorkDto` MUST support:

```ts
rightPanel: {
  previousAttempts: Array<{
    submissionId: string;
    attemptNumber: number;
    submittedAt: string;
    decision: "Accepted" | "NeedsRevision" | null;
  }>;

  context: {
    courseTitle: string;
    lessonTitle: string;
    studentDisplayName: string;
    submittedAt: string | null;
  };
};
```

Quick note templates are local UI helpers and MUST NOT become domain entities.

Primary review actions belong in the right panel:

```text
Принять решение
Вернуть на доработку
```

## 29.8. No duplication rule

The context panel MUST NOT duplicate:
- entire lesson list;
- entire task list;
- full attempt timeline;
- full solution body;
- full teacher queue.

It contains summaries, shortcuts and contextual data only.

## 29.9. Acceptance additions

21. Every desktop screen renders a right contextual panel.
22. Right-panel data is derived from the same authoritative screen query where practical.
23. No extra query is added solely to duplicate already-fetched screen data.
24. History is not duplicated in the right panel.
25. Review actions stay visible in teacher review right panel.
26. On mobile, context panel content is preserved and moved inline rather than dropped.


## C3 implemented read boundary (2026-10-07)

Authoritative C# DTOs: `src/Lms.Application/ReadModels.cs`. No frontend implementation in C3.
G09: all active assignments are required; lesson percent = Accepted / active assigned × 100;
empty lesson = NotStarted, percent 0. Course percent = completed / available logical lessons × 100;
empty denominator = 0. Multiple content versions do not duplicate logical lessons in course progress.
Course lesson rows carry `lesson` metadata plus `courseVersionIds`; LessonWorkspace carries primary
`lesson` plus `contentVersions` and exact-version problem rows. Versions are ordered by publication
number; primary metadata uses the earliest available version. Mixed-version course metadata has
`publishedVersionId=null`; there is no modeled enrollment-to-version binding.
Unavailable student versions are excluded from LessonWorkspace; if none are available, return 403.
Withdrawn assignments are excluded from lists/progress and direct student reads return 403.
Teacher progress covers only course/group-authorized active assignments; teacher enrollment is null.
User display names, designated course teacher, group labels, grade, hints/materials/events and download
metadata have no source: null/[] or omitted optional sections. Submission/Message DTOs preserve
`attachmentIds`; they do not invent file metadata or URLs. Markdown is source text, never executable HTML.
Queue search uses available course/lesson/problem titles and IDs, plus student UUID; profile-name
search awaits a trusted profile source. Summary includes every active filter and all pages.
History submissions/reviews are newest attempt first; messages are CreatedAt ascending then ID.
Missing exact content returns 503 ContentUnavailable, never a newer statement.
