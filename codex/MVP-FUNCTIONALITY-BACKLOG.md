# Sapientum — MVP functionality and completion backlog

> **2026-10-12 update.** [Owner decision](TEACHER-REVOCATION-OWNER-DECISION.md) cancels the physical-completion proof gate for Teacher Revocation and authorizes continuation of HTTP/React migration. Logical deadline/security/unknown-outcome requirements and all non-timing readiness gates remain.

2026-10-10. Evidence-based planning snapshot; priorities are recommendations, not implementation authorization. “Implemented” means the stated component/dev scope only. Full MVP is not accepted. Synthetic-only until separate pilot/security/legal authorization.

## Functionality register

| Function | Status | Evidence / remaining boundary | Required for first full MVP |
|---|---|---|---|
| Submit → Review → Revise → Accept; immutable attempts/messages | Implemented in dev scope | [C2](C2-RESULTS.md), [G](G-RESULTS.md), [H](H-RESULTS.md), [K](K-RELEASE-READINESS.md); current CommandsController and ProblemWorkService | Yes; regress after security integration |
| Multiple photos, upload ordering, protected originals, history snapshots | Partial | [U1](U1-RESULTS.md); local storage works; physical file-completion proof is no longer required; secure HTTP/React integration and logical deadline/replay checks remain | Yes |
| Teacher photo annotation and feedback | Implemented in dev scope; physical validation blocked | [J2](J2-RESULTS.md), [device protocol](K-DEVICE-TEST-PROTOCOL.md); tablet emulation is not physical acceptance | Yes; devices chosen by owner |
| Course/lesson/problem display, Markdown/LaTeX, progress | Partial | [E](E-RESULTS.md), [F](F-RESULTS.md), [G](G-RESULTS.md); known-ID published data available; full content publication/material journey unproven | Yes |
| Teacher review queue, paging/filtering and summaries | Implemented in dev scope; catalogs/statistics partial | [I](I-RESULTS.md), [K gaps](K-MVP-GAPS.md); no complete roster/filter catalogs, summary is not a full statistics workflow | Yes, authorized counts must match visible rows |
| External Markdown import/validation/immutable publication and opening lessons | Absent as complete user journey | [content architecture](../docs/architecture/CONTENT-ARCHITECTURE.md); PublishedContent models/readers and fixtures do not prove a publisher | Yes; external authoring, no CMS |
| Reference solutions / rubric in review workspace | Absent | [K gaps](K-MVP-GAPS.md); no trusted dedicated DTO/material source | Yes under proposal; R04 owner workaround decision open |
| Trusted names, course/group/student catalogs | Absent as complete workflow | [K gaps](K-MVP-GAPS.md); null/UUID fallbacks are honest but incomplete | Yes for usable navigation; approved limited workaround possible |
| Org/account provisioning, enrollment and teacher assignment | Partial | [S1](L1-S1-RESULTS.md), [S3](L1-S3-RESULTS.md) internal foundations; S5 admin journey absent | Yes; no inferred enrollment from account |
| Login/refresh/logout/org switch/browser tab expiry | Partial; migration authorized and in progress | [S2](L1-S2-RESULTS.md), [A1](L1-S4-A1-RESULTS.md), [A2](L1-S4-A2-RESULTS.md); secure HTTP/React composition and regression closure remain | Yes |
| Activation/recovery/security operator approval/blocking UI | Absent as complete journey; blocked by S4/S5 | [implementation plan](L03-L1-IMPLEMENTATION-PLAN.md), ADR-L17; primitives/schema do not complete S6 | Yes |
| Org/resource/processing authorization | Partial | S3/A1/A2 accepted components; [I1](L1-S4-A4-I1-RESULTS.md) parser accepted; [I2-P](L1-S4-A4-I2-P-RESULTS.md) has blocking findings; exact off | Conservative enforcement required; exact matching not automatically required |
| Restart, restore, operations and release qualification | Partial / blocked | [K readiness](K-RELEASE-READINESS.md), [S1](L1-S1-RESULTS.md); dev restart and isolated rehearsal do not qualify deployed DB+originals recovery | Reproducible synthetic acceptance; all operational/legal gates before real pilot |

## Prioritized backlog with dependencies and acceptance

P0 = decision/security blocker; P1 = complete MVP journey; P2 = measured improvements after core scope. Responsible role is a proposal, not an assigned person. Every implementation item requires its own scoped authorization and guardrails contract/negative test matrix.

