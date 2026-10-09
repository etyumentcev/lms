# L0.2 decision gates — актуализация L0.3

2026-10-09. Owner/evidence для принятых строк: владелец проекта, явное задание «Checkpoint L0.3: Owner Decisions & L1 Readiness», §§2–9. Это принятие границ, не evidence реализации. Исторический [L02-RESULTS](L02-RESULTS.md) не изменён.

Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY.

| Gate / status | Принятое решение / оставшаяся зависимость | Когда блокирует |
|---|---|---|
| ORG-G01 ACCEPTED | Organization обязательна, каждый Course принадлежит ровно одной; LearningSpace entity отсутствует в MVP; ADR-L11/L12 | Технические ключи и quarantine — PROPOSED T03/T06 |
| ORG-G02 ACCEPTED | IdentityUser → Actor; OrganizationMembership отдельно от CourseEnrollment и TeacherAssignment; один login в нескольких организациях, одна основная Student/Teacher роль в MVP | Schema/constraints PROPOSED; глобальная роль не заменяет scope |
| ORG-G03 ACCEPTED (architecture) | Версионированный ProcessingContext вне учебных агрегатов; связь с курсом; текущие ограничения сильнее исторической версии | Persistence T05 PROPOSED; основания/сроки REQUIRE LEGAL REVIEW |
| ORG-G04 ACCEPTED | Server-side organization/course/group isolation; admin без автоматического чтения работ; актуальные file checks; commit-time check и отзыв | T04 PROPOSED; implementation/tests NOT VERIFIED |
| ORG-G05 REQUIRE LEGAL REVIEW | Роли A/P по целям, договор LMS, поручение, собственные цели ООО, основания и обязанности сторон | Юридические workflows и любой real-data пилот; не изолированная synthetic Identity разработка |
| ORG-G06 ACCEPTED (architecture) | Scoped privileged erasure, approved request/manifest/evidence, A не удаляет B/общую Identity; restore не возвращает уничтоженное | Сроки, исключения, mixed-subject procedure REQUIRE LEGAL REVIEW; production erasure запрещён до проверки |
| ORG-G07 ACCEPTED | Аддитивные изменения, сохранение истории/IDs, явный synthetic mapping, подтверждённый real manifest, unknown quarantine | Конкретная миграция T06 PROPOSED; real mapping без manifest запрещён |
| ORG-G08 REQUIRE LEGAL REVIEW | 273-ФЗ, лицензирование, требования к сторонней организации, будущие курсы ООО | До соответствующей real образовательной деятельности; synthetic L1 не подтверждает применимость |
| C01/C02 ACCEPTED (business process) | Уполномоченный администратор образовательной организации выдаёт accounts по её списку; одноразовая активация/восстановление, проверенный индивидуальный канал, без обязательных email/телефона | T01/T02 PROPOSED: механизм, интерфейс, параметры, проверка получателя, evidence, компрометация канала |

PD-G01–14 остаются открытыми по [L01 gates](L01-DECISION-GATES.md). D1–D5/D4a–d/D6/D7 сохранены; юридическая квалификация D4 не стала LEGAL VERIFIED. Принятие архитектуры не утверждает пять отдельных таблиц ProcessingContext/Purpose/Basis/Restriction/Retention.

Следующий шаг: рассмотреть [технический пакет T01–T06](L03-L1-READINESS.md), затем отдельно разрешить synthetic-only L1. [Исполнимый план](L03-L1-IMPLEMENTATION-PLAN.md). В L0.3 L1–L4 не запускаются.
