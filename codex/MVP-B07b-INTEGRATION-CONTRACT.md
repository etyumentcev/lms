# MVP-B07b-P — контракт импорта и предпросмотра

2026-10-11 · **PROPOSED — OWNER REVIEW**. Разрешена только документация. [Owner register](L03-OWNER-DECISION-REGISTER.md) принимает B07a в Linux x86_64 / synthetic offline component scope; этот проект не разрешает B07b implementation или B08. Нормативная база: [Content Spec](../docs/content/COURSE-CONTENT-SPEC.md), [content architecture](../docs/architecture/CONTENT-ARCHITECTURE.md), [guardrails](SAPIENTUM-MVP-ENGINEERING-GUARDRAILS.md), [reconciliation](MVP-GUARDRAILS-RECONCILIATION.md), ADR [L15](../docs/adr/ADR-L15-jwt-delayed-revocation.md), [L16](../docs/adr/ADR-L16-organization-jwt-browser-session.md), [L18](../docs/adr/ADR-L18-synthetic-processing-restrictions.md). Ниже MUST описывает предлагаемый контракт после отдельного принятия, а не уже работающий путь.

## 1. Пользовательский сценарий и границы

**Actor → Use case:** доверенный преподаватель выбранного курса → передать внешний Markdown-пакет → получить diagnostics → увидеть student/teacher preview → передать готовый кандидат отдельному B08 publish. Создание курса, редактор/CMS, Git hosting/fetch, универсальный файловый сервис и новая авторизация исключены. Первая интеграция — только controlled synthetic fixtures; upload сам по себе не доказывает synthetic provenance.

**Invariants → Entities/VO:** серверные org/course/actor/context; отдельные неизменяемые projections; приватные файлы; source snapshot, ImportAttempt, BuildReceipt, PreviewSelection как логические понятия, не утверждённая DB schema. Compiler success не выдаёт прав и не публикует. Preview показывает проект, не фактическую доступность ученику после назначения.

**States:** Receiving → Validating → Ready | Invalid | Failed; Receiving/Validating → Cancelled; terminal draft → Expired. Ошибка любой private части блокирует обе projections. Ready неизменяем; исправление пакета создаёт новую попытку. Published не является состоянием B07b: связь с опубликованной версией создаётся только B08.

## 2. Передача и исполнение

Предлагаемый пользовательский вход — один ZIP с `content-package.yaml` в корне и полным деревом Content Spec 1.1; пользователь выбирает существующий курс и указывает source revision. Это новая transport-обёртка над B07a, не существующая возможность CLI. Никаких URL, Git credentials, серверных путей или shell options от клиента. Legacy доступен лишь как отдельная validation-операция без preview/build.

Сервер присваивает opaque import ID, проверяет authority/context до приёма и сохраняет поток в private quarantine. Узкий ZIP adapter допускает только обычные файлы/каталоги: deny absolute/traversal/backslash/drive paths, links/special entries, duplicate/case-fold collisions, encrypted/nested archives как transport, лишние entries вне inventory. Проверять фактические распакованные bytes и число entries, не доверять ZIP headers; никакого extract-all. B07a затем повторно проверяет snapshot своими правилами.

Compiler запускается отдельным непривилегированным Linux x86_64 process с закреплёнными Python/Node/KaTeX зависимостями, без сети/секретов/DB, read-only input и единственным private output directory. Пути и параметры задаёт orchestrator. Проверки openat2 и версии зависимостей обязательны; unsupported environment → Failed. Compile синхронно ограничен жизнью операции, без продолжения пользовательской записи после cancel/deadline; очередь при перегрузке отклоняет admission.

Предлагаемый стартовый synthetic профиль для owner review: ZIP ≤100 MB, expanded package ≤100 MB, ≤2 000 entries, один compile на actor и максимум два на host; worker memory ≤512 MiB, CPU budget ≤20 s, wall budget ≤30 s, output ≤200 MB, process count ≤32. Per-file ограничения B07a сохраняются, tighter limit wins. Supervisor должен ограничивать всё дерево Python/Node и storage, прекращать его до возврата terminal state. Эти значения — гипотеза для проверки, не доказанные operational bounds. Нет доказанного kill/I/O bound → интеграция blocked; G02 этим не закрывается. HTTP admission/body/queue/response потребуют отдельного доказательства T04/E03 и разрешения S4 HTTP, не суммы независимых таймеров.

## 3. Diagnostics и предпросмотр

