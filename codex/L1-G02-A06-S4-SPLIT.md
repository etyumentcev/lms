# G02/A06 — S4-SPLIT

2026-10-10. G02/A05 B/C DIRECTION ACCEPTED; G02/A06 DOCUMENTATION AUTHORIZED — основание: явное задание владельца A06. Все SHALL/«должен» ниже — предложенные обязательства, NOT ACCEPTED / NOT IMPLEMENTED / NOT PROVEN. Новая норма, архитектурная квалификация и разрешение реализации требуют отдельных решений.

**A06 NORMATIVE PACKAGE READY FOR OWNER DECISION · T04 UNCHANGED · S4 BLOCKED.**

## Предлагаемая граница двух scopes

Это декомпозиция для будущего решения. **S4-A NOT AUTHORIZED; S4-B NOT AUTHORIZED.** Их можно отдельно разрешать и оценивать; это не два независимо готовых к эксплуатации продукта. Shared clock, HTTP и DB publication gates сохраняются. S4-A PASS не означает S4 PASS и не открывает S5–S8. [Точный нормативный diff](L1-G02-A06-NORMATIVE-DIFF.md).

## S4-A — Identity, JWT, HTTP Authorization

Входит: issuer/validator (signature, allowed algorithms, issuer/audience, true expiry/nbf, org/actor/session/grants), login/refresh/logout/switch/session/CSRF adapters, browser memory token provider/coordinator; HttpOnly Secure refresh cookies, SameSite/Origin/CSRF; O01 rotation; server-derived scope, IDOR и current ProcessingRestrictions/SyntheticOnly на всех route aliases, lists/counts/history. No localStorage tokens; no scope from UI; Student owner/Teacher assigned course/group, admin не educational bypass.

Зависимости: accepted S1/S2/S3/B01/O01 и их исходное evidence; approved scope; conservative clock design; transaction linearization issue/revoke/block/refresh; trusted original HTTP budget. S4-B API contracts могут быть test doubles для policy checks, но такие tests не доказывают storage/publication/HTTP bounds. Ни auth DB mutations, ни обычные domain commands не исключаются из B-PG по названию scope.

Предлагаемые acceptance criteria:

- A-AC1: JWT≤900s, D≤revoke+7200s, exp≤earliest D−600s, exp≤Now deny; invalid signature/algorithm/issuer/audience/nbf/expired/scope deny; ±clock/rounding, near-exp/D, multiple nodes, issue/revoke/regrant races. Unknown clocks fail closed; обычный revoke A не затрагивает B.
- A-AC2: fresh DB issue/refresh/switch, one org per token, no revoked grant reissue; logout revokes refresh session; E03 block запрещает login/issue/refresh, revoke-all/audit atomic. Unblock не возвращает old grants/session. Проверять через существующие foundations/fixtures; S6 UI/lifecycle не включать.
- A-AC3: O01 verified consumed predecessor age≤30s inclusive → 409 refresh_already_rotated без JWT/token/Set-Cookie и без compromise; age>30s → revoke только данной session и atomic secret-free audit. Один successor при concurrency, другие sessions живы; потерянный response не redisclose successor. Browser tabs разных org, reload/coordinator/fallback, idle60m/absolute12h.
- A-AC4: cookies/CSRF/Origin и malicious cross-origin запросы, no persistent JS secrets, secret canary logs; unsupported browser coordinator без tested fallback — STOP.
- A-AC5: inventory всех existing `/api/v1` routes/aliases и planned auth routes; wrong org/course/group/owner deny до materialization/counts; current restrictions выше historical allow; missing context/DB unavailable deny; SyntheticOnly нельзя обойти header/config request. File guards тестируются и на negative cases, без объявления file integration готовой.
- A-AC6: standalone component/protocol evidence отделено от runtime acceptance. Для HTTP acceptance нужны actual ingress-to-last-controlled-egress≤60s, auth transaction outcome/late-effect safety и B-CLOCK/B-EGRESS/B-PG closure на соответствующих путях. Тестовые fake clocks не заменяют эти доказательства.

Результат будущего S4-A: отдельно проверенный auth/policy slice на synthetic стенде, без general ingress cutover и без объявления готовности files. Unauthorized paths не допускаются временно ради demo. Отключение files не закрывает общую S4 acceptance. При недоказанном shared bound возможна только явно разрешённая компонентная работа, не публичное включение.

