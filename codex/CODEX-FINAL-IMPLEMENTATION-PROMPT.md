# FINAL IMPLEMENTATION PROMPT FOR CODEX
## LMS — 6 основных экранов + Submit → Review → Revise → Accept

Работай в существующем репозитории LMS.  
Не начинай с написания кода.

Твоя задача — реализовать согласованный vertical slice LMS для шести основных экранов, строго опираясь на существующую доменную модель и утверждённые UI/API-контракты.

---

# 1. Source of truth

Перед любыми изменениями обязательно прочитай четыре документа:

```text
docs/domain/LMS-DOMAIN-SPEC.md
docs/features/FEATURE-SUBMISSION-REVIEW.md
docs/ui/LMS-FRONTEND-CONTRACT.md
docs/ui/LMS-UI-FLOW-SPEC.md
```

Они являются нормативными.

Порядок приоритета при расхождениях:

```text
1. LMS-DOMAIN-SPEC.md
2. FEATURE-SUBMISSION-REVIEW.md
3. LMS-FRONTEND-CONTRACT.md
4. LMS-UI-FLOW-SPEC.md
```

Если текущая реализация в репозитории противоречит этим документам, не подстраивай документы под код молча.

Сначала:
- зафиксируй расхождение;
- определи, относится ли оно к domain, application, API или UI;
- предложи минимально необходимое изменение;
- только затем реализуй.

Не придумывай новую предметную модель ради удобства UI.

---

# 2. Обязательное правило: сначала analysis / ExecPlan

До написания production-кода выполни анализ репозитория и создай/обнови ExecPlan.

В ExecPlan обязательно зафиксируй:

```text
1. Фактическую структуру solution/repository.
2. Найденные проекты backend/frontend.
3. Существующие aggregate/entity/value object/use case.
4. Существующие API endpoints.
5. Существующие read models.
6. Существующие reusable UI components.
7. Существующие design tokens/layout components.
8. Существующие тесты для Submission/Review.
9. Gaps относительно четырёх source-of-truth документов.
10. Нужны ли schema/migration changes — yes/no с доказательством.
11. Порядок реализации.
12. Команды build/test.
```

Также перед кодом выпиши:

```text
Actor
→ Use Case
→ Invariants
→ Aggregate
→ Entity / Value Object
→ Domain Events
→ Evidence from repository/docs
→ State transitions
```

До завершения этого анализа production-код не писать.

---

# 3. Domain state machine — отклонения запрещены

`ProblemWork.Status` в MVP имеет только четыре значения:

```text
NotStarted
Submitted
NeedsRevision
Accepted
```

Разрешённые переходы:

```text
NotStarted    -> Submitted
Submitted     -> NeedsRevision
NeedsRevision -> Submitted
Submitted     -> Accepted
```

Запрещено добавлять:

```text
InProgress
Draft
Editing
Reviewing
Completed
Rejected
```

как новые domain/backend состояния `ProblemWork`.

UI-черновик не является domain state.

Правила:

- typing не меняет `ProblemWork.Status`;
- autosave не меняет `ProblemWork.Status`;
- добавление фото не создаёт Submission;
- Submission создаётся только при успешном `SubmitSolution`;
- AttemptNumber увеличивается только при успешной отправке;
- предыдущие Submission immutable;
- после `Submitted` ученик не может редактировать текущую попытку;
- после `NeedsRevision` ученик редактирует новый draft, но domain status остаётся `NeedsRevision`;
- после повторного submit статус снова становится `Submitted`;
- `Accepted` — терминальное состояние MVP.

Любое отклонение от этой state machine запрещено без отдельного явного решения.

---

# 4. Scope

Нужно реализовать 6 экранов:

```text
1. Course — список уроков
2. Lesson — список задач
3. Student Problem — Решение
4. Student Problem — История
5. Teacher Review Queue
6. Teacher Review
```

И end-to-end workflow:

```text
Course
→ Lesson
→ Problem
→ Submit attempt #1
→ Review Queue
→ Teacher Review
→ RequestRevision
→ Student sees feedback
→ Submit attempt #2
→ Teacher Review
→ Accept
→ Student sees Accepted
→ History contains both immutable attempts
```

---

# 5. Что НЕ входит в scope

Не реализовывать без отдельного утверждённого контракта:

