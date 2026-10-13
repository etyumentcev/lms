# Checkpoint K — Integration & Release Hardening

**Result: NOT_READY for pilot and production.** Audit, real E2E, measurements, scoped
fixes, regression and requested reports completed. Operational and device release
blockers remain explicit; K completion does not mean release approval.

## 1. Branch / base / commit

`codex/k-release-hardening`, exact base `47d6de3`, canonical `/home/tea/github/lms`.
No new worktree or delegated agents. Initial checkout was clean at `594578d` on the
J2 branch. K was explicitly created from the requested older base; the confirmed
stylus patch from `594578d` was ported with its three regression tests, not silently
included by changing the base. Result commit is the commit containing this report:
`git log -1 --format='%H %s' -- codex/K-RESULTS.md` (reported to the user after commit).

## 2. Changed files

- API `QueriesController.cs`: role-gated teacher screen GET alias, existing general
  work GET and review command contracts preserved.
- Infrastructure `AttachmentService.cs`: all attached downloads follow current work
  authorization, including uploader; `PostgresWorkStore.cs` and
  `PostgresWorkspaceQueries.cs`: split history queries with consistent snapshot.
- Web annotation model/editor: tested stylus gesture reservation + secure RNG UUID
  fallback; teacher query uses role-gated alias. No redesign or new Domain workflow.
- Browser scripts `release-check.sh`, `release-smoke.mjs`, `release-performance.mjs`;
  existing tablet script waits for async resize geometry, existing J1/J2 smoke
  expectations reflect Student denial. Same Playwright stack; no new dependency.
- Regression tests in `annotations.test.tsx`, `teacherReview.test.tsx`,
  `BearerAuthenticationTests`, `AttachmentApiTests`, `AnnotationApiTests`,
  `PersistenceTests` and transaction fixtures; new `ReleasePerformanceTests.cs`.
- Seven K reports, durable `codex/evidence/k/`, ExecPlan §26, README/frontend README,
  Feature/Frontend K contract amendments. No migrations or historical data rewrites.

Exact tracked inventory: `git show --stat` / `git show --name-only` on the result commit.

## 3. Baseline

362 frontend and248 backend tests PASS (43 Domain,16 Application,11 Infrastructure,
178 API); frontend build PASS. Raw baseline logs checked in under `evidence/k/`.
Existing historical smoke coverage audited before extending it: J2 annotated only
one page, did not reorder in its complete workflow, and lacked pre-resubmit History
verification. C4/U1 already had real DB/auth/claim/rollback tests; they were retained.

## 4–5. New E2E and results

**PASS**, `release-check.sh` clean isolated Docker runner; `browser.json`, `runner.log`.
All35 requested main steps covered in `K-TEST-MATRIX.md`, including ordered real uploads,
annotations on both pages, feedback, NeedsRevision → second attempt → Accepted,
previous remarks, immutable originals and both histories after reload.
Additional real negative/transport/route/terminal cases pass; API responses are never
mocked. Held-request test delays then continues a real request during route change;
unit coverage also forces late responses that ignore abort. Empty solutions correctly
return422; text-only works. Every fake test resource is disposable. Cleanup passed.

## 6–9. Security, authorization, attachments, annotations

`K-SECURITY-AUDIT.md` contains severity, exploit, endpoint, evidence and disposition.
**P0 fixed**: upload owner could retain access to attached originals after parent
access revocation. Four red tests (200 instead of404) now pass; student reading an
actual authorized teacher photo message and foreign-student denial also checked.
Backend JWT/unsigned claims/IDOR/scopes/annotation ownership and bounds pass.
Teacher-screen Student restriction now enforced by server alias; C4 general own-work
read remains available. Original SHA-256 and immutable Review/attachment DB constraints
pass. Per-file/request bounds are not global storage/rate quotas: K-S02 remains open.

## 10–11. Concurrency and transaction integrity

**PASS** real PostgreSQL first-submit, attachment claim/delete and concurrent-review
races: one winner, loser conflict, correct versions/attempts/history. Existing post-write
rollback retained; new commit-entry failure tests confirm first-submit/review atomicity
and correct retry. Annotations are part of the Review JSONB INSERT/transaction.
Two new interleaved-write snapshot cases prove no mixed history with split queries.
The old test barrier was scoped to write transactions to preserve its intended race;
read snapshots must not deadlock sequential fixture preflight. Full suites rerun.
Process-kill/power-loss/Review commit-acknowledgement loss are NOT TESTED.

## 12. Persistence / migrations

**PASS** clean PostgreSQL bootstrap, legacy Review upgrade ([] annotations), model
migration checks, immutable history, Docker API restart, down/up, repeated deployment
migrate/seed and disposable down-v reset. K adds no schema migration. Test reset does
not delete user data: only `lms-k-smoke` volumes, never `lms-local`. Backup restore is
NOT TESTED and remains a real-data release gate.

## 13. Performance

P1/P2/P3:1/5/10 generated A4 photos; P4:500 strokes/50000 points; P5:20 annotated
reviewed attempts; P6:1000-work server-paginated queue. Actual baseline/final JSON
and measurement limitations are in `K-PERFORMANCE-REPORT.md`.

