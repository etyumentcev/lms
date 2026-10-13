# MVP-B07b-C2-G — isolated processing writer compatibility

2026-10-11. **ISOLATED IMPLEMENTATION; OWNER ACCEPTANCE PENDING. READY BLOCKED.**

Основание: owner принял C2-F с findings и разрешил C2-G: внутренний исполняемый протокол, additive migration candidate и disposable PostgreSQL tests, без production DI и runtime cutover. [C2-F](MVP-B07b-C2-F-RESULTS.md) остаётся отдельным принятым исследованием. Ни HTTP, ни пользовательский import workflow этим результатом не разрешены.

## Результат и границы

Реализованы общий порядок `Organization → ProcessingGuard → affected rows`, CAS guard revision, атомарные mutation/audit/command journal, закрытые SQL capabilities и отдельные .NET adapters. Старый Serializable snapshot после конкурирующей записи abort-ится; повтор требует новой транзакции, актуальной revision и повторной проверки состояния. Ограниченные operational roles не имеют прямого DML.

**Полный operational cutover заблокирован:** [docker-compose.yml](../docker-compose.yml) использует один bootstrap PostgreSQL principal `lms_local` в `POSTGRES_USER` и общем connection profile для migrate/API. Обычный REVOKE не ограничивает владельца/суперпользователя. Требуется отдельно согласованное разделение deployment/emergency owner и operational credentials, аудит grants и перевод оставшихся writers. Compose, credentials, текущие writers и DI не изменены. Изолированный механизм исполняется; устранение обходов в действующем окружении не заявляется.

Actor → use case: доверенная внутренняя administrative capability изменяет processing state; будущая private write capability проверяет захваченный context перед DB mutation. Это системные capabilities, не новые пользовательские роли/claims/ScopeAction. Серверный adapter обязан самостоятельно разрешить actor/org/course/subjects; переданный DTO не является доказательством полномочий.

Entities/state: immutable context versions; open/closed binding intervals; Active → Withdrawn restriction с row Version; Enabled ↔ Suspended synthetic organization; monotonic guard revision; append-only command journal. Production ImportAttempt не создан. Тестовый Ready marker — только disposable fixture, не реализация draft storage.

## Изменения

| Файл | Назначение |
|---|---|
| [ProcessingStateAdministration.cs](../src/Lms.Application/ProcessingStateAdministration.cs) | Закрытый набор команд, expected revision, OperationId, captured processing evidence; reconciliation contract |
| [PostgresProcessingStateAdministration.cs](../src/Lms.Infrastructure/PostgresProcessingStateAdministration.cs) | Dedicated clean DbContext, Serializable, DB function, COMMIT до возврата результата; без retry |
| [PostgresProcessingWriteBoundary.cs](../src/Lms.Infrastructure/PostgresProcessingWriteBoundary.cs) | DB-only composition boundary будущего writer; общие lock/CAS/current checks |
| [20261011120000_ProcessingWriterGuardC2G.cs](../src/Lms.Infrastructure/Migrations/20261011120000_ProcessingWriterGuardC2G.cs) | SQL-managed additive migration; исторические migrations/model snapshot не изменены |
| [ProcessingWriterCompatibilityTests.cs](../tests/Lms.Infrastructure.Tests/ProcessingWriterCompatibilityTests.cs) | Реальные adapters под ограниченными DB roles, concurrency/fault/migration/privilege tests |

Точный candidate DDL: [Up](evidence/MVP-B07b-C2-G/candidate-up.sql), [Down](evidence/MVP-B07b-C2-G/candidate-down.sql), извлечённые из migration. Migration обнаруживается обычной EF chain: её наличие не является разрешением запускать migrate-only против действующей БД.

Созданы `ProcessingGuards` и `ProcessingWriteJournal`; FK/unique/check constraints и immutable journal trigger. Guard создаётся для существующих Organizations с revision=0 под table lock, а для новых — AFTER INSERT trigger. Это единственный новый hook на существующей таблице; он не переводит legacy writes на новый протокол. SQL функции PUBLIC EXECUTE не получают; migration не создаёт и не наделяет production roles правами.

`processing_change` поддерживает AddContext, Open/Close/ReplaceBinding, Add/WithdrawRestriction, SetOrganizationState. Замена binding атомарно закрывает старый и открывает новый интервал на одной server timestamp. Context version не обновляется in-place. Создание новой версии само по себе не активирует её. SyntheticOnly=false и real approval не доступны.

## Транзакционный контракт

