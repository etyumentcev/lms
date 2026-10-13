# Ограниченный отзыв полномочий преподавателя

> **2026-10-12 HTTP/React cutover follow-up.** Исторические утверждения ниже о неактивированном публичном HTTP и неподключённом ledger сохранены для трассировки прежнего checkpoint. Feature-gated composition теперь подключена к реальному `Program.cs`, а актуальные route/React/M1 границы зафиксированы в [итоговом cutover-отчёте](../../codex/TEACHER-REVOCATION-CUTOVER-RESULTS.md). Browser auth, runtime DB profile, AUTH-01–AUTH-04 и production readiness остаются непринятыми.

> **2026-10-12 — действующее решение владельца: logical deadline, без hard real-time.** Нормативный источник: [решение владельца](../../codex/TEACHER-REVOCATION-OWNER-DECISION.md). JWT преподавателя живёт не более 15 минут; issue/refresh проверяют актуальные полномочия; исходный deadline логической операции не превышает 30 минут; при deadline приложение запрашивает cancellation и после него не подтверждает success. Физическое завершение PostgreSQL `COMMIT`, filesystem/kernel I/O, HTTP egress и cleanup к deadline **не гарантируется и не является acceptance criterion**. Replay/unknown-outcome/access-control/tenant/privacy/processing protections сохраняются. Поздний физический effect — принятый остаточный риск, а не поздний success или разрешение на повтор.
>
> Требования supervisor, worker isolation, hard-real-time fencing, storage/PG/edge quiescence и численных worst-case stop bounds, приведённые ниже, **superseded как нормативные требования Teacher Revocation**. Они сохранены как историческое объяснение предыдущей модели и отрицательные результаты; PASS им не присваивается. Отсутствие их доказательства больше не блокирует readiness само по себе. Логический deadline, cancellation request, no-late-success, fresh issue/refresh, replay/unknown COMMIT и security gates остаются обязательными.

> 2026-10-12 mandatory-context update: the owner authorized removal of legacy teacher write/file compatibility. See [current enforcement matrix](../../codex/TEACHER-CONTEXT-ENFORCEMENT.md). Optional-context statements below describe the prior checkpoint; no public cutover or production/AUTH acceptance is implied.

2026-10-12. Основание: задание AUTH-01–AUTH-04, согласование плана «выполни план» и ответ «2» на выбор модели отказов: **ограниченная модель с проверяемыми условиями эксплуатации**. Это разрешение реализации и тестирования; не production qualification, не HTTP cutover и не разрешение обработки реальных ПД.

## Действующий контракт

- JWT преподавателя действует не более 900 секунд; положительный validator leeway запрещён.
- Issue/refresh заново читают актуальные account/session/membership/grants из авторитетной PostgreSQL. Отозванные полномочия не входят в новые JWT.
- Deadline логической операции неизменяем, начинается от исходного ingress и не превышает 1800 секунд. Retry, refresh и новый request не продлевают ранее зарегистрированную попытку.
- При достижении deadline приложение запрашивает cancellation. После deadline запрещены новые application stages, новый commit dispatch и успешное подтверждение клиенту.
- Уже начатый syscall, `COMMIT`, egress или cleanup может физически завершиться после deadline. Такой исход учитывается как unknown/late fact; он не превращается в своевременный success и не запускает replay.
- Временная модель ordinary revoke: не более 15 минут существующей JWT authority + не более 30 минут логической операции; требование прекращения допуска новых операций применяется в принятой модели 15/30/60 минут, без обещания физической quiescence.

## Сохранённые инварианты

S2/S3 resource ownership, organization/course/group isolation, current ProcessingRestrictions, immutable history, private attachment parent checks, CAS/locks/audit, durable attempt identity, replay prevention and unknown-`COMMIT` handling не ослабляются. Файлы и PostgreSQL не объявляются общей транзакцией. Late ACK фиксирует факт, но не открывает success старого запроса. Реальные ПД и pilot по-прежнему требуют отдельных security/legal/operations gates.

## Историческая редакция 2026-10-12 до упрощения

Следующий текст сохранён для прослеживаемости прежней bounded-physical модели. При конфликте применяется действующий контракт выше.

### Контракт и точная замена прежних требований (исторический)

