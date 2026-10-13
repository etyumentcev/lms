# MVP-B01-G02-DEADLINE-01 — isolated application deadline

2026-10-12. Основание: явное **OWNER DECISION — MVP-B01-G02-CLOCK-01** в задании: CLOCK-01 isolated component accepted; DEADLINE-01 isolated implementation authorized; CLOCK-DEPLOY-01 open. Этот отчёт не изменяет исторические checkpoints или нормативные документы. [NR1 N1–N7](MVP-B01-G02-NORMATIVE-REVIEW.md) и ограниченный D1–D8 действуют; старые implementation-blocked notices читаются с учётом этой узкой авторизации.

**DEADLINE-01 OWNER ACCEPTANCE PENDING · CLOCK-DEPLOY-01 OPEN · G02 BLOCKED · H04/READY BLOCKED · B08 NOT AUTHORIZED · PRS1 EXACT OFF · S4 HTTP CUTOVER NOT AUTHORIZED.**

## Scope и изменённые файлы

Только новый application component, synthetic tests и отчёт/evidence:

- [ApplicationDeadline.cs](../src/Lms.Application/ApplicationDeadline.cs): trusted ingress boundary, единственный существующий deadline, temporal action gates, independent closure/COMMIT outcome.
- [ApplicationDeadlineTests.cs](../tests/Lms.Application.Tests/ApplicationDeadlineTests.cs): unit boundary/concurrency/failure/no-replay tests.
- [ApplicationDeadlineCompositionTests.cs](../tests/Lms.Infrastructure.Tests/ApplicationDeadlineCompositionTests.cs): композиция с CLOCK-01 `LocalQualifiedClockSource` и synthetic verifier; production verifier остаётся unqualified.
- Этот `codex/MVP-B01-G02-DEADLINE-01-RESULTS.md`.
- `codex/evidence/MVP-B01-G02-DEADLINE-01/`: logs, TRX, test-summary.json и preservation.json.

Existing `QualifiedAuthorityClock`, `IQualifiedClockSource`, nested `Deadline`, clock source, production DI, API middleware/routes, schema/migrations, processing writers, JWT temporal contracts, S2/S3 и production configuration **не изменены**. Нет новых domain commands/grants, cancellation source, timers или background workers. Специализированные skills `model-domain`, `implement-use-case`, `implement-feature` отсутствуют в доступном каталоге; их выполнение не заявляется. Работа и self-review последовательные, без subagents.

## Actor → use case → инварианты → VO → transitions → authority/data → tests

Trusted server ingress adapter фиксирует один `t0` на earliest controlled ingress до queue/header/body. `Capture` protected: входы только server-designated request kind и существующий abort token; клиентские t0/remaining/budget отсутствуют. Для тестов — закрытые `SyntheticIngress` adapters. Это in-process trust boundary, не защита от произвольного trusted кода; adapter обязан вызывать capture ровно один раз на новую операцию, не на retry.

Use case: передать тот же `ApplicationDeadline` через admission, locks/waits, stages, retry и egress. Ordinary = 60s, только явно designated upload/download = 480s. Unknown enum отвергается. Новая внутренняя стадия/попытка не получает нового handle. Новый Range — самостоятельный авторизованный запрос; перенос retry старой команды под новый handle запрещён интеграционным контрактом.

VO: `ApplicationRequestKind`, `ApplicationAction`, `ApplicationGateDecision`, `ApplicationGateStatus`, `CommitOutcome`. Gate — только temporal precondition; `Allowed` не является domain permission, JWT validation, durable q или результатом COMMIT.

| Данные/действие | Источник полномочия/данных | Предел этого slice |
|---|---|---|
| t0, kind | trusted ingress adapter; qualified clock sample | production ingress не подключён; client headers не читаются |
| remaining | существующий `QualifiedAuthorityClock.Deadline` | synthetic source/verifier доказывают компонент, не физическое время |
| abort | существующий caller CancellationToken / explicit Invalidate | только terminal closure, без утверждения rollback |
| admission | temporal gate + отдельно существующие JWT/scope gates | U(a)<exp и signature/scope остаются у caller |
| q после locks | temporal gate + transaction owner/domain/current processing/authority | U(q)<D, guards/CAS/immutable set остаются у writer; gate не делает q durable |
| COMMIT dispatch / ACK | trusted transaction/transport adapter | single attempt; ACK сообщает факт, не новое полномочие |
| response/JWT/cookie/chunk | fresh ResponseEnqueue + existing response/credential policy | gate не выдаёт credentials; late ACK не bypass |

