# G02/A05 — T04 Revision Proposal

2026-10-10. DOCUMENTATION AND ANALYSIS ONLY. PROPOSED, NOT ACCEPTED, NOT IMPLEMENTED.

**A05 PROPOSAL READY FOR OWNER DECISION · T04 UNCHANGED · S4 BLOCKED.**

## 1. Основания и сохранность

Работа выполнена в `/home/tea/github/lms`, на обнаруженной ветке `codex/l1-s4-jwt-api-authorization`, HEAD `a60ee6c73c4400bd1bdf6c6df434358372745c41`. По прямому заданию A05 ветка не переключалась; merge/rebase не выполнялись. Исходное дерево содержит modified S4 checkpoint и AttachmentDeadlineBoundaryTests.cs, untracked A02/A03/evidence и .obsidian; эти файлы сохранены. Никаких runtime/schema/config/test изменений A05.

Нормативная основа: [реестр T01–T06/E01–E06/O01](L03-OWNER-DECISION-REGISTER.md), [ADR-L15](../docs/adr/ADR-L15-jwt-delayed-revocation.md), [L1 Security Spec](../docs/architecture/L1-SECURITY-SPEC.md), [ADR-L18](../docs/adr/ADR-L18-synthetic-processing-restrictions.md), [S4 checkpoint](L1-S4-RESULTS.md). Действующие требования не изменены.

A02: [архитектура и I1–I7](../docs/architecture/L1-G02-BOUNDED-EXECUTION.md), [failure model](../docs/architecture/L1-G02-FAILURE-MODEL.md), [варианты](L1-G02-OPTIONS.md), [V0–V3](L1-G02-VERIFICATION-PLAN.md). A02 option C — storage gateway, не вариант C из этого A05.

A03: [readiness](L1-G02-IMPLEMENTATION-READINESS.md), [experiments](L1-G02-EXPERIMENT-RESULTS.md), [timing](L1-G02-TIMING-PROOF.md), [storage](L1-G02-STORAGE-EVIDENCE.md), [PG](L1-G02-POSTGRES-EVIDENCE.md), [edge](L1-G02-EDGE-EVIDENCE.md). FIFO cancellation gap, epoch TOCTOU, retained writable FD, commit ACK ambiguity и buffered delivery — ограниченные контрпримеры. P03 не квалифицирован; короткие probes не worst-case proof.

A04 найден в сохранённом пакете `/tmp/lms-g02-a04`, подтверждён чтением предыдущей задачи «Исследовать инфраструктурные гарантu». Изучены [кандидат](/tmp/lms-g02-a04/L1-G02-A04-CANDIDATES.md), [контракты](/tmp/lms-g02-a04/L1-G02-A04-CONTRACT-MATRIX.md), [ledger](/tmp/lms-g02-a04/L1-G02-A04-TIMING-LEDGER.md), [решение](/tmp/lms-g02-a04/L1-G02-A04-OWNER-DECISION.md). Пакет и подготовленный там S4 append существуют, но **не установлены в canonical repo**. Текущий canonical checkpoint заканчивается A03. A05 не подменяет его staged копией и не переносит A04 молча. Ссылки /tmp пригодны локально, не являются постоянным repository archive; архивирование A04 требуется до последующей нормативной консолидации. Вывод A04: один AWS-кандидат C04-1, V1 CONTRACT EVIDENCE BLOCKED; no numeric storage/PG/egress/supervisor/clock bounds. Это не универсальная невозможность T04. Новое исследование продуктов A05 не проводит.

## 2. Варианты решения

| Вариант | Гарантия | Цена / G02 |
|---|---|---|
| A — оставить T04 | Весь storage/PG/cleanup/controlled egress заканчивается за 60/480s при принятой модели; unknown outcome допустим только после установленного stop | Нужен полный platform contract и V1–V3. Ни P03, ни C04-1 не подходят по имеющемуся evidence. Смена timeout, процесса или облака не закрывает G02. Сохранить blocker либо исследовать иной стек отдельным решением |
| B — разделить гарантии | HTTP и допустимость результата имеют deadline; private физический I/O может завершиться позже, outcome может остаться unknown | Убирает требование arbitrary-I/O quiescence как условие HTTP completion. Не решает сам по себе late domain commit и edge hang. Нужны новые publication/storage boundaries, recovery, admission caps; общий safety proof отсутствует |
| C — ограничить failure model | Строгий full completion только для перечисленных отказов с численными проверенными bounds | Честнее широкого обещания, но нельзя объявить safety необязательной при частом WAL/storage stall. Исключённые faults должны вести к изоляции/deny, а не поздней публикации. P03 автоматически не принимается |

