# MVP-B07b-C2-F — isolated persistence validation

2026-10-11 · **VALIDATION COMPLETED WITH FINDINGS · OWNER ACCEPTANCE PENDING · READY IN CURRENT RUNTIME BLOCKED**.

Основание: явное owner decision в задании — B07b-C2 и OD-C2-01/02 accepted with conditions; разрешены только isolated persistence validation, disposable synthetic PostgreSQL и private FS. Оно заменяет прежние `PROPOSED/NOT ACCEPTED` формулировки в [C2 design](MVP-B07b-C2-INTEGRATION-DESIGN.md), но не разрешает server implementation. Старые checkpoint документы оставлены без изменений.

## 1. Результат и область доказательства

Минимальные отдельные ImportAttempt, command/transition journal и guard пригодны как основа протокола: на disposable PostgreSQL исполнены CAS, закрытые transitions, атомарные audit/journal/metadata, сохранение tombstones, запрет serving DML и согласованные concurrency schedules. Private FS подтвердил no-replace seal, вызовы fsync, недоступность orphan и сохранение bytes при unknown outcome.

**Общая writer compatibility НЕ подтверждена.** Реальный EF writer и прямые SQL paths существующих processing tables обходят кандидатный guard. Исполнен контрпример: restriction commit раньше Ready commit, но Serializable importer со старым snapshot всё равно сохраняет Ready. Последующий fresh read отказывает. Поэтому green probes совместимого протокола не разрешают Ready в текущем приложении.

Второй установленный предел: проверка времени после locks запрещает Ready при уже наступившем expiry, но не исключает commit после expiry, если транзакция остановилась после последней проверки времени. Fresh read после expiry отказывает без physical cleanup. Строгая commit-deadline guarantee **NOT PROVEN**, G02 не закрыт.

Изменены только этот отчёт и [evidence directory](evidence/MVP-B07b-C2-F/). Нет изменений production runtime, application schema, EF migrations, API, frontend, DI, authorization, processing writers или owner register. Никаких HTTP/file delivery, B08/pin/publication, новых claims/roles приложения/ScopeAction/PRS1 operations. Экспериментальные PostgreSQL NOLOGIN principals из disposable cluster не являются образовательными ролями.

## 2. Feature contract и доверенная граница

Actor → server-resolved synthetic author с действующим whole-course TeacherAssignmentScope, Teacher membership той же generation/org и immutable ownership → admission, source seal, transition/Complete, metadata read predicate → неизменяемые org/course/owner/context/binding/TTL, текущий processing deny, CAS и атомарный audit → ImportAttempt + ImportTransitions + ProcessingGuard → Receiving → Validating → Ready либо Invalid/Failed/Cancelled, затем Expired → authority/context/role/FS/concurrency probes.

| Источник | Проверка в эксперименте | Граница |
|---|---|---|
| S2 identity/session | Существующие S2 и S4 component issuance/ACK tests в regression; synthetic actor задан trusted harness | Нового session/server adapter нет; пользовательские import/preview остаются deny |
| S3 authority | Fixtures выданы существующей `public.s3_change_authority`; JOIN membership/TeacherAssignmentScope по org/actor/generation, GroupId=NULL, Active, Verified CourseRegistry | SQL predicate моделирует fresh job boundary; не вводит per-request JWT revoke barrier ADR-L15/16 |
| Attempt | org+owner predicate до metadata result; owner, course и контекст immutable | Possession ImportId не даёт доступа |
| Processing | Один действующий binding, same-org context, SyntheticOnly, enabled org; любой Active restriction контекста запрещает synthetic operation | Намеренно консервативнее subject matching. Нет opaque ResourceRef/operation-mask interpretation или PRS1 exact |
| Files | Только заранее заданные synthetic bytes и server-generated generation; закрытый manifest | Не ZIP ingress, не C1 compiler integration, не content delivery |
| Internal executor | NOLOGIN role получает только EXECUTE фиксированных функций; PUBLIC EXECUTE revoked, fixed search_path | Его параметры trusted. Нельзя предоставить эту capability клиенту или serving principal |

