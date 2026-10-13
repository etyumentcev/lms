# M1-04 — Accounts, Activation & Recovery: результаты

Дата: 12.10.2026  
Статус: **DONE (component acceptance, synthetic-only)**  
Граница: только M1-04; группы, курсы, enrollment и teacher assignment не создавались; commit/deploy не выполнялись.

## Итог

Администратор организации в production React UI видит SQL-фильтруемый список участников, поиск, фильтры и карточку аккаунта; создаёт Student или Teacher из synthetic roster и получает одноразовый activation code. Участник вводит organization/login/code, самостоятельно задаёт пароль и входит через существующие `PostgresIdentitySessions` / `PostgresComponentTokenIssuer` и React session gate.

Recovery реализован через `Requested → Approved → Fulfilled`: org-admin создаёт verified request, отдельный platform JWT с capability `account-security:approve` подтверждает запрос, код Recovery раскрывается один раз, consume атомарно меняет пароль, завершает challenge/request и отзывает все refresh sessions. Та же независимая проверка нужна для связи уже существующей identity со второй организацией; новый `IdentityUser`/`Actor` не создаётся.

## Вертикальные цепочки

### Provisioning и список

`AccountsPanel` → `POST /api/v1/admin/accounts` → component JWT admin authority (org/actor только из token) → `PostgresAccountAdministration.Provision` → serializable transaction/advisory roster lock → `IdentityUser` + `Actor` + `ActorIdentity` + `AccountSecurityState` + `OrganizationMembership` + organization-scoped participant profile + существующий `AccessChallenge` + receipt/audit → one-time no-store response.

`GET /api/v1/admin/accounts` → server-side admin check → organization/role/state/search predicates в SQL → `Count` и только затем `OrderBy/Skip/Take`. Foreign rows не влияют на count/page. Карточка не показывает memberships других организаций; до independent approval shared-link response не раскрывает foreign UserId/ActorId.

### Activation

`AccountAccessPanel` → exact HTTPS Origin + antiforgery + per-IP limiter → `POST /api/v1/auth/activate` → общий `AccessChallengeProtocol` (тот же Crockford/HMAC domain, что и M1-03) → security/user/mapping/challenge locks → password hash + active mapping + consumed challenge + audit → обычный `/auth/login`.

Student/Teacher до M1-05 имеют active membership, но ноль enrollment/teacher grants. Login создаёт штатную refresh session, React показывает «Нет доступных курсов» и не подделывает учебные полномочия.

### Recovery и shared identity

`POST /admin/accounts/{actor}/recovery` → org-admin authority → `RecoveryRequest(Requested)` + secret-free audit.  
`SecurityOperatorPanel` → platform JWT `account-security:approve` → независимый operator Actor без membership/education grants → approve/reject. Approve создаёт Recovery challenge на 15 минут.  
`POST /auth/recover` → challenge/user/security/request/session locks → new password/security stamp/credential epoch → challenge Consumed + request Fulfilled + all refresh sessions revoked + audit в одной transaction.

Если username уже соответствует active identity другой организации, provisioning создаёт `AccountLinkRequest(Requested)`. Только independent approve добавляет вторую `OrganizationMembership` и scope-local profile; identity/actor/history не дублируются и foreign identifiers до approval не раскрываются org-admin.

## Challenge/security contract

- общий `AccessChallenges`, без второй OTP-таблицы или параллельного token protocol;
- 16 Crockford Base32 символов / 80 CSPRNG bits, формат `XXXX-XXXX-XXXX-XXXX`;
- HMAC-SHA256 с существующим configured key/version; activation domain совместим с уже выданными M1-03 codes;
- Activation 24 часа, Recovery 15 минут; purpose mismatch невозможен;
- terminal states необратимы; used/expired/locked/invalid различаются пользовательским UI;
- максимум 5 ошибок/code и 5 ошибок/account/15 минут; HTTP verify 10/min/IP;
- issue 3/hour/recipient, 20/hour/initiator, cooldown 60 секунд;
- reissue отзывает прежний live challenge; stable receipt не раскрывает secret повторно;
- plaintext codes/passwords/JWT/refresh secrets не пишутся в DB audit или logs; screenshots содержат только один synthetic code, потреблённый тем же E2E.

## Transaction/failure semantics

- Provision/reissue/recovery request/approval/link approval: service владеет одной PostgreSQL transaction; serializable + advisory command/roster/request lock; authorization проверяется внутри transaction; commit — единственная точка успеха.
- Challenge consume: `ReadCommitted` под advisory user+purpose lock, затем security → user → mapping → challenge → recovery request → sessions locks. Это даёт один concurrent winner, а второй детерминированно получает `Used`, без serialization 500.
- Password/challenge/request/session revoke/audit сохраняются одним commit. Audit failure откатывает всю мутацию.
- Stable roster/idempotency receipts предотвращают duplicate effects. Потерянный commit ACK — unknown outcome; UI сообщает проверить результат и не выполняет автоматический replay.

