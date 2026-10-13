# G02/A04 — Worst-case timing ledger

2026-10-09. **V1 CONTRACT EVIDENCE BLOCKED.** Кандидат C04-1; [контракты и источники](L1-G02-A04-CANDIDATES.md), [подробная матрица](L1-G02-A04-CONTRACT-MATRIX.md). Нормативная композиция из [A03 timing proof](L1-G02-TIMING-PROOF.md) и [A02 architecture](../docs/architecture/L1-G02-BOUNDED-EXECUTION.md) сохранена.

## Неизменные обязательства

`t0 = earliest controlled ingress`, до admission queue/body. Для успешного, отменённого, ошибочного и unknown пути одинаково:

`L = max(last storage activity, PG operation end, worker/handle cleanup, last controlled egress send) − t0`.

Достаточное консервативное условие: `Q + S + P + X + R + E + J ≤ B`.

`B=60s` ordinary, `B≤480s` только explicitly long upload/download. Detection, dispatch, termination и подтверждение quiescence входят в бюджет исходного request. Параллельность не сокращает сумму без dependency proof; неизвестный член не равен нулю. A02 illustrative allocations не выдаются за численные контракты C04-1.

| Term | Что включает C04-1 | Контрактный worst-case bound | Источник / статус |
|---|---|---|---|
| Q | Ingress/TLS/header/body admission, arm supervisor, auth, bounded queue | Не установлен | E1/E2, U1–U3; NOT ESTABLISHED |
| S | S3 read/write/multipart completion/seal/delete, transfer; outstanding service I/O | Не установлен | S1–S3; NOT ESTABLISHED; abort-as-stop CONTRADICTED |
| P | PG query/commit/rollback/backend execution, выделенная часть outcome resolution | Не установлен | P2–P6; NOT ESTABLISHED |
| X | Detection → supervisor dispatch → EC2/IAM fence → worker/backend/device stop → handles/quiescence | Не установлен | F1–F3, U1–U3, S2; NOT ESTABLISHED |
| R | Оставшаяся часть authoritative evidence/resolve, без повторного счёта P; доказательство невозможности late effect | Не установлен | P2/P3/P6, S2; NOT ESTABLISHED |
| E | Response, codec/kernel/ENA/NIC queues, retransmission/cutoff, last-send certificate | Не установлен | E1–E3, F1/F2; NOT ESTABLISHED |
| J | Clock uncertainty/drift, granularity и неучтённый scheduler jitter; без повторного счёта dispatch X | Не установлен | C1, U1/U3; NOT ESTABLISHED |
| Σ ordinary | Все требуемые stages и stop tail | **≤60s не доказано** | V1 FAIL / NOT ESTABLISHED |
| Σ long transfer | Те же stages, общий исходный deadline | **≤480s не доказано** | V1 FAIL / NOT ESTABLISHED |

Это отсутствие доказанного конечного bound, не утверждение математической бесконечности каждой операции. Для V1 достаточно одного отсутствующего обязательного bound; здесь независимые пробелы в storage, PG, edge, supervisor и времени.

## Числа, которые нельзя подставлять в сумму

| Число | Значение | Почему не контрактный upper bound |
|---|---|---|
| 60s / 480s | T04 ceiling и исследуемые stream timeout ceilings | Установка timer не доказывает момент физического прекращения; без известного tail нельзя выделить usable stage budget |
| 60–120s | Typical RDS failover (P3) | Может увеличиваться; не bound PG termination, rollback или authority resolution |
| Секундная точность Wait.Timestamp | Отбрасывание milliseconds (U1) | Это granularity, не максимальная задержка dispatch/EC2 stop; нельзя принять J=1s |
| Retain-until | Период WORM-защиты S3 version | Не stop latency; до него physical deletion может быть запрещён |
| 120s = 600−480 | Остаток clock/admission reserve | Не дополнительное время rollback/storage/egress |

Нет A04 latency экспериментов, p99, max-observed, численных SLA по scheduler/queue/NIC или оценок «обычно достаточно». Длительности предыдущих P03 probes не evidence для C04-1.

## Покрытие путей

| Путь (`/api/v1`) | B | Неразрешённые обязательства |
|---|---:|---|
| POST /uploads | 480s только explicit long, иначе 60s | Q/S/P/X/R/E/J, включая потерянный S3/COMMIT ACK и multipart abort |
| GET /attachments/{id}/content, каждый Range | То же правило | PG authorization, S3 read, reader termination, last-send; Range не продлевает старый request |
| DELETE /attachments/{id} | 60s | Tombstone/claim serialization, physical delete, protected-version retention conflict, quiescence |
| POST /problem-assignments/{assignmentId}/submissions | 60s | Atomic claim/result, commit/fence, unknown outcome; attachment IDs не long exemption |
| POST /problem-works/{problemWorkId}/messages | 60s | Те же PG/claim и edge obligations |
| POST /problem-works/{problemWorkId}/reviews/needs-revision, /reviews/accept | 60s | Domain commit, references/current checks и termination/edge |
| Прочие ordinary GET/commands | 60s | Q/P/X/E/J; S=0 только если отсутствие storage I/O доказано для конкретного пути |

Новый запрос/status/recovery не получает права завершить позднюю старую mutation. Outcome может остаться unknown после **доказанного** physical completion, но неизвестный stop нельзя вынести из L переименованием в recovery. Таблица описывает obligations; интеграция C04-1 с маршрутами не реализована.

## Остальные константы T04

Сохраняются `e≤D−600`, conservative admission `a<e`, true JWT lifetime≤900s, ordinary revoke≤7200s. Как в A03:

`completion < D−600 + Δ + λ + L`; требуется `Δ+λ+L≤600`.

При L≤480 на Δ+λ остаётся максимум 120s. Ни L, ни Δ/λ для C04-1 не доказаны. `900+480=1380s` остаётся лишь условным следствием lifetime/completion предпосылок; это не новый acceptance bound или разрешение изменить 7200s. **60/480/600/900/7200 не изменены.**
