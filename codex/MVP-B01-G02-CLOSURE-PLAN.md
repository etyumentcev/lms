# MVP-B01 — G02 closure plan

2026-10-11. **ASSESSMENT READY FOR OWNER REVIEW · G02 BLOCKED.** Documentation/repository inspection only. Ни одно предлагаемое изменение ниже не реализовано и не авторизовано этим документом.

## 1. Вывод и ближайшее действие

G02 нельзя закрыть ограниченным добавлением CancellationToken, timeout middleware или переносом I/O в процесс. В текущем репозитории нет проверяемых верхних границ PostgreSQL COMMIT/rollback, filesystem completion, worker teardown и последней controlled HTTP-передачи. Есть полезные component tests, isolated ACL/transaction evidence и контрпримеры недостаточных механизмов. **732/732 C2-J PASS подтверждают принятый isolated scope, а не G02.**

Минимальный следующий шаг — отдельное owner решение о действующем completion contract, затем короткая квалификация **одного** deployment profile по PG/egress/clock, до реализации общего gateway или ImportAttempt Ready. Если численные контракты не получены, остановить эту ветвь с перечнем недостающих гарантий; продолжать отдельно разрешённые synthetic product components. Не повторять исследование множества поставщиков и не строить инфраструктуру в надежде, что тесты заменят контракт.

MVP-B01 здесь — safe synthetic HTTP gate, не принятое S3 B01 Generation-aware Enrollment Move. Последнее сохраняется без изменений. Успех B01 assessment не равен B01/G02 acceptance, B02/B11 completion или разрешению HTTP.

## 2. Основания и фактический baseline

Нормативные источники: [реестр T04/E03/E06/O01 и C2-J](L03-OWNER-DECISION-REGISTER.md), [ADR-L15](../docs/adr/ADR-L15-jwt-delayed-revocation.md), [ADR-L16](../docs/adr/ADR-L16-organization-jwt-browser-session.md), [ADR-L17](../docs/adr/ADR-L17-account-security-recovery-challenges.md), [ADR-L18](../docs/adr/ADR-L18-synthetic-processing-restrictions.md). Применены [guardrails](SAPIENTUM-MVP-ENGINEERING-GUARDRAILS.md) с [reconciliation](MVP-GUARDRAILS-RECONCILIATION.md).

Evidence: [A03 readiness](L1-G02-IMPLEMENTATION-READINESS.md), [PG](L1-G02-POSTGRES-EVIDENCE.md), [storage](L1-G02-STORAGE-EVIDENCE.md), [edge](L1-G02-EDGE-EVIDENCE.md), [timing proof](L1-G02-TIMING-PROOF.md), [A04 ledger](L1-G02-A04-TIMING-LEDGER.md), [A04 contracts](L1-G02-A04-CONTRACT-MATRIX.md). Исторические V1 FAIL/NOT ESTABLISHED, V2/V3 NOT SATISFIED не исправлены на PASS.

[A06 owner package](L1-G02-A06-OWNER-DECISION.md), [normative diff](L1-G02-A06-NORMATIVE-DIFF.md), [proof obligations](L1-G02-A06-PROOF-OBLIGATIONS.md), [failure model](L1-G02-A06-FAILURE-MODEL.md) и [S4 split](L1-G02-A06-S4-SPLIT.md) — предложения. Принято направление B/C и документационная подготовка; **E03-C, решение N, новая failure model и реализация A/B не приняты**. P1–P9/R0–R4 используются ниже для трассировки предлагаемого пути, не вместо действующих T04/E03/I1–I7/V0–V3.

[C2-J report](MVP-B07b-C2-J-RESULTS.md) и [summary](evidence/MVP-B07b-C2-J/test-summary.json): 43+272+198+219=732 PASS; 33 isolated LOGIN tests. H03 closed только в isolated scope; production wiring, operational activation/recovery и ImportAttempt writer не реализованы этой приёмкой. H04 helpers denial — ожидаемый negative PASS, не Ready success. Исторический report не переписан.

