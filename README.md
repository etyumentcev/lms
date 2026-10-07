# LMS — greenfield foundation

ASP.NET + PostgreSQL + DDD target architecture. Checkpoints B through C4 implement and verify Domain, Application, PostgreSQL persistence, immutable content resolution and authenticated command/read HTTP API for Submit → Review → Revise → Accept.

## Local startup with Docker Compose

Requirements: **Docker Engine + Docker Compose** only. Run from the repository root:

```sh
docker compose up --build
```

Wait for `postgres` and `api` to become healthy and `web` to start. On first use,
Docker downloads build/runtime images and dependencies; no host .NET SDK, Node/npm or
PostgreSQL installation is needed. Open **http://localhost:8080/local-login.html**.
The application is served at **http://localhost:8080**; readiness is
**http://localhost:8080/api/health**. The web port is bound to loopback only.

### Development authentication

In another terminal, from the same repository, obtain an eight-hour Student bearer token:

```sh
docker compose exec api dotnet Lms.Api.dll --dev-token
```

Paste the printed token into the local login page and choose “Открыть учебный курс”.
This opens `/courses/local-demo`. Open “Делимость и остатки”, then any problem to
reach the existing Problem placeholder. Nested routes support browser refresh.
The application does not sign in anonymous visitors automatically: without a valid token,
workspace requests return 401. API signature, issuer, audience, lifetime and resource
permissions remain enforced. Tokens are kept in sessionStorage by the **Compose-only**
host adapter, for the current tab; close the tab/clear sessionStorage to sign out.
After eight hours obtain a new token and revisit the local login page.

For the seeded Teacher, use:

```sh
docker compose exec api dotnet Lms.Api.dll --dev-token Teacher
```

Teacher sees the granted course and real assignment rows; Lesson has no role/profile field,
so its shell uses neutral navigation and does not invent a student enrollment or personal progress.
The Problem page remains a placeholder in checkpoint F.

Compose sets both `ASPNETCORE_ENVIRONMENT=Development` and `LocalDemo__Enabled=true`.
The local token command, signing configuration and seed are disabled outside Development.
The checked-in database credentials and signing key are **public local-only defaults**,
not production secrets. Do not deploy this Compose configuration or local-development
web image. The generic web Docker target `production` contains only built static files;
the explicit `local-development` target adds the login page and host bearer adapter.
Production authentication still requires the existing trusted OIDC authority/audience setup.

### Services, migrations and demo data

Startup order: PostgreSQL 17.6 healthcheck → one-shot `migrate` → API healthcheck → nginx web.
`migrate` runs the checked-in EF migrations with `MigrateAsync` and migration history,
including `20261007035847_InitialPersistence`; it never uses EnsureCreated or handwritten schema.
Re-running is safe. API and migration containers use the ASP.NET runtime image, not an SDK.
The web container proxies `/api/*` to `api:8080` before its React SPA fallback: one browser origin,
no feature-specific API URLs or CORS configuration. API errors keep their HTTP status/body.

After migration, the Development-only seed atomically provisions the immutable `local-demo`
course, one available lesson, a Student, a Teacher grant and four active assignments via the
existing model/access facts. It creates no ProblemWork or submissions. Its deterministic IDs
and publication marker make subsequent startup a no-op; existing learner data is preserved.
The other three workflow statuses are covered by feature/contract tests; this seed intentionally
remains a NotStarted vertical slice. There is no user/profile management subsystem.

PostgreSQL data lives in the named Docker volume `lms-local_postgres-data` (managed by Docker).
PostgreSQL and API have no host ports; only nginx exposes loopback port 8080. To change that
port, edit the web port mapping in `docker-compose.yml`.

```sh
# Inspect services and logs
docker compose ps -a
docker compose logs -f api
docker compose logs -f web
docker compose logs -f postgres
docker compose logs migrate

# Stop; preserve the database
docker compose down
# Start again; existing migrations and data are retained
docker compose up --build

# Full local database reset (deletes ALL data in the development volume)
docker compose down -v
# Reapply migrations and recreate the deterministic demo
docker compose up --build
```

## Structure

