# G02/A06 — NORMATIVE-DIFF

2026-10-10. G02/A05 B/C DIRECTION ACCEPTED; G02/A06 DOCUMENTATION AUTHORIZED — основание: явное задание владельца A06. Все SHALL/«должен» ниже — предложенные обязательства, NOT ACCEPTED / NOT IMPLEMENTED / NOT PROVEN. Новая норма, архитектурная квалификация и разрешение реализации требуют отдельных решений.

**A06 NORMATIVE PACKAGE READY FOR OWNER DECISION · T04 UNCHANGED · S4 BLOCKED.**

## Правила применения

Это reviewable replacement specification, не применённый patch. Все цитаты ниже сняты с canonical working tree после A04 import; byte hashes исходников приведены в конце. Исторические checkpoint/authorization entries и A02–A04 evidence не переписывать. Будущее принятие обязано отдельно указать эффективную редакцию и дату; слово ACCEPTED в старом документе не переносится на A06.

Нормативно включаемые тексты этого кандидата: [семь определений, timing, M1–M5 и boundaries](L1-G02-A06-FAILURE-MODEL.md), [дословные P1–P9 и R0–R4](L1-G02-A06-PROOF-OBLIGATIONS.md), [S4-A/B criteria и S4/S7 boundary](L1-G02-A06-S4-SPLIT.md). Ссылки — часть предлагаемой редакции; ни один из этих текстов пока не действует. Параметры 60/480/600/900/7200 сохраняются.

## ND-01 — owner register: строка T04 и G02

**Операция:** Заменить указанный текст после отдельного принятия. Сейчас исходный документ не изменён.

**Действующее требование / исходный текст:**

```text
| T04 JWT & Delayed Revocation | ACCEPTED | Подписанный access JWT ≤15 минут, local signature/lifetime/issuer/audience/scope validation. Fresh DB authority при issue/refresh. Обычный revoke атомарно фиксирует AccessValidUntil ≤ RevocationRequestedAt + 2 hours; TokenExpiresAt = min(Now + 15 minutes, AccessValidUntil - 10 minutes). Недостаточный остаток — не выдавать такие права. Ordinary API 60 секунд, max HTTP 8 минут, reserve 10 минут. Scope-specific revoke, A не отзывает B; jobs и irreversible writes не наследуют бессрочный JWT | ADR-L01/L14/L15, ORG-G04, isolation, feature/UI, L1 plan | E01/E02/E03/E06 ниже; legal gates не закрыты | Отменены ordinary revoke barrier, mandatory per-request PG freshness/stream locks/Redis/immediate JWT invalidation. Emergency security отдельно |

- **G02 Engineering validation gate:** conservative expiry/измеримые clock uncertainty и full-operation bounds по ADR-L15. Если ≤15m/≤8m не доказуемы — STOP S4/S7/S8, не ослаблять T04/E03. Настройка timeouts не требует нового owner решения; ослабление bounds требует.
```

**Предлагаемая замена (точный текст):**

```text
| T04 JWT & Delayed Revocation | PROPOSED A06, pending separate owner acceptance | Подписанный access JWT≤15 минут; local signature/lifetime/issuer/audience/scope validation, fresh DB authority при issue/refresh. Ordinary revoke атомарно устанавливает AccessValidUntil≤RevocationRequestedAt+2 hours без продления retry. TokenExpiresAt=min(Now+15 minutes, earliest AccessValidUntil−10 minutes); exp≤Now — не выдавать. HTTP от earliest controlled ingress до last controlled egress≤60s ordinary, ≤480s designated upload/download; reserve600s. Domain publication p<исходного d и применимого authority cutoff. Только доказанно изолированный private I/O может оставаться PhysicalPending; unknown outcome не rollback и не replay. Семь гарантий и supported failure model — A06 FAILURE-MODEL; P1–P9 обязательны по A06 PROOF-OBLIGATIONS. A revoke не затрагивает B; jobs/irreversible writes не наследуют бессрочный JWT | ADR-L15, Security Spec, G02, S4/S7 | Publication/egress/storage/clock proof OPEN; отдельный E03-C обязателен | Acceptance и implementation не разрешены этой строкой |

- G02 Engineering validation gate: раздельно доказать true JWT≤900s, HTTP≤60/480s, timely publication, private isolation и recovery safety по A06 R0–R4. Unbounded private I/O допустим только по P4–P6; visibility-changing COMMIT и controlled egress требуют проверяемых bounds/fences. Unknown clock/authority → fail closed. Без доказательства соответствующий путь и S4/S7/S8 acceptance BLOCKED. E03 completion не переопределяется без отдельного E03-C.
```

