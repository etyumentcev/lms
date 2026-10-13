# MVP-B07b-C2-P — private import draft storage contract

2026-10-11 · **PROPOSED — OWNER REVIEW · DOCUMENTATION ONLY**.

Основание: owner принимает B07b-C1 и разрешает только этот проект; [register](L03-OWNER-DECISION-REGISTER.md). Все MUST ниже — требования к будущему отдельно разрешённому slice, не описание действующего server path. [B07a](MVP-B07a-IMPLEMENTATION-RESULTS.md), [C1 results](MVP-B07b-C1-RESULTS.md) и их ограничения не меняются. При расхождении с ранним [B07b-P](MVP-B07b-INTEGRATION-CONTRACT.md) предлагается уточнение только durable draft boundary; оно требует принятия владельцем.

## 1. Минимальный feature contract

Actor → trusted course author, пока без утверждённого mapping полномочий → import controlled synthetic ZIP, private diagnostics и две отдельные preview → неизменяемые owner/org/course/context, целостность обеих projections, текущие restrictions и запрет student access → ImportAttempt + immutable file generation + неизменённый C1 BuildReceipt → Receiving → Validating → Ready либо Invalid/Failed/Cancelled; затем Expired → authority matrix §4 → negative/concurrency tests §9.

Ни compiler success, ни possession ImportId, ни Teacher/OrgAdmin flag не дают нового права. До решения OD-C2-01 все пользовательские import/preview operations запрещены. Нет HTTP, renderer, publication, CMS, общего blob service, новых ролей или реализации PRS1. Только synthetic fixtures с доверенным provenance: произвольный ZIP с флагом synthetic не допускается.

Ready означает atomic metadata visibility приватного проверенного кандидата, а не опубликованную версию. Файловая система и PostgreSQL не образуют общую ACID transaction: сначала durable immutable bytes, затем короткий metadata commit; обратный порядок запрещён. Сбой может оставить недоступный orphan, но не разрешает частичный Ready.

## 2. Минимальная схема — предложение, не DDL

Один aggregate ImportAttempt; файловые descriptors можно хранить закрытой структурой в строке, без универсального object catalog.

| Поля | Семантика / ограничение |
|---|---|
| ImportId UUID | Серверный opaque PK; никогда не переиспользовать |
| OrganizationId, CourseId, OwnerActorId | Неизменяемые server-resolved ключи; composite course/org FK на verified registry, Actor FK без cascade в историю; owner не передаётся другому actor |
| CommandId, RequestFingerprint | Unique (OrganizationId, OwnerActorId, CommandId); fingerprint связывает CourseId, заявленные ZIP digest/length и SourceRevision. До seal сверить заявленный digest с вычисленным сервером; это не authority |
| State, Version | Closed enum §5, bigint CAS; каждый переход/claim/pin меняет Version; overwrite Ready content запрещён |
| CreatedAt, ExpiresAt | Доверенное время admission; предложение TTL 24h без sliding extension. TTL только synthetic, не legal retention |
| ContextId, ContextVersion, BindingReference | Неизменяемый контекст admission и ссылка на binding/его интервал; same-org FK. Историческая привязка не заменяет current checks |
| SyntheticFixtureReference | Доверенная ссылка на controlled fixture/evidence; не client assertion и не PRS1 exact provenance |
| SourceRevision, SourceDescriptor? | Метка автора, не attested Git commit; server storage generation/key, byte length, SHA-256 sealed ZIP |
| StudentDescriptor?, TeacherDescriptor?, ReceiptDescriptor? | Каждый: opaque key в этой generation, length, storage integrity digest; assets перечислены закрытым manifest. Null до Ready, все три обязательны в Ready |
| ResultGeneration?, ReceiptRevision? | Связь attempt с одной принятой sealed generation и существующим C1 receipt; не меняет receipt bytes |
| ExecutionEpoch, WorkerReference?, LeaseUntil? | Fencing identity конкретного запуска и liveness hint. Lease expiry не доказательство остановки процесса |
| LastDecisionReference?, FailureCode?, Diagnostics? | Ссылка на atomic audit с current context/restriction decision; фиксированный error vocabulary и bounded escaped diagnostics B07a/C1; без stack/stdout/source snippets |
| CleanupState, CleanupEpoch | None/Pending/Deleting/Deleted/Quarantined; отдельно от результата compile. Durable deletion claim, fencing и crash recovery |

