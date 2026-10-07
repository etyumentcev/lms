# LMS Domain Specification

**Status:** Draft  
**Version:** 1.0  
**Scope:** MVP олимпиадной LMS  
**Depends on:** `COURSE-CONTENT-SPEC.md`, `LMS-MVP-ACTORS-USE-CASES.md`

---

# 1. Цель документа

Этот документ фиксирует доменную модель LMS первого релиза.

Он выводится из MVP-сценария:

> преподаватель публикует курс → открывает урок → ученик решает задачу → отправляет попытку → преподаватель проверяет → при необходимости возвращает на доработку → ученик отправляет новую попытку → задача засчитывается → прогресс обновляется.

Документ определяет:

- Bounded Contexts;
- Aggregates;
- Entities;
- Value Objects;
- Domain Events;
- State transitions;
- Invariants;
- границы ответственности;
- связи с опубликованным Markdown-контентом;
- минимальные требования к persistence.

---

# 2. Основные архитектурные принципы

## 2.1. Content и Learning Process разделены

Учебный контент и учебный процесс MUST находиться в разных моделях.

```text
Content Context
    ↓
PublishedCourseVersion
    ↓
LessonContentRef / ProblemContentRef

Learning Process Context
    ↓
Enrollment
ProblemAssignment
ProblemWork
Submission
Review
Message
Progress
```

Markdown MUST NOT быть источником состояния учебного процесса.

PostgreSQL MUST NOT дублировать тело `lesson.md` и `problem.md` как редактируемую бизнес-сущность.

---

## 2.2. Историческая воспроизводимость

Любая попытка решения MUST быть связана с конкретной опубликованной версией условия задачи.

Изменение исходного Markdown MUST NOT изменять содержание уже выданного задания задним числом.

---

## 2.3. Работа над задачей — центральный агрегат

Центральным объектом Learning Process является:

> работа конкретного ученика над конкретным назначением конкретной задачи.

Эта работа должна сохранять историю:

```text
ProblemWork
  ├── Submission #1
  ├── Message
  ├── Review: NeedsRevision
  ├── Message
  ├── Submission #2
  └── Review: Accepted
```

---

# 3. Bounded Contexts

Для MVP достаточно трёх контекстов.

## 3.1. Content Publishing Context

Отвечает за:

- импорт Markdown-курса;
- валидацию;
- публикацию immutable version;
- разрешение `course`, `lesson`, `problem` references.

Не отвечает за:

- учеников;
- прогресс;
- submissions;
- reviews.

---

## 3.2. Learning Process Context

Отвечает за:

- зачисление;
- доступность уроков;
- назначения задач;
- работу ученика над задачей;
- попытки;
- проверки;
- статусы;
- прогресс.

Это основной доменный контекст MVP.

---

## 3.3. Discussion Context

В MVP может быть реализован внутри Learning Process, но концептуально отделяется.

Отвечает за:

- сообщения;
- цитирование;
- reply-to;
- attachments;
- Markdown/LaTeX в коммуникации.

Для MVP отдельный сервис не требуется.

---

# 4. Aggregate: PublishedCourseVersion

## 4.1. Назначение

Представляет опубликованную неизменяемую версию курса.

## 4.2. Aggregate Root

```text
PublishedCourseVersion
```

## 4.3. Поля

```text
PublishedCourseVersion
- Id
- CourseId
- VersionNumber
- SourceRevision
- PublishedAt
- PublishedBy
- Status
```

## 4.4. Entity/VO

### CourseId

Стабильный идентификатор курса из `course.md`.

### SourceRevision

Например Git commit hash.

### VersionNumber

Монотонно возрастающая версия внутри `CourseId`.

## 4.5. Invariants

1. Published version MUST быть immutable.
2. `CourseId + VersionNumber` MUST быть уникальны.
3. `SourceRevision` MUST быть зафиксирован при публикации.
4. После публикации содержимое версии MUST NOT изменяться.
5. Новое содержимое MUST создавать новую version.

## 4.6. Domain Events

```text
CourseVersionPublished
```

Payload:

```text
CourseVersionId
CourseId
VersionNumber
SourceRevision
PublishedAt
PublishedBy
```

---

# 5. Entity: LessonContentReference

Не является самостоятельным Aggregate Root.

Используется как стабильная ссылка на урок внутри PublishedCourseVersion.