1. Adapter отвергает ambient transaction и tracked entities, открывает собственную Serializable transaction.
2. SQL блокирует Organization, затем guard; missing organization/guard — deny. Guard CAS и revision increment происходят до affected-row DML.
3. Current assertion требует enabled synthetic org, ровно один действующий verified course binding, точное совпадение captured context/version/binding-from, SyntheticOnly approval и непустой полный subject set. Любое Active restriction для context и global/затронутого subject запрещает операцию; opaque resource/operations не сужают запрет.
4. Mutation, SecurityAudit и journal выполняются атомарно. Fingerprint вычисляет PostgreSQL из canonical JSONB action/expected/payload, payload ограничен 4096 bytes; неизвестные поля/операции отвергаются. Повтор того же OperationId/fingerprint возвращает сохранённый результат без повторного эффекта; другой fingerprint — 23505.
5. 40001/40P01 означают rollback и необходимость нового transaction snapshot. Adapter автоматически не повторяет команду. Stale CAS — P0001.
6. Lost COMMIT acknowledgement остаётся unknown. `FindCommittedResult` выполняет только reconciliation под тем же org lock; найденная запись подтверждает commit. Отсутствие записи не доказывает прекращение старого executor и не разрешает автоматический replay/delete.

Boundary callback не является DB security boundary: всякая выдаваемая SQL capability обязана самостоятельно соблюдать порядок и проверку. Тестовая complete-функция делает это независимо; при композиции две guard revisions в одной транзакции намеренны. Будущая ImportAttempt capability должна определить собственные CAS/state/audit/idempotency/TTL, владельца и sealed generation. Эти свойства данным callback не доказаны.

`processing_change` имеет фиксированный search_path `pg_catalog,public,pg_temp`: public нужен неизменённому legacy binding trigger с unqualified ссылкой; pg_temp явно последний. Новые persistent references schema-qualified. Отдельный negative test проверяет temp-table shadowing. CREATE public для operational principal запрещён.

## Writers и совместимость

Полный поисковый inventory: [writer-inventory.txt](evidence/MVP-B07b-C2-G/writer-inventory.txt). Исследованы runtime, administration/provisioning, migrations, tests и tools; отдельного существующего production processing administration adapter не найдено.

| Writer / точный path | Совместимость и минимальное действие |
|---|---|
| Новый PostgresProcessingStateAdministration | Guarded; ограниченной роли выдать только change/find functions после cutover approval |
| Новый PostgresProcessingWriteBoundary | Guarded composition, но SQL callbacks требуют самостоятельного закрытого протокола; DI отсутствует |
| `src/Lms.Infrastructure/LmsDbContext.cs`, `SchemaFoundationMappings.cs`, `SchemaFoundations.cs`: generic EF mapped writes | SaveChanges способен обходить guard под owner; под предложенной operational ролью 42501. Перевести соответствующие команды на новый adapter, убрать table/column/inherited grants |
| `tests/Lms.Infrastructure.Tests/ComponentRestrictionHardeningTests.cs`: add/withdraw restrictions, binding setup | Legacy privileged fixture, bypass разрешён только для изолированного seed/fault. Не использовать как operational path |
| `tests/Lms.Infrastructure.Tests/ComponentIssuanceTests.cs`: binding/restriction setup | То же; существующий тест не переписан, регрессия сохранена |
| `tests/Lms.Infrastructure.Tests/SchemaFoundationTests.cs`: context, binding, restriction EF/raw SQL | Privileged schema/constraint tests; не operational compatibility proof |
| `tests/Lms.Infrastructure.Tests/ScopedAuthorityTests.cs`, `IdentitySessionTests.cs`: org/context provisioning | Guard0 автоматически создаётся новым trigger; seed остаётся privileged. Operational provisioning требует закрытой отдельно согласованной capability |
| `tests/Lms.Infrastructure.Tests/ScopedAuthorityAdditionalTests.cs`: raw organization suspension | Owner bypass; operational аналог обязан использовать SetOrganizationState |
| Исторические `src/Lms.Infrastructure/Migrations/*` | Deployment-owner exception; не изменены. Выполнять при остановленных operational writers и отдельными credentials |
| S3 `s3_change_authority` | Уже начинает с org lock, не меняет перечисленный processing state; семантика сохранена. Общий org lock совместим с протоколом |
| S2 session/identity writers | Не добавляют processing mutations; контракты/lock order не переключались; регрессия обязательна |
| `PostgresCurrentProcessingGate.cs` | Read-only, собственный snapshot; не вызван внутри write transaction. Последующее чтение после restriction commit запрещает доступ |
| Будущий ImportAttempt | Production writer отсутствует. Проверен test-only marker через реальный boundary/grants; production Ready остаётся blocked |
| Database owner/superuser/emergency operator | Может менять rows/DDL/triggers/grants в обход протокола. Отдельный audited/quiesced runbook; не выдавать operational credentials |

