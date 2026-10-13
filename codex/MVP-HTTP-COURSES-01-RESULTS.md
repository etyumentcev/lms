# MVP-HTTP-COURSES-01 — isolated protected read

2026-10-12. Owner authorization: **COURSE NAVIGATION ACCEPTED · HTTP-COURSES-01 ISOLATED AUTHORIZED** in the current task. Implementation is submitted for owner acceptance; no production cutover or deployment is claimed.

## Contract and scope

Student / Teacher → `GET /api/v1/courses` → courses from accepted server-issued scope, resolved through S3 ownership and current processing checks → existing `TokenAuthority`, `ResourceReference`, `CourseNavigationDto` → no domain state transition. `progress: null`; no count, cursor, work, student aggregate or attachment is exposed.

Dependencies: accepted course navigation; S2 sessions and `PostgresComponentTokenIssuer`; `ComponentJwt`; S3 `ComponentAuthorization` / `PostgresScopedAuthorityReader`; ADR-L18 conservative current-processing gate; CLOCK-01 / DEADLINE-01. [ADR-L15](../docs/adr/ADR-L15-jwt-delayed-revocation.md), [ADR-L16](../docs/adr/ADR-L16-organization-jwt-browser-session.md), [ADR-L18](../docs/adr/ADR-L18-synthetic-processing-restrictions.md), [guardrails](SAPIENTUM-MVP-ENGINEERING-GUARDRAILS.md) and [reconciliation](MVP-GUARDRAILS-RECONCILIATION.md) apply. Specialized model-domain / implement-use-case / implement-feature skills are unavailable; their execution is not claimed.

No Submit/Review changes, files/storage changes, schema/migrations, B08, H04/Ready or PRS1 exact implementation. No login/refresh endpoint, cookie flow or browser authentication integration is introduced.

## Architecture

`ProtectedCoursesHttp.cs` is an explicit opt-in transport adapter. It is not a controller discovered by production `MapControllers`, and production `Program.cs` is unchanged.

1. `CourseRequestIngress` captures exactly one ordinary `ApplicationDeadline` at the first application-controlled middleware, before authentication. Budget is server-fixed at 60 seconds. The same handle is kept in server-side features throughout the request; no client timeout or new stage resets it.
2. Standard ASP.NET Core authentication invokes `CourseBearerHandler` under a dedicated scheme explicitly required by the endpoint. It accepts only a single standard Authorization Bearer header. `ComponentJwt.Validate` performs the existing fixed RS256/key trust/signature, issuer, audience, shape, nbf and conservative expiry validation. Only its accepted return value populates the internal authority feature. Client course/org/role query parameters and headers do not populate authority.
3. `ICourseNavigationQueries` is the existing `PostgresCourseNavigationQueries`, with actual S3 and current-processing components. Course candidates originate only in signed scope; ownership/processing checks precede metadata reads. There is no legacy workspace adapter or new authorization policy.
4. Serialization occurs into a private byte buffer. Every returned course receives another fresh `ComponentAuthorization` check after serialization; JWT/key trust and the original deadline are checked again before writing bytes. A failed late check discards the entire buffer, including the title and list length. No partial successful list is sent by these application checks.
5. Responses are `no-store`. Invalid credentials return empty 401; late authorization denial returns empty 403; deadline/invalidation or PostgreSQL unavailability returns empty 503. Initially inaccessible courses are absent from a successful list (`{"items":[]}` when none is accessible). Missing deadlines fail closed. Request cancellation is carried by the captured deadline and DB/write calls.

The test host is a separate `WebApplication` + in-process TestServer, with no production `Program` execution and no external listener. Each test uses disposable PostgreSQL 17.6, the full existing migration chain and existing isolated capability profile. Independent request-scoped resource/processing/content DbContexts use the restricted `c2h_runtime` LOGIN, and are disposed by DI. Test setup provisions identities through the existing provisioning capability, authenticates via S2, and issues JWTs with `PostgresComponentTokenIssuer(..., restrictedCapabilities: true)`. There is no test signing shortcut for detached authority. Keys and credential fixtures remain in memory in the test assembly. Processing changes use the existing isolated processing capability.

Synthetic `HttpClock` implements a controlled probe/verifier only in the test assembly. It feeds the real `LocalQualifiedClockSource` and `QualifiedAuthorityClock`; its zero-error synthetic envelope is not evidence about the host's UTC or elapsed-time accuracy. `UnprovenLocalClockVerifier` is unchanged and separately tested to reject qualification and HTTP credentials.

## Authorization and temporal semantics

