# M1-11-EVIDENCE-FIX — результаты устранения недостатков TC-076

Дата: 2026-10-14  
Исполнитель remediation: **OpenAI Codex**  
Baseline: `main@688d8cf3305d7739a3cb76824a60ad9d5de3596b`  
Итог: **EVIDENCE REMEDIATION COMPLETE; HISTORICAL TC-076 FAIL PRESERVED; INDEPENDENT RE-REVIEW REQUIRED**.

Эта работа не меняет исходный результат M1-11 `74 PASS / 0 FAIL / 3 BLOCKED`, не превращает исторический `TC-076: FAIL` в `PASS`, не закрывает TC-077 и не объявляет `SYNTHETIC MVP ACCEPTED` или `PILOT AUTHORIZED`.

## 1. Scope и preflight

- Canonical checkout: `/home/tea/github/lms`; branch `main`; HEAD `688d8cf3305d7739a3cb76824a60ad9d5de3596b`; `origin/main` отставал на 43 commits; index был пуст.
- Рабочее дерево до начала уже содержало `.obsidian/workspace.json`, пользовательские PNG, `AccountAdministration.tsx/.css`, M1-11 harness/docs/evidence и другие tracked/untracked изменения. Они не reset/clean/checkout и не перезаписывались.
- Прочитаны guardrails/reconciliation, ADR-L05, ADR-L07, owner register, `M1-11-RESULTS.md`, acceptance matrix, runbook, исходный review package, исторический независимый FAIL, rerun3 evidence и checksum audit.
- Проектные skills `model-domain`, `implement-use-case`, `implement-feature` отсутствуют, как уже зафиксировано reconciliation; их выполнение не заявляется.
- Scope: checksum erratum, проверяемый synthetic backup set, full restore и два недостающих negative drill, evidence/docs/harness-only изменения.
- Non-goals: учебные функции, authorization model, ProcessingRestrictions, production infrastructure, schema/API/security contracts, acceptance/push/deploy.

Actor → Use Case → Invariants → Entities/VO → State transitions → Authorization/data source → Tests:

- operator remediation → создать воспроизводимый backup/restore evidence set;
- исходный rerun3 immutable; DB dump и private snapshot относятся к одному quiesced synthetic state; incomplete input fail-closed до target mutation; исторический verdict immutable;
- backup set, manifest, PostgreSQL custom dump, private-files snapshot, checksums, restore target;
- captured → validated → restored/verified либо refused-before-restore;
- только synthetic disposable DB/storage; existing server-side auth/readback; production/real data отсутствуют;
- aggregate SHA-256, manifest validator, full DB+files restore, files-only refusal, incomplete-manifest refusal, fresh DB-only protected-file refusal, build/test/JSON/whitespace checks.

Production/domain writes отсутствуют. Для synthetic capture все API instances были остановлены; `pg_dump -Fc` завершён до no-symlink копирования private root. Full restore выполнен в новую БД и новый storage root через `pg_restore --exit-on-error`. Transaction/CAS/lock/retry semantics приложения не изменялись; unknown outcome отсутствовал, exit codes и first failure сохранены.

## 2. Checksum baseline

Причина mismatch установлена: в package к уже полному корректному 64-символьному digest вручную добавлен один завершающий символ `c`. Raw audit до remediation содержал корректное значение.

| Объект | Ошибочное значение | Корректное значение |
|---|---|---|
| `rerun3-688d8cf/final-journey-2/`, 51 файл / 9389902 bytes | `8b20a2cbf5ec939efe8365ed953544bf179c11aa3c042ce03db8206c03d9eebbc` | `8b20a2cbf5ec939efe8365ed953544bf179c11aa3c042ce03db8206c03d9eebb` |

Команда:

```bash
find codex/evidence/M1-11/rerun3-688d8cf/final-journey-2 -type f -print0 | sort -z | xargs -0 sha256sum | sha256sum
```

Полный rerun3 до и после remediation: 681 файл, aggregate `186d4e4a9da3b52210bb9f4c3241c9ed025d9b9917e56eef7c083d9976ef5838`. Расхождения с исходными artifacts нет. Отдельный документ: `codex/M1-11-EVIDENCE-FIX-CHECKSUM-ERRATUM.md`, SHA-256 `cd3fe3ea9b957409f4ae57d2f8d62fc7b02b088d33ea99b2fd9a7cf76ac9233e`.

Первоначальная ошибочная версия package сохранена byte-for-byte, SHA-256 `9f60c2d8db303a8253eb0cea529e844515597c86391ddadf835fc5708e1f2595`. Исторический `codex/TC-076-INDEPENDENT-REVIEW-RESULTS.md` не изменён, SHA-256 `a3c81a8c8b08954b4fabf2a1b59f691fb1eb85da2eedf665d75251bae7c2c447`.

## 3. Новый backup manifest

