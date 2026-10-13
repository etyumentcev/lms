# MVP-B01/G02 — normative review для synthetic MVP Sapientum

> **2026-10-12 — bounded teacher revocation.** [Действующий scoped contract](../docs/architecture/TEACHER-REVOCATION.md): fresh issue/refresh, JWT≤15m, immutable operation≤30m, revoke≤60m в выбранном bounded execution envelope. Для ordinary revoke двухчасовой D и D−10m superseded; неограниченный late COMMIT/egress не удовлетворяет новому контракту. История ниже сохранена. Processing/file safety, PRS1 и release gates не сняты; общий HTTP/physical bound NOT PROVEN.

> **MVP photo storage override — 2026-10-12.** [Owner-authorized exact diff](MVP-STORAGE-RISK-ACCEPTANCE.md): physical original tampering risk accepted; fs-verity/kernel seal, retained FD/mmap exclusion proofs and developer filesystem qualification removed only for Sapientum solution photos. Completed private upload, parent/S2/S3/current processing/deadline and all other NR1 guarantees remain required. STORAGE-PROFILE-02 CANCELLED; G02 production BLOCKED. Historical clauses/results below are preserved.


> **NR1 effective notice — 2026-10-11.** N1–N7 и ограниченный supersession D1–D8 приняты. Формулировки PROPOSED / NOT ACCEPTED, §§3/9 и итоговый статус ниже сохранены как история review до решения; они больше не описывают нормативный статус NR1. Сам текст N1–N7/D1–D8 не изменён. Техническая реализация NOT IMPLEMENTED / NOT ACCEPTED.
> Нормативный источник: [N1–N7 / точный D1–D8](MVP-B01-G02-NORMATIVE-REVIEW.md), [принятое решение](L03-OWNER-DECISION-REGISTER.md), [sync results](MVP-B01-G02-NR1-SYNC-RESULTS.md). Только controlled SyntheticOnly и отдельно принимаемые routes; неопределённый scope → blocked. Credential issuance, grants, B08 publication, general jobs и внешние irreversible effects не получают автоматического исключения. CLOCK NOT PROVEN; технически NR1 NOT IMPLEMENTED / NOT ACCEPTED.
> Исторический текст ниже сохранён; старые failure results не превращены в PASS.

2026-10-11. **DOCUMENTATION ONLY · PROPOSED, NOT ACCEPTED · STOP AT OWNER REVIEW.**

## 1. Основание и рекомендация

[Последнее решение владельца](L03-OWNER-DECISION-REGISTER.md) принимает [P1](MVP-B01-G02-P1-QUALIFICATION.md) как документальный NO-GO и останавливает квалификацию P1-SINGLE-LOCAL. T04/E03 действуют; A06 E03-C/N не приняты. Разрешён настоящий review, не применение его предложений. MVP-B01 здесь означает HTTP gate, не принятое S3 B01 Generation-aware Enrollment Move.

Рекомендация **NR1**: отдельно принять для контролируемого SyntheticOnly MVP три обязательства: deadline действий приложения; сериализованную безопасность domain decisions; учёт физически незавершённых работ и восстановление. Не требовать hard physical completion произвольного внешнего I/O как общего release gate. Это реальное изменение временного security contract, а не доказательство прежнего T04 и не переименование A06.

Два явных компромисса требуют согласия владельца: (1) уже переданные транспортному стеку bytes могут выйти позже deadline; (2) transaction, принявшая допустимое решение до cutoff, может стать durable/visible позже cutoff и даже после D/23m. Нельзя одновременно допустить неограниченный COMMIT и обещать отсутствие любой поздней видимости. Если владелец требует именно отсутствие late visibility/last controlled send, NR1 следует отклонить: остаётся прежний NO-GO. Clock uncertainty не снимается этим решением: true JWT≤900s и conservative expiry сохраняются, значит неподтверждённая временная база всё ещё блокирует admission/issuance.

Практическая выгода NR1 — замена недоступного универсального bound PG/OS/egress на проверяемые transaction, isolation, recovery и application-deadline contracts. Цена — отказ от wall-clock срока всех последствий уже принятой операции и потенциально длительная недоступность при unknown. Это не гарантия готовности P1-SINGLE-LOCAL.

Источники: [ADR-L15](../docs/adr/ADR-L15-jwt-delayed-revocation.md), [L16](../docs/adr/ADR-L16-organization-jwt-browser-session.md), [L17](../docs/adr/ADR-L17-account-security-recovery-challenges.md), [L18](../docs/adr/ADR-L18-synthetic-processing-restrictions.md), [G02 I1–I7](../docs/architecture/L1-G02-BOUNDED-EXECUTION.md), [A06 diff](L1-G02-A06-NORMATIVE-DIFF.md), [A06 model](L1-G02-A06-FAILURE-MODEL.md), [A06 decisions](L1-G02-A06-OWNER-DECISION.md), [closure plan](MVP-B01-G02-CLOSURE-PLAN.md), [guardrails](SAPIENTUM-MVP-ENGINEERING-GUARDRAILS.md), [reconciliation](MVP-GUARDRAILS-RECONCILIATION.md). Использованы repository contracts и исторические evidence; новых vendor guarantees/экспериментов не получено.

## 2. Сравнение трёх моделей

