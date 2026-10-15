# Student Course Dashboard — результаты

2026-10-15. **UI PARTIAL · OWNER ACCEPTANCE PENDING · SYNTHETIC ONLY.**
Разрешение: приложенное задание «STUDENT COURSE DASHBOARD AUTHORIZED». Применены MVP guardrails, reconciliation, ADR-L11/L13/L15/L16/L18 и последние решения из `L03-OWNER-DECISION-REGISTER.md`. PRS1 exact остаётся OFF; G02 и production cutover остаются BLOCKED. Новых HTTP routes, grants, schema и writes нет.
Названные в AGENTS.md specialist skills `model-domain`, `implement-use-case`, `implement-feature` отсутствуют в доступном каталоге; их выполнение не заявляется.

## Контракт среза

- **Actor → Use case:** Student открывает свой курс из «Мои курсы», переключает «Сводка»/«Занятия», переходит к доступному занятию.
- **Invariants → Entities/VO:** данные только назначенного курса и собственных assignment; `Accepted` означает принятую работу; один assignment учитывается один раз; при `0/0` процент и progress bar не выводятся; отсутствие attendance не означает ноль посещений. Course, published content version, lesson, assignment/work; клиент не определяет полномочия.
- **State transitions:** существующие `NotStarted → Submitted → NeedsRevision/Accepted` только читаются. Новых переходов нет.
- **Authorization/data-source matrix:** course ID в URL является selector. `/my/courses`, `/my/courses/{id}/assignments` и `/my/courses/{id}/versions/{version}/content` используют существующие S2/S3/current processing checks. `StudentAssignments` ограничивает запрос текущим actor/course/group до materialization; `StudentPublishedContent` повторно проверяет доступ и удаляет недоступные lessons/problems/assets из projection. Сторонние данные в UI не фильтруются как способ авторизации.
- **Writes:** не применимо — только GET и навигация. Transaction owner/isolation/lock order/CAS/commit/audit/retry/unknown outcome для новой записи отсутствуют.
- **Scope/non-goals:** текущий AppShell, меню, toolbar, экран задачи, доменная история и схема не менялись. Новые защищённые read endpoints из этого задания не регистрировались.

## Изменённые пути

- `src/Lms.Web/src/features/content/StudentStudyPage.tsx` — вход в новую страницу; существующий student lesson page сохранён.
- `src/Lms.Web/src/features/content/StudentCourseDashboard.tsx` — вкладки, сводка, текущий список занятий, правая панель.
- `src/Lms.Web/src/features/content/studentCourseDashboard.css` — зелёная тема, двухколоночная сетка и mobile layout.
- `src/Lms.Web/src/test/studentStudy.test.tsx`, `src/Lms.Web/src/test/studentCourseDashboard.test.tsx` — адресные проверки.
- `src/Lms.Web/scripts/student-course-dashboard-smoke.mjs` — воспроизводимая проверка Chromium с synthetic ответами.
- `codex/evidence/STUDENT-COURSE-DASHBOARD/summary.png`, `lessons.png`, `summary-mobile.png` — скриншоты.
- Этот отчёт.

В каноническом checkout `/home/tea/github/lms` до начала работы были незавершённые изменения `LearningCatalog.tsx`, `SidebarNavigation.tsx`, `shell.test.tsx`, `learningCatalog.css`, `learningCatalog.test.tsx`; они сохранены. Branch `main`, HEAD и index не переключались/не переписывались. Штатный `apply_patch` возвращал `mountinfo path is not absolute`; точные изменения применены `git apply` после `git apply --check`.

## Блок интерфейса → API/DTO → статус