`processing_validate_operational_role` — deployment-owner preflight: проверяет reachable role memberships, включая NOINHERIT/SET ROLE, owner/table/column DML, TRIGGER/TRUNCATE, CREATE public, superuser/createrole/createdb/replication/bypassrls и server-file/program roles. Это не универсальный аудит произвольных definer functions/extensions или будущих таблиц. Новую ImportAttempt таблицу/функции требуется добавить в capability audit до cutover.

## PostgreSQL evidence и schedules

Среда: pinned .NET SDK 10.0.401, Testcontainers `postgres:17.6-alpine`, disposable databases с полной EF migration chain. Все fixtures synthetic. Build/test выполнялись в `/tmp/b07c2g/repo`; действующая DB не использовалась. Старые временные логи до восстановления сессии отсутствовали, поэтому результат ниже основан на новом запуске, а не на пересказе прежнего PASS.

Команда:

```sh
cd /tmp/b07c2g/repo
/tmp/b07c2g/sdk/dotnet test tests/Lms.Infrastructure.Tests/Lms.Infrastructure.Tests.csproj -m:1 -p:UseSharedCompilation=false --logger 'trx;LogFileName=infrastructure.trx' --results-directory /tmp/b07c2g/results
```

**PASS: 198 / 198, failed 0, skipped 0**, включая 22 C2-G cases и 176 существующих regression cases. [TRX](evidence/MVP-B07b-C2-G/infrastructure.trx), [log](evidence/MVP-B07b-C2-G/infrastructure.log), [summary](evidence/MVP-B07b-C2-G/test-summary.json). Это один полный запуск окончательных файлов; пересекающиеся проверки отдельно не суммируются.

| Проверка / schedule | Ожидаемое и проверяемое поведение |
|---|---|
| Restriction-first | A actual admin adapter удерживает org/guard до COMMIT; B Ready блокируется (pg_stat_activity); A commits; B 40001; новая B P0001, Validating сохранён |
| Ready-first | A boundary/test complete commits; B restriction старого snapshot 40001; fresh B commit; stored Ready остаётся, последующий current reader deny |
| Phantom insert | Restricted raw INSERT 42501; actual admin adapter ждёт org/guard; после release commits, Ready denied |
| Binding close/replace | Writer commit → stale Ready 40001; fresh captured draft P0001; новый корректный binding разрешает course read, но не rebind старого draft |
| Context version | Новая immutable version вызывает 40001 старого snapshot; fresh retry разрешён, если активный binding не изменён |
| Organization eligibility | Suspension commit → 40001 → fresh P0001; reader deny; положительный контроль Enabled восстанавливает разрешённый read |
| Lock inversion | Privileged affected-row→org конкурирует с adapter org→guard→row: один 40P01, другой 00000. Такой emergency path требует quiescence/того же порядка |
| Journal/audit failure | Injected 23514 до commit: guard, rows, audit, journal rollback; отдельный precommit fault также rollback |
| Replay/CAS | Same operation unchanged; changed fingerprint 23505; stale expected P0001 |
| Lost acknowledgement | Реальный wire proxy теряет COMMIT CommandComplete; adapter не возвращает success; reconciliation находит journal без mutation replay |
| Operational DML | Три restricted roles; INSERT/UPDATE/DELETE/TRUNCATE на девяти таблицах 42501; DDL/disable trigger/replication-role/private helper 42501; actual EF SaveChanges также 42501 |
| Evidence/namespace | Wrong-org context/subject/binding 23503; unsupported real mode P0001; invalid operations 23514; missing context/guard deny; missing subjects ArgumentException; temp shadow 23514 |
| Role profile | Owner, column UPDATE, inherited/NOINHERIT bypass и server-program membership отвергаются 42501 |
| Migrations | S3→C2G/backfill; repeat; unused Down/Up; generated idempotent SQL дважды; injected post-DDL failure откатывает objects/history; fresh retry; used Down P0001 сохраняет history |

SQLSTATE относятся к PostgreSQL exception; EF иногда оборачивает 40001, тесты проверяют base exception. Lost ACK — transport NpgsqlException, ему не приписывается SQLSTATE. Проверки выполняют реальные предлагаемые .NET adapters под SET ROLE; owner connection используется только для disposable setup, faults и наблюдения. Ready table/function — тестовая, и её успех не доказывает production ImportAttempt implementation.

## Подтверждённое и недоказанное

Подтверждение ограничено воспроизводимыми schedules, SQL capabilities и указанными grants. Протокол предотвращает phantom bypass для этих operational roles и повторно проверяет состояние на fresh retry; audit/journal атомарны. Полная EF chain/S2/S3/processing regression входит в infrastructure suite.