Рабочая копия `/home/tea/github/lms`, branch `codex/l1-s4-jwt-api-authorization`, HEAD `492e30aef65bbddd24f107903bfaa54a563abe39`. Dirty checkout сохранён по прямому owner запрету смены branch/HEAD/index; переход на default main не выполнялся. Каталожные skills model-domain/implement-use-case/implement-feature недоступны; их исполнение не заявляется. Это assessment, новых domain/API contracts не введено.

## 3. Неизменные требования и таблица evidence

Общий отсчёт `t0` — earliest controlled ingress, включая queue/body; `B=60s` ordinary и максимум `480s` только явно designated upload/download. Submit/Review/Ready не получают 480s из-за работы с файлами. В текущем контракте `L=max(last storage activity, PG operation end, worker/handle cleanup, last controlled egress)-t0`; success/error/cancel/unknown подчиняются одному пределу. Внутренний cutoff оставляет stop/cleanup budget **внутри** B.

JWT true lifetime ≤900s; `exp≤AccessValidUntil−600s`; conservative admission без positive leeway. `Δ+λ+L≤600s`, но ни L≤480s, ни true lifetime≤900s этим не ослабляются. Остаток 120s не добавляется к rollback/cleanup/egress. Ordinary revoke ≤7200s; E03 900+480=1380s — условное следствие доказанных bounds, не самостоятельное runtime evidence.