| Блок | Серверный источник | Статус |
| --- | --- | --- |
| Заголовок, крошки, описание | `/my/courses` и разрешённый published content | Реализовано; без искусственного текста |
| «Сводка»/«Занятия», Back/Forward | URL `?tab=lessons`, React Router | Реализовано; без полной перезагрузки |
| Список занятий и переходы | `StudentAssignmentItem` + student content projection | Реализовано с прежними lesson routes; второй список данных не создавался |
| Решённые задачи | Собственные `StudentAssignmentItem.status` | Реализовано; только `Accepted`, assignment ID один раз, `0/0` без процента |
| Последние уроки | Те же разрешённые занятия и статусы задач | До пяти по убыванию порядка; процент = уникальные задачи со статусом `Accepted` / уникальные назначенные задачи урока; при отсутствии назначений процент не показывается |
| Просмотр материалов | Нет read model просмотра | BLOCKED; карточка без фиктивного `0/0` |
| Посещения занятий | Нет attendance read model | Не показывается по ТЗ |
| Ближайшие события | Нет course event query | BLOCKED; понятное пустое состояние, без ссылки-заглушки |
| Последние сданные задачи | `/student/progress-history` есть, но сортирует по курсу/занятию до pagination, не по времени отправки | BLOCKED; первая страница не выдаётся за «последние» |
| Последние материалы | Published content содержит theory/assets, но нет отдельного списка с типом/датой публикации и route | BLOCKED; ссылка «Все материалы» не выдумана |
| О курсе | Разрешённое серверное описание | Реализовано |
| Преподаватель, расписание, место | Нет нужных полей в доступной student projection | BLOCKED; ФИО, Zoom, адрес и время не выдуманы |

Существующие `/courses/{id}/workspace` и teacher APIs не использованы для персональной сводки: course-content access сам по себе не даёт права на student work/attendance/history. Новый `StudentCourseSummary` HTTP contract требует отдельного решения и независимых negative-access тестов перед подключением.

## Проверки

- `PATH=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin:$PATH pnpm exec vitest run` (из `src/Lms.Web`): **21 files, 438 tests PASS**. Новые тесты: вкладки и Back/Forward, маршрут занятия, уникальный `Accepted`, `0/0`, отсутствие attendance, порядок последних пяти уроков и отсутствие выдуманных timestamp.
- `PATH=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin:$PATH pnpm run build` (из `src/Lms.Web`): **PASS**, TypeScript и Vite production build.
- `LMS_PLAYWRIGHT_MODULE=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright /home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node src/Lms.Web/scripts/student-course-dashboard-smoke.mjs` (при локальном Vite на 127.0.0.1:5178): **PASS** — обе вкладки, Back/Forward, unknown API requests `[]`, mobile `scrollWidth=390, innerWidth=390`. Только synthetic mocks; живой backend E2E **NOT RUN**.
- `dotnet --info`: **NOT RUN backend regression/build** — доступный `/snap/bin/dotnet` сообщил, что SDK/Runtime отсутствует, и не смог установить его без sudo. Backend код в этом срезе не менялся.
- `git diff --check`: **PASS**.

### Скриншоты

- [Сводка](evidence/STUDENT-COURSE-DASHBOARD/summary.png)
- [Занятия](evidence/STUDENT-COURSE-DASHBOARD/lessons.png)
- [Сводка, mobile 390 px](evidence/STUDENT-COURSE-DASHBOARD/summary-mobile.png)

Отличия от утверждённого макета: существующий AppShell и его меню оставлены, оформление карточек контекстной колонки уточнено; доступно две карточки прогресса (attendance не ведётся); неподдержанные виджеты содержат пояснение вместо чисел/примеров из макета. UI соответствует зелёной теме и двухколоночной структуре; полный parity зависит от перечисленных серверных источников.

## Guardrails §7 и negative-access

| Проверка | Результат |
| --- | --- |
| Actor/use case/states; S2/S3/PRS1; blocked gates | **covered** — выше; новые gates не открыты |
| Org/course/group/ownership на сервере | **covered существующими paths по inspection**, но backend regression **NOT RUN**; собственный actor/course проверяется до projection |
| Subject/processing evidence | **covered существующим `EnsureRead` по inspection**; independent runtime proof для этого изменения **NOT RUN** |
| Списки/counts до раскрытия | **covered по inspection** для используемых student routes; новые total/cursor отсутствуют |
| Attachments/history | **not applicable** — новых file/history access paths нет; недоступные assets не открываются |
| Transaction/lock/CAS/retry/unknown outcome | **not applicable** — новых writes нет; GET error/retry остаётся существующим |
| Validation/limits/safe errors | **covered** frontend декодером и существующими loading/error states; widget-level error state для отсутствующих read models **blocked** |
| Unit/integration/negative/browser | frontend 438 PASS, synthetic browser PASS; backend/API negatives для нового summary contract **NOT RUN**, потому что contract не создан; live E2E **NOT RUN** |
| Schema/API/security/unrelated files | **covered** — изменений нет; ранее существовавшие dirty files сохранены |
| Real data/secrets/release claims | **covered** — скриншоты используют synthetic fixture, token только одноразовый в памяти тестового браузера; production readiness не заявлена |