**Причина изменения:** Смешанный full completion разделяется по B/C; G02 сохраняет stop.

**Влияние на безопасность:** Ослабляется только physical termination изолированного I/O; новые late effects не разрешены.

**Изменения acceptance criteria:** Full-operation oracle заменяется раздельными HTTP/publication/physical/knowledge oracles; остальные security tests сохранены.

**Открытые обязательства доказательства:** P1–P9; B-PG/B-EGRESS/B-CLOCK/B-E03.

## ND-02 — ADR-L15: §Решение, абзац формулы и 23m

**Операция:** Заменить указанный текст после отдельного принятия. Сейчас исходный документ не изменён.

**Действующее требование / исходный текст:**

```text
Если exp ≤ Now, такие права не выдаются; окончательно revoked grant исключается и из refresh. При наличии конечных сроков зависимости применяется наиболее ранняя граница. Для права без конечного deadline ограничение остаётся Now+15 минут. Revoked grants не выдаются/не возвращаются при refresh; scheduled withdrawal, если представлен отдельно, не продлевает его исходный deadline и не становится повторной выдачей revoked grant. JWT, выпущенный до заявки, истечёт максимум через 15 минут; максимальная 8-минутная операция завершится максимум через 23 минуты после заявки, что укладывается в утверждённые 2 часа. Если иной existing deadline раньше, issuer уже обязан был учитывать его.
```

**Предлагаемая замена (точный текст):**

```text
Если exp≤Now, такие права не выдаются; окончательно revoked grant исключается из refresh. Для dependencies применяется earliest D. Без конечного D действует Now+15m. Scheduled withdrawal и retries не продлевают исходный D, regrant создаёт новое поколение. JWT до заявки прекращает новые admission максимум через 900s; допустимые новые domain effects и controlled egress заканчиваются максимум через 1380s после заявки лишь при доказанных publication/HTTP contracts. Это не срок физического окончания isolated private I/O или получения знания об исходе. Более ранний existing D обязательно учитывается issuer.
```

**Причина изменения:** Ограничить область утверждения 23m.

**Влияние на безопасность:** Запретить ложное обещание остановки всего I/O; не расширять authority.

**Изменения acceptance criteria:** Revoke/issue race и conservative true-expiry tests плюс independent temporal evidence.

**Открытые обязательства доказательства:** P3/P9, B-CLOCK/B-PG/B-EGRESS.

## ND-03 — ADR-L15: весь §Длительность и целостность

**Операция:** Заменить указанный текст после отдельного принятия. Сейчас исходный документ не изменён.

**Действующее требование / исходный текст:**

```text
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
```

**Предлагаемая замена (точный текст):**

```text
## Длительность и целостность

Ordinary HTTP≤60s; designated upload/download≤480s; reserve до D=600s; JWT≤900s; ordinary revoke≤7200s. Нормативные определения request deadline, authorization validity, logical completion, publication safety, physical I/O completion, recovery, supported failures и timing contract принимаются дословно из G02/A06 FAILURE-MODEL §§«Семь раздельных гарантий» и «Временной контракт». P1–P9 из A06 PROOF-OBLIGATIONS обязательны; accepted deployment/failure dossier — prerequisite, не результат этой редакции.

Cancellation передаётся каждому executor; cutoff до d оставляет доказанный publication/egress tail. Timeout не свидетельствует о rollback, quiescence или logical completion. После cutoff не начинать stages/COMMIT/chunks/retries. Уже issued private I/O может остаться pending только при доказанной изоляции; already-issued visibility-changing COMMIT обязан своевременно завершиться либо быть необратимо fenced. Если это не доказано, путь не допускается.

Каждая фотография — отдельный private upload. Submit требует fresh JWT и атомарных owner/org/course/context/eligibility/expiry/claim проверок вместе с domain/result/exact reference. DB и storage не одна transaction. Unknown сохраняет quarantine/originals, а не разрешает позднюю publication. Temporary cleanup — отдельная exact-version maintenance operation; retention политики не являются обещанием physical completion. Production retention C06/C08/PD-G07 остаётся открытым.

Каждый download/Range вновь авторизуется по JWT, server parent scope и current restrictions. Private storage/no-store; no public/static/CDN/presigned bypass, no PG lock/connection held для stream. К d прекращается controlled egress; release/cancel handles инициируется, но physical completion private read учитывается отдельно. Уже доставленные bytes не отзываются.

Jobs сохраняют actor/scope/grant generation/context и bounded step deadline, не вечную JWT authority. Перед irreversible step проверяются fresh authority/restrictions, перед выдачей — новая authorization. Каждый step должен завершить publication до своего deadline и authority cutoff; fresh check до зависшего COMMIT недостаточен. Expired command не получает новый budget через recovery или service identity. General jobs не включены в S4-B.
```