```text
LessonContentReference
- CourseVersionId
- LessonId
- Order
- Title
```

Инвариант:

```text
CourseVersionId + LessonId
```

MUST однозначно определять immutable lesson content.

---

# 6. Entity: ProblemContentReference

```text
ProblemContentReference
- CourseVersionId
- LessonId
- ProblemId
- Order
- Title?
```

Инвариант:

```text
CourseVersionId + ProblemId
```

MUST однозначно определять immutable problem content.

---

# 7. Aggregate: Enrollment

## 7.1. Назначение

Связывает Student с Course.

## 7.2. Aggregate Root

```text
Enrollment
```

## 7.3. Поля

```text
Enrollment
- Id
- StudentId
- CourseId
- GroupId?
- EnrolledAt
- Status
```

Status:

```text
Active
Inactive
```

## 7.4. Invariants

1. Student MUST иметь active enrollment для доступа к Course.
2. Duplicate active enrollment для одного Student/Course SHOULD NOT существовать.
3. Deactivation MUST NOT удалять историю submissions и reviews.

## 7.5. Events

```text
StudentEnrolled
EnrollmentDeactivated
```

---

# 8. Aggregate: LessonAvailability

## 8.1. Назначение

Хранит доступность опубликованного урока.

## 8.2. Aggregate Root

```text
LessonAvailability
```

## 8.3. Поля

```text
LessonAvailability
- Id
- CourseId
- CourseVersionId
- LessonId
- IsPublished
- AvailableFrom?
- PublishedAt?
- PublishedBy?
```

## 8.4. Invariants

1. Lesson MUST ссылаться на существующую PublishedCourseVersion.
2. Student MUST NOT получать lesson content, если `IsPublished == false`.
3. Если `AvailableFrom` задано, доступ раньше этого момента MUST быть запрещён.
4. Публикация урока MUST NOT изменять Markdown.

## 8.5. Events

```text
LessonPublished
LessonUnpublished
LessonAvailabilityScheduled
```

---

# 9. Aggregate: ProblemAssignment

## 9.1. Назначение

Представляет назначение конкретной задачи ученику.

Это критически важно: `ProblemContentReference` — контент, `ProblemAssignment` — учебное назначение.

## 9.2. Aggregate Root

```text
ProblemAssignment
```

## 9.3. Поля

```text
ProblemAssignment
- Id
- StudentId
- CourseId
- CourseVersionId
- LessonId
- ProblemId
- AssignedAt
- DueAt?
- IsRequired
- Status
```

Assignment Status:

```text
Active
Withdrawn
```

## 9.4. Invariants

1. Assignment MUST ссылаться на конкретный `CourseVersionId`.
2. Assignment MUST ссылаться на существующий `ProblemId` в этой version.
3. Один Student SHOULD иметь не более одного active assignment для одной версии задачи.
4. Assignment MUST NOT автоматически перепривязываться к новой версии курса.
5. Withdrawn assignment MUST сохраняться в истории.

## 9.5. Events

```text
ProblemAssigned
ProblemAssignmentWithdrawn
```

---

# 10. Aggregate: ProblemWork

## 10.1. Назначение

Главный агрегат MVP.

Представляет всю работу ученика над одним `ProblemAssignment`.

## 10.2. Aggregate Root

```text
ProblemWork
```

## 10.3. Поля

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
```

## 10.4. Status

```text
NotStarted
Submitted
NeedsRevision
Accepted
```

## 10.5. State machine

```text
NotStarted
   |
   | SubmitSolution
   v
Submitted
   | \
   |  \
   |   \ Review(NeedsRevision)
   |    v
   |  NeedsRevision
   |      |
   |      | SubmitSolution
   |      v
   |    Submitted
   |
   | Review(Accepted)
   v
