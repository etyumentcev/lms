# M1-11 — Full Acceptance Rerun 3

Дата: 14.10.2026  
Baseline: `main@688d8cf3305d7739a3cb76824a60ad9d5de3596b`  
Исходный результат rerun 3: **NOT ACCEPTED — 74 PASS / 0 FAIL / 3 BLOCKED / 0 NOT RUN**  
Текущая owner disposition: **SYNTHETIC MVP ACCEPTED — 76 PASS / 0 FAIL / 0 BLOCKED / 1 WAIVED BY OWNER FOR M1 / 0 NOT RUN**  
Граница: synthetic-only; production/real data/pilot не разрешены; push/deploy не выполнялись.

## Outcome

Полный clean journey E01–E14 прошёл. На новой disposable среде production React выполнил bootstrap, accounts, synthetic ProcessingContextVersion, CourseContextBinding, CourseEditor, ZIP import, v1/v2 publication, 12×5 assignments, two-photo submit, lost-ACK reconciliation, two-page annotations, NeedsRevision, resubmit, Accepted, history and progress. Отдельные normal-login public API действия сформировали нормативную матрицу `Accepted=2`, `NeedsRevision=2`, `Submitted=2`, `NotStarted=54` без SQL/domain writes.

API restart, PostgreSQL restart и coordinated DB+private-files restore прошли с повторным UI/API/DB/file readback. Четыре protected originals сохранили size/order/SHA-256; T-X получил 403 на work и каждый файл. DB-only restore дал ожидаемые явные file-read failures и не был принят как успешный restore.

Независимый re-review выполнил новый Chromium HTTPS lifecycle и restore A/B/C и подписал `TC-076 RE-REVIEW: PASS`. Владелец затем зафиксировал `TC-077: PASS — SYNTHETIC MVP ACCEPTED`. Исходный TC-071 остаётся `REFERENCE UNAVAILABLE`, но отдельно установлен `WAIVED BY OWNER FOR M1`; это не visual PASS. Обязательная задача `UI-VISUAL-ACCEPTANCE` должна быть закрыта до пилота с реальными учениками. `PILOT AUTHORIZED`, production deployment и реальные данные детей не разрешены.

## Preflight

- canonical checkout `/home/tea/github/lms`, branch `main`, HEAD `688d8cf3305d7739a3cb76824a60ad9d5de3596b`, `MERGE_HEAD` absent, index initially empty;
- нормативы intact: plan 789 lines, SHA-256 `7a893405463e3993502c10f4d8f5c0fb772136d63b166aed555a326b8497f537`; acceptance 1585 lines, SHA-256 `466f55cef6a49b608ef77bad2f2b5ffa81ec3b4ea3559bad37c59aa10f53b800`;
- 77 identifiers TC-001…TC-077 без gaps/duplicates;
- прочитаны AGENTS.md, guardrails/reconciliation, ADR-L13/ADR-L18, owner register и FIX-01/FIX-02 decisions;
- exact approved PNG references по всем 11 filenames не найдены; подмены не использовались;
- исходный результат `REFERENCE UNAVAILABLE` сохранён; owner decision `M1-OD-071` записан отдельно как `WAIVED BY OWNER FOR M1` и не меняет raw evidence;
- сохранены pre-existing `.obsidian/workspace.json`, PNG и AccountAdministration UI/CSS; runtime app code не менялся;
- недоступные project skills `model-domain`, `implement-use-case`, `implement-feature` не заявляются выполненными;
- границы: `PRS1 EXACT OFF · G02 BLOCKED · S4 HTTP NOT AUTHORIZED`.

## Actor → Use Case → invariants

