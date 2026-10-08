# L0.2 — Organization: модель и фактическая база

Дата: 2026-10-09. Техническая модель **PROPOSED**, не реализована. Бизнес-решения владельца **ACCEPTED** по заданию L0.2. Compliance status: **NOT VERIFIED**. Pilot readiness: **NOT_READY**.

## Принятый scope

Первый пилот: одна сторонняя образовательная организация, её ученики 5–8 классов и преподаватели, один учебный контекст, закрытая выдача accounts. Организация набирает учеников, заключает договоры с родителями и управляет обучением. Российское ООО разрабатывает и эксплуатирует LMS. В будущем то же ООО вправе планировать собственное образовательное направление и одновременно предоставлять платформу другим организациям; второго юридического лица архитектура не требует. Правомерность конкретной образовательной деятельности проверяется отдельно.

Platform Management и Education Management — независимые полномочия, даже когда их осуществляет одна компания или один сотрудник. Организатор обучения — предметная принадлежность курса, не термин «владелец персональных данных». Юридический оператор определяется по цели, а не по владению сервером.

## FACT на base 3aa34c1

| Понятие | Реализация и зависимости | Пробел для L1/L2 |
|---|---|---|
| Actor | [Ports](../../src/Lms.Application/Ports.cs): record Guid/AuthorRole; [ActorMapper](../../src/Lms.Api/ActorMapper.cs) принимает один authenticated principal, один sub и одну Student/Teacher role | Нет account persistence, membership, organization или platform grants |
| Student / Teacher | UUID в assignment/history/access records; отдельных profiles/таблицы Actors нет | Не считать будущий roster уже реализованным; имя в DTO nullable |
| Course / Lesson | [PublishedContent](../../src/Lms.Infrastructure/PublishedContent.cs): immutable ContentJson/version; LessonAccess задаёт публикацию/дату; CourseId строка | Нет полноценного CRUD/организатора, GroupId тоже access metadata |
| Enrollment | [AccessFacts](../../src/Lms.Infrastructure/AccessFacts.cs): StudentId+CourseId PK, GroupId, IsActive | Не организация и не основание обработки |
| Teacher assignment | TeacherAccess: TeacherId, CourseId, GroupId nullable; null означает весь курс | Добавить active membership и организационный scope; course-wide grant сейчас не требует active enrollment ученика |
| ProblemAssignment | [Модель](../../src/Lms.Domain/ProblemAssignment.cs): StudentId, CourseId, exact CourseVersionId/LessonId/ProblemId, Active/Withdrawn | Корневая связь с учебной границей отсутствует |
| ProblemWork | [Aggregate](../../src/Lms.Domain/ProblemWork.cs): один work на assignment, StudentId, Version, immutable history | Граница наследуется от assignment, не от автора очередного события |
| Submission / Review / Message | [History](../../src/Lms.Domain/History.cs): FK work; Review также submission/message; actor UUID; attachment UUID arrays | Нет законного основания или OrganizationId; добавлять юридические поля на каждую строку не нужно |
| Attachment | [StoredAttachment](../../src/Lms.Infrastructure/StoredAttachment.cs): OwnerId, private StorageKey, pending либо SubmissionId/MessageId | Pending owner недостаточен для изоляции одного пользователя между организациями |
| Annotations | [VO](../../src/Lms.Domain/Annotations.cs), Review JSONB; страницы только current submission, immutable вместе с Review | Контекст через Review→work, не самостоятельная учебная сущность |
| Policies / queries | [Policy](../../src/Lms.Infrastructure/PostgresAccessPolicy.cs), [workspace](../../src/Lms.Infrastructure/PostgresWorkspaceQueries.cs), [queue SQL](../../src/Lms.Infrastructure/PostgresReviewQueue.cs) | Ownership/course/group есть; organization/privacy check отсутствует; SQL требует отдельного изменения |
| API / Web | [Commands](../../src/Lms.Api/CommandsController.cs), [Queries](../../src/Lms.Api/QueriesController.cs), [Program](../../src/Lms.Api/Program.cs), [client](../../src/Lms.Web/src/api/client.ts), [App](../../src/Lms.Web/src/App.tsx): bearer, шесть экранов | Identity/session/scope UI — будущий L1; Organization API отсутствует |