**Причина изменения:** Устранить противоречащие B/C обещания rollback/handle release/cleanup deadline.

**Влияние на безопасность:** Private late work допустим лишь при isolation; не разрешены late claim, delete success по tombstone или job authority inheritance.

**Изменения acceptance criteria:** A-AC1–6/B-AC1–7; физический и логический outcome проверяются отдельно.

**Открытые обязательства доказательства:** P1–P9, все technical blockers.

## ND-04 — ADR-L15: §Acceptance и §L0.4 — формальные пределы

**Операция:** Заменить указанный текст после отдельного принятия. Сейчас исходный документ не изменён.

**Действующее требование / исходный текст:**

```text
## Acceptance (будущие проверки, NOT RUN)

Fake-clock boundary exp/15m/2h/10m/60s/8m; JWT до/после revoke, refresh races с atomically committed deadline, regrant generation, A/B independence; signature/issuer/audience/algorithm allowlist; multi-process issuance; slow uploads/downloads/Range, disconnect/uncertain commit, proxy buffering и clock skew; jobs after expiry/revoke; real PostgreSQL transaction integrity и неизменность history. [L1 plan](../../codex/L03-L1-IMPLEMENTATION-PLAN.md).

## L0.4 — формальные пределы, E03 и доказуемость

T04 параметры не меняются. Пусть D — AccessValidUntil в общей временной шкале, e — exp, a — admission, L — полный interval до прекращения серверной операции и последней передачи bytes edge (не только handler return). Для токена, выпущенного с известным D: e≤D−600s. При идеальных часах a<e и L≤480s дают completion<D−120s. Для обычного endpoint L≤60s. Очередь/body/materialization/commit/stream включены; timeout не обновляется при активности клиента.

Пусть Δ — суммарная верхняя граница расхождения issuer/API/DB/edge clocks и rounding относительно D, λ — допускаемое validator leeway. Тогда conservative bound `completion < D−600s + Δ + λ + L`. Для D safety нужно `Δ+λ+L≤600s`; при L≤480s остаётся максимум 120s на Δ+λ. Этот запас **не разрешает** JWT после фактических 15m и операции дольше 8m. Принятая инженерная обязанность: expiry validation консервативно сокращает admission на uncertainty, не добавляет default positive leeway; иначе утверждение E03 о максимум 15m неверно. При неограниченной/неизвестной clock error гарантии нет → fail closed issuance/admission либо STOP acceptance, не «примерно два часа».

Cancellation latency, transaction commit и proxy buffering входят в L, а не в дополнительный хвост после 480s. Внутренний execution cutoff должен быть раньше 480s на доказанный completion/cancel budget; использовать monotonic elapsed time, coherent UTC bounds для exp/D. Before-commit check без ограничения времени commit не доказывает completion≤D. DB statement/transaction/lock timeouts, rollback и edge termination проверяются вместе; RequestAborted не гарантирует, что уже запущенный commit отменён. Lost connection может оставить committed outcome: восстановить через read/version/idempotency, не повторять Domain command автоматически. Если storage/commit/edge не дают проверяемого bounded behavior, нельзя принять соответствующий path в L1.

У issuance/revoke общий linearization contract: атомарное принятие revoke устанавливает D без продления при retries; issuer snapshot согласован с этим commit. Token до заявки при conservative true expiry ≤issuance+900s и L≤480s даёт completion≤request+1380s (23m), поэтому 2h bound соблюдён даже если token ещё не содержал новый D. Token после заявки учитывает D−600s. Операция непосредственно перед D не допускается этим новым JWT, поскольку exp минимум на 10m раньше; request непосредственно перед exp допускается, но завершается в bound. Уже expired JWT нельзя «спасти» восьмиминутным лимитом.

E03 SecurityBlocked commit запрещает новые login/issue/refresh и отзывает refresh-sessions; уже linearized access JWT сохраняются. Идеальный максимум новых admission — 15m после block, completion — до 23m после block; измеримая wall-clock гарантия требует conservative expiry и L bounds выше. Никакого обещания «всё прекращается через 15m» либо immediate revoke. Никакой обязательной central denylist/Redis. Audit/block atomicity не отменяет ранее committed learning history. Current ProcessingRestrictions применяются отдельно по ADR-L18, не по двухчасовой задержке.

Engineering acceptance G02: near-exp/near-D requests, ±clock offsets/rounding, delayed queues/body/commit, canceled connections, multiple API nodes, stalled downstream и edge buffering. Проверить upper bound, а не только факт посланной cancellation. Документ описывает условия доказательства; runtime evidence NOT RUN.
```

