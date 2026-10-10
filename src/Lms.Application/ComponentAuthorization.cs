namespace Lms.Application;

// An externally qualified interval containing true UTC. No implicit system-clock fallback.
public sealed record AuthorityTime(DateTimeOffset Earliest, DateTimeOffset Latest)
{
    public bool Valid => Earliest <= Latest && Earliest >= DateTimeOffset.UnixEpoch;
}
public interface IAuthorityClock { AuthorityTime? Read(); }

// A bearer value must not appear in generated record ToString or JSON diagnostics.
public sealed class AccessToken(string value)
{
    public string Reveal() => value;
    public override string ToString() => "[REDACTED]";
}
public sealed record TokenAuthority(SessionIdentity Identity, CurrentAuthority Authority,
    DateTimeOffset NotBefore, DateTimeOffset ExpiresAt);

public static class ComponentExpiry
{
    public static DateTimeOffset? Calculate(AuthorityTime? time, DateTimeOffset sessionExpiry,
        IEnumerable<DateTimeOffset?> authorityDeadlines)
    {
        if (time is not { Valid: true }) return null;
        if (time.Earliest > DateTimeOffset.MaxValue - SessionPolicy.MaximumAccessLifetime) return null;
        var cutoff = time.Earliest.Add(SessionPolicy.MaximumAccessLifetime);
        if (sessionExpiry < cutoff) cutoff = sessionExpiry;
        foreach (var deadline in authorityDeadlines)
            if (deadline is { } d)
            {
                if (d <= DateTimeOffset.UnixEpoch.AddMinutes(10)) return null;
                if (d.AddMinutes(-10) < cutoff) cutoff = d.AddMinutes(-10);
            }
        cutoff = DateTimeOffset.FromUnixTimeSeconds(cutoff.ToUnixTimeSeconds()); // round down
        return cutoff > time.Latest ? cutoff : null;
    }
}

// Only component evidence. A true result is not HTTP admission or permission to publish.
public interface ICurrentProcessingGate
{
    Task<bool> Allows(ResourceOwnership resource, Guid actorId, ScopeAction action, CancellationToken cancellationToken);
}
public sealed class ComponentAuthorization(IScopedAuthorityReader resources, ICurrentProcessingGate processing,
    IAuthorityClock clock)
{
    public async Task<bool> Allows(TokenAuthority token, ResourceReference reference, ScopeAction action,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(action) || reference.Kind == ResourceKind.Attachment) return false; // files outside A1
        var now = clock.Read();
        if (now is not { Valid: true } || now.Earliest < token.NotBefore || now.Latest >= token.ExpiresAt) return false;
        var resource = await resources.ResolveResource(reference, cancellationToken);
        if (resource == null || !ScopePolicy.Allows(token.Authority, resource, action, token.Identity.ActorId)) return false;
        // Availability errors propagate without an allow result. No allow cache.
        if (!await processing.Allows(resource, token.Identity.ActorId, action, cancellationToken)) return false;
        now = clock.Read();
        return now is { Valid: true } && now.Earliest >= token.NotBefore && now.Latest < token.ExpiresAt;
    }
}
