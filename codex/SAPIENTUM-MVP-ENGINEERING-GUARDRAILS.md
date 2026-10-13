# Sapientum LMS — MVP Engineering Guardrails

> **NR1 effective notice — 2026-10-11.** Прежнее «T04/E03 unchanged» ниже — исторический статус. Единственное новое нормативное изменение — scoped temporal supersession D1–D8 по N1–N7. Остальные guardrails и открытые security gates сохранены; NR1 не разрешает implementation или release.
> Нормативный источник: [N1–N7 / точный D1–D8](MVP-B01-G02-NORMATIVE-REVIEW.md), [принятое решение](L03-OWNER-DECISION-REGISTER.md), [sync results](MVP-B01-G02-NR1-SYNC-RESULTS.md). Только controlled SyntheticOnly и отдельно принимаемые routes; неопределённый scope → blocked. Credential issuance, grants, B08 publication, general jobs и внешние irreversible effects не получают автоматического исключения. CLOCK NOT PROVEN; технически NR1 NOT IMPLEMENTED / NOT ACCEPTED.
> Исторический текст ниже сохранён; старые failure results не превращены в PASS.

**Owner proposal · 2026-10-10 · applies to all new MVP work, pending adoption in repository**

## 0. Intent and precedence

Goal: complete user workflows, not only six screen layouts, without creating authorization, privacy, persistence, or operational debt requiring later rewrites.

Precedence: accepted ADRs and owner decisions > domain invariants > this document > feature-level implementation plan. On contradiction, **STOP and request an owner decision**; never silently weaken an accepted control. This document is a cross-cutting implementation policy, **not** authorization to bypass S4/PRS1/G02 gates or to deploy with real children's data.

**Current security boundary:** S1–S3, S4-A1/A2 accepted as component scope; PRS1 normative contract accepted, I1 accepted; I2 authorized but not reported accepted; exact PRS1 gate OFF; S4 HTTP not authorized; T04/E03 unchanged; G02 blocked; external human audit / SR-01–SR-04 open. Record any future owner decision separately. The pivot to MVP prioritization **parks** I2–I5; it does not revoke prior authorizations or imply permission to start S4 HTTP.

## 1. What counts as a complete MVP

Do not count a screen as complete by itself. Deliver a closed set of end-to-end use cases with tests. At minimum establish:

1. Provision organization, actors and enrollment/assignments via an approved administrative path (no assumed public self-registration).
2. Activate account / recover access / sign in / refresh / sign out and recover from expiry across browser tabs.
3. Teacher prepares/publishes a course and lessons, assigns eligible students, views progress and materials.
4. Student sees only assigned courses and tasks, opens Markdown/LaTeX materials, submits one or more A4 photographs and optionally text, and sees submission history.
5. Teacher queue filters and pages only authorized submissions, opens original images and rubric/reference, marks up photographs on tablet, submits feedback; student revises and teacher accepts.
6. Attachments retain server-verified parent, ownership, state and lifecycle; no direct public object URLs or unscoped downloads.
7. Course progress and teacher statistics are computed using the same eligibility/authorization filter as their underlying rows.
8. Validate user-visible errors, empty states, concurrency conflicts, upload failures/retries, session expiry, mobile/tablet accessibility and restore/restart behavior.

Each use case needs an accountable actor, command/query, invariant, error/retry policy, complete UX and executable acceptance tests. Features requiring still-blocked infrastructure must remain synthetic-only or disabled; do not label them production-ready.

## 2. Non-negotiable security invariants

**Never trust a client-supplied** OrganizationId, ActorId, role, teacher assignment, course enrollment, attachment parent, review ownership, prior review ID, status, scope or permission. Route IDs are selectors, not authority. Resolve ownership and authorization on the server using S3's accepted model; enforce org/course/group boundaries on every command, query, history lookup, attachment and aggregate. Deny on missing/ambiguous links.

Authorize **before materialization**, including list counts, pagination, sorting, search, exports and summary statistics; unauthorized rows cannot influence counts, cursors, facets or side-channel metadata. Do not use client-side filtering as the security boundary. Avoid APIs that accept unrestricted arbitrary expressions or SQL fragments.

