# Owner decision — Teacher Revocation logical deadline

2026-10-12. Owner: владелец проекта. Source: explicit request «Sapientum — упрощение требований Teacher Revocation».

## Decision

For the Sapientum MVP the requirement to prove physical completion of PostgreSQL `COMMIT`, filesystem or kernel I/O, HTTP egress, cleanup, or any other system call within 30 minutes is cancelled.

The accepted contract is:

1. teacher access JWT lifetime is at most 15 minutes;
2. issue and refresh use current authoritative permissions;
3. one logical operation has an immutable deadline of at most 30 minutes from its original admission;
4. at the deadline the application requests cancellation;
5. after the deadline the application must not acknowledge the operation as successful;
6. physical completion of processes, system calls, `COMMIT`, egress, or cleanup by the deadline is not guaranteed;
7. replay protection, idempotency/attempt identity, unknown-`COMMIT` handling, authorization, organization isolation, private-file parent checks, immutable history, and current processing restrictions remain in force.

Ordinary revocation stops new permission issuance and admission of new operations under the accepted 15/30/60-minute model. A late physical side effect after the logical deadline is an explicitly accepted residual risk. It never authorizes a late success response, redisclosure of credentials, automatic replay, a second execution, or access by a caller that is not otherwise authorized.

## Supersession and readiness effect

This decision supersedes only requirements whose sole purpose was proving physical termination or quiescence within the operation deadline. In particular, a production-readiness finding cannot remain blocked only because there is no hard upper bound for kernel I/O, filesystem calls, PostgreSQL `COMMIT`/WAL, transport queues, or cleanup. Supervisor processes, worker isolation, hard-real-time fencing, and equivalent mechanisms are not required for this contract.

Historical A02/A04/A05/A06, P1, FIFO, PostgreSQL, storage, edge, and timing results remain evidence of what the platform does not guarantee. They are not rewritten as PASS and must not be cited as proof of a guarantee that this decision intentionally removes.

This decision does not waive logical deadline enforcement, cancellation request behavior, no-late-success, replay/unknown-outcome safety, current authority on issue/refresh, resource and tenant authorization, personal-data safeguards, deployment clock/admission correctness, capacity/observability, restore, device, legal, or independent security gates. It does not by itself declare Sapientum production-ready or authorize real personal data.

The same owner request authorizes continuation of the existing HTTP/React migration and correction of remaining API regression failures under these preserved invariants. No commit or deploy is authorized.

**PHYSICAL-COMPLETION GATE CANCELLED FOR TEACHER REVOCATION · LOGICAL DEADLINE CONTRACT ACCEPTED · HTTP/REACT MIGRATION AUTHORIZED TO CONTINUE · HISTORICAL EVIDENCE RETAINED · NO PRODUCTION-READY CLAIM · NO COMMIT/DEPLOY.**
