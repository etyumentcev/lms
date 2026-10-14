# TC-076 — independent reproducibility review package

Prepared: 2026-10-14  
Evidence fix updated: 2026-10-14  
Status: **PACKAGE RECONCILED AFTER HISTORICAL TC-076 FAIL; NEW INDEPENDENT REVIEW NOT EXECUTED / NOT PASSED**  
Scope: independent reproduction of M1-11 on synthetic data, including restart and coordinated backup/restore.  
Source baseline: `688d8cf3305d7739a3cb76824a60ad9d5de3596b` plus the preserved M1-11 acceptance harness and evidence-fix scope listed below.

Checksum correction: [ERRATUM](M1-11-EVIDENCE-FIX-CHECKSUM-ERRATUM.md). Evidence-fix report: [M1-11-EVIDENCE-FIX-RESULTS](M1-11-EVIDENCE-FIX-RESULTS.md). Historical independent result [remains `TC-076: FAIL`](TC-076-INDEPENDENT-REVIEW-RESULTS.md). The [original erroneous package](evidence/M1-11/evidence-fix-20261014-codex/historical/M1-11-TC-076-INDEPENDENT-REVIEW-PACKAGE-original-erroneous.md) is preserved byte-for-byte with SHA-256 `9f60c2d8db303a8253eb0cea529e844515597c86391ddadf835fc5708e1f2595`.

This package does not declare `SYNTHETIC MVP ACCEPTED`, does not close TC-077, and does not authorize a real-student pilot. TC-071 remains `REFERENCE UNAVAILABLE`; owner decision `M1-OD-071` is a separate `WAIVED BY OWNER FOR M1` disposition only. `UI-VISUAL-ACCEPTANCE` remains mandatory before a real-student pilot.

## 1. Reviewer independence and stop conditions

The reviewer must not be the author of rerun 3, its result report, or this evidence-fix execution. Record name, organization/role, contact, host, UTC timestamps and signature in section 8. A second person may operate the runner, but the signer remains accountable for the evidence reviewed. The remediation artifacts below are inputs to review, not a replacement for independent execution.

Stop and record **FAIL** if any of the following occurs:

- the supplied source snapshot cannot be tied to the recorded HEAD and working-tree inventory;
- a required command, secret scan, restart, backup, restore or negative restore is omitted;
- user/domain state is repaired by direct SQL/DML, dev bearer, hidden grant or manual UUID;
- the exact evidence hashes below do not match before review;
- a failed required step is replaced by a retry without preserving the first failure;
- evidence contains a production secret or real child data;
- DB-only/incomplete restore appears successful or protected bytes/history/authorization change after restore.

No reset, clean, checkout, commit, push or deploy is part of this review. Preserve unrelated dirty files.

## 2. Normative inputs and harness identity

Run from `/home/tea/github/lms`. HEAD alone is not the complete source identity because acceptance files are intentionally uncommitted until TC-076 and TC-077. Use the owner-supplied read-only snapshot of the current canonical working tree; record `git status --short --branch`, `git rev-parse HEAD`, staged diff, unstaged diff and untracked paths before running. Do not silently recreate missing untracked harness files.

| Path | SHA-256 before independent review |
|---|---|
| `codex/SAPIENTUM-M1-PLAN.md` | `7a893405463e3993502c10f4d8f5c0fb772136d63b166aed555a326b8497f537` |
| `codex/SAPIENTUM-M1-ACCEPTANCE-TEST-CASES.md` | `466f55cef6a49b608ef77bad2f2b5ffa81ec3b4ea3559bad37c59aa10f53b800` |
| `docs/ops/M1-SYNTHETIC-RUNBOOK.md` | `034fb02770ddb81ddf17145f31be728b6bb2fb3d0b18c471e868446e87136664` |
| `tests/Lms.Api.Tests/ApiFixture.cs` | `9ab7b0528a44f892415e04eb6065cbd83e434f9ed15caf2c678c8ce2163d7477` |
| `tests/Lms.Api.Tests/M1SyntheticAcceptanceBrowserTests.cs` | `e2485c423109276764386d89df4dd394ef27139b663fee304c450967114692d7` |
| `tools/backup_restore/verify_m1_backup_set.py` | `35fc73983d35bec313c54935f337fffab9525e9bcbdc728c4daa5cc9e661c141` |
| `tools/browser_read/m1-11-setup.mjs` | `cef0e46d15ae8e27a8d976b9d5bd570281d9e1144b843891b7a9dcf823d22f6d` |
| `tools/browser_read/m1-11-readback.mjs` | `3d00b7888e63f417d8547f326a0a8d3109f3ec5c85994d3e7b6f046376030caa` |