Student, OrgAdmin membership flags, group-only teacher, wrong course/org/owner, revoked membership и stale membership generation проверены отрицательно. Platform operator/Course Read не являются входами candidate predicate и не предоставляют новых прав; отдельного платформенного authentication adapter в эксперименте нет. Реальных student/teacher данных нет.

Отсутствующие skills `model-domain`, `implement-use-case`, `implement-feature` не выполнялись. Прочитаны repository AGENTS, guardrails/reconciliation, C2 design, ADR-L15/16/18 и S3 authority contract.

## 3. Candidate DDL и transaction protocol

**Полный исполнявшийся DDL:** [candidate.sql](evidence/MVP-B07b-C2-F/probe/candidate.sql). Это экспериментальный schema `c2f`, не migration и не готовая схема runtime. [Program.cs](evidence/MVP-B07b-C2-F/probe/Program.cs) содержит fixed fixtures и assertions. Ни один существующий writer не переписан.

| Объект | Существенные ограничения |
|---|---|
| `c2f.ImportAttempt` | UUID id; immutable org/course/owner/context/version/binding/admission/expiry/admission command+fingerprint; same-org course/context FKs, Actor FK, binding reference; 24h TTL CHECK; enum state; Version; epoch; source/result/generation; cleanup marker |
| `c2f.ImportTransitions` | PK(import_id,operation_id), unique(import_id,to_version), fingerprint, from/to version, outcome, unique FK на существующий SecurityAudit; append-only trigger |
| `c2f.ProcessingGuard` | PK(org), положительная revision; **compatible=false по умолчанию**; отсутствие row → deny |
| `check_row` | Immutable keys/TTL, сохранение source/result, Version+1, закрытые transitions, DELETE запрещён |
| `audit_row` | AFTER INSERT/UPDATE создаёт SecurityAudit и journal в той же transaction; failure любого INSERT откатывает всё |
| `admit/step` | Whole-course author + ownership/current context/restrictions, CAS, idempotency/fingerprint conflict; запрет unsealed/partial/cross-generation Ready |
| `readable` | Только boolean metadata predicate: Ready, owner/org/authority/current context/restrictions, cleanup None, время до expiry; не читает файлы |
| Privileges | PUBLIC revoked; serving role без table DML/descriptor SELECT и без command EXECUTE; executor только узкие функции |

Descriptors содержат server key, byte length, SHA-256 и generation UUID; result допускает только полный набор student/teacher/receipt одной generation. Source generation хранится внутри source descriptor. SQL проверяет форму/согласованность, **не существование, digest или durability файлов**. `sealed=true` допустим только от trusted storage executor после проверки FS. Для публичного входа такой параметр неприемлем.

Транзакцией владеет harness/orchestrator. Основные concurrency schedules — Serializable. Порядок: S2 account/user/identity/session locks, **если требуются** → S3 Organizations → UPDATE ProcessingGuard revision → ImportAttempt FOR UPDATE → metadata + audit + journal. Реальные S2 locks не добавлялись в candidate функции; S3 org lock согласован с существующим authority writer. Один org на command. Guard создаётся при trusted fixture provisioning, не lazy fallback.

UPDATE guard revision принципиален: snapshot transaction после чужого guard commit получает `40001`, а не доверяет старому snapshot. После доказанного abort допускается новая transaction и повтор current checks с тем же OperationId. Не выполняется автоматический compile replay. `P0001` — domain denial/CAS в этом harness; `23505` — fingerprint/idempotency conflict. `40P01` — доказанный deadlock abort. Потеря ACK не классифицируется как abort.

Файлы записываются до Ready transaction. Длительного FS I/O под DB locks нет, кроме намеренных fault schedules с задержкой commit. Admission/expiry используют clock_timestamp; TTL не продлевается. Это системные часы PostgreSQL, **не доказанная qualified clock модель G02**.

Упрощения candidate: cancellation проверена при заранее quiescent executor и сразу переводит в Cancelled с Version/epoch fence. Durable stop intent, worker teardown, отдельный cleanup epoch/claim, quotas, fixture provenance resolver и bounded diagnostics здесь не реализованы. Cleanup marker зарезервирован, но не является реализованным janitor. Не объявляется полная реализация C2 state machine runtime.

## 4. PostgreSQL и EF/S2/S3 evidence