**Предлагаемая замена (точный текст):**

```text
## Acceptance и формальные пределы

Обязательны A06 R0–R4, A-AC1–6/B-AC1–7 и A05 F01–F24 без ослабления scope/restrictions/O01 tests. Временной вывод выполняется по A06 FAILURE-MODEL §«Временной контракт»: Δ+λ+B≤600 при независимых true JWT≤900 и HTTP≤60/480, остаток120s не I/O budget. Actual commit p<d доказывается отдельно; precommit timestamp не oracle. Unknown clocks запрещают issue/admission; running cutoff нуждается в независимом bounded enforcement.

E03 SecurityBlocked запрещает login/issue/refresh, revokes refresh sessions с atomic audit; previously linearized JWT сохраняются до true expiry≤15m. Максимум admission 15m и новых допустимых эффектов/egress 23m условен на доказанные contracts. Распространение B/C на PhysicalPending/OutcomeUnknown при E03 требует отдельного решения E03-C; до него действует прежняя E03 full-completion норма и пакет не вводится. Ни mandatory denylist/Redis, ни immediate JWT invalidation не добавляются. Current ProcessingRestrictions отдельно, без ordinary2h privacy delay.

Runtime evidence NOT RUN для новой редакции. Исторические A02–A04 результаты сохраняют прежний смысл; документационная проверка не G02 PASS.
```

**Причина изменения:** Переформулировать L как разные наблюдаемые события, избежать скрытой правки E03.

**Влияние на безопасность:** Сохранены timing/security числа; исключение physical I/O требует отдельного E03 согласия.

**Изменения acceptance criteria:** Actual ingress/last TX и authoritative visibility traces вместо cancellation-only checks.

**Открытые обязательства доказательства:** B-PG/B-EGRESS/B-CLOCK/B-E03; R1–R4.

## ND-05a — L1 Security Spec: boundary row

**Операция:** Заменить указанный текст после отдельного принятия. Сейчас исходный документ не изменён.

**Действующее требование / исходный текст:**

```text
| API → PostgreSQL | Даже доверенный app может ошибиться с foreign IDs | Unique/composite FK/guards, stable Actor/membership generation, atomic code/recovery/session/audit, least-privilege app без DDL/history bypass | S1/S6 |
```

**Предлагаемая замена (точный текст):**

```text
| API → PostgreSQL | Foreign IDs, ambiguous transaction outcome | Unique/composite FK/guards, scoped generations; atomic domain/result/OperationId/exact reference, P2/P3 deadline-safe visibility across readers/counts/history/claims; no late commit; least privilege no DDL/history bypass | S1/S6 foundations; S4-A auth transactions; S4-B publication |
```

**Причина изменения:** Сделать publication и recovery обязательными до file acceptance.

**Влияние на безопасность:** No bypass/private isolation распространяются на все readers, не только file endpoint.

**Изменения acceptance criteria:** P2/P3/P4/P8 coverage и B-AC1–7.

**Открытые обязательства доказательства:** P1–P9; boundary dossier.

## ND-05b — L1 Security Spec: boundary row

**Операция:** Заменить указанный текст после отдельного принятия. Сейчас исходный документ не изменён.

**Действующее требование / исходный текст:**

```text
| API → attachment storage | Private key/path не выдаёт права | Pending uploader+org+course/context, parent scope/current processing, atomic claim, no-store/bounded stream/no public static path | S4 basic / S7 hardening |
```

**Предлагаемая замена (точный текст):**

```text
| API → attachment storage | Private path/receipt не authority | P1–P9; immutable exact versions/private isolation, server uploader/org/course/context+parent/current restrictions, atomic claim, no-store, deadline-safe egress, no public bypass; unknown preserves originals | S4-B mandatory safety; S7 further hardening |
```

**Причина изменения:** Сделать publication и recovery обязательными до file acceptance.

**Влияние на безопасность:** No bypass/private isolation распространяются на все readers, не только file endpoint.

**Изменения acceptance criteria:** P2/P3/P4/P8 coverage и B-AC1–7.

**Открытые обязательства доказательства:** P1–P9; boundary dossier.

## ND-05c — L1 Security Spec: boundary row