- **P1 fixed**, history Cartesian expansion: teacher GET p95 **5520.76 →59.36 ms**;
  student workspace p95 **5328.28 →71.87 ms**; full20-image History load single
  observation **5601.73 →281.72 ms**. No loss of snapshot consistency.
- Queue page50 p95 **39.28 ms**, fixed5 SQL commands; no queue N+1.
- Remaining **P2**: dense canvas next-frame wait p95 **73ms**, near4 MiB payload;
  teacher Lesson1001 assignments **2011 queries /1166.20ms**. These are reported,
  not hidden under the history improvement. Staging capacity/soak not certified.
- Object URLs:1 active while viewing,0 after navigation; React Profiler confirms
  no per-point commits. JS memory/listeners measured; no multi-hour/GPU leak claim.

## 14–15. Browser and physical devices

Headless Chrome real E2E PASS at five widths. Emulated tablet PASS: EXIF6, DPR2,
portrait/landscape, CDP pen pressure,201 strokes/20002 points, fullscreen fallback,
original bytes. Resize assertion initially raced ResizeObserver; now waits for the
same exact geometry invariant (no relaxed tolerance), rerun passes.
Screenshots saved in `/tmp/lms-k-run/{browser,tablet,performance}`. Automated geometry
is verified; local image viewer failed due environment sandbox mountinfo error, so
no additional human visual-review PASS is claimed.
**Physical iPad/Apple Pencil/touch/Safari/Android BLOCKED — physical device unavailable.**
Safari/WebKit engine NOT TESTED.90 device/scenario protocol rows ready for a real tester;
no fake signature/screenshots or equivalence between emulation and physical testing.

## 16. Contract gaps

Reference solutions/criteria have no compiled teacher-only source; names lack trusted
profile store; filter lookups absent. `K-MVP-GAPS.md` records source, user impact,
minimal authorized solutions, estimates/priorities and pre-pilot/defer recommendations.
No new scoring, roles, course architecture, reference content or fabricated names added.

## 17. Fixed defects

| ID | Severity | Fix / regression evidence |
|---|---|---|
| K-S01 | P0 | Attached uploader authorization bypass closed;4 API revocation cases red→green |
| K-P01 | P1 | History cross-product eliminated with repeatable snapshot;2 interleaved read cases + before/after P5 |
| K-C01 | P1 for affected tablet/local-network use | Port stylus touch-action + UUID fallback;3 tests fail on base then pass. Physical acceptance still blocked. |
| K-S03 | P2 contract discrepancy | Student cannot enter Teacher Review through role-gated backend alias;2 API cases and browser403 |
| K-T01 | P2 test reliability | Tablet resize smoke waits for ResizeObserver/RAF; original geometry tolerance unchanged; successful rerun |

## 18. Remaining defects / gates

Open P1: deployed real identity/session integration, exposed-service resource controls,
coordinated backup/restore/recovery. Open P2: Lesson/Course N+1, dense drawing headroom,
orphan reconciliation/manual retention, roster/lookup/material source gaps. Intended
physical-device compatibility is BLOCKED. No remaining confirmed P0 application
finding from this audit. See readiness for closure evidence; do not release by treating
P1 gates as cosmetic limitations.

## 19–22. Regression, Docker/browser smoke and builds

- **Frontend366 PASS**,0 failed/skipped, TypeScript/Vite PASS.
- **Backend260 PASS**:43 Domain +16 Application +15 Infrastructure +186 API,
  0 failed/skipped; targeted final P6+Lesson measurement also PASS.
- .NET build **PASS,0 warnings/errors**. `git diff --check` PASS.
- Docker/browser/emulated-tablet/performance runner **PASS**; API/migrate/Web builds,
  healthy startup and clean reset; final cleanup leaves no K containers/volumes.
- Current logs/raw results retained under `codex/evidence/k/`; reproduction commands
  and scenario-to-test mapping in `K-TEST-MATRIX.md`. Transient screenshots under /tmp.

## 23–25. Pilot / production readiness and blockers

**NOT_READY** for both. Development succeeds; pilot/production IdP/TLS/operations,
resource protections and coordinated restore have not been demonstrated. Intended
real-device/browser acceptance missing. `K-RELEASE-READINESS.md` evaluates13 dimensions
separately for Development/Pilot/Production and lists explicit closure steps.
Green localhost Compose never establishes production readiness.

## 26. Recommended next checkpoint

Pilot Identity & Operations Readiness: select/validate staged IdP + deployment,
resource limits, backup/restore/operator recovery and real-device testing, then rerun
this E2E with real staged identities. Recommendation only; not started.

## 27. ExecPlan changes / exit

Updated current-status line and added §26 with audit order, baseline, findings,
scoped fixes, measurements, reports, release decision and remaining gates. Existing
checkpoint history kept. Commit records this K result; working tree checked clean
after commit. Stop after K. No following checkpoint, push, merge or deployment requested.
