# G02/A03 — Experiment results

2026-10-09. **INFRASTRUCTURE EVIDENCE BLOCKED · T04 NOT PROVEN · S4 BLOCKED.**

## Provenance, execution и oracle

Synthetic fixtures only, 2026-10-09; canonical repository HEAD `a60ee6c73c4400bd1bdf6c6df434358372745c41`, ветка `codex/l1-s4-jwt-api-authorization`. Standalone Python 3.14.4 + pinned local PG/nginx images, никаких LMS connections/migrations/Compose запусков. [Environment](evidence/L1-G02-A03/environment.json), [harness](evidence/L1-G02-A03/probe.py), [results](evidence/L1-G02-A03/results.json), [PG log](evidence/L1-G02-A03/pg.log), [nginx log](evidence/L1-G02-A03/nginx.log), [полный успешный run](evidence/L1-G02-A03/attempt-4.log).

Контроллер использует monotonic timestamps; PID/wchan для child и отдельное PG соединение для oracle. «Независимый» oracle здесь отделён от проверяемого worker/client, **не независим от host kernel/PG storage**. Absolute clock uncertainty не калибрована; результаты не являются T04 trace по всем требованиям V2/V3. Operation identity — synthetic names `lost/rollback/stopped`, generation=1 в PG, generation/epoch 1→2 в TOCTOU. Native/edge probes — отдельные процессы/соединения с ID строки trace, не gateway protocol. Device mutation time и last NIC egress неизвестны; timestamp observer — время наблюдения, не нижнего события.

Все локальные assertions финального run прошли. PASS означает воспроизведение заявленного ограниченного факта/контрпримера, **не PASS инвариантов deployment**.

## Проверяемые утверждения

| ID / A02 coverage | Условия и fault injection | Наблюдение / независимый oracle | Статус и недопустимый вывод |
|---|---|---|---|
| fifo-open / F02 | tmpfs FIFO без writer; child входит native open | `/proc/pid/wchan=wait_for_partner`, child pending, kill + waitpid exit=-9 | PASS probe; не D-state/device stop proof |
| fifo-read / F02 | FIFO с удерживаемым peer FD, нет bytes | anon_pipe_read, kill + waitpid exit=-9 | PASS probe; не completion произвольного disk read |
| fifo-write / F02 | FIFO peer не читает, pipe заполнен | anon_pipe_write, kill + waitpid exit=-9 | PASS probe; не disk flush/cancel bound |
| userspace-hang / F01 | Busy loop в отдельном PID | Parent получил readiness, kill/reap exit=-9 | PASS probe; не scheduler upper bound/independent kernel supervisor |
| generation-toctou / F04 | Pause после epoch check/open, controller меняет epoch, resume write+fsync | Controller прочитал 4 bytes `late`, hash и порядок событий в trace | COUNTEREXAMPLE I4 для surrogate; не реализация C, не запись после process exit |
| retained-fd-seal / F19 partial | Открытый writable FD, rename и chmod 0400 | Новое содержимое `EDIT`/hash после write через старый FD | COUNTEREXAMPLE seal surrogate; не durable immutable storage |
| pg-lost-ack / F06,F08 partial | Real PG transaction; отправить COMMIT, не читать ответ | Второе соединение: сначала zero effects при uncommitted writer, затем Committed; row lock resolver дважды видит один effect | PASS observation; не packet-drop network fault, не WAL failure; absence не rollback |
| pg-rollback / F05 partial | Insert + явный ROLLBACK с ответом | Fresh primary count=0 | PASS healthy rollback; lost rollback ACK NOT RUN |
| pg-terminate-signal / F07 surrogate | SIGSTOP одного fixture backend, pg_terminate_backend(pid, default 0) | signal=true и backend ещё в pg_stat_activity; после CONT исчезает, effect отсутствует | COUNTEREXAMPLE signal=exit; НЕ WAL/fsync hang |
| pg-clean-restart / F09 partial | Clean docker restart, тот же private volume | Fresh primary только effect `lost` | PASS finite history observation; не crash/power loss/failover safety |
| tcp-close-buffer / F14 partial | Sender sendall 64KiB, close; receiver ждёт 100ms | Receiver получил 65536 bytes после sender close | PASS counterexample close=receive completion; не доказательство late NIC send |
| nginx-trickle / F14 partial | nc upstream 10 bytes с интервалом 200ms, buffering off, inactivity=1s | Client body=10 bytes, elapsed=1.650587s | COUNTEREXAMPLE total deadline=inactivity timer; не 60/480s acceptance |
| nginx-buffered-cutoff / F14 partial | buffering on, 128KiB upstream; client не читает 300ms; kill own nginx container | После kill клиент получил 131193 bytes (HTTP headers+131072 body), EOF; nginx access log body=131072 | PASS buffered delivery observation; physical queue location/cutoff NOT ESTABLISHED |