Diagnostics возвращаются только авторизованному владельцу попытки с правом на этот курс: status и упорядоченные `severity/code/file/line/column/message/related` из [B07a model](../tools/content_compiler/model.py). Только относительные package paths, фиксированные сообщения, escaping при показе; никаких source snippets, stack traces, host paths, stdout/stderr или формул в логах. Transport/timeout/quota ошибки имеют отдельные фиксированные integration codes, не меняют compiler code vocabulary. Не обрезать errors молча: при превышении лимита ответ обозначает truncation и остаётся Failed/Invalid. Diagnostics и сведения об их количестве недоступны ученику.

Student preview загружает только student JSON и reachable student manifest. Teacher preview — отдельный запрос и отдельно авторизованный teacher bundle, не скрытый tab с уже загруженными решениями. Teacher payload не попадает в student response, DOM, preload, cache, analytics или общий asset list. Нельзя заново получать student view фильтрацией teacher JSON. `packageHash`, teacher hashes, build metadata и diagnostics отсутствуют в student payload; studentProjectionHash допустим по B07a контракту.

Оба preview предназначены преподавателю; ученикам draft не выдаётся вообще. Каждое чтение/asset request проверяет import parent → org/course/context → actor/authority → projection → manifest entry; opaque ID/hash не capability. Asset path/URL не принимается как право. Private storage, no-store; никаких public URLs/CDN или source ZIP download через preview. Unknown MIME/path/hash → deny. Renderer использует безопасный Markdown/LaTeX subset, escaping/sanitization, без raw HTML/scripts, автоматических внешних fetch/embeds; изображения только через разрешённый manifest. Смена org/projection, logout/expiry очищают память и Blob URLs; late response не восстанавливает старый scope. Browser rendering/parity и cache isolation ещё NOT PROVEN.

## 4. Временное хранение и provenance

Private namespace по server org/course/import ID; student и teacher directories физически раздельны, source/diagnostics/receipt доступны только orchestration и разрешённому автору. Предлагаемый synthetic TTL — 24 часа от admission, без продления чтениями; после expiry чтение запрещено даже до физической очистки. Failed/cancelled staging удаляется bounded cleanup; сбой удаления остаётся quarantine, не Ready. TTL не является утверждённой production retention policy. Cleanup не следует symlinks и не удаляет pinned B08 candidate; гонка expiry/pin решается CAS.

BuildReceipt связывает server import ID, org/course/actor, immutable source snapshot ID, заявленную `sourceRevision`, server-computed digest исходного upload, `packageHash`, `studentProjectionHash`, `teacherArtifactHash`, schema/compiler revision, build time, context/provenance references и manifest integrity. Source revision от клиента — метка, не доказательство Git commit; server snapshot+digest обеспечивает точную воспроизводимую связь. Hashes берутся из проверенного B07a результата и сверяются по B07a canonical algorithms, не с JSON клиента; ZIP digest не подменяет packageHash. Полный receipt приватен; одинаковый student hash не означает одинаковую teacher/package version.

Ready фиксируется только после проверки обеих projections и полного receipt, с atomic visibility; CLI rename не доказывает crash durability. Для будущего durable implementation orchestrator владеет короткой metadata transaction, без file/process I/O под DB locks: предложены Serializable, порядок course/context guards → import row, CAS expected state/version, current restrictions recheck и secret-free audit в одном commit. Конкретные guard rows/lock compatibility с restriction writer надо проверить до реализации schema; неясность блокирует запись. Файлы staged до commit, failed commit оставляет quarantine для reconciliation. Потеря commit acknowledgement → indeterminate, чтение по server-bound command/import ID, без replay; тот же command ID с другим input digest → conflict. Новый explicit retry создаёт новый attempt. Точный storage/schema design остаётся зависимостью, не разрешением migrations.

B08 получает pinned Ready receipt и artifacts по server ID, повторно проверяет целостность, authority, context и expected version; атомарно сохраняет связь `ContentVersionId ↔ import/source snapshot/revision ↔ packageHash/studentProjectionHash/teacherArtifactHash`. Нельзя пересобрать другой пакет под прежним receipt, привязать только по student hash или менять уже выданные задания. После expiry — новый import, не неявная публикация исчезнувших bytes.

## 5. Authority и существующие компоненты

