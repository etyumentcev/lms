# L0.1 — Инвентаризация персональных данных

Дата 2026-10-09. FACT относится к исходникам `b1e4084`, не к обследованию реального сервера. Сроки/основания ниже — PROPOSAL / DECISION REQUIRED. Правовой источник для всех категорий: [R01–R20](PD-LEGAL-REQUIREMENTS.md), **LEGAL SOURCE NOT VERIFIED**. Для каждой цели B см. [реестр оснований](PD-PROCESSING-BASES.md). ПД/ИБ/ops назначаются через PD-G10/13.

FACT: отдельных Student/Teacher profiles и IdentityUser persistence нет. Actor — `Actor(Guid Id, AuthorRole Role)` в Application. Enrollment реализован как AccessEnrollments. Course/Lesson — опубликованный ContentJson и AccessLessons, а не редактируемые профили людей. Прогресс вычисляется запросами, не отдельная таблица аналитики. UUID, timestamps, связи курса и почерк позволяют связать запись с человеком.

Источники: [History](../../src/Lms.Domain/History.cs), [Assignment](../../src/Lms.Domain/ProblemAssignment.cs), [ProblemWork](../../src/Lms.Domain/ProblemWork.cs), [контекст/схема](../../src/Lms.Infrastructure/LmsDbContext.cs), [AccessFacts](../../src/Lms.Infrastructure/AccessFacts.cs), [StoredAttachment](../../src/Lms.Infrastructure/StoredAttachment.cs), [AttachmentService](../../src/Lms.Infrastructure/AttachmentService.cs), [frontend client](../../src/Lms.Web/src/api/client.ts), [Compose](../../docker-compose.yml), [Identity proposal](../adr/ADR-L01-identity-integration.md).

Общие сроки (не численные обещания): T1 — время необходимой идентификации/договора; T2 — учебная цель и отдельно обоснованный архив; T3 — короткое окно pending; T4 — конечный период безопасности; T5 — срок backup, совместимый с уничтожением; T6 — правовая доказательственная цель. Значения, старт отсчёта, исключения и hold-owner утверждаются PD-G07; до этого нет правила «хранить вечно». Общий канал передачи X1 — same-origin API ↔ разрешённый браузер плюс внутренние DB/files; X2 — только уполномоченные ПД/ops; X3 — отдельный согласованный backup в РФ. Все физические адреса UNKNOWN, каналы будущего пилота PROPOSAL.

## I01 IdentityUser / mapping / сессии — PROPOSAL

| Поле | Содержание |
|---|---|
| Data category | Минимальный login, password hash, stamp, lockout; IdentityUserId↔ActorId, role/active; хеш session/challenge и сроки. Email/phone defaults не означают их сбор |
| Subject | Ученик, преподаватель |
| Purpose | B01/B02/B09: вход, доступ, безопасность |
| Legal basis | Кандидат основания по указанному B; утвердить PD-G02/03, не считать consent универсальным |
| Collection | Оператор по проверенному roster, пароль задаёт субъект |
| Storage | Будущие AspNetUsers/ActorIdentities/IdentitySessions в PostgreSQL РФ; сейчас отсутствуют |
| Access | Субъект — свой вход; issuer — минимум; credential hashes не Teacher |
| Retention | T1 + T4/T6 отдельно |
| Correction | Login/contact только проверенным процессом; ActorId не перепривязывать |
| Deletion | Отозвать сессии/коды, удалить credentials/mapping по ADR-L10 после обработки зависимостей |
| Transfer | X1/X2/X3; без обязательного SMTP |
| Evidence | Schema allowlist, audit provisioning, тест revoke/replay |

## I02 Student/Teacher/ActorId — FACT и минимальный roster PROPOSAL

| Поле | Содержание |
|---|---|
| Data category | StudentId, TeacherId, AuthorId, SubmittedBy, OwnerId UUID; отдельных person tables нет; имена DTO nullable |
| Subject | Ученики, преподаватели |
| Purpose | B01/B02/B03: авторство и scope |
| Legal basis | Кандидат основания по указанному B; утвердить PD-G02/03, не считать consent универсальным |
| Collection | Доверенный seed сейчас; в пилоте проверенный issuer |
| Storage | Assignment/history/access; внешний roster тоже ПД в контуре ООО |
| Access | Свои записи / scoped Teacher; roster только уполномоченным |
| Retention | T1/T2 |
| Correction | Исправить ошибочную привязку через разбор инцидента, не переносить историю другому человеку |
| Deletion | Обойти все ссылки UUID, тексты, фото и копии; замены имени недостаточно |
| Transfer | X1/X2/X3, внешний roster отдельно регистрировать |
| Evidence | Инвентарь всех ID, отсутствие автоматического backfill |

