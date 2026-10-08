# K — Manual device test protocol

Физические устройства не предоставлены. **BLOCKED — physical device unavailable**
для всех физических прогонов ниже. Это не PASS и не подпись проверяющего.
Linux headless Chrome и CDP pen/tablet emulation описаны отдельно в K-RESULTS;
они не подтверждают Apple Pencil, palm rejection, Safari или Android hardware.

Подготовка: выделенный pilot/staging dataset, Student/Teacher с разрешённым курсом,
две различные фотографии A4 (включая JPEG EXIF6), Submitted #1. Не использовать
реальные работы для reset. Записать модель устройства, OS/build, browser/version,
тип пера, DPR, масштаб ОС, commit и URL окружения. Пройти revision → attempt2 →
accept. Сверять attachment IDs и SHA-256 до/после, не только внешний вид.

Для каждого прогона заполнить фактический результат, PASS/FAIL/BLOCKED, ссылку на
screenshot/video/лог и имя реального исполнителя. Сейчас эти поля не вымышлены.
При FAIL приложить минимальный сценарий, console/network errors без токенов и severity.
Поворот/resize должны сохранять пиксельное совпадение слоя, точность проверяется по
выбранной детали оригинала до/после zoom; допуск согласовать с владельцем пилота.

## iPad + Apple Pencil

OS/browser version: **не получены**. Исполнитель/дата: **не назначены**.

| № / проверка | Шаги | Ожидаемый результат | Фактический результат | Статус | Evidence |
|---|---|---|---|---|---|
| 1. Стилус | Открыть Submitted с двумя фото; провести тонкий и сильный штрих стилусом | Непрерывный штрих следует наконечнику; давление не обязательно для сохранения | Не выполнено | BLOCKED — physical device unavailable | — |
| 2. Palm rejection | Коснуться ладонью до/после контакта пера и продолжить линию | Нет лишних штрихов, случайного pan/zoom или потери основного штриха | Не выполнено | BLOCKED — physical device unavailable | — |
| 3. Палец | Проверить обычный touch; включить «Рисовать пальцем» и нарисовать | Без opt-in нет штриха; после opt-in штрих сохраняется | Не выполнено | BLOCKED — physical device unavailable | — |
| 4. Pen/Hand | Нарисовать; переключить «Рука», переместить; вернуться к «Перо» | Режимы не смешиваются, прежние пометки сохраняются | Не выполнено | BLOCKED — physical device unavailable | — |
| 5. Pinch | Два пальца в режиме «Рука»; повторить в «Перо» | Hand допускает browser pinch; Pen резервирует поверхность и не портит штрих | Не выполнено | BLOCKED — physical device unavailable | — |
| 6. Pan | На 200% переместить фото рукой; провести линию после смещения | Фото/слой совпадают, координаты относительно изображения | Не выполнено | BLOCKED — physical device unavailable | — |
| 7. Undo/Redo | Нарисовать два штриха, отменить/повторить; перейти на вторую страницу | Точный штрих восстанавливается, история страниц независима | Не выполнено | BLOCKED — physical device unavailable | — |
| 8. Fullscreen | Войти/выйти; если API недоступен, проверить fallback, Escape/кнопку выхода | Фото, инструменты и страницы доступны, выход восстанавливает focus/scroll | Не выполнено | BLOCKED — physical device unavailable | — |
| 9. Поворот | С аннотацией сменить portrait/landscape | Нет потери штриха и сдвига слоя относительно фото | Не выполнено | BLOCKED — physical device unavailable | — |
| 10. Resize | Изменить viewport/split view при открытом фото и в fullscreen | Размер слоя совпадает с фото, controls доступны | Не выполнено | BLOCKED — physical device unavailable | — |
| 11. Страницы | Добавить разные штрихи на обе страницы; переключить туда/обратно | Своя аннотация, zoom и undo для каждой страницы | Не выполнено | BLOCKED — physical device unavailable | — |
| 12. Координаты | Отметить угол/деталь при 100% и 200%, pan; сохранить; открыть снова | Отметки остаются на деталях ориентированного оригинала, включая EXIF6 | Не выполнено | BLOCKED — physical device unavailable | — |
| 13. Отправка Review | Пустой feedback + revision, затем заполнить; отдельную попытку принять без feedback | Revision требует текст; одно действие даёт один Review, Accept допускает пустой текст | Не выполнено | BLOCKED — physical device unavailable | — |
| 14. Повторное открытие | После подтверждения перезагрузить глубокую ссылку Teacher Review | Та же попытка/пометки, завершённый Review только для чтения | Не выполнено | BLOCKED — physical device unavailable | — |
| 15. Student History | Войти Student, открыть History обеих попыток и оригиналы | Feedback/слои видны, редактора нет, оригиналы неизменны | Не выполнено | BLOCKED — physical device unavailable | — |

