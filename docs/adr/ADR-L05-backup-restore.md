# ADR-L05 — Coordinated backup & restore

Статус: **PROPOSAL B06**, **DECISION REQUIRED C04–C05**. [Реестр](../../codex/L-DECISION-REGISTER.md).

## FACT — что нужно сохранить

[LmsDbContext](../../src/Lms.Infrastructure/LmsDbContext.cs): ProblemAssignments/ProblemWorks, Submissions/Reviews/Messages, DomainEvents, AccessEnrollments/AccessLessons/AccessTeachers/AccessAttachments, PublishedContentVersions, StoredAttachments. Ordered AttachmentIds — uuid[]; Reviews.Annotations — JSONB. [Migrations](../../src/Lms.Infrastructure/Migrations) содержат immutable history/content triggers, attachment claiming и tombstones. Полная DB backup сохраняет их и migration history, не только основные таблицы.

[Compose](../../docker-compose.yml) хранит DB и originals в двух volumes. [AttachmentService](../../src/Lms.Infrastructure/AttachmentService.cs) пишет file до metadata, при uncertain commit сохраняет file; delete сначала tombstone, затем file. [LocalFileAttachmentStorage](../../src/Lms.Infrastructure/LocalFileAttachmentStorage.cs) делает temp→random key rename, без общей ACID transaction с PostgreSQL. [K security S05/S06](../../codex/K-SECURITY-AUDIT.md): orphan risk и coordinated restore не проверены. Restart/down-up K — не restore evidence.

## Альтернативы / PROPOSAL

| Вариант | Consistency | Цена |
|---|---|---|
| Maintenance, остановить всех writers, logical DB dump + file copy | Самый простой проверяемый barrier между хранилищами | Перерыв записи на период локальной копии; **рекомендация**, если C04 допускает |
| Coordinated volume snapshot | Только общий атомарный snapshot нужных volumes и корректный PostgreSQL snapshot protocol | Зависит от неизвестного storage/VPS; два независимых snapshot не согласованы |
| Online + WAL/PITR + immutable-file journal/retention | Привязка files к DB restore point, запрет преждевременного удаления, reconciliation | Дороже разработка/операции; при строгом RPO/downtime requirement |

`pg_dump` даёт согласованное состояние БД, но не внешних файлов ([PostgreSQL 17](https://www.postgresql.org/docs/17/backup-dump.html)). WAL/PITR также не восстанавливает originals автоматически ([PITR](https://www.postgresql.org/docs/17/continuous-archiving.html)). Нельзя копировать live PGDATA обычным tar и называть это безопасным backup.

## Проект backup runbook

1. Проверить staging space, off-host destination и encryption key; назначить backup set ID. Global operation lock исключает deploy/migrate/provision/cleanup/другой backup.
2. Edge maintenance блокирует новые запросы; drain upload/commands, остановить API/workers, запретить operator writes. PostgreSQL остаётся доступной backup job. Проверить отсутствие writers, не просто сменить UI banner. Лимит drain/окно согласовать C04; при сомнении backup FAIL.
3. Logical dump всей LMS DB matching pg_dump tool; roles/ownership/extensions configuration безопасно (role credentials в encrypted secret bundle либо заново provisioned, не открытый SQL). Скопировать originals, Data Protection keys, защищённую config/secret recovery information и при необходимости proxy state. PublishedContentVersions сохраняет историю; текущий Markdown её не заменяет.
4. При неподвижной системе manifest: UTC barrier/dump/copy times, commit/image digests, migration IDs, DB version, counts, все StorageKey/AttachmentId/size/SHA-256/linkage, files missing/extra/tombstoned/pending, key-ring/config/tool versions. Manifest содержит персональные metadata — тоже шифровать. Все nondeleted StoredAttachments должны иметь оригинал; legacy history ID без StoredAttachment отдельно перечислить как gap, не молча PASS. Проверить attempts/versions/annotations/exact content refs.
5. Проверить локальный set/checksums, закрыть manifest; только затем разрешить writes. Off-host upload encrypted set может идти после barrier, если локальная копия immutable. Authenticated encryption стандартным инструментом, unique set names, проверка удалённой целостности. Checksum без authenticity не защищает от подмены.
6. Success только после подтверждённого off-host полного set/manifest. Failure → alert, last successful backup age не обновлять. Retention удаляет только целые sets по C04/C05/C08, не последнюю проверенную копию. Encryption secret не хранить единственной копией рядом с ciphertext или на VPS.

Cleanup — writer, всегда остановлен на barrier. Unreferenced файлы допустимо включить как quarantined extras, не удалять во время backup. Длительность copy большого dataset измерить и при необходимости пересмотреть C04/online альтернативу; короткое окно заранее не обещано.

## Restore drill на чистом сервере (L3 rehearsal, L4 acceptance)

1. Выбрать завершённый off-host set; чистый isolated host/volumes с matching app/tools, без public DNS/обычных пользователей. Замер с incident declaration включает provisioning/download/decrypt/validation, не только pg_restore.
2. Получить decryption material по escrow с назначенным оператором; проверить authenticity/manifest/checksums. Missing key/partial set → FAIL. Исходные backup не перезаписывать.
3. Восстановить DB (schema/triggers/data/migration history), originals под теми же storage keys, Data Protection keys + unwrap material, config/permissions. Новые migrations — отдельный tested upgrade после восстановления исходной версии. До public traffic отозвать все restored sessions/reset challenges и проверить roster/grants: backup мог предшествовать security revoke. Нужен защищённый журнал изменений доступа вне потерянного host либо ручная re-approval всех accounts.
4. Сравнить row counts/FKs/versions/content hashes/ordered attachment references и SHA-256 всех originals; missing linked files недопустимы. Проверить immutable Reviews/annotations и обе попытки через UI, не только SQL import.
5. Настоящие Identity accounts: sign-in, negative scopes, protected downloads, overlays, Submit→Revise→Accept на отдельном новом test assignment. Проверить backup восстановленной среды. Не запускать destructive K release-check.sh на восстановленных реальных данных.
6. Зафиксировать measured RTO и давность последнего off-host backup (RPO exposure), evidence/исполнитель/commit. Отдельно corrupt file, missing secret, failed restore. Только operator sign-off разрешает cutover/доступ, не green health.

## DECISION REQUIRED — варианты, не принятые цели

| Вариант | Возможные цели для обсуждения | Последствия/относительная стоимость |
|---|---|---|
| Экономный | Например RPO до суток, RTO до рабочего дня | Daily coordinated copy, доступный оператор; можно потерять день работ; maintenance |
| Учебный день | Например RPO несколько часов, RTO несколько часов | Чаще backups, быстрее download, response/drill; больше IO/storage/окон |
| Строгий | Например RPO минуты, RTO около часа | WAL/PITR + files, подготовленная recovery среда/дежурство; существенно сложнее |

Числа — иллюстрации C04, не SLA/конфигурация/обещание. Владелец задаёт допустимую потерю работ, простой и backup window; drill доказывает достижимость. C05: off-host location/юрисдикция, стоимость/ёмкость/retention, доступ и хранитель ключа. Всё это UNKNOWN. Если требования не подтверждены измерением — NOT_READY.
