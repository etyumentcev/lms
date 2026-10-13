# MVP-B07b-C2-H — isolated credentials and writer cutover

2026-10-11. **ISOLATED PROFILE VERIFIED WITH BLOCKERS · CUTOVER STOPPED · OWNER ACCEPTANCE PENDING.**

Основание: явное owner authorization C2-H в текущей задаче; C2-G принят. Историческая надпись acceptance pending в C2-G не переопределяет новое решение. ProcessingGuard и принятые adapters не перепроектированы.

## Результат

Создан воспроизводимый disposable profile: PostgreSQL 17.6, отдельные настоящие LOGIN principals для migration owner, application runtime и processing executor. Credentials генерируются в памяти; `SET ROLE` от bootstrap не используется как замена runtime authentication. C2-G применяется полным EF migration chain под non-superuser owner. Приложение запускается существующим `ApiFactory`/WebApplicationFactory с runtime connection string; synthetic Submit → Review → Revise → Accept проходит.

**Полный cutover не выполнен:** два существующих compatibility paths несовместимы с принятым C2-G operational preflight. По указанию owner «зафиксировать точный блокер и остановить cutover» переключение остановлено. Успех отрицательного теста S2 означает подтверждение блокера, а не работающий restricted S2 deployment. Production DI, HTTP implementation, действующий compose и credentials не менялись.

## Контракт и границы

Actor → use case: deployment operator создаёт только disposable cluster/roles/schema; внутренний processing executor вызывает принятые команды; application runtime исполняет существующие application paths. Educational roles не получают административные capabilities.

Invariants → entities/VO → transitions: synthetic-only Organization; immutable ProcessingContextVersion; binding intervals; Active → Withdrawn restrictions; monotonic ProcessingGuard revision; append-only command journal. Command содержит OperationId и ExpectedRevision. Новых ImportAttempt/Ready entities нет.

Authorization/data sources: ресурсы и текущие ограничения берутся из PostgreSQL; test-only authentication использует существующий test assembly handler. Он не доказывает S4 HTTP authorization. Пользовательские claims/ScopeAction/routes не добавлены. Для processing command сохраняется C2-G transaction ownership: fresh dedicated context, Serializable, Organization → Guard → affected rows, CAS, атомарные mutation/audit/journal и COMMIT до success. 40001/40P01 требуют fresh transaction/recheck; lost ACK остаётся unknown, автоматического replay нет. Privileged provisioning — только synthetic fixture до operational work.

## Фактические principals / grants

Полный исполняемый allowlist: [profile.sql](../tools/processing_isolated/profile.sql). Launcher и независимые assertions: [IsolatedProcessingDeploymentTests.cs](../tests/Lms.Api.Tests/IsolatedProcessingDeploymentTests.cs).

| Principal | Ownership и атрибуты | Фактический доступ |
|---|---|---|
| `postgres` bootstrap | Disposable cluster bootstrap, superuser; не передаётся application/executor | Создание 3 LOGIN roles и disposable DB. После migration закрывает PUBLIC EXECUTE на extension functions и оставляет EXECUTE migration owner. Не используется для application commands |
| `c2h_owner` migration/deployment | LOGIN, NOSUPERUSER/NOCREATEDB/NOCREATEROLE/NOINHERIT/NOREPLICATION/NOBYPASSRLS; owner DB, application tables/functions | Полный deployment/schema/fixture доступ; может обходить guard по определению owner. Credential отсутствует в runtime host |
| `c2h_runtime` application | Те же отрицательные role flags; memberships отсутствуют; не owner | CONNECT, USAGE public; SELECT на явном списке application/S2/S3/processing-reader tables; ограниченные INSERT/UPDATE ниже; EXECUTE только `s3_change_authority` среди public functions |
| `c2h_processing` executor | Те же отрицательные role flags; memberships отсутствуют; не owner | CONNECT, USAGE public; EXECUTE только `processing_change(uuid,uuid,bigint,text,jsonb)` и `processing_find_command(uuid,uuid,bigint,text,jsonb)`. Никаких прямых table privileges |