- Platform operator → synthetic bootstrap; org admin → accounts/course/groups/context/binding/scopes; CourseEditor → import/publish; teacher → assign/review/progress; student → study/submit/history; operator → restart/restore.
- Invariants: server-owned org/course/group/parent; CourseEditor distinct from group teaching; G1/G2 and cross-org isolation; current restrictions fail closed; immutable content/assignment/history; private file parent checks before egress; CAS/idempotency prevents duplicates.
- Writes use existing transaction/isolation/lock/CAS/audit contracts. Unknown outcomes reconcile by authoritative GET; no blind mutating replay.
- Entities/VO and transitions: Organization/Identity/Membership, ProcessingContext/Binding, Course/Group/ContentVersion, Assignment/Work/Submission/Review/Annotation/Attachment; `NotStarted → Submitted → NeedsRevision → Submitted → Accepted`.

## E01–E15

| Stage | Status | Fact/evidence |
|---|---:|---|
| E01 | **PASS** | Org A/B bootstrap, admin activation/login through React. |
| E02 | **PASS** | P01–P12, T-A/T-X created, activated and logged in. |
| E03 | **PASS** | context v1 + same-org binding via admin UI; draft/G1/G2; 12 enrollments; separate CourseEditor; T-A G1, T-X G2. |
| E04 | **PASS** | invalid ZIP diagnostics, valid ZIP, separate previews, immutable v1 publish. |
| E05 | **PASS** | five G1 tasks × 12 = 60; v2 published without rewriting v1. |
| E06 | **PASS** | course/lesson/problem/hints and exact v1 teacher reference; no teacher canary in student projection. |
| E07 | **PASS** | two real PNG pages; reorder/delete/re-add; one effect after double-click/lost ACK. |
| E08 | **PASS** | scoped queue and exact protected original/reference. |
| E09 | **PASS** | both-page annotations, 768 fullscreen, NeedsRevision, lost-review ACK reconciliation. |
| E10 | **PASS** | attempt 2 and Accepted; terminal mutation blocked. |
| E11 | **PASS** | student/teacher history plus exact 12×5 `2/2/2/54` progress. |
| E12 | **PASS** | wrong org/course/group/role/parent/file and revoked authority negatives. |
| E13 | **PASS** | CAS/idempotency, duplicate clicks and unknown outcomes; no duplicate effect. |
| E14 | **PASS** | fresh API and PostgreSQL restarts with logins, hashes, history/progress and denials. |
| E15 | **PASS** | Rerun backup/restore PASS; independent re-review reproduced full DB+files restore and fail-closed files-only/incomplete-manifest drills. Raw TC-071 remains `REFERENCE UNAVAILABLE`, separately waived only for M1. |

Primary evidence: `codex/evidence/M1-11/rerun3-688d8cf/complete-6/m1-11-complete-6.trx`, `e2e-journey.json`, `backup-restore/*.json`, screenshots subdirectories.

## Restart and restore

- API restart: exact aggregate `12/60/54/2/2/2`, review history `5/3/2`, two annotations, four originals 200 with original hashes, five T-X denials 403.
- PostgreSQL stop/start: connection remapped and fresh app/login readback repeated successfully.
- Backup: custom-format DB dump plus full private file tree; manifest records dump SHA-256/size and every file path/size/SHA-256.
- Restore: new database and new storage root; Accepted main work, v1/v2, 60 assignments, exact progress, history, annotations, bytes and denials verified.
- Negative: restored DB with empty private root returns explicit 500 for all four protected originals; no placeholder/silent success.

## Browser gaps

1. `/my/courses`: initial evidence shows login 200 followed by 404. Root cause was an obsolete isolated test profile missing current CourseRoster route/catalog schema/readonly ACL, not an app defect. Profile updated; normal React login/reload/expiry/two-tab/org-switch/logout test PASS: `browser-gaps/my-courses-fixed-5.trx`.
2. Pre-issued-token harness: legacy teacher journey injected `window.lmsAuth` and tokens into localStorage. It now creates credentials only, uses production React login/logout for every actor, preserves submit/photo/annotation/revision/accept/revoke/cross-org assertions, and PASS: `browser-gaps/real-auth-teacher-fixed-2.trx`. No pre-issued bearer is passed to mandatory browser code.

ProcessingContext first run also exposed a nondeterministic test oracle (`effectiveFrom=UtcNow+10ms` immediately asserted current). The stored first failure is `regression/processing-context-rerun.trx`; using an already effective timestamp preserves all CAS/journal/history assertions and PASS is `processing-context-fixed.trx`.

