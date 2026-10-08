# ExecPlan: шесть экранов LMS и Submit → Review → Revise → Accept

**Текущее состояние: шесть экранов и J2 реализованы; K завершён, pilot/production NOT_READY. L0 Architecture & Decision Gates подготовлен 2026-10-09: только документы, L1–L4 не начаты.** Актуальный L0 — §27 и `codex/L0-RESULTS.md`; K — §26 и `codex/K-RESULTS.md`. Старые scope/status записи ниже — история checkpoint; разрешение текущего запуска ограничено L0.

Дата: 2026-10-07. Статус: **Repository Preflight и domain analysis выполнены; greenfield foundation + Domain/Application checkpoint выполнен**.

Checkpoint B принят пользователем: greenfield foundation, solution/project references/test infrastructure и Domain/Application для ProblemAssignment, ProblemWork, Submission, Review, Message выполнены. На текущем запуске разрешено изменить только этот ExecPlan. Production code, Domain/Application implementation, packages, migrations, API endpoints, frontend и state machine не менять. C1–N ниже — план последующих запусков, не разрешение на их выполнение сейчас.

## 1. Цель и источники

Будущий результат: Course → Lesson → Student Problem → фото/Submission #1 → Teacher Queue → NeedsRevision → Student feedback → Submission #2 → Accept → Accepted с двумя неизменяемыми попытками и решениями преподавателя. Desktop всех шести экранов имеет общий shell с левой навигацией и правой контекстной панелью; mobile сохраняет контекст inline.

Прочитан final prompt из **основного checkout**: `/home/tea/github/lms/codex/CODEX-FINAL-IMPLEMENTATION-PROMPT.md`, разделы 1–25. На момент проверки он untracked и отсутствует в worktree `/home/tea/.codex/worktrees/18db/lms`; файл не копировался и не изменялся. Это не подмена отсутствующего prompt старой задачей.

Прочитаны полностью четыре нормативных документа, в порядке приоритета prompt:

1. `docs/domain/LMS-DOMAIN-SPEC.md` (далее Domain).
2. `docs/features/FEATURE-SUBMISSION-REVIEW.md` (далее Feature).
3. `docs/ui/LMS-FRONTEND-CONTRACT.md` (далее Frontend).
4. `docs/ui/LMS-UI-FLOW-SPEC.md` (далее Flow).

Также прочитаны зависимости и контекст: `docs/domain/LMS-MVP-ACTORS-USE-CASES.md` (Actors), `docs/content/COURSE-CONTENT-SPEC.md` (Content), `docs/domain/LMS-DOMAIN-BOUNDARY.md`, `docs/architecture/CONTENT-ARCHITECTURE.md`, все три исходных документа в `codex/` и `/home/tea/github/lms/README.md`. `git diff --no-index docs /home/tea/github/lms/docs` не показал различий.

Визуальный референс присутствует: `docs/ui/mockups/lms-six-screens-right-panel.png`, PNG 1536×1024. Просмотр через `view_image` не состоялся из-за ошибки sandbox `mountinfo path is not absolute`; визуальное соответствие не оценено. Макет не определяет доменное поведение. Устранить ограничение перед визуальной реализацией.

## 2. Исходный Repository Preflight и принятая greenfield-архитектура

Исходный HEAD: `01545f0` (`docs: define LMS submission review UI flow`), detached HEAD. До создания плана `git status --short` пуст. Локальные ветки `main` и `feature/submission-review-ui` содержат тот же набор из 12 tracked файлов. Remote не запрашивался; другие внешние репозитории не исследовались.

```text
/home/tea/.codex/worktrees/18db/lms/
├── .git                         # ссылка на gitdir основного checkout
├── codex/
│   ├── CODEX-TASK-LMS-SIX-SCREENS.md
│   ├── CODEX-TASK-SELF-CONTAINED-AUDIT.md
│   ├── CONTENT-CONTRACT.md
│   └── EXECPLAN-LMS-SIX-SCREENS.md  # единственный новый файл этой задачи
└── docs/
    ├── architecture/CONTENT-ARCHITECTURE.md
    ├── content/COURSE-CONTENT-SPEC.md
    ├── domain/{LMS-DOMAIN-BOUNDARY,LMS-DOMAIN-SPEC,LMS-MVP-ACTORS-USE-CASES}.md
    ├── features/FEATURE-SUBMISSION-REVIEW.md
    └── ui/{LMS-FRONTEND-CONTRACT,LMS-UI-FLOW-SPEC}.md + mockups/*.png
```

Основной checkout `/home/tea/github/lms` дополнительно содержит untracked `README.md`, final prompt и `examples/`. README прямо называет репозиторий документацией. Примеры находятся в `examples/course/`, хотя README описывает `examples/course-v1/`. Они не являются приложением или тестовым harness.

Проверены tracked-файлы, полное перечисление файлов через `rg --files --hidden -g '!.git'`, корневые каталоги обоих checkout и деревья локальных веток. Выводы относятся к доступному репозиторию, а не ко всей возможной инфраструктуре проекта.

| Предмет проверки | Фактический результат | Evidence |
|---|---|---|
| Solution/backend/frontend | Не найдены; `src/Lms.Api`, `Lms.Application`, `Lms.Domain`, `Lms.Infrastructure`, `Lms.Web` отсутствуют | Полный inventory обоих checkout; README |
| Project manifests и зависимости | Нет `.sln`, `.slnx`, `.csproj`, `package.json`, lockfiles, конфигурации сборки | Тот же inventory |
| Aggregate/entity/VO implementations | Ни одного исходного файла реализации | Есть только модели внутри Markdown Domain/Feature |
| Application handlers, controllers, routes, API client | Не найдены | Endpoint declarations существуют только в спецификациях |
| Read models, repositories, mappings, migrations | Не найдены | SQL в Feature §11 — нормативная логическая схема, не deployed schema |
| Authorization, authenticated principal, Course/Group access | Реализация отсутствует | Domain §24, Feature §18 описывают требования |
| Upload/storage/content resolver/outbox | Реализация отсутствует | Feature §§10,16–19, Content §§29–31 |
| UI, tokens, layout, draft/autosave | Не найдены | Названия компонентов в Frontend — рекомендации, не библиотека |
| Submission/Review tests, fixtures, E2E, CI | Не найдены | Только описания тестов Feature §§24–26 |
| AGENTS.md/CLAUDE.md/PLANS.md/локальные skills | Не найдены в деревьях checkout; проверенные родительские AGENTS.md отсутствуют | Inventory, поиск AGENTS и проверки предков |
| Build/test commands | Исполняемые команды проекта не обнаружены | Нет manifests, CI и executable validator |

**Greenfield decision (принято пользователем):** отсутствие приложения ожидаемо. Создаём единственный `Lms.sln` в этом репозитории; искать другой checkout не нужно. G01 закрыт. Отсутствие authorization implementation — задача проектирования application ports, а не blocker для Domain/Application.

### Минимальная solution architecture

```text
Lms.sln
src/
  Lms.Domain/          # чистые entities, aggregate, invariants, events
  Lms.Application/     # use cases, access/persistence ports
  Lms.Infrastructure/  # пустой class library, адаптеры позже
  Lms.Api/             # пустой host-проект, composition root позже
  Lms.Web/             # зарезервированный каталог, frontend позже
tests/
  Lms.Domain.Tests/
  Lms.Application.Tests/
  Lms.Infrastructure.Tests/ # резерв тестового проекта
  Lms.Api.Tests/            # резерв тестового проекта
```

Dependencies: Domain → ничего; Application → Domain; Infrastructure → Application/Domain; Api → Application/Infrastructure. Web будет работать через HTTP contracts, без ссылок на EF/domain assemblies. Domain не зависит от EF Core, ASP.NET или UI. Тесты используют xUnit; infrastructure/api проекты не получают фиктивных passing tests. Web пока только README, без выбора framework и dependencies.

Минимум модели: ProblemAssignment (student/course/content version/lesson/problem, active/withdrawn); ProblemWork с immutable Submission/Review/Message и attachment IDs. Enrollment, availability и teacher course/group access представлены application authorization port; их полные aggregates и CRUD не создаются. Content publishing, read projections и шесть экранов отложены.

Application orchestration: загрузить assignment/work, проверить actor/access/attachment ownership, вызвать aggregate, сохранить через порт с expected persisted version. Порт требует atomic compare-and-save, unique work per assignment и transactional domain events; реализация PostgreSQL не входит в checkpoint. In-memory fake используется только в тестах и не доказывает поведение БД.

Contract decisions checkpoint B: domain RequestRevision допускает optional feedback согласно Domain §21; application RequestRevision требует непустой feedback согласно final prompt, Feature и UI. Текущая реализация сохраняется; окончательное размещение business rule остаётся отдельным архитектурным решением G03. Submit использует null expectedVersion только при отсутствии work; существующий work требует version. API wire format, locked lesson metadata и progress formulas отложены до соответствующего слоя; рекомендуемое упрощение optional UI comment записано в G07. Выполненный checkpoint B не пересматривается.

## 3. Существующие use cases/API/read models: документация ≠ код

Таблица описывает исходный preflight: **контракт найден, implementation ещё не было**. Теперь SubmitSolution, RequestRevision, AcceptSubmission, PostMessage реализованы в `src/Lms.Application/ProblemWorkService.cs`, модели — в `src/Lms.Domain/`. Все перечисленные HTTP endpoints и query/read models по-прежнему отложены. Base path `/api/v1`. При реализации вписывать реальные пути use cases/tests. Контроллеры и read models отложены.

| Use case | Endpoint | Read model / результат | Evidence и будущая проверка |
|---|---|---|---|
| Upload (техническая операция) | `POST /uploads` | Temporary attachment ID и metadata | Feature §10; Frontend §8.7; owner, limits, type validation, authorized download |
| SubmitSolution, UC-A01 | `POST /problem-assignments/{assignmentId}/submissions` | work/submission ID, attemptNumber, Submitted, version | Feature §§7,9.1; атомарность, first-submit race, версии |
| PostMessage, UC-A06 | `POST /problem-works/{problemWorkId}/messages` | messageId, createdAt | Feature §§7,9.6; access, same-work reply target, status unchanged |
| RequestRevision, UC-A04 | `POST /problem-works/{problemWorkId}/reviews/needs-revision` | reviewId, messageId, NeedsRevision, version | Feature §§7,9.4; current submission, feedback, expectedVersion |
| AcceptSubmission, UC-A05 | `POST /problem-works/{problemWorkId}/reviews/accept` | reviewId, optional messageId, Accepted, version | Feature §§7,9.5; feedback optional, terminal state |
| GetProblemWork, UC-A03 | `GET /problem-works/{problemWorkId}` | TeacherProblemWork / ProblemWorkDetails | Feature §7; Frontend §11: immutable statement, current submission, permissions, complete history |
| GetReviewQueue, UC-A02 | `GET /teacher/review-queue` | Paginated items + summary | Feature §§7,9.3,21 и дополнение; Frontend §§10,29.6 |
| GetCourseWorkspace, UC-A07 | `GET /courses/{courseId}/workspace` | CourseWorkspace | Feature §§7,9.7; Frontend §§6,29.2 |
| GetLessonWorkspace, UC-A08 | `GET /courses/{courseId}/lessons/{lessonId}/workspace` | LessonWorkspace | Feature §§7,9.8; Frontend §§7,29.3 |
| GetStudentProblemWorkspace, UC-A09 | `GET /problem-assignments/{assignmentId}/workspace` | StudentProblemWorkspace + history | Feature §§7,9.9; Frontend §§8–9,29.4–5 |

Queue query: `courseId?`, `groupId?`, `studentId?`, `attemptKind=all|first|revision`, `search?`, `page`, `pageSize`. Authorization, row filtering, pagination and totals выполняются server-side. `first = attemptNumber == 1`, `revision > 1`. Queue source — только Submitted; summary не вычислять по текущей странице.

