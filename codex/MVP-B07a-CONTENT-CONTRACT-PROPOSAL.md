# MVP-B07a-C1 — Content contract proposal

2026-10-10 · **PROPOSED / NOT ACCEPTED**. Documentation only.

Основание: OWNER AUTHORIZATION — MVP-B07a-C1; [B07a results, вариант A](MVP-B07a-RESULTS.md), [Content Spec v1](../docs/content/COURSE-CONTENT-SPEC.md), [архитектура содержания](../docs/architecture/CONTENT-ARCHITECTURE.md), [Codex content contract](CONTENT-CONTRACT.md). MUST ниже — предлагаемые нормы, действующие только после отдельного принятия владельцем. Действующая спецификация не изменена. [Матрица](MVP-B07a-CONTENT-CONTRACT-MATRIX.md) и [решение/diff](MVP-B07a-CONTENT-CONTRACT-OWNER-DECISION.md) составляют единый review package.

Actor → автор synthetic локального курса; use case → validation и раздельные immutable student/teacher projections; invariants → закрытые материалы не попадают в student output, разрешение ссылок fail closed, воспроизводимость, отсутствие записи в source; entities/VO → package revision, course/lesson/problem, hint, teacher material, asset, manifest, hashes; states → input → invalid либо compiled (не published). Data source → только явно переданный локальный root; compiler не выдаёт grants, не читает сеть/Git credentials/БД и не исполняет shell. Tests → будущие acceptance cases в матрице; сейчас document review, runtime NOT RUN.

## N1. Revision и совместимость

1. Предлагаемая редакция называется **Content Spec 1.1 / B07a-C1**. Базовые документы нового пакета MUST использовать `course/v1.1`, `lesson/v1.1`, `problem/v1.1`; новые sidecar schemas — `hints/v1`, `teacher/v1`; отдельный YAML inventory — `content-package/v1`. Это явная новая revision, а не расширение смысла `*/v1` задним числом.
2. Строгие правила N2–N9 MUST применяться только к пакету с `content-package.yaml` schema `content-package/v1` и базовыми schemas `*/v1.1`. Смешение v1/v1.1 MUST давать ERROR `E_SCHEMA_MIX`. Inventory MUST перечислять все входные документы и assets. Новые поля MUST NOT внедряться в v1.
3. Пакет без inventory с исходными `course/v1`, `lesson/v1`, `problem/v1` MUST проверяться legacy validator по прежней спецификации. Его valid/invalid статус MUST NOT меняться из-за требований нового профиля. В частности, неизвестное поле остаётся WARNING без семантики, `problem.id` course-wide SHOULD не становится legacy MUST. Sidecar schema без нового inventory — неподдерживаемое расширение, ERROR. Наличие ошибочного inventory MUST NOT вызывать fallback к legacy.
4. Legacy validation не является безопасной классификацией ресурсов. Запрос **компиляции** legacy пакета в новый двухартефактный контракт MUST завершаться `E_REVISION_REQUIRED`, без artifacts, при сохранении отдельного результата legacy validation. Даже корректный legacy пакет не конвертируется автоматически. Автор MAY вручную создать новую редакцию пакета с явной классификацией; старые source/snapshots/assignments MUST NOT изменяться. Это ограничение совместимости вынесено на решение D01.
5. Старые readers MUST отклонять неизвестные новые schemas; новый compiler MUST NOT подменять их `v1`. Поля старой базовой модели сохраняют значение: `source` задачи — библиография, `order` — порядок, ID — стабильный идентификатор, не путь. Правила Markdown/LaTeX/санитизации §§10–18, запрет HTML/includes/DSL, разделение контента и LMS state сохраняются, с явно перечисленными уточнениями ниже.

## N2. Пакет, поля, IDs и discovery

Вход — immutable снимок одного каталога курса и явные параметры compiler: `contractRevision`, `compilerRevision`, конечные положительные byte limits для document/image/static/package; необязательный `sourceRevision` как непрозрачная строка provenance вызывающей стороны. Root MUST быть явно передан, не выбирается из поля документа. Compiler MUST NOT читать parent directories, сеть, Git metadata или пользовательские attachments. `sourceRevision` не доказательство чистоты checkout и не authority.

