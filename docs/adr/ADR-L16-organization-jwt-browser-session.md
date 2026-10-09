# ADR-L16 — Organization-scoped JWT и browser refresh-session

2026-10-09. **ACCEPTED E01/E02** по L0.4 owner request §4. DTO/поля — engineering contract; **O01 ACCEPTED** по заданию L1/S2. [Реестр](../../codex/L03-OWNER-DECISION-REGISTER.md). S1 ACCEPTED; только S2 AUTHORIZED, S3–S8 не разрешены. SYNTHETIC DATA ONLY. Временные гарантии — ADR-L15.

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

## O01 — ACCEPTED: Concurrent Refresh & Replay

Owner evidence: explicit L1/S2 task, 2026-10-09. Supersedes the proposed 5-second policy.

1. Every successful refresh atomically replaces its predecessor. Concurrent operations on a session serialize transactionally, independently of browser coordination.
2. A consumed predecessor presented within **30 seconds inclusive** of its consumption returns **409 `refresh_already_rotated`**, with no new refresh token, no access JWT and **no Set-Cookie** (including deletion). It does not automatically mean compromise or revoke the session.
3. A verified predecessor presented after 30 seconds revokes **only that session**, atomically with a secret-free security audit. Other sessions and the account remain enabled. Unknown/forged tokens cannot trigger revocation.
4. Tabs coordinate refresh/switch; browser-wide single-flight and a tested fallback remain S4. Server security does not depend on them. A delayed conflict response cannot overwrite the winner's cookie because it carries no Set-Cookie.
5. Lost HTTP response after commit does **not** guarantee session recovery. If the successor cookie was not installed, no successor secret can be reconstructed or reissued. Retry inside the window returns 409; retry outside it revokes the session. New login may be necessary. Tests must preserve this limitation.

E02 idle60m/absolute12h and memory-only access/HttpOnly Secure refresh remain unchanged. E03 SecurityBlocked always denies login/refresh; its authorized block workflow revokes all sessions in S6. Replay alone does not set SecurityBlocked. T04 existing JWT lifetime is unchanged: refresh-session revocation is not immediate access-JWT invalidation.

S2 implements internal outcomes only. S4 must map the conflict exactly, apply cookies only after commit, validate exact Origin plus CSRF on every cookie mutation, and verify actual browser response ordering. No public endpoint/coordinator is authorized in S2.

## Verification — NOT RUN

15m/60m/12h boundaries, earliest grant/exclusion, A+B switch/tabs/reload, forged/duplicate claims/no role-only access; concurrent issuance/revoke, CAS/parallel refresh/replay/reordered responses/crash/lost cookie, logout/reset/block; CSRF/Origin/XSS и secret canaries. Никаких mandatory Redis/per-request ordinary JWT grant freshness.
