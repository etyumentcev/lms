using Lms.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure;

// Use a dedicated context per operation, as with other persistence adapters.
// Not registered at public ingress until S4/S5 authorization exists.
public sealed class PostgresIdentityMapping(LmsDbContext db, TimeProvider clock, bool restrictedCapabilities = false) : IIdentityMapping
{
    public async Task<IdentityLink> CreateInactive(Guid userId, Guid actorId, Guid provisioningOrgId,
        string userName, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || actorId == Guid.Empty || provisioningOrgId == Guid.Empty ||
            string.IsNullOrWhiteSpace(userName) || userName.Length > 256)
            throw new UseCaseException("invalid_identity_mapping");
        if (db.ChangeTracker.Entries().Any())
            throw new InvalidOperationException("Identity creation requires a fresh, dedicated DbContext.");
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        if (restrictedCapabilities)
        {
            // Only the trusted internal provisioning LOGIN can execute this capability.
            // IDs are explicit mapping selectors, never evidence of caller authority.
            var normalized = new UpperInvariantLookupNormalizer().NormalizeName(userName);
            var at = clock.GetUtcNow();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT public.s2_create_inactive({userId}, {actorId}, {provisioningOrgId}, {userName}, {normalized}, {at})", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return new IdentityLink(userId, actorId, false);
        }
        // Explicit actor ID only. Never guess an association or reassign an existing mapping.
        if (!await db.Set<ActorRecord>().AnyAsync(x => x.ActorId == actorId, cancellationToken))
            db.Add(new ActorRecord { ActorId = actorId });
        db.Users.Add(new IdentityUser<Guid> { Id = userId, UserName = userName,
            NormalizedUserName = new UpperInvariantLookupNormalizer().NormalizeName(userName),
            SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString() });
        db.Add(new ActorIdentity { UserId = userId, ActorId = actorId, IsActive = false,
            ProvisioningOrgId = provisioningOrgId });
        db.Add(new AccountSecurityState { UserId = userId, State = "Enabled", CredentialEpoch = 1,
            Version = 1, ChangedAt = clock.GetUtcNow() });
        var auditAt = clock.GetUtcNow();
        db.Add(new SecurityAudit { AuditId = Guid.NewGuid(), ActorId = actorId,
            OrganizationId = provisioningOrgId, Action = "identity_mapping_created", SubjectRef = userId.ToString(),
            Result = "inactive", TraceId = Guid.NewGuid().ToString(), OccurredAt = auditAt });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new IdentityLink(userId, actorId, false);
    }

    public Task<IdentityLink?> Find(Guid userId, CancellationToken cancellationToken = default)
        => db.Set<ActorIdentity>().AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => new IdentityLink(x.UserId, x.ActorId, x.IsActive)).SingleOrDefaultAsync(cancellationToken);
}
