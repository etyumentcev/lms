# L1/S4-A2 — ProcessingRestrictions selector contract: owner decision required

2026-10-10. **OPEN — NOT ADOPTED.** S4-A2 permits hardening and synthetic verification; it does not authorize inventing normative selector semantics. Existing conservative denial remains in force. This document is not a new ADR, schema change or HTTP authorization.

## Established contract and evidence

- [Owner T05/E06](L03-OWNER-DECISION-REGISTER.md): current restrictions take priority over historical approval, immutable versions, same-org period binding and SyntheticOnly; missing context denies.
- [ADR-L18](../docs/adr/ADR-L18-synthetic-processing-restrictions.md): operation/subject/resource scope, current PostgreSQL check, no positive cross-request cache, server-derived ownership, separate transactional write recheck/serialization.
- [Processing context model](../docs/architecture/PROCESSING-CONTEXT-MODEL.md): Review/Submission → work → assignment → course → organization; attached files inherit parent; identity/security purpose is separate.
- [Persistence shape](../src/Lms.Infrastructure/SchemaFoundations.cs): nullable opaque `ResourceRef`, integer `Operations`. [Schema mappings](../src/Lms.Infrastructure/SchemaFoundationMappings.cs) constrain `Operations` to 1–31, with no meaning assigned to individual bits. [ScopeAction](../src/Lms.Application/ScopedAuthority.cs) is `Read=0, Submit=1, Review=2`, not an approved processing bitmask.

No inspected accepted document defines a ResourceRef grammar, canonical equality, a registry of operation bits or their mapping to ScopeAction. Resource-root context inheritance is defined; the exact propagation/equality of an individual resource-scoped restriction is not. Inferring bit positions from ScopeAction ordinals, parsing arbitrary strings as IDs, or treating unknown selectors as irrelevant would introduce authorization semantics.

## Decision needed before narrowing conservative denial

The owner must approve a selector contract covering all of the following, or explicitly retain conservative denial:

1. Resource kinds, identifier grammar/canonicalization, organization binding, null/empty semantics and treatment of malformed/unknown references.
2. Operation-bit meanings for each of the five persisted positions; explicit mapping for Read/Submit/Review, composite actions and unknown bits. Do not infer these from enum numeric values.
3. Whether a restriction on course/group/assignment/work also applies to descendants; exact ancestors for submission/review, and future file/job paths. Define sibling and ancestor reads, lists/counts and materialization separately.
4. Subject applicability: caller, student/resource owner, all affected subjects, null semantics and aggregate/list behavior.
5. Versioning/compatibility, selector validation on writes, handling of existing opaque values, and required negative acceptance cases. Schema evolution, if needed, needs separate authorization.

This is a decision request identifying missing semantics, not a proposed hidden default. No exact-selector implementation is represented as complete.

## Current component behavior retained

For the applicable same-org context, an Active restriction matching the caller, resource student or all subjects denies every component action regardless of `ResourceRef` or `Operations`. This can overdeny, including malformed or apparently unrelated selectors; it does not narrow prohibitions based on an invented interpretation. Withdrawn restrictions do not deny. Child resource ownership continues to come from S3 server-side resolution. Missing/ambiguous/out-of-period context, unavailable DB, invalid action, stale caller-owned transaction/tracked context and lost clock qualification fail closed.

S4-A2 tests cover fresh deny after an earlier allow on the same component instance; Read/Review on assignment/work/submission/review; each persisted operation bit and opaque/null/empty selectors; foreign organization/course; unknown child; withdrawal; and binding expiry during the DB check. These are component checks, not queue/count filters, legal freshness guarantees or mutation serialization.

## Next minimal authorization proposal — not granted

After S4-A2 acceptance, authorize a documentation-only S4-A3 selector-contract decision package resolving the five items above, plus independent security review of A1/A2. Any later exact matching implementation requires an accepted contract and a separately authorized scope. Public HTTP integration, normative T04/E03 changes and G02 acceptance do not follow from such a decision.

**T04 UNCHANGED · E03 UNCHANGED · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**
