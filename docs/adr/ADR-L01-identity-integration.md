# ADR-L01 — ASP.NET Core Identity integration

Статус: **PROPOSAL**. D3 (ASP.NET Core Identity) уже принят владельцем; выбор cookie и детали ниже ещё не реализованы. Реестр: [B01–B03, C01–C02](../../codex/L-DECISION-REGISTER.md). Дата анализа: 2026-10-09, база `c7a500f`.

## FACT — существующая граница

- [Program.cs](../../src/Lms.Api/Program.cs) регистрирует JWT Bearer, `MapInboundClaims=false`, HTTPS discovery, issuer/audience/signature/lifetime validation. Без Authority/Audience production authentication закрыта. `UseAuthentication` создаёт principal до authorization; Identity services/store пока отсутствуют.
- [ClaimsActorMapper](../../src/Lms.Api/ActorMapper.cs) требует ровно одну authenticated identity, один непустой UUID `sub`, одну `role=Student|Teacher`. Возвращает [Actor](../../src/Lms.Application/Ports.cs), а не EF user. Identity NameIdentifier нельзя автоматически считать текущим `sub`.
- `[Authorize]` на [CommandsController](../../src/Lms.Api/CommandsController.cs), [QueriesController](../../src/Lms.Api/QueriesController.cs), [AttachmentsController](../../src/Lms.Api/AttachmentsController.cs) требует authentication. Именованных ASP.NET authorization policies нет. Роли проверяются в `ProblemWorkService`, query adapters и teacher alias; scopes — [PostgresAccessPolicy](../../src/Lms.Infrastructure/PostgresAccessPolicy.cs), query/queue filters, не JWT.
- Student требует своё active assignment, enrollment и доступный exact-version lesson. Teacher course-wide grant (`GroupId=null`) не требует student enrollment/availability; group grant требует active matching membership. Это C4-семантика, не дефект, который надо молча менять.
- Все 13 business routes используют bearer: четыре command POST, шесть query GET (включая teacher alias), POST uploads, GET content, DELETE attachment. Полный перечень в [L architecture](../../codex/L-ARCHITECTURE.md). `/api/health` anonymous.
- [browserClient.ts](../../src/Lms.Web/src/api/browserClient.ts) получает token от `window.lmsAuth`; [client.ts](../../src/Lms.Web/src/api/client.ts) добавляет Authorization, использует same-origin fetch и XHR multipart, обрабатывает 401, не делает refresh/retry. [main.tsx](../../src/Lms.Web/src/main.tsx) не содержит session provider. Данные экрана и drafts живут в компонентах: смена аккаунта требует remount, одного обновления token недостаточно.
- [LocalDevelopment](../../src/Lms.Api/LocalDevelopment.cs) допускает seed/`--dev-token` только при Development AND LocalDemo.Enabled. Token живёт 8 часов. [local-login.html](../../docker/local-login.html) сохраняет его в sessionStorage, [local-auth.js](../../docker/local-auth.js) подключён только Docker target local-development. [production target](../../src/Lms.Web/Dockerfile) этих файлов не включает. Это dev tooling, не действующий user login API.

## PROPOSAL — выбор transport

| Вопрос | A: Identity cookie | B: bearer |
|---|---|---|
| Выдача/хранение | SignInManager; HttpOnly, Secure, host-only cookie, Path=/, SameSite=Lax; желательно `__Host-` name | Identity token handler либо отдельная JWT выдача; access только в памяти, refresh в HttpOnly cookie |
| Same-origin SPA | Fetch уже same-origin; добавить session bootstrap и CSRF к fetch/XHR | Сохраняет getAccessToken, но выдача/refresh/lifecycle сейчас отсутствуют |
| CSRF | Обязательный antiforgery header для всех unsafe methods, включая login/logout/upload/password/reset | Header access token не ambient credential; refresh-cookie endpoints всё равно требуют CSRF |
| Lifetime | Session cookie, явные idle и absolute limits; закрытие браузера не считается гарантированным logout | Короткий access lifetime + refresh lifetime, rotation/replay detection; memory теряется при reload |
| Отзыв | DB validation session/account/stamp на каждом запросе | JWT до expiry остаётся действителен без DB deny/session check; refresh revocation недостаточно |
| Сложность | Одна browser-схема, существующие handlers и небольшой session registry | Дополнительные token issuance/refresh/races; сложнее сохранить no-retry semantics |

