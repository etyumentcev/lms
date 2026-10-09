# L1 — Identity, JWT, Organization Scopes & Access Lifecycle

2026-10-09. T01–T06: **ACCEPTED** по [реестру владельца](L03-OWNER-DECISION-REGISTER.md). L1 **НЕ РАЗРЕШЁН / НЕ НАЧАТ**. Future scope: **SYNTHETIC DATA ONLY**. Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY. LEGAL SOURCE NOT VERIFIED.

Принятые решения задают требования; поля/indexes/ports/routes и порядок slices ниже — инженерный план, а не дополнительные принятые владельцем решения. OPEN E01–E06 блокируют соответствующие slices; нельзя молча реализовать PROPOSED вариант. [ADR-L15](../docs/adr/ADR-L15-jwt-delayed-revocation.md) заменяет старый ordinary revoke barrier и cookie-only transport. [Execution task](CODEX-TASK-L1-EXECUTION.md) подготовлен отдельно, не выполняется.

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
| AccessChallenges | ChallengeId UUID PK; UserId FK; Purpose, SecretMac, KeyVersion, ExpiresAt, FailedAttempts, ConsumedAt, InvalidatedAt | Unique active (user,purpose) через partial index consumed/invalidated NULL; expiry проверяет handler, не volatile index predicate; индекс user/expiry |
| IdentitySessions (точный refresh contract OPEN E02) | SessionIdHash PK; UserId FK; lifecycle/expiry/revocation metadata | Не mandatory per-request session validation; refresh secrets не plaintext |
| AccountSecurityState | UserId PK/FK; CredentialEpoch, State | Row lock для reset/login/change-password/global revoke; mapping active проверяется при issue/refresh |
| SecurityAudit | AuditId UUID PK, org nullable для global security, actor/action/resource refs, result, versions, traceId, evidenceRef, UTC | Index(org,time), (subjectRef,time); без кодов, паролей, raw roster, body работ; retention пока не задан юридически |
| ProcessingContextVersions | PK(ContextId,Version); OrganizationId nullable для явно platform purpose; purpose/rule refs, approval state, EffectiveFrom | Immutable policy payload; no seeded LegalApproved value; purpose/LegalBasis/RetentionPolicy — nullable reviewed refs/VO, **не три дополнительные обязательные таблицы** |
| CourseContextBindings | PK(CourseId,EffectiveFrom); ContextId/Version FK; EffectiveTo nullable | Non-overlap intervals per course (PostgreSQL exclusion constraint или проверенный сериализованный trigger); same org constraint trigger; исторические периоды сохраняются |
| ProcessingRestrictions | RestrictionId PK; ContextId, OriginVersion (composite FK → ProcessingContextVersions), SubjectActorId nullable FK Actors, ResourceRef nullable, Operations, State, Version | OriginVersion фиксирует provenance, restriction применяется ко всем версиям ContextId; Context/subject indexes; current active restrictions intersect историческую policy, ambiguity→deny; subject scope подтверждается resolver |


Для отзыва нужны scoped RevocationRequestedAt/AccessValidUntil и generation на соответствующих полномочиях/зависимостях, с атомарным audit и optimistic concurrency. Включение этих полей в конкретные records — инженерная детализация T04. Composite course/org, membership и group constraints защищают межорганизационные связи; arrays/JSON attachments требуют guard/trigger review, не только обычных FK. Nullable quarantine никогда не разрешает runtime access.

ContextVersions immutable; CourseContextBindings имеют непересекающиеся периоды и ту же organization; историческая операция сохраняет действовавшую version через immutable sidecar/audit, без изменения старых events. Current ProcessingRestrictions сильнее исторического allow. SyntheticOnly marker не legal approval; реальные ПД запрещены независимо от наличия строки. ProcessingPurpose/LegalBasis/RetentionPolicy пока references, не обязательные отдельные таблицы; subject consent/representative/case records вне L1. E06 должен определить current enforcement, обычный JWT не отменяет T05.