Минимальный command journal ImportTransitions: (ImportId, OperationId) unique, RequestFingerprint, FromVersion, ToVersion, Outcome, AuditReference. Создаётся в том же commit, без отдельного сервиса/outbox; необходим для восстановления неизвестного commit и отсутствия duplicate audit. Admission CommandId сохраняется и после удаления bytes; срок удаления tombstone/journal требует отдельной retention policy. До неё в synthetic slice metadata не очищать автоматически.

Ограничения: same-org ссылки обязательны; owner/course/context immutable; state transition + version + journal/audit atomic; source descriptor после seal immutable; result descriptors только вместе в Ready; DB runtime role не имеет обходного UPDATE/DELETE. Restricted writer и constraints проверяются на disposable DB до реализации. Никаких изменений существующих migrations сейчас.

BuildReceipt **сохраняется byte-for-byte в формате C1**: receiptRevision, zipDigest, packageHash, studentProjectionHash, teacherArtifactHash, sourceRevision, compilerRevision, contractRevision, runtimeFingerprint, limits. ImportId/org/actor/context/timestamps живут в ImportAttempt/audit, не добавляются в receipt. Это уточняет более широкий логический receipt из B07b-P и сохраняет воспроизводимость C1. SHA-256 физического файла не заменяет B07a canonical semantic hashes.

## 3. Доверенная цепочка и ProcessingRestrictions

1. ActorId и identity/session поступают из trusted S2/S4 adapter; org из проверенного single-org authority. CourseId запроса — только selector. Resolve verified CourseRegistry → enabled Organization → same-org course; запрос не задаёт owner/context/path.
2. Проверить право course author по OD-C2-01 и ownership attempt. Group-only teacher недостаточен, даже если общий Course Read сейчас проходит ScopePolicy. Существующий `CanAccess(Course, Read)` не заменяет специальный contract.
3. Разрешить единственный действующий CourseContextBinding → immutable ProcessingContextVersion этой org, интервал, SyntheticOnly и controlled source provenance. Missing/ambiguous/expired/unknown → deny. Сохранить admission refs; при смене binding/version attempt не перепривязывать: запретить content operations и требовать новый import.
4. При каждом read до content/diagnostics materialization проверить текущий контекст и current restrictions, без allow cache. При writes повторить проверку в mutation transaction §6. Проверять admission context и текущую привязку; historical allow/receipt не отменяет новый запрет. DB outage/time qualification loss → deny.
5. Affected sources — ZIP, обе projections, receipt/diagnostics и referenced assets; draft не включает student work/assignment. Affected subjects нельзя считать пустыми по отсутствию parser errors: следующий slice допускает только заранее проверенные synthetic fixture subjects; неизвестный/неполный набор → deny. Restriction на любого declared subject либо actor либо whole context запрещает операцию; student projection не повод игнорировать teacher/source dependencies сборки.
6. PRS1 exact OFF. Пока нет принятого operation mapping импорта, не выдавать его за Read/Submit/Review, не использовать reserved 8/16, не создавать exact sidecar из parsing. Предлагаемое консервативное правило нового adapter: любой Active restriction применимого context/subject блокирует content operation независимо от opaque ResourceRef/operation mask. Оно не сужает существующий gate и требует review вместе с OD-C2-01/02.

Ordinary signed authority сохраняет ADR-L15/16 delayed revocation; нет per-request current-grant barrier. Новый worker step и recovery не наследуют вечное admission allow: fresh authority/context перед запуском и Ready commit, новое разрешение при delivery. Это job boundary; отсутствие доступного approved fresh-authority path останавливает шаг. Cleanup не раскрывает content и не требует прав ушедшего автора; служебное удаление допускается только по принятой retention policy, с audit и без обхода hold/pin.

