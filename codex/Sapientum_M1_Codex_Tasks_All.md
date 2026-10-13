# Все задачи M1 для Codex — Sapientum

<!-- Файл: 00_README.md -->

# Sapientum — задания для Codex до M1

**Версия:** 12.10.2026 · **Цель:** `SYNTHETIC MVP ACCEPTED`, не пилот с реальными персональными данными детей.

## Результат M1

В настоящем React-приложении без dev bearer и ручных SQL-операций: оператор и администратор создают учебную организацию, группу и участников; преподаватель публикует Markdown-курс и назначает задачи; ученик с телефона видит материалы, отправляет две фотографии A4; преподаватель просматривает оригиналы, рисует замечания, возвращает на доработку и принимает работу; история, прогресс и файлы доступны после перезапуска. Все действия защищены server-side scope и проходят автоматизированный браузерный E2E на **синтетических данных**.

## Как запускать

1. Работать в каноническом checkout `~/github/lms`; убедиться в фактическом пути и текущем состоянии `git` перед изменениями. Снимки документации от 10–12 октября — ориентир, а не доказательство текущего HEAD.
2. **Сначала передать только** `01_M1_00_SCOPE_AND_OWNER_DECISIONS.md`. Он заканчивается предложениями владельцу и не разрешает правки runtime.
3. После письменного принятия решений и явного разрешения соответствующего среза передавать **по одному** файлы `02`–`12` в указанном порядке. При каждом задании прикладывать `M1_COMMON_RULES.md` или ссылаться на его копию в репозитории.
4. При каждом завершении требовать работающий вертикальный сценарий, код, тесты и файл доказательств. Не считать отчёт о планировании выполненной реализацией.
5. Если задача блокируется отсутствующим owner-решением, Codex формирует **один конкретный decision request** с минимальными вариантами; не создаёт скрытые обходы и не начинает незапрошенное исследование.

## Зависимости

`M1-00 → M1-01 → M1-02 → M1-03 → M1-04 → M1-05 → M1-06 → M1-07 → M1-08 → M1-09 → M1-10 → M1-11`.

Некоторые контрактные и тестовые части `M1-03`–`M1-06` допускают параллельную подготовку, но **не параллельное изменение одних и тех же runtime contracts, схем и API**. После каждого среза обновлять `codex/M1-PROGRESS.md` с фактической цепочкой UI → API → Application → DB/files и статусами `DONE / PARTIAL / BLOCKED`.

## Что уже существует — не переделывать без доказанной необходимости

- Domain workflow `Submit → Review → Revise → Accept`, CAS, immutable история.
- Шесть основных экранов, очередь проверки, фото-first upload/viewer и аннотации canvas.
- S1–S3 / S4 компоненты личности, сессий и scope; внутренние `PostgresTeacherCommands`, `TeacherOperationContext`.
- B07a компилятор Markdown и B07b-C1 изолированный ZIP-импортёр (74/49 успешных компонентных тестов в отчётах).
- Текущие ограничения ProcessingRestrictions и `PRS1 exact OFF` — не активировать exact как условие M1.

## Ограничение определения M1

M1 — воспроизводимый **синтетический** учебный процесс, а не право использовать реальные данные детей. Отдельная M2 требует юридической и эксплуатационной готовности, 152-ФЗ и решения о запуске пилота. Не подставлять реальные списки детей, фотографии их работ или реальные секреты в тесты/демо.

## Структура пакета

- `M1_COMMON_RULES.md` — общие требования ко всем заданиям.
- `01_...`–`12_...` — отдельные готовые промпты для последовательной передачи Codex.
- `M1_E2E_ACCEPTANCE.md` — единая приёмочная матрица для финального задания.

---

<!-- Файл: M1_COMMON_RULES.md -->

# Общие инструкции Codex для всех задач M1

Ты работаешь над Sapientum LMS в **существующем** репозитории `~/github/lms`. Сначала выясни реальный checkout, текущую ветку/HEAD, staged/unstaged/untracked, миграции и существующую реализацию. Прочитай `AGENTS.md`, действующие owner decisions/ADR и `codex/SAPIENTUM-MVP-ENGINEERING-GUARDRAILS.md`, `codex/MVP-READINESS-INVENTORY.md`, `codex/MVP-BACKLOG.md`, а также более новые результаты соответствующего компонента. Отдельные имена и пути в задании — ориентиры; не выдумывай отсутствующие контроллеры/endpoint'ы.

## Приоритет и право на изменения