Student route и cache key используют assignment ID. До первой отправки корректны `problemWorkId=null`, `status=NotStarted`, `attemptCount=0`, `version=null`, пустая история. GET/draft/upload не создают попытку. Primary query общий для Solution и History.

Content resolver должен использовать `assignment.CourseVersionId` и конкретную задачу этой версии, а не latest Markdown. Availability, permissions, current submission, progress counts и version приходят с сервера. Ни read model, ни панель не требуют нового aggregate.

## 4. Reusable UI и design tokens

Evidence **R0** во всех строках: полный inventory обоих checkout не содержит UI-кода, CSS, frontend manifests или библиотеки компонентов. Evidence **F**: `docs/ui/LMS-FRONTEND-CONTRACT.md` §§17–18,29. Названия ниже — кандидаты из контракта, не найденные реализации.

`Create*` — greenfield-кандидат будущего UI checkpoint. Существующей библиотеки нет; это не blocker. Сейчас UI не создаётся, в будущем нужен один минимальный набор общих компонентов по спецификации.

| Need | Existing component | Reuse / Extend / Create | Evidence |
|---|---|---|---|
| Общий shell | Не найден | Create* AppShell | R0; F; final §18 |
| Левая навигация | Не найдена | Create* SidebarNavigation | R0; Frontend §19 |
| Контекстная панель | Не найдена | Create* ContextPanel/ContextSection | R0; Frontend §29 |
| Breadcrumbs и заголовки | Не найдены | Create* Breadcrumbs/PageHeader | R0; F |
| Tabs | Не найдены | Create* Tabs с keyboard navigation | R0; Frontend §§8,21 |
| Button/async actions | Не найдены | Create* общий Button/AsyncButton | R0; F |
| StatusBadge/availability | Не найдены | Create* StatusBadge/AvailabilityBadge | R0; Frontend §§3,18 |
| ProgressBar | Не найден | Create* общий derived progress view | R0; Frontend §§6–7,17 |
| Table/List | Не найдены | Create* reusable table/list primitives | R0; Frontend §§6–7,10 |
| Upload/capture | Не найден | Create* AttachmentUploader | R0; Frontend §8.7 |
| Preview/gallery | Не найдены | Create* AttachmentPreview/AttachmentGrid | R0; F |
| Markdown/Math | Не найден | Create* единый безопасный renderer | R0; Frontend §4.4; Content §§10–18,27 |
| EmptyState | Не найден | Create* общий empty view | R0; F |
| ErrorState/InlineError | Не найдены | Create* общий error view | R0; Frontend §§3.6,17 |
| LoadingSkeleton | Не найден | Create* общий loading view | R0; F |
| Modal/Drawer | Не найдены | Create* только при необходимости preview/tablet panel | R0; final §7 |
| Submission/attempt history | Не найдены | Create* SubmissionView/AttemptTimeline/AttemptBadge | R0; Frontend §9 |
| Feedback/messages | Не найдены | Create* TeacherFeedbackBanner/MessageThread/MessageComposer | R0; Frontend §§9,11 |
| Draft/autosave | Не найден | Create* browser-local adapter, если в приложении нет готового | R0; Frontend §8.6; final §13 |
| Design tokens | Не найдены | Reuse/Extend после получения Sapientum assets; не изобретать библиотеку | R0; Frontend §18 |

Tokens по спецификации: `--status-neutral-*`, `--status-info-*`, `--status-warning-*`, `--status-success-*`; это рекомендации, а не существующие CSS variables. Не зашивать hex-цвета статусов в экраны. Правый rail содержит summaries/actions, не копии списков/решения/timeline. Полный student timeline только на History. На teacher review справа допустимы краткие предыдущие попытки и постоянные review-actions.

## 5. Domain analysis

Изначально модели существовали только в документах. На текущем checkpoint реализованы ProblemAssignment, ProblemContentReference, ProblemWork, Submission, Review, Message и events. Остальные документированные агрегаты ниже перечислены как контекст, а не как выполненная работа.

Bounded contexts: Content Publishing, Learning Process, Discussion (в MVP может оставаться внутри Learning Process). Aggregate roots: PublishedCourseVersion, Enrollment, LessonAvailability, ProblemAssignment, ProblemWork. Внутри ProblemWork — Submission, Review, Message; техническое хранение attachments может быть отдельным. Progress и ReviewQueue — projections, а не mutable aggregates.

Refs/VO: CourseId, SourceRevision, VersionNumber, LessonContentReference, ProblemContentReference, непрозрачные typed IDs. Assignment фиксирует CourseVersionId; переиздание контента не перепривязывает старые assignments. Время — UTC с timezone semantics. Нельзя менять state через repository/controller или frontend.

### Actor → Use Case → Invariants → Aggregate → Entity/VO → Events → Evidence → transitions

| Actor / Use case | Invariants | Aggregate / Entity–VO | Domain Events | Evidence | State transitions |
|---|---|---|---|---|---|
| Student / Open Course, Lesson, Problem | Active enrollment, доступный урок, свой assignment; immutable content; отсутствующий work допустим | Чтение Enrollment, LessonAvailability, ProblemAssignment, PublishedCourseVersion и optional ProblemWork; content refs | Нет | Domain §§7–10,18,24; Feature UC-A07–09; Actors UC-S02–04 | Нет; absence отображается NotStarted |
| Student / Upload и draft | Own temporary attachments, валидация; draft не Submission, не status | Технический upload; без нового aggregate | Нет событий ProblemWork | Feature §10; Frontend §§8.6–8.7; Flow §§3,8 | Нет |
| Student / SubmitSolution | Own active assignment/enrollment, available lesson, text OR attachment, valid ownership; только NotStarted/NeedsRevision; attempt+1 только при success; старые попытки immutable | ProblemWork (lazy creation); Submission, SubmissionAttachment, assignment/content refs, Version | SolutionSubmitted; ProblemWorkStarted MAY при первом submit | Domain §§10–12,18,20,23–24,31; Feature UC-A01, §§15–16 | NotStarted → Submitted; NeedsRevision → Submitted |
| Teacher / GetReviewQueue | Только разрешённые Course/Group и Submitted; current attempt; filters до pagination | Projection ProblemWork + Submission + context | Нет | Domain §29; Feature UC-A02, §21 | Нет |
| Teacher / GetProblemWork | Доступ Course/Group, immutable statement, исторические попытки | Чтение ProblemWork + assignment/content refs | Нет | Feature UC-A03; Frontend §11 | Нет |
| Teacher / RequestRevision | Authorized teacher, Submitted, latest unreviewed submission, атомарный expectedVersion; Domain и Application требуют непустой feedback согласно закрытому G03 (C1.5) | ProblemWork; Review(NeedsRevision), Message, SubmissionId, Version | RevisionRequested; проверить принятую политику MessagePosted для feedback | Domain §§15,21,23,32–33; Feature UC-A04, §§15–17 | Submitted → NeedsRevision |
| Teacher / AcceptSubmission | Те же access/current/version checks; feedback optional; Accepted terminal; одна финальная review | ProblemWork; Review(Accepted), optional Message, SubmissionId, Version | SubmissionAccepted | Domain §§15,22–23; Feature UC-A05 | Submitted → Accepted |
| Student/Teacher / PostMessage | Доступ; непустое сообщение; ReplyTo в том же work; safe Markdown/attachments; не создаёт attempt/review | ProblemWork; Message/MessageAttachment/ReplyToMessageId | MessagePosted | Domain §13; Feature UC-A06, §18; Flow §12 | Нет |
| Student/Teacher / History и progress | Preserved immutable attempts/reviews; feedback через Review.MessageId; прогресс производный | Read models из ProblemWork и assignment; не новый aggregate | Нет | Domain §§16,25,34; Feature AT-17–23; Frontend §9 | Нет |

Публикация версии, enrollment, открытие урока и назначение задач — prerequisites. Их documented events: CourseVersionPublished, StudentEnrolled/EnrollmentDeactivated, LessonPublished/Unpublished/AvailabilityScheduled, ProblemAssigned/ProblemAssignmentWithdrawn (Domain §§4–9). Реализацию этих подсистем данный UI slice не должен молча расширять.

Каноническая state machine:

```text
NotStarted --SubmitSolution--> Submitted --RequestRevision--> NeedsRevision
                                  ^                              |
                                  +--------SubmitSolution--------+
Submitted --AcceptSubmission--> Accepted (terminal)
```

Все иные переходы запрещены. `InProgress`/`Completed` допустимы только для derived lesson presentation; Draft/Editing/Reviewing/Rejected не добавлять в ProblemWork. AttemptCount равен числу submissions. State и история изменяются атомарно, первая concurrent отправка защищается также uniqueness assignment→work. Stale review не может менять новую попытку. Events должны фиксироваться transactionally; транзакционную реализацию порта и outbox добавить в будущем persistence checkpoint.

## 6. Gaps и противоречия

Исходный список gaps checkpoint B; строки G06/G08/G09/G11/G15/G16 ниже актуализированы после C4. Для domain blockers действует final §24: не выбирать молча между конфликтующими контрактами.

| ID / слой | Evidence / расхождение | Минимальное дальнейшее действие |
|---|---|---|
| G01 — repository, CLOSED | Пользователь подтвердил greenfield | Создать единственный solution по архитектуре §2 |
| G02 — authorization, PLANNED | Greenfield, middleware пока не нужен | Application access port + negative tests; authenticated principal и production access adapter до API |
| G03 — CLOSED, C1.5 | Принято пользователем: RequestRevision всегда требует непустой feedback как глобальный domain invariant | Domain самостоятельно отклоняет null/empty/whitespace с FeedbackRequired без мутации; Application сохраняет раннюю validation. Domain Spec §21 и Feature UC-A04 синхронизированы. Accept feedback остаётся optional |
| G04 — Application RESOLVED; API deferred | Feature UC-A01: `ExpectedVersion?`; §13.2 требует version от state-changing commands. Frontend §8.8 и Feature §9.1 omit version; Flow §8 допускает `ExpectedVersion?`; work до первой отправки отсутствует | Application реализует null для нового work и exact version для существующего. Save port требует atomic compare-and-save и unique assignment. Wire DTO и реальная PostgreSQL race проверяются позже |
| G05 — deferred до content/read API | Domain §8 запрещает student content неопубликованного/ещё недоступного урока; Frontend §7.6 допускает содержимое недоступного урока по политике; Actors UC-S02 запрещает видеть сам урок | Current application access port требует available lesson для submit. Политика выдачи locked metadata/content отложена до read API; сейчас content не выдаётся |
| G06 — CLOSED backend C4 | C2 Problem Details отличается от раннего Frontend ApiErrorDto | Frontend §3.6 уточнён: transport type/title/status/instance/code/traceId; code обязателен. Client adapter — будущий UI. Все 9 routes проверены |
| G07 — application/UI student comment, PLANNING RECOMMENDATION | Submission состоит из `bodyMarkdown + attachments`; отдельная сущность «student solution comment» не нужна | Для будущего Student Solution UI использовать label «Пояснение к решению — необязательно» для bodyMarkdown. Не вводить отдельную persistence-сущность комментария и не делать скрытый второй POST Message при SubmitSolution. Это planning decision, без изменения кода сейчас |
| G08 — CLOSED backend C4 | Browser-local draft не является server state | Backend не возвращает hasDraft; wire assertions фиксируют отсутствие. Локальный draft остаётся за пределами C4 |
| G09 — CLOSED C3, verified C4 | Progress только derived | Lesson = Accepted / active assignments; Course = completed / available logical lessons; нулевой denominator = 0, empty lesson NotStarted. Withdrawn/locked rules и revision проверены, mutable persistence нет |
| G10 — content/read model | DTO содержат hints/tags/difficulty/estimatedMinutes/grade/material metadata; Content v1 не задаёт все эти поля, Domain §37 исключает hints engine | Использовать существующие metadata/read sources, иначе null/[]/empty UI. Не расширять Front Matter или hints engine молча; teacher solutions не выдавать student |
| G11 — CLOSED backend C4 | Message связан с work/reply, не Submission | Review.SubmissionId + MessageId связывают feedback; общие messages — отдельная work discussion, без выдуманного SubmissionId. History linkage/order проверены HTTP tests |
| G12 — stale descriptions | Frontend §§5,10.7,23 описывают workspace/search как недостающие, но Feature v1.1 уже документирует UC-A07–09 и search; это не доказательство существования кода | Использовать полный актуальный documented contract, проверить handler once, не дублировать use cases |
| G13 — secondary documents | Feature/Frontend ссылаются на отсутствующий `LMS-UI-FLOW-SPEC-v2.md`; реальный файл без v2. Audit утверждает отсутствие mockup, хотя PNG есть; Flow §21 предлагает уже выполненную замену «Зачтено» | В плане использовать канонические пути final prompt; будущая отдельная docs cleanup без переопределения source of truth |
| G14 — scope | Actors §13 считает шестым экраном group/student progress; final §4 и нормативные UI docs считают History отдельным экраном | Следовать текущему slice из final; не добавлять седьмой экран/analytics |
| G15 — CLOSED C3, verified C4 | Summary semantics | Все active filters, включая attemptKind/search, до pagination; summary по полному filtered dataset. Dataset больше pageSize с исключаемыми строками проверен |
| G16 — CLOSED C3, verified C4 | Teacher CourseWorkspace без student enrollment | enrollment=null и teacherContext с grants; progress только authorized active assignments. Same-course/group isolation проверена |
| G18 — OpenAPI, DEFERRED | В C2–C4 нет OpenAPI package/registration/document route | Отдельный technical gap; генерация схем не добавляется в C4 |
| G17 — source packaging | Final prompt/README/examples только untracked в основном checkout; worktree их не содержит | До будущего выполнения обеспечить доступность утверждённого prompt в выбранной базе; не коммитить пользовательские файлы автоматически |

