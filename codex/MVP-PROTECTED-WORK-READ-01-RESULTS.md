# MVP-PROTECTED-WORK-READ-01 — isolated work/history reads

2026-10-12. **PARTIAL IMPLEMENTATION — WORK/HISTORY OWNER ACCEPTANCE PENDING; PHOTO PATH BLOCKED.** Authorization: the current owner decision accepts BROWSER-READ-01 and authorizes isolated protected work reads, explicitly requiring the dependent file path to stop if an unaccepted storage/recovery contract is needed. This report does not claim that the complete photographic workflow is implemented or accepted.

## Contract and pre-implementation inventory

Student → problem → permitted work → historical attempt/feedback; assigned Teacher → lesson → permitted works → work → historical attempt/feedback and statement. No domain transitions: only transient loading, success, error and attempt selection. Existing S2 JWT, S3 ownership, current conservative ADR-L18 processing, synthetic qualified clock and original 60-second application deadline are reused. Ordinary revocation remains delayed; resource/processing/time gates are fresh, not a new per-request grant freshness policy.

Read repository AGENTS, MVP guardrails/reconciliation, ADR-L15/L18, NR1 N1–N7, preceding HTTP/workspace/browser reports and relevant S3/component code. The specialized model-domain, implement-use-case and implement-feature skills are unavailable in the session; their execution is not claimed.

| Category / existing source | Treatment |
|---|---|
| Immutable CourseRegistry/PublishedContentVersions; WorkspaceContent DTO | Common course content only. Exact course/version/lesson/problem membership verified independently. A course grant does not imply work/file access. Existing protected statement endpoint remains available to assigned teachers. |
| ProblemAssignment + immutable AssignmentScope | Server-resolved org/course/group/student and membership/enrollment generations; actual assignment content reference checked against requested version/lesson/problem. No client ownership claims. |
| ProblemWork | S3 Work Read before status/history materialization. Minimal list: opaque ID, problem selector, status, authorized href. No profiles, aggregate progress, unfiltered counts or pagination. |
| Submission | Independent S3 Submission Read and verified work/student chain; attempts ordered by AttemptNumber. Body/time and attachment **count only** from authorized submission history. No attachment IDs. |
| Review | Independent S3 Review Read; submission/work parent checked; current restrictions for the historical teacher as affected subject, including when the reader is a student. Feedback message must belong to the same work and teacher. No annotation data. |
| Message | Author and parent selectors checked before body; student author must match owner, reply parent must be in this work. Every author needs same-org membership evidence and an additional current processing check. Unknown author evidence denies the entire history. Role labels only, no identity/profile IDs. |
| StoredAttachment / AttachmentScope / AccessAttachments / LocalFileAttachmentStorage | Inventoried, not used by new queries. Their legacy eligibility/claim does not prove NR1 sealed immutable exact-version storage. Metadata, originals, previews, Range and A4 viewing remain blocked. |
| Legacy mixed workspace DTO / QueriesController / AttachmentsController | Not reused or registered by the new isolated composition. Legacy writes and file authorization are unchanged. |

Historical work reads follow accepted S3 Read semantics, including ownership after enrollment moves; they do not infer submit eligibility from lesson availability or assignment active status. Statement availability follows the existing independent content endpoint. Work lists contain only already-existing works, not invented unstarted assignments.

## Implemented HTTP and browser paths

Let `L=/api/v1/courses/{courseId}/versions/{versionId}/lessons/{lessonId}`.

| GET | Result |
|---|---|
| `L/works` | Authorized works for the exact lesson |
| `L/problems/{problemId}/works` | Authorized works for the exact problem |
| `L/works/{workId}` | Minimal work, ordered attempts, per-attempt reviews, message history, exact protected statement href |

`ProtectedWorkHttp.MapProtectedWorkReads` is explicit opt-in. The existing test `HttpHost` registers it only with `workReads:true`; production Program.cs is unchanged. The separately built browser-read entry uses existing Sapientum AppShell/Markdown renderer and new read-only work screen. No Submit, Review, upload, claim, annotation persistence, cookies/refresh, auth endpoint, test credentials, production clock verifier or production activation was added.

All route IDs are selectors. Candidates are authorized before personal response fields are materialized or sorted; no pagination/aggregate leaks. The query captures the authorized immutable history ID set, excluding concurrent appends. Work versions are rechecked before response: concurrent append/state changes fail closed rather than returning a mixed snapshot. Every returned resource/author is re-gated; the shared private-buffer lifecycle revalidates JWT trust/lifetime and original ResponseEnqueue deadline. No positive authorization cache. Safe errors are empty 401/403/503; no-store applies. Late expiry/clock loss can yield 503 at the earlier deadline gate; new expired-JWT requests yield 401. No timing-side-channel or atomic check-to-network-send claim.

