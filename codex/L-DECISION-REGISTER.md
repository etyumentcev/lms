# L — Decision Register

> Актуальная редакция L0.3 от 2026-10-09: D4 сохранён как историческое решение и уточнён D4a–d; C01/C02 и ORG-G01–04/06/07 ACCEPTED в указанных границах. [Readiness](L03-L1-READINESS.md) и [план L1](L03-L1-IMPLEMENTATION-PLAN.md) supersede прежние prerequisites. Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY.

Дата: 2026-10-09. Историческая база L0 `c7a500f`; L0.3 documentation-only от `c1cc698729b6525c5ccd5bd1c03bc6b742397a12`. **FACT** — проверено кодом/документом или явно принято владельцем; **PROPOSAL** — рекомендация; **DECISION REQUIRED** — ответ владельца; **UNKNOWN** — нет исходных данных. PROPOSAL не означает accepted. Исторически L0 закрыл D1–D3. В L0.1 приняты владельцем также D4/D5; PD-G01–14 остаются открытыми. Подробности: [L0.1 gates](L01-DECISION-GATES.md).

Переход в ACCEPTED требует записи decision/date/owner/evidence и обновления затронутых ADR/плана. Технические B решения инженер может принять в разрешённом этапе после указанной проверки; L0 не разрешает implementation. Отсутствие ответа не является согласованием. [Архитектура](L-ARCHITECTURE.md), [этапы](L-IMPLEMENTATION-PLAN.md), [краткие вопросы](L0-RESULTS.md).

## M1 acceptance extension — 2026-10-14

Owner: владелец Sapientum. Evidence решения: явное задание `OWNER DECISION — M1-11 / TC-071` от 2026-10-14. Решение меняет только обязательность критерия для **M1 Synthetic MVP** и не переписывает исходные evidence или результаты прогонов.

| ID | Статус | Решение | Неизменённый факт | Обязательное продолжение / не-следствия |
|---|---|---|---|---|
| M1-OD-071 | **ACCEPTED — WAIVED BY OWNER FOR M1** | Точное визуальное сравнение с отсутствующими утверждёнными PNG исключено из обязательных условий M1 Synthetic MVP. | TC-071 сохраняет исходный результат `REFERENCE UNAVAILABLE`; фиктивный `PASS` запрещён. | Создать и закрыть `UI-VISUAL-ACCEPTANCE` до пилота с реальными учениками. TC-076 не закрывается автоматически; TC-077 остаётся за окончательным решением владельца. Acceptance commit запрещён до TC-076 и финального TC-077; push/deploy запрещены. `SYNTHETIC MVP ACCEPTED` и `PILOT AUTHORIZED` не объявлены. |
| M1-OD-077 | **ACCEPTED — SYNTHETIC MVP ACCEPTED** | После положительного независимого TC-076 re-review владелец принял этап M1 для synthetic-only среды и разрешил scoped acceptance commit. | Raw rerun3 остаётся `74 PASS / 0 FAIL / 3 BLOCKED`; historical TC-076 FAIL и TC-071 `REFERENCE UNAVAILABLE` неизменны. Текущая disposition: `76 PASS / 0 FAIL / 0 BLOCKED / 1 WAIVED / 0 NOT RUN`. | Production deployment, реальные данные детей, push и deploy не разрешены. `UI-VISUAL-ACCEPTANCE` обязателен до пилота; `PRS1 exact = OFF`, `G02 = BLOCKED`, `S4 HTTP NOT AUTHORIZED`; `PILOT AUTHORIZED` не следует. Evidence: [TC-077](TC-077-OWNER-DECISION.md), [TC-076 re-review](TC-076-INDEPENDENT-RE-REVIEW-RESULTS.md). |

`UI-VISUAL-ACCEPTANCE` — обязательный pre-pilot gate: предоставить точные утверждённые PNG, сопоставить их с живыми React routes в согласованных viewport, записать отклонения и отдельное решение владельца. M1 waiver не является визуальной приёмкой и не переносится на pilot/real-data readiness.

## A — Already accepted

