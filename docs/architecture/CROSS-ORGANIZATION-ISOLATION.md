# L0.2 — Authorization isolation

**ACCEPTED ORG-G04 (L0.3); технические механизмы PROPOSED.** Требования ниже — будущие acceptance criteria, не PASS. [Фактические зависимости](ORGANIZATION-MODEL.md).

## Policy и scope resolution

`Allow = authenticated active session AND active membership AND resource boundary matches AND educational operation grant AND object ownership/course/group permission AND processing permission AND workflow invariant`.

Platform actions используют отдельный набор полномочий. Нельзя добавить `Administrator OR ...` к формуле. Student видит только собственные работы; отдельное разрешение на чужую работу потребует нового use case, в пилоте такого разрешения нет. Teacher ограничен назначенными курсами/группами внутри organization. Course-wide grant сохраняет возможность работать с историей независимо от active enrollment ученика, но не обходит membership преподавателя, organization suspension или privacy block. Export — отдельное право и manifest, не побочный эффект Teacher role.

Для direct ID сервер сначала загружает минимальные trusted scope metadata, проверяет membership/permission и лишь затем контент/history. Для list/queue/count/progress фильтрует разрешённый scope **до** поиска, pagination, totals и aggregates. Route/header/body OrganizationId — только кандидат для сопоставления; не authority. В пилоте collection/upload context определяется сервером по единственному разрешённому контексту; неизвестный или неоднозначный scope отклоняется. Jobs/provisioning/export также проходят scope boundary.

[PostgresAccessPolicy](../../src/Lms.Infrastructure/PostgresAccessPolicy.cs) проверяет course/group/owner без organization. [WorkspaceQueries](../../src/Lms.Infrastructure/PostgresWorkspaceQueries.cs) имеет отдельные LINQ guards; [ReviewQueue](../../src/Lms.Infrastructure/PostgresReviewQueue.cs) использует raw SQL, который EF global filters не защищают. [WorkStore](../../src/Lms.Infrastructure/PostgresWorkStore.cs) загружает по ID; scope-aware load/save и commit-time permission требуются отдельно. API `[Authorize]`/JWT подтверждают личность, не организацию. Новый resolver должен покрывать все эти пути и оба GET work aliases, не только команды.

## Persistence и файлы

Course registry с глобально уникальным CourseId и неизменяемым OrganizationId служит единственной границей корня. FK от assignments/content/access lessons/enrollments/grants к registry, membership consistency для student/teacher и composite scoped references либо проверяющие triggers должны исключать cross-boundary insert. Конкретная схема — ORG-G01/02: FK CourseId сам по себе не проверяет наличие membership actor в нужной организации. Недопустимые прямые DB inserts должны отклоняться; application checks нужны дополнительно. GroupId всегда course-local. Смена membership не переписывает historical author, inactive membership остаётся для ссылочной целостности на законный срок.

Work/Submission/Review/Message/Events наследуют scope через существующие FK; Review→Submission и ReplyTo уже связаны work composite FK. UUID arrays attachments и annotations JSONB не защищены обычным organization FK: проверять parent+scope при claim/review внутри транзакции и добавить защищённые constraints/trigger для обхода application. Не дублировать legal fields на каждой строке. Query filters — дополнительный механизм, не единственная защита. RLS возможен позже, не объявляется реализованным или обязательным для MVP.

Pending attachment binding = uploader ActorId + OrganizationId + policy version; проверяются membership, purpose и quota уже при upload. Нельзя upload в A, затем claim в B тем же actor. При claim scope файла должен совпасть с assignment; повторный claim запрещён, проверка и запись атомарны. Attached GET проходит parent work policy для всех, даже uploader. Pending GET/DELETE проверяют owner **и** active scope; cleanup после revoke действует отдельным service permission, не отозванным пользовательским правом. Storage keys закрыты, прямых публичных URL/долгоживущих bearer links нет; каждый старый preview URL повторно авторизуется. Для response/cache — no-store, frontend Blob cleanup; уже полученные bytes не отзываются.

## Отзыв, блокирование и исключительный доступ

