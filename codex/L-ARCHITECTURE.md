# L — Architecture baseline and proposed pilot boundary

> Актуальные решения L0.3: [readiness](L03-L1-READINESS.md), [implementation plan](L03-L1-IMPLEMENTATION-PLAN.md). C01/C02 и ORG-G01–04/06/07 ACCEPTED (бизнес/архитектура), implementation NOT VERIFIED. Предыдущее уточнение L0.2: [Organization](../docs/architecture/ORGANIZATION-MODEL.md) и [controller/processor](../docs/compliance/PD-CONTROLLER-PROCESSOR-MODEL.md). Историческое «ООО оператор» ниже читать по цели согласно D4a–d; внешняя организация входит уже в первый пилот.

Дата: 2026-10-09. FACT baseline `c7a500f56f26d94e3249a8c8a87e76018b0f5683`, ветка результата `codex/l0-architecture`. L0 меняет только документацию. **NOT_READY** из K сохраняется. D1 VPS/сервер, D2 Compose, D3 ASP.NET Core Identity приняты владельцем; D4/D5 добавлены и ACCEPTED в L0.1: ООО РФ выбрано владельцем платформы (исторический D4 уточнён D4a–d), compliance обязателен до первого real-data пилота; принципы ORG и T01–T06 ACCEPTED в текущей редакции L0.3; прочие механизмы PROPOSED. [Decision register](L-DECISION-REGISTER.md) отделяет accepted/technical/owner, [план](L-IMPLEMENTATION-PLAN.md) — будущие этапы.

## FACT — слои и точки интеграции

| Компонент | Подтверждённая ответственность | Reuse / будущая граница |
|---|---|---|
| [Lms.Domain](../src/Lms.Domain/ProblemWork.cs), [History](../src/Lms.Domain/History.cs), [ProblemAssignment](../src/Lms.Domain/ProblemAssignment.cs) | Aggregate ProblemWork, immutable Submission/Review/Message, exact content reference; четыре статуса; annotations VO | Не добавлять IdentityUser, sessions, quota, admin role или новый status в Domain |
| [Lms.Application Ports](../src/Lms.Application/Ports.cs), [ProblemWorkService](../src/Lms.Application/ProblemWorkService.cs) | Actor(Guid,AuthorRole), IAccessPolicy/IWorkStore; role/access→aggregate→atomic save | Identity host adapter выдаёт тот же Actor; permissions продолжают проверяться на backend |
| [LmsDbContext](../src/Lms.Infrastructure/LmsDbContext.cs), [PostgresWorkStore](../src/Lms.Infrastructure/PostgresWorkStore.cs) | PostgreSQL/EF; compare-and-save Version, append-only history/events; split history snapshot | Additive identity/resource metadata, не переписывать историю; restore сохраняет schema/triggers |
| [AccessFacts](../src/Lms.Infrastructure/AccessFacts.cs), [PostgresAccessPolicy](../src/Lms.Infrastructure/PostgresAccessPolicy.cs) | Trusted enrollment/lesson/teacher grants; no public CRUD | Provisioning администратором образовательной организации; роль не равна scope, Identity не выдаёт enrollment автоматически |
| [PostgresWorkspaceQueries](../src/Lms.Infrastructure/PostgresWorkspaceQueries.cs), [PostgresReviewQueue](../src/Lms.Infrastructure/PostgresReviewQueue.cs), [PublishedContent](../src/Lms.Infrastructure/PublishedContent.cs) | Exact immutable snapshots; scoped server projections/queue filters/paging; read не создаёт work | Existing read DTOs и capabilities остаются; trusted profile enrichment только отдельным решением |
| [AttachmentService](../src/Lms.Infrastructure/AttachmentService.cs), [LocalFileAttachmentStorage](../src/Lms.Infrastructure/LocalFileAttachmentStorage.cs) | Original bytes вне DB/web root, random keys; metadata/claim/tombstone; parent-work auth | Resource admission/ledger/cleanup, backup barrier; private storage сохранить |
| [Program](../src/Lms.Api/Program.cs), [ActorMapper](../src/Lms.Api/ActorMapper.cs) | JWT signature/issuer/audience/lifetime → claims → Actor; health/migrate/dev guards | Identity/JWT issue/refresh integration; CSRF при cookies; no demo fallback в pilot |
| [Web client](../src/Lms.Web/src/api/client.ts), [App](../src/Lms.Web/src/App.tsx) | React/TS, same-origin bearer fetch/XHR, no retry; feature local state | Session bootstrap/remount и CSRF transport; существующие uploader/Blob cleanup/annotation components |
| [Compose](../docker-compose.yml), [nginx](../docker/nginx.conf) | Development loopback stack, separate DB/files volumes, migrate job | Отдельный pilot config, HTTPS edge, DP keys, backup/runbooks; текущий local config не production |

