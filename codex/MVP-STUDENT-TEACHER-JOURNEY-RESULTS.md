# MVP Student → Teacher journey — results

2026-10-12. Основание: OWNER DECISION — MVP-B01-G02-DEADLINE-01 в задании: DEADLINE-01 isolated component accepted; synthetic vertical slice authorized. Работа выполнена последовательно в `/home/tea/github/lms`.

**OWNER ACCEPTANCE PENDING · G02 BLOCKED · CLOCK-DEPLOY-01 OPEN · H04/READY BLOCKED · S4 HTTP CUTOVER NOT AUTHORIZED.** Полный пользовательский synthetic MVP не объявляется принятым. Есть проверенный legacy API integration journey и исправления UI; защищённая сквозная браузерная демонстрация пока заблокирована интеграцией.

## Контракт и границы

Actor → Use Case: ученик отправляет фотографии и исправление; назначенный преподаватель открывает условие/фото/историю, возвращает замечание и принимает исправление. Entities: ProblemAssignment, ProblemWork, Submission, Review, Message, StoredAttachment, immutable content version. States: NotStarted → Submitted → NeedsRevision → Submitted → Accepted. Исторические submissions/reviews/messages не редактируются; review привязан к конкретной submission; фото читаются через защищённый endpoint по opaque ID, не public storage URL. Прогресс рассчитывает сервер.

Источник данных UI — существующие workspace DTO/API и server permissions. Capability определяет только ссылку/видимость UI; полномочие повторно проверяет сервер. Изменения не добавляют права, DTO, endpoints, storage/migrations, production clock verifier или auth adapter. Использованы существующие компоненты AttachmentPreview, SubmissionViewer, AnnotationPhoto и MarkdownContent.

Прочитаны AGENTS.md, [guardrails](SAPIENTUM-MVP-ENGINEERING-GUARDRAILS.md), [reconciliation](MVP-GUARDRAILS-RECONCILIATION.md), ADR-L06/L10/L15/L18 и последний DEADLINE-01 checkpoint. Специализированные skills model-domain/implement-use-case/implement-feature отсутствуют в доступном каталоге; их выполнение не заявляется.

### Блокер интеграции, а не отсутствующие формы

`src/Lms.Api/Program.cs` подключает `ClaimsActorMapper`, `PostgresAccessPolicy`, `PostgresWorkStore`, legacy workspace queries/AttachmentService. Этот HTTP путь не подключает `PostgresScopedAuthorityReader`, component JWT policy и current ProcessingRestrictions. В legacy Actor нет принятой organization-scoped HTTP authority. TestServer использует test-only auth из test assembly и legacy grants; его успешный сценарий НЕ доказывает S2/S3/organization/processing integration.

`ProblemWorkService` проверяет legacy access до `PostgresWorkStore.Save`. Save владеет отдельной PostgreSQL transaction (default ReadCommitted), существующую work строку обновляет CAS по Version (row lock), добавляет неизменяемую историю/events и атомарно commits. При первом submit уникальное назначение защищено constraint; claim attachments выполняют существующие triggers/locks. Snapshot load — RepeatableRead. Это не новый принятый протокол fresh authority/current processing под всеми locks: такой write в этом slice НЕ реализован. Unknown commit нельзя трактовать как rollback; UI запрещает автоматическую повторную отправку и требует authoritative refresh. Durable idempotency/resolution и bounded dispatch остаются отдельными зависимостями. Новых writes/lock order/retry semantics в реализации нет.

Нельзя «починить демонстрацию» включением LocalDemo/подстановкой lmsAuth/test headers в пользовательский host: это обошло бы требуемую интеграцию. Новых слушающих test endpoints не создано. Продолжаются только независимые UI и изолированные compatibility tests, явно разрешённые заданием. S3/current-processing компонентные negatives проверены regression отдельно, не приписаны этому HTTP journey.

## Переходы до и после

Во всех строках ниже «API работает» означает existing isolated legacy TestServer + реальный PostgreSQL, а не принятый S4 HTTP.