## 3. T01/T02 — credentials и provisioning

Минимальный UI администратора своей организации создаёт account по synthetic roster, назначает основную Student/Teacher role, выдаёт activation/recovery codes и отдельно управляет membership/enrollment/teacher assignments/блокировкой/отзывом. Provision account не создаёт grants. No public registration, email/phone необязательны. Первый org admin создаётся platform operator по контролируемой процедуре, no self-escalation; platform role не даёт work access. Shared-account recovery deny без независимого account-security подтверждения E04. Реальная процедура представителей несовершеннолетних OPEN PD-G03.

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

Engineering proposal: consume code + password change + security lifecycle + audit атомарны, parallel consume ровно один; lost issuance response не восстанавливает plaintext. E05 уточняет reissue/canonical input/cooldown partitions и rotation. Предложенная процедура ротации: отдельные key IDs в secret store, новая выдача текущим key, старые active challenges только по записанной версии до их expiry, затем вывод ключа после отсутствия live challenges; при компрометации invalidate affected challenges и независимая повторная проверка получателя. Это runbook proposal, custodian и compromise response должны быть согласованы; не менять accepted MAC/entropy. Login lockout/refresh TTL не выдаются за принятые числа T01.

## 4. T04 — JWT, scoped revoke и длительные операции

Подписанный JWT ≤15 минут, локальные signature/lifetime/issuer/audience/scope checks; актуальная DB authority при issue/refresh. Нет обязательного DB freshness lookup на каждом HTTP. Для конкретных полномочий атомарно:

`AccessValidUntil <= RevocationRequestedAt + 2 hours`

`TokenExpiresAt = min(Now + 15 minutes, AccessValidUntil - 10 minutes)`

Если exp ≤ Now, соответствующие права не выдаются. Refresh не продлевает revoked grants; повторный revoke не переносит deadline; старое Generation не оживает. JWT до принятия заявки живёт максимум 15 минут и с 8-минутной операцией укладывается в 2 часа. Несколько организаций: **PROPOSED E01** отдельные JWT по выбранному scope, minimum deadline только включённых grants/dependencies; A deadline не ограничивает независимую выдачу B. Один комбинированный JWT не объявлен accepted.

Server timeouts: ordinary API 60 секунд, maximum HTTP 8 минут от admission, включая queue/body/DB/stream; reserve 10 минут. JWT expiry во время уже начатой операции не требует automatic interruption. Completion должен быть до AccessValidUntil; 2-минутный остаток покрывает только доказанные clock/cancellation/edge затраты, не дополнительное право. Тестировать slow clients, buffering, cancellation latency и DB commit boundary; полная процедура в ADR-L15. Disconnect/timeout → cancel I/O, rollback незавершённой transaction; committed/ambiguous outcome не обещать отменённым, frontend перечитывает state/version без command replay.

Каждая фотография отдельным запросом во временное хранилище, scope uploader+organization+course+context. Submit заново проверяет действующий JWT и атомарно связывает eligible files с work; foreign course даже в той же org deny. Temporary expiry/cleanup конечны; synthetic fixture TTL до решения C06/C08 не production retention. DB/file ambiguous commit quarantine до reconciliation. Attached download проверяет parent scope для всех, включая uploader; каждый GET/Range авторизуется, no-store, max transfer 8 минут, deadline закрывает stream без PG lock/connection на время передачи. Уже скачанные bytes не отзываются.

Jobs: перед irreversible step — свежая authority/restrictions и bounded deadline; перед выдачей результата — новая авторизация; no forever JWT/service bypass. Long jobs разбить на ограниченные steps и остановить при revoke/unknown authority. Emergency E03 отдельно; privacy block E06 отдельно. Ordinary revoke не требует PG shared/exclusive request locks, drain, Redis или immediate invalidation всех JWT. Transaction locks для code/claim/целостности и future backup/erasure maintenance остаются в своих областях.

## 5. API/UI engineering contracts

