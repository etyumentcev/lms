# L1 — Identity, JWT, Organization Scopes & Access Lifecycle

2026-10-09. T01–T06 и E01–E06: **ACCEPTED** по [реестру владельца](L03-OWNER-DECISION-REGISTER.md). L1 **НЕ РАЗРЕШЁН / НЕ НАЧАТ**. Future scope: **SYNTHETIC DATA ONLY**. Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY. LEGAL SOURCE NOT VERIFIED.

Принятые решения задают требования. DTO/DDL/алгоритмы ниже — инженерная детализация, не самостоятельное owner approval. E01–E06 закрыты L0.4; только **O01 concurrent refresh coordination/replay policy** требует выбора до S4. G01–G04 — engineering review/validation, не blanket blockers S1–S3. [ADR-L15](../docs/adr/ADR-L15-jwt-delayed-revocation.md), [L16](../docs/adr/ADR-L16-organization-jwt-browser-session.md), [L17](../docs/adr/ADR-L17-account-security-recovery-challenges.md), [L18](../docs/adr/ADR-L18-synthetic-processing-restrictions.md) — нормативные contracts; [security specification](../docs/architecture/L1-SECURITY-SPEC.md) — trust boundaries. [Execution task](CODEX-TASK-L1-EXECUTION.md) не выполняется.

## 1. Контекст и неизменяемая граница

Canonical `/home/tea/github/lms`, последовательно, без новых worktrees; future branch/HEAD/clean tree проверить после отдельного разрешения. Не считать L0.3 base автоматической базой L1. Только synthetic A/B, общий S_AB, outsiders S_A/S_B, teachers T_A/T_B, admin-only OA/OB, platform-only P. Нет реальных roster/телефонов/email/представителей/согласий/договоров и фиктивного legal basis. Никаких production erasure/consent UI, полной multi-org панели или L2–L4.

[Domain ProblemWork](../src/Lms.Domain/ProblemWork.cs) сохраняет Submission→Review→Revise→Accept, terminal Accepted, immutable content refs/history/annotations. Authorization не новый Domain status. [Application ports](../src/Lms.Application/Ports.cs) отделяют identity/scope/context от aggregates. [DbContext](../src/Lms.Infrastructure/LmsDbContext.cs), [AccessFacts](../src/Lms.Infrastructure/AccessFacts.cs), [AccessPolicy](../src/Lms.Infrastructure/PostgresAccessPolicy.cs), [WorkStore](../src/Lms.Infrastructure/PostgresWorkStore.cs), [AttachmentService](../src/Lms.Infrastructure/AttachmentService.cs) расширяются аддитивно. LINQ и raw SQL [queue](../src/Lms.Infrastructure/PostgresReviewQueue.cs) фильтруют scope до totals/paging; одного global filter недостаточно. API и browser используют JWT, но existing development tooling не production issuance.

База схемы: последняя существующая migration `20261008154119_ReviewAnnotations`; CourseId/GroupId строки, ActorId Guid. Scope sidecars дополняют existing access tables; никаких массовых UPDATE immutable history. Course ownership неизвестен → quarantine/deny, не default organization.

## 2. Schema T03/T05 — accepted records, engineering details

Все FK по умолчанию RESTRICT. User↔Actor 1:1, stable UUID; один course owner; multi-org membership; course-local group. Membership/grant Generation или эквивалент исключает оживление старых grants после regrant. FK на стабильную пару membership, не на изменяемый Generation. Active/withdrawal/deadline fields уточняются в implementation review; двухфазный Revoking/drain не требуется.