## 7. Schema/migrations: решение с доказательством

**Checkpoint B: migrations не было.** Выполнены Domain/Application и ports; PostgreSQL persistence ещё не реализован. На текущем documentation-only запуске EF Core packages и migrations не добавляются.

**Checkpoint C1: YES — initial PostgreSQL schema/migration ожидается и является нормальной частью greenfield.** Schema должна отражать утверждённые domain invariants, а не удобство DTO/read model. В C1 вводятся EF Core, PostgreSQL provider, DbContext, mappings и initial migration.

Обеспечить unique ProblemWork per ProblemAssignment, unique AttemptNumber per ProblemWork, связь Review с конкретной Submission, одну финальную Review на Submission, четыре статуса, immutable history/content references, ownership/FKs и атомарный optimistic version. State change, Submission/Review/Message и новые domain events сохраняются в одной транзакции; rollback не оставляет частичной истории или событий. Первая конкурентная отправка не должна создавать два ProblemWork.

Для C3 workspace/rightPanel/queue summary сначала использовать projections/joins/query services. По возможности строить read projections query-side без лишних mutable tables. Не создавать migration ради DTO, Draft aggregate или mutable CourseProgress/LessonProgress aggregate. Если схема не может выразить утверждённый invariant, зафиксировать конкретное расхождение перед её изменением.

## 8. Checkpoints будущей реализации и проверки

A и B завершены; результаты B сохранены. Крупный backend checkpoint C заменён на C1–C4. Все следующие этапы пока запланированы. После каждого implementation checkpoint — build и узкие релевантные tests; ошибки исправить до перехода дальше.

Зависимости:

- C1 опирается на завершённый B и существующие application ports.
- C2 начинается только после успешного C1.
- C3 начинается после успешного C1; использует Domain/Application, но не дублирует command logic. Для публикации GET endpoints использует authentication/access boundary C2: если C3 начат до завершения C2, можно готовить resolver/query services, но HTTP access boundary не обходить.
- C4 проверяет C1–C3 вместе и начинается после их готовности.
- UI checkpoint D начинается только после успешного C4. Смысл дальнейших UI checkpoints сохраняется.

| Checkpoint | Планируемая работа | Проверяемый выход |
|---|---|---|
| A — analysis + ExecPlan — DONE | Этот документ; inventory; reuse и gap tables | Repository Preflight и domain analysis выполнены |
| B — greenfield foundation + Domain/Application — DONE | Solution, references, test infrastructure; aggregate и 4 use cases; ports | Build: 0 warnings/errors; 17 domain + 12 application tests прошли; state machine сохранена |
| C1 — PostgreSQL persistence | EF Core/provider, DbContext, mappings, initial migration, persistence adapters, atomic version/transactions/events | Реальные PostgreSQL integration tests; полные exit criteria §8.1 |
| C2 — command API + auth/access | Principal → Actor, access adapters, command HTTP endpoints, error/version mapping | HTTP/API integration tests; без frontend/workspace queries |
| C3 — content resolver + read models/workspace queries | Immutable resolver, пять GET endpoints, capabilities, server-side queue/progress projections | No-work student state, исторический statement, корректные permissions и query results |
| C4 — backend contract/integration tests | Совместная проверка C1–C3 | Контракты access/content/queue/pagination/errors/concurrency подтверждены до D |
| D — Shared UI shell, DONE | React/TypeScript/Vite; shared tokens/primitives, shell/sidebar/context, transport, placeholder routes | 49 frontend tests; browser responsive/keyboard/contrast smoke; 187 backend tests; оба build без warnings/errors; §17 |
| E — Course, DONE | C4 CourseWorkspace query/decoder, Student/Teacher, server progress, locked rows, actual context | 83 frontend + 187 backend tests; responsive and live Course API smoke; оба build без warnings/errors; §18 |
| F — Lesson + local Compose — DONE | Assignment-keyed Lesson, server statuses/progress, safe context; PostgreSQL → migrations → API → nginx | 120 frontend / 195 backend tests; live browser; clean bootstrap, persistence/reset; §19, F-RESULTS.md |
| G — Student Solution | Statement над tabs; photo-first; local draft; upload→IDs→submit; comment согласно решению G07 | Text-only/photo-only; empty disabled; no draft attempt; Submitted/Accepted read-only; NeedsRevision feedback+draft; failed submit сохраняет фото |
| H — Student History | Общий workspace, newest attempts, reviews/messages, безопасный renderer | Обе immutable попытки и правильный feedback; сообщения не меняют state; full timeline не дублируется справа |
| I — Teacher Queue | Authorized server filters/search/pagination/summary, current attempts | Submitted only; first/revision на данных больше pageSize; correct totals/empty states |
| J — Teacher Review | Current submission, permissions/version, feedback/actions справа | Optional accept feedback; required revision после G03; 409 отключает stale actions, refetch, no retry; два конкурентных reviewer |
| K — Cache invalidation | Проверить общий cache и keys, не вторую копию status | Submit: student/lesson/course/queue; review: teacher/queue/student/lesson/course; message: student/teacher. Invalidation подключать сразу с mutations, здесь сквозная проверка |
| L — Responsive/a11y | Photo/capture mobile, inline context, teacher tablet, a11y | Все шесть desktop panels; mobile сохраняет смысл/действия; focus errors, pending, keyboard upload и labels |
| M — Integration/E2E | Реальная persistence/authorization + две роли, фото | Feature AT-01–23, integration API→DB→queue/progress; весь обязательный Submit→Revise→Accept путь |
| N — Final DDD review | Проверить aggregate boundaries/atomicity/content refs/auth/no duplication | Итоговый report final §25 с реальными files, reuse, migrations, commands/results/deviations |

### 8.1. C1 — PostgreSQL persistence

Scope:

- EF Core и PostgreSQL provider; DbContext, mappings, initial migration.
- Persistence adapters для существующих application ports: хранение assignment/work, загрузка изолированного aggregate, atomic save. HTTP authentication/access adapters относятся к C2.
- Optimistic concurrency с атомарной проверкой persisted version; защита первого INSERT от дублирования work на assignment.
- Atomic transaction boundary для state/history и transactional persistence новых domain events. События не публикуются до commit и не сохраняются повторно как новые при каждом reload/save.
- Persistence integration tests на реальном PostgreSQL; test double checkpoint B не заменяет эти проверки.

Persistence invariants:

1. Один ProblemWork на один ProblemAssignment.
2. AttemptNumber уникален внутри ProblemWork.
3. Старые Submission immutable.
4. Review относится к конкретной Submission того же work.
5. Stale review не может изменить новую попытку.
6. Optimistic version проверяется атомарно.
7. Первая конкурентная отправка защищена от создания двух ProblemWork.
8. State change + Submission/Review/Message + domain events сохраняются атомарно.
9. Accepted остаётся terminal; persistence не обходит доменные переходы.

Вне C1: HTTP API, authentication middleware, frontend, read/workspace endpoints, review queue UI, course/lesson UI.

**Exit criteria C1 — все пункты обязательны:**

- [x] PostgreSQL поднимается в integration test environment.
- [x] Initial migration применяется к чистой test database.
- [x] Persistence adapters реализуют существующие application persistence ports.
- [x] Unit tests B остаются зелёными: 17 domain + 12 application как принятый baseline.
- [x] Persistence/integration tests используют реальные concurrent transactions и отдельные units of work/DbContexts, а не последовательную имитацию race.
- [x] Race first submit не создаёт два ProblemWork на одно assignment и две успешные первые попытки.
- [x] Stale expectedVersion отвергается без записи изменений проигравшей транзакции; stale review не изменяет новую попытку.
- [x] State/history/events коммитятся атомарно; тест принудительной ошибки подтверждает rollback всех частей.
- [x] Остальные persistence invariants выше подтверждены тестами: immutable submissions, unique attempts, review linkage, terminal Accepted.
- [x] `dotnet build Lms.sln` проходит без warnings/errors; допустимы ранее использованные single-node flags окружения.
- [x] Domain/Application/Infrastructure tests проходят; команды и результаты внесены в отчёт C1.

### 8.2. C2 — command API + authentication/access boundary

Scope:

- Authenticated principal → application Actor; identity/role не брать из request body.
- Authentication/access adapters для student ownership/enrollment/availability и teacher course/group access.
- Command endpoints:

```http
POST /api/v1/problem-assignments/{assignmentId}/submissions
POST /api/v1/problem-works/{problemWorkId}/messages
POST /api/v1/problem-works/{problemWorkId}/reviews/needs-revision
POST /api/v1/problem-works/{problemWorkId}/reviews/accept
```

- Upload endpoint `POST /api/v1/uploads` — только если storage contract готов; иначе явно отметить как deferred, не подменять фиктивным upload.
- Problem Details / error mapping; стабильные codes; HTTP semantics для expectedVersion, concurrency и stale submissions.
- HTTP/API integration tests, включая authentication/authorization и ошибки commands.

Вне C2: frontend, workspace/read queries (кроме минимально необходимых command responses), UI-specific projections. Контроллеры вызывают существующие application use cases; domain transitions не дублируются в HTTP слое.

### 8.3. C3 — Content resolver + read models

Scope: immutable content resolver по CourseVersionId / ProblemContentReference и read endpoints:

```http
GET /api/v1/problem-works/{problemWorkId}
GET /api/v1/teacher/review-queue
GET /api/v1/courses/{courseId}/workspace
GET /api/v1/courses/{courseId}/lessons/{lessonId}/workspace
GET /api/v1/problem-assignments/{assignmentId}/workspace
```

Permissions/capabilities входят в read DTO. Queue filtering/search/pagination/summary выполняются server-side. Student query использует ProblemAssignmentId и возвращает корректное состояние до первой отправки:

```text
problemWorkId = null
status = NotStarted
attemptCount = 0
version = null
```

Content разрешается из назначенной immutable version; запрещённый locked content не выдаётся. Progress — projection/read model: не создавать mutable CourseProgress/LessonProgress aggregate. Query services могут использовать Domain/Application, но не создавать work/attempt и не дублировать command logic. Frontend в C3 не входит.

### 8.4. C4 — Read-model/API contract tests

