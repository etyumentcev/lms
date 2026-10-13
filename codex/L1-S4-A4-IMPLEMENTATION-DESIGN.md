# L1/S4-A4 — Implementation design

2026-10-10. Documentation only; synthetic fixtures only. **EXISTING** — подтверждено исходным кодом; **ACCEPTED** — принятая норма PRS1; **PROPOSED** — решение A4, ещё не принято; **NOT PROVEN** — обязательство будущей проверки, не результат теста.

## Основание и граница

**ACCEPTED:** [contract §1–6](L1-S4-A3-SELECTOR-CONTRACT.md), [R1 matrix](L1-S4-A3-SELECTOR-ACCEPTANCE-MATRIX.md), [normative diff](L1-S4-A3-R1-NORMATIVE-DIFF.md), [owner acceptance](L1-S4-A3-OWNER-DECISION.md), [T04/E03/E06 register](L03-OWNER-DECISION-REGISTER.md). A4 не меняет ни одну норму. Предыдущий запрет на проектирование DDL superseded только явным разрешением A4 документации; выполнение DDL/миграции не разрешено.

**PROPOSED:** один внутренний PostgreSQL unit of work: trusted operation plan → S3 ownership + dependency/subject closure → current binding → provenance classification → OR prohibitions → domain mutation/CAS → receipt/audit → commit. Exact policy — дополнительное необходимое условие, не источник educational authority. До доказательства всех зависимостей работает conservative denial; HTTP, files, jobs и arbitrary Message writes не подключаются.

Пакет: [schema](L1-S4-A4-PROVENANCE-SCHEMA.md), [transactions](L1-S4-A4-TRANSACTION-PROTOCOL.md), [rollout](L1-S4-A4-ROLLOUT-PLAN.md), [verification](L1-S4-A4-ACCEPTANCE-MATRIX.md), [owner decisions/slices](L1-S4-A4-OWNER-DECISION.md).

## Реестр проверенных фактов

| ID | EXISTING: источник и точный символ | Следствие для проекта |
|---|---|---|
| E1 | [SchemaFoundations.cs](../src/Lms.Infrastructure/SchemaFoundations.cs), `ProcessingRestriction`, `ProcessingContextVersion`, `CourseContextBinding`; [SchemaFoundationMappings.cs](../src/Lms.Infrastructure/SchemaFoundationMappings.cs), `CK_Restriction_State` | Restriction: GUID ID/context/org/nullable subject, bigint OriginVersion/Version, nullable text ResourceRef, integer Operations 1–31, Active/Withdrawn. Profile отсутствует |
| E2 | [SchemaFoundationsM1.Up](../src/Lms.Infrastructure/Migrations/20261009094756_SchemaFoundationsM1.cs), `s1_restriction_provenance`, `s1_stable_key`, `s1_binding_history`, `EX_CourseBinding_NoOverlap` | Selector/subject/context immutable; binding можно только закрыть. Нет sidecar-pairing, запрета поздней аттестации или monotone restriction state/version в этом trigger |
| E3 | [PostgresCurrentProcessingGate.Allows](../src/Lms.Infrastructure/PostgresCurrentProcessingGate.cs) | Dedicated clean context, собственная RepeatableRead transaction; отказ при уже открытой transaction. Subject check только caller/student; opaque bits/ref игнорируются conservative. Нельзя вызвать внутри новой write transaction без нового adapter |
| E4 | [ComponentAuthorization.Allows](../src/Lms.Application/ComponentAuthorization.cs) | S3 ownership/ScopePolicy + processing + clock; Attachment deny безусловен; bool не связывает check с Save |
| E5 | [PostgresScopedAuthorityReader](../src/Lms.Infrastructure/PostgresScopedAuthorityReader.cs), `Snapshot`, `Course`, `Assignment`, `Work`, `Submission`, `Resource`, `Attachment`, `CanAccess` | При внешней transaction использует её; frozen AssignmentScope, review→конкретная submission с WorkId check; attachment resolver предпочитает Submission ветку, full author closure отсутствует |
| E6 | [ScopedAuthority.cs](../src/Lms.Application/ScopedAuthority.cs), `ResourceOwnership`, `ScopePolicy.Allows`, `ScopeAction` | ResourceOwnership не содержит Review.TeacherId/Message.AuthorId/полноту subjects. Ordinals Read/Submit/Review=0/1/2 не PRS1 mask |
| E7 | [PostgresIdentitySessions](../src/Lms.Infrastructure/PostgresIdentitySessions.cs), `LockAccount`, `LockSession`, `WithIssuingSession`; [PostgresComponentTokenIssuer.Issue](../src/Lms.Infrastructure/PostgresComponentTokenIssuer.cs) | Security→user→identity mapping→session→organization; signed result выходит после commit. PRS1 не должен инвертировать порядок |
| E8 | [PostgresScopedAuthorityAdministration.Change](../src/Lms.Infrastructure/PostgresScopedAuthorityAdministration.cs); [ScopedAuthorityS3.Up](../src/Lms.Infrastructure/Migrations/20261009153218_ScopedAuthorityS3.cs), `s3_change_authority`, `s3_capture_assignment_scope` | Organization FOR UPDATE перед membership/enrollment/grant; move меняет generation и сохраняет assignment affiliation |
| E9 | [ProblemWorkService.SubmitSolution/Review/PostMessage](../src/Lms.Application/ProblemWorkService.cs); [PostgresWorkStore.Load/Save](../src/Lms.Infrastructure/PostgresWorkStore.cs) | Load может join transaction, Save всегда начинает свою. Authorization/domain calls предшествуют Save; версия work защищает lost update, не restriction race |
| E10 | [ProblemWork.SubmitSolution/ReviewCurrent/PostMessage](../src/Lms.Domain/ProblemWork.cs); [History.cs](../src/Lms.Domain/History.cs), `Submission`, `Review`, `Message` | Каждая попытка имеет GUID; latest/unreviewed domain guards. Feedback создаётся Message без attachments; MessageId nullable, отдельного feedback-kind нет |
| E11 | [InitialPersistence.Up](../src/Lms.Infrastructure/Migrations/20261007035847_InitialPersistence.cs), composite review→submission/work и review→message/work FK, unique SubmissionId, immutable history triggers | Wrong-work/missing parent обычно блокируется DB; negative resolver fixtures моделируют corruption. Reverse MessageId index не unique; отсутствие reverse link не доказывает ordinary Message |
| E12 | [StoredAttachment](../src/Lms.Infrastructure/StoredAttachment.cs); [AttachmentStorage.Up](../src/Lms.Infrastructure/Migrations/20261008013249_AttachmentStorage.cs), `CK_StoredAttachment_Link`, `claim_submission_attachments`, `claim_message_attachments`, `protect_stored_attachment` | DB уже запрещает оба parent, claim проверяет owner и прежнюю привязку для найденного StoredAttachment (отсутствующую запись пропускает), attached record защищён от изменения. Сохранить это, добавить defensive resolver checks |
| E13 | [AnnotationRules.Copy](../src/Lms.Domain/Annotations.cs); [ReviewAnnotations.Up](../src/Lms.Infrastructure/Migrations/20261008154119_ReviewAnnotations.cs) | Annotation ID обязан принадлежать reviewed submission; annotations хранятся в Reviews. Metadata parsing не разрешает storage |

