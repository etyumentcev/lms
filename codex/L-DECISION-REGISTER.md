# L — Decision Register

> L0.2 уточняет исторический D4 через D4a–d ниже. Формулировки L0.1 об единственном операторе не распространяются на все цели первого пилота. Текущие prerequisites: [L02 gates](L02-DECISION-GATES.md).

Дата: 2026-10-09. База `c7a500f`, L0 documentation only. **FACT** — проверено кодом/документом или явно принято владельцем; **PROPOSAL** — рекомендация; **DECISION REQUIRED** — ответ владельца; **UNKNOWN** — нет исходных данных. PROPOSAL не означает accepted. Исторически L0 закрыл D1–D3. В L0.1 приняты владельцем также D4/D5; PD-G01–14 остаются открытыми. Подробности: [L0.1 gates](L01-DECISION-GATES.md).

Переход в ACCEPTED требует записи decision/date/owner/evidence и обновления затронутых ADR/плана. Технические B решения инженер может принять в разрешённом этапе после указанной проверки; L0 не разрешает implementation. Отсутствие ответа не является согласованием. [Архитектура](L-ARCHITECTURE.md), [этапы](L-IMPLEMENTATION-PLAN.md), [краткие вопросы](L0-RESULTS.md).

## A — Already accepted

| ID / вопрос | Статус и известные факты | Рекомендация / альтернативы | Что решить / кто | Этапы и отсутствие решения | ADR |
|---|---|---|---|---|---|
| D1 Где размещать пилот? | ACCEPTED владельцем в L0 request: собственный VPS/сервер | Следовать; managed platform не рассматривается вместо решения | Модель закрыта; конкретные параметры отдельно C03 | L3 опирается на D1, C03 ещё блокирует live hosting | [L03](../docs/adr/ADR-L03-vps-deployment.md) |
| D2 Как развёртывать? | ACCEPTED: Docker Compose; есть local Compose | Сохранить; Kubernetes не нужен | Закрыто владельцем | L3: нужна pilot конфигурация, dev compose не готов | [L03](../docs/adr/ADR-L03-vps-deployment.md) |
| D3 Какая identity platform? | ACCEPTED: ASP.NET Core Identity; сейчас только JwtBearer boundary | Identity внутри existing host; внешний IdP не предлагается | Закрыто владельцем; критического противоречия не найдено | Основа L1; не закрывает provisioning/transport | [L01](../docs/adr/ADR-L01-identity-integration.md) |
| D4 Кто оператор? | **ACCEPTED** владельцем в L0.1 2026-10-09: российское ООО владельца | Выбор оператора закрыт, реквизиты UNKNOWN | PD-G01 только точные реквизиты/полномочия | До real data: заполнение и проверка документов | [L09](../docs/adr/ADR-L09-personal-data-lifecycle.md) |
| D5 Когда обеспечить compliance? | **ACCEPTED**: применимые 152-ФЗ и связанные нормы до первого real-data пилота | Обязательный release gate; не переносить на production | PD-G02–14 обеспечивают evidence, не переоткрывают решение | Любой обязательный gap запрещает READY_FOR_PILOT | [матрица](../docs/compliance/PD-COMPLIANCE-MATRIX.md) |

## B — Technical decisions (все PROPOSAL)