Совместными contract/integration tests для C1–C3 подтвердить:

- NotStarted task открывается без ProblemWork.
- Student route/read query работает по ProblemAssignmentId.
- Locked lesson не выдаёт запрещённый content.
- Teacher authorization ограничивает доступ соответствующим Course/Group.
- Review queue содержит только Submitted.
- AttemptKind filters работают до pagination.
- Totals/summary рассчитываются по соответствующему набору данных, а не только по текущей странице.
- Historical problem statement остаётся immutable после публикации следующей версии.
- Current submission определяется корректно.
- Student/teacher permissions соответствуют backend access rules.
- API error codes стабильны.
- Concurrency/stale cases корректно представлены через HTTP, без частичных записей.

Успешный C4 — обязательный входной критерий D. Это backend contract checkpoint; browser integration/E2E остаётся в M.

Cache student workspace keyed по assignment ID; queue key содержит все filters/search/page/pageSize. Invalidating cache одного браузера не обновляет другой browser автоматически: проверить существующую refetch-on-focus/navigation стратегию, без введения notification subsystem. Не заменять авторитетные submissions выдуманным optimistic объектом из краткого command response.

Draft identity: student + assignment + nextAttemptNumber. Сохранять его и uploads при submit failure; очищать текущий draft только после подтверждённого успеха. ConcurrencyConflict и stale submission не auto-retry; refetch меняет status/version/actions. Review buttons используют актуальные server permissions и current submission ID.

## 9. Фактические команды и результаты принятого checkpoint B

.NET SDK 10.0.401 установлен локально в `/tmp/lms-dotnet` через официальный `dotnet-install.sh`; системный launcher не имел SDK. `global.json` фиксирует SDK, все backend/test проекты target `net10.0`. В обычной среде использовать `dotnet` с этим SDK. Установка в `/tmp` временная, не часть репозитория.

```sh
dotnet restore Lms.sln
dotnet build Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test tests/Lms.Domain.Tests/Lms.Domain.Tests.csproj --no-build --no-restore -m:1
dotnet test tests/Lms.Application.Tests/Lms.Application.Tests.csproj --no-build --no-restore -m:1
```

Здесь команды выполнены с `/tmp/lms-dotnet/dotnet`:

- Restore: успешно, все 8 проектов.
- Build: успешно, 0 warnings / 0 errors. Первая parallel build завершилась ошибкой MSB4166 (child nodes exited); повторная single-node build прошла.
- Domain: **17 passed**, 0 failed, 0 skipped.
- Application: **12 passed**, 0 failed, 0 skipped.
- `git diff --check`: без замечаний для tracked diff; новые файлы дополнительно проверяются отдельно.

Domain tests: четыре перехода, запрет submit/review в недопустимых состояниях, immutable attempts и attachment snapshots, старые submission, owner/withdrawn assignment, message reply isolation, attachment validation, события и UTC.
Application tests: полный цикл с версиями null/1/2/3, required revision feedback, optional accept feedback, отрицательные access cases, attachment policy, stale/missing version, propagation save conflict без retry, messages без новой попытки, missing entities и role checks.

Это unit tests. StoreSpy — test double для взаимодействий, не concurrency simulator или persistence adapter. Реальная транзакционная атомарность, race двух PostgreSQL transactions, auth middleware, загрузка фото, безопасный renderer и HTTP/E2E пока не проверялись и не заявляются выполненными.

`Lms.Infrastructure.Tests` и `Lms.Api.Tests` созданы как тестовые проекты без фиктивных тестов; пока только собираются. `Lms.Web` — зарезервированный каталог с README, без frontend project/framework. `Lms.Api` — Web SDK project с пустой entry point; не запускает HTTP server и не объявляет endpoints.

## 10. Изменённые файлы и результат checkpoint B

- `Lms.sln`, `global.json`, `Directory.Build.props`, `.gitignore`, `README.md` — foundation и воспроизводимые команды.
- `src/Lms.Domain/Lms.Domain.csproj`, `ProblemAssignment.cs`, `History.cs`, `ProblemWork.cs` — чистая модель.
- `src/Lms.Application/Lms.Application.csproj`, `Ports.cs`, `ProblemWorkService.cs` — use cases/access/store contracts.
- `src/Lms.Infrastructure/Lms.Infrastructure.csproj` — пустая граница будущих адаптеров.
- `src/Lms.Api/Lms.Api.csproj`, `Program.cs` — проект без HTTP реализации.
- `src/Lms.Web/README.md` — frontend отложен.
- Четыре `tests/Lms.*.Tests/Lms.*.Tests.csproj`; `ProblemWorkTests.cs`, `ProblemWorkServiceTests.cs` — test infrastructure и unit tests.
- Этот ExecPlan — greenfield decision, архитектура, scope и отчёт.

DDD review: Domain не имеет project/package dependencies; Application зависит только от Domain. State transitions находятся в ProblemWork, Submission/Review не имеют setters или самостоятельных публичных constructors. Read-only collections не выдают mutable lists, входные attachment IDs копируются. Assignment content reference неизменяем. Authorization и atomic persistence — явно выделенные application ports. Нет EF/ASP.NET/UI зависимостей в Domain. Нет лишних агрегатов, repositories с business rules, HTTP endpoints, database migrations или UI implementation.

Минимальные ограничения: модель assignment содержит только нужный для slice identity/ownership/content/status; поля scheduling/progress и workflows authoring/enrollment не реализованы. Контракт событий пока in-memory внутри aggregate; следующий persistence checkpoint должен сохранять только новые события атомарно и не публиковать их до commit. Текущий API host не предоставляет аутентификацию; Actor будет строиться доверенным адаптером из principal, а не из JSON.

- [x] G01 закрыт: пользователь подтвердил greenfield.
- [x] Solution, project references и test infrastructure созданы.
- [x] Domain/Application только разрешённого slice реализованы.
- [x] 29 unit tests прошли; вся solution собирается.
- [ ] PostgreSQL persistence/migrations — отложены.
- [ ] HTTP API/auth adapters/read models — отложены.
- [ ] Frontend и шесть экранов — отложены.
- [ ] Integration/E2E — отложены.

**Checkpoint B завершён и принят.** Результаты build и 29 unit tests выше относятся к запуску B; в текущем documentation-only запуске тесты повторно не запускались.

## 11. Текущее обновление плана и open decisions

Изменён только `codex/EXECPLAN-LMS-SIX-SCREENS.md`: C разделён на C1–C4, добавлены зависимости, schema/migration policy и exit criteria C1, уточнены G03/G07. Уже выполненные решения B, state machine, 17 domain + 12 application tests и нефункциональные заготовки Infrastructure/API/Web сохранены.

Оставшиеся решения перед соответствующими этапами:

- **G03 CLOSED в C1.5:** non-empty revision feedback — глобальный domain invariant по решению пользователя; Domain Spec и implementation синхронизированы.
- **C1:** зафиксировать EF mappings, concurrency token/atomic save, формат и lifecycle persisted domain events и PostgreSQL test environment; выбрать технические решения в границах существующих ports/invariants.
- **C2 / G04 / G06:** способ authentication и источник access facts, wire semantics expectedVersion и стабильный Problem Details envelope; готовность upload/storage contract.
- **C3 / G05 / G09 / G15 / G16:** допустимые locked metadata, формулы progress (включая пустые/optional/withdrawn assignments), filters для summary/facets, teacher CourseWorkspace без фиктивного enrollment.
- **C3 / G08 / G10 / G11:** server/local hasDraft boundary, источники optional content metadata/hints, представление work messages относительно attempts.
- **G07:** planning recommendation зафиксирована — bodyMarkdown с optional explanation label, attachments, без отдельной comment entity и скрытого POST Message; реализовать только на будущем UI checkpoint.

На этом запуске запрещены изменения production code, Domain/Application implementation, добавление EF Core packages, migrations, API endpoints, frontend и изменения state machine. **После краткого diff summary и списка open decisions остановиться; C1 не начинать.**

## C1 implementation decision (2026-10-07)

Текущий запуск разрешает C1; ограничения предыдущего documentation-only запуска исторические. Обнаружена materialization incompatibility: EF не может связать constructor ProblemWork(ProblemAssignment), owned Content в constructor assignment и несовпадающие параметры constructors history с mapped properties. До изменения Domain фиксируем минимальное решение: private parameterless constructors только для materialization; публичные constructors, invariants, state machine и Application прежние. Get-only properties mapped через backing fields. Reload имеет пустой список новых events. PostgreSQL + EF Core 10, append-only history, bigint Version conditional UPDATE, unique assignment INSERT, transactional event journal без publisher. Attachments — snapshot uuid[] существующих IDs; metadata/storage вне C1. G03 открыт.

## 12. Фактический результат C1 (2026-10-07)

C1 выполнен на базе 2124a15d10746a786278b9dd52b2d8edabcc36df. Все exit criteria §8.1 закрыты.

- EF Core 10.0.12 / Npgsql provider 10.0.3; PostgreSQL 17.6 в Testcontainers 4.15.0.
- Initial migration `20261007035847_InitialPersistence`: ProblemAssignments, ProblemWorks, Submissions, Reviews, Messages, DomainEvents. PK/FK/unique/check constraints, append-only history triggers и immutable assignment reference. Attachment IDs — uuid[] snapshots.
- `PostgresWorkStore` реализует существующий IWorkStore: isolated single-query aggregate load, conditional version UPDATE / unique first INSERT, history/events в одной transaction. Stable event IDs предотвращают повторную запись, включая fresh store; внешней публикации нет.
- Только private materialization constructors добавлены в Domain по заранее записанной incompatibility. Application, state machine и существующие tests не изменены. G03 остаётся OPEN.
- SDK /tmp/lms-dotnet: 10.0.401, global.json соответствует. Restore успешен; build всей solution — **0 warnings / 0 errors**.
- **17 Domain + 12 Application + 10 Infrastructure tests passed**, 0 failed, 0 skipped. Реальный PostgreSQL; migration на пустых БД; обе concurrent races используют отдельные DbContexts/transactions с barrier после BeginTransaction. Rollback fault injection происходит после SQL writes до commit.
- Дополнительно подтверждены immutable submission/attachment snapshots, cross-work FK protection, stale review после новой попытки, optional Domain feedback, required Application feedback, Accepted terminal для submit/review и разрешённое message без смены статуса, event deduplication.
- Команды: `/tmp/lms-dotnet/dotnet restore Lms.sln`; build `--no-restore -m:1 -p:UseSharedCompilation=false`; отдельные `dotnet test` Domain/Application/Infrastructure csproj `--no-build --no-restore -m:1`. `git diff --check` успешен.

Полный отчёт по всем 11 пунктам prompt, package/file list, schema constraints, scenarios, воспроизведение и ограничения: [C1-RESULTS.md](C1-RESULTS.md). README обновлён. Sandbox mountinfo failure обойдён только через одобренные escalation calls; первые dependency audit/version conflicts устранены обновлением/выравниванием packages, без suppressions.

**Остановка после C1.** C2/C3/C4, HTTP/auth, workspace/read queries и frontend не начаты. Дальнейшие открытые решения остаются в §11; C1 из этого списка выполнен. Исторические пометки B «persistence отложен» больше не описывают текущий статус.

## 13. C1.5 — Domain hardening (2026-10-07)

Scope: только аудит EF materialization constructors и закрытие G03; C2 не начинать. Исторические упоминания optional Domain feedback и OPEN G03 в B/C1 отражают состояние тех checkpoint и заменены текущим решением ниже.

Проверены пять C1 constructors: `ProblemAssignment()`, `ProblemWork()`, `Submission()`, `Review()`, `Message()`. Все private; EF успешно materialize их в PostgreSQL tests C1. Изменения constructors не требуются и не внесены. Domain entities/children имеют get-only или private setters; read-only collections не выдают mutable lists. Internal constructors children существовали до C1 и вызываются только aggregate внутри Domain, InternalsVisibleTo отсутствует. Infrastructure не получает public/internal ORM factories или setters: EF materialization использует backing fields; обычный API создания/переходов остаётся domain-controlled.

