# L0.2 decision gates — актуализация L0.3

2026-10-09. Owner/evidence для принятых строк: владелец проекта, явное задание «Checkpoint L0.3: Owner Decisions & L1 Readiness», §§2–9. Это принятие границ, не evidence реализации. Исторический [L02-RESULTS](L02-RESULTS.md) не изменён.

Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY.

| Gate / status | Принятое решение / оставшаяся зависимость | Когда блокирует |
|---|---|---|
| ORG-G01 ACCEPTED | Organization обязательна, каждый Course принадлежит ровно одной; LearningSpace entity отсутствует в MVP; ADR-L11/L12 | Технические ключи и quarantine — ACCEPTED T03/T06; DDL review в L1 |
| ORG-G02 ACCEPTED | IdentityUser → Actor; OrganizationMembership отдельно от CourseEnrollment и TeacherAssignment; один login в нескольких организациях, одна основная Student/Teacher роль в MVP | Основные schema/constraints ACCEPTED T03; детали review; глобальная роль не заменяет scope |
| ORG-G03 ACCEPTED (architecture) | Версионированный ProcessingContext вне учебных агрегатов; связь с курсом; текущие ограничения сильнее исторической версии | Persistence T05 ACCEPTED; основания/сроки REQUIRE LEGAL REVIEW |
| ORG-G04 ACCEPTED | При обычном отзыве прекращение новых авторизованных операций в пределах 2 часов: JWT ≤15 минут, scoped AccessValidUntil и запас 10 минут; API 60 секунд / max HTTP 8 минут; A не отменяет B, admin не reader | T04 ACCEPTED; E01/E03/E06 ACCEPTED; implementation NOT VERIFIED, [ADR-L15](../docs/adr/ADR-L15-jwt-delayed-revocation.md) |
| ORG-G05 REQUIRE LEGAL REVIEW | Роли A/P по целям, договор LMS, поручение, собственные цели ООО, основания и обязанности сторон | Юридические workflows и любой real-data пилот; не изолированная synthetic Identity разработка |
| ORG-G06 ACCEPTED (architecture) | Scoped privileged erasure, approved request/manifest/evidence, A не удаляет B/общую Identity; restore не возвращает уничтоженное | Сроки, исключения, mixed-subject procedure REQUIRE LEGAL REVIEW; production erasure запрещён до проверки |
| ORG-G07 ACCEPTED | Аддитивные изменения, сохранение истории/IDs, явный synthetic mapping, подтверждённый real manifest, unknown quarantine | Последовательность T06 ACCEPTED; конкретные migrations NOT RUN; real mapping без manifest запрещён |
| ORG-G08 REQUIRE LEGAL REVIEW | 273-ФЗ, лицензирование, требования к сторонней организации, будущие курсы ООО | До соответствующей real образовательной деятельности; synthetic L1 не подтверждает применимость |
| C01/C02 ACCEPTED (business process) | Уполномоченный администратор образовательной организации выдаёт accounts по её списку; одноразовая активация/восстановление, проверенный индивидуальный канал, без обязательных email/телефона | T01/T02 ACCEPTED: минимальный UI/16-symbol code/параметры/проверка получателя; E02–E05 ACCEPTED; O01/G01–G04 отдельно |

PD-G01–14 остаются открытыми по [L01 gates](L01-DECISION-GATES.md). D1–D5/D4a–d/D6/D7 сохранены; юридическая квалификация D4 не стала LEGAL VERIFIED. Принятие архитектуры не утверждает пять отдельных таблиц ProcessingContext/Purpose/Basis/Restriction/Retention.

Следующий шаг: отдельно разрешить S1–S3 sequential synthetic foundations; O01 concurrent refresh policy решить до S4. E01–E06 и T01–T06 ACCEPTED, повторного согласования не требуют. [Readiness](L03-L1-READINESS.md). L1–L4 сейчас не разрешены.