## Компонентные контракты — PROPOSED

Это сигнатуры проектирования, не добавленные интерфейсы. Все результаты immutable; тип `Unknown` не преобразуется в `Allow`.

| Контракт | Вход → результат; обязательства |
|---|---|
| `IPrs1Parser.Parse(raw)` | `CanonicalSelector | Invalid(reason)`; строгий ASCII 4096 bytes, UUID lowercase/nonzero, sid strict UTF-8 ≤1024 bytes/nonempty/canonical unpadded base64url; round trip bytes, без trim/NFC/case-fold/percent decode/I/O. `Build(trusted IDs)` отдельно |
| `IOperationPlanner.Build(command, projection)` | `CompletePlan | Unsupported`; набор contributions: resource identity, required mask, read/write effect, source IDs, output field, subject evidence. Registry 1/2/4; 3/5 compound. Не общий bool из ScopeAction |
| `IPrs1DependencyResolver.Resolve(transaction, plan, actor)` | S3 `ResolveResource` для каждого ownership root + immutable graph identities, frozen affiliation и `CompleteSubjects` для каждой contribution. Обязательно transaction-scoped, без client-provided completeness flag |
| `IProvenanceVerifier.Classify(row, sidecar, evidence, birth)` | `VerifiedPrs1(revision) | Conservative(reason) | Unavailable`; equality всех immutable полей, decision/command/birth proofs; profile allowlist pinned в binary. Нет parser-based adoption |
| `ICurrentRestrictionEvaluator.Evaluate(binding, rows, graph, plan)` | Pure `Deny(reason) | SelectorPass`; всё incomplete/unsupported → deny. Legacy применяет context+subject, verified PRS1 ещё closure+bit intersection; active rows OR |
| `IPostgresPrs1UnitOfWork.Execute(commandId, token, command)` | Владеет fresh context/transaction/locks, перечитывает все входы, не принимает старый bool admission. Возвращает committed result, denied/conflict либо indeterminate; внешних effects нет |

`CompleteSubjects` конструируется только resolver из поддерживаемого projection descriptor; содержит actor IDs с source/evidence IDs и перечень полностью обследованных источников. Размер/глубина graph ограничены; превышение/цикл/неизвестный field deny, не truncation. Один known source с несколькими subjects — одна неделимая contribution. Caller включается в каждый plan; root owner, SubmittedBy, TeacherId, AuthorId, UploaderActorId и subjects всех вложенных источников добавляются до gate.

