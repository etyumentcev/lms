# G02/A03 — Infrastructure profile P03

2026-10-09. **INFRASTRUCTURE EVIDENCE BLOCKED · T04 NOT PROVEN · S4 BLOCKED.**

## Scope и provenance

Владелец принял направление G02/A02 C и разрешил A03 research/isolated prototypes. Это supersedes историческое «owner decision pending» A02 только для выбора направления и исследования; runtime implementation не разрешён. Канонический каталог `/home/tea/github/lms`, ветка `codex/l1-s4-jwt-api-authorization`, HEAD `a60ee6c73c4400bd1bdf6c6df434358372745c41`; branch switch не выполнялся. Предсуществующие изменения S4/test, A02 документы и `.obsidian` сохранены. Snapshot diff/hash находится в `/tmp/lms-g02-a03/`; итоговый preservation report — в evidence.

Выбран конкретный доступный **кандидат P03**, а не гипотетический certified storage product. Ниже зафиксирован реально исследованный стек и отсутствующие компоненты C. P03 поддерживает только перечисленные synthetic эксперименты при исправном host kernel. **Ни одного supported T04 deployment не установлено.** Неизвестный firmware SLA или отсутствующий supervisor нельзя заменить выдуманной версией/конфигурацией. Полный runnable C profile не построен, поскольку необходимые контракты отсутствуют; это blocker, а не незаявленная готовность.

Точные команды/вывод: [environment.json](evidence/L1-G02-A03/environment.json). Изолированный [probe.py](evidence/L1-G02-A03/probe.py) содержит точные docker arguments; LMS Compose не запускался.

## Версии, режимы и failure domains

