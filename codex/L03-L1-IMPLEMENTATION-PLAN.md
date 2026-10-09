# L1 — Identity, Organization Scopes & Access Lifecycle

Дата: 2026-10-09. L0.3 documentation-only. L1 НЕ НАЧАТ. Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY.

Принятые владельцем инварианты: C01/C02 (business process), ORG-G01–04/06/07; [reconciliation](L03-DECISION-RECONCILIATION.md). **Все механизмы, численные параметры, имена новых типов/таблиц/API ниже — PROPOSED T01–T06**, а не дополнительные принятые решения. [Readiness](L03-L1-READINESS.md) требует их рассмотрения и отдельного разрешения synthetic-only L1. Изменение кода, API и migrations этим документом не выполняется.

## 1. Исполнительный контекст и предел

Работать последовательно в `/home/tea/github/lms`; без новых worktrees. Для будущего L1 проверить фактические HEAD/ветку/clean tree и новое разрешение; не использовать L0.3 base как автоматически разрешённую базу реализации. Следовать актуальному repository workflow (main, если владелец отдельно не указал ветку).

Использовать только явно синтетические A/B организации, общий account S_AB, отдельные Student S_A/S_B, Teacher T_A/T_B, admin-only OA/OB и platform-only P. Никаких реальных roster, email, телефонов, договоров, согласий или legal basis. L1 не создаёт consent/representative/DSAR/production erasure product. Нужны технические contracts для scoped lifecycle и тестовые current restrictions; они не доказывают правомерность обработки. Реальные accounts и pilot запуск заблокированы всеми PD/release gates.

Фактические точки изменений:

| Слой | Файл / будущая работа |
|---|---|
| Domain | [ProblemWork](../src/Lms.Domain/ProblemWork.cs), [ProblemAssignment](../src/Lms.Domain/ProblemAssignment.cs), [History](../src/Lms.Domain/History.cs): существующий workflow, immutable content refs/Accepted terminal сохранить; authorization не превращать в новый статус |
| Application | [Ports](../src/Lms.Application/Ports.cs), [ProblemWorkService](../src/Lms.Application/ProblemWorkService.cs): scope/session ports, отдельные access lifecycle use cases, commit guard |
| Infrastructure | [LmsDbContext](../src/Lms.Infrastructure/LmsDbContext.cs), [AccessFacts](../src/Lms.Infrastructure/AccessFacts.cs), [PostgresAccessPolicy](../src/Lms.Infrastructure/PostgresAccessPolicy.cs): Identity, registry, constraints, current permission lookup |
| Read paths | [PostgresWorkspaceQueries](../src/Lms.Infrastructure/PostgresWorkspaceQueries.cs), [PostgresReviewQueue](../src/Lms.Infrastructure/PostgresReviewQueue.cs): LINQ и raw SQL фильтровать до totals/paging; EF filter не заменяет SQL guard |
| Writes/files | [PostgresWorkStore](../src/Lms.Infrastructure/PostgresWorkStore.cs), [AttachmentService](../src/Lms.Infrastructure/AttachmentService.cs): единая transaction/barrier, pending scope/claim, stream lease |
| API | [Program](../src/Lms.Api/Program.cs), [ActorMapper](../src/Lms.Api/ActorMapper.cs), [ApiErrors](../src/Lms.Api/ApiErrors.cs), controllers: cookie/session/CSRF, scoped admin policy и ошибки |
| Web | [App](../src/Lms.Web/src/App.tsx), [browserClient](../src/Lms.Web/src/api/browserClient.ts), [client](../src/Lms.Web/src/api/client.ts): session bootstrap, fetch/XHR CSRF, state reset |
| Tests | Existing Domain/Application/Infrastructure/API test projects и Web tests; реальные PostgreSQL/HTTP/browser там, где fake не доказывает boundary |

База схемы: последняя migration `20261008154119_ReviewAnnotations`. CourseId/GroupId — строки; ActorId — Guid в существующих ссылках, таблицы Actors нет. Enrollment PK = StudentId+CourseId; TeacherAccess PK = Id, nullable GroupId означает whole-course. Immutable triggers защищают историю/content/attached metadata; новые sidecar records позволяют сохранить её без массового UPDATE.

## 2. Domain, aggregate boundaries и lifecycle (T03 PROPOSED)

Organization — root административного состояния; CourseRegistry — один owner для globally unique CourseId, связь с опубликованным контентом через существующий CourseId. Organization может представлять стороннего организатора или ООО; никаких юридических статусов из этой записи не выводится. В MVP ownership после подтверждения не изменяется даже для пустого курса; исправление неверного legacy mapping — maintenance case с доказательством, выключенным доступом и отдельным review. Public transfer endpoint отсутствует.

IdentityUser отвечает за credentials; Actor registry сохраняет неприсваиваемый другому человеку UUID. OrganizationMembership — отдельный root пары OrganizationId/ActorId, administrative permissions и educational role. PrimaryEducationalRole account ограничивает MVP Student либо Teacher; admin-only/platform-only account может иметь NULL educational role. Effective роль берётся из membership и сверяется с primary role, не из AspNetUserRoles. Будущая отмена ограничения одной роли меняет policy, а не Identity mapping/исторические AuthorRole.

