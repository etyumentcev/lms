# ADR-L14 — Cross-organization identity / authorization isolation

2026-10-09. **ACCEPTED — архитектурная граница ORG-G02/04.** Owner: владелец; evidence: явное задание L0.3 §§4–7. Юридические роли/основания **REQUIRE LEGAL REVIEW**. Основные таблицы T03/T05 и миграционная последовательность T06 **ACCEPTED**; дополнительные инженерные механизмы требуют review, реализация **NOT VERIFIED**. L1 не разрешён.

## Решение и альтернативы

IdentityUser→ActorId, membership и scope отдельно. Одна role/account в пилоте; будущие memberships не объединяют историю. Platform role не даёт учебное чтение.

Global Teacher/Admin отвергнуты; отдельный account на организацию не обязателен и усложняет recovery.

Подробности, доказательство необходимости и migration consequences: [модель](../architecture/IDENTITY-ORGANIZATION-SCOPES.md). [Authorization isolation](../architecture/CROSS-ORGANIZATION-ISOLATION.md) и [матрица правовых ролей](../compliance/PD-CONTROLLER-PROCESSOR-MODEL.md) обязательны совместно. Основные records приняты T03/T05; ports/детали DDL — инженерный дизайн, не существующие классы.

## Последствия и проверка

Цена: новые registry/membership/policy records и обязательный resolver во всех queries, raw SQL, files, jobs и maintenance. Сохраняются exact content refs/state machine/immutable ordinary history. Unknown legacy roots не получают реальную organization автоматически.

Принятие зафиксировано в [gates](../../codex/L02-DECISION-GATES.md). Основные schema решения приняты; детали DDL/index/trigger проверяются в L1; правовые workflows — отдельного legal review. [L1 plan](../../codex/L03-L1-IMPLEMENTATION-PLAN.md) ограничен synthetic-only scope. Затем нужны synthetic two-organization negative tests, clean/upgrade migration, revoke races и lifecycle/restore evidence. Документ не доказывает реализацию. ADR-L09/L10 сохраняют силу с контекстным уточнением L0.2.

Compliance status: **NOT VERIFIED**. Pilot readiness: **NOT_READY**.

## T04 — superseding ordinary revoke

ORG-G04: ordinary revoke ≤2h, JWT≤15m, reserve10m/API60s/maxHTTP8m по ADR-L15; A не отменяет B. E01–E06 ACCEPTED L0.4. [ADR-L16](ADR-L16-organization-jwt-browser-session.md) задаёт one-org JWT/session, [ADR-L17](ADR-L17-account-security-recovery-challenges.md) security lifecycle, [ADR-L18](ADR-L18-synthetic-processing-restrictions.md) restrictions. O01 refresh coordination ACCEPTED (30 секунд по заданию L1/S2, ADR-L16); нет mandatory grant freshness lookup на каждом HTTP.