Нет реализованных Student/Teacher profile aggregates или таблицы Actors: Actor — record Application, остальные identity UUID живут в assignment/history/access metadata. Identity persistence и mapping в ADR-L01 — **новые предложения**, не найденные таблицы. [ReadModels](../src/Lms.Application/ReadModels.cs) допускают nullable names; K не заполняет их вымышленными данными.

## FACT — authentication endpoint inventory

Все пути ниже имеют prefix `/api/v1`, `[Authorize]` на контроллерах и текущую bearer scheme. `/api/health` отдельно anonymous DB connectivity probe.

| Method | Path | Дополнительная server boundary |
|---|---|---|
| POST | /problem-assignments/{assignmentId}/submissions | Student ownership/enrollment/lesson/attachments/version |
| POST | /problem-works/{problemWorkId}/messages | Own Student или Teacher course/group; attachment/reply checks |
| POST | /problem-works/{problemWorkId}/reviews/needs-revision | Teacher scope/current submission/version/feedback/annotations |
| POST | /problem-works/{problemWorkId}/reviews/accept | Teacher scope/current submission/version/annotations |
| GET | /problem-assignments/{assignmentId}/workspace | Student owner/access |
| GET | /problem-works/{problemWorkId} | Student own work или scoped Teacher |
| GET | /teacher/problem-works/{problemWorkId} | Teacher-only alias + scope |
| GET | /courses/{courseId}/workspace | Enrollment либо Teacher grants |
| GET | /courses/{courseId}/lessons/{lessonId}/workspace | Scoped authorized assignments/content |
| GET | /teacher/review-queue | Teacher + server course/group filters |
| POST | /uploads | Authenticated actor ownership + validated single file |
| GET | /attachments/{id}/content | Pending owner; attached parent-work auth для всех, даже uploader |
| DELETE | /attachments/{id} | Owner pending only, attached=409 |

Sources: [CommandsController](../src/Lms.Api/CommandsController.cs), [QueriesController](../src/Lms.Api/QueriesController.cs), [AttachmentsController](../src/Lms.Api/AttachmentsController.cs). Нет production password login/registration/reset routes. `--dev-token` — CLI, не public login endpoint. Dev tokens/sessionStorage и seed описаны в [LocalDevelopment](../src/Lms.Api/LocalDevelopment.cs) и [docker local adapter](../docker/local-auth.js).

## PROPOSAL — поток pilot identity и operations

```text
Browser -> HTTPS same-origin edge -> API local JWT signature/lifetime/issuer/audience/scope validation
    -> IdentityUserId -> trusted Actor/scope claims -> resource/workflow policy
    -> ProblemWorkService -> Domain -> PostgresWorkStore
Browser -> authorized attachment GET -> private original bytes
Organization admin -> scoped provisioning -> Identity + mapping
Organization admin -> separate membership/enrollment/teacher assignment use cases
Maintenance barrier -> DB + originals + keys/config -> encrypted off-host set
```

T01–T06 ACCEPTED: Identity credential store + JWT ≤15 минут; fresh DB authority при issue/refresh. Ordinary revoke scoped ≤2 часа с reserve 10 минут / API 60 секунд / max HTTP 8 минут по [ADR-L15](../docs/adr/ADR-L15-jwt-delayed-revocation.md). Identity UUID не заменяет historical ActorId. Cookie-only, per-request grants freshness и revoke barrier отменены; E01–E06 ACCEPTED; O01 refresh coordination OPEN. Реализация NOT VERIFIED.

