# L0.1 — Personal Data Compliance Architecture: results

**Compliance status: NOT VERIFIED.**

**Pilot readiness: NOT_READY.**

Дата выполнения: 2026-10-09. Documentation-only checkpoint. Анализ/проектирование выполнены; юридическая верификация остаётся открытой, а не считается выполненным compliance. Следующее действие — review владельцем и профильным юристом; L1–L4 не начаты.

## 1. Base / branch / commit

Canonical `/home/tea/github/lms`. До изменений `git status --porcelain=v1` пуст; HEAD и проверенный base совпали: `b1e40841e557179b844965227a1d420c1a82282e`, исходная ветка `codex/l0-architecture`. Создана ровно `codex/l01-personal-data-compliance` непосредственно от этого commit; другие ветки не менялись, дополнительных worktrees/агентов нет.

Result commit — commit, содержащий этот отчёт; точный hash после commit выводится командой `git log -1 --format='%H %s' -- codex/L01-RESULTS.md` и сообщается владельцу. Self-referential hash не записывается внутрь того же commit. Push/merge/deployment, реальные accounts, migrations и production code не выполнялись/не изменялись. Начальный sandbox exec отказал с `mountinfo path is not absolute`; чтение/запись выполнены через одобренные escalated команды в canonical checkout.

## 2. Изученные источники проекта

Обязательные документы исследованы по архитектуре, инвариантам, данным/доступу и актуальным amendments; ранние illustrative schemas отделены от реализации:

- [ExecPlan](EXECPLAN-LMS-SIX-SCREENS.md), [K results](K-RESULTS.md), [K readiness](K-RELEASE-READINESS.md), [K security](K-SECURITY-AUDIT.md), [K gaps](K-MVP-GAPS.md).
- [L0 results](L0-RESULTS.md), [decision register](L-DECISION-REGISTER.md), [architecture](L-ARCHITECTURE.md), [implementation plan](L-IMPLEMENTATION-PLAN.md).
- [ADR-L01](../docs/adr/ADR-L01-identity-integration.md), [L02](../docs/adr/ADR-L02-account-provisioning.md), [L03](../docs/adr/ADR-L03-vps-deployment.md), [L04](../docs/adr/ADR-L04-https-secrets.md), [L05](../docs/adr/ADR-L05-backup-restore.md), [L06](../docs/adr/ADR-L06-resource-protection.md), [L07](../docs/adr/ADR-L07-device-browser-scope.md), [L08](../docs/adr/ADR-L08-operations-privacy.md).
- [Domain](../docs/domain/LMS-DOMAIN-SPEC.md), [Feature](../docs/features/FEATURE-SUBMISSION-REVIEW.md), [Frontend contract](../docs/ui/LMS-FRONTEND-CONTRACT.md), [UI flow](../docs/ui/LMS-UI-FLOW-SPEC.md), [README](../README.md).

Код: Domain History/ProblemAssignment/ProblemWork; Application Ports/ProblemWorkService; Infrastructure LmsDbContext/AccessFacts/PostgresAccessPolicy, StoredAttachment/AttachmentService и migrations immutable/claiming; API Program/claims boundary и controllers через архитектурный inventory; Web client/browser adapter/Blob lifecycle/storage; Docker Compose/nginx; существующие tests/исторические K evidence. Реальный сервер/провайдерские snapshots/данные пользователей не обследовались. Новых tables/classes не создано; предложенные имена явно PROPOSAL.

## 3. Нормативные требования: результат проверки

**Подтверждённых актуальных редакций на 09.10.2026 нет. LEGAL SOURCE NOT VERIFIED.** Официальные страницы 152-ФЗ, 23-ФЗ, 156-ФЗ, РКН/уведомления и ФСТЭК возвращали Timeout/Internal Error. Поисковые выдержки не заменены на «официальное подтверждение». Реестр [PD-LEGAL-REQUIREMENTS](../docs/compliance/PD-LEGAL-REQUIREMENTS.md) содержит журнал S01–S10, нормы/пункты, кандидаты дат, applicability, owner/evidence и R01–R20. Точные действующие редакции/сроки, включая возможные изменения 2026, должны быть проверены юристом. Проекты/старые режимы не применены автоматически.

Охвачены 152-ФЗ, 23-ФЗ/локализация, 156-ФЗ/отдельное согласие, ПП 1119, применимость ФСТЭК 21/заменяющих актов, РКН 178/179/180/187/140, условные ФСБ/СКЗИ/инциденты, ГК/СК для несовершеннолетних. Это перечень проверки и проект требований, не заявление об исчерпывающей применимости всех актов.

