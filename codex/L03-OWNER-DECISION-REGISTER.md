# L0.3 / L0.4 — Owner Decision Register

2026-10-09. Owner: владелец проекта. Evidence: явное задание «L0.3 — Owner Decisions Consolidation & L1 Readiness», §4 T01–T06, окончательная редакция T04. База проверена: `ee39d38d500ef6e820429827809dd1a70a4797ab`. ACCEPTED означает решение, не реализацию и не разрешение L1. Документация не подтверждает юридическое соответствие.

Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY. LEGAL SOURCE NOT VERIFIED.

| Decision ID | Status | Утверждённая редакция / инварианты | Затрагиваемые документы | Открытые инженерные / юридические вопросы | Последствия L1 |
|---|---|---|---|---|---|
| T01 Account Security | ACCEPTED | Одноразовые activation/recovery codes: 16 символов Crockford Base32, 80 бит CSPRNG, группы по четыре; activation 24 часа, recovery 15 минут; максимум 5 неудачных проверок кода. Проверка: 10/мин/IP и 5/15 мин/account. Выдача: 3/час/получатель, 20/час/инициатор, cooldown 60 секунд. Только HMAC-SHA256 с отдельным секретом и процедурой ротации; пароли 12–128 символов, ASP.NET Core Identity; browser protection, CSRF при cookies, audit без секретов, NAT tests | ADR-L01/L02/L04/L06, L1 plan, UI contracts | E02/E05 ниже; реальные сроки audit/полномочия представителей PD-G03/07 | Восемьдесят бит приняты; прежние 26 символов / 128 бит отклонены. Серверная cookie-сессия как обязательный transport заменена T04 |
| T02 Provisioning & Recovery | ACCEPTED | Минимальный org-admin UI: account из roster, основная Student или Teacher роль, выдача activation, recovery, отдельные membership/enrollment/teacher assignment, блокировка/отзыв. Нет public signup/обязательных email/phone. Код виден только при выдаче после личной проверки или через заранее подтверждённый индивидуальный канал. Recovery проверяет получателя/представителя. Org admin видит только свою организацию. Shared account recovery требует независимого account-security подтверждения; первый org admin создаётся уполномоченным platform operator; platform admin не reader | ADR-L02/L14, Identity scopes, UI contracts, L1 plan | E04 ACCEPTED: independent platform security operator; юридическая проверка представителей несовершеннолетних OPEN | UI принят, CLI не альтернативное условие запуска. Полная multi-org панель вне L1; создание account не enrol |
| T03 Physical Schema | ACCEPTED | AspNetUsers и связанные Identity tables; Actors, ActorIdentities, Organizations, OrganizationMemberships, CourseRegistry, CourseGroups, EnrollmentScopes, TeacherAssignmentScopes, AttachmentScopes; структуры provisioning/challenges/sessions/security/audit. ActorId стабильный UUID; user↔actor 1:1; multi-org account; Course ровно одной Organization, unknown quarantine; Generation либо эквивалент не оживляет revoked grants; DB cross-org constraints; no Identity/Organization cascade в историю; sidecars сохраняют IDs/immutable history | ADR-L01/L11/L12/L14, domain, organization/identity/isolation models, L1 plan | Конкретные поля/index/trigger DDL — инженерный дизайн с проверкой; E01; реальные mapping/поля PD-G04 | Аддитивная schema, без массового переписывания aggregates |
| T04 JWT & Delayed Revocation | ACCEPTED | Подписанный access JWT ≤15 минут, local signature/lifetime/issuer/audience/scope validation. Fresh DB authority при issue/refresh. Обычный revoke атомарно фиксирует AccessValidUntil ≤ RevocationRequestedAt + 2 hours; TokenExpiresAt = min(Now + 15 minutes, AccessValidUntil - 10 minutes). Недостаточный остаток — не выдавать такие права. Ordinary API 60 секунд, max HTTP 8 минут, reserve 10 минут. Scope-specific revoke, A не отзывает B; jobs и irreversible writes не наследуют бессрочный JWT | ADR-L01/L14/L15, ORG-G04, isolation, feature/UI, L1 plan | E01/E02/E03/E06 ниже; legal gates не закрыты | Отменены ordinary revoke barrier, mandatory per-request PG freshness/stream locks/Redis/immediate JWT invalidation. Emergency security отдельно |
| T05 ProcessingContext | ACCEPTED | ProcessingContextVersions, CourseContextBindings, ProcessingRestrictions вне aggregates; immutable versions, course binding с периодом и той же Organization; historic operation→действовавшая версия; current restrictions выше historic allow. Нет подтверждённого основания — реальные ПД запрещены | ADR-L13, context model, domain, L1 plan | E06 ACCEPTED: synthetic enforcement; real freshness отдельно; PD-G02/03/07/08 | SYNTHETIC DATA ONLY; нет полноценного consent/contracts/retention/legal basis UI. Запись context не evidence compliance |
| T06 Additive Migration | ACCEPTED | M1 schema expansion → M2 inventory/controlled backfill → M3 constraints/cutover. Empty DB без переноса; synthetic explicit mapping; unknown/real автоматический перенос запрещён; quarantine. IDs/history сохранены; repeatability где предусмотрено; no guessed owner/no grants from account; проверенные backup/restore и отдельный controlled cutover | ADR-L11, organization model, L1 plan, execution task | DDL/dry-run/restore evidence ещё NOT RUN; реальные manifest/основания вне L1 | T06 не разрешает запуск migrations на реальной БД; L0.3 не запускает никаких migrations |

