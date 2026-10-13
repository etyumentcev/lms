# L1/S4-A4-I1 — автономный PRS1 value layer

2026-10-10. Реализовано по прямому OWNER AUTHORIZATION S4-A4-I1, принятому [PRS1 §1–6](L1-S4-A3-SELECTOR-CONTRACT.md), [R1 matrix](L1-S4-A3-SELECTOR-ACCEPTANCE-MATRIX.md), [A4 design](L1-S4-A4-IMPLEMENTATION-DESIGN.md), [A4 matrix](L1-S4-A4-ACCEPTANCE-MATRIX.md) и решениям A4-O1–O9 в разрешённом I1 scope. Исторические статусы документационного пакета не переписаны. Новая нормативная семантика не введена.

**Parser success означает только syntactically valid selector; не verified PRS1 provenance, authorization или access grant.**

## Реализация и типы

| Новый файл | Содержание |
|---|---|
| `src/Lms.Application/Prs1Selector.cs` | Строгий parser и builder из типизированных значений; immutable selector |
| `src/Lms.Application/Prs1OperationPlan.cs` | Явная registry операций и допустимых новых масок |
| `tests/Lms.Application.Tests/Prs1SelectorTests.cs` | Pure grammar, Unicode, canonical round-trip, отрицательные reason branches и границы |
| `tests/Lms.Application.Tests/Prs1OperationPlanTests.cs` | Pure registry, 49 пар масок/планов, reserved/unknown rejection, отсутствие ScopeAction conversion |
| `codex/L1-S4-A4-I1-RESULTS.md` | Этот отчёт |

Публичные типы: sealed records `Prs1Selector`, `Prs1OperationPlan`; enums `Prs1ResourceKind`, `Prs1SelectorError`, `Prs1Operation`, `Prs1OperationError`. Отдельных internal типов нет. Records имеют private constructors и только get-only properties: внешнее создание некорректного значения через constructor/object initializer/with assignment закрыто. Private helper methods selector: UUID validation, strict sid decode/encode, error result; private strict UTF-8 codec. Нет новых interfaces, dependency registrations или package dependencies.

`Prs1Selector.TryParse` и `TryBuild` возвращают bool, nullable value и конкретный enum error; при отказе value всегда null. Builder получает Guid organization, typed kind и соответствующие string/Guid identities; лишние поля или отсутствующая entity отвергаются как InvalidStructure, отсутствующий/пустой sid — EmptyStringId. Builder может сериализовать typed Guid в lowercase D, parser не исправляет неканонический wire input. Успех не доказывает существование, ownership, same-org цепочку или доверенность входного ID.

`Prs1OperationPlan.TryForOperation` явно перечисляет Read=1, Submit=2, Review=4, SubmitWithReturnedContent=3 и ReviewWithSubmissionRead=5. `TryCreate` допускает только полный явный список declared masks 1–7, включая 6/7 как совокупности нормативных битов, без выдуманного имени новой команды. `ValidateNewRestrictionMask` проверяет только новое значение. 8–31 целиком ReservedDeny; zero/negative/unknown bits имеют отдельные причины. При unknown+reserved установлен приоритет UnknownBits, без stripping; отрицательные значения имеют NegativeMask. Неизвестный enum operation → UnsupportedOperation. Transfer/Lifecycle mapping отсутствует.

План содержит только объявленные required bits. Его создание не доказывает полноту effects реальной команды; generic command/projection planner и evaluator не реализованы. Нет overload/operator для ScopeAction: ordinals 0/1/2 не преобразуются в PRS1. Как любой C# enum, Prs1Operation допускает явный небезопасный cast со стороны будущего caller; такой внешний misuse не является предоставленным mapping и должен предотвращаться при отдельно разрешённой integration review.

## Grammar cases и порядок ошибок