Решение G03: RequestRevision требует непустой feedback глобально. Проверка Domain находится до AddMessage/Review/status/version/events; ошибка DomainException(FeedbackRequired). Application уже выполняет раннюю проверку с UseCaseException(FeedbackRequired), поэтому production Application не меняется. Accept optional feedback сохранён. Persistence production code/schema/migration не меняются; тест event deduplication минимально адаптирован к обязательному feedback и дополнительному MessagePosted.

Документы: Domain Spec §21, Feature UC-A04, README и этот ExecPlan. Исторический C1-RESULTS.md остаётся отчётом C1, а не текущим feedback contract.

Проверки: Domain theory null/empty/spaces/tab-newline и полная неизменность aggregate с существующей history; valid revision создаёт linked message/review/events; Accept без feedback. Application theory проверяет раннюю ошибку и отсутствие mutation/save. Фактические результаты:

- Full solution build: **0 warnings / 0 errors**.
- Domain: **24 passed**, 0 failed/skipped (раньше 17; optional-revision test заменён required-feedback cases).
- Application: **15 passed**, 0 failed/skipped (раньше 12; feedback theory расширена до четырёх inputs).
- Infrastructure: **10 passed**, 0 failed/skipped, реальный PostgreSQL 17.6 в Testcontainers; migration/roundtrip/races/rollback/event deduplication сохранены.
- `git diff --check`: успешно. Constructors и production Application/Infrastructure не изменены.

Команды (SDK 10.0.401, соответствует global.json):

```sh
/tmp/lms-dotnet/dotnet build Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false
/tmp/lms-dotnet/dotnet test tests/Lms.Domain.Tests/Lms.Domain.Tests.csproj --no-build --no-restore -m:1
/tmp/lms-dotnet/dotnet test tests/Lms.Application.Tests/Lms.Application.Tests.csproj --no-build --no-restore -m:1
/tmp/lms-dotnet/dotnet test tests/Lms.Infrastructure.Tests/Lms.Infrastructure.Tests.csproj --no-build --no-restore -m:1
git diff --check
```

**C1.5 завершён; G03 закрыт. Остановка до C2.** API/auth/read models/frontend не изменялись.

## 14. C2 — implementation decisions (2026-10-07)

Авторизован только C2. Ветка `codex/c2` перенесена из чистого worktree 7cff в 88c7 и fast-forward до C1.5 `43957ff`. Baseline: Domain 24, Application 15, Infrastructure 10 passed на реальном PostgreSQL.

До implementation: Domain/state machine и PostgresWorkStore compare-and-save не меняются. Добавляются только access facts в PostgreSQL: active student/course enrollment с optional group, published/scheduled lesson по course/version/lesson, teacher grant на course либо group, attachment ownership/eligibility metadata. Это входы существующего IAccessPolicy, без enrollment CRUD, новых domain aggregates или workspace projections. Отсутствующие facts запрещают доступ.

Authentication: стандартный JWT Bearer с проверкой signature/issuer/audience/lifetime через настраиваемый HTTPS authority; конкретный IdP не выбирается. Заменяемый IActorMapper принимает только authenticated identity с единственным UUID sub и единственной role Student/Teacher. Test authentication существует только в test assembly. Нет actor IDs из body/query/custom headers в production.

Upload decision B: upload/storage deferred. Production metadata не заполняется фиктивными ID; тесты seed metadata только в своих изолированных БД. Будущий trusted upload producer обязан завершить storage/file validation до выставления eligibility. Сейчас text-only flow полноценный, неизвестные attachments fail closed.

HTTP: submit 201, review/message 200; malformed 400 ValidationFailed, semantic 422, unauthenticated 401, access 403 Forbidden, missing 404 NotFound, workflow/concurrency 409. Feature-specific access/not-found aliases нормализуются в стабильные Forbidden/NotFound по C2 request. Missing version для существующего work остаётся 409 ConcurrencyConflict (existing Application semantics); review expectedVersion обязателен на wire, missing/null = 400 ValidationFailed. Единый Problem Details с code и traceId, включая framework failures. G03 закрыт; no retries.


### C2 — verified results and exit criteria

- [x] Реальный ASP.NET Core host; Kestrel Production smoke: 401 Problem Details без token.
- [x] Четыре command endpoints работают через real PostgreSQL; thin HTTP boundary.
- [x] Principal → Actor; настоящий JWT Bearer validation и test-only scheme; нет identity из request.
- [x] Student/teacher course/group authorization, enrollment/lesson availability и attachment ownership; negative paths covered.
- [x] Единый Problem Details, including framework errors; stale version = 409 ConcurrencyConflict, no retry.
- [x] Два HTTP requests синхронизированы на начале двух real PostgreSQL transactions: first-submit/review races проверены.
- [x] Full build: 0 warnings/errors. Domain 24 + Application 15 + Infrastructure 10 + API 50 = **99 passed**, 0 failed/skipped.
- [x] Domain/Application/PostgresWorkStore/initial migration C1 без diff; G03 required feedback сохранён.
- [x] Migration 20261007090442_CommandAccessFacts только добавляет 4 access tables; pending model changes отсутствуют.
- [x] Upload B deferred явно, без fake production IDs; README и report с 14 пунктами обновлены.

G04/G06 закрыты на уровне C2 transport/access boundary. Остаются deployment выбор IdP и provisioning
trusted facts; если внешний subject не internal UUID, требуется trusted IActorMapper lookup.
Upload/storage claiming/cleanup остаются отдельной dependency. Authorization проверяет facts на момент
use case; синхронизация administrative revoke с in-flight transaction не вводилась и не меняет C1 semantics.

Подробности DTO/status mappings, tests и воспроизведение: `codex/C2-RESULTS.md`, README.
**C2 завершён. Остановка: C3/C4/read models/workspaces/frontend не начинались.**

## 15. C3 — decisions before implementation (2026-10-07)

Only C3 authorized; branch `codex/c3` from accepted C2 merge `0b29654`.
Baseline: build 0 warnings/errors; Domain 24, Application 15, Infrastructure 10, API 50 passed.
No Domain/command-side changes. Existing schema has no published content source: historical
resolution cannot be implemented from access facts alone. Add one immutable published-version
snapshot table (JSONB compiled student-safe course/lesson/problem content, exact UUID identity,
course/version uniqueness, source revision). This is content storage, not a UI projection table.
Protect the entire snapshot against UPDATE/DELETE; no authoring/compiler/publication API in C3.
Trusted provisioning supplies already compiled snapshots. Resolver never falls back to latest.
Course/lesson catalogs use provisioned versioned lesson access facts, retaining versions referenced
by assignments. Multiple lesson versions appear explicitly with CourseVersionId; lesson route
combines their assignment rows, resolving each exactly. Final projection groups course rows by
stable LessonId (version IDs retained), so publication does not double-count course progress. No invented enrollment/version binding.

G08: no server hasDraft; browser-local only. G16: teacher enrollment null, separate context.
G09: all active assignments required; accepted/active for lessons; completed/available for course;
empty lesson is NotStarted, percent 0. Teacher progress is scoped to accessible assignments.
Locked student lesson/problem => 403, course rows only safe metadata; inactive/withdrawn => 403.
G15: queue summary uses all filters including attemptKind/search before pagination.
User profile/group labels, hints, materials, events and attachment download metadata lack sources:
null/[]; preserve attachment IDs. No new persistence just for these fields. G07 unchanged.

### C3 — verified results and exit criteria

- [x] All five authenticated GET endpoints implemented with C2 actor/access boundary.
- [x] Exact immutable content resolution; old/new publication and DB mutation protection verified.
- [x] Student NotStarted/no work, no GET writes; assignment-keyed navigation.
- [x] Real student enrollment, teacher enrollment null and separate grant context.
- [x] Derived progress; all active assignments required, zero safe, logical lesson identity across versions.
- [x] Student unavailable content returns 403; course metadata redacted; mixed versions cannot leak locked statements.
- [x] SQL queue Submitted-only, filters/search/page/summary; same filtered dataset and repeatable-read snapshot.
- [x] Server permissions, current submission, deterministic immutable history/review/message linkage.
- [x] Domain, command Application and C1/C2 store/access policy unchanged.
- [x] Full build 0 warnings/errors; 24 Domain + 15 Application + 10 Infrastructure + 78 API = 127 passed (99 existing + 28 new), no failures/skips.
- [x] G08/G09/G16 decisions synchronized minimally in read contract; provisioning/profile/storage gaps recorded.

Full 13-part report: `codex/C3-RESULTS.md`. C4 and frontend not started; stop after C3.


## 16. C4 — pre-implementation contract audit (2026-10-07)

Scope: backend contract/integration only. Base accepted C3 merge `16ee5c1`.
Branch `codex/c4-contract`: `codex/c4` already belongs to worktree 88c7 at C2;
that worktree/branch is preserved. Baseline full build: 0 warnings/errors;
Domain 24 + Application 15 + Infrastructure 10 + API 78 = 127 passed, no skips.

Discrepancies recorded before edits; source of truth is accepted C2/C3 decisions,
Domain/Feature invariants and the C3 read-boundary amendment, not older UI sketches:

- D01 / G06: Frontend §3.6 calls `{code,message}` the transport envelope, while C2
  implements RFC Problem Details `{type,title,status,instance,code,traceId}`. Clarify
  transport versus future client adapter; retain stable C2 statuses/codes.
- D02: Frontend SubmitSolutionRequest omits expectedVersion; C2 and Flow require
  null/omitted on first submit and the latest read version on resubmit. Fix the example.
- D03: Frontend §4.2 says every teacher command needs a version, but Feature UC-A06
  and accepted C2 PostMessage have no expectedVersion or response version. Document
  the exception: internal compare-and-save, 409 on concurrent change, fresh GET for version.
- D04 / G11: attempt view sketch suggests per-attempt general messages, while messages
  belong to work. Only Review.SubmissionId/MessageId links feedback to an attempt;
  preserve separate work discussion without inventing message SubmissionId.
- Older rich read DTO sketches differ from accepted C3 (nullable profile/storage fields,
  nested lesson metadata, attachmentIds, teacher enrollment=null). Make C3/C4 wire
  precedence explicit and freeze actual JSON property sets in HTTP regression tests.
- SubmissionAlreadyReviewed is a defensive Domain guard unreachable through valid
  persisted workflow (status/current-submission guards win). Test middleware mapping
  separately; do not fabricate corrupt persisted work or alter state-machine precedence.
- OpenAPI is not configured (no package/registration/document route). Track technical
  gap separately; no documentation subsystem or production test-only routes.

Add complete HTTP/PostgreSQL flow, wire/error/auth matrices, capability-command checks,
message concurrency, and stronger filtered pagination/progress regressions. Reuse existing
historical-content tests unchanged. No confirmed Domain/command/persistence defect so far.


### C4 — verified results and exit criteria

- [x] Baseline C3: full build 0 warnings/errors; all original 127 tests passed before implementation.
- [x] All 4 command and 5 read contracts checked; wire DTOs/statuses/errors stable.
- [x] Full HTTP + real PostgreSQL flow: NotStarted → Submit → Revision → Resubmit → Accept.
- [x] NotStarted GET writes no work/submission/event; versions and immutable history verified.
- [x] Authentication and student/teacher course/group authorization matrices, same-course isolation.
- [x] Publication A before/after B, exact old teacher content, existing DB immutability tests retained.
- [x] Queue filters before pagination, full filtered summary, latest attempt and reviewed exclusions.
- [x] Accepted-only derived progress, empty/partial/full/withdrawn/unavailable/revision cases.
- [x] True capability → valid command succeeds under same access/state; message version/race checked.
- [x] Full build: 0 warnings/errors; Domain 24 + Application 15 + Infrastructure 10 + API 138
  = **187 passed**, 0 failed/skipped (127 unchanged existing + 60 new cases).
