namespace Lms.Application;

public enum AccountRole { Student, Teacher }

public sealed record ProvisionAccountCommand(Guid OrganizationId, Guid AdministratorActorId,
    string ExternalRosterKey, string DisplayName, string UserName, AccountRole Role,
    string IdempotencyKey, string TraceId);

public sealed record ProvisionAccountResult(Guid UserId, Guid ActorId, Guid OrganizationId,
    string DisplayName, string UserName, AccountRole Role, string State, bool Created,
    Guid? ChallengeId, ActivationCode? ActivationCode, Guid? ApprovalRequestId);

public sealed record AccountListQuery(Guid OrganizationId, Guid AdministratorActorId,
    string? Search, AccountRole? Role, string? State, int Offset, int Limit);

public sealed record OrganizationAccount(Guid UserId, Guid ActorId, string ExternalRosterKey,
    string DisplayName, string UserName, AccountRole Role, string State,
    DateTimeOffset CreatedAt, int OrganizationCount);

public sealed record OrganizationAccountPage(IReadOnlyList<OrganizationAccount> Items, int Total,
    int Offset, int Limit);

public sealed record IssueActivationCommand(Guid OrganizationId, Guid AdministratorActorId,
    Guid ActorId, string IdempotencyKey, string TraceId);

public sealed record ActivationIssueResult(Guid ChallengeId, bool Created, ActivationCode? Code,
    DateTimeOffset ExpiresAt);

public sealed record ActivateAccountCommand(Guid OrganizationId, string UserName, string Code,
    string Password, string TraceId);

public sealed record RequestRecoveryCommand(Guid OrganizationId, Guid AdministratorActorId,
    Guid ActorId, string VerificationRef, string IdempotencyKey, string TraceId);

public sealed record RecoveryRequestResult(Guid RequestId, string State, bool RequiresIndependentApproval,
    Guid? ChallengeId, ActivationCode? RecoveryCode, DateTimeOffset? ExpiresAt);

public sealed record ConsumeRecoveryCommand(Guid OrganizationId, string UserName, string Code,
    string NewPassword, string TraceId);

public sealed record SecurityApprovalItem(Guid RequestId, string Kind, Guid OrganizationId,
    Guid TargetUserId, Guid InitiatorActorId, string State, DateTimeOffset RequestedAt);

public sealed record SecurityApprovalCommand(Guid RequestId, string VerificationRef,
    string OperatorSubjectHash, bool Approve, string TraceId);

public sealed record SecurityApprovalResult(Guid RequestId, string State, Guid? ChallengeId,
    ActivationCode? RecoveryCode, DateTimeOffset? ExpiresAt);

public interface IAccountAdministration
{
    Task<ProvisionAccountResult> Provision(ProvisionAccountCommand command, CancellationToken ct = default);
    Task<OrganizationAccountPage> List(AccountListQuery query, CancellationToken ct = default);
    Task<OrganizationAccount?> Get(Guid organizationId, Guid administratorActorId, Guid actorId,
        CancellationToken ct = default);
    Task<ActivationIssueResult> IssueActivation(IssueActivationCommand command, CancellationToken ct = default);
    Task Activate(ActivateAccountCommand command, CancellationToken ct = default);
    Task<RecoveryRequestResult> RequestRecovery(RequestRecoveryCommand command, CancellationToken ct = default);
    Task ConsumeRecovery(ConsumeRecoveryCommand command, CancellationToken ct = default);
}

public interface IAccountSecurityOperations
{
    Task<IReadOnlyList<SecurityApprovalItem>> Pending(string operatorSubjectHash, CancellationToken ct = default);
    Task<SecurityApprovalResult> Decide(SecurityApprovalCommand command, CancellationToken ct = default);
}