Membership lifecycle: Active → Revoking → Revoked; возврат только явным CreateMembership нового поколения. Pending invitation/credentials не создают membership. Повторный membership той же пары сохраняет row/UUID, повышает Generation; старые grants привязаны к прежнему поколению и **не оживают**. CourseEnrollment: Active → Revoked; смена GroupId — явный move с revoke barrier, повторное зачисление новым Generation. TeacherAssignment: Active → Revoked; расширение group→course — отдельная выдача, не silent NULL update. Assignment ProblemAssignment Active/Withdrawn остаётся учебным объектом, отличным от TeacherAssignment.

Scope VO: OrganizationScope, CourseScope(CourseId, OrganizationId), GroupScope(CourseId, GroupId), PermissionSnapshot(session/account/membership/course epochs, operation, resource, context version). Эти значения выдаёт server resolver; client IDs — только input для сверки. LifecycleManifestRef содержит context/resource set/approval/evidence refs, но L1 не исполняет erasure. ProcessingContext находится вне учебных aggregates.

Новые учебные DomainEvents не нужны. Existing immutable events не переписываются. SecurityAuditEvent — отдельный durable audit, атомарен с permission mutation. Для межпроцессного revoke используются DB barriers, а не best-effort event bus; outbox понадобится только при появлении внешнего потребителя (вне L1).

## 3. Persistence proposal T03/T05

Все FK по умолчанию RESTRICT; никаких каскадов от Identity/Organization к учебным данным. Версионные counters bigint, UTC timestamps. UUID непустые; строки CourseId сохраняют текущий тип и точное сравнение. Таблица — техническое предложение, а не автоматически принятая Domain entity.

| Record/table | PK / FK / поля | Constraints и indexes |
|---|---|---|
| AspNetUsers и стандартные AspNetUserClaims/Logins/Tokens/Roles/RoleClaims/UserRoles | IdentityUser<Guid>, password hash/stamp/lockout; email/phone nullable, не собирать | Unique NormalizedUserName, provider keys стандартной Identity schema; AspNet roles не выдают educational/admin scope |
| Actors | ActorId UUID PK; PrimaryEducationalRole nullable Student/Teacher | Не менять UUID/человека; исторические actors без credentials допустимы, не синтезировать accounts |
| ActorIdentities | UserId PK/FK AspNetUsers; ActorId FK Actors UNIQUE; IsActive, ProvisioningOrgId FK Organization | 1:1 mapping; provenance выдачи не даёт владельцу этой org глобального security права |
| Organizations | OrganizationId UUID PK; State, AccessEpoch | Enabled/Suspended; строковые display metadata только synthetic до field allowlist |
| OrganizationMemberships | PK(OrganizationId, ActorId), FK обоих roots; Generation, State, EducationalRole, PermissionFlags, Version | PK(org,actor) для grants; generation сравнивается при authorization (не FK на изменяемое поколение); CHECK role/permissions; индекс(actor,state,org); admin permissions отделены от education |
| CourseRegistry | CourseId string PK; OrganizationId nullable FK только для quarantine; MappingState; AccessEpoch | UNIQUE(CourseId,OrganizationId); CHECK verified→org NOT NULL, unknown→org NULL; новые active courses только Verified |
| Existing content/access/assignments | Добавить FK PublishedContentVersions.CourseId, AccessLessons.CourseId, ProblemAssignments.CourseId, AccessEnrollments.CourseId, AccessTeachers.CourseId → CourseRegistry | Backfill registry включает и Unknown roots; FK сохраняет history, resolver допускает только Verified; existing triggers неизменны |
| CourseGroups | PK(CourseId,GroupId); FK CourseRegistry | GroupId course-local; одинаковые group strings в A/B независимы |
| EnrollmentScopes (sidecar существующего CourseEnrollment/AccessEnrollments) | PK(StudentId,CourseId), FK AccessEnrollments; OrganizationId, MembershipGeneration, EnrollmentGeneration, State, Version | Composite FK(course,org)→registry и (org,student)→membership; MembershipGeneration сверяется при каждом admission; group из AccessEnrollments проверяется FK(course,group)→CourseGroups; индекс(org,course,state) |
| TeacherAssignmentScopes (sidecar AccessTeachers) | GrantId PK/FK AccessTeachers; TeacherId, CourseId, GroupId nullable, OrganizationId, MembershipGeneration, State, Version | Composite FK к registry и (org,teacher)→membership; trigger сверяет GrantId→TeacherId/CourseId/GroupId в обе стороны, group FK; partial UNIQUE(teacher,course,group) WHERE active AND group NOT NULL и UNIQUE(teacher,course) WHERE active AND group NULL |
| AttachmentScopes | AttachmentId PK/FK StoredAttachments; OrganizationId, CourseId, UploaderActorId, ContextId/Version, Quarantined | Composite course/org и context refs; binding immutable; parent claim trigger проверяет тот же course/org, arrays/JSON refs, uploader по принятой file policy; index(course,org) |
| ProvisioningRequests | PK(org,ExternalRosterKey); UserId nullable до successful provision, PayloadDigest, State | Idempotency key; одинаковый payload→тот же результат без кода, другой→409; UserId не раскрывается другой org |
| AccessChallenges | ChallengeId UUID PK; UserId FK; Purpose, SecretMac, KeyVersion, ExpiresAt, FailedAttempts, ConsumedAt, InvalidatedAt | Unique active (user,purpose) через partial index consumed/invalidated NULL; expiry проверяет handler, не volatile index predicate; индекс user/expiry |
| IdentitySessions | SessionIdHash PK; UserId FK; CredentialEpoch, CreatedAt, LastSeenAt, AbsoluteExpiry, RevokedAt | Index(user,revokedAt), expiry; cookie secret не хранится plaintext |
| AccountSecurityState | UserId PK/FK; CredentialEpoch, State | Row lock для reset/login/change-password/global revoke; mapping active проверяется каждый request |
| SecurityAudit | AuditId UUID PK, org nullable для global security, actor/action/resource refs, result, versions, traceId, evidenceRef, UTC | Index(org,time), (subjectRef,time); без кодов, паролей, raw roster, body работ; retention пока не задан юридически |
| ProcessingContextVersions | PK(ContextId,Version); OrganizationId nullable для явно platform purpose; purpose/rule refs, approval state, EffectiveFrom | Immutable policy payload; no seeded LegalApproved value; purpose/LegalBasis/RetentionPolicy — nullable reviewed refs/VO, **не три дополнительные обязательные таблицы** |
| CourseContextBindings | PK(CourseId,EffectiveFrom); ContextId/Version FK; EffectiveTo nullable | Non-overlap intervals per course (PostgreSQL exclusion constraint или проверенный сериализованный trigger); same org constraint trigger; исторические периоды сохраняются |
| ProcessingRestrictions | RestrictionId PK; ContextId, OriginVersion (composite FK → ProcessingContextVersions), SubjectActorId nullable FK Actors, ResourceRef nullable, Operations, State, Version | OriginVersion фиксирует provenance, restriction применяется ко всем версиям ContextId; Context/subject indexes; current active restrictions intersect историческую policy, ambiguity→deny; subject scope подтверждается resolver |

