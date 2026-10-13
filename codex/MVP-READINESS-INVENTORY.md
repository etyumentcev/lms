# Sapientum LMS — MVP readiness inventory

> **2026-10-12 supersession.** [Owner decision](TEACHER-REVOCATION-OWNER-DECISION.md) cancels the physical-completion proof gate for Teacher Revocation and authorizes continuation of HTTP/React migration. Historical audit/test counts below are retained. X14 is now PARTIAL, not BLOCKED; logical deadline/security and all non-timing gates remain open.

Дата: 2026-10-10. **AUDIT ONLY**. Канонический checkout `/home/tea/github/lms`, HEAD `492e30aef65bbddd24f107903bfaa54a563abe39`, фактическая ветка `codex/l1-s4-jwt-api-authorization`. Dirty checkout сохранён; правило main не применялось переключением ветки. Снимок включает существующие untracked S4/PRS1 документы и компоненты, а не только HEAD.

## Вывод и метод подсчёта

**Нет: администратор не может самостоятельно подготовить среду, преподаватель опубликовать курс, ученик активироваться и пройти полный путь до Accepted без технической подготовки.** Учебное ядро значительно продвинуто: имеются UI, HTTP, PostgreSQL и регрессия Submit → Review → Revise → Accept. Seed, dev bearer и прямые fixtures не считаются завершённым MVP.

Статус относится к указанной функции в **целевом пользовательском процессе**. `VERIFIED` требует достаточного подтверждения именно этого scope; зелёные unit/API tests не повышают весь browser journey в VERIFIED. `IMPLEMENTED_NOT_VERIFIED` — функциональный UI/API фрагмент найден, но целевая интеграция/устройство не подтверждены. `PARTIAL` — конкретная связка отсутствует. `MISSING` — use case не реализован, даже если есть общие таблицы. `BLOCKED` — явный gate. Разрешение использования указано отдельно: все нынешние успешные проверки — isolated synthetic/component/legacy scope, **не** разрешение S4 HTTP или пилота.

Обязательных строк A/T/S/X: **63** (это операции по ролям, общие компоненты намеренно повторно видны в разных journeys; не процент готовности и не число уникальных задач). **VERIFIED: 0**, **IMPLEMENTED_NOT_VERIFIED: 17**, **PARTIAL: 39**, **MISSING: 7**, **BLOCKED: 0**. N01–N04 исключены из знаменателя. Ноль VERIFIED полного целевого scope не означает отсутствие рабочего кода; результаты компонентных проверок приведены ниже.

## Исходные материалы и расхождения

- `AGENTS.md`, Guardrails и reconciliation прочитаны. **`codex/SAPIENTUM-MVP-READINESS-AUDIT.md` отсутствует** в рабочем дереве; его содержание не реконструировалось. Этот новый inventory не выдаётся за отсутствующий входной документ.
- Прочитаны T01–T06/E01–E06/O01/B01 и последующие решения [owner register](L03-OWNER-DECISION-REGISTER.md), [S3](../docs/architecture/L1-S3-SCOPED-AUTHORITY.md), [A06](L1-G02-A06-OWNER-DECISION.md), [I2-P](L1-S4-A4-I2-P-RESULTS.md), content/domain/use-case требования и применимые ADR. Старые результаты K/S1–S4 — историческая evidence, не новый PASS.
- Guardrails §0 называет I2 authorized; последний register разрешает **I2-P validation, не I2 implementation**. Следовать register; исходные документы не переписывать (reconciliation R01).
- Guardrails «current permission» нельзя трактовать как немедленный ordinary revoke. T04/E01 разрешают локальную JWT authority до exp; resource ownership/current restrictions остаются server-side (R02).
- Teacher authoring означает внешний Markdown + импорт + immutable publication, не CMS (R03). Доступ к эталону/критериям в новом задании обязателен; старое K-P2 с внешним workaround не принято как освобождение текущего scope (R04).
- Старый domain use-case draft говорит о двух learning roles. T02/E04 добавляют org-admin и platform/security operator; это отдельные полномочия, не Teacher с полным чтением.
- S3 B01 сохраняет историческую группу назначения; действующие HTTP queries/policy связывают teacher с **текущим** `AccessEnrollments.GroupId`. Это расхождение runtime и принятой целевой семантики, а не автоматическая отмена одного источника. До cutover не объявлять новую гарантию действующей.
- K checkpoints о UI COMPLETE/legacy flow не доказывают SYNTHETIC MVP ACCEPTED. Требуется повторная приёмка после интеграции.
- Упомянутые AGENTS specialist skills `model-domain`, `implement-use-case`, `implement-feature` недоступны в каталоге сессии; их выполнение не заявляется. Аудит не требует implementation workflow.

## Карта доказательств

Ссылки E используются в каждой строке: перечисленные методы/маршруты ограничивают точный scope доказательства. Отсутствие установлено по **полной** route/DI/UI карте, а не только поиску ключевого слова.