Среда: свежий container `postgres:17.6-alpine`, PostgreSQL 17.6 x86_64, `fsync=on`, `synchronous_commit=on`, `full_page_writes=on`, UTC. Доступ candidate harness через выделенный Unix socket, container `--network none`, без published DB port. Только новые `c2f` и `c2f_public_reference`; ambient connection strings не использованы. Regression использует собственные disposable Testcontainers.

SDK 10.0.401 загружен в `/tmp/b07c2f/sdk`; build/test запускались на копии исходников `/tmp/b07c2f/repo`. Системный snap `dotnet` не имел SDK и не смог выполнить автоматическую установку; repository не перестраивался на месте.

| Проверка | Фактический результат |
|---|---|
| Empty DB → вся цепочка до S2 → S3 | 8 migrations; PASS |
| Повторный Migrate, EF HasPendingModelChanges | Нет pending model changes; повтор успешен |
| Additive candidate после полной цепочки, повтор EF Migrate | PASS |
| Чистый replay generated EF script на второй disposable DB | PASS |
| public schema candidate DB vs чистый replay | Совпадают byte-for-byte после удаления случайных pg_dump `restrict/unrestrict` tokens; [hash evidence](evidence/MVP-B07b-C2-F/schema-comparison.json) |
| Existing S1/S2/S3/component processing regression | **111 passed, 0 failed, 0 skipped**; [TRX](evidence/MVP-B07b-C2-F/regression/regression.trx), [log](evidence/MVP-B07b-C2-F/regression.log) |
| Candidate assertions/probe groups | 83 завершены; [JSON](evidence/MVP-B07b-C2-F/probe/results.json), [log](evidence/MVP-B07b-C2-F/probe.log). Включают ожидаемые counterexamples, не 83 доказательства безопасности |
| Структурные state transitions | Все 49 пар исполнены: 15 разрешённых, 34 отклонены `23514`; [matrix](evidence/MVP-B07b-C2-F/matrix-results.json) |
| Wire COMMIT ACK loss candidate | Реальный frame drop, клиент NpgsqlException, commit/journal обнаружены fresh connection; [evidence](evidence/MVP-B07b-C2-F/wire-results.json) |

Регрессия включает `Additive_S2_upgrade_preserves_existing_sessions_and_verifier_history`, `B01_forward_upgrade_preserves_S2_rows_and_does_not_backfill_history`, S3 privilege/rollback/generation cases, A1 issuer и `A2_wire_lost_PostgreSQL_commit_acknowledgement_never_returns_JWT`. Никакого изменения S2/S3 semantics не сделано.

Отдельный дополнительный запуск с фильтром по именам файлов `ComponentIssuanceTests|ComponentCommitAcknowledgementTests` нашёл **0 tests** — не PASS. Эти тесты являются partial `ScopedAuthorityTests` и уже исполнены в 111-test run; подтверждено по TRX. [No-match log](evidence/MVP-B07b-C2-F/authority-extra.log) сохранён. Первичный harness остановился на ошибке observer deadlock race: первой могла завершиться успешная сторона уже после abort другой. Observer исправлен на проверку обоих исходов; финальный полный run — 83 groups. Это correction тестового harness, не runtime fix.

### SQLSTATE и исполненные concurrency schedules

