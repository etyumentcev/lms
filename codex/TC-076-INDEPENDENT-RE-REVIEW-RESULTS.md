# TC-076 — повторная независимая проверка Sapientum M1

Дата: 2026-10-14  
Проверяющий: **OpenAI Codex, отдельный автоматизированный независимый reviewer/runner**  
Контакт: текущая задача Codex; внешнего контакта нет  
Интервал UTC: `2026-10-14T01:30:38Z` — `2026-10-14T03:13:18Z`  
Итог: **TC-076 RE-REVIEW: PASS**

## 1. Независимость, версия и граница

Текущий reviewer не участвовал в M1-11 rerun 3, создании `M1-11-RESULTS.md` или выполнении `M1-11-EVIDENCE-FIX`. Remediation evidence использован только как проверяемый вход; весь обязательный runtime/restore/regression цикл выполнен заново в отдельной среде и записан в новый root.

- Repository: `/home/tea/github/lms`.
- Branch/HEAD: `main@688d8cf3305d7739a3cb76824a60ad9d5de3596b`, `origin/main` позади на 43 commits.
- Полная source identity включает незакоммиченные acceptance harness/docs из `preflight/source-identity.txt`; HEAD один недостаточен.
- Новый evidence root: `codex/evidence/M1-11/independent-rereview-20261014T0130Z-codex/`.
- SHA-256 inventory: `SHA256SUMS`, 292 записи; SHA-256 inventory-файла `bacae8ae721d382f3c43bd899fb3df10d372b2fe9f2102496c031f59e7cd9ecd`; verification exit 0. Root после добавления inventory: 294 файла / 45417642 bytes.
- Только synthetic data. Код приложения, test expectations, security rules, schema/migrations и исходные evidence не менялись.
- Commit, acceptance commit, push, merge и deploy не выполнялись.
- Граница сохранена: `PRS1 EXACT OFF · G02 BLOCKED · S4 HTTP NOT AUTHORIZED`.

Среда: Ubuntu 26.04.1 / Linux `7.0.0-38-generic`, Docker `29.8.1`, .NET SDK `10.0.401`, Node `24.19.0`, pnpm `11.25.0`, Python `3.14.4`, Google Chrome `154.0.8037.57`, Git `2.53.0`, ripgrep `15.2.0`.

## 2. Проверка доказательств до запуска

Все обязательные stop-checks совпали.

| Проверка | Факт | Результат |
|---|---|---:|
| Исправленный SHA-256 | `8b20a2cbf5ec939efe8365ed953544bf179c11aa3c042ce03db8206c03d9eebb`, 64 hex | **PASS** |
| Source/harness identity | 8/8 SHA-256 из исправленного package | **PASS** |
| Исходный rerun3 | 681 файл / 142211716 bytes / `186d4e4a9da3b52210bb9f4c3241c9ed025d9b9917e56eef7c083d9976ef5838` | **PASS** |
| Все 11 подгрупп rerun3 | files/bytes/digest совпали | **PASS** |
| Исторический TC-076 | `TC-076: FAIL`, SHA-256 `a3c81a8c8b08954b4fabf2a1b59f691fb1eb85da2eedf665d75251bae7c2c447` | **PASS / preserved** |
| Исторический ошибочный package | byte-for-byte SHA-256 `9f60c2d8db303a8253eb0cea529e844515597c86391ddadf835fc5708e1f2595` | **PASS / preserved** |
| Remediation backup set | actual UTC interval, 17 migrations, custom dump, 4-file/138148-byte private inventory, checksums; independent validator exit 0 | **PASS** |
| Матрица | 77/77 ID; raw `74 PASS / 0 FAIL / 3 BLOCKED`; TC-071 raw verdict не переписан, waiver отделён; TC-077 открыт | **PASS** |

Raw audit: `preflight/checksum-audit.txt`, `preflight/remediation-backup-validation.json`, `preflight/remediation-manifest-audit.json`, `preflight/matrix-audit.txt`.

## 3. Новый Chromium HTTPS E2E