`public` schema принадлежит стандартному `pg_database_owner` (через ownership DB — migration owner). Все application tables и non-extension functions принадлежат `c2h_owner`. Trusted `btree_gist` устанавливает C functions с owner `postgres`; проверка допускает только extension membership btree_gist + owner postgres + NOT SECURITY DEFINER. Это не operational membership. Bootstrap ACL step необходим: non-superuser migration owner сам не владеет этими extension functions. Runtime/executor не имеют EXECUTE ни одной из них.

Runtime SELECT: AccessEnrollments, AccessLessons, AccessTeachers, AccessAttachments, StoredAttachments, PublishedContentVersions, ProblemAssignments, ProblemWorks, Submissions, Messages, Reviews, DomainEvents, Organizations, Actors, ActorIdentities, AccountSecurityState, AspNetUsers, IdentitySessions, RefreshVerifiers, OrganizationMemberships, EnrollmentScopes, TeacherAssignmentScopes, CourseRegistry, CourseGroups, AssignmentScopes, AuthorityHistory, AttachmentScopes, ProcessingContextVersions, CourseContextBindings, ProcessingRestrictions.

Runtime INSERT: ProblemWorks, Submissions, Messages, Reviews, DomainEvents, AccessAttachments, StoredAttachments, IdentitySessions, RefreshVerifiers. UPDATE: ProblemWorks(Status,Version,AcceptedAt), AccessAttachments(IsEligible), StoredAttachments(SubmissionId,MessageId,Deleted), IdentitySessions, RefreshVerifiers, AccountSecurityState(UserId), ActorIdentities(UserId), AspNetUsers(PasswordHash). Последние column grants нужны существующим S2 FOR UPDATE/rehash paths; профиль остаётся candidate, не утверждение о завершённом аудите всей identity security boundary. DELETE/TRUNCATE/TRIGGER grants отсутствуют.

Нет прямого DML на Organizations, context versions, bindings, restrictions, guard, journal, SecurityAudit. Нет CREATE schema/database/public, TEMP, SET ROLE к owner/bootstrap/другому executor, role inheritance и опасных role attributes. Default owner grants на tables/sequences отсутствуют; PUBLIC EXECUTE для новых owner functions отозван. Проверено создание будущей table/function и отказ обеим operational ролям. При новых migration/functions аудит и explicit grants необходимо повторять; default deny сам по себе не доказывает безопасность будущего SQL.

SECURITY DEFINER: runtime allowlist — существующий S3 capability; executor allowlist — две принятые C2-G functions. Private helpers не callable напрямую. Сохранены фиксированные C2-G search paths; operational CREATE/TEMP запрещены. Изменение functions/triggers от operational logins запрещено. Политика не претендует на доказательство отсутствия уязвимостей PostgreSQL или произвольных будущих extensions.

## Writers: переключённые и оставшиеся

| Path | Фактическое состояние |
|---|---|
| C2-G `PostgresProcessingStateAdministration` | В isolated launcher работает только с `c2h_processing`; AddContext/OpenBinding/CloseBinding/AddRestriction/WithdrawRestriction/SetOrganizationState проверены. Privileged EF fallback отсутствует |
| Generic EF processing mutations | От обоих operational logins отвергаются; реальный SaveChanges для Organization получает 42501 |
| Existing work store / HTTP commands | Не переписаны; in-process app использует только runtime login, workflow проходит. Seed/verification owner context отдельно от app |
| S3 administration | Runtime имеет только существующую SQL capability. Полная S3 regression прошла в стандартных fixtures; полный S3 component matrix под новым login NOT RUN |
| S2 Authenticate/Rotate/Revoke/RevokeAll audit writer | **BLOCKED H01**; Authenticate реально откатывается на SecurityAudit. Остальные используют тот же Audit/SaveChanges путь, их restricted success не заявляется |
| Component token issuer | **BLOCKED H02**; требует Organization FOR UPDATE. Проверен отказ SQL; end-to-end issuer под новым profile NOT RUN |
| Identity mapping, org/course provisioning | Только privileged synthetic fixtures; operational onboarding не подключён. Identity mapping также пишет SecurityAudit |
| C2-G `PostgresProcessingWriteBoundary` | Принятый isolated regression сохранён; в новом application profile не подключён. Production ImportAttempt отсутствует, Ready заблокирован |
| Migration/emergency writers | Только deployment owner при остановленных operational writers; не runtime fallback |