Browser links: lesson → works; problem → works; work → statement. Attempt selector initially shows latest, selecting an older attempt displays its own review. New requests on navigation, refresh and persisted pageshow; errors clear old private content. No automatic retry on 401/403 and no mutation controls. PhotoCount is only the number of historical references, **not** evidence that originals exist, are verified, or are readable. UI says photographs are unavailable; it creates no image/object/storage URL.

## File blocker — exact dependent path stopped

**STORAGE-READ-01 (local report identifier): BLOCKED.** NR1 N3 requires a provably immutable exact version with confirmed durability/hash/length and no retained/issued writer before eligibility. N5 forbids serving pending/unverified files. `StoredAttachment` has StorageKey/Size and parent/deleted fields, but no sealed exact-version/hash/durability proof. `AttachmentScope` confirms org/course/context/parent scope and quarantine, not physical sealing. `LocalFileAttachmentStorage` uses a writable local namespace and rename; it does not implement the N3 writer exclusion/recovery contract. `ComponentAuthorization` explicitly excludes Attachment from its previously accepted A1 scope. The present owner authorization permits a new protected path, but does not supply the missing storage evidence or waive N3/N5.

Consequently no file/metadata HTTP adapter is registered, no private storage is opened, no file auth check is weakened, no synthetic byte buffer is substituted as a supposedly accepted storage implementation. Direct attachment content requests remain 404 in this isolated host, including known unverified IDs. The work/history slice is independent and continues. No schema/Ready/seal/recovery implementation was attempted.

Blocked scenarios: original photo order across pages, accessible confirmed original, deleted/missing original behavior at a real file adapter, swapped attachment/submission denial at that adapter, Range/preview reauthorization, restrictions/expiry for new **file** requests, real synthetic A4 zoom/fullscreen. These are **NOT RUN / BLOCKED**, not covered by work tests or old frontend photo mocks.

Conditions to resume files and subsequently protected photo submission:

1. Accept and implement a scoped storage profile: immutable exact-version identity, hash/length/durability evidence, exclusion of outstanding writers, quarantine/unknown outcome and finite recovery/capacity/restart accounting under N3. Define how existing legacy files remain unverified or are qualified; no silent eligibility conversion.
2. Implement independent JWT + S3 parent (Submission XOR Message) + org/course/group/student/uploader/context/current-processing checks for metadata/original/each supported Range/preview, with deadline and response gates. No public/presigned/static bypass or PG lock during stream.
3. Run real PostgreSQL/HTTP/file negative matrices and real A4 Chrome E2E for both roles, including multi-page order and historical attempts, missing/deleted/quarantined files, foreign IDs, restriction/expiry and boundaries.
4. Protected upload/claim/Submit requires separate authorization and accepted mutation transaction/idempotency/CAS/audit/unknown-outcome protocol; this task does not change legacy writes. Review and annotations remain separate. Deployment clock, G02, legal/security and production cutover gates remain open.

## Test evidence

Execution results are appended below. All data are synthetic. Generated JWTs use the existing anonymous child pipe and in-memory window.lmsAuth contract; none are written to URL, storage, source, evidence or public credentials. Kestrel uses random loopback port with no fixture/control endpoint. Chrome control commands go through the anonymous pipe. Logs redact JWTs; no HAR/trace/video.

HTTP tests independently cover own controls, same-group peer Student denial, unassigned-group Teacher denial, real existing foreign-course/foreign-org works, forged/missing work and version/lesson/problem parents, client role/group/student flags, before/after materialization reader/student/context restrictions, embedded teacher restriction, missing author evidence, lost processing DB access, exact deadline boundaries, lost clock/JWT expiry, concurrent work version change, delayed revocation and historical ownership after group move. SELECT access to all three private attachment tables is revoked and history still succeeds. Existing S3 regression supplies immutable-parent/FK and authority generation controls; no malicious history rewrite is used to build fixtures.

Browser E2E independently executes Student and Teacher with real HTTP/PostgreSQL and Chrome. Checks work/attempt/review navigation, statement/KaTeX, foreign work and parent denials, current restriction committed after default-cache reads, refresh clearing private state, expiry clearing memory token, history refetch, tablet viewport, no cookies/storage/IndexedDB/cache/304, no remote calls or token URLs/logs. It asserts submission rows unchanged after the scenario. Screenshots show synthetic histories and the explicit photo-unavailable state; they are not A4 evidence.