Every state-changing request checks current domain invariants and expected version in the same transaction that commits the change. Do not accept a stale `Allows=true` from an earlier transaction as proof of permission. Where PRS1 exact semantics are not implemented, retain the existing conservative restrictions; never short-circuit, silently ignore restrictions or treat successful parsing as trusted provenance.

**File access**: treat uploads/downloads, previews, thumbnails and annotations as protected resources. Verify organization, actor, context, parent (Submission XOR Message), file ownership and status at each access; disallow unverified or pending files from downstream reads. Use opaque server-controlled identifiers, content-type/size limits and private storage. Never use a caller-supplied filesystem path, arbitrary URL, or publicly addressable file object. Unsupported file operations deny.

**Authentication**: use existing S2/S4 session and JWT contracts; no new home-made token/cookie schemes, client-provided JWT scopes, localStorage refresh tokens or insecure cookie flags. Preserve token expiration, key-trust, CSRF/Origin and session revocation rules when HTTP integration is separately authorized. Never put secrets or bearer tokens into logs, URLs or analytics.

**Privacy**: minimize children's personal data, enforce organization isolation and documented ProcessingContext. No production or real-child-data pilots while G02 and applicable legal/security release gates remain unresolved. Use synthetic fixtures, pseudonymous identifiers and secret-free audit logs.

## 3. Architecture and persistence rules

- Keep Domain invariant enforcement in aggregates; Application coordinates use cases; Infrastructure implements persistence/storage; API only adapts transport; React holds presentation and transient state. Avoid domain logic or authorization decisions in controllers/components.
- Prefer extending existing S3 ownership and PRS1 interfaces; do not invent a parallel role/scope system. `ScopeAction` enum ordinals are not PRS1 operation bit masks.
- For every write identify **transaction owner, isolation, lock order, CAS/version guard, commit point, retry/unknown-outcome policy, and audit effect**. If any is unclear, stop before coding the write.
- Use additive migrations by default; no destructive changes, silent backfills, manual modification of historical submissions/reviews, or resetting user data. Verify schema changes on a disposable DB with the complete migration chain.
- Generate immutable history for submissions, reviews and messages; do not rewrite older attempts or rebind a review to the latest submission. Preserve Submit → Review → Revise → Accept.
- Commands with a risk of duplicate effects use stable command IDs/idempotency where contract requires it. A lost commit acknowledgement is **indeterminate**, not evidence of rollback.
- Treat external I/O as separately bounded and failure-prone. Do not hold PostgreSQL transactions open across network/file operations. Never claim a hard real-time guarantee from CancellationToken, WaitAsync or nginx inactivity timeouts alone.
- Config secrets via approved configuration/secret stores; no committed passwords, private keys or example real credentials.

## 4. Minimal feature execution process (Codex)

For **each feature**, follow the same small protocol rather than a large new architecture study:

**Step A — Feature contract (1 page max):** actor(s), user journey and screens, accepted dependencies, involved resources and data, authorization matrix, aggregates/invariants, commands/queries, state transitions, error/timeout behavior, and acceptance tests. Identify any crossing of a blocked gate.

**Step B — Threat/consistency checklist:** BOLA/IDOR, cross-org leakage, teacher/student privilege escalation, parent/file bypass, unauthorized list counts, historical attempts, concurrent writes and retries, malformed input, secrets/PII in logs. Mark each `covered`, `not applicable with reason`, or `blocked`.

**Step C — Small vertical slice:** domain → application → persistence → transport only where separately authorized → UI → unit/integration/E2E tests. Implement a complete user journey, not a set of unconnected screens. Keep diffs reviewable; no unsolicited refactors or new frameworks.

**Step D — Self-review and evidence:** paths changed, invariants preserved, risks, exact tests run with counts and commands, independent negative access tests, migration verification where relevant, coverage gaps and explicit `NOT RUN` status. Do not claim acceptance, rollout readiness or security proof solely from green unit tests.

**Step E — Owner gate:** ask for approval only for architectural/security deviations, new permissions, new exposed routes or API behavior, new cross-org data flow, new processing purpose, schema changes affecting existing data, and release-gate changes. Otherwise follow accepted contracts and complete the slice.