1. Принятые решения владельца и инварианты выше этой карточки. **Задача не изменяет сама по себе уже принятые security-нормы.** Изменения нормативных ограничений, новый exposed HTTP API, role/capability, schema/migration или production DI, которые сейчас запрещены, возможны только после явного owner approval с точным scope.
2. Не удаляй/не откатывай чужие или незакоммиченные изменения. Никаких переключений ветки, worktree, `reset --hard`, clean, rebase, merge, commit, push или deploy без отдельного указания.
3. Реализуй минимальный вертикальный slice **от действия в UI до сохранённых данных**, а не отдельный макет, тест harness или новую абстрактную архитектуру.
4. Переиспользуй принятые S2/S3 полномочия, модель Course/Group/Enrollment/Assignment, `ProblemWork`, существующие UI, компилятор/импортёр. Не создавай параллельные роли, логин, file service, состояния review или importer.
5. **Нельзя** обходить fail-closed, консервативный ProcessingRestrictions gate, подменять отсутствие сведений на Allow, доверять клиентским role/org/owner/parent, выдавать файлы по публичным путям или отдавать учительские решения ученику. Counts, pagination и агрегаты фильтруются сервером до materialization.
6. Пиши только additive migration без изменения/перезаписи истории. У каждой мутации зафиксируй transaction owner, lock order, CAS, commit point, retry и unknown outcome; не повторяй неизвестную мутацию автоматически. Таймаут не означает rollback.
7. Для M1 применяй синтетические данные; никакого использования реальных персональных данных и никакого объявления `PILOT AUTHORIZED`.

## Единый порядок работы

- **A. Короткий контракт (до страницы):** Actor → use case → UI → commands/queries → resources → invariants/state → scope/processing/parent matrix → transaction + failure semantics → тесты. Действительные owner gate/конфликты выпиши до кода.
- **B. Реализация:** узкий сквозной сценарий, минимальные исправления; не увеличивай scope за счёт рефакторинга, дизайна новых экранов или нового инфраструктурного proof-проекта.
- **C. Проверка:** релевантные unit, integration, API, React, Browser E2E и отрицательные тесты org/group/actor/parent, ошибки/повторы/expiry. Передай точные команды, counts и exit status. Если тест не запускался — `NOT RUN`, если фактически падает — `FAIL` с причиной; не маскируй и не снимай assertion только ради зелёного цвета.
- **D. Итог:** рабочая пользовательская демонстрация с указанием шагов; `changed-files`; изменённые контракты/миграции; регрессия; известные недостатки; ссылка на evidence; обновлённый `codex/M1-PROGRESS.md`. Маркировки `COMPONENT PASS`, `UI COMPLETE`, `E2E ACCEPTED`, `SYNTHETIC MVP ACCEPTED`, `PILOT AUTHORIZED` не взаимозаменяемы.

## Общее правило остановки

Если новая задача противоречит действующему owner decision или нуждается в запрещённом gate, остановись **только перед соответствующей запрещённой частью**, сохрани сделанные допустимые изменения и представь: (а) точный конфликт/доказательство, (б) минимальный вариант решения владельца, (в) что можно завершить внутри текущего scope. Не выдавай остановку за выполнение задания. Не запускай дополнительную многонедельную proof-ветку по умолчанию.

---

<!-- Файл: 01_M1_00_SCOPE_AND_OWNER_DECISIONS.md -->

# M1-00 — Зафиксировать исполнимый scope и нормативные решения владельца

**Тип:** аудит + предложения owner; **НЕ implementation authorization**. **Приоритет P0.**

## Цель

Устранить конфликт между задачей M1 (работающий кружок на синтетических данных) и техническими gate, требующими доказательств физического прекращения операций, физической неизменяемости оригиналов и идеальных транспортных таймингов. Создать конечный и проверяемый контракт для следующих задач, сохраняя обязательную защиту детских данных.

## Действия

1. Прочитать и сверить текущие нормативные тексты/owner register: G02, T04, E03, PRS1, S4, STORAGE-SEAL, STORAGE-PROFILE-01, `TEACHER-REVOCATION-RESULTS`, `TEACHER-REVOCATION-INTEGRATION`, `TEACHER-CONTEXT-ENFORCEMENT` и engineering guardrails. Установить, какие решения уже приняты и какие только предложены.
2. Построить таблицу `требование → где закреплено → что блокирует в M1 → сохранить/уточнить/исключить → какое owner решение необходимо`.
3. Предложить **только для обычных учебных прав**: JWT преподавателя ≤15 минут; продление после проверки актуальных полномочий; отзыв обычных преподавательских разрешений применяется в пределах одного часа; дедлайн операции преподавателя ≤30 минут, с кооперативной отменой и запретом позднего признания успеха. **Не заявлять физическое прекращение kernel I/O и отзыв уже отправленных байтов.** Отзыв при ограничении ProcessingRestrictions, блокировке учётной записи и иных специальных условиях не ослаблять автоматически.
4. Предложить убрать обязательный `fs-verity`/kernel seal для фотографий школьных работ, заменив прикладными write-once версиями, приватным хранилищем, SHA-256, контролем parent и backup/restore. Не менять права доступа ради упрощения хранения.
5. Подготовить два отдельных решения: **(A) нормативные изменения**, **(B) узкое разрешение synthetic HTTP/React integration по новому контракту**. Указать, какие старые gates остаются для M2/pilot.
6. Сверить реальный статус кода (включая компилятор/импортёр, context, 34 legacy API failures) и обновить backlog **как proposal**, без фальшивого VERIFIED.