## 4. Authority matrix и точное owner decision

| Trusted application operation (предложение, не API) | Authority / data source | Результат и ограничения |
|---|---|---|
| BeginImport(CommandId, CourseId, expected digest/length, SourceRevision) | Trusted actor + OD-C2-01; S3 verified course; current context/restrictions | Receiving; owner задаёт сервер; не принимает arbitrary path/URL |
| SealSourceAndValidate(ImportId, Version, OperationId) | Тот же owner/course authority; trusted orchestrator + execution epoch | Sealed source → Validating; limits C1 без ослабления |
| CompleteValidation(ImportId, epoch, result) | Только orchestrator; повтор authority/context/write gate | All-or-nothing Ready или failure; worker не пишет DB |
| GetImportStatus / GetDiagnostics | Owner + course-author authority и current processing | Bounded private status/diagnostics; forbidden/not-found одинаковый внешний результат |
| ReadPreview / ReadPreviewAsset(ImportId, projection, assetId) | Owner + отдельная проверка projection; Ready, not expired, current gate | Только выбранный student/teacher bundle и точный reachable manifest; не teacher JSON с фильтрацией |
| CancelImport(ImportId, Version, OperationId) | Owner + course-author boundary | Только активная попытка; stop/epoch fence, terminal лишь после quiescence |
| Reconcile / Expire / Cleanup | Trusted narrow infrastructure executor, не human educational grant | Только фиксированные переходы/удаление; нет произвольного чтения/export |
| PinForPublication | **DISABLED — B08 NOT AUTHORIZED** | Нет public/user operation, нет права по Teacher/Review/OrgAdmin |

Source ZIP download не входит в минимальные operations. Receipt доступен orchestration/owner diagnostics только через тот же private gate; ни receipt, ни teacher hashes/diagnostics не включаются в student preview payload. Ученику запрещены обе draft projections и факт существования. OrgAdmin/platform operator не получают чтение содержимого по административной роли. Списки/поиск сейчас отсутствуют; будущие counts/pagination только после SQL authorization/processing filter.

**OD-C2-01 — предложенная точная редакция, NOT ACCEPTED:** «Разрешить в существующем trusted application contour операции создания собственного приватного synthetic import, его статуса/диагностики, cancellation и двух preview для Actor с действующим whole-course TeacherAssignmentScope (GroupId=null) и согласованной Teacher membership generation данной организации/курса. Group-scoped assignment, Student, OrgAdmin/platform role и один Course Read не дают этих операций. Операции доступны только owner attempt; право не включает publication, source export или чтение чужих drafts. Новых ролей, permissions bits, JWT claims и PRS1 masks не вводить; принять явный adapter mapping и conservative restrictions §3. Обычная JWT freshness остаётся ADR-L15/16; job steps проверяют fresh authority». До принятия — deny. Если владелец предпочитает отдельную capability, требуется отдельное решение её issuance/provisioning/revocation; этот документ её не создаёт.

**OD-C2-02 — предложенная точная редакция, NOT ACCEPTED:** «Для отдельно разрешённого synthetic draft slice принять TTL 24h от admission, без продления чтениями; закрытый draft writer и общий per-organization processing guard для import mutations, context-binding и restriction writers по §6; tombstones сохранять до отдельной retention policy. Это processing serialization, не ordinary JWT revoke barrier. Pin/publication и production retention исключены». Если общий guard нельзя обеспечить всеми relevant writers, Ready writes остаются blocked, даже при Serializable.

## 5. State machine и physical storage

