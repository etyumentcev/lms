# MVP-B07a-C1 — Compatibility and acceptance matrix

2026-10-10 · **PROPOSED / NOT ACCEPTED**. Основание — [полная спецификация N1–N9](MVP-B07a-CONTENT-CONTRACT-PROPOSAL.md) и [owner decision](MVP-B07a-CONTENT-CONTRACT-OWNER-DECISION.md).

Все cases ниже — ожидаемые результаты будущих тестов, **NOT RUN**; обозначение VALID не является сообщением о прошедшем runtime тесте. Базовая fixture P — все файлы N9, каждый с завершающим LF. Каждый negative case меняет только указанные данные P; новые файлы добавляются в inventory, если case не проверяет discovery. Любой ERROR означает отсутствие **обоих** artifacts и любых delivery bytes; диагностика только закрытому автору. Codes ниже являются частью предлагаемого диагностического vocabulary; дополнительные implementation codes требуют документирования без ослабления этих cases.

## Совместимость

| Вход / запрос | Ожидание | Основание |
|---|---|---|
| Корректный legacy v1 без inventory; validate | Прежний VALID, без новых обязательных полей | N1, baseline §§22/38 |
| `examples/course`; validate legacy | Сохранить исходную проверку; упоминание отсутствующего PDF в code span не dependency | N1/N6; файл examples не менять |
| Legacy v1 с неизвестным `authorComment`; validate | WARNING, не новая семантика, поле не становится teacher material | Старый §38 |
| Legacy v1 со `solution` или `audience` | Не интерпретировать как секретный материал/ACL; прежняя диагностика unknown field | Нельзя наделять v1 новой семантикой |
| Корректный legacy с assets/внутренними symlinks/локальной Markdown link | Только прежние правила validator, не новые запреты revision 1.1 | Ограничения нового профиля не backport |
| Любой legacy; compile в новый contract | Отдельно legacy validation; `E_REVISION_REQUIRED`, artifacts отсутствуют | Явно неполная compile compatibility; D01 |
| Старый parser видит `course/v1.1` | Unsupported schema ERROR, не downgrade | §22 |
| Новый inventory и один `problem/v1` | `E_SCHEMA_MIX` | N1 |
| Sidecar hints/v1 без inventory в legacy | Unsupported extension ERROR `E_SCHEMA_UNSUPPORTED` | N1 |
| Невалидный inventory рядом с v1 | ERROR; не fallback в legacy | N1 |
| Historical published snapshot / assignment | Без переписывания/миграции/rebind; новая версия отдельно | N1/N4; проверка будущего publisher отдельно |

## Структура, hints и teacher materials