[DbContext](../../src/Lms.Infrastructure/LmsDbContext.cs) задаёт RESTRICT FK, unique work/assignment, CourseId+VersionNumber и StudentId+CourseId. Пять [миграций](../../src/Lms.Infrastructure/Migrations) включают immutable triggers. Иллюстративные схемы спецификаций не считаются фактическими таблицами.

## Предложение и необходимость

Минимальный Organization record: стабильный OrganizationId, display name, type (описание, не privilege), lifecycle status (provisioning/active/suspended/closed). Нужен независимый от credentials адресат курсов, membership, договорной ссылки и прекращения обслуживания. Реестр реквизитов/подписантов хранится в compliance-контуре по защищённой ссылке; ИНН/паспорт в Domain не нужны для учебных инвариантов. Identity account не является Organization.

Course ownership registry-кандидат `(CourseId globally unique, OrganizationId)` позволяет сохранить существующие строковые course routes и неизменяемые content references. CourseId обозначает конкретное проведение курса одним организатором; общий учебный материал не означает общий курс/roster. Для второго организатора создаются новый CourseId и новые published version IDs; копирование неперсонального материала допустимо лишь при правах на контент. Перенос учебной истории к другой организации не является UPDATE owner.

Organization необходима для заданной независимости двух организаторов. LearningSpace отдельной таблицей пока не нужна: [сравнение](LEARNING-SPACE-MODEL.md). ProcessingContext — отдельная compliance policy: [модель](PROCESSING-CONTEXT-MODEL.md). Membership и course/group grants нужны для активного допуска; глобальная роль их не заменяет.

## Минимальный пилот и расширение

После согласования schema: один Organization, один учебный boundary, закрытый provisioning по распоряжению образовательной организации; один основной educational role/account; без selector организаций, tenant billing, self-service onboarding, parent portal и licensing UI. ООО как platform manager не получает учебный membership автоматически. Для собственных курсов позже создаётся Organization, представляющая то же ООО в Education Management, со своими membership/курсами/контекстом; platform authority остаётся отдельной.

Второй организатор добавляется новыми корнями, без переписывания исторических Submission/Review. Общая Identity может связать того же Actor с двумя memberships, но не объединяет историю/статистику. Необходима проверка всей цепочки доступа до подключения второго организатора, уже на synthetic fixtures в L1.

## Стратегия migration — проект, не готовый SQL

1. Инвентаризировать legacy CourseId, actors, все grants, uploads и orphan references. Разделить доказанно synthetic и неизвестные данные; неизвестные закрыть от serving, не присваивать реальной организации по умолчанию.
2. Аддитивно создать Organization, membership, Course ownership registry и context bindings; сохранить существующие PK/UUID/content references. Для access records CourseId→registry и scoped membership дают организационный scope без замены текущих PK. Новые FK вводить после проверенного mapping; запретить удаление/смену ownership при истории.
3. Для pending attachments добавить отдельную scope binding 1:1 по AttachmentId с OrganizationId и context version. Attached scope вычислять через parent и проверять совпадение binding. Claims выполняются атомарно с повторной проверкой. Технический конкретный constraint/trigger design согласуется L1, concurrency implementation проверяется L1/L2.
4. Проверенное сопоставление реальных legacy roots фиксируется отдельным approved manifest; отсутствие доказательств блокирует mapping. Synthetic fixtures можно пересоздать в isolated DB или явно пометить тестовой организацией, без применения этой процедуры к real data.
5. Переключить все reads/writes/SQL/jobs на обязательный resolver, deny при missing mapping; затем валидировать FK/NOT NULL там, где нужны. Проверить clean install и upgrade с прежней immutable history. Это добавления таблиц/связей без массовой миграции history; ограничения и индексы всё же требуют reviewed migrations/maintenance window.

Для новой истории scope фиксируется до первого использования и не меняется. Один глобально уникальный CourseId — сознательное ограничение MVP; локальные одинаковые slugs в будущем требуют отдельного routing design, а не снятия изоляции. [ADR-L11](../adr/ADR-L11-organization-boundary.md), [gates](../../codex/L02-DECISION-GATES.md).
