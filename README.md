# LMS — greenfield foundation

ASP.NET + PostgreSQL + DDD target architecture. This checkpoint implements only the Domain and Application layers of Submit → Review → Revise → Accept.

## Structure

- `Lms.Domain`: ProblemAssignment, ProblemWork, immutable Submission/Review/Message, domain events. No framework dependencies.
- `Lms.Application`: four use cases; access and atomic persistence ports. Depends only on Domain.
- `Lms.Infrastructure`: reserved adapter project; no PostgreSQL implementation yet.
- `Lms.Api`: reserved Web SDK project with an empty entry point; no server/endpoints yet.
- `Lms.Web`: reserved frontend directory, not an implemented frontend project.
- `tests`: xUnit Domain/Application tests and reserved Infrastructure/API test projects.

## Build and test

Install the SDK specified in `global.json` (10.0.401).

```sh
dotnet restore Lms.sln
dotnet build Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test tests/Lms.Domain.Tests/Lms.Domain.Tests.csproj --no-build --no-restore -m:1
dotnet test tests/Lms.Application.Tests/Lms.Application.Tests.csproj --no-build --no-restore -m:1
```

In this workspace the temporary SDK is `/tmp/lms-dotnet/dotnet`. Replace `dotnet` above with that path if needed. Unit tests do not need PostgreSQL or a running API.

## Boundaries

Domain RequestRevision allows optional feedback per Domain Spec §21. The Application use case requires nonblank feedback for this slice. Accept allows absent feedback. New work requires absent expectedVersion; existing work requires its current version.

Persistence adapters must provide isolated aggregate instances, atomic compare-and-save, uniqueness per assignment, transactional attachment linking and event persistence. Unit test doubles do not prove database concurrency. Production authorization and storage adapters, HTTP contracts, migrations and UI are deferred.

See [ExecPlan](codex/EXECPLAN-LMS-SIX-SCREENS.md) for decisions, remaining contracts and checkpoint results.

---

# LMS олимпиадного кружка — документация

Этот архив содержит текущую архитектурную и функциональную документацию MVP LMS.

## Основной принцип

**Markdown определяет, что показать. LMS Domain определяет, что происходит.**

Учебные материалы курса хранятся в Markdown и статических ресурсах.
LMS отвечает за публикацию, доступ, сдачу решений, диалог, проверку и прогресс.

## Структура

```text
lms-documentation/
├── README.md
├── docs/
│   ├── content/
│   │   └── COURSE-CONTENT-SPEC.md
│   ├── architecture/
│   │   └── CONTENT-ARCHITECTURE.md
│   ├── domain/
│   │   ├── LMS-DOMAIN-BOUNDARY.md
│   │   ├── LMS-MVP-ACTORS-USE-CASES.md
│   │   └── LMS-DOMAIN-SPEC.md
│   └── features/
│       └── FEATURE-SUBMISSION-REVIEW.md
├── examples/
│   └── course-v1/
│       ├── course.md
│       ├── assets/
│       └── lessons/
│           └── 01-divisibility/
│               ├── lesson.md
│               ├── assets/
│               └── problems/
│                   ├── 01-divisibility.md
│                   └── 02-number-of-divisors.md
└── codex/
    └── CONTENT-CONTRACT.md
```

## Рекомендуемый порядок чтения

1. `docs/content/COURSE-CONTENT-SPEC.md`
   — нормативный контракт Markdown-курса.

2. `docs/architecture/CONTENT-ARCHITECTURE.md`
   — архитектура публикации и отображения контента.

3. `docs/domain/LMS-DOMAIN-BOUNDARY.md`
   — граница между Course Content и состоянием LMS.

4. `docs/domain/LMS-MVP-ACTORS-USE-CASES.md`
   — роли, MVP и пользовательские сценарии.

5. `docs/domain/LMS-DOMAIN-SPEC.md`
   — агрегаты, Entity/VO, Domain Events, state transitions и invariants.

6. `docs/features/FEATURE-SUBMISSION-REVIEW.md`
   — первый вертикальный slice `Submit → Review → Revise → Accept`,
   подготовленный для реализации через `implement-feature`.

7. `examples/course-v1/`
   — минимальный пример структуры Markdown-курса.

8. `codex/CONTENT-CONTRACT.md`
   — краткие обязательные правила для Codex.


## UI и реализация шести экранов

Дополнительные нормативные документы:

```text
docs/ui/LMS-UI-FLOW-SPEC.md
docs/ui/LMS-FRONTEND-CONTRACT.md
docs/ui/mockups/lms-six-screens-right-panel.png
codex/CODEX-TASK-LMS-SIX-SCREENS.md
```

Для Codex сначала использовать `codex/CODEX-TASK-LMS-SIX-SCREENS.md`.
