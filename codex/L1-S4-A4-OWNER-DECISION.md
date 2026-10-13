# L1/S4-A4 — Owner decision package and implementation slices

2026-10-10. Documentation only; synthetic fixtures only. **EXISTING** — подтверждено исходным кодом; **ACCEPTED** — принятая норма PRS1; **PROPOSED** — решение A4, ещё не принято; **NOT PROVEN** — обязательство будущей проверки, не результат теста.

## Решение, представленное владельцу

**PROPOSED:** принять техническое направление A4: durable sidecar с exact immutable snapshot и trusted decision; запрет поздней аттестации через protected birth ledger; immutable command receipt и atomic reissue; single-org PostgreSQL transaction под существующим organization mutex; source-aware S3 extension и fail-closed неполные projections. Нормативные правила PRS1 уже **ACCEPTED** и повторного изменения не требуют.

Ниже решения **OPEN / PROPOSED**, не утверждение об их принятии. Работа остановлена после шести документов; никакой implementation не начат.

| ID | Новое owner decision | Рекомендация / последствия |
|---|---|---|
| A4-O1 | Physical provenance mechanism | Принять candidate sidecar+decision+command+birth модель [DDL](L1-S4-A4-PROVENANCE-SCHEMA.md); existing immutable roots сохраняются, migration только отдельно |
| A4-O2 | Serialization granularity | Принять org-wide FOR UPDATE / ReadCommitted для synthetic component slice. Простота proof ценой concurrency; per-resource locks не вводить без нового design |
| A4-O3 | Trusted administrative evidence | Принять isolated synthetic writer capability и immutable approved payload/receipt; educational roles недостаточно. Production admin trust/UI пока forbidden |
| A4-O4 | Transaction contract changes | Разрешать позднее internal adapter/Save-in-owned-transaction, потому что existing gate/Save открывают разные transactions. Не менять public HTTP/API |
| A4-O5 | Subject completeness gap G-A | Минимальный scope — closed synthetic manifests; unknown free-form content deny. Альтернатива durable source/subject catalog требует отдельного design; не объявлять author set полным |
| A4-O6 | Message classification gap G-B | Не разрешать unclassified Message read/write; known feedback через reverse association, ordinary только с trusted creation evidence. Альтернатива semantic-source sidecar отдельно |
| A4-O7 | Rollout/rollback participants | Drain/fence incompatible old readers/writers. Rollback к full-subject conservative adapter либо maintenance denial, не слепой old binary rollback |
| A4-O8 | Different ContextId transition | В минимальном slice запретить; future atomic transfer adapter по explicit decision. Same ContextId version switch сохраняет restrictions |
| A4-O9 | Первый implementation slice | Отдельно разрешить только parser/operation registry + pure tests (Slice 1 ниже). Приёмка A4 design сама по себе этого не разрешает |

## Оптимальное разбиение

Гипотеза «provenance persistence + parser первым slice» слишком велика: persistence требует trusted evidence, ACL, atomic receipt и transaction protocol. Рекомендуется маленький independently reviewable parser slice, затем persistence; exact admission не включается ни одним ранним slice.

