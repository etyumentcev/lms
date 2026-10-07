# FEATURE-SUBMISSION-REVIEW

**Status:** Draft  
**Version:** 1.1  
**Scope:** первый вертикальный slice LMS  
**Flow:** `Submit → Review → Revise → Accept`  
**Depends on:** `COURSE-CONTENT-SPEC.md`, `LMS-MVP-ACTORS-USE-CASES.md`, `LMS-DOMAIN-SPEC.md`, `LMS-UI-FLOW-SPEC-v2.md`, `LMS-FRONTEND-CONTRACT.md`

---

# 1. Цель feature

Этот feature реализует полный рабочий цикл одной назначенной задачи:

1. Student отправляет первую попытку решения.
2. Teacher получает её в очереди проверки.
3. Teacher возвращает решение на доработку.
4. Student видит feedback и отправляет новую попытку.
5. Teacher принимает новую попытку.
6. История всех попыток, сообщений и проверок сохраняется.
7. Прогресс задачи становится `Accepted`.

Feature должен быть реализован как вертикальный slice:

```text
Domain
  ↓
Application
  ↓
Persistence
  ↓
API
  ↓
Frontend integration contract
  ↓
Acceptance tests
```

---

# 2. Non-goals

Feature MUST NOT включать:

- публикацию Markdown-курса;
- создание Course/Group;
- enrollment UI;
- сложную аналитику;
- уведомления;
- email;
- AI grading;
- автопроверку;
- competency model;
- attendance;
- календарь;
- leaderboard;
- teacher workload analytics.

Предполагается, что к моменту вызова feature существуют:

- Student;
- Teacher;
- Course;
- PublishedCourseVersion;
- Lesson;
- Problem;
- active Enrollment;
- active ProblemAssignment.

---

# 3. Actors

## Student

Может:

- открыть своё `ProblemAssignment`;
- отправить первую попытку;
- отправить новую попытку после `NeedsRevision`;
- видеть историю своих попыток;
- видеть feedback Teacher;
- писать сообщения;
- прикладывать файлы.

## Teacher

Может:

- видеть очередь `Submitted`;
- открыть ProblemWork;
- просмотреть текущую и предыдущие попытки;
- написать feedback;
- поставить `NeedsRevision`;
- поставить `Accepted`.

---

# 4. State machine

```text
NotStarted
    |
    | SubmitSolution
    v
Submitted
   |   \
   |    \
   |     \ Review: NeedsRevision
   |      v
   |   NeedsRevision
   |       |
   |       | SubmitSolution
   |       v
   |    Submitted
   |
   | Review: Accepted
   v
Accepted
```

Разрешённые переходы:

```text
NotStarted    -> Submitted
Submitted     -> NeedsRevision
NeedsRevision -> Submitted
Submitted     -> Accepted
```

Все остальные переходы MUST быть запрещены.

---

# 5. Aggregate boundary

Главный Aggregate Root:

```text
ProblemWork
```

Внутри aggregate:

```text
ProblemWork
├── Submission*
├── Review*
└── Message*
```

Вложения MAY храниться в отдельных таблицах/хранилище, но доменно принадлежат конкретной Submission или Message.

Изменение статуса MUST выполняться только через методы `ProblemWork`.

---

# 6. Domain model

## 6.1. ProblemWork

```text
ProblemWork
- Id
- ProblemAssignmentId
- StudentId
- Status
- AttemptCount
- LastSubmissionAt?
- LastReviewedAt?
- AcceptedAt?
- Version
```

`Version` используется для optimistic concurrency.

Status:

```text
NotStarted
Submitted
NeedsRevision
Accepted
```

---

## 6.2. Submission

```text
Submission
- Id
- ProblemWorkId
- AttemptNumber
- BodyMarkdown?
- SubmittedAt
- SubmittedBy
```

Invariants:

- Submission MUST NOT быть пустой.
- Submission MUST быть immutable.
- AttemptNumber MUST строго возрастать.
- Student MUST NOT submit при статусе `Submitted`.
- Student MUST NOT submit после `Accepted`.

---

## 6.3. Review

```text
Review
- Id
- ProblemWorkId
- SubmissionId
- TeacherId
- Decision
- MessageId?
- CreatedAt
```

Decision:

```text
Accepted
NeedsRevision
```

Invariants:

- Review MUST ссылаться на текущую непроверенную Submission.
- Review MUST создаваться только при `ProblemWork.Status == Submitted`.
- Одна Submission SHOULD иметь один финальный Review.
- Review MUST быть immutable.

---

## 6.4. Message

```text
Message
- Id
- ProblemWorkId
- AuthorId
- AuthorRole
- BodyMarkdown
- ReplyToMessageId?
- CreatedAt
- EditedAt?
```

Message MUST NOT автоматически менять статус.

---

# 7. Application use cases

## UC-A01. SubmitSolution

### Command

```text
SubmitSolutionCommand
- StudentId
- ProblemAssignmentId
- BodyMarkdown?
- AttachmentIds[]
- ExpectedVersion?
```

### Preconditions

- Student существует;
- Enrollment активен;
- ProblemAssignment принадлежит Student;
- ProblemAssignment активен;
- Lesson доступен;
- ProblemWork либо отсутствует, либо находится в `NeedsRevision`.

### Flow

1. Загрузить `ProblemAssignment`.
2. Проверить ownership.
3. Проверить доступ.
4. Получить `ProblemWork` либо создать новый.
5. Проверить допустимый status.
6. Проверить непустое содержимое:
   ```text
   BodyMarkdown not blank
   OR
   AttachmentIds.Count > 0
   ```
7. Создать `Submission`.
8. Вычислить `AttemptNumber = AttemptCount + 1`.
9. Увеличить `AttemptCount`.
10. Установить:
    ```text
    Status = Submitted
    LastSubmissionAt = now
    ```
11. Сохранить aggregate.
12. Опубликовать `SolutionSubmitted`.

### Result

```text
ProblemWorkId
SubmissionId
AttemptNumber
Status = Submitted
Version
```

### Failures

```text
404 AssignmentNotFound
403 AssignmentDoesNotBelongToStudent
409 WorkAlreadySubmitted
409 WorkAlreadyAccepted
422 EmptySubmission
409 ConcurrencyConflict
```

