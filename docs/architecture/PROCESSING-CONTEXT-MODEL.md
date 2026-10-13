# L0.2 — ProcessingContext

**ACCEPTED ORG-G03 (architecture, L0.3); persistence ACCEPTED T05; PD-G02/07/08 REQUIRE LEGAL REVIEW.** Контекст не является уже существующим классом и не создаёт законного основания фактом записи.

| Представление | Оценка |
|---|---|
| Domain entity внутри ProblemWork | Отвергнуто: правовые документы меняются независимо от учебного state machine; дублирование в Submission/Review |
| Value Object на каждом событии | Snapshot полезен, но копирование всего основания/договора создаёт рассогласование и избыточные ПД |
| Только configuration file | Простой старт, но недостаточно аудита версий/сроков/субъектных исключений и безопасного изменения |
| Отдельная compliance-модель с версионированными policy records | Принятый принцип: durable policy вне учебных агрегатов; subject basis/case records только после legal review, вне L1; Application resolver/permission port |

Минимальный policy record-кандидат: ContextId/version, boundaryRef (Organization либо отдельная platform purpose), purposeId, party determining purpose/controllerRef, processorRef/role, instructionRef/version/validity, basis rule/ref, data categories/operations, retention rule/start trigger/hold review, restrictions, correction/block/erasure decision authorities, effective interval, approval evidence/status. Юридические документы и реквизиты доступны по защищённой ссылке. Immutable version не означает бессрочное хранение содержащихся в ней ПД.

Будущий subject basis record из L0.1 (вне L1, только после legal review) получает ContextId/version, OrganizationId при учебной цели, subject/representative evidence, applicable document/version, validity/withdrawal. Не хранить паспорт/договор в work. Один policy регулирует множество работ, subject record подтверждает применимость к человеку. Роли controller/processor указываются по цели; ООО для собственного обучения не заключает поручение само с собой.

## Наследование и разрешение

1. Из ресурса сервер получает root: Review/Submission/Message/annotations → work → assignment → CourseId → Organization. Attached file следует parent; pending upload имеет binding в проверенном scope. Identity/security purpose разрешается отдельно и не наследуется из случайно выбранного курса.
2. Organization задаёт educational policy default. Course binding фиксирует утверждённую версию при вводе курса; отклонение допускается только явно проверенной policy для той же boundary, без молчаливой смены оператора. Нельзя включить analytics ООО как override образовательного договора.
3. На действие выбираются purpose и applicable subject basis; проверяются current validity, поручение, privacy blocks, membership/grants и retention restrictions. Все условия — пересечение, а не «достаточно одной роли». Отсутствующая/неоднозначная/истёкшая policy означает deny обычной операции и case escalation, не автоматическое уничтожение.
4. Для доказательства сохраняется ссылка на применённую версию и время решения в минимальном audit. Исходная версия объясняет прежнюю обработку; текущий block/withdrawal всегда сильнее прежнего разрешения. Обновления курса оформляются новой binding version с effectiveFrom; исторические события не переписываются, журнал периодов позволяет восстановить выбор версии.
5. Lifecycle jobs разрешают основания всех затронутых субъектов/целей, independent holds и полномочие инициатора до формирования manifest. Удержание части данных не разрешает обычное обучение/доступ к остальной части. Новая цель требует нового решения/основания, а не использования старого snapshot.

Не допускаются default global controller, выбор policy по присланному клиентом ID, permissive fallback при недоступном реестре или бесконечный cache policy. Ordinary revoke следует T04/ADR-L15. Current privacy restrictions по T05 нельзя молча задержать на два часа: E06 ACCEPTED (synthetic current checks; real freshness отдельно) в [реестре](../../codex/L03-OWNER-DECISION-REGISTER.md); historical allow не обходит block. Barrier для будущего destructive erasure остаётся отдельной maintenance процедурой.

Операционное хранение в той же БД допустимо, логическая ответственность отделена. Единый resolved immutable VO для одной операции может передаваться между Application/Infrastructure; это не новая сущность Domain. ORG-G03 принят; ProcessingContextVersions, CourseContextBindings и ProcessingRestrictions приняты T05; периоды binding и неизменяемые versions обязательны, детали DDL проходят инженерный review. [Матрица данных](../compliance/PD-CONTROLLER-PROCESSOR-MODEL.md), [ADR-L13](../adr/ADR-L13-processing-context.md).

## L0.3 — детализация реализации

Принятые инварианты не означают реализованные controls. [План L1](../../codex/L03-L1-IMPLEMENTATION-PLAN.md) описывает принятые T01–T06 и будущие use cases/concurrency/tests; E01–E06 ACCEPTED в L0.4; O01 refresh coordination отдельно OPEN; [readiness](../../codex/L03-L1-READINESS.md) отделяет технические решения от правовых gates. Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY.

L0.4 E06 current checks/current-write concurrency и synthetic limits нормативно определены [ADR-L18](../adr/ADR-L18-synthetic-processing-restrictions.md); policy record не доказывает реальное legal basis, real-mode включение вне L1.