- `Lms.Domain`: ProblemAssignment, ProblemWork, immutable Submission/Review/Message, domain events. No framework dependencies.
- `Lms.Application`: four use cases; access and atomic persistence ports. Depends only on Domain.
- `Lms.Infrastructure`: EF Core/Npgsql mappings, migrations, atomic `IWorkStore`, PostgreSQL access facts/policy and transactional event journal.
- `Lms.Api`: ASP.NET Core command host, JWT Bearer authentication, principal-to-Actor adapter and uniform Problem Details.
- `Lms.Web`: React/TypeScript shared UI shell (D) and CourseWorkspace screen (E), responsive context panel, reusable primitives; other feature routes remain placeholders. See [frontend commands](src/Lms.Web/README.md).
- `tests`: xUnit Domain/Application tests, real PostgreSQL persistence tests and WebApplicationFactory API tests with PostgreSQL.

## Build and test

Install the SDK specified in `global.json` (10.0.401).

```sh
dotnet restore Lms.sln
dotnet build Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test tests/Lms.Domain.Tests/Lms.Domain.Tests.csproj --no-build --no-restore -m:1
dotnet test tests/Lms.Application.Tests/Lms.Application.Tests.csproj --no-build --no-restore -m:1
dotnet test tests/Lms.Infrastructure.Tests/Lms.Infrastructure.Tests.csproj --no-build --no-restore -m:1
dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj --no-build --no-restore -m:1
```

In this workspace the temporary SDK is `/tmp/lms-dotnet/dotnet`. Replace `dotnet` above with that path if needed. Unit tests do not need PostgreSQL or a running API.

## PostgreSQL persistence (C1)

Infrastructure tests require a running Docker daemon. Testcontainers starts `postgres:17.6-alpine`,
creates a fresh database per test, applies the checked-in migration (never `EnsureCreated`), and
removes the container after the run. Image download requires registry access on the first run.

To apply migrations to a development database, install `dotnet-ef` 10.0.12 and set `LMS_POSTGRES`
to its Npgsql connection string, then run:

```sh
dotnet ef database update --project src/Lms.Infrastructure
```

Use one `LmsDbContext` and `PostgresWorkStore` per unit of work; do not share them concurrently.
Assignments can be seeded using the context; no assignment management use case is introduced.
`Save` owns its transaction and clears its tracker on completion. Load returns isolated,
untracked aggregates. Failed commands require a fresh load; the adapter never retries conflicts.
`DomainEvents` is an immutable transactional journal, with stable IDs from the corresponding
Submission/Review/Message, type name and JSON payload. It has no dispatcher or external publication.
Attachments are ordered snapshots of IDs (`uuid[]`); ownership/eligibility metadata is checked by C2; upload/storage and its lifecycle are deferred.

See [C1 report](codex/C1-RESULTS.md) for schema, constraints and verification results.

## Command API (C2)

Apply migrations first (the host does not migrate automatically), then configure:

- `LMS_POSTGRES` or `ConnectionStrings__Lms`: Npgsql connection string.
- `Authentication__Authority`: trusted HTTPS OIDC authority (issuer/discovery and signing keys).
- `Authentication__Audience`: this API's audience.

```sh
dotnet run --project src/Lms.Api --no-launch-profile -- --urls http://127.0.0.1:5187
```

The Bearer handler checks signature, issuer, audience and lifetime. Without identity-provider
configuration, authentication fails closed. Production HTTPS termination belongs to deployment.
The trusted principal must contain exactly one UUID `sub` (internal Actor ID) and one `role`
(`Student` or `Teacher`). For an IdP with different subject/role semantics, replace `IActorMapper`
with a trusted identity-mapping adapter; do not accept Actor IDs from HTTP input. Authentication
scheme registration is replaceable independently of Application/Domain. Test headers only work
in the test assembly's authentication handler; no production test-mode switch exists.

Routes (all POST, authenticated):

- `/api/v1/problem-assignments/{assignmentId}/submissions` → 201.
- `/api/v1/problem-works/{problemWorkId}/messages` → 200.
- `/api/v1/problem-works/{problemWorkId}/reviews/needs-revision` → 200.
- `/api/v1/problem-works/{problemWorkId}/reviews/accept` → 200.

