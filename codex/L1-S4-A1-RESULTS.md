# L1/S4-A1 — JWT and Authorization Components

2026-10-10. Owner authorization: **S4-A1 COMPONENT IMPLEMENTATION AUTHORIZED — SYNTHETIC ONLY** (current task). This authorizes components, not the A06 normative proposals or HTTP integration. T04/E03 are unchanged; G02 is open and S4 is not accepted.

## Baseline and preservation

Canonical directory `/home/tea/github/lms`, branch `codex/l1-s4-jwt-api-authorization`, initial HEAD `d8b1e8278df3316aa8b72f76afa0143f3ff8c780` (A04 preservation commit). All five A06 documents exist: FAILURE-MODEL, NORMATIVE-DIFF, OWNER-DECISION, PROOF-OBLIGATIONS, S4-SPLIT. Baseline matches the A06 report. No switch, merge, rebase, push or deploy.

Before editing, 352 tracked/untracked files, SHA-256 manifest, working/index patches and status were saved to `/tmp/lms-s4-a1-preservation`. Existing dirty `codex/L1-S4-RESULTS.md`, `AttachmentDeadlineBoundaryTests.cs`, A05/A06/G02 artifacts and checkpoint documentation were preserved and excluded from this change. Byte comparison found only the intended `PostgresIdentitySessions.cs` edit plus an external `.obsidian/workspace.json` change. This task never wrote or restored the Obsidian file; its initial bytes remain in the preservation directory. No claim of whole-tree immutability is made.

## Implemented components and changed files

| File | Component |
|---|---|
| `src/Lms.Application/ComponentAuthorization.cs` | Explicit bounded UTC clock contract, conservative expiry calculation, redacted bearer wrapper, token authority and internal scope/processing policy |
| `src/Lms.Infrastructure/ComponentJwt.cs` | Fixed RS256 issuer/validator profile, pinned RSA key ≥2048 bits, strict claims and scope validation |
| `src/Lms.Infrastructure/PostgresComponentTokenIssuer.cs` | Fresh session and S3 authority issuance under existing S2/S3 locks; organization switch is a fresh issue |
| `src/Lms.Infrastructure/PostgresIdentitySessions.cs` | Internal read-only issuance callback retaining existing account/session locks through signing |
| `src/Lms.Infrastructure/PostgresCurrentProcessingGate.cs` | Current PostgreSQL SyntheticOnly context/binding/restriction component check |
| `tests/Lms.Infrastructure.Tests/ComponentJwtTests.cs` | Synthetic JWT, clock and policy tests |
| `tests/Lms.Infrastructure.Tests/ComponentIssuanceTests.cs` | Synthetic PostgreSQL issuance, revoke/regrant/block, waiting-lock and restrictions tests |
| `codex/L1-S4-A1-RESULTS.md` | This report |

No dependencies, migrations, schema, HTTP registration, routes, browser code or normative documents changed.

### Issuance and clocks

Issuance uses S2's account security → Identity user → mapping → session locks, then the same organization row lock used by S3 authority administration. `PostgresScopedAuthorityReader.ResolveCurrent` executes in that transaction; signing happens before locks are released. No new grant store or detached authority source exists. Missing account, inactive mapping, SecurityBlocked, session revocation/expiry, unavailable/unknown clock, absent organization or empty educational grants cannot produce a token. Infrastructure errors propagate without an allow/token result.

Each token has one organization, separate `sub` (IdentityUserId), `actor`, `sid`, membership generation/version and concrete S3 grants. Issuance never copies scopes from an earlier organization's token. Ordinary validation/policy does not query current grants or sessions.

The injected clock must supply a qualified interval containing true UTC; no system-clock fallback or production clock qualification is provided. Expiry is rounded down to whole seconds and is at most `Earliest + 900s`, session idle/absolute cutoff, and every supplied `AccessValidUntil − 600s`. Issuance denies when the cutoff is at or before `Latest`. Validator requires `Earliest ≥ nbf` and `Latest < exp`: zero positive leeway. It also rejects lifetimes above 900 seconds.

