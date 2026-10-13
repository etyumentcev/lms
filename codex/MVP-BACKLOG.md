# Sapientum LMS — функциональный MVP backlog

2026-10-10. План для owner review, **не разрешение реализации**. Основание — [inventory с доказательствами](MVP-READINESS-INVENTORY.md), [guardrails](SAPIENTUM-MVP-ENGINEERING-GUARDRAILS.md), [reconciliation](MVP-GUARDRAILS-RECONCILIATION.md) и принятый owner register. Предыдущий `MVP-FUNCTIONALITY-BACKLOG.md` сохранён; его P1 означал complete journey, здесь приоритеты пересчитаны **по текущему заданию**: P0 блокирует обязательный процесс, P1 — качество/поддерживаемость, P2 — после MVP. Это не изменение owner decisions.

Владелец каждой роли указан как пользователь задачи; фактический исполнитель не назначен. Оценки — **инженерные человеко-дни**, включая реализацию, релевантные positive/negative tests и review, для инженера, знакомого с .NET/React/PostgreSQL. Не календарный срок. Общие зависимости не повторно включены в стоимость каждого downstream item. Gates/неопределённые proof obligations оценены отдельно, не скрыты в диапазонах.

Общий обязательный contract для каждой новой мутации: authenticated actor/capability → server-derived resource/context → invariants/state → transaction owner/isolation/lock order → expectedVersion/CAS → commit+audit → retry/unknown outcome. Для reads — filtered rows/counts/pages/aggregates до materialization. Каждая задача ниже наследует negative matrix Guardrails §5 с reason для N/A; owner разрешение новых API/schema/permissions и закрытых gates обязательно. Никаких новых migrations/runtime в этом аудите.

## Карта работ по пользовательским сценариям

| ID | Приоритет | Результат | Текущий статус | Оценка, чел.-дни |
|---|---|---|---|---|
| B01 | P0 | Разрешить путь к безопасному synthetic HTTP | BLOCKED | Не оценено: внешний decision/proof gate |
| B02 | P0 | Войти, refresh, выйти и выбрать организацию | PARTIAL | 6–10 |
| B03 | P0 | Создать организацию и первого администратора | PARTIAL | 5–8 |
| B04 | P0 | Создать учётную запись и активировать | PARTIAL | 6–10 |
| B05 | P0 | Восстановить доступ и блокировать account | PARTIAL | 4–7 |
| B06 | P0 | Создать курс/группы и выдать/отозвать назначения | PARTIAL | 4–7 |
| B07 | P0 | Импортировать и проверить внешний Markdown-курс | MISSING | 5–8 |
| B08 | P0 | Публиковать версии, открывать lessons, назначать задачи | PARTIAL | 4–7 |
| B09 | P0 | Прочитать lesson/hints/assets и teacher reference | PARTIAL | 4–7 |
| B10 | P0 | Найти курсы, группы и учеников | MISSING | 3–5 |
| B11 | P0 | Замкнуть scoped authorization для учебных действий | PARTIAL | 6–10 |
| B12 | P0 | Привязать фото к защищённому процессу | PARTIAL | 5–9 |
| B13 | P0 | Видеть базовый прогресс ученика и группы | PARTIAL | 3–5 |
| B14 | P0 | Вернуться к завершённой проверке | PARTIAL | 2–4 |
| B15 | P0 | Пережить сбой отправки без двойных эффектов | PARTIAL | 3–5 |
| B16 | P0 | Принять полный synthetic MVP | PARTIAL | 4–7 |
| B17 | P1 | Обсудить решение без новой Submission | PARTIAL | 2–4 |
| B18 | P1 | Развернуть и восстановить synthetic среду | PARTIAL | 3–6 |
| B19 | P1 | Ограничить ресурсы и убрать безопасные orphan uploads | PARTIAL | 3–6 |
| B20 | P1 | Проверить photo-first UX на выбранных устройствах | IMPLEMENTED_NOT_VERIFIED | 2–4 |
| B21 | P2 | Измеренные улучшения сверх MVP | PARTIAL | 4–10 |
| B22 | P0 перед пилотом | Разрешить реальные данные отдельным решением | BLOCKED | Не оценено: внешний decision/proof gate |
| B23 | P2 parked | PRS1 exact только после reprioritization | BLOCKED | Не оценено: внешний decision/proof gate |

## Общие условия безопасного доступа

### B01 — Разрешить путь к безопасному synthetic HTTP