| Evidence | Конкретный источник и что он доказывает |
|---|---|
| E01 | [App.tsx](../src/Lms.Web/src/App.tsx): `App`, `DemoApp`, `Placeholder`; `/courses` placeholder, нет admin/auth/import routes. [browserClient.ts](../src/Lms.Web/src/api/browserClient.ts): `window.lmsAuth` только внешний adapter. |
| E02 | [Program.cs](../src/Lms.Api/Program.cs): legacy DI/JwtBearer, health, migrate-only. [QueriesController](../src/Lms.Api/QueriesController.cs), [CommandsController](../src/Lms.Api/CommandsController.cs), [AttachmentsController](../src/Lms.Api/AttachmentsController.cs): полная карта HTTP. [Compose](../docker-compose.yml): local Development+seed. |
| E03 | [PublishedContent.cs](../src/Lms.Infrastructure/PublishedContent.cs): snapshot records/`PostgresContentResolver.Resolve`; [PostgresWorkspaceQueries](../src/Lms.Infrastructure/PostgresWorkspaceQueries.cs): `StudentProblem` возвращает `Hints=[]`, `UsefulMaterials=[]`, `Course`/`Lesson` materials=[]; [ReadModels](../src/Lms.Application/ReadModels.cs): нет teacher solution/rubric. |
| E04 | [Content architecture](../docs/architecture/CONTENT-ARCHITECTURE.md), [content spec](../docs/content/COURSE-CONTENT-SPEC.md), [actors/use cases](../docs/domain/LMS-MVP-ACTORS-USE-CASES.md), [examples](../examples/course/course.md); [MarkdownContent](../src/Lms.Web/src/components/MarkdownContent.tsx): skipHtml, KaTeX trust=false, img→alt. |
| E05 | [PostgresReviewQueue](../src/Lms.Infrastructure/PostgresReviewQueue.cs): `ReviewQueue`, SQL EXISTS до Count/Skip/Take, null display names; [ReviewQueuePage](../src/Lms.Web/src/features/review-queue/ReviewQueuePage.tsx). |
| E06 | [PostgresIdentityMapping](../src/Lms.Infrastructure/PostgresIdentityMapping.cs); [PostgresIdentitySessions](../src/Lms.Infrastructure/PostgresIdentitySessions.cs): Authenticate/Rotate/Read/Revoke/RevokeAll; [IdentitySessionTests](../tests/Lms.Infrastructure.Tests/IdentitySessionTests.cs). Это internal components, не auth endpoints. |
| E07 | [SchemaFoundations](../src/Lms.Infrastructure/SchemaFoundations.cs): Organization, CourseGroup, ProvisioningRequest, AccessChallenge, ChallengeReceipt, RecoveryRequest, AccountSecurityState; [M1](../src/Lms.Infrastructure/Migrations/20261009094756_SchemaFoundationsM1.cs). Schema не issuer/consume use case. |
| E08 | [ScopedAuthority](../src/Lms.Application/ScopedAuthority.cs): trusted `AuthorityCommand`, ScopePolicy; [administration](../src/Lms.Infrastructure/PostgresScopedAuthorityAdministration.cs): Change; [reader](../src/Lms.Infrastructure/PostgresScopedAuthorityReader.cs); [S3 migration](../src/Lms.Infrastructure/Migrations/20261009153218_ScopedAuthorityS3.cs): s3_change_authority/capture/history/privileges. |
| E09 | [ComponentAuthorization](../src/Lms.Application/ComponentAuthorization.cs): Allows, files deny; [processing gate](../src/Lms.Infrastructure/PostgresCurrentProcessingGate.cs): conservative Allows, own read transaction; [issuer](../src/Lms.Infrastructure/PostgresComponentTokenIssuer.cs), [JWT](../src/Lms.Infrastructure/ComponentJwt.cs). Нет public DI в Program. |
| E10 | [AttachmentService](../src/Lms.Infrastructure/AttachmentService.cs): Upload/Open/Delete; [storage](../src/Lms.Infrastructure/LocalFileAttachmentStorage.cs); [validator](../src/Lms.Infrastructure/ImageUploadValidator.cs); [attachment migration](../src/Lms.Infrastructure/Migrations/20261008013249_AttachmentStorage.cs). |
| E11 | [TeacherReviewPage](../src/Lms.Web/src/features/teacher-review/TeacherReviewPage.tsx), [SubmissionViewer](../src/Lms.Web/src/features/teacher-review/SubmissionViewer.tsx): fullscreen fallback; [AnnotationPhoto](../src/Lms.Web/src/components/annotations/AnnotationPhoto.tsx): pointer/pen/undo/redo; [ReviewContext](../src/Lms.Web/src/features/teacher-review/ReviewContext.tsx); [Annotations](../src/Lms.Domain/Annotations.cs). |
| E12 | [ProblemWorkService](../src/Lms.Application/ProblemWorkService.cs), [ProblemWork](../src/Lms.Domain/ProblemWork.cs), [PostgresWorkStore](../src/Lms.Infrastructure/PostgresWorkStore.cs): Save/CAS/history; [ContractWorkflowTests](../tests/Lms.Api.Tests/ContractWorkflowTests.cs), [CommandApiTests](../tests/Lms.Api.Tests/CommandApiTests.cs), [AnnotationApiTests](../tests/Lms.Api.Tests/AnnotationApiTests.cs), [ContractQueryTests](../tests/Lms.Api.Tests/ContractQueryTests.cs). |
| E13 | [SolutionForm](../src/Lms.Web/src/features/problem-workspace/SolutionForm.tsx), [HistoryTimeline](../src/Lms.Web/src/features/problem-workspace/HistoryTimeline.tsx), [AttachmentUploader](../src/Lms.Web/src/components/attachments/AttachmentUploader.tsx), [API client](../src/Lms.Web/src/api/client.ts); frontend tests `src/Lms.Web/src/test/{problem,history,attachments,annotations,teacherReview,api,course,lesson,reviewQueue}.test.*`. |
| E14 | [device protocol](K-DEVICE-TEST-PROTOCOL.md), [ADR-L07](../docs/adr/ADR-L07-device-browser-scope.md), [ADR-L05](../docs/adr/ADR-L05-backup-restore.md), [release readiness](K-RELEASE-READINESS.md). Исторические browser smoke не physical acceptance. |
| E15 | [ADR-L06](../docs/adr/ADR-L06-resource-protection.md), [README](../README.md), [PostgresFixture](../tests/Lms.Infrastructure.Tests/PostgresFixture.cs): disposable DB backup/restore; [SchemaFoundationTests](../tests/Lms.Infrastructure.Tests/SchemaFoundationTests.cs); health не storage/restore readiness. |
| E16 | [ClaimsActorMapper](../src/Lms.Api/ActorMapper.cs): authenticated sub+role; [legacy policy](../src/Lms.Infrastructure/PostgresAccessPolicy.cs): AccessEnrollments/TeacherGrants; [AccessFacts](../src/Lms.Infrastructure/AccessFacts.cs). Это не S3/S4 authority. |
| E17 | [register](L03-OWNER-DECISION-REGISTER.md), [A06 decision](L1-G02-A06-OWNER-DECISION.md), [I1 result](L1-S4-A4-I1-RESULTS.md), [I2-P result](L1-S4-A4-I2-P-RESULTS.md), [reconciliation](MVP-GUARDRAILS-RECONCILIATION.md). |

