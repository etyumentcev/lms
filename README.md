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