Existing business routes/DTO/state semantics и `/api/health` сохраняются; новый trusted JWT содержит выбранный scope. PROPOSED auth endpoints `/api/v1/auth/login`, `/refresh`, `/logout`, `/session`, `/activate`, `/reset-password`, `/change-password`, `/csrf` (при cookie flow). Точные request/response/claim/refresh contracts после E01/E02. Нет `/register` и blanket MapIdentityApi. На logout/context loss очищать queries/drafts/Blob URLs, abort fetch/XHR, cross-tab signal без секретов; commands не replay. Access/refresh storage — E02, cookie-only transport не возвращать.

Org-scoped admin routes для accounts/codes/memberships/enrollments/teacher assignments/revoke требуют scoped permissions, не global role. PROPOSED expectedVersion и idempotency keys на creates; audit атомарен с изменением. Revoke response содержит scope, acceptedAt и deadline: не «все JWT немедленно отозваны». Никакой выдачи roster B администратору A. UI минимальный, шесть основных учебных экранов сохранены.

Upload contract proposal: validated courseId в multipart; отсутствие допустимо только при ровно одном eligible course, иначе ScopeRequired, не первый из списка. Exact DTO review до реализации. Error contract сохранить ProblemDetails/code/traceId: 401 auth, 403 scope без foreign metadata, existing attachment 404 policy, 409 concurrency/shared recovery approval, 400 validation, 429 Retry-After. DB unavailable при issue/refresh/job fresh check → fail closed; локальный JWT validator не должен требовать доступность DB сам по себе.

## 6. T06 — M1–M3 и recovery

**M1 Schema Expansion:** новые Identity/roots/sidecars/context/security structures без разрушения существующей модели и изменения применённых migrations. **M2 Inventory & Controlled Backfill:** empty DB без переноса; existing synthetic только explicit actor/course/org manifest, unknown logical quarantine, real/unknown автоматический перенос запрещён. **M3 Constraints & Cutover:** inventory violations, FK/unique/guards validation, organization isolation и history verification; controlled maintenance switch после полной auth readiness. S1 готовит M3, runtime cutover только S8 после S2–S7, старые writers не обходят новые scopes.

До synthetic upgrade: согласованный backup DB+originals+keys, manifest counts/IDs/hashes и verified restore в отдельной synthetic среде. Inventory включает content/access/assignments/history/files, unknown actors без credentials; аккаунт сам не создаёт enrollment. Повторяемые шаги повторять без дубликатов/grant escalation; conflicting mappings stop. Не назначать владельца неизвестным данным. Immutable history/content/annotations и original hashes сохраняются.

Cutover: остановить ingress/writers, проверить manifest/constraints/auth paths, включить новую модель и negative smoke до ingress; quarantine недоступен на всех read/file/count paths. Failure → maintenance и forward repair либо проверенный offline restore; destructive Down запрещён, старый binary допускается только при доказанной schema/auth совместимости. Restore не возрождает revoked grants/challenges/refresh; reconcile текущую security информацию до ingress. Числа RPO/RTO и реальный backup contract C04/C05 не выдумывать. T06 не разрешает migrations на реальной БД; L0.3 вообще не запускает migrations/backfill.

## 7. Implementation slices — будущие, NOT RUN

Для каждого slice: отдельное разрешение L1, SYNTHETIC DATA ONLY; unresolved decision в зависимости → STOP этого slice. Общие invariants: existing IDs/history preserved, admin не reader, no guessed owner, no code secrets, no legal claims. Rollback по §6, не обход авторизации.

