# G02/A03 — Implementation readiness / owner report

2026-10-09. **INFRASTRUCTURE EVIDENCE BLOCKED · T04 NOT PROVEN · S4 BLOCKED.**

## Итог решения исследования

**Ни одной конкретной поддерживаемой конфигурации с доказанным T04 в A03 не установлено.** Выбранный и измеренный [profile P03](L1-G02-INFRASTRUCTURE-PROFILE.md) отклонён для строгого bounded completion: нет contractual worst-case storage/PG termination, независимого supervisor/fence, controlled-egress cutoff и clock/scheduler limits. Это вывод о данном profile/evidence, не универсальное доказательство невозможности любого оборудования.

Семь обязательных документов подготовлены; completeness пакета не означает статус INFRASTRUCTURE EVIDENCE READY. C остаётся принятым архитектурным направлением. Owner authorization A03 разрешает experiments, не реализацию gateway в LMS. T04 60s/480s/600s/900s/7200s неизменен.

## Что установлено и что остаётся недоказанным

| Гарантия / утверждение | Основание | Точный статус |
|---|---|---|
| Interruptible FIFO open/read/write и busy child можно остановить отдельно от controller | Четыре native process probes с waitpid и wchan | MEASURED только эти runs; no worst-case bound |
| Epoch check перед POSIX write не равен actual-mutation fence | Controlled pause/revoke/resume, изменившийся object hash | Доказанный контрпример toy protocol; не контрпример корректно реализованному C |
| Rename/chmod не устраняет retained writable FD | Реальный FD write после surrogate seal | Доказанный контрпример предложению такого seal |
| Unknown ACK не означает rollback; отсутствие effect до commit не oracle | Real PG observer/row lock, один result, repeated status-read | MEASURED ограниченное safety evidence; no production idempotency guarantee |
| pg_terminate_backend default success не означает exit | PG contract и SIGSTOP probe | CONTRACT semantics + MEASURED counterexample |
| Inactivity timeout не absolute end-to-end deadline | nginx docs + trickle 1.650587s при 1s timer | CONTRACT semantics + MEASURED counterexample |
| Client может читать queued bytes после close/nginx stop | 64KiB TCP и 128KiB nginx response | MEASURED; controlled last-send timestamp не измерен |
| Durable immutable storage, physical fencing и отсутствие позднего device I/O | Нет соответствующего backend contract/instrumentation | NOT ESTABLISHED |
| Commit/rollback/WAL stop, finite authoritative resolution, history после failover | Только healthy PG/clean restart; no old-primary fence | NOT ESTABLISHED |
| Restart epochs, отсутствие raw mount/FD/credential bypass | Gateway и deployment security boundary не реализованы | NOT ESTABLISHED |
| Независимый supervisor, bounded scheduler/clock/NIC queues | P03 один kernel, clock snapshot без hard limits | NOT ESTABLISHED |
| End-to-end Q+S+P+X+R+E+J≤60/480 для всех file routes | Unknown terms в timing ledger | NOT PROVEN; V1 FAIL |

## I1–I7: исходные определения сохраняются

Нормативный источник — [A02 инварианты §4](../docs/architecture/L1-G02-BOUNDED-EXECUTION.md). Здесь только evidence status, не новые определения:

| Invariant | A03 evidence status |
|---|---|
| I1 | Unique effect + atomic operation/result в одном PG prototype; concurrent/replay/auth matrix не выполнена; LMS guarantee отсутствует |
| I2 | Требование private nonreused versions сохранено; полноценные isolation/epochs NOT IMPLEMENTED |
| I3 | Receipt+Committed+current checks остаются обязательными; gateway seal/publication NOT IMPLEMENTED |
| I4 | POSIX check-then-write и retained-FD seal опровергнуты как достаточные механизмы; replacement contract отсутствует |
| I5 | Lost application ACK/false absence подтверждены; prototype читает факт без replay; full recovery/cleanup races NOT VERIFIED |
| I6 | S/P/X/E/J upper bounds отсутствуют; late private work не освобождается от deadline; NOT PROVEN |
| I7 | Clean PG restart не durable gateway epoch/old-primary fence; NOT VERIFIED |

