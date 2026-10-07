# ExecPlan: шесть экранов LMS и Submit → Review → Revise → Accept

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
| Teacher / RequestRevision | Authorized teacher, Submitted, latest unreviewed submission, атомарный expectedVersion; application требует feedback; domain допускает optional согласно G03 | ProblemWork; Review(NeedsRevision), Message, SubmissionId, Version | RevisionRequested; проверить принятую политику MessagePosted для feedback | Domain §§15,21,23,32–33; Feature UC-A04, §§15–17 | Submitted → NeedsRevision |
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

Ни один пункт не исправляется в нормативных документах в этой задаче. Для domain blockers действует final §24: не выбирать молча между конфликтующими контрактами.

| ID / слой | Evidence / расхождение | Минимальное дальнейшее действие |
|---|---|---|
| G01 — repository, CLOSED | Пользователь подтвердил greenfield | Создать единственный solution по архитектуре §2 |
| G02 — authorization, PLANNED | Greenfield, middleware пока не нужен | Application access port + negative tests; authenticated principal и production access adapter до API |
| G03 — domain/application, OPEN architectural decision | Текущая реализация B: Domain operation допускает optional feedback; Application RequestRevision требует non-empty feedback. Domain §21 и требования Feature/UI различаются по строгости | Текущий код и тесты сохранить. Если правило «возврат на доработку всегда требует объяснения» глобальное, позднее сделать его domain invariant и синхронизировать LMS-DOMAIN-SPEC.md. До отдельного решения не расширять другие use cases возврата на доработку. Accept feedback остаётся optional |
| G04 — Application RESOLVED; API deferred | Feature UC-A01: `ExpectedVersion?`; §13.2 требует version от state-changing commands. Frontend §8.8 и Feature §9.1 omit version; Flow §8 допускает `ExpectedVersion?`; work до первой отправки отсутствует | Application реализует null для нового work и exact version для существующего. Save port требует atomic compare-and-save и unique assignment. Wire DTO и реальная PostgreSQL race проверяются позже |
| G05 — deferred до content/read API | Domain §8 запрещает student content неопубликованного/ещё недоступного урока; Frontend §7.6 допускает содержимое недоступного урока по политике; Actors UC-S02 запрещает видеть сам урок | Current application access port требует available lesson для submit. Политика выдачи locked metadata/content отложена до read API; сейчас content не выдаётся |
| G06 — API error shape | Feature §8: Problem Details `title/detail/code`; Frontend §3.6: `message/fieldErrors/code` | Один API-client adapter Problem Details → ApiErrorDto; ветвиться по code; согласовать transport fields, не менять домен |
| G07 — application/UI student comment, PLANNING RECOMMENDATION | Submission состоит из `bodyMarkdown + attachments`; отдельная сущность «student solution comment» не нужна | Для будущего Student Solution UI использовать label «Пояснение к решению — необязательно» для bodyMarkdown. Не вводить отдельную persistence-сущность комментария и не делать скрытый второй POST Message при SubmitSolution. Это planning decision, без изменения кода сейчас |
| G08 — read model/local draft | Frontend §7.2 и Feature UC-A08 возвращают `hasDraft`, но final §13 допускает только browser-local draft при отсутствии механизма | Не заставлять сервер угадывать local storage. Предложение: локальная подпись по `(studentId,assignmentId,nextAttemptNumber)` с явным разделением server DTO и UI state |
| G09 — progress semantics | DTO требуют completedLessons/percent и lesson status; Domain §16 задаёт производность, но не формулы для required/optional, withdrawn, пустых уроков и denominator | До реализации согласовать формулы и zero-denominator examples; не сохранять mutable progress и не выводить логику из примерного percent=18 в Feature §9.7 |
| G10 — content/read model | DTO содержат hints/tags/difficulty/estimatedMinutes/grade/material metadata; Content v1 не задаёт все эти поля, Domain §37 исключает hints engine | Использовать существующие metadata/read sources, иначе null/[]/empty UI. Не расширять Front Matter или hints engine молча; teacher solutions не выдавать student |
| G11 — history relationships | Message связан с work/reply, не Submission; Frontend §9.4 требует messages по попытке | Review feedback связывать строго через Review.MessageId/SubmissionId. Общие work messages показывать отдельным discussion внутри history, пока нет согласованного правила группировки; не сохранять выдуманный SubmissionId |
| G12 — stale descriptions | Frontend §§5,10.7,23 описывают workspace/search как недостающие, но Feature v1.1 уже документирует UC-A07–09 и search; это не доказательство существования кода | Использовать полный актуальный documented contract, проверить handler once, не дублировать use cases |
| G13 — secondary documents | Feature/Frontend ссылаются на отсутствующий `LMS-UI-FLOW-SPEC-v2.md`; реальный файл без v2. Audit утверждает отсутствие mockup, хотя PNG есть; Flow §21 предлагает уже выполненную замену «Зачтено» | В плане использовать канонические пути final prompt; будущая отдельная docs cleanup без переопределения source of truth |
| G14 — scope | Actors §13 считает шестым экраном group/student progress; final §4 и нормативные UI docs считают History отдельным экраном | Следовать текущему slice из final; не добавлять седьмой экран/analytics |
| G15 — queue summary | Frontend §29.6 задаёт summary, но не уточняет facet counts при активном attemptKind | До query tests согласовать, какие фильтры применяются к summary; totals минимум соответствуют отфильтрованным данным, не странице |
| G16 — Course workspace actor | Feature UC-A07 разрешает Teacher, но DTO enrollment обязателен как у Student | Уточнить teacher response/персональный progress. Не фабриковать enrollment преподавателю |
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
| D — Shared UI shell, DEFERRED | Создать минимальные tokens/components по контракту → общий shell/sidebar/context rail/status primitives; актуализировать таблицу reuse | Build + проверка composition, keyboard tabs, status text; без второй UI-библиотеки |
| E — Course | Course query, availability, server progress, metadata/materials | Loading/empty/access/error; CTA, отсутствие недоступного content |
| F — Lesson | Assignment IDs, four task statuses, materials, progress, контекст | Nullable work; task actions; отсутствие client business availability logic |
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

