# G02/A02 — Модель отказов и границы гарантий

> **2026-10-12 — HISTORICAL FAILURE MODEL.** For Teacher Revocation, the current [owner decision](../../codex/TEACHER-REVOCATION-OWNER-DECISION.md) explicitly accepts late physical side effects and removes hard upper bounds for PG/FS/kernel/egress/cleanup completion from acceptance. The supervisor/fencing/quiescence requirements below are preserved as historical analysis and must not be relabelled PASS. They no longer block readiness solely because physical termination is unproved. Logical deadline, cancellation request, no-late-success, replay/unknown-outcome safety, authorization, isolation, and privacy remain current requirements.

> **2026-10-12 — bounded teacher revocation.** [Действующий scoped contract](TEACHER-REVOCATION.md): fresh issue/refresh, JWT≤15m, immutable operation≤30m, revoke≤60m в выбранном bounded execution envelope. Для ordinary revoke двухчасовой D и D−10m superseded; неограниченный late COMMIT/egress не удовлетворяет новому контракту. История ниже сохранена. Processing/file safety, PRS1 и release gates не сняты; общий HTTP/physical bound NOT PROVEN.

> **NR1 effective notice — 2026-10-11.** Историческая A02 failure model ниже сохранена. Для NR1 paths temporal completion obligations заменены только согласно D1–D8; successor failure/acceptance model — N1–N7 и §6 review. Старые FAIL/unsupported full-stop результаты не стали PASS. Safe pending accounting обязателен, универсального recovery RTO нет.
> Нормативный источник: [N1–N7 / точный D1–D8](../../codex/MVP-B01-G02-NORMATIVE-REVIEW.md), [принятое решение](../../codex/L03-OWNER-DECISION-REGISTER.md), [sync results](../../codex/MVP-B01-G02-NR1-SYNC-RESULTS.md). Только controlled SyntheticOnly и отдельно принимаемые routes; неопределённый scope → blocked. Credential issuance, grants, B08 publication, general jobs и внешние irreversible effects не получают автоматического исключения. CLOCK NOT PROVEN; технически NR1 NOT IMPLEMENTED / NOT ACCEPTED.
> Исторический текст ниже сохранён; старые failure results не превращены в PASS.

2026-10-09. **PROPOSED model; no certified deployment; S4 BLOCKED.** См. [архитектуру](L1-G02-BOUNDED-EXECUTION.md) и [план доказательства](../../codex/L1-G02-VERIFICATION-PLAN.md).

## 1. Поддерживаемая модель — контракт кандидата

Рассматриваются crash-stop/crash-restart, потеря/дублирование/переупорядочивание сообщений, bounded transient storage/network failures и зависания исполнителей. Рабочий процесс может перестать отвечать в любой фазе. Gateway, PG и edge считаются доверенными по содержанию, но могут зависнуть/упасть; Byzantine storage/администратор с raw disk access исключены. Весь admission/queue/body включён в deadline; capacity и concurrency bounded.

Для условной поддержки необходимы: pinned stack; известная погрешность часов и scheduling; отдельный fault domain supervisor; отсутствие raw write bypass; durable монотонные epochs; один авторитетный PG timeline без split brain/потери acknowledged history; storage durability и конечное прекращение outstanding I/O; контролируемый egress с абсолютным cutoff. Одновременные отказы допускаются только если остаётся действующий независимый stop path с доказанным bound для каждого зависшего участника. Коррелированный отказ исполнителя и всех его watchdog/fence механизмов не покрывается.

Это specification required capabilities, а не заявление о возможностях текущего хоста. Известный baseline: `docker-compose.yml`, PG `17.6-alpine`, local attachment volume напрямую у API, nginx без абсолютного whole-response cutoff. Ни один numeric hardware/OS completion SLA в репозитории не установлен.

## 2. Таблица отказов