| ID / вопрос | Статус и известные факты | Рекомендация / альтернативы | Что решить / кто | Этапы и отсутствие решения | ADR |
|---|---|---|---|---|---|
| D1 Где размещать пилот? | ACCEPTED владельцем в L0 request: собственный VPS/сервер | Следовать; managed platform не рассматривается вместо решения | Модель закрыта; конкретные параметры отдельно C03 | L3 опирается на D1, C03 ещё блокирует live hosting | [L03](../docs/adr/ADR-L03-vps-deployment.md) |
| D2 Как развёртывать? | ACCEPTED: Docker Compose; есть local Compose | Сохранить; Kubernetes не нужен | Закрыто владельцем | L3: нужна pilot конфигурация, dev compose не готов | [L03](../docs/adr/ADR-L03-vps-deployment.md) |
| D3 Какая identity platform? | ACCEPTED: ASP.NET Core Identity; сейчас только JwtBearer boundary | Identity внутри existing host; внешний IdP не предлагается | Закрыто владельцем; критического противоречия не найдено | Основа L1; не закрывает provisioning/transport | [L01](../docs/adr/ADR-L01-identity-integration.md) |
| D4 Кто оператор? | **ACCEPTED** владельцем в L0.1 2026-10-09: российское ООО владельца | Выбор оператора закрыт, реквизиты UNKNOWN | PD-G01 только точные реквизиты/полномочия | До real data: заполнение и проверка документов | [L09](../docs/adr/ADR-L09-personal-data-lifecycle.md) |
| D5 Когда обеспечить compliance? | **ACCEPTED**: применимые 152-ФЗ и связанные нормы до первого real-data пилота | Обязательный release gate; не переносить на production | PD-G02–14 обеспечивают evidence, не переоткрывают решение | Любой обязательный gap запрещает READY_FOR_PILOT | [матрица](../docs/compliance/PD-COMPLIANCE-MATRIX.md) |

## B — Technical decisions (B01–B03 superseded T01/T03/T04; остальные PROPOSAL)

| ID / вопрос | FACT / ограничение | Рекомендация и альтернативы | Что решить / кто | Блокируемые этапы / отсутствие решения | ADR |
|---|---|---|---|---|---|
| B01 Browser transport | ACCEPTED T04: подписанный JWT ≤15 минут | Cookie-only proposal отменён | E01/E02 ACCEPTED; O01 refresh coordination OPEN до S4 | До S4 public protocol; S2 primitives независимы | [L01](../docs/adr/ADR-L01-identity-integration.md), [L15](../docs/adr/ADR-L15-jwt-delayed-revocation.md) |
| B02 Identity persistence/mapping? | Actor UUID отдельно от credentials, history immutable, нет profiles | Независимый Identity UUID → unique Actor mapping; единый context/DB/additive migrations; альтернатива separate context | Инженер L1: migration diff/legacy inventory/atomic provisioning tests | L1 нельзя безопасно привязать историю | [L01](../docs/adr/ADR-L01-identity-integration.md), [L02](../docs/adr/ADR-L02-account-provisioning.md) |
| B03 Revocation | ACCEPTED T04: scoped ordinary revoke ≤2 часа | Local JWT checks; fresh DB issue/refresh; 10m reserve / 60s ordinary / 8m maximum HTTP | E02/E03/E06 ACCEPTED; O01 отдельно OPEN | До affected slices, implementation NOT VERIFIED | [L15](../docs/adr/ADR-L15-jwt-delayed-revocation.md) |
| B04 Pilot topology/proxy/migrations? | Local nginx/API/DB/migrate, dev config | Caddy edge + production Web + single API/private DB; альтернатива nginx+ACME | Инженер L3: pinned tool/config, сети/health/restart/rollback проверить; C03 supplies host | L3 не доказано безопасное deployment | [L03](../docs/adr/ADR-L03-vps-deployment.md) |
| B05 TLS/CSRF/keys/secrets? | Нет configured TLS/forwarded headers/DP persistence | Trusted edge, same-origin, explicit antiforgery, persistent encrypted DP keys; альтернативный edge по B04 | Инженер L1/L3: secure local tests, затем actual DNS/TLS/rotation | L1 local auth design, L3/L4 real session безопасность | [L04](../docs/adr/ADR-L04-https-secrets.md) |
| B06 Как согласовать DB+files backup? | Два volumes, нет общей transaction, restore не проверен | Drain/stop всех writers, dump+files+keys+manifest, encrypted off-host; snapshot/online при строгом C04 | Инженер L3 после C04/C05: выполнить clean-host drill | L3/L4: backup без доказанной recoverability | [L05](../docs/adr/ADR-L05-backup-restore.md) |
| B07 Где ограничивать ресурсы? | Есть per-file/annotation caps, нет total quota/rate/cleanup | Edge+API permits+DB durable reservations/ledger; proxy-only недостаточен | Инженер L2/L3: измерить technical bounds, race/ENOSPC tests; значения product C06/C08 | L2/L4 K-S02/S05 остаются | [L06](../docs/adr/ADR-L06-resource-protection.md) |
| B08 Как принимать devices? | Только Chrome/CDP; физический protocol BLOCKED | Reuse K protocol, versioned evidence + Identity lifecycle; эмуляция дополнение | Инженер/tester L4 по C07 | L4 нет доказанного supported scope | [L07](../docs/adr/ADR-L07-device-browser-scope.md) |
| B09 Минимальные runbooks/logs? | traceId/errors есть, ops alerts/recovery не проверены | Bounded redacted logs, access audit, operator drills; большой monitoring stack не нужен | Инженер L3, реальный оператор C09: проверить delivery/runbooks | L3/L4 silent incidents/unknown recovery | [L08](../docs/adr/ADR-L08-operations-privacy.md) |

