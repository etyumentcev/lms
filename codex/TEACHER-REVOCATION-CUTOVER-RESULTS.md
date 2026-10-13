# Teacher Revocation HTTP + React cutover — results

Date: 2026-10-12. Repository: `/home/tea/github/lms`. Existing branch, HEAD, index and unrelated dirty files were retained. No commit, push, deploy or production migration was performed.

## Result and boundary

The enabled Teacher Revocation host now connects the protected HTTP composition to the real `Program.cs`. Teacher review, message, attachment upload/delete/read calls in React use the trusted Course bearer, deadline, current authority, processing, Origin and CSRF boundaries. Competing controller review/file actions are no longer MVC actions; the remaining shared message/content URLs select the protected teacher branch and retain the student branch. Legacy teacher upload/delete routes remain fail-closed.

This is **not production ready** and is not full M1 acceptance:

- the composition is opt-in through `TeacherRevocation:Enabled` and requires an exact HTTPS origin, qualified clock and PEM signing key configuration;
- the ordinary React application still supplies authentication through the `window.lmsAuth` host adapter; browser login/refresh/logout UI/session coordination is not implemented;
- the production-like runtime PostgreSQL role cannot yet perform the login audit write; the positive existing-`Program` test therefore uses the owner fixture, while the runtime-role denial remains a regression;
- the browser journey uses pre-seeded course/group/assignment data and pre-issued scoped access tokens;
- deployment clock/DB/storage/edge qualification, cleanup/capacity qualification and operational rollout were not performed.

The 15/30/60-minute model is unchanged. No legacy bypass and no fs-verity requirement were reintroduced.

## HTTP route matrix

| Method and URL | Caller and contract after cutover | Trusted enforcement | Legacy/conflict disposition | Evidence |
|---|---|---|---|---|
| `GET /api/v1/auth/csrf` | anonymous or validated Course bearer; returns anti-CSRF request token | HTTPS, request deadline; bearer is validated when present; no-store | one protected minimal route | `TeacherRevocationHttpTests`, full API |
| `POST /api/v1/auth/login` | credentials + `organizationId` | exact Origin, CSRF, HTTPS, deadline, S2/S3 authority, Secure/HttpOnly refresh cookie | new route; no legacy alias | `Teacher_http_login_review_replay_refresh_revoke_and_cookie_security` |
| `POST /api/v1/auth/refresh` | refresh cookie + `organizationId` | rotation, current authority, exact Origin, CSRF, HTTPS and deadline; replay is 409 | new route; no automatic replay | same test plus bounded refresh tests |
| `POST /api/v1/auth/switch-organization` | refresh cookie + selected organization | same fresh-authority rotation as refresh | new route | same test |
| logout | not registered | — | missing; blocks M1-01 | M1 matrix below |
| `POST /api/v1/problem-works/{workId}/reviews/needs-revision` | Course bearer; `commandId`, `submissionId`, `expectedVersion`, feedback, annotations | current org/course/group/processing/parent; durable versioned intent; Origin/CSRF/deadline | old controller action is `[NonAction]` | full API; React E2E |
| `POST /api/v1/problem-works/{workId}/reviews/accept` | same versioned protected protocol | same; stale/replay conflict is 409 | old controller action is `[NonAction]` | full API; React E2E |
| `POST /api/v1/problem-works/{workId}/messages` | teacher: Course bearer + `commandId` + `intentId`; student: existing student body | teacher branch requires validated scoped bearer and durable unversioned intent; student branch retains normal authorization | same URL, role-selected; teacher cannot fall through to legacy service | full API; React E2E |
| `POST /api/v1/problem-works/{workId}/uploads/{intentId}` | teacher raw JPEG/PNG; command/name/hash headers | current scope/parent/processing, durable unversioned intent, Origin/CSRF/deadline, size/hash validation | only teacher upload route | full API; React E2E |
| `DELETE /api/v1/problem-works/{workId}/attachments/{id}` | teacher pending attachment + command header | current work/parent scope, durable command, Origin/CSRF/deadline | only teacher delete route | full API and frontend tests |
| `GET /api/v1/attachments/{id}/content` | teacher Course bearer or authorized student | teacher bytes use current protected query; student branch retains owner read; private/no-store | old controller content action is `[NonAction]`; one hybrid route | full API; React E2E |
| `POST /api/v1/uploads` | student only | existing student attachment authorization | teacher is denied; cannot bypass protected work-scoped upload | `MandatoryTeacherContextTests` |
| `DELETE /api/v1/attachments/{id}` | student pending attachment only | existing student owner/parent rules | teacher is denied; cannot bypass protected work-scoped delete | `MandatoryTeacherContextTests` |
| `POST /api/v1/problem-assignments/{id}/submissions` | student | existing student workflow | intentionally retained | full API; React E2E |

