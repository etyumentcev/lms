# ADR-L13 — ProcessingContext / controller-processor responsibilities

2026-10-09. **ACCEPTED — архитектурная граница ORG-G03.** Owner: владелец; evidence: явное задание L0.3 §§4–7. Юридические роли/основания **REQUIRE LEGAL REVIEW**. Основные таблицы T03/T05 и миграционная последовательность T06 **ACCEPTED**; дополнительные инженерные механизмы требуют review, реализация **NOT VERIFIED**. L1 не разрешён.

## Решение и альтернативы

ACCEPTED T05: ProcessingContextVersions, CourseContextBindings, ProcessingRestrictions; immutable versions вне aggregates, course binding с периодом и той же organization, historical version links, current restrictions сильнее historical approval. Subject basis workflow вне L1. Роли по цели — юридическая гипотеза.

Domain entity или VO на каждом Review дублирует данные; configuration без durable версий недостаточна.

Подробности, доказательство необходимости и migration consequences: [модель](../architecture/PROCESSING-CONTEXT-MODEL.md). [Authorization isolation](../architecture/CROSS-ORGANIZATION-ISOLATION.md) и [матрица правовых ролей](../compliance/PD-CONTROLLER-PROCESSOR-MODEL.md) обязательны совместно. Основные records приняты T03/T05; ports/детали DDL — инженерный дизайн, не существующие классы.

## Последствия и проверка

Цена: новые registry/membership/policy records и обязательный resolver во всех queries, raw SQL, files, jobs и maintenance. Сохраняются exact content refs/state machine/immutable ordinary history. Unknown legacy roots не получают реальную organization автоматически.

Принятие зафиксировано в [gates](../../codex/L02-DECISION-GATES.md). Основные schema решения приняты; детали DDL/index/trigger проверяются в L1; правовые workflows — отдельного legal review. [L1 plan](../../codex/L03-L1-IMPLEMENTATION-PLAN.md) ограничен synthetic-only scope. Затем нужны synthetic two-organization negative tests, clean/upgrade migration, revoke races и lifecycle/restore evidence. Документ не доказывает реализацию. ADR-L09/L10 сохраняют силу с контекстным уточнением L0.2.

Compliance status: **NOT VERIFIED**. Pilot readiness: **NOT_READY**.

L0.4 E06 ACCEPTED: [ADR-L18](ADR-L18-synthetic-processing-restrictions.md) задаёт минимальные current restriction checks для SyntheticOnly, отдельно от JWT. Real-data freshness/propagation/in-flight guarantee требует отдельного pre-pilot решения и legal evidence.
