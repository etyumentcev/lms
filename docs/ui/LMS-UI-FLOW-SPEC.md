# LMS-UI-FLOW-SPEC v2 — aligned with FEATURE-SUBMISSION-REVIEW and LMS-DOMAIN-SPEC

## 1. Purpose

This document is the canonical frontend flow specification for the six MVP LMS screens.

The frontend MUST reflect the domain workflow and MUST NOT introduce additional `ProblemWork` statuses.

---

# 2. Canonical domain state machine

`ProblemWork.Status` has exactly four values:

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

No other transition is valid in MVP.

Recommended UI labels:

```text
NotStarted    = Не начата
Submitted     = На проверке
NeedsRevision = Нужна доработка
Accepted      = Принята
```

---

# 3. Draft is not domain state

An unsent solution draft is a technical frontend/application state.

```text
Draft != ProblemWork.Status
Draft != Submission
Draft != Attempt
```

A draft MAY contain:

```text
BodyMarkdown?
TemporaryAttachmentIds[]
SavedAt?
```

Typing, autosaving, adding or deleting temporary attachments MUST NOT change `ProblemWork.Status`.

Before first submission the domain may have no `ProblemWork` at all.

---

# 4. Attempt rules

A `Submission` is created only by successful `SubmitSolution`.

Attempt number:

```text
AttemptNumber = AttemptCount + 1
```

Therefore:

- first successful submit creates `Submission #1`;
- after `NeedsRevision`, editing does not create `Submission #2`;
- the next successful submit creates `Submission #2`;
- old submissions are immutable.

---

# 5. Screen 1 — Course

Shows:
- course title;
- teacher;
- course progress;
- lessons.

Course progress is projection/read model.

Lesson access is derived from:

```text
Enrollment
LessonAvailability.IsPublished
LessonAvailability.AvailableFrom
```

Recommended lesson presentation:

```text
Доступен / Заблокирован
```

Progress labels may be derived:

```text
Не начат / В работе / Завершён
```

These are presentation-level lesson progress labels, not `ProblemWork.Status`.

Actions:

```text
Не начат    -> Открыть
В работе    -> Продолжить
Завершён    -> Посмотреть
Заблокирован -> disabled
```

Transition:

```text
Course -> Lesson
```

---

# 6. Screen 2 — Lesson

Shows:
- tasks;
- materials;
- derived lesson progress.

Each task uses the canonical problem state:

```text
Не начата
На проверке
Нужна доработка
Принята
```

Primary actions:

| Status | Action |
|---|---|
| Не начата | Открыть |
| На проверке | Посмотреть |
| Нужна доработка | Исправить |
| Принята | Посмотреть |

There is no domain `В работе` problem state.

If a local unsent draft exists, the row MAY show secondary text:

```text
Есть черновик
```

without changing the canonical status.

Transition:

```text
Lesson -> Problem / Solution
```

---

# 7. Screen 3 — Problem / Solution

The problem statement is always visible.

Tabs:

```text
Решение
Подсказки
История
```

There is no separate `Условие` tab.

There is no separate submit page.

## 7.1 NotStarted

Backend:
- `ProblemWork` may be absent.

UI:
- editable solution area;
- photo upload is primary;
- text solution is optional;
- submit disabled until body or attachment exists.

Typing and autosave do not change domain state.

Secondary technical label:

```text
Черновик сохранён 12:14
```

## 7.2 Submitted

UI:
- status `На проверке`;
- submitted content is read-only;
- submit hidden/disabled;
- current submission number visible.

## 7.3 NeedsRevision

UI:
- status remains `Нужна доработка`;
- latest teacher feedback displayed prominently as `Что нужно исправить`;
- new draft is editable;
- no new Submission exists until submit.

The UI MAY display:

```text
Следующая попытка: №2
```

as derived preview only.

## 7.4 Accepted

UI:
- status `Принята`;
- read-only;
- submit unavailable.

## 7.5 SubmitSolution

Valid only when:

```text
BodyMarkdown not blank
OR AttachmentIds.Count > 0
```

Successful submit:

```text
NotStarted -> Submitted
NeedsRevision -> Submitted
```

and creates immutable Submission.

---

# 8. Photo upload

Photos are the primary student workflow.

UI sequence:

```text
Select / capture photo
-> temporary upload
-> receive AttachmentId
-> add AttachmentId to draft
-> SubmitSolution
```

The submit command sends:

```text
BodyMarkdown?
AttachmentIds[]
ExpectedVersion?
```

Failed submission must preserve the editor draft and uploaded temporary attachments where technically possible.

---

# 9. Screen 4 — History

This is the only full attempt-history view.

Data source:

```text
Submissions[]
Reviews[]
Messages[]
```