---

## UC-A02. GetReviewQueue

### Query

```text
GetReviewQueueQuery
- TeacherId
- CourseId?
- GroupId?
- StudentId?
- AttemptKind? // all | first | revision
- Search?
- Page
- PageSize
```

### Source

Read model/projection.

### Filter

Только:

```text
ProblemWork.Status == Submitted
```

### Result item

```text
ReviewQueueItem
- ProblemWorkId
- SubmissionId
- StudentId
- StudentDisplayName
- CourseId
- CourseTitle
- LessonId
- LessonTitle
- GroupId?
- GroupTitle?
- ProblemId
- ProblemTitle
- AttemptNumber
- SubmittedAt
- WaitingDuration
- Version
```

Queue MUST NOT быть отдельным source of truth.

---

## UC-A03. GetProblemWork

### Query

```text
GetProblemWorkQuery
- ActorId
- ProblemWorkId
```

### Authorization

Student:

- только свой ProblemWork.

Teacher:

- только ProblemWork доступного Course/Group.

### Result

```text
ProblemWorkDetails
- ProblemWorkId
- ProblemAssignmentId
- Student
- Course
- Lesson
- Problem
  - immutable StatementMarkdown
- ContentVersion
- Status
- AttemptCount
- Version
- CurrentSubmission?
- Submissions[]
- Reviews[]
- Messages[]
- Permissions
  - CanAccept
  - CanRequestRevision
  - CanPostMessage
```

Условие задачи MUST загружаться по immutable content reference.

---

## UC-A04. RequestRevision

### Command

```text
RequestRevisionCommand
- TeacherId
- ProblemWorkId
- SubmissionId
- FeedbackMarkdown
- ExpectedVersion
```

### Preconditions

- Teacher имеет право проверки;
- `ProblemWork.Status == Submitted`;
- Submission принадлежит ProblemWork;
- Submission является последней непроверенной;
- `ExpectedVersion` совпадает.

### Flow

1. Проверить Teacher authorization.
2. Загрузить ProblemWork.
3. Проверить optimistic version.
4. Проверить status.
5. Проверить Submission.
6. Проверить непустой feedback (`FeedbackRequired` при null/empty/whitespace) и создать Message от Teacher.
7. Создать Review:
   ```text
   Decision = NeedsRevision
   ```
8. Установить:
   ```text
   Status = NeedsRevision
   LastReviewedAt = now
   ```
9. Сохранить.
10. Опубликовать `RevisionRequested`.

### Result

```text
ReviewId
MessageId
Status = NeedsRevision
Version
```

### Failures

```text
403 TeacherNotAuthorized
404 ProblemWorkNotFound
409 WorkNotSubmitted
409 SubmissionAlreadyReviewed
409 SubmissionIsNotCurrent
409 ConcurrencyConflict
422 FeedbackRequired
```

`FeedbackMarkdown` MUST быть непустым для `NeedsRevision`: `null`, empty и whitespace запрещены. Это глобальный domain invariant `ProblemWork.RequestRevision`; Application сохраняет раннюю validation с `FeedbackRequired`, а Domain независимо отвергает невалидный feedback до любых изменений status/version/history/events. `AcceptSubmission` по-прежнему допускает отсутствие feedback.

---

## UC-A05. AcceptSubmission

### Command

```text
AcceptSubmissionCommand
- TeacherId
- ProblemWorkId
- SubmissionId
- FeedbackMarkdown?
- ExpectedVersion
```

### Flow

1. Проверить authorization.
2. Загрузить ProblemWork.
3. Проверить ExpectedVersion.
4. Проверить `Status == Submitted`.
5. Проверить текущую Submission.
6. Если feedback передан — создать Message.
7. Создать Review:
   ```text
   Decision = Accepted
   ```
8. Установить:
   ```text
   Status = Accepted
   LastReviewedAt = now
   AcceptedAt = now
   ```
9. Сохранить.
10. Опубликовать `SubmissionAccepted`.

### Result

```text
ReviewId
MessageId?
Status = Accepted
Version
```

### Failures

```text
403 TeacherNotAuthorized
404 ProblemWorkNotFound
409 WorkNotSubmitted
409 SubmissionAlreadyReviewed
409 SubmissionIsNotCurrent
409 WorkAlreadyAccepted
409 ConcurrencyConflict
```

---

## UC-A06. PostMessage

### Command

```text
PostMessageCommand
- AuthorId
- ProblemWorkId
- BodyMarkdown
- ReplyToMessageId?
- AttachmentIds[]
```

### Preconditions

- Author имеет доступ к ProblemWork.
- Message не пустой.
- ReplyToMessageId, если задан, относится к тому же ProblemWork.

### Result

```text
MessageId
CreatedAt
```

Message MUST NOT менять `ProblemWork.Status`.

---

## UC-A07. GetCourseWorkspace

### Query

```text
GetCourseWorkspaceQuery
- ActorId
- CourseId
```

### Purpose

Read model для экрана курса. Не изменяет domain state.

### Authorization

Student:

- имеет active Enrollment в Course.

Teacher:

- имеет доступ к Course.

### Result

```text
CourseWorkspace
- Course
  - Id
  - Title
  - Description?
  - GradeLabel?
  - Teacher?
  - PublishedVersionId
- Enrollment
  - Id
  - Status
- Progress
  - AvailableLessons
  - CompletedLessons
  - TotalLessons
  - Percent
- Lessons[]
  - LessonId
  - Order
  - Title
  - Summary?
  - Availability
  - Progress
    - Status: NotStarted | InProgress | Completed
    - AcceptedProblems
    - TotalAssignedProblems
- NextEvent? // optional read-model enrichment
```

`Lesson.Progress.Status` является derived read-model status и MUST NOT попадать в `ProblemWork.Status`.

---

## UC-A08. GetLessonWorkspace

### Query

```text
GetLessonWorkspaceQuery
- ActorId
- CourseId
- LessonId
```

### Purpose

Read model для экрана урока.

### Result