## iPad Safari (touch)

OS/browser version: **не получены**. Исполнитель/дата: **не назначены**.

| № / проверка | Шаги | Ожидаемый результат | Фактический результат | Статус | Evidence |
|---|---|---|---|---|---|
| 1. Стилус | Открыть Submitted с двумя фото; провести тонкий и сильный штрих стилусом | Непрерывный штрих следует наконечнику; давление не обязательно для сохранения | Не выполнено | BLOCKED — physical device unavailable | — |
| 2. Palm rejection | Коснуться ладонью до/после контакта пера и продолжить линию | Нет лишних штрихов, случайного pan/zoom или потери основного штриха | Не выполнено | BLOCKED — physical device unavailable | — |
| 3. Палец | Проверить обычный touch; включить «Рисовать пальцем» и нарисовать | Без opt-in нет штриха; после opt-in штрих сохраняется | Не выполнено | BLOCKED — physical device unavailable | — |
| 4. Pen/Hand | Нарисовать; переключить «Рука», переместить; вернуться к «Перо» | Режимы не смешиваются, прежние пометки сохраняются | Не выполнено | BLOCKED — physical device unavailable | — |
| 5. Pinch | Два пальца в режиме «Рука»; повторить в «Перо» | Hand допускает browser pinch; Pen резервирует поверхность и не портит штрих | Не выполнено | BLOCKED — physical device unavailable | — |
| 6. Pan | На 200% переместить фото рукой; провести линию после смещения | Фото/слой совпадают, координаты относительно изображения | Не выполнено | BLOCKED — physical device unavailable | — |
| 7. Undo/Redo | Нарисовать два штриха, отменить/повторить; перейти на вторую страницу | Точный штрих восстанавливается, история страниц независима | Не выполнено | BLOCKED — physical device unavailable | — |
| 8. Fullscreen | Войти/выйти; если API недоступен, проверить fallback, Escape/кнопку выхода | Фото, инструменты и страницы доступны, выход восстанавливает focus/scroll | Не выполнено | BLOCKED — physical device unavailable | — |
| 9. Поворот | С аннотацией сменить portrait/landscape | Нет потери штриха и сдвига слоя относительно фото | Не выполнено | BLOCKED — physical device unavailable | — |
| 10. Resize | Изменить viewport/split view при открытом фото и в fullscreen | Размер слоя совпадает с фото, controls доступны | Не выполнено | BLOCKED — physical device unavailable | — |
| 11. Страницы | Добавить разные штрихи на обе страницы; переключить туда/обратно | Своя аннотация, zoom и undo для каждой страницы | Не выполнено | BLOCKED — physical device unavailable | — |
| 12. Координаты | Отметить угол/деталь при 100% и 200%, pan; сохранить; открыть снова | Отметки остаются на деталях ориентированного оригинала, включая EXIF6 | Не выполнено | BLOCKED — physical device unavailable | — |
| 13. Отправка Review | Пустой feedback + revision, затем заполнить; отдельную попытку принять без feedback | Revision требует текст; одно действие даёт один Review, Accept допускает пустой текст | Не выполнено | BLOCKED — physical device unavailable | — |
| 14. Повторное открытие | После подтверждения перезагрузить глубокую ссылку Teacher Review | Та же попытка/пометки, завершённый Review только для чтения | Не выполнено | BLOCKED — physical device unavailable | — |
| 15. Student History | Войти Student, открыть History обеих попыток и оригиналы | Feedback/слои видны, редактора нет, оригиналы неизменны | Не выполнено | BLOCKED — physical device unavailable | — |