Read before execution: `AGENTS.md`, the two guardrail documents, ADR-L05, ADR-L07, the M1 acceptance cases, the M1 runbook, `M1-ACCEPTANCE-MATRIX.md`, `M1-11-RESULTS.md` and owner decision `M1-OD-071` in `L-DECISION-REGISTER.md`.

## 3. Source evidence inventory and checksums

All paths are relative to repository root. The source evidence tree is `codex/evidence/M1-11/rerun3-688d8cf/`. The historical `test-summary.json` is intentionally unchanged and still records the raw rerun result `74 PASS / 0 FAIL / 3 BLOCKED / 0 NOT RUN`.

Aggregate digest algorithm, executed from repository root:

```bash
find codex/evidence/M1-11/rerun3-688d8cf -type f -print0 | sort -z | xargs -0 sha256sum | sha256sum
```

The digest is SHA-256 over the sorted `sha256sum` records, including their repository-relative paths. Recompute from the same repository root.

| Evidence group | Files | Bytes | Aggregate SHA-256 |
|---|---:|---:|---|
| complete rerun 3 tree | 681 | 142211716 | `186d4e4a9da3b52210bb9f4c3241c9ed025d9b9917e56eef7c083d9976ef5838` |
| `complete-6/` final integrated run | 63 | 13110477 | `8588c77c9c913223e34b6a28e5b4c0153748db282d8491b0d0366e26a67e257d` |
| `browser-gaps/` initial failures and corrected reruns | 31 | 7597489 | `b8fa4952925c8ffdc2c61ef9f5847b9f4281148f96cf2431f70038f19573c952` |
| `regression/` first/final regression evidence | 326 | 74066260 | `ef6029cb8d8997dd60928ac25557864eb56deb0dc61ae46f28b436ccafe304e8` |
| `first-failure/` | 10 | 997901 | `919dcdf09918d4cd63d857ab522684f236eafcdde9c5a82d6e3d0a8d947d344b` |
| `final-journey/` | 28 | 3736728 | `bcdcc2b3a40c3e2fe4b68236b3f0b38d2ff59d9d66b4f4e6a3d84443b7a2fc4f` |
| `final-journey-2/` | 51 | 9389902 | `8b20a2cbf5ec939efe8365ed953544bf179c11aa3c042ce03db8206c03d9eebb` |
| `complete/` | 52 | 9732674 | `9de07909377eca861216d79727c8020ea3f6f06291d2b228197bb3ccb2bcf62fc` |
| `complete-2/` | 54 | 10590541 | `e2d88c830d0e19ad54bf32c4b1912a855e09f3d81828388cca9732ad8543ba19` |
| `complete-3/` | 63 | 12967649 | `b4797b1684f8081f3aa24366bc58afba3099126483f3d8553201eb68f03cd8fe` |
| `complete-4/` | 1 | 7689 | `5b23cde9a62bbb60d273652a610260faf8c290189cdad0ba67e273c36f619db1c` |
| `complete-5/` | 1 | 13083 | `ba707419778281078b5a2ab7031237f6d7b4333d4b383239ad51251fe841ccc9` |

Key artifact hashes:

