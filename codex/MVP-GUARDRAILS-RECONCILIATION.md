# MVP guardrails — adoption and owner review

> **NR1 effective notice — 2026-10-11.** Прежнее boundary statement «timing guarantees unchanged» сохранено как история и заменено только scoped NR1 D1–D8. A06 E03-C/N не приняты; прежние R01–R04 этим решением не закрываются.
> Нормативный источник: [N1–N7 / точный D1–D8](MVP-B01-G02-NORMATIVE-REVIEW.md), [принятое решение](L03-OWNER-DECISION-REGISTER.md), [sync results](MVP-B01-G02-NR1-SYNC-RESULTS.md). Только controlled SyntheticOnly и отдельно принимаемые routes; неопределённый scope → blocked. Credential issuance, grants, B08 publication, general jobs и внешние irreversible effects не получают автоматического исключения. CLOCK NOT PROVEN; технически NR1 NOT IMPLEMENTED / NOT ACCEPTED.
> Исторический текст ниже сохранён; старые failure results не превращены в PASS.

2026-10-10. Documentation integration only. The supplied [proposal](SAPIENTUM-MVP-ENGINEERING-GUARDRAILS.md) was already present, untracked, and is preserved byte-for-byte. Its shared process is connected through [root AGENTS.md](../AGENTS.md). This does not accept conflicting proposal wording or amend any ADR/owner decision.

## Sources and precedence

Reviewed owner register T01–T06/E01–E06/O01/B01 and later S4/PRS1 entries in [L03 register](L03-OWNER-DECISION-REGISTER.md), [I2-P findings](L1-S4-A4-I2-P-RESULTS.md), [A06 decision package](L1-G02-A06-OWNER-DECISION.md), ADR-L01–L18, content/domain contracts and component/product checkpoints linked in the backlog. Latest explicit acceptance wins over older NOT AUTHORIZED/NOT IMPLEMENTED headers; proposals and test success are not acceptance.

The supplied task's working-directory instructions are retained in root AGENTS.md. Canonical checkout has no prior AGENTS.md or `.agents/skills`; the old task checkout has only the supplied working-directory AGENTS.md. `model-domain`, `implement-use-case`, `implement-feature` were not found in canonical checkout, current old checkout, `/home/tea/.agents` or `/home/tea/.codex/skills`. No skill was removed, rewritten or recreated. Integration is at repository instruction level; reviewing the missing skills' contents remains impossible until their source is available.

## Conflicts / ambiguities presented to owner

| ID | Proposal wording / source | Accepted boundary / consequence | Proposed resolution for owner review |
|---|---|---|---|
| R01 — definite status conflict | Guardrails §0: “I2 authorized but not reported accepted” | Latest register explicitly says **I2-P AUTHORIZED; I2 IMPLEMENTATION NOT AUTHORIZED**. I2-P completed with D01–D05 findings; not I2 acceptance. | Replace with “I1 accepted; I2-P validation completed with findings; I2 implementation not authorized.” Until resolved, follow the register; no I2 work. |
| R02 — authorization ambiguity | §§2/6: re-resolve authorization on every operation / same-transaction permission | [ADR-L15](../docs/adr/ADR-L15-jwt-delayed-revocation.md), [ADR-L16](../docs/adr/ADR-L16-organization-jwt-browser-session.md): local JWT authority, no mandatory per-request DB grant freshness or ordinary revoke barrier. [ADR-L18](../docs/adr/ADR-L18-synthetic-processing-restrictions.md): current processing checks and write serialization remain mandatory. | Clarify resource ownership/workflow/current restrictions versus ordinary grant freshness; do not introduce immediate JWT revocation or a global barrier. Negative revoke tests assert accepted delayed semantics. |
| R03 — scope ambiguity | §§1/8: teacher “prepares/publishes”, “course/lesson authoring” | [Content architecture](../docs/architecture/CONTENT-ARCHITECTURE.md): external Markdown → validator/compiler → immutable publication; no CMS/editor. | Interpret preparation as external Markdown; publication journey needs its own contract. In-app editor would require a new scope decision, not this task. |
| R04 — changed completeness expectation | §1 includes rubric/reference; [K gaps](K-MVP-GAPS.md) permits owner-approved pilot workaround at P2 | Existing DTOs lack these materials. Earlier workaround is not full feature completion; device scope also remains undecided in ADR-L07. | Owner confirms in-app protected material delivery versus explicit external workaround and supported devices. Keep missing material functionality and physical device evidence visible; do not silently defer or assert tablet PASS. |

No accepted decision was changed. R01–R04 are presented for review, not resolved by this document. Shared non-conflicting process applies now; affected implementation must obey the accepted source or stop for owner decision.

## Boundary checks

- T01/T02/ADR-L01/02/17: closed provisioning, minimal admin UI, independent recovery, code limits preserved; no public signup or CLI-only replacement.
- T03/T06/ADR-L11/12/14: org is the learning boundary; additive migration/history/generations preserved; no new LearningSpace entity or historical backfill permission.
- T04/E01–E03/ADR-L15–17: timing guarantees unchanged. A06 is a proposed normative package, not approval to weaken E03/T04. G02 remains blocked.
- T05/E06/ADR-L13/18: synthetic-only, fail-closed processing, legal gates open. Documentation is not compliance certification.
- PRS1: exact gate stays off, legacy conservative behavior stays intact, I2–I5 parked; D01–D05 and SR-01–SR-04/human audit remain open.
- ADR-L03–10 operational/device/privacy/erasure requirements remain independent release gates. Existing development restart evidence is not a production restore drill.

## Validation and limitations

Only new AGENTS.md, this reconciliation and the backlog are authored in this task. Source guardrails and all pre-existing files are checked against SHA-256 baseline, with HEAD/branch/index preservation. Markdown local links and whitespace are checked. Runtime/API/schema/migrations/frontend are unchanged; runtime and browser tests **NOT RUN** (documentation-only scope). Historical test results are referenced, not rerun or newly certified.

Current checkout is `codex/l1-s4-jwt-api-authorization`, not `main`; preserved because it contains ongoing dirty work and an explicit earlier branch checkpoint. No checkout/reset/commit/push/merge/deploy performed. Default future workflow remains canonical/main; this task does not move existing work between branches.

**MVP GUARDRAILS READY FOR OWNER REVIEW · PRS1 EXACT OFF · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**

Validation result: external change detected in `.obsidian/workspace.json` during this task; this task did not edit or restore it. All other 393 baseline files, including the supplied guardrails, remain byte-for-byte unchanged. Full-tree immutability is not claimed.