## Touch без стилуса (целевое физическое устройство)

OS/browser version: **не получены**. Исполнитель/дата: **не назначены**.

| № / проверка | Шаги | Ожидаемый результат | Фактический результат | Статус | Evidence |
|---|---|---|---|---|---|
| 1. Стилус | Открыть Submitted с двумя фото; провести тонкий и сильный штрих стилусом | Непрерывный штрих следует наконечнику; давление не обязательно для сохранения | Не выполнено | BLOCKED — physical device unavailable | — |
| 2. Palm rejection | Коснуться ладонью до/после контакта пера и продолжить линию | Нет лишних штрихов, случайного pan/zoom или потери основного штриха | Не выполнено | BLOCKED — physical device unavailable | — |
| 3. Палец | Проверить обычный touch; включить «Рисовать пальцем» и нарисовать | Без opt-in нет штриха; после opt-in штрих сохраняется | Не выполнено | BLOCKED — physical device unavailable | — |
| 4. Pen/Hand | Нарисовать; переключить «Рука», переместить; вернуться к «Перо» | Режимы не смешиваются, прежние пометки сохраняются | Не выполнено | BLOCKED — physical device unavailable | — |
| 5. Pinch | Два пальца в режиме «Рука»; повторить в «Перо» | Hand допускает browser pinch; Pen резервирует поверхность и не портит штрих | Не выполнено | BLOCKED — physical device unavailable | — |
| 6. Pan | На 200% переместить фото рукой; провести линию после смещения | Фото/слой совпадают, координаты относительно изображения | Не выполнено | BLOCKED — physical device unavailable | — |
| 7. Undo/Redo | Нарисовать два штриха, отменить/повторить; перейти на вторую страницу | Точный штрих восстанавливается, история страниц независима | Не выполнено | BLOCKED — physical device unavailable | — |
| 8. Fullscreen | Войти/выйти; если API недоступен, проверить fallback, Escape/кнопку выхода | Фото, инструменты и страницы доступны, выход восстанавливает focus/scroll | Не выполнено | BLOCKED — physical device unavailable | — |
| 9. Поворот | С аннотацией сменить portrait/landscape | Нет потери штриха и сдвига слоя относительно фото | Не выполнено | BLOCKED — physical device unavailable | — |
| 10. Resize | Изменить viewport/split view при открытом фото и в fullscreen | Размер слоя совпадает с фото, controls доступны | Не выполнено | BLOCKED — physical device unavailable | — |
| 11. Страницы | Добавить разные штрихи на обе страницы; переключить туда/обратно | Своя аннотация, zoom и undo для каждой страницы | Не выполнено | BLOCKED — physical device unavailable | — |
| 12. Координаты | Отметить угол/деталь при 100% и 200%, pan; сохранить; открыть снова | Отметки остаются на деталях ориентированного оригинала, включая EXIF6 | Не выполнено | BLOCKED — physical device unavailable | — |
| 13. Отправка Review | Пустой feedback + revision, затем заполнить; отдельную попытку принять без feedback | Revision требует текст; одно действие даёт один Review, Accept допускает пустой текст | Не выполнено | BLOCKED — physical device unavailable | — |
| 14. Повторное открытие | После подтверждения перезагрузить глубокую ссылку Teacher Review | Та же попытка/пометки, завершённый Review только для чтения | Не выполнено | BLOCKED — physical device unavailable | — |
| 15. Student History | Войти Student, открыть History обеих попыток и оригиналы | Feedback/слои видны, редактора нет, оригиналы неизменны | Не выполнено | BLOCKED — physical device unavailable | — |