Технические решения не включают покупку сервера, выдачу реальных credentials, смену DNS, передачу данных провайдерам или изменение product requirements. Эти действия зависят от owner gates и отдельного разрешения соответствующего этапа.

## C — Owner decisions (C01/C02 business ACCEPTED; C03–C09 остаются открытыми)

| ID / вопрос | Известные FACT / UNKNOWN | Рекомендованный вариант / альтернативы | Что именно решить / кто принимает | Что блокируется / последствия без решения | ADR |
|---|---|---|---|---|---|
| C01 Как создавать accounts/права? | **ACCEPTED business**, владелец, L0.3 §3: уполномоченный администратор образовательной организации по её списку; no signup/email/phone requirement | T02 ACCEPTED: минимальный org-admin UI | Не переоткрывать UI/issuer; E04 verifier/linking и controlled bootstrap review | До L1 техническое согласование; real roster только после PD gates. Provision account не enrol | [L02](../docs/adr/ADR-L02-account-provisioning.md) |
| C02 Первый доступ и recovery? | **ACCEPTED business**, владелец, L0.3 §3: одноразовый код лично либо по проверенному индивидуальному каналу; recovery после подтверждения личности/полномочий, без новых прав | T01/T02 ACCEPTED: 16/80, HMAC-SHA256, TTL/rates и независимое shared recovery; E02–E05 ACCEPTED; O01/G01–G04 отдельно | Применить принятые параметры, закрыть только OPEN DECISIONS; юридическое представительство отдельно | Технические детали до соответствующего L1 slice; реальные каналы/полномочия до real accounts | [L02](../docs/adr/ADR-L02-account-provisioning.md) |
| C03 Конкретный server/domain? | D1/D2 приняты; VPS/domain/OS/ресурсы/budget/access UNKNOWN | Один host и один canonical origin; размер после C06 measurements; провайдера не выбираем | Владелец/оператор: host, OS/support, domain/DNS control, SSH, бюджет; secrets передавать вне Git | L3 live HTTPS/deploy, L4 target capacity; local design возможен | [L03](../docs/adr/ADR-L03-vps-deployment.md), [L04](../docs/adr/ADR-L04-https-secrets.md) |
| C04 Допустимые потеря данных/простой/backup окно? | Restore NOT TESTED, RPO/RTO UNKNOWN | Начать с простого coordinated backup, если приемлемы окна; экономный/учебный день/строгий варианты в ADR | Владелец: RPO, RTO, downtime и maintenance window; оператор подтверждает drill | L3 backup algorithm acceptance, L4 readiness; не гарантировать выбранные цели до измерения | [L05](../docs/adr/ADR-L05-backup-restore.md) |
| C05 Где backup и ключи? | Только local volumes; destination/custodian UNKNOWN | Encrypted off-host storage, отдельный key escrow и заместитель; альтернативы object storage/другой сервер | Владелец: место/юрисдикция, provider, бюджет, права/retention, key custodian/доступ восстановления | L3 off-host restore, L4 recovery; backup на единственном VPS недостаточен | [L05](../docs/adr/ADR-L05-backup-restore.md), [L04](../docs/adr/ADR-L04-https-secrets.md) |
| C06 Объём/нагрузка/квоты и usability budgets? | K single caller, Lesson N+1/dense canvas риски; целевая нагрузка UNKNOWN | Описать pilot workload, измерить, затем квоты; не произвольные defaults | Владелец: students/teachers/concurrency, assignments/photos/attempts, acceptable latency, storage budget, pending expiry (с C08) | L2 production quota acceptance, L3 capacity, L4 performance; fixture limits возможны раньше | [L06](../docs/adr/ADR-L06-resource-protection.md), [L03](../docs/adr/ADR-L03-vps-deployment.md) |
| C07 Кто участвует и какие devices/materials? | Safari/physical BLOCKED, display names/lookups/answers absent | Desktop-only если реально подходит; secure roster/teacher materials outside app как временный workaround; иначе tablet scope | Владелец: состав/размер группы без публикации персональных данных, OS/browser/input, tester, приемлемость ID/материалов workaround | L4 functional/device acceptance; если workaround неприемлем — отдельный enrichment scope до pilot | [L07](../docs/adr/ADR-L07-device-browser-scope.md), [L02](../docs/adr/ADR-L02-account-provisioning.md) |
| C08 Privacy/retention/удаление? | D4 ООО РФ и D5 применимое законодательство РФ ACCEPTED; originals/immutable history | Минимум данных, конечный retention, ADR-L09/L10; раскрыто PD-G02–14 | Владелец/ПД/юрист: основания, несовершеннолетние, поля/сроки, уничтожение, реальные потоки и обязательные evidence; оператор повторно не выбирается | **Любые real learner data**, L2 destructive retention, L4. Deactivation не выполняет erasure | [L08](../docs/adr/ADR-L08-operations-privacy.md), [L05](../docs/adr/ADR-L05-backup-restore.md) |
| C09 Кто эксплуатирует и получает alerts? | Нет назначенных server/backup/restore operators; SLA UNKNOWN | Один основной оператор + заместитель, проверенный канал, rehearsal | Владелец: имена/полномочия, доступность/реакция, updates/incident/backup/key duties; можно совмещать | L3 operational sign-off, L4; отсутствие ответственного не компенсируется green tests | [L08](../docs/adr/ADR-L08-operations-privacy.md) |

