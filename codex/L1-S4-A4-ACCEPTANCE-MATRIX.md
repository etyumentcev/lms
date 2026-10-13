# L1/S4-A4 — Acceptance and verification design

2026-10-10. Documentation only; synthetic fixtures only. **EXISTING** — подтверждено исходным кодом; **ACCEPTED** — принятая норма PRS1; **PROPOSED** — решение A4, ещё не принято; **NOT PROVEN** — обязательство будущей проверки, не результат теста.

## Статус и harness

**ACCEPTED:** все 29 дополнительных R1 cases из [normative matrix](L1-S4-A3-SELECTOR-ACCEPTANCE-MATRIX.md) сохранены ниже без изменения oracle. **PROPOSED / NOT IMPLEMENTED / NOT RUN:** тестовые реализации и injections этого документа. Runtime tests в A4 не запускались; старые A2 результаты не выдаются за новые.

Fixtures synthetic: org A/B; course C/D, string group G1/G2; student U/V, teacher T, message author X; assignment A1 frozen G1; W1: S11→R11(NeedsRevision)→S12→R12; W2 sibling; M11 feedback R11, M0 ordinary с trusted fixture manifest; F11 physical S11, FM physical M0, FP pending. Во всех positive controls token/generation/clock/context/binding/ownership/domain state валидны. ALLOW* означает только selector pass при всех остальных guards. D = DENY без выдачи данных/effects; C = conservative deny по applicable context+subject; Reject = атомарно без изменений старой row.

Компоненты: P parser/plan, R dependency/subject resolver, V provenance verifier, E pure evaluator, T transaction adapter, W protected writer, Q projection/prefilter. Типы U pure unit/property; I real PostgreSQL integration; C two-connection deterministic concurrency; A synthetic component adapter. Dependences: S candidate schema/roles/immutable guards; H исторические domain FK/triggers; X complete source manifests; L общий org lock и registered writers; F fault-capable primary/connection proxy. Files во всех тестах только metadata/fake storage, storage API spy ожидает **0 calls**; ComponentAuthorization.Attachment остаётся deny.

Negative persisted-corruption cases, которые DB запрещает, имеют две отдельные проверки: normal restricted-role INSERT Reject, затем pure verifier fake snapshot → C/D. Isolated owner corruption fixture допустима только synthetic disposable DB; не отключать production constraints ради теста. Каждый test проверяет нужную reason branch, чтобы unrelated JWT/scope denial не маскировал ошибку.

## Все 29 R1 cases

