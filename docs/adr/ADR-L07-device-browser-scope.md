# ADR-L07 — Device & browser scope

Статус: **DECISION REQUIRED C07**, рекомендуемый процесс **PROPOSAL B08**. [Реестр](../../codex/L-DECISION-REGISTER.md).

## FACT

[K device protocol](../../codex/K-DEVICE-TEST-PROTOCOL.md) содержит 90 физических device/scenario строк; они BLOCKED, исполнитель/версии не получены. [K results](../../codex/K-RESULTS.md) фиксирует headless Chrome и CDP tablet emulation PASS, не Safari/Apple Pencil. [UI Flow](../ui/LMS-UI-FLOW-SPEC.md) и [Frontend contract](../ui/LMS-FRONTEND-CONTRACT.md) сохраняют photo-first и tablet usability; сокращение pilot scope не отменяет будущие требования UI. [annotation-tablet-smoke.mjs](../../src/Lms.Web/scripts/annotation-tablet-smoke.mjs) пригоден для автоматизированной регрессии geometry/EXIF/DPR, не palm rejection evidence.

## Альтернативы / recommendation

| Pilot scope | Обязательная проверка в L4 | Стоимость/ограничение |
|---|---|---|
| Desktop-only с явно указанными OS/Chromium versions, mouse/keyboard | Физический desktop: весь цикл, 2 фото/order, zoom, mouse drawing, undo/redo, fullscreen/fallback, keyboard/focus, History/reload, HTTPS login/logout/expiry/две вкладки | **Минимальный рекомендуемый scope**, только если ученики реально могут переносить фото на desktop. Tablet/Safari не supported; не заявлять universal desktop support |
| Desktop + iPad/Safari (Pencil и touch отдельно) | Всё desktop + соответствующие строки K для реального iPad/OS/Safari/Pencil: palm rejection, pen pressure continuity, finger opt-in, Hand/Pen/pinch, pan, orientation/split view/resize, fullscreen fallback, 2-page overlay/EXIF, submit/reopen/Student History; camera/gallery и cookie lifecycle | Обязателен доступный hardware/tester; Linux WebKit не заменяет этот gate |
| Desktop + Android tablet | Всё desktop + K Android на конкретной модели/OS/browser/stylus (если заявлен); camera/gallery, touch/pen/palm/pinch, rotation/fullscreen, geometry и session lifecycle | Android fragmentation: acceptance ограничен протестированными устройствами/версиями; не означает Safari support |

Не исключать iPad только ради формального PASS: если это основной ввод преподавателя, рекомендовать второй вариант и оставить release BLOCKED до испытаний. UNKNOWN реальный состав устройств/пилотной группы. Владелец выбирает scope, реальные модели/версии и исполнителя; unsupported строки можно пометить NOT APPLICABLE только ссылкой на его решение, не конвертировать BLOCKED в PASS.

## PROPOSAL — acceptance evidence

Reuse `release-smoke.mjs`, `annotation-tablet-smoke.mjs`, K protocol. Добавить L Identity cookie login/expiry/recovery/session switch на каждом выбранном браузере, реальную камеру и noisy/high-resolution JPEG наряду с synthetic PNG. HTTPS target со staging Identity accounts, никакой вставки dev bearer. Записать commit, image/config revision, URL, модель/OS/browser, input/DPR/zoom, tester/date, PASS/FAIL/BLOCKED, redacted screenshot/video/network evidence; сравнить SHA-256 originals и сохранённые annotations двух attempts.

Автоматизированные geometry assertions сохранить, визуальную точность и приемлемость задержки проверяет человек по agreed workload/budgets C06/C07. Поворот/zoom не теряет strokes, canceled touch не создаёт лишние, page annotations независимы; expiry не отправляет команду повторно. Physical failure воспроизводить минимальным сценарием; исправления проходят отдельный scoped review/regression до повторного acceptance. Browser capability detection/fullscreen fallback уже есть — переиспользовать, не добавлять новый canvas/editor без доказанного дефекта.

B08 — технически определить воспроизводимый protocol/evidence; C07 — owner commitment к devices/tester/roster/workaround. L1/L2 локально не блокируются отсутствием iPad, L4 READY для iPad блокируется. В L0 новые прогоны устройств не выполнены, Safari и Apple Pencil **NOT TESTED**.