## Ограничения

Без runtime/DI/API/schema изменений, удаления тестов или неподтверждённого изменения owner register. Не объявлять, что G02/S4 закрыты. Не предлагать публичные файлы, доверие клиентским полномочиям или снятие 152-ФЗ. Нет самостоятельного auto-approval.

## Выход

- `codex/M1-SCOPE-PROPOSAL.md`: входит M1 / исключено / M2.
- `codex/M1-OWNER-DECISIONS-REQUEST.md`: краткие точные нормативные diff и явные варианты `ПРИНЯТЬ / ОТКЛОНИТЬ`.
- `codex/M1-DEPENDENCY-AND-STATUS.md`: код, gaps, последовательность, заблокированные contracts.
- `codex/M1-PROGRESS.md`: начальное состояние задач M1-00…11.

**Приёмка:** владелец может принять или отклонить каждое изменение отдельно; конфликтующих требований и разрешений не скрыто. После выполнения **остановиться и предоставить решение владельцу**. Не приступать к M1-01 автоматически.

---

<!-- Файл: 02_M1_01_SESSION_BROWSER.md -->

# M1-01 — Рабочая авторизация в браузере

**Зависимость:** принято решение M1-00, отдельно разрешён точный S4/HTTP slice. **Зона:** B02 + auth subset B11.

## Сценарий

Пользователь открывает обычный Sapientum, входит по выданным учётным данным, видит доступную ему организацию/курсы, переживает перезагрузку страницы и истечение короткоживущего access JWT; может выйти. Нет dev bearer, ручной вставки токена или тестовых обходов.

## Реализовать

- Сначала проверить существующие `PostgresIdentitySessions`, `PostgresComponentTokenIssuer`, `ComponentJwt`, `Program.cs`, `browserClient`, реальный App routing. Не создавать вторую модель токенов.
- Подключить только **разрешённые** login/refresh/logout/organization-selection HTTP adapters к S2/S3/S4 и React. Access token — in-memory; refresh — HttpOnly/Secure cookie согласно принятому контракту. Нет localStorage refresh token.
- Обеспечить после смены аккаунта или организации очистку зависимых кэшей, Blob URLs, незавершённых запросов и черновиков чужого контекста.
- Везде выводить понятные ошибки неверных данных, истёкшей сессии и временной недоступности; отказ по безопасному умолчанию без бесконечного refresh loop.
- Подтвердить JWT ≤15 минут и проверку актуальных разрешений при refresh; не обещать мгновенный отзыв всех уже выпущенных JWT.

## Тесты и доказательство

Позитивные: login → reload → refresh → logout; две вкладки; переключение доступной организации, если у пользователя несколько. Отрицательные: неверная подпись/issuer/audience; чужой org; CSRF/Origin; refresh replay; истекший/отозванный токен; cache bleed между identity; outage/ambiguous ACK. Не выдавать секреты в логи.

## Критерий готовности

В обычном React UI два **синтетических** пользователя входят и выходят, а DevTools не требует ручных токенов; готова защищённая сессия для дальнейших пользовательских journeys. Обязательные команды тестов и фактические результаты записаны в `codex/M1-01-RESULTS.md`. Не считать администрирование или учебный workflow реализованными этим этапом.

---

<!-- Файл: 03_M1_02_TEACHER_SECURE_API.md -->

# M1-02 — Перевести учебные HTTP-вызовы на защищённый контракт

**Зависимость:** M1-01; новые HTTP routes/DI отдельно разрешены принятым owner scope. **Зона:** B11, TEACHER-CONTEXT-ENFORCEMENT, часть B15.

## Контекст

12.10.2026 в отчёте `TEACHER-CONTEXT-ENFORCEMENT.md`: Domain 43 PASS, Application 369 PASS, Infrastructure 333 PASS, Frontend 408 PASS, полный API 346 PASS / 36 FAIL (после отдельных исправлений 34 legacy failures остаются). Изолированный Chromium с настоящими PostgreSQL данными проходит, но React обращается к устаревшим teacher routes. Проверь состояние текущего checkout: эти цифры — snapshot, не истинный текущий baseline.

## Реализовать

1. Составить `legacy call → защищённый use case → endpoint/UI caller → тест` для review, NeedsRevision, Accept, сообщения, teacher upload/delete, private photo read и истории. **Не открывать legacy teacher writes ради старых тестов.**
2. Подключить имеющиеся `PostgresTeacherCommands`, `TeacherOperationContext`, `PostgresProtectedPhotoQueries` и утверждённые transaction/attempt semantics через нормальные HTTP adapters. Роль, organisation, course, group, submission, processing и parent устанавливаются сервером, не из client body.
3. Перевести React teacher callers и общий API client на новые контракты. Убрать положительные ожидания на intentionally forbidden legacy routes; заменить на positive assertions для новых разрешённых routes + negative assertions для старых.
4. Сохранить версионированный review, durable at-most-once intent, корректный `409 Conflict` при stale/double submit, честную сверку неизвестного исхода, неизменность истории и no automatic replay.
5. Убедиться, что teacher reads, queue/counts и file reads используют актуальный S3/ProcessingRestrictions контекст. Не активировать PRS1 exact, не добавлять обходной privileged controller.