- **Приоритет / статус / роль:** P0 · BLOCKED · Владелец / security engineer.
- **User story:** Как владелец, хочу проверяемые границы исполнения до разрешения HTTP.
- **Компоненты:** A06 proof obligations; qualified clock; PostgreSQL commit; controlled egress; local storage; ADR-L15/17. Пути существующих компонентов — в evidence map inventory; «new/future» означает предложение, не существующий файл.
- **Зависимости:** Owner G02 decision и отдельная authorization; не менять T04/E03 в этом backlog.
- **Оценка:** достоверный диапазон сейчас отсутствует; не входит в сумму реализации.
- **Security requirements:** Deadline/authority/processing не ослаблять. Не ставить дату закрытия без proof.
- **Acceptance criteria:** Результат — принятие доказательств под текущими нормами; либо отдельно рассмотренное owner normative решение и затем proof. Ни один draft A06 не становится разрешением автоматически.
- **Отрицательные тесты:** Fault/clock/late commit/egress/restart доказательства по A06; неизвестный исход не PASS.
- **Независимость / конфликты:** Независимая исследовательская ветвь плана; runtime integration B02/B11/B12 ждёт gate.

### B02 — Войти, refresh, выйти и выбрать организацию

- **Приоритет / статус / роль:** P0 · PARTIAL · Все роли.
- **User story:** Как пользователь, хочу войти без вставки dev token и безопасно сменить организацию.
- **Компоненты:** PostgresIdentitySessions; PostgresComponentTokenIssuer; ComponentJwt; Program; browserClient; App; future session UI. Пути существующих компонентов — в evidence map inventory; «new/future» означает предложение, не существующий файл.
- **Зависимости:** B01 + отдельное S4 HTTP разрешение; existing S2/S3/A1/A2.
- **Оценка:** 6–10 чел.-дней.
- **Security requirements:** Reuse S2/S4; no public signup, no localStorage refresh; org scopes только trusted DB issue/refresh.
- **Acceptance criteria:** Login/refresh/logout/org switch/reload замкнуты; access memory-only, refresh Secure HttpOnly; O01 30s/idle60m/absolute12h; account/context switch aborts requests и очищает drafts/cache/Blob URLs.
- **Отрицательные тесты:** Wrong issuer/audience/signature/org/expired; CSRF/Origin; two tabs; forged/late refresh; lost ACK; logout не обещает instant access JWT revoke.
- **Независимость / конфликты:** Backend+browser один vertical slice; нельзя параллельно править Program/App с B11/B10 без sequencing.

## Организация, участники и восстановление

### B03 — Создать организацию и первого администратора

- **Приоритет / статус / роль:** P0 · PARTIAL · Platform operator.
- **User story:** Как оператор платформы, хочу подготовить Organization и передать управление org-admin без DML.
- **Компоненты:** SchemaFoundations Organization; IdentityMapping; SecurityAudit; new trusted bootstrap application adapter/operator UI. Пути существующих компонентов — в evidence map inventory; «new/future» означает предложение, не существующий файл.
- **Зависимости:** Owner bootstrap capability contract; B02 для HTTP; B11 для границ.
- **Оценка:** 5–8 чел.-дней.
- **Security requirements:** Новая administrative capability требует явного принятия; platform admin не образовательный reader.
- **Acceptance criteria:** Документированный approved initial bootstrap без SQL; operator создаёт SyntheticOnly org и first admin; audit atomic; org-admin не может создавать чужой bootstrap.
- **Отрицательные тесты:** Replay/partial commit/duplicate org; forged operator; foreign org; operator education reads denied.
- **Независимость / конфликты:** Contract/component работа отдельно от content; schema/DI интегрировать последовательно.

### B04 — Создать учётную запись и активировать

- **Приоритет / статус / роль:** P0 · PARTIAL · Org-admin / ученик / преподаватель.
- **User story:** Как администратор, хочу выдать проверенному участнику одноразовый доступ через roster.
- **Компоненты:** IdentityMapping; ProvisioningRequest; AccessChallenge/ChallengeReceipt; new challenge application service/admin+activation UI. Пути существующих компонентов — в evidence map inventory; «new/future» означает предложение, не существующий файл.
- **Зависимости:** B03/B02; T01/T02/E05; G03 engineering review.
- **Оценка:** 6–10 чел.-дней.
- **Security requirements:** Recipient verification, pseudonymous test roster, secret-free audit; HMAC key lifecycle и rotation defined.
- **Acceptance criteria:** Roster→account→secret once→activation→password→login; no implicit enrollment; code 16 Crockford/80bit/HMAC, activation24h, password12–128, max5 failures; issue3/h recipient,20/h initiator,cooldown60s; verify10/min/IP,5/15min/account.
- **Отрицательные тесты:** Parallel consume/reissue; wrong purpose/expired/locked; duplicate request не redisclose; shared school NAT; ambiguous identity не создаёт bypass account.
- **Независимость / конфликты:** Challenge engine общий с B05: один writer и файлы, делать последовательно; UI после contract.