| Переход | До | После / persistence и evidence |
|---|---|---|
| Преподаватель открывает курс → занятие | CoursePage/LessonPage и реальные GET workspace; immutable content из PostgreSQL | Без изменений, API integration читает оба workspace. `/courses` остаётся placeholder, нужна известная ссылка на курс |
| Занятие → отправленная работа для проверки | Все ссылки вели в `/problem-assignments/:id`, student-only workspace; переход преподавателя разорван | Если server canAccept/canRequestRevision и work ID присутствует — `/teacher/reviews/:workId`, «Проверить решение». Оба capability проверены UI tests. Role-gated teacher endpoint существовал |
| Ученик открывает задачу → загружает одну/несколько страниц | ProblemPage/SolutionForm/AttachmentUploader; multipart POST uploads, preview через authenticated GET | Работало; новая integration проверяет две страницы PNG/JPEG. Метаданные в PostgreSQL, оригинальные bytes в private local storage, не в PostgreSQL |
| Отправка фото | POST assignment submissions; CAS/version, server ownership и claim | Новая integration отправляет photo-only work, сохраняет точный порядок IDs. Никаких mock API в этом integration test |
| Преподаватель открывает фото и условие | TeacherReviewPage/SubmissionViewer: historical statement, оригинал, страницы, масштаб/перемещение/fullscreen | Сохранено. Integration сравнивает statement и byte-for-byte GET originals после reopen; UI tests проверяют просмотр. Читаемость настоящего листа А4 на планшете/телефоне НЕ ПРОВЕРЕНА: test images маленькие |
| Замечание → NeedsRevision | ReviewForm требует непустое замечание; POST reviews/needs-revision; Review+Message+event сохраняются | Сохранено; новое test читает замечание отдельным GET ученика |
| Исправление → Submitted | Новая submission с новой загрузкой; прежний attachment нельзя повторно claim; история сохраняется | Новый test загружает JPEG исправления (раньше full workflow исправлял только текст), затем открывает history и прошлые originals |
| Выбор предыдущей попытки | Старые фото доступны, замечание только в боковом контексте; результат в основной области скрыт для archive; фото замечания не отображались | Результат и его текст/вложения видны рядом с выбранной попыткой. Связь строго review.submissionId → messageId; новая integration/UI проверяют неизменность/выбор; 404 вложения не скрывает текст и не выдаёт фото |
| Accepted → прогресс | POST reviews/accept, исторические записи, server course/lesson progress и queue | Новое test подтверждает Accepted, 2 submissions, 2 reviews, 2 messages, 6 events, курс 100%, queue total 0. Старая integration отдельно проверяет lesson 100% |
| Повторное открытие | Имеются отдельные history/file tests, но full workflow не проверял две фотопопытки вместе | Новые HttpClient + независимые DbContext + свежие GET: обе попытки, оба замечания, три originals доступны ученику/преподавателю. Restart процесса/БД и backup restore этим тестом не проверяются |
| Вход/переключение пользователей → полный browser E2E | window.lmsAuth ожидает trusted hosting integration; landing demo links содержат mock IDs | BLOCKED без обхода S2/S3/ProcessingRestrictions; браузерный E2E с PostgreSQL NOT RUN. Frontend jsdom не назван browser E2E |

Для terminal work (Accepted/NeedsRevision), где lesson DTO уже не даёт review capability, изменённая ссылка не выводит роль по догадке: teacher navigation/read-only history из занятия остаётся отдельным функциональным пробелом. История доступна по известной teacher work ссылке. До первой отправки teacher → student-only problem workspace также не закрыт этим изменением; нужен принятый read-only teacher assignment contract или уже имеющийся безопасный контекст.

## Изменённые файлы

- `src/Lms.Web/src/features/lesson/LessonPage.tsx`: переход к проверке по существующим server capabilities/work ID.
- `src/Lms.Web/src/features/teacher-review/TeacherReviewPage.tsx`: результат выбранной попытки и защищённые фото замечания.
- `src/Lms.Web/src/test/lesson.test.tsx`: 3 routing regression cases.
- `src/Lms.Web/src/test/teacherReview.test.tsx`: 2 history/photo-denial cases.
- `tests/Lms.Api.Tests/StudentTeacherJourneyTests.cs`: отдельный сквозной API compatibility test на disposable PostgreSQL.
- Этот отчёт и `codex/evidence/MVP-STUDENT-TEACHER-JOURNEY/`.

Никакого редизайна или изменения backend runtime. Фото замечания отображаются при наличии attachmentIds в полученном Message DTO; существующий ReviewForm не добавляет фото в review command. Этот UI case проверен синтетическим DTO и не заявляется новым сквозным workflow загрузки фото преподавателем.

## Проверки

Окончательные результаты: **frontend 371/371 PASS**, 12 files, failed 0; **TypeScript + Vite build PASS**. Backend по последнему результату каждого assembly: **860/860 PASS**, failed 0, skipped 0 (Domain 43, Application 340, Infrastructure 257, API 220). Это полный solution run плюс повтор всего API assembly после исправления нового test expectation, а не один полностью зелёный solution run. Остальные assemblies после него не менялись.

Точные команды (из `/home/tea/github/lms`):