| Отказ | Поведение архитектуры | Граница поддержки / статус T04 |
|---|---|---|
| Блокирующий filesystem I/O, interruptible wait | Supervisor cancels/kills worker, наблюдает exit, storage quiescence и закрытие handles | Поддерживаемый кандидат только при доказанном X; FIFO probe доказывает один случай, не worst case |
| Неотменяемый syscall / D-state / зависший device flush | Закрыть новые команды/доступ, isolate generation, не ACK fence до прекращения issued I/O | Без независимого storage/device bound **не поддерживается строгий completion**; default POSIX не решает |
| Worker завис в user space | Внешний supervisor, deadline, kill/reap, запрет late IPC | Условно поддерживается; ОС/scheduler и orphan children должны укладываться в X |
| PG COMMIT/WAL fsync завис | Cancel/backend termination, далее инфраструктурное fencing при необходимости; unknown outcome, без replay/purge | PG timeout не доказывает прекращение kernel I/O; без backend/WAL bound T04 не доказан |
| COMMIT выполнен, ACK потерян | Durable OperationId/result, authoritative serialized resolve, сохранить bytes | Safety поддерживается при сохранении PG history; resolution latency ограничена лишь при доступной DB; late mutation запрещена |
| Процесс/API/container умер | Stop связанных worker/backend/streams независимым механизмом, fail-closed restart | Container exit не доказывает остановку дочерних/удалённых исполнителей; нужны evidence по каждому |
| Storage unreachable/read-only/full | Нет новых stages, bounded failure, quarantine sealed/unknown bytes | Timeout клиента не завершает серверную запись; зависший remote storage требует собственного deadline/fence contract |
| Storage теряет подтверждённые bytes/повреждает их | Hash/receipt mismatch → no publication, incident | Доступность/целостность результата не гарантированы при нарушении durable storage contract; не разрешать dangling successful attachment |
| Edge задерживает/buffers response, slow/trickle client | Один абсолютный admission-to-egress deadline, независимый stop, очистка очередей | Inactivity timers недостаточны; для edge hang нужен отдельный действующий control path |
| Внешняя сеть удерживает уже отправленные bytes | Новая отправка прекращается вовремя | Момент получения клиентом вне bounded model; безусловной сетевой гарантии нет |
| Recovery service недоступен | Unknown/quarantine, сохранить originals, запрет reuse/resume; capacity cap → reject | Не обещать RTO. T04 возможен только если старый executor уже завершён другим путём |
| Recovery и все stop/fence paths недоступны при живом старом writer | Не объявлять завершение; incident, admission закрыт где возможно | **Unsupported combined failure; T04 guarantee отсутствует** |
| Clock скачок/неизвестная погрешность | Monotonic elapsed + conservative UTC; issuance/admission fail closed | Если running supervisor также потерял bounded clock/scheduling, completion вне модели |
| Разделение сети / split brain / рестарт старого primary | Старый epoch не допускается; новый writer только после доказанного fencing | Без storage/PG fencing старого writer новое поколение не активируется; availability жертвуется |
| Произвольный отказ ОС, питания, оборудования, всех сетей | Нет обещания bounded completion/durability/recovery | Неподдерживаемый; kill/power-off не доказывает прекращение device writes и safe restart |

Утрата recovery availability не равна утрате completion safety: status может остаться unknown после физически завершившегося commit. Но если исходная работа продолжает выполняться, перенос её в recovery не легализует превышение 60/480s.

## 3. Что считается доказательством и что им не является

Три независимые оси: safety (нет forbidden effect), bounded termination (всё исполнение и egress остановлены вовремя), availability (можно обслуживать/recover). Fail closed может сохранить первую и потерять третью; оно само по себе не обеспечивает вторую. Умолчание о зависшем orphan I/O — нарушение модели.

Нужен deployment dossier: версии kernel/filesystem/storage/firmware/PG/edge; перечень контролируемых hops и failure domains; worst-case bound всех stop/drain/flush путей с источником; схема fencing и credential isolation; clock-error/drift bounds; capacity/queue limits; поведение после reboot/failover; доказательство отсутствия позднего I/O и duplicate history. P99/max observed не являются универсальным upper bound. Если платформа не позволяет установить конечный bound, выбранный deployment отклоняется, а не принимается с «достаточным timeout».

Новые admission запрещаются при unknown authority/clock/storage epoch, незавершённом fence или исчерпанном orphan budget. Outstanding slots не освобождаются по timeout клиента; нужны exit+quiescence evidence. Не создавать бесконечные replacement workers и не fail over на другой timeline без fences. Само обнаружение неисправности тоже входит в X/J, а не происходит после B.

## 4. Первичные источники и предел вывода

Проверены 2026-10-09; это основания для отказа от ложных гарантий, а не сертификация deployment:

- [Linux completions](https://docs.kernel.org/scheduler/completion.html): обычный `wait_for_completion` использует uninterruptible ожидание без timeout; существуют отдельные interruptible/timeout варианты. Из этого следует отсутствие общей гарантии «каждый native wait завершится от SIGKILL», но не утверждение, что все Linux I/O неотменяемы.
- [PostgreSQL 17 client timeouts](https://www.postgresql.org/docs/17/runtime-config-client.html): statement/lock/transaction timeouts ограничивают соответствующие DB операции; prepared transactions имеют отдельные ограничения. Документация не задаёт hardware-fault termination SLA.
- [PostgreSQL WAL reliability](https://www.postgresql.org/docs/17/wal-reliability.html): durability зависит от честности flush и storage stack. Отключение durability ради timeout не решает задачу.
- [nginx proxy module](https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_read_timeout): proxy read timeout действует между чтениями, а не на весь response; buffering — отдельный механизм. Поэтому текущая конфигурация не доказывает абсолютный final-byte deadline.

Вывод о невозможности полного доказательства на текущем baseline — архитектурный вывод из этих границ и локального кода. Ни успешный FIFO kill, ни finite fault-injection suite не доказывают поведение при произвольном отказе ОС/сети/оборудования.
