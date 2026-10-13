# G02/A04 — Contract matrix

2026-10-09. **V1 CONTRACT EVIDENCE BLOCKED.** Единственный кандидат: C04-1, строго профиль и [source register](L1-G02-A04-CANDIDATES.md#источники-и-область-применения) S1–C1. Ссылки ID ниже разрешаются в этом реестре; версия/конфигурация строки наследует профиль C04-1, исключения указаны явно.

`CONTRACTUALLY ESTABLISHED` — узкая семантика первичного источника при его предпосылках; `EXPERIMENTALLY OBSERVED` — только измеренный случай; `ASSUMED` — проектная предпосылка без проверки; `NOT ESTABLISHED` — достаточное доказательство отсутствует; `CONTRADICTED` — конкретное утверждение противоречит источнику. A04 не проводил экспериментов, поэтому измеренных bound в этой матрице нет. Пробы P03 не переносятся на AWS.

| Требование | Контракт / применимость | Численный bound | Источник | Статус |
|---|---|---|---|---|
| Storage: immutable versions | S3 COMPLIANCE version защищена в её retention; reference обязан содержать versionId | Заданный retain-until, не latency bound | S1 | CONTRACTUALLY ESTABLISHED |
| Storage: durable acknowledged bytes | S3 Standard: multi-AZ redundancy и annual durability design target; полного fault envelope, device flush evidence и bound нет | Не установлен для C04-1/V1 | S3, S5; профиль managed backend | NOT ESTABLISHED |
| Storage: no late writes после epoch loss | Предположение «Object Lock запрещает новые versions устаревшего writer» неверно | Отсутствует | S1 | CONTRADICTED |
| Storage: outstanding I/O | Предположение «AbortMultipartUpload ACK означает прекращение всех in-flight writes» неверно | Отсутствует | S2 | CONTRADICTED |
| Storage: raw bypass | Endpoint deny + role isolation — proposed topology; policy/session/mount/code review отсутствует | Не временной контракт | S4, F3 | ASSUMED |
| Storage: restart/failover | WORM остаётся backend свойством version; durable LMS epoch, запрет replay и quiescence после рестарта не предоставлены Object Lock | Не установлен | S1–S3, F3 | NOT ESTABLISHED |
| PostgreSQL: commit durability | PG17 synchronous_commit=on/fsync=on при корректном storage; RDS Multi-AZ single standby synchronous replication | Не latency bound | P2, P4 | CONTRACTUALLY ESTABLISHED |
| PostgreSQL: commit/rollback/backend end | Нет документированного конечного worst-case bound при WAL/fsync hang в исходной модели | Не установлен | P4, P5 | NOT ESTABLISHED |
| PostgreSQL: terminate means success | При timeout=0 true означает отправку сигнала; positive timeout может закончиться false. Это не unconditional stop | Число timeout — ожидание caller, не backend bound | P5 | CONTRADICTED |
| PostgreSQL: acknowledged history + old primary | Synchronous replica полезна; полного verified old-primary isolation, no-loss timeline и restart contract на V1 нет | Не установлен | P2–P4 | NOT ESTABLISHED |
| PostgreSQL: authoritative outcome | OperationId/result в той же transaction, сериализованный resolver на authority — обязанность дизайна, не функция готового RDS | Не установлен | P5, P6; A02 invariants | ASSUMED |
| Fencing: mutation point | StopInstances ограничен EC2; IAM deny не атомарная epoch-проверка у S3 mutation. Запрошенный physical fence не установлен | Не установлен | F1–F3, S1, S2 | NOT ESTABLISHED |
| Edge: stream lifetime control | Envoy1.35 max_stream_duration сбрасывает stream по timer при работающем event loop | Config ≤60/480s, не ingress-to-wire bound | E1 | CONTRACTUALLY ESTABLISHED |
| Edge: весь egress покрыт max_stream_duration | Flow-control flush corner case явно исключён из этого timer; userspace reset недостаточен | Не установлен | E2 | CONTRADICTED |
| Edge: kernel/NIC cutoff и oracle | Last controlled hop — edge EC2 ENI; guest socket/log не hardware last-send oracle; независимый bounded stop не доказан | Не установлен | E3/E4, F1/F2, C1 | NOT ESTABLISHED |
| Supervisor: независимость | Step Functions не зависит от жизнеспособности worker guest; полная независимость от shared AWS failures не доказана | Dispatch/termination bound отсутствует | U1–U3, F1–F3 | ASSUMED |
| Clock: uncertainty/scheduler | NTP + monotonic не дают upper bound drift/dispatch при выбранных hang/partition; сведения о timestamp precision недостаточны | Δ, λ, J не установлены | C1, U1/U3 | NOT ESTABLISHED |

## Storage и physical fencing

Backend WORM и epoch fencing — разные обязательства. Если writer отправил PUT/part до revoke, внешний stop worker не даёт контракта завершения запроса внутри S3. Приложение может проверять generation перед отправкой, но нет предъявленного механизма, который сериализует **LMS epoch loss с физической mutation** внутри S3. Иначе сохраняется TOCTOU из A03. Conditional object operations также нельзя без доказательства объявить cross-object epoch fence.

Object Lock не sealing protocol всего request: temporary multipart parts, новые versions и delete markers требуют отдельного учёта. Проверка bytes по точному versionId полезна для публикации, но не доказывает прекращение orphan writes. Retention не должна приводить к выдаче запрещённого physical DELETE за завершённый: если такой путь требует удалить защищённую version до retain-until, эта настройка несовместима с ним (S1). Менять lifecycle/T04 ради принятия кандидата A04 не разрешает.

No raw bypass остаётся ASSUMED даже при private endpoint: проверить нужно все роли, сохранённые sessions, прямые маршруты, alternate access points и management privileges. Root/cloud administrator Byzantine исключён исходной моделью; обычные ошибки конфигурации этим не исключаются. Restart старого EC2 не должен возвращать write authority без нового доказанного epoch; действительный credential сам по себе не право возобновить старый запрос.

## PostgreSQL: termination и outcome

**PostgreSQL 17 / RDS17.6 не предоставляет в рассмотренных источниках конечного worst-case bound commit/rollback/backend termination при выбранной модели зависаний WAL/fsync.** Настройки statement_timeout/lock_timeout/transaction_timeout и pg_terminate_backend не восполняют его (P4/P5). API connection close не останавливает удалённый backend с доказанной задержкой.

При утрате COMMIT ACK результат unknown. Для authoritative resolution нужны durable OperationId + domain result в общей transaction, проверенный authority/timeline, завершение или fence старой transaction, затем сериализованное чтение; read replica и одиночный SELECT-not-found недостаточны (P6 и A02). Ни повтор mutation, ни удаление потенциально committed bytes не разрешаются неизвестным результатом. Недоступность resolver может сохранить статус unknown после доказанного stop; она не оправдывает продолжающуюся работу за deadline.

RDS failover управляет primary/standby, но публичный интерфейс не предъявляет нужного этому proof численного old-primary fence/quiescence receipt (P2/P3). Это **не утверждение о наличии split brain в RDS**. Синхронная репликация признаётся, однако весь invariant acknowledged history при конкретных fault interleavings и restart здесь не квалифицирован. Snapshot restore/asynchronous replica как новый authority не предложены.

## Последний контролируемый hop и oracle

Топология: client ↔ public route ↔ **edge EC2 ENI** ↔ Linux TCP/TLS/Envoy ↔ private execution/RDS/S3. Последний адресуемый владельцем сетевой hop — ENI edge-инстанса. ENI является логическим компонентом, primary ENI не detach-able (E4). Физические NIC и fabric AWS лежат под ним и не получают пользовательского reset/drain certificate. Уже отправленные вовне bytes могут прибыть позже; это не late send, но guest handoff вниз по стеку нельзя автоматически считать окончательной отправкой.

`t0` должен быть earliest controlled ingress, включая очередь, TLS/header/body и admission. Envoy stream timer начинается внутри обработки stream, поэтому до него есть отдельный недоказанный интервал. Активный trickle не отменяет stream timer, но это ещё не cutoff всех очередей. Нужны bounds для userspace codec buffers, kernel send/retransmit/qdisc, driver/NIC/fabric и для независимого stop при hang edge. Нулевой delayed_close_timeout не выбран: E2 не обещает им немедленное прекращение flush.

Linux timestamp interface различает места наблюдения (E3). RX hardware timestamp (C1), access log, tcpdump в guest, TCP ACK и close не являются полным доказательством последней физической TX и отсутствия будущих sends. Достоверный last-send oracle C04-1 **NOT ESTABLISHED**; trace loss/неизвестные очереди дают inconclusive, не PASS.

## Supervisor, admission и correlated failures

Step Functions Standard с Wait и EC2 SDK call может исполняться после смерти guest OS (U1/U2); отдельный аппаратный supervisor не предполагается. Нельзя приписывать сервису отдельное питание, физический management link, watchdog oscillator или independent NIC cut relay: таких контрактов нет.

Требуемая последовательность — зарегистрировать trusted deadline до допуска mutation, дождаться подтверждённого arm, на deadline выполнить stop, установить worker/backend/storage/edge quiescence. Она не реализована; timeout state machine или её завершение не означает stop удалённой задачи. Wait timestamp имеет секундную гранулярность и обрезает milliseconds; точное scheduling delay не ограничено контрактом U1. Throttling/quotas U3 не дают deadline admission.

Лимит concurrency/очереди/объёмов ещё должен иметь доказанный capacity envelope. Произвольное маленькое число slots не устраняет бесконечное ожидание нижнего слоя; orphan slot нельзя освобождать по client timeout. При unknown fence/clock/authority дизайн требует закрытого admission, но текущее LMS не сертифицировано как реализующее это.

Общие зависимости: region, EC2 control plane, IAM, AWS network и RDS/S3 control/data planes. Модель исключает отказ всех stop paths одновременно, но даже при **работающем** Step Functions отсутствует bound его EC2 request и завершения ранее принятого S3/PG I/O. Следовательно, отказ V1 не основан только на исключённой глобальной катастрофе.