| ID / точное требование | Компонент и уже имеющееся evidence | Конкретный недоказанный сценарий | Ограниченное исправление / приёмка |
|---|---|---|---|
| G-CLOCK: квалифицированные UTC intervals и monotonic elapsed, fail closed при unknown; единый epoch | `QualifiedAuthorityClock` проверяет интервалы, regression/jump/epoch, latch-closed; `QualifiedClockTests` используют synthetic source. Component issuer/JWT консервативны. Production `IQualifiedClockSource` не предоставлен; Program использует TimeProvider.System | Реальный drift/suspend/scheduler stall/истечение qualification между issue, queue, admission и commit; несколько узлов и edge | Реализовать qualified source и общий deadline adapter можно узко после выбора profile; истинность входных bounds требует отдельного контракта. AT01/02/09 |
| G-JWT: L15/L16 issue/refresh/revoke linearization, earliest deadline, org/generations; E03 не выдаёт новые credentials после block | ComponentJwt/ComponentExpiry/PostgresComponentTokenIssuer; S2/S3 и C2-J restricted tests, lost ACK не выпускает JWT/secret. Program пока использует existing JwtBearer/ClaimsActorMapper, component boundary не подключён | Реальный HTTP host с conservative expiry, задержанный signed result после COMMIT, near-exp request, block/revoke/refresh races на нескольких узлах; browser response ordering | Отдельный B02 adapter на существующие компоненты, explicit validator configuration; не новая auth система. PG/clock/egress остаются shared blockers. AT01/03/10 |
| G-PG: commit, rollback, отмена и release укладываются в B; никаких late domain effects | PostgresProcessingWriteBoundary: Serializable, private helpers, mutate, CommitAsync(ct). C2-J: ACL, atomic audit, lock/race/40001/40P01 и реальная потеря COMMIT ACK. A03: backend SIGSTOP/termination counterexample | COMMIT допущен до d, WAL/storage/backend зависает и возобновляется после d; late visible mutation; rollback или disposal не завершается в остатке. Precheck не доказывает конец COMMIT | Timeout/remaining-budget plumbing — узкое улучшение, **не closure**. Нужен completion/fence contract или отдельно рассмотренная visibility architecture. AT03/04/09 |
| G-HTTP-IN: absolute deadline до queue/header/body, traffic не обновляет таймер, capacity reject до effects | Upload controller ограничивает multipart bytes; nginx `proxy_request_buffering off`; общего ingress deadline в Program/nginx нет | Медленные headers/body, trickle, queue перед middleware, abandoned request с продолжающейся работой, saturation | Trusted t0/deadline propagation, finite queue/body/concurrency limits и классификация routes; клиент не задаёт authority/deadline. AT02/09 |
| G-HTTP-OUT: response/error/stream и очереди заканчиваются ≤B на последнем controlled hop | Controller возвращает File и private/no-store; A03 демонстрирует недостаточность inactivity timeout/close как completion oracle | Slow reader, buffering, queued retransmit, зависший API/edge/kernel; handler завершён, egress ещё действует | Absolute independent cutoff + контракт всех controlled queues и last-TX oracle. nginx timeout/buffering off в одиночку недостаточны. AT05/09 |
| G-FS: open/read/write/flush/rename/delete/handle release входят в исходный B; exact version private, immutable и durable перед eligibility | LocalFileAttachmentStorage: async copy/flush, но sync open/move/delete/finally. FIFO test доказывает pending open после cancel; A03 retained-FD write опровергает rename/chmod seal. C1 private staging не durable import storage | Disk stall/D-state, parent I/O, outstanding device I/O после worker exit, retained FD меняет sealed bytes; delete/claim race | Изолировать I/O и убрать unsafe inline compensation можно ограниченно; universal physical stop и seal этим не доказаны. Backend profile/fence/quiescence либо отдельное owner изменение. AT06/07/08 |
| G-WORKER: child/descendants/control plane/parent/cleanup вместе ≤B, без бесконечных replacements | C1 systemd cgroup + bwrap, memory/tasks/CPU caps, observed PID identities/cgroup-empty до возврата. run_worker имеет wall timer, control timeout=3s, wait=5s, finally kill/stop/reset; importer snapshot имеет отдельный start | Systemctl/parent FS/scheduler зависает; snapshot→worker→verify→rmtree суммарно превышают budget; sampling/empty cgroup не доказывает отсутствие device work; reboot теряет orphan accounting | Один исходный deadline для всех фаз, reserved teardown budget, bounded slots/bytes и restart inventory. Это дополняет C1, не отменяет его accepted limits. AT06/09 |
| G-AFTER: после d не продолжать expired command, не допубликовывать, не replay; unknown не rollback | Issuer не возвращает token при failed commit boundary; C2-J lost ACK показывает committed identity/session с exception. AttachmentService.Upload в catch выполняет query/delete с CancellationToken.None | Поздний compensation I/O; SELECT absence пока original COMMIT ещё pending приводит к удалению potentially committed original. Delete держит transaction scope до окончания storage.Delete (commit уже выполнен) | Убрать unbounded compensation; durable operation identity/quarantine и serialized authoritative outcome protocol; закрыть DB scope перед stream/FS. При текущем T04 нельзя просто вынести незавершённый I/O после d в background. AT04/08 |
| G-PROCESSING: E06 current checks до reads/counts, в owning write transaction, fail closed; scope/parent server-derived | S3/A1/A2 компоненты; C2-F–J guard/ACL foundation, закрытый PostgresProcessingWriteBoundary. C2-J whole-course predicate только тестовый; Ready capability отсутствует | Ready-first/restriction-first под реальным narrow executor, fresh whole-course authority после lock, deadline во время ожидания, read/count/projection bypass | H04 закрыть только ImportAttempt writer: immutable binding, S2/S3, guard revision, CAS/state/journal/audit в одной transaction. Не выдавать EXECUTE private helpers runtime. AT07/10/11 |
| G-RESTART: старые epochs/writers не оживают, acknowledged history не теряется; capacity учитывает pending | A03 clean single-PG restart; C1 cgroup observations; durable import operation/recovery/accounting ещё отсутствуют | Controller crash, reboot, lost outcome, old primary после promotion, orphan занимает ресурс, retry получает новый бюджет | Узкий single-primary/no-promotion profile уменьшает scope, но не доказывает stop/durability. HA если включён требует fence/history proof; иначе явно запрещён в profile. AT04/08/09 |

