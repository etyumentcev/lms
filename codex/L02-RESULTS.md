# L0.2 — Organization, LearningSpace & Processing Context: results

2026-10-09. Documentation-only checkpoint.

**Compliance status: NOT VERIFIED.**

**Pilot readiness: NOT_READY.**

## Base / branch / commit

Canonical checkout `/home/tea/github/lms`; исходное дерево чистое, исходная ветка `codex/l01-personal-data-compliance`, HEAD совпал с base `3aa34c18041e74570b04ba10a01f4294d451bc5d`. Ветка `codex/l02-organization-processing-context` создана непосредственно от base. Дополнительных worktrees/агентов нет. Result hash сообщается после commit отдельно, не записывается self-referential hash в этот файл.

## Изученные источники

Обязательные документы изучены по предметным зависимостям, authorization, history и актуальным amendments:

- [Domain spec](../docs/domain/LMS-DOMAIN-SPEC.md), [Submission/review](../docs/features/FEATURE-SUBMISSION-REVIEW.md), [Frontend contract](../docs/ui/LMS-FRONTEND-CONTRACT.md), [UI flows](../docs/ui/LMS-UI-FLOW-SPEC.md).
- [Architecture](L-ARCHITECTURE.md), [implementation plan](L-IMPLEMENTATION-PLAN.md), [decision register](L-DECISION-REGISTER.md), [L01 results](L01-RESULTS.md), [L01 gates](L01-DECISION-GATES.md), [ExecPlan](EXECPLAN-LMS-SIX-SCREENS.md).
- [Inventory](../docs/compliance/PD-DATA-INVENTORY.md), [flows](../docs/compliance/PD-DATA-FLOW-MAP.md), [bases](../docs/compliance/PD-PROCESSING-BASES.md), [lifecycle](../docs/compliance/PD-LIFECYCLE-AND-ERASURE.md), [matrix](../docs/compliance/PD-COMPLIANCE-MATRIX.md), [ADR-L09](../docs/adr/ADR-L09-personal-data-lifecycle.md), [ADR-L10](../docs/adr/ADR-L10-erasure-and-immutable-history.md).

Код: Domain ProblemAssignment/ProblemWork/History/Annotations; Application Ports/ProblemWorkService; Infrastructure AccessFacts/PostgresAccessPolicy/LmsDbContext/StoredAttachment/AttachmentService/PublishedContent/PostgresWorkspaceQueries/PostgresReviewQueue/PostgresWorkStore и migration structure; API ActorMapper/Program/CommandsController/QueriesController/AttachmentsController; Web App/api client/browserClient и contracts. Фактический dependency map со ссылками находится в [Organization model](../docs/architecture/ORGANIZATION-MODEL.md). Student/Teacher profiles, Identity persistence, Organization и ProcessingContext не объявлены реализованными.

## Принятый бизнес-scope и предложенная модель

Владелец принял стороннюю организацию как организатора пилота, 5–8 классы, закрытые accounts, один контекст, Platform/Education separation и возможность собственных курсов того же ООО. D4 сохранён исторически и уточнён D4a–d. Конкретная юридическая квалификация — гипотеза.

Предлагается Organization root и неизменяемая course ownership registry. [LearningSpace](../docs/architecture/LEARNING-SPACE-MODEL.md) отдельной entity не нужна при одном контексте на организатора; вариант 1:N рассмотрен для будущего независимого администрирования. Неявный tenant отвергнут. Globally unique CourseId сохраняет routes и возможность добавить второго организатора без переписывания history. Новые таблицы/связи допускают аддитивную стратегию, но FK/constraints и verified legacy mapping требуют согласования и upgrade tests. Unknown roots не присваиваются реальной организации автоматически; synthetic fixtures имеют отдельную стратегию.

[ProcessingContext](../docs/architecture/PROCESSING-CONTEXT-MODEL.md) — durable versioned compliance policy вне учебных aggregates, плюс subject basis. Domain entity/копия юридических полей на каждом Review и configuration-only рассмотрены и отклонены. Course binding задаёт наследование; current blocks/withdrawal ограничивают прежние approvals.