**Рекомендуется B/C:** отказаться только от универсального physical-I/O completion bound для изолированного private staging/reads/cleanup; сохранить HTTP 60/480s на явно квалифицированной egress-модели и жёсткий deadline авторитетного эффекта/публикации. Для commit, который способен сделать domain result видимым, остаётся C: доказанный commit/fence bound либо новая архитектура, исключающая видимость позднего commit. Последняя здесь не считается спроектированной до готовности. Нельзя просто добавить Published=true, второй COMMIT или time check: последний visibility-changing COMMIT имеет ту же проблему.

Это осознанно **не обещание немедленно разблокировать S4**. Без квалифицированной границы publication и HTTP даже новая редакция не проходит приёмку. Если владелец требует physical completion также для private orphan, выбрать A. Если он требует safety при произвольном зависании последнего publisher, нужна другая проверенная архитектура, а не ослабление формулировки задним числом.

## 3. Предлагаемые нормативные определения

Следующие SHALL-требования — текст кандидата, а не действующая норма.

* `t0` — earliest controlled ingress до queue/header/body; `a` — момент JWT admission. Время очереди не создаёт новый бюджет.
* `B=60s` ordinary; `B≤480s` только явно обозначенный upload/download, каждый Range отдельно. `d=t0+B`, передаваемый как trusted remaining budget, монотонно уменьшаемый с учётом transit/uncertainty.
* Request deadline: к d завершить HTTP service и последнюю отправку из последнего контролируемого egress, включая его buffers/retransmit/NIC queues. Получение уже вышедших bytes внешним клиентом не ограничено. Handler return/close не oracle.
* Authorization validity: JWT admission только при фактическом `a<exp`, lifetime≤900s, signature/issuer/audience/scope. Exp внутри принятого request не обязательно прерывает его; это не разрешение нового stage после d. Ordinary revoke/issue/refresh сохраняют исходную сериализацию.
* Logical operation completion: авторитетно определённый `Committed` либо `Aborted`, с единственным результатом и невозможностью позднего противоположного исхода. HTTP timeout не является ни одним из них. Конечное время получения этого знания при partition не обещается.
* Publication: первый авторитетный domain effect, eligibility/claim, видимость в query/count/history/queue, выдача bytes или создание доступного reference/capability. Не только HTTP ACK и не только file URL. Внутренний private intent не domain event.
* Publication safety: просроченная operation не создаёт новый domain effect/eligibility и не публикует его по старой authority. Законно committed до d результат можно позже прочитать новым авторизованным request; это не новое применение команды.
* Physical I/O completion: фактическое прекращение storage/PG/network work и всех issued queues/handles. Оно учитывается отдельно и не выводится из HTTP completion.
* Recovery: установление уже произошедшего исхода, quarantine/fence/accounting и безопасное обслуживание orphan; никогда replay/resume expired command.

## 4. Предлагаемые инварианты P1–P9

Это новые ID; A02 I1–I7 не переопределяются и не объявляются выполненными.

P1. Durable OperationId и binding `(org, actor, kind, idempotencyKey, canonicalDigest)` подтверждены до side effects. Один key — один command/result; иной digest → conflict. Scope, grant generation и execution generation различны.

P2. Operation identity, domain mutation, immutable reference и result фиксируются атомарно. Все readers/queues/counts/claims включены в visibility boundary. Если используется private intent, он не попадает в историю и не влияет на workflow до законной publication.

P3. Visibility-changing linearization `p<d` и до применимого authorization cutoff. Доказательство времени включает завершение COMMIT, а не timestamp до отправки. При невозможности доказать своевременность result остаётся deny/quarantined; это требует enforcement во всех readers, а не решения resolver на словах. Если DB late commit уже виден хотя бы одному обычному reader, safety нарушена.

P4. Private staging namespace никогда не переиспользуется; `(epoch, operation, executionGeneration, objectVersion)` уникален. Старый worker может воздействовать только на свою private version. Он не имеет credentials/FD/mount для published bytes, metadata, новых generations или public egress.

P5. Seal означает доказанную неизменяемость exact version/hash/length/durability, включая retained handles. Rename/chmod/hash alone недостаточны. Поздняя private запись не может изменить sealed/published version. Если backend не доказывает это, version не eligible.

P6. Fencing authority атомарно проверяется в компоненте, меняющем защищаемый объект. PG CAS — metadata fence, не physical storage fence. Control fence может запретить publication без утверждения quiescence; physical fence ACK допускается только с отдельным evidence. Старый epoch после restart не оживает.