```text
LessonWorkspace
- Course
  - Id
  - Title
- Lesson
  - Id
  - Order
  - Title
  - Summary?
  - Availability
- Progress
  - AcceptedProblems
  - TotalAssignedProblems
  - Percent
- Problems[]
  - ProblemAssignmentId
  - Problem
    - Id
    - Order
    - Title?
    - ShortStatement?
    - DifficultyLabel?
    - EstimatedMinutes?
    - Tags[]
  - Availability
  - Work
    - ProblemWorkId?
    - Status: NotStarted | Submitted | NeedsRevision | Accepted
    - AttemptCount
- Materials[]
```

Для ещё не начатой задачи допустимо:

```text
ProblemWorkId = null
Status = NotStarted
AttemptCount = 0
```

Frontend MUST открывать student problem workspace по `ProblemAssignmentId`, а не требовать существующий `ProblemWorkId`.

---

## UC-A09. GetStudentProblemWorkspace

### Query

```text
GetStudentProblemWorkspaceQuery
- StudentId
- ProblemAssignmentId
```

### Purpose

Главный read model студенческого экрана задачи. Должен работать до создания `ProblemWork`.

### Preconditions

- Student имеет доступ к assignment;
- assignment существует;
- immutable problem content может быть разрешён из published course version.

### Result

```text
StudentProblemWorkspace
- Assignment
  - Id
  - IsActive
- Course
  - Id
  - Title
- Lesson
  - Id
  - Order
  - Title
- Problem
  - Id
  - Order
  - Title?
  - StatementMarkdown
  - CourseVersionId
  - Tags[]
- Availability
- Work
  - ProblemWorkId?
  - Status: NotStarted | Submitted | NeedsRevision | Accepted
  - AttemptCount
  - Version?
  - LatestSubmission?
  - LatestReview?
  - LatestTeacherFeedback?
- Hints[]
- History
  - Submissions[]
  - Reviews[]
  - Messages[]
- Permissions
  - CanSubmit
  - CanPostMessage
```

Правила:

- unsent draft MUST NOT менять `ProblemWork.Status`;
- unsent draft MUST NOT создавать `Submission`;
- attempt создаётся только успешным `SubmitSolution`;
- immutable problem statement MUST загружаться из content version, связанной с assignment.

---

# 8. API conventions

Base path:

```text
/api/v1
```

Формат:

```text
application/json
```

Ошибки SHOULD использовать единый Problem Details формат:

```json
{
  "type": "https://lms/errors/concurrency-conflict",
  "title": "Concurrency conflict",
  "status": 409,
  "code": "ConcurrencyConflict",
  "detail": "The problem work was changed by another user.",
  "traceId": "..."
}
```

---

# 9. API endpoints

## 9.1. Submit solution

```http
POST /api/v1/problem-assignments/{assignmentId}/submissions
```

Request:

```json
{
  "bodyMarkdown": "Я получил $n=12$.",
  "attachmentIds": ["01JXYZ..."]
}
```

Response `201 Created`:

```json
{
  "problemWorkId": "pw_123",
  "submissionId": "sub_456",
  "attemptNumber": 1,
  "status": "Submitted",
  "version": 1
}
```

### Notes

StudentId MUST браться из authenticated principal, а не из request body.

---

## 9.2. Get problem work

```http
GET /api/v1/problem-works/{problemWorkId}
```

Response:

```json
{
  "id": "pw_123",
  "status": "NeedsRevision",
  "attemptCount": 1,
  "version": 2,
  "problem": {
    "courseVersionId": "cv_10",
    "lessonId": "divisibility",
    "problemId": "divisibility-01",
    "title": "Делимость суммы"
  },
  "submissions": [
    {
      "id": "sub_456",
      "attemptNumber": 1,
      "bodyMarkdown": "Решение...",
      "submittedAt": "2026-10-06T10:15:00Z",
      "attachments": []
    }
  ],
  "reviews": [
    {
      "id": "rev_1",
      "submissionId": "sub_456",
      "decision": "NeedsRevision",
      "createdAt": "2026-10-06T12:20:00Z",
      "messageId": "msg_10"
    }
  ],
  "messages": [
    {
      "id": "msg_10",
      "authorRole": "Teacher",
      "bodyMarkdown": "Проверь переход во второй строке.",
      "replyToMessageId": null,
      "createdAt": "2026-10-06T12:20:00Z"
    }
  ]
}
```

---

## 9.3. Teacher review queue

```http
GET /api/v1/teacher/review-queue
```

Query params:

```text
courseId?
groupId?
studentId?
attemptKind=all|first|revision
search?
page=1
pageSize=50
```

Response:

```json
{
  "items": [
    {
      "problemWorkId": "pw_123",
      "submissionId": "sub_456",
      "studentId": "student_1",
      "studentDisplayName": "Иван Иванов",
      "courseId": "olympiad-math-5",
      "courseTitle": "Олимпиадная математика",
      "lessonId": "divisibility",
      "lessonTitle": "Делимость",
      "groupId": null,
      "groupTitle": null,
      "problemId": "divisibility-01",
      "problemTitle": "Делимость суммы",
      "attemptNumber": 1,
      "submittedAt": "2026-10-06T10:15:00Z",
      "waitingDurationSeconds": 7200,
      "version": 1
    }
  ],
  "page": 1,
  "pageSize": 50,
  "total": 1
}
```

---

## 9.4. Request revision

```http
POST /api/v1/problem-works/{problemWorkId}/reviews/needs-revision
```

Request:

```json
{
  "submissionId": "sub_456",
  "feedbackMarkdown": "> Здесь используется делимость суммы.\n\nОбъясни этот переход подробнее.",
  "expectedVersion": 1
}
```

Response:

```json
{
  "reviewId": "rev_1",
  "messageId": "msg_10",
  "status": "NeedsRevision",
  "version": 2
}
```

---

## 9.5. Accept submission

```http
POST /api/v1/problem-works/{problemWorkId}/reviews/accept
```

Request:

```json
{
  "submissionId": "sub_789",
  "feedbackMarkdown": "Верно.",
  "expectedVersion": 3
}
```

Response:

```json
{
  "reviewId": "rev_2",
  "messageId": "msg_11",
  "status": "Accepted",
  "version": 4
}
```

---

## 9.6. Post message

```http
POST /api/v1/problem-works/{problemWorkId}/messages
```

Request:

