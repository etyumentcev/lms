# MVP-B07a-C1 — Owner decision package / точный нормативный diff

2026-10-10 · **D01–D08 ACCEPTED · B07a IMPLEMENTATION AUTHORIZED**.

Owner decision recorded from the explicit implementation request in this task: D01–D08 and Content Spec 1.1 accepted together. The exact normative diff below has been applied after verifying both recorded SHA-256 fingerprints. B07b/B08 remain unauthorized; format acceptance is not implementation acceptance. The remainder below is preserved historical proposal/review evidence; its PENDING/NOT ACCEPTED statements describe the pre-decision state.

Подготовлены [предложение N1–N9](MVP-B07a-CONTENT-CONTRACT-PROPOSAL.md) и [acceptance matrix](MVP-B07a-CONTENT-CONTRACT-MATRIX.md). Основание — OWNER AUTHORIZATION — MVP-B07a-C1, documentation only. Вариант A из [B07a results](MVP-B07a-RESULTS.md) не считается принятым. Действующий [Content Spec](../docs/content/COURSE-CONTENT-SPEC.md) остаётся неизменным; никакая строка ниже не является принятой owner decision.

## Решения для окончательной приёмки

Все решения ниже **PENDING**; рекомендуемый целостный вариант — D01–D08 вместе. Частичное принятие не запускает compiler: несовместимые комбинации должны вернуться в редактуру proposal и matrix. Ни новых runtime grants, ни API authorizations этот пакет не запрашивает.

| ID | Точное предлагаемое решение | Почему нужно решение владельца |
|---|---|---|
| D01 | Принять Content Spec 1.1: новые base `*/v1.1`, sidecars `hints/v1`,`teacher/v1`, inventory `content-package/v1`; legacy сохраняет validation, новый compile требует ручного opt-in/revision | Вариант A предлагал сохранить три base schemas; строгие поля, IDs, assets и path rules несовместимы с молчаливым расширением v1. Нет automatic migration; legacy compile в новый contract сознательно не поддержан |
| D02 | Одна hint на файл, обязательные target problem/order/publication; `with-problem` либо `withheld`, отсутствие default; никакого staged engine | A называл hints student-visible, но не определял publication policy |
| D03 | Teacher discriminated union: notes→course/lesson/problem, solution/rubric→problem; один solution/rubric, несколько notes; order по target, exact packageHash version | Ранее комбинации, cardinality, ordering и version binding отсутствовали |
| D04 | Явный inventory assets с ID/path/ownerKind/ownerId/visibility/mediaType; student/teacher roots; scope только owner descendants; manifests только reachable | Это новые нормы ownership/reuse. Student root не public URL, directory и Markdown не grant |
| D05 | Strict unknown/duplicate/ambiguous YAML rejection только в новой revision; problem IDs course-wide MUST; полный inventory без silent ignored files | Legacy неизвестные fields SHOULD warning и problem uniqueness SHOULD остаются прежними |
| D06 | Новый профиль: запрет всех symlinks/hardlinks>1, строгие ASCII local paths, запрет local Markdown/fragment/query links, HTTPS-only; PNG/JPEG/WebP/PDF/TXT, SVG отложен | Ужесточение legacy §13/15/20/25 делается только явной revision. Авторский Git каталог требует отдельного подготовленного package root |
| D07 | Принять точные projection JSON/manifests, закрытые diagnostics/build metadata, JCS/SHA-256 правила N7–N8; student hash только projection, package hash только teacher/build | Hash и metadata способны раскрыть teacher changes; sourceRevision не authority. Student hash не ContentVersionId |
| D08 | MB=1,000,000 bytes для новой revision, limits включают inventory/unused/teacher, caller package limit обязателен; ERROR атомарно блокирует оба artifacts; принять matrix как future acceptance obligations | Старый §28 не определяет единицы/total boundaries; никаких production quotas и runtime PASS этим не создаётся |

Возможная формулировка решения владельца (шаблон, **не подписанное решение**): «Принимаю D01–D08 и нормативные N1–N9 предложения MVP-B07a-C1 в проверенной редакции, совместно с acceptance matrix. Разрешаю отдельное внесение точного diff ниже в Content Spec. Это принятие формата не является принятием реализации, публикации или разрешением B07b/B08/B09». Если нужны изменения, указать D-ID и заменяющий текст; состояние до окончательного принятия — NOT ACCEPTED. Прежние authorizations не отменяются, но в этой задаче B07a остаётся PAUSED до окончательной редакции.

## Сравнение с вариантом A (включая дополнительные решения)