После фиксации revoke следующая проверка обязана видеть новую версию; TTL-кэш разрешений без invalidation не подходит. Перед commit command проверяет scope под общим с revoke lock/version protocol. Завершение административной операции revoke подтверждается только после barrier/drain ранее допущенных requests/streams; до этого статус «отзыв выполняется». При гонке либо действие завершилось до effective revoke, либо отклонено, но не записано после подтверждённого revoke. Уже доставленные данные не исчезают с устройства. Этот более строгий будущий контракт уточняет прежнее предупреждение L0 о in-flight requests, а не утверждает, что текущий код умеет отмену.

| Операция | Platform Management | Education Management |
|---|---|---|
| Infrastructure/config/monitoring | Назначенный ops, минимум metadata | Нет по educational роли |
| Создание Organization | Отдельное platform permission по approved onboarding | Не создание чужой организации |
| Account maintenance | Только отдельно назначенная глобальная security maintenance роль, без impersonation/просмотра работ | Уполномоченный администратор организации выдаёт accounts и управляет roster/scopes своей организации |
| Курсы/группы/зачисление/учителя | Нет автоматически | Organization manager в своём scope |
| Задачи/review/progress/materials | Нет автоматически | Teacher grant; student ownership где применимо |
| Backup/restore/incidents | Отдельные ops полномочия/договор, private encrypted sets | Не доступ к общему dump |
| Export/erasure | Отдельный executor по manifest | Решение уполномоченного оператора в пределах цели |

Break-glass: verified ticket/incident и цель, решение уполномоченного controller (либо заранее договорно разрешённый emergency порядок), конкретные organization/resources/actions, отдельная временная capability, независимый reviewer, expiry/revoke, audit начала/чтений/экспорта/окончания, последующая проверка. Read-only по умолчанию; impersonation и просмотр всех организаций не нормальная поддержка. При экстренном блокировании ИБ действует в заранее установленных полномочиях, не получает blanket read. DB/root access технически может обходить app: разделить credentials, хранить выдачу под контролем, фиксировать maintenance audit; приложение само по себе не обеспечивает запрет для root. Результаты поддержки нельзя отправлять в внешние tickets вместе с работами детей.

## Synthetic negative / attack matrix для L1–L4

| Сценарий | Ожидаемое доказательство |
|---|---|
| Student A запрашивает чужой work/assignment A и B | Нет content/history/permissions/metadata; известный Forbidden/NotFound по контракту, без названия чужой организации |
| Teacher A, включая whole-course, читает/reviews/messages B | Отказ, ни review, ни message, ни Version increment |
| Подмена course/group/student/organization IDs в query/body/header | Отказ либо пересечение с allowed set; totals/search не раскрывают B |
| Один Actor в A+B, pending A claim в B | Атомарный отказ, file остаётся в A; parent не меняется |
| Foreign annotations attachment / reply / submission | Отказ и unchanged aggregate; DB bypass insert тоже отклонён |
| Revoke membership/grant при открытом экране/скачивании/command | Новые запросы/старый URL запрещены; barrier/version race test; B остаётся доступна |
| Privacy block при whole-course grant | Work, queue, progress, files и export не обходят block |
| Platform-only account, self-grant, истёкший break-glass | Учебный доступ запрещён; попытки audit; issuer не повышает себя |
| Export A / erase A / прекращение договора A | Нет rows/files/manifest B; unchanged B sentinel/hash и доступ B |
| Restore старого snapshot с A revoked/erased | Current ledger применяется до ingress; stale session запрещена |
| Orphan course/file/context, stale cached grants | Fail closed; нет fallback к global tenant |
| Реальный SQL queue/count и прямые URL во всех API aliases | Scope до pagination/summary; не только mocked policy тест |

L1 доказывает Identity/membership/read/write/file scope и гонки отзыва на real PostgreSQL/HTTP; L2 — квоты/cleanup; erasure только после legal review, claim races уже L1, L3 — maintenance/restore, L4 — сквозной packet. Тесты должны иметь две synthetic организации, совпадающие локальные имена групп, одного общего actor и отдельного outsider. Никакие результаты прежних тестов не доказывают эту матрицу.

## L0.3 — детализация реализации

Принятые инварианты не означают реализованные controls. [План L1](../../codex/L03-L1-IMPLEMENTATION-PLAN.md) задаёт PROPOSED schema, use cases, concurrency и тесты; [readiness](../../codex/L03-L1-READINESS.md) отделяет технические решения от правовых gates. Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY.
