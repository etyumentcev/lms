# J2 — Image Annotation

2026-10-08. Только J2, база `bf3b750`, ветка `codex/j2-image-annotation`.
Канонический checkout `/home/tea/github/lms`; без дополнительных worktrees.
Итоговый commit — commit, содержащий этот отчёт (`git log -1 --oneline`).

## Контракт и архитектура

Контракт записан в ExecPlan §25 до реализации UI. Review получает immutable
`AnnotatedAttachment → PenStroke → StrokePoint`; новый aggregate, lifecycle,
ProblemWork.Status, object storage и annotation endpoints не создавались.
Review.Id / SubmissionId задают связь, AttachmentId проверяется по текущей Submission.
JSONB выбран для чтения/записи целого ограниченного набора без запросов по точкам.
Оригинальные bytes и порядок AttachmentIds не изменяются.

Оба existing review commands принимают optional `annotations`; старые клиенты могут
не передавать поле. Read DTO teacher details / student workspace history включают
массив. Миграция `20261008154119_ReviewAnnotations` добавляет NOT NULL jsonb с default
`[]`; старые Review читаются. Immutable-history PostgreSQL trigger запрещает UPDATE.
EF converter восстанавливает read-only коллекции; Domain делает defensive deep copy.

Штрих: UUID, tool=`pen`, #RRGGBB, width, points с optional pressure. x/y ∈ [0,1]
относительно ориентированного оригинала; width — доля меньшей стороны изображения,
[0.0005,0.05]. Давление [0,1], null → 0.5; реальная толщина width × (0.5 + pressure).
Идентификаторы страниц и штрихов уникальны. Допустимы пустые annotations.

Лимиты на Review: 20 страниц, 2000 штрихов, 4096 точек на штрих, 100000 точек всего.
HTTP body обоих commands ≤4 MiB; реальный Kestrel/nginx smoke доказал 413.
Domain violation → 422 InvalidAnnotations, malformed payload → 400 ValidationFailed.

## Сохранение, доступ, concurrency

Authorization и expected version проверяются существующим Application flow.
Attachment проверяется до добавления feedback/message, Review или изменения статуса.
FeedbackRequired не ослаблен. Единственный PostgresWorkStore.Save коммитит решение,
пометки, статус, version, feedback и события одной транзакцией.
Реальные PostgreSQL tests проверяют rollback после writes и гонку двух reviews с
разными annotations: сохраняется набор победителя, история проигравшего отсутствует.

Нет server drafts или незавершённых Review для чтения учеником. Student и посторонний
teacher не могут отправить annotations. API не принимает произвольную metadata/HTML;
цвет и инструмент ограничены. Фото загружается прежним bearer-aware protected endpoint,
без URL физического хранилища. Оригиналы доступны отдельно через Blob URL.

## Редактор

Canvas поверх точной области img, без object-fit полей. Размер определяется реальными
naturalWidth/naturalHeight браузера после EXIF orientation. Canvas учитывает DPR;
pointer normalization использует transformed image rectangle, включая zoom/pan.
Перо красное по умолчанию, выбор цвета и трёх толщин; pressure optional. Coalesced
points, round joins/caps, requestAnimationFrame. React не обновляется на каждую точку;
сериализация выполняется при финальной команде, не на pointermove.

Ластик удаляет целый пересекаемый штрих (проверяется расстояние до сегментов).
Undo/redo сохраняются отдельно по AttachmentId, до 50 undo действий на страницу.
Zoom 100–400%, Fit, перемещение рукой/клавиатурой; страницы сохраняют zoom/pan/history.
Alt+Left/Right сохраняет focus. Новая попытка не наследует штрихи старой.

Мышь, pen, touch используют Pointer Events. Touch drawing включается явно; по умолчанию
touch предназначен для browser gestures. После pen касания ладонью игнорируются;
Hand допускает touch pan. Второй pointer отменяет provisional stroke. Pointer cancel
не коммитит. Pressure не требуется. Browser palm rejection остаётся эвристикой.

Fullscreen API с fixed-position fallback, Escape/exit, focus trap и scroll lock.
В режиме остаются фото, инструменты, page counter/navigation; shell и feedback не видны.
Student History и завершённая teacher review показывают тот же read-only слой с toggle,
zoom и оригиналом. Графические замечания объявляются текстом с числом штрихов.
J1 statement/feedback/context/history layout и строгие review capabilities сохранены.