| Record/table | PK / FK / поля | Constraints и indexes |
|---|---|---|
| AspNetUsers и стандартные AspNetUserClaims/Logins/Tokens/Roles/RoleClaims/UserRoles | IdentityUser<Guid>, password hash/stamp/lockout; email/phone nullable, не собирать | Unique NormalizedUserName, provider keys стандартной Identity schema; AspNet roles не выдают educational/admin scope |
| Actors | ActorId UUID PK; PrimaryEducationalRole nullable Student/Teacher | Не менять UUID/человека; исторические actors без credentials допустимы, не синтезировать accounts |
| ActorIdentities | UserId PK/FK AspNetUsers; ActorId FK Actors UNIQUE; IsActive, ProvisioningOrgId FK Organization | 1:1 mapping; provenance выдачи не даёт владельцу этой org глобального security права |
| Organizations | OrganizationId UUID PK; State | Enabled/Suspended; строковые display metadata только synthetic до field allowlist |
| OrganizationMemberships | PK(OrganizationId, ActorId), FK обоих roots; Generation, State, EducationalRole, PermissionFlags, Version | PK(org,actor) для grants; generation сравнивается при authorization (не FK на изменяемое поколение); CHECK role/permissions; индекс(actor,state,org); admin permissions отделены от education |
| CourseRegistry | CourseId string PK; OrganizationId nullable FK только для quarantine; MappingState | UNIQUE(CourseId,OrganizationId); CHECK verified→org NOT NULL, unknown→org NULL; новые active courses только Verified |
| Existing content/access/assignments | Добавить FK PublishedContentVersions.CourseId, AccessLessons.CourseId, ProblemAssignments.CourseId, AccessEnrollments.CourseId, AccessTeachers.CourseId → CourseRegistry | Backfill registry включает и Unknown roots; FK сохраняет history, resolver допускает только Verified; existing triggers неизменны |
| CourseGroups | PK(CourseId,GroupId); FK CourseRegistry | GroupId course-local; одинаковые group strings в A/B независимы |
| EnrollmentScopes (sidecar существующего CourseEnrollment/AccessEnrollments) | PK(StudentId,CourseId), FK AccessEnrollments; OrganizationId, MembershipGeneration, EnrollmentGeneration, State, Version | Composite FK(course,org)→registry и (org,student)→membership; MembershipGeneration сверяется при issue/refresh; token scope при admission; group из AccessEnrollments проверяется FK(course,group)→CourseGroups; индекс(org,course,state) |
| TeacherAssignmentScopes (sidecar AccessTeachers) | GrantId PK/FK AccessTeachers; TeacherId, CourseId, GroupId nullable, OrganizationId, MembershipGeneration, State, Version | Composite FK к registry и (org,teacher)→membership; trigger сверяет GrantId→TeacherId/CourseId/GroupId в обе стороны, group FK; partial UNIQUE(teacher,course,group) WHERE active AND group NOT NULL и UNIQUE(teacher,course) WHERE active AND group NULL |
| AttachmentScopes | AttachmentId PK/FK StoredAttachments; OrganizationId, CourseId, UploaderActorId, ContextId/Version, Quarantined | Composite course/org и context refs; binding immutable; parent claim trigger проверяет тот же course/org, arrays/JSON refs, uploader по принятой file policy; index(course,org) |
| ProvisioningRequests | PK(org,ExternalRosterKey); UserId nullable до successful provision, PayloadDigest, State | Idempotency key; одинаковый payload→тот же результат без кода, другой→409; UserId не раскрывается другой org |
| AccessChallenges | ChallengeId UUID PK; UserId FK; Purpose, State, SecretMac, KeyVersion, ExpiresAt, FailedAttempts, TerminalAt, Version, RecoveryRequestId | Unique (user,purpose) WHERE State=Created; terminal states irreversible; expiry проверяет handler, не volatile index predicate; индекс user/expiry |
| IdentitySessions (E02 ACCEPTED) | sid PK; UserId FK; CreatedAt, LastSuccessfulRefreshAt, AbsoluteExpiresAt, RevokedAt, Version; refresh family/generation/verifiers | Не mandatory per-request session validation; refresh secrets не plaintext |
| AccountSecurityState | UserId PK/FK; CredentialEpoch, State | Row lock для reset/login/change-password/global revoke; mapping active проверяется при issue/refresh |
| SecurityAudit | AuditId UUID PK, org nullable для global security, actor/action/resource refs, result, versions, traceId, evidenceRef, UTC | Index(org,time), (subjectRef,time); без кодов, паролей, raw roster, body работ; retention пока не задан юридически |
| ProcessingContextVersions | PK(ContextId,Version); OrganizationId nullable для явно platform purpose; purpose/rule refs, approval state, EffectiveFrom | Immutable policy payload; no seeded LegalApproved value; purpose/LegalBasis/RetentionPolicy — nullable reviewed refs/VO, **не три дополнительные обязательные таблицы** |
| CourseContextBindings | PK(CourseId,EffectiveFrom); ContextId/Version FK; EffectiveTo nullable | Non-overlap intervals per course (PostgreSQL exclusion constraint или проверенный сериализованный trigger); same org constraint trigger; исторические периоды сохраняются |
| ProcessingRestrictions | RestrictionId PK; ContextId, OriginVersion (composite FK → ProcessingContextVersions), SubjectActorId nullable FK Actors, ResourceRef nullable, Operations, State, Version | OriginVersion фиксирует provenance, restriction применяется ко всем версиям ContextId; Context/subject indexes; current active restrictions intersect историческую policy, ambiguity→deny; subject scope подтверждается resolver |


Для отзыва нужны scoped RevocationRequestedAt/AccessValidUntil и generation на соответствующих полномочиях/зависимостях, с атомарным audit и optimistic concurrency. Включение этих полей в конкретные records — инженерная детализация T04. Composite course/org, membership и group constraints защищают межорганизационные связи; arrays/JSON attachments требуют guard/trigger review, не только обычных FK. Nullable quarantine никогда не разрешает runtime access.