## UI

Functional 390/768/1440 browser coverage is present under `complete-6/{screenshots,materials,photos,review,progress}`. U01–U03 fixes did not regress. All exact approved originals are absent, so U01–U12 remain **REFERENCE UNAVAILABLE**, never visual PASS. Owner decision `M1-OD-071` waives that criterion only for M1 Synthetic MVP; `UI-VISUAL-ACCEPTANCE` remains mandatory before a real-student pilot. U13 has no standalone PNG by contract and is functional-only.

## Regression

- .NET build: PASS, 0 warnings / 0 errors.
- Ordinary solution tests: Domain 43, Application 369, Infrastructure 343, API 385; 1140 PASS, 12 opt-in SKIP, 0 FAIL (final rerun).
- React/Vitest: first run 420 PASS / 1 timing FAIL (`attachments.test.tsx` mock observation under concurrent compiler load); isolated rerun PASS; full rerun 421/421 PASS. First JSON retained.
- React production build: PASS, 302 modules.
- content compiler: 74/74 PASS.
- content importer: 49/49 PASS.
- Chromium HTTPS/API: first batch 386 PASS / 14 FAIL because of one future-time oracle and 13 Node-24 directory-import failures; preserved as `browser-all-first.trx`. First exact 15-case batch was 14 PASS / 1 `ERR_NETWORK_CHANGED` flake; final sequential rerun is **15 PASS / 0 FAIL / 0 SKIP** in `regression/browser-only-rerun.trx`.
- No skip, flake or first failure is converted silently into PASS; each has a retained artifact and cause above.

## Guardrails §7

- client authority/IDOR/cross-org/wrong course/group/role: **covered** — J/G/R positive controls and denials;
- post-pagination/count leakage: **covered** — exact server aggregates/filter negatives;
- file-parent bypass/inaccessible originals: **covered** — four real files plus forged/foreign negatives;
- stale authorization: **covered** — fresh login after revoke has no educational scope;
- unsafe restriction narrowing: **covered** — current restrictions/CAS tests; no bypass added;
- mutable history/originals: **covered** — versions, attempts, annotations and hashes survive restore;
- duplicate effects/unknown outcome: **covered** — one submit/review effect after lost ACK;
- secrets/real data: **covered** — ephemeral synthetic credentials redacted; no real minors;
- unsupported readiness: **covered** — synthetic acceptance stated only after TC-076/077; pilot/production/real-data claims remain prohibited.

## Open gates and readiness

| ID | Priority | Gate |
|---|---:|---|
| M1-11-R3-D02 / UI-VISUAL-ACCEPTANCE | Pre-pilot | Exact approved PNG references unavailable; raw verdict stays `REFERENCE UNAVAILABLE`. `WAIVED BY OWNER FOR M1` only; exact-reference visual acceptance is mandatory before real-student pilot. |

- **UI COMPLETE:** functional yes; exact-reference visual acceptance remains `REFERENCE UNAVAILABLE` and is waived only for M1.
- **E2E ACCEPTED:** yes; rerun3 plus independent re-review PASS.
- **SYNTHETIC MVP ACCEPTED:** yes, owner decision `M1-OD-077`, synthetic-only.
- **PILOT AUTHORIZED:** no; G02 and `UI-VISUAL-ACCEPTANCE` remain open.
- **Readiness:** `SYNTHETIC MVP ACCEPTED`; pilot, production deployment and real child data remain unauthorized.

Owner decision TC-077 authorizes the scoped commit `test(m1): complete synthetic MVP acceptance`; this report participates in that commit. Push/deploy are not performed.

## Historical exact Git status at rerun 3 completion

Branch/HEAD: `main`, ahead of `origin/main` by 43, `688d8cf3305d7739a3cb76824a60ad9d5de3596b`. Index empty; `git diff --check` PASS.