```text
- rubric/scoring;
- AI grading;
- competency engine;
- notification subsystem;
- полноценный chat subsystem;
- calendar subsystem;
- leaderboard;
- gamification;
- новый Draft aggregate;
- отдельный workflow "start problem";
- отдельный command "mark lesson complete";
- отдельный mutable CourseProgress aggregate;
- новый design system.
```

Если UI-макет содержит блок, данных для которого нет в текущей модели, не создавай новый bounded context ради декоративного блока.

Либо:
- используй уже существующий read model;
- либо оставь блок скрытым/empty;
- либо зафиксируй как out-of-scope.

---

# 6. Ожидаемая структура проекта

Проверь фактическую структуру.

Ожидаемый layout:

```text
src/Lms.Api
src/Lms.Application
src/Lms.Domain
src/Lms.Infrastructure
src/Lms.Web
```

Если отличается — адаптируйся к реальному repository.

Не создавай параллельный solution или дублирующие проекты.

---

# 7. Сначала переиспользование, потом новые компоненты

Перед созданием любого UI-компонента найди существующие аналоги.

В ExecPlan добавь таблицу:

```text
Need | Existing component | Reuse / Extend / Create | Evidence
```

Минимально проверить наличие:

```text
AppShell
Sidebar
ContextPanel
Breadcrumbs
Tabs
Button
StatusBadge
ProgressBar
Table/List
Upload
AttachmentPreview
Markdown/Math renderer
EmptyState
ErrorState
LoadingSkeleton
Modal/Drawer
```

Запрещено создавать вторую UI-библиотеку рядом с существующей.

---

# 8. Read-model/API contract

Следуй `LMS-FRONTEND-CONTRACT.md`.

Если отсутствуют, реализуй read endpoints:

```http
GET /api/v1/courses/{courseId}/workspace

GET /api/v1/courses/{courseId}/lessons/{lessonId}/workspace

GET /api/v1/problem-assignments/{assignmentId}/workspace
```

Переиспользуй/расширь существующие:

```http
GET /api/v1/problem-works/{problemWorkId}

GET /api/v1/teacher/review-queue
```

Проверь существование command endpoints:

```http
POST /api/v1/uploads

POST /api/v1/problem-assignments/{assignmentId}/submissions

POST /api/v1/problem-works/{problemWorkId}/messages

POST /api/v1/problem-works/{problemWorkId}/reviews/needs-revision

POST /api/v1/problem-works/{problemWorkId}/reviews/accept
```

Если use case уже существует, не создавай второй.

---

# 9. Student Problem должен открываться по ProblemAssignmentId

Это обязательное архитектурное правило.

До первой отправки `ProblemWork` может не существовать.

Поэтому student route/query не должен требовать `ProblemWorkId`.

Используй:

```text
/problem-assignments/{assignmentId}
```

и:

```http
GET /api/v1/problem-assignments/{assignmentId}/workspace
```

Допустимое состояние:

```text
problemWorkId = null
status = NotStarted
attemptCount = 0
```

Frontend должен корректно работать в этом состоянии.

---

# 10. Экран 1 — Course

Route:

```text
/courses/{courseId}
```

Отрисовать:

```text
- course title;
- teacher;
- course metadata;
- lessons;
- lesson availability;
- derived lesson progress;
- right contextual panel.
```

Правая панель:

```text
- прогресс курса;
- ближайшие события, только если данные уже доступны;
- преподаватель;
- материалы курса.
```

Не реализовывать календарь ради этой панели.

---

# 11. Экран 2 — Lesson

Route:

```text
/courses/{courseId}/lessons/{lessonId}
```

Отрисовать:

```text
- lesson header;
- task list;
- task status;
- materials;
- progress;
- right contextual panel.
```

Canonical task status:

```text
Не начата
На проверке
Нужна доработка
Принята
```

Правая панель:

```text
- описание урока;
- прогресс;
- сроки, если уже есть модель;
- discussion summary, если уже есть модель.
```

---

# 12. Экран 3 — Student Problem / Solution

Route:

```text
/problem-assignments/{assignmentId}?tab=solution
```

Обязательные правила:

