# L1/S4-A3 — Proposed ProcessingRestrictions selector contract

> **Актуальный owner decision, 2026-10-10: PRS1 NORMATIVE CONTRACT ACCEPTED.** [Решение и границы приёмки](L1-S4-A3-OWNER-DECISION.md). Сверка contract §1–6, matrix и normative diff выполнена до фиксации; смысловых расхождений нет. Прежние статусы proposed / NOT ADOPTED / READY FOR OWNER ACCEPTANCE ниже — исторические статусы подготовки и superseded в принятом scope. Нормативные правила и acceptance oracles сохранены; cases остаются **NOT IMPLEMENTED / NOT RUN**. §6 sidecar — кандидат, механизм provenance требует отдельной приёмки; DDL, administrative writer, lock protocol, rollout/reissue/migration и implementation не разрешены. SR-01–SR-04 — integration gates; human audit OPEN.

**PRS1 NORMATIVE CONTRACT ACCEPTED · EXACT IMPLEMENTATION NOT AUTHORIZED · T04 UNCHANGED · E03 UNCHANGED · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**

## Редакция R1, представленная на приёмку (сохранена без изменения)

2026-10-10. R1. **PRS1 CORE DESIGN DIRECTION ACCEPTED WITH CHANGES; PRS1 NOT YET NORMATIVELY ADOPTED; DOCUMENTATION ONLY.** Владелец принял S4-A2 component scope commit `492e30aef65bbddd24f107903bfaa54a563abe39`, результат отчёта 429 passed / 0 failed / 0 skipped. Это не принятие exact selectors. Действующий conservative gate остаётся без изменений.

## Основание и граница

[ADR-L18](../docs/adr/ADR-L18-synthetic-processing-restrictions.md), [ProcessingContext](../docs/architecture/PROCESSING-CONTEXT-MODEL.md), [S4-A2 decision](L1-S4-A2-OWNER-DECISION.md) задают current prohibition, same-org context и server-derived affiliation, но не грамматику или битовую семантику. [Schema](../src/Lms.Infrastructure/SchemaFoundations.cs) хранит nullable string ResourceRef, Operations 1–31 и immutable provenance; [ScopeAction](../src/Lms.Application/ScopedAuthority.cs) имеет ordinals 0/1/2, которые не являются битовой маской.

Ниже «должен» означает требование **предлагаемого** профиля PRS1 после отдельного принятия и реализации. Документ не меняет T04, E03, runtime, schema, HTTP, legal basis или порядок допуска real data. Все fixtures synthetic. Matrix: [acceptance](L1-S4-A3-SELECTOR-ACCEPTANCE-MATRIX.md); решения: [owner](L1-S4-A3-OWNER-DECISION.md).

## Статус правил после Owner Decision S4-A3

Приняты направления D01–D09: versioned canonical same-org references; Read=1/Submit=2/Review=4 с compound plans; reflexive subtree; полное server-derived affectedSubjects; prefilter; legacy conservative; database-backed provenance; AI review LIMITED ASSURANCE; SR-01–SR-04 integration gates. **Это принятие направления, не окончательная нормативная приёмка PRS1 и не разрешение реализации.**

Уточнения R1 для окончательной приёмки: точная grammar/лимиты §1, ReservedDeny fallback §2, attempt/attachment/nested closure §3, минимальный provenance design §6. Transfer/Lifecycle не определены, биты 8/16 зарезервированы. Конкретная DDL, rollout и serialization protocol зависят от отдельно утверждаемой будущей схемы. Внешний human audit OPEN. Итоговый нормативный diff: [R1 diff](L1-S4-A3-R1-NORMATIVE-DIFF.md).

## 1. Ресурсы, грамматика и организация — D01

Предлагается versioned, case-sensitive ASCII wire format:

```text
ref        = "prs1/" org "/" target
org        = uuid
target     = "all"
           / "course/" sid
           / "group/" sid "/" sid
           / "assignment/" uuid
           / "work/" uuid
           / "submission/" uuid
           / "review/" uuid
           / "attachment/" uuid
uuid       = lowercase nonzero UUID in exact 8-4-4-4-12 hex form
sid        = canonical unpadded base64url(strict UTF-8 bytes of a nonempty stored ID)
```