Draft остаётся в памяти TeacherReviewPage при paging/history, failed POST и explicit
refresh. При конфликте нет автоматического retry; требуется явное обновление, с
предупреждением о возможной чужой проверке. Pending блокирует редактор и обе команды.
SPA navigation/back и beforeunload предупреждают о потере annotations. После успешного
POST draft очищается, authoritative GET обновляет read-only результат. Если он неуспешен,
повторяется только GET. Восстановление после crash/reload не реализовано и не обещается.

## Проверки

| Проверка | Результат |
|---|---|
| Frontend Vitest | 362 passed (331 baseline + 31 J2), 0 failed/skipped |
| Backend | 248 passed: Domain 43, Application 16, Infrastructure 11, API 178 |
| TypeScript/Vite | PASS |
| .NET 10 build/test compilation | PASS, 0 warnings/errors |
| Legacy Review migration upgrade | PASS, старые annotations=[] |
| PostgreSQL immutable update/atomic rollback/concurrent annotations | PASS |
| Chrome full workflow | PASS, RequestRevision + Accept, errors=[] |
| Responsive 320/375/768/1024/1440 | PASS, no horizontal overflow, exact img/canvas geometry |
| Student History geometry | PASS, fixed-height legacy CSS исключён для annotation image |
| Original bytes / restart | PASS, hashes неизменны после review/restart/down-up |
| HTTP request limit | PASS, >4 MiB →413 |
| Tablet/EXIF/DPR | PASS: orientation 6, 4200×3000 →3000×4200, DPR2, portrait/landscape |
| Pen/stress | PASS: native pen pressure, 201 strokes / 20002 points, ~3.6s drawing scenario |
| Fullscreen | Native API и принудительный fallback PASS |
| git diff --check | PASS |

Frontend tests покрывают исходный protected image, pointer drawing/cancellation,
normalized geometry/zoom/pan/resize, style/eraser/undo/redo, page isolation/history,
fullscreen fallback, touch opt-in/multiple touch, read-only/toggle, normalized command
payload для обоих решений, empty legacy commands, ошибки/409/double-submit J1,
unsaved navigation/reload, foreign attachment rendering и image-decode retry.

Browser scripts: `src/Lms.Web/scripts/annotation-smoke.mjs` и
`annotation-tablet-smoke.mjs`. Только disposable `lms-j2-smoke`, localhost:8085;
`lms-local` volumes не сбрасывались. Tablet script предполагает чистую assignment #2;
основной workflow script заканчивается reset своего disposable stack.

Evidence: `/tmp/lms-j2-{backend,web-tests,web-build,browser,tablet,compose}.log`,
`/tmp/lms-j2-infrastructure-final.log`, `/tmp/lms-j2-browser/results.json`,
`/tmp/lms-j2-tablet/results.json`, screenshots в этих двух каталогах.
Обязательный six-screen mockup и desktop/mobile/fullscreen screenshots просмотрены.

## Обнаруженные и исправленные проблемы

- Keyboard paging терял focus при remount фото — focus восстанавливается.
- Старый Student History CSS задавал 22rem height, создавая contain-поля — annotation
  image имеет собственную пропорциональную геометрию; browser assert сравнивает размеры.
- Пустой getCoalescedEvents пропускал move sample — используется основное событие;
  отдельный regression test и stress assertion требуют сохранения всех 20002 точек.
- Canvas мог закрывать retry при image decode failure — слой скрыт до successful load.

## Границы проверки

Физическое перо/iPad/Safari не доступны: проверены Chrome tablet emulation, touch unit
scenarios и native pen events через CDP, без заявления о hardware certification.
Browser-controlled pinch остаётся нативным; zoom кнопками и Hand управляют фото.
Undo ограничен 50 действиями; 4096 точек завершают вместимость одного штриха с уведомлением.
Черновик не сохраняется на сервер/диск. J1 gaps reference solutions/criteria остаются:
источник этих данных отсутствует, вымышленные поля не добавлялись.

ExecPlan и Domain/Feature/Frontend contract amendments обновлены. После commit — остановка.
