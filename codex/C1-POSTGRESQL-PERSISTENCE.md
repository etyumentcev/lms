ExecPlan принимаю.

Выполни только checkpoint C1 — PostgreSQL persistence.

Не переходи к C2/C3/C4, API, auth, read models или frontend.

## Scope C1

Реализуй:

- EF Core;
- PostgreSQL provider;
- DbContext;
- entity mappings;
- initial migration;
- persistence adapters для существующих application ports;
- optimistic concurrency;
- atomic transaction boundary;
- transactional persistence domain events;
- integration tests against real PostgreSQL.

## Обязательные persistence invariants

Проверь и обеспечь на уровне модели/БД/transaction semantics:

- один ProblemWork на один ProblemAssignment;
- уникальный AttemptNumber внутри ProblemWork;
- previous Submission immutable;
- Review относится к конкретному Submission;
- stale review не может изменить уже сменившуюся текущую попытку;
- ExpectedVersion проверяется атомарно;
- concurrent first submit не может создать два ProblemWork;
- state transition + Submission/Review/Message + domain events сохраняются атомарно;
- Accepted остаётся terminal;
- attachment IDs сохраняются как snapshot текущей Submission.

## Архитектурные ограничения

Не переносить business rules в Infrastructure.

Domain/Application менять только если обнаружена реальная несовместимость утверждённого persistence contract; в таком случае сначала зафиксируй проблему в ExecPlan и сделай минимальное изменение.

Не создавать:
- HTTP endpoints;
- controllers;
- authentication middleware;
- read/workspace queries;
- frontend;
- CourseProgress/LessonProgress tables;
- server Draft aggregate;
- queue projections ради UI.

Read-model удобство не является основанием для изменения schema.

## PostgreSQL integration testing

Тесты должны использовать настоящий PostgreSQL, а не EF InMemory provider.

Обязательно проверить:

1. first submit persists ProblemWork + Submission #1;
2. concurrent first submit:
   - только один ProblemWork;
   - проигравшая транзакция получает контролируемый conflict;
3. successful revision workflow persists Attempt #2;
4. AttemptNumber unique per work;
5. stale ExpectedVersion отвергается;
6. RequestRevision + Review + optional/required Message сохраняются атомарно согласно текущему контракту;
7. Accept persists terminal state;
8. subsequent mutation after Accepted rejected;
9. previous Submission rows/content не изменяются;
10. rollback не оставляет частично сохранённую history;
11. domain events фиксируются в одной transaction boundary;
12. existing 17 domain + 12 application tests остаются зелёными.

## Migration

Создай initial migration как нормальную часть greenfield-проекта.

После создания migration:
- покажи основные таблицы;
- PK/FK;
- unique constraints;
- indexes;
- concurrency/version column;
- объясни, какой invariant обеспечивается каждым важным constraint.

Не добавляй таблицы, которые не нужны текущему vertical slice.

## Domain events

Не публикуй external/domain events до успешного commit.

Если для C1 нужен outbox, сначала проверь утверждённые документы и текущие ports.
Не вводи полноценную messaging infrastructure без необходимости.

Допустимый результат C1:
- events сохранены transactionally или накоплены таким образом, что публикация возможна только после commit.

## Exit criteria

C1 завершён только если:

- PostgreSQL поднимается в integration test environment;
- initial migration применяется к пустой БД;
- persistence adapters реализуют текущие application ports;
- race first submit протестирован реальными concurrent PostgreSQL transactions;
- stale version протестирован;
- atomic rollback протестирован;
- все старые unit tests зелёные;
- новые infrastructure/integration tests зелёные;
- `dotnet build Lms.sln` проходит без warnings/errors.

## Финальный отчёт

После завершения дай:

1. изменённые файлы;
2. NuGet packages;
3. schema/table overview;
4. constraints и какие invariants они защищают;
5. persistence adapters;
6. transaction/concurrency strategy;
7. migration name;
8. integration test scenarios;
9. build/test commands;
10. результаты;
11. обнаруженные deviations/open decisions.

После отчёта остановись.

Не начинай C2.