Тесты ниже проверяют положительный synthetic control, отрицательные temporal inputs и честный unknown outcome. Новых data-access paths нет; org/course/group/role/parent/count negative matrix не расширяется и не подменяется clock tests.

## Композиция и инварианты

```text
IQualifiedClockSource (existing; synthetic/local qualified source in tests)
  -> QualifiedAuthorityClock.StartDeadline(B) (existing)
     -> cutoff = monotonicEarliest(t0) + B (existing)
        -> TrustedApplicationIngressAdapter.Capture -> один ApplicationDeadline
           -> Check(Admission / StartStage / DomainDecision / CommitDispatch / ResponseEnqueue)
           -> DispatchCommit(callback): fresh gate -> Unknown -> one callback invocation
           -> AcknowledgeCommit: external fact only -> fresh ResponseEnqueue required
```

`remaining=max(0,cutoff−monotonicLatest(now))` — существующий conservative lower bound. Qualified clock сериализует sampling и запрещает regression/смену epoch; application lock сериализует terminal state и single dispatch reservation. Нет subtraction UTC для бюджета, нового stopwatch или reset API. Unknown source/read exception/непригодная uncertainty/epoch → clock fail-closed; отсутствующий deadline при ingress даёт terminal ClockUnqualified. Null remaining не преобразуется в ноль или Allow.

`Check` возвращает snapshot, **не reusable permit**. Перед каждым новым action требуется новый вызов после ожиданий. `DispatchCommit` сам повторяет проверку, поэтому ранее полученный Allow после паузы не используется. Callback должен синхронно передать один уже фиксированный атомарный набор transport; запрещены retry, scheduling будущей задачи, новая mutation decision и domain authorization внутри callback. Его возврат `Allowed` описывает gate **перед dispatch** (поля этого snapshot тоже относятся к нему), а не ACK/успех. Текущее состояние читается новым `Check`.

Gate lock не удерживается через external callback; инвалидировать старую операцию можно и пока transport заблокирован. Между последней проверкой/резервированием попытки и transport send остаётся check-to-dispatch race. Late COMMIT уже допустимого неизменного набора в отдельно принятых NR1 paths не объявляется rollback. При expiry, обнаруженном до попытки dispatch, callback вообще не вызывается. Нет `allowLateCommit`/grants/credential exemption switch.

## Состояния

Closure и external outcome ортогональны:

| Состояние | Переход / поведение |
|---|---|
| Open + NotIssued | fresh gate может Allowed; remaining только уменьшается |
| DeadlineExpired | remaining=0; все новые действия закрыты; equality уже deny |
| ClockUnqualified | unknown remaining; все новые действия закрыты; новый clock epoch не оживляет handle |
| OperationInvalidated | explicit Invalidate / observed caller cancellation; terminal для handle |
| Unknown | устанавливается **до** invocation COMMIT callback; pending, exception и потерянный ACK не разрешают replay или response |
| AcknowledgedCommitted | trusted ACK сообщает факт, в том числе поздний; не сбрасывает closure; при открытом deadline возможен только ResponseEnqueue |

При нескольких причинах closure сохраняется первая наблюдённая причина. При Unknown `Status=ExternalOutcomeUnknown`, а `Closure` отдельно сохраняет expiry/clock/invalidation; это не скрывает clock failure. После ACK неизвестность COMMIT снимается, но terminal closure остаётся. После первой попытки dispatch admission/stage/q/повторный COMMIT запрещены даже при своевременном ACK. Нет API «считать aborted», auto replay/resume, нового transaction или physical-completion flag. ACK не доказывает завершение других I/O, egress или quiescence.

Expiry обнаруживается при gate, а не независимым timer. Caller обязан по отказу прекратить новые действия и инициировать существующий cancellation/abort/rollback protocol. Этот slice не обещает callback к wall-clock моменту d, удалённый rollback, остановку queued bytes или освобождение pending capacity. Уже переданные transport bytes не отзываются.

## Матрица application actions

| Action | Fresh check / component evidence | Обязательство интеграции |
|---|---|---|
| Admission | 60/480, before/equal/after, queue+body consumption, unknown source | capture до queue/header/body; JWT U(a)<exp и прочие admission/capacity gates |
| StartStage | same handle/retry без reset; concurrent reads; invalidation после Allow | не передавать cached Allow; cancellation/abort enforcement |
| DomainDecision | strict deadline после synthetic lock wait; expiry запрещает новый q | одна PG transaction; fresh domain/authority U(q)<D/current processing под всеми locks/CAS |
| CommitDispatch | fresh check внутри single-attempt wrapper; pause до wrapper deny; concurrent dispatch один winner; exception → Unknown | фиксированный допустимый набор, непосредственный transport send, dispatch margin/envelope; route-specific NR1 eligibility |
| ResponseEnqueue | strict границы; pause до enqueue; late ACK и unknown deny; timely ACK positive | gate для каждого chunk и success/JWT/cookie непосредственно перед enqueue; реальные queues и transport tail отдельно |