Minimal ProcessingContext boundary обоснован тремя durable records: неизменяемая версия политики, временная привязка курса и текущие ограничения, которые нельзя вписать в immutable версию. ProcessingPurpose/LegalBasis/RetentionPolicy пока value references в policy, не таблицы/автоматические юридические решения. Subject consent/basis/representative/case tables **не создаются в L1**. При будущей необходимости вводятся отдельно после legal review.

Synthetic policy имеет статус `SyntheticOnly` и test purpose marker, не «consent», «договор» или legal basis. Resolver разрешает её только при явно разрешённом synthetic environment; real mode при отсутствии reviewed policy deny. В L1 real-data mode запуском не разрешён. Binding history/audit объясняют прошлую версию, current restriction всегда выше прежнего Allow; expiry/недоступность registry → fail closed, а не уничтожение. Численные retention/hold rules не выдумывать.

FK не доказывает active membership и current generation; transaction checks обязательны. Прямой insert чужого org/course/grant/file должен отклоняться composite constraints/guard triggers. Quarantine строки могут не иметь sidecar: app допускает только complete Verified graph. Новые trusted provisioning paths требуют sidecars; direct DB writes ограничены ролями и triggers, app не владеет DDL.

Транзакции: UserManager и mapping/request/audit используют один context/connection/transaction; исключить внутренние независимые commits. Enrollment/grant mutation + sidecar + audit + epoch в одной транзакции. Code consume + password hash/stamp + session revoke + audit — одна транзакция. Если commit issuance успешен, но ответ с кодом потерян, plaintext не восстанавливается: отдельный reissue инвалидирует прежний code после проверки authority. File claim + work append + version + audit использует тот же connection, guard до фиксации. Файл хранится вне DB: failed commit оставляет только quarantined/pending original, cleanup L2; никогда не объявлять DB/files одной атомарной транзакцией.

## 4. Migration T06 PROPOSED

Миграции только в будущем L1. M1 добавляет registry/Identity/sidecars/policy/barrier records, не меняя bytes/UUID/immutable history. M2 делает контролируемую inventory/backfill операцию с manifest; M3 включает и валидирует FK/guards и resolver cutover. Не править уже применённые пять migrations. Nullable transition допускается только для явно quarantined ownership; unknown не становится фиктивной реальной Organization. Для runtime Verified Course принадлежность обязательна и единственна.