```json
{
  "bodyMarkdown": "Почему здесь обязательно число чётное?",
  "replyToMessageId": "msg_10",
  "attachmentIds": []
}
```

Response:

```json
{
  "messageId": "msg_12",
  "createdAt": "2026-10-06T13:10:00Z"
}
```

---

## 9.7. Course workspace

```http
GET /api/v1/courses/{courseId}/workspace
```

Response `200 OK`:

```json
{
  "course": {
    "id": "olympiad-math-7",
    "title": "Подготовка к олимпиаде Эйлера",
    "description": null,
    "gradeLabel": "7 класс",
    "teacher": {
      "id": "teacher_1",
      "displayName": "Е. А. Тюменцев",
      "avatarUrl": null
    },
    "publishedVersionId": "cv_10"
  },
  "enrollment": {
    "id": "enr_1",
    "status": "Active"
  },
  "progress": {
    "availableLessons": 4,
    "completedLessons": 1,
    "totalLessons": 12,
    "percent": 18
  },
  "lessons": []
}
```

---

## 9.8. Lesson workspace

```http
GET /api/v1/courses/{courseId}/lessons/{lessonId}/workspace
```

Response MUST содержать `ProblemAssignmentId` для каждой задачи и MUST поддерживать `ProblemWorkId = null` для `NotStarted`.

Пример элемента задачи:

```json
{
  "problemAssignmentId": "pa_123",
  "problem": {
    "id": "problem_1",
    "order": 2,
    "title": "Остатки при делении",
    "shortStatement": "Найдите остаток...",
    "difficultyLabel": null,
    "estimatedMinutes": 20,
    "tags": ["остатки"]
  },
  "availability": {
    "isAvailable": true,
    "reason": null
  },
  "work": {
    "problemWorkId": null,
    "status": "NotStarted",
    "attemptCount": 0
  }
}
```

---

## 9.9. Student problem workspace

```http
GET /api/v1/problem-assignments/{assignmentId}/workspace
```

Response MUST работать даже если `ProblemWork` ещё не создан.

```json
{
  "assignment": {
    "id": "pa_123",
    "isActive": true
  },
  "course": {
    "id": "olympiad-math-7",
    "title": "Подготовка к олимпиаде Эйлера"
  },
  "lesson": {
    "id": "remainders",
    "order": 2,
    "title": "Остатки и сравнения"
  },
  "problem": {
    "id": "problem_1",
    "order": 2,
    "title": "Остатки при делении",
    "statementMarkdown": "Найдите остаток...",
    "courseVersionId": "cv_10",
    "tags": ["остатки"]
  },
  "availability": {
    "isAvailable": true,
    "availableFrom": null,
    "reason": "Available"
  },
  "work": {
    "problemWorkId": null,
    "status": "NotStarted",
    "attemptCount": 0,
    "version": null,
    "latestSubmission": null,
    "latestReview": null,
    "latestTeacherFeedback": null
  },
  "hints": [],
  "history": {
    "submissions": [],
    "reviews": [],
    "messages": []
  },
  "permissions": {
    "canSubmit": true,
    "canPostMessage": false
  }
}
```

---

# 10. Attachment API assumption

Этот slice предполагает существование upload flow.

Минимально:

```http
POST /api/v1/uploads
```

Возвращает временный `attachmentId`.

После Submit/Message attachment становится связанным с сущностью.

Unclaimed upload MAY удаляться background cleanup.

Upload security MUST включать:

- size limit;
- content type validation;
- random storage key;
- authorization;
- отсутствие прямого доверия к filename;
- запрет executable content where appropriate.

---

# 11. PostgreSQL schema

Ниже нормативная логическая схема. Точные типы ID MAY быть UUID/ULID.

---

## 11.1. problem_works

```sql
create table problem_works (
    id uuid primary key,
    problem_assignment_id uuid not null unique,
    student_id uuid not null,
    status varchar(32) not null,
    attempt_count integer not null default 0,
    last_submission_at timestamptz null,
    last_reviewed_at timestamptz null,
    accepted_at timestamptz null,
    version bigint not null default 0,

    constraint ck_problem_work_status
        check (status in (
            'NotStarted',
            'Submitted',
            'NeedsRevision',
            'Accepted'
        )),

    constraint ck_attempt_count_nonnegative
        check (attempt_count >= 0)
);
```

### Indexes

```sql
create index ix_problem_works_student
    on problem_works(student_id);

create index ix_problem_works_status
    on problem_works(status);
```

---

## 11.2. submissions

```sql
create table submissions (
    id uuid primary key,
    problem_work_id uuid not null
        references problem_works(id),
    attempt_number integer not null,
    body_markdown text null,
    submitted_at timestamptz not null,
    submitted_by uuid not null,

    constraint uq_submission_attempt
        unique(problem_work_id, attempt_number),

    constraint ck_attempt_number_positive
        check (attempt_number > 0)
);
```

Index:

```sql
create index ix_submissions_problem_work
    on submissions(problem_work_id, attempt_number);
```

---

## 11.3. submission_attachments

```sql
create table submission_attachments (
    id uuid primary key,
    submission_id uuid not null
        references submissions(id),
    file_name text not null,
    content_type varchar(255) not null,
    size_bytes bigint not null,
    storage_key text not null,
    created_at timestamptz not null,

    constraint ck_submission_attachment_size
        check (size_bytes > 0)
);
```

---

## 11.4. messages

```sql
create table messages (
    id uuid primary key,
    problem_work_id uuid not null
        references problem_works(id),
    author_id uuid not null,
    author_role varchar(32) not null,
    body_markdown text not null,
    reply_to_message_id uuid null
        references messages(id),
    created_at timestamptz not null,
    edited_at timestamptz null,

    constraint ck_message_author_role
        check (author_role in ('Student', 'Teacher'))
);
```

Indexes:

```sql
create index ix_messages_problem_work_created
    on messages(problem_work_id, created_at);
```

---

## 11.5. message_attachments

```sql
create table message_attachments (
    id uuid primary key,
    message_id uuid not null
        references messages(id),
    file_name text not null,
    content_type varchar(255) not null,
    size_bytes bigint not null,
    storage_key text not null,
    created_at timestamptz not null,

    constraint ck_message_attachment_size
        check (size_bytes > 0)
);
```