- [ ] PostgreSQL поднимается в integration test environment.
- [ ] Initial migration применяется к чистой test database.
- [ ] Persistence adapters реализуют существующие application persistence ports.
- [ ] Unit tests B остаются зелёными: 17 domain + 12 application как принятый baseline.
- [ ] Persistence/integration tests используют реальные concurrent transactions и отдельные units of work/DbContexts, а не последовательную имитацию race.
- [ ] Race first submit не создаёт два ProblemWork на одно assignment и две успешные первые попытки.
- [ ] Stale expectedVersion отвергается без записи изменений проигравшей транзакции; stale review не изменяет новую попытку.
- [ ] State/history/events коммитятся атомарно; тест принудительной ошибки подтверждает rollback всех частей.
- [ ] Остальные persistence invariants выше подтверждены тестами: immutable submissions, unique attempts, review linkage, terminal Accepted.
- [ ] `dotnet build Lms.sln` проходит без warnings/errors; допустимы ранее использованные single-node flags окружения.
- [ ] Domain/Application/Infrastructure tests проходят; команды и результаты внесены в отчёт C1.

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

- **G03:** окончательно решить, является ли non-empty revision feedback глобальным domain invariant; при положительном решении синхронизировать Domain Spec и реализацию отдельным изменением. До него не расширять другие revision use cases.
- **C1:** зафиксировать EF mappings, concurrency token/atomic save, формат и lifecycle persisted domain events и PostgreSQL test environment; выбрать технические решения в границах существующих ports/invariants.
- **C2 / G04 / G06:** способ authentication и источник access facts, wire semantics expectedVersion и стабильный Problem Details envelope; готовность upload/storage contract.
- **C3 / G05 / G09 / G15 / G16:** допустимые locked metadata, формулы progress (включая пустые/optional/withdrawn assignments), filters для summary/facets, teacher CourseWorkspace без фиктивного enrollment.
- **C3 / G08 / G10 / G11:** server/local hasDraft boundary, источники optional content metadata/hints, представление work messages относительно attempts.
- **G07:** planning recommendation зафиксирована — bodyMarkdown с optional explanation label, attachments, без отдельной comment entity и скрытого POST Message; реализовать только на будущем UI checkpoint.

На этом запуске запрещены изменения production code, Domain/Application implementation, добавление EF Core packages, migrations, API endpoints, frontend и изменения state machine. **После краткого diff summary и списка open decisions остановиться; C1 не начинать.**