Финальные observed kill/reap: open 1.155493ms, read 1.163885ms, write 1.134429ms, busy-loop 1.114756ms. Это один sample каждого; не max population, не WCET. TOCTOU storage hash и PG system identifier/timeline сохранены в JSON. Значения `observed_mutation_ns` являются временем read oracle после worker exit, а не временем device write.

## Воспроизведение и cleanup

Из каталога репозитория (нужны те же локальные image IDs и Docker; никаких pull автоматически):

```sh
python3 codex/evidence/L1-G02-A03/probe.py /tmp/lms-g02-a03-recheck
```

Скрипт создаёт private temp FIFO и disposable containers с label `lms.g02=a03-synthetic`; PG получает новый anonymous volume и random loopback port. В finally удаляются только созданные container IDs с их anonymous volumes; temporary files удаляются TemporaryDirectory. На финальном run оба контейнера удалены; существующие LMS volumes/services не трогались. Скрипт предназначен для trusted synthetic localhost среды и использует PG trust только в ней. На аварийное завершение самого controller/SIGKILL Python finally гарантии нет: IDs напечатаны/label позволяет оператору проверить остатки, это не lifecycle implementation.

Attempt 1: readiness принял временный initdb unix-socket server; TCP connect получил EOF, контейнер удалён. Partial trace сохранён; этот запуск не counted as full PASS. Attempt 2: начальный readiness исправлен на TCP, после restart Docker переназначил random host port; старый port дал ConnectionRefused; cleanup выполнен. [Attempt 2](evidence/L1-G02-A03/attempt-2.log) сохраняет ошибку. Исправление — перечитать docker port после restart. [Attempt 3](evidence/L1-G02-A03/attempt-3.log) успешно прошёл прежний набор; attempt 4 добавил buffering/cutoff и прошёл полностью. Эти ошибки — ограничения harness, не симптомы PG durability.

## Невыполненные обязательства

| A02 fault matrix | A03 статус |
|---|---|
| F01/F02/F04 | Частичные реальные OS surrogates; delete/rename/flush hang и D-state NOT RUN |
| F03 stale gateway/restart | NOT RUN: gateway отсутствует |
| F05/F06/F08 | Ограниченные PG paths выше; unknown rollback, replica lag, concurrent production resolver NOT RUN |
| F07 | Только SIGSTOP signal semantics; WAL/fsync/device fault NOT RUN |
| F09 | Только clean PG restart; crash между всеми durable boundaries NOT RUN |
| F10/F11/F12/F13 | Concurrent idempotency/claim-delete-recovery/storage corruption/failover NOT RUN |
| F14 | Trickle/buffer/close surrogates; full API/TLS/Range/slow upload/physical NIC cutoff NOT RUN |
| F15/F16 | Near-exp/D, clock faults, ordinary revoke integration NOT RUN |
| F17/F18 | Recovery outage, supervisor restart/capacity и combined failures NOT RUN |
| F19 | Retained-FD negative case; security no-bypass audit NOT VERIFIED |
| F20 | TOCTOU late continuation surrogate; full expired-command recovery NOT RUN |

Нет device-level instrumentation и независимого fence appliance; разрушительные hangs shared host не запускались. Это явные evidence gaps, а не silently skipped PASS. Real-time 60/480s full operation tests и backend regression не запускались: runtime/test source не менялся, новый C не реализован, V1 уже BLOCKED. Исторические 355/355 из S4 не присваиваются A03. Security/DDD review — самостоятельная проверка документации/ограничений, не независимое review и не V3 acceptance.