---

## 11.6. reviews

```sql
create table reviews (
    id uuid primary key,
    problem_work_id uuid not null
        references problem_works(id),
    submission_id uuid not null
        references submissions(id),
    teacher_id uuid not null,
    decision varchar(32) not null,
    message_id uuid null
        references messages(id),
    created_at timestamptz not null,

    constraint uq_review_submission
        unique(submission_id),

    constraint ck_review_decision
        check (decision in (
            'Accepted',
            'NeedsRevision'
        ))
);
```

Indexes:

```sql
create index ix_reviews_problem_work
    on reviews(problem_work_id, created_at);
```

---

# 12. Database invariants

Database SHOULD enforce structural invariants:

- unique attempt number;
- one final review per submission;
- valid enum-like values;
- FK ownership relations;
- positive attachment size.

Database MUST NOT быть единственным местом бизнес-валидации state transitions.

Например переход:

```text
NeedsRevision -> Accepted
```

должен блокироваться domain model, а не только SQL.

---

# 13. Optimistic concurrency

`problem_works.version` является concurrency token.

## 13.1. Read

При чтении клиент получает:

```json
{
  "version": 3
}
```

## 13.2. Command

State-changing command MUST передавать `expectedVersion`.

Например:

```json
{
  "submissionId": "sub_789",
  "expectedVersion": 3
}
```

## 13.3. Update

SQL conceptually:

```sql
update problem_works
set
    status = @status,
    last_reviewed_at = @now,
    version = version + 1
where
    id = @id
    and version = @expectedVersion;
```

Если affected rows = 0:

```text
ConcurrencyConflict
```

---

# 14. EF Core mapping guidance

Если используется EF Core, `Version` SHOULD быть configured как concurrency token.

Пример концептуально:

```csharp
builder.Property(x => x.Version)
    .IsConcurrencyToken();
```

При каждом успешном state-changing save значение увеличивается.

`DbUpdateConcurrencyException` MUST быть преобразован в application-level `ConcurrencyConflict`.

---

# 15. Critical concurrency scenarios

## 15.1. Два Teacher одновременно проверяют одну работу

Начало:

```text
Status = Submitted
Version = 5
```

Teacher A:

```text
Accept, expectedVersion=5
```

Teacher B:

```text
NeedsRevision, expectedVersion=5
```

Только один command MUST завершиться успешно.

Второй MUST получить:

```text
409 ConcurrencyConflict
```

---

## 15.2. Двойной submit

После первого успешного Submit:

```text
Status = Submitted
```

Второй concurrent Submit MUST быть отклонён.

---

## 15.3. Проверка устаревшей попытки

Teacher открыл Submission #1.

До выполнения review состояние изменилось.

Review старой Submission MUST быть отклонён как:

```text
SubmissionIsNotCurrent
```

или `ConcurrencyConflict`.

---

# 16. Transactions

Следующие операции MUST быть атомарными.

## SubmitSolution transaction

В одной транзакции:

```text
create/update ProblemWork
insert Submission
link Attachments
increment Version
```

## RequestRevision transaction

```text
insert Message
insert Review
update ProblemWork.Status
increment Version
```

## AcceptSubmission transaction

```text
insert optional Message
insert Review
update ProblemWork.Status
set AcceptedAt
increment Version
```

Domain event publication SHOULD происходить через outbox после commit.

---

# 17. Outbox

Для MVP outbox SHOULD быть предусмотрен, даже если внешних consumers пока мало.

Table example:

```sql
create table outbox_messages (
    id uuid primary key,
    occurred_at timestamptz not null,
    type text not null,
    payload jsonb not null,
    processed_at timestamptz null
);
```

В той же транзакции записывается:

```text
SolutionSubmitted
RevisionRequested
SubmissionAccepted
```

Это позволит позднее безопасно подключить:

- notifications;
- analytics;
- competency evidence;
- audit pipelines.

---

# 18. Authorization rules

## Student

Для Submit/Get/PostMessage backend MUST проверить:

```text
assignment.StudentId == currentUser.Id
AND enrollment active
AND assignment active
```

## Teacher

Для Review/Get queue backend MUST проверить:

```text
Teacher has access to assignment Course/Group
```

TeacherId MUST извлекаться из authenticated principal.

MUST NOT доверять TeacherId из request body.

---

# 19. Markdown security

Любой `BodyMarkdown` MUST считаться недоверенным вводом.

Renderer MUST:

- запрещать raw HTML;
- sanitize generated HTML;
- запрещать `javascript:`;
- запрещать unsafe links;
- безопасно рендерить LaTeX;
- безопасно разрешать `attachment://`.

API SHOULD хранить исходный Markdown, а не только HTML.

---

# 20. Error model

Рекомендуемые application error codes:

```text
AssignmentNotFound
AssignmentInactive
AssignmentDoesNotBelongToStudent

ProblemWorkNotFound
WorkAlreadySubmitted
WorkNeedsRevision
WorkAlreadyAccepted
WorkNotSubmitted

EmptySubmission
SubmissionNotFound
SubmissionAlreadyReviewed
SubmissionIsNotCurrent

TeacherNotAuthorized
StudentNotAuthorized

InvalidReplyTarget
InvalidAttachment

ConcurrencyConflict
```

Mapping:

```text
400 — malformed request
401 — unauthenticated
403 — unauthorized
404 — entity inaccessible/not found
409 — invalid state / concurrency
422 — semantic validation
```

---

# 21. Read model: Review Queue

Для MVP очередь MAY строиться query из normalized tables.

Позднее SHOULD стать projection.

Conceptual SQL:

```sql
select
    pw.id as problem_work_id,
    s.id as submission_id,
    pw.student_id,
    s.attempt_number,
    s.submitted_at,
    pw.version
from problem_works pw
join submissions s
  on s.problem_work_id = pw.id
 and s.attempt_number = pw.attempt_count
where pw.status = 'Submitted'
order by s.submitted_at asc;
```

При больших объёмах добавить projection table.

---

# 22. Frontend contract — six-screen LMS

Canonical frontend specification is defined in:

```text
LMS-UI-FLOW-SPEC-v2.md
LMS-FRONTEND-CONTRACT.md
```