**Рекомендация:** A для единственного same-origin browser client. Это технический B01, не требует отдельного решения владельца, пока нет требования native/API clients или cross-origin. B — разумная альтернатива при таком новом требовании, не текущая необходимость. Identity встроенные bearer tokens не являются JWT и не совместимы автоматически с существующим JwtBearer handler ([Microsoft](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0)). Не вызывать публичный `MapIdentityApi` целиком: это могло бы открыть ненужную регистрацию.

## PROPOSAL — persistence и mapping

Все новые имена в этом разделе — проект, а не существующие классы/таблицы.

1. `LmsIdentityUser : IdentityUser<Guid>` — credentials, lockout, security stamp. UUID Identity генерируется независимо от ActorId. IdentityUser **не заменяет** Student/Teacher/Actor; существующие author/owner IDs никогда не переписываются.
2. `ActorIdentities`: IdentityUserId PK/FK → AspNetUsers (RESTRICT), ActorId UNIQUE NOT NULL/nonempty UUID, PrimaryEducationalRole nullable CHECK Student|Teacher (в Actors по T03), IsActive, provision audit timestamps. Один user → один Actor, один Actor → один user. PrimaryEducationalRole здесь ограничивает MVP одной Student/Teacher ролью; effective role и полномочия разрешаются через membership и course/group grants по принятому ORG-G02. Admin-only actor допускается без educational role. Разные роли в организациях в будущем не меняют Identity; в MVP конфликтующая роль отклоняется.
3. `IdentitySessions`: случайный непрогнозируемый session identifier (в БД hash), UserId FK RESTRICT, created/lastSeen/absoluteExpiry/revokedAt; index user/expiry. Это технические сессии, не Domain aggregate. Cookie несёт защищённый session id. При password reset/deactivation — stamp rotation и revoke всех sessions; logout отзывает текущую server session, не только стирает cookie. Хранить только необходимые audit данные.
4. Стандартные Identity user/claim/login/token/role tables при использовании IdentityDbContext — AspNetUsers, AspNetUserClaims, AspNetUserLogins, AspNetUserTokens, AspNetRoles, AspNetRoleClaims, AspNetUserRoles. Не создавать публичный role-management API; unused role tables не дают права Teacher. Нормализованный login уникален; email опционален при operator recovery, уникальность email определяется C02, нельзя считать общий родительский email уникальной личностью ребёнка.
5. Предпочтительно один `LmsDbContext`, расширенный IdentityDbContext с `base.OnModelCreating` и нынешними mappings: одна БД/миграционная цепочка и одна provisioning transaction. Альтернатива — отдельный Identity context/schema в той же БД: лучше модульная изоляция, но нужно координировать транзакции и migrations. Отдельная БД для малого пилота не оправдана. Domain/Application references к Identity не добавлять.

Principal factory выпускает отдельный Identity user id и доверенные actor claims после DB lookup. На request boundary проверить session, stamp, active mapping; только затем передавать Actor в existing services. `IActorMapper` можно сохранить синхронным: async DB check выполняет cookie validation, mapper читает только верифицированные claims. Запретить смешение нескольких schemes/identities. Не доверять Role/ActorId из login JSON, URL или браузера.

Identity security-stamp validation периодична по умолчанию; её наличие само по себе не даёт немедленного отзыва ([Microsoft configuration](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-10.0)). Для пилота предлагаем проверять session/active/stamp каждый запрос, без положительного межзапросного cache. Это цена дополнительного DB read; измерить L4. ORG-G04 требует повторной проверки перед commit и revocation barrier. PROPOSED протокол L0.3 сериализует commit/revoke, прекращает новые допуски и дожидается старых streams до подтверждения отзыва; текущий код этого не реализует. DB unavailable → fail closed (503), не считать это успешной авторизацией.

## PROPOSAL — API/frontend integration

Новые routes-кандидаты под `/api/v1/auth`: GET session (минимум actorId/role/expiry), GET csrf (no-store request token), POST login/logout/change-password/activate/reset-password. Названия/DTO зафиксировать в L1 contract tests. Activation/reset набор зависит от C02. Полные Identity entities клиенту не отдавать. Ошибки согласовать с текущим Problem Details; 401/403 JSON, без redirect на HTML login.