Полный исходный inventory: [C2-G writer inventory](evidence/MVP-B07b-C2-G/writer-inventory.txt); C2-H дополнительно проверил S2 audit и token-issuer row locks. Никакого атомарного переключения всех writers после выявления blockers не было.

## Точные blockers

**H01 — S2 audit vs C2-G preflight.** `PostgresIdentitySessions.Audit` добавляет SecurityAudit entity, Authenticate/Rotate/revoke сохраняют его через SaveChanges в своей transaction. У runtime нет INSERT → PostgreSQL 42501 с `SecurityAudit` в message; после Authenticate в DB нет session. Если внутри диагностической transaction дать INSERT SecurityAudit, `processing_validate_operational_role('c2h_runtime')` возвращает 42501. Transaction с пробным grant откатывается, отсутствие INSERT проверяется заново. Это прямой конфликт существующего S2 writer с принятым operational predicate, а не отсутствующий пароль или transient failure.

**H02 — issuer Organization lock vs запрет column UPDATE.** `PostgresComponentTokenIssuer.Issue` выполняет `SELECT ... Organizations ... FOR UPDATE`. PostgreSQL требует UPDATE privilege хотя бы на одну колонку; новый login получает 42501. C2-G preflight запрещает и table, и любой column INSERT/UPDATE на Organizations. Выдача UPDATE для обхода проверки или удаление lock меняли бы принятую защиту/issuance serialization, поэтому не сделаны.

Предлагаемый следующий узкий slice: отдельно согласовать закрытую S2 audit capability с ограниченным набором audit actions/fields и сохранением transaction atomicity; отдельную закрытую organization-lock capability для issuer без DML grant и без изменения lock order/JWT temporal semantics. Затем адаптировать существующие component adapters, расширить capability audit и повторить restricted S2/S3/issuance matrix. Здесь эти capabilities не реализованы и ProcessingGuard не изменён.

## Выполненные проверки

Финальная команда в временной копии канонического репозитория:

```sh
cd /tmp/b07c2h/repo
/tmp/b07c2g/sdk/dotnet test Lms.sln -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/b07c2h/final-results
```

**PASS 704/704, failed 0, skipped 0:** Domain 43, Application 272, Infrastructure 198, API 191 (186 существующих + 5 C2-H). [Summary](evidence/MVP-B07b-C2-H/test-summary.json), [log](evidence/MVP-B07b-C2-H/regression.log), TRX в том же evidence directory. Это один финальный solution run; предыдущие диагностические прогоны не суммируются. Initial C2-H ownership assertion ошибочно требовал migration-owner для 188 extension functions; затем при PUBLIC revoke обнаружился нужный owner EXECUTE. Финальный launcher учитывает оба факта, финальный run зелёный.