`content-package.yaml` MUST быть единственным inventory, UTF-8 без BOM, YAML mapping с точными полями:

| Поле | Тип / требование |
|---|---|
| `schema` | REQUIRED, ровно `content-package/v1` |
| `courseId` | REQUIRED, ID существующего `course.md` |
| `documents` | REQUIRED, непустой список уникальных package-relative `.md` paths, включая `course.md` |
| `assets` | REQUIRED, список записей N5; пустой `[]` разрешён |

Документы MUST иметь UTF-8 YAML Front Matter `---`, затем Markdown body. YAML MUST быть single-document, без duplicate keys (на любой глубине), aliases/anchors, merge keys, custom tags и нестроковых keys. Null вместо поля, неверный тип, float/string вместо integer и неизвестный enum MUST давать ERROR. Строки MUST быть Unicode NFC; Unicode keys не заменяют точные ASCII имена полей. Все новые схемы MUST отклонять неизвестные поля (`E_FIELD_UNKNOWN`), включая попытки `audience`, `solution`, `rubric`, `releaseAfter`, `ownerId` в базовых документах; serializer MUST использовать whitelist, не passthrough YAML.

| Schema | REQUIRED front matter | OPTIONAL front matter | Body |
|---|---|---|---|
| `course/v1.1` | `schema,id,title` | `description` | student Markdown |
| `lesson/v1.1` | `schema,id,title,order` | нет | student Markdown |
| `problem/v1.1` | `schema,id,order` | `title,source` | student Markdown, без скрытого teacher content |
| `hints/v1` | см. N3 | `title` | одна подсказка |
| `teacher/v1` | см. N4 | `title` | один teacher material |

`id`, `courseId`, `targetId`, `ownerId` MUST соответствовать `^[a-z][a-z0-9-]*$`. ID уникальны по namespace внутри пакета: lessons, problems, hints, teacher materials, assets — каждый отдельно; course один. Problem IDs в revision 1.1 MUST быть уникальны во всём курсе. Совпадение `lesson.id` и `problem.id` допустимо: тип обязателен в ссылке. Глобальную уникальность course ID MUST обеспечивать будущая система публикации; offline validator гарантирует только один course и согласованность `courseId`.

`order` везде MUST быть integer 1..2147483647, без gaps requirement: уроки уникальны внутри курса; задачи внутри урока; hints по target problem; teacher material — по паре `(targetKind,targetId)` независимо от kind. `title`, если задан, MUST быть непустой строкой; обязательный body sidecar MUST содержать хотя бы один непробельный символ. `description` и `source` — строки. Общий document byte limit ограничивает все поля и body.

Структура base content MUST быть `course.md`, `lessons/<directory>/lesson.md`, `lessons/<directory>/problems/<file>.md`; каждый lesson directory MUST иметь один lesson.md, каждый problem принадлежит ровно этому lesson. Каталог `lessons/` обязателен; пустые lessons/problems допустимы. Sidecars MUST быть в `materials/hints/*.md` или `materials/teacher/*.md` без дополнительных уровней. Schema определяет тип и видимость; каталог/суффикс не заменяет schema. Несоответствие schema допустимому месту — ERROR, не переинтерпретация.

Inventory MUST быть полным: любой обычный файл внутри root, кроме самого inventory, MUST быть перечислен ровно один раз как document или asset. Необъявленные файлы, дубли paths, document в asset list, inventory в списках и непризнанные типы файлов MUST давать ERROR. Root — подготовленный content package, не весь checkout; `.git`, `.obsidian`, build outputs внутри него не игнорируются молча. Каталоги без файлов не влияют на hash, кроме обязательной проверки `lessons/`.

## N3. Hints и политика публикации

