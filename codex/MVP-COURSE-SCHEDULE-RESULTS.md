# Course Schedule & Academic Workload — результат

Дата проверки: 2026-10-15. Статус: **реализовано в synthetic-only контуре; owner acceptance pending**.

Разрешённый срез: PostgreSQL-модель, защищённые HTTP routes, ученический календарь и административный редактор. Не затрагивались PRS1 I2, G02, production, реальные данные, commit/push/deploy.

## Модель и инварианты

Акторы и операции:

- ученик читает расписание только доступного ему курса;
- назначенный преподаватель читает расписание назначенного курса;
- администратор организации читает и изменяет нагрузку/расписание только курса своей организации;
- право преподавателя на изменение расписания не выдумывалось: writes оставлены только администратору.

Хранимые модели:

- `CourseWorkloads`: `TotalAcademicHours`, `ScheduledAcademicHours`, `IndependentAcademicHours`, `AcademicHourMinutes`, version/CAS и audit identity;
- `CourseScheduleRules`: день недели, локальные start/end, явно заданные академические часы, период, IANA timezone, формат, место/HTTPS URL, тип события, active/version;
- `CourseScheduleExceptions`: устойчивая пара `(RuleId, OriginalDate)`, перенос/отмена и overrides одного экземпляра;
- `CourseScheduleCommandReceipts` и `CourseScheduleAudits`: replay-safe идемпотентность и аудит mutations.

Инвариант `Total = Scheduled + Independent` проверяется приложением и PostgreSQL constraint. Академические часы события не вычисляются из его длительности. Фактически проведённые часы возвращаются `null`, пока нет достоверного источника проведения. Отмена вычитает плановые часы; перенос сохраняет или явно меняет академические часы.

Разовое занятие представляется правилом с единственной совпадающей датой (`EffectiveFrom = EffectiveTo`); отдельная бесконечная материализация не создаётся. События генерируются на чтении не более чем за 93 дня, `upcoming` сканирует не более 366 дней bounded-чанками. Идентичность экземпляра стабильна: `ruleId:originalDate`.

DST: несуществующее локальное время отклоняется `NonexistentLocalTime`; для неоднозначного времени явно выбирается больший UTC offset (более ранний instant), тихого смещения нет.

## Транзакции и авторизация

Writes выполняются в `Serializable` transaction. Порядок: processing advisory lock → актуальная admin membership `FOR UPDATE` → course `FOR UPDATE` и org ownership → current processing gate → idempotency advisory lock/receipt → CAS → domain write + audit → commit. Повтор того же ключа и payload возвращает `Changed=false`; другой payload — конфликт. Неизвестный исход не ретраится клиентом автоматически.

Read paths повторно проверяют component authority и current processing до публикации результата. Course/org selectors фильтруются на сервере. Optional `LessonId` принимается только для опубликованного lesson того же курса; missing/forged parent отклоняется. Вложений, истории и постраничных counts в этом срезе нет.

HTTP:

- `GET /api/v1/courses/{courseId}/schedule`
- `GET /api/v1/courses/{courseId}/workload`
- `GET /api/v1/courses/{courseId}/schedule/events?from=...&to=...`
- `GET /api/v1/courses/{courseId}/schedule/upcoming?limit=...`
- `GET /api/v1/admin/courses/{courseId}/schedule`
- admin commands для workload, rules и occurrence move/cancel.

Admin writes требуют HTTPS, exact allowed Origin, CSRF, валидный CourseBearer, org-admin permission и current processing evidence.

## UI

Существующий dashboard расширен вкладкой «Расписание» без второго AppShell. Реализованы week/month/list, previous/next, «Сегодня», today marker, loading/empty/error, диалог события, высота по реальному временному интервалу и мобильный list fallback. Правая панель читает workload/rules/upcoming из тех же API. Редактор встроен в существующий admin course screen и поддерживает workload, recurring/single-date rules, timezone, format/place и явный move/cancel одного occurrence.

Synthetic example: 72 ак. ч. = 48 по расписанию + 24 самостоятельной работы; понедельник 17:00–19:00 (2 ак. ч.) и суббота 10:00–13:00 (3 ак. ч.).

## Изменённые области

- `src/Lms.Application/CourseSchedule.cs`
- `src/Lms.Infrastructure/CourseSchedule*`, `PostgresCourseScheduleService.cs`, `LmsDbContext.cs`, migration `20261015150352_CourseScheduleWorkloadMvp*` и snapshot
- `src/Lms.Api/CourseScheduleHttp.cs`, composition, route mapping и error mapping
- `src/Lms.Web/src/api/contracts/courseSchedule.ts`
- calendar/summary/editor React components и их scoped CSS
- существующие `StudentCourseDashboard` и `CourseAdministration`
- service, HTTP и frontend tests; Chromium smoke script