| Обязательство | Действующий T04/E03 | A06, не принят | NR1, предлагаемая ограниченная модель |
|---|---|---|---|
| Response | 60/480s до full completion и last controlled send | Те же 60/480s до last controlled send, включая kernel queues | 60/480s — единый budget действий приложения; gate на каждом новом действии; abort при обнаружении expiry. Hard last-send при fault не обещается |
| Domain effect | COMMIT и эффект завершаются внутри общего bound и до D | Publication p<d и authority cutoff, зависший visibility-changing COMMIT должен завершиться/fence вовремя | Решение q до cutoff под transaction guards; commit может проявиться позже. После q нет нового domain decision от старого executor; late commit допустим только для разрешённого класса |
| Physical completion | Внутри B, включая rollback/handles/storage | Только доказанно private I/O может остаться pending | Изолированный I/O, ранее выданный COMMIT и cleanup учитываются отдельно; универсального B/RTO нет |
| Unknown | Не rollback, no replay; pending work всё равно обязано закончиться в B | No replay; unknown timing publication остаётся blocker | No replay; авторитетное resolution без срока, ресурсы остаются учтёнными, новые conflicts deny |
| E03 | Admission ≤15m, completion условно ≤23m | Admission ≤15m, effects/egress условно ≤23m, private tail возможен | Admission ≤15m сохранён; новые decisions ограничены request cutoff; late visibility/transport tail после 23m возможны, full-stop обещание явно снимается |
| Clock/JWT | Conservative bound обязателен | Обязателен | Обязателен для authority; dispatch hard bound при произвольном OS stall не заявляется |
| Что остаётся blocker | PG/egress/clock/full I/O evidence | PG/egress/clock + isolation/recovery | Clock authority evidence, coverage gates, transaction serialization, immutable storage, unknown/capacity/restart evidence |

NR1 не меняет область организационных полномочий, не разрешает B08 и не делает новые publications/permissions допустимыми по одному старому JWT. Новая модель для learning mutations включает только заранее перечисленные Submit/Review/attachment claim и отдельный будущий ImportAttempt writer; перечень требует route-by-route acceptance. Ready не Published. Grant creation, B08 publication, general jobs, external messages/payments и irreversible external effects не получают исключение late COMMIT автоматически. Для них сохраняется прежний gate до отдельного контракта. S2 identity transactions требуют отдельной проверки §7, без изменения E01/E02/O01/E04/E05.

## 3. NR1 — точный предлагаемый нормативный addendum

Следующие N1–N7 — полный нормативный текст кандидата. До отдельного принятия не действуют. Минимальный diff — **одна append-only запись владельца из §9, включающая N1–N7 по ссылке**, с точным ограничением supersession из §4. Исторические ADR и A02/A06 не удаляются и не переписываются. Документационная синхронизация ссылок и effective-date notice потребует следующего задания; сейчас никакого normative patch не применяется. Числа сохраняются, меняется смысл 60/480 и completion/revoke guarantee, это явно указано ниже.

### N1. Response deadline

`t0` — earliest controlled ingress, до очереди/header/body; `d=t0+B`, B=60s ordinary, B=480s только для явно designated upload/download. Budget не возобновляется активностью, retry, очередью или restart; каждый Range — новый авторизованный запрос. Trusted remaining budget только уменьшается. Приложение должно проверять deadline/cancellation перед admission, каждым новым stage, mutation decision, dispatch COMMIT и каждым enqueue response chunk. При expiry оно прекращает попытку ответа/новой выдачи, инициирует cancellation/abort и не запускает новые действия старой операции. ACK после d не разрешает success body, JWT или cookie в старом ответе.

При работоспособных clock/scheduler enforcement должен срабатывать в принятом deployment envelope, с cutoff margin для измеренного и обоснованного dispatch budget; acceptance фиксирует этот envelope и нарушения. При остановке ОС/scheduler обещание выполнения к конкретному wall-clock моменту отсутствует; после возобновления обязательна проверка до дальнейшего действия. Check-to-send/check-to-COMMIT race с OS pause не объявляется устранённой. Уже issued I/O/COMMIT и queued transport bytes могут закончиться поздно по N2/N3. Физический last controlled TX≤d не обещается даже если close был вызван вовремя. Клиентская доставка тем более не ограничена. Application gate не заменяет авторизацию и не разрешает buffer reuse между пользователями.

### N2. Domain effect safety

Для разрешённых learning mutations в одной PostgreSQL transaction сохраняются canonical OperationId/idempotency binding, payload digest, actor/org/course/context/generation, expected versions, domain change, exact file references, result и обязательный audit. Внешний I/O внутри этой transaction запрещён. `q` — последняя успешная проверка domain/processing/authority/time перед фиксированным набором mutations под всеми необходимыми locks/CAS. Это **authorization decision point, не физический COMMIT timestamp**. Проверка времени в transaction не доказывает время commit.

Для q должны выполняться исходный d и применимый authority cutoff D, принятый JWT admission и текущие workflow/eligibility/context/restrictions. Истечение JWT после корректного admission само по себе не вводит новое право и не продлевает d. Ordinary revoke сохраняет принятую delayed семантику; актуальность обычных grants на каждом read не вводится. Jobs, issuance и другие специальные paths сохраняют свои более строгие fresh checks. После ожидания locks нужны fresh reads и новая проверка; cached Allows не принимается. Все конкурирующие restriction/context/claim writers обязаны сериализоваться теми же guards; phantom insert также охвачен. Неизвестный/missing guard → deny.

До q истёкшая/недопустимая операция не может создать domain mutation. После q допустим только неизменный атомарный набор уже проверенных mutations данной transaction. Более поздний COMMIT этого набора может стать видимым после d/D; это явно разрешённое исключение времени видимости, а не новая авторизация expired command. Если cancel/expiry обнаружен до dispatch COMMIT, приложение не отправляет COMMIT; пытается rollback, сохраняя unknown до доказательства. Если COMMIT уже выдан либо pause произошёл между последней проверкой и dispatch, исход может быть поздним. Повторная transaction, дополнительная publication, новый grant, новая claim, outbox delivery, новый worker stage или продолжение после rollback от старой authority запрещены.

Сериализация должна обеспечивать: если prohibition/change победило первым, q видит его и deny; если q победило первым, conflicting prohibition/change не подтверждается как committed раньше этой transaction. Это сохраняет current ProcessingRestrictions при любом порядке. Время ожидания restriction commit может быть неограниченным; UI не должен объявлять запрет применённым до подтверждённого commit. Не менять accepted temporal semantics ordinary revoke/group generations/history. После позднего commit каждый новый read проверяет текущие restrictions/parent/scope, и может deny ранее допустимый результат. Сам поздний факт остаётся в неизменяемой истории, без автоматической компенсации.

