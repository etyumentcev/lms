# G02/A06 — FAILURE-MODEL

2026-10-10. G02/A05 B/C DIRECTION ACCEPTED; G02/A06 DOCUMENTATION AUTHORIZED — основание: явное задание владельца A06. Все SHALL/«должен» ниже — предложенные обязательства, NOT ACCEPTED / NOT IMPLEMENTED / NOT PROVEN. Новая норма, архитектурная квалификация и разрешение реализации требуют отдельных решений.

**A06 NORMATIVE PACKAGE READY FOR OWNER DECISION · T04 UNCHANGED · S4 BLOCKED.**

## Семь раздельных гарантий — предлагаемый нормативный текст

1. **Request deadline.** `t0` — earliest controlled ingress до очереди, headers/body и proxy buffering; `B=60s` для ordinary API, `B≤480s` только для явно обозначенного upload/download. Каждый Range — отдельный запрос. `d=t0+B` неизменен при retries, transit и смене исполнителя. К d должны завершиться HTTP service и последняя передача последнего контролируемого egress, включая buffers, kernel/NIC queues и retransmits. Возврат handler, close и клиентский EOF не являются доказательством. Время доставки bytes, уже покинувших границу контроля, внешней сетью не ограничивается.
2. **Authorization validity.** Admission `a<exp` с conservative clock envelope; true JWT lifetime≤900s, signature/issuer/audience/algorithm/scope проверены. Issue/refresh/switch используют fresh authority. Для самого раннего применимого `D=AccessValidUntil`: `exp=min(Now+900s,D−600s)`; exp≤Now — не выдавать. Ordinary revoke атомарно устанавливает `D≤RevocationRequestedAt+7200s`, retries не продлевают D; regrant — новое поколение. Истечение JWT внутри принятого запроса само по себе не отменяет его, но не разрешает переходы после d. Current ProcessingRestrictions действуют отдельно, без ordinary-revoke задержки.
3. **Logical operation completion.** Авторитетно установлен единственный `Committed` или `Aborted`, исключён поздний противоположный исход. HTTP timeout не означает ни один из них. Committed требует своевременного авторитетного эффекта; Aborted требует доказанного отсутствия эффекта и невозможности будущего commit, но не обязательно прекращения доказанно изолированного private I/O. Время получения знания об исходе при partition/recovery outage не ограничено универсальным RTO.
4. **Publication safety.** Publication — первый авторитетный domain effect, eligibility/claim, изменение query/count/history/queue, доступная reference/capability либо выдача bytes. Domain linearization `p<d` и до применимого authority cutoff; HTTP egress≤d. Private intent не domain event. Проверка времени до COMMIT не доказывает p. Неизвестная своевременность — deny/quarantine во всех readers. Поздний domain commit — нарушение, даже если клиент получил timeout. Новый авторизованный запрос может читать ранее своевременно committed результат; он не публикует задним числом просроченную команду.
5. **Physical I/O completion.** Фактическое прекращение работы и issued queues/handles наблюдается отдельно. Универсальный 60/480s bound снимается только с доказанно изолированного private staging/read/cleanup I/O, которое не может изменить опубликованную version, domain state, eligibility/claim, доступ или egress. Direct visibility-changing PG COMMIT и отправка edge не подпадают под исключение. PhysicalPending не называется quiescence или physical deletion.
6. **Recovery.** Только установление прежнего исхода, поддержание quarantine/fence/accounting и безопасное обслуживание orphan. Нет нового COMMIT пользовательской команды, replay/resume, нового бюджета старой operation или наследования её JWT. Status/read — новый bounded запрос с новой авторизацией. Service identity имеет минимальные maintenance-права, а не права пользователя. Unknown не означает rollback. Потенциально committed originals сохраняются. Purge exact version требует доказанных no refs/claim race serialization и quiescence; tombstone не подтверждает physical delete.
7. **Supported failure model.** Гарантии условны на явно квалифицированный deployment и перечисленные ниже отказы. Пока ни один deployment A06 не квалифицирован. Неизвестный обязательный bound либо отсутствующий safety mechanism означает BLOCKED, а не исключение неудобного fault после теста.

