# L1/S4-A3 — Proposed selector acceptance matrix

> **Актуальный owner decision, 2026-10-10: PRS1 NORMATIVE CONTRACT ACCEPTED.** [Решение и границы приёмки](L1-S4-A3-OWNER-DECISION.md). Сверка contract §1–6, matrix и normative diff выполнена до фиксации; смысловых расхождений нет. Прежние статусы proposed / NOT ADOPTED / READY FOR OWNER ACCEPTANCE ниже — исторические статусы подготовки и superseded в принятом scope. Нормативные правила и acceptance oracles сохранены; cases остаются **NOT IMPLEMENTED / NOT RUN**. §6 sidecar — кандидат, механизм provenance требует отдельной приёмки; DDL, administrative writer, lock protocol, rollout/reissue/migration и implementation не разрешены. SR-01–SR-04 — integration gates; human audit OPEN.

**PRS1 NORMATIVE CONTRACT ACCEPTED · EXACT IMPLEMENTATION NOT AUTHORIZED · T04 UNCHANGED · E03 UNCHANGED · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**

## Редакция R1, представленная на приёмку (сохранена без изменения)

2026-10-10. R1; core direction accepted with changes, PRS1 NOT YET NORMATIVELY ADOPTED. **PROPOSED / NOT IMPLEMENTED / NOT RUN.** Контракт: [PRS1 proposal](L1-S4-A3-SELECTOR-CONTRACT.md). Все ожидаемые результаты ниже условны: после owner adoption и отдельной implementation authorization. Наличие строки не означает существующий executable test. Текущая политика остаётся conservative; baseline 429/0/0 взят из [S4-A2 report](L1-S4-A2-RESULTS.md), не повторён в A3.

## Метод и oracle

Controlled synthetic fixtures: org A/B; отдельные и совпадающие строковые course/group labels; teacher T, students S1/S2, author U; course C1/C2; assignment A1/A2; work W1/W2; submission S11/S12; review R11/R12; attached/pending files. Для каждого positive control обеспечить действующие JWT scope/generations, qualified clock, один SyntheticOnly context/binding, иначе deny может скрыть selector defect. Проверять не только boolean: отсутствуют запрещённые bytes/metadata/count contributions и mutation effects. Unknown infrastructure допускает exception/deny, но не allow/result. Для parser failures проверять причины, чтобы unknown kid/scope не маскировал нужную ветку.

`ALLOW*` = selector gate не запрещает, остальные проверки всё равно обязательны. `DENY` = нет доступа/effect. `REJECT` = запись атомарно отклонена. Негативные случаи выполняются по всем применимым Read/Submit/Review и с trusted actor scope, достаточным без restriction.