Нет исключения для позднего создания нового полномочия или B08 publication. Нельзя выдать q за durable decision до успешного commit, вернуть успех по одному q или объявить unknown rollback. Строка q/time в БД — audit факта проверки при доверенных clock/code, не hard timing oracle.

### N3. Physical completion и recovery

Storage/worker без domain credentials пишет только в уникальный private namespace старого operation/generation; namespace не переиспользуется. Только доказанно неизменяемая exact version с подтверждённой durability/hash/length может стать eligible. Retained writer handles и issued writes должны быть исключены до seal/claim; rename/chmod/hash сами по себе этого не доказывают. Late worker output не создаёт Ready/parent/reference/permission. Если immutability не доказана — quarantine, путь blocked.

OutcomeUnknown, PhysicalPending и delivery Closed — независимые состояния. Timeout PostgreSQL, RequestAborted, disconnect, timeout ожидания rollback и kill request не являются доказательством rollback. Unknown запрещает automatic replay/resume, в том числе с новым key, через refresh, recovery или service identity. Resolver устанавливает прежний outcome на authoritative primary с сериализацией с исходной transaction и невозможностью возврата старого writer/primary. SELECT-not-found/replica lag не доказательство abort. Committed возвращает прежний факт после новой авторизации; Aborted допускается только при доказанном отсутствии эффекта и невозможности late commit. После доказанного abort пользователь может явно создать новую авторизованную команду; старый OperationId terminal.

Регистрация identity/capacity durable до side effects; unknown registration → side effects не начинать. Слоты, pending transactions/workers, staging/orphan bytes и очереди имеют численные caps до admission; timeout не освобождает их. При недоступном учёте/recovery или исчерпанном cap — reject новых операций затронутого domain. Никаких бесконечных replacement workers. Restart закрывает admission до reconciliation/epochs, без resume старой authority. При отсутствии поддержанного failover он отключён; нельзя объявить новый primary authoritative, пока старый может писать.

Cleanup — отдельная least-privilege exact-version maintenance operation после доказанного отсутствия refs, исключения concurrent claim и quiescence. Unknown originals сохраняются. Tombstone означает логическое скрытие, не physical deletion. TTL задаёт момент eligibility для cleanup, не гарантированный срок физического удаления; legal retention/erasure gates не закрываются. Универсального RTO для произвольного hardware/PG/storage outage нет; численные recovery objectives для поддержанного профиля принимаются отдельно и проверяются restore drill.

### N4. JWT и E03

Сохраняются true JWT≤900s, conservative expiry без positive leeway, signature/issuer/audience/algorithm/scope validation; fresh authority при issue/refresh; exp=min(Now+900s, earliest D−600s); exp≤Now — deny; D≤RevocationRequestedAt+7200s без продления retry; A/B isolation и generation semantics. Неизвестный UTC envelope → deny issuance/admission и новые q; после reboot/suspend/clock discontinuity нет автоматического восстановления trust. Monotonic timer не доказывает UTC bound. Отсутствующий clock contract остаётся release blocker. Для admission используется верхняя граница UTC interval: U(a)<exp; для q — U(q)<D при конечном D и conservative elapsed(q)<B от исходного t0. Issuer учитывает uncertainty так, чтобы true lifetime≤900s; положительный leeway запрещён. Условие Δ+λ+B≤600s остаётся достаточным для decision envelope при доказанных clock/dispatch предпосылках, но не ограничивает late COMMIT или last TX. Неизвестные Δ/dispatch нельзя подставлять как ноль.

SecurityBlocked атомарно запрещает login/issue/refresh, отзывает все refresh sessions и пишет audit; unblock отдельно, без resurrection. Уже допустимый JWT допускает admission только до true exp≤15m. Для разрешённых NR1 paths 60/480 ограничивают новые application actions/decisions по N1/N2 при поддержанном clock/execution envelope. Прежний вывод «полная операция/все effects/egress прекратятся через 23m» снимается: late COMMIT ранее допустимого набора и queued bytes могут проявиться позже, без нового полномочия. Ordinary revoke сохраняет прекращение новых admission/decisions в допустимом временном envelope, но **не** старую гарантию физического окончания/поздней видимости до двух часов. Без clocks нет безусловного wall-clock утверждения даже для admission.

### N5. Files, processing и jobs

Каждая фотография — отдельный private upload; Submit — новый JWT admission и атомарные owner/org/course/context/eligibility/expiry/claim checks вместе с решением. DB/files не общая transaction; unknown → quarantine и сохранение originals. Каждый download/Range/preview/thumbnail/annotation проходит independent parent/scope/current processing gate; pending/unverified файлы не выдаются. Private/no-store, нет public/static/CDN/presigned bypass; PG connection/lock не держится во время stream. Сохраняется synthetic restriction-update/write serialization и read check до materialization. Для уже принятого stream newly committed restriction не становится immediate kill: application budget N1 сохраняется, transport tail явно может быть позже. Это изменение прежней temporal защиты stream требует принятия NR1; реальная processing freshness остаётся отдельным pre-pilot решением.

Jobs не получают perpetual JWT authority: fresh authority/context/restriction перед каждым irreversible step и перед delivery, scoped immutable history, unknown deny. NR1 не разрешает general jobs или late publication через maintenance. Import Ready, B08, grant writers и остальные исключённые paths требуют собственного owner contract; перенос работы в фон не обход gate.

### N6. Admission и acceptance

Release gate проверяет application deadline coverage, domain serialization/no duplicate effect, immutable storage isolation, finite capacity, authoritative no-replay recovery и conservative clock/JWT. Hard completion любого внешнего I/O не общий prerequisite для разрешённых NR1 paths. Safety при fail-stop/crash/restart/partition/stall проверяется при сохранении доверенных DB atomicity/durability, kernel isolation и single authoritative history. Byzantine storage/OS/operator, потеря подтверждённого WAL и произвольная одновременная утрата всех защит не покрываются; результат — incident/restore/deny, не заявление безопасности.

