# MVP photo read — isolated synthetic implementation

2026-10-12. **PHOTO READ IMPLEMENTED IN SYNTHETIC SCOPE · OWNER ACCEPTANCE PENDING · G02 PRODUCTION BLOCKED.** Authorization: explicit owner decision accepting physical-original tampering risk, removing fs-verity from MVP and cancelling STORAGE-PROFILE-02. [Exact normative diff and scope](MVP-STORAGE-RISK-ACCEPTANCE.md). This is a private photo-read slice, not production cutover or complete upload/review/acceptance HTTP integration.

## Contract and implementation

Student → own work/attempt/pages; assigned Teacher → assigned work/attempt/pages. Invariants: S2 JWT, S3 org/course/group/student/teacher and immutable parent chain, current conservative ADR-L18 restrictions, original application deadline, independent authorization for every manifest/original request. Entities: existing StoredAttachment, AttachmentScope, Submission, ProblemWork and assignment scope. No domain transitions on reads. UI states: loading → page/zoom; new page/attempt → new authorized fetch; denial/unavailable → no image. Historical attempt IDs and attachment order remain unchanged.

| Data/path | Server authority and behavior |
|---|---|
| Work + Submission | Existing ProtectedWorkRead checks exact course/version/lesson/work, independent S3 Work/Submission checks, historical authors and current processing before materialization; captured WorkVersion is rechecked before return. Course access alone is insufficient. |
| Attachment manifest | Existing S3 ResolveResource + ScopePolicy, non-pending parent, exact requested submission/work, student/uploader identity, same-org synthetic context, current processing; no unqualified legacy IDs or file paths. Original ordered AttachmentIds, maximum 10. |
| Each original | All parent and attachment checks again before opening storage; Size 1–10 MiB, PNG/JPEG validation, full bounded bytes in private memory. Fresh parent/processing/eligibility + JWT trust/lifetime + ResponseEnqueue deadline before response. |
| Storage | Existing LocalFileAttachmentStorage: unique GUID keys, private staging, completed write/flush/close then rename without overwrite. Unix new directories 0700, files 0600. Synthetic root is a fresh random directory under /tmp, outside static web root; fixture owns/disposes it. No kernel seal or FS qualification. |
| Failure | Incomplete Store never publishes metadata; failed stage is cleaned, existing originals never overwritten. AttachmentService now retains original after any metadata-save failure: SELECT-not-found is not proof against a late commit. No automatic replay or deletion based on that assumption. Explicit reconciliation remains required. Missing/truncated/structurally corrupt original → empty 503; denied scope → empty 403; absent/invalid JWT → 401. |
| Resources | Maximum 10 photos per manifest, 10 MiB per original, four concurrent photo requests, reject excess with 503 and no queue. Only one current browser image, old blob URL revoked on page/attempt change, refresh and unmount. No DB transaction/lock or filesystem handle is retained during response delivery. These are read bounds, not a completed durable upload capacity/recovery manager. |

The new opt-in routes are:

```text
GET /api/v1/courses/{course}/versions/{version}/lessons/{lesson}/works/{work}/submissions/{submission}/photos
GET /api/v1/courses/{course}/versions/{version}/lessons/{lesson}/works/{work}/submissions/{submission}/photos/{attachment}
```

Production Program.cs and legacy controller registration are unchanged. Test HttpHost explicitly registers photo storage and routes. ComponentAuthorization's global A1 attachment exclusion remains unchanged; the new adapter composes accepted parent authorization and existing S3 attachment policy. The legacy direct `/api/v1/attachments/{id}/content` route remains 404 in this isolated host. Range is ignored and returns the same full bounded 200 response after all gates; partial ranges/derived thumbnails are not implemented. No public/static/presigned URLs. Browser fetch carries Authorization; `<img>` receives only a transient in-memory blob URL, never a token or public file URL. Responses are no-store/nosniff; no token persistence, automatic retry or refresh flow was introduced.

The work DTO reports `photoAvailability: protected` only in the explicit photo composition. The previous history-only composition still reports unavailable and its existing tests remain valid. Message attachments, annotation persistence and new review/submit HTTP commands are outside this slice.