This feature MUST remain compatible with those contracts.

## 22.1. Screen map

```text
1. /courses/{courseId}
   -> GET /courses/{courseId}/workspace

2. /courses/{courseId}/lessons/{lessonId}
   -> GET /courses/{courseId}/lessons/{lessonId}/workspace

3. /problem-assignments/{assignmentId}?tab=solution
   -> GET /problem-assignments/{assignmentId}/workspace

4. /problem-assignments/{assignmentId}?tab=history
   -> same student workspace read model

5. /teacher/reviews
   -> GET /teacher/review-queue

6. /teacher/reviews/{problemWorkId}
   -> GET /problem-works/{problemWorkId}
```

## 22.2. Student problem rules

Problem statement MUST be visible above tabs.

Tabs:

```text
Решение
Подсказки
История
```

There MUST NOT be a separate `Условие` tab.

There MUST NOT be a separate submit route/page.

Photo upload is the primary solution flow. Text is optional.

Domain status mapping:

```text
NotStarted    -> Не начата
Submitted     -> На проверке
NeedsRevision -> Нужна доработка
Accepted      -> Принята
```

Unsent draft:

- is technical UI/application state;
- does not create `ProblemWork` status;
- does not create Submission;
- does not increment AttemptCount.

At `Submitted`:

- solution is read-only;
- submit disabled/hidden;
- show `На проверке`.

At `NeedsRevision`:

- latest Teacher feedback is shown as actionable block `Что нужно исправить`;
- draft editor enabled;
- status stays `NeedsRevision` while editing;
- new attempt is created only by successful `SubmitSolution`.

At `Accepted`:

- solution read-only;
- show `Принята`;
- further submit forbidden.

## 22.3. History rules

Full history exists only on `История` tab.

It is composed from:

```text
Submissions[]
Reviews[]
Messages[]
```

The solution tab MAY show only:

- current/latest attempt number;
- latest actionable Teacher feedback;
- link `Все попытки`.

It MUST NOT duplicate full attempt history in the right panel.

## 22.4. Teacher queue

Queue contains only:

```text
ProblemWork.Status == Submitted
```

UI filters:

```text
Все
Первые попытки     // AttemptNumber == 1
После доработки    // AttemptNumber > 1
```

These are read-model filters, not domain statuses.

## 22.5. Teacher review

Review workspace shows:

```text
Problem statement
Student
Current attempt
Previous attempts
Attachments
Discussion
Current Version
```

Actions:

```text
[Вернуть на доработку]
[Принять решение]
```

`Вернуть на доработку` requires non-empty feedback.

`Принять решение` MAY have empty feedback.

After `409 ConcurrencyConflict` UI MUST:

1. show stale-work message;
2. refetch `ProblemWork`;
3. never repeat command automatically.

---

# 23. Frontend read-contract invariants

1. Student task screen is keyed by `ProblemAssignmentId`, not `ProblemWorkId`.
2. `ProblemWork` MAY be absent before first submit.
3. `ProblemWork.Status` has exactly four values in MVP.
4. `InProgress` MUST NOT be persisted as problem status.
5. Course/Lesson progress is server-derived read model.
6. Availability and capability flags come from backend.
7. Immutable problem statement is resolved using assignment/content version.
8. Successful commands invalidate affected read models.
9. Frontend capability checks do not replace backend authorization.
10. API/frontend DTOs MUST NOT expose EF entities or aggregate internals directly.

---

# 24. Acceptance tests

Ниже тесты являются обязательными для feature.

---

## AT-01. First submission

**Given**

```text
Student has active assignment
No ProblemWork exists
```

**When**

Student sends:

```text
BodyMarkdown = "Решение..."
```

**Then**

```text
ProblemWork created
Status = Submitted
AttemptCount = 1
Submission #1 exists
SolutionSubmitted emitted
Review queue contains work
```

---

## AT-02. Submission with attachment only

**Given**

Active assignment.

**When**

Student sends no text but one valid attachment.

**Then**

Submission succeeds.

---

## AT-03. Empty submission rejected

**When**

Student sends:

```text
BodyMarkdown = blank
Attachments = []
```

**Then**

```text
422 EmptySubmission
```

No domain state changes.

---

## AT-04. Cannot submit twice while waiting

**Given**

```text
Status = Submitted
```

**When**

Student submits again.

**Then**

```text
409 WorkAlreadySubmitted
```

AttemptCount unchanged.

---

## AT-05. Teacher sees submitted work

**Given**

Student submitted solution.

**When**

Authorized Teacher opens review queue.

**Then**

Queue contains current submission.

---

## AT-06. Teacher requests revision

**Given**

```text
Status = Submitted
Version = 1
Submission #1 current
```

**When**

Teacher sends NeedsRevision with feedback.

**Then**

```text
Review created
Message created
Status = NeedsRevision
Version = 2
RevisionRequested emitted
Queue no longer contains item
```

---

## AT-07. Student sees feedback

**Given**

Review = NeedsRevision.

**When**

Student opens ProblemWork.

**Then**

Student sees:

- original Submission;
- Teacher feedback;
- status `NeedsRevision`.

---

## AT-08. Student submits revision

**Given**

```text
Status = NeedsRevision
AttemptCount = 1
```

**When**

Student submits revised solution.

**Then**

```text
Submission #2 created
AttemptNumber = 2
Status = Submitted
AttemptCount = 2
Old Submission preserved
Version increments
Queue contains Submission #2
```

---

## AT-09. Teacher accepts revision

**Given**

```text
Status = Submitted
Submission #2 current
```

**When**

Teacher accepts.

**Then**

```text
Review(Accepted) created
Status = Accepted
AcceptedAt set
SubmissionAccepted emitted
Queue item removed
```

---

## AT-10. Cannot submit after accepted

**Given**

```text
Status = Accepted
```

**When**

Student sends new submission.

**Then**

```text
409 WorkAlreadyAccepted
```

No new Submission created.

---

## AT-11. Old attempt cannot be reviewed

**Given**

Submission #2 is current.

**When**

Teacher attempts to accept Submission #1.

**Then**

```text
409 SubmissionIsNotCurrent
```

---

## AT-12. Double review concurrency

