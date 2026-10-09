# L0.3 — Decision reconciliation and contradiction audit

2026-10-09. Base `c1cc698729b6525c5ccd5bd1c03bc6b742397a12`, parent `3aa34c18041e74570b04ba10a01f4294d451bc5d`. Owner/evidence: явное задание владельца L0.3 §§2–11. Только документация. Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY.

Приоритет: явные принятые решения L0.3 → актуальные ADR/реестры/план → исторические L0–L0.2 reports. Исторические отчёты и тестовые evidence не переписаны. В EXECPLAN прежние milestone sections сохранены как история и явно superseded текущим L0.3 разделом. Технические детали и рекомендации не повышаются в ACCEPTED автоматически.

## Decision reconciliation

| ID | Статус L0.3 | Граница принятия |
|---|---|---|
| D1/D2/D3 | ACCEPTED, сохранены | Собственный VPS/сервер, Docker Compose, ASP.NET Core Identity |
| D4/D4a–d | Историческое решение сохранено и уточнено | ООО владеет LMS; роли по целям; A предположительно controller, ООО processor/отдельный controller, собственные курсы отдельный educational context; квалификация REQUIRE LEGAL REVIEW |
| D5 | ACCEPTED, сохранено | Применимые требования до первого real-data пилота, не после него |
| D6/D7 | ACCEPTED | Пилот 5–8 классы, одна внешняя организация/её набор и договоры; ООО может обучать без второго юрлица; Platform/Education отдельно |
| C01/C02 | ACCEPTED business process | Org admin issuer, индивидуальная проверенная выдача одноразовых codes, no mandatory email/phone; technical parameters PROPOSED |
| ORG-G01/02/03/04/06/07 | ACCEPTED architecture | Organization/no LearningSpace, Identity/membership/assignments, versions/current restrictions, scopes/revoke, scoped erasure, safe legacy mapping |
| ORG-G05/08 и PD-G01–14 | OPEN / REQUIRE LEGAL REVIEW | Нет закрытия ролей/оснований/представительства/сроков/обязанностей/лицензирования/мер/evidence |
| T01–T06 | PROPOSED | Параметры codes, интерфейс, schema, barrier, context persistence, migration implementation |

ADR-L11/L12/L13/L14 перешли из PROPOSED в ACCEPTED для архитектурной границы. ADR-L09/L10 — ACCEPTED для scoped lifecycle/erasure principles, но не правовых процедур; ADR-L02 — ACCEPTED для C01/C02 business. ADR-L01 transport/schema остаётся PROPOSED при ранее принятом D3.

## Source → Existing statement → Conflict → Resolution → Updated document

Existing statements описывают базовую редакцию, не текущую норму. Для отрицательных требований отмечено, где нарушения не найдено; их не выдаём за исправленные дефекты.