Для каждого из 5 actions проверены 3 границы × 3 request kinds = 45 boundary cases. Уже cached Allow не используется в повторной проверке. Пауза **внутри** dispatch после последнего gate отдельно показана как допустимый late external fact с закрытым старым ответом, а не как доказанный dispatch bound.

Credential issuance и grant creation не получают NR1 late-COMMIT exception: здесь не создаются credentials/grants и не выбираются разрешённые routes. Универсальный temporal gate не доказывает пригодность excluded path. Их отдельные strict issuance/transaction contracts обязательны; закрытый ResponseEnqueue после late ACK не является доказательством отсутствия позднего grant в БД.

## Transaction, retries и physical accounting

Новых database writes нет. Будущий B02 writer остаётся transaction owner: isolation, canonical OperationId/payload binding, lock order, current ProcessingRestrictions/authority, CAS, exact references, atomic domain/result/audit и idempotency должны приниматься отдельно. Внутренний lock этого компонента защищает только in-memory gate state и single dispatch, не DB serialization. Разрешённый temporal q не фиксирует и не проверяет содержимое mutations.

OutcomeUnknown запрещает повторную попытку в этом handle; никакой callback retry не выполняется, даже после exception. Durable no-replay между процессами/рестартами/новыми keys, authoritative resolver, proven abort, PhysicalPending slots и finite caps **не реализованы**. Production restart должен закрывать admission до reconciliation; создание нового handle не разрешает replay неизвестной команды. HTTP/body/transport и DB cancellation не считаются доказанным rollback. Pending capacity нельзя освобождать на основании этого gate.

## Component evidence

SDK `/tmp/b07c2g/sdk/dotnet` 10.0.401, target net10.0; команды из `/home/tea/github/lms`:

```sh
/tmp/b07c2g/sdk/dotnet test tests/Lms.Application.Tests/Lms.Application.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~ApplicationDeadlineTests --logger trx --results-directory /tmp/deadline01/targeted
/tmp/b07c2g/sdk/dotnet test Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/deadline01/regression
```

Первая targeted проверка: **64/64 PASS**, failed 0, skipped 0. После неё добавлены ещё 4 application cases и 6 CLOCK-01 composition cases; все проверены полной regression. Первая проверка не суммируется с финальной.

**Финальная regression: 859/859 PASS, failed 0, skipped 0**, exit code 0:

| Assembly | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Domain | 43 | 0 | 0 |
| Application | 340 | 0 | 0 |
| Infrastructure | 257 | 0 | 0 |
| API | 219 | 0 | 0 |

В финальной regression **74 новых deadline tests** (68 application + 6 infrastructure composition) и **53 existing CLOCK-01 tests**; обе выборки Passed, извлечены по полным test names из TRX, не выведены из прежнего отчёта. Остальные 732 existing tests тоже PASS. Positive controls используют truthful synthetic UTC/monotonic intervals и trusted synthetic verifier. Existing integration fixtures запускают disposable PostgreSQL 17.6 с полной цепочкой миграций; production DB не используется.

Evidence: [regression log](evidence/MVP-B01-G02-DEADLINE-01/regression.log), [первый targeted log](evidence/MVP-B01-G02-DEADLINE-01/targeted.log), [assembly counters и именные выборки](evidence/MVP-B01-G02-DEADLINE-01/test-summary.json); TRX в подкаталогах `regression/` и `targeted/` того же evidence directory.

Отказы independently asserted: exact expiry/после него → DeadlineExpired; null/source exception/epoch → ClockUnqualified; abort/invalidation после Allow → OperationInvalidated; COMMIT exception/pending → ExternalOutcomeUnknown, callback count остаётся один. Timely ACK → response allowed до d, late ACK → response denied. Concurrent 16 readers ×100 samples → monotone budget, terminal expiry. Concurrent dispatch contender tests → ровно один callback; explicit invalidation не ждёт завершения callback.

## Deployment evidence и B02 prerequisites

**CLOCK-DEPLOY-01 OPEN.** Fake clock/verifier inputs — доверенные synthetic fixtures, не доказательство external UTC/rate/age/continuity envelope. `UnprovenLocalClockVerifier` по-прежнему не допускает application action. Реальные step/suspend/reboot/OS stalls, production dispatch bounds и HTTP queues этим slice не квалифицированы.

Условия последующего отдельно авторизованного B02 integration:

1. Принять реальные earliest controlled ingress, server route designation 60/480 и propagation одного handle до/после queue/header/body/retries; client t0/kind/budget не authority. На новых Range новая авторизация; на retry неизвестной команды запрещён новый обходной handle.
2. Закрыть CLOCK-DEPLOY-01 независимым time-envelope evidence, verifier и поддерживаемым failure profile. Unknown bounds нельзя подставлять как ноль.
3. Принять cutoff margin и численный dispatch budget, check-to-send/check-to-COMMIT scheduler assumptions, overload/invalidation propagation; fault-inject pauses между check и action. Sampled gate не обещает hard atomic time/send.
4. Подключить fresh gates ко всем actions в конкретном принятом path, с existing cancellation/abort, не заменяя JWT/authority/current processing/lock/CAS. Выбор learning NR1 path требует отдельной acceptance; issuance/grants/B08 не включены.
5. Реализовать и доказать transaction protocol и durable operation/capacity/recovery/no-replay accounting; неизвестный COMMIT не rollback, результат resolver только с новой авторизацией. Старую authority не возобновлять.
6. Проверить реальные response enqueue, success/JWT/cookie suppression после d/unknown, edge/HTTP buffering и tail. Application closure не physical last TX; transport enforcement отдельный slice.
7. Повторить route-by-route negative authorization/parent/count/leakage, реальные PG race/ACK loss, HTTP/browser tests и отдельную owner acceptance. Unit/composition/regression этого slice не E2E ACCEPTED, SYNTHETIC MVP ACCEPTED или PILOT AUTHORIZED.

HTTP ingress/egress integration, real dispatch enforcement, credential/grant temporal integration и deployment faults **NOT RUN / NOT PROVEN**. Existing API regression — compatibility evidence, не разрешение S4 HTTP cutover.

## Guardrails §7 / self-review

| Проверка | Статус / evidence |
|---|---|
| Actor/use case/invariants/states | covered — контракт и state matrix выше |
| Accepted S2/S3/PRS1; blocked gates | covered — исходные contracts/production files не изменены; integration отсутствует |
| Org/course/group/ownership; client authority/IDOR | not applicable — новых access paths/IDs нет; trusted kind не принимается из клиента |
| Subjects/embedded sources; restriction narrowing | not applicable — processing data/writers не изменены, время не даёт permission |
| Lists/counts/aggregates/post-pagination leakage | not applicable — queries/materialization отсутствуют |
| Attachments/parent bypass/mutable history | not applicable — file/storage/history mutations отсутствуют |
| Transactions/lock order/CAS/stale authorization/retry/unknown | covered в component scope — temporal check не authority cache; single dispatch и Unknown tests; DB/durable protocol blocked до B02 |
| Validation/limits/safe error | covered — enum validation, fixed budgets, strict boundaries, missing source/error fail-closed, terminal invalidation |
| Unit/integration/negative/E2E | unit + CLOCK-01 composition + full .NET regression; новые access negatives N/A; actual HTTP/E2E NOT RUN |
| Неавторизованные schema/API/security/unrelated edits | covered — только новые перечисленные файлы, preservation evidence ниже |
| Real data/secrets/unsafe logs/unsupported readiness | covered — synthetic fixtures; нет secrets/logging в компоненте; deployment acceptance не заявляется |
| Точные проверки/remaining gaps | evidence ниже; физическое время/dispatch/HTTP/durable recovery NOT PROVEN |

## Preservation и owner gate

Canonical directory `/home/tea/github/lms`. Checkout изначально dirty на `codex/l1-s4-jwt-api-authorization`, HEAD `492e30aef65bbddd24f107903bfaa54a563abe39`; branch не переключалась на main по прямому требованию сохранить branch/HEAD/index. Commit/merge/rebase/push/deploy не выполнялись. SHA-256 baseline охватывает tracked/untracked files кроме `.obsidian/` и ignored build outputs. Existing checkpoint documentation сохраняется. Общий `git diff --check` сообщает исходный `docs/content/COURSE-CONTENT-SPEC.md:1325` (new blank line at EOF); посторонний файл не исправлялся.

Итог [preservation.json](evidence/MVP-B01-G02-DEADLINE-01/preservation.json): **567/567 baseline files побайтово сохранены; branch, HEAD и SHA-256 index совпадают**. Whitespace новых source/test/report files — PASS; локальные ссылки отчёта проверены. `.obsidian/` и ignored build artifacts не входят в утверждение сохранности.

Остановиться на owner acceptance этого isolated component. Следующий B02/HTTP/deployment slice не начат. CLOCK-01 принят владельцем; DEADLINE-01 этим отчётом не объявляется принятым.
