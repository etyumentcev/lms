# Sapientum LMS — Codex workflow

## Working directory and Git

- Canonical directory: `/home/tea/github/lms`. Work sequentially on `main` unless the user explicitly requests another branch or parallel work. Do not create worktrees; new tasks use Local mode.
- Older tasks pointing at `.codex/worktrees/*/lms` must use the canonical directory explicitly.
- Preserve pre-existing tracked/untracked changes and checkpoint history. Never reset, clean, overwrite or switch a dirty checkout merely to enforce the default branch. Record the mismatch and resolve it separately.
- No commit, push, merge or deploy without user authorization.

## Mandatory entry point for every development task

Read [MVP guardrails](codex/SAPIENTUM-MVP-ENGINEERING-GUARDRAILS.md) together with [reconciliation](codex/MVP-GUARDRAILS-RECONCILIATION.md), the applicable ADRs and latest explicit owner decisions. Accepted decisions take precedence; historic checkpoint headlines do not override later decisions. Guardrails and backlog do not authorize implementation or release across a closed gate.

Before implementation, give a compact **Actor → Use Case → Invariants → Entities/VO → State transitions → Authorization & data-source matrix → Tests**. Identify scope/non-goals, accepted dependencies, blocked gates and the source of authorization. For writes specify transaction owner/isolation, lock order, CAS, commit/audit, retry and unknown-outcome handling. Unclear write semantics stop that write.

Apply guardrails §§2–3 and §6 before coding and during self-review. Explicitly check client authority/IDOR, cross-org access, post-pagination filtering, file-parent bypass, stale authorization across transactions, unsafe restriction narrowing, mutable history, duplicate effects, secrets and unsupported readiness claims. Use `covered`, `not applicable — reason`, or `blocked` with evidence; a keyword scan alone is not a security review.

Require independently asserted negative-access tests from §5 for every affected access path, alongside positive controls. Include wrong org/course/group/assignment/role, missing or forged parent, revoked/moved membership under the accepted temporal semantics, missing processing evidence, inaccessible attachments, and counts/pagination leakage. Record applicability, expected denial and actual command/result. UI paths also cover expiry/retry/duplicate submission and relevant browser security. Documentation-only tasks validate documents; runtime tests are NOT RUN, not implied PASS.

Use the existing `model-domain`, `implement-use-case`, `implement-feature` and other applicable skills when available; read their SKILL.md and keep their specialist workflow. This policy supplies shared entry/exit checks and does not replace or duplicate those skills. If a required skill is absent, report that fact; never invent its instructions or claim it ran.

Implement only the requested smallest authorized slice. No unrelated refactors/frameworks, new permissions, API/schema/security changes or cross-org processing without an applicable authorization. A conflict with an accepted decision stops the affected work and goes to the owner with both sources and a concrete proposed resolution; continue independent authorized work. Do not ask again for authorization already granted.

Final report: outcome/scope; changed paths; preserved invariants; positive/negative tests with exact commands/results or NOT RUN; open risks/dependencies/owner decisions; readiness label. Use the guardrails §7 checklist in each change report (N/A requires reason). Keep `UI COMPLETE`, `E2E ACCEPTED`, `SYNTHETIC MVP ACCEPTED`, and `PILOT AUTHORIZED` separate.

Current boundary: **PRS1 EXACT OFF · G02 BLOCKED · S4 HTTP NOT AUTHORIZED**. I1 accepted; I2-P validation completed with findings; I2 implementation not authorized. I2–I5 parked for MVP prioritization. Synthetic data only. See [MVP backlog](codex/MVP-FUNCTIONALITY-BACKLOG.md).
