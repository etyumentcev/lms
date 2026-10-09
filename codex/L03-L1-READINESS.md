# L0.3 — L1 Readiness Gate

Дата: 2026-10-09. **Status: BLOCKED_PENDING_OWNER_DECISIONS.**

Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY. LEGAL SOURCE NOT VERIFIED.

Готов план для рассмотрения, но нельзя трактовать новые технические предложения как принятые. Особенно выбор provisioning interface и recipient verification/shared-account recovery влияет на user flow и полномочия. Статус не означает повторного открытия C01/C02 business или ORG принципов. L1 не начат; checkpoint завершается документацией.

## A. Решения владельца — ACCEPTED

Owner: владелец проекта; evidence: явное задание L0.3 §§2–9, 2026-10-09.

| Решение | Отражение / результат review |
|---|---|
| C01/C02 | Org administrator по roster; одноразовые activation/reset codes лично/через проверенный индивидуальный канал; identity/authority check; no new learning grants; no mandatory email/phone/public registration. ADR-L02 и decision register согласованы |
| ORG-G01 | Organization обязательна, один Course owner, нет LearningSpace entity MVP; ADR-L11/L12 ACCEPTED |
| ORG-G02 | Identity/Actor/membership/enrollment/teacher assignment, одна основная Student/Teacher role MVP и несколько organizations на login; ADR-L14 ACCEPTED |
| ORG-G03 | Версии ProcessingContext вне aggregates, course binding, актуальные restrictions; ADR-L13 ACCEPTED как архитектура |
| ORG-G04 | Server scope/commit guard/file recheck/revocation, admin без автоматического work access; ADR-L14 ACCEPTED |
| ORG-G06 | Scoped privileged erasure/request/authority/manifest/evidence, no A→B/global Identity cascade, no restore resurrection; ADR-L09/L10 ACCEPTED только архитектурные принципы |
| ORG-G07 | Additive migration, verified manifest, unknown quarantine, synthetic mapping; ADR-L11 и план |
| D1–D5/D4a–d/D6/D7 | Сохранены VPS/Compose/Identity/ООО и compliance до real pilot; внешняя организация 5–8 классы/собственные курсы ООО/Platform–Education separation |

## B. Открытые технические предложения

Все строки **PROPOSED**, owner принимает процесс/границы, инженер фиксирует reviewed implementation decision. Это полный пакет для следующего рассмотрения; отдельное согласование уже принятых принципов не нужно. Разрешение L1 может явно принять пакет либо перечислить изменения/делегированные инженерные решения; молчание не считается принятием.

| ID / блокер | Конкретное предложение | Кто / необходимое решение | Где описано |
|---|---|---|---|
| T01 activation/recovery/security | 128-bit random code/HMAC, activation 24h/reset 15min, 5 attempts, reissue invalidates previous, rate limits; password/session/revoke/compromise policy | Владелец принимает UX/политику; инженер проверяет crypto/transaction parameters | [Plan §§5–7](L03-L1-IMPLEMENTATION-PLAN.md) |
| T02 provisioning/recipient | Ограниченная org-admin форма (альтернатива personal-auth CLI), explicit bootstrap, проверка получателя/evidence, независимое approval shared-account recovery/linking | Владелец выбирает интерфейс и полномочия/процесс; legal review реальных представителей отдельно | Plan §§5/7/8 |
| T03 schema | Identity+stable Actor, nullable admin-only educational role, membership/grant generations, sidecars/composite FK/guard triggers, no cascade | Инженер/владелец принимают технический пакет либо делегирование; сверка модели до migration | Plan §§2/3 |
| T04 revocation | DB locks/epochs, commit-time recheck, Revoking→drain→Revoked, cross-process stream leases, bounded timeout | Инженер утверждает реализацию, владелец — pending/timeout semantics; tests обязательны | Plan §6 |
| T05 ProcessingContext boundary | Три records: versions/bindings/current restrictions; purpose/basis/retention как refs/VO; no consent tables | Инженер принимает минимальный persistence scope; не утверждает правовые значения | Plan §3 |
| T06 migration | Additive M1–M3, verified synthetic manifest, Unknown quarantine, guarded cutover, backup/forward repair/offline recovery | Инженер принимает последовательность/constraints/recovery; реальные manifests вне L1 | Plan §4 |

B01–B03 раннего реестра сохраняются как технические предложения, уточнённые T01/T03/T04. Выбор данного статуса обусловлен T01/T02 и непринятым пакетом механизмов, **не отсутствием уже полученных бизнес-решений**. После рассмотрения T01–T06 оценка может стать READY_FOR_L1_AUTHORIZATION; эта запись сама не меняется задним числом и не разрешает реализацию.

## C. Юридические зависимости

ORG-G05: окончательная квалификация A/ООО по каждой цели, LMS договор/поручение, собственные цели, основания/права/обязанности. ORG-G08: применимость 273-ФЗ, лицензирование сторонней организации/будущих собственных курсов. PD-G01–14: реквизиты/представители/consent/минимальные поля/сроки и holds/организационные документы/РКН/ИСПДн/меры/инциденты/backup/evidence/финальный review. Все остаются OPEN / REQUIRE LEGAL REVIEW; никаких LEGAL VERIFIED.

До этих решений нельзя реализовать юридически значимые production consent/basis/representative/DSAR/retention/erasure workflows, утвердить numerical legal periods/mixed-subject destruction или начать real processing. Политики с `SyntheticOnly` marker не являются consent или legal basis. Для изолированного технического L1 эти workflows исключены, поэтому текущий L1 status не BLOCKED_PENDING_LEGAL_DECISIONS; такой статус потребуется при расширении scope на них без правовых решений.

## D. Synthetic-only execution после отдельного разрешения

Допустимый будущий объём: schema/migration на empty и synthetic old DB, Identity/session/Actor mapping, org/membership/course/group scopes, closed provisioning, activation/recovery, authorization/file/commit guards, policy version/restriction contracts, две синтетические организации, negative tests и UI auth states. Ни реальные users, ни real legacy upgrade, ни настоящие legal approvals/consents, ни production erasure, ни L2–L4 в это разрешение не входят.

## E. Release gates

L1 completion ≠ READY_FOR_PILOT. До real pilot нужны legal/PD/ORG evidence, L2 resource protection, L3 hosting/HTTPS/keys/backup/restore/operator drills и L4 acceptance/performance/physical devices; C03–C09 остаются в своих границах. Исторические K tests и новые doc-checks не подтверждают эти controls.

Следующее действие владельца после L0.3: рассмотреть конкретный T01–T06 пакет и отдельно разрешить L1 исключительно с synthetic data. До этого STOP. Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY.
