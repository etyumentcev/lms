# G02/A05 — Acceptance and fault-injection matrix

2026-10-10. PROPOSED acceptance; все новые execution tests **NOT RUN**. Исторические A03 probes и 355/355 regression не доказательство нового протокола.

**A05 PROPOSAL READY FOR OWNER DECISION · T04 UNCHANGED · S4 BLOCKED.**

[Proposal / P1–P9 / M1–M5](L1-G02-T04-REVISION-PROPOSAL.md) · [Security impact](L1-G02-T04-SECURITY-IMPACT.md).

## Gates и свойства

A02 V0–V3 сохраняют прежний исторический статус: research authorized; V1 FAIL/NOT ESTABLISHED, V2/V3 NOT SATISFIED. Ниже новые proposed gates R0–R4, не переименование старого FAIL в PASS.

| Gate | Требование | Проверка / артефакт | Статус |
|---|---|---|---|
| R0 | Отдельная утверждённая редакция требований, failure model и scope реализации; A04 archived; E03 consistency разрешена | Owner decision, normative diff, stable A04 provenance | PENDING OWNER; A05 draft не approval |
| R1 | Contracts всех enforcement boundaries, numerical bounds для HTTP/publication и явно выбранных physical paths | Pinned stack, clock/dispatch/capacity ledger, no-bypass map, commit/edge fence dossier | NOT ESTABLISHED |
| R2 | Протокол/implementation обеспечивают P1–P9 | State-machine/interleaving review, DB constraints, IAM/FD/mount audit, unit/PG tests | NOT IMPLEMENTED |
| R3 | Adversarial end-to-end oracle, включая реальные60/480s | Полная матрица ниже на synthetic стенде; traces без потерянных событий | NOT RUN |
| R4 | Security/DDD review и вся S4 acceptance | Независимый review, regression, HTTP/browser/security evidence; owner acceptance | NOT RUN; S4 BLOCKED |

| Свойство | Гарантия / модель | Механизм проверки, PASS oracle |
|---|---|---|
| Request deadline | H≤60/480s при M1/M4 contracts | Earliest ingress timestamp и last controlled TX, включая retransmits/queues; independent watchdog evidence. close/EOF/log не достаточны |
| Authorization validity | True JWT≤900s; D≤revoke+7200s; exp≤D−600s; conservative admission | Boundary clock tests, issuance/revoke linearization, generation races и trusted clock envelope |
| Logical completion | Единственный Committed/Aborted, unknown допустим без bounded RTO | Durable identity/result и primary serialized observer, no future opposite outcome; timeout не counted as aborted |
| Publication safety | No new effect p≥d или по stale operation authority; M1/M2 и qualified M3 | Все DB readers/events/counts/claims и storage versions instrumented; доказательство времени p. Precommit stamp непригоден |
| Physical completion | Только объявленные bounded paths M1; M2 private work может pending | Worker/backend/device/storage observations раздельно; no quiescence claim без lower-layer contract; orphan accounting |
| Recovery | Resolve existing fact, no replay/resume; M5 authority continuity | Command invocation count, immutable history, same OperationId, fresh status authorization, safe purge refs/quiescence |

Численные stage budgets нельзя заполнить p99/max observed. Неизвестный обязательный bound → R1 FAIL, missing oracle/trace → INCONCLUSIVE и gate не пройден. Модель исключений не меняется после неудачного теста. Новый протокол не считается доказанным только потому, что тесты проходят.

## Negative tests / fault injection

Во всех строках future status NOT RUN. Fault fixtures только изолированные synthetic, не shared disk/production. Trace связывает OperationId, org/actor, grant/execution generations, immutable version, d, cutoff, commit outcome и controlled-send; clocks имеют измеренный и контрактно ограниченный envelope.

