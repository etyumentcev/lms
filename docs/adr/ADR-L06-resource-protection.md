# ADR-L06 — Resource protection

Статус: **PROPOSAL B07**, продуктовые объёмы/retention **DECISION REQUIRED C06/C08**. [Реестр](../../codex/L-DECISION-REGISTER.md).

## FACT — reuse и gaps

[K-S02/S05](../../codex/K-SECURITY-AUDIT.md) — открытые quota/rate/orphan risks. [AttachmentsController](../../src/Lms.Api/AttachmentsController.cs) ограничивает один файл и multipart envelope MaxBytes+64 KiB. [AttachmentService](../../src/Lms.Infrastructure/AttachmentService.cs) накапливает MemoryStream, делает ToArray для validator; concurrency увеличивает RAM больше размера одного файла. [ImageUploadValidator](../../src/Lms.Infrastructure/ImageUploadValidator.cs) проверяет JPEG/PNG и 60M pixels. MaxBytes default 10 MiB, startup диапазон до 100 MiB. Это existing values, не новые предлагаемые квоты.

[Annotations](../../src/Lms.Domain/Annotations.cs): 20 pages, 2000 strokes, 4096 points/stroke, 100000 points, finite normalized coordinates; [CommandsController](../../src/Lms.Api/CommandsController.cs) review body 4 MiB. [K performance](../../codex/K-PERFORMANCE-REPORT.md): dense payload около 3.77 MB, next-frame p95 73ms; whole history и Lesson N+1 остаются. Queue paginated, history не paginated. Max points не гарантирует, что serialized body влезет в 4 MiB.

StoredAttachment immutable metadata/claim triggers и row-lock delete уже защищают attached originals. Pending AccessAttachments.ExpiresAt выставляется DateTimeOffset.MaxValue. Нет scheduler/ledger/quota/rate limiter. nginx body unlimited. Лимиты отдельного request не ограничивают общую стоимость для authenticated actor.

## PROPOSAL — матрица enforcement

Все имена config ниже — проект, значений L0 не назначает. Technical bounds измеряет инженер; product quotas C06 согласует владелец. Любой отказ до Domain mutation не создаёт work/attempt/review.

| Риск | Enforcement / configuration (PROPOSAL) | Race / invariant / test |
|---|---|---|
| Password guessing/CPU hash DoS | Edge IP/global budget до expensive password hash; API login budget по normalized login и trusted IP, Identity lockout; LoginRate/Window/Lockout/Concurrency | Generic errors без enumeration; account-only lockout не единственная защита от distributed attack. Concurrent attempts ограничены, lockout не bypass через casing; 429 Retry-After; shared school NAT проверить |
| Upload flood/RAM | Edge body/connection/rate bound + API per-actor/global upload permits до чтения multipart, validation и MemoryStream; UploadRate/Burst/ConcurrentUploads | Scope actor только после auth, IP только trusted proxy. Cancellation освобождает permit в finally; неизвестный Content-Length не bypass; finite queue/timeouts, два параллельных upload не удваивают разрешённый budget |
| Per-user storage | Durable PostgreSQL reservation до записи файла; quota ActorStoredBytes/ActorPendingBytes/PendingCount, server-derived ActorId | Атомарное compare-and-reserve под row lock; SUM затем INSERT без lock запрещено. Attached+pending+reserved учитываются, delete не освобождает физический budget до подтверждения unlink |
| Total storage | Global ledger/reservation + Actual free-space guard; TotalStoredBytes/FreeReserveBytes | Фиксированный порядок locks global→actor→attachment; simultaneous actors не oversubscribe. Worker/CLI используют тот же механизм, а не обходят API |
| Pending accumulation | PendingLifetime/GracePeriod/MaxPendingCount; upload metadata конечный ExpiresAt + scheduled sweeper | Cleanup/claim/delete сериализуются теми же row locks. Проверять expiry при transactional claim, не только ранним IAccessPolicy (текущий trigger expiry не проверяет); no successful claim after tombstone |
| Orphan/tmp files | Reconciler сравнивает DB/storage, durable upload reservation/lease, quarantine/retry; OrphanGrace/LeaseTimeout | Возраст файла сам по себе недостаточен: in-flight/uncertain commit не удалять. DB unavailable → cleanup stop. Повторный scan/lease check; linked history never deleted |
| Download flood | API authorization каждого GET как в K + per-actor/IP/global rate/concurrency/bandwidth envelope; DownloadRate/ConcurrentDownloads | Не открывать static volume/CDN. Streaming/cancellation/timeout, no public cache. Revoked scope →404 до content. Повторные/slow downloads ограничены, bytes received ранее не отзываются |
| Annotation/body amplification | Сохранить 4 MiB и Domain limits; edge cap согласован с route, early reject; review concurrency/timeouts | Не уменьшать принятую модель без требования. Max+1/boundaries/JSON precision/cancellation; invalid payload не создаёт partial feedback/review; compression bypass проверить, request compression не включать без bounds |
| Other POST/read abuse | Body/text/attachment-count technical bounds для submit/message и global request concurrency; DB connection pool/command timeout; bounded limiter partitions | Точные thresholds выбрать после payload census, не вводить скрытый max attempts. 429/413 consistent errors; no auto-retry commands; malicious IDs не создают бесконечную cardinality limiter state |
| Disk exhaustion (DB/files/logs/temp/backups) | Host monitoring + reserve guard, logs rotation, ограниченный staging/tmp, backup admission check | Reserve расчёт: максимальные in-flight/temp + WAL/DB growth + logs + recovery space. Fail upload closed до disk full; reads/recovery доступны, если storage здоров. ENOSPC и failed unlink тестировать в isolated small volume |

