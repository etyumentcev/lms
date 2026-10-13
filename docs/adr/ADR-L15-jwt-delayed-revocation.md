# ADR-L15 — JWT и ограниченный по времени обычный отзыв

> **2026-10-12 — CURRENT OWNER OVERRIDE.** [Teacher Revocation logical-deadline decision](../../codex/TEACHER-REVOCATION-OWNER-DECISION.md) replaces every clause below that requires physical completion/quiescence of PostgreSQL `COMMIT`, filesystem/kernel I/O, HTTP egress, cleanup, or executors within 30 minutes. Current acceptance is JWT≤15m, fresh authority on issue/refresh, immutable logical operation deadline≤30m, cancellation requested at deadline, and no success acknowledgement after deadline. Late physical effects are an accepted residual risk and remain subject to unknown-outcome/replay/access-control protections. Historical 2h/10m and physical-completion text is retained below for traceability, not as the current algorithm or readiness gate.

> **2026-10-12 — bounded teacher revocation.** [Действующий scoped contract](../architecture/TEACHER-REVOCATION.md): fresh issue/refresh, JWT≤15m, immutable operation≤30m, revoke≤60m в выбранном bounded execution envelope. Для ordinary revoke двухчасовой D и D−10m superseded; неограниченный late COMMIT/egress не удовлетворяет новому контракту. История ниже сохранена. Processing/file safety, PRS1 и release gates не сняты; общий HTTP/physical bound NOT PROVEN.

> **MVP photo storage override — 2026-10-12.** [Owner-authorized exact diff](../../codex/MVP-STORAGE-RISK-ACCEPTANCE.md): physical original tampering risk accepted; fs-verity/kernel seal, retained FD/mmap exclusion proofs and developer filesystem qualification removed only for Sapientum solution photos. Completed private upload, parent/S2/S3/current processing/deadline and all other NR1 guarantees remain required. STORAGE-PROFILE-02 CANCELLED; G02 production BLOCKED. Historical clauses/results below are preserved.


> **NR1 effective notice — 2026-10-11.** Только в NR1 scope: D1 заменяет абзац «Deadline измеряется…» на N1/N4; D2 — «Engineering requirement…» на N2/N3; D3 — два file/stream абзаца на N3/N5; D4 — весь формальный completion proof §L0.4 на N1/N2/N4/N6; D8 — производный вывод 23m в §Решение. Числа таблицы 60/480s означают application budget N1, не physical completion. JWT/admission/revoke serialization сохранены. Ordinary jobs paragraph и исключённые paths не ослаблены.
> Нормативный источник: [N1–N7 / точный D1–D8](../../codex/MVP-B01-G02-NORMATIVE-REVIEW.md), [принятое решение](../../codex/L03-OWNER-DECISION-REGISTER.md), [sync results](../../codex/MVP-B01-G02-NR1-SYNC-RESULTS.md). Только controlled SyntheticOnly и отдельно принимаемые routes; неопределённый scope → blocked. Credential issuance, grants, B08 publication, general jobs и внешние irreversible effects не получают автоматического исключения. CLOCK NOT PROVEN; технически NR1 NOT IMPLEMENTED / NOT ACCEPTED.
> Исторический текст ниже сохранён; старые failure results не превращены в PASS.

2026-10-09. **ACCEPTED T04**, owner: владелец; evidence: окончательная редакция T04 в задании консолидации L0.3. Реализация NOT VERIFIED, L1 не разрешён. Supersedes cookie/per-request session и PostgreSQL revoke barrier в прежних ADR-L01, ADR-L14 и ORG-G04. Остальные принципы изоляции сохранены.

## Решение

Подписанный JWT содержит необходимые идентификаторы и ограниченные полномочия. API локально проверяет signature, issuer, audience, lifetime и scope; access lifetime ≤15 минут. Актуальные DB grants проверяются при каждом issue/refresh, без обязательной PostgreSQL freshness-проверки прав на каждом HTTP-запросе. Запросы к БД для ресурса, workflow и ProcessingRestrictions остаются возможными. Refresh не продлевает отозванные grants.

При принятии обычного revoke в одной transaction фиксируются соответствующий grant/dependencies, `RevocationRequestedAt` и `AccessValidUntil <= RevocationRequestedAt + 2 hours`, с audit; повторный запрос не сдвигает deadline вперёд. Подтверждение принятия заявки означает сохранение deadline, а не немедленную invalidation всех JWT. До commit нельзя подтвердить заявку. Regrant создаёт новое поколение, не отменяет deadline старого.