| Компонент / configuration P03 | Режим и failure domain | Stop и ограничение | Контракт / необходимые предпосылки |
|---|---|---|---|
| Ubuntu 26.04.1 LTS x86_64; Linux `7.0.0-38-generic`, PREEMPT_DYNAMIC | Один физический host; общий scheduler/kernel у controller, workers, PG, nginx | SIGKILL + waitpid наблюдает exit; нет worst-case scheduling/exit SLA | [Linux completion waits](https://docs.kernel.org/scheduler/completion.html); нужна доказанная максимальная задержка scheduling/stop, сейчас отсутствует |
| `/tmp` tmpfs, FIFO/process fixtures | Volatile synthetic storage; не durable object backend | Kill interruptible FIFO; tmpfs не моделирует device/WAL hang | Нет durability или worst-case bound; результаты не переносить на ext4/NVMe |
| ext4 `rw,relatime,stripe=128`, `/dev/nvme0n1p2`; WD PC SN5000S SDEQNSJ-1T00-1014, revision `34230100` | Docker data root на ext4, PG private anonymous volume; локальный NVMe/cache/power domain | Нет реализованного storage fence/quiescence API; power-off не доказательство отмены выданного I/O | [fsync](https://man7.org/linux/man-pages/man2/fsync.2.html), [WAL reliability](https://www.postgresql.org/docs/17/wal-reliability.html); device completion/flush honesty/power-loss contract не получен |
| Gateway execution model: C protocol из A02; для probes Python 3.14.4 controller + один subprocess на native I/O | Сам gateway NOT IMPLEMENTED; controller не принимает LMS traffic; one worker per probe, no replacement до wait | Controller kill/reap; нет serialized durable Fence/Seal | Нет реализации actual-mutation epoch check и quiescence ACK; выбранный POSIX backend не предоставляет их сам |
| Worker isolation: отдельный PID; Docker namespaces у PG/nginx | FIFO worker — тот же UID, не security sandbox. Container kernel общий | SIGKILL/reap; runtime kill контейнера | [cgroup v2](https://docs.kernel.org/admin-guide/cgroup-v2.html); exit не доказывает отсутствие device writes; no-bypass НЕ VERIFIED |
| Independent supervisor | **ABSENT** за пределами kernel/device failure domain; Python parent независим только от user-space loop ребёнка | Внешнего проверенного stop path нет | Требуется отдельный fault domain + bounded device/PG/egress fence. Same-host parent не удовлетворяет C для kernel hang |
| PG 17.6 Alpine x86_64, single primary, fsync=on, synchronous_commit=on, full_page_writes=on, wal_sync_method=fdatasync | Fresh anonymous volume, default local WAL, no replication/failover; synthetic schema operations/effects | SIGTERM via pg_terminate_backend; затем observed disappearance; нет WAL-hang bound | [PG settings](https://www.postgresql.org/docs/17/runtime-config-wal.html), [signaling](https://www.postgresql.org/docs/17/functions-admin.html); честный flush и один authoritative timeline обязательны |
| nginx 1.28.3, OpenSSL 3.5.5, Alpine image; HTTP/1.0 loopback probe | nginx → synthetic nc upstream внутри одного container; один controlled proxy; TLS/CDN отсутствуют в опыте | proxy_read_timeout=1s, send_timeout=1s, buffering off/on; docker kill только своего container | [proxy module](https://nginx.org/en/docs/http/ngx_http_proxy_module.html), [send_timeout](https://nginx.org/en/docs/http/ngx_http_core_module.html#send_timeout): inactivity, не absolute cutoff; kernel/NIC queues SLA отсутствует |
| chrony 4.8; Linux monotonic clock, clocksource в environment.json | Все probe timestamps одним host clock; UTC tracking — snapshot | Unknown error → требование deny admission/issuance; в LMS не реализовано A03 | [chronyc tracking](https://chrony-project.org/doc/4.6/chronyc.html): оценка состояния не contract future bound. Допустимая ошибка для acceptance пока НЕ установлена |
| Docker 29.8.1 rootless, containerd 2.3.5, runc 1.5.1, rootlesskit 3.1.0, gvisor-tap-vsock, overlayfs | Containers disposable; ports только 127.0.0.1; volume PG отдельный; no Compose/orchestrator changes | docker rm -f -v только IDs созданных fixtures | Runtime/network sharing не создаёт независимого watchdog; rootless forwarding — дополнительная очередь |

Image pins (использованы ID, без pull/latest resolution):

- PG: `sha256:ef257d85f76e48da1c64832459b59fcaba1a4dac97bf5d7450c77753542eee94`.
- nginx executable из локального web image: `sha256:2a9ff2bed2452490f7ac6cc2c64ad6ec8c22aad2ee71d89d7b4705f916fdda14`; запущен entrypoint nginx с isolated config, LMS assets не исследовались/не изменялись.

## Поддерживаемые режимы и admission requirements

Опыты покрывают исправную ОС, interruptible FIFO, user-space hang, control-plane TOCTOU, responsive PG с lost client knowledge, SIGSTOP backend, nginx trickle/backpressure. Они не сертифицируют даже этот restricted режим для 60/480s. Device D-state, WAL/fsync hang, kernel hang, failover, NIC reset не инъецировались: необходим изолированный device/VM стенд; shared host для таких воздействий не использовался.

Для будущего кандидата: bounded queue/concurrency/object/body sizes; durable epochs, no raw mounts/FD bypass; no replacement до quiescence; authoritative PG timeline; immutable seal; controlled final egress; известные clock/scheduling bounds. Эти условия — **ASSUMPTIONS/requirements**, не измеренные свойства P03. Timeout values в probe — параметры контрпримера. Не назначены произвольные production capacity numbers или timeout budgets для видимости готовности.

Недоступность recovery допускает unknown без replay/purge, только если исходная работа уже завершена. Одновременная потеря всех stop paths остаётся unsupported по A02. Исключение всех storage/PG hangs из failure model для получения PASS недопустимо.

## Классификация evidence

**CONTRACT** — документированная семантика примитива с оговорками, не latency SLA. **MEASURED** — конкретный finite trace. **ASSUMPTION** — обязательство будущего deployment без подтверждения. **NOT ESTABLISHED** — требуемый bound/свойство без доказательства; **NOT RUN** — опыт не выполнен. Ни P99, ни max observed не становятся CONTRACT.

[Storage](L1-G02-STORAGE-EVIDENCE.md) · [PG](L1-G02-POSTGRES-EVIDENCE.md) · [Edge](L1-G02-EDGE-EVIDENCE.md) · [Timing](L1-G02-TIMING-PROOF.md) · [Experiments](L1-G02-EXPERIMENT-RESULTS.md) · [Readiness](L1-G02-IMPLEMENTATION-READINESS.md).