## I03 Enrollment — FACT

| Поле | Содержание |
|---|---|
| Data category | AccessEnrollments StudentId/CourseId/GroupId/IsActive; AccessTeachers TeacherId/CourseId/GroupId; null group = весь курс |
| Subject | Ученик; teacher grants о преподавателе |
| Purpose | B03: участие и разграничение |
| Legal basis | Кандидат основания по указанному B; утвердить PD-G02/03, не считать consent универсальным |
| Collection | Trusted provisioning; публичного CRUD нет |
| Storage | PostgreSQL access tables |
| Access | Issuer и backend; учитель только в scope; ученик своё |
| Retention | T1/T2 |
| Correction | Изменение grants/membership с audit/revoke |
| Deletion | Отзыв прав не erasure; удалить access records при прекращении основания |
| Transfer | X1/X2/X3 |
| Evidence | Cross-group отрицательные tests; повторная сверка после restore |

## I04 Course/Lesson — FACT

| Поле | Содержание |
|---|---|
| Data category | PublishedContentVersions ContentJson, version; AccessLessons availability. Название группы/свободный текст могут содержать ПД |
| Subject | Обычно не о субъекте; авторы/ученики если внесены в текст |
| Purpose | B03/B04: выдача контента |
| Legal basis | Кандидат основания по указанному B; утвердить PD-G02/03, не считать consent универсальным |
| Collection | Публикация учебного контента |
| Storage | JSONB PostgreSQL и исходные Markdown в repo |
| Access | Ученик по enrollment; Teacher по scope |
| Retention | Неперсональный контент вне ПД retention; случайные ПД — T2 |
| Correction | Опубликовать новую версию; старый immutable snapshot не исправлять незаметно |
| Deletion | При ПД в snapshot — ADR-L10 отдельный manifest/разбор общих ссылок |
| Transfer | X1/X3; Git remote отдельно если содержит ПД |
| Evidence | Контент review, запрет имён в учебных fixtures/репозитории |

## I05 ProblemAssignment/ProblemWork/прогресс — FACT

| Поле | Содержание |
|---|---|
| Data category | StudentId, exact content ref, assignment status; work status/version/AcceptedAt; вычисляемый прогресс |
| Subject | Ученик |
| Purpose | B03/B07/B08 |
| Legal basis | Кандидат основания по указанному B; утвердить PD-G02/03, не считать consent универсальным |
| Collection | Назначение оператором и команды ученика/учителя |
| Storage | ProblemAssignments/ProblemWorks; проекции в памяти |
| Access | Свой ученик и scoped Teacher |
| Retention | T2 |
| Correction | Access metadata отдельно; оценку/terminal state не переписывать как profile edit |
| Deletion | Согласованный граф уничтожения; неперсональные агрегаты лишь после оценки идентифицируемости |
| Transfer | X1/X3 |
| Evidence | Manifest объектов, отрицательные read tests, отсутствие строк в queue после удаления |

## I06 Submission — FACT

| Поле | Содержание |
|---|---|
| Data category | SubmittedBy, timestamp, AttemptNumber, BodyMarkdown, ordered AttachmentIds |
| Subject | Ученик; третьи лица в содержимом |
| Purpose | B04/B05/B07 |
| Legal basis | Кандидат основания по указанному B; утвердить PD-G02/03, не считать consent универсальным |
| Collection | SubmitSolution |
| Storage | Submissions PostgreSQL, файлы отдельно |
| Access | Свой ученик/scoped Teacher |
| Retention | T2 |
| Correction | Immutable; новая попытка только по workflow, правовой запрос отдельно |
| Deletion | Привилегированное уничтожение графа ADR-L10; не DELETE обычного API |
| Transfer | X1/X3 |
| Evidence | Trigger regression, выгрузка поиска всех body/ID после erasure |

## I07 Review и annotations — FACT

| Поле | Содержание |
|---|---|
| Data category | TeacherId, SubmissionId, decision, MessageId, date; JSONB page/stroke/points/pressure/color. Почерк/рисунки могут идентифицировать |
| Subject | Преподаватель и ученик |
| Purpose | B06/B07 |
| Legal basis | Кандидат основания по указанному B; утвердить PD-G02/03, не считать consent универсальным |
| Collection | Review command |
| Storage | Reviews.Annotations JSONB; overlay в браузере |
| Access | Свой ученик/scoped Teacher |
| Retention | T2 |
| Correction | Review и annotations immutable; никаких тихих UPDATE |
| Deletion | Удаление связанных Reviews/annotations по manifest; teacher erasure затрагивает чужие work и требует проверки основания |
| Transfer | X1/X3 |
| Evidence | Нет orphan annotation; linked file/read denial; чужие работы не удаляются автоматически |