| Обязательство | Фактическое evidence / ограничение |
|---|---|
| App startup / synthetic workflow | Новый C2-H test: existing ASP.NET app/TestServer, restricted authenticated DB login; health, Submit → Review → Revise → Accept, две сохранённые submissions; foreign student denied. Это in-process application startup, не Kestrel/edge deployment и не S4 HTTP acceptance |
| C2-G migration / guards | Owner применяет S3, seed существующей org, additive C2-G; backfill revision 0; новая org получает guard; repeat Migrate дважды/profile повторно не меняют использованный guard |
| Direct DML/DDL / EF | Обе роли: INSERT/UPDATE/DELETE/TRUNCATE/disable triggers на 7 защищённых tables → 42501; CREATE table/schema, function alter, private helper, replication role → 42501; EF SaveChanges → 42501 |
| Principals / SET ROLE / defaults | Реальные session_user assertions; нет memberships/unsafe flags/CREATE/TEMP; SET ROLE owner/bootstrap/peer denied; future object access denied; public function allowlist точно 1/2 |
| Inherited/NOINHERIT grants | Перезапущенный C2-G test `Operational_profile_preflight_rejects_owner_inherited_and_column_DML`: опасные reachable roles/column grants/server-program membership отвергаются. Новый profile дополнительно требует zero memberships; отдельная инъекция inheritance в его реальные logins NOT RUN |
| Restriction-first / Ready-first | C2-G final regression: real adapters, org/guard wait, old snapshot 40001, fresh Ready denied; обратный порядок — Ready commit, restriction fresh commit, subsequent current read deny |
| Context/binding races | C2-G final regression: stale snapshot abort, fresh recheck, close/replace binding и context changes. Новый login profile отдельно проверяет обычные open/close команды |
| Snapshot/deadlock/fresh retry | C2-G final regression: 40001/40P01 и fresh transaction/recheck; никакого автоматического retry не добавлено |
| Lost COMMIT ACK | C2-G final regression: реальный wire proxy теряет acknowledgement, reconciliation по journal, без replay |
| Idempotent migrations/fault rollback | C2-G final regression: повтор idempotent script, rollback injected failure/fresh retry, использованный Down запрещён; C2-H повтор полного Migrate под non-superuser owner |
| S2/S3/JWT semantics | Стандартная regression PASS, source implementation неизменна; новый profile несовместим с H01/H02, successful restricted S2/issuance не заявляется |
| Новые пользовательские полномочия | Production source/DI/routes/claims/enums неизменны; только SQL principals и test host. Нет user-facing processing administration path |

Concurrency/fault evidence относится к C2-G disposable fixtures с ограниченными SET ROLE principals, а не к завершённому deployment C2-H. Повтор всей этой матрицы через новые LOGIN credentials остаётся обязательным после снятия H01/H02. Python content compiler/import, browser/device/edge/storage probes NOT RUN — этот slice их не меняет. G02 не закрыт.

## Подтверждение отсутствия processing bypass и его пределы

В проверенном isolated profile runtime login не владеет защищёнными объектами, не наследует owner, не может SET ROLE, менять processing rows через raw SQL/EF, менять functions/triggers, создавать shadow objects или вызывать processing helpers/admin functions. Доступный S3 capability не изменяет processing state; закрытые processing mutations доступны только отдельному executor через C2-G. Это подтверждение конкретного profile, не действующего deployment и не общей защищённости всех application данных от компрометации runtime credential.

Owner/bootstrap остаются технически привилегированными: их credentials не допускаются в application process. Test launcher как deployment orchestrator знает owner и создаёт отдельный runtime host; это не production secret-delivery implementation. Profile intentionally не пригоден для cutover до H01/H02. Полнота restricted S2/S3, app-wide guard integration, production ImportAttempt, future SQL capabilities, real-data/legal gates и G02 остаются NOT PROVEN/BLOCKED.

## Точный controlled runtime cutover plan