```sh
PATH=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin:$PATH pnpm --dir src/Lms.Web test
PATH=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin:$PATH pnpm --dir src/Lms.Web build
/tmp/b07c2g/sdk/dotnet test Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/mvp-journey/regression
/tmp/b07c2g/sdk/dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/mvp-journey/api-final
```

Первоначальная targeted команда совпадает с командой воспроизведения ниже, но использовала `/tmp/mvp-journey/targeted`. Её exit 1; solution run exit 1 из-за нового negative expectation; final API/frontend/build exit 0. [Сводка TRX](evidence/MVP-STUDENT-TEACHER-JOURNEY/test-summary.json), [frontend](evidence/MVP-STUDENT-TEACHER-JOURNEY/frontend.log), [build](evidence/MVP-STUDENT-TEACHER-JOURNEY/build.log), [solution run](evidence/MVP-STUDENT-TEACHER-JOURNEY/regression.log), [final API](evidence/MVP-STUDENT-TEACHER-JOURNEY/api-final.log). TRX сохранены в соответствующих evidence подкаталогах. Новые 5 UI cases входят в 371, новый API journey входит в 220; повторные запуски не суммируются.

`git diff --check` по четырём изменённым frontend файлам — PASS. Новые test/report проверены на trailing whitespace; локальные ссылки отчёта проверены. Чужие dirty files не исправлялись.

Новая integration: `ContractApiTests.Photo_revision_journey_reopens_originals_history_and_progress_in_fresh_scopes`. Host — WebApplicationFactory/TestServer без TCP listener; test auth только в `tests/Lms.Api.Tests/ApiFixture.cs`; PostgreSQL 17.6-alpine disposable Testcontainers, полная цепочка migrations, отдельная database, private temporary storage удаляется fixture. Синтетические PNG/JPEG из `AttachmentImages`; реальные фотографии/данные пользователей не используются.

Первый targeted run выявил ошибку ожидания самого нового теста: 4 events вместо 6. Существующий Domain дополнительно выпускает MessagePosted для каждого замечания; ожидание исправлено на 2 SolutionSubmitted + 2 review events + 2 MessagePosted. В первом полном run новый negative test также ожидал 404 на чужую work, тогда как существующий workspace contract возвращает 403 Forbidden (фотографии — 404). Ожидание исправлено по исходному query access contract; отказ не ослаблен. Runtime ради PASS не изменялся. Первоначальный FAIL сохранён в evidence; окончательный результат — в final API regression.

### Negative-access и ограничения доказательства

| Случай | Evidence / ожидаемое поведение | Область |
|---|---|---|
| Чужая work и все 3 фото обеих попыток | Новый journey: независимые чужие Student/Teacher получают 403 на work и 404 на фото после acceptance; положительные byte checks владельца/teacher | Legacy HTTP |
| Student открывает teacher path/принимает работу | Новый journey: 403 на GET teacher alias и POST accept | Legacy HTTP |
| Wrong course/group/teacher assignment, другой ученик, counts | AttachmentApiTests.Submitted_content_honors_teacher_group_access; ContractQueryTests.Shared_course_never_exposes_other_students_rows_or_other_teacher_groups; queue filters до pagination | Legacy HTTP |
| Forged/missing file parent, foreign upload, reuse claim | AttachmentApiTests.Foreign_attachment_and_failed_submit_do_not_claim_any_pages, Metadata_without_real_stored_resource_is_not_eligible; AttachmentConsistencyTests + S3 parent tests | HTTP + отдельные S3 components |
| Membership revoke/move, cross-org и unknown context | ScopedAuthorityTests/ScopedAuthorityAdditionalTests/ScopedAuthoritySecurityTests; ComponentJwtTests.Component_policy_uses_parent_and_current_processing_never_current_grants; ProcessingWriterCompatibilityTests | S3/current-processing components; НЕ integrated HTTP |
| Нет processing/subject evidence, unsafe restriction narrowing | Cross_org_context_subject_binding_invalid_real_mode_and_missing_evidence_deny; existing component restriction hardening tests | Component evidence, HTTP BLOCKED |
| Старая версия/двойная отправка, rollback/immutable history | Existing CommandApiTests, ContractWorkflowTests.Concurrent_messages_return_one_conflict_without_partial_history, AttachmentConsistencyTests | Legacy API/PG; unknown durable outcome ещё не закрыт |
| Фото замечания недоступно | Новый UI case: 404 показывает безопасную ошибку, текст сохраняется, img отсутствует; никаких новых POST | Mocked UI, не server security proof |
| Expiry/retry/duplicate UI | Existing problem/teacherReview/attachments/transport tests: 401, 409/500, запрет replay, повтор preview, bearer transport, URL cleanup, inert Markdown | jsdom; real browser session/CSRF integration NOT RUN |