## Desktop Safari (macOS)

OS/browser version: **не получены**. Исполнитель/дата: **не назначены**.

| № / проверка | Шаги | Ожидаемый результат | Фактический результат | Статус | Evidence |
|---|---|---|---|---|---|
| 1. Стилус | Открыть Submitted с двумя фото; провести тонкий и сильный штрих стилусом | Непрерывный штрих следует наконечнику; давление не обязательно для сохранения | Не выполнено | BLOCKED — physical device unavailable | — |
| 2. Palm rejection | Коснуться ладонью до/после контакта пера и продолжить линию | Нет лишних штрихов, случайного pan/zoom или потери основного штриха | Не выполнено | BLOCKED — physical device unavailable | — |
| 3. Палец | Проверить обычный touch; включить «Рисовать пальцем» и нарисовать | Без opt-in нет штриха; после opt-in штрих сохраняется | Не выполнено | BLOCKED — physical device unavailable | — |
| 4. Pen/Hand | Нарисовать; переключить «Рука», переместить; вернуться к «Перо» | Режимы не смешиваются, прежние пометки сохраняются | Не выполнено | BLOCKED — physical device unavailable | — |
| 5. Pinch | Два пальца в режиме «Рука»; повторить в «Перо» | Hand допускает browser pinch; Pen резервирует поверхность и не портит штрих | Не выполнено | BLOCKED — physical device unavailable | — |
| 6. Pan | На 200% переместить фото рукой; провести линию после смещения | Фото/слой совпадают, координаты относительно изображения | Не выполнено | BLOCKED — physical device unavailable | — |
| 7. Undo/Redo | Нарисовать два штриха, отменить/повторить; перейти на вторую страницу | Точный штрих восстанавливается, история страниц независима | Не выполнено | BLOCKED — physical device unavailable | — |
| 8. Fullscreen | Войти/выйти; если API недоступен, проверить fallback, Escape/кнопку выхода | Фото, инструменты и страницы доступны, выход восстанавливает focus/scroll | Не выполнено | BLOCKED — physical device unavailable | — |
| 9. Поворот | С аннотацией сменить portrait/landscape | Нет потери штриха и сдвига слоя относительно фото | Не выполнено | BLOCKED — physical device unavailable | — |
| 10. Resize | Изменить viewport/split view при открытом фото и в fullscreen | Размер слоя совпадает с фото, controls доступны | Не выполнено | BLOCKED — physical device unavailable | — |
| 11. Страницы | Добавить разные штрихи на обе страницы; переключить туда/обратно | Своя аннотация, zoom и undo для каждой страницы | Не выполнено | BLOCKED — physical device unavailable | — |
| 12. Координаты | Отметить угол/деталь при 100% и 200%, pan; сохранить; открыть снова | Отметки остаются на деталях ориентированного оригинала, включая EXIF6 | Не выполнено | BLOCKED — physical device unavailable | — |
| 13. Отправка Review | Пустой feedback + revision, затем заполнить; отдельную попытку принять без feedback | Revision требует текст; одно действие даёт один Review, Accept допускает пустой текст | Не выполнено | BLOCKED — physical device unavailable | — |
| 14. Повторное открытие | После подтверждения перезагрузить глубокую ссылку Teacher Review | Та же попытка/пометки, завершённый Review только для чтения | Не выполнено | BLOCKED — physical device unavailable | — |
| 15. Student History | Войти Student, открыть History обеих попыток и оригиналы | Feedback/слои видны, редактора нет, оригиналы неизменны | Не выполнено | BLOCKED — physical device unavailable | — |

## Chromium desktop (физический ввод)