## Write boundaries

Synthetic seed runs sequentially against a disposable PostgreSQL database and real private files: AttachmentService.Upload (format/size validated; Store before metadata SaveChanges), trusted AttachmentScope creation, then existing PostgresWorkStore.Save claims attachments with immutable submission history in its transaction. PostgresWorkStore owns the READ COMMITTED transaction: new work INSERT or version-CAS row UPDATE, immutable history and stable-ID domain-event journal, attachment row locks in sorted UUID order via the existing claim trigger, then SaveChanges/Commit. Storage I/O finishes before that transaction. Fixture failures propagate without retry. No changes to that claim/CAS/locks/audit protocol. Two pages belong to attempt 1, a distinct page belongs to attempt 2. No rewriting of historical arrays or test bypass of DB triggers.

No new user-facing domain write is registered. NR1 N2/N3 durable command registration before side effects, finite pending accounting, serialization/reconciliation and authoritative unknown-outcome handling have **not** been waived or proven for legacy upload/Submit. Their full protected HTTP integration remains an implementation dependency. The small original-retention correction prevents unsafe cleanup, but is not a claim that the full recovery protocol now exists. Backup/recovery remains the separate ADR-L05 operational requirement; no restore drill or production RPO/RTO acceptance is claimed.

## Verification

Exact commands below run in `/home/tea/github/lms`; logs and TRX summaries are retained under [evidence](evidence/MVP-PHOTO-READ).

```sh
/tmp/b07c2g/sdk/dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~Http_photo_'
export PATH=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin:$PATH
pnpm --dir src/Lms.Web exec vite build --config vite.browser-read.config.ts
LMS_BROWSER_READ=1 LMS_BROWSER_NODE=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node LMS_PLAYWRIGHT_MODULE=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright/index.mjs LMS_BROWSER_EXECUTABLE=/opt/google/chrome/chrome /tmp/b07c2g/sdk/dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~Browser_photo_' --logger trx --results-directory /tmp/mvp-photo/browser-final
/tmp/b07c2g/sdk/dotnet test Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/mvp-photo/regression
pnpm --dir src/Lms.Web exec vitest run src/test/protectedPhotos.test.tsx src/test/protectedWork.test.tsx
pnpm --dir src/Lms.Web test --no-file-parallelism
pnpm --dir src/Lms.Web build
```

- Initial photo HTTP matrix **13/13 PASS**. Final expanded matrix and compensation tests: results appended below.
- Chrome **154.0.8037.57**, actual Chrome binary + Playwright, real Kestrel/PostgreSQL/private filesystem: **2/2 PASS**, Student and Teacher, zero skipped. [Log](evidence/MVP-PHOTO-READ/browser-final.log), [Student](evidence/MVP-PHOTO-READ/Student/results.json), [Teacher](evidence/MVP-PHOTO-READ/Teacher/results.json). No request mocks, no HAR/trace/video. JWTs travel via anonymous child pipe and in-memory auth, with diagnostic redaction. Submission rows are asserted unchanged after each scenario.
- Frontend final serial regression **408/408 PASS**, 16 files; protected photo/work tests **10/10 PASS**. TypeScript/production Vite and isolated browser build **PASS** (browser bundle-size advisory).
- Backend full regression: Domain **43 PASS**, Application **340 PASS**, Infrastructure **284 PASS** (includes private-stage/permissions/no-overwrite). API portion was interrupted after approximately ten minutes without a final result; **INCOMPLETE, not PASS**. The initial container check used a different Docker CLI context, so no claim of a proven hang is made. Final focused API verification is recorded below. Broader tests are not new acceptance of historical storage qualification or production.
- Development failures retained: missing TestHost import; platform analyzer rejecting unconditional UnixCreateMode setter; first Chrome run found stale page image briefly labelled with the new page number (fixed by binding rendered URL to selected href); one unit test used a cross-realm Node Blob instead of browser Blob (fixed test response). [Initial HTTP](evidence/MVP-PHOTO-READ/http-initial.log), [platform build](evidence/MVP-PHOTO-READ/browser.log), [Chrome failure](evidence/MVP-PHOTO-READ/browser2.log), [initial frontend](evidence/MVP-PHOTO-READ/frontend.log). Failures are not erased or labelled PASS.