| ID | Условие / действие | Ожидаемый oracle PRS1 |
|---|---|---|
| F01 | Каждый из 7 kinds и all; canonical round-trip | Ровно один same-org target; identity bytes сохраняются |
| F02 | UUID uppercase/zero/bad hyphens; whitespace, suffix, extra slash | REJECT; corrupted persisted row — conservative deny |
| F03 | sid padding, invalid alphabet/UTF-8, nonzero trailing bits, empty | REJECT без permissive decoder fallback |
| F04 | Case/NFC/NFD разные ID; percent/double encoding | Не alias; только ordinal stored identity, иначе deny ambiguity |
| F05 | 4096/4097 ref bytes, 1024/1025 decoded sid bytes | Граница принята при валидном target / REJECT за границей |
| F06 | Unknown kind/version, wildcard/prefix/path traversal | REJECT; persisted — conservative deny, никаких network/file reads |
| O01 | ResourceRef org B, row/context/authority org A | REJECT/DENY даже при совпавшем entity ID |
| O02 | Чужой parent/course/context, contradictory hints | DENY; request не назначает affiliation |
| O03 | Missing/deleted/ambiguous resource или broken chain | DENY, не skip restriction |
| O04 | Same-org смена ContextVersion при прежнем ContextId | Active restriction сохраняет действие независимо OriginVersion |
| O05 | Смена ContextId без решения о переносе restrictions | Переход не допускает обход запрета |
| B01 | Маски 1,2,4 отдельно против basic Read/Submit/Review | Запрет только по явной registry; ordinal 0/1/2 не используется |
| B02 | Новая PRS1 restriction mask 8/16/24/9/31 | REJECT целиком; никакого Transfer/Lifecycle mapping |
| B03 | Required plan 8/16/9/25/31, export/download/jobs даже без restrictions | DENY; reserved/unsupported operations не исполняются |
| B04 | Все masks 1–7 × required plans 1–7, достоверная provenance | DENY при bit intersection и остальных matches; positive controls ALLOW* |
| B05 | 0, -1, 32 и смеси unknown bits | REJECT; persisted corruption не становится allow |
| B06 | Submit с возвращаемыми данными / Review с чтением | Read restriction запрещает plans 3/5 |
| B07 | Неизвестный action или пропущенный effect в plan | DENY; нет implicit Read/default action |
| H01 | Restriction course C1, операции на всех descendants | DENY; C2 даёт ALLOW* при отсутствии иных restrictions |
| H02 | Assignment A1 → W1 → S11 → R11; attachment parent=S11 | DENY на каждом descendant; A2 даёт ALLOW*; direct review parent не предполагается |
| H03 | Work W1 restriction | DENY children; самостоятельные ancestor metadata ALLOW* |
| H04 | Submission S11 restriction | R11 и attachment S11 DENY; sibling S12/R12 ALLOW* |
| H05 | Review R11 restriction; parent response embeds R11 | Embedded child не выдаётся; standalone parent без child ALLOW* |
| H06 | Student move G1→G2; старая assignment G1 | G1 restriction всё ещё DENY; текущая enrollment не меняет closure |
| H07 | Null group и group scoped selector | Null не wildcard; неполная affiliation DENY |
| A01 | Attached file с parent restriction, owner/uploader restriction | DENY metadata/download; parent gate нельзя обойти direct ID |
| A02 | Pending upload без target parent; последующая привязка к restricted W1 | Admission не разрешает downstream; finalize recheck DENY |
| A03 | Quarantine, missing reciprocal association, wrong uploader/context | DENY, не course-only fallback |
| A04 | Message file parent work; неподдерживаемый Message selector | Наследует work; selector REJECT |
| A05 | Текущий A2 ComponentAuthorization для Attachment | DENY остаётся независимо proposed selector outcome |
| S01 | Subject=null с matching resource/operation | DENY для любого caller/owner |
| S02 | Restriction caller T / owner S1, T читает работу S1 | DENY в обоих случаях отдельно |
| S03 | Restriction author U/third-party affected subject | DENY; requester не может удалить U из server-derived set |
| S04 | Restriction S2, работа только S1/T без S2 | ALLOW* при доказанном полном affectedSubjects |
| S05 | Subject-bearing data, неполное/неизвестное множество | DENY; не допускать caller-only fallback |
| S06 | Две overlapping restrictions; одна unrelated, другая matching | DENY; нет last-write-wins или более узкого allow |
| L01 | Queue/list с restricted и unrestricted candidates | Только разрешённые строки, prefilter до materialization/pagination |
| L02 | Count/sum/facets/total/cursor на тех же candidates | Ни одного forbidden contribution/скрытого total; сравнить с oracle доступного набора |
| L03 | Aggregate contribution с несколькими subjects, один restricted | Contribution исключён целиком, не частичная утечка |
| L04 | Caller-wide all restriction или невозможный безопасный filter | DENY всего запроса, не пустой успешный ответ как обход |
| L05 | Parent metadata содержит embedded restricted child | Child gate до выдачи; whole operation DENY если безопасное исключение не определено |
| V01 | Legacy null/empty/opaque и каждый старый bit 1/2/4/8/16 | Текущий conservative DENY по context/subject сохраняется |
| V02 | Legacy строка случайно canonical prs1, без profile provenance | Conservative DENY; parser success не разрешает narrowing |
| V03 | Unknown future profile в mixed readers | Conservative DENY; old reader не пропускает запись |
| V04 | Reissue+withdraw legacy; concurrent reader и rollback | Нет allow gap, audit/provenance сохранены; narrowing отдельно одобрен |
| V05 | Invalid new selector/subject/org/mask; stale expectedVersion | REJECT атомарно, старая restriction неизменна |
| V06 | Старый writer/new reader и rollback exact feature | Нет reinterpretation opaque values; conservative policy восстанавливается |
| C01 | Allow check, затем committed restriction, тот же component instance | Следующий check DENY, нет positive cross-request cache |
| C02 | Restriction/write concurrent в обоих порядках, commit failure | Предусмотренная serialization; нет mutation на stale allow; unknown outcome не success |
| C03 | Clock lost/regressed/interval вне binding; DB unavailable | DENY/exception без result; проверка whole interval |
| C04 | Multiple bindings / non-SyntheticOnly / forged client context | DENY |
| C05 | Restriction после read check / во время stream | Не заявлять instantaneous cancellation; отдельно проверить будущий approved boundary, G02 остаётся OPEN |

## R1: дополнительные negative cases

S11/S12 здесь — разные попытки одного W1; R11 относится к S11, R12 к S12. Доменный setup: S11→NeedsRevision(R11)→S12→R12. File cases — будущие dependency oracles, не разрешение интеграции; текущий Attachment gate остаётся DENY.

