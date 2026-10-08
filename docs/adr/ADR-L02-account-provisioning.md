# ADR-L02 — Account provisioning

Статус: **PROPOSAL / DECISION REQUIRED C01–C02**. Связи: [реестр](../../codex/L-DECISION-REGISTER.md), [Identity](ADR-L01-identity-integration.md).

## FACT и повторное использование

[LocalDevelopment.Seed](../../src/Lms.Api/LocalDevelopment.cs) создаёт фиксированные demo Actor IDs, content, enrollment, teacher grant, lesson access и assignments. Это пример атомарного trusted provisioning, не production onboarding. [AccessFacts](../../src/Lms.Infrastructure/AccessFacts.cs) — технические records, отдельных реализованных Student/Teacher profile aggregates/таблиц нет. [LmsDbContext](../../src/Lms.Infrastructure/LmsDbContext.cs): enrollment key StudentId+CourseId, teacher grant Id с CourseId/GroupId. Учетная запись сама по себе не выдаёт доступ к курсу. [K gaps](../../codex/K-MVP-GAPS.md): нет trusted names, lookup/catalog endpoints и teacher-only answer source.

## Альтернативы и рекомендация

| Модель | Изменения и риски | Вывод |
|---|---|---|
| Ручное создание оператором через restricted CLI | Нужны проверенный roster, dry-run/import report, защищённая выдача доступа; ручные ошибки и нагрузка оператора | **Рекомендуется** для ограниченного пилота |
| Invitation workflow | Expiry/one-use invitation, delivery, resend, identity verification, возможно SMTP; не равен public registration | Отложить до роста группы; одноразовая activation выдача вручную всё ещё первая модель |
| Самостоятельная регистрация | Moderation, consent/abuse, неподтверждённые лица, credential recovery; роли не выбираются публично | Не включать в пилот без отдельного продуктового решения |

Не проектировать admin panel и не добавлять Admin в Domain.AuthorRole. Оператор — полномочие инфраструктуры (SSH/restricted deployment command), не Teacher с глобальными правами. Teachers создаёт только оператор по утверждённому списку. Ни public request, ни email domain не назначают Teacher.

## PROPOSAL — минимальный lifecycle

| Операция | Проект процедуры/атомарность | Проверка |
|---|---|---|
| Создать Student | UserManager, отдельный ActorId, mapping Student; по явному roster enrollment Course/Group, assignment на существующий immutable content; всё в transaction | Повторный import с тем же external provisioning key не дублирует account/assignment; конфликт останавливает импорт |
| Создать Teacher | UserManager + mapping Teacher, конкретные Course/Group grants; null group только при явной выдаче whole-course доступа | Login без grants не даёт queue/work; group grant не расширяется до course-wide |
| Enrollment/lesson | Использовать AccessEnrollments/AccessLessons и ProblemAssignment, валидировать exact version и уникальность назначения в provisioning | Никакой fake published content и автоматического создания ProblemWork |
| Выдать первый доступ | Предлагается одноразовый activation code, вручную через заранее проверенный канал; user задаёт собственный пароль | В БД hash code + expiry/consumedAt; consume + password setup атомарны, повтор/гонка отклонены |
| Сменить пароль | Identity password validators, current password, CSRF, stamp rotation и revoke sessions; затем явный relogin | Старый пароль и старые cookie больше не работают |
| Восстановить | Оператор проверяет личность по согласованной процедуре; выдаёт одноразовый reset code, аннулируя предыдущий | Generic public errors; оператор не видит/не узнаёт существующий пароль |
| Деактивировать | IsActive=false + revoke sessions + stamp rotation; enrollment/grants отключать по выбранному охвату (account либо один курс) | История и immutable actor attribution остаются; account deny проверяется и на downloads |
| Восстановить доступ после ошибки | Только оператор: повторно подтвердить identity и scope, новый code/session; не переиспользовать старую cookie | Повторное включение не оживляет отозванные sessions |

Activation/reset records — **PROPOSAL**, добавить в identity persistence после C02; один активный challenge на user/purpose, hashed secret, expiry, usedAt, ограничение попыток; UserManager token cryptography можно переиспользовать, но one-use consumption обеспечить явно. Не передавать коды через shell arguments, access logs, URL query или Git. Restricted command выдаёт код через защищённый канал, audit фиксирует факт, не значение. Numeric limits/expiry технически обосновать в L1; C02 определяет канал и ответственного.

Предпочтительно login-идентификаторы из roster, без обязательного личного email ребёнка; email recovery — альтернатива. При email варианте **UNKNOWN** SMTP provider, sender/domain verification, credentials, delivery/bounce process, кто владеет адресом (ученик/родитель), правила shared email. Это внешние C02/C03/C08 зависимости; не делать вид, что SMTP есть. Parent account/role не добавлять. Временный пароль с mandatory change — допустимая, но менее удобная для безопасной выдачи альтернатива, оператору не хранить его после передачи.

## Gates, risks и exit

**C01 владельцу:** закрытый roster, manual CLI и одна роль на account? Рекомендация да; нужен уполномоченный issuer и перечень course/group grants. Для разработки L1 достаточно синтетического roster и согласованной модели; персональные данные собрать позже по privacy gate.

**C02 владельцу:** manual verified activation/recovery без SMTP? Рекомендация да для маленькой группы при доступном операторе; пользователь зависит от его времени ответа. Самостоятельное email recovery увеличит L1 и потребует поставщика/адресов. Эти решения нельзя закрыть технически за владельца.

B02/B03: миграция/mapping/transaction/CSRF реализуются технически после разрешения L1. C07/C08 до real accounts: состав группы, минимальные имена, retention и privacy. Минимальный workaround — защищённый roster ActorId→имя у учителя плюс существующие IDs; его приемлемость решает владелец, не объявлять identity table готовым UI profile source.

Risks: ошибочная привязка чужой истории, повторный import, shared email, невозможность связаться с оператором, privileged CLI compromise. Dry-run показывает account/role/course/group/assignments; explicit validation, DB transaction, audit, least privilege. Тестировать partial failure и concurrent imports, code replay, Teacher role injection, deactivation после загрузки файлов, сохранение истории. Внешняя отправка приглашений и реальные users сейчас не создаются.


## L0.1 amendment — 2026-10-09 (приоритет для будущих этапов)

До активации нужны утверждённая цель/основание, минимальный набор полей и проверка представительства при применимости. Реквизиты/правила PD-G02/03/04 не выдумывать. Controlled provisioning/activation не заменяет отдельное согласие и доказательства. Внешний request channel доступен после deactivation.

Статус изменений **PROPOSAL**, кроме принятых D4/D5. [ADR-L09](ADR-L09-personal-data-lifecycle.md), [ADR-L10](ADR-L10-erasure-and-immutable-history.md), [lifecycle](../compliance/PD-LIFECYCLE-AND-ERASURE.md), [gates](../../codex/L01-DECISION-GATES.md). **Compliance NOT VERIFIED; pilot NOT_READY.**
