# G02/A03 — End-to-end timing proof obligations

2026-10-09. **INFRASTRUCTURE EVIDENCE BLOCKED · T04 NOT PROVEN · S4 BLOCKED.**

## Нормативная модель без изменения T04

Определения взяты из [A02 §6](../docs/architecture/L1-G02-BOUNDED-EXECUTION.md) и [ADR-L15](../docs/adr/ADR-L15-jwt-delayed-revocation.md): Q — admission/queue/auth; S — body/storage/stream; P — PG/commit/rollback и bounded outcome resolution; X — termination/fencing/handle closure; R — evidence recovery boundary; E — response/edge cutoff; J — clock/scheduler uncertainty. При разнесении P/R одна и та же работа не считается дважды; ни один stage не получает новый полный бюджет.

`t0` — earliest controlled ingress до очереди и body. Для каждого пути, включая error/cancel/compensation/unknown:

`L = max(last storage activity, PG operation end, worker/handle cleanup, last controlled egress) − t0`.

Достаточное консервативное условие: `Q + S + P + X + R + E + J ≤ B`, B=60s ordinary; B≤480s только явно long upload/download. До допуска очередного шага требуется `remaining ≥ proven step worst-case + all required termination tail`. Перекрытие stages не вычитается без отдельного dependency proof.

## Покрытие маршрутов (prefix /api/v1)

| Endpoint | B | Декомпозиция обязательств, включая adverse path | Результат P03 |
|---|---:|---|---|
| POST /uploads | 480s maximum при explicit long designation; иначе 60s | Q auth/admission; S body/create/write/seal; P identity+metadata/result; X stop writer/PG/handles; R unknown/quiescence evidence; E response cutoff; J | S/P/X/R/E/J неизвестны; FAIL proof |
| GET /attachments/{id}/content | 480s maximum при explicit long designation; иначе 60s | Q authorization; P parent/current restrictions; S open/read/stream; X readers/handles; R no remaining executor; E queues/cutoff; J | S/P/X/E/J неизвестны; FAIL proof |
| Каждый Range на content, если поддержан | тот же ceiling, новый request | Весь тот же путь, без продления прошлого запроса; PG lock не удерживается весь stream | Новый Range implementation NOT RUN; proof отсутствует |
| DELETE /attachments/{id} | 60s | Q; P tombstone/result vs claim; S exact-version unlink; X outstanding delete/PG; R outcome/ref/quiescence; E response; J | P/S/X/R/E/J неизвестны; FAIL proof |
| POST /problem-assignments/{assignmentId}/submissions | 60s | Q/body; P attachment eligibility + atomic claim/domain/result; S receipt/storage checks если нужны; X/R transaction completion; E/J | Не получает 480s из-за attachment IDs; P/X/E/J неизвестны |
| POST /problem-works/{problemWorkId}/messages | 60s | Q/body; P atomic attachment claim/message; S checks если нужны; X/R/E/J | Тот же ordinary blocker |
| POST /problem-works/{problemWorkId}/reviews/needs-revision и /reviews/accept | 60s | Q bounded annotation body; P original refs/current checks/domain result; S только если выполняется I/O; X/R/E/J | Metadata/annotations не long transfer exemption; P/X/E/J неизвестны |

Inventory сверено с AttachmentsController/CommandsController. Nonfile GET routes сохраняют ordinary 60s; здесь не введён status endpoint. S=0 допустимо только для доказанно отсутствующего physical I/O пути; это не обнуляет P/X/E. Успешные и abandoned запросы подчиняются одинаковому B. Отложенный purge не засчитывается как завершённый physical DELETE.

## Worst-case ledger

| Term | A02 research target ordinary / long (s) | CONTRACT upper bound P03 | Почему измерение не замена |
|---|---:|---|---|
| Q | 5 / 10 | NOT ESTABLISHED | Нет certified admission/capacity/scheduler envelope |
| S | 15 / 360 | NOT ESTABLISHED | FIFO exit не worst-case disk read/write/fsync/delete |
| P | 10 / 30 | NOT ESTABLISHED | PG healthy commit не WAL/rollback hang bound |
| X | 15 / 40 | NOT ESTABLISHED | Kill/reap не device/PG/NIC fence, independent supervisor отсутствует |
| R | 5 / 10 | NOT ESTABLISHED | Resolver может быть недоступен; unknown допустим лишь при доказанном quiescence |
| E | 5 / 20 | NOT ESTABLISHED | Inactivity timers и client receive timestamps не last controlled send |
| J | 5 / 10 | NOT ESTABLISHED | Clock snapshot не max drift/scheduling/dispatch delay |

Арифметика целей: 5+15+10+15+5+5+5=60, 10+360+30+40+10+20+10=480. Это **не timing proof** и не runtime settings. Unknown term не равен нулю и не объявляется математически бесконечным для любой платформы: отсутствует обоснованный конечный upper bound именно P03. Поэтому ни одно из двух неравенств для всех допустимых отказов подтвердить нельзя. Иной подтверждённый route budget не найден.

## Clock proof и запас

`e≤D−600`, conservative admission `a<e`, true JWT lifetime≤900; `completion < D−600 + Δ+λ+L`. Нужно `Δ+λ+L≤600`, при L≤480 остаётся максимум 120s для Δ+λ. **Эти 120s не дополнительное исполнение/rollback/egress.** Отдельно L≤60/480 и true JWT≤900 неизменны, default positive leeway не допускает фактический поздний admission.

Chrony 4.8 tracking snapshot сохранён, но upper bound Δ, drift between syncs, suspend/clock-step handling и scheduler jitter не доказаны. Probe intervals используют один monotonic clock; абсолютная accuracy не калибрована. Наносекундное представление не означает наносекундную точность. Неизвестная clock error → требование fail-closed issuance/admission, не утверждение что текущий LMS это реализует.

В A03 нет near-exp/near-D integration, 60s/480s real-time end-to-end runs или saturation suite: V1 уже не пройден и C не реализован. Успех коротких измерений не выдаётся за full-period evidence. Ordinary revoke 7200s, token completion 900+480=1380s остаются **условными** выводами ADR-L15 при выполнении предпосылок, не новыми измеренными гарантиями.