- Access JWT живёт не более 900 секунд. Истёкший JWT не допускает новую операцию. Положительный validator leeway запрещён.
- Каждый issue/refresh заново читает account/session, OrganizationMembership, TeacherAssignmentScope/TeacherAccess и EnrollmentScope/EnrollmentAccess на авторитетной PostgreSQL. Refresh secret подтверждает только сессию.
- Ordinary revoke фиксирует State=Revoked; новые выдачи сразу исключают эти права. Изменение и issuance сериализуются существующей organization row lock. Права другой организации не затрагиваются.
- Ранее выданный JWT не требует немедленной invalidation, а начатая операция — прерывания из-за ordinary revoke. Current ProcessingRestrictions не относятся к этой отсрочке.
- Общий потолок одной логической операции — 1800 секунд от исходного ingress. Более короткие существующие request budgets 60/480 секунд сохраняются. Новых публичных long-running routes нет.
- Начало отсчёта не переносится за чтение body, queue, lock или регистрацию. Refresh, внутренний retry и новый HTTP request не продлевают исходный cutoff.
- Cancellation запрашивается за 30 секунд до deadline; весь completion reserve входит в исходные 30 минут. После отмены нет новых stages/commit dispatch/success. Исполнитель ожидается до завершения, не отсоединяется через Task.Run/WaitAsync.
- Все действия на основе отозванного разрешения прекращаются в пределах 60 минут **в пределах модели отказов ниже**. При true JWT<=15m и полной duration<=30m фактический worst case <45m от commit отзыва.

Заменены только ordinary temporal clauses T04/ADR-L15: D<=request+2h и exp<=D-10m больше не алгоритм ordinary revoke. Новая миграция сохраняет прежние строки/историю/constraints, но новые записи AccessValidUntil равны RevocationRequestedAt и обозначают маркер отзыва, а не аренду ещё действующих прав. Это время внутри транзакции, не утверждение о времени её commit. Прежние generation/version/CAS/lock/atomic audit сохранены. Legacy ComponentExpiry поддерживает явные ограничивающие deadlines; ordinary issuer их не передаёт.

NR1 N1–N4/D1–D6/D8: неограниченные поздние COMMIT/egress больше не удовлетворяют новому bounded contract. В поддерживаемом envelope они должны завершаться в общем бюджете. За пределами envelope сохраняется безопасная обработка unknown, но временная гарантия объявляется нарушенной, а не PASS. Late ACK никогда не разрешает новый success/JWT/cookie. Уже committed факт не откатывается и не заменяется повторным выполнением.

E01/E02/O01, account block, S3 ownership/generations, ADR-L18 processing checks, PRS1 exact OFF и текущие release gates сохранены. Для general jobs, grants, B08 и публичного HTTP нет автоматического разрешения по этому документу. Принятое 12 октября снятие fs-verity для фотографий остаётся в силе; новый контракт не возвращает physical tampering qualification.

## Условия эксплуатации и границы доказательства

Владелец выбрал ограниченную модель, а не гарантию при произвольном зависании kernel/диска/PostgreSQL. Для включения конкретного пути необходимо установить одновременно:

1. Qualified UTC/monotonic clock имеет подтверждённые bounds; uncertainty учитывается консервативно. Clock rollback/suspend/reboot/потеря qualification закрывают старый контекст. Неизвестная ошибка времени не принимается равной нулю.
2. Планировщик доставляет cancellation, код прекращает stages, PostgreSQL/FS/контролируемый edge завершают уже начатые действия и освобождают ресурсы в суммарном completion reserve <=30s. Длительность до controlled ingress не должна оставаться неучтённой при заявлении полного HTTP bound.
3. Исполнители получают связанный cancellation token; loops имеют конечные объёмы/проверки; нет detached tasks, самостоятельного resume после restart или бесконечных retry.
4. Новый irreversible dispatch нельзя начинать после execution cutoff. DB/FS не объявляются общей transaction. Proxy buffering и уже отправленные transport bytes учитываются отдельно; доставка по внешней сети не отзывается.

30s — параметр условного execution envelope, **не измеренное свойство текущей машины**. Fake-time тест доказывает взаимодействие ранней cancellation, join и запрета публикации, но не bound native open/COMMIT/edge. FIFO-тест, в котором отмена не завершает open, остаётся отрицательным свидетельством вне кооперативного envelope. UnprovenLocalClockVerifier остаётся fail-closed. Production clock/DB/storage/edge qualification не выполнена, G02 production BLOCKED.

Не добавляются Redis, message broker, отдельный gateway или универсальный supervisor. Изоляция уже существующего content importer сохраняется ради недоверенного контента/ресурсных лимитов. Если конкретное I/O не отвечает условиям envelope, следующий минимальный механизм — локальный изолированный исполнитель с узкими правами и наблюдаемым завершением; один SIGKILL не доказывает остановку произвольного kernel I/O.

## Actor → use case → invariants → state

| Actor/use case | Источник и инварианты | Состояния/проверки |
|---|---|---|
| Teacher: issue/refresh | PostgreSQL account/session + fresh membership/grants; один org в JWT; роли не заменяют grants | predecessor consumed → один successor; no-scope допускает identity session без educational JWT |
| Teacher: операция | Valid JWT + server resource ownership/course/group/action + current processing + workflow | исходный deadline; Open → invalidated/expired; старый контекст не оживает |
| Повтор команды | Server-derived org/actor/kind/resource/expectedVersion и digest canonical payload | один immutable durable attempt; повтор даёт только FirstAttempt=false, не новое исполнение |
| Student/history/files | S3 поколения и неизменные parent chains; completed private originals | исторические Submit → Review → Revise → Accept сохранены; файл чужой org/course/group запрещён до storage read |