ContextVersions immutable; CourseContextBindings имеют непересекающиеся периоды и ту же organization; историческая операция сохраняет действовавшую version через immutable sidecar/audit, без изменения старых events. Current ProcessingRestrictions сильнее исторического allow. SyntheticOnly marker не legal approval; реальные ПД запрещены независимо от наличия строки. ProcessingPurpose/LegalBasis/RetentionPolicy пока references, не обязательные отдельные таблицы; subject consent/representative/case records вне L1. E06 ACCEPTED: current restrictions отдельно от JWT по ADR-L18, SyntheticOnly не legal basis; real-data freshness отдельный pre-pilot gate.

## 3. T01/T02 — credentials и provisioning

Минимальный UI администратора своей организации создаёт account по synthetic roster, назначает основную Student/Teacher role, выдаёт activation/recovery codes и отдельно управляет membership/enrollment/teacher assignments/блокировкой/отзывом. Provision account не создаёт grants. No public registration, email/phone необязательны. Первый org admin создаётся platform operator по контролируемой процедуре, no self-escalation; platform role не даёт work access. Shared-account recovery только через независимого platform security operator E04; минимальный operator UI входит в S6, без учебного доступа. Реальная процедура представителей несовершеннолетних OPEN PD-G03.

| Контроль T01 ACCEPTED | Требование |
|---|---|
| Код | 16 символов Crockford Base32 = 80 бит CSPRNG, группы по четыре |
| TTL | Activation 24 часа; recovery 15 минут |
| Проверки | Максимум 5 неудач на код; 10/мин/IP и 5/15 мин/account |
| Выдача | 3/час/получатель; 20/час/инициатор; cooldown 60 секунд |
| Хранение | Только HMAC-SHA256 с отдельным секретным ключом; key version и документированная ротация |
| Пароль | 12–128 символов, ASP.NET Core Identity credential storage |
| Канал | Личная проверка либо заранее подтверждённый индивидуальный канал; код только в момент выдачи |
| Browser/audit | Без секретов в logs/URL; защищённое хранение и CSRF при cookies; тест общего школьного NAT |

E05 требует atomic consume+target, irreversible terminal states и idempotency без secret redisplay; E04 independent platform security operator approval и E03 SecurityBlocked по ADR-L17. Добавить RecoveryRequests с initiator/approver/state/evidence refs, scoped idempotency records и refresh-generation verifier history. T01 key rotation runbook: versioned current issuance key, bounded validation предыдущими keys до expiry живых codes, compromised key→invalidate; key custody/normalization/limiter topology G03 engineering. E02 TTL приняты: idle60m/absolute12h, access memory/refresh HttpOnly Secure cookie.

## 4. T04 — JWT, scoped revoke и длительные операции

Подписанный JWT ≤15 минут, локальные signature/lifetime/issuer/audience/scope checks; актуальная DB authority при issue/refresh. Нет обязательного DB freshness lookup на каждом HTTP. Для конкретных полномочий атомарно:

`AccessValidUntil <= RevocationRequestedAt + 2 hours`

`TokenExpiresAt = min(Now + 15 minutes, AccessValidUntil - 10 minutes)`

Если exp ≤ Now, соответствующие права не выдаются. Refresh не продлевает revoked grants; повторный revoke не переносит deadline; старое Generation не оживает. JWT до принятия заявки живёт максимум 15 минут и с 8-минутной операцией укладывается в 2 часа. E01 ACCEPTED: один JWT одной org, user/actor/sid/org и concrete grants; exp earliest included grants, скоро истекающие можно исключить. A deadline не ограничивает выдачу B. Claims/resource matching по ADR-L16, refresh/switch читают DB.

Server timeouts: ordinary API 60 секунд, maximum HTTP 8 минут от admission, включая queue/body/DB/stream; reserve 10 минут. JWT expiry во время уже начатой операции не требует automatic interruption. Completion должен быть до AccessValidUntil; 2-минутный остаток относится к формальному clock uncertainty budget, не дополнительному праву; cancellation/commit/edge уже включены в максимум 8 минут. Тестировать slow clients, buffering, cancellation latency и DB commit boundary; полная процедура в ADR-L15. Disconnect/timeout → cancel I/O, rollback незавершённой transaction; committed/ambiguous outcome не обещать отменённым, frontend перечитывает state/version без command replay.

Каждая фотография отдельным запросом во временное хранилище, scope uploader+organization+course+context. Submit заново проверяет действующий JWT и атомарно связывает eligible files с work; foreign course даже в той же org deny. Temporary expiry/cleanup конечны; synthetic fixture TTL до решения C06/C08 не production retention. DB/file ambiguous commit quarantine до reconciliation. Attached download проверяет parent scope для всех, включая uploader; каждый GET/Range авторизуется, no-store, max transfer 8 минут, deadline закрывает stream без PG lock/connection на время передачи. Уже скачанные bytes не отзываются.