## 4. Инвентаризация данных

[PD-DATA-INVENTORY](../docs/compliance/PD-DATA-INVENTORY.md): 15 категорий с purpose/basis/collection/storage/access/retention/correction/deletion/transfer/evidence. FACT: Identity/profiles отсутствуют; learning связывается UUID; Submissions/Messages содержат Markdown и arrays attachment IDs, Reviews — annotations JSONB; события — JSONB с IDs. StoredAttachment сохраняет filename/owner/time/links; original bytes/EXIF находятся вне PostgreSQL. Pending ExpiresAt сейчас MaxValue; DELETE не уничтожает metadata. Логи/browser/roster/copies тоже в контуре, UUID не анонимизация. ФИО/email/phone/DOB/паспорт не добавлены «для удобства».

## 5. Модель оператора

D4 **ACCEPTED**: российское ООО владельца; реквизиты UNKNOWN. D5 **ACCEPTED**: compliance до первого real-data пилота — обязательный gate. [Основания/роли](../docs/compliance/PD-PROCESSING-BASES.md) отличают субъектов/представителей, сотрудников, внешних учителей, admins, VPS/backup providers; поручение определяется реальными операциями и договором, не названием участника. Future schools требуют нового legal/isolation решения без multi-tenant реализации сейчас.

## 6. Несовершеннолетние

Возрастные/дееспособные группы и стороны договора неизвестны. Нельзя принять универсальное «родительский checkbox достаточно» или собрать паспорт автоматически. PD-G03 требует юриста и минимального подтверждения полномочий; proposed evidence versioned/отдельно от terms; отзыв/пересмотр полномочий обязателен по применимой модели. Письменную форму, если нужна, нельзя заменить урезанной web формой.

## 7. Правовые основания

B01–B12 разделяют accounts/зачисление/работы/фото/проверку/историю/прогресс/logs/backup/уведомления/будущий AI. Кандидаты: необходимое исполнение договора, конкретная законная обязанность, отдельное согласие, обоснованный законный интерес там, где допустим. Ни один кандидат не считается установленным без PD-G02. Backup/immutable history не самостоятельное бессрочное основание.

## 8–9. Data flows и локализация

[PD-DATA-FLOW-MAP](../docs/compliance/PD-DATA-FLOW-MAP.md): Mermaid DFD, F01–F08 и checklist всех внешних зависимостей. Рекомендация — управляемый контур/копии/logs/keys в РФ, без необязательного SaaS/SMTP/CDN/AI. Реальные адреса UNKNOWN. Иностранный сервис не объявлен автоматически запрещённым; основной PostgreSQL в РФ недостаточен. Отдельно анализ ст. 12 и ст. 18 ч. 5, remote support/egress/browser/CI.

## 10–12. ИСПДн, угрозы и защита

[Threat model](../docs/compliance/PD-THREAT-MODEL.md): методика границ/категорий/counts/types/уровня; УЗ **UNKNOWN**, PD-G06. 18 сценариев включают scope, фото, VPS/backup/logs, CSRF/session/brute force, admin/misconfiguration, race, availability, restore и ложного представителя. [Security controls](../docs/compliance/PD-SECURITY-CONTROLS.md): 14 групп мер с нормативными R, угрозами T, компонентами, evidence и статусом. MFA/шифрование/сертификация оцениваются по угрозам/применимости, а не объявлены универсально достаточными.

## 13–15. Lifecycle, immutability и backup

[Lifecycle](../docs/compliance/PD-LIFECYCLE-AND-ERASURE.md), [ADR-L09](../docs/adr/ADR-L09-personal-data-lifecycle.md), [ADR-L10](../docs/adr/ADR-L10-erasure-and-immutable-history.md). PROPOSAL: purpose/basis/case boundary отдельно от auth/enrollment; block закрывает и Teacher whole-course reads. Различаются disable/revoke/block/erase/anonymize/destroy. Сроки R10–R16 кандидатные до официальной сверки.

Рекомендуемый вариант D: независимая Identity + отдельное privileged уничтожение по approved manifest; обезличивание только с доказательствами. Обычные immutable commands/Accepted сохраняются; исключение storage triggers требует принятого ADR и будущего privilege-safe design. Учтены mixed-subject work/teacher deletion, files/EXIF/Markdown/annotations/DomainEvents/metadata; удаление только IdentityUser недостаточно.