**Given**

```text
Status = Submitted
Version = 5
```

Teacher A and Teacher B both read version 5.

**When**

Teacher A accepts successfully.

Teacher B requests revision using `expectedVersion = 5`.

**Then**

Teacher B gets:

```text
409 ConcurrencyConflict
```

Only one Review exists for current Submission.

---

## AT-13. Unauthorized student

**Given**

Student B does not own assignment of Student A.

**When**

Student B attempts GET or submit.

**Then**

```text
403/404
```

No data leakage.

---

## AT-14. Unauthorized teacher

**Given**

Teacher has no access to Course/Group.

**When**

Teacher opens/reviews work.

**Then**

```text
403 TeacherNotAuthorized
```

---

## AT-15. Message does not change status

**Given**

```text
Status = NeedsRevision
```

**When**

Student posts a message.

**Then**

```text
Status remains NeedsRevision
Message created
```

---

## AT-16. Reply target must belong to same work

**When**

Message references `ReplyToMessageId` from another ProblemWork.

**Then**

```text
422 InvalidReplyTarget
```

---

## AT-17. History preserved

**Given**

Flow:

```text
Submit #1
NeedsRevision
Submit #2
Accepted
```

**When**

Student or Teacher opens ProblemWork.

**Then**

Visible/persisted history contains:

```text
Submission #1
Review NeedsRevision
Feedback Message
Submission #2
Review Accepted
```

Nothing overwritten.

---

## AT-18. Immutable content version

**Given**

Assignment points to:

```text
CourseVersionId = 10
```

Course version 11 is published with changed problem text.

**When**

Student opens existing ProblemWork.

**Then**

Problem content is still resolved from version 10.

---

## AT-19. Student can open NotStarted assignment without ProblemWork

Given:

- active assignment exists;
- no ProblemWork exists.

When:

```text
GET /problem-assignments/{assignmentId}/workspace
```

Then:

```text
200
work.problemWorkId = null
work.status = NotStarted
work.attemptCount = 0
```

---

## AT-20. Draft does not create domain work/attempt

Given:

- student opens NotStarted task.

When:

- student types text or uploads temporary attachment but does not submit.

Then:

- no Submission is created;
- AttemptCount does not change;
- domain status does not become `InProgress`.

---

## AT-21. Lesson workspace exposes assignment id for every task

Each task row returned by lesson workspace MUST contain `ProblemAssignmentId` even when `ProblemWorkId` is null.

---

## AT-22. Review queue attempt filters are server-side

Given queue contains attempt #1 and attempt #2.

When:

```text
attemptKind=revision
```

Then only attemptNumber > 1 items are returned and `total` reflects the filtered result set.

---

## AT-23. Immutable statement returned by both student and teacher workspace

Student problem workspace and teacher ProblemWork details MUST resolve the statement from the immutable content version referenced by assignment/work, not from a later mutable source.

---

# 25. Integration tests

At minimum:

```text
API submit -> DB -> queue
API needs-revision -> DB -> reload
API resubmit -> DB -> queue
API accept -> DB -> progress state

Authorization against real persistence
Concurrency using two DbContexts/transactions
Attachment authorization
Markdown storage round-trip
```

---

# 26. Unit tests

ProblemWork unit tests MUST cover:

```text
Submit first solution
Request revision
Submit revision
Accept

Reject submit while Submitted
Reject submit after Accepted
Reject review while NotStarted
Reject review while NeedsRevision
Reject old Submission review
Reject duplicate final Review

AttemptNumber increments
AcceptedAt set only on accept
Messages do not affect status
```

---

# 27. Suggested project structure

Для ASP.NET + DDD:

```text
src/
├── Domain/
│   └── ProblemWork/
│       ├── ProblemWork.cs
│       ├── ProblemWorkStatus.cs
│       ├── Submission.cs
│       ├── Review.cs
│       ├── ReviewDecision.cs
│       ├── Message.cs
│       └── Events/
│
├── Application/
│   └── ProblemWork/
│       ├── SubmitSolution/
│       ├── RequestRevision/
│       ├── AcceptSubmission/
│       ├── PostMessage/
│       ├── GetProblemWork/
│       └── GetReviewQueue/
│
├── Infrastructure/
│   ├── Persistence/
│   ├── Attachments/
│   └── Outbox/
│
└── Api/
    └── ProblemWork/
```

Tests:

```text
tests/
├── Domain.Tests/
├── Application.Tests/
└── Integration.Tests/
```

---

# 28. Implementation order for Codex

Codex SHOULD реализовывать feature в таком порядке:

1. Проверить существующую архитектуру проекта.
2. Зафиксировать/обновить ExecPlan.
3. Реализовать ProblemWork domain model.
4. Написать domain unit tests.
5. Реализовать repositories/persistence mapping.
6. Создать migrations.
7. Реализовать application commands и существующие queries.
8. Реализовать read models: CourseWorkspace, LessonWorkspace, StudentProblemWorkspace.
9. Расширить ReviewQueue/GetProblemWork DTO согласно frontend contract.
10. Добавить authorization и capability flags.
11. Реализовать API endpoints.
12. Реализовать optimistic concurrency.
13. Реализовать outbox events.
14. Реализовать 6 frontend screens по LMS-FRONTEND-CONTRACT.md, используя существующую библиотеку компонентов и дизайн-язык ленты событий.
15. Добавить integration/frontend tests.
16. Проверить acceptance tests AT-01…AT-23.
17. Провести DDD review.
18. Удалить accidental duplication/business logic из controllers/services/frontend.

---

# 29. DDD review checklist

Перед завершением Codex MUST проверить:

- state transitions находятся внутри ProblemWork;
- controllers не меняют status напрямую;
- repositories не содержат business rules;
- Submission/Review не стали независимыми aggregate roots без причины;
- version check действительно атомарен;
- old attempts immutable;
- Review связан с конкретной Submission;
- authorization есть на backend;
- queue является read model;
- content version immutable;
- domain events сохраняются transactionally;
- tests проверяют negative paths.

---

# 30. Definition of Done

Feature считается завершённым только если:

- весь flow `Submit → Review → Revise → Accept` работает end-to-end;
- две попытки сохраняются как две разные Submission;
- Teacher feedback сохраняется;
- Student видит историю;
- Review Queue показывает только `Submitted`;
- `Accepted` запрещает дальнейший submit;
- old content version остаётся воспроизводимой;
- optimistic concurrency предотвращает conflicting reviews;
- authorization проверяется на backend;
- domain tests проходят;
- integration tests проходят;
- acceptance tests AT-01…AT-23 проходят;
- migrations воспроизводимы;
- нет прямого изменения `ProblemWork.Status` вне aggregate;
- Markdown хранится как Markdown и безопасно рендерится на read side;
- все 6 экранов работают на определённых read models;
- NotStarted задача открывается без ProblemWork;
- frontend не создаёт status `InProgress` для ProblemWork;
- full attempt history не дублируется между `Решение` и `История`;
- photo-first submit flow работает на мобильном viewport.

---

# 31. Краткий prompt для `implement-feature`

```text
Implement the vertical feature defined in FEATURE-SUBMISSION-REVIEW.md.

Strictly follow:
- COURSE-CONTENT-SPEC.md
- LMS-MVP-ACTORS-USE-CASES.md
- LMS-DOMAIN-SPEC.md
- FEATURE-SUBMISSION-REVIEW.md
- LMS-UI-FLOW-SPEC-v2.md
- LMS-FRONTEND-CONTRACT.md

Before writing code:
1. inspect the existing solution;
2. identify actors and use cases;
3. list invariants;
4. identify aggregate boundaries;
5. create/update the ExecPlan.

Implementation order:
Domain -> domain tests -> persistence -> migrations ->
application use cases -> authorization -> API ->
optimistic concurrency -> outbox -> integration tests ->
acceptance tests -> DDD review.

Do not move business state transitions into controllers,
repositories, EF mappings, or frontend code.

ProblemWork is the aggregate root controlling the state of
a student's work on a problem.

Implement the six screens and required read models described in LMS-FRONTEND-CONTRACT.md.
Use the existing reusable UI component library and match the established Sapientum event-feed visual language.
Do not introduce a ProblemWork InProgress status or a separate submit page.

The feature is not complete until AT-01 through AT-23 pass.
```


---

# UI read-model update: right contextual panels

The six-screen UI now has a required right contextual panel on every desktop screen.

This does not introduce new domain entities. The panel is a projection of existing read-model data plus optional scheduling/material metadata.

## CourseWorkspace

Read model must support rendering:
- course progress;
- nearest events, if available;
- teacher summary;
- course materials.

Equivalent root fields may be reused; a nested `rightPanel` DTO is not mandatory.

## LessonWorkspace

Read model must support:
- lesson description;
- progress;
- deadlines, if configured;
- discussion summary;
- lesson materials.

## StudentProblemWorkspace

Read model must support:
- current canonical status;
- latest attempt number;
- latest actionable teacher feedback;
- useful lesson/problem materials.

Full attempt history is not duplicated in the right panel.

## ReviewQueue

Extend queue projection with summary metadata sufficient for:
- totals;
- first-attempt vs revision counts;
- average waiting time;
- breakdown by course;
- optional breakdown by grade.

Filtering that changes row set or totals must be server-side.

## TeacherProblemWork

Read model must support:
- previous attempt summaries;
- course/lesson/student context;
- current submission time;
- permissions for review actions.

`AcceptSubmission` and `RequestRevision` remain unchanged.

## Responsive rule

Right-panel data remains semantically available on mobile; frontend may render it inline below the primary workflow.

No domain-model change is required.


C3 read decisions G08/G09/G16 and exact DTO gaps are recorded in LMS-FRONTEND-CONTRACT.md §C3.
Server `hasDraft` is removed: browser-local drafts are not authoritative server data.
Teacher CourseWorkspace enrollment is null; student enrollment is the actual C2 access record.


## C4 HTTP contract clarification (2026-10-07)

Accepted C2/C3 wire decisions override illustrative IDs, rich attachment metadata,
error aliases and older examples above. C4 tests freeze these decisions without
changing Domain, commands or persistence. Full route/DTO matrix: `codex/C4-RESULTS.md`.

Every error is `application/problem+json` with `type`, `title`, `status`, `instance`,
required machine-readable `code`, and `traceId`; type is `urn:lms:error:<code>`.
Frontend MUST branch on code, not title/detail/message.

| HTTP | Stable code |
|---|---|
| 400 | ValidationFailed (binding, required fields, query validation) |
| 401 | Unauthenticated |
| 403 | Forbidden; AttachmentNotOwned |
| 404 | NotFound |
| 405 | MethodNotAllowed |
| 409 | ConcurrencyConflict, WorkAlreadySubmitted, WorkAlreadyAccepted, WorkNotSubmitted, SubmissionIsNotCurrent, SubmissionAlreadyReviewed |
| 415 | ValidationFailed (unsupported media) |
| 422 | EmptySubmission, FeedbackRequired, InvalidReplyTarget; ValidationFailed for other domain input errors |
| 503 | ContentUnavailable (missing exact historical content) |
| 500 | InternalError (unexpected failures only) |

AssignmentNotFound/ProblemWorkNotFound normalize to NotFound. AssignmentInactive,
AssignmentDoesNotBelongToStudent, StudentNotAuthorized, TeacherNotAuthorized normalize
to Forbidden. Domain/application names themselves are unchanged.
Valid persisted workflows reject duplicate reviews by version/state before reaching
the defensive SubmissionAlreadyReviewed guard; its 409 middleware mapping is tested
without fabricating a corrupt aggregate. No error-precedence change is introduced.

Submit response is 201; other commands and all GETs are 200. First submit expectedVersion
is null/omitted; resubmit requires the current read version. Both review commands
require submissionId and expectedVersion. PostMessage has no expectedVersion and returns
only messageId/createdAt, using internal compare-and-save; fresh GET returns the version.
Feedback is linked by Review.SubmissionId and Review.MessageId; ordinary messages belong
to work, never implicitly to a submission. G03 remains closed and G07 unchanged.

Upload route/storage/claiming/downloads remain deferred, required before the Student
frontend photo submit flow. AttachmentIds and prepared test ownership records are the
current boundary. OpenAPI is not configured and remains a separate technical gap.