## Closure и operation plans

| Ресурс/представление | ACCEPTED closure и PROPOSED способ проверки |
|---|---|
| Course / Group | all текущего ContextId→course→group; S3 CourseRegistry/CourseGroups, ordinal stored IDs; null group не wildcard |
| Assignment / Work | all→course→frozen assignment group, если есть→assignment→work; исключительно S3 affiliation. Enrollment move не меняет group старой assignment |
| Submission | closure work→конкретный Submission.Id; прочитать immutable SubmittedBy и reciprocal work; каждую историческую попытку независимо |
| Review | closure именно Review.SubmissionId→Review.Id; проверить WorkId с parent. Для новой mutation сохранить latest/unreviewed domain rules |
| Message feedback | Внутренний source, не новый selector kind. Reverse lookup всех Reviews с MessageId, проверить work и submission; ровно один feedback owner при доказанной классификации. Ordinary Message → work; ReplyTo content отдельно рекурсивно gate, не physical reparent |
| Embedded / derived | Каждый embedded child отдельно. AttemptCount, status, timestamps, summary и order key требуют всех исходных contributions. Для минимального adapter unsafe full aggregate → deny целиком; не выдумывать статус после удаления children |
| Annotation | Review source AND referenced attachment source, parent submission должна быть review.SubmissionId; нет bytes/thumbnail rendering |
| Attached metadata | Ровно Submission XOR Message, reciprocal AttachmentIds, owner/uploader/author, scope/context/course/org и eligibility. Оба parent→deny даже если SQL constraint должен предотвращать |
| Pending metadata | Оба parent null; all→verified course→attachment, uploader и trusted scope; eligible, unexpired, nonquarantine/nondeleted. Не разрешает destination. Attach/finalize — будущий полный recheck, сейчас unsupported |

Foreign org, missing root, удалённый/ambiguous parent, несовместимые hints, недоказанная ordinal equality → deny. Target restriction, который нельзя достоверно разрешить, не становится exact nonmatch: classify conservative; если невозможно установить context/subject — deny whole operation.

Submit plan: изменения work/new submission и journal; возврат `SubmissionResult` включает derived state/attempt, поэтому минимум mask 3 и source dependencies исторических attempts. Review plan: read submission + create review/feedback + work state/journal/result, mask 5; affectedSubjects включает student и teacher. Если reading domain history влияет на gate/output, его sources включаются; server ownership metadata lookup сам по себе не пользовательский Read. New IDs резервируются до plan внутри transaction; future children получают ancestor closure. Attachment IDs/annotations в минимальном mutation adapter отклоняются до загрузки content.

List/queue: план candidate identities + complete subjects/source graph строится до content materialization, pagination/count/order/facets. SQL prefilter допускается только с equivalence proof pure evaluator. Минимальный slice lists/aggregates отклоняет целиком; не подключать `PostgresReviewQueue` без отдельного adapter. Caller-wide/all restriction → whole request deny. Standalone Message writes пока unsupported: не назначать им новое нормативное действие самостоятельно.

## Model gaps и безопасные альтернативы

**NOT PROVEN G-A:** свободный Markdown не содержит registry всех упомянутых третьих лиц. Authors/owner ≠ доказанное полное affectedSubjects. Рекомендация владельцу: минимальный slice только явно закрытые synthetic fixtures с immutable server-authored dependency manifest; неизвестный/free-form production content deny. Альтернатива будущего owner design — durable subject/source catalog, валидация trusted writers и миграционная аттестация; не эвристический NLP/caller-only fallback.

**NOT PROVEN G-B:** nullable Review.MessageId + отсутствие Message kind не позволяют отличить orphan feedback от ordinary message по отсутствию reverse row. Рекомендация: deny unclassified Message representations; разрешать feedback только с доказанной association и ordinary synthetic messages только с доверенным creation manifest. Альтернатива — новый durable semantic-source sidecar, принимаемый отдельно; существующую историю не переписывать.

**NOT PROVEN G-C:** E3/E9 требуют новых internal transaction contracts. Существующие interfaces нельзя просто склеить: вложенный Save transaction и самостоятельный gate несовместимы. Рекомендация — internal adapter + extraction Save-in-owned-transaction без изменения public HTTP/API; старые callers остаются conservative и не получают новый маршрут.

Это не пересмотр PRS1: при неопределённости deny уже ACCEPTED. Owner выбирает допустимый implementation scope и механизм evidence. SR-01–SR-04 и external human audit остаются integration gates; локальная serial order не закрывает delivery/completion/key/clock G02.


**S4-A4 READY FOR OWNER DECISION · PRS1 ACCEPTED · IMPLEMENTATION NOT AUTHORIZED · T04/E03 UNCHANGED · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**