Один `hints/v1` документ MUST представлять ровно одну подсказку. Нормативная структура: REQUIRED `schema: hints/v1`, `id`, `targetKind: problem`, `targetId: <problem-id>`, `order: <positive-int>`, `publication: with-problem|withheld`; OPTIONAL `title`. Body — Markdown одной подсказки. Не допускаются массив hints внутри body/front matter, implicit target по соседству или filename и targets course/lesson. Target MUST существовать в том же пакете ровно один раз.

`publication` MUST быть указан явно, default отсутствует:

- `with-problem`: включить подсказку в student projection этой версии, упорядочить по `order`; будущая выдача возможна лишь вместе с разрешённым ученику target problem по точной опубликованной версии. Значение не выдаёт доступ к задаче или курсу само по себе.
- `withheld`: включить только в teacher artifact; student MUST NOT получать body, ID, title, order, metadata, count, ссылку либо asset исключительно этой подсказки. Наличие пропуска в `order` не должно выводиться как placeholder.

Это compile-time политика версии. Нет таймеров, числа попыток, персонального раскрытия, кнопки получения server-secret hint, условий и state machine. Изменение policy требует новой immutable content version; оно не меняет старые assignments. UI MAY сворачивать уже переданный `with-problem` текст, но это не security boundary. Отложенное раскрытие требует отдельного решения.

## N4. Три структуры teacher materials и версия

Общий envelope `teacher/v1`: REQUIRED `schema,id,targetKind,targetId,kind,order`; OPTIONAL `title`; никакого `visibility` override. Каждый файл содержит ровно один из вариантов:

| Структура / `kind` | `targetKind` | Body / ограничения |
|---|---|---|
| Методические рекомендации / `notes` | `course`, `lesson`, `problem` | Markdown рекомендаций преподавателю |
| Эталонное решение / `solution` | только `problem` | Markdown полного решения, не student condition |
| Критерии проверки / `rubric` | только `problem` | Markdown критериев; не вычисляемая scoring schema |

Это discriminated union с тремя отдельными документами, не три закрытых поля в problem. Несколько `notes` на target допустимы; не более одного `solution` и одного `rubric` на problem в одной версии. Все три вида необязательны: отсутствие не генерирует фиктивный ответ/рубрику и не мешает компиляции. Автоматическая оценка, weights, points, executable formulas и feedback учащемуся вне этого контракта.

Каждый material MUST ссылаться только на entity из той же версии source package. Compiled identity — `(packageHash, targetKind, targetId, materialId)`. Teacher artifact MUST содержать `packageHash` и соответствующий `studentProjectionHash`; resolver MUST NOT подставлять материалы из latest version по одному problem ID. Будущий publisher MUST сохранять связь immutable ContentVersionId с этой парой; это требование к интеграции, не её реализация/авторизация.

Teacher materials всегда teacher-only. Teacher artifact содержит student projection и отдельные `hints` (включая withheld) и `materials`. Student artifact MUST NOT иметь ключи/пустые массивы/placeholder `teacherMaterials`, `solutions`, `rubrics`, ссылки на teacher artifact, полный package hash, source revision, исходные paths, source maps или diagnostics. Граница действует на вложенные значения и metadata, а не только Markdown body.

## N5. Assets: ownership, классификация, reuse

Каждая запись `assets` inventory MUST иметь ровно REQUIRED поля:

```yaml
id: divisor-table
path: assets/student/files/table.txt
ownerKind: course
ownerId: sample-course
visibility: student
mediaType: text/plain
```

`ownerKind` MUST быть `course|lesson|problem`; `ownerId` MUST разрешаться в данном пакете. Это **content ownership** по точной версии, не OrganizationId, ActorId, роль или runtime grant. Ancestors problem→lesson→course выводятся из N2. Owner MUST существовать даже у неиспользуемого asset.

Разрешены только package-relative пути `assets/student/images/<path>`, `assets/student/files/<path>`, `assets/teacher/images/<path>`, `assets/teacher/files/<path>`, а также те же `assets/...` внутри `lessons/<directory>/`. `<path>` — один или несколько безопасных сегментов. Root location MUST соответствовать `visibility: student|teacher`, оба обязательны; `public` не используется, поскольку student asset не публичен в интернете. Lesson-local asset MUST принадлежать этому lesson либо одному из его problems; course-owned asset MUST лежать в root assets. Root asset MAY иметь lesson/problem owner, но доступность от этого не расширяется.

