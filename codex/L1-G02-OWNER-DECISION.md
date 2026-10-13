# G02/A02 — Решение владельца

2026-10-09. **G02 ARCHITECTURE READY FOR OWNER DECISION.**
Это статус готовности пакета вариантов и proof obligations, **не** подтверждение T04, не снятие G02 blocker и не разрешение runtime implementation. **S4 BLOCKED.**

## Уже разрешено и выполнено

G02/A02 DESIGN AUTHORIZED: исследование, архитектура, failure model, варианты и verification plan. Документы сохранены в каноническом `/home/tea/github/lms`, в текущей `codex/l1-s4-jwt-api-authorization` по отдельному ответу владельца, без switch/merge истории. Предсуществующие изменения S4 report и synthetic test сохранены. Новая архитектура не реализована; S5–S8, полный S7 lifecycle, M2/M3, production migration, реальные персональные данные, push/merge/deploy не начаты.

## Конкретное решение для утверждения

**Рекомендуется принять вариант C как design baseline:** ограниченный storage gateway, immutable object versions, durable OperationId/idempotency, fencing в месте фактической mutation, atomic PG result/metadata и independent supervisor/controlled edge. Изоляция worker process обязательна для блокирующих native операций; container/service — deployment механизм, не доказательство завершения.

Предлагаемый текст решения владельца:

> Принимаю G02/A02 вариант C как архитектурное направление, с инвариантами I1–I7, предложенной ограниченной failure model и verification gates V0–V3. T04 60s/8m/10m/15m/2h не меняется. Числа stage envelope являются исследовательскими целями, а не подтверждёнными bounds. Текущий deployment не сертифицирован; S4 остаётся BLOCKED. Это решение не разрешает реализацию новой архитектуры. Её scope и запуск утверждаются отдельным явным решением после рассмотрения инфраструктурных доказательств и оставшихся blockers.

Альтернатива: отклонить C и оставить G02 ARCHITECTURE BLOCKED для имеющейся инфраструктуры, запросив иной проверяемый execution/storage/PG/edge boundary. Не выбирать A/B как «уже доказанное» решение и не ослаблять T04.

## Что требуется до отдельного implementation authorization

| Решение / evidence              | Условие                                                                                                                                         |
| ------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------- |
| Конкретный supported deployment | Pin OS/kernel/filesystem/storage/PG/edge/clock/supervisor; описать failure domains и действительно независимый stop path                        |
| Storage fence contract          | Не client timeout: actual mutation ordering, quiescence outstanding I/O, immutable seal durability, restart epoch и отсутствие raw bypass       |
| PG completion/history contract  | Bound commit/WAL/rollback/backend termination; authoritative resolve, old-primary fencing и отсутствие потери acknowledged history              |
| Edge contract                   | Absolute ingress-to-last-send cutoff с bounded kernel/NIC queues; все controlled hops, не только nginx inactivity settings                      |
| Сумма worst-case bounds         | Для каждого route Q+S+P+X+R+E+J≤60/480; clock/conservative JWT доказаны отдельно; 120s reserve не execution tail                                |
| Узкий recovery scope            | Только outcome classification, quarantine/epochs и безопасная компенсация доказанного orphan; не продолжение expired command и не full S7       |
| Будущий change scope            | Явно утвердить API/protocol/schema/runtime изменения, synthetic-only среду, verification matrix и stop conditions; автоматического перехода нет |

Граница исследования не скрывает нерешённую проблему: на текущем Linux/Compose/local FS/PostgreSQL/nginx baseline **невозможно доказать T04 для неограниченного syscall/WAL/edge hang**. Если выбранная инфраструктура не предоставляет требуемые bounds, implementation не имеет основания для acceptance; очередной успешный тест этого не меняет. Архитектурное согласие владельца тоже не заменяет техническое доказательство.

## Комплект на рассмотрение

1. [Bounded execution](../docs/architecture/L1-G02-BOUNDED-EXECUTION.md): topology, states, invariants, storage fence limits, commit ambiguity, recovery и budget.
2. [Failure model](../docs/architecture/L1-G02-FAILURE-MODEL.md): поддерживаемые условия, неподдерживаемые отказы и первичные источники.
3. [Options](L1-G02-OPTIONS.md): три варианта, стоимость и рекомендация.
4. [Verification plan](L1-G02-VERIFICATION-PLAN.md): доказательство, fault injection, oracle и критерии отказа в acceptance.
5. Этот документ и обновлённый [S4 checkpoint](L1-S4-RESULTS.md).

После подготовки пакета работа остановлена. Owner decision pending; implementation NOT AUTHORIZED; T04 NOT PROVEN; S4 BLOCKED.
