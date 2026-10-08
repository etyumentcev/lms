# L0.2 — ProcessingContext

**PROPOSED; REQUIRE LEGAL REVIEW; ORG-G03 / PD-G02/07/08 OPEN.** Контекст не является уже существующим классом и не создаёт законного основания фактом записи.

| Представление | Оценка |
|---|---|
| Domain entity внутри ProblemWork | Отвергнуто: правовые документы меняются независимо от учебного state machine; дублирование в Submission/Review |
| Value Object на каждом событии | Snapshot полезен, но копирование всего основания/договора создаёт рассогласование и избыточные ПД |
| Только configuration file | Простой старт, но недостаточно аудита версий/сроков/субъектных исключений и безопасного изменения |
| Отдельная compliance-модель с версионированными policy records | Рекомендация: durable policy + subject basis/case records вне учебных агрегатов; Application resolver/permission port |

Минимальный policy record-кандидат: ContextId/version, boundaryRef (Organization либо отдельная platform purpose), purposeId, party determining purpose/controllerRef, processorRef/role, instructionRef/version/validity, basis rule/ref, data categories/operations, retention rule/start trigger/hold review, restrictions, correction/block/erasure decision authorities, effective interval, approval evidence/status. Юридические документы и реквизиты доступны по защищённой ссылке. Immutable version не означает бессрочное хранение содержащихся в ней ПД.

Subject basis record из L0.1 получает ContextId/version, OrganizationId при учебной цели, subject/representative evidence, applicable document/version, validity/withdrawal. Не хранить паспорт/договор в work. Один policy регулирует множество работ, subject record подтверждает применимость к человеку. Роли controller/processor указываются по цели; ООО для собственного обучения не заключает поручение само с собой.

## Наследование и разрешение

1. Из ресурса сервер получает root: Review/Submission/Message/annotations → work → assignment → CourseId → Organization. Attached file следует parent; pending upload имеет binding в проверенном scope. Identity/security purpose разрешается отдельно и не наследуется из случайно выбранного курса.
2. Organization задаёт educational policy default. Course binding фиксирует утверждённую версию при вводе курса; отклонение допускается только явно проверенной policy для той же boundary, без молчаливой смены оператора. Нельзя включить analytics ООО как override образовательного договора.
3. На действие выбираются purpose и applicable subject basis; проверяются current validity, поручение, privacy blocks, membership/grants и retention restrictions. Все условия — пересечение, а не «достаточно одной роли». Отсутствующая/неоднозначная/истёкшая policy означает deny обычной операции и case escalation, не автоматическое уничтожение.
4. Для доказательства сохраняется ссылка на применённую версию и время решения в минимальном audit. Исходная версия объясняет прежнюю обработку; текущий block/withdrawal всегда сильнее прежнего разрешения. Обновления курса оформляются новой binding version с effectiveFrom; исторические события не переписываются, журнал периодов позволяет восстановить выбор версии.
5. Lifecycle jobs разрешают основания всех затронутых субъектов/целей, independent holds и полномочие инициатора до формирования manifest. Удержание части данных не разрешает обычное обучение/доступ к остальной части. Новая цель требует нового решения/основания, а не использования старого snapshot.

Не допускаются default global controller, выбор policy по присланному клиентом ID, permissive fallback при недоступном реестре или бесконечный cache policy. Отзыв и privacy block применяются к следующей проверяемой операции; для завершения revoke/block применяется barrier/drain по [isolation](CROSS-ORGANIZATION-ISOLATION.md).

Операционное хранение в той же БД допустимо, логическая ответственность отделена. Единый resolved immutable VO для одной операции может передаваться между Application/Infrastructure; это не новая сущность Domain. Решения schema и точного журнала периодов блокируют L1 до ORG-G03. [Матрица данных](../compliance/PD-CONTROLLER-PROCESSOR-MODEL.md), [ADR-L13](../adr/ADR-L13-processing-context.md).