- [x] No production/Domain/Application/persistence/migration changes; no existing tests altered.
- [x] D01–D05 documentation discrepancies fixed; G06/G08/G09/G11/G15/G16 backend boundaries closed.
- [x] OpenAPI absent: G18 deferred. Upload deferred and required before Student photo submit flow.

Final report: `codex/C4-RESULTS.md` (14 requested sections). G03 remains CLOSED;
G07 remains Submission bodyMarkdown + attachments. C4 complete; **stop before D/frontend**.

## 17. D — decisions before implementation (2026-10-07)

Only checkpoint D is authorized. Branch `codex/d-ui-shell` created from exact
`b12cc2b` (initial worktree HEAD was C4 merge c1e72bd). Backend baseline before
implementation: build 0 warnings/errors; 24 Domain + 15 Application + 10 Infrastructure
+ 138 API = 187 passed, no failures/skips. Logs: /tmp/lms-d-baseline-{build,tests}.log.
Read ExecPlan, C4 report, Frontend contract, Flow, Feature and inspected the actual
1536×1024 six-screen PNG. No AGENTS.md or existing frontend/test infrastructure found.

Framework is not prescribed (React Query is illustrative only). Choose React + strict
TypeScript + Vite, React Router for route matching/links, plain CSS tokens, Vitest +
Testing Library/jsdom for component/API tests. No UI design system, cache library,
feature DTO scaffolding or domain state machine. Pin dependencies with a pnpm lockfile.
Use Node 24 runtime available in this environment; document portable pnpm commands.

Implement reusable AppShell, MainContent, SidebarNavigation, ContextPanel/ContextSection,
Breadcrumbs, PageHeader, keyboard Tabs, Button/AsyncButton, StatusBadge,
AvailabilityBadge, ProgressBar, EmptyState/ErrorState/LoadingSkeleton/InlineError.
Single central status presentation map and tokens; no feature-specific components.
Desktop >=1200px: left/main/right grid. Tablet 768–1199px: compact navigation and
collapsible context below workspace. Mobile <768px: disclosure navigation and always
inline context after main; preserve a single mounted context tree across viewport changes.

Five requested route patterns plus assignment ?tab=history use placeholders only.
A clearly labelled shell demonstration at / provides navigation and primitive examples;
role preview is presentation-only, never authentication. API foundation: injected bearer
provider, same-origin /api/v1 transport, validated Problem Details/ApiError, decoder-based
typed success boundary, cancellation, no automatic retries or feature requests.
Backend contracts/domain/persistence/API remain unchanged. E–N are not authorized.
Verification: component/API tests, full frontend build, real-browser responsive/keyboard
smoke (existing bundled browser tooling, no heavy visual-regression installation), then
full backend regression and build. Record actual results and remaining gaps here and
in codex/D-RESULTS.md; stop after D.


### D — actual reuse and verified results

Section 4's original Create* inventory is historical. Created once and ready for reuse:
AppShell, MainContent, SidebarNavigation, ContextPanel/ContextSection, Breadcrumbs,
PageHeader, Tabs, Button/AsyncButton, StatusBadge/AvailabilityBadge, ProgressBar,
EmptyState/ErrorState/LoadingSkeleton/InlineError and Section. Status mapping is central;
future feature screens compose these components. AttachmentUploader, SubmissionView,
AttemptTimeline, MessageThread, Markdown renderer and review components remain deferred.

Final compatible pinned toolchain: React/DOM 19.3.0, TypeScript 7.0.2, Vite 7.3.7,
React Router DOM 6.30.3, Vitest 4.0.18, Testing Library React 16.3.3 / user-event 14.6.7 /
jest-dom 6.9.1, jsdom 30.1.2, pnpm 11.19.0, Node 24.19.0. Initial latest dependency
combination had incompatible test types and a router client-directive build warning;
resolved by compatible version pins, not skipLibCheck/warning suppression. Router future
flags explicitly opt into transition updates/relative splat behavior. No UI framework.

- [x] Frontend production build (strict tsc + Vite): no warnings/errors.
- [x] 49 frontend tests passed: 14 shell/navigation/routes, 15 primitives/a11y, 20 transport.
- [x] Desktop 1440×1000: left 216px / flexible main / right 288px; one context landmark.
- [x] Tablet 1024×900 and 768×1024: compact sidebar and collapsible context below main.
- [x] Mobile 390×844 and 320×720: compact Menu and inline context after main, no overflow.
- [x] Actual tablet-collapse → mobile → desktop preserves context DOM and control state.
- [x] Six placeholder views × desktop/mobile = 12 direct-route browser checks; no API requests.
- [x] Keyboard tabs/arrows, skip link, visible focus, Menu/Escape; semantic labels retained.
- [x] All nine measured token text pairs exceed 4.5:1 (minimum 4.92:1).
- [x] Desktop/mobile screenshots inspected against reference; clean browser console.
- [x] Final backend build: 0 warnings/errors. 24 + 15 + 10 + 138 = 187 passed, no failures/skips.
- [x] Backend production/contracts/persistence/migrations/tests have zero diff from b12cc2b.

Commands and evidence: frontend README; /tmp/lms-d-frontend-{build,tests}.log,
/tmp/lms-d-{build,tests}.log, /tmp/lms-d-browser/results.json and screenshots. Browser
smoke is optional standalone Playwright using the bundled runtime and installed Chrome;
no heavy visual infrastructure or committed image baselines. The sandbox launcher still
fails with bubblewrap mountinfo; approved escalated commands were used, not app workarounds.

Remaining gaps: feature screens/queries, centralized feature DTO decoders, real session/IdP
integration, upload/storage/downloads, safe Markdown/LaTeX, local drafts/history/review,
cache invalidation, deployed SPA/API routing and complete workflow E2E. Existing unsourced
profile/material metadata and OpenAPI G18 remain deferred. None is implemented as fake
business data in D. D complete; **stop here, E/F/G and other checkpoints not started**.

## 18. E — decisions before implementation (2026-10-07)

Only Course screen authorized; branch `codex/e-course` from exact `6b4df3a`.
Read current ExecPlan/D/C3/C4 reports and Course/availability/responsive sections of
Frontend/Flow; the previously inspected unchanged six-screen PNG remains visual source.
Baseline passed: frontend build without warnings/errors, 49 frontend tests, full backend
build without warnings/errors and 187 backend tests. No backend changes authorized/needed.

Use D shell/primitives/tokens, one course feature with its own query/hook/components/CSS.
Central API/contracts CourseWorkspace type plus runtime decoder mirrors ReadModels.cs:
nested lesson/progress, nullable titles/publishedVersionId/profile/enrollment, teacherContext,
courseVersionIds, materials; no earlier flat CourseLessonRow sketch. Progress values are
rendered from server, not recomputed. Teacher progress is explicitly course/group scoped.
Use AvailabilityBadge (not ProblemWork StatusBadge for derived lesson states). A local
lesson presentation map uses existing semantic token classes; no new global status enum.

Context: one server progress section, optional real teacher/materials only. No events field,
course-level status/availability, designated teacher/profile source or material provider
exists in C4; omit unsourced sections, never manufacture rows. No decorative empty cards.
Locked rows use isAvailable, show server reason/date and have no link/protected summary.
Course-to-Lesson React Router navigation targets the existing placeholder only.

User confirmed no OIDC settings exist. Preserve D auth-provider boundary: a documented
trusted host runtime adapter supplies current bearer tokens and handles 401; absent adapter
sends anonymous request and renders login-required state, never fake auth or hardcoded
credentials. No new IdP selection/login framework. Same-origin /api proxy configurable for
local dev/preview; production must route API before SPA fallback. Verify provider wiring
with HTTP fixtures and real unauthenticated API; deployed IdP login remains a named gap.

Query: loading/success/error, AbortController cleanup + stale-response exclusion, manual GET
retry, no global cache or command changes. Shell remains visible in all states. Tests cover
Student/Teacher, C4 shape, errors, cancellation/races, progress, optional data, locked rows,
navigation/a11y; browser smoke covers actual layouts and HTTP-controlled response states,
clearly distinguished from backend live smoke. Finish backend regression/build and report;
stop after E, no F or later checkpoints.


### E — verified results and exit criteria

- [x] Course route calls the existing same-origin CourseWorkspace endpoint via D client.
- [x] Central C4 DTO + decoder; nested lesson/version IDs, nullable metadata/enrollment,
  teacherContext and materials match actual records; no backend changes/discrepancies requiring fixes.
- [x] Student enrollment and Teacher enrollment=null supported; Teacher progress scoped wording.
- [x] Server progress/counts preserved; 0/partial/100/zero available cases verified, no recomputation.
- [x] Available lesson Router links; locked rows no link/protected summary, reason/date retained.
- [x] D shell/tokens/primitives reused; one progress panel, only actual teacher/materials,
  no invented events, course-level availability/status, instructor identity or data cards.
- [x] Loading/error/empty UI keeps shell; 401 callback, 403/404, network/server retry, cancellation,
  stale-response guard and runtime invalid-response boundary verified.
- [x] 83 frontend tests passed (49 baseline cases retained/adapted + 34 Course cases).
- [x] Browser smoke at 1440/1024/768/390/320px: normal/empty/30 rows/unbroken titles,
  locked rows, Teacher and same DOM context restored on mobile; no horizontal overflow.
- [x] Course state browser checks: held loading, 401/403/404/503, network and explicit retry;
  keyboard navigation goes only to Lesson placeholder with main focus; screenshots inspected.
- [x] Real Course API smoke: unchanged API + real Bearer handler + isolated PostgreSQL,
  actual browser client/proxy/decoder/rendering. Student 200, Teacher 200/enrollment=null,
  anonymous 401, foreign Student 403. Temporary test-only signing config/HTTP bridge outside repo.
- [x] D shell browser regression remains green for root/primitives and five non-Course placeholders.
- [x] Frontend strict typecheck/Vite and full solution build: no warnings/errors.
- [x] Backend 24 Domain + 15 Application + 10 Infrastructure + 138 API = 187 passed, no skips.
- [x] Backend production/contracts/tests/persistence/migrations have zero diff from 6b4df3a.

Evidence: /tmp/lms-e-{frontend-build,frontend-tests,build,tests}.log,
/tmp/lms-e-browser/results.json and screenshots, /tmp/lms-e-shell-browser/results.json,
/tmp/lms-e-live/host.log. Temporary harness /tmp/lms-e-live/Smoke.cs references existing
ApiFactory and uses its real migrations/PostgreSQL, overriding only IdP signing config
like the established BearerAuthenticationTests; no fake success endpoint or production seed.
No command/workflow E2E, new package/global cache/UI kit introduced.

Contract gaps retained: no events/course availability/status; profiles/designated teacher/
grade/materials have no current source; null/[] are normal. Teacher ID grants are not a
teacher profile; strings/UUID arrays/nulls match C4. OpenAPI G18 unchanged. User confirmed
no OIDC settings; window.lmsAuth is the trusted host integration boundary, not an IdP or
login implementation. Without it the real endpoint yields login-required 401. The host
must provide live tokens/session handling and remount on identity changes; deployment
must proxy API before SPA fallback. Live smoke proves signed test-token validation and
Course rendering, not production provider login/discovery. No blocker for Course rendering.

Full 17-part report: codex/E-RESULTS.md. **E complete; stop, F and later not started.**


## 19. F — Lesson + reproducible local Compose (2026-10-07)

Authorized scope: only F, branch `codex/f-lesson` from exact `959a4ef`. Baseline verified
before implementation: 83 frontend tests, 187 backend tests, frontend and full solution builds
with zero warnings/errors. Read E/C3/C4 reports, current contracts/flow/README and six-screen
PNG. The normal sandbox launcher fails with mountinfo; approved escalated execution is used.

Implementation decisions: reuse D AppShell/context/primitives and E client/query/decoder pattern.
C4 ReadModels.cs is the wire source of truth, not the earlier §7 sketch. Lesson uses nested
lesson, contentVersions, availability, progress.totalAssignedProblems/percent, assignment rows
with studentId/work/permissions and materials. No contract defect or backend DTO change needed.
No protected content on restricted payload; actual student locked endpoint returns 403.

