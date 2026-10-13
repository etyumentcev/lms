# M1-03 — первоначальный запуск организации

Этот runbook описывает только synthetic-only M1-03. Он не создаёт учеников, преподавателей, курсы или группы и не заменяет обычную авторизацию/сессии.

## Предварительная конфигурация

1. Применить штатные EF migrations к пустой или существующей PostgreSQL БД.
2. Включить `TeacherRevocation:Enabled=true` и задать один точный HTTPS origin в `TeacherRevocation:AllowedOrigin`.
3. Включить `OrganizationBootstrap:Enabled=true`.
4. Передать `OrganizationBootstrap:ChallengeKey` как base64 от не менее чем 32 случайных байт и положительный `OrganizationBootstrap:ChallengeKeyVersion`. Ключ хранится только в secret manager; его нельзя размещать в репозитории, логах или browser storage.
5. Настроить доверенный OIDC authority и `Authentication:PlatformAudience=sapientum-platform`. Authority выдаёт platform operator JWT с `scope=organization:bootstrap`. Этот токен не является component JWT Sapientum и не содержит образовательных scopes.
6. Signing key/issuer/audience существующего component JWT и browser session configuration остаются прежними.

До истечения всех выданных 24-часовых кодов нельзя удалять активный HMAC key. M1-03 хранит `KeyVersion`, но текущий компонент принимает один активный ключ; полноценный key ring/rotation — отдельная operational qualification.

## Штатный пользовательский сценарий

1. Открыть обычный React UI по настроенному HTTPS origin и выбрать «Первый запуск организации».
2. Указать название, логин первого администратора и platform operator JWT. Нажать «Создать организацию».
3. Сохранить показанный 16-символьный Crockford Base32 код в защищённом канале. Он показывается один раз; повтор той же команды возвращает receipt без секрета.
4. Перейти к активации, указать новый пароль и подтвердить. Код живёт 24 часа и блокируется после пяти неверных попыток.
5. Перейти к обычному входу, указать логин, пароль и UUID созданной организации. После входа открывается панель администратора организации и её журнал.

В эксплуатационном процессе не требуются SQL INSERT/UPDATE и предварительный seed пользователя. Потерянный HTTP-ответ после commit проверяется повтором того же `commandId`: новая организация не создаётся, код не раскрывается повторно.

## Проверяемые границы

- platform operator может только вызвать bootstrap endpoint; он не становится Actor/member и не принимается component endpoints;
- первый администратор использует существующие `IdentityUser`, `ActorIdentity`, `OrganizationMembership`, имеет `EducationalRole=null` и только `ManageOrganization=1`;
- org-admin token не принимается platform bootstrap endpoint и не содержит Student/Teacher grants;
- overview и audit не принимают organization selector из браузера: organization/actor берутся из проверенного component JWT;
- `SecurityAudit` и immutable bootstrap receipt не содержат кода, password или JWT;
- roster/recovery/teacher/student/course/group operations отсутствуют до M1-04/M1-05.

## Диагностика

- `401`: подпись/issuer/audience/lifetime platform JWT либо component JWT не прошли проверку;
- `403`: неверный Origin/CSRF, отсутствует `organization:bootstrap`, попытка подменить operator/admin контекст;
- `409 IdempotencyConflict`: тот же `commandId` применён к другому payload/operator;
- `409 DuplicateIdentity`: нормализованный логин уже существует;
- `401 InvalidActivationCode`, `409 ActivationLocked`, `410 ActivationExpired`: состояния одноразового кода;
- любой `5xx` после create считается unknown outcome: повторить тот же `commandId`, не генерировать новый до чтения receipt.

## Deployment gate

Компонент и browser acceptance проверены на owner-backed disposable PostgreSQL. Строгий isolated профиль `c2h_runtime` из M1-01 не расширялся прямым DML на identity/organization/audit; для production deployment требуется отдельная квалификация узкой bootstrap DB capability/credential и read capability org-admin audit. Не включать endpoint на таком профиле, пока preflight не доказал требуемые права и отсутствие лишних DML/owner/superuser полномочий.
