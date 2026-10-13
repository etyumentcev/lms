# MVP-STORAGE-SEAL-01 — isolated original storage

2026-10-12. **ISOLATED CANDIDATE IMPLEMENTED · PHYSICAL PROFILE BLOCKED · OWNER ACCEPTANCE PENDING.** Основание — текущее OWNER DECISION MVP-STORAGE-SEAL-01; WORK/HISTORY accepted. Этот результат **не закрывает STORAGE-READ-01**. Успешный физический immutable seal на доступных filesystem не доказан. PHOTO READ остаётся blocked.

## Контракт и инвентаризация

Trusted storage coordinator → durable reservation → единственный private original → закрытие writer → kernel seal → durable immutable confirmation. Физическая пригодность не является разрешением доступа. Компонент internal, не реализует `IAttachmentStorage`, не возвращает bytes, paths или URL. Нет пользовательского upload/submit workflow, domain claim, HTTP endpoint или production DI. Test double существует только в test assembly. Специализированные model-domain / implement-use-case / implement-feature skills отсутствуют в доступном каталоге; их выполнение не заявлено.

Прочитаны AGENTS, MVP guardrails/reconciliation, NR1 N3/N5 и N1/N2/N6, ADR-L15/L18, CLOCK-01, DEADLINE-01 и предшествующий protected-work результат. Более позднее owner acceptance применяется поверх исторических заголовков. Clock/deadline компоненты не изменены; storage не создаёт authority clock, JWT или новую request authority. Deadline/authority integration остаётся условием будущего adapter, а CancellationToken не объявляется hard deadline.

| Existing entity / source | Факт и сохранённая граница |
|---|---|
| `StoredAttachment` | Id/OwnerId/StorageKey/Size/CreatedAt, SubmissionId XOR MessageId, Deleted. Нет hash/immutable version/durability proof. Таблица и строки не меняются. |
| `AccessAttachments` / `AttachmentAccess` | Legacy owner/IsEligible/ExpiresAt. IsEligible не доказательство N3 seal. Нет автоматического переноса eligibility. |
| `AttachmentScope` | Org/course/uploader/context/version/quarantine. FK к original, membership/context и course/org; physical proof отсутствует. |
| `AttachmentStorage` migration | INSERT Submission/Message claim trigger блокирует originals по отсортированным ID; проверяет owner/deleted/already attached. Protect trigger запрещает переписывание metadata/claimed records; DELETE сохраняет metadata. Не изменён. |
| `SchemaFoundationsM1` | Attachment scope guard сверяет legacy owner, parent attachment array, course/org и uploader. Scoped original/parent constraints сохранены. |
| `PostgresScopedAuthorityReader.Attachment` | Независимые scope, context SyntheticOnly, course/org, actual parent array/author и historical parent checks. Pending legacy eligibility — только authorization input, не новый seal. |
| `AttachmentService` | Legacy upload делает storage I/O до metadata save; compensation проверяет отсутствие metadata; delete сериализуется row lock. Этот путь не подключён к кандидату. |
| `LocalFileAttachmentStorage` | WriteThrough/temp/rename, writable namespace, нет kernel writer exclusion и durable exact-version confirmation. Не изменён и не объявлен подходящим N3 backend. |

Authorization/data-source matrix: operation/org — **доверенные внутренние server selectors**, не client authority; generation создаёт ledger; hash/length вычисляет coordinator после kernel seal на том же read-only descriptor; version digest возвращает kernel; immutable confirmation хранит PostgreSQL primary. Ни FileName, MediaType, клиентский hash, ни legacy Size/StorageKey не превращаются в proof. Для будущего endpoint required outcome — пара `(physical condition, independent authorization decision)`: даже Confirmed + access denied не допускает выдачу. Restriction/org/course/group/parent/actor/context/time checks не заменены физическим inspector.

## State machine

| Состояние | Значение / допустимые переходы |
|---|---|
| Unverified | Нет новой ledger record, в том числе legacy original. Inspector не квалифицирует его по наличию файла/hash. Нет automatic promotion. |
| PhysicalPending | Durable reservation ACK получен либо потерян; bytes могут ещё отсутствовать, писаться или быть полностью записаны. No read/claim eligibility. |
| Sealed | Единственный переход из PhysicalPending после успешного kernel exclusion/seal, sync, exact hash/length/version и атомарного PG confirmation. PhysicalPending=false. |
| OutcomeUnknown | Ошибка/неизвестный physical outcome, restart pending generation. PhysicalPending сохраняется независимо. Только сохранение состояния или Quarantined; **никогда Sealed**. |
| Quarantined | Terminal deny; receipt, если ранее существовал, неизменяем. No purge/retry/resume. |
| Missing / Damaged | Результат текущей physical inspection уже подтверждённой record: original отсутствует либо не совпали hash/length/version, verity отсутствует или чтение повреждено. Никаких bytes/result proof для выдачи. |