**Операция:** Заменить указанный текст после отдельного принятия. Сейчас исходный документ не изменён.

**Действующее требование / исходный текст:**

```text
| Request → background job | Исходный JWT не perpetual permission | Persist scoped actor/generation/context refs, fresh check перед irreversible step/delivery, bounded operation, unknown authority deny | S7 |
```

**Предлагаемая замена (точный текст):**

```text
| Request → background job | JWT не perpetual authority | Scoped actor/generations/context, fresh irreversible/delivery checks, bounded publication step; unknown deny; expired command recovery только resolves existing fact | S4-B minimal outcome recovery; S7 general jobs separately authorized |
```

**Причина изменения:** Сделать publication и recovery обязательными до file acceptance.

**Влияние на безопасность:** No bypass/private isolation распространяются на все readers, не только file endpoint.

**Изменения acceptance criteria:** P2/P3/P4/P8 coverage и B-AC1–7.

**Открытые обязательства доказательства:** P1–P9; boundary dossier.

## ND-05d — L1 Security Spec: абзац O01/G02

**Операция:** Заменить указанный текст после отдельного принятия. Сейчас исходный документ не изменён.

**Действующее требование / исходный текст:**

```text
O01: single browser-wide refresh coordinator serializes короткие issuance, не учебные запросы; нагрузка bounded retries, incompatible browser fallback требует evidence. Гонки, потерянный Set-Cookie и replay не различимы только по старому token; tradeoff принят O01: 30 секунд, 409 refresh_already_rotated без Set-Cookie; lost response не гарантирует восстановление. G02: сам CancellationToken не даёт hard completion guarantee при зависшем storage/commit; acceptance stop до доказуемого timeout/cancel budget. Уже скачанные bytes и уже committed actions не отзываются.
```

**Предлагаемая замена (точный текст):**

```text
O01 сохраняется: browser-wide refresh coordinator, 30s inclusive duplicate409 без JWT/Set-Cookie, no successor redisclosure; server safety независима от coordinator. G02 требует раздельного доказательства request deadline, timely publication, isolated private I/O и recovery по A06 FAILURE-MODEL/P1–P9/R0–R4. CancellationToken не доказательство rollback/stop; unknown не abort. Late private work допускается только при доказанной невозможности менять published bytes/domain/доступ/egress. Уже скачанные bytes и timely committed history не отзываются. Без qualified boundaries S4 acceptance BLOCKED.
```

**Причина изменения:** Заменить единый completion budget раздельными гарантиями.

**Влияние на безопасность:** O01 не изменён; неизвестность не даёт права повторить эффект.

**Изменения acceptance criteria:** Сохранить O01 suite; добавить B-AC и separate outcome oracles.

**Открытые обязательства доказательства:** P3–P9; B-E03 отдельно.

## ND-06 — G02 bounded execution: successor вместо переписывания A02

**Операция:** В будущем добавить указанный successor notice с нормативной ссылкой на A06, сохранив весь A02 текст; сейчас только предложение. Сейчас исходный документ не изменён.

**Действующее требование / исходный текст:**

```text
## 1. Что требуется обеспечить

[ADR-L15 / T04](../adr/ADR-L15-jwt-delayed-revocation.md) неизменен: обычный HTTP/API ≤60s, явно длительный upload/download ≤480s, запас до AccessValidUntil 600s, access JWT ≤900s, ordinary revoke ≤7200s. Время включает очередь, body, storage, PostgreSQL, cancellation, освобождение handles и последнюю отправку edge. Успешный ответ, прекращение ожидания и завершение операции — разные события.

Различаем: (a) отсутствие поздней публикации и повторного применения; (b) прекращение всей работы, вызванной запросом. Quarantine может обеспечить (a), но не доказывает (b). Запись даже в недоступный временный объект после deadline не считается выполнением T04. Recovery после deadline не имеет права продолжать пользовательскую запись, допубликовывать её или давать ей новый бюджет.

Текущие `LocalFileAttachmentStorage`/`AttachmentService`/`AttachmentsController` и `docker/nginx.conf` не реализуют этот контракт. Синхронные open/move/delete, compensation с `CancellationToken.None`, отсутствие общего edge deadline остаются историческими G02 blockers. Этот проект их не исправляет.
```

**Предлагаемая замена (точный текст):**