В group первый sid — CourseId, второй — GroupId. Course/Group IDs действительно строки, остальные entity IDs — GUID. UUID не требует конкретного UUID version. Максимальная длина ref — 4096 ASCII bytes, decoded sid — 1024 UTF-8 bytes; существующие ID вне лимита остаются только в conservative режиме до отдельного решения. Запрещены empty/all-zero UUID, whitespace вокруг ref, padding, percent decoding, query/fragment, дополнительные сегменты, неизвестный kind/version, invalid UTF-8 и noncanonical base64 trailing bits. Строка `course-A` кодируется как `Y291cnNlLUE`, не интерпретируется как UUID. ResourceRef — идентификатор, не URL; не разыменовывать сеть/файловую систему.

Канонизация производится при конструировании из trusted IDs: UUID lower-case D, строки — точные UTF-8 bytes, base64url без padding. Парсер принимает только точную canonical round-trip форму; не trim, case-fold, NFC/NFD-normalize и не decode дважды. Значения sid, различающиеся регистром или Unicode normalization, различны. Будущая реализация обязана обеспечить ordinal ID equality в resolver/DB либо отклонить неоднозначное разрешение. Никакое совпадение только ID/префикса не достаточно.

org обязан совпадать с OrganizationId restriction, текущего контекста, resource root и actor authority boundary. ResourceRef не назначает ownership: сервер разрешает существование и всю цепочку из БД. Чужой org, несовместимые routing hints, неизвестный/удалённый root или сломанная цепочка — deny целевой операции; запись такого selector отклоняется. `all` означает все ресурсы **текущего ContextId в данной организации**, не всю организацию независимо от контекста. Одинаковый course/group label в чужом org не совпадает.

PRS1 не имеет wildcard/prefix matching, ranges, identity/session selectors, arbitrary Message/annotation selectors. Message attachment получает цепочку своего work; отдельный Message selector требует нового профиля. Attachment kind резервируется для полного контракта; component authorization продолжает запрещать attachments.

D01 direction принят. Wire format/лимиты выше остаются детализацией для окончательной приёмки. Durable provenance должна храниться в БД (§6); одна только встроенная версия не доказывает принадлежность старой строки PRS1.

## 2. Пять битов и действия — D02

Read/Submit/Review и compound plans приняты как направление. Окончательная детализация fail-closed поведения ReservedDeny предложена в R1:

| Бит / число | Имя | Статус и значение |
|---|---|---|
| 0 / 1 | Read | Чтение содержимого/метаданных, response/list/count contribution |
| 1 / 2 | Submit | Student work/submission effects; файловые effects этим не определены |
| 2 / 4 | Review | Teacher review/feedback effects; файловые effects этим не определены |
| 3 / 8 | ReservedDeny | Не назначен Transfer; нет разрешённой операции |
| 4 / 16 | ReservedDeny | Не назначен Lifecycle; нет разрешённой операции |

Новый PRS1 writer принимает только непустые маски 1–7. Маски с 8/16 (включая 9/24/31) отклоняются целиком, без stripping bits; zero/negative/unknown bits также reject. Существующая БД допускает 1–31: это persistence range, не новая registry. Если active persisted запись содержит reserved bits, её нельзя игнорировать или свести к младшим битам: применяется legacy-style conservative deny по current context/subject **без narrowing по resource/operation**. Такой же fallback при отсутствующей/недостоверной provenance или неизвестном профиле. Значение 31 остаётся полным conservative запретом, но не допустимой новой PRS1 записью.

Для подтверждённой PRS1 записи с mask 1–7: `denied ⇔ (Operations & requiredOperations) != 0` при совпавших context, resource и subject. Read → 1, Submit → 2, Review → 4; Submit с возвращаемым содержимым → 3, Review с чтением submission → 5. Все фактические операции входят в plan до admission; mask 7 допустима для совокупности всех трёх. ScopeAction ordinals и Membership.PermissionFlags не используются как registry. Служебное ownership resolution не отдаёт пользовательское содержимое и не является самостоятельным Read.

Required plan с 8/16, unknown bits, неизвестным действием либо неполным effect set → deny независимо от наличия restrictions. Export/transfer/download/lifecycle/jobs не получают mapping 9/25 или разрешения под видом Read. Их семантика требует отдельного нормативного решения; файловая интеграция по-прежнему запрещена. Будущее назначение reserved bits не переосмысливает старые записи автоматически: нужен versioned compatibility decision.

## 3. Наследование и attachments — D03

Принято направление reflexive subtree semantics: restriction совпадает с самим узлом и всеми подтверждёнными descendants, но не ancestors/siblings. Сервер строит ancestor closure:

| Цель | Closure для resource selector |
|---|---|
| Course | all, course |
| Group | all, course, group |
| Assignment | all, course, frozen assignment group (если есть), assignment |
| Work | closure assignment, work |
| Submission | closure work, submission |
| Review | closure submission, review; work должен совпадать с review.WorkId |
| Attached file | closure единственного подтверждённого Submission либо Message→Work, attachment; semantic dependencies проверяются дополнительно |
| Pending file | all, verified course, attachment; uploader subject; если target parent ещё неизвестен, downstream выдача запрещена |

Review является ребёнком submission в существующем resolver, а не произвольным соседом submission. Restriction на submission запрещает его review и файлы этой submission; не любые файлы work. Restriction на review не запрещает другую submission; чтение parent с embedded review проверяет review отдельно. Course-wide запрет действует на все группы, group-scoped — на immutable affiliation assignment, а не на текущую группу студента после move. Null group не равно all groups. Work owner сохраняется при move; generation/scope checks остаются независимым пересечением условий.

Attachment дополнительно наследует subjects owner/uploader и parent. До привязки проверяются uploader и course; attach/finalize требует повторного check полного parent closure. Неизвестный либо неоднозначный parent, quarantine, несовпавшие stored/scope associations → deny. Нельзя использовать pending binding как обход parent restriction. Storage bytes/stream, jobs и publication требуют отдельной реализации, сериализации и разрешения; этот документ их не разрешает.

### Проверенная доменная связь и повторные попытки

Факты текущего кода: [ProblemWork](../src/Lms.Domain/ProblemWork.cs), методы SubmitSolution/ReviewCurrent, и [History](../src/Lms.Domain/History.cs) задают новый Submission.Id и AttemptNumber=AttemptCount+1 при каждой отправке. Submit запрещён в Submitted/Accepted; после NeedsRevision возможна следующая попытка. ReviewCurrent принимает только последнюю Submission и не более одного review на неё. Review хранит собственные ProblemWorkId и SubmissionId; resolver [PostgresScopedAuthorityReader](../src/Lms.Infrastructure/PostgresScopedAuthorityReader.cs) сверяет обоих родителей. Это статическая проверка кода, не новый тестовый прогон.

R1 semantics: W→S1→R1(NeedsRevision), затем W→S2→R2 — две sibling ветви. R1 остаётся под S1 после S2; нельзя разрешать parent через «latest submission». Restriction S1 охватывает R1 и attachment S1, но не S2/R2. Restriction W охватывает обе ветви. Restriction R1 не переносится на S1 bytes или sibling R2, однако любое представление R1 проверяется как R1. Review с несовпадающим WorkId/Submission.WorkId, отсутствующей submission либо дублирующей/неоднозначной связью → deny. Номер попытки не заменяет identity. Allow selector не снимает domain rule latest/unreviewed для новой review mutation.

### Вложенные объекты и альтернативные представления

Успех gate на parent metadata не разрешает весь aggregate. Каждый embedded submission/review/annotation/feedback и derived field проверяется по собственным источникам и subjects до materialization. Work.AttemptCount, status, last-reviewed/accepted timestamps, summaries/counts могут раскрывать restricted child: их нельзя выдавать из полного aggregate после удаления child rows. Допустима только доказанная safe projection из разрешённых contributions; если семантика поля требует запрещённого source, поле исключается по явно заданному projection contract либо вся операция deny. Молчаливое выдумывание статуса недопустимо.

Review.MessageId указывает на feedback Message в том же work; ReviewCurrent создаёт его с пустым AttachmentIds. Message физически work-level, но при выдаче feedback через Messages/ReplyTo/summary сервер обязан найти обратную Review.MessageId association и применить также closure Review→Submission→Work. Неопределённая/противоречивая association → deny. Обычный Message наследует work; ReplyTo не делает attachment ребёнком reply target, но выдаваемый цитируемый/referenced content проходит самостоятельный gate. Отдельный Message selector не вводится.

[Annotations](../src/Lms.Domain/Annotations.cs) допускают только AttachmentId из проверяемой submission. Annotation presentation требует обоих gates: review и referenced attachment с его submission; запрет файла не обходится thumbnail/annotation rendering. Ссылка review на файл не меняет его physical parent на review. Повторная отправка не переносит файлы/аннотации старой попытки автоматически.

### Pending и attached files: только proposed dependency semantics