| ID | Условие / действие | Ожидаемый oracle |
|---|---|---|
| RB01 | Persisted row mask 8/16/24/9/31; unrelated ResourceRef, same context/subject | Conservative DENY всех supported actions без narrowing по resource; не strip bits |
| RB02 | Reserved row плюс exact PRS1 nonmatch/withdrawn row | Active reserved prohibition сохраняется; OR не становится allow |
| RB03 | Unknown bit 32 в required plan; mask 0 | DENY plan; REJECT writer; persisted invalid → conservative fallback |
| RT01 | Новая S12 после R11; чтение R11 под restriction S11 | DENY; resolver не подставляет latest S12 |
| RT02 | Restriction S11, самостоятельная S12/R12 без embedded S11 | ALLOW*; work restriction отдельно запрещает обе ветви |
| RT03 | Review.WorkId не равен Submission.WorkId; missing submission | DENY, без work-only fallback |
| RT04 | Попытка review старой S11 после S12 / повторная review S12 | Domain mutation DENY даже при selector ALLOW* |
| RT05 | Старый AttemptNumber вместо ID либо inconsistent attempt chain | Не alias latest/другой work; ambiguous server resolution DENY |
| N01 | Work DTO скрывает S11, но отдаёт полный AttemptCount/status/LastReviewedAt | DENY unsafe projection; данные forbidden source не просачиваются через summary |
| N02 | Restriction R11/S11; feedback R11 запрошен через Messages/ReplyTo | DENY feedback по reverse Review.MessageId dependency, не work-only allow |
| N03 | Feedback MessageId чужого work, missing/ambiguous reverse association | DENY representation с неполной provenance/dependency |
| N04 | Annotation R11 с restricted attachment S11 | DENY annotation/thumbnail; review allow не разрешает file content |
| N05 | Annotation R12 с AttachmentId S11 | DENY invalid association; не переназначать parent и не наследовать latest |
| AF01 | Оба StoredAttachment.SubmissionId и MessageId заполнены | DENY ambiguity, не выбирать первую ветку |
| AF02 | Только SubmissionId=S11, reciprocal entry отсутствует / uploader mismatch | DENY; no course-only fallback |
| AF03 | Parent ReviewId или воображаемый review attachment | Unsupported parent DENY; допустимая physical связь только Submission/Message |
| AF04 | Pending expired/ineligible/no trusted scope, затем attach | DENY; старое admission не разрешает attach |
| AF05 | File S11 повторно привязывают S12 или Message | DENY без отдельно допустимого workflow; нельзя переносить старые restrictions |
| AF06 | Ordinary Message attachment с replyTo feedback | Physical parent остаётся Message→Work; выдаваемый reply content имеет отдельный gate |
| PV01 | Canonical prs1 row без sidecar, mask 1, unrelated resource | Legacy conservative DENY для applicable context/subject |
| PV02 | Sidecar org/context/origin/ref/mask расходится с row | Conservative DENY, не доверять одному prefix/profile |
| PV03 | Duplicate sidecar / unsupported ContractRevision / missing decision evidence | Conservative DENY; ambiguity не выбирается First() |
| PV04 | Exact nonmatch плюс active legacy opaque/null/reserved row | Legacy DENY; exact allow не отменяет его |
| PV05 | Withdrawn legacy + matching active valid PRS1; обратные состояния | Active prohibition применяется; withdrawn row не отменяет другую |
| PV06 | Restriction commit без provenance из-за partial failure | Atomically reject new write; при обнаружении missing provenance conservative DENY |
| PV07 | Reissue+withdraw crash/rollback/unknown acknowledgement | Нет allow gap; rollback сохраняет old active; unknown outcome не success/не blind retry |
| PV08 | DB provenance unavailable или context/subjects невозможно установить | DENY всей операции, не fallback allow |
| PV09 | Backfill provenance по prefix либо untrusted admin actor/DecisionRef | REJECT; legacy не становится exact автоматически |
| PV10 | Mixed old reader не умеет полный affectedSubjects | Exact rollout блокируется; selector overdeny недостаточно для доказательства совместимости |

## Разделение evidence

A2 имеет synthetic coverage conservative gate, свежего следующего check, отдельных битов/opaque values, child ownership, commit acknowledgement, clock/key loss. Оно не подтверждает PRS1 parser, битовую registry, all-affected-subject resolver, list/count filters, attachments/jobs или write/restriction serialization. C02/C05 — obligations будущих отдельно разрешённых adapters; не повод реализовывать их в A3. В этой стадии проверяются только документационные ссылки, состав файлов и сохранность baseline.

**S4-A3-R1 READY FOR OWNER ACCEPTANCE · PRS1 NOT YET NORMATIVELY ADOPTED · T04 UNCHANGED · E03 UNCHANGED · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**