## I08 Message/feedback — FACT

| Поле | Содержание |
|---|---|
| Data category | AuthorId/Role, BodyMarkdown, reply chain, attachments/date; возможны телефон, ФИО, специальные сведения |
| Subject | Ученик, преподаватель, упомянутые третьи лица |
| Purpose | B06/B07 |
| Legal basis | Кандидат основания по указанному B; утвердить PD-G02/03, не считать consent универсальным |
| Collection | PostMessage либо feedback при review |
| Storage | Messages PostgreSQL |
| Access | Участники в текущем scope |
| Retention | T2 |
| Correction | Immutable; уточнение отдельным сообщением только если достаточно по праву, иначе исключительная процедура |
| Deletion | Manifest учитывает ReplyToMessageId и Review.MessageId; убрать цитаты/производные |
| Transfer | X1/X3 |
| Evidence | Поиск персональных canary в Markdown, replies и review проекциях |

## I09 Attachment/оригинал/EXIF — FACT

| Поле | Содержание |
|---|---|
| Data category | FileName (до 512), OwnerId, CreatedAt, MediaType/Size, StorageKey, link/deleted; исходные JPEG/PNG с возможным EXIF/GPS/лицами/именами |
| Subject | Ученик, преподаватель, случайные третьи лица |
| Purpose | B05/B06: решение/обратная связь |
| Legal basis | Кандидат основания по указанному B; утвердить PD-G02/03, не считать consent универсальным |
| Collection | Upload, исходные байты сохраняются; EXIF stripping отсутствует |
| Storage | StoredAttachments + AccessAttachments; private filesystem вне webroot; bytes в RAM при upload |
| Access | Pending owner; attached — parent-work policy, даже для uploader |
| Retention | T3 пока pending; T2 после claim. Сейчас ExpiresAt=MaxValue, автоматического retention нет |
| Correction | Имя/байты нельзя переписать по текущим triggers; новый снимок до submit, exception после |
| Deletion | Обычный pending DELETE оставляет metadata/tombstone; полное уничтожение требует metadata+bytes+копии+EXIF |
| Transfer | X1/X3; previews локальные Blob |
| Evidence | Проверки metadata, file absence, storage/provider deletion; no-store не отзывает скачанное |

## I10 DomainEvents / audit — FACT / PROPOSAL

| Поле | Содержание |
|---|---|
| Data category | DomainEvents Payload JSONB, WorkId/time/type/version содержит IDs; не полноценный security audit. Новый audit: action/outcome/caseId/minimum target |
| Subject | Ученики, преподаватели; будущие администраторы |
| Purpose | B07/B09, доказательства |
| Legal basis | Кандидат основания по указанному B; утвердить PD-G02/03, не считать consent универсальным |
| Collection | Транзакционное событие; будущие security hooks |
| Storage | PostgreSQL; отдельный защищённый audit sink предложен |
| Access | Backend; ИБ/ПД ограниченно; не Teacher |
| Retention | T2 для domain, T4/T6 для audit |
| Correction | Append-only; ошибку отмечать новым событием |
| Deletion | События learning в графе уничтожения; evidence удерживать лишь на отдельном основании, без копии work |
| Transfer | X2/X3 |
| Evidence | Тест полноты graph traversal и минимальности log export |

## I11 IP/session/device/auth/application logs — FACT risk / PROPOSAL

| Поле | Содержание |
|---|---|
| Data category | Proxy IP/URL/agent по runtime config; exceptions/traceId, attachment ID. Session ID будущий; отдельного device fingerprint нет |
| Subject | Все посетители и администраторы |
| Purpose | B09: диагностика/безопасность |
| Legal basis | Кандидат основания по указанному B; утвердить PD-G02/03, не считать consent универсальным |
| Collection | HTTP/proxy/default runtime logging; финальная среда UNKNOWN |
| Storage | stdout/container/host logs; monitoring ещё не настроен |
| Access | Ops/ИБ; не преподаватели |
| Retention | T4, значения UNKNOWN |
| Correction | Исправление audit новым событием, без фальсификации |
| Deletion | Ротация/удаление index/archive; targeted suppression по законному запросу, отдельный hold если обоснован |
| Transfer | X2/X3; внешние error tracking не подключать без gate |
| Evidence | Canary secrets/body/IP/URL tests, audit конфигурации proxy и log agents |

## I12 Frontend/browser storage — FACT