| Переход | Условие |
|---|---|
| отсутствует → Receiving | Acknowledged admission commit, unique CommandId, authority/context и quota admission |
| Receiving → Validating | Source sealed/durable, фактический digest/length совпал, epoch claim, повтор gate; receive writer закрыт |
| Validating → Ready | C1 Ready, обе projections и receipt независимо проверены parent, worker teardown подтверждён, sealed durable result, CAS и gate commit |
| Receiving/Validating → Invalid | Отклонён ZIP/content, writers quiescent; без result pointers |
| Receiving/Validating → Failed | Runtime/integrity/timeout/cleanup fault; failure код; без result pointers; незавершённое I/O остаётся quarantined |
| Receiving/Validating → Cancelled | Cancel выиграл CAS и teardown подтверждён; без result pointers |
| Ready → Failed | Integrity loss при read/recovery; fail closed, атомарно закрыть доступ, сохранить audit/descriptors для расследования, quarantine |
| Ready/Invalid/Failed/Cancelled → Expired | TTL наступил, нет pin, active writers отсутствуют; CAS |

При TTL активного Receiving/Validating доступ немедленно запрещён; сначала stop/reconcile, затем Failed/Cancelled → Expired. Нет resurrection/recompile той же попытки; новый явный retry — новый ImportId/CommandId. Cancel после Ready — conflict, не неявное удаление. UnknownCommit — ответ операции, не состояние persisted attempt.

Private root вне web/static: server-generated org/course/import/epoch directories; source, student, teacher, receipt физически разделены. Keys не принимаются от клиента; no-follow opens, regular files, no links/special files, закрытые manifest entries и owner-only permissions. Dedicated service identity и trusted host — необходимая предпосылка; chmod не защищает от same UID/root. Никакой общей deduplication между org/attempt, публичных URL или StoredAttachments с фиктивным parent.

Последовательность seal: создать уникальную staging generation без overwrite → bounded write → проверить все bytes/length/digests → закрыть writable handles → fsync файлов и каталогов → atomic no-replace rename на том же поддержанном filesystem → fsync parent directories. Не считать rename без fsync crash durability. Source seal предшествует Validating; result seal предшествует Ready. C1 возвращает in-memory bytes после transient cleanup; durable adapter записывает их отдельно, не превращает внутренний tmpfs C1 в permanent storage.

DB не видит незавершённых generations. Preview авторизует parent, проверяет descriptor/manifest/integrity и expiry перед выдачей bounded bytes; streaming HTTP пока отсутствует. Повреждение/потеря любого обязательного Ready artifact запрещает обе projections; read failure не превращается в частичный success. Admission quotas предложены как один active compile/actor и два/host из B07b-P; durable reservation/release и disk budget проверяются до slice acceptance, это не доказанная capacity C1.

## 6. Transaction / lock protocol

Application orchestrator владеет каждой короткой PostgreSQL transaction; file/process I/O выполняется между transactions. Предложение: Serializable, dedicated clean context; retries только при **доказанном abort**, с тем же OperationId и новыми current checks, без повторного запуска compiler. Не вызывать `PostgresCurrentProcessingGate.Allows` внутри write: он отвергает ambient transaction и открывает собственный RepeatableRead snapshot.

Общий порядок: если операция действительно использует S2 session/issue locks — AccountSecurityState → AspNetUsers → ActorIdentities → IdentitySessions (stable order), затем S3 Organizations row, затем proposed ProcessingGuard(OrganizationId), затем ImportAttempt, затем transition/audit inserts. Никогда не брать S2 locks после org/import. Обычный JWT read не приобретает S2 locks. Один org/attempt на command; никаких cross-org batch transactions.

ProcessingGuard — один proposed row на org, Revision CAS. Его существование гарантируется до admission; missing → deny, не lazy fallback. Каждый relevant restriction insert/update/withdrawal, binding/context eligibility change и draft content mutation обязан брать этот guard и менять Revision. Если writer берёт org lock, берёт его **до** guard. Guard блокирует phantom restriction insert; lock одних найденных restrictions не достаточен. Эта часть ещё не реализована и требует OD-C2-02/B11; Serializable отдельного importer без совместимого writer не считается доказательством. После ожидания locks читать fresh state; stale Serializable snapshot/serialization failure → abort и новая transaction. Контекст/restrictions сравнить с guard revision и повторно проверить время перед mutation.