Accepted
```

## 10.6. Разрешённые переходы

```text
NotStarted    -> Submitted
Submitted     -> NeedsRevision
Submitted     -> Accepted
NeedsRevision -> Submitted
```

Другие переходы MUST быть запрещены в MVP.

## 10.7. Invariants

1. `ProblemWork` MUST принадлежать ровно одному `ProblemAssignment`.
2. StudentId MUST совпадать с StudentId assignment.
3. Submission может быть создан только владельцем work.
4. Review может быть создан только Teacher с доступом к соответствующему Course/Group.
5. Новая Submission MUST NOT удалять старую.
6. `Accepted` является terminal state для обычного MVP-flow.
7. После `Accepted` Student MUST NOT создавать новую Submission.
8. `NeedsRevision` MUST существовать только после Review конкретной Submission.
9. `Submitted` MUST означать наличие последней непроверенной Submission.
10. `AttemptCount` MUST совпадать с числом Submission.
11. Status MUST быть выводим из истории domain actions.
12. Work MUST сохранять связь с content version через assignment.

---

# 11. Entity: Submission

Submission находится внутри ProblemWork либо логически принадлежит ему.

## 11.1. Поля

```text
Submission
- Id
- ProblemWorkId
- AttemptNumber
- BodyMarkdown?
- SubmittedAt
- SubmittedBy
```

Attachments хранятся отдельно, но принадлежат Submission.

## 11.2. Invariants

1. `AttemptNumber` MUST начинаться с 1.
2. AttemptNumber MUST строго возрастать.
3. BodyMarkdown и Attachments не могут одновременно быть пустыми.
4. SubmittedBy MUST быть владельцем ProblemWork.
5. Submission MUST быть immutable после отправки.
6. Если редактирование появится позже, MUST сохраняться revision history.
7. Submission MUST хранить точное время отправки.
8. Submission MUST NOT быть hard-deleted из обычного UI.

## 11.3. Events

```text
SolutionSubmitted
```

Payload:

```text
ProblemWorkId
SubmissionId
StudentId
AttemptNumber
SubmittedAt
```

---

# 12. Entity: SubmissionAttachment

```text
SubmissionAttachment
- Id
- SubmissionId
- FileName
- ContentType
- Size
- StorageKey
- CreatedAt
```

## Invariants

1. Attachment MUST принадлежать конкретной Submission.
2. Attachment MUST проходить file validation.
3. StorageKey MUST NOT быть публичным доверенным URL.
4. Access MUST проверяться через authorization.
5. Attachment MUST NOT быть доступен постороннему Student.

---

# 13. Entity: Message

Message может быть частью thread, связанного с ProblemWork.

## 13.1. Поля

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

## 13.2. Invariants

1. Message MUST принадлежать одному ProblemWork.
2. Author MUST иметь доступ к этому ProblemWork.
3. `ReplyToMessageId`, если задан, MUST ссылаться на Message того же ProblemWork.
4. Message MUST NOT менять ProblemWork.Status автоматически.
5. Markdown MUST проходить sanitizer.
6. Raw HTML MUST NOT исполняться.
7. Авторство MUST быть immutable.
8. Время создания MUST быть immutable.
9. Редактирование, если разрешено, SHOULD сохранять history.

## 13.3. Events

```text
MessagePosted
MessageEdited
```

---

# 14. Entity: MessageAttachment

```text
MessageAttachment
- Id
- MessageId
- FileName
- ContentType
- Size
- StorageKey
- CreatedAt
```

Invariants аналогичны SubmissionAttachment.

---

# 15. Entity: Review

Review — решение Teacher по конкретной Submission.

## 15.1. Поля

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

## 15.2. Invariants

1. Review MUST ссылаться на Submission того же ProblemWork.
2. Teacher MUST иметь access к Course/Group.
3. Одну Submission SHOULD проверять один финальный Review.
4. Review MUST быть immutable после фиксации решения.
5. `Accepted` MUST перевести ProblemWork в Accepted.
6. `NeedsRevision` MUST перевести ProblemWork в NeedsRevision.
7. Review MUST применяться только если ProblemWork находится в Submitted.
8. Review MUST относиться к последней непроверенной Submission.
9. Review предыдущей попытки MUST NOT менять статус после появления новой Submission.

## 15.3. Events

```text
SubmissionAccepted
RevisionRequested
```

---

# 16. Progress

Progress SHOULD быть projection/read model, а не Aggregate Root.

## 16.1. ProblemProgress

Практически равен status ProblemWork.

```text
ProblemProgress
- ProblemAssignmentId
- Status
- AttemptCount
- LastActivityAt
```

## 16.2. LessonProgress

Вычисляется из assigned problems.

Пример:

```text
TotalProblems
AcceptedCount
SubmittedCount
NeedsRevisionCount
NotStartedCount
```

## 16.3. CourseProgress

```text
AvailableLessons
CompletedLessons
AssignedProblems
AcceptedProblems
SubmittedProblems
NeedsRevisionProblems
NotStartedProblems
```

## 16.4. Invariants

1. Progress MUST NOT редактироваться вручную.
2. Progress MUST быть производным от доменного состояния.
3. Rebuild projection SHOULD быть возможен из источников истины.

---

# 17. Domain Events — полный набор MVP

## Content

```text
CourseVersionPublished
LessonPublished
LessonUnpublished
LessonAvailabilityScheduled
```

## Enrollment

```text
StudentEnrolled
EnrollmentDeactivated
```

## Assignment

```text
ProblemAssigned
ProblemAssignmentWithdrawn
```

## Problem Work

```text
ProblemWorkStarted
SolutionSubmitted
RevisionRequested
SubmissionAccepted
```

## Discussion

```text
MessagePosted
MessageEdited
```

---

# 18. ProblemWorkStarted

Это событие MAY создаваться при первой Submission.

Необязательно создавать ProblemWork заранее для всех assignments.

Подход SHOULD быть lazy:

```text
ProblemAssignment exists
ProblemWork absent
    ↓
