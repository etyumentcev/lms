# C1 — PostgreSQL persistence: результат

2026-10-07. **C1 завершён.** База запуска — `2124a15d10746a786278b9dd52b2d8edabcc36df`, accepted B сохранён. C2/C3/C4 не выполнялись.

## 1. Изменённые файлы

- `src/Lms.Infrastructure/Lms.Infrastructure.csproj` — EF/Npgsql dependencies.
- `src/Lms.Infrastructure/LmsDbContext.cs` — mappings шести таблиц, owned content reference, FK/check/unique constraints, attachment conversion.
- `src/Lms.Infrastructure/PostgresWorkStore.cs` — реализация всех методов существующего `IWorkStore`.
- `src/Lms.Infrastructure/LmsDbContextFactory.cs` — design-time factory; connection string через `LMS_POSTGRES`.
- `src/Lms.Infrastructure/Migrations/20261007035847_InitialPersistence.cs`, соответствующий `.Designer.cs`, `LmsDbContextModelSnapshot.cs` — initial schema, snapshot, immutable-history/reference triggers.
- `src/Lms.Domain/{History,ProblemAssignment,ProblemWork}.cs` — только пять private materialization constructors, без framework dependencies или изменения публичного контракта.
- `tests/Lms.Infrastructure.Tests/Lms.Infrastructure.Tests.csproj`, `PostgresFixture.cs`, `PersistenceTests.cs` — Testcontainers и 10 PostgreSQL tests.
- `README.md`, `codex/EXECPLAN-LMS-SIX-SCREENS.md`, этот отчёт — воспроизведение, решения, результаты.

Application, API, Web и существующие unit tests не изменены.

## 2. NuGet

Новые прямые зависимости Infrastructure:

- `Microsoft.EntityFrameworkCore` 10.0.12;
- `Microsoft.EntityFrameworkCore.Relational` 10.0.12;
- `Microsoft.EntityFrameworkCore.Design` 10.0.12 (`PrivateAssets=all`);
- `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3.

Infrastructure tests: `Testcontainers.PostgreSql` 4.15.0; существующие xUnit/Test SDK сохранены. Временный CLI `dotnet-ef` 10.0.12 установлен в `/tmp/lms-ef`. Runtime и Design выровнены явно, чтобы исключить transitive assembly conflicts. Ранние версии заменены после NuGet audit failures; suppressions не добавлялись.

## 3–4. Schema и invariants

Все шесть прикладных таблиц имеют UUID `Id` PK; отдельно EF создаёт `__EFMigrationsHistory`.

| Таблица | Данные и важные constraints/indexes | Защита |
|---|---|---|
| `ProblemAssignments` | StudentId, CourseId, owned CourseVersionId/LessonId/ProblemId, Status; alternate key `(Id, StudentId)`; status CHECK; reference UPDATE trigger | Закреплённые identity/ownership/content reference не меняются; Withdrawn сохраняется |
| `ProblemWorks` | assignment/student, Status, `Version bigint` concurrency token, AcceptedAt; UNIQUE assignment; composite FK `(assignment, student)`; status/version CHECK | Один work на assignment, правильный владелец, четыре статуса, nonnegative version |
| `Submissions` | work, AttemptNumber, body, UTC time/author, `AttachmentIds uuid[]`; UNIQUE `(work, attempt)`; alternate `(work, Id)`; positive attempt CHECK; work FK | Уникальные положительные попытки, snapshots вложений, принадлежность work |
| `Messages` | work, author/role, body, reply, UTC time, `AttachmentIds uuid[]`; alternate `(work, Id)`; composite reply FK; role CHECK | Reply только на сообщение того же work |
| `Reviews` | work, submission, teacher, decision, optional message, UTC time; UNIQUE submission; composite submission/message FKs; decision CHECK | Одна финальная review, конкретная submission и feedback того же work |
| `DomainEvents` | work FK, stable Id, saved aggregate Version, Ordinal, Type, JSONB Payload, UTC OccurredAt; UNIQUE `(work, version, ordinal)` | Устойчивые идентификаторы и порядок новых событий внутри одного save |

FK indexes создаются migration; unique indexes также поддерживают aggregate loading. FK используют RESTRICT, без cascade удаления истории. PostgreSQL triggers отвергают UPDATE/DELETE Submission, Review, Message и DomainEvent, включая изменение attachment array. Trigger assignment запрещает перепривязку контента/владельца, сохраняя возможность Withdraw.

State transitions, latest submission, Accepted terminal, author/access rules остаются в Domain/Application. Infrastructure не реализует вторую state machine. Обычный разрешённый PostMessage после Accepted увеличивает Version, сохраняя статус. Derived AttemptCount/LastSubmissionAt/LastReviewedAt не дублируются колонками; reviews загружаются в порядке попыток, а не времени, переданного caller.

## 5–6. Adapter, transactions, concurrency, events

`PostgresWorkStore` реализует FindAssignment, FindByAssignment, FindWork и Save. Один scoped DbContext/store на unit of work, без concurrent reuse. Загрузка — no-tracking single SQL snapshot с history; экземпляры изолированы, events при reload пусты.

Save сам открывает PostgreSQL transaction. Для существующего work выполняется conditional `UPDATE … WHERE Id = … AND Version = expected`, удерживающий row lock до commit. При нуле строк — `UseCaseException("ConcurrencyConflict")`. Для first submit — INSERT, unique assignment разрешает успех только одному участнику гонки. Unique violations/EF concurrency errors переводятся в контролируемый conflict; автоматического retry нет.

В той же transaction записываются только новые history rows и события. Существующие history rows не обновляются. Commit происходит после SaveChanges; исключение/cancellation приводит к rollback при disposal, tracker очищается. Caller после конфликта должен загрузить aggregate заново.

Журнал событий — минимальное transactional persistence, без dispatcher/broker/publisher. Event Id стабилен: SubmissionId, ReviewId или MessageId соответствующего события. Save исключает уже записанные IDs даже при повторном сохранении того же aggregate через новый store; rollback не подтверждает события. `Type` — имя принятого DomainEvent, payload — JSON текущего record; Version — версия aggregate на save, Ordinal — порядок событий этого save. External delivery/consumer checkpoints и эволюция payload contract — последующая задача при появлении consumers, не C1.

## 7. Migration

`20261007035847_InitialPersistence`. Каждый integration test создаёт пустую БД, применяет `Database.MigrateAsync()` и проверяет отсутствие pending model changes. `EnsureCreated` не используется. Down удаляет таблицы и trigger functions; destructive Down не запускался на пользовательской БД.

## 8. Integration scenarios

10 passed, без skipped:

1. Initial migration, absence before submit, assignment/content roundtrip, first work/submission/attachments/time/version/events, isolated reload.
2. First submit race: два DbContext/connection и две реальные transactions, barrier после BeginTransaction; один успех, один conflict, одна attempt и один event.
3. Application RequestRevision → Attempt #2 → Accept; сохранение body/IDs/attachment/time первой попытки; stale submission rejected; Accepted terminal для submit/reviews; message/reply после Accepted разрешён.
4. Concurrent reviews: две реальные transactions, один победитель; затем resubmit и stale review — без изменения новой попытки/добавления loser history/events.
5–6. Injected failure после SQL SaveChanges, до commit: first submit и revision; rollback work/version, submissions, reviews, messages, events; повторный save сохраняет pending events.
7. Domain revision без feedback сохранена согласно G03; repeated save, fresh store и reload не дублируют events; JSON payload содержит submission ID.
8. Реальные SQL нарушения unique attempt/cross-work submission FK/UPDATE и DELETE immutable submission отвергаются.
9. Immutable assignment reference, invalid enum, withdrawal roundtrip.
10. Cross-work reply и review feedback отвергаются composite FKs.

## 9–10. Команды и результаты

SDK `/tmp/lms-dotnet/dotnet --version`: **10.0.401**, совпадает с `global.json`. Docker daemon доступен. Testcontainers запускает реальный `postgres:17.6-alpine`, создаёт отдельную database на тест и удаляет контейнер после suite.

```sh
/tmp/lms-dotnet/dotnet restore Lms.sln
/tmp/lms-dotnet/dotnet build Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false
/tmp/lms-dotnet/dotnet test tests/Lms.Domain.Tests/Lms.Domain.Tests.csproj --no-build --no-restore -m:1
/tmp/lms-dotnet/dotnet test tests/Lms.Application.Tests/Lms.Application.Tests.csproj --no-build --no-restore -m:1
/tmp/lms-dotnet/dotnet test tests/Lms.Infrastructure.Tests/Lms.Infrastructure.Tests.csproj --no-build --no-restore -m:1
git diff --check
```

- Restore успешен.
- Финальный build: **0 warnings, 0 errors**.
- Domain: **17 passed**, 0 failed/skipped.
- Application: **12 passed**, 0 failed/skipped.
- Infrastructure: **10 passed**, 0 failed/skipped, PostgreSQL suite ~5 секунд после загрузки образа.
- `git diff --check`: успешно.

Sandbox этой машины не запускался (`mountinfo path is not absolute`); команды чтения/записи/build/tests выполнялись через одобренную escalation. Docker image первоначально загружался долго, затем тесты прошли. Это ограничение окружения, не замена PostgreSQL имитацией.

## 11. Deviations/open decisions

- EF constructor binding несовместим с принятыми constructors, использующими navigation/несопоставимые параметры. Причина записана в ExecPlan **до** изменения Domain. Добавлены только private constructors; invariants/public API не изменены.
- Attachment metadata/object storage отсутствуют в принятом порте. C1 хранит точный ordered snapshot IDs в uuid[], без фиктивных metadata tables. Upload/access adapter остаётся будущей задачей.
- Transactional journal удовлетворяет разрешённому результату C1. Полная messaging infrastructure не вводилась.
- **G03 открыт**: Domain optional feedback, Application required feedback сохранены.
- C2/C3/C4, endpoints, authentication, workspace/read projections и frontend не начаты.