## Инвентаризация функций

| ID | User Journey | Function | Status | Evidence | Gap | Priority |
|---|---|---|---|---|---|---|
| A01 | Администратор | Создать Organization | PARTIAL | E02, E07 | Есть schema; нет operator bootstrap/use case/UI. | P0 |
| A02 | Администратор | Создать первого org-admin | MISSING | E01, E02, E07 | Нет доверенного platform-operator процесса; org-admin не образовательный superuser. | P0 |
| A03 | Администратор | Войти администратором | PARTIAL | E01, E06 | S2 session component есть; admin HTTP и browser login отсутствуют; интеграция BLOCKED. | P0 |
| A04 | Администратор | Создать ученика/преподавателя из roster | PARTIAL | E06, E07 | Identity mapping есть; нет admin формы/команды provisioning с проверкой полномочий. | P0 |
| A05 | Администратор | Выдать/использовать activation code | PARTIAL | E07 | AccessChallenge/Receipt — schema; issuance/consume/доставка/UI не реализованы. | P0 |
| A06 | Администратор | Восстановить доступ | PARTIAL | E06, E07 | RecoveryRequest/schema и session revoke есть; подтверждение, codes, operator UI отсутствуют. | P0 |
| A07 | Администратор | Создать курс и группы | PARTIAL | E07, E08 | CourseRegistry/CourseGroups есть; публичного административного lifecycle нет. | P0 |
| A08 | Администратор | Зачислить в группу/на курс | PARTIAL | E08 | S3 Change и SQL protocol есть; доверенного admin adapter/UI нет. | P0 |
| A09 | Администратор | Назначить преподавателя | PARTIAL | E08 | Внутренний scoped writer есть; самостоятельный UI путь отсутствует. | P0 |
| A10 | Администратор | Изменить/отозвать права | PARTIAL | E08, E09 | Generation/revoke component есть; UI и новое HTTP enforcement не подключены. | P0 |
| A11 | Администратор | Видеть пользователей/назначения | MISSING | E01, E02 | Нет admin roster/status/catalog queries/pages. | P0 |
| A12 | Администратор | Подтвердить опасные операции и видеть audit | PARTIAL | E06, E08 | Atomic audit существует в компонентах; confirmation/recovery approval/audit UI отсутствуют. | P0 |
| T01 | Преподаватель | Вход и выбор Organization | PARTIAL | E01, E06, E09 | Компоненты есть; HTTP/browser session и org switch отсутствуют; BLOCKED integration. | P0 |
| T02 | Преподаватель | Список назначенных курсов/групп | MISSING | E01, E02 | /courses — Placeholder; только known-ID workspace. | P0 |
| T03 | Преподаватель | Создать курс/выбрать подготовленную программу | PARTIAL | E03, E04 | Snapshots/readers есть; нет регистрации и публикации через продукт. | P0 |
| T04 | Преподаватель | Импорт Markdown | MISSING | E03, E04 | Нет importer/validator/compiler application path. | P0 |
| T05 | Преподаватель | Создать/изменить занятие | PARTIAL | E03, E04 | Внешние Markdown допустимы; нет повторной публикации новой версии. CMS не требуется. | P0 |
| T06 | Преподаватель | Задачи, условия, подсказки, эталоны | PARTIAL | E03, E04 | Условие читается; hints всегда []; teacher artifact/rubric отсутствует. | P0 |
| T07 | Преподаватель | Опубликовать/открыть и назначить материал | PARTIAL | E02, E03, E08 | LessonAccess и ProblemAssignment есть; commands/UI отсутствуют. | P0 |
| T08 | Преподаватель | Список учеников | MISSING | E01, E02, E05 | Нет roster; StudentDisplayName/GroupTitle проецируются null. | P0 |
| T09 | Преподаватель | Прогресс ученика и группы | PARTIAL | E03, E05 | Есть вычисляемый workspace progress/queue summary; нет roster status matrix. | P0 |
| T10 | Преподаватель | Очередь проверки | IMPLEMENTED_NOT_VERIFIED | E05, E12 | UI/API/SQL filters есть; принятый org-aware runtime и целевой browser E2E не подтверждены. | P0 |
| T11 | Преподаватель | Открыть оригинал фотографии | PARTIAL | E10, E12 | Работает legacy parent access; S4 protected storage/G02 не интегрированы. | P0 |
| T12 | Преподаватель | Проверить несколько страниц | IMPLEMENTED_NOT_VERIFIED | E10, E11, E12 | Viewer/переключение есть; целевой authenticated E2E NOT RUN. | P0 |
| T13 | Преподаватель | Замечания поверх фото | IMPLEMENTED_NOT_VERIFIED | E11, E12 | Canvas, immutable annotations и API тесты есть; финальный device/browser E2E NOT RUN. | P0 |
| T14 | Преподаватель | Предыдущие попытки и замечания | IMPLEMENTED_NOT_VERIFIED | E11, E12 | ReviewContext/history linkage есть; новая auth-интеграция не проверена. | P0 |
| T15 | Преподаватель | Эталон и критерии точной версии | MISSING | E03, E04 | Нет teacher-only source/DTO/UI; внешний workaround не принят как текущий MVP. | P0 |
| T16 | Преподаватель | Вернуть на доработку | IMPLEMENTED_NOT_VERIFIED | E11, E12 | ReviewForm→RequestRevision→Save существует; целевой E2E пока blocked. | P0 |
| T17 | Преподаватель | Принять работу | IMPLEMENTED_NOT_VERIFIED | E11, E12 | Accept/version/history/progress проверены компонентно и HTTP harness; target E2E blocked. | P0 |
| T18 | Преподаватель | История собственных проверок | PARTIAL | E05, E11 | История одной известной работы есть; нет списка ранее проверенных работ после ухода из queue. | P0 |
| T19 | Преподаватель | Полноэкранная проверка на планшете | IMPLEMENTED_NOT_VERIFIED | E11, E14 | Реальный fullscreen/CSS fallback/pen код, не макет; physical tablet acceptance отсутствует. | P0 |
| S01 | Ученик | Получить credentials/activation | PARTIAL | E07 | Schema/mapping есть; самостоятельный выдача→активация путь отсутствует. | P0 |
| S02 | Ученик | Активировать и войти | PARTIAL | E01, E06, E07 | Нет activation/session HTTP/UI; dev token не замена. | P0 |
| S03 | Ученик | Восстановить доступ | PARTIAL | E06, E07 | Нет recovery consume/approve UI; shared identity требует независимого operator. | P0 |
| S04 | Ученик | Мои назначенные курсы | MISSING | E01, E02 | /courses placeholder, каталог API отсутствует. | P0 |
| S05 | Ученик | Открыть урок и задачу | PARTIAL | E03, E12 | Known-ID navigation есть; полного lesson body/assets и входного каталога нет. | P0 |
| S06 | Ученик | Markdown/LaTeX | PARTIAL | E04, E13 | Text/math renderer работает; img заменён alt, локальные assets/attachment URI не разрешаются. | P0 |
| S07 | Ученик | Подсказки | PARTIAL | E03, E04 | UI tab/DTO есть; backend всегда возвращает пустой массив. | P0 |
| S08 | Ученик | Камера/выбор нескольких фото | IMPLEMENTED_NOT_VERIFIED | E10, E13 | JPEG/PNG multiple input есть; реальная камера/галерея целевого устройства NOT RUN. | P0 |
| S09 | Ученик | Preview и порядок страниц | IMPLEMENTED_NOT_VERIFIED | E10, E13 | Порядок сохраняется в submit/DB; UI unit и API integration PASS, target browser NOT RUN. | P0 |
| S10 | Ученик | Отправить решение | IMPLEMENTED_NOT_VERIFIED | E12, E13 | Submit UI/API/domain есть; activation→login→assigned course не замкнут. | P0 |
| S11 | Ученик | Статус проверки | IMPLEMENTED_NOT_VERIFIED | E03, E12 | Canonical status/read/refetch есть; target E2E NOT RUN. | P0 |
| S12 | Ученик | Получить замечания и overlays | IMPLEMENTED_NOT_VERIFIED | E11, E12, E13 | History/feedback отображаются; target E2E NOT RUN. | P0 |
| S13 | Ученик | Повторная Submission | IMPLEMENTED_NOT_VERIFIED | E12, E13 | NeedsRevision→Submitted создаёт новую попытку; target E2E NOT RUN. | P0 |
| S14 | Ученик | Все попытки/проверки | IMPLEMENTED_NOT_VERIFIED | E12, E13 | Immutable history существует; новый org/session boundary не интегрирован. | P0 |
| S15 | Ученик | Получить Accepted | IMPLEMENTED_NOT_VERIFIED | E12, E13 | Terminal status/read-only UI есть; полный пользовательский путь не подтверждён. | P0 |
| S16 | Ученик | Прогресс курса | PARTIAL | E03, E12 | Вычисления и course view есть; каталог и новый eligibility predicate отсутствуют. | P0 |
| S17 | Ученик | Выход/expiry/offline/reconnect | PARTIAL | E01, E06, E13 | 401 callbacks и ambiguous-send guard есть; session lifecycle/cache purge/восстановление не замкнуты. | P0 |
| X01 | Продукт | Lifecycle курса: новая версия/закрытие/архив | PARTIAL | E03, E04 | Immutable snapshot есть; publication/withdraw workflow отсутствует. Архивирование без удаления истории. | P0 |
| X02 | Продукт | Validation/error/empty states | IMPLEMENTED_NOT_VERIFIED | E12, E13 | Существующие экраны покрыты; новых admin/content/session сценариев нет. | P1 |
| X03 | Продукт | Ограничения uploads и восстановление | PARTIAL | E10, E13, E15 | Single-file 10 MiB/JPEG/PNG/60M pixels есть; quota/orphan/finite expiry отсутствуют. | P1 |
| X04 | Продукт | Конкуренция и повтор команд | PARTIAL | E08, E12, E13 | CAS work и S3 есть; message retry не идемпотентен, unknown outcome требует сверки. | P0 |
| X05 | Продукт | Адаптивность/accessibility/устройства | IMPLEMENTED_NOT_VERIFIED | E11, E13, E14 | Responsive CSS/focus есть; physical browsers и auth UX не приняты. | P1 |
| X06 | Продукт | Поиск/фильтры/навигация | PARTIAL | E01, E05, E13 | Queue search/page есть; каталоги, roster и возврат к проверенным работам неполны. | P0 |
| X07 | Эксплуатация | Диагностика | PARTIAL | E02, E15 | DB health/problem details есть; storage capacity/alerts/secret-free ops qualification не завершены. | P1 |
| X08 | Эксплуатация | Backup+restore DB и originals | PARTIAL | E14, E15 | Есть DB rehearsal tests и ADR; целый restore set включая originals/keys/revoke не доказан. | P1 |
| X09 | Эксплуатация | Первичное развёртывание | PARTIAL | E02, E15 | Local Compose migrate+seed существует; самостоятельный bootstrap и qualified ingress отсутствуют. | P0 |
| X10 | Безопасность | Org/course/group/owner из server model | PARTIAL | E08, E09, E16 | S3 resolves immutable scopes; HTTP пока использует legacy Actor/AccessFacts. | P0 |
| X11 | Безопасность | Current restrictions до reads/counts/writes | PARTIAL | E05, E09, E16 | Conservative component есть; не подключён в runtime queries/commands/files. | P0 |
| X12 | Безопасность | Файлы по parent, lifecycle и private storage | PARTIAL | E08, E10 | Legacy checks/claim triggers есть; новые org/context scopes и bounded I/O не интегрированы. | P0 |
| X13 | Безопасность | Atomic authorization/mutation/history | PARTIAL | E08, E12, E16 | История/CAS защищены; legacy access checks раньше Save transaction, новая processing serialization отсутствует. | P0 |
| X14 | Безопасность | Полная S4 HTTP интеграция | PARTIAL | E09, E17 | Physical-only gate снят и migration разрешена; secure Program/React composition, API regression closure and target E2E remain incomplete. | P0 |
| X15 | Обсуждение | Ответ ученика/учителя без новой попытки | PARTIAL | E12, E13 | PostMessage API/domain есть, compose/reply UI отсутствует; предусмотрено UC-S06/UC-T07. | P1 |
| N01 | Вне обязательного scope | Встроенный rich-text CMS | NOT_APPLICABLE | E04 | Контент редактируется внешними Markdown; это не отказ от importer/publication. | P2 |
| N02 | Вне обязательного scope | PRS1 exact matching | BLOCKED | E17 | I1 принят, I2-P findings; exact OFF, I2–I5 parked. Не обязательный MVP prerequisite. | P2 |
| N03 | Вне обязательного scope | Пилот с реальными детскими данными | BLOCKED | E14, E17 | Security/legal/operations gates; не входит в synthetic acceptance. | P2 |
| N04 | Вне обязательного scope | Расширенная аналитика/расписание/общий чат | NOT_APPLICABLE | E01, E04 | Placeholder не MVP blocker: достаточно курса, basic progress и problem discussion. | P2 |

