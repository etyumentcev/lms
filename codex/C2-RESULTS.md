# C2 — Command API + authentication/access boundary

2026-10-07. **C2 завершён.** Ветка `codex/c2`, база `43957ff` (принятые C1/C1.5).
Ветка освобождена из чистого worktree 7cff, переключена в 88c7 и fast-forward до C1.5.
Название сохранено: соответствует checkpoint. C3/C4, read models/workspaces и frontend не начаты.

## 1. Files changed

- `src/Lms.Api/Program.cs` — реальный host, DI, JWT Bearer, JSON, routing, error pipeline.
- `src/Lms.Api/ActorMapper.cs` — заменяемый `IActorMapper`, строгий principal → Actor.
- `src/Lms.Api/CommandsController.cs` — четыре thin endpoints, request DTO, command responses.
- `src/Lms.Api/ApiErrors.cs` — единый Problem Details, exception/status mapping.
- `src/Lms.Api/Lms.Api.csproj` — `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.12.
- `src/Lms.Infrastructure/AccessFacts.cs` — trusted authorization/attachment metadata.
- `src/Lms.Infrastructure/PostgresAccessPolicy.cs` — existing `IAccessPolicy` implementation.
- `src/Lms.Infrastructure/LmsDbContext.cs` — только четыре новых DbSet/mappings.
- `src/Lms.Infrastructure/Migrations/20261007090442_CommandAccessFacts.cs`, `.Designer.cs`, `LmsDbContextModelSnapshot.cs` — аддитивная migration.
- `tests/Lms.Api.Tests/ApiFixture.cs` — WebApplicationFactory, PostgreSQL/Testcontainers, test-only auth, transaction barrier.
- `tests/Lms.Api.Tests/CommandApiTests.cs` — 42 API scenarios (с учётом theory cases).
- `tests/Lms.Api.Tests/BearerAuthenticationTests.cs` — 8 scenarios настоящего Bearer handler.
- `tests/Lms.Api.Tests/Lms.Api.Tests.csproj` — `Microsoft.AspNetCore.Mvc.Testing` 10.0.12, `Testcontainers.PostgreSql` 4.15.0.
- `README.md`, `codex/EXECPLAN-LMS-SIX-SCREENS.md`, этот отчёт — запуск, решения и результаты.

Domain, Application, PostgresWorkStore и migration C1 не изменены, что проверено diff относительно C1.5.

## 2. Routes

Все маршруты требуют authenticated principal, base path `/api/v1`:

| Method/path | Success |
|---|---|
| POST `/problem-assignments/{assignmentId}/submissions` | 201 Created |
| POST `/problem-works/{problemWorkId}/messages` | 200 OK |
| POST `/problem-works/{problemWorkId}/reviews/needs-revision` | 200 OK |
| POST `/problem-works/{problemWorkId}/reviews/accept` | 200 OK |

GET/read endpoints, start/attempts/create/set-in-progress и uploads не добавлены.

## 3. Request/response DTO

IDs — UUID strings; version — JSON integer / C# Int64; status — string enum.
Входные DTO не содержат Actor. Неизвестные JSON properties отклоняются.

| Command | Request | Response |
|---|---|---|
| Submit | `bodyMarkdown?`, required `attachmentIds: Guid[]`, nullable/optional `expectedVersion` | `problemWorkId`, `submissionId`, `attemptNumber`, `status`, `version` |
| Message | required `bodyMarkdown`, optional `replyToMessageId`, required `attachmentIds: Guid[]` | `messageId`, UTC `createdAt` |
| NeedsRevision | required `submissionId`, `feedbackMarkdown` (nonblank), required `expectedVersion` | `reviewId`, `messageId`, `status`, `version` |
| Accept | required `submissionId`, optional `feedbackMarkdown`, required `expectedVersion` | `reviewId`, nullable `messageId`, `status`, `version` |

Response contracts переиспользуют существующие Application result records, без EF/aggregate internals.
First submit: version null/omitted; любое non-null значение даёт 409. Для существующего work требуется persisted version; null/omitted даёт 409 в соответствии с принятой Application semantics.
Review missing/null version или submissionId — 400, без подстановки 0/Guid.Empty.
Required revision feedback проверяет Application и независимо Domain; HTTP не дублирует state machine.

## 4. Authentication strategy

Стандартный ASP.NET Core JWT Bearer. Проверяются signature, issuer, audience, lifetime;
unsigned tokens запрещены, metadata требует HTTPS. Claims mapping middleware отключён,
чтобы adapter получал исходные имена claims. Ошибки токена не раскрываются клиенту.

Конфигурация: `Authentication__Authority`, `Authentication__Audience`; БД — `LMS_POSTGRES`
либо `ConnectionStrings__Lms`. Без валидной конфигурации IdP Bearer не аутентифицирует запросы.
Конкретный IdP не выбран; scheme registration можно заменить без изменения Domain/Application.
Host не применяет migration автоматически. Команды запуска и применения migration — в README.

Test scheme зарегистрирована только из test assembly. Production не имеет test-mode flag,
не доверяет X-Test-* и произвольным identity headers. Помимо test scheme, проверен настоящий
Bearer handler с локальными test signing keys вместо внешнего discovery.

## 5. Actor mapping

`ClaimsActorMapper` требует ровно одну authenticated identity, ровно один UUID `sub` (не empty)
и ровно одну `role` с точным значением `Student` или `Teacher`.
Не объединяет claims разных identities. Unknown/numeric/ambiguous roles и unmapped subjects запрещены.
`Actor.Id` и `Actor.Role` создаются исключительно из этих проверенных claims.
Body Actor fields отвергаются; query/header IDs не могут переопределить principal.

При будущем IdP с opaque/non-UUID subject требуется заменить `IActorMapper` на trusted lookup
внутреннего пользователя. Автоматическое доверие внешнему произвольному ID не предполагается.

## 6. Authorization strategy

Реальный `PostgresAccessPolicy` реализует существующий Application port.

- Student: свой Active assignment; active course enrollment; опубликованный lesson точной
  course/version/lesson reference и наступивший optional AvailableFrom. Та же policy для messages.
- Teacher: grant текущего teacher на course. Grant без GroupId покрывает course; group grant
  требует matching active enrollment ученика в этом course/group. Проверка одинакова для review/message.
- Attachments: каждый ID существует, принадлежит текущему Actor, eligible и ещё не expired.
  Unknown/foreign/ineligible/expired IDs дают одинаковую ошибку без раскрытия metadata.
- Missing access facts запрещают доступ. Все проверки на backend.

Новые таблицы: `AccessEnrollments` (student/course PK, group/active), `AccessLessons`
(course/version/lesson PK, published/availableFrom), `AccessTeachers` (teacher/course index,
optional group), `AccessAttachments` (ID, owner, eligibility, expiry).
Это минимальные trusted provisioning inputs, без новых domain aggregates или management APIs.
Нет production seed пользователей, grants либо attachment IDs.

## 7–8. Problem Details / HTTP status mapping

Все ошибки, включая JSON/model binding, authentication, routing, unsupported media и exceptions,
используют `application/problem+json`:

```json
{
  "type": "urn:lms:error:ConcurrencyConflict",
  "title": "ConcurrencyConflict",
  "status": 409,
  "instance": "/api/v1/problem-works/.../reviews/accept",
  "code": "ConcurrencyConflict",
  "traceId": "..."
}
```

Frontend должен ветвиться по `code`. Неожиданные exceptions логируются на сервере,
клиент получает `InternalError`, без stack trace/SQL/token details.

| HTTP | code |
|---|---|
| 400 | ValidationFailed — malformed JSON/UUID, missing required fields, unknown properties |
| 401 | Unauthenticated — отсутствующий/невалидный токен |
| 403 | Forbidden — роль/access/ownership/inactive assignment; AttachmentNotOwned — metadata refusal |
| 404 | NotFound — отсутствующая assignment/work либо route |
| 405 | MethodNotAllowed |
| 409 | ConcurrencyConflict, WorkAlreadySubmitted, WorkAlreadyAccepted, WorkNotSubmitted, SubmissionIsNotCurrent, SubmissionAlreadyReviewed |
| 415 | ValidationFailed — неподдерживаемый Content-Type |
| 422 | EmptySubmission, FeedbackRequired, InvalidReplyTarget; ValidationFailed для других domain input errors |
| 500 | InternalError |

Feature aliases AssignmentNotFound/ProblemWorkNotFound нормализуются в NotFound;
AssignmentDoesNotBelongToStudent/StudentNotAuthorized/TeacherNotAuthorized/AssignmentInactive — Forbidden.
Это явное transport decision C2; Domain/Application codes не изменены.
InvalidReplyTarget = 422 согласно Feature AT-16. Review после Accepted выдаёт существующий
WorkNotSubmitted; submit после Accepted — WorkAlreadyAccepted. Не подменяем domain semantics.

## 9. Concurrency mapping

И ранняя проверка version в Application, и PostgreSQL compare-and-save failure дают
409 `ConcurrencyConflict`. Автоматического retry нет. PostgresWorkStore не изменён.
API race tests синхронизируют две реальные DB transactions перед сохранением: first submit
оставляет один work/submission/event; reviews оставляют одно решение и только его feedback.
Stale/null resubmit и stale/duplicate review не изменяют persisted history.

## 10. Upload decision

**B — deferred dependency.** Storage/file validation/claiming/cleanup contract отсутствует;
`POST /uploads` не реализован. Text-only flow полностью работает.

Добавлена только metadata boundary для существующего attachment port. Будущий trusted producer
должен физически сохранить/проверить файл перед eligibility и реализовать claiming/link lifecycle
с cleanup. C2 не выдаёт production IDs и не создаёт фиктивные файлы. Положительные attachment
scenarios используют явно test-only metadata в изолированной БД, не доказывают object storage.

## 11. API integration test scenarios

WebApplicationFactory/TestServer + реальный `postgres:17.6-alpine`; новая БД на case,
обе migrations и проверка отсутствия pending model changes; production store/access adapters.

Покрыто:

1. First submit 201, version 1, attempt 1; first non-null version отвергается без work/events.
2. NeedsRevision с feedback; resubmit с актуальной версией; null/stale versions → 409.
3. Accept без feedback; Accepted terminal; сохранение двух immutable attempts и reviews.
4. Message/reply не меняют status/attemptCount; сообщения student/teacher, в том числе после Accepted.
5. Чужой assignment/work, inactive assignment, inactive/missing enrollment, unavailable/scheduled lesson.
6. Teacher без grant, неверная/отсутствующая group, matching group и whole-course grant.
7. Attachment другого владельца, missing, expired, ineligible; проверка и в submit, и в message;
   test-only eligible attachment-only solution сохраняет точные IDs.
8. Revision feedback null/empty/whitespace → 422 без мутации.
9. Non-current submission, stale/duplicate review, invalid cross-work reply.
10. Malformed JSON/UUID/version, missing/null fields, forged Actor body; единый error envelope.
11. 401/404/405/415, unsupported/ambiguous roles, query/header impersonation.
12. Production не принимает test headers/unsigned token.
13. Настоящий Bearer: valid signed token → 201; wrong key/issuer/audience, expired/future → 401;
    invalid subject/role → 403.
14. Детерминированные races двух HTTP requests и реальных transactions: first submit и reviews.

Все обязательные C2 scenarios покрыты; raw PostgreSQL constraints/rollback tests C1 остаются зелёными.

## 12. Build/test results

Baseline перед implementation: Domain 24, Application 15, Infrastructure 10 passed.
Финальные команды (SDK 10.0.401):

```sh
/tmp/lms-dotnet/dotnet build Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false
/tmp/lms-dotnet/dotnet test Lms.sln --no-build --no-restore -m:1
git diff --check
```

- Full solution build: **0 warnings, 0 errors**.
- Domain: **24 passed**.
- Application: **15 passed**.
- Infrastructure/PostgreSQL: **10 passed**.
- API/PostgreSQL: **50 passed**.
- Всего **99 passed**, 0 failed, 0 skipped.
- Kestrel отдельно запущен на loopback в Production environment; unauthenticated POST →
  401 application/problem+json Unauthenticated. Процесс после smoke check остановлен.
- `git diff --check` успешен; diff Domain/Application/PostgresWorkStore/initial migration пуст.

Полные transient logs: `/tmp/lms-c2-build.log`, `/tmp/lms-c2-tests.log`, `/tmp/lms-c2-host.log`.
Обычный sandbox launcher в окружении не работает (bubblewrap mountinfo); использована одобренная
escalation. Один крупный write command не стартовал из-за timeout approval review; разделённые
операции успешно выполнены. Системный dotnet wrapper не содержит SDK; использован принятый `/tmp/lms-dotnet`.

## 13. Remaining decisions / deviations

- IdP и provisioning внутренних user IDs/roles/grants остаются deployment/integration decisions.
  Принят строгий initial claims contract, с заменяемым mapper и deny-by-default.
- Upload/storage и atomic claiming/cleanup — явная deferred dependency B; metadata seed только tests.
- HTTP aliases нормализованы; missing existing-work version = 409 по existing Application semantics.
- Markdown сохраняется как исходный текст; renderer/sanitization при выводе относится к последующему read/UI scope.
- Access facts проверяются на момент Application authorization. Синхронизация конкурентного
  административного revoke с выполняющейся командой не вводилась; persistence C1 не изменялась.
- Нет blockers C2. G03 CLOSED; обязательный revision feedback не пересматривался.

## 14. ExecPlan changes

Обновлён текущий статус, добавлен §14: baseline, branch, решения до implementation,
auth/access/metadata architecture, upload B, transport statuses/version semantics, результаты
и exit checklist. Исторические B/C1 записи сохранены как история.

**Остановка после C2. C3/C4/read models/workspace queries/frontend не начинались.**