Admission commit: resolve trusted chain, gate, insert Receiving + unique CommandId + audit/journal. При conflict читать существующий результат лишь после текущей authorization; другой fingerprint → conflict. До acknowledged commit bytes не принимать.

Start commit: после source seal проверить descriptor, state=Receiving, expected Version, deadline/current gate; записать source pointer и execution epoch, Validating, audit. Затем запуск C1, без открытой DB transaction. Crash до запуска не допускает автоматического второго запуска: recovery §7.

Ready commit: заранее sealed result + confirmed teardown; под locks проверить state=Validating, epoch, version, отсутствие cancel/cleanup/pin, время < ExpiresAt, неизменность chain и current processing/fresh job authority. Одним commit установить все pointers, Ready, Version+1, decision refs и transition/audit. Success вернуть только после acknowledged commit/disposal и новой delivery/time authorization. Потеря delivery authority не откатывает состоявшийся Ready.

Cancellation сначала увеличивает ExecutionEpoch/Version и фиксирует stop intent (FailureCode=cancel_requested) в активной строке; старый completion CAS больше не проходит. После подтверждённого stop → Cancelled. Если Ready уже выиграл locks/commit, cancel получает conflict. Timeout аналогично fence/stop, затем Failed; не называть timer trigger доказательством quiescence. Late writers могут писать только в старую непубликуемую generation и не получают DB authority.

Audit/journal в одной transaction; audit failure откатывает state. Audit содержит IDs, transitions, decision refs/codes, без токенов, source text/host paths и диагностических secrets. Filesystem seal остаётся orphan при aborted DB write; compensation никогда не удаляет потенциально committed generation вслепую.

## 7. Failure / recovery

| Сбой | Решение |
|---|---|
| Timeout/cancel C1, OOM, invalid output | Закрыть visibility; fence, stop/reap всего дерева; только подтверждённый stop даёт Cancelled. Failed/quarantine при невозможности доказать stop; не обещать hard bound |
| Crash Receiving / partial source | Recovery приобретает locks, fencing epoch, убеждается в отсутствии live writer; Failed и cleanup. Никакого resume произвольного partial ZIP |
| Crash Validating / lease expired | Lease лишь сигнал. Установить fence, найти worker по trusted reference, подтвердить termination; Failed. Автоматический recompile этой попытки исключён |
| Crash после seal до Ready | Непривязанный result остаётся private orphan. Сначала разрешить DB outcome; затем quarantine/cleanup, не автоматически Ready |
| Ready commit выполнен, ACK потерян | UnknownCommit, без success/artifacts и без delete/replay. Reread journal+attempt на authoritative primary под теми же locks; matching operation/epoch/pointers означает committed. Replicas/absence during pending backend не доказательство rollback |
| Outcome всё ещё неизвестен / DB недоступна | Сохранить bytes, запретить delivery/cleanup; pending reconciliation. Подтверждать abort только после завершения исходной transaction и authoritative lock/read. Не вводить timeout «значит rollback» |
| ACK был, ответ потерян | Повтор OperationId возвращает сохранённый metadata result после новой auth/current gate; не повторяет compile или audit |
| Crash deletion | CleanupEpoch + Deleting сохраняются; повтор safe delete той же generation после quiescence, fsync directory; absent bytes idempotent success, permission/I/O errors → Quarantined |
| Потеря/коррупция Ready bytes, несогласованный restore DB/FS | Read deny, Ready→Failed/quarantine; проверить все descriptors при recovery. Не пересобирать под старым receipt; backup/restore acceptance отдельно |

Отказ systemd/DBus, D-state, смерть orchestrator, storage/WAL/failover и qualification clock остаются proof gaps C1/G02. Никакой recovery не обещает T04 completion. Отсутствие delete bounds даёт quarantine, а не заявленное физическое удаление к TTL.

## 8. Expiry / cleanup / pin и граница B08