`TokenExpiresAt = min(Now + 15 minutes, AccessValidUntil - 10 minutes)`

Если exp ≤ Now, такие права не выдаются; окончательно revoked grant исключается и из refresh. При наличии конечных сроков зависимости применяется наиболее ранняя граница. Для права без конечного deadline ограничение остаётся Now+15 минут. Revoked grants не выдаются/не возвращаются при refresh; scheduled withdrawal, если представлен отдельно, не продлевает его исходный deadline и не становится повторной выдачей revoked grant. JWT, выпущенный до заявки, истечёт максимум через 15 минут; максимальная 8-минутная операция завершится максимум через 23 минуты после заявки, что укладывается в утверждённые 2 часа. Если иной existing deadline раньше, issuer уже обязан был учитывать его.

Новая семантика **ORG-G04**: при обычном отзыве система гарантирует прекращение новых авторизованных операций в пределах двух часов с момента принятия заявки. Для этого используются краткоживущие JWT, ограничение срока их выдачи относительно предельного времени действия полномочий и ограничение длительности выполняемых операций.

A-revoke не отменяет B. E01 ACCEPTED: один educational JWT только одной org; composition/claims по [ADR-L16](ADR-L16-organization-jwt-browser-session.md), resource ownership определяется server data model.

## Длительность и целостность

| Параметр | ACCEPTED |
|---|---|
| Обычный API-запрос | 60 секунд |
| Максимальная HTTP-операция | 8 минут |
| Запас до AccessValidUntil | 10 минут |
| Максимальная задержка ordinary revoke | 2 часа |

Deadline измеряется от server admission, включая очередь, чтение body, DB работу и передачу ответа; активный трафик не обнуляет таймер. Обычный endpoint ограничен 60 секундами; явно длительный upload/download — максимум 8 минут. Истечение JWT внутри уже принятой операции само по себе не требует её прерывания. При admission до exp, exp ≤ AccessValidUntil−10 минут и duration ≤8 минут операция завершается до AccessValidUntil с 2-минутным остатком. Clock uncertainty/leeway учитываются формально ниже; buffering/cancellation/commit уже входят в полный максимум 8 минут, не добавляются из остатка. Conservative expiry не разрешает поздний admission; проверить синхронизацию часов и крайние значения реальными тестами.

Engineering requirement: согласовать edge/API/DB/storage deadlines; cancellation передавать до I/O и transactions. Timeout/RequestAborted прекращает работу, rollback незавершённой transaction, закрывает stream; background continuation пользовательской записи после отмены запрещена. Перед commit проверять deadline; cancellation во время commit может дать неизвестный клиенту исход. Не обещать rollback уже committed изменения при потере соединения: повторное чтение состояния/version и idempotency где предусмотрено, без автоматического replay commands. DB command timeout и rollback/recovery должны обеспечивать предел; одного кооперативного CancellationToken недостаточно как доказательства. Если bounded completion не доказано, L1 acceptance не проходит.

Каждая фотография загружается отдельным запросом во временное private storage. При Submit действующий JWT проверяется заново; owner+organization+course+context, eligibility/expiry и claim сверяются атомарно с записью решения. DB/files не общая transaction: failed/uncertain commit оставляет файл временным/quarantined до reconciliation. Неиспользованные temporary files очищаются по конечному сроку; численное production retention ещё C06/C08/PD-G07, synthetic fixture TTL не юридический срок.

Каждый download/Range request заново авторизуется по JWT и parent scope, передача ограничена серверным deadline ≤8 минут; медленный клиент отключается. После deadline закрыть поток/освободить handles; продолжение требует нового JWT-запроса. Не держать PG connection/lock на время передачи. Private storage/no-store, без public URLs/CDN обхода; уже доставленные bytes не отзываются. Edge buffering не должен продолжать выдачу после предельного времени.

Job сохраняет actor/scope/generation/context и ограниченный operation deadline, а не вечное разрешение из исходного JWT. Перед необратимым изменением заново проверяются актуальные полномочия/ограничения; перед выдачей результата — новая авторизация. Долгое задание разбивается на ограниченные шаги, каждый проверяет authority и deadline; revoked grant/unknown authority → stop без выдачи результата. Service identity не наследует пользовательские права автоматически. Проверка перед irreversible step и время его завершения не могут обходить AccessValidUntil; race/timeout tests обязательны.

## Отменённые альтернативы и открытые вопросы

