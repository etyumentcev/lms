# ADR-L16 — Organization-scoped JWT и browser refresh-session

2026-10-09. **ACCEPTED E01/E02** по L0.4 owner request §4. DTO/поля — engineering contract; **O01 PROPOSED** отдельно. [Реестр](../../codex/L03-OWNER-DECISION-REGISTER.md). L1 не разрешён, SYNTHETIC DATA ONLY. Временные гарантии — ADR-L15.

## Claims и authority

Формальная shape (имена engineering): `iss`, `aud`, `iat`, `nbf`, `exp`, `jti`, `sub` (IdentityUser UUID), `actor_id` (stable Actor UUID), `sid` (session reference), ровно один `org_id`, `educational_role` при наличии, `grants[]`. Каждый grant: `grant_id`, `generation`, `membership_generation`, `course_id`, явный `group_scope=WholeCourse|Group(group_id)`, `actions[]`, `access_valid_until` (finite UTC либо unbounded). Administrative capabilities отделены от educational grants. sid не bearer secret/mandatory DB session lookup каждого API request.

Existing ClaimsActorMapper считает sub ActorId: будущий adapter явно читает actor_id, не пишет Identity UUID в историю. Null group existing TeacherAccess означает whole-course; missing group нового wire contract не расширяет permission. Duplicate critical claims/multiple authenticated identities/unknown format/malformed IDs→deny. Fixed algorithm allowlist/known signing key, signature/issuer/audience/nbf/lifetime и exp−iat≤15m проверяются локально. Security operator использует отдельную security capability/audience, не educational JWT на все org; конкретный adapter engineering review.

Server data model определяет resource ownership: work→assignment→course registry→org, attached file→parent, pending→trusted uploader/course/org binding. Client route/body/query/claims только сверяются с корнем. Allow = valid JWT AND matching org/course/group/owner/action AND processing gate AND workflow invariant. Teacher без grants deny. Missing/excluded grant не fallback на роль; no eligible grants→no learning token/explicit no-scope result, identity session может жить для другой org. Queue/count filters до aggregation. JWT A не даёт B даже общему S_AB.

Issue/refresh/switch перечитывают DB security/membership/grants/generations. Для selected subset G одной org: `exp=min(Now+15m,min(AccessValidUntil(grant/dependencies)−10m))`; unbounded deadline не уменьшает exp. Revoked/expired/old-generation исключены; скоро истекающие можно исключить, не расширяя остальные. Response сообщает фактически выданные scopes. A deadline не ограничивает B. Issuance/revoke transaction linearization: либо issuer до committed revoke и old-token bound, либо видит deadline/revoked state; read-then-issue race без согласованной transaction не считается безопасным.

## Session model и browser

Record: sid, userId, createdAt, lastSuccessfulRefreshAt, absoluteExpiresAt=createdAt+12h, revokedAt/reason, concurrencyVersion; refresh family/generation/current+consumed verifiers для rotation/replay. Refresh opaque CSPRNG secret, server хранит только cryptographic verifier (hash либо keyed MAC с отдельным key version), не plaintext. Access JWT только per-tab memory; no localStorage/sessionStorage/persistent cache. HttpOnly Secure host-only refresh cookie общая, selected org per-tab, не mutable active-org session field.

Idle: 60m после последнего successful refresh; ordinary API сам idle не продлевает. Login задаёт начало idle. Successful refresh атомарно обновляет activity, никогда за fixed12h; timeout→new login. Reload получает access через refresh. Engineering routes `/api/v1/auth/login`, `/refresh`, `/switch-organization`, `/logout`, `/session`, `/csrf`; refresh и switch через один rotation path, requested org всегда кандидат для fresh membership check. Switch не выдаёт B по одному JWT A.

Logout revokes текущую refresh-session и очищает cookie/local state; password recovery revokes все refresh-sessions. Existing JWT до exp, sid не denylist. SecurityBlocked проверяется на login/issue/refresh/consume по ADR-L17. На org switch очистить drafts/cache/Blob URLs и abort old requests, late response не заселяет новый scope; no command replay. Cross-tab logout общий, выбор org независим.

Cookie SameSite — явная настройка, engineering default Lax, без Domain, Path=/, `__Host-` name; no cross-origin credential CORS. Unsafe cookie endpoints требуют CSRF token и exact allowed Origin, SameSite не замена. No mutation GET; responses no-store, refresh secret никогда JS; trusted proxy/forwarded IP review. XSS: сохранить escaping/Markdown boundary, CSP совместимую с UI, no secret URLs/logs; HttpOnly не предотвращает действия XSS от пользователя.

## Atomic rotation — E02

Проверить current verifier, SecurityBlocked, revocation/idle/absolute; CAS generation, consume predecessor, сохранить ровно один successor verifier/activity и fresh selected-org JWT decision в одной transaction. Set-Cookie/JWT только после commit. Concurrent запросы не создают двух descendants; DB failure→no issuance. Logout/reset/block сериализуются с issuer: после commit новые issuance запрещены, ранее linearized JWT остаются под T04/E03 bound.

Reused predecessor фиксируется как potential compromise без secrets. Request id не credential и не разрешает повторный secret response. Lost response: если successor cookie установлен, возможен новый refresh; иначе re-login, сервер не восстанавливает plaintext successor. Не переустанавливать старую cookie delayed response поверх новой. Lost transport response отличается от обычной здоровой конкуренции вкладок.

## O01 — конкретный PROPOSED вариант

Browser-wide single-flight coordinator для refresh/switch: Web Locks при поддержке, capability-tested broker/fallback до release. Под lock вкладка отправляет свой org, получает свой JWT; cookie update завершается до release. Следующая вкладка отправляет successor и получает другой org JWT. BroadcastChannel только signals/metadata, никогда tokens. Crash освобождает coordinator, server authority определяет timeout. Unsupported browser без проверенного fallback — acceptance STOP, не скрытое сокращение device scope.

Server CAS обязателен даже при coordinator. **PROPOSED 5-second race window**: stale consumed predecessor в течение 5s получает 409 RefreshSuperseded, no JWT/no Set-Cookie, audit potential replay, без automatic family revoke. Вкладка после lock может один раз retry с current cookie; если successor отсутствует — re-login без loop. Вне окна replay→revoke этой refresh-session/generic401/audit, не SecurityBlocked аккаунта автоматически. Старый token внутри окна не получает ничего, но задерживается реакция на кражу — owner tradeoff. Same idempotency request не раскрывает successor.

Альтернатива strict broker без окна, отказ/re-login для неразрешимой гонки и отдельная replay reaction. Она тоже должна доказать отсутствие false normal concurrency termination; family revoke на каждом duplicate не удовлетворяет E02. Владелец выбирает вариант/окно/reaction либо явно делегирует policy. **O01 блокирует S4**, не S1 schema/S2 internal CAS/S3 scopes.

## Verification — NOT RUN

15m/60m/12h boundaries, earliest grant/exclusion, A+B switch/tabs/reload, forged/duplicate claims/no role-only access; concurrent issuance/revoke, CAS/parallel refresh/replay/reordered responses/crash/lost cookie, logout/reset/block; CSRF/Origin/XSS и secret canaries. Никаких mandatory Redis/per-request ordinary JWT grant freshness.
