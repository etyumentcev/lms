# ADR-L14 — Cross-organization identity / authorization isolation

2026-10-09. **PROPOSED / DECISION REQUIRED ORG-G02/04. REQUIRE LEGAL REVIEW** для правовых ролей/оснований. Бизнес-scope принят владельцем, техническая схема ещё не принята. L1 этим ADR не разрешается.

## Решение и альтернативы

IdentityUser→ActorId, membership и scope отдельно. Одна role/account в пилоте; будущие memberships не объединяют историю. Platform role не даёт учебное чтение.

Global Teacher/Admin отвергнуты; отдельный account на организацию не обязателен и усложняет recovery.

Подробности, доказательство необходимости и migration consequences: [модель](../architecture/IDENTITY-ORGANIZATION-SCOPES.md). [Authorization isolation](../architecture/CROSS-ORGANIZATION-ISOLATION.md) и [матрица правовых ролей](../compliance/PD-CONTROLLER-PROCESSOR-MODEL.md) обязательны совместно. Имена records/ports — предложения, не существующие классы.

## Последствия и проверка

Цена: новые registry/membership/policy records и обязательный resolver во всех queries, raw SQL, files, jobs и maintenance. Сохраняются exact content refs/state machine/immutable ordinary history. Unknown legacy roots не получают реальную organization автоматически.

Принятие фиксируется owner/date/evidence по [gates](../../codex/L02-DECISION-GATES.md), с review schema и правовых workflows до L1. Затем нужны synthetic two-organization negative tests, clean/upgrade migration, revoke races и lifecycle/restore evidence. Документ не доказывает реализацию. ADR-L09/L10 сохраняют силу с контекстным уточнением L0.2.

Compliance status: **NOT VERIFIED**. Pilot readiness: **NOT_READY**.