```text
 M .obsidian/workspace.json
 M codex/M1-PROGRESS.md
 M codex/evidence/M1-03-ORGANIZATION-BOOTSTRAP/01-create-organization.png
 M codex/evidence/M1-03-ORGANIZATION-BOOTSTRAP/02-one-time-code.png
 M codex/evidence/M1-03-ORGANIZATION-BOOTSTRAP/03-activate-admin.png
 M codex/evidence/M1-03-ORGANIZATION-BOOTSTRAP/04-admin-dashboard.png
 M codex/evidence/M1-04-ACCOUNTS-ACTIVATION/01-accounts-empty.png
 M codex/evidence/M1-04-ACCOUNTS-ACTIVATION/02-create-student.png
 M codex/evidence/M1-04-ACCOUNTS-ACTIVATION/03-student-code.png
 M codex/evidence/M1-04-ACCOUNTS-ACTIVATION/04-accounts-list.png
 M codex/evidence/M1-04-ACCOUNTS-ACTIVATION/05-student-activation.png
 M codex/evidence/M1-04-ACCOUNTS-ACTIVATION/06-first-student-login.png
 M codex/evidence/M1-06-CONTENT-PUBLICATION/01-import-diagnostics-invalid.png
 M codex/evidence/M1-06-CONTENT-PUBLICATION/02-import-ready.png
 M codex/evidence/M1-06-CONTENT-PUBLICATION/03-preview-student.png
 M codex/evidence/M1-06-CONTENT-PUBLICATION/04-preview-teacher.png
 M codex/evidence/M1-06-CONTENT-PUBLICATION/05-published-v1-assigned-g1.png
 M codex/evidence/M1-06-CONTENT-PUBLICATION/06-published-v2.png
 M codex/evidence/M1-06-CONTENT-PUBLICATION/07-student-v1-assignments-after-v2.png
 M codex/evidence/M1-07-MATERIALS-REFERENCE/01-course-1440.png
 M codex/evidence/M1-07-MATERIALS-REFERENCE/02-lesson-1440.png
 M codex/evidence/M1-07-MATERIALS-REFERENCE/03-lesson-768.png
 M codex/evidence/M1-07-MATERIALS-REFERENCE/04-lesson-390.png
 M codex/evidence/M1-07-MATERIALS-REFERENCE/05-problem-1440.png
 M codex/evidence/M1-07-MATERIALS-REFERENCE/06-hints-390.png
 M codex/evidence/M1-07-MATERIALS-REFERENCE/07-teacher-reference-1440.png
 M src/Lms.Web/src/features/organization-admin/AccountAdministration.tsx
 M src/Lms.Web/src/features/organization-admin/accounts.css
 M tests/Lms.Api.Tests/ApiFixture.cs
 M tests/Lms.Api.Tests/ProcessingContextAdministrationHttpTests.cs
 M tests/Lms.Api.Tests/ReactBrowserSessionTests.cs
 M tests/Lms.Api.Tests/ReactTeacherJourneyTests.cs
 M tools/browser_read/content-publication.mjs
 M tools/browser_read/photos.mjs
 M tools/browser_read/react-browser-session.mjs
 M tools/browser_read/react-teacher-journey.mjs
 M tools/browser_read/run.mjs
 M tools/browser_read/teacher-journey.mjs
 M tools/browser_read/work.mjs
?? codex/M1-11-RESULTS.md
?? codex/M1-ACCEPTANCE-MATRIX.md
?? codex/SAPIENTUM-M1-ACCEPTANCE-TEST-CASES.md
?? codex/SAPIENTUM-M1-PLAN.md
?? codex/evidence/M1-11/
?? docs/ops/
?? tests/Lms.Api.Tests/M1SyntheticAcceptanceBrowserTests.cs
?? tools/browser_read/m1-11-readback.mjs
?? tools/browser_read/m1-11-setup.mjs
```

Preflight user changes (`.obsidian`, prior PNG set, AccountAdministration TS/CSS) remain unstaged and were not intentionally edited. Regression-generated M1-05/M1-10/FIX-02 and tracked `__pycache__` changes were removed before this status.
