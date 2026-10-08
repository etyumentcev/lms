# L — Architecture baseline and proposed pilot boundary

Дата: 2026-10-09. FACT baseline `c7a500f56f26d94e3249a8c8a87e76018b0f5683`, ветка результата `codex/l0-architecture`. L0 меняет только документацию. **NOT_READY** из K сохраняется. D1 VPS/сервер, D2 Compose, D3 ASP.NET Core Identity приняты владельцем; всё будущее устройство ниже PROPOSAL. [Decision register](L-DECISION-REGISTER.md) отделяет accepted/technical/owner, [план](L-IMPLEMENTATION-PLAN.md) — будущие этапы.

## FACT — слои и точки интеграции

| Компонент | Подтверждённая ответственность | Reuse / будущая граница |
|---|---|---|
| [Lms.Domain](../src/Lms.Domain/ProblemWork.cs), [History](../src/Lms.Domain/History.cs), [ProblemAssignment](../src/Lms.Domain/ProblemAssignment.cs) | Aggregate ProblemWork, immutable Submission/Review/Message, exact content reference; четыре статуса; annotations VO | Не добавлять IdentityUser, sessions, quota, admin role или новый status в Domain |
| [Lms.Application Ports](../src/Lms.Application/Ports.cs), [ProblemWorkService](../src/Lms.Application/ProblemWorkService.cs) | Actor(Guid,AuthorRole), IAccessPolicy/IWorkStore; role/access→aggregate→atomic save | Identity host adapter выдаёт тот же Actor; permissions продолжают проверяться на backend |
| [LmsDbContext](../src/Lms.Infrastructure/LmsDbContext.cs), [PostgresWorkStore](../src/Lms.Infrastructure/PostgresWorkStore.cs) | PostgreSQL/EF; compare-and-save Version, append-only history/events; split history snapshot | Additive identity/resource metadata, не переписывать историю; restore сохраняет schema/triggers |
| [AccessFacts](../src/Lms.Infrastructure/AccessFacts.cs), [PostgresAccessPolicy](../src/Lms.Infrastructure/PostgresAccessPolicy.cs) | Trusted enrollment/lesson/teacher grants; no public CRUD | Operator provisioning; роль не равна scope, Identity не выдаёт enrollment автоматически |
| [PostgresWorkspaceQueries](../src/Lms.Infrastructure/PostgresWorkspaceQueries.cs), [PostgresReviewQueue](../src/Lms.Infrastructure/PostgresReviewQueue.cs), [PublishedContent](../src/Lms.Infrastructure/PublishedContent.cs) | Exact immutable snapshots; scoped server projections/queue filters/paging; read не создаёт work | Existing read DTOs и capabilities остаются; trusted profile enrichment только отдельным решением |
| [AttachmentService](../src/Lms.Infrastructure/AttachmentService.cs), [LocalFileAttachmentStorage](../src/Lms.Infrastructure/LocalFileAttachmentStorage.cs) | Original bytes вне DB/web root, random keys; metadata/claim/tombstone; parent-work auth | Resource admission/ledger/cleanup, backup barrier; private storage сохранить |
| [Program](../src/Lms.Api/Program.cs), [ActorMapper](../src/Lms.Api/ActorMapper.cs) | JWT signature/issuer/audience/lifetime → claims → Actor; health/migrate/dev guards | Identity cookie host integration; CSRF/session validation; no demo fallback в pilot |
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
Browser -> HTTPS same-origin edge -> API cookie/session validation
    -> IdentityUserId -> trusted active Actor mapping -> existing access policy
    -> ProblemWorkService -> Domain -> PostgresWorkStore
Browser -> authorized attachment GET -> private original bytes
Operator -> trusted provisioning -> Identity + mapping + access facts
Maintenance barrier -> DB + originals + keys/config -> encrypted off-host set
```

Cookie transport — B01, owner provisioning/recovery — C01/C02. Cookie principal не должен отдавать Identity UUID как historical ActorId. Server session check и account mapping не заменяют per-request enrollment/grants. In-flight request до revoke может закончиться: strict serializable admin revocation не реализована и не обещана. [ADR-L01](../docs/adr/ADR-L01-identity-integration.md) задаёт migration/compatibility и session tests.

Single VPS/private DB/files — reuse, HTTPS edge/keys/secret management — L3 additions. No Kubernetes/external IdP/object-store requirement. Capacity/resources/domain/storage provider **UNKNOWN**. Неизменяемая история и originals должны переживать account deactivation, backup/restore и upgrades. Политика exceptional deletion остаётся owner/privacy gate, её нельзя заменить произвольным cleanup.

## Migration facts и будущие изменения

Пять существующих migrations: InitialPersistence (20261007035847), CommandAccessFacts (20261007090442), ImmutableContentSnapshots (20261007103352), AttachmentStorage (20261008013249), ReviewAnnotations (20261008154119). [Каталог](../src/Lms.Infrastructure/Migrations) и model snapshot — фактический источник схемы, ранние SQL sketches Feature не заменяют его. K новых migrations не добавлял.

L1 PROPOSAL: стандартные Identity tables, ActorIdentities, IdentitySessions и single-use challenges после C02. L2 PROPOSAL: budgets/reservations/cleanup technical metadata; проверить triggers и atomic expiry claim. L3 — deployment/scripts/config без новой Domain модели. Новые table/class/DTO names в ADR — предложения. Migration backfill не связывает неизвестные старые UUID с новым человеком автоматически. Upgrades тестировать с immutable legacy history и пустой БД; destructive rollback не рекомендован.

## Доказательства и ограничения

[K results](K-RESULTS.md): 366 frontend/260 backend PASS — **исторический baseline**, в L0 не перезапускался. [K test matrix](K-TEST-MATRIX.md) и [release runner](../src/Lms.Web/scripts/release-check.sh) уже проверяют real DB/API/Chrome, но используют demo bearer/isolated destructive reset. L4 переиспользует сценарий с real Identity accounts, а не запускает destructive runner на pilot data.

[K performance](K-PERFORMANCE-REPORT.md) — single local caller, не capacity certificate. [K security](K-SECURITY-AUDIT.md) S02/S04/S06 и S05 определяют L1–L3; [device protocol](K-DEVICE-TEST-PROTOCOL.md) остаётся физическим acceptance gate. [K gaps](K-MVP-GAPS.md) — names/lookups/teacher materials не решаются автоматически внедрением Identity. [K readiness](K-RELEASE-READINESS.md) NOT_READY не меняется до L4.

Проверка согласованности с [Domain Spec](../docs/domain/LMS-DOMAIN-SPEC.md), [Feature](../docs/features/FEATURE-SUBMISSION-REVIEW.md), [Frontend](../docs/ui/LMS-FRONTEND-CONTRACT.md), [UI Flow](../docs/ui/LMS-UI-FLOW-SPEC.md): state machine/immutable attempts/version/content refs/role scopes/read-only GET/derived progress сохраняются; login/upload/cleanup не создают attempt. Account identity отделена от learning process. Desktop-only — предложение временного pilot acceptance scope, не отмена tablet/mobile UI requirements. [README](../README.md) содержит исторические разделы; актуальный K amendment имеет приоритет над отметками о ещё не реализованных экранах.