**NOT PROVEN / BLOCKED:** закрытие всех privileges в реальной deployment DB; произвольные сторонние SECURITY DEFINER functions; complete future ImportAttempt authorization/owner/TTL/storage contract; physical COMMIT до TTL; bounded clock/execution G02. C2-F FS probes здесь не повторялись, FS/HTTP/browser tests NOT RUN (нет новых соответствующих paths). Транзакционные PASS не закрывают G02 и не разрешают Ready.

После использования guard/journal Down намеренно запрещён: retention decision отсутствует. При отказе cutover остановить ingress и сохранить данные; нельзя удалять journal/guard ради rollback.

## План безопасного runtime cutover — отдельный slice

1. Owner принимает C2-G и отдельно определяет scope runtime wiring/provisioning/credentials. До этого Ready/HTTP deny сохраняются.
2. Инвентаризировать реальные principals, memberships, owners, default/table/column/function grants, SECURITY DEFINER и extensions; сверить все writers с таблицей выше. Не переносить bootstrap owner в runtime.
3. Разделить migration/emergency owner и ограниченные operational principals. Согласовать закрытый provisioning contract; emergency writes требуют остановки writers и того же lock order, audit и reconciliation.
4. В controlled window применить additive migration, проверить backfill/guard для каждой org. Запретить bypass DML/DDL и опасные inherited capabilities; выдать только необходимые closed functions. Запустить preflight и независимые raw DML/SET ROLE/function negative tests для фактических logins.
5. Атомарно перевести все relevant operational writers на adapters. Не оставлять fallback к SaveChanges/owner profile. Прогнать fresh snapshot/concurrency/unknown-outcome и S2/S3 regression на изолированной копии конфигурации.
6. Только после отдельного разрешения подключить DI/runtime. Future ImportAttempt SQL capability, immutable owner/context, sealed generation, expiry/read checks, journal и retention — отдельный implementation slice; повторить privilege audit уже с её таблицами/functions.
7. Rollback: остановить новые writes/ingress, reconciliation pending outcomes, сохранить journal/audit/guard. Destructive Down после use не выполнять. B08, PRS1 exact, G02 и S4 HTTP не включать.

## Guardrails §7 / preservation

| Пункт | Статус |
|---|---|
| Actor/use case/invariants/states | covered — внутренние system capabilities и состояния описаны выше |
| S2/S3/PRS1 и gates | covered — семантика не переключена; conservative restrictions сохранены |
| Org/course/group/ownership | covered для DB provenance/current binding; user assignment/immutable ImportAttempt owner blocked до server adapter; новых пользовательских access paths нет |
| Subjects/embedded sources | covered — missing evidence deny; полнота server resolution blocked для будущего import |
| Lists/counts/aggregates | not applicable — новые list/query/UI endpoints отсутствуют |
| Attachments/history | history covered journal immutability; attachments not applicable — FS/delivery не добавлены |
| Transaction/locks/CAS/retry/unknown | covered — исполняемый protocol и fault schedules |
| Validation/limits/errors | covered — whitelist, bounded payload/subjects, domain/constraint failures |
| Tests | integration + negative + regression; browser/E2E not applicable — нет UI/HTTP |
| Scope/schema | additive candidate явно разрешён C2-G; исторические migrations/runtime/DI не менялись |
| Real data/secrets/release | synthetic-only, production access отсутствует; readiness не заявлена |
| Evidence | TRX/log и ограничения выше; G02/physical deadline NOT PROVEN |

Специалистские skills `model-domain`, `implement-use-case`, `implement-feature` недоступны в текущем каталоге; их исполнение не заявляется. Обязательные guardrails/reconciliation прочитаны.

Ветка `codex/l1-s4-jwt-api-authorization`, HEAD `492e30aef65bbddd24f107903bfaa54a563abe39` и index tree сохранены. Все 509 файлов baseline восстановленной сессии неизменны, включая пять ранее созданных C2-G исходников и посторонние dirty files; добавлены только этот отчёт и evidence. Первоначальный временный baseline до реализации утрачен, поэтому новый hash-check доказывает сохранность с момента восстановления сессии, а не заново весь прошлый этап. [Проверка](evidence/MVP-B07b-C2-G/preservation.json). Commit/push/merge/rebase/deploy не выполнялись. Предсуществующий whitespace finding `docs/content/COURSE-CONTENT-SPEC.md:1325` не исправлялся.

**C2-G OWNER ACCEPTANCE PENDING · READY BLOCKED · SERVER/HTTP CUTOVER NOT AUTHORIZED · B08 NOT AUTHORIZED · PRS1 EXACT OFF · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**