Backup: отдельный актуальный erasure/revoke ledger вне старого snapshot, quarantine restore, fail closed при его отсутствии, reconciliation intents/copies/session/access до cutover. SQL DELETE/FS unlink/encryption сами не доказательство уничтожения всех media/keys/provider copies. RPO/RTO/retention UNKNOWN. Восстановление само обработка и требует основания; карантин не юридическая индульгенция.

## 16–17. Организационные обязанности и РКН

[Организационные документы](../docs/compliance/PD-ORGANIZATIONAL-DOCUMENTS.md): requirement→role→document→mechanism→evidence; A публикация, B внутреннее, C договоры, D технические регламенты, E юридическая проверка. Шаблоны политики/согласия/приказа/case/акта/поручения/runbook — DRAFT, не подписаны. Checklist уведомления до начала, изменения/прекращения, отдельной трансграничной передачи и инцидентов (кандидаты 24/72 от одного момента выявления); роль/заместитель и квитанции обязательны. Ничего не подавалось от имени ООО.

## 18. Изменения L1–L4

[План](L-IMPLEMENTATION-PLAN.md) расширен: L1 Identity & Personal Data Lifecycle; L2 Resource Protection & Data Lifecycle; L3 Secure Deployment & Recovery; L4 Compliance & Pilot Acceptance. В L1 не начинать basis/consent workflows без согласованных оснований/ADR. L2 exception erasure не смешивается с orphan cleanup; L3 проверяет localization/copies/keys/incident operations; L4 требует R01–R20 и organizational/legal evidence. Старые оценки L0 не являются оценкой расширенного scope. Production implementation не начата.

## 19–20. Открытые решения и минимальные вопросы владельцу

Все **PD-G01–14 OPEN**, с owner/recommendation/alternatives/dependencies/evidence/status в [L01-DECISION-GATES](L01-DECISION-GATES.md). D4/D5 закрыты ACCEPTED. Для нового разрешения L1 достаточно ответить/согласовать четыре пакета:

1. C01/C02: закрытая выдача через уполномоченного оператора и одноразовый activation/reset без обязательного SMTP — какой проверенный канал и issuer?
2. G02/G03: возрастные группы учеников, стороны договора/модель услуги и статус преподавателей; кто с юристом утверждает основания и минимальное представительство?
3. G04: какие имена/roster/контакты действительно нужны, нужно ли хранить EXIF исходного фото; принять field allowlist без обязательного паспорта/DOB/телефона?
4. G07/G08/G10: принять ADR-L09/L10 и границу privileged erasure, роль ответственного/reviewer, принципы retention/holds и обработки смешанной истории?

Это уточнения оставшихся решений, не повторный выбор ООО или обязательности закона. Численные storage/backup/RPO значения и реквизиты публикации закрываются перед зависимой реализацией/real data. Отдельное разрешение L1 всё равно требуется; synthetic разработка не равна разрешению пилота.

## 21. Что проверить юристу

Официальные действующие редакции/даты каждого R, замену ФСТЭК актов и режим обезличивания; точные сроки/исключения; договорную модель/273-ФЗ/кадровые обязанности; несовершеннолетних/дееспособность/представительство/форму согласия; минимизацию EXIF; роли и поручения providers; локализацию/трансграничность всех потоков; retention/mixed-subject/hold/backup destruction и подтверждения; уведомления/каналы/инциденты. Результат — конкретное заключение по системе и evidence, а не общий сертификат «152-ФЗ compliant».

## 22. Remaining risks и проверка результата

Основные риски: неподтверждённые нормы на текущую дату, неизвестный УЗ/providers/retention, минимизация исходных фото, утечки через free text/roster/logs, незаконное остаточное хранение backups, возврат прав/ПД при restore, ложное уничтожение по tombstone, смешанные субъекты, избыточная выдача представителю, неподписанные документы/неподанные уведомления; прежние K deployment/resource/device blockers остаются.

Проверка документации: полнота 13 deliverables/14 gates/R01–R20, cross-links, приоритет amendments и сохранение C–L0, отсутствие production изменений/выдуманных реквизитов/ложных PASS; `git diff --check`. Проверка состава выполнена: 13/13 новых deliverables, 14/14 PD gates, всего 24 изменённых/новых Markdown-файла; 274 локальные ссылки разрешаются, ошибок нет. Исторические K/L0 отчёты не изменены. `git diff --check` без замечаний; staged diff проверяется перед commit. Приложение не тестировалось заново: **366 frontend / 260 backend — только исторический baseline**, новых PASS нет. После commit проверяется чистота дерева и parent/base.

**Остановка после документации/commit. Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY.**