Применимость §5: wrong org/course/group/student, missing/revoked/moved membership, processing evidence, parent/file bypass и counts/pagination требуют серверных negative tests при добавлении новых summary/material/event/work read models. В текущем UI нет нового серверного access path; ожидаемый deny остаётся существующим контрактом, фактический backend результат в этой работе **NOT RUN**. Session expiry/retry/duplicate submission проверялись только ранее существующими frontend regression, новая страница не отправляет команды.

## Необходимые следующие задачи

1. Принять минимальный защищённый `StudentCourseSummary` read contract для просмотров материалов, событий, submission timestamps/status, attendance, teacher/schedule/venue и материалов с датами; сортировку/лимиты/counts выполнять на сервере до pagination.
2. Реализовать этот contract с independent S2/S3/current ProcessingRestrictions negative-access tests (wrong org/course/group/student, revoked/moved membership, missing evidence/parent, inaccessible attachment, counts/pagination) и live synthetic HTTPS E2E.
3. Провести owner acceptance визуального результата и оставшихся отличий; затем отдельно оценивать `UI COMPLETE`, `E2E ACCEPTED`, `SYNTHETIC MVP ACCEPTED`, `PILOT AUTHORIZED`. Сейчас ни один из этих статусов не заявлен.

**Остановлено на owner acceptance; commit/push/merge/rebase/deploy не выполнялись.**

## Дополнение 2026-10-15 — обновление локального приложения

Последующий запрос владельца «Обнови локальное приложение» разрешил обновление существующего локального synthetic Compose-контура `lms-m1-local`. Предыдущая строка о deploy описывает исходную задачу реализации; для этого последующего шага выполнен локальный web rollout.

- `docker compose config --quiet` — PASS; исходные `web`, `api`, `postgres`, `identity` были healthy.
- `docker compose build web` — PASS; production Vite build, 305 modules. Новый web image: `sha256:4ecda3d422933199b821463b9ebc14cc30078795600462954cb1b6ffd29708c2`.
- `docker compose up -d --no-deps --no-build web` — PASS; пересоздан только web-контейнер. API и PostgreSQL сохранили прежние image и healthy status; их volumes и миграции этой командой не затрагивались.
- `https://localhost:8443/health` и прямая ссылка `/study/courses/olympiad` — HTTP 200; HTML выдаёт новые `index-DjtUuPDM.js` и `index-CxHrhfZU.css`. В отдаваемом JS присутствует заголовок нового виджета.
- Проверка авторизованного пользователя на живом API **NOT RUN**; это подтверждение статического bundle, HTTPS маршрута и health, не acceptance backend данных.

Контур остаётся **LOCAL SYNTHETIC ONLY · UI PARTIAL · G02 BLOCKED · PILOT NOT AUTHORIZED**. Commit/push/merge/rebase и production/public deploy не выполнялись.

## Дополнение 2026-10-15 — визуальные уточнения и повторное локальное обновление

По запросу владельца заменены текстовые символы результатов курса на контурные SVG (книга и принятая задача) с мятным круглым фоном, удалён бейдж «Студент», правая колонка получила карточки и иконки как в макете. Это изменение только представления: Actor/Use case — тот же Student/course summary; Course/lesson/assignment, источники GET, состояния, правила доступа и серверные границы неизменны. Новых writes нет, поэтому transaction owner/isolation, locks, CAS, audit, retry и unknown-outcome не применимы. Сведения без источника по-прежнему обозначены как недоступные.