| Поле | Содержание |
|---|---|
| Data category | React drafts, response data, Blob URL; demo bearer в sessionStorage только local adapter; устойчивого draft store в src не найдено |
| Subject | Ученик/преподаватель |
| Purpose | B01/B04/B06: работа UI |
| Legal basis | Кандидат основания по указанному B; утвердить PD-G02/03, не считать consent универсальным |
| Collection | API и ввод/выбор файла |
| Storage | RAM/браузер; demo sessionStorage; будущая Secure HttpOnly cookie |
| Access | Пользователь устройства; shared-device риск |
| Retention | Сессия/экран; длительный persistence не предложен |
| Correction | Черновик до отправки редактируем |
| Deletion | Abort/remount/Blob revoke/logout/cross-tab; нельзя гарантировать стирание пользовательских screenshot/download |
| Transfer | X1, локальные скачанные копии вне server erasure |
| Evidence | Account-switch/back button/session restore тест; no-store на чувствительных ответах |

## I13 Backups/WAL/snapshots — PROPOSAL

| Поле | Содержание |
|---|---|
| Data category | DB+файлы+Identity+manifest/keys; logs если включены; snapshots провайдера/WAL тоже копии |
| Subject | Все субъекты исходного набора |
| Purpose | B10: восстановление |
| Legal basis | Кандидат основания по указанному B; утвердить PD-G02/03, не считать consent универсальным |
| Collection | Coordinated backup; operational backup в коде отсутствует |
| Storage | Отдельное off-host РФ, destination UNKNOWN |
| Access | Backup operator и отдельный key custodian |
| Retention | T5 согласовать с R11/R12, не скрытый бессрочный архив |
| Correction | Только через восстановление в карантине и reconciliation |
| Deletion | Удалить/пересобрать затронутые sets либо допустимый блокированный остаток до законного deadline; key deletion требует доказательства всех копий |
| Transfer | X3, договор и место всех replicas |
| Evidence | Restore deleted canary, latest ledger watermark, provider deletion proof |

## I14 Development/test fixtures — FACT

| Поле | Содержание |
|---|---|
| Data category | LocalDevelopment seed; Testcontainers и AttachmentImages; настоящие фото в fixtures не требуются |
| Subject | Synthetic UUID/тексты/генерируемые изображения |
| Purpose | Тесты, без реальных субъектов |
| Legal basis | Кандидат основания по указанному B; утвердить PD-G02/03, не считать consent универсальным |
| Collection | Генераторы/тесты |
| Storage | Repo, isolated DB/files, K evidence; реальные среды не обследованы |
| Access | Разработчики |
| Retention | Project lifecycle для synthetic; реальные ПД запрещены до отдельного основания |
| Correction | Изменяемые fixtures |
| Deletion | Disposable reset только isolated fixtures; never copy real dump into tests |
| Transfer | CI/репозиторий: только synthetic |
| Evidence | Fixture source review, no production dump policy, secret/data scan |

## I15 ФИО/email/телефон/возраст/представитель — UNKNOWN / PROPOSAL

| Поле | Содержание |
|---|---|
| Data category | ФИО/контакт только при необходимости. DOB/адрес/паспорт не добавлять по умолчанию. Возрастная группа и проверка полномочия — минимальный кандидат |
| Subject | Ученик, учитель, представитель |
| Purpose | B01/B02/B03; доказательство B consent |
| Legal basis | Кандидат основания по указанному B; утвердить PD-G02/03, не считать consent универсальным |
| Collection | Контролируемое подтверждение; не публичная форма с документами |
| Storage | Отдельный защищённый basis/representative register; сейчас нет |
| Access | ПД/issuer; Teacher не получает документы родителя |
| Retention | T1/T6, копия документа не хранится автоматически |
| Correction | Проверить изменение контакта/представителя; новое подтверждение |
| Deletion | Удалить связь/контакты по основаниям; evidence с отдельным retention |
| Transfer | X2/X3; email delivery только после PD-G05 |
| Evidence | Field-by-field necessity review, отсутствие лишних столбцов/DTO |

## Решения минимизации

Не собирать паспорт родителя, дату рождения, телефон, биометрию или device fingerprint только потому, что это удобно Identity. Фото не биометрические ПД автоматически: критерий — обработка физиологических/биологических характеристик для установления личности, а не формат JPEG. Свободный текст способен содержать чувствительные данные без выделенного поля: предусмотреть жалобу, блокирование и уничтожение; договорный запрет ввода не устраняет обязанностей после фактического получения.

Для фото PD-G04 выбирает: (а) сохранение исходных байтов с обоснованной необходимостью metadata и минимизацией при съёмке; (б) новый pipeline очищенной копии до принятия immutable оригинала с отдельным ADR/контрактом и проверкой orientation/annotations. Вариант (б) меняет U1 и не реализуется здесь. Предупреждение пользователю само по себе не доказывает минимизацию EXIF.