| Screenshot | Student | Teacher |
|---|---|---|
| Latest attempt | [01](evidence/MVP-PROTECTED-WORK-READ-01/Student/01-current-attempt.png) | [01](evidence/MVP-PROTECTED-WORK-READ-01/Teacher/01-current-attempt.png) |
| Old attempt / feedback / blocked photos | [02](evidence/MVP-PROTECTED-WORK-READ-01/Student/02-history-files-blocked.png) | [02](evidence/MVP-PROTECTED-WORK-READ-01/Teacher/02-history-files-blocked.png) |
| Protected statement | [03](evidence/MVP-PROTECTED-WORK-READ-01/Student/03-statement.png) | [03](evidence/MVP-PROTECTED-WORK-READ-01/Teacher/03-statement.png) |
| Restriction | [04](evidence/MVP-PROTECTED-WORK-READ-01/Student/04-restricted.png) | [04](evidence/MVP-PROTECTED-WORK-READ-01/Teacher/04-restricted.png) |
| Tablet history | [05](evidence/MVP-PROTECTED-WORK-READ-01/Student/05-tablet-history.png) | [05](evidence/MVP-PROTECTED-WORK-READ-01/Teacher/05-tablet-history.png) |
| Expiry | [06](evidence/MVP-PROTECTED-WORK-READ-01/Student/06-expired.png) | [06](evidence/MVP-PROTECTED-WORK-READ-01/Teacher/06-expired.png) |

## Guardrails §7 self-review

| Requirement | Disposition |
|---|---|
| Actors, use case, invariants, states | covered — read contract/inventory above |
| Accepted S2/S3/PRS1, blocked gates | covered — shared policy unchanged; no cutover; files stopped |
| Org/course/group/student/parent server checks | covered — S3 plus exact content affiliation, real negative resources |
| Affected subjects / embedded sources | covered — reader/student + every review/message author; missing evidence denies |
| Lists/counts | covered — eligible rows only before output sorting; no unauthorized counts/pagination |
| Attachments and immutable history | history covered — exact submission/review mapping and version checks; attachment delivery BLOCKED |
| Write transaction/locks/CAS/retry/unknown outcome | not applicable — no new domain writes; fixture setup uses existing store/admin components; no replay |
| Validation/limits/safe errors | covered — typed selectors/exact DTO adapters, original deadline/no-store/empty denials; no pagination/scalable queue claim |
| Unit/integration/negative/browser | actual results below; file/A4 cases explicitly BLOCKED |
| Unapproved schema/API/security edits | covered — current owner authorizes only isolated routes; production/schema/legacy/security implementations unchanged |
| Real data/secrets/unsafe logs | covered — synthetic fixtures, memory/anonymous pipe token, redacted diagnostics |
| Exact results/remaining gaps | covered below; attachments flake retained, no full E2E acceptance claim |

## Scope and preservation

New files: Application/ProtectedWorkRead.cs; Infrastructure/PostgresProtectedWorkQueries.cs; Api/ProtectedWorkHttp.cs; Web api/contracts/protectedWork.ts, features/protected-work/ProtectedWorkPage.tsx and protectedWork.css, test/protectedWork.test.tsx; Api.Tests/ProtectedWorkHttpTests.cs and ProtectedWorkBrowserTests.cs; tools/browser_read/work.mjs; this report and its evidence.

Narrow existing-file changes: tests/Lms.Api.Tests/ProtectedCoursesHttpTests.cs (optional isolated DI/route registration); src/Lms.Web/src/App.tsx (contentReads-only routing); src/Lms.Web/src/features/content/ContentPage.tsx (work navigation links). These files already contained prior work; that work is retained. Existing checkpoints, attachments.test.tsx, legacy protocol, Program.cs, ComponentAuthorization and storage implementations are untouched.

Canonical directory `/home/tea/github/lms`; pre-existing dirty branch `codex/l1-s4-jwt-api-authorization`, HEAD `492e30aef65bbddd24f107903bfaa54a563abe39`. No switch to main, worktree, commit, push, merge, rebase, deploy or cutover. Preservation is checked against SHA-256 baseline; .obsidian and ignored build output excluded. Read/write/test commands used sandbox escalation because the default sandbox fails before execution with `mountinfo path is not absolute`; no automatic approval rejection occurred.

**WORK/HISTORY ISOLATED IMPLEMENTED — OWNER ACCEPTANCE PENDING · PHOTO PATH BLOCKED · CLOCK-DEPLOY-01 OPEN · G02 BLOCKED · H04/READY BLOCKED · PRS1 EXACT OFF · S4 HTTP CUTOVER NOT AUTHORIZED.**

## Execution results and exact commands

Run from the canonical repository. Node/pnpm PATH:

```sh
export PATH=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin:$PATH
/tmp/b07c2g/sdk/dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~Http_work_' --logger trx --results-directory /tmp/protected-work-read/targeted-final2
pnpm --dir src/Lms.Web exec vite build --config vite.browser-read.config.ts
LMS_BROWSER_READ=1 LMS_BROWSER_NODE=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node LMS_PLAYWRIGHT_MODULE=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright/index.mjs LMS_BROWSER_EXECUTABLE=/opt/google/chrome/chrome /tmp/b07c2g/sdk/dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~Browser_work' --logger trx --results-directory /tmp/protected-work-read/browser-final
/tmp/b07c2g/sdk/dotnet test Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/protected-work-read/regression
pnpm --dir src/Lms.Web test
pnpm --dir src/Lms.Web test
pnpm --dir src/Lms.Web test
pnpm --dir src/Lms.Web test --no-file-parallelism
pnpm --dir src/Lms.Web build
pnpm --dir src/Lms.Web exec vitest run src/test/protectedWork.test.tsx
```

- Targeted PostgreSQL/HTTP: **42/42 PASS**. [Log](evidence/MVP-PROTECTED-WORK-READ-01/targeted-final2.log).
- Chrome **154.0.8037.57** final run: **2/2 PASS**, Student and Teacher, zero skipped. [Log](evidence/MVP-PROTECTED-WORK-READ-01/browser-final.log), [Student results](evidence/MVP-PROTECTED-WORK-READ-01/Student/results.json), [Teacher results](evidence/MVP-PROTECTED-WORK-READ-01/Teacher/results.json). Work/history evidence only; blocked photo scenarios listed above. Final tablet/history screenshots were visually inspected; layout uses 44px attempt control.
- Backend regression: **987 PASS / 0 FAIL / 2 SKIP** (Domain 43, Application 340, Infrastructure 258, API 346 passed). The two skipped items are opt-in browser theory definitions without LMS_BROWSER_READ=1; the new work theory was run separately for both roles with **2/2 PASS**. The previous BROWSER-READ theory was not re-run in this task. [Regression log](evidence/MVP-PROTECTED-WORK-READ-01/regression.log), [TRX / test summary](evidence/MVP-PROTECTED-WORK-READ-01/test-summary.json). Targeted cases overlap regression; totals are not added.
- Frontend parallel repetition 1: **403/403 PASS**; repetition 2: **403/403 PASS**; repetition 3: **402 PASS / 1 FAIL**. Failure reproduces the pre-existing `attachments.test.tsx:35`, `selects one image and returns only confirmed attachment IDs`: callback array entry still undefined when assertion reads `attachmentId`. The test file is byte-for-byte unchanged. **Flakiness reproduced, NOT FIXED.** [Run 1](evidence/MVP-PROTECTED-WORK-READ-01/frontend-1.log), [run 2](evidence/MVP-PROTECTED-WORK-READ-01/frontend-2.log), [run 3 failure](evidence/MVP-PROTECTED-WORK-READ-01/frontend-3.log).
- Frontend serial regression: **403/403 PASS**, 15 files. [Log](evidence/MVP-PROTECTED-WORK-READ-01/frontend-serial.log). This does not erase the parallel failure. Final protected-work tests after CSS adjustment: **5/5 PASS**. [Log](evidence/MVP-PROTECTED-WORK-READ-01/frontend-work-final.log).
- TypeScript/production Vite build **PASS**; final isolated browser Vite build **PASS**, with bundle-size advisory. [Build](evidence/MVP-PROTECTED-WORK-READ-01/build-final.log), [browser build](evidence/MVP-PROTECTED-WORK-READ-01/browser-build-final.log). Production activation remains off.
- Initial development failures retained: missing fixture AccessAttachments FK rows; then 28/30 passing with two malformed negative-test URLs; nullable organization fixture compile error; initial Teacher browser child did not exit because stdin remained open after successful UI checks and was terminated. Fixed fixture/test/runner faults without weakening DB triggers, policy or runtime authorization. Final results above supersede these attempts, not their evidence. [Initial HTTP](evidence/MVP-PROTECTED-WORK-READ-01/targeted.log), [URL case](evidence/MVP-PROTECTED-WORK-READ-01/targeted2.log), [compile](evidence/MVP-PROTECTED-WORK-READ-01/targeted-final.log), [initial browser](evidence/MVP-PROTECTED-WORK-READ-01/browser.log).
- New/changed text whitespace and report local links **PASS**. Whole dirty-tree `git diff --check` retains the pre-existing `docs/content/COURSE-CONTENT-SPEC.md:1325: new blank line at EOF`; unrelated file unchanged.
- **698/701 baseline files unchanged**, with exactly three intended existing-file additions listed above. Reversing only those additions in memory reproduces the baseline hashes of all three files, confirming preservation of pre-existing edits. Branch/HEAD/index unchanged; no unexpected baseline changes. [Preservation](evidence/MVP-PROTECTED-WORK-READ-01/preservation.json).
