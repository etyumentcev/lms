# L0.2 — Identity, Actor и membership

**PROPOSED / ORG-G02 OPEN.** ASP.NET Core Identity выбран ранее (D3), но ещё не реализован. [ADR-L14](../adr/ADR-L14-cross-organization-identity.md).

| Уровень | Назначение | Не даёт автоматически |
|---|---|---|
| IdentityUser | Credentials, login, recovery, sessions | Enrollment или право читать работу |
| ActorId | Стабильный автор учебных действий; IdentityUser→ActorId unique mapping | Профиль человека в каждой организации или общее досье |
| Organization membership | ActorId+OrganizationId, статус/revocation version; один active membership на пару | Доступ ко всем курсам организации |
| Educational role | Student/Teacher в membership, позже несколько permissions | Глобальные Teacher/Administrator права |
| Course/Group scope | Enrollment ученика; явное назначение преподавателя на курс/группу | Другой курс, организацию, экспорт, поддержку |
| Platform permissions | Provision organization, config, account maintenance, backup, incident operations | Education Management или чтение всех ПД |

Первый пилот сохраняет одну основную educational role/account, один active organization context. Но данные membership располагаются отдельно от глобальной роли Identity. Actor остаётся исторической идентичностью: его нельзя перепривязать к другому человеку после удаления account. Trusted adapter в будущем строит Actor(Id, Role) для выбранного и проверенного membership; current Actor signature не считается достаточной для scope — отдельный resolved request context обязателен на всех путях.

Будущее: тот же Actor может быть Student в A/B и Teacher в C. Добавляются memberships и разрешённый контекст/роль действия, а не сливаются истории. Роль в history описывает действие на момент записи; при смене membership не обновляется. Multi-role UI и переключатель организаций не входят в L0.2/первый пилот; ограничения provisioning явно отклоняют неподдержанную комбинацию. Снятие ограничения — отдельный этап, schema уже не полагается на единственную глобальную роль.

Закрытое provisioning: образовательная организация утверждает roster и scopes; уполномоченный issuer ООО технически исполняет ограниченное поручение. Создание login не даёт issuer права выбирать себе Teacher или зачислять произвольных учеников. Атомарность account/mapping/membership/grants, dry-run, approved request reference, rollback и audit обязательны в L1. Канал activation/recovery, проверка личности и назначение issuer ещё C01/C02; закрытая выдача уже ACCEPTED, публичной регистрации нет.

Каждый request проверяет session/account, active membership, scope и privacy. Membership revoke не удаляет account, общие sessions могут оставаться для B, но A становится недоступна; global account compromise отзывает все sessions. Кэш и токен не источник актуальных grants. Для operation, пересекающей revoke, нужна повторная проверка/serialization; правило завершения и stream drain — [isolation](CROSS-ORGANIZATION-ISOLATION.md). Frontend сбрасывает queries/drafts/Blob URLs при logout/context loss, cache keys включают account и scope; уже скачанную копию невозможно отозвать технически.

## Один ученик в A и на курсах ООО B

Один login/Actor, два независимых membership и enrollment. Work A→course A→context A; Work B→course B→context B. Преподаватели, статистика, основания, сроки и erasure cases независимы. Ни A, ни B не получают список иных memberships или общий transcript по одному ActorId. UUID тоже корреляционный риск: не выдавать account mapping и чужие memberships через API/экспорт. Минимальный профиль/display name ведётся в scope организации, если будет утверждён сбор.

Запрос A удалить данные ученика не отменяет membership B и не уничтожает общий IdentityUser. Глобальная просьба закрыть account сначала отзывает сессии/credentials, затем маршрутизирует отдельные запросы по каждой цели/оператору. Сохранение учебных данных на самостоятельном основании рассматривается по каждому контексту; account deletion не доказательство уничтожения. Хранить минимальную связь для законного retained subset можно лишь при отдельном основании/сроке/допуске. Канал обращений доступен без входа; профиль родителя и teacher login не подменяют проверку представительства.