Source paths проверены чтением: [clock](../src/Lms.Application/QualifiedAuthorityClock.cs), [issuer](../src/Lms.Infrastructure/PostgresComponentTokenIssuer.cs), [processing boundary](../src/Lms.Infrastructure/PostgresProcessingWriteBoundary.cs), [attachments](../src/Lms.Infrastructure/AttachmentService.cs), [local storage](../src/Lms.Infrastructure/LocalFileAttachmentStorage.cs), [controller](../src/Lms.Api/AttachmentsController.cs), [Program](../src/Lms.Api/Program.cs), [nginx](../docker/nginx.conf), [importer](../tools/content_import/importer.py), [sandbox](../tools/content_import/sandbox.py), [clock tests](../tests/Lms.Infrastructure.Tests/ComponentHardeningTests.cs), [FIFO tests](../tests/Lms.Infrastructure.Tests/AttachmentDeadlineBoundaryTests.cs). Существование теста не означает новый запуск в этом assessment.

## 4. Точные блокеры и разделение требований

**Обязательно уже для synthetic MVP:** все строки §3 для включённых пользовательских путей, T04/E03 clocks/full completion, L16/O01, SyntheticOnly и current restrictions, no-public-bypass, immutable history, unknown-outcome safety, ограничение outstanding capacity. Synthetic data снижает риск ПД, но не освобождает от принятых timing/authorization требований. Отключённый file path может позволить отдельно принять component slice; это не полный synthetic MVP с обязательными фотографиями.

Технические блокеры, напрямую соответствующие A06:

- **B-PG:** отсутствует верхняя граница visibility-changing COMMIT/rollback/fence; влияет и на non-file auth/learning paths. Второй publication COMMIT, timestamp перед COMMIT и quarantine после видимости проблему не решают.
- **B-EGRESS:** отсутствуют independent cutoff, queue/drain bounds и независимый last-controlled-send oracle. Client EOF не замена.
- **B-CLOCK:** отсутствуют qualified host source, численные UTC/drift/dispatch guarantees и связанный deployment ledger.
- **B-ISOLATION/B-FENCE:** нет доказанной immutable/durable exact-version границы, mutation-point fencing и physical quiescence; private namespace не physical stop.
- **B-RECOVERY/B-CAPACITY:** нет durable ImportAttempt identity/outcome/retention horizon/orphan accounting; timeout не освобождает slot автоматически. Unknown не даёт purge/replay.
- **B-E03:** normative конфликт private PhysicalPending после deadline с действующим full completion. Решается только владельцем, не тестом.
- **H04:** import writer отсутствует; G02 closure не создаёт Ready capability автоматически. H03 больше не blocker isolated provisioning, но cutover principals/adapters требуют отдельной проверки.

**До реальных пользователей/ПД дополнительно:** отдельный pilot/release approval; ADR-L18 freshness/propagation/in-flight semantics реальных restrictions; подтверждённые основания/представители/retention/erasure и применимые PD/ORG gates; независимый human security review/SR-01–04, operational key/secret custody, backup/restore и поддерживаемые устройства/browser evidence. Реальный пользовательский пилот не разрешён ярлыком SyntheticOnly. Это не перенос synthetic timing/security prerequisites на потом; real-data контракты не закрываются этим документом.

**Отдельный owner пересмотр, если контракт не достижим:** full physical completion при произвольном external I/O; допустимая failure model и excluded correlated failures; возможная замена PG visibility boundary; смысл controlled HTTP completion/E03 при изменении границы. Репозиторий показывает отсутствие доказательства для рассмотренных профилей, а не теорему невозможности любой архитектуры. Принятие A06 B/C устраняет только обязанность universal stop доказанно isolated private I/O; B-PG/B-EGRESS/B-CLOCK остаются. Другие ослабления этим планом не предлагаются и не подразумеваются.