Посторонние незакоммиченные изменения сохранены. Git branch/HEAD/index не менялись; commit/push/merge/rebase/deploy не выполнялись.

## Проверки

- `/tmp/lms-dotnet-course-schedule/dotnet build Lms.sln --no-restore` — PASS, 0 warnings, 0 errors (canonical checkout).
- `/tmp/lms-dotnet-course-schedule/dotnet test Lms.sln --no-restore --logger 'console;verbosity=minimal'` — PASS: Domain 43/43, Application 369/369, Infrastructure 346/346, API 388 passed + 12 configured browser skips; 0 failed (isolated implementation worktree).
- targeted canonical PostgreSQL service test — PASS 1/1, включая workload, idempotency/CAS, несколько правил, bounded generation, move/cancel, overlap, DST, forged grant/lesson parent, cross-org и ProcessingRestrictions.
- targeted canonical HTTP+PostgreSQL test — PASS 1/1, включая migration/pending-model check, CSRF, student write denial, unassigned teacher, cross-org selector и replay.
- `pnpm --dir src/Lms.Web test -- --run` — PASS, 22 files / 445 tests.
- `pnpm --dir src/Lms.Web build` — PASS.
- Chromium smoke `course-schedule-smoke.mjs` — `week=PASS`, `month=PASS`, `editor=PASS`, unknown API calls `[]`.

Visual evidence:

- [Недельный календарь](evidence/COURSE-SCHEDULE/week.png)
- [Месячный календарь](evidence/COURSE-SCHEDULE/month.png)
- [Редактор расписания](evidence/COURSE-SCHEDULE/editor.png)

## Negative-access matrix

| Сценарий | Ожидание | Результат |
|---|---|---|
| Student admin write | 403 | PASS |
| Unassigned teacher read | no bearer / 401 | PASS |
| Forged course grant | Forbidden | PASS |
| Foreign organization course read | 404 without metadata | PASS |
| Foreign organization write | NotFound, no row | PASS |
| Missing processing binding | Forbidden | PASS |
| Active subject restriction | Forbidden | PASS |
| Missing/forged Lesson parent | ValidationFailed | PASS |
| Missing CSRF | 403 | PASS |
| Group mismatch | N/A — schedule is intentionally course-scoped after valid enrollment/assignment |
| Attachments/history | N/A — feature has no attachment/history path |
| Counts/pagination leakage | N/A — no count or paginated response; selectors are org/course scoped |
| Duplicate command | same result with `Changed=false`; no duplicate effect | PASS |

## Guardrails §7

- [x] Actor, use case, invariants and supported states documented.
- [x] Accepted S2/S3 respected; PRS1 exact remains off, G02 remains blocked; narrow S4 exception explicitly authorized by owner.
- [x] Org/course/ownership checked server-side; group N/A for course-scoped schedule.
- [x] Affected subjects and optional Lesson parent accounted for; otherwise deny.
- [x] Lists/aggregates scoped before disclosure.
- [x] Attachments/history N/A — no such access path.
- [x] Transaction, lock order, CAS, replay and unknown-outcome behavior specified.
- [x] Validation, limits and safe errors tested.
- [x] Unit/component + integration + negative-access + synthetic browser smoke recorded.
- [x] Schema/API change is inside the explicit owner exception; unrelated user edits preserved.
- [x] Synthetic data only; no secrets, unsafe logs or release claims.
- [x] Exact executed results recorded; remaining gaps below are not claimed proven.

## Оставшиеся границы

- Exact navigation from an event to a versioned Lesson is **not implemented**: current schedule can validate a course-local published `LessonId`, but the accepted event contract does not carry the immutable `ContentVersionId` required by the existing route. Adding that pair and selector needs a separate owner decision; UI does not fabricate a link.
- Teacher name is not shown because there is no authoritative per-occurrence instructor relation in this slice.
- Attendance and conducted hours remain unavailable (`null`), not fabricated from calendar presence.
- Assignment deadline events are not added because the current protected assignment source exposes no compatible due-date event contract here.
- Chromium smoke uses intercepted synthetic API responses. Full real-server browser E2E is **NOT RUN / NOT PROVEN**.
- Production cutover, pilot authorization and real-data readiness are not authorized.

Readiness labels: **UI COMPLETE (synthetic scope)**; **E2E ACCEPTED: no**; **SYNTHETIC MVP ACCEPTED: owner decision pending**; **PILOT AUTHORIZED: no**.