When the cutover feature is disabled, removed legacy review/content actions do not return; teacher writes fail closed. When enabled, `CourseRequestIngress` is before authentication and every protected mutation checks one captured deadline again before response publication.

## React call migration matrix

| UI operation | Old call/assumption | Current call | Identity and uncertain-result behavior | Evidence |
|---|---|---|---|---|
| NeedsRevision | legacy review body | same URL, protected body with `commandId/submissionId/expectedVersion/feedback/annotations` | one stable command ID per mounted logical decision; 401/403/404/409/unknown blocks mutation and requires authoritative GET | `teacherReview.test.tsx`; React E2E |
| Accept | legacy review body | same protected review URL/body | same; pending state blocks double click and the other decision | frontend test `blocks double click...`; React E2E |
| Teacher message | legacy `bodyMarkdown/attachmentIds` | same URL with `commandId/intentId/body/replyTo/attachments` | stable command and intent for the draft; 409/network/503/unknown never creates an automatic new intent | frontend tests; React E2E |
| Teacher photo upload | shared multipart `/uploads` | raw `/problem-works/{workId}/uploads/{intentId}` with command, encoded name and SHA-256 headers | upload operation object is created once per selected file and reused by explicit retry; unknown outcome disables retry | frontend tests; React E2E |
| Teacher pending-photo delete | shared `/attachments/{id}` | work-scoped protected DELETE with stable delete command | item stays visible on failure; no automatic new command | frontend/API regression |
| Teacher attachment read | unscoped/shared assumption | protected `/attachments/{id}/content` with Course bearer | blob fetched no-store; revoked/foreign parent denied before bytes | full API; React E2E |
| Student upload/read/delete | existing student routes | unchanged | student functionality preserved | full API and React E2E |

The shared API client has no mutation retry and no hidden refresh loop. It binds CSRF tokens to the current bearer, sends same-origin credentials, rejects redirects/cross-origin paths, supports Blob/File/XHR upload, invokes the host callback on 401, and leaves session recovery to the host.

## Disposition of the 34 original API failures

Categories: **obsolete** = positive setup/call still used a deliberately forbidden legacy teacher route; **negative expectation** = fail-closed was correct but the old test expected a downstream 404 rather than the authorization-boundary 403; **implementation defect** = the protected composition/result/provenance path needed a code fix; **fixture defect** = the test manufactured authority or runtime capabilities that do not match the supported composition. No test was skipped and no negative assertion was weakened to reopen a legacy path.

