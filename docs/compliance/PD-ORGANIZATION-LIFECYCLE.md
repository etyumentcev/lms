# L0.2 — Organization lifecycle / erasure

**PROPOSED / REQUIRE LEGAL REVIEW. ORG-G06, PD-G07/08/09 OPEN.** C(E) — оператор соответствующей учебной цели, P — назначенный исполнитель ООО. Для собственных курсов решение принимает P в образовательном контексте. Все workflows ниже ещё не реализованы.

| Сценарий / кто решает | Исполнитель | Объём и блокирование | Сохранить / уничтожить | Backups / evidence |
|---|---|---|---|---|
| Ученик → C(E), проверка личности | P по case | Установленные цели/организации, disputed subset block | Только самостоятельно законный retained subset; прочее destroy | Context ledger, manifest копий, ответ/акт; B не затрагивать |
| Родитель → C(E), проверка полномочий | P | Данные ребёнка в verified scope; данные родителя отдельно | Учесть права других представителей/teacher | Минимальное evidence проверки, case/deadline/copies |
| Поручение A → уполномоченный A | P с проверкой поручения | Только Organization A/purpose/resources | Не B и не самостоятельная security цель P; hold на review | Распоряжение, dry-run, completion evidence |
| Прекращение договора A↔P → A по учебным целям, P по своим | P/подрядчики | Stop A, revoke grants, protected return/export | Отдельный lawful evidence subset P; остальное уничтожить в срок | Export receipt, deletion всех копий/ledger |
| Прекращение обучения → C(E) | P | Enrollment E revoke, case по E | Не автоматическое стирание: проверить конечный lawful archive, block обычного доступа где требуется | Retention trigger, scoped manifest; другая организация неизменна |
| Закрытие собственного направления B → P как C(B) | Назначенный P executor | Membership/курсы B; platform продолжает работать | Обоснованные obligations/claims subset; остальное destroy | Решение B, copies, проверка сохранности A |
| Отзыв teacher → manager E | Issuer/access service | Membership или course/group revoke, barrier | Не удалять автоматически feedback/работы учеников; teacher DSAR отдельно | Audit, old URL deny, restore не возвращает grant |
| Организация перестала пользоваться LMS без формального расторжения → C(E)/legal P | P | Suspend scope, выяснить instructions/return terms | Пересмотр целей/holds; отсутствие расторжения не вечное основание | Case/escalation/deadline, copies и return/destroy |
| Глобальное закрытие account → субъект, решение по каждой цели | Identity executor и отдельные C(E) | Credentials/sessions revoke, context routing | Не каскадить IdentityUser→вся history; retained authors отдельно | Ответы A/B/platform и evidence каждого store |

Во всех строках backup subset наследует исходные основания/сроки. До законного уничтожения копии доступ ограничен; техническая невозможность не blanket отсрочка. Конкретные legal deadlines и retained subset утверждаются по case. Обращения доступны без account; parent login не заменяет представительство.

Case содержит OrganizationId либо явную platform purpose, ContextId/version, subject/representative verification, initiator authority, rule/deadline, decision owner, executor/reviewer. Manifest фиксирует exact resources, зависимости, retained subset/основание/expiry, действия по stores, digest/schema version. Чужая organization в closure означает остановку для разбора, не разрешение удалить её данные. Межоператорные запросы разбиваются на отдельные manifests.

Обычные commands сохраняют immutable Submission/Review/Message/annotations, Accepted terminal и optimistic concurrency. Privileged erasure — отдельная временная capability по manifest, app role не умеет bypass triggers. Block→drain→dry-run→approval→DB/files→verification→response по [lifecycle](PD-LIFECYCLE-AND-ERASURE.md). Частичный сбой оставляет block/intent/retry, не COMPLETE. Tombstone/удаление account не доказывают уничтожение.

Mixed-subject feedback/фото/цитаты и shared content требуют объектного разбора: lawful retention, destroy либо отдельно reviewed минимизация. Нельзя удалить общий course snapshot и сломать чужие работы. Hold не разрешает обычное обучение или бессрочную аналитику.

## Общие backups

Физический set A+B+platform не объединяет contexts. Manifest указывает состав, policy versions и ledger cutoff. Срок A нельзя продлить до срока B. До lawful deadline удалить set либо создать проверенную очищенную согласованную копию B и уничтожить старые replicas с доказательством recoverability B. Общий ключ нельзя уничтожать без оценки потери B. Per-organization keys — возможное расширение, не доказательство crypto-erasure.

Проверить WAL/snapshots/temp/exports/providers. Immutable lock без законного срока блокирует выбор. Restore в quarantine получает актуальный независимый ledger всех contexts, применяет block/revoke/erase, отзывает старые sessions и сверяет хвост под barrier до ingress. Недоступный ledger любого восстановленного context блокирует cutover. Ops audit/поручение ограничивают технический доступ к общему set. Synthetic drill: snapshot до erasure A/revoke teacher, A не воскресла, B сохранена и доступна; сейчас NOT VERIFIED.