Evidence root: `codex/evidence/M1-11/evidence-fix-20261014-codex/`.

| Поле | Фактическое значение |
|---|---|
| Backup ID | `m1-11-evidence-fix-20261014T010547Z-78206733cef94ab295a6adb4bc48b7dd` |
| Capture interval | `2026-10-14T01:05:47.5460034Z` — `2026-10-14T01:05:47.7292568Z`, UTC, `TimeProvider.System.GetUtcNow` |
| Build | `688d8cf3305d7739a3cb76824a60ad9d5de3596b`, purpose `M1-11-EVIDENCE-FIX` |
| Migrations | 17 exact IDs in manifest |
| PostgreSQL backup | custom-format `database.dump`, 307673 bytes, SHA-256 `ff0ce9507276a99b24967fa47d24577c178f015224f63b4a1af6487575b85d0e` |
| Private snapshot | 4 files / 138148 bytes, aggregate SHA-256 `6079cbc59948d44c2b63adfdc4f7a67074310cbf017f95df3897d20efbd563e7` |
| Manifest | SHA-256 `f5948597568b9f3dbfbcbfa3bf27fb24faad1a6e86e9e20bdd876cee11447a60` |
| Consistency method | API writers stopped; DB dump completed; unchanged private root copied without symlinks; pair verified by DB, HTTP authorization, history/progress and byte-hash oracles |

Исторический rerun3 manifest не дополнялся и не изменялся. Новый manifest содержит actual timestamps, migration list, retained dump payload, полный file inventory и описанный consistency barrier.

Первый запуск независимого validator безопасно отказал из-за ошибки validator: records сортировались по digest, а не по относительному пути. Лог сохранён; backup payload и manifest не менялись. Validator исправлен на path-order и дополнен symlink refusal. Финальная проверка полного set: exit `0`, `PASS`; SHA-256 validator `35fc73983d35bec313c54935f337fffab9525e9bcbdc728c4daa5cc9e661c141`.

## 4. Restore drills

| Сценарий | Изолированная среда / input | Expected | Actual / exit codes | Verdict |
|---|---|---|---|---:|
| A. Full DB + private files | Fresh M1 source; different empty database `c2_18ec…`; different empty storage root; complete manifest/dump/files | `createdb` и `pg_restore --exit-on-error` успешны; migrations/state/history/progress/auth/files совпадают | `createdb=0`, `pg_restore=0`; 17 migrations; 2 versions; 60 assignments; Accepted; 2 submissions/reviews/annotations; 4/4 file hashes; student/teacher reads PASS; T-X denied; no partial success/data loss | **PASS** |
| B. Files-only | Separate PostgreSQL 17.6 tmpfs container, network `none`; authentic manifest + complete files, dump absent | Refuse before target DB/storage mutation | validator `42`, reason `PostgreSQL backup payload is missing or is a symlink`; target DB absent before/after; target root empty; `createdb`/`pg_restore` not invoked | **PASS** |
| C. Incomplete manifest | Second separate PostgreSQL 17.6 tmpfs container, network `none`; dump+files complete, `capture.completedAt` removed | Refuse before target DB/storage mutation | validator `42`, reason `capture.completedAt must be an ISO-8601 UTC value ending in Z`; target DB absent before/after; target root empty; `createdb`/`pg_restore` not invoked | **PASS** |

Fresh DB-only check также сохранён: restored DB с пустым private root вернул explicit expected failures для четырёх protected originals; `restore-db-only-negative.json` имеет status `EXPECTED_FAILURE`, без placeholder/silent success.

Команды, stdout/stderr, exit codes и target-before/after evidence:

- `backup-restore/full-restore-drill.json`, `restore.json`, `restore-db-only-negative.json`;
- `negative/files-only/{commands.txt,scenario.json,validator*.log,database-*.log,target-private-after.log}`;
- `negative/incomplete-manifest/{commands.txt,scenario.json,validator*.log,database-*.log,target-private-after.log}`;
- `run-command.txt`, `test-console.log`, `test-results/evidence-fix.trx`.

Финальный evidence-root: 111 файлов / 13128021 bytes; aggregate SHA-256 `2a9afe20cf8e2024a4fb9ba56827ace03abd46d5b66328bf69fbcc796aa11954` по sorted `sha256sum` records; повторный расчёт совпал.

## 5. Проверки

