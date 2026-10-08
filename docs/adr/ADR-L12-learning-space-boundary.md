# ADR-L12 — LearningSpace boundary

2026-10-09. **PROPOSED / DECISION REQUIRED ORG-G01. REQUIRE LEGAL REVIEW** для правовых ролей/оснований. Бизнес-scope принят владельцем, техническая схема ещё не принята. L1 этим ADR не разрешается.

## Решение и альтернативы

OrganizationId достаточно для одного учебного пространства на организатора; отдельную entity LearningSpace пока не вводить.

1:N spaces нужны при независимом администрировании нескольких пространств одной организации; такого требования пока нет.

Подробности, доказательство необходимости и migration consequences: [модель](../architecture/LEARNING-SPACE-MODEL.md). [Authorization isolation](../architecture/CROSS-ORGANIZATION-ISOLATION.md) и [матрица правовых ролей](../compliance/PD-CONTROLLER-PROCESSOR-MODEL.md) обязательны совместно. Имена records/ports — предложения, не существующие классы.

## Последствия и проверка

Цена: новые registry/membership/policy records и обязательный resolver во всех queries, raw SQL, files, jobs и maintenance. Сохраняются exact content refs/state machine/immutable ordinary history. Unknown legacy roots не получают реальную organization автоматически.

Принятие фиксируется owner/date/evidence по [gates](../../codex/L02-DECISION-GATES.md), с review schema и правовых workflows до L1. Затем нужны synthetic two-organization negative tests, clean/upgrade migration, revoke races и lifecycle/restore evidence. Документ не доказывает реализацию. ADR-L09/L10 сохраняют силу с контекстным уточнением L0.2.

Compliance status: **NOT VERIFIED**. Pilot readiness: **NOT_READY**.