## Прослеживаемость UI → API → Application → Domain → Persistence

| Процесс | Фактическая цепочка | Где обрывается |
|---|---|---|
| Bootstrap/provisioning | UI отсутствует → API отсутствует → PostgresIdentityMapping/internal S3 → membership/generation invariants → Identity/Actors/Scopes/аудит | Нет trusted platform/org-admin capability adapter и пользовательского workflow. Organization не создаётся существующим controller. |
| Activation/recovery | UI/API отсутствуют → нет challenge orchestration → нормативный lifecycle Created/Consumed/Expired/Revoked/Locked → M1 challenge/recovery tables | Таблица не доказывает генерацию, rate limits, atomic consume или independent approval. |
| Session | browserClient ждёт lmsAuth → auth routes отсутствуют → S2 sessions + S4 component issuer/validator → session/expiry/O01 contracts → PostgreSQL sessions/verifiers/audit | Program не подключает эти компоненты; legacy signed dev token не соответствует целевому пути входа. |
| Контент | Course/Lesson/Problem UI → QueriesController → WorkspaceQueries/ContentResolver → ProblemContentReference immutable version → ContentVersions/AccessLessons/Assignments | Нет import/compile/publication/open/assign use cases; lesson body/hints/assets/reference не сохраняются полной моделью. |
| Учебная работа | SolutionForm/ReviewForm → CommandsController → ProblemWorkService → ProblemWork state machine/Review/Submission/Annotations → WorkStore.Save/CAS/events/DB triggers | Functional core связан, но вход/назначение и новый scope/processing enforcement не замкнуты. |
| Фото | AttachmentUploader/Viewer/Blob → uploads/content/delete → AttachmentService + legacy IAccessPolicy → parent/claim invariants в PostgreSQL → private local storage | Новый AttachmentScopes/context admission отсутствует; upload без resource context; physical completion proof no longer required, but logical deadline/no-late-success migration remains. |
| Queue/progress/history | React pages → QueriesController → WorkspaceQueries/ReviewQueue → derived statuses → PostgreSQL snapshot/filter/count | Legacy filters реальны; они не доказывают принятую org/generation/processing модель. История одной работы не каталог своих проверок. |