```text
- условие всегда видно;
- вкладки: Решение | Подсказки | История;
- отдельной вкладки "Условие" нет;
- отдельного submit-page нет;
- фото — основной сценарий;
- текст — опционален;
- комментарий ученика — опционален.
```

Photo-first order:

```text
1. фотографии решения;
2. добавить/снять фото;
3. optional text;
4. optional comment;
5. submit.
```

`SubmitSolution` допустим, если:

```text
text != empty
OR
attachmentIds.Count > 0
```

Статусы:

### NotStarted
- editor enabled;
- domain state не меняется при draft;
- можно показать `Черновик сохранён`.

### Submitted
- read-only;
- label `На проверке`;
- submit hidden/disabled.

### NeedsRevision
- status остаётся `Нужна доработка`;
- показать последний actionable feedback;
- editor enabled;
- новая попытка создаётся только при следующем submit.

### Accepted
- read-only;
- label `Принята`.

Правая панель:

```text
- текущий статус;
- текущая/последняя попытка;
- последний актуальный feedback;
- полезные материалы;
- переход к Истории.
```

Полный timeline здесь не показывать.

---

# 13. Draft strategy

Сначала найди существующий draft/autosave mechanism.

Если он есть — переиспользуй.

Если его нет — для MVP допустим local/browser draft.

Не создавать серверный Draft aggregate без отдельного решения.

Draft:

```text
!= ProblemWork.Status
!= Submission
!= Attempt
```

---

# 14. Экран 4 — Student Problem / History

Route:

```text
/problem-assignments/{assignmentId}?tab=history
```

История строится из:

```text
Submissions
Reviews
Messages
```

Новые попытки сверху.

Каждая попытка показывает:

```text
AttemptNumber
SubmittedAt
Body
Attachments
Review decision
Teacher feedback
Related messages
```

Messages:
- не меняют status;
- не создают attempt.

Правая панель:

```text
- текущий статус;
- последняя попытка;
- summary файлов;
- полезные материалы;
- возврат к Решению.
```

Не дублировать полный timeline.

---

# 15. Экран 5 — Teacher Review Queue

Route:

```text
/teacher/reviews
```

В очереди только:

```text
ProblemWork.Status == Submitted
```

Фильтры:

```text
Все
Первые попытки
После доработки
```

Mapping:

```text
first    = attemptNumber == 1
revision = attemptNumber > 1
```

Фильтры, влияющие на totals/pagination, выполнять server-side.

Main table:

```text
Student
Course / Lesson
Problem
Attempt
SubmittedAt / Waiting
AttemptKind
Action
```

Правая панель:

```text
- быстрые фильтры;
- статистика;
- распределение по курсам/классам.
```

Не фильтровать только текущую page client-side.

---

# 16. Экран 6 — Teacher Review

Route:

```text
/teacher/reviews/{problemWorkId}
```

Main:

```text
- immutable problem statement;
- student;
- current submission;
- text;
- photos;
- messages/history access.
```

Правая панель:

```text
- предыдущие попытки;
- course / lesson / student context;
- feedback editor;
- optional quick notes;
- Принять решение;
- Вернуть на доработку.
```

Не добавлять scoring/rubric.

### RequestRevision

```text
feedback required
Submitted -> NeedsRevision
```

### Accept

```text
feedback optional
Submitted -> Accepted
```

Оба command используют актуальный:

```text
ExpectedVersion
```

---

# 17. Optimistic concurrency

На `409 ConcurrencyConflict`:

```text
1. не retry автоматически;
2. отключить stale actions;
3. показать:
   "Работа уже изменилась или была проверена другим преподавателем.";
4. refetch ProblemWork;
5. обновить status/version/actions.
```

То же относится к stale submission cases.

---

# 18. Правые панели обязательны

Desktop layout всех 6 экранов:

```text
Left navigation | Main workspace | Right contextual panel
```

Это обязательное требование.

Правая панель:
- не дублирует основной контент;
- показывает context/summary/actions;
- использует один reusable layout/component;
- на mobile переносится inline, а не исчезает.

Особенно важно:

```text
Teacher Review:
review actions постоянно доступны справа на desktop.
```

---

# 19. Persistence/schema rule

Не менять schema только потому, что DTO неудобен.

Предпочитать:

```text
projection
join
query service
read model
```

Migration допустима только если утверждённый domain/feature contract требует данных, которые текущая schema объективно не может представить.

