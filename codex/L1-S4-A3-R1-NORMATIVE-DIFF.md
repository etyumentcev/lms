# L1/S4-A3-R1 — Итоговый нормативный diff для приёмки PRS1

> **Актуальный owner decision, 2026-10-10: PRS1 NORMATIVE CONTRACT ACCEPTED.** [Решение и границы приёмки](L1-S4-A3-OWNER-DECISION.md). Сверка contract §1–6, matrix и normative diff выполнена до фиксации; смысловых расхождений нет. Прежние статусы proposed / NOT ADOPTED / READY FOR OWNER ACCEPTANCE ниже — исторические статусы подготовки и superseded в принятом scope. Нормативные правила и acceptance oracles сохранены; cases остаются **NOT IMPLEMENTED / NOT RUN**. §6 sidecar — кандидат, механизм provenance требует отдельной приёмки; DDL, administrative writer, lock protocol, rollout/reissue/migration и implementation не разрешены. SR-01–SR-04 — integration gates; human audit OPEN.

**PRS1 NORMATIVE CONTRACT ACCEPTED · EXACT IMPLEMENTATION NOT AUTHORIZED · T04 UNCHANGED · E03 UNCHANGED · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**

## Редакция R1, представленная на приёмку (сохранена без изменения)

2026-10-10. **PROPOSED FINAL TEXT — PRS1 NOT YET NORMATIVELY ADOPTED.** Это diff документационного проекта, не изменение действующих T04/E03/ADR и не разрешение реализации.

[Contract](L1-S4-A3-SELECTOR-CONTRACT.md) · [Matrix](L1-S4-A3-SELECTOR-ACCEPTANCE-MATRIX.md) · [Owner decision](L1-S4-A3-OWNER-DECISION.md).

| Область          | Исходный A3                                                    | R1 для окончательной приёмки                                                                                                                                                                         |
| ---------------- | -------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Статус           | D01–D09 OPEN                                                   | Core direction accepted with changes; пакет/AI review accepted; окончательная PRS1 adoption ещё отсутствует                                                                                          |
| D01              | Альтернативные ref/storage направления                         | Versioned canonical same-org direction принят; точные grammar/4096-byte ref/1024-byte ID детали представлены на окончательную приёмку                                                                |
| D02              | Transfer=8, Lifecycle=16, plans 9/25                           | Имена операций не назначаются. 8/16 ReservedDeny. Новые masks только 1–7; required reserved plans deny. Persisted reserved rows conservative по context/subject без resource narrowing               |
| D03 попытки      | Общее subtree                                                  | R1 навсегда принадлежит S1; S2/R2 sibling ветвь того же work. Не resolver через latest. Work restriction охватывает обе ветви                                                                        |
| Embedded content | Общая проверка детей                                           | Отдельные source/subject gates для feedback через Message, annotation+file, derived statuses/timestamps/counts; unsafe projection deny                                                               |
| Attachment       | Неуточнённый parent, примеры могли подразумевать Review parent | Physical parent ровно Submission XOR Message; прямого Review parent нет. Annotation reference не меняет parent. Pending не даёт downstream permission; attach recheck; файловая интеграция запрещена |
| D04/D05          | Предложения с альтернативами                                   | Full affectedSubjects и prefilter приняты как направления; неполнота/недоказанный filter → deny                                                                                                      |
| D06/D07          | Не выбран durable provenance mechanism, возможен manifest      | Legacy conservative принят. DB-backed направление принято; минимальный sidecar candidate с immutable selector snapshot, profile revision и decision evidence; конкретная DDL не утверждена           |
| Совместимость    | Prefix недостаточен                                            | Missing/mismatch/unsupported provenance → conservative; mixed legacy/PRS1 OR; old-reader subject incompleteness блокирует rollout; atomic explicit reissue без allow gap                             |
| D08/D09          | Ожидание owner                                                 | AI review LIMITED ASSURANCE принят; human audit OPEN; SR-01–SR-04 gates сохранены                                                                                                                    |

## Проверенные доменные факты

`ProblemWork.SubmitSolution` создаёт новый Submission для каждой попытки; `ReviewCurrent` допускает review только latest/unreviewed submission. `History.Review` хранит SubmissionId и ProblemWorkId. `PostgresScopedAuthorityReader` сверяет связь review→submission→work. После NeedsRevision возможна следующая попытка; Accepted закрывает отправку. Старые связи не переписываются.

`StoredAttachment` имеет SubmissionId/MessageId, без ReviewId. Review annotations ссылаются на файлы его submission; feedback — отдельный Message, создаваемый без attachment IDs. Поэтому обратная связь Review.MessageId нужна будущей safe projection, чтобы запрет review не обходился выдачей того же feedback через Messages. R1 описывает эту зависимость, не заявляет её существующее PRS1 enforcement.

Источники: [ProblemWork](../src/Lms.Domain/ProblemWork.cs), [History](../src/Lms.Domain/History.cs), [Annotations](../src/Lms.Domain/Annotations.cs), [StoredAttachment](../src/Lms.Infrastructure/StoredAttachment.cs), [resolver](../src/Lms.Infrastructure/PostgresScopedAuthorityReader.cs). Метод — чтение кода; новые runtime tests не выполнялись.

## Окончательная приёмка и отдельные зависимости

Владельцу представлен согласованный текст contract §1–6 и matrix, включая RB01–RB03, RT01–RT05, N01–N05, AF01–AF06, PV01–PV10. Принятие этих правил ещё не разрешает их реализацию. Sidecar представлен на уровне logical fields/invariants; будущие DDL, constraint enforcement, trusted administrative writer, transaction lock order, rollout/crash recovery — отдельный design/review. Transfer/Lifecycle остаются неопределёнными; file/storage/HTTP integration и production clock/KMS не разрешены. Human audit и SR-01–SR-04 не закрыты.

Действующий runtime остаётся conservative, никакого automatic narrowing legacy. T04/E03 не меняются; G02 BLOCKED. Все новые acceptance cases PROPOSED / NOT IMPLEMENTED / NOT RUN.

**S4-A3-R1 READY FOR OWNER ACCEPTANCE · PRS1 NOT YET NORMATIVELY ADOPTED · T04 UNCHANGED · E03 UNCHANGED · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**