Production builds прошли: .NET — 0 warnings / 0 errors; React — 302 modules. Затем один новый обязательный test выполнил production React bundle через HTTPS и реальный Chromium с обычными login/forms; заранее выданный bearer в browser не применялся.

Фактический новый journey:

`Bootstrap → Organization → ProcessingContext → Course Binding → CourseEditor → Import → Publish → Assignment → Submission → Review → NeedsRevision → Resubmission → Accepted → History → Progress`

- 2 organizations, 12 synthetic students, T-A/T-X, 60 exact-version assignments;
- authority oracle: CourseEditorScope=1, TeacherAssignmentScope=1, ProcessingContextVersion=1, CourseContextBinding=1;
- state journey `NotStarted → Submitted → NeedsRevision → Submitted → Accepted`;
- 2 submissions, 2 reviews, 2 annotations;
- exact progress: `Accepted=2 / NeedsRevision=2 / Submitted=2 / NotStarted=54`;
- четыре protected originals сохранили порядок, размер и SHA-256;
- T-X получил `403` на work и каждый из четырёх originals;
- wrong org/course/group/role, missing/forged parent/file, revoked authority, current restrictions, counts/pagination, stale/CAS/duplicate paths покрыты свежим main E2E и opt-in negative suites с положительными controls.

Main TRX: `test-results/tc-076-independent-rereview.trx` — 1/1 PASS. Oracles: `e2e-journey.json`, `E03-authority-oracle.json`, `backup-restore/{api-restart,postgres-restart,restore}.json`.

## 4. Независимое восстановление

| Сценарий | Expected | Actual | Результат |
|---|---|---|---:|
| A. Full DB + private files | другая пустая DB/root; restore/history/auth/bytes совпадают | `createdb=0`, `pg_restore --exit-on-error=0`; 17 migrations; 2 versions; 60 assignments; Accepted; 2 submissions/reviews/annotations; 4/4 hashes; student/teacher reads; T-X denied | **PASS** |
| B. Files-only | exit 42 до mutation; DB отсутствует; storage пуст | validator `42`, reason `PostgreSQL backup payload is missing or is a symlink`; DB absent before/after; empty root; `createdb`/`pg_restore` не вызывались | **PASS** |
| C. Incomplete manifest | exit 42 до mutation; отсутствие partial restore | validator `42`, reason `capture.completedAt must be an ISO-8601 UTC value ending in Z`; DB absent before/after; empty root; `createdb`/`pg_restore` не вызывались | **PASS** |
| Дополнительный DB-only | protected bytes должны fail explicitly | четыре protected reads вернули 500, status `EXPECTED_FAILURE`, без placeholder/silent success | **PASS** |

Новый manifest SHA-256: `f3b16d163c5377853bf69042e15c6e8aeb5aa6588493b32929ea715b856af310`; dump SHA-256: `c16a4ab3aac9615503982c431ef044b0694838b299a2148ed2ad1799a88882f3`; private aggregate: `b97c845db0499b45c095cccb11aadabafdacefc80a127c0047406bf846b44dc6`.

Scenario B первый раз полностью дал ожидаемый restore verdict, но outer `tee` не смог создать transcript до создания каталога. Ошибка evidence-capture сохранена в `negative/files-only/first-attempt-log-wrapper-failure.txt`; B повторён только для полного transcript и снова дал exit 42 с теми же отсутствующими targets.

## 5. Regression, secrets и synthetic review

- Full .NET: Domain 43, Application 369, Infrastructure 343, API 385 PASS; 0 FAIL. Обычный API run показал 12 ожидаемых opt-in method SKIP.
- Все 12 opt-in browser methods развёрнуты последовательно: 15/15 cases PASS, 0 FAIL, 0 SKIP, включая основной M1 E2E.
- Vitest: 421/421 PASS.
- Content compiler: 74/74 PASS.
- Content importer: 49/49 PASS.
- React production build: PASS, 302 modules.

Первый OrganizationBootstrap opt-in attempt завершился exit 127 до теста, потому что после длительного solution run исчез временный `/tmp/lms-dotnet`. Failure сохранён; официальный installer восстановил точно SDK 10.0.401 в отдельный `/tmp/tc076-dotnet`, corrected run — 1/1 PASS. Это раскрытая configuration retry, не скрытая замена test failure.