| Source / rule | Enforcement |
|---|---|
| Identity, actor, org, role and grants | Existing server-side S2 issuer and accepted JWT validator only |
| Session admission to issuance | Existing S2 enabled/revoked/created/idle/absolute checks; JWT lifetime capped by issuer |
| Ordinary grant or refresh-session revocation | Already issued JWT remains usable until conservative expiry under ADR-L15/L16; new issuance is refused |
| Resource ownership and org | S3 server-side course registry resolution; query/body/header selectors cannot grant access |
| Processing | Current same-org synthetic context/binding and restrictions, no positive cache, fresh response check |
| Group-specific Teacher / Student course grant | Grants course metadata read under existing ScopePolicy; this endpoint exposes no group/student workspace data |
| Time | Qualified intervals and one monotonic t0; strict expiry and strict 60-second cutoff; invalidation is terminal |

A group-limited Teacher reading the course metadata is valid under the accepted ScopePolicy. A rule denying that read as a “wrong group” would change existing authorization. Missing-group and wrong-role assignments are independently refused by the existing administrative component; their actors cannot obtain a learning JWT. Student attempting Review, forged work parents and attachment access are not operations of this endpoint.

## Acceptance matrix

Final results and commands are recorded below. The HTTP tests are named `Http_courses_*` in `ProtectedCoursesHttpTests.cs`.

| Cases | Expected observation |
|---|---|
| Student / Teacher positive (2) | Exactly one own course, correct role and private title, `progress: null`, no count field |
| Foreign course/org and other-course selectors; forged role/org headers | Same own result; foreign and unassigned IDs absent; no authority expansion |
| No token, malformed token, wrong signing key, issuer, audience (5) | Empty 401; no metadata |
| Exact JWT expiry, unknown clock, production Unproven verifier (3) | Empty 401; no synthetic fallback |
| No grant, foreign requested issuing org, admin-only, wrong-role grant, missing-group grant (5) | No JWT from server issuer, empty HTTP 401 despite client role/course flags |
| Grant revoked / session revoked (both positive roles) | New issuance denied; previously issued token still works; exact expiry denies |
| Active restriction, closed context binding, suspended organization before query (3) | Exact empty list; no title, description or progress |
| Same three changes after query, before response (3) | Empty 403, entire private result discarded |
| t0 + 60s − 1 tick / exactly 60s / 61s (3) | 200 / empty 503 / empty 503 |
| 30s before authentication + 30s after query (1) | Empty 503; captures queue/stage time in the same deadline |
| Explicit deadline invalidation (1) | Empty 503 |
| Clock qualification loss / JWT expiry after query (2) | Empty 403 from repeated course authorization; no bytes disclosed |
| Signing-key trust removed after query (1) | Empty 401 from final JWT validation |
| Processing SELECT permission lost after query (1) | Empty 503; DB failure is not allow |
| RequestAborted canceled after query (1) | Empty 503; no metadata disclosed |

Negative assertions verify exact empty error bodies or exact `{"items":[]}`; positive controls assert the actual course title, role, cardinality, null progress and `no-store`. Restriction and context changes are persisted in PostgreSQL between checks, not simulated with a replacement allow/deny policy. Tests do not claim network timing-side-channel resistance.

## Commands and results

```sh
/tmp/b07c2g/sdk/dotnet test Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/http-courses/regression
/tmp/b07c2g/sdk/dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~Http_courses' --logger trx --results-directory /tmp/http-courses/acceptance-final
PATH=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin:$PATH pnpm --dir src/Lms.Web test
PATH=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin:$PATH pnpm --dir src/Lms.Web build
```

- Full backend: **893/893 PASS**, failed 0, skipped 0: Domain 43, Application 340, Infrastructure 258, API 252 (including 31 new HTTP cases).
- Final targeted HTTP: **31/31 PASS**, failed 0, skipped 0 ([log](evidence/MVP-HTTP-COURSES-01/acceptance-final.log)). This rerun follows a test-only strengthening of foreign-org denial with a positive control for the same user/session and an existing foreign organization. Production adapter code is identical to the full regression run.
- Earlier successful targeted run: **29/29 PASS**, before adding Unproven and 61-second boundary cases; not added to final totals.
- Frontend: **387/387 PASS**, 13 files; TypeScript + Vite build **PASS**.
- New source/test whitespace checks and report local links: **PASS**. Whole-checkout `git diff --check` still reports a pre-existing blank line at EOF in `docs/content/COURSE-CONTENT-SPEC.md:1325`; that file was not changed.

Evidence: [test summary](evidence/MVP-HTTP-COURSES-01/test-summary.json), [backend log](evidence/MVP-HTTP-COURSES-01/regression.log), [frontend test log](evidence/MVP-HTTP-COURSES-01/frontend-test.log), [build log](evidence/MVP-HTTP-COURSES-01/frontend-build.log); TRX files are under the corresponding evidence subdirectories.

