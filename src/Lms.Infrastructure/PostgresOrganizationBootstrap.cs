using System.Data;
using System.Security.Cryptography;
using System.Text;
using Lms.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure;

public sealed class OrganizationBootstrapOptions
{
    public required byte[] ChallengeKey { get; init; }
    public int KeyVersion { get; init; } = 1;
    public int MaxIssuesPerInitiatorPerHour { get; init; } = 20;

    public void Validate()
    {
        if (ChallengeKey is not { Length: >= 32 } || KeyVersion <= 0 || MaxIssuesPerInitiatorPerHour is < 1 or > 400)
            throw new InvalidOperationException(
                "Bootstrap challenge key must contain at least 256 bits; key version and issue limit (1..400) must be positive.");
    }
}

public sealed class PostgresOrganizationBootstrap(
    LmsDbContext db, TimeProvider clock, OrganizationBootstrapOptions options) : IOrganizationBootstrap
{
    private readonly PasswordHasher<IdentityUser<Guid>> passwordHasher = new();

    public async Task<BootstrapOrganizationResult> Create(BootstrapOrganizationCommand command,
        CancellationToken cancellationToken = default)
    {
        options.Validate();
        if (command.CommandId == Guid.Empty || string.IsNullOrWhiteSpace(command.OrganizationName) ||
            string.IsNullOrWhiteSpace(command.AdminUserName) || string.IsNullOrWhiteSpace(command.OperatorSubjectHash))
            throw new UseCaseException("ValidationFailed");
        var name = command.OrganizationName.Trim();
        var userName = command.AdminUserName.Trim();
        var normalized = new UpperInvariantLookupNormalizer().NormalizeName(userName);
        if (name.Length is < 2 or > 160 ||
            userName.Length is < 3 or > 128 || normalized == null || normalized.Length > 256 ||
            !DigestString(command.OperatorSubjectHash)) throw new UseCaseException("ValidationFailed");
        var payloadDigest = HexDigest(name + "\n" + normalized);

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({command.CommandId.ToString()}, 0))", cancellationToken);
        var previous = await db.Set<OrganizationBootstrapReceipt>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CommandId == command.CommandId, cancellationToken);
        if (previous != null)
        {
            if (previous.PayloadDigest != payloadDigest || previous.OperatorSubjectHash != command.OperatorSubjectHash)
                throw new UseCaseException("IdempotencyConflict");
            var previousName = await db.Set<OrganizationProfile>().Where(x => x.OrganizationId == previous.OrganizationId)
                .Select(x => x.Name).SingleAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return new(command.CommandId, previous.OrganizationId, previousName,
                previous.AdminUserId, previous.AdminActorId, previous.ChallengeId, false, null);
        }
        if (await db.Users.AnyAsync(x => x.NormalizedUserName == normalized, cancellationToken))
            throw new UseCaseException("DuplicateIdentity");

        var now = Now();
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var code = AccessChallengeProtocol.Generate();
        var user = new IdentityUser<Guid>
        {
            Id = userId,
            UserName = userName,
            NormalizedUserName = normalized,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            LockoutEnabled = true
        };
        db.AddRange(
            new Organization { OrganizationId = organizationId, State = "Enabled", SyntheticOnly = true },
            new OrganizationProfile { OrganizationId = organizationId, Name = name },
            new ActorRecord { ActorId = actorId },
            user,
            new ActorIdentity { UserId = userId, ActorId = actorId, IsActive = false, ProvisioningOrgId = organizationId },
            new AccountSecurityState { UserId = userId, CredentialEpoch = 1, State = "Enabled", Version = 1, ChangedAt = now },
            new OrganizationMembership { OrganizationId = organizationId, ActorId = actorId, Generation = 1,
                State = "Active", PermissionFlags = (int)OrganizationPermission.ManageOrganization, Version = 1 },
            new AccessChallenge { ChallengeId = challengeId, UserId = userId, Purpose = "Activation", State = "Created",
                SecretMac = AccessChallengeProtocol.Mac(options.ChallengeKey, challengeId, "Activation", code), KeyVersion = options.KeyVersion, CreatedAt = now,
                ExpiresAt = now.AddHours(24), FailedAttempts = 0, Version = 1 },
            new OrganizationBootstrapReceipt { CommandId = command.CommandId, PayloadDigest = payloadDigest,
                OperatorSubjectHash = command.OperatorSubjectHash, OrganizationId = organizationId, AdminUserId = userId,
                AdminActorId = actorId, ChallengeId = challengeId, CreatedAt = now },
            new SecurityAudit { AuditId = Guid.NewGuid(), OrganizationId = organizationId,
                Action = "organization_bootstrapped", SubjectRef = organizationId.ToString(),
                ResourceRef = actorId.ToString(), Result = "pending_activation", AuthorityVersion = 1,
                TraceId = SafeTrace(command.TraceId), EvidenceRef = "platform:" + command.OperatorSubjectHash, OccurredAt = now });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(command.CommandId, organizationId, name, userId, actorId, challengeId, true,
            new ActivationCode(AccessChallengeProtocol.Format(code)));
    }

    public async Task Activate(ActivateFirstAdministratorCommand command,
        CancellationToken cancellationToken = default)
    {
        options.Validate();
        if (command.OrganizationId == Guid.Empty || string.IsNullOrWhiteSpace(command.UserName) ||
            string.IsNullOrWhiteSpace(command.ActivationCode) || command.Password == null)
            throw new UseCaseException("ValidationFailed");
        var normalized = new UpperInvariantLookupNormalizer().NormalizeName(command.UserName.Trim());
        if (normalized == null || normalized.Length > 256 ||
            !SessionPolicy.ValidPasswordLength(command.Password)) throw new UseCaseException("ValidationFailed");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({command.OrganizationId + ":" + normalized}, 0))", cancellationToken);
        var userId = await (from u in db.Users.AsNoTracking()
                            join mapping in db.Set<ActorIdentity>().AsNoTracking() on u.Id equals mapping.UserId
                            where mapping.ProvisioningOrgId == command.OrganizationId && u.NormalizedUserName == normalized
                            select (Guid?)u.Id).SingleOrDefaultAsync(cancellationToken);
        if (userId == null) throw new UseCaseException("ActivationUnavailable");
        var security = await db.Set<AccountSecurityState>().FromSqlInterpolated(
            $"SELECT * FROM \"AccountSecurityState\" WHERE \"UserId\"={userId.Value} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        var user = await db.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"Id\"={userId.Value} FOR UPDATE")
            .SingleAsync(cancellationToken);
        var link = await db.Set<ActorIdentity>().FromSqlInterpolated(
            $"SELECT * FROM \"ActorIdentities\" WHERE \"UserId\"={userId.Value} FOR UPDATE")
            .SingleAsync(cancellationToken);
        var membership = await db.Set<OrganizationMembership>().FromSqlInterpolated(
            $"SELECT * FROM \"OrganizationMemberships\" WHERE \"OrganizationId\"={command.OrganizationId} AND \"ActorId\"={link.ActorId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        var challenge = await db.Set<AccessChallenge>().FromSqlInterpolated(
            $"SELECT * FROM \"AccessChallenges\" WHERE \"UserId\"={userId.Value} AND \"Purpose\"='Activation' ORDER BY \"CreatedAt\" DESC LIMIT 1 FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (security is not { State: "Enabled" } || membership is not { State: "Active", EducationalRole: null } ||
            !OrganizationPermissionPolicy.CanManageOrganization(new(membership.OrganizationId, membership.ActorId,
                membership.Generation, membership.Version, null, membership.PermissionFlags)) || challenge == null)
            throw new UseCaseException("ActivationUnavailable");

        var now = Now();
        string? denied = null;
        if (challenge.State != "Created") denied = "ActivationUnavailable";
        else if (now >= challenge.ExpiresAt)
        {
            challenge.State = "Expired"; challenge.TerminalAt = now; challenge.Version++;
            denied = "ActivationExpired";
        }
        else
        {
            if (!AccessChallengeProtocol.Matches(options.ChallengeKey, challenge, command.ActivationCode))
            {
                challenge.FailedAttempts++;
                challenge.Version++;
                if (challenge.FailedAttempts >= 5)
                { challenge.State = "Locked"; challenge.TerminalAt = now; }
                denied = challenge.State == "Locked" ? "ActivationLocked" : "InvalidActivationCode";
            }
        }
        if (denied == null)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, command.Password);
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            link.IsActive = true;
            challenge.State = "Consumed";
            challenge.TerminalAt = now;
            challenge.Version++;
        }
        db.Add(new SecurityAudit { AuditId = Guid.NewGuid(), OrganizationId = command.OrganizationId,
            ActorId = link.ActorId, Action = denied == null ? "first_admin_activated" : "first_admin_activation_denied",
            SubjectRef = user.Id.ToString(), ResourceRef = challenge.ChallengeId.ToString(),
            Result = denied == null ? "success" : denied, AuthorityVersion = membership.Version,
            TraceId = SafeTrace(command.TraceId), OccurredAt = now });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        if (denied != null) throw new UseCaseException(denied);
    }

    private DateTimeOffset Now()
    {
        var value = clock.GetUtcNow();
        return new(value.Ticks - value.Ticks % 10, TimeSpan.Zero);
    }
    private static string HexDigest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool DigestString(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigit) && value == value.ToUpperInvariant();
    private static string SafeTrace(string trace) => string.IsNullOrWhiteSpace(trace) ? Guid.NewGuid().ToString("N") : trace[..Math.Min(trace.Length, 128)];
}