## E01–E06 — ACCEPTED в L0.4

2026-10-09. Owner: владелец проекта. Evidence: явное задание «L0.4: E01–E06 Consolidation & Final L1 Readiness», §4. Verified base `836aad59d01f1cd7c04e9e892ebd2d8621f7c59f`. T01–T06 сохранены. Прежние E01–E06 OPEN заменены следующей редакцией, L1 не разрешён.

| Decision ID | Status | Утверждённая редакция / инварианты | Зависимости / документы | Влияние на L1 / вопросы |
|---|---|---|---|---|
| E01 Organization-scoped JWT | ACCEPTED | Один access JWT — одна active org; user/actor/session/org IDs и concrete course/group scopes. Switch/refresh читают PostgreSQL; A revoke не затрагивает B. Exp по T04/earliest included grants, скоро истекающие можно исключить. Teacher не blanket access; принадлежность ресурса — server model, не JWT/client. Нет mandatory Redis/per-request DB grant freshness | T03/T04; ADR-L16/L15, domain/isolation/UI | S3/S4; claim encoding/size — engineering |
| E02 Browser Session | ACCEPTED | Access ≤15m в памяти; opaque random refresh в HttpOnly Secure cookie, protected verifiable server storage; mandatory rotation. Idle60m, absolute12h, no localStorage. SameSite/CSRF/Origin/XSS; reload refresh; logout revoke refresh-session, recovery revoke всех sessions. Access JWT до exp. Вкладки разных org делят session; concurrency без ложного termination, reused predecessor — potential compromise | T01/T04/E01; ADR-L16 | S2 primitives/S4 browser; O01 ACCEPTED below |
| E03 Emergency Blocking | ACCEPTED | SecurityBlocked запрещает login/issue/refresh, revokes всех refresh-sessions и audit; unblock отдельное confirmed действие, old grants не восстанавливаются. Existing JWT до exp≤15m, started operation≤8m; не мгновенное прекращение. No mandatory denylist/Redis | T04/E02; ADR-L17/L15 | S6/S7; clock/completion G02 engineering evidence |
| E04 Shared Recovery | ACCEPTED | Org admin инициирует, independent platform security operator проверяет recipient/representative и approves; затем one-time code → verified recipient → new password → revoke refresh-sessions. Initiator≠approver, roster не proof, duplicate account не bypass, no secrets/document copies audit; verification impossible→pause. Minimal operator UI, no education access | T02/T03/E02/E05; ADR-L17 | S5 request/S6 approval+UI; real minors PD-G03 OPEN |
| E05 Code Lifecycle | ACCEPTED | Activation/Recovery невзаимозаменяемы; T01 16/80 Crockford+HMAC/24h/15m/rates сохранены. Created→Consumed/Expired/Revoked/Locked, terminal irreversible; reissue revokes previous same purpose; ≤5 failures; atomic target+consume, no concurrent reuse. Secret once/no redisplay; lost→new code; same idempotency key не создаёт лишний code/не раскрывает secret повторно. SecurityBlocked deny; shared recovery E04 approval; no secrets audit | T01/T03/E03/E04; ADR-L17 | S6; normalization/key custody/limiter topology engineering G03; не generic OTP platform |
| E06 Restrictions | ACCEPTED | Current restrictions отдельно от JWT, выше historical allow; immutable context versions, period course binding/same org. Missing required context/basis deny. L1 SyntheticOnly; JWT не разрешает real PII. Real pilot требует отдельного freshness решения и legal closure; ordinary2h не privacy delay | T04/T05/T06; ADR-L18/security spec | S4 admission/S7 hardening; no legal UI; real-data freshness pre-pilot gate |