Для ordinary revoke не требуются двухфазный `Revoking → Revoked` с блокировкой всех операций, shared/exclusive PG locks на каждый учебный запрос, PG stream lease, Redis или немедленная invalidation всех JWT. Locks для целостности записи/claim и maintenance backup/erasure не отменены этим ADR.

E01–E06 ACCEPTED. [ADR-L16](ADR-L16-organization-jwt-browser-session.md) задаёт claims/session/refresh, [ADR-L17](ADR-L17-account-security-recovery-challenges.md) — E03–E05, [ADR-L18](ADR-L18-synthetic-processing-restrictions.md) — E06. O01 exact concurrent refresh protocol ACCEPTED (30 секунд, ADR-L16); real-data restrictions freshness требует отдельного решения до пилота.

## Acceptance (будущие проверки, NOT RUN)

Fake-clock boundary exp/15m/2h/10m/60s/8m; JWT до/после revoke, refresh races с atomically committed deadline, regrant generation, A/B independence; signature/issuer/audience/algorithm allowlist; multi-process issuance; slow uploads/downloads/Range, disconnect/uncertain commit, proxy buffering и clock skew; jobs after expiry/revoke; real PostgreSQL transaction integrity и неизменность history. [L1 plan](../../codex/L03-L1-IMPLEMENTATION-PLAN.md).

## L0.4 — формальные пределы, E03 и доказуемость

T04 параметры не меняются. Пусть D — AccessValidUntil в общей временной шкале, e — exp, a — admission, L — полный interval до прекращения серверной операции и последней передачи bytes edge (не только handler return). Для токена, выпущенного с известным D: e≤D−600s. При идеальных часах a<e и L≤480s дают completion<D−120s. Для обычного endpoint L≤60s. Очередь/body/materialization/commit/stream включены; timeout не обновляется при активности клиента.

Пусть Δ — суммарная верхняя граница расхождения issuer/API/DB/edge clocks и rounding относительно D, λ — допускаемое validator leeway. Тогда conservative bound `completion < D−600s + Δ + λ + L`. Для D safety нужно `Δ+λ+L≤600s`; при L≤480s остаётся максимум 120s на Δ+λ. Этот запас **не разрешает** JWT после фактических 15m и операции дольше 8m. Принятая инженерная обязанность: expiry validation консервативно сокращает admission на uncertainty, не добавляет default positive leeway; иначе утверждение E03 о максимум 15m неверно. При неограниченной/неизвестной clock error гарантии нет → fail closed issuance/admission либо STOP acceptance, не «примерно два часа».

Cancellation latency, transaction commit и proxy buffering входят в L, а не в дополнительный хвост после 480s. Внутренний execution cutoff должен быть раньше 480s на доказанный completion/cancel budget; использовать monotonic elapsed time, coherent UTC bounds для exp/D. Before-commit check без ограничения времени commit не доказывает completion≤D. DB statement/transaction/lock timeouts, rollback и edge termination проверяются вместе; RequestAborted не гарантирует, что уже запущенный commit отменён. Lost connection может оставить committed outcome: восстановить через read/version/idempotency, не повторять Domain command автоматически. Если storage/commit/edge не дают проверяемого bounded behavior, нельзя принять соответствующий path в L1.

У issuance/revoke общий linearization contract: атомарное принятие revoke устанавливает D без продления при retries; issuer snapshot согласован с этим commit. Token до заявки при conservative true expiry ≤issuance+900s и L≤480s даёт completion≤request+1380s (23m), поэтому 2h bound соблюдён даже если token ещё не содержал новый D. Token после заявки учитывает D−600s. Операция непосредственно перед D не допускается этим новым JWT, поскольку exp минимум на 10m раньше; request непосредственно перед exp допускается, но завершается в bound. Уже expired JWT нельзя «спасти» восьмиминутным лимитом.

E03 SecurityBlocked commit запрещает новые login/issue/refresh и отзывает refresh-sessions; уже linearized access JWT сохраняются. Идеальный максимум новых admission — 15m после block, completion — до 23m после block; измеримая wall-clock гарантия требует conservative expiry и L bounds выше. Никакого обещания «всё прекращается через 15m» либо immediate revoke. Никакой обязательной central denylist/Redis. Audit/block atomicity не отменяет ранее committed learning history. Current ProcessingRestrictions применяются отдельно по ADR-L18, не по двухчасовой задержке.

Engineering acceptance G02: near-exp/near-D requests, ±clock offsets/rounding, delayed queues/body/commit, canceled connections, multiple API nodes, stalled downstream и edge buffering. Проверить upper bound, а не только факт посланной cancellation. Документ описывает условия доказательства; runtime evidence NOT RUN.