P7. Unknown не превращается в rollback по absence/timeout. Нет автоматического replay/purge потенциально committed original. Idempotency tombstones живут весь доказанный replay horizon; неизвестный horizon не разрешает очистку.

P8. Каждый новый read/status/Range требует нового JWT и server-derived org/course/owner/parent scope плюс current ProcessingRestrictions. Recovery service не наследует права пользователя. Public/presigned URLs, static paths, CDN bypass запрещены.

P9. Clock/authority/receipt/timeline unknown → deny; capacity исчерпана → reject до side effects. Timeout не освобождает outstanding slot без evidence прекращения/безопасной изоляции и учёта занимаемых ресурсов. Неограниченный replacement workers запрещён.

## 5. Временные свойства

Issuance сохраняет `exp=min(Now+900s, D−600s)` для самого раннего применимого D; exp≤Now → не выдавать. Revoke атомарно фиксирует `D≤RevocationRequestedAt+7200s`, retries не продлевают D; regrant — новое поколение. True JWT lifetime≤900s, без positive leeway, расширяющего фактический admission.

Для JWT с известным D при консервативном admission и `H=lastControlledSend−t0≤B`: `lastControlledSend < D−600+Δ+λ+B`; нужно `Δ+λ+B≤600`. При B=480 остаток≤120s относится только к uncertainty, не I/O/rollback хвосту. Отдельно H≤60/480 и JWT≤900 не ослабляются. Publication p также должна укладываться в d; поэтому тот же консервативный envelope ограничивает новый эффект до D. Ни формула, ни local precommit timestamp не доказывают p.

Токен до revoke: новые admissions прекращаются максимум через 900s после заявки при conservative expiry; допустимый новый effect/egress заканчивается максимум через 900+480=1380s (23m), в пределах 7200s, **условно при publication и HTTP bounds**. Эта арифметика больше не относится к physical private I/O и времени recovery. Неизвестный clock bound запрещает issuance/admission; для уже активных операций нужен независимый monotonic cutoff с установленным drift/dispatch envelope, иначе temporal claim не поддержан.

Cancellation распространяется во все I/O, но является запросом остановки. Внутренний cutoff выбирается раньше d на доказанный publication/egress tail. После cutoff нет новых stages/COMMIT/retries/chunks; уже issued private I/O может закончиться позже при P4–P6. Уже issued visibility-changing COMMIT должен иметь bound до d; иначе этот путь не допускается. Disconnect закрывает delivery и инициирует cancellation, не обещает rollback.

## 6. Failure model

| Класс | Предлагаемая гарантия / условия | Исключения |
|---|---|---|
| M1: bounded execution | Pinned stack, bounded capacity/queue, monotonic clock/drift и dispatch; storage/PG/edge bounds по каждому admitted stage | Числа пока NOT ESTABLISHED. Полный physical completion обещается лишь для путей с ledger≤B |
| M2: isolated storage worker crash/hang, delayed/duplicate messages, private outstanding writes; ACK loss; resolver unavailable | Safety P1–P9 при immutable isolation и доступной/самозакрывающейся publication boundary. HTTP deadline при независимо работающем qualified edge. Private I/O и resolution без универсального bound | Нельзя включать writable published FD или direct PG authority в «private worker». Не даёт physical quiescence |
| M3: PG/WAL/последний publisher hang | Для прямой domain mutation требуется qualified bounded commit/fence contract C; без него path rejected. Alternative nonvisible intent architecture требует отдельного design/proof | M3 не объявлен решённым quarantine flag. No late visible commit — обязательство, а не best effort |
| M4: edge/kernel/NIC/scheduler hang | HTTP bound только при независимом cutoff с проверенным worst case для всех контролируемых queues | При отсутствии такого механизма deployment не квалифицирован даже по B/C |
| M5: restart/partition/failover | Default deny до durable epoch, единственного authoritative timeline, старого primary fence и сохранённой acknowledged history | Replica absence, restart процесса и новый epoch в памяти не доказательства |
| Вне модели | Byzantine trusted DB/storage/operator, dishonest durability, повреждение всех независимых fences, произвольный correlated hardware/time failure | Нет universal safety/termination claim; incident и containment где возможно. Реальные ПД по-прежнему запрещены |

Разделение B снимает physical bound только с M2 при доказанной изоляции. C требует точного deployment dossier для M1/M3/M4; исключение fault из timing model не является разрешением late publication. Для faults, где safety также не доказана, документировать unsupported deployment, а не записывать PASS.