| Сценарий | Порядок и проверки |
|---|---|
| Empty DB | Применить существующую цепочку + additive migrations; seed только explicit synthetic A/B; создать registry раньше content/grants; ни demo passwords, ни legal basis автоматически |
| Existing synthetic DB | Backup DB/files + manifest hashes; inventory DISTINCT CourseId по content/assignments/access и actors по history/files; явный test manifest course→A/B; создать actor placeholders без users; sidecars только при подтверждённых связях |
| Unknown ownership | CourseRegistry Unknown/org=NULL; отсутствие валидной связи deny для courses/queue/counts/work/files; отдельный admin metadata inventory без контента; не default org. Однозначные parent refs linked files можно проверить, pending без course остаётся quarantined |
| Real legacy | Только утверждённый manifest CourseId→OrganizationId с provenance/reviewer/digest; вне synthetic L1; остановка при conflicting duplicate/missing courses, не auto-map |
| Constraints | Сначала inventory violations, затем NOT VALID FKs где допустимо, backfill проверенного subset, VALIDATE; unique/group constraints после duplicate report, не слияние разных grants по догадке |
| Cutover | Maintenance/no old writers; resolver + auth guard обязательны до ingress; app-role direct insert tests, все paths включены. Старый app не должен обходить quarantine |

Новые NOT NULL ставить там, где complete mapping доказан; unknown courses остаются в явной quarantine ветке CHECK до отдельного разрешения. Исторические immutable таблицы не переписываются ради sidecars. Ownership и context заполнение не выводятся из ActorId, login, имени группы или наличия одной организации в пилоте.

Migration verification: сравнить row counts, IDs, FK graph, hashes ContentJson/review annotations/files и snapshots work/history; проверить все старые immutable triggers; повторный backfill идемпотентен; missing sidecar/manifest fails closed. Ручная отмена constraint для «прохождения» запрещена.

Recovery: перед upgrade согласованный DB+files backup и копия current revocation state, проверить restore в isolated instance. В L1 synthetic drill достаточен для migration evidence, не production RPO/RTO. При сбое закрыть ingress, откатить незавершённую транзакцию, затем forward repair либо восстановить согласованный набор; re-invalidate sessions перед доступом. Не выполнять destructive Down. Возврат старого binary допускается только с доказанным сохранением новых scope/quarantine checks, иначе offline restore; stale snapshot не оживляет grants. Законный erasure ledger/production restore reconciliation требует отдельной реализации после legal review и до пилота.

## 5. Application use cases (PROPOSED)

Общие preconditions: authenticated active session для administrative/learning actions, Verified scope, current membership generation, technical processing permission, authority denied by default. Audit schema из §3; секреты не записываются. Ошибки согласуются с §7. Administrative actions не используют educational ActorMapper для admin-only principal: отдельный AdministrativePrincipal + scope policy.

| Use case / actor | Preconditions / authorization | State changes / concurrency | Errors | Audit evidence / acceptance |
|---|---|---|---|---|
| Provision account / org admin | ProvisionAccounts в org; approved synthetic roster reference; login/primary role allowlist; не self-role grant | User+Actor+mapping+idempotency/audit transaction; **без membership/enrollment**; unique login/request locks | Forbidden, ValidationFailed, ProvisioningConflict | issuer/org/roster ref/request digest/result; duplicate identical returns same result, payload mismatch 409, failure no partial user, foreign org denied |
| Issue activation code / org admin | Provisioning issuer scope; inactive unactivated account; recipient check ref; code выдавать только через approved individual channel | Lock account, invalidate прежний challenge, insert новый; plaintext один раз после commit | Forbidden, ChallengeIssueDenied, rate limit | verification method/ref/issuer/channel type/expiry, no code; reissue old code deny; unrelated org не видит account |
| Activate / code holder | anonymous + CSRF; valid unused activation challenge, inactive user; new password validators | Lock account/challenge, consume + password/stamp/audit atomic; аккаунт active, **без новых grants**, явный login | Generic InvalidChallenge, ValidationFailed, 429 | challenge opaque ref/result; 2 simultaneous submits→1 success; expiry/used invalid |
| Reset access / org admin initiates, verified holder consumes | Verified identity/authority; ResetAccess scope; shared-account safeguard ниже; no grant restoration | Issuance immediately global session/stamp revoke, prior reset invalidated; consume sets password atomically, session creation only later login | Generic InvalidChallenge/Forbidden; shared authorization pending→409 | request/verification/issuer/security approval/revoke epoch/result; старые sessions и codes deny, A/B memberships unchanged |
| Create membership / org admin | ManageMemberships в org, independently verified existing actor link или только что созданный account; запрет self-escalation | Lock actor role + org membership; role consistency, increase generation only explicit recreate; no course grants | Forbidden, RoleConflict, ConcurrencyConflict | org/actor/old-new generation/permissions/request; duplicate idempotent, чужая связь denied, old grants не оживают |
| Revoke membership / org admin | ManageMemberships в том же org; expected version | State Revoking, epoch; barrier→Revoked; затрагивает все его grants этой org, не B; §6 | Forbidden, ConcurrencyConflict; timeout→pending | scope/generation/barrier start/effective time; own A denied, B remains, old URL denied |
| Enroll student / org admin | ManageEnrollment, active Student membership, Verified course same org, valid group | EnrollmentAccess+scope generation atomically, epoch; ProblemAssignment только отдельным trusted learning provisioning | Forbidden, InvalidScope, ConcurrencyConflict | target course/group/actor/issuer; user creation/login не enrol, DB cross-org FK deny |
| Assign teacher / org admin | ManageTeachers, active Teacher membership, exact course/group; explicit whole-course grant | AccessTeachers+scope transaction + epoch; unique active grant, no inheritance from admin | Forbidden, InvalidScope, ConcurrencyConflict | course/group/full-course flag; group teacher не читает другую группу, admin не получает work |
| Revoke course/group assignment / org admin | ManageTeachers либо ManageEnrollment согласно target; current version | Grant/enrollment Revoked+epoch via barrier; rename group не расширяет scope; membership и другие courses остаются | Forbidden, ConcurrencyConflict | grant/ref/effective time; racing review/submit cannot commit after effective revoke |
| Resolve effective permissions / authenticated requester | Canonical resource root из DB, не из claims/client; current session/membership/assignment/context | Read current epochs и restrictions, no educational writes; bounded scoped query; DB failure deny | 401/403/404/503 | denial/action/scope/versions без content; raw SQL counts/paging excludes B, no implicit work creation |
| Authorize file access / authenticated requester | Pending: uploader+active matching course scope; attached: **parent work policy**, даже uploader; current restriction | Admit read lease до open; старый URL проходит снова; claim atomically same scope; release lease в finally | 401/403/404/503; claim conflict 409 | file opaque ref/action/scope/decision/lease; cross-org claim denied, stream revoke barrier, file bytes unchanged |

