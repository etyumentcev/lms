# L1/S4-A4-I2-P — isolated schema validation results

2026-10-10. Выполнено по прямому owner authorization I2-P; [решение зарегистрировано](L03-OWNER-DECISION-REGISTER.md). **Кандидатный DDL выполняется, но полная trusted writer boundary и обязательная atomic pairing не доказаны. Для I2 нужны исправления и отдельное разрешение.** Runtime/schema source не изменены.

## Среда и воспроизводимость

Canonical directory `/home/tea/github/lms`, ветка `codex/l1-s4-jwt-api-authorization`, HEAD `492e30aef65bbddd24f107903bfaa54a563abe39`. PostgreSQL 17.6, локальный image `postgres:17.6-alpine`. Каждый запуск создаёт новый `docker run --rm --network none --tmpfs /var/lib/postgresql/data:rw`; без published ports, host mounts и подключения к существующей БД. Только вымышленные UUID/строки. Контейнер удалён, cleanup exit 0; данные находились в tmpfs. Sandbox launcher вернул `mountinfo path is not absolute`; команды выполнены через разрешённый escalation.

[Harness](evidence/L1-S4-A4-I2-P/probe.py) извлекает SQL block из исходного schema document без правок. [Candidate SQL](evidence/L1-S4-A4-I2-P/candidate.sql), [prerequisites](evidence/L1-S4-A4-I2-P/prerequisites.sql), [ACL overlay](evidence/L1-S4-A4-I2-P/acl-overlay.sql), [полный transcript SQL/SQLSTATE](evidence/L1-S4-A4-I2-P/results.json), [cleanup](evidence/L1-S4-A4-I2-P/cleanup.json) сохранены в репозитории. Повтор: `python3 codex/evidence/L1-S4-A4-I2-P/probe.py` из canonical directory; использует Docker и `/tmp/lms-s4-a4-i2-p`, не редактирует source.

Prerequisite schema — **минимальная проекция**, не запуск всех EF migrations: FK targets и ключи, restriction CHECK/FK, точный `s1_stable_key` и `s1_restriction_provenance` из M1. Полная совместимость миграции с существующей моделью, SecurityAudit и domain tables этим не доказана. Candidate создаётся под `prs1_owner NOLOGIN`, не superuser; setup/check observer — postgres. Роли app/reader/writer без superuser/createrole/bypassrls и без membership owner. ACL overlay явно отличается от candidate: reader получает SELECT, serving roles только schema USAGE; writer procedures отсутствуют и не реализованы. SQL sessions используют SET ROLE для проверки эффективных privileges, а не production credentials.

## Фактические результаты

117 probes (включая setup, наблюдения и дефектные counterexamples) совпали с ожидаемыми исходами; это **не 117 security acceptance tests**. Первый проход уточнял harness, окончательный отдельно расширил UPDATE/DELETE/TRUNCATE ACL и mutation receipt checks. Результаты запусков не суммируются.

| Проверка | Наблюдение |
|---|---|
| Unmodified DDL | Применён полностью без SQL errors |
| Issue и Reissue positive controls | Полная пара commit; Reissue завершает old Withdrawn/version2 и new Active/version1 |
| Immutable history | UPDATE/DELETE provenance, decisions, commands, births и непустого mutation receipt отвергнуты SQLSTATE 23514 |
| Late attestation | Legacy, созданный до DDL, и legacy отдельной transaction после DDL: `prs1_late_attestation`, 23514 |
| Snapshot equality | Изменённый ResourceRef: `prs1_snapshot_mismatch`, 23514; остальные поля по отдельности не перебирались |
| Decision digest mismatch | `prs1_decision_mismatch`, 23514 на commit |
| Missing sidecar | Deferred FK отвергает commit (23503), root отсутствует после rollback |
| Failed Reissue | Без old withdrawal commit отвергнут 23514; old остаётся Active, new отсутствует |
| Restricted roles | Все INSERT/UPDATE/DELETE/TRUNCATE на шести защищаемых таблицах отвергнуты 42501; trigger disable и прямой вызов birth function также запрещены |
| Reader | SELECT успешно; owner membership отсутствует |
| Direct grant compatibility | Ранее выданный app INSERT на restrictions переживает candidate REVOKE PUBLIC; app вставляет root и SECURITY DEFINER trigger создаёт birth |

## Дефекты и точные предложения

### D01 — ACL deployment неполон, existing serving grant обходит boundary (blocking)

Candidate не создаёт роли, не назначает ownership и не отзывает прямые/унаследованные grants serving roles; `REVOKE ... FROM PUBLIC` не эквивалентен этому. Воспроизведение: `GRANT INSERT ON "ProcessingRestrictions" TO prs1_app`, candidate revoke, SET ROLE app, root INSERT → commit. Это не обход корректного hardened overlay: без grant app получает 42501. Это незакрытый deployment obligation, прямо предусмотренный исходным документом.

Предложение: inventory всех serving principals и role memberships; owner NOLOGIN без membership приложения; schema CREATE и object ownership только доверенному owner. Для каждого serving principal отозвать INSERT/UPDATE/DELETE/TRUNCATE на restrictions и всех пяти новых таблицах, в том числе у inherited grant roles; column grants проверять отдельно. Закрыть default PUBLIC EXECUTE до создания routines через `ALTER DEFAULT PRIVILEGES FOR ROLE prs1_owner REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC`; trusted routines получают explicit EXECUTE только у назначенного вызывающего role. Добавить catalog assertions на effective privileges и отрицательные проверки после upgrade с существующими grants. Не выдавать serving role DDL/owner/TRIGGER/replication privileges.

### D02 — trusted writer отсутствует; digest equality не проверяет полномочия и payload (blocking)

