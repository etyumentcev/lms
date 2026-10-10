using Lms.Application;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure;

// Fresh dedicated context. Same S2 lock order, followed by S3 organization lock.
// Switch organization calls Issue again; no scope is copied from a previous token.
public sealed class PostgresComponentTokenIssuer(LmsDbContext db, IAuthorityClock clock, ComponentJwt jwt)
{
    public async Task<AccessToken?> Issue(Guid userId, Guid sessionId, Guid organizationId, CancellationToken ct = default)
    {
        if (clock.Read() is not { Valid: true }) return null;
        var provider = new ConservativeTimeProvider(clock);
        var issued = await new PostgresIdentitySessions(db, provider).WithIssuingSession(userId, sessionId, async identity =>
        {
            var org = await db.Set<Organization>().FromSqlInterpolated(
                $"""SELECT * FROM "Organizations" WHERE "OrganizationId"={organizationId} FOR UPDATE""").SingleOrDefaultAsync(ct);
            if (org is not { State: "Enabled", SyntheticOnly: true }) return null;
            var current = await new PostgresScopedAuthorityReader(db, provider).ResolveCurrent(organizationId, identity.ActorId, ct);
            if (current == null) return null;
            // S3 excludes every revoked/deadline-bearing row. Do not introduce scheduled
            // grants or reinterpret its state machine. Null means explicitly unbounded.
            var token = jwt.Issue(identity, current, [null]);
            ct.ThrowIfCancellationRequested();
            return token;
        }, ct);
        ct.ThrowIfCancellationRequested();
        // Commit/disposal may wait. Recheck lifetime, clock qualification and key trust.
        return issued != null && jwt.Validate(issued) != null ? issued : null;
    }
    private sealed class ConservativeTimeProvider(IAuthorityClock clock) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => clock.Read() is { Valid: true } t ? t.Latest :
            throw new InvalidOperationException("Authority clock unavailable");
    }
}
