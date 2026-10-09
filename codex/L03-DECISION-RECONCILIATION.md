# L0.3 — Owner decisions reconciliation

2026-10-09. Verified base `ee39d38d500ef6e820429827809dd1a70a4797ab`, source branch `codex/l03-owner-decisions-l1-readiness`, clean tree. Owner/evidence: явное задание консолидации L0.3 §4, T01–T06 ACCEPTED, T04 окончательная редакция. Приоритет: это решение → [owner register](L03-OWNER-DECISION-REGISTER.md)/актуальные ADR/планы → исторические L0–L0.2 checkpoints. Документальное согласование не runtime evidence.

## Source → прежнее положение → конфликт → приоритет/изменение

| Документ / конкретное положение базовой редакции | Противоречие | Решение и изменённые документы |
|---|---|---|
| L03 readiness §B; L-DECISION-REGISTER T01–T06; L01/L02 gates | Все T оставались PROPOSED, выбор ещё требовался | ACCEPTED по явному owner request; новый полный register, readiness и gates. E01–E06 отдельно OPEN |
| L03 plan §6, ADR-L02 security paragraph; readiness T01 | 26 символов/128-bit code | T01 имеет приоритет: 16 Crockford Base32/80 бит, accepted rates/TTLs/HMAC/passwords; plan/ADR-L02/register |
| ADR-L01 transport/session; L-ARCHITECTURE flow; L register B01/B03; UI L0.3 paragraphs | Cookie-only, DB session/grants на каждый HTTP и old session TTL | T04 имеет приоритет: JWT ≤15m, local checks, DB issue/refresh; ADR-L01 rewritten, ADR-L04 keys уточнён, architecture/register/UI/plan обновлены; refresh TTL E02 OPEN |
| L03 plan §§2/6/10; ORG-G04 в L02/register; isolation revoke paragraph; ADR-L14 | Immediate commit-time PG barrier, Revoking/drain, stream connection lease | Окончательный T04 заменяет ordinary revoke: scoped deadline ≤2h, exp formula/10m reserve, 60s/8m operations. ADR-L15 принят, ORG-G04 явно изменён; isolation/identity/context/architecture/sequential plan/feature/UI/gates согласованы |
| Старые tests X03/X04 и old URL deny matrix | Немедленный запрет любых действий после acknowledgement заявки | Приём заявки фиксирует deadline, не обещает immediate invalidation; plan/ADR-L15 дают race/time-bound tests. Уже выданный до заявки JWT ≤15m + 8m completion укладывается в 2h |
| L03 plan T04 с multi-org identity; JWT содержит несколько scopes | Единый minimum exp мог связать A/B; composition не утверждался | Не объявлять выбор принятым: E01 PROPOSED org-scoped issuance, fresh B независимо от A; affected S2–S4 blocked до решения |
| Identity scopes request freshness; ProcessingContext block/barrier paragraph | T05 current restrictions нельзя молча задержать ordinary revoke политикой | E06 OPEN, proposal отдельного restrictions check; T05 сохранён. ADR-L09/L10 destructive erasure/backup maintenance barrier не ordinary authorization и не отменён |
| ADR-L02/UI T02, L03 plan frontend | Минимальная форма либо CLI, интерфейс OPEN | T02: minimal org-admin UI ACCEPTED; bootstrap platform operator; shared reset independent confirmation; concrete verifier E04 OPEN, legal representative procedure PD-G03 OPEN |
| ADR-L11–L14, architecture models, domain amendment | Все physical records/persistence PROPOSED | T03/T05 core tables и invariants ACCEPTED; exact DDL/index/trigger/ports review остаётся engineering, no automatic acceptance всех старых деталей |
| L03 plan §4 / L02 ORG-G07 | Migration sequence PROPOSED; real manifest branch мог читаться как scope L1 | T06 M1–M3 ACCEPTED, L1 only empty/synthetic; real/unknown auto-backfill forbidden; tested restore и controlled cutover, runtime M3 после S2–S7 |
| Domain/feature/UI contracts | Не описаны новые JWT long-operation/temporary photo semantics | Additive amendments: separate photo requests, temporary file, valid JWT on Submit, atomic scoped claim, bounded download/jobs; state machine/immutable history/шесть экранов сохранены |
| ADR-L03–L10, PD-G01–14, ORG-G05/08 | Риск принять T решения за compliance/production release | Legal statuses не повышены; infrastructure/device/backup evidence не объявлен пройденным; ADR-L06/07 auth terminology уточнено, maintenance barriers сохранены |
| EXECPLAN L0–L0.2 sections, L01/L02 results | Старые proposals могли читаться как текущая норма | Исторические reports/evidence сохранены; текущий L0.3 checkpoint ссылается на новый register/readiness/task, прежний L0.3 доступен в base commit |

| PD security controls C01/C03, threat T02/T10, data-flow F01/F02 | Server sessions/per-request stamp/cookie как target | JWT issue/refresh и bounded revoke; threat T IDs явно отделены от owner T IDs; legal statuses сохранены |
| examples/course/lessons/01-divisibility/lesson.md — Материалы | Относительная ссылка на отсутствующий worksheet.pdf | Заменена честным текстовым placeholder; бинарный файл не выдуман |

## Review coverage

Сверены ADR-L01–L14, L0.1/L0.2 reports и PD/ORG gates, architecture/compliance models, domain/feature/UI, L plans/register/checkpoint. Реестр отмечает все новые решения как OPEN/PROPOSED, а не ACCEPTED. Scope grants, ownership, groups, counts/raw SQL и files проверяются совместно; stable membership FK + Generation не оживляет grants, constraints не требуют переписывания immutable events. Нет нового LearningSpace, blanket admin reader или владельца unknown data.

Подготовлен отдельный [Codex L1 execution task](CODEX-TASK-L1-EXECUTION.md), не выполнен. Документационная validation — [results](L03-RESULTS.md); будущие SQL/crypto/concurrency/timeout/restore tests NOT RUN. Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY. LEGAL SOURCE NOT VERIFIED.