### B05 — Восстановить доступ и блокировать account

- **Приоритет / статус / роль:** P0 · PARTIAL · Org-admin / independent security operator / пользователь.
- **User story:** Как потерявший доступ пользователь, хочу получить recovery после проверки, не теряя историю.
- **Компоненты:** RecoveryRequest; AccessChallenge; AccountSecurityState; session RevokeAll; admin/operator/recovery UI. Пути существующих компонентов — в evidence map inventory; «new/future» означает предложение, не существующий файл.
- **Зависимости:** B04/B02; E03/E04/E05 и отдельная S6 authorization.
- **Оценка:** 4–7 чел.-дней.
- **Security requirements:** Security operator не получает student content; account-security capability независима от org roster.
- **Acceptance criteria:** Request→independent approve для shared identity→recovery15m→atomic password+consume+session revoke+audit; confirmed block/unblock; old grants/sessions не оживают.
- **Отрицательные тесты:** Initiator=approver denied; wrong org/recipient; repeated/expired code; race block/consume; no secret redisplay; JWT до exp по E03, B membership не становится authority A.
- **Независимость / конфликты:** Продолжение B04, не независимый challenge writer.

### B06 — Создать курс/группы и выдать/отозвать назначения

- **Приоритет / статус / роль:** P0 · PARTIAL · Org-admin.
- **User story:** Как администратор, хочу явно связать учащихся, группы и преподавателей и видеть состояние прав.
- **Компоненты:** CourseRegistration/CourseGroup; IScopedAuthorityAdministration.Change; s3_change_authority; admin catalog/status UI. Пути существующих компонентов — в evidence map inventory; «new/future» означает предложение, не существующий файл.
- **Зависимости:** B03/B04/B11; accepted B01 generation semantics; new registry/group command contract.
- **Оценка:** 4–7 чел.-дней.
- **Security requirements:** Authenticated org capability устанавливает scope; request IDs — selectors, не доказательство authority.
- **Acceptance criteria:** Создать CA/G1/G2; membership отдельно enrollment/teacher assignment; move/revoke/regrant через UI и expectedVersion; confirmation/audit; old assignment affiliation не меняется.
- **Отрицательные тесты:** Foreign course/group/org; stale membership generation/version; concurrent move/revoke; retry не продлевает deadline/не дублирует history; no implicit grant.
- **Независимость / конфликты:** Общий S3/admin/schema с B03/B11, sequential merge; не новый альтернативный writer.

## Подготовка и проведение курса

### B07 — Импортировать и проверить внешний Markdown-курс

- **Приоритет / статус / роль:** P0 · MISSING · Преподаватель.
- **User story:** Как преподаватель, хочу получить валидную программу из Markdown с понятными ошибками.
- **Компоненты:** New importer/validator/compiler; docs/content/COURSE-CONTENT-SPEC; examples/course; PublishedContent artifact contract. Пути существующих компонентов — в evidence map inventory; «new/future» означает предложение, не существующий файл.
- **Зависимости:** R03 external authoring; content contract owner acceptance; B08 для публикации.
- **Оценка:** 5–8 чел.-дней.
- **Security requirements:** No arbitrary shell/Git credentials/SSRF from input; UI не CMS; source provenance и student/teacher split.
- **Acceptance criteria:** Детерминированный student artifact и отдельный teacher artifact с source revision/hash; diagnostics file/line; lesson body/problem/hints/assets retained; invalid input не публикуется. B07a 3–5д component, B07b 2–3д import preview/orchestration входит в оценку.
- **Отрицательные тесты:** Duplicate IDs; missing links/assets; traversal/symlink/oversized input/unsupported schema; external retrieval без explicit policy; teacher solutions отсутствуют в student artifact.
- **Независимость / конфликты:** B07a — наиболее независимый slice: новый component/tests, без DB/API/DI/новых privileges. B07b ждёт B02/B06/B08.