## Security и архитектурный долг

Это review исходников и существующих negative tests, не penetration test и не квалификация публичного deployment. Подтверждённого нового критического exploit в рамках аудита не установлено; опасные live probes не выполнялись. Классы ниже не смешивают отсутствие интеграции с гипотетической атакой.

| ID / класс | Доказательство и влияние | Минимальное решение / проверка |
|---|---|---|
| SEC01 — подтверждённый integration gap, P0 | E02/E16: HTTP Actor = sub+role, legacy AccessFacts; нет org-scoped JWT/S3 ownership/current gate в DI. Подписанность bearer проверяется; это **не** доказательство принятия произвольных client claims. Но обещание S2/S3 для runtime неверно. | B02/B11: один approved boundary; отрицательные org/course/group/generation тесты на каждом endpoint, не отдельная вторая role система. До cutover legacy only. |
| SEC02 — подтверждённое расхождение B01, P0 | E05/E16: teacher joins current Enrollment.GroupId. E08: AssignmentScopes сохраняет original group. Перемещение ученика может менять legacy видимость старой работы, чего B01 не разрешает. Это code-level mismatch; live exploit не запускался. | B06/B11: reads/files/counts по immutable assignment affiliation; move тест с прошлой и новой задачами и двумя teachers. |
| SEC03 — подтверждённый разрыв транзакционной границы, P0 | E12: EnsureCanReview/Submit выполняется до WorkStore.Save transaction; work CAS не сериализует изменение access facts/context. E09 gate сам открывает read transaction и не является write permission receipt. | B11: проверка workflow/current restrictions и mutation в одном принятом protocol; считать обычную JWT authority по T04, не изобретать immediate revoke. Existing source доказывает структуру, конкретный race при новых writers ещё NOT PROVEN. |
| SEC04 — подтверждённый отсутствующий runtime processing gate, P0 | E09 conservative gate сохранён, но E02/E03/E05/E10 его не вызывают. Legacy queue фильтруется до counts, однако по старой eligibility. | B11/B12: server ownership + current context/restrictions до materialization/count/page; DB failure deny. Exact parser не trusted provenance; gate OFF. |
| SEC05 — частично доказанная file protection, P0 | E10: attached file следует parent даже для uploader; opaque storage key, nosniff/no-store, no public static root. Upload допускает pending без course context, ComponentAuthorization явно запрещает attachments; новое org-scoped file admission отсутствует. | B12: upload/download/delete/preview по accepted parent/context; нельзя связывать чужую или уже attached страницу, Submission XOR Message. Positive/negative AttachmentApiTests сейчас доказывают legacy scope. |
| SEC06 — принятый residual physical risk; logical checks remain, P0 regression | FIFO/PG/edge evidence correctly shows no hard physical bound; the owner explicitly accepts this for Teacher Revocation. It must not be relabelled PASS or claimed as a guarantee. | Test immutable deadline, cancellation request, no late success, replay/unknown outcome and fresh issue/refresh. No supervisor/fencing work solely for physical termination. |
| SEC07 — подтверждённая ограниченность retry, P0/P1 | Submit/Review защищены work version, first submit unique; PostMessage без client command id, повтор после уже committed ответа может добавить ещё Message. Upload retry создаёт новый pending ID. UI Submit честно требует refresh при неизвестном результате. | B15/B17: reconcile read, stable command semantics там, где replay нужен; fault/drop-ACK tests, без auto-replay мутаций. Не называть CAS универсальной идемпотентностью. |
| SEC08 — подтверждённый resource debt, P1 | E10: MemoryStream+ToArray; ExpiresAt=MaxValue; нет aggregate quota/sweeper. Claim trigger проверяет owner/eligibility/parent, но не expiry; при будущих finite TTL раннего policy check недостаточно (ADR-L06). | B19: ограничить ресурсы и сериализовать cleanup/claim; expiry recheck обязателен **при введении TTL**. Текущий finite-TTL exploit не утверждается. |
| SEC09 — защищённая history, ограничение доказательства | Domain immutable entities, append writes, attachment claim locks/triggers; tests проверяют rollback/history/foreign IDs. S3 limited-role tests запрещают DML. Local Compose postgres principal — development owner, не доказанный least-privilege production role. | B11/B18: deployment ACL review, одно направление writers, no public privileged DML. Raw parameterized SQL CAS/trigger в Infrastructure не сам по себе bypass; seed/test DML нельзя сделать admin workflow. |
| SEC10 — будущая утечка материалов/PII, риск, не текущая уязвимость | Teacher solution ещё нет, names=null. Добавление reference в shared ContentJson или client-supplied displayName сделало бы новое нарушение. | B09/B10: отдельный teacher artifact exact version, scope-minimal trusted roster; student payload/cache не содержит solution; новые поля вне принятого scope требуют owner review. |
| SEC11 — открытые findings candidate schema, не production exploit | I2-P D01–D05: inherited grants, missing trusted writer, conditional pairing, transition provenance, TRUNCATE. Это isolated candidate validation, не deployed PRS1. | B23 parked: не закрывать findings parser tests; exact OFF. |