High-confidence scan tracked/untracked source и нового evidence проверил private keys, bearer/JWT, cloud tokens и credential URLs. В новом evidence совпадений нет. Единственное source match — intentional negative fixture `https://user:pass@example.test` в compiler test; это не credential. Keyword triage не выявил сохранённых token/password values. Synthetic provenance: P01–P12, T-A/T-X, `m111-*`, две демонстрационные организации, fixture ZIP и `a4-1.png`/`a4-2.png`; real-child data и production secrets не найдены. Consumed synthetic activation codes не являются пригодными credentials.

## 6. Отклонения и ограничения

1. Harness по-прежнему записывает remediation-era metadata `evidencePurpose=M1-11-EVIDENCE-FIX` и prefix `m1-11-evidence-fix` в свежем backup manifest. Это неточная метка, но не подмена evidence: новый root, UTC timestamps, unique backup/database IDs, dump/private hashes и command logs отличаются и привязаны к этому re-review. Runtime/test code не менялся.
2. Solution API suite занял 1h16m; exit 0 и четыре TRX сохранены. PTY исчез после завершения, но финальный log/exit/TRX подтверждают результат.
3. Chromium emulation не является physical tablet/Safari evidence. Off-host backup, RPO/RTO, key escrow, production TLS/secrets, G02/legal/compliance и pilot остаются вне TC-076.
4. TC-071 остаётся `REFERENCE UNAVAILABLE`; owner waiver действует только для M1 и не является visual PASS.

## 7. Guardrails §7

- Actor/use case/invariants/states: **covered** — §§1–4.
- Accepted S2/S3/PRS1 и закрытые gates: **covered** — contracts не менялись; boundaries сохранены.
- Client authority/IDOR, wrong org/course/group/role: **covered** — main E2E + opt-in positive/negative controls.
- Lists/counts/pagination: **covered** — exact 12×5 и scoped/foreign negative suites.
- Attachments/parent/history: **covered** — four originals, forged/missing/cross-scope denials, immutable two-attempt history, restore hashes.
- Stale authorization/revoke: **covered** — fresh login after revoke denied; T-X denied before/after restart/restore.
- Transaction/lock/CAS/retry/unknown outcome: **not changed**; existing submit/review lost-ACK and duplicate/CAS assertions passed. Restore validates before target mutation.
- Unsafe restriction narrowing: **covered** — restriction negatives passed; no code/config change.
- Secrets/real data: **covered for synthetic evidence scope** — scan and provenance review PASS.
- Unsupported readiness claims: **covered** — no acceptance/pilot/production claim.

## 8. Заключение и подпись

**TC-076 RE-REVIEW: PASS.** Исправленные evidence baseline и remediation set воспроизведены, новый Chromium HTTPS lifecycle выполнен с normal auth, restart/restore и A/B/C drills дали ожидаемые результаты, regression/opt-in suites учтены, secrets/synthetic review пройден. Исторический `TC-076: FAIL` остаётся неизменным и не переписан.

Это заключение передаётся владельцу для отдельного решения по **TC-077**. Оно не объявляет `SYNTHETIC MVP ACCEPTED`, не закрывает TC-077, не авторизует pilot/production и не превращает TC-071 в PASS.

- Reviewer signature: **/s/ OpenAI Codex — TC-076 Independent Re-Review task**
- Signature form: electronic task attestation; not a human or cryptographic signature.
- Signed at UTC: `2026-10-14T03:13:18Z`.
- Machine summary: `codex/evidence/M1-11/independent-rereview-20261014T0130Z-codex/test-summary.json`.
- Signature record: `codex/evidence/M1-11/independent-rereview-20261014T0130Z-codex/reviewer-signature.txt`.

Readiness label: **TC-076 RE-REVIEW PASS · TC-077 OWNER DECISION OPEN · SYNTHETIC MVP NOT DECLARED ACCEPTED · PILOT NOT AUTHORIZED**.