До принятия конкретного supported profile и evidence каждого route G02 остаётся BLOCKED. Operational deadline miss регистрируется как failure относительно принятого envelope, а не становится PASS по факту cancellation. Наблюдаемый max/p99 не hard upper bound. Clock, authorization и processing failures нельзя скрыть как допустимую latency.

### N7. Неизменяемые ограничения scope

SyntheticOnly с controlled fixtures; никакого real-data pilot. S2/S3, E01/E02/O01/E04/E05/E06, org/course/group ownership, historical generations, restrictions conservative fallback, file-parent protection, secret-free audit сохраняются. PRS1 exact OFF; H04/Ready BLOCKED до собственного writer/evidence; B08 NOT AUTHORIZED. Принятие NR1 не квалифицирует P1, не принимает A06 E03-C/N и не разрешает runtime/schema/migrations/HTTP/cutover/deploy.

## 4. Точный минимальный diff: прежний текст → N1–N7

Ниже дословные изменяемые clauses и их replacement references. Все остальные clauses остаются в силе. Для принятого NR1 scope запись §9 имеет приоритет над перечисленными completion-требованиями и их математическими следствиями; для исключённых paths они сохраняются. Это ограниченный addendum, не механический глобальный replace слова completion. Дубли реестра T04/E03/G02 толкуются через этот же scoped override после принятия; исторические строки не редактируются.

### D1 — response/full-operation boundary

**Прежний текст (дословно):**

```text
Deadline измеряется от server admission, включая очередь, чтение body, DB работу и передачу ответа; активный трафик не обнуляет таймер. Обычный endpoint ограничен 60 секундами; явно длительный upload/download — максимум 8 минут. Истечение JWT внутри уже принятой операции само по себе не требует её прерывания. При admission до exp, exp ≤ AccessValidUntil−10 минут и duration ≤8 минут операция завершается до AccessValidUntil с 2-минутным остатком. Clock uncertainty/leeway учитываются формально ниже; buffering/cancellation/commit уже входят в полный максимум 8 минут, не добавляются из остатка. Conservative expiry не разрешает поздний admission; проверить синхронизацию часов и крайние значения реальными тестами.
```

**Предложенный текст:** точная соответствующая редакция N1; N4 заменяет только вывод completion до D, admission/JWT сохранены.

**Последствия и acceptance:** Отказ от hard last-controlled-send и общего physical bound; возможен tail после d. AT01/02/05.

### D2 — transaction cancellation и COMMIT

**Прежний текст (дословно):**

```text
Engineering requirement: согласовать edge/API/DB/storage deadlines; cancellation передавать до I/O и transactions. Timeout/RequestAborted прекращает работу, rollback незавершённой transaction, закрывает stream; background continuation пользовательской записи после отмены запрещена. Перед commit проверять deadline; cancellation во время commit может дать неизвестный клиенту исход. Не обещать rollback уже committed изменения при потере соединения: повторное чтение состояния/version и idempotency где предусмотрено, без автоматического replay commands. DB command timeout и rollback/recovery должны обеспечивать предел; одного кооперативного CancellationToken недостаточно как доказательства. Если bounded completion не доказано, L1 acceptance не проходит.
```

**Предложенный текст:** точная соответствующая редакция N2 и N3.

**Последствия и acceptance:** Precommit check перестаёт выдаваться за bounded commit; поздняя видимость разрешена только для сериализованного набора. AT03/04/06.

### D3 — file cleanup и stream

**Прежний текст (дословно):**

```text
Каждая фотография загружается отдельным запросом во временное private storage. При Submit действующий JWT проверяется заново; owner+organization+course+context, eligibility/expiry и claim сверяются атомарно с записью решения. DB/files не общая transaction: failed/uncertain commit оставляет файл временным/quarantined до reconciliation. Неиспользованные temporary files очищаются по конечному сроку; численное production retention ещё C06/C08/PD-G07, synthetic fixture TTL не юридический срок.

Каждый download/Range request заново авторизуется по JWT и parent scope, передача ограничена серверным deadline ≤8 минут; медленный клиент отключается. После deadline закрыть поток/освободить handles; продолжение требует нового JWT-запроса. Не держать PG connection/lock на время передачи. Private storage/no-store, без public URLs/CDN обхода; уже доставленные bytes не отзываются. Edge buffering не должен продолжать выдачу после предельного времени.
```

**Предложенный текст:** точная соответствующая редакция N3 и N5.

**Последствия и acceptance:** Private claim/auth неизменны; TTL не physical-delete bound, stream application cutoff не последний TX. AT02/07/08/09.

### D4 — formal completion/revoke proof

**Прежний текст (дословно):**