| Probe / schedule | SQLSTATE / наблюдение |
|---|---|
| Два Complete с Version=2 | A locks и Ready uncommitted; B реально ждёт lock (`pg_stat_activity`); A commits; B `40001`; fresh B retry `P0001`; Version=3 и один terminal transition |
| Cancel → Ready | A Cancelled commits; B `40001`, fresh retry `P0001`; Ready отсутствует |
| Ready → Cancel | A Ready commits; B `40001`, fresh retry `P0001`; Ready сохранён |
| Restriction → Ready, совместимый writer | A org+guard update+restriction; B ждёт; A commits; B `40001`; fresh B `P0001`, Version=2 |
| Ready → Restriction, совместимый writer | A Ready; B ждёт org/guard, получает `40001`; fresh restriction transaction commits `00000`; fresh readable=false при сохранённом Ready |
| Phantom restriction, несовместимый writer | Importer Serializable: org/guard + snapshot unrestricted=true; raw writer INSERT commits `00000` без guard; importer из старого snapshot Ready commits `00000`; fresh readable=false. **Контрпример**, а не acceptable Ready |
| Snapshot freshness | После чужого guard update+restriction: READ COMMITTED guard update `00000` и fresh deny; RepeatableRead/Serializable guard update `40001` |
| Lock inversion | A guard→attempt; B attempt→guard; фактический cycle, один `40P01`, другой `00000` после abort victim. Исправление — единый порядок, не увеличение timeout |
| Expiry во время lock wait | Дедлайн пересечён до mutation; после lock Ready `P0001`, Version=2; Expired terminal сохранён до physical cleanup |
| Expiry после check, до commit | Намеренная задержка после step; commit `00000`, row Ready, fresh readable=false. **Commit-deadline invariant NOT PROVEN** |
| Stale expectedVersion | `P0001`, нет лишней mutation |
| Same OperationId, different fingerprint | `23505`; same committed operation после reconciliation возвращает прежний результат без duplicate audit |
| Same admission CommandId | Same fingerprint → тот же ImportId; different fingerprint → `23505` |
| Audit/journal fault | CHECK fault в journal → `23514`; state/version/audit/journal полностью откатились |
| Unsealed / incomplete / cross-generation / path-like descriptor | `P0001`, Ready не создан |
| Immutable owner/org/course/context/binding/admission/TTL | `23514`; source/result защищены check_row; terminal Ready overwrite запрещён |
| Missing guard / authority / current context | `P0001` |
| Ambiguous overlapping binding | Existing S1 exclusion → `23P01`; stored ambiguity не создана. Defensive count=1 predicate отдельно отказывает на duplicate CTE rows, это не исполнявшийся corrupt-schema integration case |
| Serving INSERT/UPDATE/DELETE/TRUNCATE | `42501` для всех трёх candidate и трёх relevant existing processing tables; descriptors SELECT и command EXECUTE тоже `42501` |
| Tombstone/journal deletion | `23514`; retention deletion не добавлено |

Дополнительная структурная матрица работает owner DML внутри rollback-only transactions для проверки trigger, не обходит executor semantics в runtime. Доказательства same-org course/context FK опираются на установленные реальные FKs, 111 existing schema/authority tests и candidate owner/org negatives; arbitrary forged raw metadata вне trusted executor не выдаётся за отдельный end-to-end access test.

## 5. Несовместимые writers: точные paths и минимальное исправление

В `src` нет dedicated restriction/context administration writer и нет server ImportAttempt persistence. Но публичный EF context + mapped entities допускают эти mutations при наличии DB privileges. Отсутствие HTTP writer не доказывает невозможность обхода.