Render newest attempts first.

For each attempt show:

```text
Submission #N
SubmittedAt
Body
Attachments

Review if any
Decision
Teacher feedback
CreatedAt

Messages relevant to the work
```

Student messages do not change status and do not create attempts.

The right panel MUST NOT duplicate the full attempt list.

Allowed:
- current status;
- current/latest attempt number;
- `Вернуться к решению`.

---

# 10. Screen 5 — Teacher review queue

Source of truth:

```text
ProblemWork.Status == Submitted
```

Queue filters:

```text
Все
Первые попытки
После доработки
```

Derived categories:

```text
Первые попытки     = AttemptNumber == 1
После доработки    = AttemptNumber > 1
```

These are not domain statuses.

Recommended row fields:

```text
Student
Course / Lesson
Problem
AttemptNumber
SubmittedAt
WaitingDuration
Action
```

Primary action:

```text
Проверить
```

---

# 11. Screen 6 — Teacher review

Shows:
- problem statement;
- student;
- current submission;
- attachments;
- previous attempts;
- discussion;
- version token.

Actions:

```text
Принять решение
Вернуть на доработку
```

## Accept

Calls:

```text
AcceptSubmission
```

Feedback optional.

Transition:

```text
Submitted -> Accepted
```

## Request revision

Calls:

```text
RequestRevision
```

Feedback required.

Transition:

```text
Submitted -> NeedsRevision
```

After either successful command the row disappears from active review queue.

---

# 12. Messages

`PostMessage` is supported by the feature.

Rules:

- student or teacher may post if authorized;
- reply target must belong to same `ProblemWork`;
- a message does not change `ProblemWork.Status`;
- a student reply after revision does not submit a new attempt.

---

# 13. Concurrency

Teacher commands use `ExpectedVersion`.

On `409 ConcurrencyConflict`:

```text
Работа уже изменилась или была проверена другим преподавателем.
```

UI MUST:
1. stop;
2. not retry automatically;
3. reload `ProblemWork`;
4. refresh actions/status/version.

---

# 14. Access and availability

Student can submit only if backend validates:

```text
Enrollment active
ProblemAssignment active
Lesson available
Problem belongs to student
```

Frontend disable states are advisory only. Backend authorization remains authoritative.

---

# 15. Empty states

Course:
```text
В курсе пока нет уроков.
```

Lesson:
```text
В урок пока не добавлены задачи.
```

Problem:
```text
Добавьте фотографию или текст решения.
```

History:
```text
Вы ещё не отправляли решение этой задачи.
```

Queue:
```text
Все работы проверены.
```

---

# 16. Saving states

Technical editor status:

```text
Сохранение…
Черновик сохранён
Не удалось сохранить черновик
```

These labels MUST NOT be rendered as domain status badges.

Submission:

```text
Отправка…
```

After success:

```text
На проверке
```

---

# 17. Errors

Empty submission:

```text
Добавьте фотографию или текст решения.
```

Revision without feedback:

```text
Укажите, что ученику нужно исправить.
```

Concurrency:

```text
Работа уже изменилась или была проверена другим преподавателем.
```

Attachment failure:

```text
Не удалось загрузить файл. Попробуйте ещё раз.
```

---

# 18. Canonical terminology

| Backend concept | UI |
|---|---|
| Lesson | Урок |
| Problem | Задача |
| Submission | Попытка / отправленное решение |
| AttemptNumber | Попытка №N |
| Draft | Черновик |
| Review | Проверка |
| Submitted | На проверке |
| NeedsRevision | Нужна доработка |
| Accepted | Принята |
| AcceptSubmission | Принять решение |
| RequestRevision | Вернуть на доработку |

Avoid introducing synonyms for domain states.

---

# 19. Six-screen navigation

```text
[1 Course]
    ↓
[2 Lesson]
    ↓
[3 Problem / Solution] ←→ [4 History]
    |
    | SubmitSolution
    v
 Submitted / На проверке
    |
    | Teacher
    v
[5 Review Queue]
    ↓
[6 Review]
   / \
  /   \
Accept  RequestRevision
 |         |
 v         v
Accepted  NeedsRevision
Принята   Нужна доработка
             |
             └── back to [3 Problem / Solution]
```

---

# 20. Frontend acceptance criteria

1. Frontend never writes or expects `InProgress` for `ProblemWork`.
2. Typing/autosave does not mutate domain status.
3. Submission is created only on successful submit.
4. Attempt count increases only on successful submit.
5. Previous submissions remain immutable.
6. `Submitted` is read-only for student.
7. `NeedsRevision` remains the status while the student edits the revision draft.
8. Full attempt history appears only on History tab.
9. Messages do not change status.
10. Review queue contains only `Submitted`.
11. Revision feedback is required.
12. Accept feedback is optional.
13. Concurrency conflict is never automatically retried.
14. Photo upload resolves to attachment IDs before submit.
15. Course/lesson progress is derived, never manually mutated.