Под owner полная пара с `ApprovedCommand={}`, произвольным непустым Evidence, строкой `canonical` вместо PRS1 и одинаковыми произвольными 32 bytes digest успешно commit. SQL обеспечивает equality байтов, не SHA-256 фактического canonical payload; Actor FK не свидетельствует о полномочиях. Owner fixture — демонстрация отсутствующих checks, не утверждение, что restricted app сейчас может записать эти строки. В overlay writer fail-closed, но выполнить полезную команду через trusted entry point невозможно.

Предложение будущего I2: отдельно реализовать Issue/Reissue/Withdraw entry points с pinned authenticated administrative capability, reject недоказанной authority, принятой I1 grammar, nonzero IDs, same-org resource/context/subject checks. Сравнивать typed ApprovedCommand с фактическими IDs/ref/mask/expected version, пересчитывать versioned SHA-256 canonical serialization внутри доверенной boundary; DecisionRef/Evidence извне не считаются proof. Owner и fixed search_path routines не должны разрешать подмену объектов через writable public/temp. В acceptance добавить forged capability/Actor/DecisionRef, malformed payload и same digest/different fields. До этого P2 trust не закрыт.

### D03 — atomic pairing условна наличием receipt; root-only commit допустим (blocking для PRS1 writer)

Наличие Prs1Commands связывает receipt/sidecar/root, неполная пара откатывается. Но самостоятельный restriction INSERT успешно commit как legacy. Prefix не является discriminator и не может им стать: это изменило бы принятую legacy semantics. Ошибка writer, забывшего receipt и sidecar, не выявляется этим DDL. SecurityAudit не связан candidate constraints и не проверялся.

Предложение: сохранить legitimate legacy path; закрыть весь прямой serving DML и предоставить отдельные проверенные legacy/PRS1 entry points. PRS1 writer в одной owned transaction обязательно создаёт decision/receipt/root/sidecar/audit, проверяет affected row counts, завершает deferred checks перед commit. Если требуется независимая DB гарантия даже против дефекта writer, ввести защищённую explicit birth classification/obligation, устанавливаемую только trusted entry point, и deferred check обязательной пары/audit для PRS1 birth (никогда не выводить classification из prefix). Проверить fault после каждого DML и backend termination; positive legacy control обязателен.

### D04 — receipt проверяет итоговое состояние, а не факт разрешённого перехода (blocking)

После успешного Withdraw старой row Active/v1→Withdrawn/v2 новый CommandId с ExpectedOldVersion=1 и тем же OldRestrictionId успешно commit **без UPDATE**. Кроме того owner может вернуть row в Active/v1 без receipt: существующий stable-key trigger не защищает State/Version. Reissue positive control проверен отдельно после этого counterexample.

Предложение: writer под org lock и row lock проверяет Active и точный expected version, выполняет `UPDATE ... SET "State"='Withdrawn', "Version"="Version"+1 WHERE "RestrictionId"=:id AND "State"='Active' AND "Version"=:expected RETURNING ...`; ровно одна row обязательна. Ноль → reject всей transaction. Idempotent retry допустим только по прежнему CommandId с exact payload match, не новым receipt. Для защиты от дефекта writer добавить DB monotone transition guard и transaction-bound transition receipt, связывающий old/new versions и command; deferred check должен доказывать переход в этой transaction, а не совпадение конечных значений. Проверить duplicate command, stale expected, withdrawn→active и version decrement. Proposed SQL здесь не применялся.

### D05 — row-level immutable trigger не защищает TRUNCATE (hardening)

Непустой Prs1MutationReceipts под owner успешно TRUNCATE, count=0 до ROLLBACK. Restricted roles TRUNCATE получить не могут. Это ограничение привилегированного владельца, не текущий app bypass.

Предложение: запрет TRUNCATE у runtime roles обязателен (D01). Для защиты от случайного elevated DML добавить BEFORE TRUNCATE FOR EACH STATEMENT guard на immutable tables. Владелец/superuser всё равно способен изменить/снять trigger; не представлять такой guard защитой от hostile database administrator. Owner credentials не выдавать runtime.

## Граница доказательства и дальнейшие условия

P1 подтверждён только для synthetic overlay и проверенных late-attestation schedules. P2 подтверждён частично для структурной пары при наличии receipt, не для trust/payload/audit и не для отсутствующего receipt. P3–P8 полностью не доказаны. Все R1/PV cases целиком не объявляются пройденными: проверены лишь перечисленные SQL obligations PV02/03/06/07/09. Полный resolver/evaluator, reader fallback, org mutex, two-connection races, lock-order, terminate/kill/crash, dropped commit ack, failover/restore, SecurityAudit atomicity и end-to-end trusted writer **NOT RUN / NOT IMPLEMENTED**. Изолированные single-transaction commit/rollback probes не заменяют CC01–CC13 или G02.

Application regression принят владельцем по I1 отчёту; здесь повторно не запускался, поскольку Application/runtime не менялись. Candidate document сохранён byte-for-byte с историческим NOT EXECUTED; актуальное выполнение зафиксировано этим новым отчётом. Production migrations, exact gate, API/HTTP, реальные данные, commit/merge/rebase/push/deploy не выполнялись.

## Сохранность

Изменён только append к owner decision register; добавлены этот отчёт и evidence directory. Исходный dirty prefix register проверяется отдельно; остальные исходные tracked/untracked файлы сверены по SHA-256, HEAD/branch/index сохранены. Машиночитаемая проверка — [preservation](evidence/L1-S4-A4-I2-P/preservation.json). Никаких restore/reset чужих изменений не выполнялось.

**I1 ACCEPTED · I2-P VALIDATION COMPLETED WITH FINDINGS · I2 IMPLEMENTATION NOT AUTHORIZED · T04/E03 UNCHANGED · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**