Shared account safeguard T02: org A нельзя превращать в recovery authority над доступом B. Для существующего login обычный provision отвечает общим ProvisioningConflict, не сообщает memberships. Linking требует подтверждения самого authenticated user и отдельного scoped invitation/ref, без глобального user search. Recovery для account с несколькими org требует account-security approval после независимой проверки получателя; org admin инициирует запрос и передаёт approved code, но не может сам выпустить его без этого approval. Account-security роль не даёт учебного чтения. Для synthetic acceptance все approvals — явно test records. Конкретный проверенный индивидуальный канал/полномочия представителя для real pilot требуют owner/legal review; это открытая часть T02, не новое принятое бизнес-решение.

Bootstrap T02: platform onboarding capability создаёт Organization и назначает первого org admin по approved manifest, с двухсторонней сверкой authority; не назначает себе учебные grants. В тестах bootstrap через isolated fixture; обычный org admin не повышает себе administrative permissions. Повышение другого admin требует отдельного grant-admin permission/approval; оно не следует из ProvisionAccounts.

## 6. Security T01/T04 PROPOSED

| Параметр | Предложение и основание для review |
|---|---|
| Code format/storage | 16 random bytes CSPRNG (128 bits), 26 Crockford base32 symbols; ChallengeId отдельный locator, code не включает login/ПД. HMAC-SHA256(secret, key) в DB с KeyVersion, constant-time compare, ключ вне DB/Git; потеря/rotation ключа инвалидирует pending codes |
| TTL | Activation 24 часа, recovery 15 минут; короткий recovery ограничивает окно takeover, activation оставляет время личной передачи. Это security proposal, **не legal retention period** |
| Attempts/rate | 5 failed attempts/challenge→invalidate; verification 10/min/IP + 5/15min/account; issue максимум 3/hour/target + 20/hour/issuer, cooldown 60s; bounded partitions, generic responses, 429/Retry-After; проверить школьный NAT |
| Reissue | Один active code на user/purpose; previous invalidated в той же transaction; activation и reset не взаимозаменяемы; idempotent network retry не возвращает plaintext старого code |
| Recipient evidence | Method=personal verification либо preverified individual channel, verifier ID/time/request/evidenceRef без документа/секретов; новые контактные данные при recovery не считаются подтверждением |
| Compromised channel | Stop issuance, invalidate pending codes/revoke affected sessions, security case; независимая повторная проверка новым approved каналом, затем issue; не отправлять новый секрет в скомпрометированный канал |
| Password/login | Minimum 12 characters, max 128; не обрезать, разрешать passphrase; блокировать известные слабые пароли локальным списком без внешней передачи; login 5 failures/15min временный lock, rate 10/min/IP; generic login error |
| Session | Same-origin HttpOnly Secure host-only cookie + CSRF, server session registry; idle 60min, absolute 12h, no remember-me на MVP; expiry не продлевается beyond absolute; per-request DB check без positive TTL cache |
| Revocation | Logout текущей session; reset/change password/global disable — все sessions/stamp; org revoke меняет только org rights, общий login/B сохраняются; credential reset не возвращает ранее revoked grants |
| Audit | Append-only restricted audit в transaction; denies без raw personal input; если audit mutation нельзя сохранить — mutation rollback; временно synthetic evidence, real retention PD-G07 |

Revocation protocol T04: DB authoritative, работает между несколькими API процессами. Lock order: account → organization → membership → course → work/challenge, в каждом уровне отсортированные IDs; read authorization получает shared transaction/advisory locks, revoke exclusive. Enrollment/grant изменения берут тот же course lock. Не достаточно сравнить Version вне transaction: revoke может пройти между check и commit.