## Временной контракт

Trusted remaining budget только уменьшается; нельзя доверять client deadline/header. Monotonic cutoff учитывает drift, dispatch и suspend, а UTC envelope — issuer/API/DB/edge uncertainty и rounding. Все обязательные численные bounds пока NOT ESTABLISHED. Internal publication/egress cutoff выбирается до d на доказанный worst-case tail. После cutoff нет новых stages/COMMIT/chunks/retries. Cancellation распространяется, но является запросом остановки, не свидетельством rollback/quiescence.

Для известного D: `lastControlledSend < D−600+Δ+λ+B`, требуется `Δ+λ+B≤600`. При B=480 остаток 120s относится только к uncertainty; он не extra execution/rollback/drain budget. Отдельно соблюдаются true JWT≤900 и HTTP≤60/480. Conservative admission не добавляет положительный leeway после фактического exp. Publication имеет собственное доказательство p, не выводится из этой формулы.

JWT до revoke: новые admission прекращаются максимум через 900s, допустимые новые эффекты/egress — максимум через 1380s (23m), **только если** publication/HTTP contracts доказаны. Это не bound physical private I/O или recovery. Unknown clock запрещает новые issue/admission; running operations требуют независимо работающего monotonic cutoff с доказанным envelope, иначе temporal guarantee не поддержана.

## Модель отказов B/C

ID M1–M5 здесь — классы отказов A05, не разрешение migration M2/M3.

| Класс | Предлагаемая поддержка | Обязательные предпосылки / отказ при отсутствии |
|---|---|---|
| M1 bounded execution | HTTP≤B; p<d; physical bound только явно включённых путей | Pinned stack, finite capacity/queues, clock/dispatch/commit/egress ledger. Ни p99, ни max observed не worst-case contract |
| M2 private worker crash/hang, late/duplicate IPC, retained private write, ACK loss, resolver outage | Safety P1–P9; private I/O и knowledge могут оставаться pending после d | Изоляция P4/P5/P6, независимый qualified edge; нет доступа к PG domain/published bytes/egress. Если есть такой доступ — это не M2 |
| M3 PG/WAL/последний publisher hang | Direct publication только при доказанном commit/fence bound до d | Без контракта путь не допускается. Alternative invisible-intent design требует отдельного полного proof всех readers и последней promotion boundary. BLOCKER B-PG |
| M4 edge/event loop/kernel/NIC hang, buffering/retransmit | HTTP≤B только при independent cutoff всех контролируемых queues | Нет установленного механизма и lower-layer oracle — deployment не квалифицирован. BLOCKER B-EGRESS |
| M5 restart/partition/failover/old primary | Default deny до durable epoch и единственного authoritative timeline | Old-primary fence, continuity acknowledged history, no late old-generation mutation; absence на replica не abort proof |
| Correlated failure | Только явно перечисленные сочетания, при сохранённом независимом enforcement | Общий kernel/control plane/time/network fault не становится независимым watchdog. При отказе всех защит нет universal guarantee |
| Вне модели | Byzantine trusted storage/DB/operator, ложная durability, произвольная потеря всех fences/time/hardware | Нет universal safety/termination/recovery claim; containment где возможно. Такие deployments нельзя выдавать за qualified |

Исключение fault из timing guarantee не разрешает позднюю публикацию. Если containment при нём не доказан, заявляется unsupported deployment/путь. Отказ admission защищает ещё не начатую работу; он не исправляет уже issued COMMIT или уже queued packet.

## PostgreSQL publication boundary — B-PG, OPEN

Для direct domain transaction допускается только контракт, который гарантирует либо linearization p<d, либо необратимое исключение позднего visibility-changing commit. Нужны bound ожидания locks/WAL/flush/commit, источник этих bounds, independent fence и авторитетное доказательство исхода. Fence, который ждёт тот же зависший COMMIT, сам не решает задачу. Время, записанное внутри transaction перед COMMIT, не commit-time oracle.