```text
T04 параметры не меняются. Пусть D — AccessValidUntil в общей временной шкале, e — exp, a — admission, L — полный interval до прекращения серверной операции и последней передачи bytes edge (не только handler return). Для токена, выпущенного с известным D: e≤D−600s. При идеальных часах a<e и L≤480s дают completion<D−120s. Для обычного endpoint L≤60s. Очередь/body/materialization/commit/stream включены; timeout не обновляется при активности клиента.

Пусть Δ — суммарная верхняя граница расхождения issuer/API/DB/edge clocks и rounding относительно D, λ — допускаемое validator leeway. Тогда conservative bound `completion < D−600s + Δ + λ + L`. Для D safety нужно `Δ+λ+L≤600s`; при L≤480s остаётся максимум 120s на Δ+λ. Этот запас **не разрешает** JWT после фактических 15m и операции дольше 8m. Принятая инженерная обязанность: expiry validation консервативно сокращает admission на uncertainty, не добавляет default positive leeway; иначе утверждение E03 о максимум 15m неверно. При неограниченной/неизвестной clock error гарантии нет → fail closed issuance/admission либо STOP acceptance, не «примерно два часа».

Cancellation latency, transaction commit и proxy buffering входят в L, а не в дополнительный хвост после 480s. Внутренний execution cutoff должен быть раньше 480s на доказанный completion/cancel budget; использовать monotonic elapsed time, coherent UTC bounds для exp/D. Before-commit check без ограничения времени commit не доказывает completion≤D. DB statement/transaction/lock timeouts, rollback и edge termination проверяются вместе; RequestAborted не гарантирует, что уже запущенный commit отменён. Lost connection может оставить committed outcome: восстановить через read/version/idempotency, не повторять Domain command автоматически. Если storage/commit/edge не дают проверяемого bounded behavior, нельзя принять соответствующий path в L1.

У issuance/revoke общий linearization contract: атомарное принятие revoke устанавливает D без продления при retries; issuer snapshot согласован с этим commit. Token до заявки при conservative true expiry ≤issuance+900s и L≤480s даёт completion≤request+1380s (23m), поэтому 2h bound соблюдён даже если token ещё не содержал новый D. Token после заявки учитывает D−600s. Операция непосредственно перед D не допускается этим новым JWT, поскольку exp минимум на 10m раньше; request непосредственно перед exp допускается, но завершается в bound. Уже expired JWT нельзя «спасти» восьмиминутным лимитом.

E03 SecurityBlocked commit запрещает новые login/issue/refresh и отзывает refresh-sessions; уже linearized access JWT сохраняются. Идеальный максимум новых admission — 15m после block, completion — до 23m после block; измеримая wall-clock гарантия требует conservative expiry и L bounds выше. Никакого обещания «всё прекращается через 15m» либо immediate revoke. Никакой обязательной central denylist/Redis. Audit/block atomicity не отменяет ранее committed learning history. Current ProcessingRestrictions применяются отдельно по ADR-L18, не по двухчасовой задержке.

Engineering acceptance G02: near-exp/near-D requests, ±clock offsets/rounding, delayed queues/body/commit, canceled connections, multiple API nodes, stalled downstream и edge buffering. Проверить upper bound, а не только факт посланной cancellation. Документ описывает условия доказательства; runtime evidence NOT RUN.
```

**Предложенный текст:** точная соответствующая редакция N1, N2, N4 и N6 целиком заменяют этот формальный completion proof в NR1 scope; JWT/issuer/revoke serialization из N4 сохранены.

**Последствия и acceptance:** Формула Δ+λ+L≤600 больше не доказывает physical completion/visibility; 600 reserve сохранён для admission/decision envelope. Clock всё ещё blocker. AT01/03/05/10.

### D5 — E03 completion

**Прежний текст (дословно):**

```text
JWT, выпущенный до block, остаётся до exp≤15m; операция перед exp может завершаться ещё ≤8m. При доказанных conservative clock/operation bounds ADR-L15 идеальный worst case до 23m после block, **не мгновенное прекращение и не двухчасовой ordinary revoke**. Без runtime evidence не заявлять безусловный wall-clock SLA. No mandatory denylist/Redis; block не отменяет committed learning history/скачанные bytes.
```

**Предложенный текст:** точная соответствующая редакция N4; первый абзац SecurityBlocked E03 в ADR-L17 не изменяется.

**Последствия и acceptance:** Убирается full completion≤23m, но block/session/audit/true expiry не ослаблены. AT05/10.

### D6 — I6 executor stop

**Прежний текст (дословно):**

```text
6. **I6 — deadline:** no new stage, retry или commit, если не помещается доказанный worst-case остаток; все исполнители и egress прекращаются до общего предела.
```

**Предложенный текст:** точная соответствующая редакция N1–N3 и N6; I1–I5/I7 сохраняются как safety obligations с отдельным physical accounting.

**Последствия и acceptance:** No-new-actions gate и recovery заменяют all-executors-stop. Storage quiescence остаётся prerequisite seal/purge, а не всего HTTP. AT01/07/08/09.

### D7 — E06 long stream temporal dependency

**Прежний текст (дословно):**

```text
Files проходят тот же gate. Long stream после admission не обещает мгновенного прерывания при newly committed restriction; bounded duration ADR-L15 остаётся, это synthetic-only предел. Jobs fresh context/restriction check перед irreversible step и delivery; initial JWT не perpetual permission. **До real pilot отдельное решение freshness/propagation/in-flight semantics и legal evidence**; обычные два часа нельзя перенести на юридические запреты.
```

**Предложенный текст:** точная соответствующая редакция N5; все остальные L18 current restrictions/serialization clauses сохранены.

**Последствия и acceptance:** Application bound вместо physical stream bound; отдельный явный data-protection tradeoff, только synthetic. AT02/04/09.

### D8 — производные формулировки и отсутствие скрытого supersession

В ADR-L15 §Решение прежнее предложение «JWT, выпущенный до заявки, истечёт максимум через 15 минут; максимальная 8-минутная операция завершится максимум через 23 минуты после заявки, что укладывается в утверждённые 2 часа.» в NR1 scope заменяется точным текстом: «JWT, выпущенный до заявки, истечёт максимум через 15 минут; 60/480s ограничивают новые application actions по NR1 N1, но поздняя видимость ранее допустимого COMMIT и queued bytes не имеют этого wall-clock bound. Утверждение о полном завершении через 23 минуты не применяется.» Последствие: те же AT01/03/05/10, изменение ordinary revoke completion явно принимается, а не скрывается за неизменными числами.

В реестре прежний G02 clause «Если ≤15m/≤8m не доказуемы — STOP S4/S7/S8, не ослаблять T04/E03.» заменяется **только для отдельно принятых NR1 paths** на: «Если conservative true JWT≤15m, application deadline enforcement в принятом envelope, domain safety или recovery/capacity не доказаны — STOP соответствующего path и общей acceptance; physical completion внешнего I/O≤8m не является общим prerequisite NR1. Исключённые paths сохраняют прежний gate.» Acceptance AT01–AT12; clock не исключён.

A02 full-stop/fence-quiescence/late-commit FAIL формулировки сохраняются как история прежнего контракта. NR1 не объявляет их PASS: вводит successor только в разрешённом scope. A06 ND-01–ND-10 не применяются, включая E03-C/N; нормативный текст NR1 самодостаточен. Ordinary jobs paragraph L15 не ослабляется: general jobs исключены. B08/publication, новые полномочия и внешние irreversible effects не входят в late-COMMIT исключение. Если путь нельзя однозначно отнести к NR1 inventory, он blocked, а не автоматически exempt.