Command: вычислить tentative trusted root, открыть transaction, взять locks, **заново** разрешить права/versions/current restriction, выполнить work compare-and-save и file claim под locks; удерживать до commit. Revoke: сначала короткая transaction ставит Revoking и увеличивает epoch (новые admission deny), затем exclusive barrier ждёт старых holders, фиксирует Revoked и effectiveAt. Если command взял lock до revoke, он либо завершится до effectiveAt, либо увидит Revoking и отклонится. После acknowledged effective revoke новой фиксации с прежним правом быть не может. Processing restriction mutation берёт exclusive organization barrier (для всех связанных courses), увеличивает organization epoch; platform-purpose restriction — account barrier. Security reset берёт account barrier; busy/timeout возвращает pending operation, не ложное «completed».

GET/stream: держать shared cross-process lease на время materialization/доставки stream; при revoke запрещать новые leases, дождаться завершения/cancel старых. Предложение: DB dedicated connection advisory lock до dispose, bounded concurrent streams и максимум 120s, cancellation закрывает response; потеря DB lease прерывает stream. Revoke может оставаться pending до drain; app crash закрывает connection. Не возвращать completed из timeout. DB pool cost и deadlock/lease leak тестировать. Bytes уже доставленные браузеру не отзываются. Нет долгоживущих публичных signed URLs; GET private file всегда серверный, Cache-Control no-store; CDN cache запрещён этим proposal.

Break-glass не implicit admin bypass: отдельная временная capability scoped на incident/purpose/org/resources/actions, approved ticket, TTL proposal 30min, expiry/revoke/audit и reviewer. В L1 только deny обычному admin и тестовый contract capability, **не открытый production endpoint/support workflow**. Production процедура зависит ORG-G05/PD и L3. DB root способен обойти app, поэтому отдельно ops permissions/credentials/audit, приложение не объявляется защитой от root.

## 7. API contracts proposal

Prefix `/api/v1`; JSON camelCase, UTC ISO timestamps, Idempotency-Key на administrative creates, expectedVersion на permission mutations. Session cookies только после login, не автоматически после activate/reset. ProblemDetails сохраняет type/title/status/instance и extensions.code/traceId; новые code mappings явно покрыть tests, иначе существующий default превратит их в 500.

| Method/path | Request → response | Policy |
|---|---|---|
| GET /auth/csrf | empty → {requestToken} | anonymous, no-store |
| POST /auth/login | {login,password} → 204 + cookie | CSRF/rate; generic 401; body не логировать |
| POST /auth/logout | empty → 204 | CSRF, revoke текущей session |
| GET /auth/session | → {actorId,primaryEducationalRole,expiresAt,securityVersion,availableScopes:[{organizationId,permissions}]} | только собственные scopes, no-store; другой org admin не получает этот endpoint за пользователя |
| POST /auth/activate | {challengeId,code,newPassword} → 204 | anonymous + CSRF/rate; generic InvalidChallenge |
| POST /auth/reset-password | {challengeId,code,newPassword} → 204 | только уже выданный recovery code; публичного request-by-email endpoint нет |
| POST /auth/change-password | {currentPassword,newPassword} → 204 | authenticated+CSRF; revoke all, явный relogin |
| POST /organizations/{orgId}/accounts | {externalRosterKey,login,primaryEducationalRole,rosterEvidenceRef} → 201 {accountRef,state,version} | ProvisionAccounts; без учебных grants |
| POST /organizations/{orgId}/accounts/{accountRef}/activation-codes | {verificationRef,channelType} → 201 {challengeId,code,expiresAt} | issuer и recipient checks, секрет один раз/no-store |
| POST /organizations/{orgId}/accounts/{accountRef}/recovery-codes | {verificationRef,securityApprovalRef?} → 201 challenge либо 409 approval required | shared-account safeguard; no grant changes |
| POST /organizations/{orgId}/memberships | {actorLinkRef,educationalRole,requestedPermissions,approvalRef} → 201 {membershipRef,generation,version} | ManageMemberships; actor link verified, no self-escalation |
| POST /organizations/{orgId}/memberships/{actorId}/revoke | {expectedVersion,reasonRef} → 202 {operationId,state} | scope admin; barrier completion via GET operation |
| POST /organizations/{orgId}/courses/{courseId}/enrollments | {studentActorId,groupId?,expectedVersion?} → 201 scoped enrollment | ManageEnrollment |
| POST /organizations/{orgId}/courses/{courseId}/teacher-assignments | {teacherActorId,groupId?,wholeCourse,approvalRef} → 201 {grantId,version} | ManageTeachers; null requires wholeCourse=true |
| POST /organizations/{orgId}/courses/{courseId}/enrollments/{studentId}/revoke | {expectedVersion,reasonRef} → 202 operation | ManageEnrollment |
| POST /organizations/{orgId}/courses/{courseId}/teacher-assignments/{grantId}/revoke | {expectedVersion,reasonRef} → 202 operation | ManageTeachers |
| GET /organizations/{orgId}/access-operations/{operationId} | → {state,effectiveAt?} | initiator/authorized admin этого org, no foreign metadata |