| Artifact | SHA-256 |
|---|---|
| `test-summary.json` | `e5ac00545d606df634844dc420b87e8475d5e1b259c652d2dd0de2bb418113be` |
| `complete-6/environment.json` | `f754d7a771102b4e715a40d8fa568588810ac3b4648c0e67b44cccda3d44e8fa` |
| `complete-6/E03-authority-oracle.json` | `322d08f5bd37aa55d5b72d0d1b356e13a84740b89cbd9d28c7e8126cc028f0b0` |
| `complete-6/e2e-journey.json` | `53206971d0fd9b8107768535be064783b5ce5b0d806d70f493c603aeecf63ced` |
| `complete-6/m1-11-complete-6.trx` | `6dec84cad7f1dce4a0f639092502bbf924622654c981f380da94b5bf18d1d467` |
| `complete-6/backup-restore/manifest.json` | `083abbf2ee55e45678ebb09a55d8c9e09902f79a7f4dbe9a222c3d83fed19994` |
| `complete-6/backup-restore/api-restart.json` | `04ac68bbe0bbb53e71adddfab264560ebcabfc57c180994b74d9756b8a9331ce` |
| `complete-6/backup-restore/postgres-restart.json` | `d677e8728ea6235f211bd2fae51fc95e46ed8c2ac6c072f4e903f0c25e675683` |
| `complete-6/backup-restore/restore.json` | `a739efc8e6a350a8b661ab6e6ae920867c1dd1daba49896058b1feb27c9c0bff` |
| `complete-6/backup-restore/restore-db-only-negative.json` | `be0ac8cea615e97257f603bdd7ed78a219c582bac484dae1a239cf8e9aa23381` |
| `browser-gaps/my-courses-fixed-5.trx` | `04638e598a98bbd9353fa6eb7a6741d98e50cc60a16a4852c4ff98739e699aa5` |
| `browser-gaps/real-auth-teacher-fixed-2.trx` | `ec2a5668fd850c0601472e30419577fd23d827d09bcbfca10d2d3883e7161593` |
| `regression/browser-all-first.trx` | `c49c5f475d4261842f1df7e467dbf1854f2d385d6a58d10b9faaec73a05ed897` |
| `regression/browser-only-rerun.trx` | `47342826441433f4e25c296bd7e4b566d97355b3fac18f4d5258afaa48f5f74d` |
| `regression/dotnet-final-domain.trx` | `54bddf586f22248aa7d972a9240efa76b5c18d8dc4f06fb781a65f6c61af0e9a` |
| `regression/dotnet-final-application.trx` | `e1d5f8d7ac4d2f141e7e7217d9b180f7da9e0b389d532e3e27c74081d67e9659` |
| `regression/dotnet-final-infrastructure.trx` | `b903cb78fc8286b34d9075783021d7f2e9e68b71d6b11a33e6ef1eac5791aa56` |
| `regression/dotnet-final-api.trx` | `f8bf28a829baa12805c30eaccaa59d39a23633374928b081615097f1d933ab5c` |
| `regression/processing-context-rerun.trx` | `341d4136a1fbb2a5616820021e3fd7508334521e55f000b5d405f88adf8daddd` |
| `regression/processing-context-fixed.trx` | `24e66c82109fe6f6b45b35c851a7dd86d07e485d2150c587d232edb9ec93c5e6` |
| `regression/vitest-attachment-rerun.json` | `b2c705ab93b16fce76e3fe63177efd40e692fbb332706f43218b12cdcecb3bf4` |

The original rerun3 backup manifest separately records the dump SHA-256 `c3bd809228ee002bd42a2f1e09f23b8f9229c968d0128baa41043f01008138eb` and every source/restored private file path, size and SHA-256. It remains historical and must be inspected, not regenerated or edited.

The corrected checksum is governed by the linked ERRATUM. The 64-character value above is reproduced by the raw independent audit and a fresh calculation; the 65-character package value was a single appended `c`, not an evidence mutation.

## 4. Main E2E reproduction