| Path | Фактический доступ / finding | Минимальная будущая коррекция — НЕ выполнена |
|---|---|---|
| `src/Lms.Infrastructure/LmsDbContext.cs:21,39`; `SchemaFoundationMappings.cs:60–75`; `SchemaFoundations.cs:82–117` | Generic Set/Add/SaveChanges для всех трёх entities, без processing lock protocol | Выделить закрытые internal mutation functions/adapters, отозвать прямое DML у всех operational writers; mandatory org→guard revision→rows до mutation |
| `tests/Lms.Infrastructure.Tests/ComponentRestrictionHardeningTests.cs:32–34,46–47` | Реальные EF restriction insert и withdrawal/version increment без guard | Перевести relevant writer fixtures на общий протокол; raw bypass оставить отдельным отрицательным тестом. Реальный Add/SaveChanges под удерживаемым guard успешно повторён candidate harness |
| `tests/Lms.Infrastructure.Tests/ComponentIssuanceTests.cs:122–130` | EF binding/restriction setup без guard | Та же transactional boundary для будущего writer; fixture setup не считать production capability |
| `tests/Lms.Infrastructure.Tests/ComponentRestrictionHardeningTests.cs:19–20,58–60`; `SchemaFoundationTests.cs:207–220` | Binding INSERT/закрытие EffectiveTo; overlap/history constraints не захватывают guard | Close old/open new binding атомарно под org/guard; проверить текущий binding повторно после locks |
| `tests/Lms.Infrastructure.Tests/SchemaFoundationTests.cs:44–46,241`; `ScopedAuthorityTests.cs:30` | Context version INSERT через EF/raw SQL без guard; UPDATE/DELETE context уже immutable | Новый context version/eligibility publication под тем же org/guard; immutable update/delete не разрешать |
| `src/Lms.Infrastructure/Migrations/20261009094756_SchemaFoundationsM1.cs:974–985,1019–1029,1172–1178` | Existing course/org FK, overlap, history/effective/provenance triggers: нет ProcessingGuard, phantom INSERT не сериализуется с импортом | Только будущая separately authorized additive change; не менять historical migration. Одного row trigger недостаточно для доказательства порядка: tuple lock UPDATE может быть взят раньше guard |
| `src/Lms.Infrastructure/PostgresCurrentProcessingGate.cs:12–14,17–40` | Read component отказывает на ambient transaction; открывает свой RepeatableRead snapshot | Не вызывать как Ready write gate. Нужен transaction-owned current decision adapter с fresh snapshot/guard revision и полной clock check |
| `src/Lms.Infrastructure/Migrations/20261009153218_ScopedAuthorityS3.cs:194`; `PostgresScopedAuthorityAdministration.cs:24` | S3 administration уже использует Organizations FOR UPDATE; не пишет processing tables | Сохранить его порядок перед guard. Не вводить обратное взятие S2/S3 locks после import |
| Candidate `probe/candidate.sql`, функции `admit/step` | Новый draft writer всегда захватывает org+guard; compatibility flag по умолчанию false | Не переносить экспериментальную capability в serving DI; сначала общая совместимость и runtime design |

Исполнено: при удерживаемом candidate guard чужая transaction успешно (`00000`) вставляет restriction, withdraw restriction, закрывает binding, вставляет context version; отдельно EF Add/SaveChanges тоже успешно. Это не утверждение о production credentials — они не инспектировались. Fresh restricted synthetic serving role лишён DML; существующие migration-owner/test writers технически обходят guard.

Минимальное решение требует инвентаризации всех runtime/internal/provisioning/import/cleanup writers и privilege grants, включая изменение organization eligibility. Закрытая функция должна брать locks **до** row mutation. Декларация «importer Serializable» и блокировка только найденных restriction rows не предотвращают phantom. Unknown compatibility оставляет Ready disabled.

## 6. Private filesystem evidence

Полные [fs_probe.py](evidence/MVP-B07b-C2-F/fs_probe.py), [log](evidence/MVP-B07b-C2-F/fs.log), [JSON](evidence/MVP-B07b-C2-F/fs-results.json). Private random directory mode 0700 под canonical filesystem (`stat -f`: ext2/ext3 family); `/tmp` на этой машине tmpfs, поэтому его не использовали как evidence persistent FS. Файлы mode 0600; процесс trusted UID. Fixture directory удалён после тестов/reconciliation как teardown, не janitor.

| Probe | Исполненный результат |
|---|---|
| Seal | Раздельные source/student/teacher/receipt и manifest; SHA-256/length, generation UUID; все file handles закрыты до rename; fsync файлов + staging dir + parent |
| Successful publication of metadata | Actual sealed files проверены по manifest; Validating → Ready с полной одной result generation; positive metadata predicate |
| No-replace atomic rename | Linux `renameat2(RENAME_NOREPLACE)`, занятый target → `EEXIST` errno 17; target/source не перезаписаны |
| No-follow | Symlink open с O_NOFOLLOW → `ELOOP` errno 40 |
| Crash во время write, до seal, после seal | Реальные fork/_exit codes 91/92/93; partial/staged или orphan bytes не создают Ready |
| Crash после result seal перед metadata | Source уже sealed, attempt Validating; child exit 93 после result fsync/rename; Ready reference отсутствует |
| Failure до rename / после rename до parent fsync | Injected EIO errno 5 на этих boundaries; seal не acknowledged, Ready не записан. Это fault injection, не реальный отказ устройства |
| DB abort после seal | Ready update откатан, row Validating; generation отсутствует в DB; quiescent orphan сохранён, затем no-replace quarantine rename + parent fsync |
| Unknown outcome cleanup | Durable private unknown ledger; policy не вызывает rename/unlink/replay, bytes/hash/path сохранены. Logical quarantine без destructive action |
| Lost response/reconcile | Unknown bytes сохранены; journal прочитан authoritative после commit, найден один result. В FS case ACK подавлен на application boundary; отдельный wire probe действительно удаляет PostgreSQL ACK |
| Corruption | Teacher artifact digest mismatch обнаружен; нет content delivery. Сам SQL readable не обнаруживает порчу FS — storage verifier обязателен |
| fsync | 36 успешных вызовов в parent, дополнительно child fsync перед crash; успешный syscall не доказывает survival при power loss |

