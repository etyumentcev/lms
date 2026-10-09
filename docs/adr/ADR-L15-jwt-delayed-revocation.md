# ADR-L15 — JWT и ограниченный по времени обычный отзыв

2026-10-09. **ACCEPTED T04**, owner: владелец; evidence: окончательная редакция T04 в задании консолидации L0.3. Реализация NOT VERIFIED, L1 не разрешён. Supersedes cookie/per-request session и PostgreSQL revoke barrier в прежних ADR-L01, ADR-L14 и ORG-G04. Остальные принципы изоляции сохранены.

## Решение

Подписанный JWT содержит необходимые идентификаторы и ограниченные полномочия. API локально проверяет signature, issuer, audience, lifetime и scope; access lifetime ≤15 минут. Актуальные DB grants проверяются при каждом issue/refresh, без обязательной PostgreSQL freshness-проверки прав на каждом HTTP-запросе. Запросы к БД для ресурса, workflow и ProcessingRestrictions остаются возможными. Refresh не продлевает отозванные grants.

При принятии обычного revoke в одной transaction фиксируются соответствующий grant/dependencies, `RevocationRequestedAt` и `AccessValidUntil <= RevocationRequestedAt + 2 hours`, с audit; повторный запрос не сдвигает deadline вперёд. Подтверждение принятия заявки означает сохранение deadline, а не немедленную invalidation всех JWT. До commit нельзя подтвердить заявку. Regrant создаёт новое поколение, не отменяет deadline старого.

`TokenExpiresAt = min(Now + 15 minutes, AccessValidUntil - 10 minutes)`

Если exp ≤ Now, такие права не выдаются; окончательно revoked grant исключается и из refresh. При наличии конечных сроков зависимости применяется наиболее ранняя граница. Для права без конечного deadline ограничение остаётся Now+15 минут. Конкретное различение scheduled withdrawal и уже revoked state — инженерный дизайн, не основание продлевать deadline. JWT, выпущенный до заявки, истечёт максимум через 15 минут; максимальная 8-минутная операция завершится максимум через 23 минуты после заявки, что укладывается в утверждённые 2 часа. Если иной existing deadline раньше, issuer уже обязан был учитывать его.

Новая семантика **ORG-G04**: при обычном отзыве система гарантирует прекращение новых авторизованных операций в пределах двух часов с момента принятия заявки. Для этого используются краткоживущие JWT, ограничение срока их выдачи относительно предельного времени действия полномочий и ограничение длительности выполняемых операций.

A-revoke не отменяет B-grants/общую Identity. Состав JWT для нескольких организаций — **PROPOSED E01**, а не новое принятое решение: [реестр](../../codex/L03-OWNER-DECISION-REGISTER.md). UI не является authority; server сопоставляет claims с resource organization/course/group до выдачи данных, totals и mutations.

## Длительность и целостность

| Параметр | ACCEPTED |
|---|---|
| Обычный API-запрос | 60 секунд |
| Максимальная HTTP-операция | 8 минут |
| Запас до AccessValidUntil | 10 минут |
| Максимальная задержка ordinary revoke | 2 часа |

Deadline измеряется от server admission, включая очередь, чтение body, DB работу и передачу ответа; активный трафик не обнуляет таймер. Обычный endpoint ограничен 60 секундами; явно длительный upload/download — максимум 8 минут. Истечение JWT внутри уже принятой операции само по себе не требует её прерывания. При admission до exp, exp ≤ AccessValidUntil−10 минут и duration ≤8 минут операция завершается до AccessValidUntil с 2-минутным остатком. Clock skew/leeway, proxy buffering и cancellation latency должны поместиться в этот остаток; нельзя прибавлять стандартный skew сверх предела. Проверить синхронизацию часов и крайние значения реальными тестами.

Engineering requirement: согласовать edge/API/DB/storage deadlines; cancellation передавать до I/O и transactions. Timeout/RequestAborted прекращает работу, rollback незавершённой transaction, закрывает stream; background continuation пользовательской записи после отмены запрещена. Перед commit проверять deadline; cancellation во время commit может дать неизвестный клиенту исход. Не обещать rollback уже committed изменения при потере соединения: повторное чтение состояния/version и idempotency где предусмотрено, без автоматического replay commands. DB command timeout и rollback/recovery должны обеспечивать предел; одного кооперативного CancellationToken недостаточно как доказательства. Если bounded completion не доказано, L1 acceptance не проходит.

Каждая фотография загружается отдельным запросом во временное private storage. При Submit действующий JWT проверяется заново; owner+organization+course+context, eligibility/expiry и claim сверяются атомарно с записью решения. DB/files не общая transaction: failed/uncertain commit оставляет файл временным/quarantined до reconciliation. Неиспользованные temporary files очищаются по конечному сроку; численное production retention ещё C06/C08/PD-G07, synthetic fixture TTL не юридический срок.

Каждый download/Range request заново авторизуется по JWT и parent scope, передача ограничена серверным deadline ≤8 минут; медленный клиент отключается. После deadline закрыть поток/освободить handles; продолжение требует нового JWT-запроса. Не держать PG connection/lock на время передачи. Private storage/no-store, без public URLs/CDN обхода; уже доставленные bytes не отзываются. Edge buffering не должен продолжать выдачу после предельного времени.

Job сохраняет actor/scope/generation/context и ограниченный operation deadline, а не вечное разрешение из исходного JWT. Перед необратимым изменением заново проверяются актуальные полномочия/ограничения; перед выдачей результата — новая авторизация. Долгое задание разбивается на ограниченные шаги, каждый проверяет authority и deadline; revoked grant/unknown authority → stop без выдачи результата. Service identity не наследует пользовательские права автоматически. Проверка перед irreversible step и время его завершения не могут обходить AccessValidUntil; race/timeout tests обязательны.

## Отменённые альтернативы и открытые вопросы

Для ordinary revoke не требуются двухфазный `Revoking → Revoked` с блокировкой всех операций, shared/exclusive PG locks на каждый учебный запрос, PG stream lease, Redis или немедленная invalidation всех JWT. Locks для целостности записи/claim и maintenance backup/erasure не отменены этим ADR.

E01 composition, E02 refresh/browser/reset, E03 emergency, E06 current restrictions — OPEN в реестре. Экстренная блокировка compromised account — отдельное решение, двухчасовая задержка для неё не утверждена. T05 не ослабляется до двухчасового privacy block. Новые механизмы не объявлены ACCEPTED.

## Acceptance (будущие проверки, NOT RUN)

Fake-clock boundary exp/15m/2h/10m/60s/8m; JWT до/после revoke, refresh races с atomically committed deadline, regrant generation, A/B independence; signature/issuer/audience/algorithm allowlist; multi-process issuance; slow uploads/downloads/Range, disconnect/uncertain commit, proxy buffering и clock skew; jobs after expiry/revoke; real PostgreSQL transaction integrity и неизменность history. [L1 plan](../../codex/L03-L1-IMPLEMENTATION-PLAN.md).