| ID | Инвариант / компонент | Fixture и oracle | Тип; injection | Зависимости |
|---|---|---|---|---|
| RB01 | Reserved rows нельзя narrow; V/E | Active mask 8,16,24,9,31, unrelated selector, matching subject/context → C для Read/Submit/Review | U+I; inject persisted reserved rows, exact sidecar fake | S |
| RB02 | OR запретов; E | Active reserved + valid exact nonmatch + withdrawn row → C | U+A; менять order rows и withdrawn state | S |
| RB03 | Unknown plan/mask fail-closed; P/V/E/W | required 32 → D; new mask 0 → Reject; persisted 0 → C | U+I; parser/row corruption doubles | S |
| RT01 | Исторический parent сохраняется; R/E | После S12 читать R11 при restriction S11 → D | U+I+A; latest pointer/order переставить | H+X |
| RT02 | Sibling isolation; R/E | S11 restricted, standalone S12/R12 → ALLOW*; W1 restriction → D для обеих | U+I; обе ветви и all-source work control | H+X |
| RT03 | Review parents согласованы; R | Review.WorkId=W2 при S11.WorkId=W1 либо missing S11 → D | U+I; fake broken FK, real FK rejection | H |
| RT04 | Domain guard не снимается exact pass; T/domain | Review S11 после S12 либо duplicate review S12 → D, no review/event | I+A; stale ID и duplicate command with different ID | H+L |
| RT05 | AttemptNumber не identity; R | ID отсутствует, номер старый/duplicate/wrong work → D ambiguity; never latest alias | U+I; malformed chain, FK/unique rejection | H |
| N01 | Derived metadata не обходит child; Q/E | S11 hidden, полный AttemptCount/status/LastReviewedAt запрошен → D unsafe projection | U+A; projection descriptor опускает source S11 | X+H |
| N02 | Feedback через Message остаётся review source; R/Q | Restrict R11/S11, запрос M11 напрямую и ReplyTo quote → D | U+I+A; bypass work-only route | X+H |
| N03 | Association completeness; R | wrong-work/missing/две reverse Review.MessageId associations → D | U+I; corruption doubles + orphan manifest | H+X |
| N04 | Annotation проверяет review AND file; R/E | R11 allowed, F11 restricted → D annotation/thumbnail, no storage calls | U+A; strip attachment dependency | X+H |
| N05 | Annotation не переносит attachment; R | R12 references F11 from S11 → D | U+domain/I; invalid annotation JSON/fixture; no reparent | H |
| AF01 | Physical parent XOR; R | F11 имеет SubmissionId и MessageId → D | U+I; fake dual-parent + CK_StoredAttachment_Link rejects real insert | H |
| AF02 | Reciprocal/owner integrity; R | F11 нет в S11.AttachmentIds либо OwnerId≠SubmittedBy → D | U+I; corrupted metadata/owner; no course fallback | H+X |
| AF03 | Review не physical parent; P/R | воображаемый ReviewId/file parent → D unsupported | U+A; unsupported input descriptor | H |
| AF04 | Pending admission не downstream; R/T | FP expired/ineligible/missing scope, attach → D; no link writes | U+I metadata; advance clock/remove scope | H+L |
| AF05 | Rebinding не обход; R/T | Перенос F11 в S12/M0 → D без separately approved workflow | U+I; attempt update/second claim | H+L |
| AF06 | Reply не physical parent; R/Q | FM parent M0→W1 остаётся; M0.ReplyTo=M11, выдача quote отдельно D при R11 restriction | U+I+A; omitted quote dependency | H+X |
| PV01 | Prefix не attestation; V/E | canonical prs1 mask1 unrelated, нет sidecar → C | U+I; legacy до/после schema install | S |
| PV02 | Immutable equality всех snapshot fields; V | По очереди org/context/origin/ref/mask (+subject) mismatch → C | U+I; fake mismatch; real FK/trigger Reject | S |
| PV03 | Уникальность, pinned revision, evidence; V | duplicate sidecar / unknown revision / missing trusted decision → C | U+I; fake duplicate, PK/check failures, bad evidence | S |
| PV04 | Legacy OR exact nonmatch; V/E | active opaque/null/reserved legacy + exact nonmatch → C | U+I+A; row order randomization | S |
| PV05 | Только active prohibition, no cancellation by other row; E | withdrawn legacy + matching active PRS1 → D; active legacy + withdrawn PRS1 → C | U+I; both states and ordering | S |
| PV06 | Pair atomicity; W/T | Exception после root до provenance → Reject, ни root/receipt/audit; discovered legacy missing sidecar → C | I+C; fail insert/deferred commit, kill connection | S+L+F |
| PV07 | No-gap recovery; W/T | Reissue crash/rollback/unknown ack: old active либо new active+old withdrawn, never neither | I+C; barriers после каждого DML, commit ack drop | S+L+F |
| PV08 | Infrastructure/subject uncertainty deny; V/R/T | provenance read exception/context unknown/incomplete subjects → D whole operation | U+I+A; connection loss, duplicate/missing binding | S+X+F |
| PV09 | No prefix backfill/untrusted attestation; W | existing canonical legacy поздний sidecar либо forged Actor/DecisionRef → Reject | I+A; direct DML under app role; late txid; forged capability | S+L |
| PV10 | Mixed readers require complete subjects; rollout | author X restricted, old caller/student reader cannot see X → exact activation blocked, old route fenced | A+deployment harness; start stale binary/writer credentials | X+L+S |

## Конкурентные сценарии и proof obligations

Тесты используют barriers/latches вокруг получения org lock, locked read, pre-DML, precommit и ack, а не sleeps. Для каждого запускается обратный порядок contenders; независимые DbContext/connections. Наблюдать committed rows/history/receipt, отсутствие unauthorized response, no external calls и lock ownership. Timeout теста сам по себе не доказывает deadline G02.

