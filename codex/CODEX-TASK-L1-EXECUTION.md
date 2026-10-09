# CODEX TASK — L1 synthetic implementation (PREPARED, NOT AUTHORIZED)

Это самостоятельное задание для отдельного запуска после явного разрешения владельца. L0.4 не разрешает ни S1, ни migrations. Не выполнять этот документ в документационном checkpoint.

## Исходная архитектура и prerequisites

Sapientum LMS: .NET10/ASP.NET Core/EF Core10/PostgreSQL, React/TypeScript/Vite; Domain/Application/Infrastructure/API. Identity credentials отдельно от stable Actor; OrganizationMembership/course/group grants и ProcessingContext sidecars дополняют existing learning aggregates. Submit→Review→Revise→Accept/terminal Accepted, immutable history/content/annotations/original files/IDs и шесть учебных экранов сохраняются.

Canonical `/home/tea/github/lms`, sequentially, main по AGENTS.md если владелец отдельно не задал ветку; no new worktrees/parallel agents. Проверить фактические branch/HEAD/clean tree и наличие L0.4 documentation commit: подтверждённая база L0.3 `836aad59d01f1cd7c04e9e892ebd2d8621f7c59f`, L0.4 branch `codex/l04-extended-decisions-l1-readiness`; не считать автоматически merged в main. No automatic merge, не перезаписывать/коммитить чужие изменения.

До кода получить explicit authorization конкретных slices/targets. T01–T06/E01–E06 ACCEPTED по [реестру](L03-OWNER-DECISION-REGISTER.md), O01 concurrency/replay policy ACCEPTED по заданию L1/S2. S1–S3 можно отдельно разрешить без O01 с predecessor acceptance. Прочитать [план](L03-L1-IMPLEMENTATION-PLAN.md), [readiness](L03-L1-READINESS.md), ADR-L01/L02/L11–L18 и [security specification](../docs/architecture/L1-SECURITY-SPEC.md), domain/feature/UI contracts. Все численные параметры ниже обязательны, accepted O01 window 30 секунд не изменять.

## Scope и обязательные механизмы

- T01/E05: Activation24h/Recovery15m, 16 Crockford Base32/80 CSPRNG bits, display groups4, HMAC-SHA256 отдельным versioned key/rotation runbook; passwords12–128 Identity. Verify10/min/IP +5/15min/account, max5 failures/code; issue3/hour/recipient +20/hour/initiator, cooldown60s, school NAT tests. Created→Consumed/Expired/Revoked/Locked irreversible; reissue revoke previous same purpose; atomic target+consume, idempotency без secret redisplay, no purpose interchange/SecurityBlocked bypass.
- T02/E04: org-admin minimum UI, separate account/membership/enrollment/teacher assignment; controlled first-admin bootstrap. No public signup/required email/phone, one-time verified code delivery. Shared recovery initiated org admin, independently approved platform security operator, initiator≠approver, no roster-only proof/duplicate-account bypass, verification impossible→suspend. Minimal operator UI входит в S6, no blanket work access/audit document copies.
- T03/E01: IdentityUser↔Actor stable UUID unique, multi-org membership, one course owner/unknown quarantine, generations/no revived grants, DB cross-org constraints/no history cascade. Один educational JWT одной active org: user/actor/sid/org, explicit course/group/actions; server data defines resource ownership, not role/client claims. Switch/refresh reread PostgreSQL, A revoke preserves B.
- T04: access≤15m; scoped `AccessValidUntil <= RevocationRequestedAt + 2 hours`; `exp=min(Now+15m, earliest included AccessValidUntil−10m)`; revoked/insufficient grants не выдаются, near-exp grants можно исключить. Local signature/issuer/audience/time/scope checks; no mandatory Redis/per-request grant freshness. Ordinary API60s/maxHTTP8m, reserve10m, before-exp operation may finish after JWT expiry, but full completion/edge/commit/cancellation bounded. Формальное clock proof ADR-L15, no hidden default positive leeway/extra cancellation tail.
- E02: access только memory, opaque refresh HttpOnly Secure cookie, protected verifier, mandatory rotation, idle60m/absolute12h, no localStorage. CSRF/SameSite/Origin/XSS; reload refresh; per-tab org/common session; concurrency/replay policy только после O01. Logout revokes session; recovery все refresh sessions; issued JWT до exp.
- E03: SecurityBlocked atomically denies login/issue/refresh и revokes refresh sessions/audit; code не bypass; unblock confirmed action без old grant revival. Existing JWT≤15m и operation≤8m дают ideal completion≤23m после block при доказанных clock bounds, не instant revoke/не unconditional SLA.
- T05/E06: immutable ProcessingContextVersions, interval same-org CourseContextBindings, current ProcessingRestrictions отдельны от JWT; historical links/current deny priority; missing required context/basis deny. SyntheticOnly environment/fixtures; no JWT enables real data; no auto PII detection promise. Current-read/transactional restrictions per ADR-L18, real freshness отдельный pre-pilot gate.