S3 currently excludes *all* deadline-bearing/revocation-pending rows from current authority. The adapter preserves that stronger denial and supplies explicitly unbounded deadlines only for surviving rows. Finite deadline arithmetic is synthetic-tested independently; no new scheduled-grant state or reinterpretation of S3 was introduced.

### Validator and authorization

Only RS256/PKCS#1 SHA-256 with the configured RSA key, exact issuer/audience and exact JWT header profile are accepted. No algorithm negotiation, remote key URL or unsigned fallback. Canonical base64url, bounded token length, duplicate JSON property rejection, exact required property sets (including nested grant fields) and unknown-property rejection prevent ambiguous parsing. Missing/null/invalid IDs, invalid enum/versions/generations, empty grants, role/kind mismatch, duplicate enrollment courses, duplicate teacher IDs and overlapping teacher scopes deny. Optional nulls are explicit: enrollment GrantId, course-wide teacher GroupId. No implicit defaults for missing scope fields.

`ComponentAuthorization` consumes validated token authority and delegates parent affiliation to S3's server-side `ResolveResource`; it applies existing `ScopePolicy` for org/course/group, student ownership, teacher assignment and generation isolation, then checks current processing separately. It rechecks token time after asynchronous checks. It returns component evidence only, not HTTP admission or permission to publish a domain result. Files are explicitly denied in this component scope.

Processing checks use current PostgreSQL data with no positive cross-request cache: enabled SyntheticOnly organization, verified same-org course, exactly one applicable binding covering the entire clock interval and an effective SyntheticOnly context are required. Unknown/unavailable data never yields allow.

The existing schema has opaque restriction `ResourceRef`/operation bits without an implemented runtime resolver. The component conservatively denies any active restriction for the applicable context and caller/resource subject, regardless of those narrower selectors. This can overdeny; it cannot convert a relevant prohibition into an allow. A narrower interpretation needs a separately reviewed contract. Read-only component checks do **not** establish restriction/write serialization: future mutation adapters must recheck and serialize within their own transaction.

### Session and refresh scope

Existing S2 atomic refresh rotation, one-successor concurrency, inclusive 30-second O01 conflict, late replay session-only retirement/audit, idle/absolute expiry and SecurityBlocked behavior are retained and regression-tested. No alternative refresh store/protocol was created. The new callback only enables read-only JWT issuance under S2 locks. No combined refresh-plus-JWT HTTP/application workflow, cookies, CSRF or browser coordinator is claimed. Lost-response and concurrent-refresh component evidence remains distinct from browser guarantees.

## Verification

SDK `/tmp/lms-s1-dotnet` 10.0.401; existing dependencies; isolated `postgres:17.6-alpine` synthetic fixtures. Final command:

```sh
/tmp/lms-s1-dotnet/dotnet test Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/lms-s4-a1-results/final
```

**391 passed, 0 failed, 0 skipped:** Domain 43; Application 24; Infrastructure 138; API 186. Includes 36 new S4-A1 cases and the full existing S1–S3/backend regression. Build succeeded without warnings/errors. `git diff --check` passes. No Web source changed; browser integration was not tested or claimed. TRX and logs are local evidence in `/tmp/lms-s4-a1-results/final` and `/tmp/lms-s4-a1-final-tests.log`.

| Required group | Evidence |
|---|---|
| JWT issuance/expiry/conservative cutoff | Real synthetic PG issuance, 900s cap, earliest deadline minus 600s, session cutoff, fractional rounding |
| Invalid signature/issuer/audience/claims | Wrong RSA key, wrong issuer/audience/algorithm, every missing top-level claim, invalid and contradictory nested scopes, malformed encoding and duplicate JSON |
| Cross-organization denial | Policy negatives and fresh A/B issuance, independent A revoke/B access |
| Student/Teacher isolation | Student ownership/group negatives, teacher assigned-group and action checks; full S3 affiliation tests retained |
| Revoke/regrant generation races | Issuer waits on real organization lock and observes committed revoke; fresh regrant generation and old-token denial on newer-generation resource; full S3 concurrency regression |
| Concurrent refresh/replay O01 | Existing S2 real concurrent connections, exactly one successor, 0/29/30s conflicts, >30s replay, lost response and other-session survival |
| SecurityBlocked | Direct issuer denial; waiting issue observes committed block; S2 login/read/refresh block regression; old JWT stays locally valid under E03 |
| ProcessingRestrictions fail-closed | Missing context, current active restriction overriding previous allow, unknown clock and unavailable PostgreSQL |
| Clock boundaries/uncertainty | Interval-based expiry/nbf/exp, exact exp denial, one tick before/after, unknown/inverted clock; time re-read after organization-lock wait |
| Full S1–S3 regression | All solution tests pass, including existing schema/model/migration and immutable affiliation checks |