### B08 — Публиковать версии, открывать lessons, назначать задачи

- **Приоритет / статус / роль:** P0 · PARTIAL · Преподаватель с явным publish scope.
- **User story:** Как преподаватель, хочу открыть конкретную версию урока группе без изменения прошлых работ.
- **Компоненты:** PublishedContentVersion; LessonAccess; ProblemAssignment; immutable content resolver; Application publisher/open/assign + UI. Пути существующих компонентов — в evidence map inventory; «new/future» означает предложение, не существующий файл.
- **Зависимости:** B07/B06/B11; owner publisher capability и schema/API contract.
- **Оценка:** 4–7 чел.-дней.
- **Security requirements:** Same-transaction authority/workflow/current restrictions/CAS/audit; no manual DB insertion as publisher.
- **Acceptance criteria:** Import preview→publish→open lesson→assign exact version; повторный import создаёт новую version; close/withdraw прекращает новые действия по contract, history остаётся доступной по правилам; basic archive без delete.
- **Отрицательные тесты:** Unauthorized teacher/group; invalid draft; duplicate publish/assign; stale version; concurrent publication; old assignment A не получает B; absent context deny.
- **Независимость / конфликты:** Shared PublishedContent/ReadModels/DbContext: последовательно с B09/B11.

### B09 — Прочитать lesson/hints/assets и teacher reference

- **Приоритет / статус / роль:** P0 · PARTIAL · Ученик / преподаватель.
- **User story:** Как ученик, хочу изучить полный урок; как преподаватель — свериться с эталоном именно этой версии.
- **Компоненты:** PublishedContent/ReadModels; WorkspaceQueries; MarkdownContent; protected material service; Problem/Lesson/ReviewContext. Пути существующих компонентов — в evidence map inventory; «new/future» означает предложение, не существующий файл.
- **Зависимости:** B07/B08/B11; B12 для защищённых binary assets; R04 owner source/teacher policy.
- **Оценка:** 4–7 чел.-дней.
- **Security requirements:** Teacher material не включать в общий ContentJson и не скрывать CSS; source/dependencies входят в processing scope.
- **Acceptance criteria:** Backend заполняет lesson body/hints/materials; renderer показывает local asset через authorized resolver; teacher-only solution/rubric exact version; no new scoring engine.
- **Отрицательные тесты:** Student JSON/cache/network не содержит reference; foreign group/old version/missing asset denied; unsafe Markdown/TeX/URL/path; no latest fallback.
- **Независимость / конфликты:** После согласованного content contract; конфликтует с B08 по DTO/model, sequencing.

## Доступ к учебной работе и её результатам

### B10 — Найти курсы, группы и учеников

- **Приоритет / статус / роль:** P0 · MISSING · Все роли.
- **User story:** Как пользователь, хочу попасть в свой курс из приложения; как преподаватель — выбрать ученика без UUID-справочника.
- **Компоненты:** App /courses; scoped catalog queries; ReadModels; CourseRegistry/Scopes; trusted minimal roster names; queue filters. Пути существующих компонентов — в evidence map inventory; «new/future» означает предложение, не существующий файл.
- **Зависимости:** B06/B11/B02; owner minimal profile fields/source.
- **Оценка:** 3–5 чел.-дней.
- **Security requirements:** Filter server-side до page/count; names не authority и не arbitrary JWT claim; PII scope explicitly minimal.
- **Acceptance criteria:** Мои курсы→course→lesson→problem; teacher group/student options и admin statuses; stable IDs + trusted minimum label; empty state; queue URL filters сохранились.
- **Отрицательные тесты:** Foreign IDs/name/count/facets/page leakage; role spoof; no rows from current unrelated org; revoked scope по T04; shared identity no cache bleed.
- **Независимость / конфликты:** UI после contract отдельно от importer; не одновременно менять App/ReadModels с B02/B09.

### B11 — Замкнуть scoped authorization для учебных действий