[StoredAttachment](../src/Lms.Infrastructure/StoredAttachment.cs) содержит SubmissionId/MessageId, без ReviewId. Для будущего PRS1 admission предлагается строго: оба null = pending; ровно один заполнен = attached; оба заполнены = ambiguous, deny. Проверить reciprocal AttachmentIds, uploader/author, same work/course/org, scope/context, отсутствие quarantine/deleted и полноту subjects. Нельзя выводить допустимость обоих родителей из приоритета веток текущего resolver. Это proposed stronger validation, не утверждение, что оно уже реализовано.

Submission file следует конкретной попытке. Message file следует Message→Work и semantic feedback dependencies выше, если они доказаны; прямой parent Review неподдерживаем и deny. Feedback создания Review сейчас не содержит файлов. Для pending нужны trusted scope/course и uploader; отсутствие destination parent не разрешает доступ к данным будущего parent. Attach требует повторного разрешения единственного destination, полного affectedSubjects и parent closure в future serialized transaction. Истёкший/ineligible pending, смена/повторная привязка parent без отдельного допустимого workflow, отсутствие reciprocal связи → deny. Никакой proposal не открывает upload/download/storage/HTTP: текущий ComponentAuthorization запрещает Attachment целиком.

## 4. Субъекты, списки, счётчики и момент проверки — D04/D05

Для операции сервер строит affectedSubjects как множество caller, resource student/owner, uploader, review/message author и других субъектов, чьи данные читаются, изменяются, агрегируются или передаются. Это Actors, не IdentityUserId. Клиент не может сокращать множество. Пустое/неполное множество для потенциально subject-bearing содержимого → deny. В текущем компоненте достоверно доступны caller/student; расширение требует новых resolver contracts, без ложного заявления о существующей поддержке authors/third parties.

SubjectActorId=null означает всех субъектов; non-null совпадает, если входит в affectedSubjects. Для resource-neutral course metadata множество может состоять только из caller при доказанном отсутствии иных subject data. Все измерения restriction соединяются AND: current same-org ContextId + Active + subject match + resource closure match + operation bit intersection для достоверного PRS1 1–7; legacy/reserved/invalid provenance fallback из §2/§5 не сужается этими selector dimensions. Несколько restrictions соединяются OR (любой запрет побеждает); никакая более узкая запись не отменяет общую. OriginVersion — provenance, а не требование equality с новой ContextVersion: запрет ContextId продолжает действовать при смене версии. Смена ContextId требует явного переноса/решения о restrictions до допуска, иначе deny перехода.

Принято направление row-level exclusion для lists/queues: каждый candidate и каждое nested поле/ребёнок проходят тот же gate до materialization, pagination и aggregation. Caller-wide/all-resource restriction запрещает запрос целиком. Count/sum/grouping используют только разрешённые contributions; total, facets, cursors, ordering и page sizes не должны раскрывать исключённые записи. Subject-bearing contribution с несколькими субъектами исключается целиком при запрете хотя бы одного; нельзя показывать остаточную оценку на основе запрещённых данных. Если implementation не может доказать правильный prefilter, deny весь запрос. Нет «сначала SQL count, потом убрать строки».

D04/D05 направления приняты: полное множество субъектов и prefilter обязательны; caller-only и post-filter-after-count не альтернативы текущей R1 модели. Нулевой count означает отсутствие доступных contributions, не отсутствие данных. Real-data aggregate inference/privacy этим не закрыты.

Current restrictions читаются из PostgreSQL без positive cross-request cache; unknown clock/DB/ambiguous binding → deny. Весь qualified interval должен покрываться единственным binding и effective context. Read check не становится admission/egress гарантийным барьером. Будущий write обязан заново проверить полный operation plan, subjects, binding и restrictions в своей transaction и сериализоваться с restriction update; конкретный lock/CAS protocol требует отдельного design/review. Existing E06 stream limits, T04 и E03 не изменены.

## 5. Версионирование, старые opaque values и запись — D06/D07

PRS1 — версия всей registry (resource grammar, bits, inheritance, subjects), а не только parser. Restriction.Version — optimistic state version, OriginVersion — context provenance; ни одно поле не подменяет selector profile. Новая несовместимая registry требует prs2 и отдельного решения, unknown version всегда conservative deny для соответствующих context/subject. Нельзя принять синтаксически похожую старую строку за PRS1 без доказанной provenance.

Принятое направление перехода: по умолчанию **все существующие записи остаются legacy conservative**, включая null, empty, malformed, opaque и случайно похожие на `prs1/...`; любой их active applicable context/subject запрещает все операции независимо от selector. Legacy withdrawal остаётся явным решением с audit, не следствием parsing failure. Новый parser не переписывает исторические записи и не выводит meanings из старых Operations.