## 5. Минимальная последовательность изменений после новых разрешений

| Шаг | Конкретный deliverable / предел работы | Exit / stop |
|---|---|---|
| P0 — решение контракта | Отдельно рассмотреть OD01–03 ниже; при принятии нового текста синхронизировать normative successors с сохранением истории | До решения действуют T04/E03; G02 BLOCKED |
| P1 — один qualification dossier | Один pinned PG/storage/OS/edge/clock profile, inventory всех routes/aliases и privileged bypass, failure domains, numerical ledger Q/S/P/X/R/E/J с источником каждого bound; отдельно ordinary и long/error/cancel | Нет хотя бы PG/egress/clock bound → stop до общей реализации. Не подставлять p99/max-observed. Не запускать новый vendor sweep |
| P2 — узкие component corrections | Общий immutable deadline/qualification epoch; запрет budget reset; before-step remaining check; bounded admission/orphan accounting; убрать CancellationToken.None compensation; закрывать DB scope до FS/stream | Unit/component tests AT01/04/06/08/09; без production DI/HTTP. Recovery protocol согласовать до удаления/записи, не просто заменить token |
| P3 — targeted PG/egress proof | Disposable fault harness одного profile: original transaction/observer/commit oracle; independent egress oracle; полные 60/480 traces | AT02–05/09 и ledger; contract gap→NOT PROVEN, late effect/egress→FAIL. Только после P1 и отдельного experiment authorization |
| P4 — scoped integration | Сначала B02 session/browser, затем B11 learning paths; отдельно H04/ImportAttempt writer и file safety. Reuse S2/S3/C2-J; sealed receipt и durable recovery до Ready | Отдельное authorization каждого slice; AT07/10/11; не объединять с B08 или PRS1 exact |
| P5 — acceptance dossier | Contract evidence + positive/negative/fault traces + relevant regression + independent review; owner отдельно принимает G02 и отдельно разрешает runtime cutover | По действующей норме V1–V3; при явно принятом A06 также R0–R4/F01–F24. Никакого автоматического открытия S4/S7/S8 |

Не вводить generic jobs/blob gateway/новые роли/CMS как prerequisite по умолчанию. Если P1 требует новой visibility/storage architecture, остановиться с конкретным design и owner decision, не расширять P2 скрыто. No-HA profile — только предложенный минимум; будущий HA требует повторной квалификации.

## 6. Acceptance tests — будущие, NOT RUN

Матрица задаёт минимальные направленные проверки для реализации, **не сокращает** действующие obligations или [A05 F01–F24](L1-G02-T04-ACCEPTANCE-MATRIX.md)/A06 criteria при их принятии. Каждый тест сохраняет profile/version, operationId, исходные t0/d, qualification epoch/uncertainty, fault trigger, authority/DB timeline и независимый oracle. Missing oracle/trace = INCONCLUSIVE, contract gap = NOT PROVEN; наблюдение запрещённого эффекта = FAIL. Успешный конечный набор тестов не доказывает абсолютное завершение произвольного external I/O.