## 7. State transitions и ambiguous commit

Durable business state и знание observer — разные оси.

| Durable state / transition | Guard / действие |
|---|---|
| Absent → Registered | Durable idempotency binding, фиксированный d, trusted actor/org/context; unknown registration → никаких side effects |
| Registered → Staging → Sealed | До cutoff; private version; подтверждённый immutable receipt; no publication |
| Sealed → Committed | Atomic operation/result/domain/claim, expected generation, current resource/restriction transaction checks; квалифицированный visibility-changing commit до d |
| Registered/Staging/Sealed → Aborted | Авторитетно доказано отсутствие результата и невозможность позднего commit; publication permanently disabled; private bytes могут ждать безопасного purge |
| Committed / Aborted → same | Terminal; retries только читают исход после новой authorization; key не создаёт второй эффект |

`OutcomeUnknown`, `Quarantined`, `FencingPending`, `PhysicalPending` — независимые knowledge/control flags. HTTP `Closed` к d не меняет durable state в Aborted. Committed не переводится в Aborted по потерянному ACK. При невозможности доказать своевременность commit quarantine сохраняется; публикация после recovery не является разрешённым новым переходом.

Resolver начинает новый bounded service request с новой authority для выдачи результата, использует исходный OperationId. На authoritative primary получает сериализованное чтение/lock по durable binding после завершения или доказанного fence прежней transaction. Отсутствие в старом snapshot/replica не oracle. При failover нужны old-primary fence и history continuity. Lock timeout → всё ещё unknown, без новой команды.

Если подтверждён своевременный Committed, сохранить result и bytes; новый authorized read может показать прежний факт. Если доказан rollback и невозможность позднего commit, зафиксировать Aborted. Если commit случился после d в прямой domain таблице, это incident/acceptance FAIL, а не «восстановленный успех». Результат без доказательства своевременности не разрешать через read path. Предложение не считает существующую schema способной обеспечить этот запрет.

Purge — отдельная maintenance операция с exact version, quiescence, authoritative refs/claim serialization и собственным deadline; она не завершает прежнюю команду. Physical delete нельзя подтверждать до фактического удаления. Logical tombstone и physical deletion различаются в API contract; изменение существующей DELETE семантики требует отдельного согласования, не вводится A05. При недоступном recovery сохраняются quarantine и accounting; eventual completion/RTO не обещаются.

## 8. Влияние на S4 и документы

Независимо от решения G02 технически отделимы issuer/validator rules и тесты conservative expiry, claims/org scope, O01 HTTP/CSRF/Origin/browser coordinator, фильтрация ownership/counts, SyntheticOnly/current restriction policies. Это возможность будущего выделения работ, **не разрешение продолжить S4 сейчас**; public integration/acceptance зависит от временных и publication gates.

Новая архитектура нужна для durable operation/idempotency state, immutable exact versions, credentials/no-bypass, publication across all queries/domain effects, unknown recovery, orphan quotas, independent edge cutoff и commit/fence boundary. Это затрагивает S7; минимальный slice и schema/API contract должны получить отдельное разрешение. Не реализовывать полный lifecycle/jobs/erasure под видом S4.

После отдельного owner решения потребуется согласовать ADR-L15 (completion/cancel/recovery/time proof), L1 Security Spec (publication/unknown/no-bypass), owner register T04/G02, ORG-G04/isolation, S4/S7 plan и acceptance, A02 proposed I6/failure-model/timing successor. Старые A02–A04 evidence сохраняются исторически. ADR-L16/L17 и E03 cross-references нуждаются в проверке согласованности термина «started operation≤8m»; **E03 не менять автоматически**. ADR-L18 сохраняет current checks/SyntheticOnly; реальный in-flight SLA не решён A05. При обнаруженном нормативном конфликте E03 требуется отдельное явное решение до консолидации, а не скрытое переопределение.

Риски: central publication availability, stuck DB locks, private orphan disk/cost exhaustion, неопределённый outcome для пользователя, сложный idempotency retention, неустранимый late commit без нового contract, policy drift между readers, stale primary, clock loss, deletion/WORM incompatibility. Успешная документационная проверка не доказывает безопасность. Acceptance и negative tests: [матрица](L1-G02-T04-ACCEPTANCE-MATRIX.md); подробный impact: [security](L1-G02-T04-SECURITY-IMPACT.md); точный draft решения: [owner](L1-G02-A05-OWNER-DECISION.md).
