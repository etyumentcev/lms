using Lms.Application;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure;

// Current authoritative ADR-L18 read, no positive cache. Component check only:
// future write adapters must recheck/serialize inside their mutation transaction.
public sealed class PostgresCurrentProcessingGate(LmsDbContext db, IAuthorityClock clock) : ICurrentProcessingGate
{
    public async Task<bool> Allows(ResourceOwnership resource, Guid actorId, ScopeAction action, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(action) || clock.Read() is not { Valid: true } time ||
            db.Database.CurrentTransaction != null || db.ChangeTracker.Entries().Any()) return false;
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
        return await AllowsInTransaction(resource, actorId, action, cancellationToken);
    }

    // Caller holds processing_lock(org) throughout mutation and commit. Never use this
    // entry point with an unrelated transaction or detached authorization decision.
    internal async Task<bool> AllowsInTransaction(ResourceOwnership resource, Guid actorId, ScopeAction action, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(action) || clock.Read() is not { Valid: true } time || db.Database.CurrentTransaction == null) return false;
        if (!await db.Set<Organization>().AsNoTracking().AnyAsync(o => o.OrganizationId == resource.OrganizationId &&
                o.State == "Enabled" && o.SyntheticOnly, cancellationToken)) return false;
        var bindings = await (from b in db.Set<CourseContextBinding>().AsNoTracking()
            join c in db.Set<ProcessingContextVersion>().AsNoTracking() on new { b.ContextId, Version = b.ContextVersion } equals new { c.ContextId, c.Version }
            join course in db.Set<CourseRegistration>().AsNoTracking() on b.CourseId equals course.CourseId
            where b.CourseId == resource.CourseId && b.OrganizationId == resource.OrganizationId &&
                c.OrganizationId == resource.OrganizationId && course.OrganizationId == resource.OrganizationId && course.MappingState == "Verified" &&
                b.EffectiveFrom <= time.Latest && (b.EffectiveTo == null || b.EffectiveTo > time.Earliest)
            select new { b.ContextId, b.EffectiveFrom, b.EffectiveTo, ContextFrom = c.EffectiveFrom, c.SyntheticOnly, c.ApprovalState }).ToListAsync(cancellationToken);
        if (bindings.Count != 1) return false;
        var binding = bindings[0];
        if (!binding.SyntheticOnly || binding.ApprovalState != "SyntheticOnly" || binding.EffectiveFrom > time.Earliest ||
            binding.ContextFrom > time.Earliest || binding.EffectiveTo <= time.Latest) return false;
        // Existing schema has opaque ResourceRef/operation bits without a runtime resolver.
        // Conservatively deny any active applicable-context/subject restriction; never invent
        // an interpretation that could accidentally turn a prohibition into an allow.
        var restricted = await db.Set<ProcessingRestriction>().AsNoTracking().AnyAsync(r => r.OrganizationId == resource.OrganizationId &&
            r.ContextId == binding.ContextId && r.State == "Active" &&
            (r.SubjectActorId == null || r.SubjectActorId == actorId || r.SubjectActorId == resource.StudentId), cancellationToken);
        if (restricted || clock.Read() is not { Valid: true } after) return false;
        // Database waits may cross a binding boundary or lose clock qualification.
        return after.Earliest >= time.Earliest && binding.EffectiveFrom <= after.Earliest &&
            binding.ContextFrom <= after.Earliest && (binding.EffectiveTo == null || binding.EffectiveTo > after.Latest);
    }
}