CSRF token получить до anonymous login; после входа/выхода/смены account обновить. Валидировать и JSON, и multipart POST, и DELETE до изменения состояния; token в header, не в URL. SameSite — дополнительный барьер, не замена antiforgery. [Microsoft antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0) описывает cookie/request token pair; текущая регистрация AddControllers сама по себе не является готовой CSRF-защитой.

Session provider над App: при startup/focus проверяет session; на 401 блокирует mutations, aborts requests, отзывает Blob URLs, очищает данные предыдущего actor и remounts. Cross-tab logout/account-change сигнал без credentials плюс authoritative session reread. После login получить свежие work/version; не replay Submit/Review/Upload автоматически. Draft при обычной validation error сохраняется как сейчас, но при account switch очищается; предупредить о потере несохранённого текста/аннотаций. Cookie expiry, восстановление вкладки, browser session restore проверять отдельно. Return URL только local allowlisted path.

Pilot cookie-only; dev bearer оставить отдельным явно Development режимом для K tooling. Не делать production fallback на demo/JWT. Потребуются обновления transport tests, auth fixtures и Identity E2E, но existing command/read DTO/state machine сохраняются. Не утверждать bearer wire compatibility для pilot: это осознанная замена host authentication; неизвестные внешние consumers — UNKNOWN, уточнить только если появятся.

## Migration, risks, exit invariants

Additive migration после `20261008154119_ReviewAnnotations`; сначала dry-run на копии K DB и чистая установка. Проверить model snapshot без ALTER/DROP старых history/content tables. Инвентаризировать все StudentId/TeacherId/AuthorId/OwnerId: нет actor registry для автоматического trusted backfill. Создавать mapping только по подтверждённому roster; orphan/legacy IDs отчётить, не присваивать произвольному пользователю. Demo identities не переносить в real pilot. Не добавлять cascade FK из истории в Identity и не создавать пароль по существующему UUID.

Provision user/mapping/audit атомарно в одной БД, membership/enrollment/teacher assignment — отдельные авторизованные use cases; первичный доступ только после commit. Изменять ActorId после использования запретить; роль менять только отдельной проверенной operator procedure с revoke, не обычным profile edit. Rollback app разрешён только при доказанной schema/auth compatibility; иначе maintenance и restore, не destructive Down.

Tests L1: duplicate mapping/empty actor/multiple roles запрещены; login не создаёт work/enrollment; Student не становится Teacher; чужой scope и attached file denied; logout replay/expiry/reset/deactivation отклонены; CSRF missing/wrong/cross-site для всех mutations; две вкладки не показывают чужие данные; restart с сохранёнными keys и DB корректен; legacy attempts/annotations/hash неизменны. Baseline tests JWT сохраняются в dev mode, реальные Identity tests проходят через настоящий handler.

Dependencies: C01 provisioning и C02 initial/recovery policy для L1; B05 keys/proxy для L3; capacity подтверждается L4. Точные timeouts/password rules — техническая конфигурация, предложить и измерить в L1 с учётом длины занятия, не объявлять defaults принятыми в L0.


## L0.1 amendment — 2026-10-09 (приоритет для будущих этапов)

Identity mapping/session дополнены минимальным basis/case/privacy boundary по ADR-L09. C01/C02 недостаточно для новых privacy workflows: нужен минимальный пакет PD gates. Deactivation не erasure; account disable не блокирует whole-course Teacher чтение. Не собирать стандартные Identity email/phone без необходимости.

Статус изменений **PROPOSAL**, кроме принятых D4/D5. [ADR-L09](ADR-L09-personal-data-lifecycle.md), [ADR-L10](ADR-L10-erasure-and-immutable-history.md), [lifecycle](../compliance/PD-LIFECYCLE-AND-ERASURE.md), [gates](../../codex/L01-DECISION-GATES.md). **Compliance NOT VERIFIED; pilot NOT_READY.**

## L0.3 superseding decisions

C01/C02 и ORG-G01–04/06/07 ACCEPTED в своих архитектурных/бизнес-границах. [План L1](../../codex/L03-L1-IMPLEMENTATION-PLAN.md) уточняет прежние schema/API предложения и T01–T06. L1 — Identity, Organization Scopes & Access Lifecycle на synthetic data; legal basis/consent/representative/case/erasure workflow не реализуется до отдельного legal review. Все physical schema/transport details PROPOSED. Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY.
