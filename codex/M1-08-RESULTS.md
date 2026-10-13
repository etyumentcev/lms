# M1-08 — Protected Multi-Photo Submission: результаты

Дата: 13.10.2026
Статус: **DONE (component acceptance, synthetic-only)**
Граница: только M1-08; существующие authorization, publication/content, `AttachmentService`, private storage, PostgreSQL metadata, submission CAS и Teacher Revocation routes переиспользованы. M1-09, push и deploy не выполнялись.

## Git preflight

- canonical checkout: `/home/tea/github/lms`;
- branch: `main`, HEAD до M1-08: `2052548fe62679f92907e4b6c4a0c1259d23db5c`;
- upstream: `main...origin/main [ahead 38]`;
- index и worktree до начала были чистыми;
- `MERGE_HEAD` отсутствовал, конфликтов и незавершённого merge не было.

Указанный `codex/SAPIENTUM-M1-PLAN.md` отсутствует в checkout. Использован совпадающий раздел M1-08 действующего единого плана `codex/Sapientum_M1_Codex_Tasks_All.md`. Файл макета `интерфейс_решения_олимпиадной_задачи_по_математике.png` также отсутствует; сохранена принятая M1-07 структура экрана и выполнены явные UI-требования задания.

## Контракт

- **Actor → Use Case:** ученик выбирает несколько JPEG/PNG, просматривает, меняет порядок, удаляет/повторно добавляет и один раз отправляет оригинальные страницы; преподаватель читает их в установленном порядке через защищённый Teacher Revocation route.
- **Invariants:** условие выше вкладок; текстовый комментарий необязателен; нового `InProgress` нет; единственный `ProblemWork`; порядок равен порядку `AttachmentIds`; байты оригинала и SHA-256 неизменны; pending/deleted attachment не виден преподавателю; неизвестный исход не создаёт новый intent/attempt.
- **Entities/VO:** существующие `ProblemWork`, `SubmissionIntent`, `AttachmentMetadata`, private storage object и immutable assignment scope.
- **Transitions:** `NotStarted | NeedsRevision → Submitted`; локальные страницы `uploading → uploaded | failed | cancelled`; подтверждение после authoritative `GET`.
- **Authorization/data source:** work → assignment → owner/organization/course/group → attachment parent/owner → processing restrictions; student и teacher читают через существующие projections/routes, PostgreSQL является authoritative metadata store.
- **Write semantics:** `PostgresWorkStore` владеет атомарным CAS/claim; порядок блокировок и commit/audit не менялись. Upload — отдельная pending-операция. При потерянном ACK UI не повторяет submit, сохраняет тот же `IntentId` и предлагает проверить серверное состояние.

## Реализация

`AttachmentUploader` теперь показывает основное действие `Сфотографировать / Выбрать фото`, принимает несколько JPEG/PNG и оставляет каждую страницу отдельной карточкой с preview, именем, размером, перемещением, удалением и индивидуальной ошибкой. Активную загрузку можно отменить; отменённая страница остаётся в списке с `Загрузка отменена.` и доступным повтором.

`SubmissionForm` продолжает использовать существующие attachment IDs и отправляет их в текущем визуальном порядке без обязательного комментария. Existing pending/submit/unknown/reload logic не заменён второй системой. Двойное нажатие блокируется; после неопределённого результата доступен authoritative refresh, без автоматического POST и нового intent.

Backend-схема и маршруты не расширялись. Browser acceptance дополнен byte/hash/storage oracle и защищённым browser-authenticated чтением обеих страниц преподавателем.

## Реальный Chromium HTTPS

`ContentPublicationBrowserTests` поднял real HTTPS Kestrel, disposable PostgreSQL, production Vite bundle и `/opt/google/chrome/chrome`. Все пользовательские действия выполнялись через production React UI и обычный login.

Проверено:

1. ученик открыл опубликованную задачу на 390 px;
2. выбрал два реальных PNG A4 из `tests/fixtures/photos/`;
3. увидел previews, поменял порядок на `a4-2.png`, `a4-1.png`;
4. удалил и повторно добавил страницу; всего три upload, одна pending page удалена;
5. дважды нажал submit, но браузер отправил ровно один request и создал одну работу;
6. response после серверного commit намеренно потерян; UI показал unknown outcome и выполнил только authoritative `GET`;
7. reload восстановил `На проверке` и две страницы в правильном порядке;
8. преподаватель обычным login открыл очередь и обе страницы через protected routes;
9. PostgreSQL/private-storage oracle подтвердил parent, filenames, порядок, исходные bytes и SHA-256;
10. responsive evidence записан при 390, 768 и 1440 px.

Итоговые hashes в порядке submission:

- `a4-2.png`: `35437474fbf2306a1ca05c8da520233e01a29444bf93fe448949f499150c0745`;
- `a4-1.png`: `d93fc9aada5ba8db936463c3e514236b51f8071fdc7f1a2c838077f1241b57b8`.

Evidence: `codex/evidence/M1-08-PROTECTED-MULTI-PHOTO/`

- `01-upload-preview-390.png`;
- `02-reordered-preview-768.png`;
- `03-delete-readd-preview-1440.png`;
- `04-submitted-reload-390.png`;
- `05-teacher-page-1-1440.png`;
- `06-teacher-page-2-1440.png`;
- `test-summary.json`.