Single VPS/private DB/files — reuse, HTTPS edge/keys/secret management — L3 additions. No Kubernetes/external IdP/object-store requirement. Capacity/resources/domain/storage provider **UNKNOWN**. Неизменяемая история и originals должны переживать account deactivation, backup/restore и upgrades. Политика exceptional deletion теперь спроектирована в ADR-L09/L10 (принципы ACCEPTED ORG-G06, процедуры REQUIRE LEGAL REVIEW PD-G08/09); её нельзя заменить произвольным cleanup.

## Migration facts и будущие изменения

Пять существующих migrations: InitialPersistence (20261007035847), CommandAccessFacts (20261007090442), ImmutableContentSnapshots (20261007103352), AttachmentStorage (20261008013249), ReviewAnnotations (20261008154119). [Каталог](../src/Lms.Infrastructure/Migrations) и model snapshot — фактический источник схемы, ранние SQL sketches Feature не заменяют его. K новых migrations не добавлял.

L1 ACCEPTED T03, будущая реализация: стандартные Identity tables, ActorIdentities, Organization/membership/course scopes, IdentitySessions и single-use challenges по T01–T06; C02 business принят. L2 PROPOSAL: budgets/reservations/cleanup technical metadata; проверить triggers и atomic expiry claim. L3 — deployment/scripts/config без новой Domain модели. Основные таблицы T03/T05 приняты; class/DTO и детали DDL остаются инженерным дизайном. Migration backfill не связывает неизвестные старые UUID с новым человеком автоматически. Upgrades тестировать с immutable legacy history и пустой БД; destructive rollback не рекомендован.

## Доказательства и ограничения

[K results](K-RESULTS.md): 366 frontend/260 backend PASS — **исторический baseline**, в L0 не перезапускался. [K test matrix](K-TEST-MATRIX.md) и [release runner](../src/Lms.Web/scripts/release-check.sh) уже проверяют real DB/API/Chrome, но используют demo bearer/isolated destructive reset. L4 переиспользует сценарий с real Identity accounts, а не запускает destructive runner на pilot data.

[K performance](K-PERFORMANCE-REPORT.md) — single local caller, не capacity certificate. [K security](K-SECURITY-AUDIT.md) S02/S04/S06 и S05 определяют L1–L3; [device protocol](K-DEVICE-TEST-PROTOCOL.md) остаётся физическим acceptance gate. [K gaps](K-MVP-GAPS.md) — names/lookups/teacher materials не решаются автоматически внедрением Identity. [K readiness](K-RELEASE-READINESS.md) NOT_READY не меняется до L4.

Проверка согласованности с [Domain Spec](../docs/domain/LMS-DOMAIN-SPEC.md), [Feature](../docs/features/FEATURE-SUBMISSION-REVIEW.md), [Frontend](../docs/ui/LMS-FRONTEND-CONTRACT.md), [UI Flow](../docs/ui/LMS-UI-FLOW-SPEC.md): state machine/immutable attempts/version/content refs/role scopes/read-only GET/derived progress сохраняются; login/upload/cleanup не создают attempt. Account identity отделена от learning process. Desktop-only — предложение временного pilot acceptance scope, не отмена tablet/mobile UI requirements. [README](../README.md) содержит исторические разделы; актуальный K amendment имеет приоритет над отметками о ещё не реализованных экранах.

## L0.1 — Personal data compliance architecture amendment

База анализа L0.1 `b1e4084`. [Inventory](../docs/compliance/PD-DATA-INVENTORY.md), [data flows](../docs/compliance/PD-DATA-FLOW-MAP.md), [basis](../docs/compliance/PD-PROCESSING-BASES.md), [threat/control matrix](../docs/compliance/PD-SECURITY-CONTROLS.md). Это документационный проект, не новые компоненты в коде. **LEGAL SOURCE NOT VERIFIED**.

