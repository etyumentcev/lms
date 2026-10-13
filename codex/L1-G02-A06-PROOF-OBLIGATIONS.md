# G02/A06 — PROOF-OBLIGATIONS

2026-10-10. G02/A05 B/C DIRECTION ACCEPTED; G02/A06 DOCUMENTATION AUTHORIZED — основание: явное задание владельца A06. Все SHALL/«должен» ниже — предложенные обязательства, NOT ACCEPTED / NOT IMPLEMENTED / NOT PROVEN. Новая норма, архитектурная квалификация и разрешение реализации требуют отдельных решений.

**A06 NORMATIVE PACKAGE READY FOR OWNER DECISION · T04 UNCHANGED · S4 BLOCKED.**

## P1–P9: дословные предложенные обязательства A05

Источник: [A05 §4](L1-G02-T04-REVISION-PROPOSAL.md). Нумерация не заменяет исторические A02 I1–I7. Все девять — OPEN; ни одно не объявляется реализованным или доказанным.

P1. Durable OperationId и binding `(org, actor, kind, idempotencyKey, canonicalDigest)` подтверждены до side effects. Один key — один command/result; иной digest → conflict. Scope, grant generation и execution generation различны.

P2. Operation identity, domain mutation, immutable reference и result фиксируются атомарно. Все readers/queues/counts/claims включены в visibility boundary. Если используется private intent, он не попадает в историю и не влияет на workflow до законной publication.

P3. Visibility-changing linearization `p<d` и до применимого authorization cutoff. Доказательство времени включает завершение COMMIT, а не timestamp до отправки. При невозможности доказать своевременность result остаётся deny/quarantined; это требует enforcement во всех readers, а не решения resolver на словах. Если DB late commit уже виден хотя бы одному обычному reader, safety нарушена.

P4. Private staging namespace никогда не переиспользуется; `(epoch, operation, executionGeneration, objectVersion)` уникален. Старый worker может воздействовать только на свою private version. Он не имеет credentials/FD/mount для published bytes, metadata, новых generations или public egress.

P5. Seal означает доказанную неизменяемость exact version/hash/length/durability, включая retained handles. Rename/chmod/hash alone недостаточны. Поздняя private запись не может изменить sealed/published version. Если backend не доказывает это, version не eligible.

P6. Fencing authority атомарно проверяется в компоненте, меняющем защищаемый объект. PG CAS — metadata fence, не physical storage fence. Control fence может запретить publication без утверждения quiescence; physical fence ACK допускается только с отдельным evidence. Старый epoch после restart не оживает.

P7. Unknown не превращается в rollback по absence/timeout. Нет автоматического replay/purge потенциально committed original. Idempotency tombstones живут весь доказанный replay horizon; неизвестный horizon не разрешает очистку.

P8. Каждый новый read/status/Range требует нового JWT и server-derived org/course/owner/parent scope плюс current ProcessingRestrictions. Recovery service не наследует права пользователя. Public/presigned URLs, static paths, CDN bypass запрещены.

P9. Clock/authority/receipt/timeline unknown → deny; capacity исчерпана → reject до side effects. Timeout не освобождает outstanding slot без evidence прекращения/безопасной изоляции и учёта занимаемых ресурсов. Неограниченный replacement workers запрещён.


## Проверяемое закрытие обязательств