| ID / вопрос | FACT / ограничение | Рекомендация и альтернативы | Что решить / кто | Блокируемые этапы / отсутствие решения | ADR |
|---|---|---|---|---|---|
| B01 Browser authentication transport? | Same-origin client + bearer adapter, нет native requirement | Cookie HttpOnly/Secure + CSRF; bearer с refresh сложнее | Инженер L1: подтвердить cookie contract/CSRF и отсутствие внешнего bearer consumer; иначе уточнить требование | L1 auth contract не определён | [L01](../docs/adr/ADR-L01-identity-integration.md) |
| B02 Identity persistence/mapping? | Actor UUID отдельно от credentials, history immutable, нет profiles | Независимый Identity UUID → unique Actor mapping; единый context/DB/additive migrations; альтернатива separate context | Инженер L1: migration diff/legacy inventory/atomic provisioning tests | L1 нельзя безопасно привязать историю | [L01](../docs/adr/ADR-L01-identity-integration.md), [L02](../docs/adr/ADR-L02-account-provisioning.md) |
| B03 Session/revocation lifecycle? | Сейчас нет session provider, auth switch не remounts App | DB session + account/stamp check каждый request, explicit frontend reset; альтернатива periodic stamp с окном отзыва | Инженер L1: per-request semantics, timeouts обосновать, logout/expiry/cross-tab tests | L1/L4 возможны stale sessions/data; строгую in-flight отмену не обещать | [L01](../docs/adr/ADR-L01-identity-integration.md) |
| B04 Pilot topology/proxy/migrations? | Local nginx/API/DB/migrate, dev config | Caddy edge + production Web + single API/private DB; альтернатива nginx+ACME | Инженер L3: pinned tool/config, сети/health/restart/rollback проверить; C03 supplies host | L3 не доказано безопасное deployment | [L03](../docs/adr/ADR-L03-vps-deployment.md) |
| B05 TLS/CSRF/keys/secrets? | Нет configured TLS/forwarded headers/DP persistence | Trusted edge, same-origin, explicit antiforgery, persistent encrypted DP keys; альтернативный edge по B04 | Инженер L1/L3: secure local tests, затем actual DNS/TLS/rotation | L1 local auth design, L3/L4 real session безопасность | [L04](../docs/adr/ADR-L04-https-secrets.md) |
| B06 Как согласовать DB+files backup? | Два volumes, нет общей transaction, restore не проверен | Drain/stop всех writers, dump+files+keys+manifest, encrypted off-host; snapshot/online при строгом C04 | Инженер L3 после C04/C05: выполнить clean-host drill | L3/L4: backup без доказанной recoverability | [L05](../docs/adr/ADR-L05-backup-restore.md) |
| B07 Где ограничивать ресурсы? | Есть per-file/annotation caps, нет total quota/rate/cleanup | Edge+API permits+DB durable reservations/ledger; proxy-only недостаточен | Инженер L2/L3: измерить technical bounds, race/ENOSPC tests; значения product C06/C08 | L2/L4 K-S02/S05 остаются | [L06](../docs/adr/ADR-L06-resource-protection.md) |
| B08 Как принимать devices? | Только Chrome/CDP; физический protocol BLOCKED | Reuse K protocol, versioned evidence + Identity lifecycle; эмуляция дополнение | Инженер/tester L4 по C07 | L4 нет доказанного supported scope | [L07](../docs/adr/ADR-L07-device-browser-scope.md) |
| B09 Минимальные runbooks/logs? | traceId/errors есть, ops alerts/recovery не проверены | Bounded redacted logs, access audit, operator drills; большой monitoring stack не нужен | Инженер L3, реальный оператор C09: проверить delivery/runbooks | L3/L4 silent incidents/unknown recovery | [L08](../docs/adr/ADR-L08-operations-privacy.md) |

Технические решения не включают покупку сервера, выдачу реальных credentials, смену DNS, передачу данных провайдерам или изменение product requirements. Эти действия зависят от owner gates и отдельного разрешения соответствующего этапа.

## C — Owner decisions (все DECISION REQUIRED)