| Test | Действие и проверяемый результат |
|---|---|
| AT01 — время/authority | ±UTC uncertainty, rounding, exact exp/D−600, monotonic drift/suspend/jump, qualification loss между issue/commit/response, multi-node. Unknown→deny; true JWT≤900; exp не округляется вверх; положительный контроль с valid clock и действующим scope |
| AT02 — request budget | Queue до app admission, slow headers/multipart body, trickle, disconnect; отдельные ordinary 60s и designated 480s. Один trusted t0, retries/traffic не обновляют d; completion всех обязательных stages ≤B; overload отклоняется до effects |
| AT03 — PostgreSQL deadline | Stall после final precheck, при commit/WAL и rollback; resume после d. Observer на authoritative timeline проверяет domain/audit/result и все visibility paths. Нет late effect; statement timeout/pg_terminate_backend ACK не oracle completion; positive commit before d сохраняется |
| AT04 — неизвестный исход | Wire-drop COMMIT/ROLLBACK ACK, observer сначала видит absence; original writer ещё жив. Нет replay/purge/ложного Aborted; scoped status устанавливает прежний исход без продолжения команды; idempotency same key/digest один effect, different digest conflict; original d неизменен |
| AT05 — response/egress | Slow/non-reading client, buffering on/off, queued retransmits, edge/API stall. Измерить последний controlled TX и stop/drain; ни success body, ни error/secret bytes не выходят после d. Client late receive сам по себе не FAIL и не PASS controlled-TX проверки |
| AT06 — FS/worker | Faults на open/read/write/flush/rename/delete, snapshot/fingerprint/verify/rmtree, systemctl failure, detached descendants, parent crash и teardown. Проверить общий budget, handles/processes и physical oracle; FIFO kill/reap лишь positive control. Неподтверждённый stop остаётся blocked/accounted |
| AT07 — immutable/file scope | Retained writable FD после seal, stale epoch после restart, missing/truncated/wrong-hash version, wrong org/course/owner/parent, pending file, forged receipt. Deny до bytes/claim; no public/static/presigned bypass; DB connection/locks не удерживаются при download; каждый поддержанный Range заново авторизуется |
| AT08 — after-deadline/recovery | Cancel на каждой фазе, late child result, resolver outage, cleanup vs claim/Ready. После d нет новой user mutation/publication/retry budget; absent row не разрешает удаление original. Quarantine не Ready; tombstone не physical-delete ACK. По текущему T04 также доказать full stop ≤B; только отдельное принятое A06 меняет private-I/O oracle |
| AT09 — restart/capacity | Saturation и все slots заняты unknown/orphan; timeout не освобождает ресурс; restart восстанавливает accounting/epochs и отвергает stale worker. No-HA profile запрещает promotion; если HA включён, old-primary fence и непрерывность acknowledged history обязательны. Independent supervisor/clock/egress failures покрыты profile либо явный owner stop |
| AT10 — B02/B11 | O01 race: один successor, ≤30s inclusive→409 без JWT/Set-Cookie, >30s только session revoke; lost cookie→возможен re-login. CSRF/exact Origin/cookie/cache/two-tab ordering. Scope matrix wrong org/course/group/assignment/role, missing parent, revoke/move generations; lists/counts до pagination; E03 block vs issue/refresh и E06 DB unavailable→deny |
| AT11 — H04 | Narrow operational LOGIN исполняет Ready с immutable import binding, fresh whole-course authority, guard revision/CAS и journal/audit атомарно; runtime не получает helpers. Ready-first/restriction-first, stale snapshot/version, timeout while locked, serialization failure, lost ACK; один законный effect или deny/unknown, без replay. Privileged fixture успех не считается operational proof |

Новые tests нужны только после отдельного scope authorization. Существующие C2-J 732 и C1/B07a результаты остаются baseline; дополнительно повторить затронутые suites и соответствующие end-to-end tests после интеграции. Не объявлять TestServer или mock clock доказательством deployed HTTP/clock guarantees.

## 7. Зависимости B02 / B11 / B07b

| Workstream | Что уже можно переиспользовать | Что блокирует пользовательскую интеграцию |
|---|---|---|
| B02 — login/refresh/logout/org switch | S2 sessions, S3 issuer, A1/A2 JWT/clock components, C2-J restricted compatibility | B01 shared PG/clock/egress evidence + отдельное HTTP разрешение; L16 browser/O01/CSRF tests и approved deployment principals. Component acceptance не production wiring |
| B11 — scoped learning authorization | S3 server ownership/generations, conservative processing, C2 guard/ACL | B01+B02; inventory всех aliases/read/count/write/file paths, current restriction serialization и negative matrix. Exact PRS1 остаётся OFF; не расширять permissions при missing evidence |
| B07b — ImportAttempt/Ready/preview | C1 isolated compiler receipt; C2-F–J processing/identity foundation; H03 closed | H04 narrow writer, durable exact-version storage/unknown recovery, применимые G02 file bounds, approved fresh whole-course adapter; для user upload/preview дополнительно B02/B11 и HTTP authorization. Ready не publication/B08 |