## Тесты

Регрессия всех ранее падавших API/workflow тестов с матрицей `исправлен в новом контракте / устаревший тест заменён + positive control / действительно новый дефект`; нельзя просто удалять или skip. Negative: teacher чужой организации/группы, student вызывает review, чужой parent/file, revoked/moved permissions, missing processing, duplicate/late/unknown mutation, unauthorized list/count. Browser Chromium проходит настоящий teacher review через React (минимальный seeded synthetic work допустим для тестирования компонента, но **не** для финальной приёмки M1).

## Готово

Все поддерживаемые учебные HTTP-сценарии и React callers переведены на доверенный протокол; полная актуальная API/Frontend regression зелёная либо каждый незакрытый FAIL явно блокирует DONE. `codex/M1-02-RESULTS.md` включает исчерпывающую матрицу 34 legacy failures и evidence. Не выдавать isolated harness за полный E2E.

---

<!-- Файл: 04_M1_03_ORGANIZATION_BOOTSTRAP.md -->

# M1-03 — Первый запуск организации и администратор

**Зависимость:** M1-01/M1-02, отдельный owner approval для platform bootstrap и org-admin capabilities. **Зона:** B03.

## Сценарий

В чистой synthetic среде системный оператор один раз инициализирует Sapientum и создаёт организацию «Демо-кружок», затем первого администратора этой организации. Администратор авторизуется в обычном интерфейсе. Нет ручной SQL-вставки в процессе эксплуатации; нет публичной саморегистрации.

## Реализовать

- Проверить существующие Organization/Identity/OrganizationMembership/S3 entities и API gaps. Реализовать минимальный доверенный initial bootstrap механизм с защищённой операторской авторизацией (например, одна специально разрешённая команда запуска, без публичного privileged endpoint) и видимый workflow создания org/first admin.
- Системный оператор не приобретает автоматический доступ к решениям детей. Org-admin управляет своей организацией, но не становится учителем или читателем чужих работ.
- Предусмотреть понятные состояния: организация создана, администратор ещё не активирован, активирован, заблокирован. Обязательный audit trail без токенов/секретов.
- Один источник истины для membership; не заводить отдельную новую общесистемную роль `Admin` в коде Student/Teacher без явного принятого owner contract.
- Не переписывать данные предыдущих организаций; additive schema лишь при доказанной необходимости и отдельном разрешении.

## Проверки

Чистый запуск → trusted bootstrap → org A + admin A → его вход. Повторный bootstrap, попытка создать B от A, duplicate create, forged operator, lost commit ACK и восстановление после restart должны вести к определённым результатам без дублирования. Platform operator не читает образовательные файлы; admin A не управляет B.

## Готово

Можно поднять пустой экземпляр и завести первую организацию и администратора документированным поддерживаемым продуктовым процессом. Артефакты: `codex/M1-03-RESULTS.md` и инструкция первоначального запуска (без реальных ключей). Создание roster и групп относится к следующим задачам.

---

<!-- Файл: 05_M1_04_ACCOUNTS_ACTIVATION_RECOVERY.md -->

# M1-04 — Учётные записи учеников и преподавателей, активация и восстановление

**Зависимость:** M1-03; owner approval соответствующих Challenge/recovery/admin contracts. **Зона:** B04 + B05.

## Сценарий

Администратор в организации создаёт преподавателя и 12 тестовых учеников с минимумом персональных полей, каждому выдаёт одноразовый код активации. Участник сам задаёт пароль и входит. Владелец/администратор может восстановить доступ законным путём без потери учебной истории.

## Реализовать

- UI списка аккаунтов: создать; статус «не активирован / активен / заблокирован»; выдать или перевыпустить код; отозвать с подтверждением. Отдельная страница ввода кода, создания пароля и входа.
- Использовать существующие M1/S2/S3 Identity и `AccessChallenge` схемы, без обязательного email/телефона и публичной регистрации. Код показывается единожды; срок и rate limits строго по принятому T01/T02/E05 контракту (ориентир: 16 Crockford-символов, активация 24 часа, до пяти ошибок — проверить нормативную версию).
- Восстановление: безопасный verified request → approved operation → одноразовый recovery challenge → atomic password/session updates. Для shared identity нескольких организаций соблюдать принятый независимый approval contract; не упрощать его неформальной выдачей доступа.
- Идентификаторы участников, активность и минимальные display names брать из доверенного roster, а не свободного клиентского присвоения чужого ActorId.
- Не делать восстановление автоматической выдачей новых grants/course enrolment.

## Тесты

Позитивные: 12 synthetic пользователей и учитель активируются по отдельным кодам, входят, восстанавливают доступ. Негативные: неверный/использованный/истёкший код, конкурентное использование, повторная выдача, превышение лимитов, другие org, восстановление учителя посторонним admin, shared identity без необходимого approval, старый пароль и refresh после recovery.