first SubmitSolution
    ↓
ProblemWork created
ProblemWorkStarted
SolutionSubmitted
```

Для отображения `NotStarted` достаточно отсутствия ProblemWork.

Это уменьшает объём данных.

---

# 19. Commands / Domain Operations

Минимальные domain commands:

```text
PublishCourseVersion
PublishLesson
UnpublishLesson

EnrollStudent
DeactivateEnrollment

AssignProblem
WithdrawProblemAssignment

SubmitSolution
PostMessage
EditMessage

ReviewSubmissionAsAccepted
ReviewSubmissionAsNeedsRevision
```

---

# 20. Command: SubmitSolution

Input:

```text
StudentId
ProblemAssignmentId
BodyMarkdown?
AttachmentIds[]
```

Algorithm:

1. проверить enrollment;
2. проверить lesson availability;
3. проверить assignment Active;
4. получить или создать ProblemWork;
5. проверить status:
   - NotStarted -> allowed
   - NeedsRevision -> allowed
   - Submitted -> reject
   - Accepted -> reject
6. проверить непустое содержимое;
7. создать Submission;
8. увеличить AttemptCount;
9. установить `Status = Submitted`;
10. зафиксировать `SolutionSubmitted`.

---

# 21. Command: ReviewSubmissionAsNeedsRevision

Input:

```text
TeacherId
ProblemWorkId
SubmissionId
MessageMarkdown
```

Algorithm:

1. проверить Teacher access;
2. загрузить ProblemWork;
3. проверить `Status == Submitted`;
4. проверить, что Submission — последняя непроверенная попытка;
5. проверить непустой feedback: `null`, empty и whitespace MUST быть отклонены с `FeedbackRequired`;
6. создать Message от Teacher с feedback;
7. создать Review(NeedsRevision), связанный с Message и Submission;
8. установить `Status = NeedsRevision` и увеличить Version;
9. зафиксировать `MessagePosted` и `RevisionRequested`.

Непустой feedback — глобальный domain invariant `ProblemWork.RequestRevision`, а не только validation Application/UI. Domain MUST самостоятельно проверять его до изменения status, version, history или events. При отказе aggregate MUST оставаться неизменным. Application MUST сохранять раннюю validation с ошибкой `FeedbackRequired` до вызова domain operation.

Для `ReviewSubmissionAsAccepted` feedback остаётся optional (§22).

---

# 22. Command: ReviewSubmissionAsAccepted

Input:

```text
TeacherId
ProblemWorkId
SubmissionId
MessageMarkdown?
```

Algorithm:

1. проверить Teacher access;
2. проверить `Status == Submitted`;
3. проверить Submission;
4. при наличии текста создать Message;
5. создать Review(Accepted);
6. установить `Status = Accepted`;
7. установить `AcceptedAt`;
8. зафиксировать `SubmissionAccepted`.

---

# 23. Concurrency invariants

Особенно важны для проверки.

## 23.1. Двойная проверка

Два Teacher не должны одновременно успешно создать разные Review одной Submission.

Использовать optimistic concurrency либо row/version lock.

## 23.2. Повторная отправка

Student MUST NOT отправить две попытки параллельно, если после первой статус уже `Submitted`.

## 23.3. Version token

ProblemWork SHOULD иметь:

```text
Version
```

для optimistic concurrency control.

---

# 24. Authorization invariants

Authorization не должна быть только UI-проверкой.

Backend MUST проверять:

## Student

- active enrollment;
- доступ к Course;
- доступ к Lesson;
- принадлежность assignment;
- принадлежность ProblemWork;
- принадлежность attachments.

## Teacher

- доступ к Course/Group;
- право публикации;
- право проверки соответствующего Student;
- право просмотра progress.

---

# 25. Delete policy

MVP SHOULD использовать soft-delete/withdrawal там, где удаление разрушает историю.

MUST NOT hard-delete обычным use case:

- Submission;
- Review;
- Message с исторической значимостью;
- ProblemAssignment после начала работы;
- PublishedCourseVersion.

MAY hard-delete:

- временный upload, не привязанный к Submission/Message;
- не опубликованный технический draft, если он не является доменной историей.

---

# 26. Time

Все domain timestamps MUST храниться в UTC.

UI MAY отображать локальное время пользователя.

Использовать:

```text
Instant / DateTimeOffset
```

а не локальный `DateTime` без timezone semantics.

---

# 27. IDs

Domain IDs SHOULD быть непрозрачными.

Например:

```text
EnrollmentId
ProblemAssignmentId
ProblemWorkId
SubmissionId
ReviewId
MessageId
AttachmentId
CourseVersionId
```

UI MUST NOT зависеть от структуры ID.

---

# 28. Persistence boundaries

Рекомендуемые таблицы MVP:

```text
published_course_versions
lesson_availability