```text
## G02/A06 successor — effective only after separate owner acceptance

Исторические A02 §1–§7 и I1–I7 описывают прежний full-completion contract и не редактируются. Для новой редакции применяются семь определений, timing/failure model, publication/egress/storage/recovery contracts A06 FAILURE-MODEL и P1–P9 A06 PROOF-OBLIGATIONS. I6 full physical stop не переименовывается в PASS: только изолированный private I/O освобождается от universal bound. Aborted означает отсутствие domain result и невозможность будущего commit; private PhysicalPending учитывается отдельно. Control Fence не physical quiescence ACK. Purge только exact version после no refs/claim serialization и quiescence. Gateway topology остаётся design dependency, не qualified mechanism. Новые обязательства и S4 acceptance OPEN.
```

**Причина изменения:** Сохранить audit trail A02, выделив явно новую редакцию.

**Влияние на безопасность:** Не выдавать ослабление physical completion за доказательство старых I6/V1.

**Изменения acceptance criteria:** Новые R0–R4 и F01–F24; старые V статусы сохраняются.

**Открытые обязательства доказательства:** P1–P9; B-PG/B-ISOLATION/B-FENCE/B-RECOVERY.

## ND-07 — G02 failure model: successor вместо переписывания A02

**Операция:** В будущем добавить successor notice, не заменяя историческую модель. Сейчас исходный документ не изменён.

**Действующее требование / исходный текст:**

```text
## 1. Поддерживаемая модель — контракт кандидата

Рассматриваются crash-stop/crash-restart, потеря/дублирование/переупорядочивание сообщений, bounded transient storage/network failures и зависания исполнителей. Рабочий процесс может перестать отвечать в любой фазе. Gateway, PG и edge считаются доверенными по содержанию, но могут зависнуть/упасть; Byzantine storage/администратор с raw disk access исключены. Весь admission/queue/body включён в deadline; capacity и concurrency bounded.

Для условной поддержки необходимы: pinned stack; известная погрешность часов и scheduling; отдельный fault domain supervisor; отсутствие raw write bypass; durable монотонные epochs; один авторитетный PG timeline без split brain/потери acknowledged history; storage durability и конечное прекращение outstanding I/O; контролируемый egress с абсолютным cutoff. Одновременные отказы допускаются только если остаётся действующий независимый stop path с доказанным bound для каждого зависшего участника. Коррелированный отказ исполнителя и всех его watchdog/fence механизмов не покрывается.

Это specification required capabilities, а не заявление о возможностях текущего хоста. Известный baseline: `docker-compose.yml`, PG `17.6-alpine`, local attachment volume напрямую у API, nginx без абсолютного whole-response cutoff. Ни один numeric hardware/OS completion SLA в репозитории не установлен.
```

**Предлагаемая замена (точный текст):**

```text
## G02/A06 failure-model successor — effective only after separate owner acceptance

Историческая A02 model и выводы её экспериментов сохранены. Для новой редакции применяется A06 FAILURE-MODEL целиком, включая M1–M5, unsupported correlated failures и три OPEN архитектурные boundaries. Universal physical stop для isolated private I/O не требуется; HTTP/publication obligations сохраняются. Ни один deployment пока не qualified. R1 contract gap блокирует путь, finite tests его не закрывают. Outstanding work/bytes остаются accounted; recovery outage не разрешает unbounded workers, purge или replay. E03-C — отдельный обязательный owner gate.
```

**Причина изменения:** B/C меняет набор guarantees, не исторические наблюдения.

**Влияние на безопасность:** Unsupported fault не оправдывает late publication; isolated worker не включает direct PG writer/egress.

**Изменения acceptance criteria:** Pinned failure dossier и независимые time/safety/availability oracles.

**Открытые обязательства доказательства:** B-PG/B-EGRESS/B-CLOCK/B-ISOLATION/B-FENCE/B-CAPACITY.

## ND-08 — L1 implementation plan: ### S4 — JWT, organization switching и refresh

**Операция:** Заменить указанный текст после отдельного принятия. Сейчас исходный документ не изменён.

**Действующее требование / исходный текст:**