Переходы: `none → PhysicalPending → Sealed`; `PhysicalPending → OutcomeUnknown → Quarantined`; `PhysicalPending/Sealed → Quarantined`. Sealed хранит неизменяемый факт подтверждения, а текущая проверка может возвратить Missing/Damaged. Эти два inspection result не являются доказательством rollback или разрешением repair/replay. Unknown records не возвращаются в Confirmed даже при правдоподобном hash/length/version; это проверено SQL trigger и component tests.

Отдельные оси: persisted `state`, conservative `physical_pending`, immutable receipt; локальная неизвестность COMMIT ACK не переписывает committed Sealed в aborted. При потерянном reservation ACK файловые side effects вообще не начинаются. При потерянном confirmation ACK старый вызов завершается ошибкой; independent primary lookup может увидеть уже committed Sealed. Это resolution прежнего факта, **не Unknown→Sealed transition**, новый writer или replay. Missing confirmation store → exception/deny, без fallback к local metadata.

## Ownership, transactions и writer exclusion

Изолированная `storage_seal` schema устанавливается **явно только disposable fixtures**, не EF migration/startup. Нет изменений production schema/model snapshot. Schema installer владеет DDL; trusted coordinator получает только ledger capabilities, не domain-table credentials. Tests отдельно проверяют denied доступ untrusted role к ledger и denied доступ ledger coordinator role к StoredAttachments. Production service account/provisioning не создан. Schema/table/function PUBLIC privileges сняты; клиент не имеет API для `Confirm`. DB administrator/скомпрометированный trusted coordinator вне NR1 non-Byzantine модели, как и возможность отключить triggers.

Ledger владеет короткими ReadCommitted transactions с `SET LOCAL synchronous_commit=on`. Reserve/Recover/Confirm: lock singleton control row → original row(s); claim predicate test берёт original row lock. Capacity count и INSERT сериализованы control lock. Confirmation CAS включает operation/generation/org/epoch/PhysicalPending. Proof вместе со state публикуется одной transaction; original row служит retained operation/result record. Нет отдельного domain audit, поскольку domain effects отсутствуют. Mark terminal state — одна UPDATE transaction; не берёт control lock в обратном порядке. Ни одна PG transaction не охватывает file I/O. Нет автоматического retry COMMIT или replacement generation.

Namespace: pre-provisioned private 0700 root, уникальный `<operation-N>-<generation-N>/original`; CreateNew, один файл на reservation, путь не поступает от клиента. Exact version identity — `(operation, generation, SHA-256 content digest, byte length, fs-verity digest)`; digest сам по себе может совпадать у одинаковых bytes, generation никогда не переиспользуется. Несколько страниц — независимые originals/reservations, без нового Submit или manifest workflow.

