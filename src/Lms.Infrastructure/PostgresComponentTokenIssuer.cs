using Lms.Application;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure;

// Fresh dedicated context. Same S2 lock order, followed by S3 organization lock.
// Switch organization calls Issue again; no scope is copied from a previous token.
public sealed class PostgresComponentTokenIssuer(LmsDbContext db, IAuthorityClock clock, ComponentJwt jwt, bool restrictedCapabilities = false)
{
    private sealed class NoOrganizationsException : Exception { }
    public async Task<SessionResult> Login(string userName, string password, Guid organizationId,
        ApplicationDeadline deadline, CancellationToken ct = default)
    {
        if (clock.Read() is not { Valid: true } || !deadline.Check(ApplicationAction.Admission).Allowed)
            return new(SessionOutcome.Denied);
        using var cancellation = new DeadlineCancellation(deadline, TimeProvider.System, ct);
        ct = cancellation.Token;
        var provider = new ConservativeTimeProvider(clock);
        var result = await new PostgresIdentitySessions(db, provider, restrictedCapabilities).AuthenticateAndIssue(userName, password,
            identity => IssueCurrent(identity, organizationId, provider, ct), deadline, ct);
        ct.ThrowIfCancellationRequested();
        if (!deadline.Check(ApplicationAction.ResponseEnqueue).Allowed || clock.Read() is not { Valid: true } ||
            result.AccessToken is { } token && jwt.Validate(token) == null) return new(SessionOutcome.Denied);
        return result;
    }

    public async Task<BrowserSessionResult> LoginForBrowser(string userName, string password,
        Guid? preferredOrganizationId, ApplicationDeadline deadline, CancellationToken ct = default)
    {
        if (clock.Read() is not { Valid: true } || !deadline.Check(ApplicationAction.Admission).Allowed)
            return new(new(SessionOutcome.Denied), []);
        using var cancellation = new DeadlineCancellation(deadline, TimeProvider.System, ct);
        ct = cancellation.Token;
        var provider = new ConservativeTimeProvider(clock);
        IReadOnlyList<OrganizationOption> organizations = [];
        Guid? selected = null;
        SessionResult result;
        try
        {
            result = await new PostgresIdentitySessions(db, provider, restrictedCapabilities).AuthenticateAndIssue(
                userName, password, async identity =>
                {
                    organizations = await AvailableOrganizations(identity.ActorId, ct);
                    if (organizations.Count == 0) throw new NoOrganizationsException();
                    var candidate = Select(organizations, preferredOrganizationId);
                    if (candidate == null) return null;
                    var access = await IssueCurrent(identity, candidate.Value, provider, ct);
                    if (access != null) selected = candidate;
                    else
                    {
                        organizations = await AvailableOrganizations(identity.ActorId, ct);
                        if (organizations.Count == 0) throw new NoOrganizationsException();
                        if (organizations.Any(x => x.OrganizationId == candidate.Value)) selected = candidate;
                    }
                    return access;
                }, deadline, ct);
        }
        catch (NoOrganizationsException)
        {
            return new(new(SessionOutcome.NoOrganizations), []);
        }
        return FinishBrowser(result, organizations, selected, deadline);
    }

    public async Task<AccessToken?> Issue(Guid userId, Guid sessionId, Guid organizationId, CancellationToken ct = default)
    {
        if (clock.Read() is not { Valid: true }) return null;
        var provider = new ConservativeTimeProvider(clock);
        var issued = await new PostgresIdentitySessions(db, provider, restrictedCapabilities).WithIssuingSession(
            userId, sessionId, identity => IssueCurrent(identity, organizationId, provider, ct), ct);
        ct.ThrowIfCancellationRequested();
        // Commit/disposal may wait. Recheck lifetime, clock qualification and key trust.
        return issued != null && jwt.Validate(issued) != null ? issued : null;
    }
    // Trusted internal adapter, not an HTTP refresh endpoint. The caller captures the
    // original request deadline before queue/body and must retain it through retries.
    public async Task<SessionResult> Refresh(RefreshSecret secret, Guid organizationId,
        ApplicationDeadline deadline, CancellationToken ct = default)
    {
        if (clock.Read() is not { Valid: true } || !deadline.Check(ApplicationAction.Admission).Allowed)
            return new(SessionOutcome.Denied);
        using var cancellation = new DeadlineCancellation(deadline, TimeProvider.System, ct);
        ct = cancellation.Token;
        var provider = new ConservativeTimeProvider(clock);
        var result = await new PostgresIdentitySessions(db, provider, restrictedCapabilities).RotateAndIssue(secret,
            identity => IssueCurrent(identity, organizationId, provider, ct), deadline, ct);
        ct.ThrowIfCancellationRequested();
        if (!deadline.Check(ApplicationAction.ResponseEnqueue).Allowed || clock.Read() is not { Valid: true } ||
            result.AccessToken is { } token && jwt.Validate(token) == null)
            return new(SessionOutcome.Denied); // committed rotation may require re-login; never redisclose
        return result;
    }