Jobs: перед irreversible step — свежая authority/restrictions и bounded deadline; перед выдачей результата — новая авторизация; no forever JWT/service bypass. Long jobs разбить на ограниченные steps и остановить при revoke/unknown authority. E03 SecurityBlocked запрещает login/issue/refresh и revokes refresh-sessions, existing JWT≤15m и in-flight≤8m; не immediate access denial. E06 synthetic current restriction checks отдельно от JWT; real freshness gate открыт. Ordinary revoke не требует PG shared/exclusive request locks, drain, Redis или immediate invalidation всех JWT. Transaction locks для code/claim/целостности и future backup/erasure maintenance остаются в своих областях.

## 5. API/UI engineering contracts

Existing learning routes/DTO/state semantics и `/api/health` сохраняются. Auth contract/claims по ADR-L16; refresh/switch common atomic rotation protocol после решения O01. Access JWT только memory, opaque refresh HttpOnly Secure cookie, rotation/idle60m/absolute12h, no localStorage. Logout/recovery/block revoke refresh sessions, existing access JWT до exp. На смене account/org abort запросов, clear drafts/Blob URLs/query state, cache keys account+org; no command replay. Cross-tab logout общий, active org каждой вкладки независима.

Org-scoped admin routes для accounts/codes/memberships/enrollments/teacher assignments/revoke требуют scoped permissions, не global role. PROPOSED expectedVersion и idempotency keys на creates; audit атомарен с изменением. Revoke response содержит scope, acceptedAt и deadline: не «все JWT немедленно отозваны». Никакой выдачи roster B администратору A. UI минимальный, шесть основных учебных экранов сохранены.

Upload contract proposal: validated courseId в multipart; отсутствие допустимо только при ровно одном eligible course, иначе ScopeRequired, не первый из списка. Exact DTO review до реализации. Error contract сохранить ProblemDetails/code/traceId: 401 auth, 403 scope без foreign metadata, existing attachment 404 policy, 409 concurrency/shared recovery approval, 400 validation, 429 Retry-After. DB unavailable при issue/refresh/job fresh check → fail closed; локальный JWT validator не должен требовать доступность DB сам по себе.

## 6. T06 — M1–M3 и recovery

**M1 Schema Expansion:** новые Identity/roots/sidecars/context/security structures без разрушения существующей модели и изменения применённых migrations. **M2 Inventory & Controlled Backfill:** empty DB без переноса; existing synthetic только explicit actor/course/org manifest, unknown logical quarantine, real/unknown автоматический перенос запрещён. **M3 Constraints & Cutover:** inventory violations, FK/unique/guards validation, organization isolation и history verification; controlled maintenance switch после полной auth readiness. S1 готовит M3, runtime cutover только S8 после S2–S7, старые writers не обходят новые scopes.

До synthetic upgrade: согласованный backup DB+originals+keys, manifest counts/IDs/hashes и verified restore в отдельной synthetic среде. Inventory включает content/access/assignments/history/files, unknown actors без credentials; аккаунт сам не создаёт enrollment. Повторяемые шаги повторять без дубликатов/grant escalation; conflicting mappings stop. Не назначать владельца неизвестным данным. Immutable history/content/annotations и original hashes сохраняются.

Cutover: остановить ingress/writers, проверить manifest/constraints/auth paths, включить новую модель и negative smoke до ingress; quarantine недоступен на всех read/file/count paths. Failure → maintenance и forward repair либо проверенный offline restore; destructive Down запрещён, старый binary допускается только при доказанной schema/auth совместимости. Restore не возрождает revoked grants/challenges/refresh; reconcile текущую security информацию до ingress. Числа RPO/RTO и реальный backup contract C04/C05 не выдумывать. T06 не разрешает migrations на реальной БД; L0.3 вообще не запускает migrations/backfill.

## 7. Implementation slices — будущие, NOT RUN

Восемь slices сохранены, ответственность уточнена под E решения: S2 internal identity/session primitives, S3 resource/scopes, S4 JWT/refresh/switch и baseline guards на всех путях; S7 hardening файлов/jobs/restrictions/таймаутов, не первое введение защиты. S1 — durable foundations и migration rehearsal; execution M3 cutover только S8. S1–S3 могут быть отдельно разрешены без O01, HTTP auth/provisioning ingress до готового boundary закрыт. Ни один slice сейчас не разрешён.

Названия endpoints ниже — engineering contract under `/api/v1`, не уже существующие routes. DDL/index/DTO decisions в пределах accepted invariants инженер фиксирует при реализации; новые policy tradeoffs — STOP для owner. Общий результат каждого slice — code+tests+evidence в будущем, не deploy. Общие stop: real/unknown data, чужие изменения, history loss, failing critical tests, выход за принятое architecture, отсутствие отдельного разрешения.

