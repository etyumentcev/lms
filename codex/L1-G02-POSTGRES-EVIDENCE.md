# G02/A03 — PostgreSQL evidence

2026-10-09. **INFRASTRUCTURE EVIDENCE BLOCKED · T04 NOT PROVEN · S4 BLOCKED.**

## Четыре разных события

| Событие | Что позволяет утверждать | Чего не доказывает |
|---|---|---|
| Клиент перестал ждать/закрыл socket | Клиент не получил outcome | Rollback, exit backend, fsync completion |
| Серверная transaction закончена | Подтверждён COMMIT/ROLLBACK в авторитетной истории и исключён поздний writer | Долговечность при dishonest device flush или сохранность после failover |
| Durable результат | WAL flush при synchronous_commit=on/fsync=on и честном storage | Доступность или finite recovery latency |
| Outcome восстановлен | Сериализованное чтение той же identity на authoritative primary | Разрешение повторить mutation или продолжить старый request после deadline |

P03: PG 17.6 image pin в [profile](L1-G02-INFRASTRUCTURE-PROFILE.md), `fsync=on`, `synchronous_commit=on`, `full_page_writes=on`, `wal_sync_method=fdatasync`, single primary, volume на host ext4. Runtime LMS и его schema не затронуты. В fixture две synthetic таблицы: durable operation identity и unique effect, изменение в одной transaction. Trust authentication применяется только к одноразовому стенду с loopback портом; это не deployment security recommendation.

## Контракты

[PG WAL settings](https://www.postgresql.org/docs/17/runtime-config-wal.html) определяют sync commit/flush policy. [WAL reliability](https://www.postgresql.org/docs/17/wal-reliability.html) связывает сохранность с корректным storage stack. Отключение fsync/synchronous_commit ради скорости не принято. В этом profile нет проверенного device power-loss contract.

[Client timeouts](https://www.postgresql.org/docs/17/runtime-config-client.html) задают statement/transaction/lock timers; это не предельное время прекращения произвольного WAL/fsync hang. [pg_terminate_backend](https://www.postgresql.org/docs/17/functions-admin.html) при timeout=0 сообщает доставку сигнала, при положительном timeout ждёт termination и может вернуть false. Это API наблюдения, не обещание остановки в указанное время при любом отказе.

[PG failover documentation](https://www.postgresql.org/docs/17/warm-standby-failover.html) требует предотвращать возврат старого primary как writer (STONITH). P03 вообще не включает HA: запрет promotion безопаснее недоказанного failover, но не даёт availability/recovery bound. Fencing старого primary, отсутствие потери acknowledged history между timelines NOT VERIFIED.

## Реальные опыты и authoritative oracle

[results.json](evidence/L1-G02-A03/results.json) и [pg.log](evidence/L1-G02-A03/pg.log):

1. Identity `lost` durable до transaction; transaction берёт row lock, inserts unique effect, updates state. До COMMIT отдельное соединение видит count=0 — **не доказательство rollback**.
2. Клиент отправляет COMMIT и намеренно никогда не читает ответ. Отдельный observer видит Committed; исходный client socket закрывается. Это loss of acknowledgement **на границе клиентского приложения**, не packet-drop proxy и не симуляция WAL failure. Нельзя утверждать, что сервер не послал ACK: он мог попасть в socket buffer.
3. Resolver дважды выполняет fresh transaction + SELECT FOR UPDATE по уже существующей identity на том же primary и читает ровно один effect. Ни одна domain command не replayed. Не проверялись полный idempotency digest conflict/security/concurrency в LMS.
4. Явный ROLLBACK подтверждён, observer видит zero effects. Отдельно backend с незавершённым insert остановлен SIGSTOP: `pg_terminate_backend(pid)` вернул true при присутствующем backend. После SIGCONT наблюдалось исчезновение backend, effect отсутствовал. SIGSTOP моделирует отсутствие исполнения, **не D-state или WAL/fsync**.
5. Clean container restart сохранил только committed `lost`. Это MEASURED восстановление после clean shutdown с тем же volume, не power loss, crash-WAL replay, primary failover или универсальная durability гарантия.

Authoritative source для P03 — **тот же единственный PG cluster/system identifier, timeline 1, primary (pg_is_in_recovery=false), serialized operation row и effect в fresh transaction**. Identifiers recorded в trace. Для отсутствующей operation row необходим отдельный durable idempotency binding/unique serialization и доказательство конца старого writer; простого SELECT-not-found недостаточно. После failover authoritative source существует только при проверенных old-primary fence и сохранённой WAL history. Stale replica не oracle.

## Recovery, unknown и бюджет

Unknown не означает aborted. Сохранить bytes private, deny publication/claim, не purge и не replay. Получить lock/подтверждение результата в пределах P/R можно только при конечных DB/lock/network bounds. При недоступной БД unknown может сохраняться после B, **если исходные исполнители уже доказанно закончены**. Позднее status-read не делает новую mutation; поздний commit/возобновление expired команды остаётся нарушением I6.

WAL/fsync hang, rollback под device failure, backend kill во время COMMIT, lost ROLLBACK ACK, crash recovery и failover NOT RUN; контракты P/X/R NOT ESTABLISHED. Убивать PG backend SIGKILL не предлагается как безопасный rollback protocol: это может потребовать общей crash recovery. Независимый supervisor/PG persistence fence отсутствует. I1/I5 имеют только ограниченный prototype evidence; I6/I7 не доказаны.
