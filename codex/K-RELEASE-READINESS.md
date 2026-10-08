# K — Release readiness

**Decision: NOT_READY.** K audit and scoped fixes are complete; readiness is not
inferred from green development tests. There is no configured/tested real-user pilot
or public production environment in this checkpoint. Evidence: K-TEST-MATRIX,
K-SECURITY-AUDIT, K-PERFORMANCE-REPORT and checked-in `evidence/k/` artifacts.

## Environment separation

- **Development**: isolated loopback Compose with public demo signing key/seed, real
  PostgreSQL/filesystem/API/browser. Reproducible clean startup, migrations, six-screen
  lifecycle, restart, down/up and reset verified. Appropriate for development only.
- **Pilot**: limited real teachers/students still require trusted identities, access
  provisioning, HTTPS, protected storage, recoverable backup, resource controls and
  validated intended devices. None can be waived merely because the group is small.
- **Production**: public system additionally needs hardened deployment, scalable
  resource protection, monitored operations, recovery exercises and identity lifecycle.
  No production deployment, IdP or operator environment was supplied or certified.

## Assessment

| Dimension | Development | Pilot | Production | Evidence / reason |
|---|---|---|---|---|
| 1. Functional completeness | PASS | PASS for six-screen scope | PASS for six-screen scope | Real E2E two photos/reorder/two reviews/history, terminal guards. This does not include auth/deployment gates below. |
| 2. E2E reliability | PASS | NOT TESTED | NOT TESTED | Fresh isolated runner passes; historical tablet resize assertion race fixed by awaiting the unchanged invariant. Real identity/deployment E2E not run. |
| 3. Security | PASS for tested app boundaries | FAIL | FAIL | Fixed P0 attached-file revocation; JWT/IDOR/bounds pass. K-S02 quota/rate/orphan controls absent for exposed use. |
| 4. Data integrity | PASS | PASS for tested application transactions | PASS for tested application transactions | Real DB concurrency, rollback/commit-entry failures, immutable history, split-read snapshots. Backup/power-loss guarantees separately unverified. |
| 5. Performance | PASS for completed measurements | BLOCKED | BLOCKED | P1–P6 measured; P5 fixed. P2 dense canvas and Lesson N+1 remain; target workload, concurrent staging and approved budgets absent. No blanket capacity claim. |
| 6. Browser compatibility | PASS for headless Chrome | BLOCKED | BLOCKED | Chrome154.0.8037.57 browser workflow, widths, EXIF/CDP. Safari/WebKit and intended deployment browser mix not tested. |
| 7. Physical device compatibility | BLOCKED | BLOCKED | BLOCKED | Physical iPad/Pencil/touch/Safari/Android unavailable; manual protocol prepared. May be NOT APPLICABLE only after an explicitly desktop-only pilot scope decision. |
| 8. Authentication readiness | PASS for dev-only login | BLOCKED | BLOCKED | Real JWT validation pass, production demo rejection pass; actual IdP, UUID mapping, lifecycle/session host integration absent. |
| 9. Deployment readiness | PASS for isolated dev Compose | BLOCKED | BLOCKED | Public dev key/seed; no staged HTTPS/secrets/proxy hardening/migration rollback procedure exercised. Production image boundary inspected, not production deployment certification. |
| 10. Backup/restore readiness | NOT TESTED | BLOCKED | BLOCKED | Restart ≠ restore. No coordinated DB+file backup/clean-host restoration drill or RPO/RTO. |
| 11. Logging/observability | PASS for local errors/traceId | NOT TESTED | NOT TESTED | Problem Details/traceId + server logs; no deployed alerts, dashboards, retention or tested token redaction policy. |
| 12. Operational recovery | PASS for restart/transaction retry boundaries | BLOCKED | BLOCKED | Clean reset/restart/local recovery verified; restore/incident runbook/operator drill not completed. |
| 13. MVP contract gaps | PASS for honest null/ID fallbacks | BLOCKED on roster/workaround acceptance | BLOCKED on source decisions | Names/lookups/reference/criteria classified, not invented. Restricted pilot can defer with secure roster/material workflow agreed by owner. |

Chrome version is recorded exactly in `performance-final.json`; no Safari or physical
PASS is claimed. The word PASS in a scoped cell is not whole-environment approval.

## Pilot blockers and closure evidence

1. **K-S04 / P1**: stage real identity provider, internal UUID/role mapping, teacher
   scope/student enrollment and session remount; exercise allowed/denied real accounts,
   expiry/relogin and removal. Public Compose key and demo login must not serve real users.
2. **K-S02 / P1**: bound authenticated resource consumption with tested gateway/app
   limits, storage quotas/alerts and safe orphan reconciliation. Per-file limits alone
   do not prevent exhaustion. Choose the simplest controls compatible with deployment.
3. **K-S06 / P1**: coordinated PostgreSQL + originals backup, clean-host restore and
   integrity verification, approved recovery objectives and responsible operator.
4. Stage HTTPS/secrets/storage access, migration procedure, monitoring and incident
   recovery; run the same E2E against that environment without fixture auth shortcuts.
5. If tablet/Safari is a primary path, execute the physical protocol with actual devices.
   For a desktop-only pilot explicitly approve the browser/device scope instead.
6. Agree pilot roster/material workarounds, dataset limits and performance budgets;
   names/lookup sources remain P2 unless real pilot operation proves the workaround unusable.

These open P1 gates are processed and assigned closure requirements, **not hidden as
benign limitations**. No remaining confirmed P0 application defect is known from this
audit; that is not proof of absence. Do not release while a new P0 is unresolved.

## Additional production blockers

All pilot gates plus public-facing rate/concurrency/DoS controls, hardened TLS/origin/
security-header configuration, credential rotation/revocation, capacity/soak tests,
monitored backup/restore drills, operational ownership and an actual deployment review.
No READY_FOR_PRODUCTION decision is possible on the available evidence.

Recommended next checkpoint: staged **Pilot Identity & Operations Readiness** with
specified IdP/hosting, backup/restore, resource protection and real-device evidence.
It is a recommendation only. **Do not start it automatically.**