## Последовательность и переходы

Каждый slice включает 14 полей contract/acceptance/stop из §7 плана; здесь execution order. Ответственность S2 internal primitives vs S4 public protocol, S4 baseline security vs S7 hardening не дублируется.

| Slice | Реализовать | Условие перехода |
|---|---|---|
| S1 | Additive roots/sidecars/security schema shells, M1/M2 synthetic mapping, constraints/backup-restore rehearsal | Preserved IDs/history/hashes, no cross-org FKs/unknown serving, PASS; public cutover закрыт |
| S2 | Identity→Actor и internal session create/revoke/rotate CAS, verifiers/timeouts | Mapping/atomicity/idle60m/absolute12h tests PASS; no public refresh/coordinator |
| S3 | Membership/enrollment/teacher grant generations/deadlines, trusted resource scope | A/B/regrant/no role-only bypass tests PASS; S4 требует отдельного разрешения и protocol review |
| S4 | JWT issuer/validation, refresh/switch/logout/browser coordination, baseline guards всех API/file/processing paths | O01 resolved, G02 expiry validated, real HTTP/browser races+scope/CSRF tests PASS |
| S5 | Minimal org-admin UI/accounts/separate grants/recovery-request initiation/duplicate guard | Own-org only/idempotency/no self-grant PASS; unfinished S6 code controls disabled |
| S6 | Activation/recovery states, independent approval/minimal security UI, block/unblock | Full code/approval/block/concurrency/NAT/no-secrets tests PASS |
| S7 | File claim/download/cancel/deadlines/jobs/current restrictions hardening | G02 bounded completion+E06 synthetic restrictions evidence, no unchecked stream/job |
| S8 | Full regression/security/browser, controlled synthetic M3 cutover/restore evidence | Все predecessors PASS, no critical skipped test, final report; STOP |

Engineering endpoint map: auth login/refresh/switch-organization/logout/session/csrf/activate/reset-password; org accounts/memberships/enrollments/teacher-assignments/codes/recovery-requests/revoke; security request approve/suspend/reject и accounts block/unblock. Exact routes/DTO в plan/ADR contract tests, не открыть generic MapIdentityApi/register. Existing learning API errors/routes/DTO сохраняются; upload courseId и новый actor claim explicit compatibility changes.

## Данные и M1–M3

Только isolated empty/synthetic DB/generated fixtures: A/B/S_AB/S_A/S_B/T_A/T_B/OA/OB/P и независимый security operator. Никаких реальных roster/email/телефонов/фото людей/паспортов/consents/договоров, включая tests. Synthetic verification refs явно тестовые. Unknown/real target/data→STOP без backfill.

M1 schema expansion без изменения applied migrations/immutable records. M2 inventory+explicit synthetic manifest, no guessed owner/no grants из account; unknown logical quarantine. M3 constraints validation и controlled cutover после S1–S7, не ранний ingress. До upgrade tested backup DB/files/keys и offline restore/hashes; no destructive Down. Repeatable steps idempotent, conflicting mapping stop. Maintenance/no old writers/security state reconciliation/negative smoke before ingress. DB/files не общая transaction; lost commit reconciliation не удаляет linked originals. Real migration/backfill/deploy не разрешены T06 или этим task.