- Domain остаётся с теми же статусами/Accepted terminal; immutable learning commands не меняются. Отдельное исключение destruction требует принятого [ADR-L10](../docs/adr/ADR-L10-erasure-and-immutable-history.md) и реализации вне обычных use cases.
- Application получает proposed purpose/block/request boundary по [ADR-L09](../docs/adr/ADR-L09-personal-data-lifecycle.md); проверяется при read/write/queue/attachment, а не только login. Whole-course Teacher grant не обходит privacy block.
- Infrastructure: proposed минимальные basis/version/representative/case/retention metadata рядом с будущей Identity; отдельный security audit и privilege-controlled erasure. Нет автоматического Identity cascade/UUID anonymization. Triggers/FK/JSONB/arrays/files требуют полного manifest.
- API/Web: минимальный onboarding, отдельное consent действие лишь для consent-based целей, verified session/CSRF, no-store и reset/Blob cleanup. Внешний request channel сохраняется после account deactivation. Parent portal, AI/OCR и analytics не добавлены.
- Deployment: РФ placement evidence для всех stores/flows, договоры/поручения, backup encryption/keys, отдельный current erasure/revocation ledger. Restore только quarantine до reconciliation и разрешения ПД; численные RPO/RTO/сроки UNKNOWN.
- Организационный контур: стороны A/P выполняют роли по целям (D4a–d, REQUIRE LEGAL REVIEW), ПД/ИБ/issuer/ops/incident роли назначаются; [документы](../docs/compliance/PD-ORGANIZATIONAL-DOCUMENTS.md) — неподписанные шаблоны. Сторонняя организация входит в первый пилот; её договоры/поручение и legal/isolation evidence ещё обязательны.

Все обязательные [gates](L01-DECISION-GATES.md) и [compliance evidence](../docs/compliance/PD-COMPLIANCE-MATRIX.md) должны быть закрыты до первого пилота (D5). **NOT_READY; compliance NOT VERIFIED.**

## Организационная граница — ACCEPTED L0.3, детали PROPOSED

Organization→Course registry→assignment→work→history/files; LearningSpace отдельной entity пока не нужен. IdentityUser→ActorId→membership→course/group grants; platform permissions отдельно. ProcessingContext/version/basis находится вне учебных агрегатов, наследуется через course root и проверяется вместе с current blocks. [Модель и фактические зависимости](../docs/architecture/ORGANIZATION-MODEL.md), [Identity](../docs/architecture/IDENTITY-ORGANIZATION-SCOPES.md), [изоляция](../docs/architecture/CROSS-ORGANIZATION-ISOLATION.md).

Server boundary покрывает Application policy, LINQ, raw SQL queue/counts, pending/attached files, exports/jobs и maintenance. CourseId остаётся globally unique offering ID; новые registry/FK и проверенный legacy mapping допускают аддитивное расширение без переписывания immutable history. Unknown legacy fail closed. Контракт future ordinary revoke заменён T04/ADR-L15: bounded JWT/deadline/operation duration; emergency и privacy freshness отдельно OPEN.

API routes/DTO/state machine в L0.2 не меняются. В L1 существующие single-context routes могут разрешать scope сервером; неоднозначный upload/list требует отказа/будущего explicit context contract, не угадывания. Platform root технически способен обходить app — нужны ops credentials/audit и договорные ограничения. Compliance NOT VERIFIED, NOT_READY.

## L0.3 — scope L1

L1 — Identity, Organization Scopes & Access Lifecycle, только synthetic data. Юридические consent/basis/representative/case/production erasure workflows исключены до legal review; current restriction contracts и versions проверяются synthetic fixtures без фиктивных оснований. Подробные PK/FK, migration quarantine, API compatibility, use cases и tests — [L03 plan](L03-L1-IMPLEMENTATION-PLAN.md). Scope upload уточняется courseId, auth transport — принятый JWT, browser/refresh E02 ACCEPTED (idle60m/absolute12h, access memory/refresh cookie), CSRF при cookies; остальные business DTO/routes сохраняются. Platform/org admins не получают чтение работ автоматически. Scoped erasure не удаляет общий Identity, immutable history обычных операций не означает вечного хранения. Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY.
