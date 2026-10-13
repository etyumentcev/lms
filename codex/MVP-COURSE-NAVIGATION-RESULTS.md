# MVP Course & Teacher Navigation — results

2026-10-12. Основание: текущее задание владельца **COURSE NAVIGATION AUTHORIZED — ISOLATED IMPLEMENTATION**. Работа последовательно в `/home/tea/github/lms`, без subagents.

**OWNER ACCEPTANCE PENDING · PARTIAL / HTTP INTEGRATION BLOCKED · G02 BLOCKED · CLOCK-DEPLOY-01 OPEN · H04/READY BLOCKED · S4 HTTP CUTOVER NOT AUTHORIZED · PRS1 EXACT OFF.**

Реализованы UI списка курсов, навигация ученика/преподавателя, явная capability преподавателя и изолированный S3/current-processing component списка. **Полный сценарий через защищённый HTTP не завершён:** `GET /api/v1/courses` не зарегистрирован в текущем host. Страница запрашивает этот контракт, показывает безопасное состояние недоступности при 404 и не подставляет демонстрационные курсы. Нового production/test-listening host, auth adapter или обхода security gates нет. Это не UI COMPLETE, E2E ACCEPTED, SYNTHETIC MVP ACCEPTED или PILOT AUTHORIZED.

## Контракт и минимальное расширение

Actor → use case: ученик открывает назначенный курс/занятие/задачу/решение/историю; назначенный преподаватель просматривает условие без работы, открывает отправленную/возвращённую/принятую работу и историю. Entities: Course, immutable PublishedContentVersion, Lesson, ProblemAssignment, ProblemWork, Submission, Review. Состояния сохранены: NotStarted → Submitted → NeedsRevision → Submitted → Accepted. Навигация ничего не отправляет и не создаёт работу.

Инварианты: IDs — только selectors; сервер определяет ownership/capabilities; отсутствие `canSubmit` не означает Teacher; нет ссылки с отсутствующим workId; история неизменяема; чтение фото через существующий защищённый endpoint. Существующие SolutionForm, ReviewForm, SubmissionViewer, HistoryTimeline, MarkdownContent и зелёные Sapientum primitives переиспользованы.

Прочитаны AGENTS.md, guardrails/reconciliation, действующие read models/endpoints, ADR-L15/L18 и связанные resource/history/browser ограничения, owner register и DEADLINE-01 checkpoint. Специализированные `model-domain`, `implement-use-case`, `implement-feature` отсутствуют в доступном каталоге; их выполнение не заявляется.

| Данные | Контракт / server authority | Граница |
|---|---|---|
| Список курсов | Новый `ICourseNavigationQueries.Courses(TokenAuthority)` → `{items: [{course, role, progress}]}` | Только isolated component, без DI/HTTP registration |
| Кандидаты списка | Grants из уже валидированного server-issued TokenAuthority; каждый Course проходит `ComponentAuthorization` с S3 ownership и `PostgresCurrentProcessingGate` до чтения metadata | Нельзя bind TokenAuthority из request JSON, браузерных IDs или роли; подпись/issuer/audience проверяет существующий validator у будущего adapter |
| Название/описание | Последняя immutable published content version доступного курса; при отсутствии версии — серверный course ID, nullable metadata | Нет client-generated courses; по одному item на курс |
| Прогресс списка | Nullable existing `CourseProgressDto`; UI показывает только полученное значение | Новый component возвращает null: legacy CourseWorkspace aggregate не доказывает S3/subject-processing фильтрацию, поэтому не переиспользован |
| Course/Lesson/Work | Existing workspace DTO/query/endpoints | Legacy compatibility, не принятое защищённое S4 HTTP |
| Teacher navigation | Additive `PermissionsDto.CanViewAsTeacher`, вычисляется сервером после existing access checks независимо от статуса работы | Не новое право/grant. Отсутствующее поле в старом DTO не считается true |
| Условие без работы | Уже авторизованная строка LessonWorkspace + её явная capability; route включает course/lesson/assignment | Отдельный server assignment endpoint не нужен; повторный GET при открытии preview |
| История/возврат | Course/Lesson IDs из DTO, teacher attempt ID в query string; queueSearch остаётся в router state | Query string не даёт доступ; work/attachments по-прежнему авторизует сервер |