## OPEN DECISIONS и engineering review

- **O01 ACCEPTED — owner L1/S2 task, 2026-10-09.** Atomic rotation and transactional serialization; verified consumed token within 30 seconds inclusive → `409 refresh_already_rotated`, no token/JWT/Set-Cookie, no automatic compromise. After 30 seconds → revoke only that refresh-session plus atomic secret-free audit. Other sessions stay enabled. Tabs coordinate, server safety is independent. Lost committed response does not guarantee recovery; no successor redisclosure. E02/E03/T04 preserved; full protocol in [ADR-L16](../docs/adr/ADR-L16-organization-jwt-browser-session.md). S1 ACCEPTED; S2 AUTHORIZED only; S3–S8 remain unauthorized.
- **G01 Engineering, nonblocking foundations:** exact DTO/claim encoding/size, schema/index/transaction review, отдельная platform-security capability/audience без educational grants; T/E boundaries не менять.
- **G02 Engineering validation gate:** conservative expiry/измеримые clock uncertainty и full-operation bounds по ADR-L15. Если ≤15m/≤8m не доказуемы — STOP S4/S7/S8, не ослаблять T04/E03. Настройка timeouts не требует нового owner решения; ослабление bounds требует.
- **G03 Engineering:** normalization/CSPRNG/HMAC versions/rotation runbook, idempotency/limiter topology; aggregate recipient/account/initiator rates не сбрасываются purpose/reissue. Числа T01 не менять, real custodian/retention operations gates.
- **G04 Engineering:** synthetic restriction race checks и duplicate-provision guard. Без email/phone нельзя обещать автоматическую глобальную идентификацию человека; ambiguous identity → independent review, не обход recovery новым account.

[PD-G01–14](L01-DECISION-GATES.md), [ORG-G05/08](L02-DECISION-GATES.md), C03–C09 [общего реестра](L-DECISION-REGISTER.md) открыты. Real ProcessingRestrictions freshness и real minors procedure — отдельные pre-pilot decisions, не закрыты техническим ACCEPTED. [Readiness](L03-L1-READINESS.md), [L0.4 reconciliation](L04-DECISION-RECONCILIATION.md), [execution task](CODEX-TASK-L1-EXECUTION.md).

## L1/S3 / B01 authorization checkpoint — 2026-10-09

The explicit owner continuation accepts **B01 — Generation-aware Enrollment Move**, accepts S1/S2/O01 and authorizes only S3. This supersedes earlier slice-authorization statements while retaining their historical evidence. A separate forward migration may replace enrollment-specific immutable GroupId guards with atomic same-course moves, immutable generation history, expected-version/membership checks, commit-time consistency and restricted-role enforcement. Stable student/course/org keys and historical learning affiliation must remain unchanged. Existing work must not follow a student's group move automatically. S4–S8, M2/M3, legacy backfill, production migrations, real personal data and push/merge/deploy remain forbidden. See [S3 authority design](../docs/architecture/L1-S3-SCOPED-AUTHORITY.md) and the S3 results checkpoint for implementation evidence.


## L1/S4 authorization and G02 stop checkpoint — 2026-10-09

The explicit owner S4 task accepts S1/S2/S3/B01/O01 and authorizes S4 only, superseding earlier slice authorization statements. Baseline `4acd70d`, requested branch `codex/l1-s4-jwt-api-authorization`, canonical directory, no new worktree. S5–S8, M2/M3, production migration, real personal data and push/merge/deploy remain prohibited.

**S4 BLOCKED** at the G02 full file-completion boundary: a synthetic Linux FIFO test demonstrates that an entered local storage open remains pending after cancellation. This is a fault surrogate, not a production exploit or an eight-minute HTTP test. No runtime cutover or policy weakening was implemented. See [S4 results](L1-S4-RESULTS.md) for evidence, limits and the owner decision requested before continuation.

## L1/S4-A3-R1 — PRS1 normative owner acceptance — 2026-10-10

Owner: владелец проекта. Evidence: явное сообщение «Owner Decision — L1/S4-A3-R1» от 2026-10-10; [полная фиксация решения](L1-S4-A3-OWNER-DECISION.md). До записи сверены [contract §1–6](L1-S4-A3-SELECTOR-CONTRACT.md), [acceptance matrix](L1-S4-A3-SELECTOR-ACCEPTANCE-MATRIX.md) и [R1 normative diff](L1-S4-A3-R1-NORMATIVE-DIFF.md); расхождений с принятой редакцией не выявлено.

