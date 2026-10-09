# L0.1 — Decision gates

> L0.2 amendment: исторический D4 уточнён D4a–d; A — предполагаемый оператор учебной цели, P — обработчик по поручению и оператор отдельно установленных собственных целей. [L02 gates](L02-DECISION-GATES.md) добавляются к этому пакету; прежние формулировки не отменяют новый scope.

Дата 2026-10-09. **Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY.** Названия ролей ниже не означают назначения людей. Все PD-G01–14 — **DECISION REQUIRED / OPEN**, никаких фиктивных PASS. [Нормативный реестр](../docs/compliance/PD-LEGAL-REQUIREMENTS.md) имеет статус LEGAL SOURCE NOT VERIFIED.

## Уже принято — не возвращать на согласование

| Решение | Статус / owner / evidence |
|---|---|
| D1 Свой VPS/сервер | ACCEPTED / владелец / задание L0, сохранено |
| D2 Docker Compose | ACCEPTED / владелец / задание L0, сохранено |
| D3 ASP.NET Core Identity | ACCEPTED / владелец / задание L0, сохранено |
| D4 Российское ООО владельца — оператор | **ACCEPTED** / владелец / явное задание L0.1 от 2026-10-09; UNKNOWN только реквизиты |
| D5 Соблюдение применимых требований 152-ФЗ и связанных актов до первого пилота с реальными ПД | **ACCEPTED** / владелец / явное задание L0.1; обязательный release gate, перенос на production запрещён |

## PD-G01 — Точные реквизиты уже выбранного ООО

| Поле | Значение |
|---|---|
| Owner | Владелец/директор |
| Recommendation | Передать юристу наименование, ИНН/ОГРН, адрес, подписанта и контакт ПД вне публичного Git |
| Alternatives | Выбор другого типа оператора не открыт; безопасный реестр вместо публикации внутренних контактов |
| Dependencies | D4 |
| Required evidence | Проверенные реквизиты/полномочия, готовые к заполнению документов |
| Status | DECISION REQUIRED / OPEN; evidence не получено |
| Когда блокирует | До любых real data/публикации |

## PD-G02 — Цели и правовые основания

| Поле | Значение |
|---|---|
| Owner | Директор + ПД + юрист |
| Recommendation | Утвердить B01–B12, модель договора/услуги; consent только для подходящих целей |
| Alternatives | Изменить цель/минимизировать либо исключить не обеспеченную основанием обработку |
| Dependencies | PD-G01; R01–R04 |
| Required evidence | Purpose-by-purpose legal memo/договор/правовое основание, сохранённая редакция нормы |
| Status | DECISION REQUIRED / OPEN; evidence не получено |
| Когда блокирует | До реализации basis workflows (вне synthetic L1) |

## PD-G03 — Несовершеннолетние и согласия

| Поле | Значение |
|---|---|
| Owner | Владелец + юрист/ПД |
| Recommendation | Определить возрастные группы, дееспособность, стороны договора, полномочия/минимальный канал подтверждения; версионированное отдельное согласие при необходимости |
| Alternatives | Взрослый-only пилот только по новому явному scope; иные способы подтверждения с анализом избыточности |
| Dependencies | PD-G02/C01/C02; R03/R04 |
| Required evidence | Заключение по группам и полномочиям, формы/сценарии выдачи и отзыва |
| Status | DECISION REQUIRED / OPEN; evidence не получено |
| Когда блокирует | До юридических schema/workflows; synthetic L1 ограничен L0.3 |

## PD-G04 — Минимальный набор данных

| Поле | Значение |
|---|---|
| Owner | Владелец + ПД |
| Recommendation | Field allowlist I01–I15, login вместо обязательного email/телефона; отдельное решение EXIF/originals |
| Alternatives | Trusted display name если нужен; новый очищающий photo pipeline только отдельным ADR |
| Dependencies | PD-G02/03; C07 |
| Required evidence | Построчная необходимость, DTO/schema review, решение по именам/EXIF/возрасту |
| Status | DECISION REQUIRED / OPEN; evidence не получено |
| Когда блокирует | До real-data полей/сбора; photos решение до соответствующей реализации |

## PD-G05 — Инфраструктура и потоки

| Поле | Значение |
|---|---|
| Owner | Владелец/ops/ПД |
| Recommendation | Весь управляемый контур в РФ, отключённые ненужные внешние сервисы; обследовать providers/support |
| Alternatives | Иностранный сервис лишь после отдельного анализа потока и ст. 12/18 |
| Dependencies | C03/C05; R05/R06 |
| Required evidence | Договоры/ЦОД/копии/egress/support, актуальный legal memo |
| Status | DECISION REQUIRED / OPEN; evidence не получено |
| Когда блокирует | До real infrastructure/data; L3 acceptance |