## Testing и acceptance

Все группы §8 плана I/J/R/P/C/X/M/D обязательны: shared user A/B/switch/forged scopes/regrant; JWT expiry/earliest grants/fresh refresh/no resurrected rights; different-org tabs/concurrent refresh/replay/idle/absolute/logout/reset; ordinary2h/10m/60s/8m/admission before exp/before D/clock/commit/edge/cancel; multi-photo/cancel/retry/jobs/result auth; independent recovery/duplicate/self-approval/block/unblock; code expiry/reissue/five failures/concurrent use/idempotency/no secrets/purpose/NAT; context versions/same org/history/current restrictions/missing context/real-mode denial; empty/synthetic migration/unknown/quarantine/rerun/immutable history/backup-restore/no cross-org; six-screen learning regression.

Targeted tests каждого slice, full build/regression S8. Real PostgreSQL для constraints/races/restore, actual HTTP для auth/deadlines, browser для cookies/coordinator; fake clock дополняет, не заменяет environment proof. Evidence scenario/result/commit/environment, NOT RUN не PASS, старые K evidence не новый результат. Scope complete только если predecessor+own acceptance PASS, no silent missing critical tests.

## Stop conditions

Нет authorization; slice зависит от unresolved O01/failed predecessor; real/unknown data, missing independent synthetic verifier, identity/ownership ambiguity, history loss/cross-org access; нельзя доказать ≤15m/≤8m/current synthetic restriction semantics; public refresh без approved coordination; secrets in audit/storage; proposed policy выдана за owner accepted; необходимость нового data/legal/product scope. Записать blocker, не обходить checks и не создавать фиктивное evidence. Юридические workflows остаются outside L1, реальная представительская проверка требует legal review, а не dummy approval.

## Commit / отчётность

Reviewable commits по accepted slices согласно новому разрешению; только свои изменения, diff --check/appropriate tests до commit. Поддерживать checkpoint/evidence и ссылки. Не push/merge/deploy и не L2–L4 без отдельного разрешения.

Итоговый отчёт: A status/branch/commits/slices; B changes/components/contracts/migrations; C tests/evidence PASS/FAIL/NOT RUN; D invariants/security/time bounds; E targets/backup/restore/cutover; F open owner/engineering/legal/ops issues; G readiness/next authorized scope. Compliance status: NOT VERIFIED; Pilot readiness: NOT_READY; LEGAL SOURCE NOT VERIFIED. После L1 report STOP. Настоящий документ не исполнялся в L0.4.

## L1/S2 authorization checkpoint — 2026-10-09

The explicit owner task supersedes earlier authorization/O01 status statements: S1 ACCEPTED, O01 ACCEPTED (30-second policy in ADR-L16), S2 AUTHORIZED. Only internal identity/session foundations are authorized. S3–S8, public refresh/cookies/coordinator/JWT grants, M2/M3, push/merge/deploy remain excluded. See `codex/L1-S2-RESULTS.md` for current evidence.

## L1/S3 / B01 authorization checkpoint — 2026-10-09

The explicit owner continuation accepts **B01 — Generation-aware Enrollment Move**, accepts S1/S2/O01 and authorizes only S3. This supersedes earlier slice-authorization statements while retaining their historical evidence. A separate forward migration may replace enrollment-specific immutable GroupId guards with atomic same-course moves, immutable generation history, expected-version/membership checks, commit-time consistency and restricted-role enforcement. Stable student/course/org keys and historical learning affiliation must remain unchanged. Existing work must not follow a student's group move automatically. S4–S8, M2/M3, legacy backfill, production migrations, real personal data and push/merge/deploy remain forbidden. See [S3 authority design](../docs/architecture/L1-S3-SCOPED-AUTHORITY.md) and the S3 results checkpoint for implementation evidence.