Нормативная семантика PRS1 принята: canonical same-org ResourceRef; Read/Submit/Review=1/2/4, новые masks 1–7; reserved 8/16 conservative deny; historical attempt subtree; все source/subject dependencies, XOR attachment parent и pending deny; full affectedSubjects и safe prefilter; legacy conservative без automatic narrowing. Exact matching требует durable DB-backed provenance и отдельно принятого механизма. RB01–RB03, RT01–RT05, N01–N05, AF01–AF06, PV01–PV10 нормативны для будущей проверки, **NOT IMPLEMENTED / NOT RUN**.

Разрешена только документационная фиксация. Не разрешены exact implementation, runtime/API/schema/migrations, конкретный DDL sidecar, trusted administrative writer/transaction lock protocol, rollout/reissue/migration исторических restrictions, HTTP/file/storage/production integration. SR-01–SR-04 сохраняются; внешний human security audit OPEN. T04/E03 не изменены; G02 не закрыт.

**PRS1 NORMATIVE CONTRACT ACCEPTED · EXACT IMPLEMENTATION NOT AUTHORIZED · T04 UNCHANGED · E03 UNCHANGED · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**


## L1/S4-A4-I1 acceptance / I2-P authorization — 2026-10-10

Owner: владелец проекта. Evidence: явное сообщение «Owner Decision — L1/S4-A4-I1». **S4-A4-I1 COMPONENT SCOPE ACCEPTED:** canonical PRS1 parser/builder, explicit operation registry, pure acceptance tests, Application regression по [I1 report](L1-S4-A4-I1-RESULTS.md), отсутствие изменений действующего authorization path. Это component acceptance, не включение exact gate; исторический I1 report сохранён.

**S4-A4-I2-P — ISOLATED SCHEMA VALIDATION AUTHORIZED (CONFIRMED):** проверить candidate PostgreSQL DDL, restricted roles, immutable provenance, trusted writer boundary, late attestation rejection и atomic pairing только в disposable synthetic database; подготовить [I2-P results](L1-S4-A4-I2-P-RESULTS.md) с дефектами и точными предложениями исправлений. I2 implementation, production migrations, exact PRS1 gate, API/HTTP и реальные данные не разрешены. Сохранить посторонние изменения и текущую ветку; без commit, merge, rebase, push и deploy.

**I1 ACCEPTED · I2-P AUTHORIZED · I2 IMPLEMENTATION NOT AUTHORIZED · T04/E03 UNCHANGED · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**


## MVP-B07a acceptance / B07b-P documentation authorization — 2026-10-11

Owner: владелец проекта. Evidence: явное сообщение «OWNER DECISION — MVP-B07a ACCEPTED». **MVP-B07a COMPONENT SCOPE ACCEPTED**: автономный Markdown validator/compiler в проверенных границах Linux x86_64 и synthetic offline execution. По [implementation report](MVP-B07a-IMPLEMENTATION-RESULTS.md): 74 component tests PASS; Application regression 272 PASS из предыдущего запуска, повтор после изменения math integration NOT RUN. Runtime/HTTP/browser/physical security acceptance не заявляется. Исторические решения и отчёты сохранены; их прежние pending/ready заголовки не отменяют эту более позднюю приёмку.

Разрешён только B07b-P: [проект узкого контракта интеграции](MVP-B07b-INTEGRATION-CONTRACT.md), без runtime, schema, migrations, API или frontend. Результат остановить на owner review. Не выполнять commit/push/merge/rebase/deploy; сохранить посторонние изменения.

**B07a ACCEPTED · B07b-P DOCUMENTATION AUTHORIZED · B07b IMPLEMENTATION NOT AUTHORIZED · B08 NOT AUTHORIZED · PRS1 EXACT OFF · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**


## MVP-B07b-P acceptance with conditions / B07b-C1 authorization — 2026-10-11

Owner: владелец проекта. Evidence: явное сообщение «OWNER AUTHORIZATION — MVP-B07b-C1». **B07b-P ACCEPTED WITH CONDITIONS; B07b-C1 ISOLATED COMPONENT IMPLEMENTATION AUTHORIZED.** Разрешён только trusted local synthetic ZIP → isolated B07a worker → private test staging → проверенные artifacts/closed BuildReceipt. Лимиты — экспериментальный synthetic profile, не operational/G02 acceptance. Исторический [B07b-P contract](MVP-B07b-INTEGRATION-CONTRACT.md) сохранён; его прежний pending header описывает состояние до этого решения.