| Операция | Trusted source / решение |
|---|---|
| Import, diagnostics, обе preview | S2/S4 trusted identity и S3 server-resolved CourseRegistry/ownership; teacher course scope, same org, владелец попытки; current processing context/restrictions. Group-only teacher не считается course-wide importer. Предлагаемый доступ требует отдельного принятия mapping import/preview; до него deny. Client role/course/header и успешный compile не authority. |
| Student draft access | Всегда deny, включая student projection/asset/count/existence. Student delivery опубликованного контента — последующий scope. |
| Teacher materials | Только тот же trusted course-author context и отдельная projection authorization; существующий Read не считается автоматическим разрешением нового teacher-material resource. |
| Publish | B08: явная owner-approved publisher capability для org/course из существующего trusted issuance/administration contour. Teacher, admin flag или `ComponentAuthorization.Allows=true` сами по себе не разрешают Publish. Capability сейчас отсутствует; неизвестная/невыданная → deny. Encoding, provisioning и operation mapping — отдельное решение, без параллельного auth store. |

Обычные JWT grants следуют delayed revocation ADR-L15/16, без нового per-request grant freshness barrier. Resource ownership и current processing проверяются перед выдачей, ограничения перед commit — в transaction. Для worker steps действуют job rules ADR-L15: actor/scope/generation/context и deadline, fresh authority перед irreversible step, новая authorization при delivery, никакого вечного allow из admission. PRS1 exact OFF; legacy conservative restrictions нельзя сузить, reserved masks нельзя использовать как Publish.

| Уже существует (проверено по коду) | Переиспользование / предел |
|---|---|
| [B07a CLI](../tools/content_compiler/__main__.py), model/snapshot/compiler | Validation/compile, diagnostics, split manifests/hashes. Нет upload, sandbox supervisor или renderer. |
| [IScopedAuthorityReader](../src/Lms.Application/ScopedAuthority.cs): ResolveCurrent, ResolveResource, CanAccess; [ComponentAuthorization](../src/Lms.Application/ComponentAuthorization.cs) | Trusted resource resolution, component auth/processing boundaries; ResourceKind не содержит import, ScopeAction только Read/Submit/Review. Нужен утверждённый adapter/mapping, не вызов существующего метода с выдуманным enum. |
| [Schema mappings](../src/Lms.Infrastructure/SchemaFoundationMappings.cs): Organizations, OrganizationMemberships, CourseRegistry, CourseGroups, TeacherAssignmentScopes, EnrollmentScopes | Источник ownership/grants/generations; не создавать дублирующие роли/курсы. |
| ProcessingContextVersions, CourseContextBindings, ProcessingRestrictions; SecurityAudit/AuthorityHistory | Существующие context/restriction/provenance concepts; import audit mapping и transaction compatibility ещё требуют review. |
| [PublishedContentVersions / PostgresContentResolver.Resolve](../src/Lms.Infrastructure/PublishedContent.cs) | Immutable exact-version student reads; таблица имеет Id/CourseId/VersionNumber/SourceRevision/PublishedAt/ContentJson. Нет package/student hashes, teacher bundle или draft status. Не использовать как temporary staging. DTO пока не сохраняет полный B07a lesson body/hints/assets; нельзя молча отбросить поля. |
| [AccessLessons / ProblemAssignments](../src/Lms.Infrastructure/LmsDbContext.cs), [workspace queries](../src/Lms.Infrastructure/PostgresWorkspaceQueries.cs) | Published/AvailableFrom и assignment→exact version уже существуют; B08 расширяет publish/open/assign workflow. Seed inserts не trusted publisher use case. |
| StoredAttachments, AttachmentScopes и attachment service | Предназначены для submission/message parents; не переиспользовать эти таблицы/доступ для course package с поддельным parent. Нужен узкий private draft artifact adapter. |

## 6. Проверки и последовательность реализации после разрешения

Каждый deny тест — с независимым ожидаемым результатом и положительным same-scope control; отсутствие секрета проверять в bytes/headers/DOM/logs, не только статусом.