Изолированные contract/component slices можно поручать отдельно до полного G02 acceptance, сохраняя запрет HTTP/Ready claims. Граф критического пути: **owner contract → P1 PG/egress/clock qualification → P2/P3 proof → отдельная интеграция B02 → B11 → B07b user upload/preview + H04/file acceptance → общий owner acceptance/cutover decision**. H04 design и product components могут готовиться независимо по отдельному разрешению, но работа в checkout остаётся последовательной.

## 8. Трудоёмкость и stop budget

Оценки — плановые чел.-дни одного инженера после authorization, не обещание срока закрытия недоказуемой гарантии. Owner ожидание, внешний infrastructure contract и independent review не имеют подтверждённого календарного срока.

| Этап | Оценка / ограничение |
|---|---|
| P0 documentation consolidation после точного решения | 1–2 дня; без принятия E03-C/N нормативные файлы не менять |
| P1 qualification одного profile | 3–5 дней timebox на evidence dossier и go/no-go; не срок получения отсутствующих гарантий |
| P2 component plumbing/compensation/accounting design + узкая реализация | 3–5 дней при принятом механизме; новый durable protocol/schema переоценить отдельно |
| P3 targeted disposable proof harness | 4–7 дней при наличии контрактов/oracles; hardware/device instrumentation или новая architecture вне оценки |
| P5 evidence/review preparation | 1–2 дня; сам independent review отдельно |
| B02 / B11 | По [MVP backlog](MVP-BACKLOG.md): 6–10 дней каждый; отдельные slices, не включены в B01 estimate |
| H04/ImportAttempt | Предварительно 5–8 дней на narrow writer + restricted race/unknown tests после принятия storage/transaction contract; durable storage, HTTP preview и B08 не включены |

B01 ограниченная engineering часть P0–P3/P5: **12–21 чел.-день условно**, только если P1 даёт пригодные contracts. Полный G02 срок **не оценён**: нерешённые B-PG/B-EGRESS/B-CLOCK могут остановить путь. После 3–5 дней P1 отсутствие contract фиксируется как stop, а не повод бесконечно добавлять probes. Не суммировать эти оценки в дату MVP: часть integration evidence пересекается с B02/B11 и требует уточнения после P1.

## 9. Точные решения для владельца — предложения, НЕ ПРИНЯТЫ

**OD01 — оставить текущую норму или принять уже подготовленный A06.** Рекомендуется рассмотреть A06 как минимальный оформленный пересмотр, без обещания, что он закроет G02. Если выбрана текущая норма: «T04/E03 full completion сохраняю; допускаю только profile с доказанными full-stop bounds, иначе G02 остаётся BLOCKED». Если выбрана редакция B/C, нужны **два отдельных явных принятия**, а не общее «план одобрен»:

1. «Принимаю E03-C дословно из раздела “Отдельное решение E03-C” G02/A06 OWNER-DECISION. Изменение касается только completion-семантики; остальные E03 obligations сохранены». Private I/O/unknown могут пережить 23 минуты только в доказанно изолированных границах; это реальное изменение прежней гарантии.
2. «После E03-C принимаю решение N дословно из G02/A06 OWNER-DECISION: ND-01–ND-09 вместе с FAILURE-MODEL, P1–P9/R0–R4 и S4-SPLIT acceptance; разрешаю только документационную синхронизацию. Ни deployment qualification, ни implementation/cutover этим не разрешены».

Ссылки в §2 указывают точные тексты; этот assessment их не сокращает и не вводит третью редакцию. Если владелец не принимает один из пунктов, dependent B/C work остаётся blocked. Если PG/egress не квалифицируются и после принятия A06, требуется новый конкретный architecture/normative diff; автоматически заменять hard bound на SLO или исключать fault нельзя.

