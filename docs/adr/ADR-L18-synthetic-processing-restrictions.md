# ADR-L18 — ProcessingRestrictions в SyntheticOnly L1

2026-10-09. **ACCEPTED E06**, совместно T05/ADR-L13, owner L0.4 §4. Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY. LEGAL SOURCE NOT VERIFIED.

ProcessingContextVersions immutable, Organization-bound; CourseContextBindings задаёт непересекающиеся периоды same-org context. Historical operation→version/time сохраняется sidecar/audit без изменения work events. ProcessingRestrictions — current prohibition records с origin-version provenance, operation/subject/resource scope и state/version; priority над любой historical allow. Client не определяет resource/context ownership.

Минимальный L1 engineering enforcement: environment только SyntheticOnly, controlled synthetic fixtures/provenance; claim/header не включает real mode. Из resource root разрешить applicable course binding/context/same org/period/synthetic marker; missing/ambiguous/expired required context→deny. Читать текущие restrictions PostgreSQL при operation check, без positive cross-request allow cache. Это отдельный processing gate, не DB freshness обычных JWT grants. Обязательная processing DB недоступна→fail closed; local JWT signature check независим от DB.

Reads проверяются до materialization/выдачи; queue/count filters до aggregation. Writes повторно проверяют restrictions/context в transaction перед irreversible mutation; synthetic restriction-update/write decision сериализуются targeted version/CAS/lock. Старый snapshot требует recheck/deny, не принятия historical allow. Это не global ordinary revoke barrier. Audit хранит current decision refs/historical applicable version, не fake legal basis.

Files проходят тот же gate. Long stream после admission не обещает мгновенного прерывания при newly committed restriction; bounded duration ADR-L15 остаётся, это synthetic-only предел. Jobs fresh context/restriction check перед irreversible step и delivery; initial JWT не perpetual permission. **До real pilot отдельное решение freshness/propagation/in-flight semantics и legal evidence**; обычные два часа нельзя перенести на юридические запреты.

Отсутствие подтверждённого real basis запрещает реальные ПД; наличие context/JWT/SyntheticOnly claim не evidence законности. L1 isolated synthetic environment с controlled fixtures/operators, не автоматический PII detector произвольной фотографии. No consent/contract/retention UI, real representative/basis/erasure workflows вне L1.

Tests: immutable versions, course/org/period binding, historical reproducibility, current deny over old allow, missing context, DB failure, restriction/write race, SQL counts, file/jobs, forged synthetic claim/real mode deny. NOT RUN. [Security boundary](../architecture/L1-SECURITY-SPEC.md), [plan](../../codex/L03-L1-IMPLEMENTATION-PLAN.md).
