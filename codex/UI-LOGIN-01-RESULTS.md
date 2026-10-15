# UI-LOGIN-01 — результаты

Дата: 2026-10-14
Checkout: `/home/tea/github/lms`, branch `main`
Итог после решения владельца 15.10.2026: **PARTIALLY ACCEPTED — изменения основной формы логина приняты; отдельная страница первого/административного входа и её операции NOT ACCEPTED и являются обязательным DEPLOY BLOCKER. До закрытия блокера deploy запрещён.**

## Owner acceptance override — 2026-10-15

Владелец принял изменения формы логина, но отклонил готовность отдельной страницы первого
входа: страница и операции доступны любому пользователю, который попал на этот URL. UUID и
непубличность маршрута уменьшают discoverability, но не создают authorization boundary.

Исправление в этой итерации сознательно не выполняется. До любого deploy необходимо:

1. ввести серверную проверку права открыть страницу и независимо авторизовать каждую операцию;
2. сохранить отдельный доверенный bootstrap/activation/security-operator lifecycle, CSRF/Origin,
   audit, ProcessingRestrictions и fail-closed поведение;
3. доказать запрет прямого URL-доступа и вызова операций для anonymous, обычного пользователя,
   неверной роли/организации/контекста и отозванных полномочий, с positive controls;
4. повторить browser/API regression и получить отдельную owner acceptance этой границы.

Результаты тестов от 14.10.2026 ниже сохранены как историческое техническое evidence. Они
проверяли маршрутизацию, сокрытие UUID и существующие operation checks, но не закрывают новый
owner finding и не разрешают deploy.

## Scope и источник авторизации

Реализована фактическая спецификация `codex/SAPIENTUM-UI-LOGIN-01.md` (указанного в запросе `codex/Sapientum-ui-login.md` в canonical checkout нет). Приложенный PNG использован только как визуальный ориентир, не как источник инструкций или security authority.

Источники authority:

1. owner decision `UI-LOGIN-01 / AUTH API`, APPROVED 14.10.2026 — credential-first login, server-resolved organizations, single-org auto-select, protected multi-org selection, zero-org outcome и адаптация login/refresh/switch;
2. последующее явное разрешение владельца №1 — узкий runtime `SELECT` для человекочитаемых названий организаций;
3. последующее явное разрешение владельца №2 — секретный UUID-маршрут административного входа из server-side environment config.

Требуемые `AGENTS.md` skills `model-domain`, `implement-use-case`, `implement-feature` отсутствуют в доступном каталоге skills; их выполнение не заявляется. Схема PostgreSQL, роли, grants приложения, M1 learning functions и production deployment не менялись; добавлен только одобренный table-level `SELECT` существующей роли `c2h_runtime` на существующую таблицу `OrganizationProfiles`.

## Actor → Use Case → Invariants → Entities/VO → States → Matrix → Tests

- Actor: пользователь с выданными credentials; пользователь с active membership в одной/нескольких организациях; пользователь с refresh cookie; пользователь без active membership; отдельно platform/bootstrap и account-security operators со своими существующими credentials.
- Use cases: вход только по логину/паролю; server-side определение организаций; auto-select одной; выбор нескольких по названиям; cookie restore; switch; logout. Служебные bootstrap/first-admin/security flows на непубличном UUID URL реализованы технически, но их access boundary NOT ACCEPTED.
- Invariants: пароль/access JWT не попадают в URL/localStorage; refresh остаётся HttpOnly Secure cookie; список организаций не выдаётся до успешной credential verification; UUID организации — selector, не authority; JWT выдаётся только после fresh membership/authority read; CSRF, Origin, S2/S3/S4, revocation, audit и ProcessingRestrictions сохранены. Требование «знание admin URL не даёт доступ к странице/операциям» сейчас **не доказано и заблокировано**.
- Entities/VO: существующие `IdentitySession`, `RefreshVerifier`, `OrganizationMembership`, `Organization`, `OrganizationProfile`, `CurrentAuthority`; добавлены application DTO `OrganizationOption`, `BrowserSessionResult` и outcome `NoOrganizations`. Новых persistence entities нет.
- State transitions: `starting → restore → authenticated | selecting | anonymous`; login invalid → 401/no cookie/no org disclosure; zero org → 403/no successor; one org → auto-selected; many → existing short-lived refresh session without access JWT until selection; switch → rotation + current membership revalidation; logout → revoke + client cleanup.
- Authorization/data sources: credentials через existing S2 account locks; options только active membership + enabled synthetic organization + `OrganizationProfile.Name`; selected org снова проверяется под organization lock. Для admin/first-entry страницы и каждой операции принятая server-side authorization boundary отсутствует; прежних проверок недостаточно для снятия blocker.
- Tests: React build/full suite; HTTP positive/negative; S2/S3/session regression; explicit ACL positive/negative; production Nginx HTTPS; two real Chromium journeys; diff/syntax/evidence checks.

