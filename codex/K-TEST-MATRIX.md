# K — Risk and test matrix

Baseline: `47d6de3`, branch `codex/k-release-hardening`, canonical checkout. Audit-first; no additional worktree/framework. Baseline 362 frontend and 248 backend tests passed (43 Domain, 16 Application, 11 Infrastructure, 178 API). Logs `/tmp/lms-k-baseline-{web,backend}.log`. Frontend build passed. Initial frontend launcher lacked Node; rerun used bundled Node 24.

| Risk | Severity | Existing evidence inspected | K check / initial status |
|---|---|---|---|
| Broken six-screen lifecycle, order, annotations | P0 | J2 real Playwright script; only first page annotated, no reorder | Extend same stack, run isolated Docker / NOT TESTED |
| JWT/IDOR | P0 | BearerAuthenticationTests, ContractApiTests, AttachmentApiTests, AnnotationApiTests | Add unsigned token case; rerun / baseline PASS |
| Lost concurrent decision/attempt | P0 | PersistenceTests and API barriers, real PostgreSQL | Verify commit-boundary failure / baseline PASS |
| Partial upload/claim/review | P0 | AttachmentConsistencyTests, failure after SaveChanges | Extend commit-boundary evidence / baseline PASS |
| Schema/history loss | P0 | Legacy_review_survives_annotation_migration_with_empty_array | clean bootstrap, restart/down-up/reset / NOT TESTED |
| Unbounded resource consumption | P1 for public deployment | Per-file/annotation limits; no upload quotas/rate limiter | audit deployment blockers / NOT TESTED |
| Large aggregate cross-product / N+1 | P1 if primary workflow unusable | AsSingleQuery over three collections; Lesson per-assignment resolution | measure P1–P6 before changing / NOT TESTED |
| Stylus browser gesture / HTTP UUID | P1 for affected tablet pilot | post-base commit 594578d has regression tests | reproduce then port scoped fix / NOT TESTED |
| Real tablet compatibility | P1 for tablet-first pilot | only CDP/emulation | BLOCKED — physical device unavailable |
| Real IdP, TLS, backup/recovery | P0 deployment gate | dev-only compose, no deployed IdP | NOT TESTED; separate pilot/production gates |
| Missing names/lookups/reference answers | P2 provisional | nullable DTO/source absent | classify minimum pilot workaround |

Final evidence and coverage are appended after execution; initial statuses above preserve audit order.

## Reproduction

From repository root, with SDK 10.0.401, Node 24, pnpm 11.19.0, Docker and existing
Playwright + installed Chrome. This reuses the established Playwright `.mjs` stack;
adding a second TypeScript/test-runner framework would duplicate working infrastructure.
No dependency or framework was installed by K.

```sh
dotnet test Lms.sln -m:1 -p:UseSharedCompilation=false
pnpm --dir src/Lms.Web test
pnpm --dir src/Lms.Web build
dotnet build Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false
LMS_K_PERFORMANCE_OUTPUT=/tmp/lms-k-queue.json dotnet test tests/Lms.Api.Tests --filter FullyQualifiedName~Release_queue
LMS_PLAYWRIGHT_MODULE=/absolute/path/to/playwright bash src/Lms.Web/scripts/release-check.sh
git diff --check
```

Executed SDK `/tmp/lms-dotnet/dotnet`; Node directory
`/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin` added to PATH;
Playwright module `.../dependencies/node/node_modules/playwright`.
Runner refuses an existing `lms-k-smoke` project, generates dedicated Compose, binds
127.0.0.1:8086, uses distinct named volumes, runs clean seed, real E2E, emulated tablet,
reset and performance, and cleans its own containers/volumes in EXIT trap. It never
resets `lms-local`. For manual script invocation use the generated COMPOSE_FILE and
LMS_SMOKE_URL. E2E asserts project/volume identity before any destructive lifecycle.

## Coverage of requested E2E sequence

| Steps | Concrete assertions in `release-smoke.mjs` |
|---|---|
| 1–6 | fresh migrated demo, student bearer login, actual Course → Lesson → Problem links, null work |
| 7–10 | two A4 PNG multipart uploads, reorder page2 above page1, same-tick double click, authoritative Submitted |
| 11–17 | teacher login, filtered queue/ProblemWorkId link, hashes in reordered sequence, independent strokes on both pages, feedback |
| 18–23 | one revision POST, NeedsRevision, Student History before resubmit, two annotated originals and SHA-256 unchanged |
| 24–29 | distinct photo, attempt2, previous comments/attempt1, new annotation; genuine message-driven version conflict and explicit refresh |
| 30–35 | accept with blank feedback, Accepted, resubmit/review409, two immutable attempts/reviews, deep refresh keeps data |

Additional real E2E: empty solution rejected422 (contract forbids it); text-only valid;
malformed PNG upload400 blocks submit and removal recovers; required revision feedback;
return to filtered queue; browser Back/Forward; Student teacher UI/alias403, queue/review403;
anonymous work401; stale request cancellation while rapidly switching routes. The race
holds then continues a real request; it never manufactures a response. Late responses
that ignore cancellation are additionally exercised by existing unit tests
`ignores stale response after route identity changes` and queue/problem equivalents.

## Backend boundaries and outcome

- Baseline **248 PASS**, final **260 PASS**: Domain43, Application16, Infrastructure15,
  API186, zero failed/skipped. Final log `evidence/k/final-backend.log`.
- Frontend baseline **362 PASS**, final **366 PASS**, zero failed/skipped.
  `evidence/k/final-web.log`. Builds: TypeScript/Vite and .NET pass; .NET 0 warnings/errors.
- Added backend cases: unsigned JWT1, 1000-row queue/query-count1, revoked attachment
  access4, teacher alias role/scope in two states2, commit-boundary rollback2,
  interleaved snapshot read2. Existing security/concurrency tests retained.
- Frontend: ported three demonstrated stylus/UUID regressions from `594578d` (3 red on
  base → green); new React Profiler test proves 100 pointer moves do not commit React
  on every point. Final stroke creates a commit and visible annotation.
- Upload/file/metadata/claim failure stages: `AttachmentConsistencyTests`; after all
  DB writes: `Failure_after_all_writes_rolls_back_state_history_and_events`; at commit:
  `Commit_failure_rolls_back_and_retry_keeps_attempt_and_annotations`. Review/annotation
  share an INSERT and transaction, no separate annotation commit.
- Full migration bootstrap, legacy empty annotations, DB immutable triggers, concurrency
  and version semantics: `PersistenceTests`, all rerun against real PostgreSQL.

## Honest limits

Physical iPad/Pencil/Safari/Android: **BLOCKED**, see device protocol. Real production
OIDC/TLS, backup restoration, failure recovery and internet-scale/concurrent capacity:
**NOT TESTED**, release gates, never inferred from these green developer checks.
Historical J1/J2 reports remain historical. K preserves them and records changed
teacher route/attachment policy in current amendments instead of rewriting past results.

## Final browser / release-run evidence

**PASS** final `release-check.sh`: `evidence/k/runner.log`, `browser.json`,
`tablet-emulated.json`, `performance-final.json`, `queue-final.json`, `cleanup.log`.
Zero browser page errors. Tablet measured201 strokes/20002 points,3501.9ms scripted
stress; its resize test originally raced the RAF scheduled by ResizeObserver, then
passed after awaiting the unchanged exact geometry predicate. Failure log retained.
No physical-device inference. End-to-end runner succeeded after fixes and removed all
its own resources. Legacy J1/J2 smoke Student-screen expectations updated to K policy;
K superset is the executed release regression, not a claim that every historical
standalone fixture script was separately rerun.