- Только literal `prs1`, точные lowercase kind names и точное количество сегментов. `all` — отдельный явный target; null/empty, wildcard, prefix, Message/identity/session, URL, query/fragment и дополнительные сегменты не становятся selectors.
- UUID: exact lowercase D, nonzero, без требования version/variant. Guid.TryParseExact дополнительно проверяется обратным ToString("D") ordinal comparison; whitespace и permissive UUID forms не принимаются.
- sid: только ASCII base64url alphabet, без padding; невозможная длина mod 4 = 1 отвергается. Decode с технически восстановленным padding выполняется только после проверки alphabet/length; повторное canonical encoding должно совпасть байт-в-байт. Ненулевые trailing bits дают NonCanonicalBase64Url. Strict UTF-8 отвергает invalid continuation, overlong forms, truncated sequences, surrogate scalars и значения выше U+10FFFF.
- Порядок wire errors: empty → ref length → ASCII → profile → prefix structure → org UUID → kind → exact shape → IDs. sid errors: empty → alphabet/length → canonical trailing bits → decoded byte length → UTF-8. Несколько одновременно нарушенных условий возвращают первую конкретную причину.
- Нет trim/case-fold/NFC/NFD/percent decode/double decode. Encoded `%2F` остаётся строкой `%2F`, не `/`; encoded `YQ` остаётся `YQ`, не `a`. Case и normalization variants различны ordinally.
- Stored string ID по принятой grammar ограничен nonempty strict UTF-8 и byte length, без дополнительного запрета содержимого. Поэтому закодированные пробел, NUL, BOM, slash, `..` и `*` допустимы как буквальные IDs, но не приобретают wildcard/path semantics. Некавыченные wire whitespace/path/wildcard forms отвергаются. Файловое/сетевое разрешение отсутствует.
- Лимит sid проверяется по UTF-8 bytes, не UTF-16 length. ASCII и supplementary Unicode fixtures проверяют 1024/1025 отдельно для course и group slot. Unpaired UTF-16 surrogate в builder даёт InvalidUtf8 вместо replacement character.
- Максимальный валидный ref принятой grammar — group с двумя 1024-byte IDs: 2781 ASCII bytes. Поэтому валидного target ровно в 4096 bytes нет. F05 не меняет grammar: 4096-byte fixture проходит верхний ref limit и получает StringIdTooLong; 4097 получает ReferenceTooLong до decode. Отдельно успешно проверен максимальный валидный group. Требование «граница принята при валидном target» не интерпретируется как разрешение sid сверх 1024.

## Фактическая verification

Рабочий каталог `/home/tea/github/lms`. Существующая ветка `codex/l1-s4-jwt-api-authorization`, HEAD `492e30aef65bbddd24f107903bfaa54a563abe39` сохранены согласно прямому указанию пользователя. Новые ветки/worktrees не создавались.

Использован существующий SDK `/tmp/lms-s1-dotnet`, 10.0.401. Обычный `dotnet --version` попал в системный bootstrap wrapper без установленного SDK, который не смог установить SDK без sudo; установка не выполнялась. Sandbox launcher первоначально отказал с `mountinfo path is not absolute`; последующие команды выполнены через разрешённый escalation, без изменения sandbox configuration.

Команды запускались из canonical directory:

```sh
/tmp/lms-s1-dotnet/dotnet test tests/Lms.Application.Tests/Lms.Application.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~Prs1' --logger trx --results-directory /tmp/lms-s4-a4-i1/pure
/tmp/lms-s1-dotnet/dotnet test tests/Lms.Application.Tests/Lms.Application.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~Prs1' --logger trx --results-directory /tmp/lms-s4-a4-i1/pure-final
/tmp/lms-s1-dotnet/dotnet test tests/Lms.Application.Tests/Lms.Application.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/lms-s4-a4-i1/application
```

| Запуск | Passed | Failed | Skipped | Total |
|---|---:|---:|---:|---:|
| Initial I1 pure | 246 | 1 | 0 | 247 |
| Final I1 pure | 248 | 0 | 0 | 248 |
| Final весь Application project | 272 | 0 | 0 | 272 |

Первый failure: 4096-byte test fixture имел недопустимую base64url length mod 4 = 1 и получал InvalidBase64Url вместо ожидаемой StringIdTooLong. Исправлен fixture, runtime parser не менялся. Добавлен один Fact, перебирающий все ненулевые unused trailing bits для одно- и двухбайтового tail. Final commands exit 0, без build warnings/errors. Финальные 272 = 248 новых + 24 существующих Application tests; повторные запуски не суммируются как уникальное покрытие. Есть детерминированный цикл из 256 Unicode round-trip vectors внутри одного Fact, а не 256 отдельных test cases.

