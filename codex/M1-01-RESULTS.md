# M1-01 — браузерная авторизация: результаты

Дата: 12.10.2026  
Статус: **DONE (component acceptance)**  
Граница: только M1-01; M1-03 не начинался, commit/deploy не выполнялись.

## Итог

Обычный production React bundle теперь выполняет login, refresh, switch-organization и logout через ранее реализованный защищённый HTTP/S2–S4 контракт. Access JWT живёт только в памяти вкладки; opaque refresh остаётся в `HttpOnly; Secure; SameSite=Lax; Path=/` cookie. Публичной регистрации и обязательного email нет.

Страница входа приведена к выбранному макету «Сапиентум: вход в мир математики»: двухколоночная светло-зелёная композиция, бренд/описание слева, карточка входа и информационный блок справа. Левая сцена теперь использует отдельный project-local растровый hero-ассет `src/Lms.Web/src/assets/login-study-room.png`, созданный по референсу: окно, стол, растение, книги, тетрадь с геометрией и ноутбук; HTML остаётся единственным источником текста и UI. Поле организации оставлено явно, потому что текущий защищённый API требует выбранную организацию; каталог/человекочитаемый выбор относится к M1-03–M1-05.

## Вертикальная цепочка

`React LoginScreen / SessionGate` → `/api/v1/auth/login|refresh|switch-organization|logout` → `PostgresComponentTokenIssuer` / `PostgresIdentitySessions` → fresh S3 authority → `ComponentJwt` → PostgreSQL session/verifier/audit.

- login, refresh и switch-organization переиспользованы, второй token model не создавался;
- logout принимает только opaque cookie, на сервере находит/блокирует verifier и session, отзывает сессию и удаляет cookie;
- account/org switch и logout отменяют session-scoped запросы, размонтируют контекстные React state/drafts и освобождают зарегистрированные Blob URL;
- `BroadcastChannel` синхронизирует logout/session replacement между вкладками; Web Locks сериализует refresh, а fallback хранит в `localStorage` только краткоживущую coordination lease, не токен;
- reload восстанавливает сессию через refresh cookie; access expiry инициирует один refresh без replay мутаций и бесконечных циклов;
- JWT parser принимает фактический строгий S4 payload, включая числовой `ScopeRole`, и не использует payload как серверное полномочие.

## PostgreSQL least privilege

Для enabled host `TeacherRevocation:RestrictedDatabaseCapabilities` по умолчанию `true`. Runtime login/logout используют существующие узкие `SECURITY DEFINER` capabilities (`s2_lock_account`, `s2_rehash_password`, `s2_session_audit`) и не получают `INSERT` в таблицу аудита, owner или superuser.

`S2_login_audit_uses_narrow_runtime_capability_without_table_insert` выполняет реальный login ролью `c2h_runtime`, проверяет `session_created`, наличие только `EXECUTE` на `s2_session_audit`, отсутствие прямого table INSERT и fail-closed preflight при диагностической избыточной выдаче прав. Owner используется только для fixture setup и независимого oracle.

## Browser acceptance

`React_browser_login_reload_expiry_two_tabs_switch_replay_and_logout` запускает production Vite bundle в реальном headless Chromium по HTTPS с runtime DB role. Access tokens заранее не выдаются и в Node input не передаются.

Проверено:

1. экран входа и понятная сетевая ошибка без refresh loop;
2. пользователь A: React form login → доступ только к курсу org A;
3. в storage нет JWT; reload восстанавливает сессию через refresh;
4. вторая вкладка использует общую cookie-сессию, но держит свой access JWT в памяти;
5. predecessor refresh replay возвращает 409;
6. missing CSRF и чужой Origin возвращают 403;
7. после +16 минут старый access JWT возвращает 401, reload/refresh восстанавливает доступ;
8. switch org A→B не переносит курс A; чужая org выдаёт сессию без educational scope;
9. после отзыва membership A свежий switch/refresh не выдаёт scope;
10. logout возвращает 204, отзывает серверную сессию, очищает UI и вторую вкладку;
11. пользователь B входит/выходит через React в отдельном browser context и видит только org B;
12. DB oracle: две сессии отозваны, по два `session_created` и `session_revoked` audit events.

Дополнительные отрицательные проверки подписи/issuer/audience, expired/revoked authority, commit acknowledgement и bounded refresh покрыты полной Infrastructure/API regression.

## Regression

| Suite | Результат |
|---|---:|
| Solution build | PASS, 0 warnings / 0 errors |
| Domain | 43 PASS |
| Application | 369 PASS |
| Infrastructure | 334 PASS |
| API (обычный полный run) | 375 PASS / 6 opt-in browser SKIP / 0 FAIL |
| M1-01 browser opt-in | 1 PASS / 0 FAIL |
| Targeted logout + runtime audit | 2 PASS / 0 FAIL |
| Frontend Vitest | 411 PASS в 17 файлах |
| Frontend production build | PASS |
| Content compiler | 74 PASS |
| Content importer | 49 PASS |

Шесть API skip — существующие opt-in browser suites; M1-01 suite запущен отдельно с `LMS_BROWSER_READ=1` и прошёл. Первичная дополнительная попытка compiler использовала bundled Python без PyYAML и завершилась на discovery; канонический `/usr/bin/python3` run прошёл 74/74 и только он является итоговым результатом.

Точные итоговые counts: `codex/evidence/M1-01-BROWSER-AUTH/test-summary.json`.

## Evidence

- `codex/evidence/M1-01-BROWSER-AUTH/result.json` — этапы и HTTP statuses;
- `01-login.png` — новый экран входа;
- `02-login-network-error.png` — ошибка сети;
- `03-authenticated.png` — сессия пользователя A;
- `04-logged-out.png` — UI после logout;
- `05-second-user-authenticated.png` — независимый вход пользователя B.

## Изменённые контракты и файлы

- session/logout: `src/Lms.Application/IdentitySessions.cs`, `src/Lms.Infrastructure/PostgresIdentitySessions.cs`, `src/Lms.Api/TeacherRevocationHttp.cs`, `src/Lms.Api/TeacherRevocationComposition.cs`, `src/Lms.Api/Program.cs`;
- React session/UI: `src/Lms.Web/src/auth/session.tsx`, `src/Lms.Web/src/auth/auth.css`, `src/Lms.Web/src/assets/login-study-room.png`, `src/Lms.Web/src/api/sessionResources.ts`, `src/Lms.Web/src/api/client.ts`, `src/Lms.Web/src/main.tsx`;
- Blob lifecycle: `AttachmentPreview.tsx`, `AttachmentUploader.tsx`, `ProtectedPhotos.tsx`;
- tests/evidence: `session.test.tsx`, `ApiFixture.cs`, `TeacherRevocationHttpTests.cs`, `IsolatedProcessingDeploymentTests.cs`, `ReactBrowserSessionTests.cs`, `tools/browser_read/react-browser-session.mjs`.

Schema/migration не добавлялись. Работающий Teacher Revocation cutover не переписывался.

## Ограничения и остановка

Это component acceptance M1-01, а не полный M1 E2E и не разрешение пилота. Синтетические identities и memberships подготавливаются fixture; продуктовый bootstrap/onboarding относится к M1-03/M1-04. Friendly org catalog относится к последующим задачам. M1-03 не начат.