## Write semantics

- Login transaction owner остаётся `PostgresIdentitySessions.AuthenticateAndIssue`: credential locks/check выполняются до чтения options; session/verifier/audit и выбор org находятся в одной transaction. Zero organizations бросает внутренний outcome и откатывает session/verifier/audit; наружу 403 без cookie/JWT.
- Refresh/switch остаются в `RotateAndIssue`: account/session/verifier locks, generation/version CAS, consume → successor, audit и unknown-commit handling не менялись. Client selector проверяется по текущему server list; затем выполняются organization lock и fresh authority read.
- Multi-org не создаёт вторую систему сессий: до выбора используется существующая короткоживущая refresh-cookie session, access JWT отсутствует.
- Retry semantics сохранены: refresh replay 409; network/5xx не повторяется как mutation автоматически; UI предлагает восстановление. Context switch/logout/recover очищают token, session resources и Blob URLs.
- Admin URL не является write authority. Прежние endpoint-level authentication/CSRF/idempotency/audit contracts сохранены, но их достаточность для доступа со страницы и каждой операции **NOT ACCEPTED**; required negative-access evidence отсутствует.

## Выполненные требования

| Требование | Статус | Evidence |
|---|---|---|
| Hero и adaptive layout | DONE | Chromium screenshots 1440/1280/768/390; исходный hero SHA не изменён. |
| Admin links на secret UUID route | **BLOCKED / NOT ACCEPTED** | Ссылки удалены с login, однако известный URL открывает страницу и операции любому попавшему на него пользователю. Сокрытие UUID не является авторизацией. Обязательный deploy blocker. |
| Заголовок «Вход», без Help/RU/current-session button | DONE | DOM/Vitest/real Chromium. |
| Login без organizationId | DONE | React отправляет только `{ userName, password }`; legacy optional selector сохранён для совместимых callers. |
| Названия вместо GUID | DONE | Selection/toolbar используют `OrganizationProfile.Name`; real Chromium проверяет отсутствие GUID в picker. |
| Single/multi/zero organizations | DONE | HTTP/Vitest; Chromium: multi picker и single-org auto-select второго пользователя. Zero-org возвращает точное сообщение без JWT/cookie. |
| Automatic restore | DONE | Cookie refresh запускается при старте; real Chromium reload, expiry recovery и two-tab flow проходят. |
| Switch/logout/revocation/isolation | DONE | Real Chromium org A↔B, forged selector, revoked membership, cross-tab logout и independent second user. |
| Invalid login no-leak | DONE | Real Chromium получает 401, не видит names/GUID и не получает refresh cookie; HTTP test независимо подтверждает 401/no-cookie/no-org-data для неверного пароля и неизвестного логина. |
| Accessible icons/routes/safe return | DONE | SVG icons с labels/aria, BrowserRouter boundaries, allowlisted same-origin return path. |

## HTTP/API diff

Новых auth endpoints и новой schema нет.

- `POST /api/v1/auth/login`: `organizationId` optional; ответ дополнен `organizationId`, `organizationName`, `selectionRequired`, `organizations[]`. Invalid credentials — 401 без org data/cookie; zero org — 403 `no_available_organizations`; one org auto-selected; many — no access JWT until selection.
- `POST /api/v1/auth/refresh`: optional selector; rotation заново получает current organizations/names; localStorage organization ID — только revalidated hint.
- `POST /api/v1/auth/switch-organization`: тот же protected refresh rotation; forged/revoked UUID не становится authority.
- `POST /api/v1/auth/logout`: контракт не изменён; server revoke + cookie deletion.

## Secret admin route

**Текущий вывод superseded решением владельца 15.10.2026:** перечисленные ниже меры
скрывают адрес, но не защищают саму страницу и не доказывают авторизацию каждой операции.
Эта часть NOT ACCEPTED и блокирует deploy.

- `docker/local-init/init.sh` идемпотентно создаёт UUIDv4 в `/run/lms-secrets/admin-entry-uuid`, mode `0444` в read-only secrets volume.
- `docker/web-entrypoint.sh` валидирует UUID, fail-closed при отсутствии/невалидном значении, подставляет только его в Nginx template, удаляет environment variable до запуска Nginx и не печатает значение.
- Nginx отдаёт SPA только для exact secret route и явного списка публичных/учебных routes. Произвольный UUID и произвольный path получают 404; `/index.html` извне закрыт.
- Exact secret route имеет `access_log off`, `X-Robots-Tag: noindex, nofollow, noarchive`, `Referrer-Policy: no-referrer`, `Cache-Control: no-store`, frame/content protections.
- React production bundle содержит только проверку формы UUIDv4, не configured UUID; на admin entry обычный auth restore не выполняется. Служебные панели используют прежние independently authenticated operations.