- **Приоритет / статус / роль:** P0 · PARTIAL · Все роли.
- **User story:** Как участник, хочу работать только с разрешёнными ресурсами и сохранить историю после move.
- **Компоненты:** Program/ActorMapper; ProblemWorkService; WorkStore; WorkspaceQueries/ReviewQueue; S3 reader; ComponentAuthorization/processing gate. Пути существующих компонентов — в evidence map inventory; «new/future» означает предложение, не существующий файл.
- **Зависимости:** B01/B02 + explicit S4/S7 cutover; per-operation write protocol owner/security review.
- **Оценка:** 6–10 чел.-дней.
- **Security requirements:** T04 delayed ordinary authority не заменяется per-request grant freshness; conservative processing gate не ослабляется, exact OFF.
- **Acceptance criteria:** Все learning routes используют одну принятую boundary; resource принадлежность server-resolved; filters before counts; processing current deny; write check сериализован с commit; legacy current-group drift устранён; no post-hoc allow receipt.
- **Отрицательные тесты:** Матрица org/course/group/role/owner/parent; restriction write race; stale version; revoked/moved generations; DB processing outage; missing provenance; queue totals independent.
- **Независимость / конфликты:** Центральная интеграция: не параллельно B06/B08/B12 mutations; review каждый adapter.

### B12 — Привязать фото к защищённому процессу

- **Приоритет / статус / роль:** P0 · PARTIAL · Ученик / преподаватель.
- **User story:** Как ученик, хочу отправить несколько фото, доступных только участникам работы.
- **Компоненты:** AttachmentsController; AttachmentService; LocalFileAttachmentStorage; AttachmentScopes/claim triggers; uploader/viewer. Пути существующих компонентов — в evidence map inventory; «new/future» означает предложение, не существующий файл.
- **Зависимости:** B01/B11/B02 и отдельное file/storage authorization; approved upload parent/context contract.
- **Оценка:** 5–9 чел.-дней.
- **Security requirements:** Parent XOR/immutable affiliation; download by ID недостаточен; jobs не наследуют perpetual JWT.
- **Acceptance criteria:** Upload eligibility/context→preview→atomic claim→parent download→pending delete; JPEG/PNG order/hash intact; annotation не изменяет original; bounded execution evidence или BLOCKED; compensation no delete on unknown commit.
- **Отрицательные тесты:** Wrong parent/org/uploader; pending teacher read; double claim; delete-vs-submit; corrupt bytes/traversal; clock/DB/storage failure; no public storage bypass.
- **Независимость / конфликты:** Только после boundary contract, shared migrations/claims требуют последовательного исполнения с B11/B19.

### B13 — Видеть базовый прогресс ученика и группы

- **Приоритет / статус / роль:** P0 · PARTIAL · Преподаватель / ученик.
- **User story:** Как преподаватель, хочу таблицу статусов группы и переход к работе ученика.
- **Компоненты:** WorkspaceQueries progress; new scoped group/student summary DTO; course/roster progress UI. Пути существующих компонентов — в evidence map inventory; «new/future» означает предложение, не существующий файл.
- **Зависимости:** B10/B11/B08.
- **Оценка:** 3–5 чел.-дней.
- **Security requirements:** Одинаковый eligibility predicate с details; отсутствие строк не раскрывает restricted counts.
- **Acceptance criteria:** Группа→ученик→lesson/work; counts Accepted/Submitted/NeedsRevision/NotStarted и attempts; вычисление из active assignments, zero denominator safe; не расширенная аналитика.
- **Отрицательные тесты:** Foreign group/student excluded до aggregate; mixed versions; withdrawn assignments; move/generation; accepted terminal; counts совпадают с underlying rows.
- **Независимость / конфликты:** Read projection/UI возможно отдельно после frozen DTO; общий WorkspaceQueries owner.

### B14 — Вернуться к завершённой проверке

- **Приоритет / статус / роль:** P0 · PARTIAL · Преподаватель.
- **User story:** Как преподаватель, хочу найти собственную прошлую проверку после исчезновения работы из очереди.
- **Компоненты:** ReviewQueuePage or distinct review-history page; scoped query over Reviews/Submission/Assignment; ReviewContext. Пути существующих компонентов — в evidence map inventory; «new/future» означает предложение, не существующий файл.
- **Зависимости:** B10/B11.
- **Оценка:** 2–4 чел.-дней.
- **Security requirements:** TeacherId из server review, не client filter authority; история не blanket read-after-revoke.
- **Acceptance criteria:** Список своих проверок с date/course/student фильтром→конкретная attempt; текущие permissions соблюдаются; история immutable; Accepted reopen read-only.
- **Отрицательные тесты:** Other teacher attribution/foreign org/group; moved work; repeated review; page/count leakage; stale saved URL denied when no longer authorized.
- **Независимость / конфликты:** Отдельная page/query после common authorization; не новый history writer.

## Завершение пользовательского цикла