| # | Original failure (short name) | Classification | Resolution / retained assertion |
|---:|---|---|---|
| 1 | `Concurrent_messages_return_one_conflict...` | implementation defect + obsolete caller | protected teacher message with stable intent; `OperationAlreadyAttempted` maps to 409; one winner/no partial history retained |
| 2 | `Message_capability... Student, Accepted` | obsolete setup | terminal state is now created through protected teacher Accept; student message positive remains |
| 3 | `Message_capability... Student, NeedsRevision` | obsolete setup | state setup moved to protected NeedsRevision; student message positive remains |
| 4 | `Teacher_capability... needs-revision, matching-group` | fixture defect + obsolete caller | current scoped grant/context seeded through authority APIs; protected review is the positive control |
| 5 | `Submitted_content... matching=False` | negative expectation | unauthorized legacy teacher read remains 403; protected foreign-parent/read negatives retained |
| 6 | `Message_capability... Teacher, Accepted` | obsolete setup | Accept and teacher message use scoped client/protected commands |
| 7 | `Successful_submission_claims_all_pages...` | implementation defect | current-composition submission claim records immutable attachment scope/provenance; historic/direct legacy store stays fail-closed |
| 8 | `Full_flow_preserves_history...` | obsolete caller | review/message migrated to protected client; history/status/attempt-count assertions unchanged |
| 9 | `Every_command... missing needs-revision` | negative expectation | untrusted legacy route is denied before resource lookup; protected-route validation/missing-resource tests provide the positive boundary |
| 10 | `Submitted_content... matching=True` | implementation defect | protected teacher file read plus current submission attachment-scope promotion |
| 11 | `Teacher_review... course grant allowed` | obsolete caller | matching course/group authority now exercised with scoped login and protected routes |
| 12 | `Message_capability... Teacher, NeedsRevision` | obsolete setup | protected setup/message and fresh GET |
| 13 | `Navigation_teacher_capability... terminal` | obsolete setup | terminal state created by protected Accept; current read policy rechecks active scope |
| 14 | `Concurrent_reviews_have_one_winner...` | implementation defect + obsolete caller | both requests use protected versioned protocol; one success/one 409 and no partial writes |
| 15 | `Message_capability... Teacher, Submitted` | obsolete caller | protected message contract with durable unversioned intent |
| 16 | `Teacher_group_scope_and_message_history...` | negative expectation | legacy no-context read stays 403; protected matching/foreign checks retained |
| 17 | `Every_command... missing accept` | negative expectation | legacy request denied before lookup; protected missing-resource test retained |
| 18 | `Complete_HTTP_workflow...` | obsolete caller | all teacher mutations moved to scoped login/protected commands; workflow assertions unchanged |
| 19 | `Annotation_commands... accept` | obsolete caller | protected versioned review preserves annotation atomicity and expected 409 |
| 20 | `Teacher_capability... accept, matching-group` | fixture defect + obsolete caller | official scoped authority setup and protected Accept |
| 21 | `Revision_requires_feedback("   ")` | obsolete caller | protected route reaches validation and returns the validation result; legacy remains closed |
| 22 | `Teacher_review... matching groups allowed` | obsolete caller | scoped matching-group positive moved to protected route |
| 23 | `Revision_requires_feedback("")` | obsolete caller | same protected validation migration |
| 24 | `Photo_revision_journey...` | implementation defect + obsolete caller | protected review/read plus claimed attachment provenance; original/history/progress assertions retained |
| 25 | `Existing_application... runtime_login` | implementation/deployment-profile defect, still operationally open | positive `Program` journey uses owner-profile disposable DB; separate runtime-role test still proves login audit INSERT denial and blocks production profile acceptance |
| 26 | `Revision_requires_feedback(null)` | obsolete caller | same protected validation migration |
| 27 | `Foreign_and_unknown_resources...` | negative expectation | legacy teacher request stays 403 without trusted context; protected foreign/unknown/parent denials remain indistinguishable within the protected boundary |
| 28 | `Queue_search_and_attempt_filters...` | obsolete setup | review state created through protected command; queue filter/pagination assertions unchanged |
| 29 | `Teacher_screen... accepted` | obsolete setup | protected Accept plus fresh current-scope teacher read |
| 30 | `Teacher_capability... needs-revision, unavailable` | fixture defect | current processing/authority state built through official adapters; unavailable case remains denied |
| 31 | `Annotation_commands... needs-revision` | obsolete caller | protected review preserves atomic annotations and expected 409 |
| 32 | `Teacher_owned_message_original_rechecks_revoked_grant` | obsolete contract + implementation defect | teacher attachment now uses work-scoped protected upload/read; fresh revoke still denies original |
| 33 | `Progress_counts_only_accepted...` | obsolete setup | review transitions use protected commands; assignment/progress/withdrawal assertions unchanged |
| 34 | `Teacher_capability... accept, withdrawn` | fixture defect | withdrawn/current authority is created through supported administration; protected Accept remains denied |

All 34 named cases are contained in the final zero-failure API run. The original JSON is retained unchanged as baseline evidence.

## Real React UI evidence

The opt-in `ReactTeacherJourneyTests` uses the production React bundle in real headless Chromium over HTTPS, production controllers/protected teacher routes, disposable PostgreSQL and private local file storage. The browser itself selects valid synthetic PNG file bytes with file inputs (not a mocked Blob/short-circuit), draws a canvas stroke, submits forms and navigates between student and teacher screens.

Browser-confirmed sequence:

