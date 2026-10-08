# ADR-L09 — Personal data lifecycle boundary

Дата 2026-10-09. **PROPOSAL; DECISION REQUIRED PD-G02/03/04/07/08.** D4/D5 ACCEPTED владельцем: российское ООО, соблюдение применимых требований до первого real-data пилота. Не юридическое заключение: [правовые источники](../compliance/PD-LEGAL-REQUIREMENTS.md) NOT VERIFIED.

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

L1 evidence: невозможность активации без утверждённого основания, версии/отзыв/представитель, whole-course Teacher deny на blocked work, session revoke, срок case, scoped export, synthetic возрастные сценарии. L2 evidence: race-safe erasure и retention. L3: restore/ledger. L4: документы/юрист/оператор плюс E2E. Детали [lifecycle](../compliance/PD-LIFECYCLE-AND-ERASURE.md), [ADR-L10](ADR-L10-erasure-and-immutable-history.md).

Решение принимает владелец после правового review; пока не принято и ничего не реализовано. Альтернатива ручных процедур допустима лишь при доказанной полноте, сроках и доступе; «разберёмся после пилота» D5 не допускает.

## L0.2 amendment — несколько операторов

Статус остаётся PROPOSAL / DECISION REQUIRED, не принят автоматически. Исторический D4 уточнён D4a–d. Policy/case/manifest имеют Organization/context/controller scope; decision authority A не распространяется на B или собственные цели P. Identity disable/delete не заменяет purpose-specific erasure. Ordinary immutable workflow сохраняется, privileged erasure отдельно.

[ADR-L13](ADR-L13-processing-context.md) определяет наследование и версии; [организационный lifecycle](../compliance/PD-ORGANIZATION-LIFECYCLE.md) — mixed-subject cases, shared copies и ledger. Scope проверяется до closure и исполнения, чужой subset требует отдельного решения. Принятие границы ORG-G03/06 до L1, реализация и доказательства L2–L4; ни один новый PASS не заявлен.
