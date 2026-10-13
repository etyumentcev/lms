# ADR-L18 — ProcessingRestrictions в SyntheticOnly L1

> **2026-10-12 — bounded teacher revocation.** [Действующий scoped contract](../architecture/TEACHER-REVOCATION.md): fresh issue/refresh, JWT≤15m, immutable operation≤30m, revoke≤60m в выбранном bounded execution envelope. Для ordinary revoke двухчасовой D и D−10m superseded; неограниченный late COMMIT/egress не удовлетворяет новому контракту. История ниже сохранена. Processing/file safety, PRS1 и release gates не сняты; общий HTTP/physical bound NOT PROVEN.

> **MVP photo storage override — 2026-10-12.** [Owner-authorized exact diff](../../codex/MVP-STORAGE-RISK-ACCEPTANCE.md): physical original tampering risk accepted; fs-verity/kernel seal, retained FD/mmap exclusion proofs and developer filesystem qualification removed only for Sapientum solution photos. Completed private upload, parent/S2/S3/current processing/deadline and all other NR1 guarantees remain required. STORAGE-PROFILE-02 CANCELLED; G02 production BLOCKED. Historical clauses/results below are preserved.


> **NR1 effective notice — 2026-10-11.** D7 заменяет temporal dependency абзаца «Files проходят тот же gate…» на N5 только в NR1 scope: application deadline и возможный queued transport tail. Current restrictions, write serialization, pre-materialization checks и jobs fresh checks сохраняются. Реальная freshness/legal acceptance не предоставлена.
> Нормативный источник: [N1–N7 / точный D1–D8](../../codex/MVP-B01-G02-NORMATIVE-REVIEW.md), [принятое решение](../../codex/L03-OWNER-DECISION-REGISTER.md), [sync results](../../codex/MVP-B01-G02-NR1-SYNC-RESULTS.md). Только controlled SyntheticOnly и отдельно принимаемые routes; неопределённый scope → blocked. Credential issuance, grants, B08 publication, general jobs и внешние irreversible effects не получают автоматического исключения. CLOCK NOT PROVEN; технически NR1 NOT IMPLEMENTED / NOT ACCEPTED.
> Исторический текст ниже сохранён; старые failure results не превращены в PASS.

2026-10-09. **ACCEPTED E06**, совместно T05/ADR-L13, owner L0.4 §4. Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY. LEGAL SOURCE NOT VERIFIED.

ProcessingContextVersions immutable, Organization-bound; CourseContextBindings задаёт непересекающиеся периоды same-org context. Historical operation→version/time сохраняется sidecar/audit без изменения work events. ProcessingRestrictions — current prohibition records с origin-version provenance, operation/subject/resource scope и state/version; priority над любой historical allow. Client не определяет resource/context ownership.

Минимальный L1 engineering enforcement: environment только SyntheticOnly, controlled synthetic fixtures/provenance; claim/header не включает real mode. Из resource root разрешить applicable course binding/context/same org/period/synthetic marker; missing/ambiguous/expired required context→deny. Читать текущие restrictions PostgreSQL при operation check, без positive cross-request allow cache. Это отдельный processing gate, не DB freshness обычных JWT grants. Обязательная processing DB недоступна→fail closed; local JWT signature check независим от DB.

Reads проверяются до materialization/выдачи; queue/count filters до aggregation. Writes повторно проверяют restrictions/context в transaction перед irreversible mutation; synthetic restriction-update/write decision сериализуются targeted version/CAS/lock. Старый snapshot требует recheck/deny, не принятия historical allow. Это не global ordinary revoke barrier. Audit хранит current decision refs/historical applicable version, не fake legal basis.

Files проходят тот же gate. Long stream после admission не обещает мгновенного прерывания при newly committed restriction; bounded duration ADR-L15 остаётся, это synthetic-only предел. Jobs fresh context/restriction check перед irreversible step и delivery; initial JWT не perpetual permission. **До real pilot отдельное решение freshness/propagation/in-flight semantics и legal evidence**; обычные два часа нельзя перенести на юридические запреты.

Отсутствие подтверждённого real basis запрещает реальные ПД; наличие context/JWT/SyntheticOnly claim не evidence законности. L1 isolated synthetic environment с controlled fixtures/operators, не автоматический PII detector произвольной фотографии. No consent/contract/retention UI, real representative/basis/erasure workflows вне L1.

Tests: immutable versions, course/org/period binding, historical reproducibility, current deny over old allow, missing context, DB failure, restriction/write race, SQL counts, file/jobs, forged synthetic claim/real mode deny. NOT RUN. [Security boundary](../architecture/L1-SECURITY-SPEC.md), [plan](../../codex/L03-L1-IMPLEMENTATION-PLAN.md).