### Browser evidence

A4 synthetic PNG originals: 1240×1754, three distinct labelled sheets, no real data. Screenshots of history/page 1 and teacher page 2 at 200% were visually inspected; viewport scroll preserves the full A4 original.

| Scenario | Student | Teacher |
|---|---|---|
| Current attempt, distinct photo | [image](evidence/MVP-PHOTO-READ/Student/01-current-attempt.png) | [image](evidence/MVP-PHOTO-READ/Teacher/01-current-attempt.png) |
| Older attempt / first A4 page | [image](evidence/MVP-PHOTO-READ/Student/02-history-A4-page1.png) | [image](evidence/MVP-PHOTO-READ/Teacher/02-history-A4-page1.png) |
| Second page / 200% zoom | [image](evidence/MVP-PHOTO-READ/Student/03-A4-page2-zoom.png) | [image](evidence/MVP-PHOTO-READ/Teacher/03-A4-page2-zoom.png) |
| Corrupt original | [image](evidence/MVP-PHOTO-READ/Student/04-corrupt.png) | [image](evidence/MVP-PHOTO-READ/Teacher/04-corrupt.png) |
| Missing original | [image](evidence/MVP-PHOTO-READ/Student/05-missing.png) | [image](evidence/MVP-PHOTO-READ/Teacher/05-missing.png) |
| Current restriction | [image](evidence/MVP-PHOTO-READ/Student/04-restricted.png) | [image](evidence/MVP-PHOTO-READ/Teacher/04-restricted.png) |
| Tablet viewport | [image](evidence/MVP-PHOTO-READ/Student/05-tablet-history.png) | [image](evidence/MVP-PHOTO-READ/Teacher/05-tablet-history.png) |
| Expired JWT | [image](evidence/MVP-PHOTO-READ/Student/06-expired.png) | [image](evidence/MVP-PHOTO-READ/Teacher/06-expired.png) |

Browser negative assertions cover foreign attachment ID, swapped work/submission/course, anonymous file request, direct legacy URL, restriction and expiry on both manifest and original, missing and same-length corrupt files, blob revocation and no cookies/localStorage/sessionStorage/IndexedDB/cache/304/remote calls/token URLs. Tablet is emulation, not physical-device evidence.

## Guardrails §7 and security review

| Requirement | Disposition and evidence |
|---|---|
| Actor/use case/invariants/states | covered — contract above, browser Student/Teacher |
| Accepted S2/S3/PRS1, closed gates | covered — existing JWT + S3 policy + current conservative gate reused; exact PRS1 OFF, production registration unchanged |
| Org/course/group/student/teacher/parent | covered — HTTP own controls and wrong group/course/org/attachment/work/submission/version/lesson, missing/malformed IDs; same-group peer and moved owner final matrix |
| Affected subjects/evidence | covered — accepted work gates include history authors; attachment uploader equals submission student; missing scope/context/processing access denies |
| Counts/list leakage | covered — manifest checks all returned IDs before emitting order/count; inaccessible manifest denies as a whole; existing work-list pre-materialization tests retained; pagination N/A, none added |
| Attachments/history | covered — confirmed storage write before metadata, immutable claim chain and independent photo gates; pending/unqualified IDs denied; two attempts retain separate pages |
| Writes/locks/CAS/retry/unknown | no new runtime command; synthetic seed uses existing store. Original retention after uncertain metadata save corrected, no replay. Full NR1 upload protocol NOT PROVEN |
| Validation/limits/errors | covered — format/size/page/concurrency bounds; empty 401/403/503, no-store; interrupted staging, missing/corrupt files and deadline tests |
| Tests | exact results/commands above and below; production restore/clock qualification and physical-device testing NOT RUN |
| Authorized scope and unrelated files | covered — scoped docs/adapter/UI/tests only, no migrations/DI production/cutover; preservation evidence below |
| Real data/secrets/readiness | covered — disposable synthetic fixtures, in-memory JWT/pipe, no public file paths or token logs. No pilot readiness claim |
| Remaining limitations | G02 and deployment clock still blocked/open; operational backup/recovery separate; no user upload/Submit/review HTTP activation. Physical modification to a different valid same-length image can pass format checks: explicitly accepted tampering risk, not a hash/seal guarantee |