## 5. Реализуемый protocol и остающиеся blockers

Предлагаемый минимальный вариант — один authoritative PG primary, один approved API/edge profile, private versioned local storage, закрытые synthetic операторы; multi-primary/failover без fencing исключены. Это candidate topology, не повторная квалификация P1. Новые сервисы или universal storage gateway не обязательны, но isolation/seal должны быть доказаны.

Actor → Submit/Review/upload или scoped status/read → S3 ownership, current E06, immutable history/one result → OperationIdentity, attempt/version, private object receipt → Registered/Staging/Sealed/Committed либо доказанный Aborted; OutcomeUnknown/PhysicalPending отдельно → JWT plus server-derived parent/context, targeted PG guards → AT01–AT12. Это design contract, не изменение схемы.

Для будущего write adapter transaction owner — Application use case, Infrastructure использует одну connection/transaction. Candidate isolation READ COMMITTED с mandatory locks и fresh reads после ожиданий; Serializable не заменяет coverage. Сохраняется существующий порядок C2 там, где применим: org lock перед ProcessingGuard; затем operation identity и resource/claim locks в детерминированном порядке. Identity/session paths сохраняют принятые S2/S3 locks, не принуждаются к несовместимому общему порядку. Точный полный lock graph всех writers должен быть утверждён до реализации каждого adapter, иначе blocker. CAS/version и audit входят в transaction, result commit атомарен; отсутствие ACK → unknown. Никаких автоматических retries command после ambiguous commit. Явно доказанная serialization abort может допускать ограниченный retry только внутри исходного d по отдельному контракту; default — explicit new request после установленного исхода.

| Blocker | Что изменяет NR1 | Что необходимо до acceptance |
|---|---|---|
| PG physical bound | Не нужен универсальный COMMIT≤B для разрешённых mutations | Atomic result/dedup/audit, полный writer lock graph, no stale processing checks, crash/unknown resolution; late grant/publication отдельно blocked |
| Controlled egress hard bound | Заменён явно более слабым application contract | Inventory API/proxy/buffering, bounded bytes/queues, absolute budget propagation, no sends после обнаруженного expiry, поздний transport tail честно отражён в UX/security review |
| Clock | Не снят | Trusted UTC envelope, age/drift/step/suspend/reboot policy, conservative issue/validate/q; без доказательства STOP. Оценка clock closure отдельно неизвестна |
| Storage seal/retained handles | Physical pending допустим только private | Exclusive trusted sealing/copy path с подтверждённым окончанием writers и immutable exact version; receipt/durability, no bypass и race tests. Если обычная FS не обеспечивает выбранный протокол, сменить дизайн или STOP |
| Capacity/recovery | Неопределённость допустима, потеря учёта нет | Численные global/per-org caps, durable reservations до side effects, restart reconciliation, no replay horizon gaps, operator runbook и restore evidence |
| H04/Ready | Ничего автоматически не закрывает | ImportAttempt, отдельный закрытый writer, whole-course capability и processing writer compatibility; C2-J не выдаёт право на private composition helpers |
| Security cutover | Gate остаётся | B02/B11 route inventory, отрицательная matrix, SR-01–SR-04/human audit и отдельный scope/owner acceptance |

## 6. Поведение при отказах и неопределённый результат для пользователя

| Отказ | Сохраняемая гарантия в доверенной failure model | Что не гарантируется / что видит пользователь |
|---|---|---|
| PostgreSQL недоступен до проверки | Нет issue/fresh processing/read/mutation без требуемой DB; local signature не разрешает bypass | Service unavailable; не «сохранено» |
| PG завис после q/во время COMMIT | Один atomic effect либо abort, serialization с restrictions, no replay, protected status | Возможен поздний commit; нет срока разрешения. «Результат операции пока неизвестен. Не отправляйте её повторно; проверьте статус.» |
| Потерян ACK, PG позже восстановлен | Authoritative resolution, immutable прежний результат; новые reads заново авторизованы | Status «Выполнено» только после доказательства, либо «Не выполнено» только при доказанном abort. Нет гарантии восстановления cookie/секрета |
| Storage hang/crash | Private namespace/quotas и запрет unsafe seal/claim/purge | Загрузка не подтверждена, файл временный; очистка pending, не «удалён физически». Уже claimed originals сохраняются |
| OS/scheduler pause/crash | Durable identity, transaction atomicity, isolation при сохранности kernel/DB assumptions; no automatic authority resume | Нет hard deadline/availability; browser может показать network timeout. После resume проверки обязательны, pending I/O/bytes могли завершиться |
| Clock unknown/step/suspend | Deny новых issue/admission/q после обнаружения uncertainty, quarantine trust после restart | При ложном невыявленном clock trust true-expiry не доказан: это blocker/incident, а не поддержанная гарантия. Previously issued COMMIT не отменяется clock alarm |
| HTTP egress hang/disconnect | Нет новой application выдачи после обнаруженного cutoff; каждый новый request авторизуется | Уже queued bytes могут дойти позже; отсутствие ответа не доказывает abort. У закрытого соединения нельзя гарантировать JSON/HTTP error |
| Recovery/cleanup unavailable | Unknown originals/slots сохраняются; reject при caps | Длительная недоступность возможна; нет бесконечных фоновых replacement/retry |
| Correlated power/kernel/DB fault | Только если durable history и isolation сохраняются; restart default deny | При потере acknowledged history или trusted isolation гарантия вне модели; restore/incident, не автоматический resume |

