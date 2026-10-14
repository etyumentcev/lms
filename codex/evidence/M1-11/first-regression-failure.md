# First M1-11 full regression failure

Дата: 13.10.2026. Это наблюдение сохранено и не заменяется последующим зелёным прогоном.

Команда:

```text
/tmp/lms-dotnet/dotnet test Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false
```

Результат первого полного запуска:

```text
Domain:         43 passed, 0 failed
Application:   369 passed, 0 failed
Infrastructure: 341 passed, 1 failed
API:           382 passed, 0 failed, 10 opt-in skipped

Failed: Lms.Infrastructure.Tests.SchemaFoundationTests.Concurrent_overlapping_bindings_have_one_winner
Expected loser SQLSTATE: 23P01 (exclusion_violation)
Actual loser SQLSTATE:   40P01 (deadlock_detected)
```

Инвариант теста «ровно один winner» сохранился, но строгий expected loser outcome не выполнился. Попытка ослабить assertion, разрешив 40P01, была отклонена при self-review и полностью отменена. Runtime/schema/test assertion в итоговом tree не менялись.

Последующие прогоны исходного строгого теста:

- full Infrastructure suite: 342/342 PASS, `regression/infrastructure-original-assertion.trx`;
- final full solution: Domain 43 + Application 369 + Infrastructure 342 + API 382 PASS, API 11 opt-in SKIP, `regression/current-final-*.trx`.

Классификация: flaky reliability observation, не скрытый PASS и не доказанный исправленный defect. Требуется отдельное owner/engineering решение, если deterministic no-deadlock outcome является обязательным acceptance invariant.