## Gates V0–V3: без переопределения

Нормативный источник — [A02 verification plan §1](L1-G02-VERIFICATION-PLAN.md).

- **V0:** owner выбрал C/failure-model направление и разрешил A03 isolated prototype scope; выполнен для этого исследования. Runtime implementation authorization отсутствует.
- **V1:** contracts и временная композиция — **FAIL / NOT ESTABLISHED** по timing ledger.
- **V2:** реализация invariants и adversarial tests — **NOT SATISFIED**, лишь standalone negative/limited positive probes. Нет C implementation.
- **V3:** полный HTTP/storage/PG/edge evidence + security review — **NOT SATISFIED**, нет full integration и независимого review.

G02 acceptance не достигнута; S4 не разблокирован, S5 не начат. Ни successful fixture, ни согласие с C не заменяют gates.

## Зависимости и ограничения следующего решения

1. **Storage:** требуется конкретный backend/firmware contract atomic epoch at mutation + immutable durability + finite outstanding-I/O quiescence. На local ext4/NVMe P03 такой контракт не получен. Нужен численный S/X для заявленной модели отказов, включая open/read/write/delete/flush.
2. **PG:** finite WAL/rollback/backend/fence tail, authoritative history policy и доказанный old-primary fence, если планируется HA. Нельзя лечить latency отключением durability или replay неизвестного commit.
3. **Edge:** последний контролируемый hop и stop/drain contract всех userspace/kernel/runtime/NIC queues, с independent stop при edge hang. Client EOF/receive не заменяет last-send oracle.
4. **Supervisor/time/capacity:** независимый failure domain, bounded dispatch/clock drift, admission/queue/worker limits; correlated unsupported failures явно перечислить. Chrony tracking не SLA.
5. **Verification:** disposable device/VM стенд с нижнеуровневым oracle, затем V1 композиция, только после неё отдельно разрешённые C implementation + V2/V3. Полный S7 lifecycle не входит в этот scope.

Предлагаемый следующий owner scope: **A04 infrastructure contract investigation only**, если владелец решит продолжить: получить проверяемые contracts для одного конкретного storage/PG/egress/supervisor stack и изолированный device fault стенд, сохранить T04/I1–I7/V0–V3 и запреты. Нет заявления, что поставщик с нужными гарантиями найден. Альтернатива — зафиксировать P03 как непригодный и остановить G02 для этого deployment. Замена Linux процессом/container/object-store timeout сама по себе не альтернатива доказательству.

Текст предлагаемого решения (не принято автоматически): «Разрешаю только исследование конкретной инфраструктурной альтернативы и её contractual worst-case bounds. S4 BLOCKED; реализация LMS/storage gateway не разрешена; T04 и gates неизменны». Отдельное implementation решение имеет смысл только после закрытия V1 и конкретного разрешения API/protocol/schema/recovery slice. Ни A04, ни implementation в этом задании не начаты.

## Артефакты и завершение

[Profile](L1-G02-INFRASTRUCTURE-PROFILE.md) · [Storage](L1-G02-STORAGE-EVIDENCE.md) · [PostgreSQL](L1-G02-POSTGRES-EVIDENCE.md) · [Edge](L1-G02-EDGE-EVIDENCE.md) · [Timing proof](L1-G02-TIMING-PROOF.md) · [Experiments](L1-G02-EXPERIMENT-RESULTS.md) · этот report. [S4 checkpoint](L1-S4-RESULTS.md) дополнен. [Evidence checksums](evidence/L1-G02-A03/SHA256SUMS) фиксируют сохранённые fixtures/results; [preservation check](evidence/L1-G02-A03/preservation.json) подтверждает сохранность исходных файлов.

Изменения A03 ограничены документацией и standalone evidence fixture в codex/evidence, не включённым в build/runtime/tests LMS. Предсуществующий synthetic C# test не изменялся. Push, merge, deploy, production migration, реальные персональные данные, S5–S8, M2/M3 и полный S7 lifecycle не выполнялись. Остановка после пакета; **никакого автоматического перехода к реализации**.