Никаких HTTP/frontend/PostgreSQL schema/migrations/production DI, изменений S2/S3/S4, import grants/publisher capability, durable import storage/production TTL, изменений B07a contract, публикации/назначений или реальных ПД. Существующие branch/index/посторонние изменения сохраняются; commit/merge/rebase/push/deploy запрещены. [C1 results](MVP-B07b-C1-RESULTS.md) подлежат отдельной owner acceptance; эта запись не принимает реализацию.

**B07b-C1 AUTHORIZED · B07b SERVER INTEGRATION NOT AUTHORIZED · B08 NOT AUTHORIZED · PRS1 EXACT OFF · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**


## MVP-B07b-C1 acceptance / B07b-C2-P documentation authorization — 2026-10-11

Owner: владелец проекта. Evidence: явное сообщение «OWNER DECISION — MVP-B07b-C1 ACCEPTED». **B07b-C1 COMPONENT SCOPE ACCEPTED:** isolated synthetic ZIP import на Linux x86_64, bounded worker только в проверенных пределах, неизменённые B07a artifacts и BuildReceipt. Приёмка не включает server integration, HTTP, БД или publication. Все ограничения [MVP-B07b-C1-RESULTS](MVP-B07b-C1-RESULTS.md), включая narrow ZIP/runtime profile, trusted host assumptions, sampling/parent I/O/control-plane/teardown proof gaps и отсутствие G02 acceptance, сохранены без изъятий. Исторические 49 C1 / 74 B07a PASS не являются новым прогоном; исходный отчёт и evidence не переписаны.

**MVP-B07b-C2-P DOCUMENTATION/CONTRACT ONLY AUTHORIZED:** подготовить [минимальный draft storage/lifecycle design](MVP-B07b-C2-INTEGRATION-DESIGN.md): ImportAttempt, trusted org/course/actor/context binding, private ZIP/artifacts/receipt, FS/PostgreSQL atomic visibility, locks/CAS, recovery/unknown commit, expiry/cleanup/pin, current restrictions, trusted operations, reuse S2/S3/S4 и зависимости B01/B02/B11. Отсутствующее полномочие вынести на точный owner decision; не вводить grants/roles неявно. Design и OD-C2-01/02 остаются предложениями для owner review, не принятыми этой записью.

Не разрешены runtime, schema, migrations, API, frontend, PRS1 или изменения существующего authorization; universal blob storage/CMS/альтернативные роли исключены. Существующие branch/index/посторонние изменения сохранить; commit/push/merge/rebase/deploy запрещены. Остановиться на owner review; C2 server implementation и B08 не разрешены.

**B07b-C1 ACCEPTED · B07b-C2-P DOCUMENTATION AUTHORIZED · B07b SERVER IMPLEMENTATION NOT AUTHORIZED · B08 NOT AUTHORIZED · PRS1 EXACT OFF · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**


## MVP-B07b-C2-J acceptance / B01-G02 closure assessment — 2026-10-11

Owner: владелец проекта. Evidence: явное сообщение «OWNER DECISION — MVP-B07b-C2-J ACCEPTED». **C2-J ISOLATED IMPLEMENTATION ACCEPTED; H03 CLOSED IN ISOLATED SCOPE.** Принята внутренняя provisioning capability и проверенная совместимость restricted S2/S3 в границах [итогового отчёта C2-J](MVP-B07b-C2-J-RESULTS.md). По этому отчёту финальная regression **732/732 PASS, failed 0, skipped 0** (43 Domain + 272 Application + 198 Infrastructure + 219 API); это исторический результат, не новый прогон. Заголовок acceptance pending в исходном отчёте сохранён как история и заменён настоящим решением только в части owner acceptance.

**H04 отложен до реализации ImportAttempt и отдельного закрытого import writer; Import Ready BLOCKED.** Приёмка H03 не открывает private composition helpers и не доказывает Ready. Production/HTTP cutover не разрешён; G02 остаётся BLOCKED. T04/E03, текущие ограничения S4, B08 и PRS1 exact не изменены.

Авторизована только documentation/repository inspection для B01/G02 closure assessment: [MVP-B01-G02-CLOSURE-PLAN](MVP-B01-G02-CLOSURE-PLAN.md), требования/evidence, минимальные изменения, acceptance tests, зависимости и точные owner decisions. План не разрешает реализацию и не принимает A06 normative proposals. Сохранить branch, HEAD, index и посторонние изменения; без runtime/G02/S4/HTTP/B08/PRS1 изменений, миграций действующей БД, commit/push/deploy. Остановиться на owner review.