- Изменены: `StudentCourseDashboard.tsx`, `studentCourseDashboard.css`, `studentCourseDashboard.test.tsx` и три synthetic screenshot в `codex/evidence/STUDENT-COURSE-DASHBOARD/`.
- `PATH=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin:$PATH pnpm exec vitest run src/test/studentCourseDashboard.test.tsx src/test/studentStudy.test.tsx`: **5/5 PASS**. Проверено отсутствие бейджа, наличие SVG результатов и правой колонки, вкладки/история.
- `PATH=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin:$PATH pnpm exec vitest run`: **438/438 PASS**; `pnpm build` с тем же PATH: **PASS**.
- Синтетический Playwright smoke: **PASS** (вкладки, Back/Forward, tablet panel/body 880/880 px без overflow, mobile 390/390 px, unknown requests `[]`); скриншот сводки визуально проверен.
- `docker compose build web` и `docker compose up -d --no-deps --no-build web`: **PASS**; обновлён только local web image до `sha256:9091463f862346fb0b0f5a053990bba66e1e8ca4a5aa2353ebc652a223a93a07`. Web/API/PostgreSQL/identity — healthy. `/health` и `/study/courses/olympiad` вернули HTTP 200; HTML выдаёт `index-DPtQPGF2.js` и `index-BT4d6-jB.css`.
- Guardrails §7: клиент не получил новых полномочий/IDOR, cross-org path, pagination/counts или доступа к файлам; опасного сужения restrictions, mutable history, duplicate effects, secret exposure нет — **not applicable** по отсутствию изменений соответствующих paths. Положительный UI/browser контроль **PASS**; independent server negative-access и authenticated live E2E **NOT RUN** (нового серверного access path нет); readiness прежний **UI PARTIAL · LOCAL SYNTHETIC ONLY · G02 BLOCKED · PILOT NOT AUTHORIZED**.

Commit/push/merge и production deploy не выполнялись.

## Дополнение 2026-10-15 — прогресс по принятым задачам

Уточнение владельца: прогресс каждого последнего урока оценивается только по назначенным задачам ученика со статусом `Accepted`; просмотр теории, отправка на проверку и статус `NeedsRevision` не увеличивают числитель. Назначение учитывается один раз. Для урока без назначений процент и progressbar не выводятся. Используются существующие серверные student assignments и разрешённые published lessons; новых запросов, permissions, writes, схемы или переходов состояний нет.

- Изменены `StudentCourseDashboard.tsx`, `studentCourseDashboard.css`, `studentCourseDashboard.test.tsx` и synthetic screenshots; ранее существовавшие посторонние изменения сохранены.
- Адресные тесты `pnpm exec vitest run src/test/studentCourseDashboard.test.tsx src/test/studentStudy.test.tsx` с bundled Node: **6/6 PASS**. Частичный (1/3 → 33%), полный (1/1 → 100%), нулевой (0/1 → 0%), пустой урок, повтор assignment и доступная подпись прогресса покрыты.
- Полный `pnpm exec vitest run`: **439/439 PASS** при первом и последнем прогонах. Промежуточный прогон после уточнения подписи дал 438 PASS / 1 FAIL в неизменённом `attachments.test.tsx` (`onChange.mock.lastCall` был undefined); изолированный повтор этого файла **20/20 PASS**, затем полный повтор **439/439 PASS**. Причина единичного сбоя не установлена. Production `pnpm build`: **PASS**. Synthetic Chromium smoke: **PASS** (вкладки, Back/Forward, tablet 880/880 px, mobile 390/390 px, unknown requests `[]`).
- Только local web image пересобран и web-контейнер перезапущен; web/api/postgres/identity healthy, `/health` и `/study/courses/olympiad` — HTTP 200; bundle `index-C0P-xDdy.js`, `index-DfaX98T-.css`. API/БД не обновлялись. Авторизованный live backend E2E и новые independent server negative-access tests **NOT RUN**: серверных access paths не меняли.
- Guardrails §7: actor, use case, invariant, source and states указаны выше; S2/S3/processing, org/course/group/ownership, counts before pagination, file-parent and history protections сохранены существующим серверным путём (**covered by inspection**, не новая runtime-сертификация). Новые transactions/CAS/retry/unknown outcome, restriction narrowing, mutable history, duplicate write effects, secrets — **not applicable** из-за отсутствия новых writes/доступов. Production/G02/PRS1 gates не пересекались.

Readiness остаётся **UI PARTIAL · LOCAL SYNTHETIC ONLY · G02 BLOCKED · PILOT NOT AUTHORIZED**. Commit/push/merge и production deploy не выполнялись.