Контентный scope reuse: course-owned asset доступен для ссылок из курса и всех его descendants; lesson-owned — из этого lesson и его problems; problem-owned — только из этого problem. Target sidecar определяет его место в дереве. Ссылка из sibling lesson/problem или ancestor на asset более узкого scope — ERROR `E_ASSET_SCOPE`. Для reuse между уроками автор MUST явно выбрать course owner; между задачами одного урока — lesson либо course. Один asset имеет один ID, один path, одного owner, одну visibility; любое число ссылок в scope. Manifest MUST иметь одну запись на asset, не копию на каждую ссылку.

Видимость документов: base content и `with-problem` hints — student; teacher materials и `withheld` hints — teacher. Teacher документы MAY ссылаться на student и teacher assets в scope. Student документы MUST ссылаться только на student assets. Видимость MUST NOT наследоваться от автора ссылки, расширяться из-за Markdown или объединяться правилом «наиболее публичный». Asset не становится teacher-only из-за teacher ref и не становится student из-за student ref. Наследуется только content scope owner→descendants; классификация из явного manifest и совпадающего path обязательна, implicit/default visibility отсутствует. Конфликт любого объявления — ERROR, не автоматическое понижение/повышение.

Разрешённый минимальный набор media types: `.png`→`image/png`, `.jpg|.jpeg`→`image/jpeg`, `.webp`→`image/webp`, `.pdf`→`application/pdf`, `.txt`→`text/plain`. Images MUST находиться в images, PDF/TXT в files. Расширение, declared MIME и проверенная структура bytes MUST совпадать; TXT MUST быть UTF-8. SVG в этой revision **не поддержан** (`E_ASSET_TYPE`): действующий §13 разрешает его только при безопасной обработке, которая здесь не изобретается. Другие форматы требуют отдельного contract revision/решения. Ни PDF, ни binary metadata не сканируются на педагогическую секретность автоматически; автор отвечает за явную student классификацию всех bytes (включая EXIF/встроенный текст). Compiler обязан исключить teacher-classified bytes, но не способен определить скрытый ответ в разрешённом PNG.

Неиспользуемый объявленный asset MUST валидироваться и участвовать в packageHash; WARNING `W_ASSET_UNUSED`. Он MUST NOT копироваться ни в один delivery manifest. Asset manifest строится только по прямым валидным ссылкам документа соответствующей projection. Assets не имеют собственных зависимостей: внешние/локальные ссылки внутри PDF/TXT не импортируются рекурсивно, embeds/scripts/active actions PDF MUST быть отклонены. Content renderer MUST NOT исполнять файлы; конкретная безопасная доставка относится к будущему F1/B08.

## N6. Ссылки и безопасное разрешение