## Воспроизведение

Запускаемая защищённая интерактивная демонстрация не предоставляется: auth/current-processing HTTP integration BLOCKED. Ниже **автоматический synthetic API test**, а не инструкция обойти security gates. Production API/LocalDemo запускать для этого отчёта не требуется.

Предпосылки: канонический checkout, .NET 10 SDK, доступный Docker, образ `postgres:17.6-alpine`, восстановленные NuGet dependencies; для frontend Node 24, pnpm и установленные packages.

```sh
cd /home/tea/github/lms
/tmp/b07c2g/sdk/dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~Photo_revision_journey --logger trx --results-directory /tmp/mvp-journey/replay
```

Путь SDK соответствует текущей машине; на другой использовать установленный `dotnet` 10. Test сам создаёт synthetic actors/content и uploads, проходит revision/accept, перечитывает данные, проверяет отказы, удаляет test container/storage. Он не создаёт доступный браузеру endpoint или пользовательский login.

## Следующие функциональные задачи

1. Принять узкий интеграционный контракт isolated synthetic browser host, который действительно использует S2/S3, current ProcessingRestrictions и допустимый write protocol; не переносить legacy test auth в production host. Разрешение synthetic slice не снимает CLOCK-DEPLOY/G02/S4 gates.
2. После разрешения зависимостей соединить existing React → real protected API → PG/files в browser E2E, пройти курс → фотопопытка → замечание → фотоисправление → acceptance; повторить после process restart и с cross-org/processing negatives.
3. Закрыть teacher navigation для незапущенных и terminal assignments, а также список «Мои курсы»; текущие direct course/review links не заменяют полную навигацию. Не определять роль по отсутствию canSubmit.
4. На целевых desktop/tablet/mobile проверить реальные синтетические А4 2480×3508 (полный лист, zoom/pan/fullscreen/portrait), крупные JPEG/PNG, upload interruption, session expiry и recovery. В этом slice не заявлять физические устройства PASS.
5. Отдельно решить безопасное восстановление draft после ухода/expiry, повторное открытие без потери локального текста, protected reference/rubric (в существующем DTO отсутствуют). Immutable уже отправленную историю не менять.

## Guardrails §7 / self-review

| Проверка | Статус |
|---|---|
| Actor/use case/invariants/states | covered — контракт и transition matrix выше |
| S2/S3/PRS1, закрытые gates | covered в изменении; полная integration blocked, обход не добавлен |
| Org/course/group/ownership на сервере | legacy role/course/owner covered tests; S3 component tests отдельно; HTTP org boundary blocked |
| Subjects/embedded sources/current restrictions | blocked на HTTP; component regression сохранена; новых субъектов/processing writers нет |
| Counts/aggregates до раскрытия данных | covered existing query tests; изменения UI не фильтруют API rows/total |
| Attachments/history, client IDOR | covered existing protected endpoint + exact review/message linkage; новых URL/parents/client authority нет |
| Transaction/CAS/retry/unknown outcome | existing semantics описаны; новые writes N/A; integration protocol blocked |
| Validation/limits/safe errors | covered existing decoders/Markdown/preview, новый 404 case; limits не изменены |
| Unit/integration/negative/E2E | exact results выше; browser/physical A4 NOT RUN |
| Schema/API/security/unrelated edits | только перечисленные files; contracts/runtime unchanged |
| Real data/secrets/logging/readiness | synthetic only; никакого production verifier/credentials; acceptance не заявлена |
| Остаточные gaps | перечислены в transition matrix и next tasks |

## Preservation и owner gate

Исходная ветка `codex/l1-s4-jwt-api-authorization`, HEAD `492e30aef65bbddd24f107903bfaa54a563abe39` сохранены; checkout не переключался на main по требованию сохранить branch. SHA-256 index совпадает. Из 580 baseline файлов изменены только 4 заранее перечисленных frontend source/test файла; остальные **576/576 сохранены побайтово**, unexpected changes 0. Новые API test/report/evidence — отдельные добавления. `.obsidian/` и ignored build artifacts исключены из этой проверки. Evidence: [preservation.json](evidence/MVP-STUDENT-TEACHER-JOURNEY/preservation.json). Commit, merge, push, deploy, production cutover не выполнялись.

Остановлено на owner acceptance этого результата. Не объявлены UI COMPLETE, E2E ACCEPTED, SYNTHETIC MVP ACCEPTED, G02/S4 acceptance или PILOT AUTHORIZED. Полный сценарий остаётся частично заблокированным; зелёные unit/integration tests этот статус не меняют.
