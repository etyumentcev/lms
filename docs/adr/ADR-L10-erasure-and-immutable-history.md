# ADR-L10 — Immutable history and lawful erasure

Дата 2026-10-09. **PROPOSAL / DECISION REQUIRED PD-G08/09**, зависит PD-G02/07 и юридической проверки. [R01/R11–R13/R19](../compliance/PD-LEGAL-REQUIREMENTS.md) LEGAL SOURCE NOT VERIFIED. Это отдельное архитектурное решение об исключении, не разрешение запускать DELETE сейчас.

## FACT: конфликт в настоящей схеме

[InitialPersistence](../../src/Lms.Infrastructure/Migrations/20261007035847_InitialPersistence.cs) ставит BEFORE UPDATE OR DELETE triggers на Submissions/Reviews/Messages/DomainEvents и запрещает изменение student/content reference assignment. [AttachmentStorage](../../src/Lms.Infrastructure/Migrations/20261008013249_AttachmentStorage.cs) запрещает DELETE metadata и изменение attached file. [LmsDbContext](../../src/Lms.Infrastructure/LmsDbContext.cs) содержит RESTRICT FK и uuid[] связей. Annotations живут в Review JSONB. IdentityUser ещё не существует; его будущее удаление не меняет StudentId, TeacherId, Markdown, filename, EXIF или backup. Даже soft-delete pending не уничтожает metadata.

## Альтернативы

| Вариант | Идентифицируемость и контент | Logs/backups/статистика | Оценка |
|---|---|---|---|
| A Отделить Identity от learning | Убирается прямая account связь, но UUID/курс/время, фото, тексты, handwriting annotations и внешний roster позволяют определить человека | ID в audit и backup сохраняются; персональная статистика остаётся ПД | Полезная граница, недостаточно для уничтожения |
| B Контролируемое обезличивание | Нужны методика, удаление linkage, texts/photos/annotations и редких сочетаний либо доказательство невозможности связи; замена имени UUID недостаточна | Все копии/журналы тоже обработать; допустимы только проверенные агрегаты без малых ячеек/индивидуальных timestamps | Сложно доказать на маленьком олимпиадном пилоте; не основной erasure path |
| C Привилегированное уничтожение | Удаляется утверждённый набор записей/фото/сообщений/annotations и связей; исключение из storage immutability под контролем | Отдельно минимальные правомерные evidence, backup deletion/reconciliation; учебная статистика может уменьшиться | Наиболее проверяемо, но FK/trigger/гонки/смешанные субъекты требуют дизайна |
| D A + C, B только если доказан | Разделение account/learning при обычной работе, controlled destruction при отсутствии основания; без обещания сохранить персональную историю любой ценой | Ограниченные evidence на отдельном основании; raw фото/тексты не оставлять ради аналитики; согласованные копии | **Рекомендуется** до пилота |

## Рекомендованное решение D

Обычные commands, GET, retention orphan cleanup никогда не редактируют Submission/Review/annotations; Accepted не открывается повторно. Исключительный erasure workflow находится вне учебного агрегата, уничтожает утверждённые ПД и не создаёт фиктивную попытку, review или статус. Для surviving records инварианты прежние; полностью уничтоженный work отсутствует и недоступен. Изменение storage invariant разрешается только принятием этого ADR и отдельной проверенной реализацией, не молчаливым отключением triggers.

Требуемый механизм в будущем: app DB role не владелец таблиц/DDL и не может включить bypass; отдельная maintenance identity с временным узким полномочием, привязанным к неизменяемому approved manifest/case. Будущий дизайн защищённой процедуры/trigger исключения обязан проверять manifest scope, невозможность arbitrary UPDATE и отзыв capability после исполнения. Не предписывается исполняемый SQL в L0.1; `DISABLE TRIGGER ALL` в обычном app connection запрещён проектом. Не считать custom session variable достаточной авторизацией, если app может её установить.

Manifest строит полное замыкание зависимостей; порядок операции определяется FK и ссылками arrays/JSON. Одно удаление ученика может затронуть чужой teacher feedback; удаление преподавателя может затронуть много чужих работ. Нельзя автоматически уничтожать все работы этих учеников. ПД/юрист определяет остающееся договорное/правовое основание, изолированный retention subset либо отдельный reviewed минимизирующий transformation с сохранением достоверности и проверкой ссылок. Нерешённый смешанный случай блокирует готовность erasure, а не оправдывает бессрочное хранение. Определить исход по каждому объекту и субъекту до запуска.

Фото и EXIF уничтожаются с файлами; annotations как отдельный JSONB/почерк тоже проверяются; Markdown/цитаты/имена файлов/DomainEvents не забываются. Immutable content snapshot с случайными ПД — отдельный manifest зависимых назначений: нельзя удалить общую версию курса и сломать чужие работы автоматически. Редактирование опубликованного контента ради privacy требует отдельного scoped решения.

Статистика: персональный прогресс удаляется вместе с work. Разрешены только агрегаты с доказанной правомерностью и оценкой реидентификации по действующему режиму; если доказательства нет, удалить. Минимальные акты и audit об уничтожении сами могут содержать ПД и живут по отдельному основанию/сроку, не «анонимны» от hash. [Backup protocol](../compliance/PD-LIFECYCLE-AND-ERASURE.md) закрывает возвращение данных; encryption без уничтожения всех ключей не равна erasure.

## Проверка и последствия

До принятия: PD-G08 manifest/роль/reviewer/смешанные данные; PD-G09 копии/сроки; юрист проверяет формы подтверждений и сроки. До пилота: schema review, app-role inability to bypass, обычные immutable regression, полный negative search по synthetic PII в text/photo/EXIF/JSONB/events, foreign-subject preservation, crash/retry, concurrent submit/backup/delete, missing ledger fail-closed restore, акт + log export и provider proof. В L0.1 ни один такой PASS не заявлен.

Цена — сложность maintenance и потеря части исторической статистики; это принимаемое следствие законного прекращения обработки, а не повод нарушать обязательный gate. Последующее согласование владельцем и юристом обязательно перед реализацией workflow; текущее разрешение заканчивается документацией.

## L0.2 amendment — несколько операторов

Статус остаётся PROPOSAL / DECISION REQUIRED, не принят автоматически. Исторический D4 уточнён D4a–d. Policy/case/manifest имеют Organization/context/controller scope; decision authority A не распространяется на B или собственные цели P. Identity disable/delete не заменяет purpose-specific erasure. Ordinary immutable workflow сохраняется, privileged erasure отдельно.

[ADR-L13](ADR-L13-processing-context.md) определяет наследование и версии; [организационный lifecycle](../compliance/PD-ORGANIZATION-LIFECYCLE.md) — mixed-subject cases, shared copies и ledger. Scope проверяется до closure и исполнения, чужой subset требует отдельного решения. Принятие границы ORG-G03/06 до L1, реализация и доказательства L2–L4; ни один новый PASS не заявлен.
