# ADR-L17 — SecurityBlocked, independent recovery и AccessChallenge

2026-10-09. **ACCEPTED E03/E04/E05**, T01/T02 неизменны. Owner evidence: L0.4 §4; [реестр](../../codex/L03-OWNER-DECISION-REGISTER.md). SYNTHETIC DATA ONLY; implementation NOT VERIFIED. Нормативный lifecycle здесь, ADR-L02 сохраняет provisioning boundary.

## SecurityBlocked E03

AccountSecurityState: Enabled/SecurityBlocked, version, changedAt, minimal reasonRef. Подтверждённое действие уполномоченного security operator одной transaction устанавливает block, revokes все refresh-sessions и audit; audit failure→rollback. Login/issue/refresh/Activation/Recovery consume сериализуются с security row/version и deny SecurityBlocked. Unblock — отдельное подтверждённое действие/audit, не code/login. Оно не оживляет revoked refresh-sessions/grants/terminal challenges, не уменьшает Generation.

JWT, выпущенный до block, остаётся до exp≤15m; операция перед exp может завершаться ещё ≤8m. При доказанных conservative clock/operation bounds ADR-L15 идеальный worst case до 23m после block, **не мгновенное прекращение и не двухчасовой ordinary revoke**. Без runtime evidence не заявлять безусловный wall-clock SLA. No mandatory denylist/Redis; block не отменяет committed learning history/скачанные bytes.

## Shared recovery E04

Shared account определяется server memberships. RecoveryRequest: requestId, targetUserId, initiatingOrg, initiatorActor, state/version, verificationRef, approverActor, approvedAt, challengeRef; references без копий документов. Requested→Approved/Rejected/Suspended; Approved→Fulfilled/Cancelled. Approval относится к конкретному target/recovery request, не вечный bypass. Reviewer — уполномоченный platform security operator, другой человек/Actor, чем initiator; два permission sets одного человека не независимость. Roster не proof. Если независимая проверка невозможна→Suspended/no code.

Org admin видит только свою заявку/допустимый status, не org B/memberships/работы. Minimal security operator UI: pending requests, minimal verified identity refs, approve/suspend/reject, block/unblock; no учебные works/grades/blanket roster. Operator — отдельная security capability/audience, не global Teacher/Admin OR branch. Identity/session authentication общего аккаунта не обходит separation of duties.

После проверки личности получателя или полномочий представителя approval связывается с Recovery issuance; сервер заново проверяет request target/purpose/authority и approver!=initiator. Verified recipient получает secret один раз. Consume атомарно меняет password, consumes challenge/approval, revokes все refresh-sessions и audit. Reissue того же approval request заменяет live challenge; изменение target/recipient требует новой проверки; terminal request не reusable. Если account стал multi-org между request/issue/consume, E04 approval требуется заново проверить, single-org shortcut не допускается.

Duplicate provisioning не recovery path: login/user↔Actor uniqueness, roster-key idempotency, verified existing-actor link; existing match/ambiguous identity→security review, не новый account с чужими grants/history. Без обязательного email/phone нельзя обещать автоматическое обнаружение любого реального дубля; L1 synthetic manifest задаёт stable participant reference и negative duplicate tests. Нельзя восстановить доступ созданием нового Actor/переносом history. Реальная проверка представителей несовершеннолетних PD-G03 OPEN; L1 использует только synthetic verification fixtures, не фиктивные legal approvals.

## AccessChallenge E05

Purpose только Activation/Recovery; невзаимозаменяемы. 16 Crockford Base32=80 CSPRNG bits, display XXXX-XXXX-XXXX-XXXX; HMAC-SHA256 отдельным key/version, verifier связывает challengeId/user/purpose. Activation24h/Recovery15m; пароли12–128 Identity. Normalization case/hyphen/aliases — documented engineering contract/tests, не сокращать entropy генератора.

| Из | Guard/event | В | Атомарное действие |
|---|---|---|---|
| Нет | Authorized issue/rates/cooldown/account/purpose/E04 approval | Created | Revoke previous live same user+purpose; insert verifier/receipt/audit |
| Created | Correct secret, now<expiry, failures<5, Enabled, correct purpose/approval | Consumed | Target activation/password reset + session lifecycle + audit, один winner |
| Created | Wrong secret, count после попытки<5 | Created | Atomic failure increment |
| Created | Пятая неверная попытка | Locked | No target mutation/further consume |
| Created | now≥expiry | Expired | No target, эффективное expiry даже до sweeper |
| Created | Reissue/authorized invalidation | Revoked | Старый код unusable, новый distinct challenge |
| Consumed/Expired/Revoked/Locked | Любое reuse | Без изменения | Generic denial, terminal необратим |

Schema: ChallengeId/UserId/Purpose/State/SecretMac/KeyVersion/CreatedAt/ExpiresAt/TerminalAt/FailedAttempts/Version/RecoveryRequestId. Partial unique `(UserId,Purpose) WHERE State='Created'`; перед reissue retire expired Created row в transaction. No volatile Now predicate в index. CHECK failures≤5, purpose/state allowed; effective expiry/approval/security проверяются повторно под transaction. Unique index + lock/CAS user/purpose, account security guard: concurrent consume/reissue/block не создают two winners. Code+Identity target+refresh revoke+audit на одной connection/transaction, no independent UserManager commits. Rollback не оставляет consumed code с прежним password.

Idempotency scoped issuer/target/purpose/key с payload digest. Первый successful response показывает secret. Same key+same payload→receipt/status без secret и без нового code; different payload→409. Concurrent key→один insert. GET/refresh страницы не раскрывает code; lost secret/response→explicit reissue новым key после verification, старый live revoked. Не сохранять plaintext ради retries. Terminal consume retry не выполняет target снова; generic invalid/used без enumeration.

T01 limits: verify10/min/IP и5/15min/account, issue3/hour/recipient и20/hour/initiator, cooldown60s, max5 failures/code. Purpose switch/reissue/replicas не сбрасывают recipient/account/initiator budgets; trusted proxy IP, bounded partitions, atomic limiter and school NAT tests. SecurityBlocked запрещает issue/consume, Created code не bypass; unblock не resurrect terminal code.

Key rotation engineering runbook: current issuance key/version, retain previous validation keys лишь пока есть live challenges до expiry; retire controlled, compromised key→invalidate affected live challenges/independent verified reissue. Real key custodian/retention C05/PD-G07. Audit только action/actor/target refs/purpose/state/result/time/trace, без codes/passwords/refresh/JWT bodies/raw roster/document copies.

## Verification — NOT RUN

Expiry/reissue/five failures/wrong+correct concurrency/reuse/purpose mismatch, idempotency/payload conflict/lost response, key rotation/no secrets/NAT; shared approval self-approval/roster-only/duplicate/membership-added race; blocked login/refresh/consume/unblock no revived grants; password reset revokes sessions, issued JWT сохраняет accepted bound.
