# M1-07 — Materials, Lessons, Problems & Teacher Reference: результаты

Дата: 13.10.2026  
Статус: **DONE (component acceptance, synthetic-only)**  
Граница: только M1-07; импорт/публикация, авторизация, регистрация, группы, существующая загрузка фото и B07a/B07b не переписывались. M1-08, commit, push и deploy не выполнялись.

## Итог

Ученик проходит production React-цепочку от каталога M1-05 к опубликованному immutable content M1-06: видит описание курса, доступное занятие, число назначенных задач и фактический прогресс; читает Markdown/LaTeX и защищённое изображение; открывает задачи в опубликованном порядке; работает с постоянным условием над вкладками `Решение | Подсказки | История`; отправляет уже поддерживаемую фотографию решения.

Преподаватель из очереди проверки открывает ту же работу и в независимой правой панели получает эталонное решение, критерии и методические рекомендации. Reference разрешается по `ContentVersionId` immutable assignment этой попытки: опубликованная v2 не заменяет материалы v1.

## Реализованная цепочка

Student:

`LearningCatalog` → `/study/courses/{courseId}` → M1-05 catalog + M1-06 assignments → exact `/my/courses/{courseId}/versions/{contentVersionId}/content` → занятие → `/problem-assignments/{assignmentId}` → existing solution/photo/history workspace.

Teacher:

`ReviewQueuePage` → `TeacherReviewPage` → existing immutable work/assignment → отдельный `/api/v1/teacher/problem-works/{problemWorkId}/reference` → exact teacher artifact/asset projection той же версии.

`OpenAndAssign` теперь создаёт существующий `LessonAccess` для exact opened version/lesson, поэтому публикационная цепочка M1-06 и существующая student workspace read model соединены без второй модели контента.

## UI ученика

- Экран курса использует published title/description/body, реальные openings/assignments и реальные `ProblemWorkStatus`/attempt count. Прогресс считается только по реально назначенным задачам; календарные события и фиктивные метрики отсутствуют.
- Экран занятия показывает Markdown, KaTeX, только manifest-listed student assets через авторизованный Blob URL, задачи в published order, status и переход к exact assignment.
- На экране задачи заголовок и полное условие всегда находятся над вкладками. Отдельной вкладки «Условие» нет.
- Подсказки читаются только из `StudentArtifactJson` exact assignment version и сохраняют опубликованный порядок; empty state остаётся явным.
- Legacy lesson-flow сохраняет возврат через `source=lesson`; новый M1-07 flow использует immutable version URL.

## Teacher reference

- Добавлена отдельная teacher projection: `solution`, `rubric`, `notes` и только относящиеся к course/lesson/problem teacher assets.
- До чтения private artifact сервер разрешает work → assignment → immutable `AssignmentScope`, organization/group S3 scope и current processing gate.
- Reference panel находится в существующей правой контекстной панели; viewer фотографии, аннотации, комментарий и действия проверки остаются на месте.
- Synthetic fixture доказывает все три вида: `M1_TEACHER_ONLY_SOLUTION_v1`, `M1_TEACHER_ONLY_RUBRIC`, `M1_TEACHER_ONLY_NOTES`.

## Безопасность и изоляция

- Student endpoint никогда не читает teacher artifact/assets. Strict frontend decoder отклоняет лишние top-level поля; teacher canaries и `teacherArtifactHash/packageHash` отсутствуют в student JSON и DOM.
- Student/teacher assets выдаются раздельными authenticated endpoints, только после manifest + scope проверки, с `Cache-Control: no-store, private`; teacher asset дополнительно получает `X-Content-Type-Options: nosniff`.
- Teacher endpoint недоступен student token (`403` без private body). Foreign organization/work маскируется до private materialization; group/course authority и current-processing restrictions проверяются authoritative server-side.
- Markdown raw HTML не парсится; `javascript:` и remote images не загружаются; разрешены только HTTPS links и manifest-listed local assets. KaTeX работает с `trust:false` и bounded expansion. Blob URLs отзываются при unmount/logout.
- v2 publication не изменяет v1 assignments, conditions, hints или teacher reference. API и Chromium подтверждают exact `versionNumber=1` после публикации v2.

## Реальный Chromium HTTPS

`ContentPublicationBrowserTests` поднял real HTTPS Kestrel, disposable PostgreSQL, production Vite bundle и `/opt/google/chrome/chrome`. API не mock/fulfill; существующий единственный route abort в конце остаётся M1-06 проверкой unknown result/no replay.

Проверенная цепочка:

1. teacher публикует v1, открывает занятие G1 и затем публикует v2;
2. student входит через React и открывает опубликованный курс;
3. видит exact v1, пять задач и реальный нулевой прогресс;
4. открывает занятие, KaTeX и защищённый PNG;
5. открывает задачу, условие над вкладками, подсказку и пустую историю;
6. прикрепляет PNG решения и отправляет попытку;
7. teacher входит через React, открывает очередь и текущую фотографию;
8. отдельный reference показывает solution/rubric/notes v1 и не содержит solution v2;
9. перехваченные student responses не содержат teacher canaries/hashes и имеют `no-store`;
10. responsive evidence снят на 1440, 768 и 390 px.

Screenshots: `codex/evidence/M1-07-MATERIALS-REFERENCE/`

- `01-course-1440.png`;
- `02-lesson-1440.png`;
- `03-lesson-768.png`;
- `04-lesson-390.png`;
- `05-problem-1440.png`;
- `06-hints-390.png`;
- `07-teacher-reference-1440.png`;
- `test-summary.json`.

## Проверки

| Проверка | Результат |
|---|---:|
| Solution build | PASS, 0 warnings / 0 errors |
| M1-07 protected API/PostgreSQL targeted | 3 PASS / 0 FAIL |
| M1-07 real Chromium HTTPS opt-in | 1 PASS / 0 FAIL |
| Frontend Vitest full | 414 PASS / 0 FAIL |
| Frontend TypeScript + production Vite build | PASS |
| Domain full | 43 PASS / 0 FAIL |
| Application full | 369 PASS / 0 FAIL |
| Infrastructure full | 342 PASS / 0 FAIL |
| API full (opt-in browsers отдельно) | 381 PASS / 10 SKIP / 0 FAIL |

Первый полный frontend-прогон обнаружил несовместимый возврат старого lesson-flow на новый immutable URL; добавлен явный `source=lesson`, после чего navigation tests прошли. Один последующий parallel Vitest run поймал ранее существующий timing-flake одного attachment assertion; тот же файл прошёл 19/19 отдельно, а повторный полный run — 414/414. Финальный Chromium E2E после всех изменений прошёл 1/1.

## Guardrails §7

- [x] Использованы существующие published versions/artifacts, assignment reference, work read model и photo flow; второй content model не создан.
- [x] Student и teacher projections разделены на сервере, а не скрыты вкладками.
- [x] Exact version, v1-after-v2 и private canaries проверены API и браузером.
- [x] Organization/course/group/current-processing checks выполняются до private teacher artifact.
- [x] Markdown/KaTeX/assets fail closed; remote/unsafe content не становится активным.
- [x] 1440/768/390 и реальные HTTPS/PostgreSQL/React/Chromium screenshots записаны.
- [x] M1-08, WYSIWYG/CMS/calendar/analytics, commit, push и deploy не выполнялись.

## Открытые границы

- Component acceptance не закрывает G02, production restricted DB capability, deployment/cutover, backup/retention или legal/compliance acceptance.
- E2E использует только synthetic content/users и disposable PostgreSQL.
- Universal CMS, новый editor, календарь и расширенная аналитика остаются вне M1-07.
