# L0.2 — Identity, Actor и membership

**ACCEPTED ORG-G02 (L0.3), основная schema ACCEPTED T03.** ASP.NET Core Identity выбран ранее (D3), но ещё не реализован. [ADR-L14](../adr/ADR-L14-cross-organization-identity.md).

| Уровень | Назначение | Не даёт автоматически |
|---|---|---|
| IdentityUser | Credentials, login, recovery, sessions | Enrollment или право читать работу |
| ActorId | Стабильный автор учебных действий; IdentityUser→ActorId unique mapping | Профиль человека в каждой организации или общее досье |
| Organization membership | ActorId+OrganizationId, статус/revocation version; один active membership на пару | Доступ ко всем курсам организации |
| Educational role | Student/Teacher в membership, позже несколько permissions | Глобальные Teacher/Administrator права |
| Course/Group scope | Enrollment ученика; явное назначение преподавателя на курс/группу | Другой курс, организацию, экспорт, поддержку |
| Platform permissions | Provision organization, config, account maintenance, backup, incident operations | Education Management или чтение всех ПД |

Первый пилот сохраняет одну основную educational role/account, несколько независимых memberships; контекст каждого действия разрешается сервером. Но данные membership располагаются отдельно от глобальной роли Identity. Actor остаётся исторической идентичностью: его нельзя перепривязать к другому человеку после удаления account. Trusted adapter в будущем строит Actor(Id, Role) для выбранного и проверенного membership; current Actor signature не считается достаточной для scope — отдельный resolved request context обязателен на всех путях.

Будущее: тот же Actor может быть Student в A/B и Teacher в C. Добавляются memberships и разрешённый контекст/роль действия, а не сливаются истории. Роль в history описывает действие на момент записи; при смене membership не обновляется. Multi-role UI вне L1; прежнее исключение переключателя superseded E01: минимальный active-org switch входит в S4, не полноценная multi-org панель; ограничения provisioning явно отклоняют неподдержанную комбинацию. Снятие ограничения — отдельный этап, schema уже не полагается на единственную глобальную роль.

Закрытое provisioning T02 ACCEPTED: уполномоченный org admin создаёт account по roster через минимальный UI; platform operator создаёт первого org admin контролируемо. Account/mapping/audit атомарны, membership/enrollment/teacher grants — отдельные use cases. Нет public signup/mandatory email/phone; личная либо заранее подтверждённая индивидуальная передача кода. Shared-account recovery требует независимого account-security подтверждения; E04 ACCEPTED (independent platform security operator), юридическое представительство PD-G03 OPEN.

Каждый request локально проверяет JWT signature/lifetime/issuer/audience и scope, сопоставляет ресурс с organization/course/group/ownership. Fresh membership/grants/generation проверяются при issue/refresh; ordinary revoke bounded двумя часами по [ADR-L15](../adr/ADR-L15-jwt-delayed-revocation.md), A не отменяет B. E03 SecurityBlocked прекращает новую выдачу/refresh, existing JWT до exp; E06 synthetic restrictions отдельно, real freshness не двухчасовое правило. Frontend очищает queries/drafts/Blob URLs при account/context loss, cache keys включают actor/scope; скачанные bytes не отзываются.

## Один ученик в A и на курсах ООО B

Один login/Actor, два независимых membership и enrollment. Work A→course A→context A; Work B→course B→context B. Преподаватели, статистика, основания, сроки и erasure cases независимы. Ни A, ни B не получают список иных memberships или общий transcript по одному ActorId. UUID тоже корреляционный риск: не выдавать account mapping и чужие memberships через API/экспорт. Минимальный профиль/display name ведётся в scope организации, если будет утверждён сбор.

Запрос A удалить данные ученика не отменяет membership B и не уничтожает общий IdentityUser. Глобальная просьба закрыть account сначала отзывает сессии/credentials, затем маршрутизирует отдельные запросы по каждой цели/оператору. Сохранение учебных данных на самостоятельном основании рассматривается по каждому контексту; account deletion не доказательство уничтожения. Хранить минимальную связь для законного retained subset можно лишь при отдельном основании/сроке/допуске. Канал обращений доступен без входа; профиль родителя и teacher login не подменяют проверку представительства.

## L0.3 — детализация реализации

Принятые инварианты не означают реализованные controls. [План L1](../../codex/L03-L1-IMPLEMENTATION-PLAN.md) описывает принятые T01–T06 и будущие use cases/concurrency/tests; E01–E06 ACCEPTED в L0.4; O01 refresh coordination отдельно OPEN; [readiness](../../codex/L03-L1-READINESS.md) отделяет технические решения от правовых gates. Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY.

## B01 / S3 — immutable learning affiliation

2026-10-09: owner **B01 ACCEPTED**, S1/S2/O01 ACCEPTED, S3 AUTHORIZED only. A same-course enrollment move creates a new authority generation and preserves immutable group affiliation of existing work. New scoped assignments capture server-derived organization/course/student/group and enrollment/membership generations in AssignmentScopes; work/submission/review/attached-file roots inherit that affiliation, not the student’s mutable current group. Missing legacy sidecars deny new scoped access; no backfill is authorized. S4–S8 remain excluded. See [S3 authority design](L1-S3-SCOPED-AUTHORITY.md) for the protocol, role boundary and issuer transaction dependency.