## Текущий минимум L0.3

C01/C02, ORG-G01–04/06/07 и T01–T06 ACCEPTED. Полный [реестр T01–T06](L03-OWNER-DECISION-REGISTER.md) содержит ACCEPTED E01–E06 и OPEN O01; [readiness](L03-L1-READINESS.md) отделяет документальную готовность, инженерные блокеры и отдельное разрешение L1. Только SYNTHETIC DATA ONLY. ORG-G05/08 и PD-G01–14 остаются открытыми, D4/D5 не legal verification.

## L0.2 — уточнение D4 и новые предложения

Исторический D4 выше сохранён. Evidence новых бизнес-решений: явное задание владельца L0.2 от 2026-10-09; юридическая квалификация остаётся гипотезой.

| ID | Решение / статус |
|---|---|
| D4a | ACCEPTED: ООО РФ — владелец и технический оператор платформы; термин не равен оператору ПД |
| D4b | ACCEPTED как бизнес-модель: сторонняя организация управляет обучением и определяет цели учебной обработки; её квалификация как оператора REQUIRE LEGAL REVIEW |
| D4c | ACCEPTED: то же ООО может планировать собственное обучение; второе юрлицо не условие архитектуры; законность программ/лицензирование проверить |
| D4d | ACCEPTED принцип: роли оператора/обработчика определяются по цели; конкретные роли/основания REQUIRE LEGAL REVIEW |
| D6 | ACCEPTED: пилот 5–8 классы, одна сторонняя организация/контекст, закрытая выдача accounts, без public signup/multi-tenant UI |
| D7 | ACCEPTED: независимые Platform Management и Education Management полномочия |
| B10 | Принципы Organization/Course без LearningSpace **ACCEPTED ORG-G01/07 L0.3**; sidecar/keys/migration T03/T06 **ACCEPTED** |
| B11 | Версии ProcessingContext/current restrictions вне учебных агрегатов **ACCEPTED ORG-G03/06**; persistence T05 **ACCEPTED**, legal basis/сроки **REQUIRE LEGAL REVIEW** |
| B12 | Identity/membership/assignments/isolation **ACCEPTED ORG-G02/04**; schema/JWT/delayed revoke T03/T04 **ACCEPTED** |