Expiry access predicate действует независимо от janitor: qualified latest time >= ExpiresAt → deny, clock unknown → deny. Чтения не продлевают TTL. Для bounded in-process preview после чтения/проверки bytes повторить state/version/expiry/current gate до возврата; гонка удаления даёт deny, не частичный bundle. Длительные streams и read leases в этом slice не вводятся.

Cleanup под общими locks/CAS переводит unpinned expired attempt в Deleting, фиксируя CleanupEpoch; проверяет отсутствие unresolved commit и live writer. После этого никакой Ready/pin/preview не разрешён. Удаление вне transaction, строго по server descriptor, затем acknowledgement удаления в короткой transaction. Orphan scan не удаляет «по возрасту»: необходимо установить quiescence и authoritative отсутствие committed/indeterminate reference, иначе quarantine.

В C2 pin отсутствует и всегда запрещён. Для совместимости с будущим B08 минимальный **не реализуемый сейчас** handoff contract: B08 добавляет уникальную private pin reference на ImportId + ResultGeneration + Version с собственным durable OperationId. Acquire pin под теми же org/processing/import locks только для Ready, not expired, CleanupState=None и отдельно утверждённого publisher. Pin commit раньше expiry claim → cleanup bytes запрещён; expiry/Deleting раньше pin → pin denied. После обычного TTL draft preview остаётся denied даже при B08 pin; pin не продлевает author rights.

Pin нельзя снимать по одному timeout: unknown publication commit требует reconciliation B08. Release только после доказанного abort publication либо подтверждения независимой durable PublishedContentVersion, с atomic removal/reference check. Crash оставляет удержание и audit для B08 recovery. Это граница будущего контракта, не реализация pin service или бессрочное publication authority.

B07b не пишет PublishedContentVersions, AccessLessons, ProblemAssignments; Published не добавляется в State. B08 отдельно утверждает publisher capability, ContentVersionId, full immutable content mapping, relation к receipt/hashes, open/assign и историческую exact version. Source ZIP/draft descriptors не становятся public storage и не выдаются ученику после одного pin.

## 9. Negative / concurrency acceptance plan — NOT RUN

Все cases требуют независимого ожидаемого результата и positive same-scope control, проверки bytes/metadata/audit, а не одного status. Это план будущих тестов, не новый PASS C1.

| Набор | Обязательный результат |
|---|---|
| Forged actor/org/course/context, чужой ImportId, missing owner/course/parent | Deny для каждого command/status/diagnostic/projection/asset; zero existence/count/content leak |
| Student, org admin, group-only teacher, чужой course-author draft | Deny; whole-course owner control проходит только после OD-C2-01 |
| Teacher manifest asset через student path; missing/unknown asset/hash; private-only package change | Deny; student bytes неизменны, teacher/receipt не попадают в student payload; private compile error блокирует обе projections |
| Missing/ambiguous context, wrong org, unknown subjects, synthetic claim без provenance, DB unavailable | Deny; no Ready, no delivery |
| Restriction insert/withdraw/binding change vs Ready | Оба порядка под barrier: restriction-first denies; Ready-first commit исторически допустим, последующее read denied. Проверить отсутствие restriction rows до concurrent insert |
| Revoked/regranted/moved scope, exp во время ожидания | JWT reads по accepted delayed semantics; fresh job step denies retired scope; no generation resurrection; history не меняется |
| Duplicate CommandId, другой digest/label/course, stale Version, два Complete | Один attempt/result/audit; mismatch conflict; late epoch denied |
| Cancel/timeout vs Ready; expiry в Receiving/Validating | Только победивший CAS; no Ready после stop intent/expiry, confirmed stop перед Cancelled |
| fsync/rename/disk full/partial source, tampered sealed generation | No Ready/partial artifacts; immutable no-replace; quarantine recovery |
| Actual COMMIT + lost ACK, rollback, backend still live, stale replica absence | No blind replay/delete; authoritative reconciliation отличает committed/aborted/unknown |
| Crash на каждой границе seal/DB commit/cleanup; late worker после fence | Restart не публикует orphan, не удаляет потенциально committed bytes, old epoch не получает pointers |
| expiry vs read/cleanup; будущий pin vs expiry/delete/release | No late preview, no pinned deletion; pin tests deferred B08, сейчас все pin operations deny |
| TTL exact boundary/clock loss, quota saturation, repeated retries | Fail closed, bounded admission, нет sliding TTL или duplicate effects |

