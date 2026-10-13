namespace Lms.Infrastructure;

// Durable L1 foundations, separate from learning aggregates and future security handlers.
// Only controlled synthetic fixtures are permitted. Records do not grant runtime access.

public sealed class ActorRecord
{
    public Guid ActorId { get; set; }
    public string? PrimaryEducationalRole { get; set; }
}

public sealed class Organization
{
    public Guid OrganizationId { get; set; }
    public string State { get; set; } = null!;
    public bool SyntheticOnly { get; set; }
}

public sealed class OrganizationProfile
{
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = null!;
}

public sealed class OrganizationBootstrapReceipt
{
    public Guid CommandId { get; set; }
    public string PayloadDigest { get; set; } = null!;
    public string OperatorSubjectHash { get; set; } = null!;
    public Guid OrganizationId { get; set; }
    public Guid AdminUserId { get; set; }
    public Guid AdminActorId { get; set; }
    public Guid ChallengeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ActorIdentity
{
    public Guid UserId { get; set; }
    public Guid ActorId { get; set; }
    public bool IsActive { get; set; }
    public Guid ProvisioningOrgId { get; set; }
}

public sealed class OrganizationMembership
{
    public Guid OrganizationId { get; set; }
    public Guid ActorId { get; set; }
    public long Generation { get; set; }
    public string State { get; set; } = null!;
    public string? EducationalRole { get; set; }
    public int PermissionFlags { get; set; }
    public long Version { get; set; }
    public DateTimeOffset? RevocationRequestedAt { get; set; }
    public DateTimeOffset? AccessValidUntil { get; set; }
}

public sealed class CourseRegistration
{
    public string CourseId { get; set; } = null!;
    public Guid? OrganizationId { get; set; }
    public string MappingState { get; set; } = null!;
}

public sealed class CourseGroup
{
    public string CourseId { get; set; } = null!;
    public string GroupId { get; set; } = null!;
}

public sealed class CourseCatalogRecord
{
    public string CourseId { get; set; } = null!;
    public Guid OrganizationId { get; set; }
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string State { get; set; } = null!;
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CourseGroupCatalogRecord
{
    public string CourseId { get; set; } = null!;
    public string GroupId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CourseRosterCommandReceipt
{
    public Guid AdministratorActorId { get; set; }
    public string IdempotencyKey { get; set; } = null!;
    public Guid OrganizationId { get; set; }
    public string Kind { get; set; } = null!;
    public string PayloadDigest { get; set; } = null!;
    public string ResultRef { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class EnrollmentScope
{
    public Guid StudentId { get; set; }
    public string CourseId { get; set; } = null!;
    public Guid OrganizationId { get; set; }
    public string? GroupId { get; set; }
    public long MembershipGeneration { get; set; }
    public long EnrollmentGeneration { get; set; }
    public string State { get; set; } = null!;
    public long Version { get; set; }
    public DateTimeOffset? RevocationRequestedAt { get; set; }
    public DateTimeOffset? AccessValidUntil { get; set; }
}

public sealed class TeacherAssignmentScope
{
    public Guid GrantId { get; set; }
    public Guid TeacherId { get; set; }
    public string CourseId { get; set; } = null!;
    public string? GroupId { get; set; }
    public Guid OrganizationId { get; set; }
    public long MembershipGeneration { get; set; }
    public long Generation { get; set; }
    public string State { get; set; } = null!;
    public long Version { get; set; }
    public DateTimeOffset? RevocationRequestedAt { get; set; }
    public DateTimeOffset? AccessValidUntil { get; set; }
}

public sealed class CourseEditorScope
{
    public Guid GrantId { get; set; }
    public Guid EditorId { get; set; }
    public string CourseId { get; set; } = null!;
    public Guid OrganizationId { get; set; }
    public long MembershipGeneration { get; set; }
    public long Generation { get; set; }
    public string State { get; set; } = null!;
    public long Version { get; set; }
    public DateTimeOffset? RevocationRequestedAt { get; set; }
    public DateTimeOffset? AccessValidUntil { get; set; }
}

public sealed class ProcessingContextVersion
{
    public Guid ContextId { get; set; }
    public long Version { get; set; }
    public Guid OrganizationId { get; set; }
    public string? PurposeRef { get; set; }
    public string? LegalBasisRef { get; set; }
    public string? RetentionPolicyRef { get; set; }
    public string ApprovalState { get; set; } = null!;
    public bool SyntheticOnly { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
}

public sealed class CourseContextBinding
{
    public string CourseId { get; set; } = null!;
    public Guid OrganizationId { get; set; }
    public Guid ContextId { get; set; }
    public long ContextVersion { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
}

public sealed class ProcessingRestriction
{
    public Guid RestrictionId { get; set; }
    public Guid ContextId { get; set; }
    public long OriginVersion { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? SubjectActorId { get; set; }
    public string? ResourceRef { get; set; }
    public int Operations { get; set; }
    public string State { get; set; } = null!;
    public long Version { get; set; }
}

public sealed class AttachmentScope
{
    public Guid AttachmentId { get; set; }
    public Guid OrganizationId { get; set; }
    public string CourseId { get; set; } = null!;
    public Guid UploaderActorId { get; set; }
    public Guid ContextId { get; set; }
    public long ContextVersion { get; set; }
    public bool Quarantined { get; set; }
}

public sealed class ProvisioningRequest
{
    public Guid OrganizationId { get; set; }
    public string ExternalRosterKey { get; set; } = null!;
    public Guid? UserId { get; set; }
    public string PayloadDigest { get; set; } = null!;
    public string State { get; set; } = null!;
}

public sealed class OrganizationParticipantProfile
{
    public Guid OrganizationId { get; set; }
    public Guid ActorId { get; set; }
    public string ExternalRosterKey { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AccountLinkRequest
{
    public Guid RequestId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid TargetUserId { get; set; }
    public Guid TargetActorId { get; set; }
    public Guid InitiatorActorId { get; set; }
    public string ExternalRosterKey { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string EducationalRole { get; set; } = null!;
    public string State { get; set; } = null!;
    public long Version { get; set; }
    public string? VerificationRef { get; set; }
    public Guid? ApproverActorId { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}

public sealed class AccountSecurityState
{
    public Guid UserId { get; set; }
    public long CredentialEpoch { get; set; }
    public string State { get; set; } = null!;
    public long Version { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public string? ReasonRef { get; set; }
}

public sealed class IdentitySession
{
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSuccessfulRefreshAt { get; set; }
    public DateTimeOffset AbsoluteExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevocationReasonRef { get; set; }
    public long Generation { get; set; }
    public long Version { get; set; }
}

public sealed class RefreshVerifier
{
    public Guid SessionId { get; set; }
    public long Generation { get; set; }
    public byte[] Verifier { get; set; } = null!;
    public int KeyVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}

public sealed class RecoveryRequest
{
    public Guid RequestId { get; set; }
    public Guid TargetUserId { get; set; }
    public Guid InitiatingOrgId { get; set; }
    public Guid InitiatorActorId { get; set; }
    public Guid? ApproverActorId { get; set; }
    public string State { get; set; } = null!;
    public long Version { get; set; }
    public string? VerificationRef { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
}

public sealed class AccessChallenge
{
    public Guid ChallengeId { get; set; }
    public Guid UserId { get; set; }
    public string Purpose { get; set; } = null!;
    public string State { get; set; } = null!;
    public byte[] SecretMac { get; set; } = null!;
    public int KeyVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? TerminalAt { get; set; }
    public int FailedAttempts { get; set; }
    public long Version { get; set; }
    public Guid? RecoveryRequestId { get; set; }
}

public sealed class ChallengeReceipt
{
    public Guid IssuerActorId { get; set; }
    public Guid TargetUserId { get; set; }
    public string Purpose { get; set; } = null!;
    public string IdempotencyKey { get; set; } = null!;
    public string PayloadDigest { get; set; } = null!;
    public Guid ChallengeId { get; set; }
}

public sealed class SecurityAudit
{
    public Guid AuditId { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid? ActorId { get; set; }
    public string Action { get; set; } = null!;
    public string SubjectRef { get; set; } = null!;
    public string? ResourceRef { get; set; }
    public string Result { get; set; } = null!;
    public long? AuthorityVersion { get; set; }
    public Guid? ContextId { get; set; }
    public long? ContextVersion { get; set; }
    public string TraceId { get; set; } = null!;
    public string? EvidenceRef { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

// Immutable server-captured affiliation for newly created scoped assignments only.
// Absence means legacy/unknown; it is never synthesized on read or moved with enrollment.
public sealed class AssignmentScope
{
    public Guid AssignmentId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid StudentId { get; set; }
    public string CourseId { get; set; } = null!;
    public string? GroupId { get; set; }
    public long MembershipGeneration { get; set; }
    public long EnrollmentGeneration { get; set; }
}

public sealed class AuthorityHistory
{
    public long Id { get; set; }
    public string Kind { get; set; } = null!;
    public Guid OrganizationId { get; set; }
    public Guid ActorId { get; set; }
    public string Snapshot { get; set; } = null!;
    public DateTimeOffset RecordedAt { get; set; }
}