OS/browser version: **не получены**. Исполнитель/дата: **не назначены**.

| № / проверка | Шаги | Ожидаемый результат | Фактический результат | Статус | Evidence |
|---|---|---|---|---|---|
| 1. Стилус | Открыть Submitted с двумя фото; провести тонкий и сильный штрих стилусом | Непрерывный штрих следует наконечнику; давление не обязательно для сохранения | Не выполнено | BLOCKED — physical device unavailable | — |
| 2. Palm rejection | Коснуться ладонью до/после контакта пера и продолжить линию | Нет лишних штрихов, случайного pan/zoom или потери основного штриха | Не выполнено | BLOCKED — physical device unavailable | — |
| 3. Палец | Проверить обычный touch; включить «Рисовать пальцем» и нарисовать | Без opt-in нет штриха; после opt-in штрих сохраняется | Не выполнено | BLOCKED — physical device unavailable | — |
| 4. Pen/Hand | Нарисовать; переключить «Рука», переместить; вернуться к «Перо» | Режимы не смешиваются, прежние пометки сохраняются | Не выполнено | BLOCKED — physical device unavailable | — |
| 5. Pinch | Два пальца в режиме «Рука»; повторить в «Перо» | Hand допускает browser pinch; Pen резервирует поверхность и не портит штрих | Не выполнено | BLOCKED — physical device unavailable | — |
| 6. Pan | На 200% переместить фото рукой; провести линию после смещения | Фото/слой совпадают, координаты относительно изображения | Не выполнено | BLOCKED — physical device unavailable | — |
| 7. Undo/Redo | Нарисовать два штриха, отменить/повторить; перейти на вторую страницу | Точный штрих восстанавливается, история страниц независима | Не выполнено | BLOCKED — physical device unavailable | — |
| 8. Fullscreen | Войти/выйти; если API недоступен, проверить fallback, Escape/кнопку выхода | Фото, инструменты и страницы доступны, выход восстанавливает focus/scroll | Не выполнено | BLOCKED — physical device unavailable | — |
| 9. Поворот | С аннотацией сменить portrait/landscape | Нет потери штриха и сдвига слоя относительно фото | Не выполнено | BLOCKED — physical device unavailable | — |
| 10. Resize | Изменить viewport/split view при открытом фото и в fullscreen | Размер слоя совпадает с фото, controls доступны | Не выполнено | BLOCKED — physical device unavailable | — |
| 11. Страницы | Добавить разные штрихи на обе страницы; переключить туда/обратно | Своя аннотация, zoom и undo для каждой страницы | Не выполнено | BLOCKED — physical device unavailable | — |
| 12. Координаты | Отметить угол/деталь при 100% и 200%, pan; сохранить; открыть снова | Отметки остаются на деталях ориентированного оригинала, включая EXIF6 | Не выполнено | BLOCKED — physical device unavailable | — |
| 13. Отправка Review | Пустой feedback + revision, затем заполнить; отдельную попытку принять без feedback | Revision требует текст; одно действие даёт один Review, Accept допускает пустой текст | Не выполнено | BLOCKED — physical device unavailable | — |
| 14. Повторное открытие | После подтверждения перезагрузить глубокую ссылку Teacher Review | Та же попытка/пометки, завершённый Review только для чтения | Не выполнено | BLOCKED — physical device unavailable | — |
| 15. Student History | Войти Student, открыть History обеих попыток и оригиналы | Feedback/слои видны, редактора нет, оригиналы неизменны | Не выполнено | BLOCKED — physical device unavailable | — |

## Android tablet

OS/browser version: **не получены**. Исполнитель/дата: **не назначены**.