| ID | Enforcement / требуемое evidence | Отрицательная проверка и PASS oracle | Статус |
|---|---|---|---|
| P1 | Durable unique scoped binding до любого side effect; canonical digest; исходный d | A05 F01/F02: два узла/same key → один command/result; different digest conflict; foreign key/status deny; retries не обновляют d | OPEN, R2 NOT IMPLEMENTED |
| P2 | Atomic domain/result/identity/exact reference; inventory всех readers/counts/claims/events | F12/F13: concurrent claim/delete; missing version не success; private intent никогда не влияет на workflow | OPEN, зависит B-PG/B-ISOLATION |
| P3 | Commit/fence contract, authoritative linearization oracle, все visibility paths | F03/F04: stall после precheck/WAL до d, resume после d; ни одного late effect; любой такой эффект FAIL, ACK loss не rollback | OPEN, B-PG |
| P4 | Credentials/FD/mount/IPC isolation и уникальные namespace/generation | F07/F10/F21: старый worker пишет после d/restart только в private version, не в domain/published/egress | OPEN, B-ISOLATION |
| P5 | Exact version seal с backend immutability/durability и retained-handle evidence | F08/F13: поздний write не меняет опубликованные hash/length/version; surrogate seal rejected | OPEN, B-ISOLATION |
| P6 | Mutation-point atomic fence; control fence отдельно от quiescence | F09/F10/F11: epoch check→pause→fence→resume; stale mutation denied, old primary не оживает | OPEN, B-FENCE |
| P7 | Serialized outcome resolution/history continuity; safe purge/tombstone policy | F05/F06/F12/F19/F22: absent snapshot/ACK loss не abort; нет replay/purge потенциального original, physical-delete ACK только по факту | OPEN, B-RECOVERY |
| P8 | Server-derived scope/current restrictions на каждом read/status/Range; no bypass map | F02/F18/F21/F23: чужие org/course/owner/counts/aliases deny до content, no presigned/static/CDN bypass | OPEN, S4-A + S4-B integration |
| P9 | Bounded clock/authority, pre-admission caps и durable resource accounting | F16/F20: clock/receipt unknown deny; resolver outage + зависшие writers исчерпывают cap и reject, timeout не «освобождает» ресурс | OPEN, B-CLOCK/B-CAPACITY |

## Gates и доказательства времени

R0: отдельные owner acceptance нормативной редакции и E03-C, архитектурной модели, scope authorization. A04 archived — выполненная часть R0; весь R0 PENDING OWNER.

R1: pinned deployment dossier, no-bypass/failure-domain map, numerical worst-case ledger HTTP и publication, clock uncertainty/drift/dispatch, capacity limits. Для обычного пути sum(queue/header/body/admission/work/publication/egress tails)≤60; для явно long пути≤480, с резервом внутри B на stop/drain. Для параллельных стадий предоставить worst-case dependency graph, а не произвольно складывать p99. Срок p отдельно <d и authority cutoff. Все stage numbers, гарантии и источники пока NOT ESTABLISHED; 120s нельзя использовать как tail.

R2: реализованный отдельно разрешённый протокол, constraints, state-machine/interleaving review P1–P9 и минимальный recovery. NOT IMPLEMENTED.

R3: все [A05 F01–F24](L1-G02-T04-ACCEPTANCE-MATRIX.md), реальные 60/480s end-to-end synthetic runs с correlated traces и независимым egress/commit oracle. NOT RUN. Missing trace/oracle — INCONCLUSIVE и gate не пройден; contract gap — R1 FAIL; late visibility — FAIL, не успешный recovery. Матрица не сокращается после неудачи. Исторические A03 измерения не worst-case proof.

R4: независимый security/DDD review, regression, HTTP/browser evidence, coverage всех aliases и owner acceptance. NOT RUN. Исторические V0–V3 не переименовываются: V1 FAIL/NOT ESTABLISHED, V2/V3 NOT SATISFIED.

## Реестр технических blockers

| Blocker | Что отсутствует | Условие закрытия / затрагиваемый scope |
|---|---|---|
| B-PG | Контракт последнего visibility-changing COMMIT либо доказанная alternative boundary | R1+R2+F03/F04/F11, все ordinary mutations и S4-B; auth issue/rotation тоже требуют собственных transaction/timeout evidence |
| B-EGRESS | Independent cutoff, last-controlled-TX oracle и bounds kernel/NIC/retransmit | R1+F14/F15 на всех HTTP paths S4-A/B |
| B-ISOLATION | Qualified private/sealed storage boundary без retained-write bypass | P4/P5, F07/F08/F13/F21; S4-B |
| B-FENCE | Mutation-point enforcement и durable restart/old-primary fencing | P6, F09–F11; S4-B и shared DB authority |
| B-CLOCK | Conservative UTC и monotonic drift/dispatch contracts | F16/F17; S4-A/B |
| B-RECOVERY | Authoritative unknown resolution, replay horizon, safe purge proof | P7, F05/F06/F12/F19/F22; S4-B minimal recovery |
| B-CAPACITY | Численные admission/orphan limits, устойчивый accounting | P9/F20; S4-B; HTTP admission также S4-A |
| B-E03 | Отдельное owner решение конфликта completion | E03-C отдельно, до принятия новой нормы; не технический PASS |

Ни один blocker не закрывается фактом подготовки A06. Требуемые механизмы описаны в [failure model](L1-G02-A06-FAILURE-MODEL.md). Новый product/vendor research, реализация и fault experiments A06 не проводились.