| Вариант A B07a results | Предложение C1 | Сохранение / отклонение / статус |
|---|---|---|
| Отдельный нормативный extension к v1 | Version 1.1, явный opt-in и новые base schemas | Уточнение с несовместимостью, D01; A не принят |
| Сохранить три schemas и examples/course | Legacy schemas и файлы examples сохраняются; новый strict build использует v1.1 | Отличие D01; старые примеры не переписывать |
| `hints/v1` student-visible, positive order | Один hint, mandatory publication with-problem/withheld, unique order по problem | Расширение D02, закрытый hint отсутствует во всём student bundle |
| `teacher/v1`, id/targetKind/targetId/kind | Те же имена, отдельные документы union и exact version | Конкретизация D03 |
| Утвердить kind/target, ID scopes и порядок нескольких материалов | N2–N4: typed namespaces, course-unique problem IDs, unique order per target, cardinality | Принятия ещё нет, D03/D05 |
| Схема задаёт видимость, не filename | Schema+hint policy, teacher без override; layout лишь проверяет соответствие | Сохранено, N2–N4 |
| Неизвестные/неоднозначные targets ERROR | ERROR всего build, без first-match/latest | Сохранено, N2/N7 |
| Student links в teacher documents/assets ERROR | Любая local document link запрещена новым профилем; student→teacher asset ERROR | Более строгий D06, не скрытая смена §20 |
| Раздельные public/teacher roots | student/teacher roots плюс обязательный manifest и content owner scope | Термин public заменён, не world-readable, D04 |
| Проверять все ссылки, не копировать весь root | Проверять все inputs; copy только projection dependencies; unused warning | Сохранено/уточнено, D04/D08 |
| Teacher manifest/metadata не включать в student serialization | Точный whitelist JSON, no filenames/counts/source maps/diagnostics/full hash | Сохранено/уточнено D07 |
| Student source hash только student projection; full package hash teacher/build | Различимые `studentProjectionHash`, `packageHash`, `teacherArtifactHash` и noninterference tests | Сохранено/уточнено, D07 |
| Лимиты configurable; MB и boundaries решить в extension | N8: decimal bytes, exact ≤, inventory+весь source, package limit caller | Конкретизация D08; не production quota |
| После A сначала precise contract, потом полный slice | Сначала owner acceptance этого пакета; implementation здесь не выполняется | Граница сохранена |

## Точный нормативный diff — НЕ ПРИМЕНЯТЬ до принятия

Diff адресован только `docs/content/COURSE-CONTENT-SPEC.md`. Исходная точка — файл на дату этого review; его SHA-256 фиксируется в проверке ниже. Если он изменится до применения, MUST заново сверить контекст, не применяя предложение вслепую. Owner decisions, architecture и historical checkpoints не меняются.

**1. В заголовке заменить ровно две строки:**

```diff
-**Version:** 1.0
-**Schema family:** `course/v1`, `lesson/v1`, `problem/v1`
+**Version:** 1.1
+**Schema family:** legacy `course/v1`, `lesson/v1`, `problem/v1`; revision 1.1 `course/v1.1`, `lesson/v1.1`, `problem/v1.1`, `hints/v1`, `teacher/v1`, `content-package/v1`
```

В двух исходных строках после текста имеются два ASCII spaces (Markdown hard break); diff выше показывает строки без этих завершающих пробелов. При замене удалить исходные строки вместе с этими пробелами, новые строки записать без trailing spaces, с LF. Status Draft и Primary representation не менять без отдельного решения.

**2. В §22, после строки «Система MUST NOT угадывать schema по имени файла.» вставить дословно:**

> Перечень выше определяет legacy v1 и сохраняет прежний смысл. Content Spec revision 1.1 определяется нормативным приложением A (N1–N9). Пакет revision 1.1 MUST явно использовать inventory `content-package/v1` и базовые schemas `course/v1.1`, `lesson/v1.1`, `problem/v1.1`; sidecars MAY использовать `hints/v1` и `teacher/v1` по приложению A. Inventory является отдельным YAML-документом. Смешение revisions MUST приводить к ошибке. Наличие inventory с ошибками MUST NOT включать legacy fallback. Автоматическая миграция исторических пакетов MUST NOT выполняться.

**3. В §38 после последнего абзаца вставить дословно:**

> Для legacy v1 все прежние требования настоящей спецификации сохраняются. Нормативное приложение A действует только для явно выбранной revision 1.1. В этой revision неизвестные поля являются ERROR; новые правила IDs, путей, links, assets, hints, teacher materials и compiler outputs MUST применяться по приложению A. Указанные ужесточения MUST NOT ретроактивно менять validation legacy пакетов. Legacy validation не гарантирует возможность компиляции в двухартефактный контракт: такая компиляция MUST завершаться E_REVISION_REQUIRED без artifacts до явной ручной подготовки пакета новой revision. Принятие формата не изменяет опубликованные версии или привязки исторических назначений.