Без этого late COMMIT может стать виден обычным readers раньше resolver. `Published=true`, второй COMMIT, post-commit quarantine, read-before-write CAS, cancel или shutdown connection не считаются решением. Вариант private intents допустим лишь если ни один reader/count/history/queue/claim/event consumer не видит их как domain state, а последняя visibility transition сама имеет доказанную своевременность. Перенос проблемы в promotion не закрывает B-PG. Проверяемого решения в A02–A05 нет; A06 его не изобретает. Direct path заблокирован.

## Controlled egress — B-EGRESS, OPEN

Предлагаемый контракт требует точной карты controlled ingress→proxy→API→proxy→kernel/NIC, общего trusted d, запрета сброса бюджета, deadline-aware отправки и independent stop/drain механизма с bound на каждый слой. Watchdog должен переживать заявленный fault edge. Каждый queued/retransmitted byte входит в last-controlled-TX oracle. Если NIC/managed hop остаётся под контролем deployment, его нельзя объявить внешней сетью ради PASS. Физическая граница и исключения фиксируются до эксперимента.

Read/idle timers, handler abort, close и user-space packet trace недостаточны для нижних очередей. Пока ledger/dossier/oracle отсутствуют: absolute HTTP deadline не доказан, integration acceptance blocked.

## Private storage isolation — B-ISOLATION, OPEN

Предлагаемый механизм: уникальный непереиспользуемый `(epoch, OperationId, executionGeneration, objectVersion)`; worker ограничен private version и лишён PG/domain, published write, других generations и public egress capabilities. Exact sealed version получает hash/length/durability receipt только при backend-enforced immutability, учитывающей retained handles и уже issued writes. Если такая seal невозможна, version остаётся неeligible; rename/chmod/hash не замена.

Поздняя запись допустима только в прежний private namespace; она не меняет опубликованную exact version и не создаёт domain reference. Publisher отдельно проверяет receipt, generations, scope/restrictions и P2/P3. Isolation proof должен перечислить credentials/FD/mounts/IPC/readers и concurrent delete/claim. Нет интегрированного gateway или проверенного backend contract: это проект обязательства, не готовая реализация.

## Recovery state machine и capacity

`Absent → Registered → Staging → Sealed → Committed` только в исходном бюджете. Durable registration/binding подтверждаются до side effects; unknown registration останавливает их. Переход в Aborted требует authoritative absence плюс невозможность late commit. Terminal states неизменны. `OutcomeUnknown`, `Quarantined`, `FencingPending`, `PhysicalPending` — отдельные флаги знания/изоляции; HTTP Closed их не сбрасывает.

Resolver читает исходный OperationId на authoritative serialized timeline после завершения или доказанного fence прежней transaction. Lock/resolve timeout сохраняет unknown. Timely committed факт можно вернуть при fresh read authorization; late commit — incident и FAIL. Retention idempotency tombstones покрывает доказанный replay horizon; unknown horizon запрещает очистку.

Admission резервирует bounded worker/byte/queue/orphan capacity до side effects. HTTP timeout не освобождает слот без evidence прекращения или безопасной изоляции с сохранённым ресурсным учётом. Outstanding bytes/workers остаются в лимите до реального освобождения; бесконечные replacement workers запрещены. Недоступность recovery приводит к reject новых операций при достижении cap, а не к потере originals.

## E03 compatibility

Действующий E03 «started operation≤8m» через ADR-L15 означает full completion. B/C допускает private PhysicalPending и OutcomeUnknown дольше 8m: это **нормативный конфликт**, не просто уточнение терминов. SecurityBlocked login/issue/refresh/session/audit, true JWT≤15m, отсутствие immediate revoke и текущие restrictions совместимы и сохраняются. Для изменения completion требуется отдельное решение E03-C в [owner package](L1-G02-A06-OWNER-DECISION.md). До него E03 неизменен, новый пакет не вступает в силу.