## Готово

Администратор может выдать и восстановить доступ без SQL/dev token; каждый пользователь сам проходит activation/login. `codex/M1-04-RESULTS.md` содержит UI сценарий, actual tests, audit/authorization evidence. Зачисление в курс не происходит автоматически при создании аккаунта.

---

<!-- Файл: 06_M1_05_GROUPS_CATALOG.md -->

# M1-05 — Группы, назначения и понятная навигация

**Зависимость:** M1-03/M1-04 и применимый принятый scope. **Зона:** B06 + B10, basis B13.

## Сценарий

Администратор создаёт курс «Олимпиадная математика», группу «Эйлер — демо», зачисляет 12 учеников и назначает преподавателя. Преподаватель видит свою группу и имена учеников; ученик видит «Мои курсы». Отсутствует необходимость помнить UUID.

## Реализовать

- UI и API регистрации курса/группы, explicit CourseEnrollment и TeacherAssignment, назначение/снятие/перевод с подтверждением. Аккаунт ≠ член группы, членство ≠ назначение преподавателя.
- Trusted minimally sufficient roster labels (предпочтительно псевдонимы для M1); scope on database queries до count/pagination.
- Заполнить /courses и минимальные каталоги для teacher/student/admin; breadcrumbs course → lesson → task; пустые состояния.
- При переводе ученика сохранить immutable original assignment affiliation/history: старые проверки не должны незаметно становиться чужими новому преподавателю. Устранить legacy reliance на текущий Enrollment.GroupId там, где это меняет historical visibility.
- Проверить shared actor A+B: переключение организации не раскрывает старые данные, UI cache сбрасывается.

## Тесты

Реальный React org-admin создаёт курс + G1/G2; зачисляет и отзывает; учитель видит только assigned группы; student только enrolled и published content. Чужой Org B, wrong group/course, stale version, duplicate grant, move/revoke и unauthorized counts/page totals не раскрывают ни имён, ни чисел, ни истории.

## Готово

В пользовательском интерфейсе построена вся цепочка `организация → курс → группа → учитель/ученики` и видимые каталоги, без seed SQL. Документировать commands/queries/tests в `codex/M1-05-RESULTS.md`. Не приступать к публикации курса с вымышленными grants.

---

<!-- Файл: 07_M1_06_CONTENT_IMPORT_PUBLICATION.md -->

# M1-06 — Из ZIP/Markdown к опубликованному уроку и назначенным задачам

**Зависимость:** M1-02/M1-05, explicit owner approval publisher/import HTTP/API + schema if needed. **Зона:** B07b server integration + B08.

## Контекст

B07a compiler и B07b-C1 isolated ZIP importer уже имеют отчёты о компонентных PASS (74/49 tests). Не создавать второй компилятор, extractor или CMS. Существуют immutable PublishedContentVersion/Content resolver foundations, но нет готового product import → publish → open → assign.

## Сценарий

Преподаватель загружает ZIP с внешним Markdown-курсом, видит предварительную структуру/диагностику, публикует v1, открывает занятие №1 группе G1 и выдаёт пять задач. После выпуска v2 прошлое назначение продолжает ссылаться на v1.

## Реализовать

- Trusted server-side import adapter от upload к **существующему** isolated importer; валидировать MIME/размер/пути/количество файлов по принятому content contract. Никакого shell command injection, произвольного network fetch, symlink traversal; не обходить sandbox при невозможности запуска.
- Контроль загрузчика: teacher publisher capability + owner org/course + processing restrictions + limits и safe staged result. Без публикации до explicit `Publish`.
- UI: `Импортировать ZIP → Просмотреть результат/ошибки с file/line → Опубликовать immutable version → Открыть занятие → Назначить группе`.
- Idempotent/optimistic version semantics для import/publish/assignment: duplicate action не создаёт второго открытого назначения; journal/audit; all writes scoped и транзакционно согласованы. Не переписывать старую историю.
- Создать **синтетический** ZIP fixture: 2 урока, ≥5 задач в первом, несколько hints, отдельные teacher-only solutions/rubrics, один безопасный asset и LaTeX. Проверить student/teacher projections.

## Тесты

Новый ZIP → Preview → Publish v1 → G1 assigned; ошибочный ZIP не создаёт published content; v2 сохраняет ссылки v1. Wrong teacher/org, unpublished draft, stale version, parallel publish, malicious ZIP, duplicate assign и teacher content leak в student serialized output — deny. Не объявлять БД-файл и локальный ZIP единым атомарным commit без доказательства.

## Готово

Без CLI, SQL и ручной подмены ContentJson преподаватель публикует учебную программу через браузер; ученик видит назначение в курсе. Отчёт `codex/M1-06-RESULTS.md` с E2E publication evidence. Отображение содержимого материалов детально реализуется в M1-07.

---

<!-- Файл: 08_M1_07_MATERIALS_AND_REFERENCE.md -->