**C2-J ACCEPTED · H03 CLOSED IN ISOLATED SCOPE · B01/G02 CLOSURE ASSESSMENT AUTHORIZED · H04/READY BLOCKED · SERVER/HTTP CUTOVER NOT AUTHORIZED · B08 NOT AUTHORIZED · PRS1 EXACT OFF · G02 BLOCKED.**


## MVP-B01/G02-P1 acceptance NO-GO / G02-NORMATIVE-REVIEW authorization — 2026-10-11

Owner: владелец проекта. Evidence: явное сообщение «OWNER DECISION — MVP-B01/G02-P1». **P1 QUALIFICATION ACCEPTED — NO-GO; P1-SINGLE-LOCAL QUALIFICATION STOPPED.** [P1 qualification](MVP-B01-G02-P1-QUALIFICATION.md) принят как документальная квалификация с результатом NO-GO. Clock, PostgreSQL completion и controlled egress не имеют доказанных hard upper bounds. P1-SINGLE-LOCAL не разрешён для G02/S4 HTTP cutover. Это приёмка отрицательного вывода документа, не runtime acceptance и не доказательство невозможности любого deployment.

**CURRENT T04/E03 REMAIN IN FORCE; A06 E03-C AND N NOT ACCEPTED.** Исторические решения, результаты и предложения не переписываются. H04 отложен до ImportAttempt и отдельного закрытого import writer; H04/Import Ready остаются BLOCKED. B08 не разрешён; PRS1 exact остаётся OFF, прежние ограничения сохраняются.

Разрешён только **G02-NORMATIVE-REVIEW, documentation only**: [сравнение действующего T04/E03, A06 и ограниченной safety/recoverability альтернативы](MVP-B01-G02-NORMATIVE-REVIEW.md), точный предлагаемый diff, последствия, acceptance tests, blockers и текст будущего owner decision. Предложения review этим решением не принимаются. Не разрешены изменения действующих ADR/T04/E03, production configuration, реализация или Git-операции. Остановиться на owner review.

**P1 ACCEPTED · G02 NORMATIVE REVIEW AUTHORIZED · A06 NOT ACCEPTED · G02 BLOCKED · H04/READY BLOCKED · S4 HTTP NOT AUTHORIZED.**


## MVP-B01/G02-NR1 — normative acceptance / documentation synchronization — 2026-10-11

Owner: владелец проекта. Основание: явное сообщение «OWNER DECISION — MVP-B01/G02-NR1 — NR1 NORMATIVE MODEL ACCEPTED — SYNTHETICONLY SCOPE». **Дата вступления в силу: 2026-10-11**, с настоящего решения; не ретроактивная техническая приёмка.

Приняты в точности [N1–N7 и ограниченный supersession D1–D8](MVP-B01-G02-NORMATIVE-REVIEW.md). Это изменение temporal completion guarantees: 60/480s — application deadline в qualified execution envelope; возможны поздняя видимость ранее допустимого атомарного набора learning mutations и поздняя передача уже queued bytes. Универсального recovery RTO при произвольной инфраструктурной остановке нет; безопасный durable учёт pending обязателен. Прежний T04 этим не доказан. Scope — только controlled SyntheticOnly, route-by-route acceptance ещё отсутствует; Submit/Review/attachment claim — кандидаты, ImportAttempt writer требует отдельного включения. Неопределённый path blocked.

D1–D4/D8 заменяют только указанные completion clauses ADR-L15 и производные реестра; D5 — только completion clause E03 ADR-L17; D6 — I6; D7 — temporal stream dependency ADR-L18. Прежние тексты остаются историей. Для NR1 D8 применяется точный текст:

> JWT, выпущенный до заявки, истечёт максимум через 15 минут; 60/480s ограничивают новые application actions по NR1 N1, но поздняя видимость ранее допустимого COMMIT и queued bytes не имеют этого wall-clock bound. Утверждение о полном завершении через 23 минуты не применяется.

> Если conservative true JWT≤15m, application deadline enforcement в принятом envelope, domain safety или recovery/capacity не доказаны — STOP соответствующего path и общей acceptance; physical completion внешнего I/O≤8m не является общим prerequisite NR1. Исключённые paths сохраняют прежний gate.