## PD-G06 — Категории ПД/угрозы/уровень

| Поле | Значение |
|---|---|
| Owner | ИБ + ПД, утверждает директор |
| Recommendation | Следовать методике classification, не выбирать УЗ произвольно; проверить действующий ФСТЭК акт |
| Alternatives | Иные меры/компенсации только в допустимом режиме с обоснованием |
| Dependencies | PD-G04/05; C06; R08/R09/R17 |
| Required evidence | Counts/categories/types, акт уровня, модель угроз/вреда, ведомость мер и эффективности |
| Status | DECISION REQUIRED / OPEN; evidence не получено |
| Когда блокирует | До окончательного security design и L4 |

## PD-G07 — Сроки хранения

| Поле | Значение |
|---|---|
| Owner | Владелец/ПД/юрист |
| Recommendation | Утвердить T1–T6: purpose/start/period/hold/deletion deadline для каждой категории |
| Alternatives | Сократить сбор/архив; законный hold отдельно, не бессрочное хранение |
| Dependencies | PD-G02/04; R10–R13 |
| Required evidence | Подписанный retention schedule со значениями, основаниями и владельцами |
| Status | DECISION REQUIRED / OPEN; evidence не получено |
| Когда блокирует | До destruction config L2; схема lifecycle L1 учитывает сроки |

## PD-G08 — Удаление/уничтожение vs history

| Поле | Значение |
|---|---|
| Owner | Владелец/ПД/ИБ/юрист |
| Recommendation | Архитектурные принципы ADR-L09/L10 ACCEPTED L0.3; согласовать процедуры варианта D: отдельный privileged manifest workflow, минимальные evidence |
| Alternatives | B только с доказанным обезличиванием; A недостаточно; ручной workflow лишь если доказаны сроки/полнота |
| Dependencies | PD-G02/07; R11–R13/R19 |
| Required evidence | Принятые ADR, правила mixed-subject/триггеров/доступа, затем synthetic erasure evidence |
| Status | DECISION REQUIRED / OPEN; evidence не получено |
| Когда блокирует | Граница ACCEPTED L0.3; процедуры/правовые основания до erasure реализации, проверка до пилота |

## PD-G09 — Данные в backups

| Поле | Значение |
|---|---|
| Owner | Владелец/ops/key custodian/ПД |
| Recommendation | Finite retention, timely deletion copies, независимый актуальный ledger и quarantine restore |
| Alternatives | Иная backup технология при доказанных сроках, законности и erasure; crypto-erasure только с доказательствами |
| Dependencies | PD-G05/07/08; C04/C05 |
| Required evidence | Retention/RPO/RTO/keys утверждены, provider proof, restore-before-erasure drill |
| Status | DECISION REQUIRED / OPEN; evidence не получено |
| Когда блокирует | До L3 acceptance, real-data pilot |

## PD-G10 — Ответственный за обработку

| Поле | Значение |
|---|---|
| Owner | Директор |
| Recommendation | Приказ по ст. 22.1, полномочия/ресурсы, заместитель; separation reviewer/executor |
| Alternatives | Совмещение ролей по оценке с контролем, не отсутствие ответственного |
| Dependencies | PD-G01; R07 |
| Required evidence | Подписанный приказ/должностные обязанности/контакт/допуски |
| Status | DECISION REQUIRED / OPEN; evidence не получено |
| Когда блокирует | Юридическая ответственная роль до соответствующего workflow; назначение до real data |

## PD-G11 — РКН уведомления

| Поле | Значение |
|---|---|
| Owner | ПД/юрист/подписант |
| Recommendation | Проверить исключения; для automated LMS планировать уведомление до начала, отдельное трансграничное при наличии |
| Alternatives | Освобождение только по подтверждённой норме/фактам |
| Dependencies | PD-G01/02/04/05/10; R14–R16 |
| Required evidence | Официальные тексты/формы, квитанции, актуальные сведения; неподанный draft не evidence |
| Status | DECISION REQUIRED / OPEN; evidence не получено |
| Когда блокирует | До начала real processing |

## PD-G12 — Документы/договоры

| Поле | Значение |
|---|---|
| Owner | Директор/ПД/юрист |
| Recommendation | Доработать A–E templates и реальные поручения, политику, обучение/допуски |
| Alternatives | Исключить ненужного provider/операции; иной договор по реальной роли |
| Dependencies | PD-G01–05/07/10; R02/R07 |
| Required evidence | Подписи/публикация/договоры/ознакомление, vendor assessment |
| Status | DECISION REQUIRED / OPEN; evidence не получено |
| Когда блокирует | До real data/L4 |