OperationId — непрозрачный selector, не bearer permission. Status выдаётся только владельцу/допустимому scope/current context; чужой ID не раскрывает существование, payload или организацию. Пока unknown, UI блокирует автоматическую повторную отправку этого intent и сохраняет локальный command identity; backend dedup не зависит от UI. Новый key не обход: resource expectedVersion/claim uniqueness и conflict с pending intent обязательны. Browser не обещает «отменено» после timeout. При auth expiry — повторный вход и отдельный status-read; при потере разрешения status/result deny, даже если операция была успешна. Read-only refresh статуса с backoff допустим, replay команды — нет. Если identity не получен клиентом, lookup по сохранённому scoped idempotency key, без выдачи secrets. Новый upload не должен автоматически дублировать неизвестный Submit.

## 7. Влияние на MVP

| Slice | Изменение плана / сохраняемый gate |
|---|---|
| B02 session/browser | Повторно использовать S2/S3/A1/A2, O01 30s inclusive/409/no Set-Cookie, memory-only access, Secure HttpOnly refresh, CSRF/Origin. Lost refresh COMMIT не replay и не successor redisclosure. После ACK перед выдачей проверять исходный d и exp; нельзя «освежить» exp позднего JWT или доставить secret recovery. Identity/security transactions требуют отдельного timing/serialization review; generic learning exception не разрешает новые credentials после block. Clock остаётся blocker. Общий HTTP требует отдельной authorization |
| B11 scoped authorization | Обойти все read/write/list/count/file aliases, фильтровать до pagination/materialization, current E06 checks. Для каждого разрешённого write доказать q и serialization со всеми restriction/context writers; no global revoke barrier. Exact PRS1 OFF и conservative deny сохранены |
| B07b | Worker может остаться private pending при caps; late receipt не Ready. После expiry нельзя автоматом завершить Ready новым transaction. ImportAttempt/H04 writer и его admission/whole-course/context/receipt checks остаются отдельным gate. Если уже принятое Ready decision late-commits, оно допустимо только после отдельного включения writer в NR1 inventory и не публикует курс |
| Фото решений / B12/B15 | Каждое фото отдельный private upload с stable identity; подтверждённый seal до eligibility, Submit атомарно claims конкретные originals и фиксирует history. Unknown сохраняет объекты, order и command identity; no compensation delete. Download/Range/preview защищены parent/current processing. UI объясняет unknown, retry не дублирует Submission; физические device tests отдельно |
| B08 и PRS1 | Не разрешены. NR1 не создаёт publisher capability или exact provenance. Это самостоятельные owner decisions и evidence |

### Оценка после нормативного решения

Инженерные человеко-дни для одного знакомого с repository инженера, последовательная работа; не календарное обещание. Новый код в этом review не писался.

| Работа | Условная оценка |
|---|---:|
| Scoped нормативная синхронизация, route inventory, lock/authority review | 2–3 |
| Application deadline gates/transport integration и тесты | 3–5 |
| Durable identity, transaction adapters, no-replay resolution | 4–7 |
| Private storage seal/capacity/restart/cleanup minimum | 5–8 |
| Fault/security acceptance и runbook | 3–5 |
| Итого общий NR1 foundation без clock closure и downstream UI | **17–28** |

Clock qualification не имеет достоверного срока; при сохранении NO-GO общий срок G02 не определён. Первые 2–3 дня дают stop/go по route/clock/storage feasibility, не новый бесконечный provider поиск. B02 и B11 сохраняют backlog ориентиры 6–10 дней каждый; B12 5–9 и B15 3–5, но shared deadline/recovery tests уже включены в foundation и не суммируются механически. H04/ImportAttempt оценить отдельно после writer/immutable storage contract. Оценки не разрешают реализацию и не устанавливают дату MVP.

## 8. Критерии приёмки — будущие тесты, NOT RUN

Каждый тест имеет независимый oracle: domain rows/result/audit, auth denial, exact bytes/refs, resource accounting и application action trace. Trace q не выдать за timestamp COMMIT. Fault tests подтверждают safety для испытанных faults, не universal hard bound. Для каждого path positive own-resource control и negative checks обязательны.

| ID | Сценарий и ожидаемый результат |
|---|---|
| AT01 deadline | Earliest ingress queue/header/slow body, 60/480 boundaries, retry/transit/restart без reset. Fake clock и process pause между check/dispatch; после обнаруженного expiry нет нового stage/response. Observed transport tail отражается отдельно, не засчитывается как full-stop PASS |
| AT02 egress | Slow reader, proxy buffering, disconnect, Range и pause API/proxy: each new chunk gated, no new response по позднему ACK; bounded queued bytes. Уже queued late bytes документированы как NR1 tradeoff. New Range after exp denied; no public bypass |
| AT03 late COMMIT | Pause/ACK loss до/после q и dispatch, d/D истекают. До q expiry → no mutation; после допустимого q поздний committed набор ровно один, без дополнительного grant/publication. Domain+result+audit atomic; conflicting expectedVersion rejects. No success до ACK/после d |
| AT04 processing race | Restriction insert/update/withdraw/context/claim во всех порядках, включая phantom. Prohibition first → deny; q first → prohibition commit сериализуется после writer, новый read затем deny. Ни stale snapshot, ни historical allow не проходят. DB unavailable → deny |
| AT05 authority/time | exp/900/D−600/7200, earliest dependencies, clock ±offset/rounding/unknown/step/suspend/reboot; no positive leeway. Revoke retries не сдвигают D; group move/generation/history и org A/B independence. Unknown clock не объявлять qualification PASS |
| AT06 unknown/dedup | Lost ACK/registration, concurrent same key/payload и different payload, browser reconnect/new key, delayed resolver, primary restart. No replay; key conflict, one effect. Replica not-found не Aborted; unresolved writer удерживает unknown/bytes/reservation |
| AT07 storage | Retained FD/late write после receipt, forged hash/version/parent, crash during seal. Недоказанная immutability → no eligibility/Ready; old namespace не трогает new/claimed originals. Writer не имеет PG/publication/public-egress capabilities |
| AT08 recovery/capacity | Fill caps, hung worker/PG/cleanup, restart; нет освобождения слота по timeout, нет unbounded replacement. Exact purge racing claim сохраняет originals; tombstone не physical deletion. Resolution после authoritative completion, old primary fenced либо failover denied |
| AT09 access matrix | Own allowed; wrong org/course/group/assignment/role; revoked/moved по принятой семантике; forged/missing/XOR parent; pending file; missing context/subjects/sources; inaccessible attachment. Lists/counts/cursors не раскрывают forbidden rows; status не enumeration |
| AT10 S2/E03/O01 | Block vs issue/refresh/consume serialization, no credentials after committed block, late ACK no old-response cookie/JWT; exp не пересчитан. O01 ≤30s 409 без credentials; >30s session revoke only. Unblock не resurrect; no secret redisclosure; CSRF/Origin/two tabs/cache switch |
| AT11 journey | Synthetic upload two photos → Submit → Review → Revise → Accept, unknown at каждом write и auth expiry mid-edit, preserved originals/history; ImportAttempt Ready отдельно H04. No B08 enablement по тесту |
| AT12 inventory/release | Независимый review всех adapters/aliases/DB roles/storage handles; excluded paths не подключены. Accepted profile, численные caps, clock evidence, restore drill и owner path acceptance; runtime tests не заменяют нормативное approval |