| ID | Изменение/контроль | Ожидаемый результат |
|---|---|---|
| C01 | P без изменений | VALID, N9 projections, 1 student asset / 2 teacher assets |
| C02 | Удалить все hints/materials и private asset из файлов/inventory | VALID; пустые hints/materials там, где их schema разрешает; student не имеет teacher fields |
| C03 | У hint удалить `publication` | `E_FIELD_REQUIRED`, никакого default release |
| C04 | `publication: after-attempt` или поле `releaseAfter: 1` | `E_FIELD_VALUE` / `E_FIELD_UNKNOWN`, никакого reveal engine |
| C05 | Hint targetKind=lesson / неизвестный targetId | `E_TARGET_KIND` / `E_TARGET_MISSING` |
| C06 | Две подсказки одному problem с order=1 | `E_ORDER_DUPLICATE`; порядок не угадывать по filename |
| C07 | Hint order=0, -1, 1.5, "1", null, >2147483647 | `E_FIELD_TYPE` либо `E_FIELD_VALUE`, согласно нарушению |
| C08 | Поменять order hints на 10 и 30 | VALID; gaps допустимы, no placeholder |
| C09 | Withheld→with-problem с private asset link как в P | `E_ASSET_VISIBILITY`, не автоматическая публикация asset |
| C10 | Withheld→with-problem после удаления private link | VALID; новый student hash и новая версия, старые immutable данные без изменения |
| C11 | Teacher solution targetKind=lesson | `E_TARGET_KIND` |
| C12 | Второй solution или rubric тому же problem с новым ID/order | `E_MATERIAL_DUPLICATE` |
| C13 | Две notes одному target с разными ID/order | VALID |
| C14 | Solution/rubric с одним order на target | `E_ORDER_DUPLICATE`, kind не создаёт отдельную ordering scope |
| C15 | Teacher material target другого пакета / неизвестного ID | `E_TARGET_MISSING`; нет latest/global lookup |
| C16 | Дубликат lesson/problem/hint/material/asset ID своего namespace | `E_ID_DUPLICATE`, related location первого |
| C17 | ID одинаков у lesson и problem, typed targets согласованы | VALID; namespaces разные |
| C18 | Дубликат problem ID в разных lessons | `E_ID_DUPLICATE` в revision 1.1, не ужесточение legacy SHOULD |
| C19 | Невалидный ID `Pair_1`, title null, неизвестный schema | `E_ID_FORMAT`, `E_FIELD_TYPE`, `E_SCHEMA_UNSUPPORTED` |
| C20 | Duplicate YAML key `visibility` / merge / alias / custom tag / несколько YAML docs | `E_YAML`, никакого last-key-wins |
| C21 | `solution`, `rubric`, `audience`, неизвестное `authorComment` в problem/v1.1 | `E_FIELD_UNKNOWN`; без passthrough в JSON |
| C22 | Нет course.md/lessons/lesson.md, problem вне problems, sidecar вне materials | `E_PACKAGE_STRUCTURE` |
| C23 | Unlisted файл, второй path, документ также asset, inventory self-reference | `E_INVENTORY` |
| C24 | Изменить schema teacher файла на problem/v1.1 в materials/teacher | `E_PACKAGE_STRUCTURE`; путь не исправляет schema |

## Assets, ссылки и отрицательные зависимости

| ID | Изменение/контроль | Ожидаемый результат |
|---|---|---|
| A01 | Несколько student/teacher refs на shared-table в scope | VALID; один entry на projection; teacher usage не в student metadata |
| A02 | Student→assets/teacher/files/solution.txt | `E_ASSET_VISIBILITY` |
| A03 | visibility=student при path assets/teacher/... | `E_ASSET_VISIBILITY`; не выбирать менее строгое |
| A04 | Удалить asset declaration при наличии файла/link | `E_INVENTORY` / `E_ASSET_UNDECLARED`; Markdown не делает файл public |
| A05 | Declared asset отсутствует физически | `E_FILE_MISSING`, location inventory path; без молчаливого пропуска |
| A06 | Link не существует / dangling target | `E_ASSET_UNDECLARED` или `E_FILE_MISSING`, если path объявлен |
| A07 | Два ID на один path / два paths на один ID | `E_ASSET_DUPLICATE` / `E_ID_DUPLICATE` |
| A08 | Asset owner missing / wrong kind / sibling reference | `E_TARGET_MISSING` / `E_FIELD_VALUE` / `E_ASSET_SCOPE` |
| A09 | Course body→problem-owned asset даже student | `E_ASSET_SCOPE` |
| A10 | Перевести shared-table owner на lesson, оставив course link | `E_ASSET_SCOPE`; явный course owner нужен для cross-lesson reuse |
| A11 | Lesson-local asset принадлежит другому lesson или course | `E_ASSET_SCOPE` |
| A12 | Неиспользуемый корректный declared asset | `W_ASSET_UNUSED`; нет delivery entry, есть packageHash contribution |
| A13 | Неиспользуемый asset malformed/missing/oversized | ERROR; не пропускать проверку закрытых/unused inputs |
| A14 | `../../../../etc/passwd`, absolute `/etc/passwd`, `C:\secret`, UNC | `E_PATH`; никакого чтения за root |
| A15 | `%2e%2e`, `%252e`, `%2f`, backslash, NUL, query/fragment в local path | `E_PATH`, нет double decoding |
| A16 | Корректный `../../assets/student/files/table.txt` из sidecar | VALID; internal `..` разрешён после containment |
| A17 | Path выходит выше root, затем возвращается внутрь | `E_PATH` даже при кажущемся безопасным конечном path |
| A18 | Symlink наружу, внутрь, dangling; symlink directory; root symlink | `E_FILE_LINK`; не читать target bytes |
| A19 | Hard link count>1 / FIFO / device / socket | `E_FILE_LINK` / `E_FILE_TYPE` |
| A20 | Case-fold collision `A.txt`/`a.txt` | `E_PATH_COLLISION` на любой OS |
| A21 | Подмена regular file на symlink между check/read; изменение bytes во время snapshot | `E_INPUT_CHANGED`; no artifacts/no external bytes |
| A22 | Ссылка на teacher Markdown, inventory или другой lesson.md | `E_LINK_TARGET`; no source delivery |
| A23 | Unknown/неиспользуемая reference definition указывает teacher asset из student | `E_ASSET_VISIBILITY`; used/unused не bypass |
| A24 | Literal path внутри code span/block | Не dependency; VALID при иных корректных файлах |
| A25 | `javascript:`, `data:`, `attachment:`, `lms:`, `http:`, `//host`, HTTPS userinfo | `E_URI`; none fetched |
| A26 | HTTPS external image / unsupported video HTTPS | `E_EXTERNAL_IMAGE` / `W_VIDEO_PROVIDER`; video обычная ссылка, без fetch |
| A27 | Raw HTML/iframe/event handler/include | `E_MARKDOWN`; body teacher тоже проверяется |
| A28 | `.svg`, `.exe`, MIME mismatch, PNG с invalid signature/structure | `E_ASSET_TYPE`; extension alone недостаточен |
| A29 | PDF с active action/embedded executable или malformed; TXT не UTF-8 | `E_ASSET_TYPE` |
| A30 | Inline/reference image→TXT | `E_LINK_TYPE` |