1. Поддерживаются inline/reference-style Markdown links/images и autolinks безопасного Markdown v1. Весь Markdown MUST парситься, не сканироваться regex. Неиспользуемые reference definitions тоже MUST валидироваться, но не создают output dependency. Code spans/blocks — текст, не ссылки. Raw HTML и includes запрещены.
2. Локальные links/images MUST быть обычными relative paths от каталога документа к **объявленному asset**. Asset ID задаётся inventory; новый `asset://` DSL не вводится. Разрешены `./` и `../` при условии N6.3. Пути к Markdown, inventory, директориям, source files, `lms://`, `attachment://`, абсолютные POSIX/Windows/UNC paths, fragment-only links и локальные query/fragment MUST давать ERROR в revision 1.1. Внутренний lesson routing отложен; ограничение относительно legacy §20 явно утверждается в D06.
3. Inventory path MUST быть нормализованным относительным POSIX path, без empty/`.`/`..` сегментов, backslash, drive/colon, control/NUL, `%`, `?`, `#`. Сегменты MUST соответствовать `[A-Za-z0-9][A-Za-z0-9._-]*`; сравнение case-sensitive, case-fold collisions внутри пакета MUST давать ERROR для переносимости. В Markdown relative path те же сегменты, плюс `.`/`..`; percent encoding/backslash/control запрещены (без повторного decode). Нормализация слева направо MUST отклонять попытку выйти выше root даже если последующий сегмент возвращает внутрь. После нормализации путь MUST точно совпасть с одной записью assets; отсутствие записи не даёт публичность.
4. Missing file/target, duplicate ID/path, dangling link, mismatched scope/visibility/type MUST давать ERROR. Не применять «первое совпадение», поиск по filename, case-insensitive fallback или silent omission. Student→teacher reference всегда ERROR, не удаление ссылки с продолжением.
5. Root MUST быть canonical directory; любой symlink в root или ниже (включая внутренний и dangling), junction/reparse equivalent, device/FIFO/socket MUST давать ERROR до чтения целевого содержимого. Hard-linked regular files с link count >1 MUST отклоняться. Проверка lexical path не заменяет безопасного открытия: compiler MUST получать согласованный snapshot и проверять identity/containment открытого файла, не следовать подменённым links между check/read. Если доказать это невозможно или input изменился при чтении — ERROR `E_INPUT_CHANGED`, никаких artifacts. Выход за root MUST NOT приводить даже к пробному чтению target bytes. Более строгий полный запрет symlinks новой revision вынесен в D06.
6. Внешние обычные ссылки MUST использовать `https://` с host и без userinfo; network-relative `//host`, `http`, `javascript`, `data`, `file`, `ftp` и прочие schemes — ERROR. External images запрещены. HTTPS URL на отдельной строке остаётся video candidate по §16; unsupported provider — WARNING и обычная ссылка. Compiler MUST NOT обращаться к URL, проверять remote existence или извлекать remote metadata. HTTPS не считается local asset и не включается в manifest. Закрытое содержимое/ссылки на него MUST NOT авторски помещаться в student body; compiler не может доказать публичность произвольного HTTPS назначения без доступа к нему.
7. После разрешения output Markdown MUST заменять local destination на `./assets/<asset-id>.<extension>`; тип, label, alt и title сохраняются. Оригинальные directory/filename MUST NOT попадать в delivery projections. Все output Markdown destinations MUST разрешаться относительно корня соответствующего artifact bundle, включая `student` внутри teacher JSON; исходное расположение документа не участвует. Mapping target→asset ID MUST формироваться из parsed AST, не string replacement. Images MUST указывать на image type, обычный link MAY указывать на любой разрешённый type.

## N7. Compiler result и manifests

Это контракт будущего локального компонента, не API/DB schema. Сначала валидация **всего** пакета, включая withheld/teacher/unused; затем обе projections как единый результат. ERROR в закрытом документе MUST блокировать весь build; частичный student artifact запрещён. Validation не публикует контент, не создаёт assignment и не меняет source.

Результат MUST иметь `status: valid|invalid|failed`, `contractRevision`, `compilerRevision`, упорядоченный `diagnostics`, при success — `studentArtifact`, `teacherArtifact`, отдельный закрытый `buildMetadata`. Invalid/failed MUST NOT иметь artifacts/asset bytes/частично пригодный manifest. `failed` означает internal/I/O failure, не invalid учебного формата. Warning разрешает build. Legacy validation возвращается отдельно от compile capability по N1.

Diagnostics MUST содержать `{severity: ERROR|WARNING, code, file, line, column, message, related: [...]}`; `file` — package-relative, line/column — 1-based Unicode code point coordinates исходного UTF-8 текста; CRLF считается одним переводом строки. Для отсутствующего файла location — ссылающийся inventory/path token; missing course/inventory — ожидаемый filename:1:1; runtime/global failure — `content-package.yaml:1:1`. Для ошибки значения location MUST указывать начало scalar; для отсутствующего поля — начало mapping; для ошибки ссылки — начало destination, для duplicate key — начало второго key. Для duplicate primary — второе объявление в path/line order, related — первое. Сообщения MUST быть стабильными шаблонами без absolute paths, stack traces и произвольных secret snippets. Диагностика, включая teacher filenames и targets, предназначена только локальному автору/закрытому build review, MUST NOT встраиваться в student artifact или отдаваемый ученику preview. Порядок: file ordinal UTF-8, line, column, severity (ERROR перед WARNING), code, message. Identity ошибок не зависит от локали машины.

