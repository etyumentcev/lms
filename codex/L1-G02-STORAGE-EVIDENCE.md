# G02/A03 — Storage evidence

2026-10-09. **INFRASTRUCTURE EVIDENCE BLOCKED · T04 NOT PROVEN · S4 BLOCKED.**

## Контракт и ключевой ответ

**Да, на выбранном POSIX profile нельзя исключить физическое изменение storage после объявления cancel/completed/fence.** Контрпример A03 показывает запись после ложного control-plane fence (worker ещё жив). Это НЕ наблюдение поздней device записи после observed process exit. Для уже выданного kernel/device I/O после остановки worker нижний уровень не инструментирован, quiescence contract отсутствует; утверждение «больше не изменится» NOT ESTABLISHED.

[Linux completion documentation](https://docs.kernel.org/scheduler/completion.html) описывает default uninterruptible wait без timeout и отдельные killable/interruptible варианты. Это не утверждение, что каждый файловый syscall игнорирует SIGKILL; это основание не выводить универсальный finite termination bound из доставки сигнала.

[close(2)](https://man7.org/linux/man-pages/man2/close.2.html) не гарантирует физический flush; blocking I/O в другом потоке может удерживать underlying open-file description. [fsync(2)](https://man7.org/linux/man-pages/man2/fsync.2.html) задаёт flush semantics и необходимость отдельного directory fsync для entry durability, но не worst-case SLA при device hang. Следствие для P03: конечный X/S не доказан.

## Матрица операций

| Операция / риск | Evidence | Вывод и остающееся обязательство |
|---|---|---|
| Blocking open | Реальный FIFO open, `/proc/pid/wchan=wait_for_partner`, SIGKILL/waitpid | MEASURED interruptible exit; не bound дискового open/stat |
| Blocking read/write | Реальный FIFO read/full pipe write, anon_pipe_read/anon_pipe_write | MEASURED kill/reap; не disk fsync/uninterruptible I/O |
| Blocking delete/rename/flush | Анализ POSIX boundary; device fault NOT RUN | Нет отмены уже допущенного unlink/rename через PG token; нужен actual-mutation fence/quiescence для exact version |
| User-space hang | Busy worker kill/reap | Изоляция полезна при работающем scheduler; whole kernel hang остаётся общим fault domain |
| Неотменяемый native I/O / outstanding kernel I/O | Контрактные ограничения; device instrumentation NOT RUN | Отсутствует upper bound или external stop contract; container kill недостаточен |
| Поздняя запись после объявленного fence | Worker проверил epoch=1, controller записал epoch=2, worker продолжил write/fsync | MEASURED контрпример I4 для check-then-write; private orphan тоже нарушает I6, если работа после B |
| Поздняя запись после observed worker exit | NOT RUN на device boundary | Нет обещания её отсутствия; тишина после exit не является oracle будущего |
| Durable immutable versions | Rename + chmod при retained writable FD; запись изменила «sealed» файл | MEASURED: этот seal surrogate не immutable; hash в results. fsync на tmpfs не durability proof |
| Restart epochs | Epoch-файл меняется только в TOCTOU fixture | Durable monotonic epoch/restart protocol NOT IMPLEMENTED/NOT RUN; не переносить toy epoch на I7 |
| No write bypass | Fixture workers имеют UID/controller permissions; текущий API имеет volume | No-bypass NOT VERIFIED; нет credentials/mount/FD security boundary C |

## Требуемая граница fencing (I4 сохраняется)

Определения I1–I7 остаются в [A02 §4](../docs/architecture/L1-G02-BOUNDED-EXECUTION.md), не заменяются результатами probes. Перенос проверки epoch в API/PG/gateway до POSIX syscall оставляет TOCTOU. Корректный Fence должен сериализоваться с фактической mutation в выполняющем компоненте, закрыть старые команды, дождаться/прекратить выданное I/O, выдать quiescence evidence и только затем ACK/допустить новое поколение. Для physical delete нельзя reuse имён/версий. Ограничение публикации и isolation private objects защищают часть safety, но не физическое завершение T04.

Seal обязан исключить существующие writable handles, подтвердить immutable bytes/hash/length и durable namespace по реальному backend contract. Restart default deny до восстановления монотонного epoch и доказанного fence старых writers. Требуется audit всех write путей: raw mounts, inherited FDs, alternate credentials, cleanup/restore/admin paths. В A03 такой gateway не реализован; гарантия absence bypass не заявляется.

## Недостающее evidence и альтернативы

Недостающие S/X: численный worst-case для open/read/write/flush/delete, для прекращения kernel/controller/device queues, для supervisor dispatch и quiescence ACK. P03 не имеет storage-side atomic epoch enforcement или device SLA. Можно отдельно исследовать backend с контрактными immutable versions, fencing и bounded outstanding I/O, либо appliance с независимым stop boundary и документированными firmware limits. Обычный object-store request timeout, VM power-off, read-only remount или immutable naming без контрактов не решают обязательства. Наличие подходящего продукта здесь не установлено.

[Experiment trace и ограничения](L1-G02-EXPERIMENT-RESULTS.md); вывод ограничен P03, не теорема невозможности любой инфраструктуры.