### B15 — Пережить сбой отправки без двойных эффектов

- **Приоритет / статус / роль:** P0 · PARTIAL · Ученик / преподаватель.
- **User story:** Как пользователь, хочу понять, сохранилась ли работа после разрыва соединения.
- **Компоненты:** SolutionForm/ReviewForm; API client; query state; command retry contract; session remount. Пути существующих компонентов — в evidence map inventory; «new/future» означает предложение, не существующий файл.
- **Зависимости:** B02/B11/B12.
- **Оценка:** 3–5 чел.-дней.
- **Security requirements:** Не считать timeout rollback; message semantics учитывает B17; данные другой session не восстанавливаются.
- **Acceptance criteria:** Ambiguous response→read reconciliation→явное дальнейшее действие; duplicate click/stale version один effect; draft warning и сохраняемое в текущей разрешённой session состояние; auth loss очищает sensitive state; не обещать offline sync.
- **Отрицательные тесты:** Dropped commit ACK; stale tab; refresh failure mid-upload/review; double submit; canceled request; changed account with cached Blob; no automatic replay.
- **Независимость / конфликты:** UI errors можно отдельно после session contract, command/schema изменения review с B11.

### B16 — Принять полный synthetic MVP

- **Приоритет / статус / роль:** P0 · PARTIAL · Все роли / тестировщик.
- **User story:** Как владелец, хочу воспроизводимо пройти все обязательные роли без ручной БД.
- **Компоненты:** Future browser E2E fixtures; existing tests + release smoke adaptation; scoped deployment/test runbook. Пути существующих компонентов — в evidence map inventory; «new/future» означает предложение, не существующий файл.
- **Зависимости:** B02–B15; B01; owner S8/M3 authorization где применимо.
- **Оценка:** 4–7 чел.-дней.
- **Security requirements:** UI COMPLETE ≠ E2E ACCEPTED ≠ SYNTHETIC MVP ACCEPTED; no real data.
- **Acceptance criteria:** E2E-01–09 выполняются через product UI с negative API assertions; no dev token/SQL roster; restart/restore representative cycle; independent evidence each status; owner acceptance.
- **Отрицательные тесты:** Все negative cases inventory; fail closed dependencies; two org/shared actor; repeat/move/expiry/outage; regression существующей history.
- **Независимость / конфликты:** Automation fixtures писать после contracts; финальный прогон последовательно на изолированной среде.

## Качество MVP и эксплуатация

### B17 — Обсудить решение без новой Submission

- **Приоритет / статус / роль:** P1 · PARTIAL · Ученик / преподаватель.
- **User story:** Как участник, хочу ответить на замечание и задать вопрос в истории работы.
- **Компоненты:** HistoryTimeline/ReviewContext; existing PostMessage/MessageRequest; new composer/reply UI. Пути существующих компонентов — в evidence map inventory; «new/future» означает предложение, не существующий файл.
- **Зависимости:** B02/B11/B15.
- **Оценка:** 2–4 чел.-дней.
- **Security requirements:** Existing API не равен user-ready; no auto resend, idempotency design если replay нужен.
- **Acceptance criteria:** ReplyTo same work; текст/фото по разрешённому scope; message не меняет status/attempt count; UI compose/reply + refresh/history; ambiguous retry reconciles.
- **Отрицательные тесты:** Cross-work reply; empty body; foreign attachment; student review escalation; duplicate committed message retry; concurrent messages.
- **Независимость / конфликты:** Отдельный composer после command/session contract; без изменения Review semantics.

### B18 — Развернуть и восстановить synthetic среду

- **Приоритет / статус / роль:** P1 · PARTIAL · Оператор.
- **User story:** Как оператор, хочу воспроизвести первый запуск, рестарт и восстановление полного набора.
- **Компоненты:** Compose/runbook; health/logs; backup manifest DB+originals+keys; isolated restore checks. Пути существующих компонентов — в evidence map inventory; «new/future» означает предложение, не существующий файл.
- **Зависимости:** B03/B12 для implementation; подготовка до B16, финальная проверка совместно с B16; owner ops scope, ADR-L03–05/08.
- **Оценка:** 3–6 чел.-дней.
- **Security requirements:** Это synthetic rehearsal; off-host custody/RPO/RTO/retention и operational pilot acceptance отдельно B22.
- **Acceptance criteria:** Безопасный изолированный deployment; отдельный migration principal; snapshot set согласован с writers; clean restore проверяет hashes/versions/overlays/scopes, session reconciliation; DB/storage/errors observable без secrets.
- **Отрицательные тесты:** Missing/corrupt original/key; incomplete backup; failed restore; post-backup revoke; DB unavailable; no development key at qualified ingress.
- **Независимость / конфликты:** Runbook/fixture preparation отдельно; restore qualification после integrated system.

