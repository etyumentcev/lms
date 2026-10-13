# M1-05 — Группы, курсы, назначения и каталоги: результаты

Дата: 12.10.2026  
Статус: **DONE (component acceptance, synthetic-only)**  
Граница: только M1-05; import, publication, course content, attendance, calendar, расширенная аналитика и публичный каталог не добавлялись; commit/push/deploy не выполнялись.

## Итог

В production React UI администратор организации создаёт черновой курс «Подготовка к олимпиаде Эйлера», две группы, выбирает участников из каталога без UUID, зачисляет 12 synthetic students и назначает преподавателя. Доступны подтверждённые перевод/исключение ученика и снятие назначения преподавателя. Преподаватель получает только назначенные группы и активных учеников; ученик видит только свои курсы в «Мои курсы».

Курс остаётся в состоянии `Draft`. M1-05 не создаёт `PublishedContentVersion`, уроки, задания или фиктивные опубликованные материалы.

## Вертикальные цепочки

### Администратор

`OrganizationAdminDashboard` → «Курсы и группы» → `CourseAdministrationPanel` → protected `/api/v1/admin/courses/**` → component bearer authority → `PostgresCourseRosterAdministration` → существующие `OrganizationMembership`, `EnrollmentScope` и `TeacherAssignmentScope` через принятую `s3_change_authority` → PostgreSQL transaction/receipt/audit.

Создание курса одновременно регистрирует глобально уникальный `CourseId` в существующем `CourseRegistration` и его минимальные catalog metadata. Группа одновременно создаёт существующий `CourseGroup` и course-local label. UI использует серверные идентификаторы только внутри ссылок/команд; пользователь выбирает курс, группу и участника по названию/псевдониму.

### Преподаватель и ученик

`LearningCatalog` → `GET /api/v1/my/courses` → validated current component token → пересечение organization membership с его `EducationalGrant` → разрешённые course/group labels. Для Teacher group counts и roster вычисляются только после ограничения назначенными group IDs. `GET /api/v1/teacher/courses/{courseId}` не возвращает неназначенный курс.

Student получает только course/group его active enrollment. Draft-статус показан явно; переход к материалам отсутствует до M1-06/M1-07.

## Модель полномочий и историческая принадлежность

- отдельная роль, ACL или roster membership не создавались;
- admin authority — существующий `OrganizationMembership.PermissionFlags`;
- student authority — существующий `EnrollmentScope` / `CourseEnrollment` generation;
- teacher authority — существующий `TeacherAssignmentScope` / `TeacherAssignment` grant;
- CourseId глобально уникален, GroupId course-local; catalog rows имеют FK к принятым registry rows;
- перевод вызывает существующую S3 `Move`: меняется только текущее enrollment и его generation/version;
- ранее созданный `AssignmentScope` продолжает хранить исходные organization/course/group и enrollment generations. PostgreSQL regression подтверждает, что перевод не переписывает прежнюю группу задания;
- исключение/снятие переводят authority в `Revoked`, а не удаляют исторические строки.

## Authorization и isolation

- organization/actor для admin routes берутся только из validated bearer, не из body/header selector;
- admin membership проверяется на сервере до course rows, counters и roster;
- каталоги Teacher/Student сначала строят allow-set из scoped grants, затем читают catalog/group rows и только после этого считают разрешённых учеников;
- foreign admin видит пустой каталог, direct foreign course получает `404` без title/count;
- неназначенный Teacher не получает course token/catalog; fresh login после unassign также не получает учебный доступ;
- после shared-actor switch A → B React переходит через busy state, размонтирует M1-05 catalog state и показывает пустой B catalog; курс A не остаётся в DOM;
- logout/login другой организации дополнительно подтверждает нулевые foreign rows и отсутствие названия курса.

## Commands, transaction и failure semantics

- `CreateCourse`, `CreateGroup`, batch `EnrollStudents`, `MoveStudent`, `RemoveStudent`, `AssignTeacher`, `RemoveTeacher` выполняются в `Serializable` transaction;
- admin membership блокируется и повторно проверяется внутри mutation transaction;
- immutable receipt keyed by administrator + idempotency key хранит payload digest/result; same payload replay не повторяет эффект, другой payload получает conflict;
- `s3_change_authority` остаётся единственным writer для enrollment/teacher scope и его generation/version/CAS invariants;
- stale expected version, serialization/deadlock/unique races отображаются в `409 ConcurrencyConflict`;
- повторное зачисление/назначение — успешный no-op; повтор remove/unassign с тем же receipt — no-op replay;
- partial batch не коммитится: недействующий или foreign participant отклоняет всю команду;
- UI не делает автоматический replay при неизвестном результате, сообщает проверить состояние; при `409` обновляет roster перед следующим действием;
- mutation routes требуют HTTPS, exact Origin, antiforgery и strict JSON; responses — `no-store`.