```text
### S4 — JWT, organization switching и refresh

| Поле | Требование |
|---|---|
| Цель | Включить согласованный auth protocol и baseline authorization всех API paths. |
| Зависимости | S1–S3 PASS; O01 выбран владельцем/явно делегирован и protocol review завершён; G02 expiry/clock design validated. |
| Компоненты | API auth handlers/policies/ActorMapper, token issuer/session adapter, browser provider/coordinator, scoped query/write/file admission adapters. |
| Агрегаты/сущности | Org JWT grant snapshot, session generation, trusted resource/processing decision context. |
| Application use cases | Login/refresh/switch/logout/session, issue scoped token, request ordinary revoke с atomic D, resource admission/processing check. |
| API endpoints | `/auth/login`, `/auth/refresh`, `/auth/switch-organization`, `/auth/logout`, `/auth/session`, `/auth/csrf`; все existing learning/query/upload/download aliases проходят baseline boundary. |
| Persistence | Fresh authority on issue/refresh/switch, CAS refresh/audit, scoped deadline transaction; no per-request DB freshness ordinary grants/Redis. |
| Authorization | L16 claims one org, local validation + server resource matching + E06 processing gate. Scope не из UI; no authenticated paths temporarily exempt. |
| Domain invariants | No learning DTO/state changes; unauthorized requests не mutate; refresh не возвращает revoked rights. |
| Тесты | JWT signature/audience/issuer/15m/nbf, earliest exp/excluded grants, A→B denial/switch; two tabs different org, rotation concurrency/replay/reload/lost response, 60m/12h, CSRF/Origin; all route/count guards; revoke issuance races. |
| Acceptance | O01 documented and concurrency tests satisfy E02 without false normal logout; expiry conservative; all paths guarded, malformed scope fail closed. |
| Stop conditions | O01 unresolved, clock/expiry not bounded, unsupported coordinator without tested fallback, silent role/admin access. |
| Результат | Synthetic auth/scope API/browser with basic file/restriction/deadline gates; no general ingress cutover. |
| Переход | S4 critical matrix PASS, затем S5 authorization; hardening evidence S7 ещё обязателен. |
```

**Предлагаемая замена (точный текст):**

```text
### S4 — два отдельно разрешаемых scopes

S4-A Identity/JWT/HTTP Authorization и S4-B Files/Publication Safety определяются дословно документом A06 S4-SPLIT: scope, dependencies, A-AC1–6/B-AC1–7 и stop conditions. S4-A component acceptance не S4 acceptance; public integration требует shared PG/clock/egress evidence. Общий S4 acceptance — оба scopes, все mandatory file use cases/aliases и R0–R4. Ни один scope не разрешён принятием текста декомпозиции; S5 и далее требуют отдельных решений.
```

**Причина изменения:** Разделить работу и перенести минимальные safety prerequisites до file acceptance.

**Влияние на безопасность:** Нельзя принять S4 за счёт выключения обязательных files или переноса безопасности в S7.

**Изменения acceptance criteria:** Точный replacement criteria — A06 S4-SPLIT A-AC/B-AC; общий R0–R4.

**Открытые обязательства доказательства:** Shared B-PG/B-EGRESS/B-CLOCK; file P1–P9; separate scope authorization.

## ND-09 — L1 implementation plan: ### S7 — Files, long operations, jobs, restrictions hardening

**Операция:** Заменить указанный текст после отдельного принятия. Сейчас исходный документ не изменён.

**Действующее требование / исходный текст:**

```text
### S7 — Files, long operations, jobs, restrictions hardening

| Поле | Требование |
|---|---|
| Цель | Доказать bounded operation/processing guarantees поверх baseline guards S4. |
| Зависимости | S4/S6 PASS, ADR-L15 G02 design, E06 synthetic contract; без новых legal UI. |
| Компоненты | AttachmentService/storage/API timeouts/edge contract, Application jobs/processing ports, query restrictions, browser abort/expiry UX. |
| Агрегаты/сущности | AttachmentScope/pending lifecycle, immutable work/submission linkage, processing decision refs/current restrictions, scoped job metadata. |
| Application use cases | Separate photo upload, atomic Submit claim, timed download/Range, cancel/reconcile uncertain commit, finite temp cleanup contract, authorize job step/result, apply synthetic restrictions. |
| API endpoints | Existing upload/Submit/attachment GET/DELETE; job result endpoint только для реально требуемого L1 job adapter, не новая general export/jobs platform. |
| Persistence | Targeted claim/cleanup/restriction transaction guards, history version refs; no DB connection held for stream, no unbounded job grants. |
| Authorization | Fresh JWT admission/parent scopes; processing current-read and transactional write guard; jobs reauthorize before irreversible/delivery. Grant freshness DB на каждый HTTP не добавлять. |
| Domain invariants | Original bytes/immutable events intact; current deny > historic allow; SyntheticOnly environment, missing context deny; no elapsed limit reset by slow client. |
| Тесты | 60s/8m/10m/2h, before exp/before D, clock offsets/commit/cancel/edge, multi-photo/cancel/retry/lost connection, foreign file, job after revoke/result auth, old context/new block, DB unavailable, spoof real mode, raw SQL restriction counts. |
| Acceptance | Full L bound includes commit/cancel/edge ≤8m; conservative JWT expiry; no 2h privacy claim; temp cleanup no linked-file destruction; synthetic fresh restrictions evidenced. |
| Stop conditions | Unbounded storage/commit/stream, need weaken accepted bound, real-data freshness request, privacy bypass or job perpetual authority. |
| Результат | Measured security/operation packet и documented remaining real-pilot gates. |
| Переход | S7 PASS, no critical gaps → S8 authorization; иные jobs/features не добавляются молча. |
```