public sealed class PostgresOrganizationAdministrationQueries(LmsDbContext db) : IOrganizationAdministrationQueries
{
    public async Task<OrganizationAdminOverview?> Overview(Guid organizationId, Guid actorId,
        CancellationToken cancellationToken = default)
    {
        if (!await AdminMembership(organizationId, actorId).AnyAsync(cancellationToken)) return null;
        return await (from organization in db.Set<Organization>().AsNoTracking()
                      join profile in db.Set<OrganizationProfile>().AsNoTracking()
                          on organization.OrganizationId equals profile.OrganizationId
                      join membership in db.Set<OrganizationMembership>().AsNoTracking()
                          on organization.OrganizationId equals membership.OrganizationId
                      join link in db.Set<ActorIdentity>().AsNoTracking() on membership.ActorId equals link.ActorId
                      join security in db.Set<AccountSecurityState>().AsNoTracking() on link.UserId equals security.UserId
                      where organization.OrganizationId == organizationId && membership.ActorId == actorId
                      select new OrganizationAdminOverview(organization.OrganizationId,
                          profile.Name, organization.State, actorId,
                          security.State == "SecurityBlocked" ? "Blocked" : link.IsActive ? "Active" : "PendingActivation"))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrganizationAuditItem>> Audit(Guid organizationId, Guid actorId, int limit,
        CancellationToken cancellationToken = default)
    {
        if (!await AdminMembership(organizationId, actorId).AnyAsync(cancellationToken))
            throw new UseCaseException("Forbidden");
        limit = Math.Clamp(limit, 1, 100);
        return await db.Set<SecurityAudit>().AsNoTracking().Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.AuditId).Take(limit)
            .Select(x => new OrganizationAuditItem(x.AuditId, x.Action, x.SubjectRef, x.ResourceRef,
                x.Result, x.AuthorityVersion, x.OccurredAt)).ToListAsync(cancellationToken);
    }

    private IQueryable<OrganizationMembership> AdminMembership(Guid organizationId, Guid actorId)
        => db.Set<OrganizationMembership>().AsNoTracking().Where(x => x.OrganizationId == organizationId &&
            x.ActorId == actorId && x.State == "Active" && x.EducationalRole == null &&
            (x.PermissionFlags & (int)OrganizationPermission.ManageOrganization) != 0);
}