Writer делает последовательные awaited reads/writes, flush и fsync; затем awaited dispose закрывает единственный выдаваемый implementation writer. Writable handle не выходит из компонента; нет фоновых write tasks. Read-only fd передаётся `FS_IOC_ENABLE_VERITY`. По [официальному Linux fs-verity contract](https://docs.kernel.org/filesystems/fsverity.html#fs-ioc-enable-verity), ioctl исключает writable descriptors/mappings и запрещает новые writable opens; после успеха inode защищён от изменения. Это условный kernel механизм, **не вывод из rename/chmod/hash/FileShare**. Затем sync sealed inode и directory, вычисление SHA-256/length и measurement на том же descriptor, затем confirmation. Unsupported/retained writer/durability failure не имеют fallback.

Inspector сверяет length, kernel digest и полный content hash с PG receipt, используя один descriptor. Symlink final file/root запрещены. Доверенные private ancestors, отсутствие враждебного процесса того же UID и защиты root namespace — обязательные profile assumptions; код не заявляет hostile-root или универсальную pathname-race защиту. Inspector не является read permit для позднейшего reopen: будущий delivery adapter обязан проверить exact version на фактически открытом handle и держать его через чтение.

## Capacity, restart и durability assumptions

| Лимит | Число / поведение |
|---|---|
| Original bytes | 1…10 MiB (10,485,760), превышение останавливает запись до превышения cap |
| Reservation | 12 MiB (12,582,912) на original: bytes + conservative verity/namespace allowance |
| Все retained generations | 64 global / 16 per-org, включая Sealed, pending, unknown, quarantined |
| Reserved capacity | 768 MiB global / 192 MiB per-org; caps эквивалентны slot counts × fixed reservation |
| Active storage calls | 8 global / 2 per-org, fail-fast до DB/file side effects; **0 waiting queue** |
| File copy buffer | 64 KiB на active writer; inspector hashing имеет bounded library buffer; original не буферизуется целиком |
| Background workers / replacements | 0; нет автоматического retry/new key/cleanup |

12 MiB — reservation allowance, не kernel quota и не доказанный физический upper bound для произвольного filesystem: включённые verity/metadata overhead, free-space headroom и внешние writers требуют отдельной profile qualification. На ENOSPC/ошибке allocation результат остаётся deny/Unknown, reservation не освобождается.

Слоты active calls process-local и действительны для **одного trusted coordinator process**. Distributed admission не реализован. Startup Recover запускается до создания serving coordinator; ожидание startup отдельно, не пользовательская очередь. Durable byte/slot caps сериализованы PG и переживают новый процесс. Максимум незавершённых generations всё равно 64; arbitrarily spawning старых процессов/административное обходное создание ledger rows не поддержано. Внутренние ledger methods открыты test assembly для fault injection, не являются альтернативным user admission API.

Recover увеличивает epoch под control lock и переводит прежние PhysicalPending в OutcomeUnknown. Старый worker может завершить private I/O, но Confirm со старым epoch/CAS откажет. Файл и reservation сохраняются даже после kill/reap; это не автоматическое доказательство отсутствия других writers. New admission affected org отказывает, пока есть Unknown/Quarantined. Отсутствуют release API, TTL reclaim, purge claimed/unknown originals, replay или автоматическая реконструкция. Система намеренно может исчерпать cap и остановиться навсегда до отдельно авторизованного maintenance. Это ограниченный storage slice, не retention/erasure implementation.

Supported candidate assumptions: Linux x64 libc, 4096-byte verity blocks, filesystem с реально включённым fs-verity, private root и single coordinator lifecycle; local fsync честно подтверждает file data/inode/directory durability; PG single authoritative primary с fsync/full_page_writes и durable WAL, storage stack соблюдает flush/barrier semantics. Root предварительно создан и durable; directory fsync root фиксирует generation namespace, затем file/inode и generation directory sync предшествуют PG confirmation. FS и PG не общая transaction: crash до confirmation оставляет невыдаваемый orphan, после confirmation новая inspection всё равно проверяет original.

Failover, rollback подтверждённого WAL, восстановление старого snapshot как нового primary, device firmware faults, Byzantine kernel/operator и произвольная filesystem corruption вне qualified failure model. Нужны coordinated DB/files restore drill и подтверждение невозможности старого primary/writer вернуться. Универсальный recovery RTO отсутствует. Crash/kill/exception tests не доказывают power-loss consistency реального устройства. Cancellation проверяется перед новыми FS stages/confirmation; issued syscall может зависнуть, занимает capacity, не объявляется завершённым по timeout. Fresh CLOCK-01/DEADLINE-01 gates перед каждым новым authority-dependent stage должен добавить отдельно принимаемый adapter; этот компонент не продлевает authority и не подключён к HTTP.

## Physical blocker и failure evidence

**STORAGE-SEAL-PHYSICAL-01 OPEN.** На этом host Linux `7.0.0-38-generic`:

- private temporary `/tmp` на tmpfs: `FS_IOC_ENABLE_VERITY` → errno **25 / ENOTTY**;
- private temporary `/var/tmp` на ext4: ioctl → errno **95 / EOPNOTSUPP**.

Оба прогона — **PASS safe-denial**, не PASS physical seal. [TRX](evidence/MVP-STORAGE-SEAL-01/targeted.trx) и [per-test outputs](evidence/MVP-STORAGE-SEAL-01/targeted-summary.json) явно содержат `PHYSICAL PROFILE BLOCKED` и `positive physical seal and retained-handle proof NOT RUN`. Superblock/mount/kernel/host settings не изменялись. Не создавался privileged loopback filesystem для подмены профиля. Legacy originals и реальные файлы учеников не использованы.

До получения поддержанного private temporary filesystem: реальный успешный fs-verity seal, ETXTBSY с retained writable fd/mmap, отказ write/truncate после seal и real-device crash durability **BLOCKED / NOT PROVEN**. Модельные tests не закрывают эти обязательства.

## Тесты

Все PostgreSQL fixtures — Testcontainers PostgreSQL 17.6, отдельные disposable databases с полной существующей migration chain; test schema additive и opt-in. Файлы — private random temporary roots, только синтетические bytes; удаление originals есть только в fault injection / fixture teardown, не storage component.

| Требование | Выполненная проверка / предел |
|---|---|
| Write/seal; несколько страниц | ModelKernel positive, 3 independent generation namespaces, exact hash/length; физический positive BLOCKED |
| Изменение после seal | повторная Write того же operation запрещена без новых files; model content tamper выявлен hash/length; real immutable write denial BLOCKED |
| Retained writer | собственный writer закрыт до kernel enable; injected kernel refusal оставляет Unknown; настоящий fd/mmap exclusion BLOCKED |
| Crash до confirmation | настоящий child Python worker с открытым writable fd пишет/fsync private original, SIGKILL/reap; restart оставляет bytes/PhysicalPending/Unknown; replay denied |
| Crash/ACK после confirmation | моделируется потеря coordinator/ACK после authoritative COMMIT, restart сохраняет immutable receipt; **не** power-loss test |
| Reservation ACK loss | committed reservation, 0 FS effects; restart Unknown, нет replacement |
| Durability failure | injected fsync exception → Unknown, нет receipt, reservation сохранена |
| Hash/length/version | три независимых mismatch tests → Damaged, нет proof для чтения |
| Concurrent seal/claim | реальные PG competing confirmations: 1 winner; test-only claim predicate не видит pending proof; row lock блокирует quarantine до завершения probe transaction. Domain claim не реализован/не заявлен |
| Restart/accounting/caps | concurrent reservations 16/org, 64 total; после restart 64 Unknown + 64 PhysicalPending + 768 MiB retained; следующий admission denied |
| Active calls/cancellation | 2 held calls одной org, третий без queue/side effects; cancel до следующего stage исключает seal |
| Missing/quarantined/legacy | Missing, Quarantined, Unverified; legacy присутствие bytes не подтверждает original |
| Confirmation unavailable/client metadata | DB table unavailable → fail closed; untrusted SQL role deny, immutable receipt/generation UPDATE deny; нет client proof endpoint |
| Unknown resurrection | stale epoch CAS, direct SQL Unknown→Sealed, DELETE/TRUNCATE ledger denied |

Exact commands, canonical directory:

```sh
/tmp/b07c2g/sdk/dotnet test tests/Lms.Infrastructure.Tests/Lms.Infrastructure.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~StorageSealTests --logger trx --results-directory /tmp/storage-seal-tests/final2
/tmp/b07c2g/sdk/dotnet test Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/storage-seal-tests/regression
```

Targeted: **25 passed / 0 failed / 0 skipped**, из них два реальных physical-profile denial tests, не positive qualification. [Final log](evidence/MVP-STORAGE-SEAL-01/final2.log). Предыдущие development failures сохранены: [CA1416 platform guard](evidence/MVP-STORAGE-SEAL-01/initial.log), [xUnit2031 assertion analyzer](evidence/MVP-STORAGE-SEAL-01/final.log); исправлены без изменения существующего production кода. Intermediate runs [20 tests](evidence/MVP-STORAGE-SEAL-01/second.log), [24 tests](evidence/MVP-STORAGE-SEAL-01/third.log). Окончательные итоги backend regression и preservation приведены ниже.

## Условия будущего protected photo-read

1. Принять/квалифицировать реальный immutable filesystem profile, kernel retained-fd/mmap exclusion, post-seal mutations, durability и coordinated restore evidence. Устранить STORAGE-SEAL-PHYSICAL-01; green ModelKernel tests недостаточны.
2. Отдельно принять mapping confirmation → StoredAttachment exact original/generation/parent; legacy остаётся Unverified. Domain claim/future Submit требует отдельного atomic protocol, actual S3 parent/owner/org/course/context/eligibility, processing guards, time/CAS/idempotency/audit. Test claim predicate — лишь prerequisite, не реализация claim.
3. Authorize каждый metadata/original/Range/preview/annotation до materialization: JWT/S2/S3, Submission XOR Message, historical scope и current restrictions/affected subjects. Confirmed не reusable permission. На фактически открытом fd проверить receipt и immutable version; не использовать inspection→reopen как гарантию. No public URLs; no-store; no PG locks во время stream.
4. CLOCK-01 deployment qualification, original DEADLINE-01 handle, fresh action/response gates, bounded admission/transport integration и HTTP/Chrome negative matrix принимаются отдельно. Missing/damaged/unknown должны закрывать bytes, включая partial-response handling; интерфейс выдачи в этом slice отсутствует.
5. Дальнейший operational cleanup/claim retention/recovery требует отдельного решения. Нельзя снять cap по elapsed timeout или переписать receipt/unknown ради availability.

## Guardrails §7 / security self-review

| Checklist | Disposition |
|---|---|
| Actor/use case/invariants/states | covered — trusted private storage contract, state table |
| S2/S3/NR1 / blocked gates | covered — production policy не менялась; physical blocker явный |
| Org/course/group/ownership | org selector negative covered; course/group/assignment/role access N/A для internal physical-only component без user access; required future adapter BLOCKED |
| Affected subjects / processing evidence | N/A для отсутствующего access path; не добавлен allow или restriction narrowing; future integration BLOCKED |
| Lists/counts/pagination | N/A — нет user-visible списка/агрегата; internal counts только admission |
| Attachments/history | covered — legacy metadata/parent triggers неизменны; no auto qualification; immutable ledger proof; physical seal NOT PROVEN |
| Transaction/locks/CAS/retry/unknown | covered — отдельная схема, serialized reservations/epoch, monotone triggers, fail-closed/no replay |
| Validation/limits/errors | covered — typed internal selectors, no caller paths/proof, bytes/slots/executors caps; exceptions не являются public response |
| Unit/integration/negative/E2E | targeted PG/FS fault evidence; full backend regression ниже; browser E2E N/A: UI/routes не меняются; physical positives BLOCKED |
| Schema/API/security/unrelated changes | covered — только новые isolated files и test-installed schema; нет production migration/DI/API |
| Real data/secrets/readiness | covered — synthetic bytes, no credentials in evidence; G02/HTTP/pilot не объявлены ready |
| Exact tests / gaps | covered — model, denial, real worker crash и NOT PROVEN различены |

## Изменённые файлы и сохранность

Только новые файлы:

- `src/Lms.Infrastructure/StorageSeal/PrivateOriginalStore.cs` — bounded writer и physical inspector;
- `src/Lms.Infrastructure/StorageSeal/LinuxVerity.cs` — Linux x64 ioctl/fsync backend;
- `src/Lms.Infrastructure/StorageSeal/SealLedger.cs` — isolated PG schema, reservations/epoch/CAS/receipt;
- `src/Lms.Infrastructure/StorageSeal/AssemblyVisibility.cs` — internal test access;
- `tests/Lms.Infrastructure.Tests/StorageSealTests.cs` — 25 synthetic fault/negative tests;
- этот report и `codex/evidence/MVP-STORAGE-SEAL-01/`.

Canonical `/home/tea/github/lms`; существующая dirty branch `codex/l1-s4-jwt-api-authorization`, HEAD `492e30aef65bbddd24f107903bfaa54a563abe39` сохранены. На main не переключался согласно dirty-tree AGENTS exception и требованию сохранить branch. Baseline включает 752 существовавших файлов, кроме `.obsidian` и ignored build outputs. Index не изменялся; сохранность сверяется SHA-256. Команды использовали escalation из-за ошибки default sandbox `mountinfo path is not absolute`; automatic approval rejection не было. Commit/push/merge/rebase/deploy/cutover не выполнялись.

**WORK/HISTORY ACCEPTED · STORAGE-SEAL CANDIDATE OWNER ACCEPTANCE PENDING · PHOTO READ BLOCKED · CLOCK-DEPLOY-01 OPEN · G02 BLOCKED · H04/READY BLOCKED · PRS1 EXACT OFF · S4 HTTP CUTOVER NOT AUTHORIZED.**


## Окончательные execution results

- **Backend regression: 1012 passed / 0 failed / 2 skipped**: Domain 43, Application 340, Infrastructure 283, API 346 passed. [Log](evidence/MVP-STORAGE-SEAL-01/regression.log), [counters и skipped names](evidence/MVP-STORAGE-SEAL-01/regression-summary.json). Targeted 25 входят в regression и не суммируются повторно.
- Два skips — существующие opt-in Browser_work / Browser_read definitions без LMS_BROWSER_READ=1; browser/HTTP photo E2E не запускались и не объявляются PASS. Frontend checks N/A: frontend не менялся.
- **Preservation: 752/752 baseline files unchanged**, branch/HEAD/index unchanged, unexpected baseline changes 0. [SHA-256 verification](evidence/MVP-STORAGE-SEAL-01/preservation.json).
- New-file whitespace и report links PASS. Whole dirty-tree `git diff --check` оставляет прежний `docs/content/COURSE-CONTENT-SPEC.md:1325: new blank line at EOF`; файл byte-for-byte сохранён.
- Нет изменений production adapter/DI/HTTP, domain schema/grants, Submit/Review, B08/H04/PRS1. Storage physical qualification и photo-read остаются BLOCKED; остановка на owner acceptance.
