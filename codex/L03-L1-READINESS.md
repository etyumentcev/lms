# L1 Readiness — актуальная оценка L0.4

2026-10-09. Documentation package READY после проверок [L04 results](L04-RESULTS.md). T01–T06/E01–E06 ACCEPTED; реализация L1 НЕ НАЧАТА.

| Независимый аспект | Статус / критерий |
|---|---|
| Architecture readiness | CONDITIONAL для полного L1: O01 exact refresh coordination/replay policy OPEN. Foundations S1–S3 архитектурно определены; остальные принятые T/E решения не переоткрываются |
| Implementation readiness | План/execution task определены для всех 8 slices; S1 готов к отдельному разрешению, S2/S3 после predecessor acceptance. S4 blocked O01, S5–S8 зависят от S4; G02 runtime time bounds ещё должны быть доказаны |
| Owner authorization | NOT GRANTED: нет отдельного разрешения ни на S1, ни на остальной L1 |
| Pilot readiness | NOT_READY: legal/organization/operations/release gates не закрыты; SYNTHETIC DATA ONLY |

Compliance status: NOT VERIFIED.

Pilot readiness: NOT_READY.

LEGAL SOURCE NOT VERIFIED.

## Accepted decisions

| ID | Status | Нормативный источник |
|---|---|---|
| T01 | ACCEPTED | Owner register / ADR-L17 |
| T02 | ACCEPTED | ADR-L02/L17 |
| T03 | ACCEPTED | ADR-L01/L11/L14 + plan |
| T04 | ACCEPTED | ADR-L15 |
| T05 | ACCEPTED | ADR-L13/L18 |
| T06 | ACCEPTED | Plan M1–M3 |
| E01 | ACCEPTED | ADR-L16 one-org claims/resource boundary |
| E02 | ACCEPTED | ADR-L16 session/refresh; O01 отдельно |
| E03 | ACCEPTED | ADR-L17 SecurityBlocked, ADR-L15 temporal proof |
| E04 | ACCEPTED | ADR-L17 independent approval/minimal security UI |
| E05 | ACCEPTED | ADR-L17 challenge states/atomicity/idempotency |
| E06 | ACCEPTED | ADR-L18 SyntheticOnly/current restrictions |

[Полный реестр](L03-OWNER-DECISION-REGISTER.md) содержит provenance, зависимости, invariants и границы принятия. [Security specification](../docs/architecture/L1-SECURITY-SPEC.md) описывает все trust boundaries. Документирование не runtime evidence.

## Независимая оценка slices

Статус учитывает фактическое отсутствие реализации predecessors, а не только качество плана. Разрешение владельца требуется даже строке READY. В колонке «design» отдельно показана возможность включить slice в sequential authorization.

| Slice | Статус сейчас | Зависимости / blocker | Design readiness и порядок |
|---|---|---|---|
| S1 Schema | READY_FOR_OWNER_AUTHORIZATION | Explicit synthetic target/backup-restore rehearsal при реализации; O01 не нужен | Определён; разрешить первым M1/M2/constraints rehearsal, без runtime cutover |
| S2 Identity/session primitives | BLOCKED_BY_DEPENDENCY | S1 ещё не принят; G01 schema/transaction review | READY_WITH_NONBLOCKING_RISKS по design. После S1 можно internal session CAS/60m/12h/verifiers; без public refresh/coordinator |
| S3 Membership/resource scopes | BLOCKED_BY_DEPENDENCY | S1/S2 ещё не приняты | READY_FOR_OWNER_AUTHORIZATION по design после predecessors; O01 не нужен, no public admin ingress |
| S4 JWT/refresh/switch | BLOCKED_BY_OPEN_DECISION | O01 + S1–S3; conservative expiry G02 evidence | Владелец выбирает/делегирует race/replay protocol, затем contract review/tests. Не считать E02 acceptance принятием 5-second proposal |
| S5 Provisioning UI | BLOCKED_BY_DEPENDENCY | S4 acceptance; synthetic participant refs и duplicate-review fixtures | Определён; после S4. Не выпускать code/approval через unfinished S6 |
| S6 Codes/recovery/block | BLOCKED_BY_DEPENDENCY | S5 + session path S4; G03 key/limiter review; независимый synthetic verifier | Определён E03–E05, no new owner decision о самом workflow. Реальных representatives не использовать |
| S7 File/jobs/restriction hardening | BLOCKED_BY_DEPENDENCY | S4/S6, G02 completion bounds и G04 synthetic restrictions evidence | Определён для synthetic; failure bounds блокирует acceptance, не оправдывает ослабление T04 |
| S8 Integration/M3 cutover | BLOCKED_BY_DEPENDENCY | S1–S7 PASS/O01 resolved/complete critical evidence | Последним: synthetic controlled cutover, final report/STOP |