Lesson has no teacherContext/enrollment/role marker. Render actual row work status/progress
without inferring role from absence of canSubmit; neutral shell navigation avoids fake student
identity. Multiple-student rows retain their source student IDs, since profile names are absent.
Materials are DTO-compatible but currently empty from backend. Dates only from availability;
no invented deadline, hints, profile, teacher solution or events. One server progress panel.

Docker: PostgreSQL 17.6 named volume + health, one-shot EF MigrateAsync and atomic deterministic
Development-only NotStarted seed, API runtime + health, nginx static web + /api reverse proxy +
SPA fallback. Multi-stage builds. No new application architecture, Domain/Application/read or
persistence semantic changes. API host changes limited to migrations/seed CLI, gated local JWT
signing/token CLI and database readiness. No anonymous login: manually paste CLI-issued bearer
in Compose-only local login host adapter. Production/Staging never enable that issuer/key/seed.
Production web target excludes this adapter. Default credentials are explicitly local-only.

Verification and final exit checklist are recorded below after completion; G onward remain deferred.


### F — verified results and exit criteria

- [x] Real LessonWorkspace via D/E API/query foundation; exact C4 decoder and response IDs.
- [x] Course → Lesson → assignment placeholder; null work, keyboard navigation and main focus.
- [x] Four canonical statuses, server attempts/progress, safe optional materials and empty states.
- [x] Restricted/403 content handling; no Course-data bypass; source/teacher gaps documented.
- [x] One ContextPanel DOM after main on mobile; no duplicated large progress or problem list.
- [x] 120 frontend tests, strict frontend build without warnings/errors.
- [x] Browser controlled states at 320/375/768/1024/1440; no overflow; screenshots inspected.
- [x] Real container API/PostgreSQL browser Student/Teacher vertical slice and route refresh.
- [x] D/E browser regression remains green; API 401/403/404 preserve Problem Details through nginx.
- [x] Compose clean build/up: PostgreSQL healthy → three EF migrations → API healthy → web healthy.
- [x] Runtime SDK absent; web runtime only static artifacts/config; separate production target excludes local auth.
- [x] Explicit Development-only bearer CLI/manual login; production/staging/auth boundary tests.
- [x] Atomic idempotent NotStarted seed, no works; down/up retains data, down -v removes volume,
  second clean startup re-applies all migrations and restores demo successfully.
- [x] 195 backend tests (all prior 187 + 8 local auth/security cases); full build 0 warnings/errors.
- [x] Domain/Application/Infrastructure/DTOs/migrations unchanged. API-only infrastructure changes listed.
- [x] Root README sufficient for Docker-only startup, auth, URLs, logs, stop/restart/reset.

Evidence and all 29 report points: `codex/F-RESULTS.md`. Teacher unavailable-content/role distinction,
material/profile sources and production IdP remain explicit enrichment/integration gaps, not fabricated
client state. **Stop after F. No G/upload/History/Teacher Queue/Review implementation.**


## 20. U1 — Attachment Upload & Storage (complete, 2026-10-08)

Authorized base `4d1400d`, branch `codex/u1-attachments`. Baseline: 120 frontend,
195 backend tests pass; both builds have zero warnings/errors; Compose startup healthy.
No G screens or ProblemWork state/Submit/Review semantics changes.

Pre-implementation decisions: preserve the documented POST `/api/v1/uploads` route.
U1 response uses the requested mediaType/size/createdAt names, superseding the older
recommended contentType/sizeBytes sketch. JPEG/PNG only, originals preserved; 10 MiB
configurable default. Protected content and pending delete under `/api/v1/attachments`.
Storage port belongs in Application; filesystem adapter and metadata in Infrastructure.
StoredAttachment is distinct from immutable ordered Submission.AttachmentIds.
Claim metadata in the same PostgreSQL transaction as new history. Existing Message
attachments must also be claimed to avoid deleting a file referenced by message history.
This is persistence protection of the existing contract, not a new message feature.

Uploaded → Attached; pending delete marks a technical Deleted tombstone before physical
delete, so a concurrent submit cannot claim it. Failed physical deletion is retryable.
File write precedes metadata commit, with best-effort compensation on database failure.
There is no filesystem/database distributed transaction. Crashes can leave unreferenced
files; future cleanup may remove old pending/tombstoned/unreferenced files after a grace
period. No scheduler in U1. Attached originals and ownership cannot change.


U1 verified exit checklist:
- [x] Real JPEG/PNG multipart upload, owner-derived metadata, protected content and pending delete.
- [x] Local storage port/adapter; originals unchanged; random safe keys; configured 10 MiB default.
- [x] Additive AttachmentStorage EF migration; F upgrade and clean four-migration bootstrap.
- [x] Uploaded/Attached/technical Deleted lifecycle; ownership/metadata/reference immutability.
- [x] Atomic PostgreSQL claims for ordered Submission snapshots and existing Message attachments.
- [x] Reuse, foreign access, concurrent claim/delete and failure rollback/compensation tested.
- [x] 225 backend tests (195 baseline + 30 U1), zero failures/skips.
- [x] 143 frontend tests (120 baseline + 23 U1), zero failures/skips.
- [x] Frontend and full solution builds: zero warnings/errors.
- [x] Real Compose browser uploader smoke, keyboard reorder, mobile preview and cross-user checks.
- [x] API restart/down-up preserve original hash; down -v removes DB and attachment volumes.
- [x] README, Feature/Frontend amendments and 26-part U1 report updated.

Final refinements: confirm DB metadata absence before compensating an uncertain upload commit;
otherwise retain original for later reconciliation. Deleted tombstones block claim before physical
removal and allow retry. Existing positive attachment test fixtures now perform real uploads.
One read-projection fixture formerly reused a submission ID in a message; it now uploads a separate
message photo, retaining all projection/history/status assertions. Metadata-only legacy records
cannot authorize new submissions. Historical ID arrays and C1 migration are unchanged.

Evidence and all 26 requested report items are in `codex/U1-RESULTS.md`. The dev harness has no
Submit action and no server/browser draft persistence. **Stop after U1. Do not start G.**


## 21. G — Student Problem / Solution (complete, 2026-10-08)

Base f35096d, branch codex/g-student-solution. Baseline verified before implementation:
143 frontend + 225 backend tests; both builds 0 warnings/errors; Compose healthy.
Read Domain > Feature > Frontend/C4/U1 > Flow; inspected composition reference.
GET identity is ProblemAssignmentId, supports absent work. Reuse D/E/F client,
validators, shell/tabs and U1 upload/content/delete lifecycle. No backend defect found.
Submission accepts body OR photos; optional body belongs to Submission (G07).
In-memory form survives tabs, not navigation; no draft persistence/cleanup changes.
Command has synchronous duplicate guard, no retry, supplied version and mandatory
workspace refresh. Ambiguous command outcomes lock sending until explicit GET refresh.
History remains H boundary. Latest feedback comes only from work projection.
C4 hints/materials currently empty; render only supplied student-safe entries.
A shared safe Markdown/math renderer and protected original preview complete content
presentation; no teacher controls, history timeline or new uploader/client.

G verified exit checklist:
- [x] Assignment workspace, null work, immutable statement always above keyboard/URL tabs.
- [x] Real U1 photo-first upload/preview/order/delete/retry and optional body; no alternate lifecycle.
- [x] Real Submit, supplied version, synchronous double guard, no automatic retry, authoritative refresh.
- [x] NotStarted/Submitted/NeedsRevision/Accepted, permissions, projected feedback and current submission.
- [x] Hints/materials only from contract; History remains placeholder; one responsive ContextPanel.
- [x] 196 frontend tests (143 existing + 53 G), 225 unchanged backend tests, zero failures/skips.
- [x] Frontend/full solution builds zero warnings/errors; git diff --check.
- [x] Real Compose Student Course→Lesson→Problem→two A4 uploads→Submit→reload→Lesson updated.
- [x] Real review API fixtures verify revision/new attempt/Accepted; no teacher UI or invalid DB rows.
- [x] 320/375/768/1024/1440 no page overflow, long statement/math, keyboard and context preservation.
- [x] Isolated Compose restart/down-up persistence and down-v clean DB/uploads reset.
- [x] D/E/F browser regressions; README and 33-part G report with evidence/limitations.

No backend contract defect or backend production changes. Unsourced hints/materials and immutable
Markdown image assets remain existing enrichment gaps; no invented data. Local draft survives tabs
but is not persisted across navigation/refresh; pending uploads remain for U1 future cleanup.
See `codex/G-RESULTS.md`. **Stop after G; H/I/J not started.**


## 22. H — Student History (complete, 2026-10-08)

Base `6157592`, branch `codex/h-student-history`, canonical `/home/tea/github/lms`;
no additional worktree or parallel agents. Baseline: 196 frontend, 225 backend tests,
both builds zero warnings/errors. Domain/Feature, C3/C4 wire amendments, U1/G results,
Frontend History and UI Flow §9 plus composition mockup informed implementation.

Pre-implementation decisions: workspace already contains complete history, so use the
existing assignment GET without duplicating work-details GET. Preserve server newest
attempt order; join Review.SubmissionId/MessageId, never attach work discussion by time.
General messages are independent entries, newest first from reversed server CreatedAt/ID
order; timestamp merge positions them among attempt blocks. Equal cross-kind timestamps
put messages first; server order resolves same-kind ties. Backend has no common event
sequence, so this is deterministic presentation, not a reconstructed domain audit log.
Historical content is assignment-level exact immutable problem/courseVersionId; no
per-attempt revision field exists. Do not fetch latest publication or invent snapshots.

History refresh uses the existing workspace state, retains statement/tabs during GET,
hides stale timeline during loading/error and updates server context atomically on success.
Initial deep link needs one GET; reopening History refetches. Solution has no extra history
request or old-photo rendering, though C3 workspace payload itself always includes history.
No endpoint/backend production defect found; no backend/dependency/state-machine changes.

H verified exit checklist:
- [x] Real read-only History on existing query-tab route, condition remains above tabs.
- [x] Null work / empty history; no work creation or null work GET.
- [x] Pure immutable attempt grouping, correct review/feedback and separate messages.
- [x] Protected original pages retain attempt identity/order; no edit/delete; error/retry.
- [x] Available historical content reference used, no latest revision fallback.
- [x] Context status/count/latest/materials/return link, no duplicated timeline, mobile after main.
- [x] Loading/errors/GET retry/focus, cancellation/stale protection, Submit → History refresh.
- [x] 233 frontend tests = 196 prior + 37 H; 225 backend regressions, zero failed/skipped.
- [x] Frontend/full solution builds zero warnings/errors; default and isolated Compose healthy.
- [x] Real browser Submit #1 → Revision → Message → Submit #2 → Accept; immutable hashes.
- [x] Reload/API restart persistence, keyboard enlargement/tabs/Back/Forward, no page errors.
- [x] Five widths 320/375/768/1024/1440, long feedback/body/12 attempts, no horizontal overflow.
- [x] Documentation/result report updated; final commit and clean tree verified at delivery.

Evidence and all 22 report items: `codex/H-RESULTS.md`; logs `/tmp/lms-h-*`, screenshots
and browser result JSON `/tmp/lms-h-browser/`. G03 remains CLOSED. Existing enrichment,
workspace full-payload and per-attempt content-reference limits are documented honestly.
**Stop after H. I Teacher Queue and J Teacher Review not started.**


## 23. Checkpoint I — Teacher Review Queue (2026-10-08)

База `200ab25`, новая запрошенная ветка `codex/i-teacher-queue`; canonical checkout,
последовательно, без новых worktrees. Baseline: 233 frontend/225 backend tests,
обе сборки zero warnings/errors, default Docker Compose healthy.