## S4-B — Files and Publication Safety

Входит: private storage isolation, immutable exact versions, durable OperationId/scoped idempotency, publication fencing, ambiguous commit handling, minimal outcome recovery/quarantine/capacity accounting и controlled egress. Upload каждой фотографии отдельно; Submit/claim — самостоятельная авторизованная ordinary operation. Download/Range — новый JWT/parent/current-restriction check. Immutable history/originals сохраняются.

Зависимости: отдельная normative acceptance включая E03-C; отдельно одобренный architecture/schema/API design и разрешение реализации; S4-A authorization contract (для design возможны synthetic stubs; integration требует A evidence); R1 qualified storage/PG/edge/clock/fence/capacity dossier. Ни storage gateway, ни schema changes A06 не разрешает.

Предлагаемые acceptance criteria:

- B-AC1: P1/P2 durable binding до side effects, один result per key, иной digest conflict; atomic domain/claim/result/exact version, неизменные scope/generation/deadline. F01/F02/F12.
- B-AC2: P3 timely p<d/authority cutoff для каждого visibility path, включая queries/counts/history/events/queues; stalled COMMIT после precheck не создаёт late effect. Private intent или второй COMMIT не считаются автоматическим решением. F03/F04/F11.
- B-AC3: P4–P6 old worker/retained FD/late IPC/restart не меняют published version/domain/новое поколение. Seal/hash/length/durability и no-bypass evidence, physical fence отдельно от control fence. F07–F13/F21.
- B-AC4: unknown ACK/absence не rollback и не replay; сохранены potentially committed originals, fresh authorized status only, no late promotion; purge exact version только при no refs+quiescence. F05/F06/F12/F19/F22. Не вводить новую DELETE semantics без отдельного согласования.
- B-AC5: absolute ingress-to-last-controlled-TX≤60s ordinary и≤480s designated upload/download/каждый Range, slow/trickle/buffering/hang/disconnect/retransmit; no PG lock на время stream. F14–F16. Bytes уже вне контролируемой границы не отзываются.
- B-AC6: current restrictions/scoped reads и SyntheticOnly, no raw/static/CDN/presigned bypass; cap exhaustion reject до side effects, accounting pending I/O сохраняется. F18/F20/F21/F23.
- B-AC7: весь R0–R4, включая независимо рассмотренные contracts, negative matrix и security/DDD review. NOT RUN/NOT IMPLEMENTED сейчас. Finite successful tests без R1 не PASS.

## S4/S7 boundary — точный предлагаемый replacement scope

Минимальные safety prerequisites нельзя отложить до S7: unique durable operation/result, immutable versions/no bypass, deadline-aware publication/egress, unknown handling, default-deny restart, минимальное authoritative resolution и bounded orphan accounting входят в S4-B до file acceptance. Minimal recovery не меняет пользовательский command и не replayed его.

S7 сохраняет последующее hardening и масштабную проверку уже принятого контракта, general jobs/step authorization/result delivery только по отдельному scope, полный lifecycle/retention/erasure и operational recovery/runbooks по отдельным решениям. Реальные retention/legal basis/ProcessingRestrictions freshness остаются pre-pilot gates. S4-B не реализует full S7, erasure platform или general job engine. S5/S6/S8 и migration M2/M3 также вне этого разрешения.

Общий S4 acceptance требует S4-A + S4-B integration, всех обязательных file use cases/aliases и R0–R4. S7 не служит оправданием пропустить security prerequisites S4. Переход к S5 или другим slices всегда требует отдельного owner authorization.

## Рекомендация по отдельному разрешению S4-A

Рекомендуется после рассмотрения A06 отдельно разрешить **только component/protocol scope S4-A** с synthetic tests и явным сохранением G02 integration stop. Это позволяет проверить JWT/O01/scopes без выдуманного решения B-PG/B-EGRESS. Решение должно прямо исключать production/public ingress cutover, files implementation и S4 acceptance. Если ожидается сразу end-to-end runtime acceptance S4-A, сначала закрыть shared clock/PG/egress obligations. В текущем A06 никакой из этих вариантов не разрешён.
