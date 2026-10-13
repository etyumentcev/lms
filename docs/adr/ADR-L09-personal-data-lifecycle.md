# ADR-L09 — Personal data lifecycle boundary

Дата 2026-10-09. **ACCEPTED — архитектурные принципы ORG-G03/06**, владелец, явное задание L0.3 §§6/8. Контекстное ограничение обработки и scoped privileged erasure приняты; конкретные процедуры, сроки, основания, исключения и mixed-subject решения **REQUIRE LEGAL REVIEW**. Механизмы ниже **PROPOSED**, не разрешены к production исполнению. [Источники](../compliance/PD-LEGAL-REQUIREMENTS.md): LEGAL SOURCE NOT VERIFIED. Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY.

## Контекст и варианты

[Ports](../../src/Lms.Application/Ports.cs) содержит Actor и учебные policy, но не privacy lifecycle; [AccessFacts](../../src/Lms.Infrastructure/AccessFacts.cs) grants не являются основаниями обработки. Course-wide Teacher читает независимо от student active enrollment. Поэтому account disable и снятие enrollment не обеспечивают блокирование всех данных. Identity пока proposal ADR-L01.

A: всё в IdentityUser/IsActive — просто, но не останавливает чтение учителем, не покрывает цели/representative/retention. B: полный CRM/parent portal/consent product — избыточный сбор и scope. **Рекомендация C:** минимальные отдельные purpose/basis/case records с контролируемым operator workflow, защищённой доказательственной ссылкой и единым privacy boundary для данных; сложный UI не нужен до подтверждённой необходимости.

## Проект решения по слоям

| Слой | Изменение в будущем / контракт |
|---|---|
| Domain | Сохранить состояния/Accepted terminal, неизменяемые Submission/Review/Message/annotations; не вводить PrivacyDeleted в ProblemWorkStatus и Admin в AuthorRole |
| Application | Новые порты-кандидаты `IProcessingPermission`, `IPrivacyCaseService`, basis/case DTO; право по цели проверять для read/command/export/upload, отдельно от IAccessPolicy; expiry/block fail closed |
| Infrastructure | Identity mapping/session из L01; purpose/basis/representative/case records, audit/due jobs/secure document refs; блокирование графа объекта и derivative reads, не только аккаунта |
| API | Cookie+CSRF, минимальная session выдача, internal operator commands с отдельным полномочием; authenticated DSAR не единственный канал; никакого public erasure endpoint |
| Web | Только минимальные поля/информирование/отдельное consent действие при нужном основании; session reset/Blob cleanup; понятное ограничение доступа; не превращать consent в обязательный checkbox всех целей |
| Operations | Внешний доступный канал обращений, reviewer/deadlines, contracts/policy/notification evidence, актуальный ledger и backup reconciliation |

Модель `ProcessingBasisRecord`, версии текстов и представитель описаны в [основаниях](../compliance/PD-PROCESSING-BASES.md); state machine case: Received → Verified → Assessed → Blocked/Executing → VerifiedComplete → Responded, либо мотивированный Rejected/WaitingEvidence. WaitingEvidence не останавливает законный таймер автоматически. Отдельные purpose permissions не создают новые Domain роли и не подменяют enrollment. Физическая schema/имена — проект.

Для in-flight операций: privacy block устанавливается до drain; новая операция после block отклоняется; destructive action только после завершения старых writers/reads, либо общего maintenance barrier для малого пилота. Авторизация запроса до block сама по себе не отменяет исполнение, поэтому нужны тесты гонок. Export чужих данных исключён, но сохранение доказательств/уточнение допускается по отдельному законному процессу.

## Последствия / acceptance

Плюс: основание отделено от роли и credentials, нет избыточного parent profile. Цена: новое техническое состояние, read-path coverage, защищённые реестры/сроки. Необходимость поля/документа проверяется перед schema addition. Existing UUID нельзя автоматически сопоставить человеку.

L1 evidence: версии/current restriction, whole-course Teacher deny на blocked work, session revoke на synthetic fixtures без фиктивного согласия. Основания/представитель/case deadlines/scoped export — отдельный юридически согласованный workflow до real data, не обязательная реализация L1. После legal review — evidence race-safe erasure и retention до пилота; не автоматический scope L1/L2. L3: restore/ledger. L4: документы/юрист/оператор плюс E2E. Детали [lifecycle](../compliance/PD-LIFECYCLE-AND-ERASURE.md), [ADR-L10](ADR-L10-erasure-and-immutable-history.md).

Архитектурное решение принято владельцем в L0.3; процедуры требуют правового review, ничего не реализовано. Альтернатива ручных процедур допустима лишь при доказанной полноте, сроках и доступе; «разберёмся после пилота» D5 не допускает.

## L0.2 amendment — несколько операторов

L0.3 supersedes прежний статус: архитектурные принципы ACCEPTED, юридические процедуры REQUIRE LEGAL REVIEW, технические детали PROPOSED. Исторический D4 уточнён D4a–d. Policy/case/manifest имеют Organization/context/controller scope; decision authority A не распространяется на B или собственные цели P. Identity disable/delete не заменяет purpose-specific erasure. Ordinary immutable workflow сохраняется, privileged erasure отдельно.

[ADR-L13](ADR-L13-processing-context.md) определяет наследование и версии; [организационный lifecycle](../compliance/PD-ORGANIZATION-LIFECYCLE.md) — mixed-subject cases, shared copies и ledger. Scope проверяется до closure и исполнения, чужой subset требует отдельного решения. Граница ORG-G03/06 принята; в L1 только контракты и synthetic restriction checks; юридический workflow после legal review, доказательства до пилота; ни один новый PASS не заявлен.