## Executable E2E-план

**Все E2E-01–09: целевой run NOT RUN / BLOCKED**, не FAIL приложения по неисполненному сценарию. Ниже воспроизводимые ручные сценарии и точки будущей автоматизации. Сейчас запускать можно существующие harness tests (§проверки); отсутствующие endpoint names не выдуманы. Для каждого шага фиксировать UI action, request/response без токенов, ID/version/status, отсутствие чужих данных и ожидаемую persistent state через read-only assertions. Прямой SQL для подготовки целевого journey запрещён; старые fixtures допустимы только в component regression.

Общая будущая fixture F: новая disposable SyntheticOnly среда с явно разрешённым bootstrap platform operator; org A/B, два org-admin; один shared actor A+B; по два ученика и два преподавателя A, чужие actor/teacher B; группы G1/G2 A и GB B; course CA/CB; v1/v2 CA, два lessons, задача P1 с hint/reference/rubric и task P2; две разные PNG/JPEG A4 с контрольными hashes. Все имена псевдонимные. Создавать accounts/scopes/course через UI/разрешённый product bootstrap, не seed DML. Для отрицательных проверок удерживать идентификаторы чужих fixtures у test runner, не выдавая их UI пользователя. Auth access/refresh secrets не писать в evidence. Clock/fault controls допустимы только в специально разрешённом test adapter.

### E2E-01 — Первый запуск организации

1. Platform operator создаёт A и первого org-admin, получает audit receipt; org-admin A активируется и входит.
2. Из roster создать teacher/student; выдать одноразовые activation codes; активировать каждого отдельным browser context.
3. Создать CA/G1; явно зачислить student в course/group, назначить teacher; проверить roster и состояния. Account creation само не зачисляет.
4. Повторить конфликтный request со старой version: нет второй membership/history/audit effect; admin A не создаёт/меняет B; platform operator не читает student work.

Есть: M1/S2/S3 internal foundation. Нет: bootstrap/admin capability/pages/API, codes. Tests: IdentitySessionTests mapping/no implicit grants, ScopedAuthorityTests, SchemaFoundationTests. Fixture: пустая среда + operator, затем F через UI. **Точная остановка: шаг 1 — отсутствует product bootstrap.** Минимум B03/B04/B06 + B02/B11, recovery отдельно B05. HTTP зависит от B01; full plan не обходится dev token.

### E2E-02 — Подготовка курса

1. Teacher с CA/G1 импортирует `examples/course` с добавленными в future fixture hint, lesson body, local image и отдельным solution/rubric; видит file/line diagnostics.
2. Invalid duplicate ID/missing asset/unsafe path пакет не публикуется. Исправить и выпустить v1; открыть lesson для G1, выдать exact-version assignments.
3. Внешне изменить Markdown → импорт v2. Старая assignment продолжает v1; новая получает v2. Student не получает draft/teacher solution ни в JSON, ни assets/cache.

Есть: immutable ContentVersions, resolver, exact reference tests `Publication_B_never_changes_previously_read_assignment_A_or_teacher_work`. Нет: import/compiler/open/assign и полный material model. Fixture: E2E-01/F + два пакета. **Остановка: шаг 1 — нет import action.** Минимум B07/B08/B09; dependency B06/B11/B12. No CMS required.

### E2E-03 — Ученическая работа

1. Student активируется/входит → «Мои курсы» → CA → lesson → P1, читает formula/hint.
2. Выбрать две A4 фотографии, дождаться protected preview, поменять порядок; отправить; два быстрых клика не создают вторую попытку.
3. После GET/reload статус Submitted, attempt=1, original hashes и page order совпадают; у teacher одна queue row, у чужого student none.