Предложенное минимальное будущее подключение: отдельно авторизованный adapter `GET /api/v1/courses` должен получать TokenAuthority из принятого validator, вызывать новый component и применять accepted ingress/deadline/egress правила. В этом slice adapter не реализован: текущий Program.cs использует ClaimsActorMapper/legacy grants, а интеграционный защищённый host не предоставлен. Подключать component через этот mapper означало бы обойти S3/processing boundary.

Список не возвращает total/count/cursor и не использует постраничную фильтрацию: до materialization каждого item проверяются scope и current processing; после metadata чтения — повторный gate. Проверяются qualified time и expiry в начале/конце всего вызова. Исключения processing/DB не превращаются в allow. Курс не несёт student work/progress/subject-derived aggregates. Положительного межзапросного кеша нет. Resource/processing checks переиспользуют принятые компоненты и их собственные read snapshots; нового long-lived/ambient snapshot и writer-протокола нет.

Новые writes, transaction owner/lock order/CAS/audit/commit/retry — **not applicable**, добавлены только чтение и presentation. Existing submit/review write semantics не изменены; UI не выполняет автоматический replay. Работа не заявляет принятую интеграцию этих legacy writes с current-processing serialization или deadline.

## Работающие переходы

| Путь | Реализация и проверка |
|---|---|
| Мои курсы → Курс → Мои курсы | Реальная страница вместо placeholder; название, описание, роль, nullable server progress, loading/empty/error; повторный GET при возврате. UI contract tests PASS; public HTTP adapter BLOCKED |
| Курс → Занятие → Задача → Решение → История | Existing pages/forms; полный UI test проходит из списка до истории, browser back/forward между вкладками и возврат к тому же занятию |
| Преподаватель → Занятие → Работы учеников | Заголовок по явным capabilities; серверные rows; ни роль, ни доступ не выводятся из отсутствия canSubmit |
| NotStarted, workId=null → условие | Вложенный route `/courses/:courseId/lessons/:lessonId/assignments/:assignmentId`; существующий Markdown, «Решение ещё не отправлено», без submit/review controls и фиктивных work links |
| Submitted → Проверить решение | Existing teacher work screen, `canAccept`/`canRequestRevision` по-прежнему определяют действия |
| NeedsRevision → Посмотреть решение | Явная capability открывает work/history; новая проверка недоступна до следующей submission |
| Accepted → Посмотреть решение → История | Capability сохраняется при terminal status; existing screen остаётся read-only; тест проверяет попытки и отсутствие кнопки принятия |
| История преподавателя → попытка → текущая | `?tab=history`, `?attempt=...`; все существующие attempts выбираются из DTO, browser back/forward восстанавливают выбор; кнопка текущей попытки и возврат к занятию |
| Возврат к курсу/занятию | Breadcrumbs из server DTO; явные ссылки «Назад к занятию/курсу», отдельный возврат к очереди сохраняет её filters |
| Отзыв/исчезнувшее назначение | Новый GET на list/lesson-preview navigation, 403 скрывает данные; отсутствующая или чужая preview row показывает недоступность |
| Несохранённые пометки | Смена query внутри того же work сохраняет draft; уход на другой pathname/reload сохраняет existing confirmation. History index отслеживается при смене location key |

Browser history проверена в MemoryRouter/jsdom, не в настоящем защищённом browser host. Восстановление произвольного неотправленного текста после ухода со страницы не добавлялось; сохранён именно навигационный контекст, существующие submission flows не переписаны.

## Точные проверки

Команды запускались из `/home/tea/github/lms`. Evidence: [каталог](evidence/MVP-COURSE-NAVIGATION/), [сводка](evidence/MVP-COURSE-NAVIGATION/test-summary.json).

```sh
PATH=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin:$PATH pnpm --dir src/Lms.Web test
PATH=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin:$PATH pnpm --dir src/Lms.Web build
/tmp/b07c2g/sdk/dotnet test Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/course-navigation/regression
/tmp/b07c2g/sdk/dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/course-navigation/api-final
```