Initial execution with system `dotnet` failed before tests: the snap launcher found no installed SDK and could not install one noninteractively. Subsequent commands use the already available SDK from earlier checkpoints; no SDK installation or machine configuration change was needed.

Intermediate targeted failures were fixture errors: the first fixture mixed real session creation with an earlier synthetic clock (S2 correctly denied issuance), then attempted to change immutable course ownership (PostgreSQL correctly refused). The fixture now uses coherent synthetic S2 time and an authorized organization suspension. These failures were not bypassed in production code. Final results supersede intermediate runs, and their logs are retained in evidence.

## Limits and next endpoints

- **CLOCK-DEPLOY-01 OPEN**: production UTC/rate/uncertainty/age evidence remains absent; `UnprovenLocalClockVerifier` stays fail-closed. Synthetic test results cannot qualify deployment.
- **HTTP ingress/egress NOT PROVEN**: TestServer does not observe edge acceptance, upstream queue, header arrival, proxy buffering, physical socket drain, suspension across send or a multi-node deployment. The captured t0 is the first controlled application middleware; no claim that it equals external ingress. No hard physical completion or DB cancellation latency bound is demonstrated.
- Final authorization/time observations and response enqueue are not an atomic operation with PostgreSQL changes or scheduler pauses. A newly committed restriction after the last check, and already queued transport tails, retain the existing read/in-flight limitations; immediate global revocation is not claimed. No PRS1 exact serialization is added.
- Session logout/revoke does not create an access-token denylist. Session/credential issuance remains its own protected component boundary; no public auth UI, CSRF/cookie/refresh, browser race or real-device E2E acceptance is supplied here.
- `progress: null` until a separately accepted protected aggregate exists. No production workspace read endpoint is authorized by this result.
- Next read-only endpoints require explicit route scope/owner authorization, accepted resource/parent/subject mapping, genuine S3 and current-processing checks before materialization and response, the same request deadline and accepted validator, independent no-leakage negative tests, and a separate decision for production cutover with qualified clock and ingress/egress evidence. Legacy `IWorkspaceQueries` is not automatically qualified by these course tests.

## Guardrails §7 / self-review

| Item | Status and evidence |
|---|---|
| Actor/use case/invariants/states | covered — read-only contract above; no domain transitions |
| Accepted S2/S3/PRS1, closed gates | covered — reuses accepted components; exact OFF; production Program unchanged |
| Org/course/group/ownership | covered — signed grants + real S3; foreign client selectors cannot change authority; group metadata semantics documented |
| Subjects/embedded sources | covered — actor/course processing checks; no student-derived aggregates |
| Lists/counts | covered — candidate authorization before metadata, no pagination/count API, whole-response late denial |
| Attachments/history | not applicable — no file/history read or mutation in this route; existing regression retained |
| Transactions/locks/CAS/retry/unknown outcome | not applicable to route writes — none; existing read transaction and issuer/capability protocols unchanged; no retry added |
| Validation/limits/errors | covered — existing 64KiB JWT / 256-grant validator bounds, fixed scheme, safe empty denial, deadline/DB failure checks |
| Unit/integration/negative/E2E | covered by recorded regression and in-process HTTP tests; browser/edge/device evidence NOT PROVEN |
| Unapproved API/schema/unrelated changes | covered — only explicitly authorized isolated HTTP adapter; no schema/production composition changes |
| Real data/secrets/logging/readiness | covered — synthetic fixtures, generated memory-only secrets, no token logging or release claim |
| Actual results and gaps | recorded below/above; external clock and ingress/egress remain open |

## Changed files and preservation

New source: `src/Lms.Api/ProtectedCoursesHttp.cs`.
New tests: `tests/Lms.Api.Tests/ProtectedCoursesHttpTests.cs`.
New report: this file. New evidence: `codex/evidence/MVP-HTTP-COURSES-01/` (test logs/TRX, summary and preservation hashes).

All **615/615 baseline files are unchanged byte-for-byte**, including production Program, domain, existing course query, frontend, existing tests and checkpoint documentation. `.obsidian/` and ignored build artifacts are excluded from the baseline. Only the new files/evidence listed above were added.

Branch remains `codex/l1-s4-jwt-api-authorization`; HEAD remains `492e30aef65bbddd24f107903bfaa54a563abe39`; index SHA-256 is unchanged. Canonical checkout was already on this dirty branch; no switch to main was attempted. See [preservation evidence](evidence/MVP-HTTP-COURSES-01/preservation.json).

No commit, push, merge, rebase or deploy. Stop at owner acceptance.

**HTTP-COURSES-01 ISOLATED IMPLEMENTED — OWNER ACCEPTANCE PENDING · CLOCK-DEPLOY-01 OPEN · G02 BLOCKED · H04/READY BLOCKED · PRS1 EXACT OFF · S4 HTTP CUTOVER NOT AUTHORIZED.**
