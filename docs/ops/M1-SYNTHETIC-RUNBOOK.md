# M1 synthetic end-to-end runbook

Статус: **acceptance runbook, synthetic-only; `SYNTHETIC MVP ACCEPTED` по owner decision TC-077 после PASS независимого TC-076 re-review**. Raw rerun3 и historical FAIL неизменны. Документ не является production deployment runbook и не разрешает pilot/real data.

## 1. Preconditions и запреты

1. Работать из canonical checkout `/home/tea/github/lms` на согласованном commit; записать `git status --short --branch`, `git rev-parse HEAD`, staged diff и список pre-existing dirty paths.
2. Не reset/clean/checkout чужие изменения. Для прогонов 13–14.10.2026 отдельно сохранялись `.obsidian/workspace.json` и 14 PNG M1-06/M1-07.
3. Убедиться, что нормативные `codex/SAPIENTUM-M1-PLAN.md` и `codex/SAPIENTUM-M1-ACCEPTANCE-TEST-CASES.md` находятся в checkout. Если их нет — сообщить до запуска и не создавать сокращённую замену.
4. Использовать только synthetic names/content/photos. Запрещены production secrets, реальные данные детей, manual user/entity SQL/DML и скрытые grants.
5. Требуются Docker, .NET 10 SDK, Node 24/pnpm, Python 3/Pillow, Chrome/Chromium и Playwright. Docker должен иметь ресурс для disposable PostgreSQL.
6. Действующие gates остаются: `PRS1 EXACT OFF · G02 BLOCKED`; M1-11 не меняет permission/schema/API contracts.

## 2. Build и static prerequisites

Из repository root:

```bash
/tmp/lms-dotnet/dotnet build Lms.sln --no-restore
env PATH=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin:/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/bin/fallback:$PATH pnpm --dir src/Lms.Web build
```

Production build должен завершиться без TypeScript/Vite errors. Не запускать M1-11 против dev server или mock API.

## 3. Clean integrated environment

`M1SyntheticAcceptanceBrowserTests` создаёт unique disposable PostgreSQL `postgres:17.6-alpine`, применяет все migrations, создаёт unique private storage root и запускает Kestrel HTTPS с production React bundle. Synthetic TLS/JWT/bootstrap material живёт только внутри test process.

Команда:

```bash
env \
  LMS_BROWSER_READ=1 \
  LMS_BROWSER_NODE=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node \
  LMS_BROWSER_EXECUTABLE=/opt/google/chrome/chrome \
  LMS_PLAYWRIGHT_MODULE=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright \
  /tmp/lms-dotnet/dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj \
    --no-build --no-restore -m:1 \
    --filter FullyQualifiedName~M1SyntheticAcceptanceBrowserTests \
    --logger 'console;verbosity=normal' \
    --logger 'trx;LogFileName=m1-11.trx'
```

Evidence root по умолчанию: `codex/evidence/M1-11/`. Для отдельного запуска допустим unique `LMS_M1_11_EVIDENCE`, но итоговый manifest должен ссылаться на него.

## 4. Required UI journey

Не использовать direct DB writes для успеха сценария.

1. Через platform bootstrap UI создать Org A и Org B, сохранить одноразовые codes только в memory процесса.
2. Активировать обоих admins и войти обычным login.
3. В Org A создать P01–P12, T-A и T-X через Accounts UI; активировать каждый account и проверить первый login.
4. Создать draft course, G1/G2; зачислить P01–P12 в G1; отдельным штатным UI назначить T-A как CourseEditor курса и как teacher G1, T-X только как teacher G2. Проверить, что CourseEditor не дал T-A доступ к G2.
5. T-A: invalid ZIP diagnostics, valid ZIP, student/teacher preview, publish v1, open lesson, assign five exact-version tasks to G1, publish v2 without rewriting v1.
6. P01: course → lesson → problem → hints; select two real fixture images, reorder/delete/re-add, submit with lost-response reconciliation.
7. T-A: scoped queue → exact work/reference → both pages/annotations → NeedsRevision with lost-response reconciliation.
8. P01: see feedback/history, upload second attempt, submit.
9. T-A: Accept; verify terminal mutation denial, review history and 12×5 group progress.
10. Execute wrong org/course/group/role/parent/file, revoked/moved membership, restriction, stale CAS, duplicate and cache/logout checks against the same resource class.

Current outcome on `688d8cf`: FIX-02 provides the approved React/API path for one immutable synthetic ProcessingContextVersion and same-org CourseContextBinding. Create both through the admin UI before import and record the 1/1 read-only oracle. Never replace this with SQL/EF fixture writes, a dev bearer or invented UUID.

## 5. Processing prerequisite and DB/file oracles

Immediately after E03, use a read-only oracle to record CourseEditorScope, TeacherAssignmentScope, ProcessingContextVersion and CourseContextBinding counts. Each expected count is 1; T-A must not receive G2. Missing context/binding remains a fail-closed denial; create it only through the FIX-02 admin UI/API.

## 5.1 Final-state oracles

Read-only postconditions may query the disposable database; they must never create or repair user state. Required final oracle after a future green journey:

- 2 organizations; Org A has 12 students + T-A/T-X + admin;
- 12 G1 enrollment scopes; T-A correct explicit course authority plus G1; T-X G2 only;
- 2 immutable content versions; earlier assignments still reference v1;
- 60 assignment/scopes (12×5), exact group/version;
- P01 work terminal Accepted, 2 submissions, 2 reviews, annotations for both pages;
- four linked original photo rows/objects with exact pre/post SHA-256 and order;
- student projection/cache/assets contain no teacher-only canary;
- progress/history counts match the acceptance document and contain no unauthorized rows.

Every DB assertion must be paired with the UI/API action that created the state. A read oracle is evidence, not a setup channel.

## 6. Restart drill

Run only after the full Accepted state exists.

1. Record API health, DB counts, content version IDs, work/submission/review IDs and SHA-256 for each protected original.
2. Stop the API gracefully; start a fresh API process against the same PostgreSQL/private storage/configuration.
3. Login afresh as P01 and T-A; re-open exact history/progress/photos/annotations. Compare IDs/counts/hashes.
4. Stop API, restart the disposable PostgreSQL container, wait for `pg_isready`, then start a new API process.
5. Repeat reads and negative access checks. Cached DOM from before restart is not evidence; all reads must produce new HTTP responses.
6. Any missing history/object, changed hash/order, widened scope, 5xx or silent fallback is FAIL.

Normal test disposal or an API restart before content exists is not TC-063/064.

## 7. Coordinated synthetic backup/restore

Run against a completed source environment and restore into a different disposable PostgreSQL/database plus a different empty private-storage root.

1. Quiesce mutating requests; record the last audit/receipt marker.
2. Create PostgreSQL custom-format dump with `pg_dump -Fc` from the source database.
3. Copy the complete private-storage tree without following symlinks. Include content/import assets if stored outside DB, original photos, configuration/key material required to decrypt/verify data, and a manifest.
4. Manifest must contain build SHA, migration list, DB dump SHA-256, every file relative path/size/SHA-256, capture time and synthetic-only label. Do not include plaintext secrets.
5. Create a new empty restore database and storage root; restore with `pg_restore --exit-on-error`, copy files, apply only authorized forward migrations, then start a fresh API/React process.
6. Re-run the complete read/authorization/file oracle from section 5, including negative org/group/parent/file access and post-pagination counts.
7. Perform two negative drills: DB-only restore and files-only/incomplete manifest. Startup or the first dependent read must fail explicitly; a superficially green UI with missing originals is FAIL.
8. Destroy disposable environments only after logs/TRX/screenshots/manifest are copied into the evidence root and secrets are excluded.

Rerun 3 executed this drill from a real Accepted source state. Evidence is `codex/evidence/M1-11/rerun3-688d8cf/complete-6/backup-restore/`: custom dump manifest, identical file manifests, API/PostgreSQL restart, clean restore and DB-only negative restore. A dump without full user data must still never be recorded as TC-065 PASS.

## 8. UI/reference evidence

Capture the exact required state at 1440, 768 and 390 pixels. For every U01–U13 row record:

- reference filename and whether the exact approved original was available;
- actual screenshot, viewport and route/state;
- visual verdict and observable mismatches;
- `REFERENCE UNAVAILABLE` when the exact file is absent.

Do not substitute similarly named artwork. Do not mark PASS from DOM assertions alone. In rerun 3 none of the 11 exact approved filenames was available: record `REFERENCE UNAVAILABLE` for U01–U12 even when functional screenshots and DOM assertions pass.

## 9. Full regression

```bash
/tmp/lms-dotnet/dotnet test Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false --logger trx
env PATH=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin:/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/bin/fallback:$PATH pnpm --dir src/Lms.Web test
env PATH=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin:/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/bin/fallback:$PATH pnpm --dir src/Lms.Web build
env SAPIENTUM_CONTENT_NODE=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node NODE_PATH=/home/tea/github/lms/src/Lms.Web/node_modules python3 -m unittest discover -s tests/content_compiler -v
env SAPIENTUM_CONTENT_NODE=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node NODE_PATH=/home/tea/github/lms/src/Lms.Web/node_modules python3 -m unittest discover -s tests/content_import -v
```

The ordinary API run lists opt-in browser tests as SKIP. Expand every skipped method with `LMS_BROWSER_READ=1`. Run browser methods sequentially to avoid competing dynamic Kestrel/Chromium instances. Current harnesses use `createRequire`; set `LMS_PLAYWRIGHT_MODULE` to the Playwright package directory. Record the first configuration failure and corrected result without overwriting TRX. The mandatory M1-11 browser test is a separate required verdict.

Record every fail, skip and flaky observation from the first run. Do not replace a failed required E2E by component tests or conceal it with a retry.

## 10. Completion and Git

Before any commit:

- all mandatory P0 cases must be PASS or have an explicit owner disposition that changes the acceptance contract;
- restart and coordinated restore must have current-build evidence;
- matrix/results/progress must agree with raw evidence;
- `git diff --check`, branch/HEAD/index/status and preserved dirty-path comparison must pass;
- staged files must contain only M1-11 scope and must exclude pre-existing dirty PNG/Obsidian files.

После независимого `TC-076 RE-REVIEW: PASS` владелец принял TC-077 и разрешил scoped commit `test(m1): complete synthetic MVP acceptance`. В commit включаются только документы и evidence завершения M1; acceptance harness, pre-existing dirty PNG/Obsidian/UI исключаются. Never push or deploy from this runbook без отдельного owner decision.