| ID / priority | Concrete outcome / responsible role | Dependencies / authorization | Completion evidence |
|---|---|---|---|
| M00 / P0 | Resolve reconciliation R01–R04; owner confirms minimal journey, materials and device scope | Owner; no runtime prerequisite | Explicit decision with sources, no silent T04/E03 or content architecture change |
| M01 / CLOSED-NORMATIVE | Remove physical-completion proof from Teacher Revocation readiness; preserve historical evidence | [Owner decision](TEACHER-REVOCATION-OWNER-DECISION.md) accepted; no supervisor/fencing work required | Historical negative results retained; no physical guarantee claimed; logical deadline/cancellation/no-late-success remains executable acceptance |
| M02 / P0 | S4 login/refresh/logout/switch browser integration; backend/frontend owners | Current owner authorization to continue HTTP/React migration; S1–S3/A1/A2 prerequisites | Positive/negative org scopes; O01 two-tab rotation; expiry/replay/lost response; CSRF/Origin; no token leaks |
| M03 / P1 | S5 minimal org-admin roster/account/enrollment/teacher assignment journey | M02 + S5 authorization | Org isolation, no implicit grants, generation move/revoke, stale version, independent platform bootstrap |
| M04 / P1 | S6 activation/recovery + independent operator UI and block/unblock | M03/M02, G03 review + S6 authorization | T01 limits and terminal challenges; initiator≠approver; duplicate/retry/audit atomicity; shared-account isolation |
| M05 / P1 | External Markdown validation/compiler/publication → open lesson → exact-version assignment | M00, trusted administrative authority from M03 for integration; product contract may be documented earlier | Immutable publication, bad input rejection, draft hidden, teacher scope denied outside group, old assignments unchanged |
| M06 / P1 | Authorized course/roster catalogs, names and progress/statistics navigation | M03/M05; trusted source decision; schema/API changes only separately authorized | Same eligibility predicate for rows/counts/pages/aggregates; no other-student/org metadata, no untrusted JWT display-name authority |
| M07 / P1 | Complete material delivery and teacher reference/rubric workflow | M00/M05; protected file integration depends on M01/M08 | Exact content version; teacher-only solutions absent from student payload/cache; missing material handled explicitly |
| M08 / P1 | S7 protected files/jobs/restrictions, failure/retry and resource controls | M02/M04; separate S7 authorization, G04/SR review | Independent parent/context checks; processing/write races; logical deadlines/cancellation/no-late-success; quotas/orphans; unknown commit outcome handled without duplicate effects |
| M09 / P1 | Close complete synthetic journey and S8 integration/cutover acceptance | M02–M08; authorized S8/M3 only | Admin→activation→publication→assignment→two-photo submission→annotated revise→accept→history/progress; denied cross-org paths; restart/restore and failure/expiry tests |
| M10 / P1 release | Physical device/browser and usability acceptance; owner/tester | M00 supported device choice + M09 staged synthetic target | Actual selected hardware/browser/inputs with K protocol; upload retry/offline/reconnect, accessibility and cache safety; failures remain blocked |
| M11 / P0 before pilot | Legal, independent security audit, HTTPS/secrets, backups, monitoring and incident readiness; owner/security/legal/operator | M09/M10; ADR-L03–10, PD/ORG gates, SR-01–SR-04 | Named operators, actual DB+original restore drill, accepted legal/freshness/retention evidence and separate PILOT AUTHORIZED decision |
| M12 / parked | PRS1 I2–I5 completion only if reprioritized | New explicit authorization; I2-P D01–D05, durable provenance/trusted writer/transaction proof | Accepted exact semantics/provenance/race suite before any gate enablement; keep conservative legacy until then |
| M13 / P2 | Bounded history, batched content lookup and dense-canvas optimization | Agreed workload/budgets and measured failures | Reproduce performance gap, improve within approved budget, preserve visibility/version/history; no speculative rewrite |

Suggested sequence: M00 → M02 → M03 → M04; M01 is normatively closed by the 2026-10-12 owner decision; M05/M06/M07 product contracts can be prepared with synthetic fixtures while integration is blocked. This is dependency planning, not permission for parallel implementation. Then M08 → M09 → M10 → M11. M12 is parked, not a hidden prerequisite forcing exact PRS1 into MVP. M11 preparation can happen earlier, but release cannot precede its closure.

No new runtime test execution is claimed here. Evidence comes from existing checkpoints plus inspected source inventory; a missing complete journey is not a claim that every supporting type is absent. Reassess this register after each accepted slice, retaining separate implementation, owner acceptance and release labels.

**TEACHER-REVOCATION PHYSICAL GATE CANCELLED · LOGICAL DEADLINE/SECURITY GATES ACTIVE · HTTP/REACT MIGRATION AUTHORIZED IN CURRENT SCOPE · PRS1 EXACT OFF · SYNTHETIC MVP ACCEPTED · PILOT/PRODUCTION/REAL DATA NOT AUTHORIZED.**