# M1-07 — Полные материалы занятия и отдельный эталон преподавателя

**Зависимость:** M1-06 и approved content artifact contract. **Зона:** B09 + study-view часть B10.

## Сценарий

Ученик открывает занятие и задачи: видит вводную теорию, математические формулы, изображения и подсказки; преподаватель на том же занятии видит эталонные решения и критерии оценки именно опубликованной версии. Ученик ни через UI, ни через API/cache не может получить teacher-only материал.

## Реализовать

- Вывести из compiled student projection в backend/UI lesson body, problem statement, допустимые hints (в порядке), безопасные локальные assets, Markdown/LaTeX без переавторинга.
- Teacher reference/rubric — **отдельная защищённая projection/endpoint**, не CSS hide и не общий ContentJson. Всегда exact ContentVersionId/ProblemId из назначения или текущей работы; никаких «latest» fallback для старой попытки.
- В интерфейсе задачи условие всегда над вкладками; ученическая вкладка «Подсказки». В контексте проверки на большом экране — постоянное условие и быстрый доступ к эталону, критериям и предыдущим замечаниям.
- Безопасный Markdown renderer, разрешение asset URL только через авторизованную выдачу; не включать абсолютные private пути в ответы.
- Адекватные пустые/повреждённые материалы и невозможность раскрыть черновики чужой группе.

## Тесты

Student/teacher с G1/G2 и версии v1/v2; LaTeX/rendered images, hints, teacher-only material. Network payload/cache snapshot ученика не содержит solution/rubric/private asset, даже если вкладка скрыта. Malicious HTML/URL/TeX/path, unauthorized group/version/asset, missing content/processing deny; тесты мобильного отображения и ошибки загрузки.

## Готово

Можно провести реальное учебное занятие **на синтетической группе**: все материалы видны нужной аудитории, решения преподавателя отделены и привязаны к точной версии. Отчёт `codex/M1-07-RESULTS.md` с screenshot/DOM/API assertions. Не создавать полноценный WYSIWYG редактор.

---

<!-- Файл: 09_M1_08_PHOTO_STORAGE_SUBMISSION.md -->

# M1-08 — Защищённая сдача решения из нескольких фотографий

**Зависимость:** M1-02/M1-05/M1-06, owner approval accepted photo storage/HTTP scope (после M1-00). **Зона:** B12 + student part B15.

## Сценарий

Ученик открывает выданную задачу с телефона, выбирает/снимает две фотографии листов A4 (JPEG/PNG), видит preview, меняет порядок, отправляет. После reload учитель в очереди видит ровно одну работу и обе страницы в правильном порядке. Ученик может открыть свои сохранённые оригиналы.

## Реализовать

- Связать уже существующие AttachmentUploader, SubmissionForm, AttachmentService, private local storage, parent/claim и Postgres metadata с серверной авторизацией `actor + organization + course + group + assignment + work + parent + processing`. Перед чтением bytes и на download/preview/delete проверять каждый применимый scope.
- Сохранить оригинальные байты и hash/version, исключить перезапись оригинала аннотациями; private 0600/0700 data root, нет public static paths и guessed direct URL. `fs-verity` и физическое прекращение syscall не являются приёмкой **только если** решение M1-00 формально это исключило.
- Поддержать ≥2 фотографий, правильный order, safe limits/validation, отсутствие частичной опубликованной Submission при сбое загрузки, нет orphan leak в пользовательских списках. Сверку неизвестного результата делать GET/read, не повторять команду вслепую.
- Отказ teacher legacy upload/delete/read не обходить; для преподавателя проверка photo идёт по согласованным protected routes; отрицательный parent access проверять независимо от того, знает ли пользователь file ID.

## Тесты

Two-page upload → preview → reorder → submit → reload → protected teacher view; exact hash/order, unauthorized student/teacher/org/parent/claimed file/expired session/read, unsupported file/bomb/oversize, cancel/dropped ACK/double-click/retry, parent deletion conflict, restart. Counts queue не раскрывают чужую работу.

## Готово

Две фотографии реально проходят через React → API → DB/files → teacher protected read, без тестовых fake Blob/attachment short-circuit. `codex/M1-08-RESULTS.md` содержит E2E evidence и негативные кейсы. Разметка и review отдельно в M1-09.

---

<!-- Файл: 10_M1_09_REVIEW_ANNOTATION_CYCLE.md -->

# M1-09 — Полный Submit → Review → Revise → Accept в React

**Зависимость:** M1-02/M1-07/M1-08. **Зона:** учебный workflow, photo annotation, teacher context; использовать готовые Domain/UI.

## Сценарий

Преподаватель открывает queue со своими ученическими работами, выбирает задачу, видит обе страницы A4, условие, предыдущие замечания, эталон и критерии; пером рисует поверх фотографии и возвращает на доработку. Ученик читает замечания, сдаёт новое решение; учитель принимает. Все попытки и разметка связаны с правильной версией и оригинальными страницами.