| Проверка | Точный результат |
|---|---|
| Final frontend | **387 passed, 0 failed**, 13 files; `frontend-verified.log` |
| Final build | **PASS**, tsc + Vite; `build-verified.log` |
| Domain regression | **43 passed, 0 failed, 0 skipped** |
| Application regression | **340 passed, 0 failed, 0 skipped** |
| Infrastructure / реальный PostgreSQL | **258 passed, 0 failed, 0 skipped**, включая новый CourseNavigation test |
| Final API / реальный PostgreSQL + in-process TestServer | **221 passed, 0 failed, 0 skipped**, включая новый teacher navigation test |
| Совокупный final backend | **862 passed** по четырём проектам; full-solution первоначальный run не переименован в PASS |
| Browser E2E / реальные устройства | **BLOCKED / NOT RUN** — нет разрешённого защищённого host с S3 + current ProcessingRestrictions + accepted ingress/egress composition |
| Whitespace затронутого scope | PASS. Общий `git diff --check` сообщает существующую постороннюю blank line в `docs/content/COURSE-CONTENT-SPEC.md:1325`; файл не изменён |

Первоначальные failures сохранены: frontend 369/371 (старое ожидание placeholder и блокировка перехода к истории при unsaved annotations); первая build — отсутствующий обязательный AppShell.context. После исправлений — результаты выше. Первый backend run: Domain/Application/Infrastructure PASS; API 219 passed/2 failed. Причины: exact PermissionsDto assertion не учитывал additive field; новый test submit пропустил обязательный `attachmentIds: []`. Обновлены assertion и test request; runtime submit validation не ослаблена. Final API rerun — 221/221.

### Negative-access evidence

| Случай | Ожидаемый отказ и фактическая проверка |
|---|---|
| Чужая организация / неизвестный mapping | Новый PG `Navigation_list_enforces_scope_processing_and_delayed_revocation_without_counts_leakage`: чужой org, b/legacy/missing не раскрывают items; положительные Student/Teacher controls |
| Чужой курс / отсутствующий teacher grant | Тот же PG test: a2 без required processing evidence, пустые grants Student/Teacher → пустой list. Existing S3 scope matrix и shared-course API tests отдельно проверяют неподходящий course/group |
| Неназначенный преподаватель / чужая work | Новый API `Navigation_teacher_capability_survives_terminal_state_and_absent_work_never_has_work_id`: outsider получает 403 на lesson/work; student получает 403 teacher alias |
| Отзыв назначения | Новый PG test: refreshed/current authority больше не содержит отозванного grant; ранее выданный token действует до exp по ADR-L15, на exp → Forbidden. Legacy API test удаляет teacher grant и проверяет 403 lesson/work — это отдельная legacy temporal модель, не требование immediate JWT revoke |
| Перемещение membership/group | Existing `Course_enrollment_is_explicit_and_does_not_imply_named_group`, `Concurrent_membership_revoke_and_move_never_leave_effective_old_membership_grants`, S3 security suites прошли в infrastructure regression |
| Current restrictions / missing context | Новый list test: subject restriction исключает Student course; Teacher course metadata остаётся без student-derived progress. Existing `A2_opaque_selectors_cannot_bypass_parent_or_child_restrictions`, `Suspended_org_quarantine_expired_missing_context_deny` — PASS |
| Lists/counts/pagination | Новый list test проверяет точный разрешённый набор без иных items/counts. Existing `Shared_course_never_exposes_other_students_rows_or_other_teacher_groups` и `Queue_search_and_attempt_filters_exclude_earlier_rows_before_pagination` — PASS |
| Missing/forged parent, чужие вложения/история | Existing `Ownership_follows_submission_review_parent_and_denies_foreign_student`, `Pending_attachment_is_uploader_only_attached_file_uses_parent_scope`, attachment/journey suites — PASS. Новых file routes/parent contracts нет |
| UI forged assignment / withdrawn access | Новые UI tests: forged/неразрешённый preview не показывает statement; повторное чтение после 403 не оставляет course/lesson content |
| Expiry/retry/duplicate submission | Existing frontend transport/problem/review/attachment tests — PASS; new list 401/403/404/503 safe states; no POST introduced. Browser CSRF/session/physical-device proof — BLOCKED |