Prerequisites: Docker; .NET 10 SDK at `/tmp/lms-dotnet/dotnet`; Node 24/pnpm; Python 3/Pillow; Chrome at `/opt/google/chrome/chrome`; Playwright in the bundled runtime. Use only synthetic data and a new evidence directory. Do not write new output into `rerun3-688d8cf`.

1. Record source identity and verify section 2 hashes.
2. Recompute the full source-evidence aggregate from section 3; retain the command output.
3. Build production artifacts:

```bash
/tmp/lms-dotnet/dotnet build Lms.sln --no-restore
env PATH=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin:/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/bin/fallback:$PATH pnpm --dir src/Lms.Web build
```

4. Choose a new empty evidence path such as `codex/evidence/M1-11/independent-YYYYMMDD-reviewer/`. Then run the mandatory E2E sequentially:

```bash
env \
  LMS_M1_11_EVIDENCE=codex/evidence/M1-11/independent-YYYYMMDD-reviewer \
  LMS_BROWSER_READ=1 \
  LMS_BROWSER_NODE=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node \
  LMS_BROWSER_EXECUTABLE=/opt/google/chrome/chrome \
  LMS_PLAYWRIGHT_MODULE=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright \
  /tmp/lms-dotnet/dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj \
    --no-build --no-restore -m:1 \
    --filter FullyQualifiedName~M1SyntheticAcceptanceBrowserTests \
    --logger "console;verbosity=normal" \
    --logger "trx;LogFileName=tc-076-independent.trx"
```

5. Confirm the fresh run created two organizations, 12 synthetic students, two teachers, 60 exact-version assignments and the terminal distribution `Accepted=2 / NeedsRevision=2 / Submitted=2 / NotStarted=54` through normal React/API actions. Confirm wrong-org/course/group/role/parent/file and revoked authority denials.
6. Preserve the first failure and all logs. A corrected rerun is additional evidence, not a replacement.
7. Run the full regression commands in runbook section 9 and account for every skip. Browser methods must run sequentially with `LMS_BROWSER_READ=1`.

## 5. Independent backup/restore verification

The mandatory E2E must create the full Accepted source state before this check. Follow runbook sections 6–7 and record each phase separately.

1. **API restart:** capture IDs/counts/hashes, stop API gracefully, start a fresh API against the same DB/private root, perform fresh P01/T-A logins and HTTP reads, and repeat T-X denials.
2. **PostgreSQL restart:** stop API, restart the disposable PostgreSQL container, wait for readiness, start a new API, then repeat history/progress/original-byte/authorization checks.
3. **Coordinated backup:** quiesce writes; create `pg_dump -Fc`; copy the complete private tree without following symlinks; write a manifest with source SHA, migrations, dump hash/size, every relative file path/size/hash, capture time and synthetic-only label. Do not include plaintext secrets.
4. **Clean restore:** use a different empty database and private root; run `pg_restore --exit-on-error`; copy files; start a new API/React process; repeat DB/API/file and negative-authorization oracles. All four original byte hashes, order, two submissions, two reviews, two annotations, v1/v2, 60 assignments and terminal progress must match.
5. **Negative restore:** restore the database with an empty private root. All four protected original reads must fail explicitly; placeholders, silent fallback or a green dependent UI are FAIL. Also exercise an incomplete/files-only manifest as required by the runbook, or record FAIL if the harness does not produce that required negative.
6. Compare the new manifest internally and retain its SHA-256. Do not overwrite the original `complete-6/backup-restore/manifest.json`.

### Evidence-fix supplement for reviewer verification

The remediation operator created, but did not independently attest, a new isolated synthetic set at `codex/evidence/M1-11/evidence-fix-20261014-codex/`:

- full manifest `backup-restore/manifest.json`, SHA-256 `f5948597568b9f3dbfbcbfa3bf27fb24faad1a6e86e9e20bdd876cee11447a60`;
- retained PostgreSQL custom dump `backup-restore/backup-set/postgresql/database.dump`, SHA-256 `ff0ce9507276a99b24967fa47d24577c178f015224f63b4a1af6487575b85d0e`;
- private snapshot aggregate `6079cbc59948d44c2b63adfdc4f7a67074310cbf017f95df3897d20efbd563e7`, four files / 138148 bytes;
- full DB+files restore commands, exit codes and oracles in `backup-restore/full-restore-drill.json`;
- files-only refusal in `negative/files-only/scenario.json`;
- incomplete-manifest refusal in `negative/incomplete-manifest/scenario.json`;
- validator first failure and corrected result in `backup-restore/full-set-validation.json`.

The reviewer must verify this set and run the package independently. These author-produced remediation artifacts do not change the historical `TC-076: FAIL` and cannot themselves produce a new TC-076 PASS.

## 6. Secrets and synthetic-data review

Record the exact scanner/tool versions and commands. Scan tracked files, untracked source/harness files, new TRX/JSON/logs/screenshots metadata and backup manifests. At minimum check private keys, bearer/refresh tokens, connection strings with non-ephemeral credentials, passwords, cloud credentials, external URLs containing credentials and personal data. Manual review must confirm synthetic names/content/photos and absence of real children. Test-process ephemeral credentials may exist only in memory; any published usable secret is FAIL.

Do not claim legal/compliance or production readiness from a clean scan. Record operational limitations: Chromium emulation is not a physical tablet test; off-host backup, RPO/RTO, production TLS/secrets and G02 remain outside this synthetic acceptance.

## 7. PASS / FAIL rule

TC-076 is **PASS** only when one signed independent report demonstrates all of the following on the supplied source snapshot:

- pre-run source and original evidence checksums match;
- production build and main E2E pass without hidden setup;
- restart and coordinated restore pass with matching history, authorization and bytes;
- DB-only and required incomplete restore fail explicitly;
- required regression/opt-in browser suites are accounted for;
- secret/synthetic-data review passes with commands and findings attached;
- new evidence paths and their SHA-256 manifest are attached;
- all deviations, skips, retries and first failures are disclosed.

Any unmet item is **FAIL**. `NOT RUN`, unsigned output, author self-attestation or local rerun 3 evidence alone cannot close TC-076.

## 8. Independent conclusion template

Copy this section into a new signed report; do not edit the original rerun evidence.

### TC-076 independent conclusion

- Verdict: **PASS / FAIL**
- Reviewer full name:
- Organization / role:
- Contact:
- Independence statement: I did not author M1-11 rerun 3, its harness result, or its result report: **YES / NO**
- Review start/end in UTC:
- Host / OS / Docker / .NET / Node / Chromium versions:
- Repository HEAD:
- Working-tree status attached: **YES / NO**
- Section 2 source hashes: **MATCH / MISMATCH**
- Original 681-file evidence aggregate: **MATCH / MISMATCH**
- New evidence root:
- New evidence manifest SHA-256:

| Check | PASS / FAIL | Command or evidence path | Findings / first failure / retry |
|---|---|---|---|
| Production build |  |  |  |
| Main E2E |  |  |  |
| 12×5 final oracle |  |  |  |
| Negative access matrix |  |  |  |
| API restart |  |  |  |
| PostgreSQL restart |  |  |  |
| Coordinated DB+files restore |  |  |  |
| DB-only negative restore |  |  |  |
| Incomplete/files-only negative restore |  |  |  |
| Full regression and opt-in browsers |  |  |  |
| Secrets scan |  |  |  |
| Synthetic-data manual review |  |  |  |
| Operational limitations recorded |  |  |  |

Open findings / deviations:

Final rationale for PASS or FAIL:

- Reviewer signature:
- Signature form / verification reference:
- Signed at UTC:

A PASS report closes only TC-076 after repository reconciliation. It does not close TC-077, accept the M1 automatically, authorize pilot/production, or convert TC-071 into PASS.
