# G02/A02 — Verification plan и критерии доказательства

2026-10-09. **PLAN ONLY; новые fault tests NOT RUN; S4 BLOCKED.**
[Архитектура / I1–I7](../docs/architecture/L1-G02-BOUNDED-EXECUTION.md) · [Failure model](../docs/architecture/L1-G02-FAILURE-MODEL.md) · [Owner decision](L1-G02-OWNER-DECISION.md).

## 1. Доказательство состоит из нескольких частей

- Safety argument/model: состояния, transition relation, единственный authority holder, stale generation, commit/fence ordering и невозможность повторного domain effect. Перебрать interleavings и crash между каждой парой durable действий. Для PG и gateway явно указать linearization points; не предполагать общую FS/PG transaction.
- Infrastructure argument: pinned versions, численные worst-case contracts Q/S/P/X/R/E/J, источник каждого bound и независимость stop paths. P99/max observed, успешный kill и «timeout установлен» недостаточны. Отсутствующий bound → FAIL/NOT ESTABLISHED.
- Fault injection: реальные PG, gateway/storage и edge в disposable synthetic environment; проверить соответствие реализации модели, включая отрицательные контрпримеры. Тесты не доказывают все отказы ОС/железа.
- Security review: credentials/mount/FD bypass, no direct publication, no stale writer, scope/current restrictions, atomic claim/delete и leakage через status. В A02 независимое review не проводилось.

Gate V0: владелец выбрал architecture/failure model и отдельно разрешил соответствующий implementation/prototype scope. Gate V1: contracts и доказательство временной композиции. Gate V2: реализация invariants и adversarial tests. Gate V3: полный HTTP/storage/PG/edge evidence + security review. Только после всех gate может рассматриваться G02 acceptance; S4 имеет собственные оставшиеся критерии.

## 2. Измерения и oracle

Каждый trace содержит synthetic OperationId/generation/epoch, trusted admission, stage start/end, durable commit outcome, worker/backend identity, observed exit, last mutation и last controlled-egress send. Без body, secrets/JWT и персональных данных. Часы измерения калибруются с известной погрешностью; отсутствие события stop не интерпретируется как stop. Trace loss → INCONCLUSIVE/FAIL, не PASS.

Для запроса oracle требует `latest(actual storage activity, PG operation end, worker/handle cleanup, edge last send) - t0 ≤ B` с консервативным учётом измерительной ошибки. Для abandoned/unknown request тоже проверяется quiescence. Долгая crash recovery DB может быть инфраструктурной работой, но поздний пользовательский commit или возобновлённый writer не исключаются из oracle переименованием в recovery.

Наблюдение «тишина N секунд» не доказывает отсутствие будущей записи. Нужны barrier/quiescence ACK с проверенным смыслом, исключённые stale credentials/epochs и restart tests. Device-level delayed writes требуют lower-layer observability/contract. Недоступный instrument не заменять mock PASS.

## 3. Матрица fault injection