True JWT lifetime≤900s, conservative expiry без положительного leeway, exp≤AccessValidUntil−600s, D≤request+7200s без retry extension, delayed revocation, E01/E02/O01/E04/E05, S2/S3 org/course/group authorization, current ProcessingRestrictions и сериализация writes, atomic mutation/result/audit, immutable history/file versions, parent authorization и отдельная авторизация каждого нового запроса сохранены. Unknown outcome ≠ rollback; automatic replay запрещён. Late-COMMIT исключение не распространяется автоматически на credential issuance, grants, B08 publication, general jobs и внешние irreversible effects. CLOCK NOT PROVEN — самостоятельный обязательный blocker для issuance/admission/q и общей acceptance.

**NR1 нормативно принято; технически NOT IMPLEMENTED / NOT ACCEPTED.** A06 E03-C/N и ND-01–ND-10 не принимаются. P1-SINGLE-LOCAL остаётся NO-GO; T04/E03/A02–A06 evidence и failure results сохранены, не исправлены и не превращены в PASS.

Авторизована только документационная синхронизация и [короткий implementation roadmap](MVP-B01-G02-NR1-IMPLEMENTATION-ROADMAP.md); [результат и точный diff](MVP-B01-G02-NR1-SYNC-RESULTS.md) остановить на owner acceptance. Следующий инженерный slice должен давать проверяемый код qualified clock source и evidence после отдельного задания, без нового бесконечного qualification цикла. Runtime/schema/production DI/HTTP/Docker/edge/processing guards не менять. Branch/HEAD/index и посторонние изменения сохранить; commit/merge/rebase/push/deploy запрещены.

**NR1 NORMATIVELY ACCEPTED · G02 IMPLEMENTATION BLOCKED · CLOCK NOT PROVEN · H04/READY BLOCKED · B08 NOT AUTHORIZED · PRS1 EXACT OFF · S4 HTTP CUTOVER NOT AUTHORIZED.**


## 2026-10-12 — Sapientum MVP photo storage risk acceptance

Explicit owner decision: **PHYSICAL TAMPERING RISK ACCEPTED · FS-VERITY NOT REQUIRED FOR MVP · STORAGE-PROFILE-02 CANCELLED · PHOTO READ DEVELOPMENT AUTHORIZED IN SYNTHETIC SCOPE · S2/S3/PROCESSING GUARANTEES PRESERVED · G02 PRODUCTION BLOCKED.** The [exact scoped NR1/ADR override](MVP-STORAGE-RISK-ACCEPTANCE.md) is effective now for solution photographs only. Historical NO-GO/test results retained. No other NR1 guarantees or production gates waived. No commit/push/merge/rebase/deploy/cutover authorized.


## 2026-10-12 — bounded teacher revocation: implementation authorization

Источник: задание AUTH-01–AUTH-04, ответ владельца «выполни план» после анализа и ответ «2» на вопрос о модели отказов: **ограниченная модель с проверяемыми условиями эксплуатации**. Разрешены реализация согласованного плана и тесты в каноническом checkout, без commit/push/deploy. [Точный scoped contract и supersession](../docs/architecture/TEACHER-REVOCATION.md).

JWT≤15m, fresh authority на каждом issue/refresh, logical operation≤30m без renew через retry/refresh, revoke≤60m в принятом envelope. Ordinary two-hour D и D−10m заменены исключением revoked rights из новых выдач; historical rows остаются. Неограниченные late COMMIT/egress не выполняют новый temporal contract; вне подтверждённого envelope временная гарантия NOT PROVEN, unknown сохраняется без replay. Processing gates, immutable history, private files/parent authorization, O01 и photo physical-tampering acceptance сохранены.

Это не приёмка реализации и не подтверждение 30s completion reserve текущей инфраструктурой. PRS1 EXACT OFF, G02 PRODUCTION BLOCKED; публичный HTTP cutover/реальные ПД не включены. Canonical checkout остаётся на dirty codex/l1-s4-jwt-api-authorization согласно запрету переключать незавершённую работу. Результаты и оставшиеся части плана фиксируются в [отчёте](TEACHER-REVOCATION-RESULTS.md).

## 2026-10-12 — Teacher Revocation logical deadline / physical completion cancelled

Owner: владелец проекта. Основание: явное решение «Sapientum — упрощение требований Teacher Revocation». [Полная запись решения](TEACHER-REVOCATION-OWNER-DECISION.md).

