# M1-03 — Organization Bootstrap: результаты

Дата: 12.10.2026  
Статус: **DONE (component acceptance)**  
Owner decision: **`M1-03-BOOTSTRAP-01` принят**.  
Граница: только M1-03; commit/deploy не выполнялись, M1-04 не начинался.

## Итог

Системный оператор через production React UI и отдельный проверяемый platform JWT создаёт synthetic-only организацию и первого администратора без seed пользователя и без SQL как пользовательского шага. Создание атомарно; код активации раскрывается один раз. После активации администратор входит через существующий session/JWT механизм и видит только панель и audit своей организации.

## Вертикальная цепочка

`PlatformBootstrapPanel` → `POST /api/v1/platform/organizations` → named JWT scheme (`aud=sapientum-platform`, `scope=organization:bootstrap`) → `PostgresOrganizationBootstrap` → serializable transaction/advisory command lock → `Organization` + `OrganizationProfile` + `IdentityUser` + `Actor` + `ActorIdentity` + `AccountSecurityState` + `OrganizationMembership` + `AccessChallenge` + immutable receipt + `SecurityAudit` → commit.

`FirstAdminActivationPanel` → exact Origin + antiforgery + per-IP limiter → HMAC challenge verification → password hash + active identity + consumed challenge + audit → обычный `/auth/login` → существующий `PostgresComponentTokenIssuer` / `ComponentJwt` → `OrganizationAdminDashboard`.

Новая параллельная модель пользователя, membership, сессии, роли или audit не создавалась. Администратор имеет `EducationalRole = null`, именованный бит `ManageOrganization = 1` и пустой список educational grants.

## Security / isolation

- platform operator subject сохраняется только как SHA-256 hash в receipt/audit и не становится LMS Actor;
- operator/admin токены взаимно не подменяются; org-admin не создаёт вторую организацию;
- admin overview/audit берут organization и actor из валидированного component JWT, без browser-supplied foreign selector;
- foreign organization overview возвращает отсутствие результата, foreign audit — `Forbidden` до данных/counts;
- `ScopePolicy` не разрешает admin-only authority чтение образовательного ресурса;
- activation: 80 random bits, 16 Crockford Base32 symbols, 24 hours, HMAC-SHA256, five failures, fixed-time comparison, single use;
- `Cache-Control: no-store` установлен на bootstrap/activation/admin responses; код/JWT/password отсутствуют в audit;
- immutable receipt обеспечивает duplicate/lost-ACK/restart semantics; повтор не раскрывает secret.

## Реальный Chromium

Opt-in тест `OrganizationBootstrapBrowserTests` поднял реальный HTTPS Kestrel + production Vite bundle + PostgreSQL и прошёл:

1. открыть обычный login screen и перейти в «Первый запуск организации»;
2. ввести platform JWT, название и логин;
3. создать organization/admin и увидеть одноразовый код;
4. активировать администратора новым паролем;
5. войти через обычную форму;
6. увидеть org-admin dashboard, состояние Active и audit `first_admin_activated`;
7. DB oracle: один immutable receipt, одна активная identity, ноль enrollment/teacher grants.

Screenshots:

- `codex/evidence/M1-03-ORGANIZATION-BOOTSTRAP/01-create-organization.png`;
- `02-one-time-code.png`;
- `03-activate-admin.png`;
- `04-admin-dashboard.png`.

Панель сохраняет визуальный язык выбранного макета: светлая зелёная палитра, левый navigation rail, компактные метрики, cards, статус и журнал. Действия пользователей/курсов/групп показаны как будущие M1-04/M1-05 и не активированы.

## Проверки

| Suite | Результат |
|---|---:|
| Solution build | PASS, 0 warnings / 0 errors |
| Domain | 43 PASS |
| Application | 369 PASS |
| Infrastructure | 338 PASS |
| API full run | 376 PASS / 7 opt-in browser SKIP / 0 FAIL |
| M1-03 real Chromium opt-in | 1 PASS / 0 FAIL |
| Frontend Vitest | 411 PASS / 0 FAIL |
| Frontend production build | PASS |

Targeted tests additionally cover empty-schema migration/model parity, same-command replay, payload conflict, lost response/restart, immutable receipt, no secret in audit, pre-activation login denial, activation replay, exact expiry, five-attempt lock, own/foreign organization queries, forged/missing capability, missing Origin and operator/admin token separation.

## Изменённые контракты

- application: `OrganizationBootstrap.cs` — named permission, commands/results and ports;
- infrastructure: additive profile/receipt schema, migration/snapshot, bootstrap writer/admin queries, admin-capable reuse of strict component JWT;
- API: platform JWT scheme, create/activate/admin endpoints, Origin/CSRF/rate limit/error mapping;
- React: bootstrap form, activation form, org-admin dashboard, existing session gate support for admin-only component JWT;
- tests: PostgreSQL, HTTP and real Chromium harness; UI reference copied to `docs/ui/m1/`.

## Оставшиеся ограничения

- это component acceptance, не полный M1 E2E и не production deployment approval;
- strict isolated `c2h_runtime` bootstrap DB capability/credential ещё не квалифицированы; owner-backed disposable PostgreSQL доказан, deployment profile — отдельный gate;
- platform operator UI принимает уже выданный configured-authority JWT; полноценный IdP redirect/SSO UX и key-ring rotation не входят в M1-03;
- UI пока использует UUID организации при обычном входе; friendly organization picker/catalog относится к последующим заданиям;
- M1-04 roster/teacher/student/recovery и M1-05 course/group management намеренно отсутствуют.