| ID / coverage | Injection / интерливинг | Обязательный oracle и expected result |
|---|---|---|
| F01 P1 | Два узла, same key same digest; затем different digest | Ровно один domain effect/result; conflict для иной команды; no deadline reset |
| F02 P1/P8 | Same key и guessed OperationId из другой org/actor | Deny без чужого status/content; scoped unique binding и server ownership |
| F03 P3 | Pause после deadline check до COMMIT; resume после d | Ни одного late visible effect. Если direct PG commit проходит — FAIL архитектуры, не допустимый unknown |
| F04 P3/M3 | WAL/flush stall и lost COMMIT ACK, timer/cancel во время commit | Authoritative commit time/visibility плюс independent fence evidence; нет timely proof → deny; contract отсутствует → R1 FAIL |
| F05 P7 | Observer видит absent пока writer uncommitted, затем commit | No abort/purge/replay по absence; сохранить bytes, один исход |
| F06 P7 | Потерять ROLLBACK ACK, resolver unavailable | Unknown без конечного RTO; сохранены private bytes/slots, no success/no replay |
| F07 P4 | Storage open/read/write/flush hang; deadline; поздний resume | HTTP cutoff независимо; только старая private version меняется. PhysicalPending явно; no published mutation |
| F08 P5 | Seal, retained writable FD; поздний write, rename/chmod | Published hash/length/version неизменны; surrogate seal rejected; иначе FAIL |
| F09 P6 | Pause между epoch check и mutation; fence/generation change | Stale publication CAS denied на mutation boundary; PG fence не выдаётся за storage quiescence |
| F10 P6/M5 | Crash/restart на каждом durable boundary; delayed old IPC | Default deny до restored epoch/identity; старый worker не публикует/не получает новый d |
| F11 M5 | Partition/failover, возврат old primary; stale replica absent | Нет двух authorities/потери acknowledged result; без fence fail closed, никаких новых команд |
| F12 P2/P7 | Concurrent claim/delete/recovery purge одного exact version | Atomic eligibility/result; attached original не удаляется; purge только после no refs + no writer evidence |
| F13 P5 | Hash/length mismatch, missing version, storage ACK без подтверждённой durability | Quarantine; no dangling successful attachment/content |
| F14 HTTP | Slow/trickle body и consumer, buffering on/off, каждый Range | Общий t0 не сбрасывается; actual last controlled TX≤d, ordinary60s и long480s отдельно |
| F15 M4 | Edge/event-loop/OS hang; outstanding kernel/NIC/retransmit queues | Independent cutoff и lower-layer TX oracle; отсутствие future send evidence/contract → не PASS |
| F16 time | Near exp/D, ±offset/rounding, clock step/suspend, sync loss | JWT true≤900s, H≤B, p<d и до D; unknown clocks deny; запас120s не extra execution |
| F17 revoke | JWT до/после revoke, concurrent refresh, retry revoke/regrant | D не продлён, revoked grants не reissued, новое поколение не оживляет старое; A не влияет на B |
| F18 restrictions | Current block после admission, stale snapshot write, missing context/DB outage | Transactional restriction/write serialization; новый read/status deny; synthetic stream только исходный bound, no claimed instant stop |
| F19 recovery | Поздний receipt/ACK, recovery с новым JWT после d | Только чтение timely committed факта; no late claim/domain effect, no replay old command |
| F20 capacity | Много uninterruptible private writers; resolver down | Bounded worker/byte/queue budgets; admission reject до side effects; timeout не создаёт свободный slot фиктивно |
| F21 bypass | Raw mount/FD, alternate credentials, static/CDN/presigned path | Ни один worker/reader не обходит publication/immutable/authorization gate; negative access audit |
| F22 lifecycle | Delete protected immutable version / purge с неизвестным commit | Нет ложного physical-delete success; logical tombstone не physical erase; retention conflict explicit |
| F23 security | Forged JWT/org/context, valid JWT wrong course/group, old historic allow | Scope до SQL counts/materialization, E06 current deny, no real-mode header bypass |
| F24 existing S4 | O01 races/lost refresh response, cookies/Origin/CSRF, route aliases | Прежние O01/E01–E06 tests без ослабления; no JWT/Set-Cookie в duplicate409; no alias bypass |

Для F03/F04 наблюдаемая поздняя visibility не лечится выдачей HTTP timeout. Если будущий design выбирает private intents, tests должны доказать, что каждый existing reader/event consumer исключает intent и что последний promotion commit сам своевременен; иначе тот же FAIL.

## Endpoint coverage

Все `/api/v1` routes из S4 inventory обязательны, включая оба work GET aliases, teacher review queue, course/lesson/assignment workspace и counts. Ordinary GET/POST commands/DELETE имеют B=60s. POST uploads и GET attachments/content (каждый Range) могут иметь B≤480s только при explicit designation. Submit/messages/review accept/needs-revision не получают 480s из-за attachment IDs. Login/refresh/switch/logout и status/recovery delivery также bounded ordinary requests; новый status endpoint пока лишь design dependency, не создан.

Files: pending и attached paths, private staging, immutable versions, claim/delete races, orphan unknown, no-store и no public URL. Jobs не получают вечную authority; реализация general jobs/S7 не включена. S4 acceptance нельзя получить отключением обязательных file use cases.

## Проверка самого A05

Проверяются четыре новых Markdown artifacts, локальные ссылки, единый status, сохранность всех pre-existing repository/A04 файлов, неизменность HEAD/ветки и отсутствие runtime changes. Backend regression/fault injection не запускаются для документационного изменения. Финальный результат документарной проверки фиксируется в owner package; он не R1–R4 PASS.