## Changed paths and preservation

New: Application/ProtectedPhotoRead.cs; Infrastructure/PostgresProtectedPhotoQueries.cs; Api/ProtectedPhotoHttp.cs; Web/features/protected-work/ProtectedPhotos.tsx and test/protectedPhotos.test.tsx; API photo HTTP/browser tests; Infrastructure/PrivatePhotoStorageTests.cs; tools/browser_read/photos.mjs; three tests/fixtures/photos originals; both requested reports and new evidence.

Narrow existing-file edits: AttachmentService (retain unknown original), LocalFileAttachmentStorage (private creation permissions), PostgresProtectedWorkQueries and protectedWork DTO/UI (explicit protected-photo mode), protectedWork.css, isolated test HttpHost registration, AttachmentConsistencyTests (conservative retention expectation); NR1/ADR-L15/L18/bounded-execution notices and append-only owner register. Production Program.cs, global A1/S3 policy, DB schema and historical evidence unchanged.

Canonical directory `/home/tea/github/lms`; existing dirty branch `codex/l1-s4-jwt-api-authorization`, HEAD `492e30aef65bbddd24f107903bfaa54a563abe39`. No branch switch, commit, push, merge, rebase, worktree, deploy or production cutover. Baseline snapshots/hashes include tracked and untracked source/docs/evidence; `.obsidian` and ignored build products excluded. [Preservation manifest](evidence/MVP-PHOTO-READ/preservation.json) and [task-only exact diff](evidence/MVP-PHOTO-READ/changes.diff) compare against the dirty starting state, not HEAD. Shell/tool reads/writes required escalation because the sandbox fails before execution with `mountinfo path is not absolute`; no approval rejection occurred.

**UI PHOTO READ IMPLEMENTED · CHROME SYNTHETIC SCENARIOS PASS · OWNER ACCEPTANCE PENDING · FULL SYNTHETIC MVP NOT CLAIMED · PILOT NOT AUTHORIZED.**


## Final execution and preservation

- Final targeted API **65/65 PASS**, zero skipped: **16 photo**, **42 protected-work**, **7 attachment consistency**. Covers authorization before storage opens, same-group peer denial, historical ownership after group move, pending/unqualified references, concurrency saturation/release, file faults, before/after-read restrictions, JWT/deadline/clock failures, and before/after metadata-save exceptions retaining the original. [Log](evidence/MVP-PHOTO-READ/final-targeted.log), [TRX counters](evidence/MVP-PHOTO-READ/test-summary.json).
- Final Chrome after the retention correction **2/2 PASS**, both roles, zero skipped. The earlier successful 2/2 run is overlapping evidence, not additional scenarios.
- Backend broad run completed **43 Domain + 340 Application + 284 Infrastructure PASS**. API portion **INCOMPLETE / interrupted**, so no full-solution PASS is claimed. Focused final results above validate affected API paths on final code. [Broad log](evidence/MVP-PHOTO-READ/regression.log).
- Frontend final **408/408 PASS**; final TypeScript/Vite build **PASS**. [Frontend](evidence/MVP-PHOTO-READ/frontend-final.log), [build](evidence/MVP-PHOTO-READ/build-final.log), [isolated build](evidence/MVP-PHOTO-READ/browser-build-final.log).
- Preservation: **772/785 baseline files unchanged**; exactly 13 intended existing files changed. Four normative notices reverse to byte-identical original bodies; original owner-register prefix preserved. Branch, HEAD and index unchanged. New files/evidence and hashes are enumerated in preservation.json; task-only diff includes textual new/changed files, with PNG originals separately listed in the manifest.

Additional final command:

```sh
/tmp/b07c2g/sdk/dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~Http_photo_|FullyQualifiedName~AttachmentConsistencyTests|FullyQualifiedName~Http_work_' --logger 'console;verbosity=normal' --logger trx --results-directory /tmp/mvp-photo/final-targeted
python3 /tmp/mvp-photo/finalize.py
```