### B19 — Ограничить ресурсы и убрать безопасные orphan uploads

- **Приоритет / статус / роль:** P1 · PARTIAL · Оператор / пользователь.
- **User story:** Как оператор, хочу чтобы повторные загрузки не исчерпали storage/RAM.
- **Компоненты:** AttachmentService; validator; future reservations/quota/sweeper; request limits/ops metrics. Пути существующих компонентов — в evidence map inventory; «new/future» означает предложение, не существующий файл.
- **Зависимости:** B12/B11; owner C06/C08 workload/retention; separate schema/job approval.
- **Оценка:** 3–6 чел.-дней.
- **Security requirements:** Не вводить произвольные production quota/retention; privacy erasure — отдельный privileged workflow.
- **Acceptance criteria:** Finite resources/controlled upload concurrency; transactional reservation и claim/expiry; orphan quarantine/retry; known storage budget; invariant linked history not deleted; понятные 413/429.
- **Отрицательные тесты:** Parallel quota oversubscription; expiry vs claim; delete failure; lost ACK; DB outage stops cleanup; backup barrier; many files/large originals.
- **Независимость / конфликты:** Конфликт с B12 по lifecycle/schema: делать после него; не независимый cleanup writer.

### B20 — Проверить photo-first UX на выбранных устройствах

- **Приоритет / статус / роль:** P1 · IMPLEMENTED_NOT_VERIFIED · Ученик / преподаватель / tester.
- **User story:** Как преподаватель на планшете, хочу читать A4 и оставлять устойчивые пометки.
- **Компоненты:** K-DEVICE-TEST-PROTOCOL; AnnotationPhoto/SubmissionViewer; uploader; session forms; responsive CSS. Пути существующих компонентов — в evidence map inventory; «new/future» означает предложение, не существующий файл.
- **Зависимости:** Owner C07 device/browser/tester; B12/B15/B16.
- **Оценка:** 2–4 чел.-дней.
- **Security requirements:** Не заменять physical Safari/Pencil headless emulation и не сокращать scope без owner decision.
- **Acceptance criteria:** Physical selected tablet/browser/stylus плюс student camera/gallery: multi-page/fullscreen fallback/zoom/rotation/palm/reopen/history/session; explicit model/version/evidence.
- **Отрицательные тесты:** Touch/pen cancel; palm accidental strokes; EXIF/orientation; 2 pages; resize/expiry/offline; accessibility/focus.
- **Независимость / конфликты:** Протокол независимо; device run после integration. Цена не включает неопределённые аппаратные дефекты.

## После synthetic MVP / отдельные gates

### B21 — Измеренные улучшения сверх MVP

- **Приоритет / статус / роль:** P2 · PARTIAL · Учитель / оператор.
- **User story:** Как пользователь, хочу быстрый длинный history и большие очереди после измеренного роста.
- **Компоненты:** History pagination/lazy photo; content batching; canvas/performance tools. Пути существующих компонентов — в evidence map inventory; «new/future» означает предложение, не существующий файл.
- **Зависимости:** Owner workload/budget; measured regression.
- **Оценка:** 4–10 чел.-дней.
- **Security requirements:** Optimization не снимает authorization/count filters.
- **Acceptance criteria:** Оптимизировать только воспроизведённый bottleneck; retain exact history/visibility; advanced analytics/schedule/CMS не входят автоматически.
- **Отрицательные тесты:** Large history/no leakage/old snapshot/order; cancellation; p95 budget repeatable.
- **Независимость / конфликты:** Можно после MVP; changes только measured component.

### B22 — Разрешить реальные данные отдельным решением

