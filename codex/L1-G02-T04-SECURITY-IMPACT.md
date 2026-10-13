# G02/A05 — T04 Security Impact

2026-10-10. PROPOSED ONLY. [Определения, модель и P1–P9](L1-G02-T04-REVISION-PROPOSAL.md).

**A05 PROPOSAL READY FOR OWNER DECISION · T04 UNCHANGED · S4 BLOCKED.**

A/B/C ниже — варианты A05, не названия worker/container/gateway A02. Анализ основан на сохранённых A02–A04, не на новой реализации. Safety NOT PROVEN; independent review NOT RUN.

## Сравнение по обязательным свойствам

| Свойство | A: T04 unchanged | B: разделение | C: узкая модель / рекомендуемый B/C |
|---|---|---|---|
| JWT≤15m | Сохранить conservative true expiry | Без изменения; timeout не продлевает exp | То же; unknown clock → deny; clock contract обязателен |
| Ordinary revoke≤2h | Existing JWT + full completion дают 23m при предпосылках | New admission/эффект/egress ограничены; physical private I/O может позже | B/C сохраняет 2h для authority/видимости, не заявляет physical stop≤2h; direct commit требует bound |
| Reserve10m | Покрывает full L и uncertainty | Покрывает HTTP/publication, не universal I/O | 120s после 480s только uncertainty; не extra commit time |
| Ordinary60s | Весь путь и quiescence | HTTP/publication deadline60s; outcome может unknown | B/C: нет автоматического расширения command до480s из-за файлов |
| HTTP8m | Full operation≤480s | Controlled egress≤480s, conditional на работающий cutoff | B/C не подменяет egress API return; непроверенный edge оставляет blocker |
| In-flight после revoke | Exp внутри запроса допускается до исходного deadline | То же, но private I/O не authority и не может публиковать | После d никакого нового effect, даже если JWT действовал при admission; fresh read законного старого результата допустим |
| Late storage writes | Даже private write после d нарушает T04 | Только неизолированная/публикующая запись нарушает safety; private orphan допускается явно | B/C допускает private-only late work при no-bypass; published mutation всегда FAIL |
| Ambiguous PG commit | Unknown после доказанного stop; no replay | Unknown может длиться; опасность late visible COMMIT остаётся | B/C требует timely authoritative commit evidence/qualified bound; не объявляет PG solved |
| Idempotency | Durable identity + one atomic result | Необходима для unknown без повторной команды | Same key/different payload conflict; new key не обход unresolved same domain intent; domain uniqueness/version guard нужен |
| Immutable versions/fencing | Physical fence и quiescence до B | Exact immutable versions, publication fence отдельно от physical fence | B/C сохраняет mutation-point fence для защищаемых bytes; PG epoch не отменяет POSIX write |
| Recovery после d | Read/classify, исходная работа уже закончена | Read/classify/contain при private PhysicalPending; no resume | Read-only resolution не получает права позднего claim/COMMIT; cleanup отдельный, доказанный safe |
| Публикация/доступ к файлам | Commit+receipt+current gates, no public bypass | Те же gates плюс доказательство timely publication | VersionId/hash/reference immutable; late receipt не превращается в eligible; каждый Range новый JWT/gates |
| Org isolation | A-revoke не отменяет B | Без изменения, включая status/idempotency/recovery | OperationId не credential; same key другой org не раскрывает чужой результат; server-root scope до counts |
| ProcessingRestrictions | ADR-L18 current reads и transactional write check | Не наследует 2h window; old snapshot не allow | B/C: current gate перед publication/новым read; synthetic stream bounded, no instant in-flight promise; real freshness OPEN |

## Критические attack/failure sequences

1. Admission законен → storage завис → HTTP закрыт → старый worker вернулся. Допустим лишь изолированный private write. Ни late receipt, ни прежний JWT не разрешают metadata/claim/URL. Объект не переиспользуется. Публикация старым worker — FAIL.
2. Проверка d внутри transaction → WAL завис → d истёк → COMMIT стал видимым в history. Ни precommit time, ни cancel, ни fence, ожидающий ту же lock, не устраняют контрпример. В прямой domain schema такой path остаётся blocked без численного contract. Quarantine после уже прочитанной истории слишком поздно.
3. ACK потерян → observer видит отсутствие → compensation удаляет bytes → исходная transaction commits. Требуется authoritative serialization и невозможность future commit до purge; при unknown bytes сохраняются.
4. Seal через rename/chmod → retained writable FD меняет файл. Это опровергнуто A03. Нужен настоящий immutable backend boundary и аудит credentials/handles, не дополнительная проверка hash перед read.
5. Refresh/regrant создаёт новые права → resolver получает новый JWT. Новый JWT разрешает читать своевременно committed факт, но не завершать старую команду. Повтор desired action возможен только как отдельно разрешённая новая команда после доказанного terminal исхода и domain concurrency checks; автоматический retry с новым key запрещён.
6. Epoch увеличен → старый primary возвращается. Без fence старого authority и общей durable history новые записи/reads не допускаются. Liveness жертвуется; новый epoch в API не доказательство storage/PG fence.
7. Deadline закрывает upstream → proxy/kernel продолжает egress. Пока последний controlled send не ограничен, HTTP свойство не доказано. Client receive позже сам по себе не нарушение: bytes могли выйти вовремя.

## Что изменяется, а что не выводится

B/C явно ослабляет прежнее physical completion требование для изолированных private operations. Это изменение смысла T04, поэтому требует owner approval, даже при тех же пяти числах. Оно не вводит per-request DB freshness обычных grants, immediate revoke, mandatory Redis или DB stream locks. DB reads ресурсов/current restrictions и operation fences остаются отдельными механизмами.

E03 SecurityBlocked запрещает issue/refresh по прежнему contract. Existing access admission≤15m сохраняется; утверждение started operation≤8m в действующем E03 связано с прежним full completion. A05 не вправе объявить его автоматически совместимым с новым physical хвостом. Owner draft выбирает направление и требует явного reconciliation без изменения E03; если это невозможно, необходим отдельный owner scope либо вариант A. T01–T06/E01–E06/O01 пока неизменны.

Safety условно выводится из атомарного результата, своевременной publication, полной mediation всех читателей, durable unique identity, immutable isolation и непротиворечивой authority history. Реализация этих предпосылок НЕ ПРОВЕРЕНА. Ограниченный список fault tests не доказывает все interleavings; нужны protocol review, contracts и независимый security review. Availability, finite recovery RTO, physical erasure и юридическая допустимость не следуют из safety.

Действующий runtime не сертифицирован по предлагаемой модели. SYNTHETIC DATA ONLY; real pilot, retention/legal basis и real restriction propagation не разрешены. Минимальный audit должен хранить operation/generation/deadline/result/receipt/timeline evidence и причины deny без JWT/secrets/файлов/персональных данных.