Есть: SolutionForm/upload/Submit/domain/store/queue. Tests: ContractWorkflowTests, AttachmentApiTests `Successful_submission_claims_all_pages_in_request_order_and_history_remains_readable`, problem/attachments frontend. Исторический release-smoke использует dev token. Fixture: F с опубликованной assignment, синтетические файлы. **Остановка: activation/login/catalog раньше загрузки.** B02/B04/B10/B11/B12 + E2E-02. Upload G02 не обходить.

### E2E-04 — Проверка

1. Teacher G1 входит → queue filter CA/G1/student; total/summary/page совпадают с разрешёнными Submitted.
2. Открыть работу → обе страницы → fullscreen → strokes на каждой, undo/redo, открыть reference точной версии.
3. Добавить feedback → NeedsRevision; row исчезает из Submitted queue; student видит feedback/overlays, bytes originals неизменны.
4. Teacher G2/B и Student не могут принять работу через прямой POST или получить content ID.

Есть: E11/E12 и AnnotationApiTests; teacherReview/annotations unit; исторический annotation-tablet-smoke. Нет: session/scopes/material source/physical qualification. Fixture: E2E-03 + другой teacher. **Остановка: auth integration; при dev harness отдельная остановка — reference отсутствует.** B02/B09/B11/B12; B20 для physical acceptance.

### E2E-05 — Повторная попытка и Acceptance

1. Student открывает замечания, загружает **новые** фото, отправляет attempt=2; первая Submission/Review/annotations/hashes неизменны.
2. Teacher открывает очередь, сверяет первую/вторую страницы попыток; принимает **текущую** Submission.
3. Student видит Accepted и progress; повторный Accept/Submit или review старой Submission не меняет историю; teacher находит выполненную проверку через список, а не сохранённый URL.

Есть: полный legacy HTTP state-machine test `Complete_HTTP_workflow_preserves_versions_content_history_queue_and_progress`, history/teacherReview frontend. Нет: входной full E2E и каталог completed reviews. Fixture: E2E-04. **Остановка: зависимости E2E-03/04; финальный teacher history entry point отсутствует.** B14 плюс предыдущие slices. Эта цепочка работает в harness, не объявляется target VERIFIED.

### E2E-06 — Изоляция организаций

1. Через реальные sessions войти A/B/shared actor; попытаться открыть чужие course/work/assignment URLs, teacher alias, upload parent, download/delete IDs и review/messages JSON selectors.
2. Проверить каждый Query/Command/Attachment route из E02; foreign data отсутствуют в body/count/page/summary/filter options, не только скрыты в UI. Сначала positive same-scope контроль.
3. В одной org teacher G2 не видит G1; student не видит другого; shared actor switch A→B очищает cache/drafts/Blob URLs. Missing/quarantined context и active conservative restriction дают deny; DB processing outage fail closed.

Есть: ScopePolicy/ScopedAuthoritySecurity/ComponentRestrictionHardening + legacy ContractApi/ReadApi/Attachment tests. Fixture: F, отдельные contexts и synthetic current restrictions через будущую trusted administration, не untrusted JSON. **Остановка: legacy HTTP не использует Organization authority/current restriction boundary.** B11/B12/B10/B02; exact PRS1 не нужен, conservative deny остаётся.

### E2E-07 — Отзыв/перемещение доступа

1. Org-admin отзывает enrollment/teacher grant A; записать server RevocationRequestedAt/AccessValidUntil, token exp без раскрытия token. Refresh/issue учитывает текущее право; B unaffected.
2. Проверить старый JWT по **принятым delayed semantics** T04, а после expiry/cutoff — отсутствие новых неразрешённых requests; не требовать instant JWT revocation.
3. Move G1→G2: old assignment остаётся G1, new assignment G2; old teacher видит old work по допустимому grant, new teacher не наследует old work. Regrant не оживляет generation автоматически.
4. Отдельно current ProcessingRestriction overrides historical allow, проверка write race; не подменять это обычным revoke.

Есть: S3 move/revoke/regrant concurrency tests; ComponentIssuance/Hardening tests. Fixture: F + старый/новый work и controllable qualified test clock. **Остановка: нет admin revoke UI и HTTP uses current-group legacy join.** B06/B11/B12/B01. Проверять deadlines controlled clock + отдельный real timing proof; ускорение часов не доказывает G02.

### E2E-08 — Сессия и восстановление

1. Issue activation → consume once → login → refresh → reload → two-tab concurrent rotation. Один successor, другой 409 в inclusive 30s O01 window без Set-Cookie/ложного compromise.
2. Logout текущей session → повторный вход; access JWT живёт только по принятой exp семантике. Expired/malformed/wrong signature/issuer/audience/org, forged refresh, bad Origin/CSRF → deny.
3. Recovery initiate → independent operator approve shared account → одноразовый recovery → новый пароль → старые refresh sessions отозваны. Activation/recovery не взаимозаменяемы; 5 failures/лимиты T01, NAT positive control; secret once, lost response не redisclose.
4. Поздний replay predecessor отзывает только target session; другие sessions и org не смешиваются.

Есть: IdentitySessionTests O01/rotation/ACK loss, ComponentJwt/Key/Issuance. Нет: HTTP cookies/CSRF/browser coordinator/activation/recovery orchestration. Fixture: F + shared identity + независимый operator, approved test clock. **Остановка: нет activation/auth routes.** B02/B04/B05/B01. Не заменять bootstrap паролем из SQL.

### E2E-09 — Ошибки и конкуренция

1. Одновременно Submit/Review одной version: один effect, второй conflict; свежий GET показывает истину. Stale assignment/open/publish/admin version не перезаписывает данные.
2. Оборвать upload: форма объясняет ошибку, блокирует Submit до исправления; retry не присваивает чужой/удалённый ID. Delete vs claim не удаляет linked original.
3. Потерять response после committed Submit/Review/Message: UI считает исход неизвестным, сверяет состояние; нет blind replay. Незавершённый draft при logout очищается; временный network failure не выдаётся за rollback.
4. DB/storage failure и restriction/writer race: нет partial history/двойных эффектов; unknown commit не вызывает удаление потенциально committed bytes. Restart/restore проверить отдельной ops fixture.