enrollments
problem_assignments
problem_works

submissions
submission_attachments

messages
message_attachments

reviews
```

Projection tables MAY включать:

```text
student_course_progress
student_lesson_progress
teacher_review_queue
```

---

# 29. Review Queue как projection

Review Queue SHOULD быть read model.

Источник:

```text
ProblemWork.Status == Submitted
```

Queue item:

```text
ReviewQueueItem
- ProblemWorkId
- SubmissionId
- StudentId
- CourseId
- LessonId
- ProblemId
- AttemptNumber
- SubmittedAt
```

Queue MUST NOT быть отдельным источником истины.

---

# 30. Suggested Aggregate Boundaries

Рекомендуемый вариант:

```text
PublishedCourseVersion
Enrollment
LessonAvailability
ProblemAssignment
ProblemWork
```

Внутри ProblemWork:

```text
Submission
Review
Message
```

Однако attachments MAY храниться отдельно технически.

Главное правило:

> изменения статуса задачи должны проходить через ProblemWork Aggregate Root.

Нельзя разрешать repository напрямую менять `ProblemWork.Status`.

---

# 31. Почему Submission не Aggregate Root

Submission не должен независимо менять учебное состояние.

Он существует в контексте конкретной ProblemWork.

Иначе легко получить:

```text
Submission создана
но ProblemWork осталась NeedsRevision
```

или конфликт двух параллельных Submission.

Поэтому операция должна идти через:

```text
ProblemWork.Submit(...)
```

---

# 32. Почему Review не Aggregate Root

Review меняет state ProblemWork.

Следовательно:

```text
ProblemWork.Accept(...)
ProblemWork.RequestRevision(...)
```

должны быть атомарными операциями.

Review фиксирует факт принятого решения, но не управляет work отдельно.

---

# 33. Domain methods

Концептуальный API Aggregate Root:

```text
ProblemWork.SubmitSolution(...)
ProblemWork.PostMessage(...)
ProblemWork.RequestRevision(...)
ProblemWork.Accept(...)
```

Методы MUST проверять invariants внутри доменной модели.

Application Service MUST NOT вручную вычислять допустимые state transitions.

---

# 34. Example domain timeline

```text
09:00 ProblemAssigned

10:15 ProblemWorkStarted
10:15 SolutionSubmitted #1
      status = Submitted

12:20 MessagePosted by Teacher
12:21 RevisionRequested
      status = NeedsRevision

14:10 MessagePosted by Student

15:00 SolutionSubmitted #2
      status = Submitted

17:30 MessagePosted by Teacher
17:31 SubmissionAccepted
      status = Accepted