Архитектурные принципы B10–B12 superseded решениями ORG L0.3; технические детали не приняты автоматически. C07 возраст 5–8 классов принят; devices/capacity открыты. PD-G01 относится к реквизитам/полномочиям A и P; PD-G02/12/14 проверяют роли по цели. D4 нельзя трактовать как окончательное признание ООО единственным оператором всех учебных ПД.

## L0.3 — accepted decisions и технические предложения

Owner: владелец проекта. Date: 2026-10-09. Evidence: прежнее задание «Checkpoint L0.3» для ORG/C и явное задание «Owner Decisions Consolidation & L1 Readiness» §4 для T01–T06; дополнительные предложения не принимаются автоматически.

| ID | Статус / содержание | ADR |
|---|---|---|
| ORG-G01 | ACCEPTED Organization обязательна, ровно один owner Course, LearningSpace entity не вводится | L11/L12 |
| ORG-G02 | ACCEPTED IdentityUser/Actor/membership/enrollment/teacher assignment разделены; один login multi-org; одна Student/Teacher role MVP; administrative scope отдельно | L14 |
| ORG-G03 | ACCEPTED версии policy вне учебных агрегатов, Course binding, current restrictions выше history; основные три таблицы отдельно приняты T05 | L13/L09 |
| ORG-G04 | ACCEPTED: server scopes/admin не reader; ordinary revoke прекращает новые операции в пределах 2 часов через JWT ≤15 минут, deadline/reserve 10 минут и HTTP 60 секунд / max 8 минут | L14/L15 |
| ORG-G06 | ACCEPTED scoped erasure/manifest/evidence, A не удаляет B или общий Identity; backup restore не возвращает уничтоженное | L09/L10 |
| ORG-G07 | ACCEPTED additive migrations, no guessed legacy owner, quarantine, synthetic mapping и verified real manifest | L11 |
| ORG-G05/08 | REQUIRE LEGAL REVIEW: роли/договоры/поручение/основания и 273-ФЗ/лицензирование | L13 и compliance |
| T01 | ACCEPTED 16 Crockford Base32 / 80 бит, HMAC-SHA256, accepted TTL/rates/passwords; JWT по T04 | [Owner register](L03-OWNER-DECISION-REGISTER.md) |
| T02 | ACCEPTED минимальный org-admin UI, bootstrap, проверка получателя и независимое shared recovery | [Owner register](L03-OWNER-DECISION-REGISTER.md) |
| T03 | ACCEPTED Identity/Actor/organization/scopes tables, Generation, DB isolation, no cascade | [Owner register](L03-OWNER-DECISION-REGISTER.md) |
| T04 | ACCEPTED JWT ≤15 минут, ordinary revoke ≤2 часа, 10m reserve, API 60s/max HTTP 8m | [Owner register](L03-OWNER-DECISION-REGISTER.md) |
| T05 | ACCEPTED ProcessingContextVersions/CourseContextBindings/ProcessingRestrictions, immutable versions | [Owner register](L03-OWNER-DECISION-REGISTER.md) |
| T06 | ACCEPTED аддитивные M1–M3, explicit synthetic mapping, unknown quarantine, backup/restore/cutover | [Owner register](L03-OWNER-DECISION-REGISTER.md) |

D1–D5, D4a–d, D6/D7 и обычные Domain invariants сохранены. Учебная организация управляет набором, договорами с родителями, преподавателями, курсами и группами; ООО предоставляет и обслуживает платформу, может отдельно организовывать собственные курсы без второго юрлица. Platform/Education Management разделены. Юридическая квалификация **REQUIRE LEGAL REVIEW**. Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY.

## L0.4 extension

E01–E06 ACCEPTED по явному заданию L0.4 §4; [Owner Decision Register](L03-OWNER-DECISION-REGISTER.md) содержит утверждённые редакции. O01 concurrent refresh policy PROPOSED/OPEN до S4; G01–G04 engineering review, не новый blanket gate S1–S3. Отдельное разрешение L1 отсутствует; [L04 reconciliation](L04-DECISION-RECONCILIATION.md).