## Неразглашение, версия, детерминизм и лимиты

| ID | Проверка (будущий независимый assertion) | Ожидание |
|---|---|---|
| S01 | Добавить уникальные synthetic sentinels в teacher body/title/IDs/filename, withheld text и private asset bytes | Ни одного sentinel во всём student JSON, bundle names/bytes/manifest; проверять JSON tree и raw bytes, не только DTO |
| S02 | Искать private asset ID/hash/size/MIME/source path/owner, sourceRevision/packageHash, teacher count/backlinks | Отсутствуют в student projection; отдельно проверить отсутствие запрещённых keys, а не случайное совпадение короткой строки |
| S03 | Сравнить P с P′: только teacher text/title/order/private asset bytes изменены, обе версии valid | Побайтово одинаковые student JSON/assets; packageHash и teacherArtifactHash различаются |
| S04 | Только teacher reference на уже student-used shared-table добавлен/удалён | Student manifest/hash без изменений |
| S05 | Добавить student-classified asset, используемый только teacher | Только teacher manifest; student не знает его существования |
| S06 | Teacher-only ошибка, включая invalid unused asset | Invalid всего build; нет частичного student результата, diagnostics закрыты |
| S07 | Изменить sourceRevision при одинаковых files/parameters | Одинаковые artifacts/hash; различается только buildMetadata.sourceRevision |
| S08 | Изменить bytes student asset/body/hint | Меняется studentProjectionHash и packageHash; teacher hash также |
| S09 | Только YAML whitespace/source path изменены с корректировкой links/inventory, student projection сохранена | Package hash меняется; student hash не меняется, если projection bytes те же |
| S10 | Повторить build 2 раза, разные absolute roots, порядок enumeration, locale, mtime | JSON, copied assets, diagnostics побайтово одинаковы при одной compilerRevision |
| S11 | Разные private пакеты дают один studentProjectionHash | Разрешено; publisher связывает exact packageHash, не выбирает latest material |
| S12 | Попытка выдать teacher bundle/student manifest entry без server parent/auth context | Будущая доставка MUST deny по принятым правилам; integration NOT RUN / B07b/B08/F1 отдельно |
| S13 | Known student text содержит авторский секрет / секрет в EXIF student asset / закрытый HTTPS URL | Нельзя заявлять automatic semantic leak detection; author review required, teacher classification negatives отдельно |
| L01 | Document/image/static/package ровно limit и limit+1 bytes | Ровно проходит при валидном формате; +1 → `E_LIMIT`; inventory входит в document/total |
| L02 | Большой teacher/withheld/unused файл, маленький student bundle | `E_LIMIT`; input total учитывает все файлы |
| L03 | Нет package limit, нулевой/negative/noninteger limit | `failed`, `E_CONFIG`, до чтения source |
| L04 | Недоступный файл / internal exception | `failed`, `E_IO` / `E_INTERNAL`, никаких partial artifacts или stack/absolute paths в diagnostics |
| D01 | Duplicate key/ID в двух местах, CRLF и Unicode до ошибки | Точный file:line:column по N7, related location, stable code/template |
| D02 | Unsupported video + unused asset | WARNING не блокируют, порядок diagnostics стабилен |