Проверка ordinary JWT grants во время операции не нужна. Resource ownership, актуальные processing restrictions и domain invariants не удаляются. Новый token не изменяет authority или deadline уже начатой операции.

## Транзакции и повторные попытки

**Refresh:** PostgresIdentitySessions владеет одной READ COMMITTED transaction на dedicated context. Lock order security → Identity user → actor link → session → organization (restricted profile использует существующие security-definer capabilities). После ожиданий используется свежее время. Rotation/generation/CAS/consumption/successor/session audit + fresh grant snapshot + signing выполняются под этими locks; secret result выходит только после ACK и disposal. O01 <=30s conflict без credentials; поздний replay отзывает только session. Нет fresh scope → успешная rotation с AccessToken=null. Ошибка перед commit откатывает всё; lost/late ACK не даёт replay/redisclosure и может требовать нового login.

**Attempt reservation:** PostgresTeacherOperationAttempts владеет отдельной короткой READ COMMITTED transaction до domain/file effects. Existing organization lock сериализует регистрацию; unique actor/org/command key и unique actor/org/kind/resource/expectedVersion предотвращают повтор со сменой key. Payload mismatch → conflict. Immutable row хранит original deadline с округлением вниз до PostgreSQL microseconds. ACK регистрации не является domain success. Поздний или неизвестный ACK не запускает исполнителя. Каждая следующая попытка (включая restart/refresh) — только FirstAttempt=false; resume/renew/delete нет. Потеря процесса после резервирования может привести к отсутствию domain effect без автоматического retry: это осознанный минимальный at-most-once attempt contract.

Этот ledger не authorizer и не result resolver. Он принимает **trusted host input**, не DTO клиента, и не выдаёт actor/scope права. До регистрации host должен авторизовать caller/resource; перед domain mutation необходимо повторить current processing/workflow проверки внутри её transaction. Для команд без естественного aggregate expectedVersion требуется отдельный intent contract; их нельзя подключать с выдуманным version=0. Retention/cleanup записей запрещены до определения replay horizon.

**Domain writes:** текущие domain CAS/history/claim и processing protocols сохранены. PostgresWorkStore принимает исходный deadline, проверяет его до domain persistence/COMMIT и перед возвратом; DispatchCommit сохраняет Unknown до ACK, late ACK записывает committed факт, но не открывает success. AttachmentService принимает тот же deadline и запрещает late eligibility/metadata/success, LocalFileAttachmentStorage проверяет cancellation перед final rename; отменённый staging остаётся private для отдельной cleanup. Optional constructor arguments сохраняют старую компонентную совместимость; это не автоматическое включение нового протокола legacy routes. Ledger ещё не подключён к публичным Submit/Review/Message/upload. Поэтому общий durable transaction «domain + result + audit» для всех teacher routes этим изменением не заявляется. Не включать новый route без соответствующей composition и negative matrix. Это явный остаток плана интеграции, а не скрытое выполнение AUTH-03 всеми legacy endpoints.

## Что реализовано и что остаётся

| Область | Реализация |
|---|---|
| JWT 15m | ComponentJwt сохраняет conservative validation; legacy bearer теперь reject long lifetime/missing nbf, ClockSkew=0; demo JWT сокращён до15m |
| Fresh refresh | PostgresComponentTokenIssuer.Refresh использует одну session/authority transaction и исходный deadline |
| Operation30m | TeacherOperation budget, immutable monotonic + UTC cutoff, ранняя cooperative cancellation и запрет позднего результата |
| Retry | SQL attempt identity; concurrent/reconnect/new key не получают второе FirstAttempt; неизвестный commit не rollback |
| Isolated HTTP reads | CourseRequestIngress передаёт deadline-linked token вниз и ожидает handler; parent/current processing/no-store сохранены |
| Ordinary revoke | Аддитивная функция S3 без новой двухчасовой аренды, исторические записи не меняются |
| Domain/file boundaries | Deadline-aware WorkStore/AttachmentService добавлены и проверяются с исходным контекстом; legacy callers без контекста ещё требуют composition |
| General jobs / all legacy writes | Не подключены к новому operation protocol; общий полный AUTH-03 NOT PROVEN |
| Public refresh/switch/login, cutover | Не включены; secure cookies/Origin/CSRF/browser coordination требуют отдельной законченной HTTP composition |
| Production physical bound | NOT PROVEN: требуется qualification выбранного clock/DB/FS/edge envelope |

План не считается полностью принятым только по зелёным компонентным тестам. Интеграция legacy write/file paths и production qualification остаются обязательными до общей приёмки.