Decisions: Domain §29 → Feature UC-A02/queue → C3/C4 amendments/ReadModels.cs/
PostgresReviewQueue.cs → UI Flow §10 → mockup composition. Queue — server projection
Submitted/latest submission. Стабильный порядок SubmittedAt/ProblemWorkId. Exact C4
DTO; nullable names fallback на IDs без lookup. Нет filter catalog/grade/byGroup или
global unfiltered total: не изобретать эти данные из paginated rows/filtered byCourse.

Реализованы /teacher/reviews, cancellable bearer GET/runtime decoder, все documented
filters и explicit-submit search, URL normalization/Back/Forward, server pagination,
filtered all-pages summary/context, loading/error/retry/empty, responsive rows.
J остаётся placeholder; CTA uses ProblemWorkId; return URL сохраняется в Router state,
возврат/remount получает актуальную queue. Нет polling, mutable aggregate, local
business-state mirror, dependencies или backend changes. Имена/группы — existing gap.

I exit checklist:
- [x] Real authorized Submitted queue; first/revision и excluded states live verified.
- [x] Server course/group/student/attempt/search filters and page/pageSize/total.
- [x] Summary independent of page size; available context only, no duplicated rows.
- [x] Validated URL, Back/Forward, reset to page one, cancellation/stale guards.
- [x] ProblemWorkId → unchanged J placeholder → preserved URL + authoritative refresh.
- [x] 401/403/404/errors/retry/empty; live Student API/UI forbidden.
- [x] 320/375/768/1024/1440, long labels, no overflow, keyboard and landmarks.
- [x] 276 frontend = 233 baseline + 43 I; 225 backend, zero failed/skipped.
- [x] Both builds zero warnings/errors, default/dedicated Compose startup healthy.
- [x] Real Student photo → Teacher queue, filters/search/pagination/J browser smoke PASS.
- [x] API restart/down-up history/photo persistence; dedicated clean reset PASS.
- [x] Shared shell browser smoke and git diff --check PASS.
- [x] Results/README updated, final commit and clean working tree verified at delivery.

24-part report: `codex/I-RESULTS.md`. Evidence `/tmp/lms-i-*.log`, browser screenshots/
results `/tmp/lms-i-browser/`, lifecycle `/tmp/lms-i-lifecycle.json`. G03 remains CLOSED.
**Stop after I. Do not start J.**

## 24. J1 Teacher Review — decisions before implementation (2026-10-08)

Scope: J1 only, branch `codex/j1-teacher-review`, base `a388c6d`, canonical checkout.
Source order: Domain → Feature → Frontend C3/C4 amendments → Flow → mockup.
- Existing ProblemWorkDetailsDto provides exact immutable statement, currentSubmission,
  ordered attachmentIds, history, permissions and version. No backend change needed.
- Teacher details has no reference solutions, evaluation criteria or materials and no
  trusted source for them. Nonblocking gap: omit sections; do not invent DTO fields/tests.
- Earlier Frontend §29.7 puts actions in the right panel; explicit J1 composition places
  feedback/actions in main. Follow J1; context holds identity and compact prior attempts.
- Feature UC-A03/C4 permits Student to GET their own work. This is not a teacher-only
  endpoint; review commands remain forbidden, and UI follows separate server capabilities.
  No fabricated role field or stricter backend policy; foreign work and review denial tested.
- Feedback joins Review.messageId; attempt joins Review.submissionId. No time-based guesses.
- Existing native textarea styling/form conventions, common API client and U1 protected
  AttachmentPreview will be reused. J1 adds paging/zoom without a canvas or annotations.
- Commands use currentSubmission.id/version; immediate mutual exclusion, no automatic retry,
  explicit refresh after conflict/uncertain outcome, authoritative GET after success.

### J1 results and exit checklist

- Real ProblemWork GET, immutable statement, protected paged/zoom viewer, compact history,
  feedback and both commands implemented with existing capabilities/version.
- Immediate double-submit lock, explicit conflict/uncertain refresh, authoritative success
  GET and fresh queue return with original filters. Backend unchanged; gaps above retained.
- Frontend 331/331 (276 baseline +55); backend 225/225; both builds 0 warnings/errors.
- Docker/browser real Submit → Revision → Submit → Accept, responsive five widths,
  concurrency/double-click/Student denial, immutable photos/history, restart/down-up and
  dedicated clean reset PASS; shared shell smoke PASS.
- Full report/evidence/limitations: `codex/J1-RESULTS.md`. Commit and clean tree checked
  as final step. Only J1 delivered. J2 annotations not started.

## 25. J2 — контракт до реализации (2026-10-08)

Ветка codex/j2-image-annotation, база bf3b750. Review получает immutable value objects
AnnotatedAttachment → PenStroke → StrokePoint. JSONB выбран потому, что набор читается
и сохраняется целиком, без независимого lifecycle или запросов по отдельным точкам.
ReviewId/SubmissionId принадлежат Review; attachmentId проверяется по current Submission.
Старые записи получают пустой массив. Существующий запрет UPDATE/DELETE Review и
транзакция PostgresWorkStore сохраняются. Optional annotations в обоих review commands.
Координаты [0,1] относительно изображения после EXIF orientation браузером; width —
доля меньшей стороны. Только pen, #RRGGBB, pressure optional [0,1].
Лимиты: 20 страниц, 2000 штрихов всего, 4096 точек на штрих, 100000 точек всего,
4 MiB HTTP body. Пустые наборы допустимы. Серверных черновиков нет.
UI: общий слой canvas над protected original, локальная история каждой страницы;
финальная команда атомарна, ошибки сохраняют draft. Student получает read-only слой.
Новые статусы, aggregates и endpoints не добавляются.

- [x] Backend/domain/persistence/API и тесты
- [x] Editor, teacher/student integration и тесты
- [x] Browser/responsive verification
- [x] J2-RESULTS; итоговый commit содержит этот отчёт

J2 completed: 362 frontend / 248 backend tests, .NET/Vite builds, PostgreSQL migration/
rollback/concurrency, Chrome workflow и tablet EXIF/DPR/native-pen stress PASS.
20,002 points / 201 strokes за ~3.6s, original bytes unchanged. Исправлены geometry
CSS cascade, keyboard focus, empty coalesced samples и image-error overlay.
Hardware iPad/Safari не проверялись; draft crash recovery не заявляется.
Подробности и evidence: `codex/J2-RESULTS.md`. После итогового commit — остановка.


## 26. K — Integration & Release Hardening (2026-10-08)

Canonical checkout, requested branch `codex/k-release-hardening` from `47d6de3`.
Audit-first baseline before fixes:362 frontend /248 backend PASS. Existing contracts,
authorization, storage/migrations/Docker and actual smoke coverage inspected; risk matrix
recorded before implementation. No worktree, parallel agent, new framework or schema.

New evidence: reproducible Playwright real-API release runner (35 main steps, negative
uploads/submit/role/terminal/navigation boundaries), six performance profiles, SQL-count
queue/lesson measurement, unsigned JWT, attachment revocation, commit-boundary failure
and interleaved snapshot tests. Existing tests remain; write barriers explicitly exclude
read-only snapshot transactions. Physical-device protocol90 rows honestly BLOCKED.

Confirmed fixes: P0 uploader bypass of revoked parent-work access; P1 Cartesian history
read (20-attempt teacher p95 5520.76→59.36ms, student5328.28→71.87ms); ported proven P1
stylus/UUID compatibility fix from post-base594578d; P2 teacher-screen role gate; asynchronous
resize assertion in tablet smoke. General C4 own-work API remains backward-compatible;
new teacher-only alias has the same DTO. Attached bytes, Domain transitions and migrations
unchanged. Split reads share RepeatableRead snapshot; concurrency tests verify consistency.

Final evidence:366 frontend /260 backend PASS, both builds PASS (.NET0 warnings/errors),
Docker/real browser and CDP tablet runner PASS, no browser errors, cleanup PASS. P6 queue
5 SQL queries at page sizes1/50/200; adjacent Lesson1001 assignments2011queries/1166.20ms
remains P2, as does dense500-stroke next-frame p95 73ms. No fabricated performance budgets.
Raw summaries and logs retained in `codex/evidence/k`, screenshots in `/tmp/lms-k-run`.

Seven deliverables: K-TEST-MATRIX, K-SECURITY-AUDIT, K-PERFORMANCE-REPORT,
K-DEVICE-TEST-PROTOCOL, K-MVP-GAPS, K-RELEASE-READINESS, K-RESULTS.
**NOT_READY**: real IdP/session/provisioning, deployment security/resource protections,
coordinated DB+photo backup/restore and operational recovery remain release gates;
Safari/physical devices unavailable. P0 finding fixed, open P1 gates explicitly tracked
with remediation, not masked as nonblocking limitations. Names/lookups/reference/criteria
classified, not implemented speculatively. Recommend staged Identity & Operations
Readiness only after a new instruction. Stop after K commit; no next checkpoint.


## 27. L0 — Architecture & Decision Gates (2026-10-09)

Canonical `/home/tea/github/lms`, clean base `c7a500f`, requested branch
`codex/l0-architecture` непосредственно от K. Последовательно, без новых worktrees,
без изменения кода/Domain/schema/migrations/Docker. D1 собственный VPS/сервер,
D2 Compose и D3 ASP.NET Core Identity приняты пользователем; остальные рекомендации
не считаются принятыми. Полный результат: [L0-RESULTS](L0-RESULTS.md).

- [x] Изучены обязательные K/normative sources и фактические слои/auth/storage/E2E.
- [x] Созданы ADR-L01–L08: Identity, provisioning, VPS, HTTPS/secrets, backup,
  resource protection, device scope, operations/privacy.
- [x] [L-ARCHITECTURE](L-ARCHITECTURE.md): Actor mapping и existing component boundaries.
- [x] [L-DECISION-REGISTER](L-DECISION-REGISTER.md): accepted D1–D3, proposed B01–B09,
  open C01–C09; минимум для L1 — C01 provisioning и C02 initial access/recovery.
- [x] [L-IMPLEMENTATION-PLAN](L-IMPLEMENTATION-PLAN.md): последовательность, gates,
  changes/tests/exit/complexity/risks, future branch/merge policy.
- [x] Source links, Domain consistency, docs-only diff и whitespace проверены;
  app tests не перезапускались, K baseline 366 frontend/260 backend исторический.

PROPOSAL: same-origin Identity cookie + CSRF/server session validation; независимый
IdentityUserId→ActorId без переписывания истории; operator-only provisioning; private
single-host topology; coordinated DB+originals+keys backup; durable resource admission
и cleanup, не удаляющий linked history. Safari/Apple Pencil не объявлены проверенными.
Owner hosting/recovery/privacy/quotas/devices/ops gates остаются открытыми.

| Следующий этап | Prerequisites / scope | Exit |
|---|---|---|
| L1 Identity | Разрешение L1 + C01/C02; Identity mapping/session/login/CSRF/provisioning, synthetic data | Real Identity auth/scope/lifecycle/upgrade/browser tests; no demo pilot auth |
| L2 Resource Protection | Accepted L1 + разрешение; C06/C08 для product quotas/retention | Rate/concurrency/quota/cleanup races, disk safety, immutable history preserved |
| L3 Deployment & Backup | Accepted L2 + разрешение; C03–C05/C09, capacity/privacy inputs | HTTPS/private Compose/secrets/keys, migration/rollback и off-host clean-host restore доказаны |
| L4 Pilot Acceptance | Accepted L1–L3 + разрешение; owner gates закрыты | Real-account E2E, recovery/performance/physical devices, evidence-based readiness |

Будущий порядок: review L0 → отдельно разрешённая integration K+L0 → L1 review/merge
→ L2 → L3 → L4, каждый от accepted predecessor, отдельная ветка, sequential canonical
checkout. Сейчас push/merge/deployment не выполняются. Исторический ранний пункт
«L — Responsive/a11y» в §8 остаётся историей первоначального плана; текущее задание
переопределяет новый L как Pilot Identity & Operations Readiness.

**После L0 остановиться: согласовать результат с владельцем. L1–L4 не начинать.**
