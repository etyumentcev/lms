using System.Data;
using System.Security.Cryptography;
using System.Text;
using Lms.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure;

public sealed class PostgresAccountAdministration(
    LmsDbContext db, TimeProvider clock, OrganizationBootstrapOptions options)
    : IAccountAdministration, IAccountSecurityOperations
{
    private readonly PasswordHasher<IdentityUser<Guid>> passwordHasher = new();

    public async Task<ProvisionAccountResult> Provision(ProvisionAccountCommand command, CancellationToken ct = default)
    {
        options.Validate();
        var roster = Required(command.ExternalRosterKey, 3, 128);
        var display = Required(command.DisplayName, 2, 160);
        var userName = Required(command.UserName, 3, 128);
        var idempotency = Required(command.IdempotencyKey, 1, 128);
        var normalized = new UpperInvariantLookupNormalizer().NormalizeName(userName)
            ?? throw new UseCaseException("ValidationFailed");
        var role = command.Role.ToString();
        var payload = Digest(normalized + "\n" + display + "\n" + role);
        var now = Now();

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await Advisory(command.OrganizationId + ":provision:" + roster, ct);
        var admin = await LockAdmin(command.OrganizationId, command.AdministratorActorId, ct);
        var prior = await db.Set<ProvisioningRequest>().SingleOrDefaultAsync(x =>
            x.OrganizationId == command.OrganizationId && x.ExternalRosterKey == roster, ct);
        if (prior != null)
        {
            if (prior.PayloadDigest != payload) throw new UseCaseException("IdempotencyConflict");
            if (prior.UserId is { } completed)
            {
                var result = await Result(command.OrganizationId, completed, false, null, null, ct);
                await tx.CommitAsync(ct);
                return result;
            }
            var link = await db.Set<AccountLinkRequest>().AsNoTracking().SingleAsync(x =>
                x.OrganizationId == command.OrganizationId && x.ExternalRosterKey == roster && x.State == "Requested", ct);
            await tx.CommitAsync(ct);
            return new(Guid.Empty, Guid.Empty, command.OrganizationId, display, userName, command.Role,
                "AwaitingIndependentApproval", false, null, null, link.RequestId);
        }

        var existingUser = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedUserName == normalized, ct);
        if (existingUser != null)
        {
            var mapping = await db.Set<ActorIdentity>().AsNoTracking().SingleOrDefaultAsync(x => x.UserId == existingUser.Id, ct)
                ?? throw new UseCaseException("IdentityAmbiguous");
            if (!mapping.IsActive) throw new UseCaseException("IdentityAmbiguous");
            if (await db.Set<OrganizationMembership>().AnyAsync(x => x.OrganizationId == command.OrganizationId && x.ActorId == mapping.ActorId, ct))
                throw new UseCaseException("DuplicateMembership");
            var requestId = Guid.NewGuid();
            db.AddRange(
                new ProvisioningRequest { OrganizationId = command.OrganizationId, ExternalRosterKey = roster,
                    PayloadDigest = payload, State = "Pending" },
                new AccountLinkRequest { RequestId = requestId, OrganizationId = command.OrganizationId,
                    TargetUserId = existingUser.Id, TargetActorId = mapping.ActorId,
                    InitiatorActorId = command.AdministratorActorId, ExternalRosterKey = roster,
                    DisplayName = display, EducationalRole = role, State = "Requested", Version = 1,
                    RequestedAt = now },
                Audit(command.OrganizationId, command.AdministratorActorId, "shared_identity_link_requested",
                    requestId, requestId, "pending_approval", admin.Version, command.TraceId, now));
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return new(Guid.Empty, Guid.Empty, command.OrganizationId, display, userName,
                command.Role, "AwaitingIndependentApproval", true, null, null, requestId);
        }

        await EnsureIssueBudget(Guid.Empty, command.AdministratorActorId, now, ct);
        var userId = Guid.NewGuid(); var actorId = Guid.NewGuid(); var challengeId = Guid.NewGuid();
        var code = AccessChallengeProtocol.Generate();
        var user = new IdentityUser<Guid> { Id = userId, UserName = userName, NormalizedUserName = normalized,
            SecurityStamp = Guid.NewGuid().ToString("N"), ConcurrencyStamp = Guid.NewGuid().ToString("N"), LockoutEnabled = true };
        db.AddRange(user,
            new ActorRecord { ActorId = actorId, PrimaryEducationalRole = role },
            new ActorIdentity { UserId = userId, ActorId = actorId, IsActive = false, ProvisioningOrgId = command.OrganizationId },
            new AccountSecurityState { UserId = userId, CredentialEpoch = 1, State = "Enabled", Version = 1, ChangedAt = now },
            new OrganizationMembership { OrganizationId = command.OrganizationId, ActorId = actorId, Generation = 1,
                State = "Active", EducationalRole = role, PermissionFlags = 0, Version = 1 },
            new OrganizationParticipantProfile { OrganizationId = command.OrganizationId, ActorId = actorId,
                ExternalRosterKey = roster, DisplayName = display, CreatedAt = now },
            new ProvisioningRequest { OrganizationId = command.OrganizationId, ExternalRosterKey = roster,
                UserId = userId, PayloadDigest = payload, State = "Completed" },
            Challenge(challengeId, userId, "Activation", code, now, now.AddHours(24), null),
            new ChallengeReceipt { IssuerActorId = command.AdministratorActorId, TargetUserId = userId,
                Purpose = "Activation", IdempotencyKey = idempotency, PayloadDigest = payload,
                ChallengeId = challengeId },
            Audit(command.OrganizationId, command.AdministratorActorId, "account_provisioned", userId,
                actorId, "pending_activation", admin.Version, command.TraceId, now));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); ct.ThrowIfCancellationRequested();
        return new(userId, actorId, command.OrganizationId, display, userName, command.Role,
            "PendingActivation", true, challengeId, new ActivationCode(AccessChallengeProtocol.Format(code)), null);
    }

    public async Task<OrganizationAccountPage> List(AccountListQuery query, CancellationToken ct = default)
    {
        if (!await CanAdmin(query.OrganizationId, query.AdministratorActorId, ct)) throw new UseCaseException("Forbidden");
        var search = query.Search?.Trim(); var role = query.Role?.ToString(); var state = query.State?.Trim();
        var rows = from profile in db.Set<OrganizationParticipantProfile>().AsNoTracking()
                   join membership in db.Set<OrganizationMembership>().AsNoTracking()
                       on new { profile.OrganizationId, profile.ActorId } equals new { membership.OrganizationId, membership.ActorId }
                   join mapping in db.Set<ActorIdentity>().AsNoTracking() on profile.ActorId equals mapping.ActorId
                   join user in db.Users.AsNoTracking() on mapping.UserId equals user.Id
                   join security in db.Set<AccountSecurityState>().AsNoTracking() on user.Id equals security.UserId
                   where profile.OrganizationId == query.OrganizationId && membership.State == "Active" &&
                         membership.EducationalRole != null && (role == null || membership.EducationalRole == role) &&
                         (string.IsNullOrEmpty(search) || EF.Functions.ILike(profile.DisplayName, "%" + search + "%") ||
                          EF.Functions.ILike(user.UserName!, "%" + search + "%") || EF.Functions.ILike(profile.ExternalRosterKey, "%" + search + "%"))
                   select new { profile, membership, mapping, user, security };
        if (!string.IsNullOrEmpty(state)) rows = rows.Where(x =>
            state == "Blocked" ? x.security.State == "SecurityBlocked" :
            state == "Active" ? x.security.State == "Enabled" && x.mapping.IsActive :
            state == "PendingActivation" && x.security.State == "Enabled" && !x.mapping.IsActive);
        var total = await rows.CountAsync(ct);
        var offset = Math.Max(0, query.Offset); var limit = Math.Clamp(query.Limit, 1, 100);
        var materialized = await rows.OrderBy(x => x.profile.DisplayName).ThenBy(x => x.profile.ActorId)
            .Skip(offset).Take(limit).ToListAsync(ct);
        return new(materialized.Select(x => new OrganizationAccount(x.user.Id, x.profile.ActorId,
            x.profile.ExternalRosterKey, x.profile.DisplayName, x.user.UserName!, Enum.Parse<AccountRole>(x.membership.EducationalRole!),
            x.security.State == "SecurityBlocked" ? "Blocked" : x.mapping.IsActive ? "Active" : "PendingActivation",
            x.profile.CreatedAt, 1)).ToList(), total, offset, limit);
    }

    public async Task<OrganizationAccount?> Get(Guid organizationId, Guid administratorActorId, Guid actorId, CancellationToken ct = default)
    {
        var page = await List(new(organizationId, administratorActorId, null, null, null, 0, 100), ct);
        return page.Items.SingleOrDefault(x => x.ActorId == actorId);
    }

    public async Task<ActivationIssueResult> IssueActivation(IssueActivationCommand command, CancellationToken ct = default)
    {
        options.Validate(); var now = Now(); var idempotency = Required(command.IdempotencyKey, 1, 128);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await Advisory(command.OrganizationId + ":activation:" + command.ActorId, ct);
        var admin = await LockAdmin(command.OrganizationId, command.AdministratorActorId, ct);
        var target = await Target(command.OrganizationId, command.ActorId, ct);
        if (target.Mapping.IsActive || target.Security.State != "Enabled") throw new UseCaseException("ActivationUnavailable");
        var receipt = await db.Set<ChallengeReceipt>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.IssuerActorId == command.AdministratorActorId && x.TargetUserId == target.User.Id &&
            x.Purpose == "Activation" && x.IdempotencyKey == idempotency, ct);
        if (receipt != null)
        {
            var previous = await db.Set<AccessChallenge>().AsNoTracking().SingleAsync(x => x.ChallengeId == receipt.ChallengeId, ct);
            await tx.CommitAsync(ct); return new(previous.ChallengeId, false, null, previous.ExpiresAt);
        }
        await EnsureIssueBudget(target.User.Id, command.AdministratorActorId, now, ct);
        await RevokeLive(target.User.Id, "Activation", now, ct);
        var challengeId = Guid.NewGuid(); var code = AccessChallengeProtocol.Generate();
        db.AddRange(Challenge(challengeId, target.User.Id, "Activation", code, now, now.AddHours(24), null),
            new ChallengeReceipt { IssuerActorId = command.AdministratorActorId, TargetUserId = target.User.Id,
                Purpose = "Activation", IdempotencyKey = idempotency,
                PayloadDigest = Digest(command.ActorId.ToString()), ChallengeId = challengeId },
            Audit(command.OrganizationId, command.AdministratorActorId, "activation_issued", target.User.Id,
                challengeId, "success", admin.Version, command.TraceId, now));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(challengeId, true, new ActivationCode(AccessChallengeProtocol.Format(code)), now.AddHours(24));
    }

    public Task Activate(ActivateAccountCommand command, CancellationToken ct = default)
        => Consume(command.OrganizationId, command.UserName, command.Code, command.Password, "Activation", command.TraceId, ct);

    public async Task<RecoveryRequestResult> RequestRecovery(RequestRecoveryCommand command, CancellationToken ct = default)
    {
        var verification = Required(command.VerificationRef, 3, 128); var idempotency = Required(command.IdempotencyKey, 1, 128); var now = Now();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await Advisory(command.OrganizationId + ":recovery:" + command.ActorId, ct);
        var admin = await LockAdmin(command.OrganizationId, command.AdministratorActorId, ct);
        var target = await Target(command.OrganizationId, command.ActorId, ct);
        if (!target.Mapping.IsActive || target.Security.State != "Enabled") throw new UseCaseException("RecoveryUnavailable");
        var prior = await db.Set<SecurityAudit>().AsNoTracking().Where(x => x.OrganizationId == command.OrganizationId &&
            x.ActorId == command.AdministratorActorId && x.Action == "recovery_requested" &&
            x.ResourceRef == idempotency).Select(x => x.SubjectRef).SingleOrDefaultAsync(ct);
        if (prior != null)
        {
            var requestId = Guid.Parse(prior); var request = await db.Set<RecoveryRequest>().AsNoTracking().SingleAsync(x => x.RequestId == requestId, ct);
            await tx.CommitAsync(ct); return new(requestId, request.State, true, null, null, null);
        }
        var id = Guid.NewGuid();
        var audit = Audit(command.OrganizationId, command.AdministratorActorId, "recovery_requested", id, id,
            "pending_approval", admin.Version, command.TraceId, now);
        audit.ResourceRef = idempotency;
        db.AddRange(new RecoveryRequest { RequestId = id, TargetUserId = target.User.Id,
                InitiatingOrgId = command.OrganizationId, InitiatorActorId = command.AdministratorActorId,
                State = "Requested", Version = 1, VerificationRef = verification, RequestedAt = now }, audit);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(id, "Requested", true, null, null, null);
    }

    public Task ConsumeRecovery(ConsumeRecoveryCommand command, CancellationToken ct = default)
        => Consume(command.OrganizationId, command.UserName, command.Code, command.NewPassword, "Recovery", command.TraceId, ct);

    public async Task<IReadOnlyList<SecurityApprovalItem>> Pending(string operatorSubjectHash, CancellationToken ct = default)
    {
        ValidateDigest(operatorSubjectHash);
        var recoveries = await db.Set<RecoveryRequest>().AsNoTracking().Where(x => x.State == "Requested")
            .OrderBy(x => x.RequestedAt).Take(100)
            .Select(x => new SecurityApprovalItem(x.RequestId, "Recovery", x.InitiatingOrgId,
                x.TargetUserId, x.InitiatorActorId, x.State, x.RequestedAt)).ToListAsync(ct);
        var links = await db.Set<AccountLinkRequest>().AsNoTracking().Where(x => x.State == "Requested")
            .OrderBy(x => x.RequestedAt).Take(100)
            .Select(x => new SecurityApprovalItem(x.RequestId, "SharedIdentityLink", x.OrganizationId,
                x.TargetUserId, x.InitiatorActorId, x.State, x.RequestedAt)).ToListAsync(ct);
        return recoveries.Concat(links).OrderBy(x => x.RequestedAt).Take(100).ToList();
    }

    public async Task<SecurityApprovalResult> Decide(SecurityApprovalCommand command, CancellationToken ct = default)
    {
        options.Validate(); ValidateDigest(command.OperatorSubjectHash);
        var verification = Required(command.VerificationRef, 3, 128); var now = Now();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await Advisory("security:" + command.RequestId, ct);
        var operatorId = OperatorActor(command.OperatorSubjectHash);
        if (!await db.Set<ActorRecord>().AnyAsync(x => x.ActorId == operatorId, ct)) db.Add(new ActorRecord { ActorId = operatorId });
        var recovery = await db.Set<RecoveryRequest>().FromSqlInterpolated(
            $"SELECT * FROM \"RecoveryRequests\" WHERE \"RequestId\"={command.RequestId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (recovery != null)
        {
            if (recovery.State != "Requested") throw new UseCaseException("ApprovalUnavailable");
            if (recovery.InitiatorActorId == operatorId) throw new UseCaseException("IndependentApprovalRequired");
            if (!command.Approve)
            {
                recovery.State = "Rejected"; recovery.Version++;
                db.Add(Audit(recovery.InitiatingOrgId, operatorId, "recovery_rejected", recovery.TargetUserId,
                    recovery.RequestId, "rejected", recovery.Version, command.TraceId, now));
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                return new(recovery.RequestId, recovery.State, null, null, null);
            }
            var security = await LockSecurity(recovery.TargetUserId, ct);
            if (security.State != "Enabled") throw new UseCaseException("RecoveryUnavailable");
            await EnsureIssueBudget(recovery.TargetUserId, operatorId, now, ct);
            await RevokeLive(recovery.TargetUserId, "Recovery", now, ct);
            var challengeId = Guid.NewGuid(); var code = AccessChallengeProtocol.Generate();
            recovery.State = "Approved"; recovery.ApproverActorId = operatorId; recovery.VerificationRef = verification;
            recovery.ApprovedAt = now; recovery.Version++;
            db.AddRange(Challenge(challengeId, recovery.TargetUserId, "Recovery", code, now, now.AddMinutes(15), recovery.RequestId),
                Audit(recovery.InitiatingOrgId, operatorId, "recovery_approved", recovery.TargetUserId,
                    recovery.RequestId, "success", recovery.Version, command.TraceId, now));
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return new(recovery.RequestId, recovery.State, challengeId,
                new ActivationCode(AccessChallengeProtocol.Format(code)), now.AddMinutes(15));
        }
        var link = await db.Set<AccountLinkRequest>().FromSqlInterpolated(
            $"SELECT * FROM \"AccountLinkRequests\" WHERE \"RequestId\"={command.RequestId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (link == null || link.State != "Requested") throw new UseCaseException("ApprovalUnavailable");
        if (link.InitiatorActorId == operatorId) throw new UseCaseException("IndependentApprovalRequired");
        link.ApproverActorId = operatorId; link.VerificationRef = verification; link.DecidedAt = now; link.Version++;
        if (!command.Approve)
        {
            link.State = "Rejected";
            db.Add(Audit(link.OrganizationId, operatorId, "shared_identity_link_rejected", link.RequestId,
                link.RequestId, "rejected", link.Version, command.TraceId, now));
        }
        else
        {
            if (await db.Set<OrganizationMembership>().AnyAsync(x => x.OrganizationId == link.OrganizationId && x.ActorId == link.TargetActorId, ct))
                throw new UseCaseException("DuplicateMembership");
            link.State = "Approved";
            db.AddRange(new OrganizationMembership { OrganizationId = link.OrganizationId, ActorId = link.TargetActorId,
                    Generation = 1, State = "Active", EducationalRole = link.EducationalRole, PermissionFlags = 0, Version = 1 },
                new OrganizationParticipantProfile { OrganizationId = link.OrganizationId, ActorId = link.TargetActorId,
                    ExternalRosterKey = link.ExternalRosterKey, DisplayName = link.DisplayName, CreatedAt = now },
                Audit(link.OrganizationId, operatorId, "shared_identity_link_approved", link.TargetUserId,
                    link.RequestId, "success", link.Version, command.TraceId, now));
            var provision = await db.Set<ProvisioningRequest>().SingleAsync(x => x.OrganizationId == link.OrganizationId &&
                x.ExternalRosterKey == link.ExternalRosterKey, ct);
            provision.UserId = link.TargetUserId; provision.State = "Completed";
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(link.RequestId, link.State, null, null, null);
    }

    private async Task Consume(Guid organizationId, string userName, string code, string password,
        string purpose, string trace, CancellationToken ct)
    {
        options.Validate();
        if (!SessionPolicy.ValidPasswordLength(password)) throw new UseCaseException("ValidationFailed");
        var normalized = new UpperInvariantLookupNormalizer().NormalizeName(Required(userName, 3, 128))!;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await Advisory(organizationId + ":consume:" + purpose + ":" + normalized, ct);
        var userId = await (from candidateUser in db.Users.AsNoTracking()
                            join candidateMapping in db.Set<ActorIdentity>().AsNoTracking() on candidateUser.Id equals candidateMapping.UserId
                            join membership in db.Set<OrganizationMembership>().AsNoTracking() on candidateMapping.ActorId equals membership.ActorId
                            where candidateUser.NormalizedUserName == normalized && membership.OrganizationId == organizationId && membership.State == "Active"
                            select (Guid?)candidateUser.Id).SingleOrDefaultAsync(ct);
        if (userId == null) throw new UseCaseException(purpose + "Invalid");
        var security = await LockSecurity(userId.Value, ct);
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\"={userId.Value} FOR UPDATE").SingleAsync(ct);
        var mapping = await db.Set<ActorIdentity>().FromSqlInterpolated($"SELECT * FROM \"ActorIdentities\" WHERE \"UserId\"={userId.Value} FOR UPDATE").SingleAsync(ct);
        var challenge = await db.Set<AccessChallenge>().FromSqlInterpolated(
            $"SELECT * FROM \"AccessChallenges\" WHERE \"UserId\"={userId.Value} AND \"Purpose\"={purpose} ORDER BY \"CreatedAt\" DESC LIMIT 1 FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (security.State != "Enabled" || challenge == null) throw new UseCaseException(purpose + "Invalid");
        var now = Now();
        if (challenge.State == "Consumed") throw new UseCaseException(purpose + "Used");
        if (challenge.State == "Locked") throw new UseCaseException(purpose + "Locked");
        if (challenge.State == "Expired" || now >= challenge.ExpiresAt)
        {
            if (challenge.State == "Created") { challenge.State = "Expired"; challenge.TerminalAt = now; challenge.Version++; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
            throw new UseCaseException(purpose + "Expired");
        }
        if (challenge.State != "Created") throw new UseCaseException(purpose + "Invalid");
        var recentAccountFailures = await db.Set<SecurityAudit>().CountAsync(x => x.SubjectRef == user.Id.ToString() &&
            (x.Action == "activation_denied" || x.Action == "recovery_denied") && x.OccurredAt > now.AddMinutes(-15), ct);
        if (recentAccountFailures >= 5) throw new UseCaseException("ChallengeRateLimited");
        if (!AccessChallengeProtocol.Matches(options.ChallengeKey, challenge, code))
        {
            challenge.FailedAttempts++; challenge.Version++;
            if (challenge.FailedAttempts >= 5) { challenge.State = "Locked"; challenge.TerminalAt = now; }
            db.Add(Audit(organizationId, mapping.ActorId, purpose.ToLowerInvariant() + "_denied", user.Id,
                challenge.ChallengeId, challenge.State == "Locked" ? purpose + "Locked" : purpose + "Invalid",
                null, trace, now));
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            throw new UseCaseException(challenge.State == "Locked" ? purpose + "Locked" : purpose + "Invalid");
        }
        RecoveryRequest? request = null;
        if (purpose == "Recovery")
        {
            if (challenge.RecoveryRequestId == null) throw new UseCaseException("RecoveryInvalid");
            request = await db.Set<RecoveryRequest>().FromSqlInterpolated(
                $"SELECT * FROM \"RecoveryRequests\" WHERE \"RequestId\"={challenge.RecoveryRequestId.Value} FOR UPDATE").SingleAsync(ct);
            if (request.State != "Approved" || request.ApproverActorId == null || request.InitiatorActorId == request.ApproverActorId)
                throw new UseCaseException("IndependentApprovalRequired");
        }
        user.PasswordHash = passwordHasher.HashPassword(user, password); user.SecurityStamp = Guid.NewGuid().ToString("N");
        mapping.IsActive = true; security.CredentialEpoch++; security.Version++; security.ChangedAt = now;
        challenge.State = "Consumed"; challenge.TerminalAt = now; challenge.Version++;
        if (request != null) { request.State = "Fulfilled"; request.Version++; }
        var sessions = await db.Set<IdentitySession>().FromSqlInterpolated(
            $"SELECT * FROM \"IdentitySessions\" WHERE \"UserId\"={user.Id} AND \"RevokedAt\" IS NULL ORDER BY \"SessionId\" FOR UPDATE").ToListAsync(ct);
        foreach (var session in sessions) { session.RevokedAt = now; session.RevocationReasonRef = "recovery"; session.Version++; }
        db.Add(Audit(organizationId, mapping.ActorId, purpose == "Activation" ? "account_activated" : "recovery_consumed",
            user.Id, challenge.ChallengeId, "success", security.Version, trace, now));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    private async Task<(IdentityUser<Guid> User, ActorIdentity Mapping, AccountSecurityState Security)> Target(Guid org, Guid actor, CancellationToken ct)
    {
        var data = await (from profile in db.Set<OrganizationParticipantProfile>().AsNoTracking()
                          join mapping in db.Set<ActorIdentity>().AsNoTracking() on profile.ActorId equals mapping.ActorId
                          join user in db.Users.AsNoTracking() on mapping.UserId equals user.Id
                          join security in db.Set<AccountSecurityState>().AsNoTracking() on user.Id equals security.UserId
                          join membership in db.Set<OrganizationMembership>().AsNoTracking()
                              on new { profile.OrganizationId, profile.ActorId } equals new { membership.OrganizationId, membership.ActorId }
                          where profile.OrganizationId == org && profile.ActorId == actor && membership.State == "Active" && membership.EducationalRole != null
                          select new { user, mapping, security }).SingleOrDefaultAsync(ct);
        return data == null ? throw new UseCaseException("NotFound") : (data.user, data.mapping, data.security);
    }

    private async Task<ProvisionAccountResult> Result(Guid org, Guid userId, bool created, ActivationCode? code, Guid? challenge, CancellationToken ct)
    {
        var x = await (from user in db.Users.AsNoTracking()
                       join mapping in db.Set<ActorIdentity>().AsNoTracking() on user.Id equals mapping.UserId
                       join profile in db.Set<OrganizationParticipantProfile>().AsNoTracking() on mapping.ActorId equals profile.ActorId
                       join membership in db.Set<OrganizationMembership>().AsNoTracking()
                           on new { profile.OrganizationId, profile.ActorId } equals new { membership.OrganizationId, membership.ActorId }
                       join security in db.Set<AccountSecurityState>().AsNoTracking() on user.Id equals security.UserId
                       where user.Id == userId && profile.OrganizationId == org
                       select new { user, mapping, profile, membership, security }).SingleAsync(ct);
        return new(x.user.Id, x.mapping.ActorId, org, x.profile.DisplayName, x.user.UserName!,
            Enum.Parse<AccountRole>(x.membership.EducationalRole!), x.security.State == "SecurityBlocked" ? "Blocked" :
            x.mapping.IsActive ? "Active" : "PendingActivation", created, challenge, code, null);
    }

    private async Task<OrganizationMembership> LockAdmin(Guid org, Guid actor, CancellationToken ct)
    {
        var membership = await db.Set<OrganizationMembership>().FromSqlInterpolated(
            $"SELECT * FROM \"OrganizationMemberships\" WHERE \"OrganizationId\"={org} AND \"ActorId\"={actor} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (membership is not { State: "Active", EducationalRole: null } ||
            !OrganizationPermissionPolicy.CanManageOrganization(new(membership.OrganizationId, membership.ActorId,
                membership.Generation, membership.Version, null, membership.PermissionFlags))) throw new UseCaseException("Forbidden");
        return membership;
    }
    private Task<bool> CanAdmin(Guid org, Guid actor, CancellationToken ct) => db.Set<OrganizationMembership>().AsNoTracking().AnyAsync(x =>
        x.OrganizationId == org && x.ActorId == actor && x.State == "Active" && x.EducationalRole == null &&
        x.PermissionFlags == (int)OrganizationPermission.ManageOrganization, ct);
    private Task<AccountSecurityState> LockSecurity(Guid userId, CancellationToken ct) => db.Set<AccountSecurityState>()
        .FromSqlInterpolated($"SELECT * FROM \"AccountSecurityState\" WHERE \"UserId\"={userId} FOR UPDATE").SingleAsync(ct);
    private async Task Advisory(string key, CancellationToken ct) => await db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
    private async Task RevokeLive(Guid userId, string purpose, DateTimeOffset now, CancellationToken ct)
    {
        var live = await db.Set<AccessChallenge>().Where(x => x.UserId == userId && x.Purpose == purpose && x.State == "Created").ToListAsync(ct);
        foreach (var item in live) { item.State = now >= item.ExpiresAt ? "Expired" : "Revoked"; item.TerminalAt = now; item.Version++; }
        if (live.Count > 0) await db.SaveChangesAsync(ct);
    }
    private async Task EnsureIssueBudget(Guid userId, Guid initiator, DateTimeOffset now, CancellationToken ct)
    {
        if (userId != Guid.Empty)
        {
            var recent = await db.Set<AccessChallenge>().CountAsync(x => x.UserId == userId && x.CreatedAt > now.AddHours(-1), ct);
            var latest = await db.Set<AccessChallenge>().Where(x => x.UserId == userId).MaxAsync(x => (DateTimeOffset?)x.CreatedAt, ct);
            if (recent >= 3) throw new UseCaseException("ChallengeRateLimited");
            if (latest != null && now - latest < TimeSpan.FromSeconds(60)) throw new UseCaseException("ChallengeCooldown");
        }
        var issued = await db.Set<SecurityAudit>().CountAsync(x => x.ActorId == initiator &&
            (x.Action == "activation_issued" || x.Action == "account_provisioned" || x.Action == "recovery_approved") &&
            x.OccurredAt > now.AddHours(-1), ct);
        if (issued >= options.MaxIssuesPerInitiatorPerHour) throw new UseCaseException("ChallengeRateLimited");
    }
    private AccessChallenge Challenge(Guid id, Guid userId, string purpose, string code, DateTimeOffset created,
        DateTimeOffset expires, Guid? recovery) => new() { ChallengeId = id, UserId = userId, Purpose = purpose,
        State = "Created", SecretMac = AccessChallengeProtocol.Mac(options.ChallengeKey, id, purpose, code),
        KeyVersion = options.KeyVersion, CreatedAt = created, ExpiresAt = expires, FailedAttempts = 0,
        Version = 1, RecoveryRequestId = recovery };
    private static SecurityAudit Audit(Guid org, Guid? actor, string action, Guid subject, Guid resource,
        string result, long? version, string trace, DateTimeOffset at) => new() { AuditId = Guid.NewGuid(),
        OrganizationId = org, ActorId = actor, Action = action, SubjectRef = subject.ToString(), ResourceRef = resource.ToString(),
        Result = result, AuthorityVersion = version, TraceId = SafeTrace(trace), OccurredAt = at };
    private static Guid OperatorActor(string hash) => new(SHA256.HashData(Encoding.ASCII.GetBytes("m1-security-operator:" + hash))[..16]);
    private DateTimeOffset Now() { var value = clock.GetUtcNow(); return new(value.Ticks - value.Ticks % 10, TimeSpan.Zero); }
    private static string Required(string? value, int min, int max)
    { var result = value?.Trim(); return result is { } && result.Length >= min && result.Length <= max ? result : throw new UseCaseException("ValidationFailed"); }
    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void ValidateDigest(string value) { if (value.Length != 64 || value.Any(c => !char.IsAsciiHexDigit(c))) throw new UseCaseException("Forbidden"); }
    private static string SafeTrace(string trace) => string.IsNullOrWhiteSpace(trace) ? Guid.NewGuid().ToString("N") : trace[..Math.Min(128, trace.Length)];
}
