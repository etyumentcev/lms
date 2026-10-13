# L0.4 — E01–E06 reconciliation and architecture review

2026-10-09. Verified base `836aad59d01f1cd7c04e9e892ebd2d8621f7c59f`, clean source branch `codex/l03-owner-decisions-consolidated`. Owner evidence: L0.4 task §4. Приоритет accepted E01–E06 → актуальные ADR/plan/contracts → historical L0.1–L0.3 reports. T01–T06 сохранены; accepted values не заменены инженерными предложениями. Реализация NOT RUN.

## Systematic reconciliation

| Взаимодействие / source и прежнее положение | Конфликт / уточнение | Приоритет и минимальное согласование |
|---|---|---|
| T01+E02; ADR-L01 browser paragraph, owner register E02, UI L0.3 | Memory/refresh/TTL ещё OPEN, старые session defaults не приняты | E02 теперь ACCEPTED idle60m/absolute12h/rotation/HttpOnly Secure, access≤15m memory; ADR-L16 нормативный protocol, L01/L04/UI/security controls synchronized |
| T01+E05; plan §3, schema AccessChallenges | Reissue/terminal states/idempotency были engineering uncertainties | E05 ACCEPTED finite-state/unique Created/atomic consume/one-time display/idempotent receipt; ADR-L17, plan schema/S6. T01 16/80/HMAC/rates/TTLs/passwords неизменны |
| T02+E04; ADR-L02 и Identity scopes E04 OPEN | Кто независимо approves, минимальный operator UI не определены | Platform security operator≠initiator, recipient verification, suspend if impossible, no educational access; minimal operator UI S6 принят E04, не новая support platform |
| T03+E01; existing ActorMapper sub→Actor и plan session/scopes | Identity sub нельзя записать как historical actor; org claim не resource owner | ADR-L16 user sub отдельно actor_id/sid/org/grants; resource root server-side; S2 mapping, S3 registry, S4 handlers. Код только прочитан |
| T03+E05; schema challenges/sessions/audit | Нет полного lifecycle/recovery request/verifier history | Security records additive, one live purpose code, unique successor/session CAS, independent request/audit, no secrets; S1 shells/S2 primitives/S6 flows без immutable learning rewrite |
| T04+E01; ADR-L15 composition PROPOSED | Не утверждён one-org JWT, общий exp мог связать A/B | E01 one active org/token, earliest included grants, optional exclusion near-exp; A expiry не влияет на B refresh; L16 + L15/reference/contracts |
| T04+E02; refresh/revoke concurrency | Fresh read сам по себе не исключает issuance race/revoked grants resurrection | Transaction linearization/CAS и fresh DB issue/refresh/switch; O01 exact tab concurrency/replay reaction отдельно OPEN, не blanket E02 OPEN |
| T04+E03; прежний emergency E03 OPEN | Два часа не emergency, no instant termination | SecurityBlocked stops new issuance/revokes refresh, existing JWT≤15m и operation≤8m; ideal≤23m completion только при conservative clocks/full operation bounds, L15/L17 |
| T04+E06; current restrictions против no per-request grants DB | JWT не legal/processing permit, нельзя silently применить 2h delay | ADR-L18 отдельный synthetic current-read/transaction processing gate; DB resource/restriction lookup не grant-freshness check. Real freshness pre-pilot decision |
| T05+E06; context model/ADR-L13 | E06 раньше blocking unclear | Immutable version/same-org interval bindings/current deny/historical refs сохранены; SyntheticOnly runtime/fixture boundary, no fake basis, no legal UI |
| T06+E01–E06; plan slices | Schema/mapping/constraints могут быть готовы раньше public protocol | S1 schema/security shells + M1/M2/rehearsal; S2 internal session; S3 scopes. Runtime M3 только S8 после всех checks. No real/unknown auto-backfill |
| T04 10m reserve vs 8m HTTP; L15 old brief explanation | 2m residual мог ошибочно позволить late JWT или cancellation tail | Формальные e/a/D/Δ/λ/L, conservative expiry, total operation≤480s including commit/edge/cancel. Clock/timeout evidence G02, no unconditional guarantee из CancellationToken |
| E02 healthy concurrency vs replay detection | Старый refresh одинаково выглядит при benign race и краже | O01 PROPOSED single-flight + CAS + 5s refusal/audit window then session revoke; alternative strict broker. Window/reaction не accepted молча, S4 blocked |
| Readiness/plan/task E01–E06 blanket blocked | Новые owner decisions не означают automatic all-slice READY | Независимая readiness: S1 ready, S2/S3 predecessor-blocked but independent of O01, S4 O01, S5–S8 dependencies. No owner authorization |

Дополнительно устранено старое исключение org selector в Organization/Identity/LearningSpace models: минимальный active-org switch теперь входит в S4 по E01; полная multi-org панель и multi-role UI остаются вне scope. Старый текст о cancellation из двухминутного остатка исправлен: cancellation/commit/edge входят в total8m, остаток не расширяет duration.

## Trust boundaries и проверка реализуемости

[Security specification](../docs/architecture/L1-SECURITY-SPEC.md) покрывает Browser/API/Identity-session/org authorization/PostgreSQL/attachment storage/jobs/security operator. Проверены отсутствие client-ID trust/role-only access/refresh resurrection/E04 provisioning bypass/secrets audit/perpetual job grant/unbounded stream/JWT privacy bypass/mandatory Redis/per-request DB JWT freshness. Это design review, не implementation proof.

Read-only code review подтвердил: ClaimsActorMapper сейчас единственный sub/Student|Teacher, Actor immutable ID; AccessFacts enrollment/course/group и TeacherAccess null WholeCourse требуют explicit new claims mapping; существующая схема/immutable migrations сохранены. New security entities вне ProblemWork, middleware/ports в Clean Architecture. No new LearningSpace, global Teacher, reassign history, guessed unknown owner.

Нагрузка: synthetic current restrictions читают DB, обычный JWT grants validator локальный; security guarantee не заменяется положительным grant cache. Это признанный throughput cost, G04 measurements до оптимизации. O01 coordination сериализует короткий refresh в браузере, не учебные запросы. G02 unbounded DB/edge completion при failures может заблокировать acceptance; обещания при произвольном отказе железа не сделаны.

## Open categories и пределы

O01 owner choice/delegation до S4. G01–G04 engineering review/test gates, не требуют переутверждения E01–E06. Real privacy freshness, PD-G01–14/ORG-G05/08 и C03–C09 не закрываются; полный список [readiness](L03-L1-READINESS.md). Real representatives/consent/retention/erasure excluded, synthetic independent personas не legal evidence.

Исторические L01/L02/L03 results и L03 reconciliation сохранены, EXECPLAN добавлен superseding L0.4 checkpoint. Актуальны [register](L03-OWNER-DECISION-REGISTER.md), [plan](L03-L1-IMPLEMENTATION-PLAN.md), [execution task](CODEX-TASK-L1-EXECUTION.md). В них no permission to execute. [Results](L04-RESULTS.md) фиксирует validation.

Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY. LEGAL SOURCE NOT VERIFIED. SYNTHETIC DATA ONLY.