| ID | Schedule / компоненты | Expected oracle | Fault injection / dependencies |
|---|---|---|---|
| CC01 | Issue restriction во время Submit; W/T | Issue lock+commit first → Submit D/no effects; Submit first → его commit до restriction, следующий check D | Barrier после evaluation и attempt competing lock; L+S+H, C |
| CC02 | Withdraw restriction во время Review; W/T | Review first с active restriction D; withdraw first → selector может pass, domain guards сохраняются | Both orders, rollback revoke, L+S+H, C |
| CC03 | Binding switch одновременно с authorization; T | Только цельный old/new binding; interval пересекает boundary → D; ContextId switch без transfer decision Reject | Barrier после lock, clock crossing, duplicate binding fake; L+S, C/U |
| CC04 | Reissue падает между root/sidecar/withdraw/audit; W/T | До commit old Active; после commit replacement Active, old Withdrawn; ни одного allow gap | Kill backend/statement faults каждой позиции, rollback; L+S+F, C |
| CC05 | Commit acknowledgement loss; T | Indeterminate, не success/no blind retry; receipt reconciliation под org lock даёт ровно один effect | Proxy drop server ack, delayed backend completion, same-ID concurrent retry; L+S+F, C |
| CC06 | Mixed legacy/PRS1 issue/revoke; V/E/T | Любой applicable active legacy побеждает exact nonmatch; withdrawal одной не снимает другую | Concurrent insert, randomized order, unknown profile; L+S, C/U |
| CC07 | S3 move G1→G2 одновременно с Submit/Review; R/T | Frozen A1 stays G1, G1 restriction D; new assignment captures цельный generation; no mixed scope | `s3_change_authority` barrier; assignment capture path; L+H, C |
| CC08 | Несколько Submissions W1 и новый attempt; R/T | S11 restriction не переносится на S12, W1 covers обе; parent aggregate unsafe D, latest domain rule enforced | Concurrent duplicate submits/CAS; shuffled AttemptNumber fake; L+H+X, C/U |
| CC09 | Feedback Message/ReplyTo при concurrent review create; R/Q/T | Читается цельная association или D; no work-only feedback disclosure | Pause между Message и Review insert внутри одной tx; L+H+X, C |
| CC10 | Incomplete affectedSubjects; R/P | Отсутствующий X/embedded source/unknown Markdown → D независимо restrictions list | Drop source, fake completeness, graph cycle/limit, unsupported projection; X, U/A |
| CC11 | Full lock graph S2/S3/PRS1 | Issuer security→session→org; S3 org→scope; PRS1 never org→security; deterministic deadlock victim no success | Concurrent Issue/Rotate/Move/PRS1; forced timeout/deadlock; L+F, C |
| CC12 | Stale snapshot / nonparticipant writer | Pre-lock RepeatableRead adapter rejected; direct role DML rejected; no stale allowed mutation | Begin old snapshot, writer bypass credentials, trigger-side lock inversion fixture; S+L, I/C |
| CC13 | Unknown outcome after restore/replica ambiguity | Не объявлять receipt absence rollback; admission blocked until authoritative reconciliation | Lagging replica, changed DB lineage, incomplete restore; S+F, I/harness |

## Базовая матрица и regression scope

29 R1 cases дополняют, не заменяют старые cases: F01–F06 parser canonical property vectors (каждый kind/all, Unicode byte differences, 4096/4097 и 1024/1025); O01–O05 org/binding integrity; B01–B07 49 combinations masks/plans плюс invalids; H01–H07 reflexive subtree/frozen group; A01–A05 metadata-only + unchanged Attachment denial; S01–S06 full subjects/OR; L01–L05 reference candidate-set oracle до count/pagination; V01–V06 compatibility/reissue; C01–C05 current-state and lifetime limits. Fixture/oracles исходной matrix обязательны. В минимальном slice unsupported projection/list cases ожидают whole deny, но их positive safe-projection acceptance остаётся NOT IMPLEMENTED и не объявляется пройденной.

S2/S3 regression: session rotation/replay/unknown commit; membership/enrollment generations and move; immutable roots; token scope checks; T04/E03 arithmetic; existing conservative legacy denial. Выбирать фактические test names при реализации, не придумывать новые выполненные результаты. Fault-injection evidence хранит schedule, SQLSTATE, tx outcomes, IDs/digests без content/secrets и exact git revision/schema profile.

## Доказательства, необходимые для приёмки

P1 schema/ACL делает late attestation невозможной для app role; P2 same-tx pair + decision matches payload; P3 все policy-affecting writers входят в mutex; P4 graph/subjects complete для каждого enabled projection; P5 pure evaluator = prefilter; P6 unknown outcome не создаёт второй effect; P7 S2/S3 lock order не инвертирован; P8 rollback/fencing сохраняют conservative protection. Все P1–P8 **NOT PROVEN** до отдельно разрешённых тестов. Human audit/SR-01–SR-04/G02 не закрываются этой таблицей.


**S4-A4 READY FOR OWNER DECISION · PRS1 ACCEPTED · IMPLEMENTATION NOT AUTHORIZED · T04/E03 UNCHANGED · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**