**4. После §40 добавить `## Приложение A. Content Spec revision 1.1 — B07a-C1`.**

Точное содержимое приложения — **дословный непрерывный фрагмент** [PROPOSAL](MVP-B07a-CONTENT-CONTRACT-PROPOSAL.md), начиная со строки `## N1. Revision и совместимость` включительно и заканчивая последним абзацем N9 (перед `## Review evidence и границы`). Это incorporation by exact text range: никакого пересказа, placeholders, TODO или дополнительного выбора при применении diff. Фрагмент содержит полные N1–N9, таблицы и fixture. Относительных Markdown links на документы репозитория внутри фрагмента нет; file names в примерах являются содержимым fixture. В приложении заголовки N1–N9 сохраняются дословно. Вводную строку приложения добавить дословно:

> Настоящее приложение нормативно только для revision 1.1; при расхождении с §§4–38 его явно заданные правила имеют приоритет для этой revision. Legacy v1 не изменяется. Compiler создаёт артефакты, но не публикует курс и не выдаёт runtime authority.

§§4–21/23–37/39–40 физически не редактировать. В частности, §9 о student-only problem body и отсутствие встроенных скрытых решений сохраняются; sidecars вводятся только приложением. Порядок применения шагов 1–4 атомарный: нельзя принять schemas из §22 без ограничений приложения. Матрица остаётся отдельным acceptance документом, а не реализацией tests.

## Проверки, рабочая копия и оставшиеся риски

Canonical directory `/home/tea/github/lms`; исходные HEAD `492e30aef65bbddd24f107903bfaa54a563abe39`, branch `codex/l1-s4-jwt-api-authorization` сохранены. Dirty checkout не переключался на main по прямому указанию сохранить ветку и изменения. Созданы только три deliverables C1. Commit/push/merge/rebase/deploy и новые worktrees не выполнялись.

Проверка документов: `python3 /tmp/check_b07ac1.py` — **PASS, exit 0**. Проверены три документа; positive fixture: 12 файлов, 9 Markdown документов, 2 assets; все 401 baseline files неизменны; HEAD/branch сохранены. Скрипт проверяет local links, извлекает и проверяет YAML полной fixture, resolve assets/targets, размеры, baseline SHA-256, HEAD/branch. Скрипт находится в `/tmp`, не является repository compiler/test implementation.

Whitespace: `git diff --no-index --check /dev/null <each of three deliverables>` — **PASS: пустой diagnostic output для всех трёх**, exit 1 означает новые непустые файлы относительно /dev/null. Первичная проверка обнаружила четыре trailing-space строки в отображении diff; оформление исправлено, повторная проверка прошла. Runtime tests **NOT RUN**: документация не реализует enforcement. До B07a необходима приёмка D01–D08; до B07b/B08/B09/F1 — отдельные интеграционные решения и authorizations. Формат не обнаруживает смысловую секретность student текста/изображения/внешнего HTTPS назначения; авторская классификация и будущая защищённая доставка обязательны.

Зафиксированный SHA-256 действующего Content Spec: `88a7f52d08f4d71d99a5d8bdc48b3b39f566de4d09bc20d79b02ba97f6bd7028`. SHA-256 точного нормативного фрагмента N1–N9 (UTF-8, от начала заголовка N1 до начала `## Review evidence`, включая разделительные LF): `fad1dd34a0488c17c63003495aeddab9789d5a086b9285aa14994809e74e4d47`. При изменении proposal этот review fingerprint MUST обновляться до принятия.

Guardrails §7: covered — actor/use case/invariants/states в proposal; принятые S2/S3/PRS1/gates сохранены; validation/limits/error obligations и future tests заданы; только три согласованных documentation paths, без secrets/real data/release claims. N/A — runtime ownership/subjects/list filters/attachments/history/transactions: новых executable paths нет. Blocked/NOT PROVEN — negative-access, filesystem safety и student serialization до compiler implementation. Unit/integration/E2E NOT RUN; отсутствие runtime доказательств не маскируется проверкой Markdown.

**B07a-C1 READY FOR OWNER REVIEW · CONTENT EXTENSION NOT YET ACCEPTED · B07a IMPLEMENTATION PAUSED · B07b/B08 NOT AUTHORIZED · PRS1 EXACT OFF · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**