### S1 — Schema foundations

| Поле | Требование |
|---|---|
| Цель | Создать аддитивный фундамент T03/T05/T06/E01–E06 без runtime auth cutover. |
| Зависимости | Отдельное разрешение S1; подтверждённая empty/synthetic DB и read-only inventory; O01 не требуется. |
| Компоненты | Infrastructure DbContext/mappings/future additive migrations и infrastructure tests; Domain workflows не меняются. |
| Агрегаты/сущности | Actors/ActorIdentities, Organizations/Memberships, CourseRegistry/Groups, scope sidecars, ProcessingContextVersions/Bindings/Restrictions; schema shells IdentitySessions/refresh verifier history/AccountSecurityState/Challenges/RecoveryRequests/Audit. |
| Application use cases | Inventory/quarantine/mapping validation ports; не публичный provisioning. |
| API endpoints | Новых публичных endpoints нет; ingress закрыт. |
| Persistence | M1 новые таблицы/RESTRICT/composite keys/Generation; M2 explicit synthetic manifest; M3 constraints rehearsal, not live cutover. Session/code/approval columns по L16/L17, без выбора O01 response policy. |
| Authorization | Unknown registry/неполная scope graph deny; fixture/migration authority не учебный grant. |
| Domain invariants | IDs/content/history/annotations неизменны; no cascade/guessed owner; same-org bindings/non-overlap. |
| Тесты | M1→M2→M3 rehearsal empty+synthetic, unknown quarantine, direct cross-org insert, rerun/conflict, backup/restore/hashes. |
| Acceptance | Все FK/unique/guards и preservation assertions подтверждены на synthetic DB; no applied migration rewrite. |
| Stop conditions | Unknown/real target, ambiguous mapping, destructive Down/history UPDATE, restore rehearsal failure. |
| Результат | Reviewed additive schema и воспроизводимый synthetic migration packet. |
| Переход | S1 tests/evidence PASS и разрешение следующего slice; public cutover остаётся закрыт. |

### S2 — Identity mapping и session primitives

| Поле | Требование |
|---|---|
| Цель | Связать Identity credentials со stable Actor и реализовать безопасные внутренние session primitives. |
| Зависимости | S1 accepted; T01/E02/E03; O01 не нужен для internal session CAS без browser exposure. |
| Компоненты | Identity Infrastructure, Application session/credential ports, trusted Actor adapter, unit/PG tests. |
| Агрегаты/сущности | IdentityUser, ActorIdentities, IdentitySession, refresh token generations/verifiers, AccountSecurityState. |
| Application use cases | Authenticate credentials, create/read/revoke session, atomic rotate primitive, revoke-all; account Enabled guard. E03 operator workflow в S6. |
| API endpoints | Только internal contracts для `/auth/login`, `/auth/session`, `/auth/logout`, `/auth/refresh`; public handlers/браузерный flow принадлежат S4. |
| Persistence | Opaque refresh verifiers/consumed history, created/activity/absolute timestamps, version/unique successor; one user↔Actor, no plaintext. |
| Authorization | Identity authentication не выдаёт course grant; blocked/expired session denied; fixture identities не production login. |
| Domain invariants | actor_id отдельно от Identity sub; no work/enrollment from login; idle60m/absolute12h fixed. |
| Тесты | Malformed/duplicate mapping, password lengths, concurrent CAS one successor, expired idle/absolute, revoke/logout primitives, secret canaries. |
| Acceptance | Internal session lifecycle atomic, verifiers protected, no extra descendants/timeout resurrection; Claim mapper compatibility planned. |
| Stop conditions | Необходимость выбрать O01/browser fallback либо раскрыть public refresh → отложить до S4; inconsistent security transaction → STOP. |
| Результат | Tested internal identity/session services, no public session exposure. |
| Переход | S2 PASS и S3 authorization; завершение S2 не означает готовый browser auth. |

### S3 — Membership и resource scopes

