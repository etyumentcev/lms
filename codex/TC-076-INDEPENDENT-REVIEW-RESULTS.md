# TC-076 — независимая проверка Sapientum M1

Дата проверки: 2026-10-14  
Проверяющий: **OpenAI Codex, отдельный автоматизированный оператор проверки**  
Роль: независимый read-only reviewer/runner; данное выполнение не создавало M1-11 rerun 3, его harness result или `M1-11-RESULTS.md`  
Контакт: текущая задача Codex; внешнего контакта и человеческой подписи нет  
Зафиксированный интервал UTC: `2026-10-14T00:48:46Z` — `2026-10-14T00:49:47Z`  
Итог: **TC-076: FAIL**

## 1. Проверяемая версия и граница

- Repository: `/home/tea/github/lms`
- Branch / commit: `main@688d8cf3305d7739a3cb76824a60ad9d5de3596b`, ahead of `origin/main` by 43 commits.
- Index на preflight пуст; checkout уже содержал перечисленные в `source-identity.txt` tracked/untracked изменения. Они сохранены.
- Source identity для M1-11 — commit плюс незакоммиченные harness/docs-файлы, перечисленные в review package.
- Только synthetic data. Runtime-код, исходный `rerun3-688d8cf`, матрица и прежние результаты не изменялись.
- `PRS1 EXACT OFF · G02 BLOCKED · S4 HTTP NOT AUTHORIZED` сохранены. Commit, push и deploy не выполнялись.

Окружение: Linux `7.0.0-38-generic`, Docker client/server `29.8.1`, .NET SDK `10.0.401`, Node `v24.19.0`, pnpm `11.25.0`, Python `3.14.4`, Google Chrome `154.0.8037.57`, Git `2.53.0`, `sha256sum` uutils `0.8.0`. Полный вывод: `codex/evidence/M1-11/independent-20261014-codex/versions.txt`.

## 2. Проверенные артефакты и SHA-256

Алгоритм из package выполнен из repository root:

```bash
find codex/evidence/M1-11/rerun3-688d8cf -type f -print0 | sort -z | xargs -0 sha256sum | sha256sum
```

Проверено 39 зафиксированных SHA-256: 6 source/harness файлов, 12 evidence-групп и 21 key artifact. Counts и bytes всех 12 evidence-групп совпали.

| Группа | Зафиксировано | Пересчитано | Результат |
|---|---|---|---:|
| полный rerun 3, 681 файл / 142211716 bytes | `186d4e4a9da3b52210bb9f4c3241c9ed025d9b9917e56eef7c083d9976ef5838` | то же | MATCH |
| `complete-6/` | `8588c77c9c913223e34b6a28e5b4c0153748db282d8491b0d0366e26a67e257d` | то же | MATCH |
| `browser-gaps/` | `b8fa4952925c8ffdc2c61ef9f5847b9f4281148f96cf2431f70038f19573c952` | то же | MATCH |
| `regression/` | `ef6029cb8d8997dd60928ac25557864eb56deb0dc61ae46f28b436ccafe304e8` | то же | MATCH |
| `final-journey-2/`, 51 файл / 9389902 bytes | `8b20a2cbf5ec939efe8365ed953544bf179c11aa3c042ce03db8206c03d9eebbc` | `8b20a2cbf5ec939efe8365ed953544bf179c11aa3c042ce03db8206c03d9eebb` | **MISMATCH** |
| остальные 7 группы | как в package | совпали | MATCH |
| все 6 source/harness файлов | как в package | совпали | MATCH |
| все 21 key artifacts | как в package | совпали | MATCH |

Зафиксированное значение `final-journey-2/` имеет 65 hex-символов и не может быть SHA-256. Фактическое значение имеет 64 hex-символа. Независимо от вероятной редакционной причины это точное несовпадение с review package.

Полный raw output: `codex/evidence/M1-11/independent-20261014-codex/checksum-audit.txt`.

## 3. Сверка acceptance matrix с протоколами

Структурная сверка дала 77 уникальных ID в `SAPIENTUM-M1-ACCEPTANCE-TEST-CASES.md` и 77 уникальных ID в `M1-ACCEPTANCE-MATRIX.md`, без gaps/duplicates. Raw `test-summary.json` соответствует заявленному исходному итогу `74 PASS / 0 FAIL / 3 BLOCKED / 0 NOT RUN`; отдельная owner disposition TC-071 корректно не переписывает raw verdict. Main TRX содержит один интегрированный test с результатом Passed. `e2e-journey.json`, restart/restore JSON и authority oracle подтверждают заявленные core counts, state journey, четыре file hash, T-X denials и DB-only expected failure.

Однако матрица и `M1-11-RESULTS.md` **не полностью соответствуют утверждённой restore-процедуре**:

1. Runbook §7 требует два отрицательных drill: DB-only и files-only/incomplete manifest. Во всём `rerun3-688d8cf` найден только DB-only artifact; `test-summary.json` также учитывает только `dbOnlyRestore`. Evidence files-only/incomplete-manifest отсутствует.
2. `complete-6/backup-restore/manifest.json` не содержит обязательные по runbook/package migration list и capture time. В нём есть `head`, synthetic-only label, dump digest/size, source/restored files и verified summary.
3. Поэтому отдельный TC-066 из acceptance specification подтверждён DB-without-files negative, но более строгая общая процедура runbook и условия TC-076 не подтверждены. Формулировку «backup/restore itself PASS» нельзя использовать как доказательство полного соответствия procedure.

Raw audit: `codex/evidence/M1-11/independent-20261014-codex/matrix-audit.txt`.

## 4. Фактический результат повторения

Раздел 1 review package требует немедленно остановиться и поставить FAIL при любом несовпадении исходных evidence hashes. Stop condition сработал до production build и запуска disposable PostgreSQL/React environment. Повторное использование прежнего PASS не выполнялось.

| Проверка | Результат | Команда / evidence | Вывод |
|---|---:|---|---|
| Source/harness hashes | PASS | `checksum-audit.txt` | 6/6 MATCH |
| Original evidence aggregate | **FAIL** | `checksum-audit.txt` | 11/12 groups MATCH; `final-journey-2/` recorded digest malformed/mismatched |
| Matrix ↔ protocols/raw evidence | **FAIL** | `matrix-audit.txt` | 77/77 structurally present; files-only negative and complete manifest fields absent |
| Production build | NOT RUN | — | обязательный preflight stop |
| Main E2E | NOT RUN | — | обязательный preflight stop |
| 12×5 final oracle | NOT RUN | — | прежний raw oracle изучен, но не принят вместо воспроизведения |
| Negative access matrix | NOT RUN | — | прежний raw evidence изучен, но не принят вместо воспроизведения |
| API restart | NOT RUN | — | прежний raw evidence изучен, но не принят вместо воспроизведения |
| PostgreSQL restart | NOT RUN | — | прежний raw evidence изучен, но не принят вместо воспроизведения |
| Coordinated DB+files restore | NOT RUN | — | прежний raw evidence изучен, но не принят вместо воспроизведения |
| DB-only negative restore | NOT RUN | — | прежний raw evidence изучен, но не принят вместо воспроизведения |
| Incomplete/files-only negative restore | **FAIL / MISSING SOURCE EVIDENCE** | `matrix-audit.txt` | требуемый rerun3 artifact отсутствует |
| Full regression / opt-in browsers | NOT RUN | — | обязательный preflight stop |
| Secrets scan | NOT RUN | — | обязательный preflight stop; clean scan не заявляется |
| Synthetic-data manual review | NOT RUN | — | обязательный preflight stop; real-child-data conclusion не заявляется |

Следовательно, сценарии Bootstrap → Admin → Course → Import → Publish → Assignment → Submission → Review → NeedsRevision → Resubmission → Accepted → History → Progress, org isolation, restart persistence и coordinated restore **не были независимо воспроизведены в этой проверке**. Они остаются NOT RUN, а не переносятся из rerun 3.

## 5. Guardrails §7 и ограничения

- Client authority/IDOR, cross-org, wrong course/group/role, post-pagination leakage, parent/file bypass, stale authorization, mutable history, duplicate effects: **blocked for independent confirmation** — новый E2E не запускался после mandatory stop.
- Transaction owner/isolation/lock/CAS/retry: **not applicable to this report write**; пользовательские/domain writes не выполнялись.
- Unsafe restriction narrowing: **covered** — границы не менялись, runtime/schema/API/security edits отсутствуют.
- Secrets/real data: **not proven by this run** — scanner/manual review не запускались после mandatory stop.
- Unsupported readiness claims: **covered** — `SYNTHETIC MVP ACCEPTED`, `PILOT AUTHORIZED`, compliance/production readiness не объявляются.
- Physical tablet/Safari, off-host backup, RPO/RTO, production TLS/secrets и G02 остаются вне synthetic acceptance.

## 6. Итоговое заключение

**TC-076: FAIL.** Основания:

1. исходный checksum baseline review package не совпадает с пересчитанным evidence и содержит невозможное 65-символьное значение;
2. mandatory stop не позволяет перейти к фактическому независимому воспроизведению;
3. исходный rerun3 не содержит требуемого files-only/incomplete-manifest negative evidence, а backup manifest неполон относительно runbook/package.

Для нового TC-076 требуется отдельное owner/repository reconciliation: исправить review package через явное решение без изменения исходного evidence; определить disposition отсутствующего files-only drill и недостающих полей manifest; затем запустить полный независимый сценарий заново в новом evidence root. Прежний PASS не может быть использован вместо этого прогона.

- New evidence root: `codex/evidence/M1-11/independent-20261014-codex/`
- New evidence manifest file SHA-256: `53cc56c98d357b536148d2c54917998bcc4fcc769b802464dc38552ce82ab65e`
- Подтверждение проверяющего: **OpenAI Codex — электронная аттестация текущей задачи, не человеческая и не криптографическая подпись.**
- Signed at UTC: `2026-10-14T00:49:47Z`

Этот FAIL не меняет исходные evidence, не принимает и не отклоняет Synthetic MVP решением владельца, не закрывает TC-077 и не разрешает pilot, push или deploy.