## Guardrails review

- Client authority / IDOR: **blocked для admin entry** — organization selector и safe return покрыты, но прямой доступ к странице первого/административного входа и её операциям не имеет принятой server-side authorization boundary. До исправления deploy запрещён.
- Cross-org access: **covered** — Chromium подтверждает course A/B isolation, independent second user и revoked membership removal.
- Post-pagination filtering / counts leakage: **not applicable — login organization options не пагинируются; invalid credentials и неизвестный логин возвращают нулевой список и не раскрывают count/names**.
- Wrong org/course: **covered** — forged org plus real course A/B isolation.
- Wrong group/assignment/role: **not applicable — эти access paths не изменялись; JWT issuance продолжает existing S3 grants/ProcessingRestrictions**.
- Missing/forged parent and inaccessible attachments: **not applicable — parent/file paths не менялись**.
- Revoked/moved membership: **covered** — revoked org исчезает из current options и не может быть выбран снова; accepted temporal semantics сохранены.
- Missing processing evidence: **covered by preservation** — component issuance остаётся за existing processing gate; Chromium читает только разрешённый course context.
- Stale authorization: **covered** — options snapshot не является authority; completion выполняет fresh membership + organization lock.
- Unsafe restriction narrowing: **covered by preservation** — ProcessingRestrictions/grants не менялись.
- Mutable history / duplicate effects: **covered** — generation/version CAS, replay 409 и audit сохранены.
- Browser expiry/retry: **covered** — 401 expiry→refresh, network unknown outcome, replay 409, two-tab coordination.
- Duplicate submission: **not applicable — submission flow не менялся**.
- Secrets: **covered** — password/access JWT memory-only; refresh HttpOnly; configured admin UUID отсутствует в bundle, UI, result JSON и access log; missing secret fails closed.
- Schema/roles: **covered** — migrations и role memberships не менялись. ACL test доказывает: runtime может SELECT profile name, не может DML/TRUNCATE/REFERENCES/TRIGGER; processing role не может даже SELECT.

## Validation

### PASS

- Production frontend build:
  - `docker run --rm -e CI=true -v /home/tea/github/lms/src/Lms.Web:/source -w /source lms-ui-login-build-test pnpm build`
  - PASS, TypeScript + Vite, 302 modules.
- Full frontend suite:
  - `docker run --rm -e CI=true -v /home/tea/github/lms/src/Lms.Web:/source -w /source lms-ui-login-build-test pnpm test --run`
  - **19 files, 427 tests PASS, 0 FAIL**; session suite **9/9**.
- HTTP auth/admin/roster regression:
  - `/tmp/lms-dotnet/dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~TeacherRevocationHttpTests|FullyQualifiedName~OrganizationBootstrapHttpTests|FullyQualifiedName~AccountAdministrationHttpTests|FullyQualifiedName~CourseRosterHttpTests' -m:1`
  - **8 PASS, 0 FAIL**.
- S2/S3/session regression:
  - `/tmp/lms-dotnet/dotnet test tests/Lms.Infrastructure.Tests/Lms.Infrastructure.Tests.csproj --no-restore --filter 'FullyQualifiedName~IdentitySessionTests|FullyQualifiedName~BoundedRefreshTests|FullyQualifiedName~ScopedLoginDeadlineTests|FullyQualifiedName~ComponentIssuanceTests|FullyQualifiedName~ComponentHardeningTests|FullyQualifiedName~ComponentCommitAcknowledgementTests' -m:1`
  - **31 PASS, 0 FAIL**.
- Restricted ACL:
  - `/tmp/lms-dotnet/dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj --no-restore --filter FullyQualifiedName~Runtime_can_read_organization_names_but_cannot_mutate_profiles -m:1`
  - **1 PASS, 0 FAIL**.
- Real Chromium HTTPS restricted acceptance:
  - `LMS_BROWSER_READ=1 ... /tmp/lms-dotnet/dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~React_browser_login_reload_expiry_two_tabs_switch_replay_and_logout -m:1`
  - **1 PASS, 0 FAIL**, 19 s. Stages include invalid 401/no-leak, multi names/no GUID, single auto-select, restore, expiry, two tabs, replay, CSRF/Origin, switch, forged org, revoke, isolation, logout.