| Проверка | Команда | Результат |
|---|---|---|
| Python syntax | `python3 -m py_compile tools/backup_restore/verify_m1_backup_set.py` | PASS, exit 0 |
| API test build | `/tmp/lms-dotnet/dotnet build tests/Lms.Api.Tests/Lms.Api.Tests.csproj --no-restore -p:UseSharedCompilation=false` | PASS, 0 warnings / 0 errors |
| React production build | `pnpm --dir src/Lms.Web build` с bundled Node PATH | PASS, 302 modules |
| Fresh synthetic run | exact command in `run-command.txt` | PASS, 1/1, 1.4438 minutes |
| Full set validator | `verify_m1_backup_set.py .../backup-restore --manifest .../manifest.json` | PASS, exit 0 |
| Files-only validator | same tool against files-only input | expected REFUSED, exit 42; scenario PASS |
| Incomplete-manifest validator | same tool against incomplete input | expected REFUSED, exit 42; scenario PASS |
| JSON parse | all new `*.json` through `jq empty` | PASS, exit 0 |
| Secrets patterns | exact `rg` in `security-scan-result.txt` | 0 matches; `rg` exit 1 means no matches |
| `git diff --check` | repository root | PASS, exit 0, no output |

Первый compiler result с nullable Testcontainers exit codes был сохранён в текущей task history и исправлен до green build. Первый full-set validator refusal сохранён в evidence. Эти промежуточные результаты не выданы за PASS.

## 6. Guardrails §7 / security review

- Actor/use case/invariants/states: **covered** — §§1–4.
- Accepted S2/S3/PRS1 and blocked gates: **covered** — runtime/permissions не изменены; `PRS1 EXACT OFF · G02 BLOCKED · S4 HTTP NOT AUTHORIZED`.
- Client authority, wrong org/course/group/role, counts/pagination, stale membership: **not applicable to evidence-file writes**; fresh M1 harness сохранил существующие E12/readback checks, но этот remediation run не является независимой аттестацией.
- File parent/inaccessible attachments: **covered** — full restore exact bytes и T-X denial; DB-only gives explicit file failures.
- Missing/forged parent and missing processing evidence: **not affected** — authorization/processing implementation не менялась; existing M1 harness assertions выполнены.
- Transaction/lock/CAS/retry/unknown outcome: **not applicable to report/manifest writes**; backup consistency barrier и restore failure boundary описаны; target mutations не начинаются до manifest validation.
- Unsafe restriction narrowing/mutable history/duplicate effects: **covered** — код этих областей не изменён; restored immutable history/counts совпали.
- Validation/limits/safe errors: **covered** — strict schema/time/hash/path/symlink/exact-file checks; deterministic exit 42 before mutation.
- Secrets/real data: **covered for this evidence scope** — synthetic fixtures only; pattern scan 0 matches; production/compliance readiness не следует.
- Unsupported readiness claims: **covered** — никаких acceptance/pilot/production заявлений.

## 7. Изменённые пути и сохранённые границы

Основные intentional paths:

- `codex/M1-11-EVIDENCE-FIX-RESULTS.md`;
- `codex/M1-11-EVIDENCE-FIX-CHECKSUM-ERRATUM.md`;
- `codex/M1-11-TC-076-INDEPENDENT-REVIEW-PACKAGE.md`;
- `codex/evidence/M1-11/evidence-fix-20261014-codex/`;
- `tests/Lms.Api.Tests/ApiFixture.cs` и `M1SyntheticAcceptanceBrowserTests.cs` — только экспорт dump, timestamps/migrations/commands и evidence manifest;
- `tools/backup_restore/verify_m1_backup_set.py` — fail-closed verifier.

Не изменялись исходный `rerun3-688d8cf`, исторический `TC-076-INDEPENDENT-REVIEW-RESULTS.md`, `M1-11-RESULTS.md`, acceptance matrix, пользовательские PNG, Obsidian, `AccountAdministration`, учебные функции, authorization/ProcessingRestrictions, migrations и production infrastructure.

## 8. Open gates / readiness

- Новый независимый TC-076 review: **NOT RUN**; исправленный package передаётся другому проверяющему.
- Исторический `TC-076: FAIL`: **preserved**.
- TC-077 owner decision: **OPEN**.
- TC-071: исходный verdict сохранён; `WAIVED BY OWNER FOR M1` не является PASS.
- Off-host backup, RPO/RTO, key escrow, C04/C05/C09, physical devices, G02/legal gates: **NOT PROVEN / outside scope**.
- Настоящий runtime backup/restore defect не выявлен. Выявлены и устранены evidence defects: malformed checksum baseline, неполный исторический manifest coverage и отсутствующие negative drill artifacts; первый validator ordering defect также сохранён и исправлен.

Финальный Git audit: branch `main`, HEAD `688d8cf3305d7739a3cb76824a60ad9d5de3596b`, index пуст, `git diff --check` PASS. Полный `git status --short --branch` сохранён в `codex/M1-11-EVIDENCE-FIX-GIT-STATUS.txt`; он содержит pre-existing dirty paths и intentional evidence-fix paths. Commit, push, merge и deploy не выполнялись.

Readiness label: **EVIDENCE REMEDIATION COMPLETE · TC-076 INDEPENDENT RE-REVIEW REQUIRED · SYNTHETIC MVP NOT ACCEPTED · PILOT NOT AUTHORIZED**.