## 5. Mandatory negative acceptance tests by feature

For every new user-visible data access path, test at least: authenticated own resource; different organization; same organization but wrong course/group/assignment; student attempting teacher operation; teacher not assigned to group; revoked/moved membership as relevant; missing/forged parent; mismatched ownership; stale version/concurrent mutation; incomplete processing/subject evidence; inaccessible attachment; and forbidden data absent from list/count/pagination. Not-applicable cases require an explanation in the feature report.

For browser/UI flows also test: session expiry mid-edit, refresh failure, CSRF/Origin failure as applicable, upload retry, duplicate submission, mobile/tablet interaction, offline/reconnect, safe display of untrusted Markdown/LaTeX, and no leakage through client cache/logging.

## 6. Red flags: STOP before implementation or merge

1. A client parameter determines the organization/owner/role without re-resolution.
2. A new endpoint accesses a resource without a shared server-side authorization boundary.
3. A list/count/aggregate is filtered only after pagination or only in the browser.
4. A file is served by ID/path without independent parent/context authorization.
5. A security-relevant mutation separates authorization and data commit across transactions without an accepted protocol.
6. Existing conservative restrictions are bypassed or narrowed by parsing/flags/fallback.
7. An unsupported workflow is made to work by relaxing domain rules or treating `Unknown` as `Allow`.
8. A migration rewrites history or a retry might duplicate externally visible effects.
9. A tool claims G02, PRS1 exact, human audit, legal readiness, HTTP release or production readiness without explicit evidence and owner authorization.

## 7. PR / merge checklist (copy into every change)

- [ ] Actor, use case, invariant and supported states documented.
- [ ] Accepted S2/S3/PRS1 rules respected; blocked gates not crossed.
- [ ] Org/course/group/ownership checked on server for command and query.
- [ ] Affected subjects and embedded sources accounted for, otherwise deny.
- [ ] Lists/counts/aggregates filtered before revealing unauthorized data.
- [ ] Attachments and history independently protected.
- [ ] Transaction, lock order, CAS, retries and unknown outcome specified.
- [ ] Validation, limits and safe error behavior covered.
- [ ] Unit + integration + negative-access + relevant E2E tests recorded.
- [ ] No unapproved schema/API/security or unrelated-file edits.
- [ ] No real data, secrets, unsafe logs or unsupported release claims.
- [ ] Tests actually run with exact results; remaining gaps marked NOT PROVEN.

## 8. MVP workstream and gates

**Track P — product completeness:** course/lesson authoring, assignments, student submission UX, photo annotation/review, materials, history, queues and progress/statistics, onboarding/admin user journey. Work on synthetic data using accepted component interfaces and protected backend mock/adapters without creating unauthorized HTTP entry points.

**Track I — integration:** S4 HTTP/login/refresh and real storage integration only after **separate narrow owner authorization** and required review of security/timeout dependencies. Integrate one complete journey at a time, with authorization regression.

**Track S — security completion:** preserve S4-A4-I1 acceptance, park unfinished I2–I5 unless reauthorized as priority, keep PRS1 exact disabled and legacy conservative, manage SR-01–SR-04/G02/human audit and legal readiness as explicit pre-pilot gates.

**Completion labels:** `UI COMPLETE` != `E2E ACCEPTED` != `SYNTHETIC MVP ACCEPTED` != `PILOT AUTHORIZED`. Never collapse these statuses.

## 9. Instruction to Codex on every feature request

> Implement this Sapientum LMS feature under `SAPIENTUM-MVP-ENGINEERING-GUARDRAILS.md`, the accepted ADRs and owner decisions. First give a compact Actor → Use Case → Invariants → Entities/VO → State transitions → Authorization & data-source matrix → Tests, identify any blocked gate, then implement the smallest end-to-end slice. Reuse S2/S3 ownership and existing security components; never create a parallel authorization system. Enforce server-side authorization before lists/counts/attachments and fail closed on unknown processing evidence. Preserve all pre-existing dirty files. Run relevant positive and negative tests, report exact outcomes and open proof obligations. Stop for owner decision on a conflicting requirement or crossing a blocked gate. Do not commit, push or deploy without authorization.
