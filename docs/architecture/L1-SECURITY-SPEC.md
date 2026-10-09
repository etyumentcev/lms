# L1 — Security and trust boundaries

2026-10-09. T01–T06/E01–E06 ACCEPTED, implementation NOT VERIFIED. SYNTHETIC DATA ONLY. Эта specification объединяет enforcement map; механизмы нормативно описаны в ADR-L15–L18 и не дублируются здесь полностью. O01 ACCEPTED; S1 ACCEPTED; only S2 AUTHORIZED by the 2026-10-09 owner task.

| Boundary | Недоверенный input / доверенный источник | Обязательный enforcement | Evidence slice |
|---|---|---|---|
| Browser → API | org/course/group/actor IDs, upload, токены и UI permissions не authority | Signature/issuer/audience/time/scope, server resource root, CSRF+Origin для cookies; никаких secrets persistent JS storage | S4/S7 |
| Identity/session → token issuer | Password/refresh secret, selected org | Identity credentials, SecurityBlocked, idle60m/absolute12h, verifier+rotation CAS, current DB grants; no stale grant refresh | S2 foundation / S4 protocol |
| Organization authorization → resource | Role/claims не устанавливают ownership | Resource→course→org; explicit action/group/course/owner, scopes до SQL counts; no admin OR bypass | S3/S4 |
| API → PostgreSQL | Даже доверенный app может ошибиться с foreign IDs | Unique/composite FK/guards, stable Actor/membership generation, atomic code/recovery/session/audit, least-privilege app без DDL/history bypass | S1/S6 |
| API → attachment storage | Private key/path не выдаёт права | Pending uploader+org+course/context, parent scope/current processing, atomic claim, no-store/bounded stream/no public static path | S4 basic / S7 hardening |
| Request → background job | Исходный JWT не perpetual permission | Persist scoped actor/generation/context refs, fresh check перед irreversible step/delivery, bounded operation, unknown authority deny | S7 |
| Organization admin → security operator | Roster/initiator не proof recipient | Independent approval, initiator≠approver, minimal refs/no docs/secrets, duplicate provision не recovery bypass; no educational access | S5/S6 |
| JWT → ProcessingRestrictions | Signed grants не legal basis | Current restrictions отдельно, immutable context versions/current binding same org, SyntheticOnly environment and fixtures; missing context/real mode deny | S4/S7 |

Основные contracts: [ADR-L15 time bounds](../adr/ADR-L15-jwt-delayed-revocation.md), [ADR-L16 claims/sessions](../adr/ADR-L16-organization-jwt-browser-session.md), [ADR-L17 codes/recovery/blocking](../adr/ADR-L17-account-security-recovery-challenges.md), [ADR-L18 restrictions](../adr/ADR-L18-synthetic-processing-restrictions.md).

## Отказ и изоляция

JWT A для B — deny до content; role Teacher без course grant — deny. Missing group не WholeCourse. Empty grants не global access. Client org switch только request кандидата, issuer перечитывает membership. Generation regrant не оживляет старые права. Issuance/revoke/block/consume races имеют transaction linearization; no best-effort audit after mutation.

Protected token/challenge verifiers отделены от audit; минимальные identifiers/time/result/trace, без code/password/refresh/JWT body/document copies. Raw secrets в crash logs/URLs/browser stores запрещены. Verification failure сообщения не раскрывают roster B. Security operator не impersonates learner и не получает works от своих administrative capabilities. Нет public register или universal OTP framework.

## Scalability и остаточные риски

Ordinary JWT grants проверяются локально; mandatory Redis и per-request DB freshness для них отсутствуют. Resource lookup, ProcessingRestrictions check, refresh и mutations могут читать PostgreSQL: «нет DB freshness для JWT» не равно «API вообще без БД». Synthetic restrictions intentionally current-read, что добавляет DB load; измерить S7/S8, не оптимизировать positive allow cache ценой E06 без нового решения. Real-data freshness/propagation/in-flight SLA остаётся отдельным pre-pilot gate.

O01: single browser-wide refresh coordinator serializes короткие issuance, не учебные запросы; нагрузка bounded retries, incompatible browser fallback требует evidence. Гонки, потерянный Set-Cookie и replay не различимы только по старому token; tradeoff принят O01: 30 секунд, 409 refresh_already_rotated без Set-Cookie; lost response не гарантирует восстановление. G02: сам CancellationToken не даёт hard completion guarantee при зависшем storage/commit; acceptance stop до доказуемого timeout/cancel budget. Уже скачанные bytes и уже committed actions не отзываются.

## L1 boundary

IdentityUser credentials → stable Actor; security/session records вне Domain. OrganizationMembership и scope sidecars не переписывают learning history. AccessChallenge и RecoveryRequest — security application lifecycle, не ProblemWork states. SyntheticOnly не legal approval/PII detector. Consent/contract/retention UI и реальные representatives/erasure вне L1. Чётко маркированные synthetic verifications позволяют S6 tests без юридического решения для реальных людей.

Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY. LEGAL SOURCE NOT VERIFIED. [Readiness](../../codex/L03-L1-READINESS.md).

## L1/S2 authorization checkpoint — 2026-10-09

The explicit owner task supersedes earlier authorization/O01 status statements: S1 ACCEPTED, O01 ACCEPTED (30-second policy in ADR-L16), S2 AUTHORIZED. Only internal identity/session foundations are authorized. S3–S8, public refresh/cookies/coordinator/JWT grants, M2/M3, push/merge/deploy remain excluded. See `codex/L1-S2-RESULTS.md` for current evidence.

## L1/S3 / B01 authorization checkpoint — 2026-10-09

The explicit owner continuation accepts **B01 — Generation-aware Enrollment Move**, accepts S1/S2/O01 and authorizes only S3. This supersedes earlier slice-authorization statements while retaining their historical evidence. A separate forward migration may replace enrollment-specific immutable GroupId guards with atomic same-course moves, immutable generation history, expected-version/membership checks, commit-time consistency and restricted-role enforcement. Stable student/course/org keys and historical learning affiliation must remain unchanged. Existing work must not follow a student's group move automatically. S4–S8, M2/M3, legacy backfill, production migrations, real personal data and push/merge/deploy remain forbidden. See [S3 authority design](L1-S3-SCOPED-AUTHORITY.md) and the S3 results checkpoint for implementation evidence.