[Identity/membership](../docs/architecture/IDENTITY-ORGANIZATION-SCOPES.md): один account→Actor, независимые memberships и course/group scope. Одна основная роль на account остаётся ограничением пилота; будущие роли в разных организациях не объединяют histories. Multi-role UI не добавлен.

## Роли, изоляция и lifecycle

[Controller/processor matrix](../docs/compliance/PD-CONTROLLER-PROCESSOR-MODEL.md): A — предполагаемый оператор учебных целей, ООО — обработчик по поручению; собственные цели ООО отдельно, Identity/security unresolved до review. Для своих курсов ООО оператор учебной цели без второго юрлица. Подготовлены требования и неподписанные проекты договорных положений; применимость 273-ФЗ/лицензии проверяется по фактической деятельности, льгота ООО не предполагается.

[Isolation](../docs/architecture/CROSS-ORGANIZATION-ISOLATION.md) определяет backend formula, query/SQL/count scope, file upload binding/claim/parent checks, commit-time revoke и barrier/drain, export scope, negative attacks и break-glass. Текущие course/group guards полезны, но organization boundary отсутствует. Platform permissions не дают учебный read; DB/root требует отдельного операционного контроля.

[Organization lifecycle](../docs/compliance/PD-ORGANIZATION-LIFECYCLE.md) покрывает ученика/родителя/поручение/завершение обучения/договора/деятельности/teacher revoke/account deletion. A не может уничтожить B; independent holds и backups по context. Shared copy не продлевает сроки A до B. Ordinary immutable workflow сохраняется, erasure отдельный привилегированный manifest с evidence и quarantine restore/current ledger.

## L1–L4 / влияние на API / решения

L1 получает schema/membership/session/provisioning/scope/file isolation и synthetic two-org tests после gates. L2 — organization quota/lifecycle/erasure/retention/audit. L3 — ответственность сторон, localization/contracts/support/backup/restore/access logs. L4 — evidence isolation, правомерность уничтожения, договор/поручение/152-ФЗ и возможность B без утечки A. Детали в обновлённом implementation plan.

Production code, migrations, API/DTO, шесть экранов и Domain state machine не изменены. Будущий scope resolver может сохранить single-context routes; ambiguous context должен отклоняться. Никаких гарантий migration/test результатов до реализации.

Все [ORG-G01–08](L02-DECISION-GATES.md) OPEN. До L1 нужны schema/membership/policy/access/lifecycle/legacy решения ORG-G01–04/06/07 и правовое согласование затрагиваемых целей ORG-G05 вместе с прежними C01/C02/PD gates. Closed provisioning и возрастной scope повторно не согласуются; issuer/recovery/представительство ещё открыты. Подписанные договоры, фактические меры/уведомления/лицензирование по применимости нужны до real data.

## Риски и проверка

Риски: raw SQL обход filters, pending uploads одного actor между A/B, implicit global roles, stale sessions/grants, неизвестная принадлежность legacy, shared backup сроки, смешанные субъекты, прямой root access. Каждый связан с будущими controls/tests; риск не объявлен устранённым документом.

Выполнен архитектурный review: альтернативы/необходимость сущностей, API compatibility, immutable invariants, единая семантика LearningSpace/Organization, статус ADR PROPOSED, D4 уточнение и scope lifecycle. Выполнена проверка относительных файловых ссылок новых документов и добавленных amendments, diff whitespace и docs-only состава. Юридический review документации исключает утверждение о подтверждённых ролях/лицензионном освобождении; web lookup справочных текстов не заменяет официальную legal verification. App tests не запускались (documentation-only), исторические результаты не заявлены как новые PASS.

После commit проверяются parent/base, состав 25 Markdown файлов и чистота дерева; результат этих Git проверок сообщается отдельно. Sandbox exec недоступен из-за mountinfo error, использованы одобренные escalated команды canonical checkout. Одна попытка записи была остановлена сбоем автоматического approval review по лимиту; после указания владельца продолжить работа возобновлена.

Остановка после документации/commit. L1–L4 не начаты; push, merge, deployment не выполнялись.