## PD-G13 — Инциденты

| Поле | Значение |
|---|---|
| Owner | Incident lead/ПД/ИБ, назначает директор |
| Recommendation | Утвердить таймеры по проверенной редакции, канал/подписант/заместитель, tabletop |
| Alternatives | Иной дежурный процесс с соблюдением срока; outsourcing по договору |
| Dependencies | PD-G05/10/11; R15/R16 |
| Required evidence | Runbook/контакты/сроки/правовая проверка и synthetic drill |
| Status | DECISION REQUIRED / OPEN; evidence не получено |
| Когда блокирует | До L3 operational acceptance/L4 |

## PD-G14 — Юридическая проверка перед пилотом

| Поле | Значение |
|---|---|
| Owner | Директор + квалифицированный юрист |
| Recommendation | Проверить официальные редакции на дату запуска и весь evidence packet; закрыть все обязательные gaps |
| Alternatives | Оставить synthetic-only до закрытия; waiver обязательной нормы не допускается |
| Dependencies | Все PD gates; L1–L4; R01–R20 |
| Required evidence | Подписанное заключение с scope/date/исключениями, evidence index, owner release approval |
| Status | DECISION REQUIRED / OPEN; evidence не получено |
| Когда блокирует | До READY_FOR_PILOT |

## Закрытие и последовательность

Запись решения включает gate/decision/owner/date, ссылку на проверяемое evidence, legal edition и affected ADR/tests. «Рекомендовано» не значит ACCEPTED; решение без implementation evidence не закрывает release control. G01/G10 actual реквизиты можно хранить в защищённом реестре с reference вместо избыточной публикации в Git. G14 не становится PASS из подписи владельца без официальных источников и доказательств.

C01/C02 ACCEPTED в бизнес-процессе L0.3; параметры T01/T02 ACCEPTED; дополнительные E02–E05 OPEN; C03/C05 связаны G05/G09; C06 — G06/G07; C08 раскрыт G02–09/G11–14 (выбор оператора/РФ уже закрыт D4/D5); C09 — G10/G13. Исторические вопросы L0 не отменяют принятые D4/D5.

## L0.3 — пакет до отдельного разрешения L1

Supersedes прежний минимальный пакет L0.1/L0.2: C01/C02 и ORG-G01–04/06/07 приняты. Уполномоченный администратор образовательной организации выдаёт accounts, одноразовые activation/recovery codes передаются лично либо по проверенному индивидуальному каналу. Отсутствие обязательных email/phone принято. Параметры и проверка получателя ACCEPTED T01/T02; дополнительные E02–E05 OPEN, юридическое представительство отдельно PD-G03.

[Readiness L0.3](L03-L1-READINESS.md) фиксирует принятые T01–T06, оставшиеся E01–E06 и необходимость отдельного разрешения. Отдельное разрешение L1 допускает только синтетические Identity/scopes/restriction contracts. PD-G02/03/04/07/08/10 блокируют конкретные правовые workflows и реальные данные, но не изолированную техническую Identity разработку без consent/legal-basis records. Все PD gates остаются OPEN; архитектурные принципы ADR-L09/L10 приняты, процедура уничтожения/сроки/представительство не утверждены.

## L0.2 — уточнение открытых gates

PD-G01: проверить реквизиты/подписантов A и P, хранить защищённо. PD-G02: purpose-by-purpose operator/processor и поручение, включая unresolved Identity/security. PD-G03: 5–8 классы уже приняты; возраст/дееспособность/родительские полномочия ещё требуют проверки. PD-G04: scoped profiles/membership/policy refs и EXIF минимум. PD-G05/06: потоки/угрозы межорганизационного доступа и техперсонала. PD-G07–09: сроки/holds/manifests по controller, общие backups не продлевают срок A до B. PD-G10: назначить ответственных каждой стороны и decision/executor/reviewer. PD-G11–13: уведомления/договоры/инциденты распределить по фактической роли. PD-G14: проверка обеих сторон и лицензирования по применимости.

Closed provisioning, issuer и бизнес-процесс C01/C02 ACCEPTED в L0.3; T01/T02 ACCEPTED; дополнительные E02–E05 OPEN. Не требовать повторно выбрать возрастной scope или второе юрлицо. Текущий пакет до L1 определён [L02 gates](L02-DECISION-GATES.md) и readiness L0.3; статус всех недоказанных controls OPEN.