## Reservation / cleanup алгоритм (проект)

Таблицы-кандидаты `StorageBudgets`, `UploadReservations` (новые, не существующие): bytes nonnegative, уникальный reservation id, actor, storage key, expiry/lease/state, один global budget. Один процесс API достаточно для rate limiter, но durable quota в PostgreSQL нужна даже после restart. Distributed rate limiter не вводить без multi-replica requirement; при replicas aggregate limit обязан остаться общим.

До чтения multipart reserve worst-case MaxBytes и место для temp/envelope, после validation скорректировать actual size; expired reservation не означает, что можно удалить file — подтвердить отсутствие активного writer и committed metadata. Storage.Store → metadata+accounting commit; ambiguous commit сохраняет file и reservation до reconciliation, никогда преждевременно не возвращает budget. Physical deletion failure остаётся в physical accounting даже при tombstone. Reconciler идемпотентен, ограничен batch/time и одним maintenance lease.

На migration L2 scan размеров/metadata/temp/tombstones, первоначальный ledger reconcile с диском; existing attached files защищены, owner quota ниже existing usage блокирует только новые uploads, не стирает историю. Product pending expiry применяется с объявленным переходным grace, не внезапно удаляет все старые uploads. Cleanup снимает eligibility под lock и tombstone, commit, затем unlink/retry. Backup barrier останавливает cleanup/reservation writers (ADR-L05).

**Технически можно обосновать** request parser/cap/concurrency/timeout/free reserve, bounded queues и partition count. **Owner C06/C08**: сколько можно хранить на ученика, общая плановая ёмкость/бюджет, pending срок и retention работ. Нельзя назвать произвольные rate/quota числа безопасными только потому, что они небольшие. L2 tests используют явные маленькие fixture limits, не production defaults. Производственные значения выводятся из C06 workload, legitimate burst (класс одновременно сдаёт), measurements и C03 capacity; invalid/unset required production settings → startup validation failure.

Альтернативы: только nginx limiter проще, но не знает ActorId/DB quota; только application limiter не защищает до multipart buffering и от saturation; отдельный object store/Redis не нужен пока один VPS, новая внешняя зависимость потребует отдельного обоснования. Рекомендуется комбинированный edge + app + DB ledger, файловый adapter сохранить.

## Exit и зависимости

L1 identity даёт trusted actor/login; L2 обеспечивает API controls/ledger/safe cleanup, L3 добавляет edge/host controls; L4 измеряет целый stack. Unit+real PostgreSQL races: параллельные uploads разных users/одного user, cancel/crash/uncertain commit, cleanup vs Submit/Message/Delete, DB outage, quota rebuild, backup lock. Fault injection не считать доказательством power-loss durability. Нельзя закрывать K-S02 только тестом 413, K-S05 — лишь наличием cron.


## L0.1 amendment — 2026-10-09 (приоритет для будущих этапов)

Обычный cleanup сохраняет запрет удаления linked history. Отдельный privileged workflow ADR-L10 обязателен при применимых требованиях прекращения/уничтожения до пилота и не является расширением orphan cleanup. Pending metadata/filename/OwnerId тоже ПД; tombstone не полное уничтожение. Retention/races/audit evidence относятся к PD-G07/08.

Статус изменений **PROPOSAL**, кроме принятых D4/D5. [ADR-L09](ADR-L09-personal-data-lifecycle.md), [ADR-L10](ADR-L10-erasure-and-immutable-history.md), [lifecycle](../compliance/PD-LIFECYCLE-AND-ERASURE.md), [gates](../../codex/L01-DECISION-GATES.md). **Compliance NOT VERIFIED; pilot NOT_READY.**