## Реализовать

- Переиспользовать существующие ReviewQueue, viewer/canvas, ReviewContext, immutable annotation entities, `ProblemWorkService` и `PostgresTeacherCommands`. Не создавать новый аннотационный редактор или state machine.
- Desktop: постоянное условие и контекст проверки рядом с большим листом; teacher reference/rubric/older feedback доступны в 1–2 действия. Fullscreen tablet: только лист и минимальный pen/undo/page controls. Сохранять координаты overlays независимо от zoom/orientation.
- UI feedback: `NeedsRevision` с текстом/разметкой; student read; новая Submission; `Accept` immutable, закрывает дальнейшую пересдачу по принятой модели. Непринятая/конкурирующая review не меняет историю.
- Все teacher writes должны идти через утверждённый `TeacherOperationContext` и idempotent/versioned protocol, не legacy fallback. Правильная обработка HTTP 409/401/403, повторного клика и неизвестного исхода.

## Тесты

Полный React browser run с двумя реальными PNG/JPEG, overlay на обеих страницах, NeedsRevision → ReSubmit → Accept → reload, immutable attempt chain, hashes unchanged. Проверить чужого преподавателя, student spoof review, old version/late intent, revoked/expired teacher, accepted terminal, page annotation alignment, fullscreen/keyboard/pen-emulation; реальные физические планшеты входят в M2, не обещать их проверку без устройства.

## Готово

Повторяемый сквозной браузерный тест и ручной demo сценарий проходят **без direct SQL setup после нормального пользовательского onboarding** и без legacy teacher routes. Отчёт `codex/M1-09-RESULTS.md` с evidence скриншотов и результатами backend/React/browser тестов.

---

<!-- Файл: 11_M1_10_PROGRESS_HISTORY_RETRY.md -->

# M1-10 — Прогресс группы, история проверок и понятные сбои

**Зависимость:** M1-05…M1-09. **Зона:** B13+B14+B15 и минимальный B17.

## Сценарий

Преподаватель открывает группу и видит 12 учеников × выданные задания со статусами `NotStarted / Submitted / NeedsRevision / Accepted`, может перейти к любой своей прошлой проверке (включая исчезнувшую из очереди). Ученик видит свои попытки, замечания и прогресс. После потери ответа он понимает, сохранилась ли работа, и не создаёт дубль.

## Реализовать

- Scoped server-side roster progress matrix и totals на основе **назначенных** работ, а не текущих чужих UUID; исходные assignments и version учитываются, пустые знаменатели безопасны. Включить фильтры группы/ученика/статуса для преподавателя без утечек counts.
- Отдельный `Мои проверки`/history entry point, дата, ученик, задача, статусы, переход к attempt с исходными overlays. После Accepted view read-only, пока сохраняются применимые права доступа.
- Согласовать с session client обработку upload/review timeout: fetch current state и безопасное явное продолжение. Lost ACK ≠ rollback; не делать автоматическую новую mutation с другим intent. Предусмотреть читабельные error/empty/offline states.
- Минимальная переписка внутри work только если не порождает дополнительный неподтверждённый protocol; она не должна быть prerequisite для прогресса. Не создавать глобальный чат.

## Тесты

12 synthetic student statuses (включая never started), v1/v2/withdrawn assignment, G1/G2, converted membership, pagination/count leakage, foreign org, old teacher, ended assignment, history after Accept. Network drop on commit acknowledgement, duplicate review/submit, reload, two tabs and expired session; никакой двойной domain event. E2E клики dashboard → work → history → back.

## Готово

Без SQL или UUID копирования преподаватель контролирует работу группы, а обе роли находят все разрешённые попытки. `codex/M1-10-RESULTS.md` включает значения матрицы и API/UI consistency evidence.

---

<!-- Файл: 12_M1_11_SYNTHETIC_ACCEPTANCE.md -->

# M1-11 — Сквозная приёмка Synthetic MVP и воспроизводимый запуск

**Зависимость:** M1-00…M1-10 приняты в соответствующем scope. **Зона:** B16 + минимальные B18/B19/B20 для M1. Не включает real-data pilot.

## Цель

На **чистой синтетической среде** последовательно продемонстрировать M1 без SQL fixture как пользовательского шага, без dev JWT, без ручного создания Course/Work IDs и без test-only API bypass. Отдельный E2E fixture может создавать только исходную разрешённую bootstrap-capability согласно принятому контракту.

## Реализовать