Wrong group/assignment и historical move: draft не имеет assignment parent; нельзя подставить assignment/StoredAttachment для обхода. Общая S3 regression будущего adapter обязательна; создание assignment и immutable learning history здесь не меняются. Lists/counts N/A — operations отсутствуют; если их добавляют, сначала pre-materialization tests. HTTP/CSRF/Origin/browser cache/XSS/physical device/E2E tests deferred до B02/B11 и отдельного preview/HTTP scope, не покрываются этим design.

## 10. Reuse, зависимости и следующий минимальный slice

| Уже реализовано | Что переиспользовать / чего нет |
|---|---|
| [S2](L1-S2-RESULTS.md), [PostgresIdentitySessions](../src/Lms.Infrastructure/PostgresIdentitySessions.cs) | Stable user↔actor, sessions, rotation, lock order; не import privilege и не HTTP login |
| [S3](L1-S3-RESULTS.md), [authority contract](../src/Lms.Application/ScopedAuthority.cs) | Verified ownership, generations, org lock, CAS/audit, immutable assignment affiliation; ResourceKind не имеет Import, ScopeAction только Read/Submit/Review |
| [S4-A1](L1-S4-A1-RESULTS.md), [A2](L1-S4-A2-RESULTS.md) | Internal JWT, qualified clock accounting, commit-safe issuance, key trust, current conservative read gate; не write receipt, файлы/HTTP запрещены ComponentAuthorization |
| [Current processing gate](../src/Lms.Infrastructure/PostgresCurrentProcessingGate.cs) | Правила current context/deny; собственная RepeatableRead transaction, нет mutation serialization или full source/subject resolver |
| [PRS1 I1](L1-S4-A4-I1-RESULTS.md), [I2-P](L1-S4-A4-I2-P-RESULTS.md) | Parser/registry component; I2-P findings не trusted runtime provenance. Exact OFF, I2 implementation не разрешена |
| B07a / C1 | Неизменённые compiler, ZIP validation, isolated bounded worker, split artifacts/receipt; нет DB/storage recovery, host quota service и browser renderer |

Точные зависимости по [MVP backlog](MVP-BACKLOG.md) и [roadmap](MVP-IMPLEMENTATION-ROADMAP.md):

- **MVP-B01 = G02 safe synthetic HTTP gate**, не одноимённое принятое S3 решение Generation-aware Enrollment Move. Нужны принятые proof bounds clock/PostgreSQL/storage/egress и отдельное authorization; C1 resource caps его не закрывают. Изолированный будущий persistence experiment возможен только с отдельным разрешением и без HTTP/readiness claims; functional server/file integration ждёт применимые G02 obligations.
- **MVP-B02 = session/browser integration**: trusted authenticated actor/single-org issuance, login/refresh/logout/switch/reload, O01 cookie/CSRF/Origin и clearing caches. Нужен до пользовательского upload/preview; internal S2/A2 reusable без объявления B02 completed. Dev bearer — не замена.
- **MVP-B11 = общий scoped authorization cutover**: ownership/processing filters, transaction-compatible current restrictions и review каждого write adapter. Для Ready необходим совместимый writer protocol §6, для reads — approved import/projection mapping. Полный B11 HTTP slice сам ждёт B01/B02 и отдельного S4/S7 разрешения; локальный contract не объявляет его выполненным.
- **B06** предоставляет существующий verified course/author scope через accepted provisioning; C2 не создаёт курс/grant. **B08** не prerequisite для private unpinned lifecycle; нужен только для pin/publish handoff и publisher decision. Это сужает общую roadmap-зависимость B07b→B08 для данного draft-only этапа, не разрешает publication.