## Реальный Chromium HTTPS

Opt-in `CourseRosterBrowserTests` поднял real HTTPS Kestrel, production Vite bundle, PostgreSQL и `/usr/bin/google-chrome`; Playwright не mock/route/fulfill API. Исходные synthetic accounts создавались через принятые application services, не SQL. Все course/group/enrollment/teacher пользовательские операции выполнялись через React UI.

1. admin открыл пустой catalog и создал Draft course;
2. создал две группы;
3. выбрал ровно 12 synthetic students и зачислил в основную группу;
4. назначил преподавателя;
5. teacher увидел один курс, одну назначенную группу и 12 учеников;
6. student увидел курс в «Мои курсы»;
7. shared student переключился из organization A в B и увидел пустой каталог без stale данных A;
8. admin перевёл одного ученика во вторую группу и исключил другого;
9. исключённый student потерял каталог; teacher основной группы увидел только оставшихся 10;
10. unassigned teacher увидел пустое состояние; foreign admin — ноль курсов.

Screenshots:

- `codex/evidence/M1-05-COURSE-ROSTER/01-admin-course-catalog-empty.png`;
- `02-admin-course-two-groups.png`;
- `03-admin-roster-12.png`;
- `04-teacher-course-catalog.png`;
- `05-teacher-assigned-roster.png`;
- `06-student-my-courses.png`;
- `06a-student-after-organization-switch.png`;
- `07-admin-after-move-remove.png`;
- `08-removed-student-denied.png`;
- `09-foreign-organization-empty.png`;
- machine-readable `test-summary.json` (`passed=true`, `Draft`, 2 groups, 11 active + 1 revoked, switch isolated, 10 screenshots).

## Фактически выполненные проверки

| Проверка | Результат |
|---|---:|
| Solution build | PASS, 0 warnings / 0 errors |
| Domain full | 43 PASS |
| Application full | 369 PASS |
| Infrastructure full | 342 PASS / 0 FAIL |
| API full | 378 PASS / 9 opt-in browser SKIP / 0 FAIL |
| M1-05 infrastructure targeted (history/move/remove/unassign/isolation) | 1 PASS / 0 FAIL |
| M1-05 protected HTTP targeted, включая unassign replay | 1 PASS / 0 FAIL |
| M1-05 real Chromium HTTPS opt-in, final production bundle | 1 PASS / 0 FAIL |
| Frontend Vitest full | 411 PASS / 0 FAIL |
| Frontend production TypeScript/Vite build | PASS |

## Изменённые contracts и paths

- Application: `CourseRosterAdministration.cs` — commands/results/query port.
- Infrastructure: `PostgresCourseRosterAdministration`, additive catalog/receipt entities, mappings, migration `20261012232000_CourseRosterCatalogM1` и model snapshot.
- API: `CourseRosterHttp`, DI/composition и map в уже защищённом host.
- React: `CourseAdministration`, `LearningCatalog`, dashboard/nav integration и Sapientum green responsive CSS.
- Tests/evidence: PostgreSQL history/CAS/idempotency/isolation tests, protected HTTP test, real Chromium runner, 10 screenshots и JSON summary.

## Guardrails §7

- [x] Actor/use cases/entities/states и граница M1-05 зафиксированы.
- [x] Existing OrganizationMembership/EnrollmentScope/TeacherAssignmentScope переиспользованы.
- [x] Scope/organization проверяются до rows/count/search; foreign totals не раскрываются.
- [x] AssignmentScope исторической работы не выводится из текущего Enrollment.GroupId.
- [x] Idempotency, stale CAS, duplicates, revoke/remove и transaction rollback проверены.
- [x] HTTPS/Origin/CSRF/strict JSON/no-store сохранены.
- [x] Реальный PostgreSQL/HTTP/React/Chromium и negative-access evidence записаны.
- [x] Additive migration only; existing completed M1-01–M1-04 не переписывались.
- [x] Только synthetic data; publication/import/materials не подделаны.

## Открытые границы

- component acceptance не означает production deployment, pilot authorization или legal/compliance acceptance;
- strict isolated runtime DB capability для новых catalog/receipt writes остаётся deployment qualification;
- вход по UUID организации остаётся ранее зафиксированной M1-03 границей; внутри M1-05 course/group/roster UUID пользователю не нужны;
- M1-05 не доказывает published student content: курс намеренно Draft;
- attendance, calendar, analytics, public catalog, import и publication не начинались;
- следующий этап M1-06 не выполнялся.
