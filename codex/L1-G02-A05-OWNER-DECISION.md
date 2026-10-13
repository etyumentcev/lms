# G02/A05 — Owner decision draft

2026-10-10. **A05 PROPOSAL READY FOR OWNER DECISION · T04 UNCHANGED · S4 BLOCKED.**

Решение ещё НЕ ПРИНЯТО. Разрешение A05 распространяется только на документацию и анализ; никакого automatic implementation authorization.

## Рекомендация

Выбрать B/C как направление отдельной редакции T04: HTTP deadline и publication safety отделить от физического окончания изолированного private I/O. Сохранить 60/480/600/900/7200 и запрет позднего domain effect. Прямой PostgreSQL COMMIT и controlled egress остаются обязательствами с проверяемыми bounds; B/C не устраняет их одним cancellation/quarantine протоколом. Если такие bounds или иная доказанная visibility boundary не получены, S4 остаётся blocked.

[Полное предложение и нормативный draft](L1-G02-T04-REVISION-PROPOSAL.md) · [Security impact](L1-G02-T04-SECURITY-IMPACT.md) · [Acceptance matrix](L1-G02-T04-ACCEPTANCE-MATRIX.md).

## Точный предлагаемый текст решения владельца

> Принимаю рекомендацию G02/A05 B/C как направление подготовки новой редакции T04. Разрешаю отдельный документационный этап консолидации требований и архитектурных обязательств по этому предложению. Это решение само по себе не вводит новую редакцию T04 и не разрешает реализацию S4.
>
> В новой редакции должны быть раздельно определены request deadline, authorization validity, logical operation completion, publication safety, physical I/O completion и recovery. Сохранить access JWT не более 15 минут, ordinary revoke не более 2 часов, запас 10 минут, ordinary HTTP 60 секунд и maximum HTTP 8 минут. HTTP bound относится к earliest controlled ingress — last controlled egress, а не только к возврату handler. Для него требуется явно поддерживаемая и проверяемая модель отказов.
>
> Допустить отсутствие универсального срока физического прекращения только для доказанно изолированного private I/O, не способного менять опубликованные версии, domain result, eligibility, claim или доступ. Неопределённый исход может сохраняться после HTTP deadline. Это не разрешение на позднюю публикацию, продолжение команды, новый COMMIT, retry с новым бюджетом или автоматический replay.
>
> Авторитетный domain effect и публикация должны быть своевременны относительно исходного operation deadline и применимого срока полномочий. Проверка времени до COMMIT не является доказательством. Для visibility-changing PostgreSQL commit необходим проверяемый completion/fence contract либо отдельно рассмотренная архитектура, исключающая позднюю видимость. Если требование не доказано, соответствующий путь и S4 не принимаются.
>
> Recovery только устанавливает прежний исход и поддерживает безопасную quarantine/изоляцию. Новый авторизованный запрос может прочитать ранее своевременно committed результат; он не может завершить просроченную команду. Unknown commit не считается rollback; потенциально committed originals не удаляются. Physical deletion не подтверждается по одному tombstone.
>
> Сохранить межорганизационную изоляцию, current ProcessingRestrictions, SyntheticOnly, immutable versions, idempotency, no-public-bypass и запрет наследования бессрочной authority. T01–T06, E01–E06 и O01 в действующей редакции остаются неизменными до отдельного нормативного утверждения. При консолидации явно проверить совместимость нового смысла completion с E03; конфликт не разрешать скрытым переопределением E03. Если нужна его корректировка, представить отдельное решение владельца.
>
> До нормативной консолидации архивировать сохранённый A04 из /tmp/lms-g02-a04 с provenance, не заменяя текущий S4 checkpoint старой staged копией. Затем представить конкретный нормативный diff, failure model, proof obligations и минимальную границу будущего S4/S7 recovery slice для отдельного утверждения. Новая редакция требований, архитектура и разрешение реализации — отдельные решения. Ни A05, ни согласие с направлением B/C не являются доказательством безопасности, V1 PASS или S4 acceptance.
>
> До этих решений: T04 UNCHANGED; S4 BLOCKED. Runtime, schema, infrastructure deployment, S5–S8, полный S7 lifecycle, M2/M3, production migrations, реальные персональные данные, push/merge/deploy не разрешены.

Этот draft выбирает направление следующей редакции, а не объявляет её принятой одним заголовком. При желании оставить full physical completion владелец выбирает A; тогда P03/C04-1 остаются непригодными по имеющемуся evidence и нужен отдельно разрешённый поиск qualified stack либо остановка G02 для этих deployment. Вариант C без B требует доказать full physical bound для каждого явно включённого fault; список исключений не должен скрывать известные риски публикации.

## Baseline, сохранность и проверка

Canonical directory `/home/tea/github/lms`; ветка `codex/l1-s4-jwt-api-authorization`, HEAD `a60ee6c73c4400bd1bdf6c6df434358372745c41`. Ветка не переключалась; merge/rebase/commit/push не выполнялись. A04 найден и прочитан в `/tmp/lms-g02-a04`, его четыре deliverables и staged checkpoint сохранены; они отсутствуют в canonical codex. Текущее задание разрешает работу в текущей ветке без переключения, поэтому повторное разрешение ветки не запрашивалось.

Предсуществующие modified checkpoint/test, untracked A02/A03/evidence и остальные файлы сохранены. A05 создаёт только четыре указанных Markdown-файла. Контрольная сумма baseline хранится локально в `/tmp/lms-g02-a05-baseline.json`; документарная проверка — `/tmp/lms-g02-a05-validation.json`. Проверка: четыре artifacts/status и 28 локальных ссылок — PASS; HEAD/ветка неизменны; 330 исходных файлов и все 7 файлов A04 совпадают с SHA-256 baseline. Единственное внешнее изменение baseline — `.obsidian/workspace.json`: A05 его не редактировал и не восстанавливал. Первая проверка обнаружила это отличие; повторная проверка выделила его явно, без объявления полной byte-for-byte неизменности всего дерева. Backend tests и fault experiments A05 NOT RUN: runtime не менялся. Независимый security review NOT RUN.

Остановка после предложения. **A05 PROPOSAL READY FOR OWNER DECISION · T04 UNCHANGED · S4 BLOCKED.**