| Поле | Требование |
|---|---|
| Цель | Обеспечить multi-org grants/generations и достоверную принадлежность ресурсов. |
| Зависимости | S1/S2 accepted, E01/T03; независимо от O01. |
| Компоненты | Application access use cases, Infrastructure registries/AccessFacts/query scope builder, DB guard tests. |
| Агрегаты/сущности | OrganizationMembership, EnrollmentScope, TeacherAssignmentScope, Course/Group registry, trusted resource scope VO. |
| Application use cases | Create/revoke membership, enroll/move/revoke enrollment, assign/revoke teacher, resolve current grants/resource root; aggregate changes не публиковать до S4/S5. |
| API endpoints | Contracts `/organizations/{orgId}/memberships`, `/courses/{courseId}/enrollments`, `/teacher-assignments`, scoped `/revoke`; public admin adapters S5. |
| Persistence | Generation/version/deadline/audit mutations атомарны, stable membership FK; old grants не переиспользуются. |
| Authorization | No global Teacher/Admin access; explicit org/course/group/action, same org owner, student ownership; actor A+B не общий transcript. |
| Domain invariants | Account≠membership≠enrollment; regrant новый Generation; historical AuthorRole/ActorId неизменны. |
| Тесты | A+B разные права, group names collisions, foreign course/work/file roots, revoked/regrant, null whole-course explicit, direct constraint bypass. |
| Acceptance | Current grant resolver и resource scope contracts proved, A revoke сохраняет B, no role-only fallback. |
| Stop conditions | Неоднозначная resource affiliation, guessed org, cross-org DB связь, grants revival. |
| Результат | Internal scoped authority модель для issuer/API; ingress ещё закрыт. |
| Переход | S3 evidence PASS; S4 только после решения O01 и отдельного разрешения. |

### S4 — JWT, organization switching и refresh

| Поле | Требование |
|---|---|
| Цель | Включить согласованный auth protocol и baseline authorization всех API paths. |
| Зависимости | S1–S3 PASS; O01 выбран владельцем/явно делегирован и protocol review завершён; G02 expiry/clock design validated. |
| Компоненты | API auth handlers/policies/ActorMapper, token issuer/session adapter, browser provider/coordinator, scoped query/write/file admission adapters. |
| Агрегаты/сущности | Org JWT grant snapshot, session generation, trusted resource/processing decision context. |
| Application use cases | Login/refresh/switch/logout/session, issue scoped token, request ordinary revoke с atomic D, resource admission/processing check. |
| API endpoints | `/auth/login`, `/auth/refresh`, `/auth/switch-organization`, `/auth/logout`, `/auth/session`, `/auth/csrf`; все existing learning/query/upload/download aliases проходят baseline boundary. |
| Persistence | Fresh authority on issue/refresh/switch, CAS refresh/audit, scoped deadline transaction; no per-request DB freshness ordinary grants/Redis. |
| Authorization | L16 claims one org, local validation + server resource matching + E06 processing gate. Scope не из UI; no authenticated paths temporarily exempt. |
| Domain invariants | No learning DTO/state changes; unauthorized requests не mutate; refresh не возвращает revoked rights. |
| Тесты | JWT signature/audience/issuer/15m/nbf, earliest exp/excluded grants, A→B denial/switch; two tabs different org, rotation concurrency/replay/reload/lost response, 60m/12h, CSRF/Origin; all route/count guards; revoke issuance races. |
| Acceptance | O01 documented and concurrency tests satisfy E02 without false normal logout; expiry conservative; all paths guarded, malformed scope fail closed. |
| Stop conditions | O01 unresolved, clock/expiry not bounded, unsupported coordinator without tested fallback, silent role/admin access. |
| Результат | Synthetic auth/scope API/browser with basic file/restriction/deadline gates; no general ingress cutover. |
| Переход | S4 critical matrix PASS, затем S5 authorization; hardening evidence S7 ещё обязателен. |

### S5 — Provisioning UI и admin operations

| Поле | Требование |
|---|---|
| Цель | Дать org admin минимальный UI T02 без shared recovery bypass. |
| Зависимости | S4 PASS; accepted T02/E04; synthetic participant mapping/verification fixtures. |
| Компоненты | Application provisioning/grant adapters, API scoped admin controllers, Web admin forms, audit. |
| Агрегаты/сущности | ProvisioningRequest, Identity/Actor, membership/enrollment/assignment, RecoveryRequest initiation. |
| Application use cases | Create account, assign primary role, separate grants/revoke, controlled first admin bootstrap, initiate recovery. Code issuance/approval executable only S6; до этого disabled/no secret path. |
| API endpoints | `/organizations/{orgId}/accounts`, memberships/enrollments/teacher-assignments/revoke; `/accounts/{accountRef}/recovery-requests`; bootstrap controlled operator procedure, no public self-grant. |
| Persistence | Roster-key/payload/idempotency and actor-link constraints; provisioning/audit atomic, unresolved duplicate review path. |
| Authorization | Org admin только своя org; platform bootstrap≠educational access; independent security approval нельзя заменить roster. |
| Domain invariants | Account не enrol; no duplicate Actor/history transfer для обхода recovery; admin не AuthorRole. |
| Тесты | OA/OB/P own/foreign scope, self-grant, duplicate/provision retry, atomic failure, roster-only reset forbidden, no foreign memberships disclosure. |
| Acceptance | Минимальный org UI и request initiation complete; security-sensitive unfinished S6 actions недоступны; no partial credentials. |
| Stop conditions | Identity ambiguity interpreted as new account, leaked B metadata, bypass независимого approval, real roster. |
| Результат | Synthetic org-admin workflows и audited recovery requests. |
| Переход | S5 PASS и отдельное S6 разрешение; role/verification fixtures не legal evidence. |

