using System.Security.Cryptography;
using System.Text;
using Lms.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure;

// All operations own one transaction on a fresh context. Lock order is security,
// Identity user, mapping, session. Future password/block/mapping writers must use
// the same security lock and revoke sessions atomically (S6). No HTTP registration.
public sealed class PostgresIdentitySessions(LmsDbContext db, TimeProvider clock) : IIdentitySessions
{
    private readonly PasswordHasher<IdentityUser<Guid>> hasher = new();

    public async Task<SessionResult> Authenticate(string userName, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName) || userName.Length > 256 || !SessionPolicy.ValidPasswordLength(password))
            return Denied();
        var normalized = new UpperInvariantLookupNormalizer().NormalizeName(userName);
        var userId = await db.Users.AsNoTracking().Where(x => x.NormalizedUserName == normalized)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
        if (userId is null) return Denied();
        RequireFreshContext();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var account = await LockAccount(userId.Value, cancellationToken);
        var now = Now();
        if (!Enabled(account, now) || account!.Value.User.PasswordHash is not { } hash) return Denied();
        var user = account.Value.User;
        var verified = hasher.VerifyHashedPassword(user, hash, password);
        if (verified == PasswordVerificationResult.Failed) return Denied();
        if (verified == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = hasher.HashPassword(user, password);
        var session = new IdentitySession { SessionId = Guid.NewGuid(), UserId = user.Id,
            CreatedAt = now, LastSuccessfulRefreshAt = now, AbsoluteExpiresAt = now + SessionPolicy.AbsoluteLifetime,
            Generation = 1, Version = 1 };
        var secret = NewSecret();
        db.Add(session);
        db.Add(Verifier(session, secret, now));
        Audit(session, "session_created", now);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return Success(session, account.Value.Link, secret);
    }

    public async Task<SessionResult> Rotate(RefreshSecret token, CancellationToken cancellationToken = default)
    {
        var digest = Digest(token);
        if (digest is null) return Denied();
        // Only a digest ever reaches EF parameters/logs. Indexed discovery is not authority.
        var candidate = await (from v in db.Set<RefreshVerifier>().AsNoTracking()
                               join s in db.Set<IdentitySession>().AsNoTracking() on v.SessionId equals s.SessionId
                               where v.KeyVersion == 1 && v.Verifier == digest
                               select new { s.UserId, s.SessionId, v.Generation }).SingleOrDefaultAsync(cancellationToken);
        if (candidate is null) return Denied();
        RequireFreshContext();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var account = await LockAccount(candidate.UserId, cancellationToken);
        var session = await LockSession(candidate.SessionId, cancellationToken);
        var now = Now(); // Read after waiting for locks; never extend life using request-start time.
        if (session is null || session.RevokedAt is not null) return Denied();
        if (!Enabled(account, now) || SessionPolicy.Expired(now, session.LastSuccessfulRefreshAt, session.AbsoluteExpiresAt))
        {
            Retire(session, "session_unavailable", now);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return Denied();
        }
        var predecessor = await db.Set<RefreshVerifier>().SingleAsync(x => x.SessionId == session.SessionId &&
            x.Generation == candidate.Generation, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(predecessor.Verifier, digest)) return Denied();
        if (predecessor.ConsumedAt is { } consumed)
        {
            if (now >= consumed && now - consumed <= SessionPolicy.ConcurrentReplayWindow)
                return new(SessionOutcome.RefreshAlreadyRotated);
            Retire(session, "refresh_replay", now);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return Denied();
        }
        if (predecessor.Generation != session.Generation) return Denied();
        predecessor.ConsumedAt = now;
        // Flush consume before inserting successor: respect the partial unique live index.
        await db.SaveChangesAsync(cancellationToken);
        session.Generation++;
        session.Version++;
        session.LastSuccessfulRefreshAt = now;
        var successor = NewSecret();
        db.Add(Verifier(session, successor, now));
        Audit(session, "session_rotated", now);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return Success(session, account!.Value.Link, successor);
    }

    public async Task<SessionIdentity?> Read(Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        RequireFreshContext();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var account = await LockAccount(userId, cancellationToken);
        var session = await LockSession(sessionId, cancellationToken);
        var now = Now();
        if (!Enabled(account, now) || session is null || session.UserId != userId || session.RevokedAt is not null ||
            SessionPolicy.Expired(now, session.LastSuccessfulRefreshAt, session.AbsoluteExpiresAt)) return null;
        return Identity(session, account!.Value.Link);
    }

    public async Task Revoke(Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        RequireFreshContext();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAccount(userId, cancellationToken);
        var session = await LockSession(sessionId, cancellationToken);
        if (session is not null && session.UserId == userId && session.RevokedAt is null)
            Retire(session, "session_revoked", Now());
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task RevokeAll(Guid userId, CancellationToken cancellationToken = default)
    {
        RequireFreshContext();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAccount(userId, cancellationToken);
        var sessions = await db.Set<IdentitySession>().FromSqlInterpolated(
            $"""SELECT * FROM "IdentitySessions" WHERE "UserId" = {userId} AND "RevokedAt" IS NULL ORDER BY "SessionId" FOR UPDATE""")
            .ToListAsync(cancellationToken);
        var now = Now();
        foreach (var session in sessions) Retire(session, "sessions_revoked", now);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    // A1 read-only issuance shares exactly the S2 account/session locks. Callback signs
    // before locks are released; no token is returned if reading/signing fails.
    internal async Task<AccessToken?> WithIssuingSession(Guid userId, Guid sessionId,
        Func<SessionIdentity, Task<AccessToken?>> issue, CancellationToken ct)
    {
        RequireFreshContext();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var account = await LockAccount(userId, ct);
        var session = await LockSession(sessionId, ct);
        var now = Now();
        if (!Enabled(account, now) || session == null || session.UserId != userId || session.RevokedAt != null ||
            now < session.CreatedAt || SessionPolicy.Expired(now, session.LastSuccessfulRefreshAt, session.AbsoluteExpiresAt)) return null;
        var expiry = session.LastSuccessfulRefreshAt + SessionPolicy.IdleTimeout;
        if (session.AbsoluteExpiresAt < expiry) expiry = session.AbsoluteExpiresAt;
        return await issue(Identity(session, account!.Value.Link) with { AbsoluteExpiresAt = expiry });
    }

    private void RequireFreshContext()
    {
        if (db.ChangeTracker.Entries().Any())
            throw new InvalidOperationException("Session operations require a fresh, dedicated DbContext.");
    }

    private async Task<(IdentityUser<Guid> User, ActorIdentity Link, AccountSecurityState Security)?> LockAccount(Guid userId, CancellationToken ct)
    {
        var security = await db.Set<AccountSecurityState>().FromSqlInterpolated(
            $"""SELECT * FROM "AccountSecurityState" WHERE "UserId" = {userId} FOR UPDATE""").SingleOrDefaultAsync(ct);
        if (security is null) return null;
        var user = await db.Users.FromSqlInterpolated(
            $"""SELECT * FROM "AspNetUsers" WHERE "Id" = {userId} FOR UPDATE""").SingleOrDefaultAsync(ct);
        var link = await db.Set<ActorIdentity>().FromSqlInterpolated(
            $"""SELECT * FROM "ActorIdentities" WHERE "UserId" = {userId} FOR UPDATE""").SingleOrDefaultAsync(ct);
        return user is null || link is null ? null : (user, link, security);
    }
    private Task<IdentitySession?> LockSession(Guid sessionId, CancellationToken ct)
        => db.Set<IdentitySession>().FromSqlInterpolated(
            $"""SELECT * FROM "IdentitySessions" WHERE "SessionId" = {sessionId} FOR UPDATE""").SingleOrDefaultAsync(ct);
    private static bool Enabled((IdentityUser<Guid> User, ActorIdentity Link, AccountSecurityState Security)? account, DateTimeOffset now)
        => account is { } a && a.Security.State == "Enabled" && a.Link.IsActive &&
           !(a.User.LockoutEnabled && a.User.LockoutEnd > now);
    private static SessionResult Denied() => new(SessionOutcome.Denied);
    private static SessionIdentity Identity(IdentitySession s, ActorIdentity a) => new(s.UserId, a.ActorId, s.SessionId, s.AbsoluteExpiresAt);
    private static SessionResult Success(IdentitySession s, ActorIdentity a, RefreshSecret token)
        => new(SessionOutcome.Success, Identity(s, a), token);
    private DateTimeOffset Now()
    {
        var now = clock.GetUtcNow();
        return new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero); // PG microsecond precision.
    }
    private static RefreshSecret NewSecret() => new(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
    private static byte[]? Digest(RefreshSecret secret)
    {
        var value = secret.Reveal();
        if (value.Length != 64 || value.Any(c => !char.IsAsciiHexDigit(c))) return null;
        // Exact canonical uppercase encoding; no equivalent secret spellings.
        if (value != value.ToUpperInvariant()) return null;
        return SHA256.HashData(Encoding.ASCII.GetBytes("lms-refresh-v1:" + value));
    }
    private static RefreshVerifier Verifier(IdentitySession s, RefreshSecret token, DateTimeOffset now)
        => new() { SessionId = s.SessionId, Generation = s.Generation, Verifier = Digest(token)!, KeyVersion = 1, CreatedAt = now };
    private void Retire(IdentitySession session, string action, DateTimeOffset now)
    {
        session.RevokedAt = now < session.CreatedAt ? session.CreatedAt : now;
        session.RevocationReasonRef = action;
        session.Version++;
        Audit(session, action, now);
    }
    private void Audit(IdentitySession session, string action, DateTimeOffset now)
        => db.Add(new SecurityAudit { AuditId = Guid.NewGuid(), Action = action, SubjectRef = session.UserId.ToString(),
            ResourceRef = session.SessionId.ToString(), Result = "success", AuthorityVersion = session.Generation,
            TraceId = Guid.NewGuid().ToString(), OccurredAt = now });
}