Есть: CommandApiTests concurrent submits/reviews, ContractWorkflowTests concurrent messages, AttachmentConsistencyTests compensation/rollback, ComponentCommitAcknowledgementTests, SolutionForm/ReviewForm error tests. Нет: полного auth-aware fault journey и общего retry contract; Message повтор после успешного commit может дублироваться. Fixture: F + isolated proxy/fault adapters, только synthetic. **Остановка: auth integration; remaining message/session/recovery reconciliation отсутствует.** B15/B17/B11/B12/B18. Не запускать fault injection на действующей БД.

## Проверки, выполненные в этом аудите

Рабочий каталог команд — `/home/tea/github/lms`, frontend — его `src/Lms.Web`. Исходные файлы не менялись. Существующий backend suite использует `postgres:17.6-alpine` Testcontainers с одноразовыми БД `c1_*`/`c2_*` и temporary storage; fixtures прочитаны перед запуском. Миграции запускались только самим suite в disposable DB, **не** в действующей Compose DB. В данном задании пользователь явно разрешил existing isolated tests; это новые результаты аудита, не наследование документационного PASS.

```sh
/tmp/lms-s1-dotnet/dotnet test Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/lms-mvp-readiness-tests
# cwd: /home/tea/github/lms/src/Lms.Web
PATH=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin:$PATH pnpm test
PATH=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin:$PATH pnpm exec tsc --noEmit
```

| Проверка | Результат | Покрытие и предел |
|---|---|---|
| Domain | 43 passed, 0 failed, 0 skipped | State machine/annotation invariants. |
| Application | 272 passed, 0 failed, 0 skipped | Use cases/ScopePolicy/PRS1 parser/operation plans; parser green не exact gate. |
| Infrastructure | 176 passed, 0 failed, 0 skipped | Schema/migrations/restore rehearsals, S2/S3/S4 components, restricted role, races. FIFO regression подтверждает negative G02 evidence. |
| API | 186 passed, 0 failed, 0 skipped | Legacy HTTP + real PostgreSQL workflow/files/negative scope/count/concurrency. Test auth и seed не реальный onboarding. |
| Backend итого | **677 passed, 0 failed, 0 skipped**, exit 0 | TRX: `/tmp/lms-mvp-readiness-tests/*.trx`. |
| Frontend | **366 passed**, 12 files, 0 failed/skipped, exit 0 | Vitest/jsdom/mock API/transport, не live browser. |
| TypeScript | exit 0 | `tsc --noEmit`; bundler build отдельно NOT RUN. |
| Browser E2E / physical devices | **NOT RUN** | Сценарии выше заблокированы отсутствующими путями; historical smoke не переименован в новый run. |
| Production restore/deploy/legal/security audit | **NOT RUN** | За рамками разрешённого synthetic audit. |

Диагностика окружения: стандартный `dotnet --version` попал в `/snap/bin/dotnet`, попытался bootstrap SDK и завершился ошибкой отсутствия sudo terminal; `node --version` — command not found. Установки не выполнялись. Затем использованы уже существующие `/tmp/lms-s1-dotnet` **10.0.401** и bundled Node **v24.19.0**; pnpm wrapper сообщил **11.25.0** при packageManager 11.19.0. Lockfile/dependencies не изменялись. Это воспроизводимость инструментария, не failure tests.

`release-check.sh` **не запускался**: он поднимает фиксированный `lms-k-smoke`, выполняет `down -v`, seed и dev-token login. Эти действия не нужны для доказательства отсутствующих сценариев. Current controlled tests выше безопасно проверили существующее ядро. Исходный поиск `rg --files src tests scripts deploy` также сообщил отсутствие root `scripts`/`deploy`; реальные smoke scripts найдены в `src/Lms.Web/scripts`, это не вывод об отсутствии всех scripts.

## Guardrails §7 — checklist аудита

- Covered: actor/use case/state/invariants описаны в таблицах и E2E; изменены только три новые planning documents.
- Covered: current Teacher Revocation owner supersession recorded; physical-only gate removed without claiming a physical guarantee. PRS1, logical deadline/security and release boundaries remain.
- Blocked with evidence: новая server org/resource/subject/processing/count/file authorization и write serialization — SEC01–06, B01/B11/B12.
- Covered existing / unproven target: immutable history, CAS, validation, error states, negative tests — SEC07–09 и таблица результатов.
- Not applicable — audit не вводит schema/API/PII/permissions/новые writers, поэтому новый transaction protocol не реализуется; требования к будущим writes включены в backlog.
- Covered: synthetic data only, no secrets in report, no commit/push/merge/rebase/deploy; device/legal/pilot readiness не заявляется.

## Проверка сохранности и документов

`python3 /tmp/validate_lms_mvp_audit.py` — PASS: локальные Markdown links разрешаются, trailing whitespace отсутствует, статусы пересчитаны из 63 inventory rows, TRX counters подтверждают 677/0/0. `git diff --check` — exit 0. Первый вспомогательный validator завершился IndexError, ошибочно пытаясь считать двухколоночную mapping table backlog как семиколоночный inventory; ограничен нужным файлом и повторно успешно выполнен. Это ошибка tooling аудита, не приложения, не скрытый test failure.

SHA-256 baseline `/tmp/lms-mvp-audit-baseline.json`: **397 предсуществующих tracked/untracked файлов без изменений**, включая `.obsidian/workspace.json`, Guardrails, checkpoints, owner register и предсуществующий dirty test. HEAD и ветка сохранены, index пуст. Добавлены только три требуемых Markdown-документа; generated bin/obj/test temporary artifacts не являются правками исходного runtime. Machine-readable validation: `/tmp/lms-mvp-audit-validation.json`; временные evidence не выдаются за durable CI artifacts. Для owner review все команды, результаты и ограничения перенесены в этот документ.

См. [backlog](MVP-BACKLOG.md) и [roadmap](MVP-IMPLEMENTATION-ROADMAP.md).

**TEACHER-REVOCATION PHYSICAL GATE CANCELLED · X14 PARTIAL · HTTP/REACT MIGRATION AUTHORIZED TO CONTINUE · LOGICAL DEADLINE/SECURITY GATES ACTIVE · PRS1 EXACT OFF · MVP/PILOT NOT ACCEPTED.**