| Source | Existing statement | Conflict | Resolution | Updated document |
|---|---|---|---|---|
| L-DECISION-REGISTER C01/C02 | Issuer UNKNOWN, operator CLI; старт L1 blocked без business ответа | Ответ уже дан, issuer — org admin | Business ACCEPTED; T01/T02 отдельно PROPOSED | [Register](L-DECISION-REGISTER.md), [gates](L02-DECISION-GATES.md) |
| ADR-L02 | Teachers создаёт только технический оператор, SSH procedure | Подмена образовательной authority платформой | Org-admin scoped issuer, UI/CLI отдельный выбор; bootstrap/self-grant границы | [ADR-L02](../docs/adr/ADR-L02-account-provisioning.md) |
| ADR-L02/L01 | User/mapping/enrollment/grants в едином provisioning | Account ошибочно выдаёт учебные права | Account creation отдельно от membership/enrollment/assignment | ADR-L02, [ADR-L01](../docs/adr/ADR-L01-identity-integration.md), [L1 plan](L03-L1-IMPLEMENTATION-PLAN.md) |
| L02 gates / ADR-L11/L12 | ORG-G01 DECISION REQUIRED | Organization/no LearningSpace уже приняты | ACCEPTED, физические детали PROPOSED | [L11](../docs/adr/ADR-L11-organization-boundary.md), [L12](../docs/adr/ADR-L12-learning-space-boundary.md), [Organization](../docs/architecture/ORGANIZATION-MODEL.md) |
| LearningSpace model | Рекомендуется не создавать entity | Требуется принятое обязательство MVP | Нет LearningSpace entity L1; future 1:N только доказанная потребность | [LearningSpace](../docs/architecture/LEARNING-SPACE-MODEL.md) |
| ADR-L01 mapping role | Единственный источник business role в global ActorIdentities | Одна роль не заменяет organization/course scope | Primary role только MVP constraint; effective rights через membership/grants, future roles без Identity rewrite | ADR-L01, [Identity scopes](../docs/architecture/IDENTITY-ORGANIZATION-SCOPES.md), [L14](../docs/adr/ADR-L14-cross-organization-identity.md) |
| ADR-L13/processing model | ORG-G03 OPEN, policy/subject records лишь recommendation | Версионная граница уже принята, пять названий не таблицы | ACCEPTED principles; три persistence records PROPOSED, no fake basis/consent | [L13](../docs/adr/ADR-L13-processing-context.md), [context model](../docs/architecture/PROCESSING-CONTEXT-MODEL.md) |
| L architecture / B03 / L01 | Strict in-flight revoke не обещан | ORG-G04 требует commit-time checks и отзыв | Требование принято; DB barrier/locks/epochs PROPOSED; current implementation unverified | [Architecture](L-ARCHITECTURE.md), ADR-L01, [isolation](../docs/architecture/CROSS-ORGANIZATION-ISOLATION.md) |
| Isolation/ADR-L14 | Admin blanket work access уже запрещён, ORG-G04 OPEN | Не дефект разрешений документа, устаревший статус | ACCEPTED; pending/attached file и old URL/commit criteria конкретизированы | L14, isolation, L1 plan |
| ADR-L09/L10 | Все принципы PROPOSAL/DECISION REQUIRED | Scoped erasure principles приняты | ACCEPTED architecture; request authority/manifest/evidence; real workflow после legal review | [L09](../docs/adr/ADR-L09-personal-data-lifecycle.md), [L10](../docs/adr/ADR-L10-erasure-and-immutable-history.md) |
| PD-DATA-INVENTORY I01 / lifecycle closure | Удалить credentials/mapping, Identity в общем closure | Без уточнения может удалить общий login при erase A | Общий Identity исключён из org-scoped erasure; global lifecycle отдельно | [Inventory](../docs/compliance/PD-DATA-INVENTORY.md), [lifecycle](../docs/compliance/PD-LIFECYCLE-AND-ERASURE.md), [org lifecycle](../docs/compliance/PD-ORGANIZATION-LIFECYCLE.md) |
| PD-PROCESSING-BASES / organizational docs / L architecture | ООО оператор; future external schools | Внешняя организация уже в первом пилоте; квалификация не подтверждена | D4a–d/purpose-specific working model REQUIRE LEGAL REVIEW | [Bases](../docs/compliance/PD-PROCESSING-BASES.md), [documents](../docs/compliance/PD-ORGANIZATIONAL-DOCUMENTS.md), [party model](../docs/compliance/PD-CONTROLLER-PROCESSOR-MODEL.md), architecture, ADR-L08 |
| L-IMPLEMENTATION-PLAN / L01 gates | До любого L1 нужны legal workflows; L1 реализует basis/representative/cases | Смешаны synthetic auth и юридический workflow | Synthetic L1 narrowed; legal procedures excluded до review; L2–L4/release dependencies сохранены | [Sequential plan](L-IMPLEMENTATION-PLAN.md), [L01 gates](L01-DECISION-GATES.md), [matrix](../docs/compliance/PD-COMPLIANCE-MATRIX.md), [controls](../docs/compliance/PD-SECURITY-CONTROLS.md) |
| Organization migration | Unknown owner уже нельзя угадывать, ORG-G07 открыт | Принято, но не конкретизировано | ACCEPTED; empty/upgrade/unknown/constraints/recovery sequence PROPOSED | Organization, L02 gates, L1 plan §4 |
| Domain spec | Единственная точка изменения ProblemWork, immutable history | Может читаться как запрет законного erasure | Явно ограничено ordinary learning operations; privileged lawful erasure отдельно | [Domain](../docs/domain/LMS-DOMAIN-SPEC.md), [Actors/use cases](../docs/domain/LMS-MVP-ACTORS-USE-CASES.md) |
| UI contracts / L1 future upload | Account switch state и неоднозначный upload context | Multi-org account требует точного pending scope | Минимальные auth формы/state reset; courseId upload proposal, no full multi-org UI | [Frontend contract](../docs/ui/LMS-FRONTEND-CONTRACT.md), [UI flow](../docs/ui/LMS-UI-FLOW-SPEC.md), L1 plan |
| Retention/backup документы | Уже запрещают forever и restore resurrection | Нарушение не найдено; нельзя принять неизвестные сроки | Сохранён запрет бессрочного хранения; численные сроки/holds остаются legal review | L09/L10, lifecycle/party model, L1 plan |
| Compliance status/legal sources | NOT VERIFIED / LEGAL SOURCE NOT VERIFIED | Нарушение не найдено; owner acceptance не verification | Статусы сохранены; нормы не «проверены» поисковыми выдержками | Compliance documents, readiness |
| Исторические L0–L0.2 reports и EXECPLAN milestones | Старые предложения/issuer/prerequisites и прежние test PASS | Не должны применяться как текущие решения | Reports без изменений; superseding раздел L0.3 и ссылки | [EXECPLAN](EXECPLAN-LMS-SIX-SCREENS.md), этот audit |

## Review coverage и пределы доказательств

Просмотрены текущие `docs/adr`, `docs/architecture`, `docs/compliance`, domain/UI specifications и L decision/architecture/plans; сверены code facts ActorMapper, AccessFacts, DbContext, controllers, ApiErrors и пять migrations. Полнотекстовый поиск проверяет operator-only, global role, admin bypass, LearningSpace, cascade, guessed ownership, forever retention, compliance и L1 dependencies. Исторические reports/evidence не исправляются под новые решения. CONTENT-ARCHITECTURE и content specifications не требуют смены учебного content contract; будущий organization registry не переписывает immutable snapshots.

Feasibility review выявил mutable generation FK риск: grants ссылаются FK на стабильную membership пару, generation проверяется актуальным resolver, поэтому revoke/recreate не ломает FK и не оживляет grants. Teacher group nullable требует двух partial unique indexes; JSON/arrays нуждаются в отдельном trigger guard. Unknown legacy ownership не может одновременно быть NOT NULL и неизвестным: явная quarantine ветка CHECK отделена от Verified courses.

Документальный review не доказывает SQL implementation, cryptographic implementation, concurrency либо юридическую применимость. Все соответствующие tests в L1 plan — будущие NOT RUN. Новый PASS относится только к выполненным документационным проверкам в [results](L03-RESULTS.md).
