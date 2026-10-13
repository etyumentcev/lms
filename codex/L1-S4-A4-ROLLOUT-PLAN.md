# L1/S4-A4 — Legacy transition and rollout

2026-10-10. Documentation only; synthetic fixtures only. **EXISTING** — подтверждено исходным кодом; **ACCEPTED** — принятая норма PRS1; **PROPOSED** — решение A4, ещё не принято; **NOT PROVEN** — обязательство будущей проверки, не результат теста.

## Безопасная последовательность — только PROPOSED

Все существующие rows legacy независимо от `prs1/`, маски и даты. Разрешение A4 не выполняет ни один шаг ниже. Источник принятой нормы: [contract §5–6](L1-S4-A3-SELECTOR-CONTRACT.md); actual old reader — [PostgresCurrentProcessingGate.Allows](../src/Lms.Infrastructure/PostgresCurrentProcessingGate.cs), проверяет только caller/student. Поэтому selector overdeny старого reader недостаточен для полного affectedSubjects.

Рекомендация: **никакого concurrent serving старых incompatible readers/writers** в первом cutover. Feature off означает новый full-subject conservative adapter; rollback на старый binary разрешён только при доказанном покрытии поддерживаемых операций. Иначе maintenance/deny, не восстановление потенциального обхода.

1. Inventory synthetic metadata: все serving binaries, DB roles, jobs, writers, replicas, API bypass routes, schema lineage, restrictions/bindings/subjects; никакого real content. Согласованный backup/restore и baseline row counts/checksums. Unknown mapping/capability → stop.
2. После отдельного разрешения поставить additive schema с exact feature OFF. Старые roots не изменяются; никакого backfill sidecar/birth. Проверить permissions/pair guards/restricted-role tests. Expansion сама по себе ничего не сужает.
3. Подготовить full-subject conservative adapter, supported complete plans и org lock protocol для всех разрешённых participants. Неподдерживаемые content/Message/list/files deny. Дренировать/отключить старые paths, отозвать writer credentials; stale cached binary не должен продолжать serving. Process inventory + DB role enforcement, не только feature flag.
4. Только после synthetic verification разрешить new PRS1 creation при conservative serving; old readers, если остаются в offline harness, должны видеть Active root и overdeny, не читать sidecar вместо root. Exact feature OFF по default при startup/restore/unrecognized version.
5. Explicit owner-admin decision на **каждый** новый/reissued RestrictionId с утверждённым payload/evidence. Перед reissue проверить readiness exact reader и coverage; если нет — сохранить old active. Одним commit создать replacement+provenance+receipt/audit и withdraw old CAS. Никакой separate retire-then-create sequence.
6. Exact activation только для доказанного synthetic component scope, всех readers/writers и source contracts. Missing feature readiness/config/provenance → conservative либо whole deny. Mixed legacy+PRS1 всегда OR. Не включать automatic migration по org flag.
7. Наблюдать secret-free failure reasons/receipt integrity; crash не вызывает автоматически narrowing, reissue, old withdrawal или feature enable. Фактический rollout и operational automation не входят в A4.

## Пять процедур

| Сценарий | Процедура и безопасный outcome |
|---|---|
| Empty synthetic DB | Проверить реально пустой approved synthetic environment; установить схему только отдельным slice, создать explicit org/context/course fixtures, новый admin decision, issue через protected writer. До acceptance reader conservative; пустота не evidence provenance. Empty roots отсутствуют — никаких guessed owners |
| Existing synthetic legacy | Зафиксировать IDs/immutable fields/states/version; все active legacy сохраняют запреты. Prefix похожий на PRS1 ничего не меняет. Reissue per-row под org mutex/CAS и receipt; old history сохраняется. Отсутствие решения → продолжить conservative |
| Mixed readers/writers | Сначала compatibility inventory + subjects/plans/locks. E3 old reader не поддерживает full subjects, значит не допускать его к новым affected operations. Drain/fence несовместимых writers до cutover; old writer с прямым DML блокирует activation. New reader классифицирует old writes legacy. Unsupported future profile conservative. Proven compatible conservative readers допустимы только после explicit tests, не по предположению |
| Failed/interrupted rollout | Оставить/вернуть exact OFF; сохранить schema/history/active roots. Если нет full-subject conservative adapter или writer fencing — остановить serving. Не делать down migration/drop sidecar/auto-reactivation withdrawn rows. Resume после inventory, consistent restore check, receipt reconciliation и повторной readiness approval |
| Unknown transaction outcome | Сохранить command ID/digest/new+old IDs, не success и не blind retry. Authoritative primary + org lock после old transaction, прочитать durable receipt. Committed → не повторять effects; absent при доказанной continuity → полный retry того же command после проверки decision/CAS; uncertainty → blocked. Не создавать replacement с другим ID на основании timeout |

## No-gap proof и rollback

Под org lock reader видит либо precommit old Active (replacement невидим), либо postcommit new Active/old Withdrawn. FK/deferred pair и audit в той же transaction. Crash между INSERT new, sidecar и UPDATE old до commit откатывает всё. Crash после commit не отменяет new. Concurrent revoke/reissue с одинаковым expectedVersion: один wins, второй conflict; rollback второго не снимает первый запрет. Проверять обоих участников и final rows, а не только returned status.

Rollback exact policy переводит **все active rows** в conservative interpretation по полному context/subject; не удаляет PRS1 sidecar и не переписывает selector. Если narrowing уже explicit committed, withdrawn legacy не восстанавливается автоматически: новая active PRS1 становится conservative по своим subject/context. Возврат прежней более широкой subject/context политики — отдельное административное решение/new restriction, не молчаливая переинтерпретация истории.

Consistent database backup восстанавливается целиком вместе с decisions/receipts/births/sidecars. Partial restore или replica lag не proof current authority; admission остановлен до reconciliation. Birth ledger не заполняется восстановителем из prefix. Unknown ledger/integrity → conservative/deny. При restore возможна потеря acknowledged transactions вне локальной durability модели: это отдельная infrastructure qualification, **NOT PROVEN**, не автоматическая reissue.

## Release gates

Schema/role review; canonical grammar tests; immutable history/late-attach negative tests; all 29 R1 cases; concurrency barriers и commit-loss proof; full subject/source evidence; S2/S3 lock graph; exact disabled default; rollback drill; human audit и SR-01–SR-04. File/storage/HTTP/real data/G02 остаются отдельными закрытыми границами. Прохождение future component tests не даёт разрешения S4 HTTP или production rollout.


**S4-A4 READY FOR OWNER DECISION · PRS1 ACCEPTED · IMPLEMENTATION NOT AUTHORIZED · T04/E03 UNCHANGED · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**
