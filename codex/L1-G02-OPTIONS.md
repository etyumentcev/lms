# G02/A02 — Варианты изоляции и рекомендация

2026-10-09. **DESIGN ONLY; выбор требует owner decision; S4 BLOCKED.**
[Архитектура](../docs/architecture/L1-G02-BOUNDED-EXECUTION.md) · [Failure model](../docs/architecture/L1-G02-FAILURE-MODEL.md) · [Verification](L1-G02-VERIFICATION-PLAN.md) · [Owner decision](L1-G02-OWNER-DECISION.md).

## Сравнение

| Вариант | Что можно независимо прекратить | Что может продолжаться после остановки | Fencing / commit / recovery | Цена и вывод |
|---|---|---|---|---|
| A. Отдельный worker process на каждую файловую операцию | Managed execution вне API; supervisor может отправить kill, наблюдать exit и закрыть IPC при работающей ОС | Uninterruptible syscall до фактического exit; уже выданные kernel/device writes; отдельный PG backend и edge | Сам процесс не обеспечивает durable epochs, fencing FS или разрешение commit ambiguity; нужна дополнительная граница | Минимальные IPC/операционные затраты. Полезный building block, **недостаточен как решение T04** |
| B. Изолированный container/service | Namespace/cgroup/resources/credentials; stop/kill всех принадлежащих workloads при доступном runtime/kernel; отдельный host уменьшает общий failure domain | Container kill не отменяет issued I/O; same-host kernel общий; remote storage/PG/edge живут независимо; service timeout лишь закрывает клиентское ожидание | Улучшает отсутствие bypass и blast radius. Durable state, storage-side epochs, crash protocol остаются необходимыми | Больше эксплуатации/latency. Контейнеризация существующего адаптера сама по себе не закрывает G02 |
| C. Storage gateway с ограниченным протоколом | Приём команд и публикацию контролирует единая доверенная граница; изолированный I/O можно остановить отдельно; supervisor контролирует session/deadline | Уже допущенная storage mutation и внутренние реплики/flush могут продолжаться без контракта нижнего уровня; gateway crash не равен stop PG/edge | Можно задать exact-version immutable storage, durable operation identity, serial fence ACK после quiescence и replay rules. PG остаётся отдельным atomic/result boundary | Самая высокая стоимость проектирования/проверки. **Рекомендуется как архитектурное направление**, только с проверяемыми storage/PG/edge completion contracts |

Ни один вариант не обещает произвольный OS/device/network failure. A может быть частью C, B — способом deployment C; комбинация не превращает недоказанные примитивы в доказательство. Внешний gateway с обычным client request timeout эквивалентен переносу неизвестности на другой сервер.

## Почему C

Нужны контроль каждой фактической mutation, отсутствие raw mount у API, недоступные старому worker immutable versions, устойчивое operation identity и отдельная публикация через PG. Gateway позволяет выразить и проверить эти требования на ограниченном наборе verbs. Worker process даёт независимость API от native open; отдельный supervisor и controlled edge нужны для общего времени. Рекомендация не утверждает наличие готового продукта, который уже удовлетворяет всему контракту.

Основной tradeoff — отказ в обслуживании и сохранение quarantine при неизвестном исходе вместо повторного исполнения. Операционные затраты: supervisor вне failure domain, PG/storage fence evidence, конечные capacity limits, telemetry и минимальный recovery resolver. Требуется выбрать реальную инфраструктуру; обычный VPS/Compose нельзя объявить hard-real-time средой по результатам коротких тестов.

Не рекомендуются: `Task.Run/WaitAsync` как termination, попытка abort managed thread, compensation без общего deadline, read-token-then-write, silent background continuation, удаление при SELECT-not-found, новый primary без fencing старого, переход на asynchronous durability ради latency, автоматический replay mutation.

## Альтернативы для решения владельца

1. **Принять C как design baseline** и оставить implementation/acceptance закрытыми до evidence gates. Уточнить конкретный storage/PG/edge stack, ограниченную failure model и численные worst-case bounds. Если контракт не реализуем на выбранной платформе, отклонить deployment без изменения T04.
2. Отклонить C и запросить другой ограниченный протокол/инфраструктуру с теми же proof obligations. A/B без дополнительных примитивов не считать достаточной заменой.
3. Остановить G02 как архитектурно заблокированный для имеющейся инфраструктуры. Сохранить S4 BLOCKED, не отключать обязательные файловые use cases под видом успешного S4.

Рекомендация: вариант 1. Это не разрешение начать runtime и не предложение ослабить T04. Неизвестны физические bounds storage/WAL/edge/scheduling; у выбранного кандидата пока есть условный safety argument, но нет deployment-level termination proof.

## Связь с текущим кодом

| Точка baseline | Предлагаемая ответственность после отдельного разрешения |
|---|---|
| `src/Lms.Infrastructure/LocalFileAttachmentStorage.cs` | Заменяемая локальная I/O граница; никакого прямого FS в API |
| `src/Lms.Infrastructure/AttachmentService.cs` | Durable operation/result transaction; убрать недоказанную compensation по отсутствию metadata; сохранить atomic claim/tombstone |
| `src/Lms.Api/AttachmentsController.cs` | Absolute budget от ingress, bounded body и response, новый status/idempotency contract без automatic replay |
| `docker/nginx.conf`, `docker-compose.yml` | Controlled egress и independent supervisor/failure domains; потребуется другой contract, не просто inactivity timeout |
| `tests/Lms.Infrastructure.Tests/AttachmentDeadlineBoundaryTests.cs` | Существующие gap/FIFO probes сохранить как ограниченные evidence, не превратить в T04 acceptance |

Это карта будущих изменений, не спецификация уже сделанных изменений. Ни один из этих runtime/config/test файлов в A02 не изменён.