| Негативные сценарии | Ожидаемый результат |
|---|---|
| Forged role/org/import ID; wrong course/group; чужой import; student; отсутствующий publisher capability | Deny до materialization; нет файлов, diagnostics, existence/count/pagination утечки. Проверять каждый query, command и asset path. |
| Revoked/moved membership, stale generation, expired JWT, org switch/late response | Соблюдение accepted delayed semantics; refresh не возвращает revoked grant; worker recheck deny; historical assignment не переезжает. |
| Missing/ambiguous context, DB unavailable, active restriction, concurrent restriction at Ready/pin | Fail closed, serialization/recheck; никакого stale allow или unsafe restriction narrowing. |
| Forged/missing parent; teacher asset через student manifest; shared asset с private usage; wrong hash | Deny, student bytes/hash независимы от teacher-only изменений, обе projections не раскрываются при private validation error. |
| ZIP bomb/traversal/link/collision, expanded limits +1, FIFO/race, malformed math/YAML, missing runtime | Invalid/Failed без внешнего чтения, сети, частичного Ready; boundary limits и valid package controls. |
| Worker hang/OOM/child process, output overflow, storage failure, cancel, process crash | Bounded stop проверен инструментально; quarantine, ни success, ни обещания rollback неизвестного commit. |
| Duplicate submit, changed body under same command ID, stale CAS, restart, lost reply, cleanup vs pin | Не более одного Ready transition на attempt; conflict/indeterminate resolution; никакой потери pinned bytes. |
| Teacher HTML/LaTeX injection, preview toggle/cache/back navigation, expiry mid-upload, CSRF/Origin, reconnect | Нет исполнения/утечки; новая authorization перед retry, без automatic command replay. HTTP/browser tests только после отдельного разрешения. |
| Publish B вместо A для старого assignment, двойной publish/assign | B08 acceptance: immutable A остаётся A; эти операции не выполняются в B07b. |

План, последовательно: (1) owner review этого transport/limits/TTL/authority split; (2) отдельно разрешённый B07b component slice: ZIP ingress + bounded worker + fake private storage, positive/negative synthetic tests; (3) review минимального draft persistence и transaction/auth mapping с B02/B06/B11, затем только отдельное разрешение schema/runtime; (4) protected preview adapter/renderer и parity/isolation tests; (5) HTTP/frontend подключение только при отдельном разрешении S4 и доказанных необходимых bounds; (6) отдельный B08 contract и implementation authorization для trusted publish, immutable full-content mapping, lesson opening и exact-version assignments. Ни один шаг не считается разрешённым этой нумерацией.

**Граница:** B07b заканчивается приватным проверенным Ready candidate с diagnostics/preview/receipt и expiry. B08 начинается trusted Publish command, назначением ContentVersionId, durable publication/provenance, открытием lessons и assignment mutations. B07b не пишет PublishedContentVersions/AccessLessons/ProblemAssignments. B09/B12 delivery не считается завершённой через preview. Owner review нужен для предложенных значений и mappings; отсутствие publisher policy не обходится role Teacher.

## 7. Проверка документа и guardrails §7

Covered в проекте: actor/states/invariants; S2/S3/PRS1 boundaries; org/course/group/owner checks; sources/subjects/context fail-closed; filtering до counts; независимая защита assets/history; transaction/CAS/retry/unknown outcome; limits/safe diagnostics. Blocked до реализации/review: конкретный durable storage/locks и publisher capability, bounded worker evidence, runtime negative-access и browser isolation. Existing attachments/history изменения — N/A: этот документ ничего в них не меняет. Schema/API/frontend/runtime changes — N/A: documentation only. Real data/secrets/release claims отсутствуют.

Документационная проверка: локальные ссылки, whitespace, append-only register и сохранность baseline — см. результат ниже. Runtime/component/integration/negative-access/browser/E2E tests в B07b-P **NOT RUN**. По историческому [B07a report](MVP-B07a-IMPLEMENTATION-RESULTS.md): 74 component PASS; 272 Application PASS ранее, повтор после math integration NOT RUN. Это не runtime/HTTP/browser/physical security acceptance. Specialist skills model-domain/implement-use-case/implement-feature отсутствуют в доступном каталоге; их запуск не заявлен.

Канонический каталог использован; dirty branch `codex/l1-s4-jwt-api-authorization` сохранена по AGENTS.md, переключения на main не было. Никаких commit/push/merge/rebase/deploy.

**B07a ACCEPTED · B07b-P READY FOR OWNER REVIEW · B07b IMPLEMENTATION NOT AUTHORIZED · B08 NOT AUTHORIZED · PRS1 EXACT OFF · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**

Validation 2026-10-11: PASS — 455 существующих файлов вне целевого register совпадают с SHA-256 baseline; register сохраняет прежние bytes как prefix; 17 локальных ссылок разрешаются; whitespace contract и `git diff --check -- codex/L03-OWNER-DECISION-REGISTER.md` PASS. Проверка выполнена Python assertions; runtime tests NOT RUN.