1. Один документированный способ поднять PostgreSQL + API + React + private file store (существующий Compose можно доработать) с миграциями, health checks и безопасной synthetic конфигурацией. Не выкатывать в публичный production и не размещать реальные секреты в репозитории.
2. Автоматизированный браузерный тест: trusted bootstrap → org-admin → user activation/login → course/group/teacher assignment → teacher ZIP import/publish/assign → student study/hints → upload **двух** фотографий → teacher annotated NeedsRevision → student resubmit → teacher Accept → history/progress.
3. Negative второй организации и чужих actor/file/group на основных endpoints; проверка отсутствия teacher-only solutions в student API/cache, истечения session, повторов, concurrency conflict, dropped ACK и expected status.
4. Restart API/DB и проверка persisted files/history/annotations. **Синтетическая** backup+restore репетиция на отдельном isolated runner с согласованным набором DB + file store + key material и документированными ограничениями; если полнота не доказана, NOT PROVEN вместо PASS.
5. Проверка responsive desktop + mobile viewport + pen/touch emulation. Реальная физическая проверка на выбранном планшете — отдельная M2 release-activity, не скрытая M1 гарантия.
6. Полная регрессия .NET, frontend, compiler/importer и browser; не исключать из run важные suites молча. Failures показывать в итоговом отчёте.

## Критерий M1

Все строки `M1_E2E_ACCEPTANCE.md` имеют реальное evidence `PASS` в основном React с PostgreSQL/files, а не только component-level. Никакая поддерживаемая user action не требует ручных IDs/SQL/dev token. Сохранение originals/истории/permissions доказано. Нет P0 runtime fails; оставшиеся M2 legal/security/ops gates перечислены, не выдаются за принятые.

## Выход

- `codex/M1-11-RESULTS.md`: commands, runtimes, browser screenshots, database assertions, exact PASS/FAIL/NOT RUN.
- `codex/M1-ACCEPTANCE-MATRIX.md`: трассировка каждого теста к evidence.
- `docs/ops/M1-SYNTHETIC-RUNBOOK.md`: clean start, stopping, restart, backup/restore, troubleshooting.
- Явный финальный статус `M1 SYNTHETIC ACCEPTANCE CANDIDATE`. Только **владелец** может принять `SYNTHETIC MVP ACCEPTED`; **не** заявлять `PILOT AUTHORIZED`.

---

<!-- Файл: M1_E2E_ACCEPTANCE.md -->

# M1 — единая матрица финальной браузерной приёмки

**Среда:** clean disposable synthetic-only instance, реальные React → API → PostgreSQL + private files, два отдельных browser contexts, минимум 2 орг A/B, 12 синтетических учеников, 1+ преподаватель, 2 урока, 5 задач, 2 JPEG/PNG решения. Нет реальных данных детей.

| ID | Сквозное действие | Ожидаемый наблюдаемый результат |
|---|---|---|
| E01 | Bootstrap организации A и первого администратора | UI/authorized bootstrap, без SQL/ручного dev bearer, audit receipt |
| E02 | Создать 12 учеников и учителя, выдать активацию | Реальные коды в single display; каждый активируется и входит; ошибочный/expired code не принимается |
| E03 | Создать курс/группу, зачислить/назначить преподавателя | Каталоги отражают только разрешённые связи; отсутствует implicit grant |
| E04 | Teacher импортирует ZIP и публикует v1 | Markdown, 2 урока, 5 задач, assets и diagnostics; invalid package не публикуется |
| E05 | Teacher открывает урок и назначает 5 задач G1 | Student увидел урок; чужой G2/Org B — нет; новая версия v2 не меняет old assignment |
| E06 | Student открывает lesson/task/hints | Корректные LaTeX/изображения; student API не содержит teacher solutions/rubrics |
| E07 | Student отправляет две страницы A4 | Protected preview/order/hash, одна Submission после duplicate click; status Submitted |
| E08 | Teacher открывает queue/работу | Только assigned scope; видны обе страницы, условие, reference, история |
| E09 | Teacher размечает и отправляет NeedsRevision | Overlay/feedback сохранены по исходным страницам, student видит |
| E10 | Student пересдаёт; teacher Accept | Ровно 2 попытки, immutable reviews/annotations; terminal Accepted |
| E11 | Progress/dashboard и история обеих ролей | Counts совпадают с underlying authorized work, доступ к прошлой проверке, нет UUID-navigation |
| E12 | Logout/refresh/revocation/foreign access | Нет межорганизационных, чужих group/file/parent/teacher/count утечек |
| E13 | Ошибки: lost ACK, stale version, repeat, bad media | Нет двойных effects; неизвестный исход сверяется чтением; корректный UI error |
| E14 | Restart и повторное чтение оригинала/пометок | Работы, история, версия контента, фото и overlay сохранились |
| E15 | Clean test deployment/rehearsal | Reproducible commands, documented synthetic backup/restore result, actual browser runs |

**Для каждой строки:** скрипт/команда + fixture + UI action + ожидаемый HTTP outcome + DB/file invariant + PASS/FAIL/NOT RUN + screenshot/log path. `PASS` допускается только если тест реально исполнен на целевом контракте; прошлые component/isolated PASS — вспомогательная evidence.

## Условия, которые остаются за границей M1

Real student data, 152-ФЗ readiness, внешний security review, продакшн-деплой, RPO/RTO и off-host restore, физический планшет и эксплуатационные SLO — отдельный набор M2. Их отсутствие не превращать в скрытые M1 PASS, но и не выдавать M1 за разрешение начать реальный кружок.