Student JSON MUST иметь ровно `{artifactSchema: "student-content/v1", contractRevision: "1.1", studentProjectionHash, course, assets}`. `course` — `{id,title,description?,bodyMarkdown,lessons}`; lesson — `{id,title,order,bodyMarkdown,problems}`; problem — `{id,title?,source?,order,bodyMarkdown,hints}`; hint — `{id,title?,order,bodyMarkdown}`. Hints только with-problem. Optional absent fields MUST быть опущены, не null. Lessons/problems/hints — arrays по order. `assets` — student manifest ниже. Никакие дополнительные входные поля не сериализуются.

Teacher JSON MUST иметь ровно `{artifactSchema: "teacher-content/v1", contractRevision: "1.1", packageHash, studentProjectionHash, student, hints, materials, assets}`. `student` — точный student JSON; `hints` — все `{id,title?,targetKind,targetId,order,publication,bodyMarkdown}`; `materials` — `{id,title?,targetKind,targetId,kind,order,bodyMarkdown}`. Teacher arrays сортируются по targetKind (course, lesson, problem), targetId ordinal, order. `assets` — объединение assets, реально нужных student и всем teacher/hint documents, с одним entry на ID. Teacher artifact целиком закрыт, даже если содержит публично допустимые части.

Каждая manifest entry MUST содержать ровно `{id, ownerKind, ownerId, visibility, mediaType, sizeBytes, sha256, artifactPath}`. `artifactPath` — `assets/<id>.<extension>`, extension lowercase, `.jpeg` сохраняется как `.jpeg`; bytes — неизменённые валидированные source bytes. Student manifest содержит только student assets, реально использованные student projection. Teacher-only ID/path/hash/MIME/size/owner/count и teacher-only usage student asset MUST NOT просачиваться в student manifest. Нет backlinks, `usedBy`, total package count, source path или global dedup metadata. Shared student asset MAY физически копироваться в оба закрытых bundles; storage dedup не даёт разрешение.

Артефакт — JSON и ровно файлы его manifest; никаких source YAML, diagnostics или общего assets archive в student bundle. Наличие student label не разрешает public storage URL. Будущая выдача MUST повторно применять принятые server-side authorization/processing/content-version rules, включая parent/context для assets. Compiler manifest не доверяет client role и не заменяет S2/S3/PRS1/F1. B07b/B08/B09 остаются отдельными этапами.

`buildMetadata` MUST иметь `{contractRevision,compilerRevision,packageHash,studentProjectionHash,teacherArtifactHash,limits,sourceRevision?}` и MAY быть сохранён только в закрытом build контуре. `limits` — фактически применённые byte limits. Source maps допускаются только отдельным закрытым диагностическим результатом, не новым полем artifacts. `SourceCommit` будущей публикации MUST NOT браться из student input как authority; `PublishedAt/ContentVersionId` назначаются publisher, не compiler.

## N8. Hashes, limits и детерминизм

`sha256` — SHA-256 в 64 lowercase hex; hash не является секретом или ACL. Для исключения неоднозначности все структурированные hash inputs и output JSON MUST использовать RFC 8785 JSON Canonicalization Scheme (JCS), UTF-8 без BOM/завершающего LF. Integer fields ограничены безопасным JSON range; входные order — N2. Body text MUST нормализовать CRLF/CR в LF и сохранять остальные code points, whitespace и terminal newline после front matter; link rewriting MUST быть детерминированным при фиксированной compilerRevision. Любое изменение Markdown serializer требует новой compilerRevision. Arrays имеют заданный здесь порядок; manifest — ordinal asset ID; source entries — ordinal package-relative path. Времена, random IDs, absolute root, mtime, directory enumeration, OS, locale и Git HEAD MUST NOT влиять на artifacts.