1. student uploads one valid synthetic PNG file and submits;
2. teacher opens protected work/photo, uploads a PNG remark and posts a message;
3. teacher draws one annotation stroke and sends NeedsRevision;
4. student sees the result, uploads one revised PNG and resubmits;
5. teacher accepts; the Accept action disappears and student sees Accepted;
6. the teacher grant is revoked and the same UI receives 403;
7. an independently scoped teacher from a second organization receives 403.

Database postconditions: Accepted; two submissions; two reviews; three messages; one persisted annotation containing one stroke; three attachment metadata rows; teacher grant Revoked.

Important limits:

- authentication is **not** performed through a login screen: three access tokens are issued by the fixture and passed through the documented `window.lmsAuth` adapter;
- course, group, enrollment, teacher grant, published problem and assignment/work are pre-seeded; no user creates or assigns them in the browser;
- each submission has one valid PNG fixture, not a camera photograph and not the required two-page upload/reorder/reload proof for M1-08;
- one mouse-emulated canvas stroke is persisted on one page; annotations on both pages, coordinate alignment across zoom/orientation, pen/touch/fullscreen and physical handwriting were not proven;
- a repeated Accept button is absent after success, and frontend tests prove double-click produces one POST. Durable same-intent replay/lost-ACK is covered by API/Infrastructure harnesses, not by the browser journey;
- teacher pending-attachment deletion is covered by React component + protected API tests, not clicked in this browser journey;
- `failure.json` is an intentionally retained intermediate runner-debug artifact from a failed selector/setup iteration; `result.json` and the later green test run are the final outcome. It is not hidden or counted as a final product failure.

Evidence:

- `codex/evidence/TEACHER-REVOCATION-REACT-E2E/result.json`
- `codex/evidence/TEACHER-REVOCATION-REACT-E2E/react-teacher-journey.png`
- `tests/Lms.Api.Tests/ReactTeacherJourneyTests.cs`
- `tools/browser_read/react-teacher-journey.mjs`

## Regression

Final-source results are recorded in `codex/evidence/TEACHER-REVOCATION-CUTOVER/test-summary.json`. The full API run was intentionally repeated after rebuilding the narrow historical-schema guard.

| Suite | Final result | Failures | Skips | Notes |
|---|---:|---:|---:|---|
| Domain | 43 PASS | 0 | 0 | full project |
| Application | 369 PASS | 0 | 0 | full project |
| Infrastructure | 334 PASS | 0 | 0 | full project |
| API, browser enabled | 383 PASS | 0 | 0 | final binary, browser opt-in; 15m26s |
| Frontend Vitest | 408 PASS in 16 files | 0 | 0 | full suite |
| Frontend production build | PASS | 0 | n/a | Vite production bundle used by Chromium |
| Content compiler | 74 PASS | 0 | 0 | canonical pinned Node and `NODE_PATH` |
| Content importer | 49 PASS | 0 | 0 | full suite |
| Real React/Chromium journey | 1 PASS | 0 | 0 | also included in browser-enabled full API |

Intermediate failures were not suppressed:

- Infrastructure initially had 332 PASS / 2 FAIL: historical pre-M1 schema had no `AttachmentScopes`, and a direct legacy store unexpectedly promoted provenance. The fix checks schema presence and only promotes in the explicitly composed current path; final 334 PASS.
- the first frontend invocation had an invalid PATH (`node` not found), so it was not counted as a test result; the canonical runtime rerun passed 408.
- two compiler invocations omitted the pinned runtime configuration and produced `E_CONFIG` setup failures (132 then 131 reported subcases); the canonical command passed 74.
- several browser journey construction iterations failed on selector text, a fixture clock preceding the grant, token reset on navigation, ambiguous locators, an open readline handle and nondeterministic collection order. Those harness defects were fixed; the final journey and DB oracles pass. The retained `failure.json` makes this history visible.

No final suite uses skip to hide a failure.

## M1 correspondence

Statuses below are deliberately conservative and use the acceptance text in `Sapientum_M1_Codex_Tasks_All.md`; this work did not start any additional M1 task. Here **BLOCKED means that a mandatory roadmap acceptance path is absent**, not that the implemented cutover regression failed.

