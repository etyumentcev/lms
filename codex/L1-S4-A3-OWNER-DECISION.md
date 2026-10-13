# L1/S4-A3 — Owner decision package

## Owner Decision — L1/S4-A3-R1 — 2026-10-10

Owner: владелец проекта. Evidence: явное сообщение «Owner Decision — L1/S4-A3-R1» от 2026-10-10 в текущей задаче. **PRS1 NORMATIVE CONTRACT ACCEPTED.** Принята нормативная семантика ProcessingRestrictions selector profile PRS1 контракта S4-A3 с изменениями S4-A3-R1.

Перед фиксацией сверены локальные contract §1–6, acceptance matrix и normative diff: расхождений с принятой редакцией не выявлено. Нормативный текст и oracle строк сохранены без изменений. §6 описывает candidate mechanism; принятие требований к durable DB-backed provenance не утверждает этот механизм.

### Принятый scope

1. Canonical versioned ResourceRef с same-org binding и строгим server-side resolution.
2. Read=1, Submit=2, Review=4; допустимые новые маски 1–7.
3. Биты 8/16 — ReservedDeny. Persisted reserved rows обрабатываются conservative по context/subject без resource narrowing; required reserved operations не разрешаются.
4. Reflexive subtree с историческими Submission/Review связями каждой попытки.
5. Embedded content, Message feedback, annotations, derived metadata и attachments требуют проверки всех применимых источников и субъектов.
6. Attachment physical parent — Submission XOR Message; pending не предоставляет downstream permission.
7. Полный server-derived affectedSubjects; отсутствие доказанной полноты → deny.
8. Safe filtering до materialization, pagination, count и aggregation; недоказанная корректность → deny.
9. Legacy restrictions остаются conservative; automatic narrowing запрещён.
10. Exact PRS1 matching требует durable DB-backed profile provenance и отдельно принятого механизма её обеспечения.
11. RB01–RB03, RT01–RT05, N01–N05, AF01–AF06, PV01–PV10 — нормативные требования будущей проверки; **NOT IMPLEMENTED / NOT RUN**.

### Не принято и не разрешено

Приёмка не разрешает exact selector matching implementation; runtime/API/schema/migrations; конкретный DDL sidecar; trusted administrative writer и transaction lock protocol; rollout/reissue/migration исторических restrictions; HTTP/file/storage/production integration; закрытие G02 или изменение T04/E03. SR-01–SR-04 остаются integration gates; внешний human security audit **OPEN**.

Разрешена только документационная фиксация. Формулировки ниже о NOT ADOPTED, proposed и ожидании приёмки сохранены как история подготовки пакета и superseded этим решением только в принятом scope. Candidate sidecar, writer, serialization и rollout остаются предложениями, требующими отдельного решения. Runtime остаётся conservative.

**PRS1 NORMATIVE CONTRACT ACCEPTED · EXACT IMPLEMENTATION NOT AUTHORIZED · T04 UNCHANGED · E03 UNCHANGED · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**

## Пакет до окончательной приёмки (история)

2026-10-10. R1. **S4-A3 DOCUMENTATION AND AI SECURITY REVIEW ACCEPTED. PRS1 CORE DESIGN DIRECTION ACCEPTED WITH CHANGES.** Уже полученные решения отделены от предложений; подготовка документов не является их нормативным принятием.

## Принято владельцем

**S4-A2 COMPONENT SCOPE ACCEPTED.** Принят commit `492e30aef65bbddd24f107903bfaa54a563abe39` на основании [отчёта S4-A2](L1-S4-A2-RESULTS.md): **429 passed, 0 failed, 0 skipped**. Приёмка ограничена внутренними JWT issuance, qualified clock/deadline, signing-key lifecycle и conservative ProcessingRestrictions. Это результат A2, не новый прогон A3.

**ProcessingRestrictions exact selector semantics: NOT ADOPTED.** Действующая консервативная политика сохранена. Исторические отчёты не переписаны; эта запись фиксирует более позднее решение владельца.

Разрешён только documentation/security review S4-A3. Не разрешены runtime/schema/HTTP изменения, exact selector implementation, S4-B, S5–S8, production clock/KMS и реальные персональные данные.

## Артефакты и результаты

- [Selector contract](L1-S4-A3-SELECTOR-CONTRACT.md): PRS1 grammar, пять битов, inheritance, subjects, compatibility и write validation; **PROPOSAL — NOT ADOPTED**.
- [Acceptance matrix](L1-S4-A3-SELECTOR-ACCEPTANCE-MATRIX.md): отрицательные и контрольные сценарии; **PROPOSED / NOT IMPLEMENTED / NOT RUN**.
- [Security review](L1-S4-A3-SECURITY-REVIEW.md): отдельный AI reviewer, не автор A1/A2, статическая проверка восьми областей. Внешний человеческий аудит и организационная независимость не заявляются.

Review не установил подтверждённых дефектов принятого component scope. SR-01–SR-04 фиксируют snapshot freshness, lifetime validated authority после key distrust, host clock/key trust и будущий subject/selector contract. Для каждого указаны severity, evidence, компонент и рекомендация. Это Informational/integration gates; отсутствие найденных дефектов не доказывает безопасность будущей интеграции. Исправлений кода не выполнялось.

