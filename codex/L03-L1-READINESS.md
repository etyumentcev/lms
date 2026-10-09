# L0.3 — L1 Readiness Gate

2026-10-09. **Documentation readiness: READY** по завершённой сверке обязательного пакета, проверкам ссылок и diff в [results](L03-RESULTS.md). **Technical execution: CONDITIONAL / BLOCKED FOR AFFECTED SLICES**, OPEN E01–E06. **Owner authorization for L1: NOT GRANTED.** L1 НЕ НАЧАТ.

Compliance status: NOT VERIFIED.

Pilot readiness: NOT_READY.

LEGAL SOURCE NOT VERIFIED.

## Решения владельца

| Decision | Status |
|---|---|
| T01 Account Security | ACCEPTED |
| T02 Provisioning & Recovery | ACCEPTED |
| T03 Identity & Organization Schema | ACCEPTED |
| T04 JWT & Delayed Revocation | ACCEPTED |
| T05 ProcessingContext | ACCEPTED |
| T06 Additive Migration | ACCEPTED |

Утверждённые редакции и provenance — [Owner Decision Register](L03-OWNER-DECISION-REGISTER.md). Технические решения владельца согласованы. ORG-G04 обновлён окончательным T04; C01/C02 и ORG-G01–04/06/07, D1–D7/D4a–d сохранены в своих границах. Старые 26/128 code и PG ordinary revoke barrier отменены, server cookie session не конкурирует с JWT.

## Обязательный пакет документации

Проверены [plan](L03-L1-IMPLEMENTATION-PLAN.md), [reconciliation](L03-DECISION-RECONCILIATION.md), [results](L03-RESULTS.md), [domain](../docs/domain/LMS-DOMAIN-SPEC.md), [feature](../docs/features/FEATURE-SUBMISSION-REVIEW.md), [frontend](../docs/ui/LMS-FRONTEND-CONTRACT.md), [UI flow](../docs/ui/LMS-UI-FLOW-SPEC.md), реестр T01–T06, ADR-L01–L14 и новый [ADR-L15](../docs/adr/ADR-L15-jwt-delayed-revocation.md), L0.1/L0.2 gates/reports, PD/ORG registers и [отдельное execution task](CODEX-TASK-L1-EXECUTION.md). READY относится к документированию решений/блокеров, не к выполненным runtime controls. Исторические reports не новый PASS.

## OPEN DECISIONS — технические блокеры

Полные формулировки и предложения в Owner Register; ни одно предложение не ACCEPTED без решения.

| ID | Необходимое решение | Блокирует |
|---|---|---|
| E01 | Multi-org JWT composition и scoped deadline/claim contract без A→B отказа | S2–S4 |
| E02 | Browser token storage, refresh TTL/rotation/replay/concurrency, logout/reset semantics | S2/S6/S7 |
| E03 | Emergency compromise authority/SLA/mechanism; 2 часа не признаны достаточными | S6/S7 и security acceptance |
| E04 | Независимый shared-account verifier/linking/evidence без раскрытия B | S5/S6 |
| E05 | Cooldown partition/canonical code input/reissue, MAC rotation custody и limiter topology | S6 |
| E06 | Freshness/propagation/races текущих ProcessingRestrictions, без ослабления T05 | S4/S7 |

Закрыть вопросы либо получить явное делегирование инженерного выбора в соответствующих границах. Реализацию незатронутых foundations можно отдельно разрешить, но это не разрешение полного L1; текущего разрешения нет ни на один slice. Документационное завершение L0.3 не скрывает эти блокеры.

## Данные, legal и release gates

Разрешённый будущий scope L1: **SYNTHETIC DATA ONLY**. Empty/synthetic migration rehearsal, Identity/JWT/Actor/org/course/group/file scopes, минимальный org-admin UI, коды и synthetic ProcessingContext/restrictions по плану. Записи SyntheticOnly не согласия и не юридические основания. Реальные ПД, real migration/backfill, production erasure, полноценный legal/consent UI и L2–L4 исключены.

[PD-G01–PD-G14](L01-DECISION-GATES.md) OPEN: реквизиты/полномочия, цели/основания, несовершеннолетние/представители, минимальные поля/EXIF, инфраструктура/потоки, угрозы/уровень/меры, сроки/holds, erasure/mixed-subject, backups, ответственные, уведомления, документы/договоры, инциденты, финальный юридический evidence review. [ORG-G05/ORG-G08](L02-DECISION-GATES.md) REQUIRE LEGAL REVIEW: purpose-specific роли/договор/поручение и образование/лицензирование. Тексты законов не верифицировались в этом checkpoint; новые юридические выводы не делались.

C03–C09 ещё требуют host/domain, RPO/RTO/window, off-host/key custody, workload/quotas, device/tester/materials, privacy/retention, operators/alerts. L2 protection, L3 hosting/HTTPS/keys/restore/drills и L4 physical devices/performance/compliance evidence остаются release gates. L1 tests/архитектурное ACCEPTED не READY_FOR_PILOT.

## Next action

Владельцу рассмотреть только OPEN E01–E06 и отдельно разрешить определённый scope L1 на synthetic data. T01–T06 повторного утверждения не требуют. До разрешения — STOP после документационного commit; execution task не выполнять.