Для MVP отменено требование гарантированного физического завершения PostgreSQL `COMMIT`, filesystem/kernel I/O, HTTP egress, cleanup и процессов за 30 минут. Действующий контракт: JWT≤15m; fresh authority на issue/refresh; immutable logical deadline≤30m; cancellation request при deadline; после deadline нет success acknowledgement; replay/unknown outcome/access control/data protection сохраняются. Late physical side effect принят как остаточный риск и не даёт права на late success или повтор.

Исторические A02/A04/A05/A06/P1/FIFO/PG/storage/edge результаты сохранены и не переименованы в PASS. Отсутствие supervisor, worker isolation, hard-real-time fencing, physical quiescence или worst-case syscall/COMMIT bounds больше не является blocker само по себе. Логический deadline, clock/admission correctness, cancellation/no-late-success, replay/unknown, S2/S3/processing/privacy и остальные production gates не сняты.

Разрешено продолжить текущую HTTP/React migration и исправление API regressions под сохранёнными инвариантами. Commit/deploy и работа с реальными ПД не разрешены.

**TEACHER-REVOCATION PHYSICAL GATE CANCELLED · LOGICAL 15/30/60 CONTRACT ACCEPTED · HTTP/REACT MIGRATION CONTINUATION AUTHORIZED · HISTORICAL RESULTS RETAINED · NO PRODUCTION-READY CLAIM · NO COMMIT/DEPLOY.**

## 2026-10-13 — M1-06 Content Import & Publication, SyntheticOnly

Owner: владелец проекта. Основание: явный ответ «да» на представленное перед реализацией точное решение M1-06.

Разрешены только в границах M1-06 и controlled `SyntheticOnly`: принятие OD-C2-01/OD-C2-02 для этого среза; whole-course teacher publisher grant через существующий S3 `TeacherAssignmentScope(GroupId=null)`; H04 `ImportAttempt` writer; additive schema/migration/Application/API/React; trusted server adapter к неизменённым B07a/B07b; защищённые test/local HTTPS routes; immutable v1/v2, exact-version assignments, stable idempotency/CAS/unknown-outcome semantics, current org/course/group scopes и processing restrictions; student/teacher projection isolation; real Chromium evidence.

Решение не разрешает production/public cutover, реальные ПД, universal CMS/blob storage, новый compiler/importer, WYSIWYG, M1-07, PRS1 exact, ослабление G02 или обход существующих S2/S3/processing contracts. Restricted production database capability и deployment qualification остаются отдельной границей. Commit/push/deploy не разрешены.

**M1-06 SYNTHETIC-ONLY IMPLEMENTATION AUTHORIZED · OD-C2-01/02 ACCEPTED FOR THIS SLICE · H04 IMPORTATTEMPT AUTHORIZED · WHOLE-COURSE S3 PUBLISHER ACCEPTED · TEST HTTPS ONLY · G02/PRODUCTION BLOCKED · NO M1-07 · NO COMMIT/DEPLOY.**


## 2026-10-13 — M1-10 HTTP read authorization, SyntheticOnly

Owner: владелец проекта. Основание: явное сообщение «OWNER DECISION — M1-10 HTTP READ AUTHORIZATION».

Только для M1-10 и controlled `SyntheticOnly` разрешены реализация и регистрация трёх защищённых read-only HTTP projections:

- `GET /api/v1/teacher/group-progress` — прогресс учеников/групп и серверные счётчики назначений/status;
- `GET /api/v1/student/progress-history` — собственная история назначений и попыток ученика;
- `GET /api/v1/teacher/review-history` — история разрешённых проверок преподавателя, включая `Accepted`.

Маршруты используют существующие S2/S3/S4 authentication, текущие полномочия и `ProcessingRestrictions`; организация, курс, группа, ученик и teacher assignment проверяются сервером до возврата строк, счётчиков и агрегатов. Разрешено authoritative read-back текущего состояния после timeout/потери ответа; неизвестный результат не разрешает автоматический повтор write-операции. Teacher Revocation и действующие ограничения не ослабляются.

Исключение из `S4 HTTP NOT AUTHORIZED` действует только для перечисленных GET routes. Новые роли, permissions, grants, schema и write endpoints не разрешены. Production/public cutover, реальные ПД, push и deploy запрещены; G02 остаётся BLOCKED. M1-11 не разрешён этим решением.

**M1-10 SYNTHETIC-ONLY GET PROJECTIONS AUTHORIZED · EXACT THREE ROUTES · S2/S3/S4 + PROCESSING PRESERVED · NO SCHEMA/WRITES/NEW GRANTS · G02/PRODUCTION BLOCKED · NO M1-11/PUSH/DEPLOY.**