| Slice / цель | Зависимости | Основные изменения | Инварианты | Проверки | Критерии приёмки |
|---|---|---|---|---|---|
| S1 Schema foundation | T03/T05/T06, L1 authorization | M1 roots/sidecars/context, M2 inventory/manifests, M3 constraints rehearsal | Stable Actor; one Course owner; generations/no cascade; immutable versions | Empty+synthetic upgrade, direct cross-org inserts, rerun/conflict/quarantine, backup/restore/hash | Valid constraints и preserved IDs/history; unknown deny; runtime cutover ещё закрыт |
| S2 Identity/JWT | S1, E01/E02 | Identity mapping, signed token issue/refresh/browser lifecycle | ≤15m; local validation; fresh authority on issue; no public signup | Bad signature/issuer/audience/claims, expiry/skew, DB issue failure, mapping race, CSRF | Trusted scope/actor; no dev fallback; contract documented |
| S3 Membership/course/group | S2, E01 | Separate memberships/enrollment/teacher grants, Generation/deadline/audit | Regrant не оживляет grants; A/B independent; no implicit enroll | S_AB, same group names, forged IDs, whole-course/group, concurrent mutations | DB/app isolation, no global admin bypass, scoped issuance |
| S4 Authorization/files/context | S3, E06 | All queries/raw SQL/commands/file claim, context version links/current restrictions, bounded revoke | Filters before counts; current restrictions > historical allow; deadline T04 | All API aliases, foreign files/annotations, revoke/refresh races, old URL, restriction change | Все пути защищены, T04 bounds доказаны; не отложено до S7 |
| S5 Provisioning UI | S4, E04 | Org-admin minimal UI, controlled first-admin bootstrap, separate account/grant actions | Only own org; one-time display; no self-grant/shared takeover | OA/OB/P negative tests, atomic/idempotent provision, account≠enrol | Минимальный accepted UI complete; foreign metadata отсутствуют |
| S6 Activation/recovery | S5, E02/E03/E04/E05 | T01 codes/rates/MAC/rotation/passwords, independent shared recovery | Exactly once; 16/80; no secrets; no restored grants | Expired/replayed/reissue/concurrent code, rotation/lost response, rate boundaries, shared NAT, password lengths | Accepted T01 numbers enforced; agreed security lifecycle evidenced |
| S7 Long operations/security | S4/S6, E02/E03/E06 | Timeouts/cancel/transaction bounds, upload/download/jobs, browser reset/hardening | 60s/8m/10m/2h; no forever job JWT; A revoke preserves B | Multi-instance, slow I/O/Range/edge buffer/disconnect/uncertain commit, job revoke, cross-tab | No completion beyond deadline; emergency/restriction decisions implemented as agreed |
| S8 Integration/cutover | S1–S7, all applicable E decisions | Controlled M3 synthetic cutover, full regression/security/browser evidence | No real data; same six-screen cycle/history; no partial auth rollout | Build/unit/integration/PostgreSQL/API/browser; restore; secret canaries; all negative tests | Complete evidence packet; L1 report, remaining gates explicit, STOP до нового этапа |

## 8. Acceptance matrix и отчётность

Обязательные сценарии: Submit→Review→Revise→Accept/history immutable; Student own/Teacher course/group/admin-only; shared S_AB и A revoke/B fresh issue; deadline до/после revoke/refresh с fake clock и реальными HTTP/PG transaction tests; 15m/2h/10m/60s/8m границы; malformed token/expiry/CSRF; parallel consume/rates/NAT; direct DB cross-org guards и SQL totals; temporary claim/cleanup/uncertain commit; streaming/cancel/jobs; historical context/current restriction; empty/synthetic upgrade/quarantine/rerun/restore; browser account switch/no replay/Blob cleanup.

Проверять каждый slice подходящими tests, затем полный build/regression в S8. Fake/mocks не заменяют реальный PostgreSQL для constraint/race и HTTP для transport/timeout. Evidence указывает commit/environment/scenario/result, NOT RUN не PASS. Исторические K 366/260 не доказательство L1. Document-only checkpoint не запускает приложение.

L1 exit требует всех критериев S1–S8, закрытых applicable OPEN DECISIONS и evidence. Compliance status остаётся NOT VERIFIED, Pilot readiness NOT_READY; legal/real-data release не разрешён техническими tests. L2–L4 требуют отдельных разрешений.