```

История MUST позволять восстановить:

- сколько было попыток;
- какая попытка проверялась;
- какой feedback был дан;
- когда задача была принята.

---

# 35. MVP business invariants — canonical list

Ниже список, который SHOULD использоваться в domain tests.

### Content

1. PublishedCourseVersion immutable.
2. Assignment references exact CourseVersion.
3. Historical content does not change retroactively.

### Enrollment

4. Student needs active enrollment.
5. Inactive enrollment does not erase history.

### Lesson

6. Unpublished lesson inaccessible to Student.
7. Publishing lesson does not mutate Markdown.

### Assignment

8. Assignment belongs to one Student.
9. Assignment references one immutable problem version.
10. Withdrawn assignment cannot accept new Submission.

### ProblemWork

11. Only valid transitions are allowed.
12. Accepted is terminal in MVP.
13. Submitted means one current unreviewed Submission exists.
14. NeedsRevision follows teacher Review.
15. Previous attempts remain preserved.
16. Attempt numbers strictly increase.
17. Status changes only through ProblemWork domain methods.

### Submission

18. Submission cannot be empty.
19. Submission immutable after send.
20. Student cannot submit while already Submitted.
21. Student cannot submit after Accepted.

### Review

22. Only authorized Teacher can review.
23. Review targets current unreviewed Submission.
24. Accepted changes work to Accepted.
25. NeedsRevision changes work to NeedsRevision.
26. One final Review per Submission.

### Message

27. Message belongs to one ProblemWork.
28. ReplyTo points inside same ProblemWork.
29. Message does not change status.
30. Markdown is sanitized.

### Attachments

31. Attachment access is authorized.
32. Attachment storage URL is not trusted as public identity.

### Progress

33. Progress is derived.
34. Progress is never manually edited.

### Concurrency

35. Concurrent conflicting reviews cannot both succeed.
36. Concurrent duplicate submissions cannot both succeed.

---

# 36. Domain tests — минимальный набор

Codex MUST реализовать unit tests как минимум для:

```text
Submit first solution:
NotStarted -> Submitted

Request revision:
Submitted -> NeedsRevision

Submit revised solution:
NeedsRevision -> Submitted

Accept:
Submitted -> Accepted

Cannot submit while Submitted

Cannot submit after Accepted

Cannot review NotStarted

Cannot review NeedsRevision

Cannot accept old Submission after newer Submission exists

Attempt number increments

Old submissions preserved

Message does not change status

ReplyTo from another ProblemWork rejected

Unauthorized Teacher rejected

Student cannot access another Student work

Assignment remains bound to original CourseVersion
```

---

# 37. Out of scope

MVP domain MUST NOT пока включать:

- балльную систему;
- автоматическую математическую проверку;
- AI grading;
- hints engine;
- competence model K/T/M/R/H/S/P;
- recommendations;
- adaptive assignment;
- leaderboard;
- parent accounts;
- payments;
- tariffs;
- attendance;
- calendar;
- notifications engine;
- deadline penalties;
- peer review.

Они могут быть добавлены отдельными bounded contexts позже.

---

# 38. Extension points

Архитектура SHOULD позволять позднее добавить:

```text
Hint
TeacherObservation
CompetencyEvidence
SkillTag
ProblemMetadata
IndividualTrajectory
AutoAssessment
Notification
DeadlinePolicy
Attendance
```

Но MVP MUST NOT зависеть от них.

---

# 39. Связь с будущей моделью Evidence

В будущем события:

```text
SolutionSubmitted
RevisionRequested
SubmissionAccepted
```

могут становиться Evidence для модели компетенций ученика.

Например:

- количество попыток;
- время до первой Submission;
- число циклов доработки;
- наличие самостоятельного решения;
- тип feedback.

Однако текущий MVP MUST хранить факты учебного процесса без попытки интерпретировать их как компетенции.

Это сохраняет чистую архитектурную границу.

---

# 40. Итоговая доменная схема

```text
                 CONTENT PUBLISHING
                        |
                PublishedCourseVersion
                        |
             +----------+----------+
             |                     |
    LessonContentRef      ProblemContentRef
             |                     |
-------------+---------------------+----------------
                        |
                LEARNING PROCESS
                        |
                    Enrollment
                        |
                LessonAvailability
                        |
                ProblemAssignment
                        |
                    ProblemWork
               /          |          \
              /           |           \
       Submission       Message       Review
           |
      Attachment
```

Главный инвариант:

> `ProblemWork` — единственная доменная точка, через которую меняется состояние работы ученика над задачей.

И главный архитектурный принцип:

> **Content описывает условие обучения. Learning Process описывает историю действий вокруг этого условия.**
