namespace Lms.Application;

// Trusted internal ports, not request DTOs or educational authorization. S4 must
// authorize ingress, enforce Origin/CSRF and read current organization grants.
public sealed record IdentityLink(Guid UserId, Guid ActorId, bool IsActive);
public sealed record SessionIdentity(Guid UserId, Guid ActorId, Guid SessionId,
    DateTimeOffset AbsoluteExpiresAt);

// Deliberately not a record: generated ToString must never print a bearer secret.
public sealed class RefreshSecret(string value)
{
    public string Reveal() => value;
    public override string ToString() => "[REDACTED]";
}

public enum SessionOutcome { Success, Denied, RefreshAlreadyRotated }
public sealed class SessionResult(SessionOutcome outcome, SessionIdentity? identity = null,
    RefreshSecret? refreshToken = null, AccessToken? accessToken = null)
{
    public SessionOutcome Outcome { get; } = outcome;
    public SessionIdentity? Identity { get; } = identity;
    public RefreshSecret? RefreshToken { get; } = refreshToken;
    public AccessToken? AccessToken { get; } = accessToken;
    public bool HasEducationalScope => AccessToken != null;
    // S4 contract: conflict maps to 409 refresh_already_rotated, no JWT/Set-Cookie.
    public string? ErrorCode => Outcome switch
    {
        SessionOutcome.RefreshAlreadyRotated => "refresh_already_rotated",
        SessionOutcome.Denied => "invalid_session",
        _ => null
    };
}

public interface IIdentityMapping
{
    // Atomic internal foundation only; no activation/password/grants/provisioning workflow.
    Task<IdentityLink> CreateInactive(Guid userId, Guid actorId, Guid provisioningOrgId,
        string userName, CancellationToken cancellationToken = default);
    Task<IdentityLink?> Find(Guid userId, CancellationToken cancellationToken = default);
}

public interface IIdentitySessions
{
    // Credential verification and session creation are one security transaction.
    Task<SessionResult> Authenticate(string userName, string password, CancellationToken cancellationToken = default);
    Task<SessionResult> Rotate(RefreshSecret token, CancellationToken cancellationToken = default);
    // Cookie-authenticated logout. The opaque verifier is resolved and locked by the
    // implementation; callers never derive trusted user/session IDs from request data.
    Task<bool> Revoke(RefreshSecret token, CancellationToken cancellationToken = default);
    Task<SessionIdentity?> Read(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);
    // IDs here must come from a trusted host adapter, never an unauthenticated request.
    Task Revoke(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);
    Task RevokeAll(Guid userId, CancellationToken cancellationToken = default);
}

public static class SessionPolicy
{
    public static readonly TimeSpan MaximumAccessLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(12);
    public static readonly TimeSpan ConcurrentReplayWindow = TimeSpan.FromSeconds(30);
    public static bool ValidPasswordLength(string? password) => password is { Length: >= 12 and <= 128 };
    public static bool Expired(DateTimeOffset now, DateTimeOffset lastRefresh, DateTimeOffset absoluteExpiry)
        => now < lastRefresh || now >= lastRefresh + IdleTimeout || now >= absoluteExpiry;
}