FS JSON содержит 16 записей, включая profile, fsync count и **NOT PROVEN** durability note — это не «16 runtime tests PASS». Sealed synthetic payloads не являются настоящим C1 BuildReceipt/compiled course; неизменность формата C1 не проверялась интеграцией. Hard-link/special-file/host compromise, physical power cut, WAL/device flush/failover, crash-consistent backup/restore, arbitrary media и hard storage timing bounds **NOT PROVEN**. Нет claimed guarantee от chmod против same UID/root.

Unknown cleanup probe — исполняемый policy harness, не durable distributed janitor. Реальный cleanup state machine, lease/epoch fencing, worker liveness и recovery после смерти orchestrator остаются следующим отдельно разрешённым scope. Physical teardown disposable fixtures после завершения и reconciliation не является разрешением удалять runtime unknown generations.

## 7. Подтверждённые и недоказанные инварианты

**Подтверждено в указанном disposable профиле:** candidate schema additive к полной EF chain; existing S2/S3 regression; immutable association/TTL; закрытые transitions и CAS; атомарные journal/audit/metadata; idempotency conflict; restricted serving direct DML deny; current read deny после restriction/expiry; serialization с совместимыми writers; реальный stale-snapshot/phantom counterexample; actual deadlock abort; no-replace rename, syscall fsync, process-crash orphan safety; реальный commit ACK loss без успеха/replay/delete.

**Не подтверждено / blocked:** общая совместимость текущих writers; production privileges; server authority adapter; qualified clock и strict commit deadline; C1 receipt/projection semantic integrity; generic source/subject completeness; durable cleanup и recovery fencing; worker quiescence при настоящем cancel; crash/power-loss durability; filesystem hostile-entry coverage и quotas; HTTP/browser/preview safety. Никакие UI COMPLETE/E2E ACCEPTED/SYNTHETIC MVP ACCEPTED/PILOT AUTHORIZED не объявлены.

## 8. Минимальные изменения runtime и следующий slice

Не реализовано автоматически:

1. Закрытый processing mutation protocol: org→guard revision→affected rows, revoke operational direct DML, context/restriction insert/withdraw/binding closure eligibility через те же functions. Cross-org batches запретить либо отдельно определить порядок. Убедиться, что ни один writer не обходит его.
2. Trusted import executor adapter поверх S2/S3 с immutable owner, fresh job authority и transaction-owned processing decision. Не расширять ScopeAction/claims/PRS1. Пользовательские import/preview operations deny до принятия adapter.
3. Полный descriptor/provenance/manifest verifier: C1 bytes и receipt, generation identity, digest/length, source/subject completeness, trusted no-follow private path resolution; никакого клиентского sealed flag. Source generation не перепривязывать.
4. Durable cancel intent/epoch + подтверждение остановки; cleanup claim/epoch и explicit reconciliation state. Unknown commit сохраняет bytes и metadata, исключает auto replay/delete. Tombstone/journal retention не вводить до owner decision.
5. Clock/commit contract: access deny после expiry уже моделируется; strict Ready commit-before-deadline требует принятого G02 timing/transaction bound либо отдельного owner уточнения инварианта. Нельзя представлять pre-commit clock check как доказательство deadline самого COMMIT.

**Рекомендуемый следующий slice:** отдельно разрешённый **processing writer compatibility implementation/design** с закрытыми capabilities и полным race suite; import server slice пока не начинать. Если owner предпочитает ещё одну validation iteration — проверить candidate wrappers для всех relevant writers, S2/org/guard lock ordering и crash/recovery на принятом storage profile. Критерий выхода: отсутствие воспроизводимого bypass при operational privileges, обе ordering sequences, snapshot abort/recheck, audit atomicity, отсутствие обратного lock order. Границы G02/B02/B11 и отдельное разрешение server integration сохраняются.