| ID / сценарий | Инъекция и наблюдение | Критерий |
|---|---|---|
| F01 worker user-space hang | Pause/busy-loop worker в каждой фазе; API/supervisor работают | I6: kill+exit+quiescence+handles в X, ответ/egress в B; slot не освобождён раньше |
| F02 blocking open/read/write/flush/rename/unlink | Controlled test backend/FIFO отдельно для interruptible waits; device fault только в disposable VM, отдельно от surrogate | I2/I4/I6: нет late mutation; либо доказанный lower-layer stop, либо deployment FAIL. FIFO нельзя выдать за D-state proof |
| F03 lost/stale worker | Delay completion, restart gateway, replay старые IPC/chunks/seal после Fence и после g+1 | I2–I4/I7: старый result не меняет PG/новый объект, fence ACK только после outstanding I/O quiescence |
| F04 token TOCTOU | Pause после generation check, revoke, затем продолжить native write | На POSIX-only adapter ожидаемый контрпример; кандидат обязан устранить race или признать отсутствие физического fence |
| F05 commit not yet started / rollback | Fail до BEGIN, после insert до COMMIT; потерять ACK rollback отдельно | I1/I5: confirmed rollback отличён от unknown; нет premature delete; identity не теряется |
| F06 commit succeeds, ACK lost | Разорвать server→client после durable COMMIT, сохранить DB trace | Ровно один domain result, сохранён файл; retry same key читает факт, не replay; resolver на primary |
| F07 COMMIT/WAL hang | Fault storage PG/WAL, lock wait отдельно; оборвать API/client | PG backend остаётся учитываемым; cancel-send не PASS; end/fence ≤P+X или T04 FAIL |
| F08 false absence | Старый transaction ещё способен commit; SELECT-not-found в другой connection/replica | Purge запрещён; resolver сериализуется через operation lock и authoritative timeline |
| F09 crashes at every boundary | После identity, chunk, seal, перед/после domain commit, перед response; crash API/gateway/container по очереди | I1–I5/I7: no dangling successful metadata, no duplicate, no auto-resume expired mutation |
| F10 retry concurrency | Два API nodes, один key/digest; иной digest с тем же key; replay после deadline и restart | Unique atomic result; conflict для mismatch, deadline не продлён; status auth; не два descendants/results |
| F11 claim/delete/recovery race | Concurrent submit claim, tombstone, purge, recovery и старый worker | Row/CAS ordering сохраняет attached original; purge только exact unreferenced quiescent version |
| F12 storage failure | Full/read-only/disconnect, потеря seal response, corruption/hash mismatch | Unknown/quarantine, bounded stop, no publication до receipt+metadata; capacity bounded |
| F13 PG failover / old primary return | Partition, recovery WAL, old writer reconnects | Не начинать новое поколение без fence; no acknowledged-history loss; при невозможности FAIL |
| F14 edge trickle/buffering | Медленный upload/download, активный трафик на границе timeout, все Range; pause edge | Whole-operation deadline не продляется; last egress включая queues в E; supervisor independent |
| F15 clocks and admission | ±bound offsets, rounding, monotonic drift, wall jump, queue delay, JWT near exp/D | JWT фактически ≤900s; B≤60/480; Δ+λ+L≤600; unknown error закрывает admission |
| F16 ordinary revoke | Issue/revoke race, old JWT, finite D, повтор revoke, A/B | ≤2h неизменно; old token completion bound ≤1380s при предпосылках; не продлевать D/чужой scope |
| F17 recovery unavailable | Выключить resolver при lost commit ACK; отдельно отказ всех stop paths | При quiesced executor unknown сохраняется без purge; комбинированный неподдерживаемый отказ явно FAIL/no guarantee |
| F18 supervisor restart/capacity | Crash watchdog, orphan workers, saturate slots/queues | Independent stop path сохраняет bound или модель отклоняется; нет бесконечных replacement/очередей |
| F19 raw bypass | Попытки API/worker открыть shared mount, writable sealed FD, direct storage/edge URL | Доступ запрещён ОС/credentials/protocol; одного теста application authorization недостаточно |
| F20 recovery after deadline | Возврат ACK и executor после B, operator запускает reconcile | Нет нового commit/claim/chunk/publish старой операции; поздняя активность фиксируется как FAIL, а не successful recovery |

Комбинации отдельно: worker hang + API crash; commit ACK loss + gateway restart; storage hang + recovery outage при живом independent supervisor; edge hang + API cancellation. Для общего kernel failure supervisor должен находиться за его пределами. Одновременная потеря всех независимых stop paths фиксируется как неподдерживаемая, без ложного PASS.

## 4. Реальные сроки и критерии выпуска evidence

Тестировать обычные 60s и long 480s, включая admission перед exp/D и задержку на каждом stage cutoff. Scaled clocks пригодны для перебора состояний; минимум отдельные real-time runs проверяют реальную интеграцию timers/edge/PG. Повторение под saturation выявляет хвосты, но не превращает sampling в математическое доказательство.

Для каждой строки отчёт: build/commit, topology, versions, failure injection point, expected/actual, timestamps+uncertainty, authoritative transaction evidence, object hashes/versions, termination/quiescence evidence, last edge send, PASS/FAIL/INCONCLUSIVE, ссылка на redacted artifacts. Неподдерживаемые инфраструктурные случаи не молча SKIP: явное ограничение deployment и нерешённое требование.

Необходимы: все I1–I7, отсутствие unresolved critical findings, договорённый failure envelope, доказанная композиция bounds и соответствие ей тестовой инфраструктуры. Отдельные новые тесты успешны, но storage/PG/edge bound отсутствует → **G02 NOT ACCEPTED**. Нельзя принять file path через отключение требуемой функциональности. Backend regression после будущей интеграции проверяет совместимость, но не заменяет T04 proof.

## 5. Что проверено в A02

Прочитаны текущий storage/service/controller, Compose/nginx, ADR-L15 и S4 checkpoint; сопоставлены первичные источники (ссылки в failure model). Подготовлены документы; выполнена проверка ссылок, обязательных разделов, diff и сохранности существующего test file. Runtime/config/schema не менялись. Новые fault-injection или backend tests в A02 не запускались: нет новой реализации. Исторические 355/355 и FIFO probe в [S4 results](L1-S4-RESULTS.md) принадлежат предыдущему checkpoint, не этому доказательству.