- `packageHash` = SHA256(JCS(`{contractRevision:"1.1", files:[{path,sizeBytes,sha256},...]}`)), где files содержит inventory и **все** declared documents/assets, хеши их исходных bytes до нормализации. Порядок files — ordinal UTF-8 path. Таким образом teacher edits, unused assets, source YAML whitespace и переименование файла меняют packageHash. SourceRevision, directory metadata и caller limits сюда не входят.
- `studentProjectionHash` = SHA256(JCS(student JSON **без** ключа `studentProjectionHash`)). Содержимое assets представлено entry hashes; teacher/build metadata в input MUST отсутствовать. Teacher-only edit не меняет student JSON/hash при неизменной valid student projection. Student ID/title/body/asset bytes или допустимая student dependency меняют projection hash. Ошибка teacher части вместо этого блокирует весь результат.
- `teacherArtifactHash` = SHA256(JCS(полного teacher JSON)). Он хранится только buildMetadata, поэтому нет самоссылки. Teacher JSON включает packageHash и тем самым фиксирует точную source version.

Для одинаковых bytes входа, contract/compiler revision и параметров MUST получаться побайтово одинаковые JSON, copied assets и упорядоченные diagnostics. BuildMetadata тоже одинаковы, если одинаков optional sourceRevision. Разные sourceRevision меняют только buildMetadata. Не обещается одинаковый вывод разных compiler revisions; они MUST проходить одну acceptance matrix. PackageHash плюс compilerRevision фиксируют происхождение; student hash — identity projection, а не всей версии курса и не замена ContentVersionId.

Размеры MUST измеряться по исходным bytes, включая front matter. Для новой revision `MB = 1,000,000 bytes`; рекомендуемые defaults §28: document 1,000,000; image 10,000,000; static 50,000,000. Inventory MUST укладываться в document limit. Package total — сумма bytes inventory, всех документов и всех assets (каждый path один раз, unused тоже), а не только delivery. Package limit REQUIRED от caller, default production quota здесь не назначается. `size == limit` допускается, `size > limit` ERROR. Все limits MUST быть положительными целыми <= 9007199254740991; invalid/missing configuration — failed без чтения пакета. Compiler MUST проверять bounds при чтении, не после неограниченного накопления; exact operational time/memory budgets остаются задачей реализации, не обещанием hard timeout.

## N9. Полный положительный пример (synthetic)

Ниже все файлы пакета; это текстовая fixture спецификации, **не результат работающего compiler**. Каждый code block задаёт UTF-8 файл с завершающим LF. Directory `lessons/one/problems/` создаётся структурой. Compile parameters: revision `1.1`, compilerRevision `fixture-design-1`, limits document=1000000/image=10000000/static=50000000/package=100000000; sourceRevision=`synthetic-example-1`. Все asset bytes заданы текстом, отсутствующих binary fixtures нет.

`content-package.yaml`:

```yaml
schema: content-package/v1
courseId: counting
documents:
  - course.md
  - lessons/one/lesson.md
  - lessons/one/problems/pairs.md
  - materials/hints/pairs-first.md
  - materials/hints/pairs-second.md
  - materials/teacher/course-notes.md
  - materials/teacher/lesson-notes.md
  - materials/teacher/pairs-solution.md
  - materials/teacher/pairs-rubric.md
assets:
  - id: shared-table
    path: assets/student/files/table.txt
    ownerKind: course
    ownerId: counting
    visibility: student
    mediaType: text/plain
  - id: solution-notes
    path: assets/teacher/files/solution.txt
    ownerKind: problem
    ownerId: pairs
    visibility: teacher
    mediaType: text/plain
```

`course.md`:

```markdown
---
schema: course/v1.1
id: counting
title: Счёт парами
---

Курс о парах. [Таблица](./assets/student/files/table.txt).
```

`lessons/one/lesson.md`:

```markdown
---
schema: lesson/v1.1
id: first-lesson
title: Пары делителей
order: 1
---

Сравните пары. [Таблица](../../assets/student/files/table.txt).
```

`lessons/one/problems/pairs.md`:

```markdown
---
schema: problem/v1.1
id: pairs
title: Делители шести
order: 1
source: Synthetic fixture
---

Перечислите положительные делители числа $6$.
```

`materials/hints/pairs-first.md`:

```markdown
---
schema: hints/v1
id: pair-products
targetKind: problem
targetId: pairs
order: 1
publication: with-problem
title: Произведения
---

Ищите пары чисел с произведением $6$.
```

`materials/hints/pairs-second.md`:

```markdown
---
schema: hints/v1
id: private-pair-check
targetKind: problem
targetId: pairs
order: 2
publication: withheld
---

Проверьте список в [закрытой заметке](../../assets/teacher/files/solution.txt).
```

`materials/teacher/course-notes.md`:

```markdown
---
schema: teacher/v1
id: course-guidance
targetKind: course
targetId: counting
kind: notes
order: 1
---

Предложите ученику сначала привести свой пример.
```

`materials/teacher/lesson-notes.md`:

```markdown
---
schema: teacher/v1
id: lesson-guidance
targetKind: lesson
targetId: first-lesson
kind: notes
order: 1
---

Используйте [общую таблицу](../../assets/student/files/table.txt).
```

`materials/teacher/pairs-solution.md`:

```markdown
---
schema: teacher/v1
id: pairs-reference
targetKind: problem
targetId: pairs
kind: solution
order: 1
---

Делители: $1,2,3,6$. [Заметка](../../assets/teacher/files/solution.txt).
```

`materials/teacher/pairs-rubric.md`:

```markdown
---
schema: teacher/v1
id: pairs-criteria
targetKind: problem
targetId: pairs
kind: rubric
order: 2
---

Список полный; лишних чисел нет; ученик объяснил делимость.
```

`assets/student/files/table.txt`:

```text
n | pairs
```

`assets/teacher/files/solution.txt`:

```text
6 = 1*6 = 2*3
```

Ожидаемый результат: valid без diagnostics; 1 course, 1 lesson, 1 problem. Student содержит только hint `pair-products`; manifest содержит одну запись `shared-table`, bytes=10, output path=`assets/shared-table.txt`, даже при двух ссылках. Teacher содержит оба hints, четыре materials и два assets; `solution-notes` имеет 14 bytes и `assets/solution-notes.txt`. Точные hashes вычисляются будущим compiler по N8, здесь значения не выдумываются. Teacher lesson notes используют тот же shared-table без раскрытия этого usage ученику. Student ссылки курса/урока переписываются в `./assets/shared-table.txt`. Никакого поля или bytes `private-pair-check`, `pairs-reference`, `pairs-criteria`, `solution-notes` в student bundle нет.

## Review evidence и границы

Сверка требований — [матрица](MVP-B07a-CONTENT-CONTRACT-MATRIX.md); принятие — только [owner decision](MVP-B07a-CONTENT-CONTRACT-OWNER-DECISION.md). Эта версия не создаёт runtime classes, JSON Schema, migrations, API, frontend, compiler либо publication storage.

Guardrails §7: covered — actor/use case/invariants/states, validation/limits/ошибки заданы как proposal; S2/S3/PRS1 и закрытые gates сохранены; новых schema/API/security **реализаций** нет, предложенная schema revision требует решения. N/A — runtime org/course/group checks, subjects, pagination/counts, attachments/history writes, transaction/lock/CAS/retry/unknown outcome: runtime access paths и writes не добавлены. Blocked / NOT PROVEN — enforcement student/teacher, безопасное открытие файлов и deterministic compiler до реализации и тестов. Unit/integration/negative-access/E2E — NOT RUN (documentation only). Использованы только synthetic тексты, секретов/реальных ПД нет. Отсутствующие skills model-domain/implement-use-case/implement-feature не применялись; реализация ими не заявлена.

**B07a-C1 READY FOR OWNER REVIEW · CONTENT EXTENSION NOT YET ACCEPTED · B07a IMPLEMENTATION PAUSED · B07b/B08 NOT AUTHORIZED · PRS1 EXACT OFF · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**