### S6 — Activation, recovery и security blocking

| Поле | Требование |
|---|---|
| Цель | Реализовать E03–E05 lifecycle и minimal security operator UI. |
| Зависимости | S5 PASS, T01/E02–E05; G03 keys/rates/normalization и synthetic independent verifier готов. |
| Компоненты | Identity/Application security services, challenge/approval stores, API handlers, Web code forms и security-operator inbox/block controls. |
| Агрегаты/сущности | AccessChallenge states/purpose, RecoveryRequest approval, AccountSecurityState SecurityBlocked, IdentitySessions, SecurityAudit. |
| Application use cases | Issue/reissue/consume Activation/Recovery; independently approve/suspend/reject recovery; reset password+revoke-all; confirmed block/unblock. |
| API endpoints | `/auth/activate`, `/auth/reset-password`; scoped `/accounts/{id}/activation-codes`, `/recovery-codes`; `/security/recovery-requests/{id}/approve / suspend / reject`, `/security/accounts/{id}/block / unblock` engineering route names. |
| Persistence | One Created per user/purpose; terminal irreversible, code target/approval/password/session/audit transaction, idempotency receipt without secret, account block serialized with issuance. |
| Authorization | Security operator distinct capability/audience no education; initiator≠approver; shared membership rechecked at consume; SecurityBlocked no code bypass. |
| Domain invariants | 16/80 HMAC, accepted TTL/rates, exactly once, no secrets; unblock never restores old grants/refresh; existing access JWT expire naturally. |
| Тесты | Activation/simple/shared recovery, missing approval/self-approval/duplicate, member added during recovery, blocked/unblock races; expiry/reissue/five failures/concurrent consume/idempotency/purpose split/no secret/NAT/key rotation; reset all refresh sessions. |
| Acceptance | Full finite-state/race matrix PASS, independent security UI minimal, blocked issuance denied, fresh approval required where necessary, logs clean. |
| Stop conditions | Real representative/doc evidence requested, absent independent synthetic verifier, secrets redisplayed, partial password/code transaction, immediate JWT block promise. |
| Результат | Closed synthetic account lifecycle with verified temporal limitations. |
| Переход | S6 PASS → S7; juridical real-person procedure остаётся pre-pilot gate. |

### S7 — Files, long operations, jobs, restrictions hardening

| Поле | Требование |
|---|---|
| Цель | Доказать bounded operation/processing guarantees поверх baseline guards S4. |
| Зависимости | S4/S6 PASS, ADR-L15 G02 design, E06 synthetic contract; без новых legal UI. |
| Компоненты | AttachmentService/storage/API timeouts/edge contract, Application jobs/processing ports, query restrictions, browser abort/expiry UX. |
| Агрегаты/сущности | AttachmentScope/pending lifecycle, immutable work/submission linkage, processing decision refs/current restrictions, scoped job metadata. |
| Application use cases | Separate photo upload, atomic Submit claim, timed download/Range, cancel/reconcile uncertain commit, finite temp cleanup contract, authorize job step/result, apply synthetic restrictions. |
| API endpoints | Existing upload/Submit/attachment GET/DELETE; job result endpoint только для реально требуемого L1 job adapter, не новая general export/jobs platform. |
| Persistence | Targeted claim/cleanup/restriction transaction guards, history version refs; no DB connection held for stream, no unbounded job grants. |
| Authorization | Fresh JWT admission/parent scopes; processing current-read and transactional write guard; jobs reauthorize before irreversible/delivery. Grant freshness DB на каждый HTTP не добавлять. |
| Domain invariants | Original bytes/immutable events intact; current deny > historic allow; SyntheticOnly environment, missing context deny; no elapsed limit reset by slow client. |
| Тесты | 60s/8m/10m/2h, before exp/before D, clock offsets/commit/cancel/edge, multi-photo/cancel/retry/lost connection, foreign file, job after revoke/result auth, old context/new block, DB unavailable, spoof real mode, raw SQL restriction counts. |
| Acceptance | Full L bound includes commit/cancel/edge ≤8m; conservative JWT expiry; no 2h privacy claim; temp cleanup no linked-file destruction; synthetic fresh restrictions evidenced. |
| Stop conditions | Unbounded storage/commit/stream, need weaken accepted bound, real-data freshness request, privacy bypass or job perpetual authority. |
| Результат | Measured security/operation packet и documented remaining real-pilot gates. |
| Переход | S7 PASS, no critical gaps → S8 authorization; иные jobs/features не добавляются молча. |

### S8 — Integration, migrations и acceptance