| Slice | Точный scope, затрагиваемые файлы/слои (будущие) | Prerequisites и инварианты | Acceptance / stop / что запрещено |
|---|---|---|---|
| 1 — strict value layer | Новые `src/Lms.Application/Prs1Selector.cs`, `Prs1OperationPlan.cs`; pure tests `tests/Lms.Application.Tests/Prs1SelectorTests.cs`, `Prs1OperationPlanTests.cs`. Parser/constructor, typed explicit bit registry, shape validation; никаких live resolvers/writers | Отдельное разрешение владельца. Только accepted grammar/masks; parser success не provenance; нет permissive normalization/implicit ScopeAction cast | F01–F06, B01–B07 applicable pure checks/RB03; byte round trips + boundary vectors. STOP если grammar interpretation требует новой нормы. Нет schema/runtime DI/HTTP/exact gate/writer/rollout; persisted evaluation не реализуется |
| 2 — protected persistence | `SchemaFoundations.cs`, `SchemaFoundationMappings.cs`, новая forward migration в `src/Lms.Infrastructure/Migrations/` (имя назначается tooling при реализации), новые `PostgresPrs1Administration.cs`, persistence contracts; `tests/Lms.Infrastructure.Tests/Prs1ProvenanceTests.cs` | A4-O1/O2/O3 и Slice1 accepted; schema review/role model; root+birth+sidecar+receipt+audit атомарны, legacy untouched, no late attach | PV01–03,06–09 persistence obligations, rollback/CAS/ACL tests на disposable synthetic PG. STOP при bypass role, unsupported PG feature, non-atomic audit/receipt. Exact serving OFF; никаких real records/reissue rollout/API |
| 3 — graph и pure evaluator | Новые `Prs1Dependencies.cs`, `Prs1RestrictionEvaluator.cs`; extension/refactor `PostgresScopedAuthorityReader.cs` + новый `PostgresPrs1DependencyReader.cs`, без альтернативного ownership; unit/infrastructure dependency tests | A4-O5/O6, S3 contracts сохранены; trusted synthetic manifests, all attempts/authors/source completeness, conservative OR | RB/RT/N/AF/V/PV applicable cases; corruption doubles и same-org checks; storage spy 0. STOP incomplete evidence or ordinal ambiguity. Attachment component access/lists without proof/Message writes/file bytes запрещены |
| 4 — transaction adapter | Новый `PostgresPrs1UnitOfWork.cs`, internal Save-in-owned-transaction extraction в `PostgresWorkStore.cs`, adapters around `ProblemWorkService.cs`; dedicated transactional processing gate, unchanged old conservative entry point; concurrency tests | Slices2/3; A4-O2/O4/O7/O8; all writers fenced and lock inventory complete. Full plan under org lock, CAS/domain guards/receipt, unknown outcome protocol | CC01–CC13 applicable local DB proofs, no nested tx, S2/S3 regressions. STOP nonparticipant writer, lock inversion, uncertain evidence. Synthetic direct component calls only, no HTTP/storage/jobs/public DI |
| 5 — synthetic component acceptance | Test harness/fixtures in `tests/Lms.Infrastructure.Tests/Prs1ComponentTests.cs`, fault infrastructure and documentation results; precise supported projection descriptors | All previous slices accepted; separate authorization; human review/SR gates tracked, exact still feature OFF outside isolated harness | Все 29 R1 and base-case obligations либо честно scoped NOT IMPLEMENTED для forbidden integrations; no claim full PRS1 release при отсутствующих positive cases. Rollback/drain/ack-loss drill. STOP any unknown proof. Production rollout/files/S4 HTTP/S4-B/S5–S8 по-прежнему запрещены |

Paths новых файлов — проектные имена, не существующие файлы. Не создавать migration вместе со Slice1. Owner принимает каждый slice отдельно. Attachment/aggregate/ordinary Message positive integrations могут потребовать отдельного разрешённого последующего slice после решения G-A/G-B; текущие minimum denials не объявляются полной поддержкой PRS1.

## Риски и принципиальные ограничения

| Риск | Оценка / mitigation |
|---|---|
| Неполный subject/source graph даёт false allow | Высокий, blocking. Closed synthetic fixtures/manifest, unknown deny; не выводить third parties из authors |
| Writer обходит org lock | Высокий, blocking. Инвентаризация ролей/paths, DML fencing, deterministic concurrency tests; trigger после row update не замена правильному entry lock |
| Forged provenance/late legacy attestation | Высокий. Birth + immutable decision + pair receipt + ACL; parser/prefix не evidence; DBA trust отдельно |
| Unknown commit/retry duplicate | Высокий. Stable command ID, immutable payload/receipt, authoritative reconciliation; no automatic compensation |
| Org mutex уменьшает throughput / deadlocks | Средний для synthetic slice, workload NOT PROVEN. Короткие DB-only transactions, canonical order, no file/network work; future optimization отдельно |
| Clock/key trust, external completion, restore continuity | Blocking для release. SR-01–SR-04/human audit и G02 остаются открыты; локальная commit order их не решает |

## Сохранность и фактическая проверка A4

Рабочий каталог `/home/tea/github/lms`; существующая ветка `codex/l1-s4-jwt-api-authorization`; baseline HEAD `492e30aef65bbddd24f107903bfaa54a563abe39`. Baseline hashes/branch/index записаны в `/tmp/lms-s4-a4/baseline.json`; документационная verification — `/tmp/lms-s4-a4/validation.json`. В scope ровно шесть `codex/L1-S4-A4-*.md`. Accepted A3/R1 документы, checkpoints, runtime/tests/migrations/API/frontend и чужие dirty files не редактируются; `.obsidian/workspace.json` не восстанавливается при внешнем изменении. Документационные проверки: шесть файлов, все 29 R1 IDs ровно по одному, локальные Markdown links и whitespace без ошибок; 372 baseline файла совпадают по SHA-256, включая существующие чужие изменения. HEAD/ветка/index сохранены. Проверка повторяется после копирования пакета в canonical repository; итоговый machine-readable отчёт указан выше.

Runtime tests, SQL execution, migration, rollout, commit, push, merge, rebase и deploy не выполнялись. DDL — кандидат на review, не результат запуска PostgreSQL. Все future acceptance tests NOT RUN. Следующий рекомендуемый шаг — owner decision по A4 и отдельное ограниченное разрешение Slice1; до него implementation запрещён.


**S4-A4 READY FOR OWNER DECISION · PRS1 ACCEPTED · IMPLEMENTATION NOT AUTHORIZED · T04/E03 UNCHANGED · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**