Минимальный следующий implementation slice — **предложение, NOT AUTHORIZED**:

1. Принять/отклонить OD-C2-01/02, schema/storage/state protocol; отдельно разрешить isolated synthetic persistence component, определить поддержанный filesystem/DB profile. Проверить lock compatibility всех restriction/context writers; если нет — остановить Ready writes.
2. Только на disposable DB/private filesystem: ImportAttempt + journal + processing guard, trusted internal operations Begin/Seal/Complete/Status/Diagnostics/Preview bytes/Cancel/Expire/Reconcile/Cleanup. Fixed controlled fixtures, unchanged C1, отдельные additive candidate migrations только после разрешения. Никаких endpoints, DI production, UI, generic jobs/storage.
3. Исполнить §9 DB/FS fault/concurrency matrix, migration empty/upgrade checks и targeted S2/S3/S4 regression; проверить branch/index/foreign files. Owner review результата, без server acceptance по одному green test run.
4. Отдельный subsequent gate для functional server preview: B01/B02/B11, file bounds и renderer/cache/security tests. B08 требует самостоятельного разрешения и не включается автоматически.

## 11. Ограничения и документационная проверка

Все ограничения [C1 results](MVP-B07b-C1-RESULTS.md) действуют без изъятий: Linux x86_64 и pinned runtime; narrow ZIP profile; trusted host/UID/runtime installation; cgroup worker limits не ограничивают весь parent; CPU sampling overshoot; wall cap только worker interval; нет hard parent I/O/control-plane/scheduler bound, orchestrator-death proof, systemd/DBus/D-state guarantee или universal archive/polyglot audit. Новых performance/durability/security proofs этим документом не получено. 49 C1 / 74 B07a PASS — историческая evidence, в C2-P не повторялась.

Guardrails §7 self-review:

- covered — actor/use case/states, authority/data-source matrix, immutable ownership/history и current restrictions описаны; missing capability явно blocked OD-C2-01.
- covered — S2/S3 temporal semantics сохранены; PRS1 exact/G02/S4 HTTP/B08 gates не пересечены.
- covered as design — source/subject evidence, asset parent, transaction/lock/CAS/audit/unknown outcome, limits/errors; runtime correctness **NOT PROVEN**.
- not applicable — lists/counts/aggregates: не включены; HTTP/frontend/E2E/runtime/migration tests **NOT RUN**, documentation-only.
- covered — только этот документ и append register; нет schema/API/runtime/security changes, real data, secrets или claims release readiness.
- blocked for implementation — owner mapping/TTL/processing writer decisions, durable filesystem profile, fault tests и применимые B01/B02/B11 gates.

Specialist skills model-domain/implement-use-case/implement-feature отсутствуют в доступном каталоге; их выполнение не заявляется. Canonical `/home/tea/github/lms`, существующая dirty branch `codex/l1-s4-jwt-api-authorization` сохранена вместо переключения на main. Проверка ссылок, whitespace, append-only register и сохранности исходных файлов/HEAD/branch/index выполняется отдельно; runtime tests NOT RUN.

**B07b-C1 ACCEPTED · B07b-C2-P READY FOR OWNER REVIEW · B07b SERVER IMPLEMENTATION NOT AUTHORIZED · B08 NOT AUTHORIZED · PRS1 EXACT OFF · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**

Фактическая документационная проверка: `python3 - <<'PY' … PY` (stdlib pathlib/re/hashlib/subprocess; baseline `/tmp/b07c2-baseline.json`, исходный register `/tmp/b07c2-register-before`) — **PASS**: 37 локальных ссылок в двух документах разрешаются; whitespace новых строк корректен; register append-only; все 480 исходных файлов вне register неизменны по SHA-256; HEAD/branch/index совпадают с baseline. `git diff --check -- codex/L03-OWNER-DECISION-REGISTER.md` — **PASS**, exit 0. Runtime/DB/FS fault/browser/E2E tests — **NOT RUN**. Проверка — self-review, не independent security acceptance.
