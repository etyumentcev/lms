# L0.2 — Decision gates

2026-10-09. Compliance status: **NOT VERIFIED**. Pilot readiness: **NOT_READY**. Все ORG gates OPEN; принятие схемы не равно implementation evidence. Сохраняются обязательные [L0.1 gates](L01-DECISION-GATES.md).

ACCEPTED владельцем: пилот одной сторонней организации, 5–8 классы, её учителя, закрытые accounts, без public registration/multi-organization UI; ООО предоставляет платформу и может в будущем само обучать; разделение Platform/Education. Controller/processor модель принята только как рабочая гипотеза, REQUIRE LEGAL REVIEW.

| Gate / status | Решение / рекомендация и альтернатива | Owner / evidence | Когда блокирует |
|---|---|---|---|
| ORG-G01 DECISION REQUIRED | Organization root + Course ownership registry, без отдельного LearningSpace; альтернатива 1:N только по доказанному требованию | Владелец/архитектор: принять ADR-L11/L12, schema keys и migration mapping policy | **L1 schema** |
| ORG-G02 DECISION REQUIRED | Identity→Actor + membership, одна основная role/account; issuer по roster, independent scope revoke | Владелец: ADR-L14, issuer полномочия, C01/C02 recovery/channel; затем tests | **L1 provisioning/schema** |
| ORG-G03 DECISION REQUIRED / REQUIRE LEGAL REVIEW | Policy/version/subject basis вне Domain, course binding + current blocks; определить Identity и platform purposes | Владелец/ПД/юрист: ADR-L13, purpose matrix, minimal fields, authority/retention principles | **L1 workflows/schema**, PD-G02/03/04/07/08/10 |
| ORG-G04 DECISION REQUIRED | Полный scope resolver, commit-time revoke/barrier, narrow break-glass; не global Admin | Владелец/ИБ/архитектор: permissions и revocation semantics, затем two-org test matrix | **L1 access contract**, L3 ops |
| ORG-G05 REQUIRE LEGAL REVIEW | Роли A/P по каждой цели и поручение; own purposes отдельно | Юрист/стороны: legal memo, реквизиты/подписанты обеих сторон, подписанный договор и поручение | Роли/основания затрагиваемых L1 workflows до реализации; подписанный packet до real data |
| ORG-G06 DECISION REQUIRED / REQUIRE LEGAL REVIEW | Scoped erasure/holds и multi-controller backup; ADR-L09/L10 без global Identity cascade | Владелец/ПД/ИБ/юрист: authority/manifest principles до L1; сроки/копии/drills L2–L4 | **Граница lifecycle L1**, operational acceptance L2–L4 |
| ORG-G07 DECISION REQUIRED | Legacy inventory, unknown quarantine, synthetic-only backfill; аддитивные registry/FK | Архитектор/владелец данных: provenance manifest, clean/upgrade tests, no history reassignment | **L1 migration design**, до применения к real legacy |
| ORG-G08 REQUIRE LEGAL REVIEW | Применимость 273-ФЗ/лицензии по реальной деятельности | Юрист/организатор: applicability memo и license evidence либо мотивированная неприменимость | A до пилота; P до собственных курсов, не дополнительная функция L1 |

До L1 согласовать ORG-G01–04/06/07, правовые решения ORG-G05 для затрагиваемых workflows и прежний минимальный пакет PD/C gates. C01 закрыт только в части closed provisioning; issuer/канал/recovery ещё открыты. Возрастной scope 5–8 принят, полномочия родителей ещё проверяются. После согласования требуется **отдельное разрешение L1 на synthetic data**. Подписанные live документы/реальные реквизиты/инфраструктурные evidence нужны до real data, не для isolated tests.

Любое закрытие gate записывает decision/date/owner/evidence/affected ADR/tests. Условная норма — только мотивированная неприменимость, не молчаливый skip. Не требовать второго юрлица и не возвращать уже принятый scope на согласование.