## Authorization и negative-access evidence

- own org admin list/create/reissue/recovery: PASS;
- foreign org admin list/target: `Forbidden` до rows/counts: PASS;
- Student/Teacher не получают admin token/capability; platform token не принимается component endpoints: сохранено M1-03 + HTTP regression;
- shared identity требует independent operator; initiator не может быть approver: PASS;
- duplicate identity не создаёт новый Actor/User; link approval добавляет вторую membership: PASS;
- invalid/used/expired/locked codes и concurrent consume: PASS;
- recovery old password denied, new password accepted, old refresh session revoked: PASS;
- no implicit enrollment/teacher assignment: DB oracle PASS;
- course/group/assignment/attachment parent cases: not applicable — M1-04 не читает и не изменяет эти ресурсы.

## Реальный Chromium HTTPS

Opt-in `AccountAdministrationBrowserTests` поднял real HTTPS Kestrel, production Vite bundle, PostgreSQL и system Chrome. Playwright не mock/route/fulfill API.

1. platform bootstrap и activation первого admin;
2. обычный admin login;
3. открыть «Пользователи», создать synthetic Student из roster;
4. получить одноразовый код;
5. выйти, открыть общий activation flow;
6. ввести org/login/code, задать/подтвердить пароль;
7. первый обычный student login;
8. увидеть безопасное состояние без курсов; DB oracle: identity active, Student membership, 0 enrollment, 0 teacher grants.

Screenshots:

- `codex/evidence/M1-04-ACCOUNTS-ACTIVATION/01-accounts-empty.png`;
- `02-create-student.png`;
- `03-student-code.png`;
- `04-accounts-list.png`;
- `05-student-activation.png`;
- `06-first-student-login.png`;
- machine-readable summary: `test-summary.json`.

## Фактически выполненные проверки

| Проверка | Результат |
|---|---:|
| Solution build | PASS, 0 warnings / 0 errors |
| Domain full | 43 PASS |
| Application full | 369 PASS |
| Infrastructure full checkpoint | 340 PASS / 0 FAIL |
| Final M1-03/M1-04 targeted infrastructure | 7 PASS / 0 FAIL |
| API full | 377 PASS / 8 opt-in browser SKIP / 0 FAIL |
| M1-04 HTTP targeted | 1 PASS / 0 FAIL |
| M1-04 real Chromium HTTPS opt-in | 1 PASS / 0 FAIL |
| Frontend Vitest full | 411 PASS / 0 FAIL |
| Frontend production TypeScript/Vite build | PASS |

Exact commands are preserved in task history; opt-in browser used `LMS_BROWSER_READ=1`, bundled Node/Playwright and `/usr/bin/google-chrome`.

## Изменённые контракты и paths

- Application: `AccountAdministration.cs` — commands/results/ports only.
- Infrastructure: shared `AccessChallengeProtocol`, `PostgresAccountAdministration`, participant profile/link request entities, additive migration + snapshot, M1-03 bootstrap refactored onto the shared compatible challenge helper.
- API: `AccountAdministrationHttp`, DI/map, strict JSON, CSRF/Origin/rate/error mapping.
- React: list/search/filters/cards/create dialog, one-time secret display, activation/recovery screens, independent operator queue, Sapientum green responsive CSS, login/dashboard integration.
- Tests/evidence: PostgreSQL state/race/isolation/recovery/shared identity tests, HTTP test, real Chromium test/runner/screenshots.

## Guardrails §7

- [x] Actor/use case/invariants/states documented.
- [x] Existing S2/S3/session/challenge rules reused; PRS1 exact/G02 unchanged.
- [x] Organization resolved from server token; foreign org denied before data/count.
- [x] List count/search/filter/pagination authorized in SQL before materialization.
- [x] No attachment/history/course/group access added (N/A for those resources).
- [x] Transaction/lock/commit/retry/unknown outcome documented and race-tested.
- [x] Input lengths, strict DTOs, code lifecycle and rate limits covered.
- [x] Unit/integration/API/negative-access/real-browser evidence recorded.
- [x] Additive migration only; no enrollment/assignment side effects.
- [x] Synthetic data only; no secrets in audit/logs; no pilot/release claim.

## Открытые границы

- component acceptance не означает production deployment/pilot authorization;
- strict isolated runtime DB capability для новых provisioning/recovery writes отдельно не квалифицирована;
- platform operator/security operator получают уже выданный JWT вручную; полноценный IdP redirect/key-ring operations остаются deployment work;
- synthetic verification refs не являются юридической проверкой представительства реального несовершеннолетнего; PD-G03/G02 остаются открыты;
- block/unblock SecurityBlocked и operational key rotation UI не расширялись сверх M1-04 activation/recovery;
- M1-05 groups/courses/enrollment/teacher assignment не начат.