Для A18/A21 будущий filesystem test MUST независимо доказать **отсутствие чтения** secret target (instrumented reader/доступы), не только отсутствие его строки в output. Для S01–S05 expected student shape/bytes MUST формироваться независимо от production projection code. Expected success в negative-access cases без соответствующего denial не PASS.

## Конкретные отрицательные примеры

Каждый пример — самостоятельная мутация P. Позиции считаются в указанном полном небольшом фрагменте либо от явно указанного места замены; будущий harness MUST сверять coordinates исходного fixture, не hard-code offsets другого файла.

**N-A: неизвестная политика**, файл `materials/hints/pairs-first.md`:

```markdown
---
schema: hints/v1
id: pair-products
targetKind: problem
targetId: pairs
order: 1
publication: after-attempt
---

Подсказка.
```

Ожидается `E_FIELD_VALUE` на строке 7, column 14 (начало scalar `after-attempt`); artifacts отсутствуют.

**N-B: утечка через ссылку**, заменить body задачи целиком на:

```markdown
[Ответ](../../../assets/teacher/files/solution.txt)
```

Путь существует и owner совпадает; ожидается `E_ASSET_VISIBILITY` на destination, а не успешное удаление ссылки. Manifest teacher не переносится в student.

**N-C: неоднозначный YAML**, заменить asset запись:

```yaml
id: solution-notes
path: assets/teacher/files/solution.txt
ownerKind: problem
ownerId: pairs
visibility: teacher
visibility: student
mediaType: text/plain
```

Ожидается `E_YAML` на втором key `visibility` (строка 6 этого блока), related на строку 5; последнее значение не побеждает.

**N-D: traversal**, заменить body course.md:

```markdown
[Секрет](../outside.txt)
```

Ожидается `E_PATH` на destination, даже если файл существует; target MUST NOT открываться. Отдельная мутация: declared student asset — symlink к outside.txt → `E_FILE_LINK` на inventory path.

**N-E: несовместимая смесь**, заменить только schema задачи на `problem/v1`. Ожидается `E_SCHEMA_MIX` на scalar schema, никакой автоматической миграции.

**N-F: неверное teacher назначение**, в pairs-solution.md заменить `targetKind: problem` на `targetKind: lesson` и targetId на first-lesson. Ожидается `E_TARGET_KIND`, хотя lesson существует: solution разрешён только problem.

## Выполненные проверки документации

В этой задаче проверены локальные ссылки трёх документов, whitespace, согласованность полной synthetic fixture (inventory, YAML, targets, paths, byte lengths) и неизменность baseline существующих файлов. Это облегчённая проверка документации, **не реализация Course Validator**, не runtime acceptance и не доказательство безопасности compiler. Точные команды/результаты приведены в owner-decision report.

Runtime unit/integration/negative-access/browser/E2E: **NOT RUN — documentation only**. Real org/role/revocation/processing/attachment delivery cases сейчас N/A к изменениям файлов; обязательны при появлении runtime delivery. Frontend cache и transport manifests пока не существуют, leak prevention **NOT PROVEN**.

**B07a-C1 READY FOR OWNER REVIEW · CONTENT EXTENSION NOT YET ACCEPTED · B07a IMPLEMENTATION PAUSED · B07b/B08 NOT AUTHORIZED · PRS1 EXACT OFF · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**