Если нужна migration:
- сначала зафиксировать это в ExecPlan;
- привести evidence;
- объяснить, почему read-model/query недостаточно.

---

# 20. Cache invalidation

Следуй `LMS-FRONTEND-CONTRACT.md`.

После commands invalidation должен обновлять затронутые projections.

Минимально:

### SubmitSolution
```text
StudentProblemWorkspace
LessonWorkspace
CourseWorkspace
TeacherReviewQueue
```

### RequestRevision / Accept
```text
TeacherProblemWork
TeacherReviewQueue
StudentProblemWorkspace
LessonWorkspace
CourseWorkspace
```

### PostMessage
```text
StudentProblemWorkspace
TeacherProblemWork
```

Не поддерживай вручную две независимые копии статуса.

---

# 21. Error / empty / loading states

Реализуй contract states из `LMS-FRONTEND-CONTRACT.md`.

Минимум:

```text
Добавьте фотографию или текст решения.

Не удалось загрузить файл. Попробуйте ещё раз.

Вы ещё не отправляли решение этой задачи.

Все работы проверены.

По выбранным фильтрам работ нет.

Укажите, что ученику нужно исправить.

Работа уже изменилась или была проверена другим преподавателем.
```

При failed submit уже загруженные фотографии не должны исчезать.

---

# 22. Tests

Не ограничиваться component snapshots.

Backend/application tests:

```text
- NotStarted assignment opens without ProblemWork;
- draft does not create Submission;
- attempt increments only on successful submit;
- previous Submission immutable;
- queue contains only Submitted;
- attemptKind filtering is server-side;
- revision feedback required;
- accept feedback optional;
- concurrency conflict handled;
- immutable problem content resolved correctly.
```

Frontend tests:

```text
- status -> correct UI mode;
- Submitted is read-only;
- NeedsRevision shows feedback and editable draft;
- Accepted is read-only;
- empty submit disabled;
- history not duplicated in right panel;
- review action permissions;
- revision disabled without feedback;
- right panel rendered on all six desktop screens.
```

Required E2E:

```text
Student opens Course
→ Lesson
→ Problem
→ uploads photo
→ submits attempt #1
→ Teacher sees queue item
→ requests revision
→ Student sees feedback
→ submits attempt #2
→ Teacher accepts
→ Student sees Accepted
→ History contains both immutable attempts and review decisions
```

---

# 23. Порядок реализации

Строго следовать checkpoints:

```text
A. Repository analysis + ExecPlan

B. Domain/application alignment check

C. Read-model/API gaps

D. Shared UI shell
   - left nav
   - main content
   - reusable right ContextPanel
   - shared status tokens/components

E. Course screen

F. Lesson screen

G. Student Problem / Solution
   - photo upload
   - draft
   - submit

H. Student History
   - attempts
   - reviews
   - messages

I. Teacher Review Queue

J. Teacher Review
   - RequestRevision
   - Accept
   - concurrency

K. Cache invalidation

L. Responsive behavior

M. Integration/E2E tests

N. Final DDD review
```

После каждого checkpoint:
- build;
- targeted tests;
- исправить ошибки;
- только затем переходить дальше.

---

# 24. Stop conditions

Остановись и зафиксируй blocker, если:

```text
- source-of-truth документы противоречат друг другу по domain behavior;
- существующая schema не может выразить утверждённый invariant;
- текущая архитектура требует изменения aggregate boundary;
- critical API contract невозможно реализовать без изменения domain model;
- отсутствует необходимый authorization model.
```

Не обходи такие проблемы временными UI-hack.

---

# 25. Definition of Done

Перед завершением предоставь итоговый implementation report:

```text
1. Repository paths used.
2. Files changed.
3. Existing components reused.
4. Components extended.
5. New reusable components created.
6. API/read models added/changed.
7. Application/domain changes.
8. Migrations and justification, if any.
9. Tests added.
10. Commands used for build/test.
11. Test results.
12. E2E result.
13. Remaining deviations.
14. DDD review findings.
```

Задача считается выполненной только если работает весь vertical slice:

```text
Submit
→ Review
→ NeedsRevision
→ Resubmit
→ Accept
```

и UI всех шести экранов соответствует четырём source-of-truth документам.