Перед будущим включением exact matching нужны inventory только synthetic metadata, явная аттестация owner для каждого нового/reissued RestrictionId + profile и совместимость всех readers/writers. Принято направление durable database-backed profile/provenance; минимальный candidate design приведён в §6, DDL ещё не утверждена и не реализована. До принятия и реализации этого механизма prefix недостаточен, narrowing запрещён. Для новой PRS1 записи универсальность кодируется `prs1/<org>/all`; null/empty невалидны. Существующие immutable selector fields не редактируются: создаётся новая запись, затем старая withdrawal в согласованной transaction без allow gap; narrowing при этом требует explicit approval.

Смешанные старые/новые readers допускаются лишь с доказательством, что старые readers overdeny и не игнорируют новую запись. Unsupported profile у любого обслуживающего exact reader → conservative deny, а не skip. Rollback возвращает conservative policy, не удаляет restrictions. Нельзя включить org-wide feature flag, автоматически объявляющий все opaque rows PRS1.

Будущий write validator проверяет: trusted administrative capability (JWT educational role недостаточно), organization/context provenance, canonical form/limits, supported profile, mask 1–7 без reserved bits, существование и same-org root/subject membership, полную frozen affiliation, отсутствие ambiguities, expectedVersion/CAS, audit. Read-after-write/cache invalidation не заменяет transaction serialization. Unknown reference не игнорировать и не «создавать placeholder». Invalid persisted запись, обнаруженная при чтении, вызывает conservative deny по её applicable context/subject и диагностическую remediation без содержимого персональных данных. Invalid request на запись отклоняется атомарно, прежняя restriction остаётся.

D06/D07 направления приняты: legacy conservative и database-backed provenance. External manifest не является достаточной provenance для exact admission. Любая schema/migration сейчас запрещена.

## 6. Минимальный durable provenance design — кандидат, DDL NOT APPROVED

Предлагается отдельная immutable DB sidecar `RestrictionSelectorProvenance` с one-to-one ключом RestrictionId и same-org FK к restriction. Логические поля: RestrictionId, OrganizationId, ContextId, OriginVersion; ProfileId=`PRS1`, ContractRevision (точная принятая registry revision); CanonicalResourceRef и Operations как immutable snapshot; DecisionRef, attesting administrative actor, RecordedAt; optional SupersedesRestrictionId для explicit reissue. Это логический design, не существующая таблица/миграция. DecisionRef — ссылка на durable audit/owner-authorized decision, не клиентское утверждение или legal basis.

Exact-capable reader в одном согласованном чтении проверяет уникальность provenance, approved supported revision, same org/context/origin и точное совпадение selector snapshot с restriction. Missing provenance = legacy; mismatch/duplicate/unsupported/deleted reference = invalid provenance; в обоих случаях conservative fallback по context/subject, не skip и не parsing-based adoption. Недоступность DB/невозможность установить даже context/subject → deny всей операции. State/Version restriction остаются отдельными: withdrawal/CAS не переписывают immutable profile/snapshot. Нельзя автоматически дописывать provenance к legacy из распознанного prefix.

Новые restriction + provenance + audit создаются атомарно одной transaction под административной capability. Для narrowing legacy предлагается новая RestrictionId с explicit decision и supersedes link, затем withdrawal старой в той же transaction и с version/CAS. Rollback сохраняет прежний запрет; unknown commit не трактуется как rollback/успех и не порождает слепой повтор. В mixed наборе все active rows объединяются OR: exact nonmatch не отменяет matching legacy prohibition; withdrawn legacy не отменяет новую active restriction. До readiness всех readers сохраняется conservative admission; отсутствие полного affectedSubjects у старого reader нельзя объявить безопасным только потому, что он игнорирует selectors.

Для будущего отдельного schema review остаются: конкретные composite keys/FK/constraints, append-only enforcement/DB roles, trusted audit actor и DecisionRef, isolation/lock order с restriction writes, rollout/cutover и crash/restore consistency. Sidecar не самовыдаёт административные полномочия, не заменяет SR-01–SR-04, current processing check или G02. Никакой migration/DDL не выполняется в R1.

**S4-A3-R1 READY FOR OWNER ACCEPTANCE · PRS1 NOT YET NORMATIVELY ADOPTED · T04 UNCHANGED · E03 UNCHANGED · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**