## Owner Decision S4-A3 — принятые направления

Получено после подготовки исходного пакета. Приёмка документации и отдельной AI review не является окончательным нормативным принятием PRS1.

| ID | Зафиксированное решение владельца |
|---|---|
| D01 | Принять versioned canonical resource references с обязательным same-org binding |
| D02 | Read=1, Submit=2, Review=4 и compound plans; 8/16 — ReservedDeny до отдельного нормативного определения |
| D03 | Reflexive subtree по server-confirmed chain; уточнить повторные submissions/reviews и attachment parents |
| D04 | Полное server-derived affectedSubjects; неполное множество → deny |
| D05 | Prefilter до materialization, pagination, aggregation; недоказанная корректность → deny |
| D06 | Legacy conservative; автоматический narrowing запрещён |
| D07 | Durable database-backed selector provenance; конкретная схема отдельно |
| D08 | Отдельный AI review принят с LIMITED ASSURANCE; внешний human security audit OPEN |
| D09 | SR-01–SR-04 сохраняются integration gates |

## R1 на окончательную приёмку

Разрешена и выполнена документационная ревизия. [Нормативный diff](L1-S4-A3-R1-NORMATIVE-DIFF.md) перечисляет точные изменения и оставшиеся зависимости. Обновлены contract и matrix; исходный security review сохранён как принятый исторический артефакт, новая независимая проверка в R1 не заявляется.

Предлагаемые детали R1: reject новых masks с reserved bits и conservative fallback persisted rows; связь review с конкретной попыткой; separate gates embedded/derived/feedback/annotation content; XOR Submission/Message attachment parent и pending recheck; DB sidecar provenance с immutable snapshot/decision evidence и atomic reissue. Эти детали представлены для окончательной приёмки, а не объявлены уже действующим runtime контрактом.

Точные grammar/лимиты, fallback и dependency semantics, логический sidecar design требуют окончательного решения по пакету. Конкретные DDL/constraints/DB roles/serialization/rollout требуют отдельного schema review и разрешения реализации. Transfer/Lifecycle semantics, files/HTTP integration, human audit и SR-01–SR-04 остаются OPEN. Принятие PRS1 не разрешит автоматически implementation, schema migration, HTTP, S4-B/S5–S8, G02 closure или изменение T04/E03.

## Сохранность и верификация исходного A3 (история)

Canonical `/home/tea/github/lms`; текущая ветка `codex/l1-s4-jwt-api-authorization`, HEAD `492e30aef65bbddd24f107903bfaa54a563abe39`. Указание владельца сохранить текущую ветку выполнено без перехода на main. Switch, merge, rebase, push, deploy и новые worktrees не выполнялись.

Состав A3 — ровно четыре новых `codex/L1-S4-A3-*.md`. Существующие dirty A1/A2/S4 reports, `tests/Lms.Infrastructure.Tests/AttachmentDeadlineBoundaryTests.cs`, G02 artifacts и `.obsidian/workspace.json` не редактировались. Baseline hashes, status и index сохранены в `/tmp/lms-s4-a3/`; итоговая проверка — `validation.json`. Проверяются состав новых файлов, ссылки, whitespace, сохранность baseline и index. Runtime tests не перезапускались: runtime/tests не менялись. Локальный commit не создавался; документы оставлены в рабочем дереве для решения владельца.

Итоговая проверка 367 baseline файлов выявила одно внешнее изменение `.obsidian/workspace.json`; эта задача файл не записывала и не восстанавливала. Остальные 366 baseline файлов совпали по SHA-256. Поэтому полная неизменность рабочего дерева не заявляется. Первоначальная строгая проверка остановилась на этом отличии; оно явно классифицировано как внешнее и сохранено. Добавлены ровно четыре документа; ссылки и whitespace без ошибок, index/HEAD/ветка неизменны.

Первая попытка записи была остановлена технической ошибкой automatic approval review (usage limit), а не решением о небезопасности действия. После указания владельца продолжить повторный доступ был разрешён.

## Сохранность R1

Baseline и копии исходных четырёх документов: `/tmp/lms-s4-a3-r1/`. Scope R1: изменения contract/matrix/owner decision и новый `L1-S4-A3-R1-NORMATIVE-DIFF.md`. Security review, runtime, schema/migrations, API, T04/E03 и чужие изменения не редактировались. Проверка сохранности, ссылок, статусов/битовых mappings и whitespace фиксируется в `validation.json`. Тесты не запускались: acceptance cases остаются NOT RUN. Commit не создавался. Ветка/HEAD/index сохраняются. Итог: ссылки и whitespace без ошибок; security review и все baseline файлы вне трёх редактируемых документов совпали по SHA-256, кроме внешнего изменения `.obsidian/workspace.json`. Этот файл задача не записывала и не восстанавливала; его новое содержимое сохранено. Добавлен ровно один R1 normative diff.

**S4-A3-R1 READY FOR OWNER ACCEPTANCE · PRS1 NOT YET NORMATIVELY ADOPTED · T04 UNCHANGED · E03 UNCHANGED · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**
