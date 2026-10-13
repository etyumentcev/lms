# MVP-B07a — результаты проверки контракта

2026-10-10. **BLOCKED — OWNER CONTENT-CONTRACT DECISION REQUIRED.**

Реализация разрешена сообщением OWNER AUTHORIZATION — MVP-B07a. Применён его §5: если действующий content contract не позволяет безопасно разделить Student/Teacher, остановиться и представить точное противоречие и минимальные варианты решения. Это остановка до реализации, не завершённый B07a и не owner acceptance готового компонента.

## Feature contract и границы

Actor → автор локального synthetic курса. Use case → локальный пакет в diagnostics либо два immutable artifacts. Invariants → детерминизм, отсутствие teacher-only данных во всём student artifact, fail closed, ограниченные локальные чтения. Entities/VO → Course/Lesson/Problem, source hash, asset manifest, отдельные teacher materials. States → source → invalid либо compiled; переходов публикации нет. Authorization/data-source matrix → явно переданный локальный root только для чтения; compiler не выдаёт grants, не использует сеть, shell, Git credentials, БД или HTTP. Tests → positive/determinism/preservation и все negative cases из owner authorization, затем Application regression; сейчас NOT RUN из-за остановки до кода.

Зависимости: [content specification](../docs/content/COURSE-CONTENT-SPEC.md), [content architecture](../docs/architecture/CONTENT-ARCHITECTURE.md), [Codex content contract](CONTENT-CONTRACT.md), [guardrails](SAPIENTUM-MVP-ENGINEERING-GUARDRAILS.md), [reconciliation](MVP-GUARDRAILS-RECONCILIATION.md), examples/course. Необходимые по AGENTS.md skills model-domain, implement-use-case, implement-feature отсутствуют в доступном каталоге; их применение не заявляется.

## Точное противоречие / недостающий контракт

1. Specification §9: problem.md содержит только разрешённый ученику контент, запрещает скрытые решения/ответы/teacher comments/rubric. Отдельные `01.solution.md` и `01.hints.md` приведены лишь как пример; явно сказано: «Эти файлы не входят в обязательный v1».
2. §22: каждый документ MUST иметь schema; допустимы только course/v1, lesson/v1, problem/v1; неподдерживаемая schema — ошибка; угадывать schema по имени файла запрещено.
3. §38: неизвестным полям MUST NOT придавать новую семантику. Следовательно, добавить к problem/v1 поле audience/solution/rubric и начать управлять им видимостью нельзя без нормативного расширения.
4. Нет схемы/обязательных полей teacher material, однозначной связи sidecar с lesson/problem, правил доступности hints или разделения assets. `source` в §8 — библиографическое описание, не нормативная связь sidecar с задачей.

Например, для `01.solution.md` без schema нарушается §22; `schema: solution/v1` не поддержана; `schema: problem/v1` не определяет teacher-only классификацию, а вывод её из суффикса был бы новым правилом. Копирование всех ресурсов общего assets в student artifact также не имеет основания считать их разрешёнными ученику. Это не утверждение, что безопасный compiler технически невозможен: для полного B07a отсутствует согласованный входной контракт, его нельзя молча изобрести.

## Минимальные варианты решения владельца

**A — рекомендуется: принять отдельный нормативный B07a extension к v1.** Сохранить три существующие схемы и examples/course. Явно разрешить sidecar schemas `hints/v1` (student-visible) и `teacher/v1` (teacher-only), поля `id`, `targetKind: course|lesson|problem`, `targetId`, `kind: notes|solution|rubric` для teacher, положительный `order` для hints. Решение владельца должно утвердить допустимые сочетания kind/target, область уникальности ID и порядок нескольких материалов. Схема задаёт видимость, имя файла её не задаёт. Неизвестные/неоднозначные targets — ERROR. Student links в teacher documents/assets — ERROR. Для assets закрепить явное разделение public/teacher roots, проверять все ссылки и не копировать весь root автоматически; teacher manifest и metadata не включать в student serialization. Определить, что student source hash хеширует только student projection, а полный package hash хранится в teacher/build metadata, чтобы не переносить teacher metadata в student output. Это предложение, не принятый контракт и не реализованная схема.

**B — уменьшить B07a до student-only v1 validator/compiler**, отдельным решением отложив hints/teacher materials/rubric. Это меняет обязательный scope текущего разрешения и не может считаться выполнением текущего B07a.

После выбора A сначала зафиксировать точный extension contract, затем реализовать и проверить полный slice. Повторное разрешение уже согласованных DB/API-free работ не требуется; требуется именно решение по формату.

Лимиты уже заданы §28: configurable, рекомендованы Markdown 1 MB, image 10 MB, static file 50 MB. Package limit должен быть явным параметром вызывающего кода; production quota не изобретать. Единицы MB и точные граничные проверки нужно зафиксировать в extension/публичном контракте реализации.

## Изменения, результаты и доказательства

Единственный созданный файл этой задачи: `codex/MVP-B07a-RESULTS.md`. Публичных runtime contracts и compiler implementation нет. Входной реализованный формат, реальные compiled student/teacher artifacts и asset manifests отсутствуют; демонстрационные JSON не выдаются за реальные результаты.

Новые positive/component/negative tests: **NOT RUN — implementation blocked**. Application regression: **NOT RUN — остановка до реализации**. Отсутствие утечки в serialization: **NOT PROVEN**, нет реализации для проверки. Проверены текст спецификации §§9/22/28/38 и examples inventory. Проверка whitespace отчёта: `git diff --no-index --check /dev/null codex/MVP-B07a-RESULTS.md` — whitespace diagnostics отсутствуют; exit 1 для сравнения нового непустого файла с /dev/null, не runtime test.

Канонический каталог `/home/tea/github/lms`, существующая ветка `codex/l1-s4-jwt-api-authorization` сохранена по прямому указанию владельца. Предсуществующие tracked/untracked изменения, включая `.obsidian/workspace.json`, не редактировались и не восстанавливались. Commit/merge/rebase/push/deploy не выполнялись. Новые worktrees, DB/schema/migrations, HTTP endpoints, frontend, assignments и production DI не создавались и не менялись.

## Guardrails §7 / self-review

- covered — actor/use case/invariants/states описаны выше; реализация blocked.
- covered — S2/S3/ProcessingRestrictions не изменялись; PRS1 exact остаётся off.
- not applicable — org/course/group ownership, IDOR, роли, revocation, processing evidence и authorization transactions: новых runtime access paths нет.
- blocked — разделение student/teacher, nested data, metadata, serialization и assets требует принятого контракта и будущих negative tests.
- not applicable — lists/counts/pagination, attachments, mutable history: код этих путей не менялся.
- not applicable — transaction owner/isolation/lock order/CAS/commit/audit/retry/unknown outcome: нет runtime writes; единственная запись — новый локальный отчёт.
- blocked — validation/path traversal/symlink/size/unsafe links/несанкционированное чтение: требования сохранены, реализация и проверки не выполнены.
- covered — тестовые результаты не выдуманы; все runtime tests явно NOT RUN.
- covered — нет изменений schema/API/security/frontend/чужих файлов, реальных персональных данных или секретов.
- covered — readiness не завышена; этот отчёт не доказывает безопасность или готовность публикации.

**B07a BLOCKED ON CONTENT CONTRACT · B07b/B08 NOT AUTHORIZED · PRS1 EXACT OFF · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**