1. Owner принимает blockers/evidence C2-H и определяет следующий scope устранения H01/H02; текущий runtime не переключать.
2. Реализовать согласованные closed audit/organization-lock capabilities; сохранить S2 transaction owner/lock order, JWT delayed revocation, audit atomicity, no replay. ProcessingGuard semantics не ослаблять.
3. На новом disposable cluster повторить non-superuser migration owner, bootstrap extension ACL lockdown, guard backfill/new-org checks, explicit allowlists/default grants. Проверить все owners, memberships (включая SET ROLE), columns, functions, triggers и extensions перед app start.
4. Запустить реальные runtime/executor logins: полный S2 authenticate/rotate/revoke/issue и S3 matrix; все необходимые processing writers только C2-G; owner отсутствует в их connection factories, fallback запрещён. Provisioning contract должен быть явно закрыт или оставаться quiesced deployment-only.
5. Повторить все mandatory concurrency/fault/unknown-outcome schedules именно с этими logins и synthetic workflow. Любой несовместимый writer снова останавливает cutover. Сверить сохранность user permissions и exact PRS1 OFF.
6. Только после отдельного owner authorization действующего runtime: остановить ingress/writers; reconcile неизвестные outcomes; применить additive migration/ACL; проверить guards; атомарно заменить connection profile/разрешённое wiring; выполнить synthetic smoke и снять блокировку ingress лишь после preflight. Эта задача не разрешает этот шаг или новый HTTP runtime.
7. При отказе: остановить ingress/writes, сохранить guard/journal/audit и выполнить reconciliation. Не возвращать owner credential в runtime и не делать destructive Down после использования. B08/publication/pin/Ready/G02/S4 HTTP не включать.

## Изменённые файлы и preservation

Добавлены только `tools/processing_isolated/profile.sql`, `tests/Lms.Api.Tests/IsolatedProcessingDeploymentTests.cs`, этот отчёт и `codex/evidence/MVP-B07b-C2-H/{regression.log,test-summary.json,*.trx,preservation.json}`. Существующие C2-G исходники, migrations, HTTP runtime, production DI и compose не менялись.

Branch `codex/l1-s4-jwt-api-authorization`, HEAD `492e30aef65bbddd24f107903bfaa54a563abe39`, index и hashes предсуществующих файлов сверены с entry baseline: [preservation](evidence/MVP-B07b-C2-H/preservation.json). Dirty checkout не переключался на main. Commit/merge/rebase/push/deploy не выполнялись. Предсуществующий whitespace finding `docs/content/COURSE-CONTENT-SPEC.md:1325` оставлен.

## Guardrails §7

| Проверка | Статус |
|---|---|
| Actor/use case/invariants/states | covered — контракт выше, synthetic-only |
| S2/S3/PRS1/blocked gates | covered — source unchanged; H01/H02 явно блокируют cutover; exact OFF |
| Org/course/group/ownership | covered existing regression; новых user access paths нет; restricted S2/issuer BLOCKED |
| Subjects/embedded sources | covered C2-G missing-evidence regression; нового import path нет |
| Lists/counts/aggregates | covered existing API regression; новых queries нет |
| Attachments/history | covered existing regression; новый isolated workflow без attachments; storage probes N/A — не менялись |
| Transaction/CAS/retry/unknown | covered C2-G regression; no replay; profile-wide rerun после blockers обязателен |
| Validation/limits/errors | covered SQLSTATE/allowlist/negative controls; fail closed |
| Unit/integration/negative/E2E | 704 PASS; TestServer synthetic path; browser/device N/A, production E2E NOT RUN |
| Scope/schema | owner-authorized isolated roles/grants; C2-G migration unchanged; runtime cutover stopped |
| Real data/secrets/release | synthetic-only; credentials random in memory; no secrets in artifacts; no readiness claim |
| Evidence/gaps | exact final command/TRX; profile compatibility BLOCKED, G02 NOT PROVEN |

`model-domain`, `implement-use-case`, `implement-feature` отсутствуют в доступном skill catalog; исполнение не заявляется. Прочитаны обязательные guardrails/reconciliation, C2-G и применимые ADR-L15/L18.

**C2-G ACCEPTED · C2-H OWNER ACCEPTANCE PENDING WITH BLOCKERS · CUTOVER STOPPED · READY BLOCKED · SERVER/HTTP CUTOVER NOT AUTHORIZED · B08 NOT AUTHORIZED · PRS1 EXACT OFF · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**