**OD02 — минимальное следующее задание.** «Разрешаю только P1: за 3–5 чел.-дней подготовить qualification dossier одного явно названного deployment profile для synthetic MVP и go/no-go по B-PG/B-EGRESS/B-CLOCK и применимым storage/full-stop obligations. Нет новых runtime/config/schema changes, HTTP integration или fault experiments без отдельного scope. Отсутствие контрактов — STOP, не PASS. T04/E03 действуют в последней явно принятой редакции; G02 BLOCKED». Profile и supported failure set утверждаются как часть review dossier, не выдумываются из локальных latency observations.

**OD03 — реализация и cutover раздельно.** После P1 владелец отдельным заданием разрешает конкретные P2/P3 компоненты/стенд и критерии, затем B02/B11/H04 slices. «Принятие плана не разрешает Import Ready, выдачу EXECUTE private helpers, S4 HTTP/runtime cutover, B08, PRS1 exact, production migrations или real-data pilot». После evidence — отдельное G02 acceptance и отдельное deployment authorization с principal/secret/adapters inventory по C2-J. До этого H04/Ready остаётся BLOCKED.

## 10. Проверка документации и guardrails §7

В этом задании изменены только этот новый план и append реестра C2-J. Runtime/fault/browser/unit/integration tests **NOT RUN — documentation only**; 732/732 — проверенный по сохранённому summary исторический итог. Новых deployment/vendor claims нет; текущие внешние контракты не исследовались.

| Guardrails §7 | Результат assessment |
|---|---|
| Actor/use case/invariants/states | covered: owner assessment; future users/operations и неизменные bounds в §§3/6; новых states не введено |
| S2/S3/PRS1/gates | covered: accepted contracts сохранены, exact OFF, cutover blocked |
| Org/course/group/ownership | covered в plan/AT07/10/11; runtime implementation blocked, новый access path N/A |
| Affected subjects/embedded sources | covered как E06/H04 prerequisites; новое materialization N/A |
| Lists/counts/pagination | covered как B11/AT10 obligation; новых queries нет |
| Attachments/history | covered: immutable version/parent/claim/unknown требования; исправления не заявлены |
| Transaction/locks/CAS/retry | covered assessment existing boundary и H04 owning transaction; missing commit bound явно blocked |
| Validation/limits/errors | covered: общий deadline/qualification/capacity/oracles; production qualification blocked |
| Unit/integration/negative/E2E | NOT RUN; future acceptance matrix и historical evidence разделены |
| Schema/API/security/unrelated edits | covered: только два Markdown deliverables; исторические решения не переписаны |
| Real data/secrets/readiness | covered: данных/secrets не добавлено; нет unsupported release claims |
| Checks/evidence gaps | document checks ниже; B-PG/B-EGRESS/B-CLOCK и остальные gaps NOT PROVEN |

Документационная проверка: **PASS** — 37 локальных ссылок плана/нового append разрешаются; whitespace новых текстов и `git diff --check -- codex/L03-OWNER-DECISION-REGISTER.md` без ошибок; сумма сохранённых C2-J counters = 732. Реестр содержит исходный текст byte-for-byte плюс append. SHA-256 baseline 550 предсуществующих файлов: кроме разрешённого append реестра изменений нет; branch/HEAD/index сохранены. Локальный verification record: `/tmp/lms-b01-g02-closure/validation.json`. Полный репозиторный diff включает прежние изменения и не выдаётся за diff этого задания.

**Остановка на OWNER REVIEW. C2-J ACCEPTED · H03 CLOSED IN ISOLATED SCOPE · H04/READY BLOCKED · G02 BLOCKED · SERVER/HTTP CUTOVER NOT AUTHORIZED · B08 NOT AUTHORIZED · PRS1 EXACT OFF.**