Порядок gates: owner принимает/отклоняет NR1 → отдельная документационная синхронизация → отдельное разрешение ограниченного implementation/test slice → clock/profile + security/recovery evidence → owner G02 acceptance → отдельный S4 HTTP cutover. При отказе принять temporal tradeoffs действует T04/E03 и NO-GO P1; backdoor через configuration/timeout нет.

## 9. Точный текст будущего решения владельца — ПРОЕКТ, НЕ ПРИНЯТО

> **OWNER DECISION — MVP-B01/G02-NR1.** Принимаю только для контролируемого SyntheticOnly MVP нормативный addendum N1–N7 из `codex/MVP-B01-G02-NORMATIVE-REVIEW.md` от 2026-10-11 и ограниченный supersession D1–D8. Явно согласую замену hard full-operation/last-controlled-egress 60/480s на application deadline в принятом execution envelope, возможность поздней видимости ранее допустимого сериализованного learning COMMIT и выхода уже queued bytes после d/D/23m. Это ослабление temporal completion guarantee; прежнее доказательство окончания всех effects до двух часов не заявляется. До истечения cutoff нельзя принимать недопустимое решение, после него нельзя заново авторизовать/продолжать команду; исключение не распространяется на новые grants, B08 publication, general jobs и внешние irreversible effects. Current ProcessingRestrictions и защита файлов сохраняются с явно принятой N5 temporal семантикой stream. True JWT≤900s, conservative expiry, reserve600s, D≤request+7200s, E01/E02/O01/E04/E05, org/course/group authority и synthetic-only сохраняются; unknown clock остаётся blocker. Unknown COMMIT не rollback; automatic replay запрещён. PhysicalPending требует isolation, durable finite accounting и безопасного resolution, а не фиктивного success/quiescence. Принимаю отсутствие универсального recovery RTO при произвольном infrastructure stall. A06 E03-C/N не принимаю, P1-SINGLE-LOCAL остаётся NO-GO и не квалифицирован этой редакцией. Разрешаю следующим отдельным заданием только документационную синхронизацию ссылок/effective notice с сохранением истории; runtime/schema/migrations/production configuration/HTTP cutover не разрешаю. H04/Ready BLOCKED, B08 NOT AUTHORIZED, PRS1 EXACT OFF; G02 остаётся BLOCKED до evidence и отдельной owner acceptance. Остановиться после документационной синхронизации; реализация требует нового явного задания.

Этот текст — объект owner review, не запись принятого решения. Альтернатива владельца: отклонить NR1, сохранив весь T04/E03 и P1 NO-GO. Частичное принятие, исключающее late-COMMIT или transport-tail clauses, не закрывает соответствующие B-PG/B-EGRESS blockers и требует уточнённого контракта до реализации.

## 10. Проверка документации и guardrails §7

| Проверка | Статус/основание |
|---|---|
| Actor/use case/invariants/states | covered — §§3/5/7; проект, не implementation |
| S2/S3/PRS1, gates | covered — N4/N7; ничего не включено |
| Server org/course/group/ownership | covered как неизменный contract и AT09; runtime NOT RUN |
| Affected subjects/sources | covered — missing evidence deny, AT04/09; no exact bypass |
| Lists/counts | covered — pre-materialization и AT09; новых queries нет |
| Attachments/history | covered — N3/N5 и AT07/09/11 |
| Transaction/locks/CAS/unknown | covered в design; полный adapter lock graph blocked до реализации, AT03/04/06 |
| Validation/limits/errors | covered — N1/N3, §6; численные capacity profile ещё blocked |
| Unit/integration/negative/E2E | NOT RUN — documentation only, будущая matrix §8 |
| Unapproved edits | Только новый review и append реестра; действующие ADR/runtime/config не редактируются |
| Real data/secrets/release claims | covered — synthetic only, никаких secrets/готовности |
| Evidence и limitations | Документальные цитаты/links/preservation проверяются отдельно; proof obligations не объявлены PASS |

Специализированные model-domain/implement-use-case/implement-feature skills отсутствуют по repository reconciliation; их выполнение не заявляется, implementation не проводится. Проверка документов не является security/runtime acceptance. Никаких Git-операций, тестовых стендов, migrations, vendor messaging или реализации.

**P1 ACCEPTED · G02 NORMATIVE REVIEW READY FOR OWNER REVIEW · A06 NOT ACCEPTED · NR1 NOT ACCEPTED · T04/E03 IN FORCE · G02 BLOCKED · H04/READY BLOCKED · B08 NOT AUTHORIZED · PRS1 EXACT OFF · S4 HTTP NOT AUTHORIZED.**

Documentation validation: PASS — register prefix byte-for-byte preserved; 6 baseline ADR/G02/config files SHA-256 unchanged; 7 old-text blocks match sources; local Markdown links resolve; fences/whitespace valid. Runtime/security/fault tests NOT RUN. Validation script: `/tmp/validate-g02-review.py`; no Git commands used.
