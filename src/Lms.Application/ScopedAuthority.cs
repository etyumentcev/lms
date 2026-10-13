namespace Lms.Application;

public enum ScopeRole { Student, Teacher }
public enum AuthorityKind { Membership, Enrollment, Teacher, CourseEditor }
public enum AuthorityOperation { Grant, Revoke, Move }
public enum ResourceKind { Course, Group, Assignment, Work, Submission, Review, Attachment }
public enum ScopeAction { Read, Submit, Review }

// Trusted internal command, never bind directly to a public request. S5 must establish the
// administrative caller's capability separately. expectedVersion=0 means create only.
public sealed record AuthorityCommand(AuthorityKind Kind, AuthorityOperation Operation, Guid OrganizationId,
    Guid ActorId, long ExpectedVersion, long? MembershipGeneration = null, string? CourseId = null,
    string? GroupId = null, Guid? GrantId = null, ScopeRole? Role = null, int Permissions = 0);
public sealed record AuthorityChange(long Generation, long Version, DateTimeOffset? AccessValidUntil, Guid? GrantId);
public sealed record MembershipAuthority(Guid OrganizationId, Guid ActorId, long Generation, long Version, ScopeRole? Role, int Permissions);
public sealed record EducationalGrant(AuthorityKind Kind, Guid? GrantId, string CourseId, string? GroupId,
    long MembershipGeneration, long Generation, long Version);
public sealed record CurrentAuthority(MembershipAuthority Membership, IReadOnlyList<EducationalGrant> Grants);
public sealed record ResourceReference(ResourceKind Kind, Guid Id = default, string? CourseId = null, string? GroupId = null);
public sealed record ResourceOwnership(Guid OrganizationId, string CourseId, string? GroupId = null,
    Guid? StudentId = null, Guid? AssignmentId = null, Guid? WorkId = null, Guid? SubmissionId = null,
    Guid? ReviewId = null, Guid? AttachmentId = null, Guid? UploaderActorId = null, bool PendingAttachment = false,
    long? MembershipGeneration = null, long? EnrollmentGeneration = null);

public static class ScopePolicy
{
    public static readonly TimeSpan RevocationWindow = TimeSpan.FromHours(2);
    public static bool Allows(CurrentAuthority authority, ResourceOwnership resource, ScopeAction action, Guid actorId)
    {
        if (!Enum.IsDefined(action) || authority.Membership.ActorId != actorId ||
            authority.Membership.OrganizationId != resource.OrganizationId ||
            resource.PendingAttachment && resource.UploaderActorId != actorId) return false;
        return authority.Grants.Any(g => g.MembershipGeneration == authority.Membership.Generation && g.CourseId == resource.CourseId &&
            (g.Kind switch
            {
                AuthorityKind.Enrollment => authority.Membership.Role == ScopeRole.Student && action != ScopeAction.Review &&
                    (resource.StudentId == null || resource.StudentId == actorId) &&
                    (resource.MembershipGeneration == null || g.MembershipGeneration >= resource.MembershipGeneration) &&
                    (resource.EnrollmentGeneration == null || g.Generation >= resource.EnrollmentGeneration) &&
                    // A move does not move old work. Its owner may still read it with a current course enrollment.
                    (resource.StudentId != null || resource.GroupId == null || resource.GroupId == g.GroupId),
                AuthorityKind.Teacher => authority.Membership.Role == ScopeRole.Teacher && action != ScopeAction.Submit &&
                    (g.GroupId == null || g.GroupId == resource.GroupId || resource.StudentId == null && resource.GroupId == null),
                _ => false
            }));
    }
}

public interface IScopedAuthorityAdministration
{
    Task<AuthorityChange> Change(AuthorityCommand command, CancellationToken cancellationToken = default);
}
public interface IScopedAuthorityReader
{
    Task<CurrentAuthority?> ResolveCurrent(Guid organizationId, Guid actorId, CancellationToken cancellationToken = default);
    Task<ResourceOwnership?> ResolveResource(ResourceReference reference, CancellationToken cancellationToken = default);
    Task<bool> CanAccess(Guid organizationId, Guid actorId, ResourceReference reference, ScopeAction action,
        CancellationToken cancellationToken = default);
}