---

# 21. Backend changes required

No domain model change is required.

Only one documentation/UI terminology adjustment is recommended in `FEATURE-SUBMISSION-REVIEW.md`:

```text
Accepted UI label:
Зачтено -> Принята
```

Everything else can be implemented using the existing feature commands, queries, state machine and domain invariants.


---

# 22. Правая контекстная панель — обязательная часть layout

Во всех шести основных экранах используется правая контекстная панель.

Общий принцип:

```text
Left navigation | Main workspace | Right contextual panel
```

Правая панель:
- не дублирует основное содержимое;
- показывает контекст, прогресс, вспомогательные действия и связанные материалы;
- может сворачиваться на узких экранах в inline-блоки;
- не должна содержать вторую полную копию timeline, списка задач или решения.

## 22.1. Экран 1 — Курс

Правая панель содержит:

```text
Прогресс курса
Ближайшие события
Преподаватель
Материалы курса
```

`Прогресс курса` является read-model projection.

`Ближайшие события` показываются только если соответствующий источник данных доступен.

## 22.2. Экран 2 — Урок

Правая панель содержит:

```text
О уроке
Прогресс урока
Сроки / ближайшие дедлайны
Обсуждение урока
```

Не повторять список задач.

## 22.3. Экран 3 — Задача / Решение

Правая панель содержит:

```text
Текущая задача
Текущий статус
Последняя/текущая попытка
Последний актуальный комментарий преподавателя
Полезные материалы
Ссылка "Перейти к истории"
```

Полная история попыток здесь запрещена.

## 22.4. Экран 4 — История

Правая панель содержит:

```text
Текущий статус
Текущая/последняя попытка
Файлы текущей работы
Быстрые действия
Полезные материалы
Ссылка "Вернуться к решению"
```

Полный timeline остаётся только в центральной области.

## 22.5. Экран 5 — Очередь проверки

Правая панель содержит:

```text
Фильтры
Статистика очереди
Распределение по курсам / классам
```

Фильтры должны работать server-side, если влияют на набор или totals.

## 22.6. Экран 6 — Проверка решения

Правая панель содержит:

```text
Предыдущие попытки
Контекст задачи / курса / урока
Быстрые заметки преподавателя
Принять решение
Вернуть на доработку
```

Основные review-actions должны оставаться видимыми в правой панели на desktop.

## 22.7. Responsive

На узком viewport правая панель:
- не исчезает логически;
- её блоки перемещаются под основной контент в приоритетном порядке;
- critical actions сохраняются доступными.

Для student problem screen порядок:
```text
условие
актуальный комментарий
фото
текст/комментарий
отправить
контекстные блоки правой панели
```

Для teacher review screen:
```text
решение
фото
комментарий преподавателя
Принять / Вернуть
контекст / предыдущие попытки
```

## L0.3 — будущая Identity integration, PROPOSED

Шесть основных учебных экранов сохраняются. Дополнительные минимальные login/activation/reset формы не являются multi-organization management UI. Accounts выдаёт уполномоченный администратор образовательной организации; public registration и обязательные email/phone отсутствуют. При 401 очищаются previous account state/drafts/Blob URLs; при 403 закрывается недоступный scope, session перечитывается без replay команды. Startup/focus/cross-tab проверяют сервер, frontend не является authorization authority.

[API compatibility и contracts L1](../../codex/L03-L1-IMPLEMENTATION-PLAN.md): existing business routes/DTO сохраняются, подписанный JWT ≤15 минут задаёт auth transport; browser/refresh E02 OPEN, CSRF там, где cookies; POST uploads добавляет validated courseId для scope, отсутствие допустимо только при единственном доступном course. Все files повторно авторизуются сервером. T02 ACCEPTED: минимальный UI администратора своей организации для accounts/activation/recovery и отдельных membership/enrollment/teacher assignments/блокировки. Полная multi-org панель вне L1. В L0.3 frontend/API код не изменяется. Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY.

T04: принятие обычного отзыва показывает сохранённый deadline (≤2 часа), не немедленную invalidation JWT. API 60 секунд, max HTTP 8 минут; timeout/обрыв не вызывает автоматический повтор команды. Каждое фото отдельным upload, перед Submit действующий JWT; temporary files очищаются по сроку. Scope A/B не смешивать. [ADR-L15](../adr/ADR-L15-jwt-delayed-revocation.md), [OPEN E01–E06](../../codex/L03-OWNER-DECISION-REGISTER.md).
