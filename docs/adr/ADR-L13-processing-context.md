# ADR-L13 — ProcessingContext / controller-processor responsibilities

2026-10-09. **ACCEPTED — архитектурная граница ORG-G03.** Owner: владелец; evidence: явное задание L0.3 §§4–7. Юридические роли/основания **REQUIRE LEGAL REVIEW**. Физическая schema и механизмы **PROPOSED**, реализация **NOT VERIFIED**. L1 не разрешён.

## Решение и альтернативы

Отдельные durable compliance policies/version/subject basis; наследование от course root, current restrictions сильнее historical approval. Роли по цели — юридическая гипотеза.

Domain entity или VO на каждом Review дублирует данные; configuration без durable версий недостаточна.

Подробности, доказательство необходимости и migration consequences: [модель](../architecture/PROCESSING-CONTEXT-MODEL.md). [Authorization isolation](../architecture/CROSS-ORGANIZATION-ISOLATION.md) и [матрица правовых ролей](../compliance/PD-CONTROLLER-PROCESSOR-MODEL.md) обязательны совместно. Имена records/ports — предложения, не существующие классы.

## Последствия и проверка

Цена: новые registry/membership/policy records и обязательный resolver во всех queries, raw SQL, files, jobs и maintenance. Сохраняются exact content refs/state machine/immutable ordinary history. Unknown legacy roots не получают реальную organization автоматически.

Принятие зафиксировано в [gates](../../codex/L02-DECISION-GATES.md). Детали schema требуют технического решения; правовые workflows — отдельного legal review. [L1 plan](../../codex/L03-L1-IMPLEMENTATION-PLAN.md) ограничен synthetic-only scope. Затем нужны synthetic two-organization negative tests, clean/upgrade migration, revoke races и lifecycle/restore evidence. Документ не доказывает реализацию. ADR-L09/L10 сохраняют силу с контекстным уточнением L0.2.

Compliance status: **NOT VERIFIED**. Pilot readiness: **NOT_READY**.