    public async Task<BrowserSessionResult> RefreshForBrowser(RefreshSecret secret,
        Guid? preferredOrganizationId, ApplicationDeadline deadline, CancellationToken ct = default)
    {
        if (clock.Read() is not { Valid: true } || !deadline.Check(ApplicationAction.Admission).Allowed)
            return new(new(SessionOutcome.Denied), []);
        using var cancellation = new DeadlineCancellation(deadline, TimeProvider.System, ct);
        ct = cancellation.Token;
        var provider = new ConservativeTimeProvider(clock);
        IReadOnlyList<OrganizationOption> organizations = [];
        Guid? selected = null;
        SessionResult result;
        try
        {
            result = await new PostgresIdentitySessions(db, provider, restrictedCapabilities).RotateAndIssue(secret,
                async identity =>
                {
                    organizations = await AvailableOrganizations(identity.ActorId, ct);
                    if (organizations.Count == 0) throw new NoOrganizationsException();
                    var candidate = Select(organizations, preferredOrganizationId);
                    if (candidate == null) return null;
                    var access = await IssueCurrent(identity, candidate.Value, provider, ct);
                    if (access != null) selected = candidate;
                    else
                    {
                        organizations = await AvailableOrganizations(identity.ActorId, ct);
                        if (organizations.Count == 0) throw new NoOrganizationsException();
                        if (organizations.Any(x => x.OrganizationId == candidate.Value)) selected = candidate;
                    }
                    return access;
                }, deadline, ct);
        }
        catch (NoOrganizationsException)
        {
            return new(new(SessionOutcome.NoOrganizations), []);
        }
        return FinishBrowser(result, organizations, selected, deadline);
    }

    private BrowserSessionResult FinishBrowser(SessionResult result,
        IReadOnlyList<OrganizationOption> organizations, Guid? selected, ApplicationDeadline deadline)
    {
        if (!deadline.Check(ApplicationAction.ResponseEnqueue).Allowed || clock.Read() is not { Valid: true } ||
            result.AccessToken is { } token && jwt.Validate(token) == null)
            return new(new(SessionOutcome.Denied), []);
        return new(result, organizations, selected);
    }

    private static Guid? Select(IReadOnlyList<OrganizationOption> organizations, Guid? preferred)
    {
        if (preferred is { } requested && organizations.Any(x => x.OrganizationId == requested)) return requested;
        return organizations.Count == 1 ? organizations[0].OrganizationId : null;
    }
    private async Task<IReadOnlyList<OrganizationOption>> AvailableOrganizations(Guid actorId, CancellationToken ct)
    {
        var rows = await (from membership in db.Set<OrganizationMembership>().AsNoTracking()
                          join organization in db.Set<Organization>().AsNoTracking()
                              on membership.OrganizationId equals organization.OrganizationId
                          join profile in db.Set<OrganizationProfile>().AsNoTracking()
                              on membership.OrganizationId equals profile.OrganizationId
                          where membership.ActorId == actorId && membership.State == "Active" &&
                                organization.State == "Enabled" && organization.SyntheticOnly
                          orderby profile.Name, organization.OrganizationId
                          select new OrganizationOption(organization.OrganizationId, profile.Name.Trim()))
            .ToListAsync(ct);
        return rows.GroupBy(x => x.Name, StringComparer.Ordinal).SelectMany(group => group.Count() == 1
            ? group : group.Select((item, index) => item with { Name = $"{item.Name} — вариант {index + 1}" }))
            .ToArray();
    }

    private async Task<AccessToken?> IssueCurrent(SessionIdentity identity, Guid organizationId,
        TimeProvider provider, CancellationToken ct)
    {
        var org = restrictedCapabilities
            ? await db.Set<Organization>().FromSqlInterpolated(
                $"SELECT * FROM public.s2_lock_issuing_organization({organizationId})").SingleOrDefaultAsync(ct)
            : await db.Set<Organization>().FromSqlInterpolated(
                $"""SELECT * FROM "Organizations" WHERE "OrganizationId"={organizationId} FOR UPDATE""").SingleOrDefaultAsync(ct);
        if (org is not { State: "Enabled", SyntheticOnly: true }) return null;
        var current = await new PostgresScopedAuthorityReader(db, provider).ResolveCurrent(organizationId, identity.ActorId, ct);
        if (current == null) return null;
        // Revoked rows are excluded immediately. Ordinary revocation has no D-10m lease.
        var token = jwt.Issue(identity, current, [null]);
        ct.ThrowIfCancellationRequested();
        return token;
    }
    private sealed class ConservativeTimeProvider(IAuthorityClock clock) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => clock.Read() is { Valid: true } t ? t.Latest :
            throw new InvalidOperationException("Authority clock unavailable");
    }
}