Нет `/register`, public signup или автоматического MapIdentityApi набора. В случае CLI те же application contracts, personal auth и audit; не DB superuser script. T02 выбирает интерфейс, не меняет issuer.

Compatibility: четыре существующих POST submission/message/review, шесть GET workspace/work/teacher alias/course/lesson/queue и GET/DELETE attachments сохраняют routes, DTO, content reference/version/status semantics. `/api/health` сохраняется. Authorization ужесточается намеренно; cookie+CSRF заменяет bearer transport в pilot, поэтому полной wire compatibility по auth не обещаем. Development JWT tooling — отдельный профиль без production fallback.

**POST /uploads** сохраняет route/file field и response DTO, но добавляет validated `courseId` multipart field для pending scope. При отсутствии поля разрешить только ровно один accessible course, иначе 400 ScopeRequired; нельзя выбрать первую org/course. Pending связывается с course, а не только org: A-file нельзя claim даже в другой course A без отдельного решения. Queue optional courseId остаётся фильтром внутри server allowed set, отсутствие фильтра не снимает isolation. Single-context frontend берёт courseId из текущего workspace; отдельный multi-org UI не требуется.

Errors: 401 Unauthenticated; 403 Forbidden без названия чужой org; 404 NotFound для отсутствующего ID (сохранить известные C4 mappings, не маскировать всё 404); 409 ConcurrencyConflict/ProvisioningConflict/RoleConflict/RecoveryApprovalRequired; 400 ValidationFailed/ScopeRequired/InvalidChallenge (generic для unknown/expired/replayed); 429 RateLimited с Retry-After; 503 AuthorizationUnavailable при недоступной authoritative DB. Foreign scope никогда не содержит resource metadata в error. Existing 409 WorkAlreadyAccepted/AttachmentAlreadyAttached и domain 422 остаются.

## 8. Frontend boundary

Шесть основных учебных экранов сохраняются. Login/activation/reset — минимальные вспомогательные формы из existing primitives; recovery показывает инструкцию обратиться к уполномоченному администратору, затем ввод кода/нового пароля. Не показывать других users, memberships либо «выберите Teacher». На 401 очистить session/drafts/Blob URLs, abort fetch/XHR и remount; на 403 убрать недоступные данные/controls и перечитать session/scope, не повторять command. Cross-tab signal без секретов; startup/focus authoritative reread. Return URL local allowlist. CSRF для JSON и multipart/DELETE, refresh token pair после login/logout; коды не URL/query/localStorage. Admin web-form либо CLI T02 отдельное решение, не полноценная панель управления организациями. Admin-only не попадает в Teacher screen.

## 9. Test matrix — будущие acceptance, NOT RUN

| ID / уровень | Конкретный synthetic сценарий | Ожидаемая проверка |
|---|---|---|
| U01 unit/domain | Submit→Review→Revise→Accept; repeat Accept/modify history | Existing invariants unchanged, no new privacy status |
| U02 unit/application | Student own work; Teacher group/course; admin-only/platform-only | Только назначенный scope; no implicit admin OR branch |
| U03 unit | Membership revoke/recreate, enrollment move, conflicting primary role | Generations не оживляют старые grants; RoleConflict |
| I01 integration | Provision user succeeds/fails midway/duplicate parallel request | Atomic user/mapping/audit, no enrollment, unique login/key |
| P01 PostgreSQL | Empty install + old synthetic DB upgrade | Triggers, ids/content/annotations/file hashes preserved; all FKs valid |
| P02 PostgreSQL | Course unknown, no sidecar, same group string A/B | Quarantine deny всех reads/files/counts, no guessed mapping |
| P03 PostgreSQL | Direct insert cross-org grant, foreign attachment array/annotation | Composite/trigger rejects; no partial mutation |
| A01 API contracts | Все 13 business routes, оба work aliases, upload course field | DTO unchanged кроме явного upload input; errors code/status/traceId, no public registration |
| A02 authentication | Cookie login/session/logout, expiry, restart, bad/mixed principal | Real handler, no demo fallback, old cookie replay denied |
| A03 activation/recovery | expired/wrong/used code, 2 concurrent consume, reissue/key rotation | Exactly once, no plaintext audit; old secrets deny; reset revokes all sessions/no grants |
| A04 security | CSRF missing/bad/cross-site на всех unsafe JSON/multipart routes | No mutation; generic errors/rate limits with bounded memory/NAT fixture |
| X01 isolation | S_AB имеет A+B; S_A/T_A/OA/P пробуют B IDs и OA читает A work | Только разрешённое, no metadata/count leaks; admin не reader |
| X02 SQL integration | review queue search/paging/totals, lesson/course progress | Фильтр до aggregation, не только mock policy; совпадающие group names не смешаны |
| X03 concurrency | Pause command после precheck, revoke, release в обоих порядках | Work commit до effectiveAt либо denied, never после acknowledged revoke |
| X04 concurrency | Multi-process/API instances, DB outage, stream открыт, revoke timeout/crash | New admissions deny; drain/pending honest; no leaked lock; DB fail closed |
| X05 attachments | Pending A claim B, same org other course, old preview URL, attached owner after revoke | Atomic refusal, existing bytes unchanged; stale link rechecks parent |
| X06 privacy contract | Old context version allows; current restriction blocks одного subject A | Work/file/queue/count deny A, B intact; SyntheticOnly не accepted в real mode |
| X07 admin/security | Self-grant, forged actor link, OA resets shared S_AB без approval | Denied; не раскрыт B roster; approved reset не меняет B enrollment |
| L01 legacy/recovery | Missing/conflicting manifest, rerun backfill, failed migration/recover | Quarantine; no silent assignment; restore offline, revoked sessions не оживают |
| W01 browser | Six-screen cycle with real Identity synthetic accounts; switch/reset/logout in 2 tabs | No stale draft/blob/command replay, CSRF fetch+XHR; login не создаёт work |
| S01 security acceptance | Full A/B fixture incl P/OA, code/audit secret canaries, all API paths | Evidence index per scenario/environment/commit, no skipped critical isolation test |