**Предлагаемая замена (точный текст):**

```text
### S7 — последующее hardening и отдельно разрешённый lifecycle

S4-B до file acceptance уже обязан обеспечить durable identity/idempotency, immutable versions, timely publication/egress, ambiguous outcome и minimal recovery/quarantine/accounting. Эти prerequisites не откладываются в S7. S7 проверяет масштаб/operational hardening, general jobs со fresh step/result authority и полный lifecycle/retention/erasure только по отдельному scope. Dependencies: S4 и S6 accepted, G02 qualified, E06 synthetic contract. Acceptance: сохранены A06 guarantees/P1–P9 при масштабировании и отказах, no perpetual job authority, safe cleanup, current restrictions; real freshness/legal gates отдельно. Unsupported HTTP/publication/fence — STOP. S8 не auto-authorized.
```

**Причина изменения:** Разделить работу и перенести минимальные safety prerequisites до file acceptance.

**Влияние на безопасность:** Нельзя принять S4 за счёт выключения обязательных files или переноса безопасности в S7.

**Изменения acceptance criteria:** Точный replacement criteria — A06 S4-SPLIT A-AC/B-AC; общий R0–R4.

**Открытые обязательства доказательства:** Shared B-PG/B-EGRESS/B-CLOCK; file P1–P9; separate scope authorization.

## ND-10 — E03: конфликт, отдельное решение

Действующая строка E03 (не заменяется данным diff):

```text
| E03 Emergency Blocking | ACCEPTED | SecurityBlocked запрещает login/issue/refresh, revokes всех refresh-sessions и audit; unblock отдельное confirmed действие, old grants не восстанавливаются. Existing JWT до exp≤15m, started operation≤8m; не мгновенное прекращение. No mandatory denylist/Redis | T04/E02; ADR-L17/L15 | S6/S7; clock/completion G02 engineering evidence |
```

Предлагаемая замена приведена **только** в отдельном решении E03-C в [OWNER-DECISION](L1-G02-A06-OWNER-DECISION.md). Причина: old full completion≤8m конфликтует с private PhysicalPending/OutcomeUnknown. Security impact: фактическая private работа и неопределённость могут пережить 23m после block; no late effects/egress и no new authority сохраняются. Acceptance: E03 block/issue races плюс раздельные physical/logical/HTTP/publication oracles. Proof: P3–P9 и B-PG/B-EGRESS. Без явного E03-C принятие B/C не согласовано; E03 и ADR-L17 автоматически не менять.

## Сопутствующая консолидация после отдельного решения

ORG-G04 и ссылки ADR-L16/L17, isolation/feature/UI/execution documents должны ссылаться на принятую редакцию без остаточных обещаний full physical completion. Это будущая документационная синхронизация, не разрешение реализации или переопределения E01/E02/E06/O01. Действующая DELETE semantics/production retention не меняется этим пакетом. Исторические checkpoints остаются историей; новый checkpoint должен явно указывать supersession, а не переписывать A02–A04 FAIL.

## SHA-256 исходников exact diff

- `codex/L03-OWNER-DECISION-REGISTER.md`: `33fd9ef96ec9756a7f550034290c3571d60faa84f64e64c98af59e1630396266`
- `docs/adr/ADR-L15-jwt-delayed-revocation.md`: `ef0c92a48de913784c747a089a1b08b616c5c0007910008d831d712777142394`
- `docs/architecture/L1-SECURITY-SPEC.md`: `72c12eefc5babed030c5d1663400d444f55b8df32be3dc2c8de8ab8bca6f1ab4`
- `codex/L03-L1-IMPLEMENTATION-PLAN.md`: `f30d1fd5de9bb215ab3a073637e65787e7fe66720af943ea835c9746aadc117c`
- `docs/architecture/L1-G02-BOUNDED-EXECUTION.md`: `faee1ce386b95120fa8f1ab75d437341c7b3fa5f571ff94a93596d3356c7516c`
- `docs/architecture/L1-G02-FAILURE-MODEL.md`: `bbc47f1ae75f75b1819253236a08e032840f8a722b6aa9f7868bc5d614d00392`