- **Приоритет / статус / роль:** P0 перед пилотом · BLOCKED · Владелец / юрист / security / оператор.
- **User story:** Как владелец, хочу знать, какие условия допускают реальный пилот.
- **Компоненты:** PD/ORG gate register; ADR-L03–10/13/18; legal/freshness/erasure; external human audit/SR-01–04. Пути существующих компонентов — в evidence map inventory; «new/future» означает предложение, не существующий файл.
- **Зависимости:** Synthetic acceptance + отдельное legal/ops/security решение.
- **Оценка:** достоверный диапазон сейчас отсутствует; не входит в сумму реализации.
- **Security requirements:** Repo legal claims не проверялись как актуальная юридическая консультация; gate remains OPEN/BLOCKED.
- **Acceptance criteria:** Named responsible parties; checked basis/representative procedures, real restriction freshness, retention/erasure/restore quarantine; HTTPS/secrets/off-host restore/incident evidence; PILOT AUTHORIZED явно.
- **Отрицательные тесты:** Isolation/erasure/backup restoration/revoke/privacy stop acceptance по принятому реальному scope; external review findings закрыты.
- **Независимость / конфликты:** Подготовка отдельно, не в estimate synthetic MVP и не разрешение отправлять кому-либо данные.

### B23 — PRS1 exact только после reprioritization

- **Приоритет / статус / роль:** P2 parked · BLOCKED · Владелец / security engineer.
- **User story:** Как владелец, хочу сохранять conservative enforcement до доказанного exact механизма.
- **Компоненты:** Prs1Selector/OperationPlan; I2-P D01–D05; provenance/trusted writer/protocol/evaluator future. Пути существующих компонентов — в evidence map inventory; «new/future» означает предложение, не существующий файл.
- **Зависимости:** Новое explicit I2–I5 authorization; independent review.
- **Оценка:** достоверный диапазон сейчас отсутствует; не входит в сумму реализации.
- **Security requirements:** PRS1 EXACT OFF; не расширять allow ради удобства MVP.
- **Acceptance criteria:** Durable provenance/atomic writer/current dependencies/races доказаны до enable; I1 parser не attestation.
- **Отрицательные тесты:** PV/CC/AF/N/RB/RT matrix; inherited grants/TRUNCATE/late attestation/root-only receipt gaps.
- **Независимость / конфликты:** Не critical path synthetic MVP; parked, не начинать.

## Сумма и границы оценки

- P0 B02–B16: **64–109 чел.-дней**.
- P1 B17–B20: **10–20 чел.-дней**.
- Оценимый остаток synthetic MVP: **74–129 чел.-дней**, примерно 14.8–25.8 инженерных недель последовательной работы без ожидания решений.
- **Сверх диапазона:** B01 G02 engineering/proof (неограниченная пока неопределённость), сроки owner/security review и физического оборудования, B22 real pilot, B23 parked PRS1. B21 4–10 чел.-дней — только опциональный после-MVP measured пакет.
- Поэтому дата готовности сейчас **не определена**. После B07a и утверждения B01/B03/B08 contracts пересчитать estimate на меньшие задачи. Не складывать старый M-backlog с этим B-backlog: это альтернативное разбиение одного остатка.

## Покрытие инвентаризации и возможность параллельной работы

| Строки inventory | Backlog |
|---|---|
| A01–A04, A11–A12 | B02/B03/B04/B06/B10 |
| A05–A06; S01–S03 | B04/B05/B02 |
| A07–A10; T02/T08 | B06/B10/B11 |
| T03–T07/T15; S05–S07; X01 | B07/B08/B09 |
| T01; S17; X14 | B01/B02/B15 |
| T09; S04/S16; X06 | B10/B13/B11 |
| T10–T14/T16–T19; S08–S15 | B11/B12/B14/B16/B20 |
| X02–X05 | B15/B19/B20/B16 |
| X07–X09 | B03/B18/B19 |
| X10–X13 | B01/B11/B12/B06 |
| X15 | B17 |
| N01/N04 | Исключены; B21 только измеренные полезные расширения |
| N02/N03 | B23/B22, вне суммы synthetic |

Параллелизм здесь — **возможность для будущего решения**, не разрешение вести parallel agents/worktrees. Текущая инструкция canonical/main sequential сохранена. Относительно независимы B07a (новый compiler + fixtures), подготовка B20 device protocol и B18 ops runbook, исследование B01; они не должны менять общие модели/DI/schema. После freeze contracts можно разнести B14 history page и B13 progress UI, но их shared query/DTO изменения интегрировать последовательно. B03–B06 имеют общие identity/admin tables; B08–B09 — content DTO/schema; B11–B12–B19 — authorization/claims/lifecycle. Их нельзя считать безопасно независимыми только потому, что экраны различны.

**MVP READINESS AUDIT READY FOR OWNER REVIEW · NO RUNTIME CHANGES · PRS1 EXACT OFF · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**