| № / проверка | Шаги | Ожидаемый результат | Фактический результат | Статус | Evidence |
|---|---|---|---|---|---|
| 1. Стилус | Открыть Submitted с двумя фото; провести тонкий и сильный штрих стилусом | Непрерывный штрих следует наконечнику; давление не обязательно для сохранения | Не выполнено | BLOCKED — physical device unavailable | — |
| 2. Palm rejection | Коснуться ладонью до/после контакта пера и продолжить линию | Нет лишних штрихов, случайного pan/zoom или потери основного штриха | Не выполнено | BLOCKED — physical device unavailable | — |
| 3. Палец | Проверить обычный touch; включить «Рисовать пальцем» и нарисовать | Без opt-in нет штриха; после opt-in штрих сохраняется | Не выполнено | BLOCKED — physical device unavailable | — |
| 4. Pen/Hand | Нарисовать; переключить «Рука», переместить; вернуться к «Перо» | Режимы не смешиваются, прежние пометки сохраняются | Не выполнено | BLOCKED — physical device unavailable | — |
| 5. Pinch | Два пальца в режиме «Рука»; повторить в «Перо» | Hand допускает browser pinch; Pen резервирует поверхность и не портит штрих | Не выполнено | BLOCKED — physical device unavailable | — |
| 6. Pan | На 200% переместить фото рукой; провести линию после смещения | Фото/слой совпадают, координаты относительно изображения | Не выполнено | BLOCKED — physical device unavailable | — |
| 7. Undo/Redo | Нарисовать два штриха, отменить/повторить; перейти на вторую страницу | Точный штрих восстанавливается, история страниц независима | Не выполнено | BLOCKED — physical device unavailable | — |
| 8. Fullscreen | Войти/выйти; если API недоступен, проверить fallback, Escape/кнопку выхода | Фото, инструменты и страницы доступны, выход восстанавливает focus/scroll | Не выполнено | BLOCKED — physical device unavailable | — |
| 9. Поворот | С аннотацией сменить portrait/landscape | Нет потери штриха и сдвига слоя относительно фото | Не выполнено | BLOCKED — physical device unavailable | — |
| 10. Resize | Изменить viewport/split view при открытом фото и в fullscreen | Размер слоя совпадает с фото, controls доступны | Не выполнено | BLOCKED — physical device unavailable | — |
| 11. Страницы | Добавить разные штрихи на обе страницы; переключить туда/обратно | Своя аннотация, zoom и undo для каждой страницы | Не выполнено | BLOCKED — physical device unavailable | — |
| 12. Координаты | Отметить угол/деталь при 100% и 200%, pan; сохранить; открыть снова | Отметки остаются на деталях ориентированного оригинала, включая EXIF6 | Не выполнено | BLOCKED — physical device unavailable | — |
| 13. Отправка Review | Пустой feedback + revision, затем заполнить; отдельную попытку принять без feedback | Revision требует текст; одно действие даёт один Review, Accept допускает пустой текст | Не выполнено | BLOCKED — physical device unavailable | — |
| 14. Повторное открытие | После подтверждения перезагрузить глубокую ссылку Teacher Review | Та же попытка/пометки, завершённый Review только для чтения | Не выполнено | BLOCKED — physical device unavailable | — |
| 15. Student History | Войти Student, открыть History обеих попыток и оригиналы | Feedback/слои видны, редактора нет, оригиналы неизменны | Не выполнено | BLOCKED — physical device unavailable | — |

## Доступные автоматизированные проверки, отдельно от физических

`release-smoke.mjs`: real Chrome mouse drawing/undo/redo/pages/fullscreen, widths
320/375/768/1024/1440, обе попытки и persisted annotations. `annotation-tablet-smoke.mjs`:
Chrome mobile viewport/DPR2, EXIF6, CDP pointerType=pen, pressure, portrait/landscape,
forced fullscreen fallback, 201 strokes. Результат текущего запуска и артефакты —
K-TEST-MATRIX; эти пункты не заполняют физические строки выше.

Safari/WebKit engine не запускался. Даже Playwright WebKit в Linux не был бы
свидетельством физического iPad Safari. Palm rejection и реальный pinch остаются
обязательным gate для пилота, где это основной способ ввода.