| ID / вопрос | Известные FACT / UNKNOWN | Рекомендованный вариант / альтернативы | Что именно решить / кто принимает | Что блокируется / последствия без решения | ADR |
|---|---|---|---|---|---|
| C01 Как создавать accounts/права? | Только demo seed; нет registration/admin. Реальные issuer/roster UNKNOWN | Закрытая группа, operator CLI, одна роль на account; альтернативы invitation/self-signup | Владелец: подтвердить manual модель и уполномоченного issuer; Student enrollment и Teacher course/group grants только по roster | **Старт L1**: onboarding/use cases нельзя выбрать за владельца. Реальный roster потребуется перед real provisioning | [L02](../docs/adr/ADR-L02-account-provisioning.md), [L01](../docs/adr/ADR-L01-identity-integration.md) |
| C02 Первый доступ и recovery? | Password/reset/SMTP отсутствуют; trusted contact UNKNOWN | Одноразовый activation/reset code от оператора, проверенный канал, без SMTP; альтернатива email invitations/recovery | Владелец: канал/проверка личности, ответственная роль и приемлемость ожидания оператора; при email определить provider/address policy | **Старт L1**: меняются credential flows/schema. SMTP закупка/config нужны до email acceptance, не выдумывать | [L02](../docs/adr/ADR-L02-account-provisioning.md) |
| C03 Конкретный server/domain? | D1/D2 приняты; VPS/domain/OS/ресурсы/budget/access UNKNOWN | Один host и один canonical origin; размер после C06 measurements; провайдера не выбираем | Владелец/оператор: host, OS/support, domain/DNS control, SSH, бюджет; secrets передавать вне Git | L3 live HTTPS/deploy, L4 target capacity; local design возможен | [L03](../docs/adr/ADR-L03-vps-deployment.md), [L04](../docs/adr/ADR-L04-https-secrets.md) |
| C04 Допустимые потеря данных/простой/backup окно? | Restore NOT TESTED, RPO/RTO UNKNOWN | Начать с простого coordinated backup, если приемлемы окна; экономный/учебный день/строгий варианты в ADR | Владелец: RPO, RTO, downtime и maintenance window; оператор подтверждает drill | L3 backup algorithm acceptance, L4 readiness; не гарантировать выбранные цели до измерения | [L05](../docs/adr/ADR-L05-backup-restore.md) |
| C05 Где backup и ключи? | Только local volumes; destination/custodian UNKNOWN | Encrypted off-host storage, отдельный key escrow и заместитель; альтернативы object storage/другой сервер | Владелец: место/юрисдикция, provider, бюджет, права/retention, key custodian/доступ восстановления | L3 off-host restore, L4 recovery; backup на единственном VPS недостаточен | [L05](../docs/adr/ADR-L05-backup-restore.md), [L04](../docs/adr/ADR-L04-https-secrets.md) |
| C06 Объём/нагрузка/квоты и usability budgets? | K single caller, Lesson N+1/dense canvas риски; целевая нагрузка UNKNOWN | Описать pilot workload, измерить, затем квоты; не произвольные defaults | Владелец: students/teachers/concurrency, assignments/photos/attempts, acceptable latency, storage budget, pending expiry (с C08) | L2 production quota acceptance, L3 capacity, L4 performance; fixture limits возможны раньше | [L06](../docs/adr/ADR-L06-resource-protection.md), [L03](../docs/adr/ADR-L03-vps-deployment.md) |
| C07 Кто участвует и какие devices/materials? | Safari/physical BLOCKED, display names/lookups/answers absent | Desktop-only если реально подходит; secure roster/teacher materials outside app как временный workaround; иначе tablet scope | Владелец: состав/размер группы без публикации персональных данных, OS/browser/input, tester, приемлемость ID/материалов workaround | L4 functional/device acceptance; если workaround неприемлем — отдельный enrichment scope до pilot | [L07](../docs/adr/ADR-L07-device-browser-scope.md), [L02](../docs/adr/ADR-L02-account-provisioning.md) |
| C08 Privacy/retention/удаление? | D4 ООО РФ и D5 применимое законодательство РФ ACCEPTED; originals/immutable history | Минимум данных, конечный retention, ADR-L09/L10; раскрыто PD-G02–14 | Владелец/ПД/юрист: основания, несовершеннолетние, поля/сроки, уничтожение, реальные потоки и обязательные evidence; оператор повторно не выбирается | **Любые real learner data**, L2 destructive retention, L4. Deactivation не выполняет erasure | [L08](../docs/adr/ADR-L08-operations-privacy.md), [L05](../docs/adr/ADR-L05-backup-restore.md) |
| C09 Кто эксплуатирует и получает alerts? | Нет назначенных server/backup/restore operators; SLA UNKNOWN | Один основной оператор + заместитель, проверенный канал, rehearsal | Владелец: имена/полномочия, доступность/реакция, updates/incident/backup/key duties; можно совмещать | L3 operational sign-off, L4; отсутствие ответственного не компенсируется green tests | [L08](../docs/adr/ADR-L08-operations-privacy.md) |

## Минимум для следующего запуска — L0.1 supersedes L0

После review L0.1 и отдельного разрешения L1 нужны C01/C02 плюс schema/workflow решения PD-G02/03/04/08 и принципы PD-G07/ответственная роль PD-G10 по [минимальному пакету](L01-DECISION-GATES.md). Основания и ADR согласуются до новых workflows. Локальная разработка только synthetic; полный live evidence остальных gates нужен до реальных данных, а не для isolated Identity test.

**D4/D5 ACCEPTED. Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY.** Все остальные открытые C/PD gates сохраняются. Действующие правовые редакции требуют проверки; [источники](../docs/compliance/PD-LEGAL-REQUIREMENTS.md).

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
| B10 | PROPOSED: Organization + course registry, LearningSpace как понятие; ADR-L11/L12, ORG-G01/07 |
| B11 | PROPOSED: ProcessingContext вне Domain, version/basis/authority; ADR-L13, ORG-G03/05/06 |
| B12 | PROPOSED: membership и scope isolation, одна основная role/account MVP; ADR-L14, ORG-G02/04 |

Все новые технические предложения требуют явного решения владельца до schema. C01 больше не спрашивает, нужен ли закрытый onboarding: он принят; issuer/канал остаются открыты. C07 возрастной scope уточнён, devices/capacity не решены. PD-G01 теперь включает реквизиты и полномочия A и P; PD-G02/12/14 проверяют цель и роли, а не заранее объявляют P оператором всех данных. [Полный пакет gates](L02-DECISION-GATES.md) дополняет C/PD, не закрывает их автоматически.