The initial bare `dotnet` command hit an unconfigured snap shim and could not run tests; the existing explicit SDK resolved this without installation. First successful full run: 383 tests. Eight additional security-review boundary cases and a JWT size guard led to the final 391-test run above. These are finite component/transaction tests, not vendor/runtime timing proofs.

## Security review and remaining dependencies

Self-review covered parsing/defaults, key/algorithm confusion, privilege scope overlap, generation isolation, lock order, expiry after waiting, no positive leeway, redacted bearer diagnostics, no per-request grant lookup and processing availability. No independent external security review is claimed. Signing keys are injected and never embedded in repository/config; tests generate ephemeral keys. Key custody/rotation and production clock health qualification remain host integration work.

Token and refresh wrappers redact `ToString` and default JSON. No logging or HTTP transport was added. Database errors are surfaced as errors, not success; any future adapter must preserve fail-closed behavior. Trusted internal DTOs must never be bound directly to public request bodies.

**Not proven:** production clock/dispatch bounds; bounded PostgreSQL commit/completion; HTTP ingress-to-last-egress; processing mutation serialization; storage/publication/recovery; real-data compliance; key operations; browser coordination. G02 B-PG/B-EGRESS/B-CLOCK and other A06 proof obligations remain open. E03-C and the proposed T04 replacement were not adopted. Synthetic PG lock tests prove observed transaction ordering only; they do not close G02 or establish a wall-clock SLA.

## No HTTP integration and next minimal stage

No component references occur in `src/Lms.Api` or `src/Lms.Web`. Existing public authorization is unchanged. No login/logout/refresh endpoints, middleware, cookies, CSRF, browser integration, storage gateway or domain publication were added. Existing API regression success is compatibility evidence only, not JWT HTTP acceptance. No real personal data or S5–S8 work.

Next minimal stage: owner acceptance of this component slice, followed by separately authorized component review of the processing selector contract and qualified clock/key adapters. Any public HTTP integration or publication work still requires explicit authorization and resolution of the applicable G02 proof obligations. No next stage is started here.

A separate local commit contains only the eight files listed above; its SHA is reported in the task completion message. Existing dirty/untracked owner artifacts remain outside that commit.

**S4-A1 READY FOR OWNER ACCEPTANCE · T04 UNCHANGED · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**


## Owner Decision — L1/S4-A1 — 2026-10-10

**S4-A1 COMPONENT SCOPE ACCEPTED.**

Владелец принимает реализацию внутренних JWT issuer/validator, scope authorization и current ProcessingRestrictions gate в пределах разрешённого S4-A1.

Основание: [отчёт S4-A1](L1-S4-A1-RESULTS.md), локальный commit `6f9bf19c0f1f8968b09ea288b0a06cd625cacb17`, зафиксированный в отчёте результат **391 passed, 0 failed, 0 skipped**. При записи решения тесты повторно не запускались.

Приёмка не распространяется на публичную HTTP-авторизацию, квалификацию production clock/key infrastructure, публикацию domain effects, файловые операции или соответствие T04 в целом.

Оставшиеся вопросы открыты:

- точная семантика ProcessingRestrictions selectors;
- запрет выдачи JWT при rollback/unknown commit;
- квалификация clock и signing-key lifecycle;
- независимая security review;
- G02 PostgreSQL/egress/storage proof obligations.

Это решение заменяет прежний статус ожидания приёмки только для S4-A1 component scope. Исторические отчёты и evidence сохраняются; общая приёмка S4 и принятие нормативных предложений A06 не заявляются.

Следующий этап **S4-A2 требует отдельного разрешения владельца**. Работа над ним этим решением не разрешена и не начата.

**T04 UNCHANGED · E03 UNCHANGED · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**
