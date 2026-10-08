# ADR-L08 — Operations & privacy

Статус: **PROPOSAL B09 / DECISION REQUIRED C08–C09**. [Реестр](../../codex/L-DECISION-REGISTER.md).

## FACT и UNKNOWN

[Domain Spec §25](../domain/LMS-DOMAIN-SPEC.md) запрещает hard-delete истории обычным use case; withdrawal/deactivation её сохраняют. [StoredAttachment migration](../../src/Lms.Infrastructure/Migrations/20261008013249_AttachmentStorage.cs) запрещает удаление metadata и изменение attached originals; [K security](../../codex/K-SECURITY-AUDIT.md) подтверждает private authorized access, но не operating compliance. [K readiness](../../codex/K-RELEASE-READINESS.md) оставляет logging/alerts/recovery непроверенными.

Факт из задания владельца: LMS хранит работы несовершеннолетних. Фото могут включать имя, почерк, фон и EXIF; originals сохраняются побайтно ([U1](../../codex/U1-RESULTS.md)). Содержание конкретных фото и применимая юрисдикция UNKNOWN. Не считать фото математических решений анонимными; privacy не исчерпывается паролями. Юридическое соответствие не оценено и не утверждается.

## PROPOSAL — минимальный операционный реестр

| Процесс | Ответственный (роль, имя UNKNOWN) | Runbook / сигнал / evidence |
|---|---|---|
| Сервер/доступ | Назначенный оператор + заместитель | Inventory OS/images/volumes, SSH least privilege, firewall, release manifest; external health failure доставлен проверенному получателю |
| Backup | Backup operator | Schedule/last-success/age/size/checksums/off-host status; alert при fail/missing; retention по C04/C05/C08 |
| Restore | Restore operator, ключевой хранитель, owner cutover approval | Чистый host drill ADR-L05; доступ к escrow возможен без потерянного VPS; измеренный RTO |
| Monitoring | Operator | Disk/inodes/WAL/log/temp/backups, memory/OOM, CPU/pool, 5xx/429, TLS expiry; thresholds после C06 measurements |
| Обновления | Operator | Dependency/OS patch review, staging regression, migration+backup+rollback plan, planned window; никакого unattended schema upgrade |
| Account/access | Уполномоченный issuer | Verified roster, atomic provisioning, audit role/scopes, deactivation/revoke, периодическая сверка grants; Teacher не оператор автоматически |
| Incident | Incident lead (может совпасть с оператором) | Закрыть доступ/отозвать sessions, сохранить минимальные forensic logs, оценить integrity, восстановить/rotate, задокументировать timeline; кому сообщать решает owner/legal input |
| Retention/requests | Data owner | Разделить pending cleanup, immutable submitted work, identity/profile, logs и backup expiry; проверить законность и согласовать процедуру до действий |

Для малого пилота один человек может совмещать роли, но нужен заместитель/escrow recovery path; имена, доступность, notification channel и ожидаемая реакция — C09. Операционные уведомления не означают новый student notification engine. Секреты/работы не помещать в alert body. Audit использовать technical records/logs вне DomainEvents: текущий event journal описывает учебный workflow, не все действия оператора.

## DECISION REQUIRED — privacy gates до real data

C08 владелец фиксирует: организацию/лицо, определяющее обработку данных; страны пользователей/server/backup/провайдеров; основания и необходимые информирование/согласия для учеников/представителей; минимальный roster/profile и recovery channel; кто видит работы; retention по категориям, порядок запросов на доступ/исправление/удаление и инцидентов. При необходимости привлекается профильный специалист после определения юрисдикции. Здесь нет выбранного закона, сроков legal notification или заявления compliance.

Рекомендуемый минимальный сбор: login, internal mapping/role, минимальное имя или защищённый внешний roster, course/group, работы/feedback; не добавлять birthdate, адрес, parent accounts, analytics/tracking без необходимости. C07 подтверждает, приемлем ли UUID/secure-roster workaround из [K gaps](../../codex/K-MVP-GAPS.md); Identity integration не решает автоматически display names/teacher materials.

Retention варианты: pending upload — короткий согласованный срок; submitted originals+reviews — срок учебного пилота и согласованный архив; logs — ограниченный operational срок; backup — отдельная finite retention с учётом восстановления. Это **предложения без значений**, не политика. Raw originals сохраняются по текущему контракту: EXIF stripping/recompression меняли бы его и не вводятся скрытно. Можно информировать о том, что следует фотографировать только работу; техническое удаление metadata требует отдельного решения и оригинальных-byte требований.

Критический конфликт: privacy deletion request может требовать удаления данных, тогда как обычные Domain use cases и DB triggers защищают историю. Деактивация — не удаление; псевдонимизация Identity имени не удаляет имя на фото/Markdown/backup. L2 cleanup удаляет только технические unclaimed/tombstoned/orphan resources, не accepted history. До реального пилота C08 должен определить процесс для исключительных запросов: отдельное reviewed maintenance/export/erasure решение, включая backup expiry и replay запрета после restore. Не отключать triggers и не обещать immediate complete erasure в L0. Если требования требуют такой реализации до первой работы, это отдельный scoped dependency, L4 заблокирован до её проверки.

## Технические действия, проверки и риски

B09: bounded redacted logs, access audit, operational runbooks и synthetic alert drills можно проектировать без выбора закона. Реальные people data — только после C08/C09 и readiness gates. Секреты, bodies, photos, session/reset tokens не логировать; хранить traceId, route template, status, минимальный operator action/target/time. Audit IDs тоже sensitive; доступ/retention ограничены.

Tests L3/L4: backup failure и low disk вызывают уведомление, оператор выполняет инструкцию; revoke возвращает denial и не уничтожает submissions; logs не содержат synthetic canary secrets; restore не оживляет удалённые/отозванные accounts; retention job не трогает linked originals; fallback operator получает escrow. Риски: неизвестная ответственность, retention forever по умолчанию, silent backup failures, утечка через logs/EXIF/roster, противоречивые обещания удаления. До закрытия owner gates readiness остаётся NOT_READY.