| Task | Status | Acceptance criteria actually evidenced | Evidence | Concrete remaining work |
|---|---|---|---|---|
| M1-01 — browser authorization | **BLOCKED** | HTTP login, refresh/switch rotation, cookies, CSRF/Origin, JWT ≤15m and revoke/replay negatives are covered at HTTP/integration level | `TeacherRevocationHttpTests`; bounded refresh/JWT tests | implement ordinary React login/session/logout and org selector; reload + expiry refresh, two tabs, cache/Blob/draft cleanup, outage/ambiguous ACK through browser; add logout route; remove fixture-issued tokens; qualify runtime DB profile |
| M1-02 — protected teacher HTTP | **PARTIAL** | protected review/message/upload/delete/read composition is in `Program.cs`; React teacher callers migrated; 34 failures disposed; current API/frontend suites green; legacy teacher bypasses fail closed; real seeded React journey passes | route/call matrices above; final API/Frontend; React result JSON | M1-01 dependency; enable/qualify real host configuration and runtime role; browser auth; complete operational qualification. Do not call the seeded isolated journey final M1 E2E |
| M1-08 — protected multi-photo solution | **PARTIAL** | valid synthetic PNG bytes traverse React → HTTPS API → DB/private files → protected teacher read without Blob/attachment short-circuit; preview/message image and negative scope checks pass | React result JSON; API attachment tests; frontend 408 | browser upload of at least two representative camera-photo pages in one submission, reorder, reload, exact order/hash, own-original read, cancel/dropped ACK/restart and browser pending-delete; no pre-seeded onboarding dependency for final acceptance |
| M1-09 — Submit→Review→Revise→Accept | **PARTIAL** | the core state cycle, real PNGs, message, one persisted stroke, revoke and cross-org denial pass through real React | React result JSON/screenshot and DB assertions | normal user onboarding without direct seed/SQL, two-page overlays, both-page immutable annotation/hash checks, actual browser old-version/late-intent/replay/expired-session cases, manual demo, alignment/fullscreen/keyboard/pen-emulation |
| M1-11 — Synthetic MVP acceptance | **BLOCKED** | only the scoped learning-cycle subset and broad regressions are evidenced | this report and test summary | dependencies M1-00…M1-10 are not all accepted; no clean user bootstrap/admin/import/publish/assign journey, no browser auth, restart/backup/restore/runbook/responsive/mobile acceptance, and no complete `M1_E2E_ACCEPTANCE` evidence |

Direct answers requested by the owner:

1. **Real React UI:** submit/revise/review/accept, three actual PNG-file transfers, teacher message, one canvas stroke, revoke and cross-org denial. **HTTP/component harness only:** login/refresh/switch, actual same-intent replay/lost ACK, most delete/error matrices, runtime-role diagnostic.
2. **Prepared data:** yes. Course, group, published problem, enrollment, teacher assignment and student assignment/work are fixture-created before the browser starts.
3. **Photos/handwriting:** valid synthetic PNG file bytes and one persisted mouse-emulated stroke are proven end to end. Representative camera photographs, real camera capture/physical handwriting, two-page ordering and annotations on both pages are not.
4. **Browser login/refresh/logout:** no. HTTP login/refresh/switch exists; logout and React session UI/coordinator do not.
5. **Full M1 blockers:** incomplete M1-01; user onboarding/admin/import/publish/assign dependencies; two-page/annotation breadth; progress/history scope outside this task; runtime DB profile; restart/backup/restore/runbook/responsive acceptance; deployment/operational qualification.

## AUTH-01–AUTH-04 remaining blockers

The repository source documents refer to AUTH-01–AUTH-04 as one acceptance group and do not provide a trustworthy per-number definition. This report therefore does not invent individual meanings or mark any of them DONE. The group remains unaccepted because:

1. browser login/refresh/logout and full session lifecycle are incomplete;
2. the restricted runtime PostgreSQL profile cannot perform the required login audit INSERT; the existing-`Program` positive journey currently uses the owner profile;
3. production qualified clock, signing-key/origin configuration, DB/storage/edge envelope and pending cleanup/capacity have not been operationally qualified;
4. no deployment, clean-environment restart/backup/restore exercise, security operations qualification or real-data/pilot authorization was performed.

The owner supersession removes physical syscall-completion proof as a Teacher Revocation acceptance requirement; it does not turn the unqualified operational items above into PASS.

**CUTOVER CODE AND SYNTHETIC REGRESSION VERIFIED; M1 PARTIAL/BLOCKED AS SHOWN; AUTH-01–AUTH-04 UNACCEPTED; NOT PRODUCTION READY; NO DEPLOY.**
