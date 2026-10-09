# L0.1 — Границы ИСПДн, классификация и угрозы

**PROPOSAL**, не утверждённая модель угроз. [R08/R09/R17](PD-LEGAL-REQUIREMENTS.md) **LEGAL SOURCE NOT VERIFIED**. Уровень защищённости **UNKNOWN / PD-G06**, не «УЗ-4 по умолчанию». ИБ назначается владельцем.

## Методика классификации

1. Составить акт границ: API/Web, PostgreSQL (Identity будущая), private files/temp, edge, host/container runtime, рабочие места admins, журналы/monitoring, backups/keys, бумажные/внешние rosters. Отделить устройства субъектов и провайдерские компоненты, зафиксировать доверительные границы F01–F08.
2. Определить категории: кандидат — иные ПД учеников/представителей/учителей; специальные/биометрические не планируются, но случайный контент и цель установления личности проверить. Публичность курса не делает данные учащихся общедоступными.
3. Подсчитать субъектов (не только active users), работников и неработников, включая архив/backup, определить применимые пороги ПП 1119. Реальные counts UNKNOWN. Отдельно схема представительства детей и вред: травля, раскрытие работ/успеваемости, контактов/местоположения, ошибочная атрибуция и утрата доступа.
4. Оценить актуальность угроз 1/2/3 типов по критерию недекларированных возможностей системного/прикладного ПО и иных угроз ПП 1119; Internet exposure или open source сами по себе не задают тип. Использовать актуальную методику ФСТЭК (кандидат 05.02.2021), БДУ, архитектуру, нарушителей/сценарии; действительность методики проверить.
5. По сочетанию категории, субъектов/порогов и типа выбрать уровень по п. 9–12 ПП 1119 и конкретные меры по п. 13–17; приложить мотивировку и подпись. Если входа не хватает, оставить gate OPEN, а не выбрать нижний уровень.
6. По действующему акту ФСТЭК выбрать базовый набор, адаптировать к системе, уточнить по угрозам, документировать компенсации/неприменимость. Оценить СКЗИ/оценку соответствия средств. Подтвердить эффективность до ввода, периодичность контроля по проверенному акту.

Входные решения: владельцы активов, число/категории субъектов, роль provider/admin, ОС/images и удалённый доступ, страны, восстановление, availability objectives. Evidence: утверждённые границы/акт уровня/оценка вреда, перечень мер, протоколы и residual-risk acceptance. Отдельный security review не заменяет юридический.

## Рабочие сценарии

Общие нарушители: внешний неаутентифицированный; свой ученик/учитель с лишним интересом; украденная сессия; ошибающийся/злонамеренный issuer/admin; provider support; скомпрометированный dependency. Активы — I01–I15 из инвентаря. Статусы: исходные K проверки — только historical evidence, новые меры не реализованы.

| ID | Угроза / путь / последствие | Мера-кандидат | Evidence / ответственный |
|---|---|---|---|
| T01 | Brute force/credential stuffing/recovery обман → чужой аккаунт | Identity lockout/rate limits, проверка recovery, password validators; MFA privileged по оценке | Replay/enumeration/NAT burst tests; engineer |
| T02 | Teacher broad/null group или чужой grant → чужие работы | Явное whole-course approval, fresh DB scope при JWT issue/refresh, local scope validation и T04 deadline; периодическая сверка | Teacher A/B/group/null tests и roster sign-off; issuer |
| T03 | Student подмена work/file ID → чужие решения | Ownership/enrollment/exact content; attached parent access | K AttachmentApiTests + новые privacy-block cases; engineer |
| T04 | Фото через bucket/URL/EXIF/cache → раскрытие | Private storage, auth stream, metadata policy, no-store | Direct static URL denial, EXIF fixture audit; ИБ |
| T05 | Ошибка выдачи ролей/перепривязка ActorId | Atomic provisioning, уникальный mapping, dry-run, без auto-backfill | Concurrent import/rollback и чужая история; issuer/engineer |
| T06 | Компрометация VPS/container/DB credentials | Patch/hardening/network least privilege/secrets, boundary между app и maintenance | Port scan, privilege tests, image inventory, key rotation; ops |
| T07 | Backup/key theft, ransomware, повреждение копии | Off-host encryption, separate keys, ограниченный доступ, проверка целостности | Clean-host restore, corrupt/missing-key failure; ops |
| T08 | Logs/alerts содержат пароль, photo, SQL/URL identifiers | Минимальные events, redaction, finite retention, доступ ИБ | Canary scan и evidence доступа к sink; ИБ |
| T09 | Cookie CSRF → чужая команда | Antiforgery на всех unsafe методах, same-origin; SameSite дополнительно | Cross-site/missing token JSON/multipart/logout/login; engineer |
| T10 | Session hijack/stale cookie/shared browser | JWT ≤15m, secure browser storage/refresh E02 OPEN, ordinary revoke T04 и emergency E03 отдельно, frontend reset | Logout/reset/deactivate replay, cross-tab/back navigation; engineer |
| T11 | Вредоносная/огромная загрузка, image bomb → CPU/RAM/disk | Signature/format/pixel limits + квоты/concurrency, безопасный decoder policy | Malformed uploads + concurrent resource tests; engineer/ops |
| T12 | Ошибка удаления/гонка с submit или backup → потеря/возврат ПД | Purpose block + drain + manifest, отдельный erasure workflow, idempotency | Concurrent submit/cleanup/erasure/backup и crash injection; engineer |
| T13 | Restore возвращает consent/access/удалённое | Внешний актуальный ledger, quarantine, revoke all sessions, reconciliation barrier | Восстановить snapshot до erasure и доказать deny/absence; ops |
| T14 | Admin злоупотребляет bypass triggers | Отдельная ограниченная роль, approved manifest, audit вне app DB, reviewer | App-role denial, изменение manifest запрещено, review действий; ИБ |
| T15 | Misconfiguration: dev key/seed, public DB, secret в repo | Отдельный production profile, startup checks, config review | Reject demo, secret scan, network tests; ops |
| T16 | DoS/disk full/backup overlap → недоступность/потеря | Admission limits, capacity/alerts, recovery objectives | Peak/ENOSPC/backup-failure drills; ops/owner |
| T17 | Реидентификация «анонимной» статистики по малой группе/времени | Не сохранять индивидуальные строки; suppress small cells, удалить linkage, проверить external knowledge | Reidentification review; ПД/ИБ |
| T18 | Подложный запрос представителя / избыточная выдача | Проверка полномочий, минимальная защищённая выдача, отделить данные других лиц | Synthetic DSAR/foreign-parent negative tests; ПД |

Остаточные риски: уже скачанные работы не отзываются с чужого устройства технически; provider физическое удаление не подтверждается SQL DELETE; privileged admin технически силён; абсолютная недоступность/нулевая реидентификация не обещаются. Для каждого риска требуется конкретный остаточный риск, компенсирующая мера и владелец. Принятие риска не позволяет отказаться от обязательной нормы.

L0.3: T01–T18 в threat references — идентификаторы угроз, не owner decisions T01–T06. Принятые auth решения и открытые E01–E06: [Owner Register](../../codex/L03-OWNER-DECISION-REGISTER.md), [ADR-L15](../adr/ADR-L15-jwt-delayed-revocation.md). Это не evidence реализации или legal verification.
