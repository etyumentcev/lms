namespace Lms.Application;

[Flags]
public enum OrganizationPermission
{
    None = 0,
    ManageOrganization = 1
}

public static class OrganizationPermissionPolicy
{
    public const int KnownMask = (int)OrganizationPermission.ManageOrganization;
    public static bool CanManageOrganization(MembershipAuthority membership)
        => membership.Role is null && membership.Permissions == (int)OrganizationPermission.ManageOrganization;
}

// The operator subject is received only from a validated platform bearer. It is
// hashed before persistence and never becomes an LMS Actor or educational grant.
public sealed record BootstrapOrganizationCommand(Guid CommandId, string OrganizationName,
    string AdminUserName, string OperatorSubjectHash, string TraceId);

public sealed class ActivationCode(string value)
{
    public string Reveal() => value;
    public override string ToString() => "[REDACTED]";
}

public sealed record BootstrapOrganizationResult(Guid CommandId, Guid OrganizationId,
    string OrganizationName, Guid AdminUserId, Guid AdminActorId, Guid ChallengeId,
    bool Created, ActivationCode? ActivationCode);

public sealed record ActivateFirstAdministratorCommand(Guid OrganizationId, string UserName,
    string ActivationCode, string Password, string TraceId);

public sealed record OrganizationAdminOverview(Guid OrganizationId, string OrganizationName,
    string State, Guid ActorId, string AdministratorState);

public sealed record OrganizationAuditItem(Guid AuditId, string Action, string SubjectRef,
    string? ResourceRef, string Result, long? AuthorityVersion, DateTimeOffset OccurredAt);

public interface IOrganizationBootstrap
{
    Task<BootstrapOrganizationResult> Create(BootstrapOrganizationCommand command,
        CancellationToken cancellationToken = default);
    Task Activate(ActivateFirstAdministratorCommand command,
        CancellationToken cancellationToken = default);
}

public interface IOrganizationAdministrationQueries
{
    Task<OrganizationAdminOverview?> Overview(Guid organizationId, Guid actorId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrganizationAuditItem>> Audit(Guid organizationId, Guid actorId, int limit,
        CancellationToken cancellationToken = default);
}