| Поле | Требование |
|---|---|
| Цель | Проверить совместную систему и выполнить controlled synthetic M3 cutover. |
| Зависимости | S1–S7 PASS, O01 resolved, G02/G03/G04 evidence, отдельное разрешение targets/cutover. |
| Компоненты | Все test projects/API/browser, migration rehearsal/cutover harness, checkpoint/evidence docs. |
| Агрегаты/сущности | Полная accepted модель, immutable baseline sentinels; никаких новых продуктовых сущностей. |
| Application use cases | End-to-end learning/account/recovery/revoke/restriction и controlled migration/recovery verification. |
| API endpoints | Все новые security/admin/auth и existing learning routes, health; no new product API. |
| Persistence | M3 validate constraints, offline backup/restore/security reconciliation, no old writers/ingress before smoke; preserved IDs/hashes. |
| Authorization | A/B/S_AB/OA/OB/P/operator matrix, malformed/stale/forged tokens, no public/dev fallback; no cross-org data/counts/files. |
| Domain invariants | Submit→Review→Revise→Accept and six screens unchanged; no cascade/history overwrite, no fake legal basis. |
| Тесты | Full build/unit/integration/real PG/API/browser; все группы §8, migration rerun/unknown/quarantine/restore, secret canaries и measured deadlines. |
| Acceptance | Evidence per scenario/commit/environment, no skipped critical checks; all scope tests PASS, one controlled synthetic cutover packet, final report. |
| Stop conditions | Missing predecessor/evidence, legal/real data, critical failure, old auth bypass, any production deployment request without authorization. |
| Результат | L1 technical acceptance report, remaining PD/ORG/C gates; НЕ готовый real pilot. |
| Переход | STOP после L1 report; L2–L4/push/merge/deploy требуют нового разрешения. |

## 8. Test coverage и порядок проверки

Все tests — synthetic. Gating ID набор связывает plan/execution/readiness; implementation results сейчас **NOT RUN**.

| Группа | Минимальные обязательные сценарии | Основной slice |
|---|---|---|
| I Identity/org | S_AB A+B разные scopes; switch; JWT A→B denial; regrant generation; чужие course/group/work/files, Teacher без grant | S3/S4 |
| J JWT/session | 15m expiry, earliest included grant/excluded near-exp grant, fresh refresh/no revoked restoration; parallel tabs org A/B; concurrent refresh/CAS/replay/lost response; idle60m/absolute12h; logout/recovery all sessions | S2 primitives, S4 public protocol, S6 reset |
| R Revoke/operations | ordinary ≤2h, reserve10m, ordinary60s/max8m; admission непосредственно перед exp и попытка перед D; clock skew/leeway/rounding, queued/body/commit/edge/cancel; multi-photo upload/cancel/retry, revoked job/result fresh auth | S4 baseline, S7 full proof |
| P Provision/recovery | activation/simple/shared reset; no independent proof/self-approval/roster-only/duplicate; membership-added race; block/unblock no old grants; security operator no education | S5/S6 |
| C Codes | expired/reissued/five failures/reused/concurrent purpose-mismatch; idempotency duplicate/payload conflict/no secret redisplay, audit canaries, HMAC rotation, NAT limits | S6 |
| X Processing | immutable versions, same org/course interval, historical reproducibility, current restrictions priority/concurrent writes, missing context, DB failure, forbidden real mode/data and JWT SyntheticOnly bypass | S4 gate, S7 hardening |
| M Migration | M1→M2→M3, empty/synthetic, unknown legacy quarantine, immutable history/IDs/hashes, repeatable allowed steps, backup/restore/no cross-org links, security state not resurrected | S1 rehearsal, S8 cutover |
| D Domain/browser | Six-screen Submit→Review→Revise→Accept/history/annotations; account/org switch clears drafts/Blob/cache without command replay; csrf/origin/xss regression | S4/S6/S8 |

Сначала targeted checks slice, затем S8 full regression/build. Реальный PostgreSQL для constraints/transactions, настоящий HTTP для auth/timeout и browser для cookie/coordinator; mocks не доказательство races/transport. Подобрать technical timeout configuration в рамках G02, не выдумывать безопасность по одному CancellationToken. Historical K tests не новый PASS.

## 9. Readiness и ограничения

[Отдельная оценка](L03-L1-READINESS.md) различает design readiness и runtime dependencies. S1 может быть разрешён сейчас отдельно; S2 после S1, S3 после S2, без O01. S4 blocked O01, S5–S8 зависят от него через S4. Юридические/операционные gates блокируют реальные данные/пилот, synthetic fixtures не требуют настоящих representatives/consents. PD-G01–14, ORG-G05/08, C03–C09 остаются открытыми; real ProcessingRestrictions freshness отдельное решение. Compliance status: NOT VERIFIED; Pilot readiness: NOT_READY; LEGAL SOURCE NOT VERIFIED. L1 не разрешён этим планом.