Файлы screenshots существуют, имеют ненулевой размер 85–214 KB и получены зелёным Chromium-сценарием. Встроенный image-view helper в этой среде завершался sandbox-ошибкой `mountinfo path is not absolute`, поэтому отдельная ручная визуальная инспекция PNG инструментом не заявляется.

## Ошибки и повторные операции

| Сценарий | Результат |
|---|---|
| превышение размера, неподдерживаемый MIME/extension, повреждённый PNG/JPEG | deny; API negative tests PASS, ошибка остаётся у отдельной страницы |
| network error | страница `failed`, доступен явный retry; submit не начинается |
| отмена upload | `AbortController`, отдельная страница остаётся с явной ошибкой; Vitest PASS |
| истечение/отзыв сессии | существующий auth refresh/logout contract; full API/frontend regression PASS |
| двойной submit | один browser request, одна submission, `doubleClickRequests=1` |
| version conflict/CAS | conflict не создаёт вторую попытку; concurrency tests PASS |
| потерянный ответ после commit | unknown state → ручной authoritative `GET`; POST не повторяется, intent не меняется |

## Authorization и negative access

| Проверка | Статус и evidence |
|---|---|
| foreign owner / чужая фотография | **covered** — protected attachment/API tests deny |
| wrong organization | **covered** — cross-org read/submit tests deny до private materialization |
| wrong course/group/assignment | **covered** — immutable scope/group/parent tests deny |
| missing/forged parent | **covered** — attachment consistency and submit validation deny |
| revoked/moved membership | **covered** — revoked enrollment/lesson/assignment tests deny по текущей temporal semantics |
| pending/deleted/missing processing evidence | **covered** — teacher read и submit deny |
| inaccessible attachment / legacy teacher bypass | **covered** — protected routes only; tombstone/foreign/legacy negative tests PASS |
| duplicate effects / mutable history | **covered** — one request/work, immutable ordered IDs and history regression PASS |
| counts/pagination leakage | **not applicable —** новый list/pagination path не добавлен; существующая teacher queue regression прошла |
| post-pagination filtering | **not applicable —** M1-08 не изменяет выборку очереди |
| unsafe restriction narrowing / stale cross-transaction auth | **covered** — gates и atomic claim не менялись; revoke/concurrency suite PASS |
| secrets | **covered** — новых секретов, public URLs или логирования token/body не добавлено |

## Проверки

| Проверка | Результат |
|---|---:|
| `dotnet build Lms.sln --no-restore` | PASS, 0 warnings / 0 errors |
| targeted attachment/submission/teacher API suite | 40 PASS / 0 FAIL |
| M1-08 real Chromium HTTPS opt-in | 1 PASS / 0 FAIL |
| frontend attachment/problem/teacher targeted | 140 PASS / 0 FAIL |
| frontend Vitest full | 415 PASS / 0 FAIL |
| frontend TypeScript + production Vite build | PASS |
| Domain full | 43 PASS / 0 FAIL |
| Application full | 369 PASS / 0 FAIL |
| Infrastructure full | 342 PASS / 0 FAIL |
| API full, serial, opt-in browsers отдельно | 381 PASS / 10 SKIP / 0 FAIL |

Первый full API запуск выполнялся параллельно с Infrastructure и получил один transient failure в уже существующем M1-06 publish-concurrency test: ожидаемые `OK + Conflict` пришли как `Conflict + Conflict`. Тот же тест немедленно прошёл отдельно 1/1; обязательный повтор всего API serial завершился 381 PASS, 10 SKIP, 0 FAIL. Финальный Chromium M1-08 сценарий после всех изменений прошёл 1/1.

## Guardrails §7

- [x] Client не является источником authority; IDOR/cross-org/course/group/assignment/parent проверки выполняются server-side.
- [x] Private originals и metadata используют существующие storage/PostgreSQL/AttachmentService; второй attachment model не создан.
- [x] Claim/order/submit остаются атомарными с CAS; unknown outcome не повторяет write.
- [x] Teacher читает только через current protected Teacher Revocation routes; legacy API не возвращён.
- [x] Originals сверены по bytes и SHA-256; pending/deleted attachment не попал в submission.
- [x] Positive controls и независимо asserted negative access прошли.
- [x] 390/768/1440, normal login, HTTPS, production React, PostgreSQL и Chromium evidence записаны.
- [x] Новый `InProgress`, новые permissions/schema/status и физические fs-verity guarantees не добавлены.
- [x] M1-09, photo annotation/full review cycle, push и deploy не выполнялись.

## Открытые границы

- Component acceptance использует synthetic users/content и disposable PostgreSQL; это не `E2E ACCEPTED`, не `PILOT AUTHORIZED` и не закрытие G02/production cutover.
- На реальном browser path проверены два PNG; JPEG/PNG parsing, exact-byte storage и reject cases отдельно покрыты API tests.
- Реальная камера физического телефона и server process restart не запускались; reload страницы и восстановление authoritative состояния проверены.
- Физическое прекращение file operations/fs-verity не относится к принятой photo-storage модели M1-08 и не заявляется.