TRX каждого запуска сохранён в соответствующем каталоге `/tmp/lms-s4-a4-i1`. Это локальные evidence artifacts; их жизненный цикл ограничен `/tmp`.

F01–F06 покрыты в pure scope. B01/B02/B03/B05/B06 и RB03 покрыты для новых values/required plans. B04: проверены все 49 registry pairs и set-intersection oracle в тесте; **не** доказаны provenance/context/resource/subject matching или persisted evaluator. B07: неизвестный operation отвергается, compound 3/5 включает Read; выявление пропущенного actual effect будущим command/projection planner **NOT IMPLEMENTED / NOT RUN**. Persisted fallback части F/B/RB03, DB-backed/domain/concurrency scenarios не объявляются пройденными. Старые A2 429 tests не считаются новым доказательством I1.

## Diff и сохранность

Baseline снят до записи файлов: `/tmp/lms-s4-a4-i1/baseline.json`; machine-readable verification: `/tmp/lms-s4-a4-i1/validation.json`. Проверены SHA-256 всех 378 исходных tracked/untracked nonignored файлов, branch/HEAD/index. 377 baseline файлов совпадают. Единственное внешнее изменение во время работы — `.obsidian/workspace.json`; ни одна команда I1 его не записывала, актуальное содержимое оставлено без восстановления. Остальные `.obsidian` файлы сохранены. Это внешнее изменение честно исключено из утверждения о byte-exact сохранности.

Task diff — ровно пять новых untracked файлов из таблицы выше, без удалений и без редактирования существующих файлов. Code/tests: 176 + 59 + 234 + 93 = 562 добавленные строки; отчёт: 80 строк; всего task diff: 5 файлов, 642 insertions / 0 deletions. Эти новые файлы ещё не staged, поэтому обычный `git diff --stat HEAD` их не показывает.

Существующий tracked diff относительно исходного HEAD остаётся: 5 файлов, 163 insertions / 18 deletions — `codex/L03-OWNER-DECISION-REGISTER.md`, `codex/L1-S4-A1-RESULTS.md`, `codex/L1-S4-A2-RESULTS.md`, `codex/L1-S4-RESULTS.md`, `tests/Lms.Infrastructure.Tests/AttachmentDeadlineBoundaryTests.cs`. Это чужие исходные изменения; I1 их не модифицирует. Существующие untracked документы A3/A4/G02 также сохранены. `git diff --check` и отдельная whitespace-проверка новых файлов выполнены.

Статический поиск `Prs1` по `src`/`tests` вне четырёх новых файлов не обнаружил ссылок. Hash verification подтверждает, что ComponentAuthorization, PostgresCurrentProcessingGate, production DI, project files, API и migrations не изменены. Новый Application код использует только System API; нет DbContext, PostgreSQL, I/O, provenance reader, ownership resolver, affectedSubjects, admission path или persisted restriction classifier/evaluator.

## Scope exclusions и рекомендация

Persistence/schema/SQL/migrations, real provenance, administrative writer, transactions/locks/receipts, resolver/evaluator, projection/source completeness, runtime exact gate, DI, API/HTTP, storage/files/jobs, rollout и deployment не реализованы и не тестировались. Full solution, Domain/Infrastructure/API test projects в I1 не запускались; выполнен весь затронутый Application project. Внешний human audit, SR-01–SR-04, G02 и последующие A4 proof obligations не закрыты. Синтаксически валидный attachment selector не снимает текущий Attachment deny.

Рекомендация: принять I1 как автономный value layer с указанной границей evidence. Не принимать его за включение PRS1, доказательство provenance/effect completeness или разрешение persistence. Следующий slice требует отдельной owner authorization. Commit, merge, rebase, push и deploy не выполнялись.

**S4-A4-I1 READY FOR OWNER ACCEPTANCE · PRS1 RUNTIME EXACT GATE OFF · PERSISTENCE NOT AUTHORIZED · T04/E03 UNCHANGED · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**

<!-- End of I1 evidence report. -->