Legacy API integration tests **не являются доказательством принятого защищённого HTTP**. S3/current-processing PostgreSQL component tests не доказывают, что production Program.cs подключает эти компоненты.

## Изменённые пути

Existing files (11):

- `src/Lms.Application/ReadModels.cs`
- `src/Lms.Infrastructure/PostgresWorkspaceQueries.cs`
- `src/Lms.Web/src/App.tsx`
- `src/Lms.Web/src/api/contracts/lessonWorkspace.ts`
- `src/Lms.Web/src/features/lesson/LessonPage.tsx`
- `src/Lms.Web/src/features/problem-workspace/ProblemPage.tsx`
- `src/Lms.Web/src/features/teacher-review/TeacherReviewPage.tsx`
- `src/Lms.Web/src/features/teacher-review/useUnsavedAnnotations.ts`
- `src/Lms.Web/src/test/shell.test.tsx`
- `src/Lms.Web/src/test/teacherReview.test.tsx`
- `tests/Lms.Api.Tests/ContractApiTests.cs`

Новые source/tests (6): `src/Lms.Application/CourseNavigation.cs`, `src/Lms.Infrastructure/PostgresCourseNavigationQueries.cs`, `src/Lms.Web/src/features/course/CoursesPage.tsx`, `src/Lms.Web/src/test/courseNavigation.test.tsx`, `tests/Lms.Infrastructure.Tests/CourseNavigationTests.cs`, `tests/Lms.Api.Tests/CourseNavigationTests.cs`. Также добавлены этот отчёт и evidence.

## Guardrails §7 / self-review

| Проверка | Статус / основание |
|---|---|
| Actor/use case/invariants/states | covered — контракт и transition matrix выше |
| Accepted S2/S3/PRS1 и закрытые gates | covered — список isolated, production DI/HTTP не менялись; exact OFF |
| Org/course/group/ownership | covered component tests; legacy row/work checks сохранены; защищённая HTTP интеграция blocked |
| Subjects / embedded sources | covered: список только course metadata без work/student aggregates; scoped progress отсутствует, не подменён legacy |
| Lists/counts до раскрытия | covered: каждый item допускается до metadata read; нет client authority, total/cursor leakage |
| Attachments/history | covered существующими server controls/regression; history records не изменяются |
| Transaction/locks/CAS/retry/unknown outcome | not applicable для новых writes — их нет; existing submit/review не переписаны, повторных POST на navigation нет |
| Validation/limits/errors | covered DTO decoders, encoded selectors, 401/403/404/503, безопасный Markdown; новый public endpoint отсутствует |
| Unit/integration/negative/E2E | точные результаты выше; browser E2E blocked |
| Schema/API/security scope | additive presentation DTO + isolated read component разрешены текущим owner task; новых rights/migrations/HTTP registration нет |
| Real data/secrets/logging/readiness | synthetic fixtures only; no deploy/cutover/unsupported acceptance claim |
| Оставшиеся proof obligations | S4 host/adapter, scoped aggregate progress, browser/device evidence |

## Preservation и owner acceptance

Сохранены исходная ветка `codex/l1-s4-jwt-api-authorization`, HEAD `492e30aef65bbddd24f107903bfaa54a563abe39` и SHA-256 Git index. Canonical directory не находился на main; checkout не переключался согласно явному требованию сохранить branch/HEAD/index и dirty work.

Из 595 baseline files 11 перечисленных изменены в рамках задачи, остальные **584/584 сохранены побайтово**, удалений нет. `.obsidian/` и ignored build artifacts не входят в baseline. Предшествующие изменения LessonPage/TeacherReviewPage дополнены, прежние checkpoints и посторонние файлы не переписаны. Evidence: [preservation.json](evidence/MVP-COURSE-NAVIGATION/preservation.json).

Оставшийся блокер конкретен: для полного «Мои курсы → …» нужен отдельно разрешённый защищённый HTTP adapter списка и принятая интеграция existing workspaces, auth/current processing/deadline. Подмены legacy auth ради демонстрации нет. Реальный browser E2E до этого не выполнялся. Никаких production cutover, deploy, commit, push, merge или rebase. Результат остановлен на owner acceptance данного isolated slice; полная цель остаётся частично заблокированной.