## 9. Команды, воспроизводимость и self-review

Evidence scripts не зарегистрированы в runtime/test projects репозитория. Их fixed paths требуют отдельного disposable workspace `/tmp/b07c2f`, копии `src`, `tests`, Directory.Build.props/global.json и SDK 10.0.401. В `probe.csproj` reference указывает на эту копию `../repo/src/...`. SDK/binaries/DB dumps не добавлены в Git. Generated full EF SQL применён только к reference DB; production migrations не создавались.

Исполнявшиеся основные команды:

```sh
# cwd=/tmp/b07c2f/repo (копия canonical sources)
/tmp/b07c2f/sdk/dotnet test tests/Lms.Infrastructure.Tests/Lms.Infrastructure.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~SchemaFoundationTests|FullyQualifiedName~IdentitySessionTests|FullyQualifiedName~ScopedAuthority|FullyQualifiedName~ComponentRestrictionHardeningTests' --logger 'trx;LogFileName=regression.trx' --results-directory /tmp/b07c2f/regression
# Только свежий isolated cluster; harness создаёт DB c2f, затем весь EF chain и candidate.
/tmp/b07c2f/sdk/dotnet run --project /tmp/b07c2f/probe/probe.csproj --no-restore
python3 /tmp/b07c2f/wire_probe.py
python3 /tmp/b07c2f/fs_probe.py
python3 /tmp/b07c2f/matrix_probe.py
# public schema comparison: pg_dump --schema-only --schema=public для c2f и c2f_public_reference
```

[SHA256 manifest](evidence/MVP-B07b-C2-F/SHA256.json) фиксирует evidence sources/results. Изначальные failed observer/no-match attempts не засчитаны как пройденные проверки. Повторяли candidate только после изменения harness/DDL; regression без изменения source повторно не гоняли.

Guardrails §7:

- covered — actor/use case/states, accepted S2/S3 temporal rules и owner mapping; server implementation boundary сохранён;
- covered in harness — ownership/org/course authority и negatives, current conservative processing; **blocked** для принятого server adapter и generic subject/provenance completeness;
- not applicable — lists/counts/pagination: новый query workflow отсутствует, metadata predicate возвращает только boolean;
- covered in candidate — immutable history, private descriptors, transaction/lock/CAS/audit, unknown outcome; **blocked** для durable cleanup/worker lifecycle и FS delivery verifier;
- covered within probes — validation/descriptor shape/errors, private synthetic fixtures; browser/E2E/real file delivery **NOT RUN**, никаких новых access paths;
- covered — 111 regression cases, candidate negative/concurrency/matrix/FS/wire evidence приведены с actual results и ограничениями;
- covered — нет production migrations/runtime/security/API/frontend/writer изменений, реальных данных или secrets; new evidence/docs only;
- covered — branch/HEAD/index и pre-existing файлы проверены отдельно ниже; no commit/push/merge/rebase/deploy.

**B07b-C2-F OWNER ACCEPTANCE PENDING · B07b SERVER IMPLEMENTATION NOT AUTHORIZED · B08 NOT AUTHORIZED · PRS1 EXACT OFF · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**

Финальная repository проверка: **PASS** — все 482 исходных tracked/untracked файлов совпадают по SHA-256; branch `codex/l1-s4-jwt-api-authorization`, HEAD `492e30aef65bbddd24f107903bfaa54a563abe39` и index сохранены. Dirty checkout не переключался на main согласно AGENTS.md. Новые пути ограничены этим отчётом и его evidence; local links/whitespace проверены. [Preservation evidence](evidence/MVP-B07b-C2-F/preservation.json).

Disposable cluster `b07c2f-pg` и его anonymous data volume удалены (`docker rm -fv`); private FS fixture directories удалены. Общий `git diff --check` завершился exit 2 из-за **pre-existing** `docs/content/COURSE-CONTENT-SPEC.md:1325: new blank line at EOF`; SHA-256 этого файла совпадает с baseline, он не исправлялся. Новые Markdown строки проверены отдельно: whitespace/local links PASS.