Run targeted tests slice-by-slice, then full regression/build at S8. PostgreSQL fixtures из tests/Lms.Infrastructure.Tests; HTTP через реальный Identity handler, browser через existing runner адаптированный к synthetic cookies. Не запускать destructive legacy runner на real data. Тесты приложения L0.3 не запускаются; K 366/260 — исторический результат, не новый PASS.

## 10. Vertical slices (все будущие, PROPOSED)

Общее условие начала: T01–T06 решены и L1 отдельно разрешён. Общие rollback: no destructive Down, maintenance/fail closed и §4 recovery; неизвестная авторизация блокирует ingress. Внутренний slice не означает готовый deployed релиз. До S8 только isolated synthetic environment.

| Slice / цель | Dependencies / слои | Schema | Tests / критерий готовности | Риски / rollback-recovery / переход |
|---|---|---|---|---|
| S1 Schema foundations | T03/T05/T06; Infrastructure | M1 roots, policy refs, barrier scaffolding; M2 explicit mapping; M3 constraints | P01–03/L01: empty+upgrade, quarantine, history hashes | Wrong backfill/locks; offline restore, без старого binary ingress; дальше только verified synthetic graph |
| S2 IdentityUser↔Actor | S1, B01/T01; Infra/API/Application | Identity mapping/security/session | A02/I01 mapping uniqueness, malformed principal, logout, CSRF базовый | Confusion Identity/Actor; disable auth ingress/restore; дальше trusted principal без demo fallback |
| S3 Membership/scopes | S2,T03; Application/Infra | Membership/enrollment/grant generations и epochs | U02/U03/X01/X07, no implicit enroll/self-escalation | Regrant revive; rollback transaction/deny; дальше resolver на двух org |
| S4 Authorization enforcement | S3,T04; Application/Infra/API | AttachmentScopes, restriction/barrier guards | X01–06/P03/A01 включая raw SQL/claim/commit revoke | Missing endpoint/race; ingress closed; **базовый revoke barrier уже здесь**, дальше ни одного незащищённого пути |
| S5 Closed provisioning | S4,T02; Application/API/минимальный admin adapter | ProvisioningRequests/audit; bootstrap fixture | I01/X07, separate membership/enrollment, scoped issuer | Account takeover/shared login; disable issuer capability, repair partial transaction; дальше negative admin suite green |
| S6 Activation/recovery | S5,T01/T02; API/Infra/Web | AccessChallenges, atomic consume/security epoch | A03/A04/W01 activation/reset, no grants, all sessions revoked | Brute force/channel compromise; invalidate challenges/sessions, не старый password hash; дальше replay/race evidence |
| S7 Session/revocation hardening | S4/S6,T04; все boundary слои/Web | Индексы/lease cleanup только по measurements | X03/X04/A02/W01 multi-process, restart, cross-tab, timeouts | Pool starvation/stale views; fail closed/revoke all; дальше bounded streams/no deadlocks |
| S8 Integration/security acceptance | S1–S7; test projects/Web/docs | Только выявленные scoped fixes, separate migration review | Все matrix IDs, full regressions/build, six-screen Identity E2E | Missing evidence→NOT COMPLETE; rollback конкретного defective slice по §4; финальный L1 report и STOP до нового разрешения |

S4 включает обязательную сериализацию отзыва до provisioning: перенос всей revocation correctness в S7 оставил бы временную дыру. S7 усиливает отказоустойчивость/нагрузку и UX, не вводит базовый security invariant задним числом.

L1 exit: synthetic Identity/org/course/group/file/session matrix passes с evidence; schema/DTO compatibility и legacy сохранность доказаны. Compliance status остаётся NOT VERIFIED; Pilot readiness NOT_READY. Consent, законное erasure, реальные представители/основания/retention и production break-glass не закрываются. Следующий L2 отдельно разрешается; правовые workflows выполняются лишь после своих решений, L3 deployment/restore и L4 release gates сохраняются.