S1–S3 независимы от O01 в design, но не друг от друга в реализации. Можно запросить единое разрешение S1→S2→S3 с обязательной приёмкой каждого или отдельные разрешения. Никакой future Authorization формулировкой этой таблицы не выдано.

## Open issues по категориям

**Owner decisions:** O01 concurrent refresh coordination/replay response/fallback; concrete proposed 5s refusal-without-revoke window и alternative strict broker в ADR-L16. До real pilot отдельное решение E06 freshness/propagation/in-flight semantics для юридических processing restrictions. Не спрашивать заново E01–E06.

**Engineering:** G01 exact claims/DTO/schema/capability encoding; G02 conservative clocks/expiry/complete operation/cancel/commit/edge bounds; G03 normalization/MAC rotation/idempotency/limiter topology; G04 synthetic restriction races/duplicate guard. Это review/tests внутри разрешённого scope. Новое owner decision понадобится при требовании изменить security guarantee, data scope либо user-visible policy сверх принятого.

**Legal:** PD-G01–PD-G14 OPEN: (01) реквизиты/authority; (02) цели/bases/роли; (03) minors/representatives/consent; (04) field minimization/EXIF; (05) infrastructure/flows; (06) threat classification/меры; (07) retention/holds; (08) erasure/mixed-subject; (09) backups; (10) responsible persons; (11) уведомления; (12) документы/contracts; (13) incidents; (14) official legal sources/final evidence review. ORG-G05 purpose-specific controller/processor agreements и ORG-G08 education/licensing REQUIRE LEGAL REVIEW. Детальные evidence/owners — [PD gates](L01-DECISION-GATES.md), [ORG gates](L02-DECISION-GATES.md). Юридический workflow/real data запрещён; synthetic verifications S6 не такие workflows.

**Operations:** C03 server/domain; C04 RPO/RTO/window; C05 off-host/keys/custody; C06 workload/storage/quotas/pending expiry; C07 devices/testers/materials; C08 privacy/retention (PD dependencies); C09 operators/alerts/drills. L2 resource controls, L3 HTTPS/deploy/restore и L4 physical devices/performance/legal release evidence остаются gates. Synthetic fixtures могут иметь test thresholds, это не production targets.

Реальные legal/operator назначения не нужны для внутреннего synthetic S1–S3. S6 требует synthetic independent personas/capabilities и verification evidence fixtures; если предлагается реальное подтверждение человека или сбор документов — STOP, PD-G03/12 и scope blocking. S7 не обещает real privacy SLA. Ни foundation tests, ни подписанный ADR не доказывают pilot readiness.

## Recommendation

Архитектурно можно отдельно разрешить **S1→S2→S3 на синтетических данных**. Сейчас начинать нельзя: owner authorization отсутствует. До S4 решить/делегировать O01, принять predecessors и проверить clock/expiry contract. Для следующих slices — dependencies/acceptance/stop conditions [плана](L03-L1-IMPLEMENTATION-PLAN.md), затем S8 evidence. [Execution task](CODEX-TASK-L1-EXECUTION.md) подготовлен и не выполняется. STOP после L0.4 documentation commit.