Submission JSON: `bodyMarkdown?`, required `attachmentIds` array, `expectedVersion` nullable.
First submit uses null/omitted version; existing work requires its persisted version.
Review JSON: required `submissionId`, required `expectedVersion`, `feedbackMarkdown` (nonblank
for needs-revision; optional for accept). Message JSON: required `bodyMarkdown` and `attachmentIds`,
optional `replyToMessageId`. Unknown JSON properties are rejected. No Actor field is accepted.
All failures use `application/problem+json` with stable `code`, `status`, `type`, `title`,
`instance` and `traceId`; stale/missing existing-work version yields 409 `ConcurrencyConflict`.
Commands are never retried automatically.

Access facts are provisioned by trusted infrastructure into `AccessEnrollments`, `AccessLessons`,
`AccessTeachers` and `AccessAttachments`; no public management endpoints or production seed IDs
are provided. Students require ownership, active assignment/enrollment, a published lesson for the
exact content version and an elapsed optional `AvailableFrom`. Teacher grants authorize either the
whole course (`GroupId = null`) or only a matching active enrollment group. Missing facts deny access.
Attachments require current ownership, eligibility and unexpired metadata.

**Upload decision B:** `/uploads` and object storage are deferred. Text-only commands work now.
The future storage producer must validate/store a file before publishing eligible metadata, and
implement atomic claiming/link lifecycle and cleanup. Synthetic attachment metadata exists only
in isolated integration test databases. No fake production attachments are available.

See [C2 report](codex/C2-RESULTS.md) for DTOs, error mapping, test scenarios and remaining decisions.

## Boundaries

Domain RequestRevision requires nonblank feedback as a global invariant (G03, C1.5). Application retains early validation with `FeedbackRequired`; Domain independently rejects null/empty/whitespace without mutation. Accept allows absent feedback. New work requires absent expectedVersion; existing work requires its current version.

Persistence adapters must provide isolated aggregate instances, atomic compare-and-save, uniqueness per assignment, transactional attachment linking and event persistence. Unit test doubles do not prove database concurrency. Production access checks and command HTTP contracts are implemented in C2. Read queries and immutable content resolution are implemented in C3. Shared UI shell and Course screen are implemented in D/E; upload/storage and subsequent feature screens remain deferred.

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

## Read API and immutable content (C3)

Five authenticated GET endpoints now expose course/lesson/student workspaces, work details and teacher
review queue. See [C3 report](codex/C3-RESULTS.md) for routes, DTOs, authorization, progress and gaps.
Apply the new `ImmutableContentSnapshots` migration using the same EF procedure above.

Trusted provisioning inserts complete `PublishedContentVersions` artifacts: exact version UUID,
CourseId, positive unique VersionNumber, SourceRevision, UTC PublishedAt and camelCase ContentJson
serialized from `PublishedCourseContent` (Infrastructure). Nested lessons/problems contain student-safe
metadata and statementMarkdown only; never include teacher solutions. Database triggers reject mutation
and duplicate references. Publish changes as a new version UUID. Existing assignments always resolve
their original version. Missing exact artifacts return 503 ContentUnavailable, never latest content.
There is no public content-authoring/publication endpoint or production seed in C3.

Queries do not create work. Progress and review queues are computed, not persisted. Student locked
content returns 403; course catalog retains safe metadata. Teacher enrollment is null. Browser-local
drafts have no authoritative server hasDraft. Profiles, hints/materials and attachment download metadata
remain explicit gaps; no synthetic content or URLs are returned. C4 backend stabilization and D shared UI shell are complete. Course screen is implemented in E; upload/storage and subsequent feature screens remain deferred.

## Shared UI shell (D)

Frontend setup, build/test commands and optional responsive browser smoke are in
[src/Lms.Web/README.md](src/Lms.Web/README.md). The shell demo makes no API requests;
Course now reads the real CourseWorkspace API; other views remain placeholders. [D results](codex/D-RESULTS.md) record the
49 frontend tests, 187 backend regressions and desktop/tablet/mobile verification.


## Course screen (E)

`/courses/:courseId` supports Student and Teacher through the C4 CourseWorkspace contract.
Server-derived progress, available/locked lessons and real optional context compose D's
shared UI. [E results](codex/E-RESULTS.md) describe 83 frontend / 187 backend passing tests,
responsive/browser/live-API checks, and contract/deployment gaps. No OIDC provider settings
exist yet: connect the trusted host bearer adapter and same-origin API proxy as documented
in [frontend setup](src/Lms.Web/README.md). F and later checkpoints remain deferred.