- Production Nginx HTTPS smoke: exact secret 200; wrong UUID 404; arbitrary path 404; login 200; direct index 404; headers PASS; configured UUID absent from bundle and exact access log.
- Real Chromium production-Nginx admin route: exact 200; wrong UUID 404; heading/actions visible; screenshot and header result recorded.
- Local-init: UUIDv4 regex PASS, mode 0444, rerun idempotent, missing web secret fail-closed without secret log.
- `/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node --check tools/browser_read/react-browser-session.mjs` — PASS.
- `git diff --check` — PASS.
- Hero SHA-256 `391fe501d2c6c133599fcdd06c659851147bf2ca739c622d031fc12491c6414b` — unchanged from pre-task evidence.

### NOT RUN / not claimed

- Broad full solution/API regression was not rerun after the final UI slice; affected targeted suites above are green. A prior broad nonbrowser API attempt was cancelled without result and is not represented as PASS.
- Независимые negative-access tests нового owner finding для прямого доступа к admin/first-entry странице и каждой операции (anonymous, обычный пользователь, wrong role/org/context, revoked authority) **NOT RUN / blocker open**.
- На момент исходного отчёта production deploy, M2, pilot, commit, push и merge не выполнялись. Решение владельца 15.10.2026 отдельно разрешило commit и интеграцию текущего снимка в `main`; push/deploy по-прежнему запрещены.

## Evidence

Real Chromium restricted auth journey:

- `codex/evidence/UI-LOGIN-01/M1-01-BROWSER-AUTH/result.json`
- `01-login.png`, `02-login-network-error.png`, `03-organization-selection.png`, `03-authenticated.png`, `04-logged-out.png`, `05-second-user-authenticated.png`.

Production Nginx secret route:

- `codex/evidence/UI-LOGIN-01/admin-entry-1440.png`
- `codex/evidence/UI-LOGIN-01/admin-route-result.json`

Responsive mocked visual states retained as layout evidence: `login-1440.png`, `login-1280.png`, `login-768.png`, `login-390.png`, `select-organization-1440.png`, `select-organization-390.png`. An intermediate harness-only strict-locator failure is archived under `prior-failures/`; it is not a final product failure.

## Changed paths

Core auth/UI:

- `src/Lms.Application/IdentitySessions.cs`
- `src/Lms.Infrastructure/PostgresComponentTokenIssuer.cs`
- `src/Lms.Api/TeacherRevocationHttp.cs`
- `src/Lms.Web/src/auth/session.tsx`
- `src/Lms.Web/src/auth/auth.css`
- `src/Lms.Web/src/main.tsx`
- `src/Lms.Web/src/test/session.test.tsx`
- `tests/Lms.Api.Tests/TeacherRevocationHttpTests.cs`
- `tests/Lms.Api.Tests/ReactBrowserSessionTests.cs`
- `tools/browser_read/react-browser-session.mjs`

Approved runtime/config slice:

- `tools/processing_isolated/profile.sql`
- `tests/Lms.Api.Tests/IsolatedProcessingDeploymentTests.cs`
- `docker/local-init/init.sh`
- `docker/nginx.conf`
- `docker/web-entrypoint.sh`
- `src/Lms.Web/Dockerfile`

Evidence/report: `codex/evidence/UI-LOGIN-01/*`, `codex/UI-LOGIN-01-RESULTS.md`.

Checkout до задачи был существенно dirty; unrelated tracked/untracked changes сохранены. На момент формирования исходного отчёта commit/push/deploy не выполнялись; дальнейшая Git-фиксация описывается отдельным owner checkpoint.

## Open risks / dependencies

- **DEPLOY BLOCKER:** secret UUID снижает discoverability, но не является authorization factor; известный URL открывает страницу и операции без принятой проверки права на вход. Нужны server-side page authorization, независимая authorization каждой операции и negative-access evidence.
- Новый SPA route в будущем потребует явного добавления в Nginx allowlist; неизвестные routes теперь fail closed 404.
- Одобренный table-level SELECT даёт runtime process чтение всех строк `OrganizationProfiles` после DB compromise; application query раскрывает только memberships уже проверенного actor. Более узкая actor-scoped capability потребовала бы отдельной schema/capability работы и не вводилась.
- Global E2E/SYNTHETIC MVP/PILOT readiness этой задачей не меняется.

## Readiness labels

- `UI COMPLETE`: **YES только для принятой основной формы логина; NO для UI-LOGIN-01 целиком**.
- `E2E ACCEPTED`: **NO для UI-LOGIN-01 целиком — admin/first-entry authorization blocker открыт**.
- `SYNTHETIC MVP ACCEPTED`: **не заявляется / без изменений**.
- `PILOT AUTHORIZED`: **NO**.
- `DEPLOY AUTHORIZED`: **NO — обязательный blocker страницы первого/административного входа**.
