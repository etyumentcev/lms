using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure;

internal static class SchemaFoundationMappings
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<ActorRecord>(e =>
        {
            e.ToTable("Actors");
            e.HasKey(x => x.ActorId);
            e.Property(x => x.ActorId).ValueGeneratedNever();
        });
        model.Entity<Organization>(e =>
        {
            e.ToTable("Organizations");
            e.HasKey(x => x.OrganizationId);
            e.Property(x => x.OrganizationId).ValueGeneratedNever();
        });
        model.Entity<OrganizationProfile>(e =>
        {
            e.ToTable("OrganizationProfiles");
            e.HasKey(x => x.OrganizationId);
            e.Property(x => x.OrganizationId).ValueGeneratedNever();
        });
        model.Entity<OrganizationBootstrapReceipt>(e =>
        {
            e.ToTable("OrganizationBootstrapReceipts");
            e.HasKey(x => x.CommandId);
            e.Property(x => x.CommandId).ValueGeneratedNever();
            e.HasIndex(x => x.OrganizationId).IsUnique();
            e.HasIndex(x => x.AdminUserId).IsUnique();
            e.HasIndex(x => x.AdminActorId).IsUnique();
            e.HasIndex(x => x.ChallengeId).IsUnique();
        });
        model.Entity<ActorIdentity>(e =>
        {
            e.ToTable("ActorIdentities");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).ValueGeneratedNever();
        });
        model.Entity<OrganizationMembership>(e =>
        {
            e.ToTable("OrganizationMemberships");
            e.HasKey(x => new { x.OrganizationId, x.ActorId });
            e.Property(x => x.OrganizationId).ValueGeneratedNever();
            e.Property(x => x.ActorId).ValueGeneratedNever();
            e.Property(x => x.Version).IsConcurrencyToken();
        });
        model.Entity<CourseRegistration>(e =>
        {
            e.ToTable("CourseRegistry");
            e.HasKey(x => x.CourseId);
        });
        model.Entity<CourseGroup>(e =>
        {
            e.ToTable("CourseGroups");
            e.HasKey(x => new { x.CourseId, x.GroupId });
        });
        model.Entity<CourseCatalogRecord>(e =>
        {
            e.ToTable("CourseCatalog");
            e.HasKey(x => x.CourseId);
            e.Property(x => x.Version).IsConcurrencyToken();
        });
        model.Entity<CourseGroupCatalogRecord>(e =>
        {
            e.ToTable("CourseGroupCatalog");
            e.HasKey(x => new { x.CourseId, x.GroupId });
            e.Property(x => x.Version).IsConcurrencyToken();
        });
        model.Entity<CourseRosterCommandReceipt>(e =>
        {
            e.ToTable("CourseRosterCommandReceipts");
            e.HasKey(x => new { x.AdministratorActorId, x.IdempotencyKey });
        });
        model.Entity<EnrollmentScope>(e =>
        {
            e.ToTable("EnrollmentScopes");
            e.HasKey(x => new { x.StudentId, x.CourseId });
            e.Property(x => x.StudentId).ValueGeneratedNever();
            e.Property(x => x.Version).IsConcurrencyToken();
        });
        model.Entity<TeacherAssignmentScope>(e =>
        {
            e.ToTable("TeacherAssignmentScopes");
            e.HasKey(x => x.GrantId);
            e.Property(x => x.GrantId).ValueGeneratedNever();
            e.Property(x => x.Version).IsConcurrencyToken();
        });
        model.Entity<ProcessingContextVersion>(e =>
        {
            e.ToTable("ProcessingContextVersions");
            e.HasKey(x => new { x.ContextId, x.Version });
            e.Property(x => x.ContextId).ValueGeneratedNever();
        });
        model.Entity<CourseContextBinding>(e =>
        {
            e.ToTable("CourseContextBindings");
            e.HasKey(x => new { x.CourseId, x.EffectiveFrom });
        });
        model.Entity<ProcessingRestriction>(e =>
        {
            e.ToTable("ProcessingRestrictions");
            e.HasKey(x => x.RestrictionId);
            e.Property(x => x.RestrictionId).ValueGeneratedNever();
            e.Property(x => x.Version).IsConcurrencyToken();
        });
        model.Entity<AttachmentScope>(e =>
        {
            e.ToTable("AttachmentScopes");
            e.HasKey(x => x.AttachmentId);
            e.Property(x => x.AttachmentId).ValueGeneratedNever();
        });
        model.Entity<ProvisioningRequest>(e =>
        {
            e.ToTable("ProvisioningRequests");
            e.HasKey(x => new { x.OrganizationId, x.ExternalRosterKey });
            e.Property(x => x.OrganizationId).ValueGeneratedNever();
        });
        model.Entity<OrganizationParticipantProfile>(e =>
        {
            e.ToTable("OrganizationParticipantProfiles");
            e.HasKey(x => new { x.OrganizationId, x.ActorId });
            e.Property(x => x.OrganizationId).ValueGeneratedNever();
            e.HasIndex(x => new { x.OrganizationId, x.ExternalRosterKey }).IsUnique();
        });
        model.Entity<AccountLinkRequest>(e =>
        {
            e.ToTable("AccountLinkRequests");
            e.HasKey(x => x.RequestId);
            e.Property(x => x.RequestId).ValueGeneratedNever();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.OrganizationId, x.ExternalRosterKey }).IsUnique();
        });
        model.Entity<AccountSecurityState>(e =>
        {
            e.ToTable("AccountSecurityState");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).ValueGeneratedNever();
            e.Property(x => x.Version).IsConcurrencyToken();
        });
        model.Entity<IdentitySession>(e =>
        {
            e.ToTable("IdentitySessions");
            e.HasKey(x => x.SessionId);
            e.Property(x => x.SessionId).ValueGeneratedNever();
            e.Property(x => x.Version).IsConcurrencyToken();
        });
        model.Entity<RefreshVerifier>(e =>
        {
            e.ToTable("RefreshVerifiers");
            e.HasKey(x => new { x.SessionId, x.Generation });
            e.Property(x => x.SessionId).ValueGeneratedNever();
        });
        model.Entity<RecoveryRequest>(e =>
        {
            e.ToTable("RecoveryRequests");
            e.HasKey(x => x.RequestId);
            e.Property(x => x.RequestId).ValueGeneratedNever();
            e.Property(x => x.Version).IsConcurrencyToken();
        });
        model.Entity<AccessChallenge>(e =>
        {
            e.ToTable("AccessChallenges");
            e.HasKey(x => x.ChallengeId);
            e.Property(x => x.ChallengeId).ValueGeneratedNever();
            e.Property(x => x.Version).IsConcurrencyToken();
        });
        model.Entity<ChallengeReceipt>(e =>
        {
            e.ToTable("ChallengeReceipts");
            e.HasKey(x => new { x.IssuerActorId, x.TargetUserId, x.Purpose, x.IdempotencyKey });
            e.Property(x => x.IssuerActorId).ValueGeneratedNever();
            e.Property(x => x.TargetUserId).ValueGeneratedNever();
        });
        model.Entity<SecurityAudit>(e =>
        {
            e.ToTable("SecurityAudit");
            e.HasKey(x => x.AuditId);
            e.Property(x => x.AuditId).ValueGeneratedNever();
        });
        model.Entity<ActorIdentity>().HasOne<IdentityUser<Guid>>().WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<ActorIdentity>().HasOne<ActorRecord>().WithMany().HasForeignKey("ActorId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<ActorIdentity>().HasOne<Organization>().WithMany().HasForeignKey("ProvisioningOrgId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<ActorIdentity>().HasIndex("ActorId").IsUnique();
        model.Entity<OrganizationMembership>().HasOne<Organization>().WithMany().HasForeignKey("OrganizationId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<OrganizationProfile>().HasOne<Organization>().WithOne().HasForeignKey<OrganizationProfile>("OrganizationId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<OrganizationBootstrapReceipt>().HasOne<Organization>().WithMany().HasForeignKey("OrganizationId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<OrganizationBootstrapReceipt>().HasOne<IdentityUser<Guid>>().WithMany().HasForeignKey("AdminUserId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<OrganizationBootstrapReceipt>().HasOne<ActorRecord>().WithMany().HasForeignKey("AdminActorId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<OrganizationBootstrapReceipt>().HasOne<AccessChallenge>().WithMany().HasForeignKey("ChallengeId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<OrganizationMembership>().HasOne<ActorRecord>().WithMany().HasForeignKey("ActorId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<OrganizationMembership>().HasIndex("ActorId","State","OrganizationId");
        model.Entity<CourseRegistration>().HasOne<Organization>().WithMany().HasForeignKey("OrganizationId").OnDelete(DeleteBehavior.Restrict);
        // PostgreSQL composite course/org FKs are migration-owned: EF alternate keys
        // would make nullable quarantine OrganizationId required. M1 also owns exclusion
        // constraints, immutable guards, and the deferred user -> actor mapping FK.
        model.Entity<CourseRegistration>().HasIndex("CourseId","OrganizationId").IsUnique();
        model.Entity<CourseGroup>().HasOne<CourseRegistration>().WithMany().HasForeignKey("CourseId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<CourseCatalogRecord>().HasOne<CourseRegistration>().WithOne().HasForeignKey<CourseCatalogRecord>("CourseId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<CourseCatalogRecord>().HasOne<Organization>().WithMany().HasForeignKey("OrganizationId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<CourseCatalogRecord>().HasIndex("OrganizationId", "CreatedAt");
        model.Entity<CourseGroupCatalogRecord>().HasOne<CourseGroup>().WithOne().HasForeignKey<CourseGroupCatalogRecord>("CourseId", "GroupId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<CourseGroupCatalogRecord>().HasIndex("CourseId", "Name").IsUnique();
        model.Entity<CourseRosterCommandReceipt>().HasOne<OrganizationMembership>().WithMany()
            .HasForeignKey("OrganizationId", "AdministratorActorId").HasPrincipalKey("OrganizationId", "ActorId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<EnrollmentScope>().HasOne<EnrollmentAccess>().WithMany().HasForeignKey("StudentId","CourseId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<EnrollmentScope>().HasOne<OrganizationMembership>().WithMany().HasForeignKey("OrganizationId","StudentId").HasPrincipalKey("OrganizationId","ActorId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<EnrollmentScope>().HasOne<CourseGroup>().WithMany().HasForeignKey("CourseId","GroupId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<EnrollmentScope>().HasIndex("OrganizationId","CourseId","State");
        model.Entity<TeacherAssignmentScope>().HasOne<TeacherAccess>().WithMany().HasForeignKey("GrantId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<TeacherAssignmentScope>().HasOne<OrganizationMembership>().WithMany().HasForeignKey("OrganizationId","TeacherId").HasPrincipalKey("OrganizationId","ActorId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<TeacherAssignmentScope>().HasOne<CourseGroup>().WithMany().HasForeignKey("CourseId","GroupId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<TeacherAssignmentScope>().HasIndex("TeacherId","CourseId","GroupId").IsUnique().HasFilter("\"State\" = 'Active' AND \"GroupId\" IS NOT NULL");
        model.Entity<TeacherAssignmentScope>().HasIndex("TeacherId","CourseId").IsUnique().HasFilter("\"State\" = 'Active' AND \"GroupId\" IS NULL");
        model.Entity<ProcessingContextVersion>().HasOne<Organization>().WithMany().HasForeignKey("OrganizationId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<ProcessingContextVersion>().HasAlternateKey(x => new { x.ContextId, x.Version, x.OrganizationId });
        model.Entity<CourseContextBinding>().HasOne<ProcessingContextVersion>().WithMany().HasForeignKey("ContextId","ContextVersion","OrganizationId").HasPrincipalKey("ContextId","Version","OrganizationId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<ProcessingRestriction>().HasOne<ProcessingContextVersion>().WithMany().HasForeignKey("ContextId","OriginVersion","OrganizationId").HasPrincipalKey("ContextId","Version","OrganizationId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<ProcessingRestriction>().HasOne<OrganizationMembership>().WithMany().HasForeignKey("OrganizationId","SubjectActorId").HasPrincipalKey("OrganizationId","ActorId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<ProcessingRestriction>().HasIndex("ContextId","State");
        model.Entity<ProcessingRestriction>().HasIndex("SubjectActorId","State");
        model.Entity<AttachmentScope>().HasOne<StoredAttachment>().WithMany().HasForeignKey("AttachmentId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<AttachmentScope>().HasOne<OrganizationMembership>().WithMany().HasForeignKey("OrganizationId","UploaderActorId").HasPrincipalKey("OrganizationId","ActorId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<AttachmentScope>().HasOne<ProcessingContextVersion>().WithMany().HasForeignKey("ContextId","ContextVersion","OrganizationId").HasPrincipalKey("ContextId","Version","OrganizationId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<AttachmentScope>().HasIndex("CourseId","OrganizationId");
        model.Entity<ProvisioningRequest>().HasOne<Organization>().WithMany().HasForeignKey("OrganizationId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<OrganizationParticipantProfile>().HasOne<Organization>().WithMany().HasForeignKey("OrganizationId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<OrganizationParticipantProfile>().HasOne<ActorRecord>().WithMany().HasForeignKey("ActorId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<AccountLinkRequest>().HasOne<Organization>().WithMany().HasForeignKey("OrganizationId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<AccountLinkRequest>().HasOne<IdentityUser<Guid>>().WithMany().HasForeignKey("TargetUserId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<AccountLinkRequest>().HasOne<ActorRecord>().WithMany().HasForeignKey("TargetActorId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<AccountLinkRequest>().HasOne<ActorRecord>().WithMany().HasForeignKey("InitiatorActorId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<AccountLinkRequest>().HasOne<ActorRecord>().WithMany().HasForeignKey("ApproverActorId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<ProvisioningRequest>().HasOne<IdentityUser<Guid>>().WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<AccountSecurityState>().HasOne<IdentityUser<Guid>>().WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<IdentitySession>().HasOne<IdentityUser<Guid>>().WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<IdentitySession>().HasIndex("UserId","RevokedAt");
        model.Entity<RefreshVerifier>().HasOne<IdentitySession>().WithMany().HasForeignKey("SessionId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<RefreshVerifier>().HasIndex(x => x.Verifier).IsUnique();
        model.Entity<RefreshVerifier>().HasIndex("SessionId").IsUnique().HasFilter("\"ConsumedAt\" IS NULL");
        model.Entity<RecoveryRequest>().HasOne<IdentityUser<Guid>>().WithMany().HasForeignKey("TargetUserId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<RecoveryRequest>().HasOne<OrganizationMembership>().WithMany().HasForeignKey("InitiatingOrgId","InitiatorActorId").HasPrincipalKey("OrganizationId","ActorId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<RecoveryRequest>().HasOne<ActorRecord>().WithMany().HasForeignKey("ApproverActorId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<RecoveryRequest>().HasAlternateKey(x => new { x.RequestId, x.TargetUserId });
        model.Entity<AccessChallenge>().HasOne<IdentityUser<Guid>>().WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<AccessChallenge>().HasOne<RecoveryRequest>().WithMany().HasForeignKey("RecoveryRequestId","UserId").HasPrincipalKey("RequestId","TargetUserId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<AccessChallenge>().HasIndex("UserId","Purpose").IsUnique().HasFilter("\"State\" = 'Created'");
        model.Entity<AccessChallenge>().HasIndex("UserId","ExpiresAt");
        model.Entity<AccessChallenge>().HasAlternateKey(x => new { x.ChallengeId, x.UserId, x.Purpose });
        model.Entity<ChallengeReceipt>().HasOne<ActorRecord>().WithMany().HasForeignKey("IssuerActorId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<ChallengeReceipt>().HasOne<AccessChallenge>().WithMany().HasForeignKey("ChallengeId","TargetUserId","Purpose").HasPrincipalKey("ChallengeId","UserId","Purpose").OnDelete(DeleteBehavior.Restrict);
        model.Entity<SecurityAudit>().HasOne<Organization>().WithMany().HasForeignKey("OrganizationId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<SecurityAudit>().HasOne<ActorRecord>().WithMany().HasForeignKey("ActorId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<SecurityAudit>().HasOne<ProcessingContextVersion>().WithMany().HasForeignKey("ContextId","ContextVersion","OrganizationId").HasPrincipalKey("ContextId","Version","OrganizationId").OnDelete(DeleteBehavior.Restrict);
        model.Entity<SecurityAudit>().HasIndex("OrganizationId","OccurredAt");
        model.Entity<SecurityAudit>().HasIndex("SubjectRef","OccurredAt");
        model.Entity<ActorRecord>().ToTable(t => t.HasCheckConstraint("CK_Actor_Role", "\"PrimaryEducationalRole\" IS NULL OR \"PrimaryEducationalRole\" IN ('Student','Teacher')"));
        model.Entity<Organization>().ToTable(t => t.HasCheckConstraint("CK_Organization_State", "\"State\" IN ('Enabled','Suspended')"));
        model.Entity<Organization>().ToTable(t => t.HasCheckConstraint("CK_Organization_Synthetic", "\"SyntheticOnly\""));
        model.Entity<OrganizationProfile>().ToTable(t => t.HasCheckConstraint("CK_OrganizationProfile_Name", "char_length(btrim(\"Name\")) BETWEEN 2 AND 160"));
        model.Entity<OrganizationBootstrapReceipt>().ToTable(t => t.HasCheckConstraint("CK_BootstrapReceipt_Digests", "char_length(\"PayloadDigest\") = 64 AND char_length(\"OperatorSubjectHash\") = 64"));
        model.Entity<OrganizationMembership>().ToTable(t => t.HasCheckConstraint("CK_Membership_Role", "\"EducationalRole\" IS NULL OR \"EducationalRole\" IN ('Student','Teacher')"));
        model.Entity<OrganizationMembership>().ToTable(t => t.HasCheckConstraint("CK_Membership_Permissions", "\"PermissionFlags\" BETWEEN 0 AND 15"));
        model.Entity<OrganizationMembership>().ToTable(t => t.HasCheckConstraint("CK_OrganizationMembership_State", "\"State\" IN ('Active','Revoked')"));
        model.Entity<OrganizationMembership>().ToTable(t => t.HasCheckConstraint("CK_OrganizationMembership_Generation", "\"Generation\" > 0 AND \"Version\" > 0"));
        model.Entity<OrganizationMembership>().ToTable(t => t.HasCheckConstraint("CK_OrganizationMembership_Deadline", "(\"RevocationRequestedAt\" IS NULL AND \"AccessValidUntil\" IS NULL AND \"State\" = 'Active') OR (\"RevocationRequestedAt\" IS NOT NULL AND \"AccessValidUntil\" IS NOT NULL AND \"State\" = 'Revoked' AND \"AccessValidUntil\" <= \"RevocationRequestedAt\" + interval '2 hours')"));
        model.Entity<EnrollmentScope>().ToTable(t => t.HasCheckConstraint("CK_EnrollmentScope_State", "\"State\" IN ('Active','Revoked')"));
        model.Entity<EnrollmentScope>().ToTable(t => t.HasCheckConstraint("CK_EnrollmentScope_Generation", "\"EnrollmentGeneration\" > 0 AND \"Version\" > 0 AND \"MembershipGeneration\" > 0"));
        model.Entity<EnrollmentScope>().ToTable(t => t.HasCheckConstraint("CK_EnrollmentScope_Deadline", "(\"RevocationRequestedAt\" IS NULL AND \"AccessValidUntil\" IS NULL AND \"State\" = 'Active') OR (\"RevocationRequestedAt\" IS NOT NULL AND \"AccessValidUntil\" IS NOT NULL AND \"State\" = 'Revoked' AND \"AccessValidUntil\" <= \"RevocationRequestedAt\" + interval '2 hours')"));
        model.Entity<TeacherAssignmentScope>().ToTable(t => t.HasCheckConstraint("CK_TeacherAssignmentScope_State", "\"State\" IN ('Active','Revoked')"));
        model.Entity<TeacherAssignmentScope>().ToTable(t => t.HasCheckConstraint("CK_TeacherAssignmentScope_Generation", "\"Generation\" > 0 AND \"Version\" > 0 AND \"MembershipGeneration\" > 0"));
        model.Entity<TeacherAssignmentScope>().ToTable(t => t.HasCheckConstraint("CK_TeacherAssignmentScope_Deadline", "(\"RevocationRequestedAt\" IS NULL AND \"AccessValidUntil\" IS NULL AND \"State\" = 'Active') OR (\"RevocationRequestedAt\" IS NOT NULL AND \"AccessValidUntil\" IS NOT NULL AND \"State\" = 'Revoked' AND \"AccessValidUntil\" <= \"RevocationRequestedAt\" + interval '2 hours')"));
        model.Entity<CourseRegistration>().ToTable(t => t.HasCheckConstraint("CK_Course_Mapping", "(\"MappingState\" = 'Verified' AND \"OrganizationId\" IS NOT NULL) OR (\"MappingState\" = 'Unknown' AND \"OrganizationId\" IS NULL)"));
        model.Entity<CourseCatalogRecord>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_CourseCatalog_Title", "char_length(btrim(\"Title\")) BETWEEN 2 AND 160");
            t.HasCheckConstraint("CK_CourseCatalog_State", "\"State\" IN ('Draft','Published') AND \"Version\" > 0");
        });
        model.Entity<CourseGroupCatalogRecord>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_CourseGroupCatalog_Name", "char_length(btrim(\"Name\")) BETWEEN 2 AND 120");
            t.HasCheckConstraint("CK_CourseGroupCatalog_Version", "\"Version\" > 0");
        });
        model.Entity<CourseRosterCommandReceipt>().ToTable(t =>
            t.HasCheckConstraint("CK_CourseRosterReceipt_Digest", "char_length(\"PayloadDigest\") = 64"));
        model.Entity<ProcessingContextVersion>().ToTable(t => t.HasCheckConstraint("CK_Context_Version", "\"Version\" > 0"));
        model.Entity<ProcessingContextVersion>().ToTable(t => t.HasCheckConstraint("CK_Context_Synthetic", "\"SyntheticOnly\" AND \"ApprovalState\" IN ('Unreviewed','SyntheticOnly') AND \"LegalBasisRef\" IS NULL"));
        model.Entity<CourseContextBinding>().ToTable(t => t.HasCheckConstraint("CK_Binding_Interval", "\"EffectiveTo\" IS NULL OR \"EffectiveTo\" > \"EffectiveFrom\""));
        model.Entity<ProcessingRestriction>().ToTable(t => t.HasCheckConstraint("CK_Restriction_State", "\"State\" IN ('Active','Withdrawn') AND \"Version\" > 0 AND \"Operations\" > 0 AND \"Operations\" <= 31"));
        model.Entity<ProvisioningRequest>().ToTable(t => t.HasCheckConstraint("CK_Provision_State", "(\"State\" = 'Pending' AND \"UserId\" IS NULL) OR (\"State\" = 'Completed' AND \"UserId\" IS NOT NULL)"));
        model.Entity<OrganizationParticipantProfile>().ToTable(t => t.HasCheckConstraint("CK_Participant_Profile", "char_length(btrim(\"ExternalRosterKey\")) BETWEEN 3 AND 128 AND char_length(btrim(\"DisplayName\")) BETWEEN 2 AND 160"));
        model.Entity<AccountLinkRequest>().ToTable(t => t.HasCheckConstraint("CK_AccountLink_State", "\"State\" IN ('Requested','Approved','Rejected','Suspended') AND \"EducationalRole\" IN ('Student','Teacher') AND \"Version\" > 0"));
        model.Entity<AccountSecurityState>().ToTable(t => t.HasCheckConstraint("CK_Security_State", "\"State\" IN ('Enabled','SecurityBlocked') AND \"CredentialEpoch\" > 0 AND \"Version\" > 0"));
        model.Entity<IdentitySession>().ToTable(t => t.HasCheckConstraint("CK_Session_Times", "\"Generation\" > 0 AND \"Version\" > 0 AND \"AbsoluteExpiresAt\" = \"CreatedAt\" + interval '12 hours' AND \"LastSuccessfulRefreshAt\" >= \"CreatedAt\" AND \"LastSuccessfulRefreshAt\" <= \"AbsoluteExpiresAt\" AND (\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"CreatedAt\")"));
        model.Entity<RefreshVerifier>().ToTable(t => t.HasCheckConstraint("CK_Refresh_Verifier", "\"Generation\" > 0 AND octet_length(\"Verifier\") = 32 AND \"KeyVersion\" > 0 AND (\"ConsumedAt\" IS NULL OR \"ConsumedAt\" >= \"CreatedAt\")"));
        model.Entity<RecoveryRequest>().ToTable(t => t.HasCheckConstraint("CK_Recovery_State", "\"State\" IN ('Requested','Approved','Rejected','Suspended','Fulfilled','Cancelled') AND \"Version\" > 0"));
        model.Entity<RecoveryRequest>().ToTable(t => t.HasCheckConstraint("CK_Recovery_Approval", "(\"ApproverActorId\" IS NULL AND \"ApprovedAt\" IS NULL AND \"State\" NOT IN ('Approved','Fulfilled')) OR (\"ApproverActorId\" IS NOT NULL AND \"ApproverActorId\" <> \"InitiatorActorId\" AND \"ApprovedAt\" IS NOT NULL AND \"VerificationRef\" IS NOT NULL)"));
        model.Entity<AccessChallenge>().ToTable(t => t.HasCheckConstraint("CK_Challenge_State", "\"Purpose\" IN ('Activation','Recovery') AND \"State\" IN ('Created','Consumed','Expired','Revoked','Locked') AND \"FailedAttempts\" BETWEEN 0 AND 5 AND \"Version\" > 0 AND octet_length(\"SecretMac\") = 32 AND \"KeyVersion\" > 0"));
        model.Entity<AccessChallenge>().ToTable(t => t.HasCheckConstraint("CK_Challenge_Terminal", "(\"State\" = 'Created' AND \"TerminalAt\" IS NULL AND \"FailedAttempts\" < 5) OR (\"State\" <> 'Created' AND \"TerminalAt\" IS NOT NULL AND \"TerminalAt\" >= \"CreatedAt\")"));
        model.Entity<AccessChallenge>().ToTable(t => t.HasCheckConstraint("CK_Challenge_Expiry", "\"ExpiresAt\" > \"CreatedAt\" AND ((\"Purpose\" = 'Activation' AND \"ExpiresAt\" <= \"CreatedAt\" + interval '24 hours' AND \"RecoveryRequestId\" IS NULL) OR (\"Purpose\" = 'Recovery' AND \"ExpiresAt\" <= \"CreatedAt\" + interval '15 minutes'))"));
        model.Entity<SecurityAudit>().ToTable(t => t.HasCheckConstraint("CK_Audit_Context", "(\"ContextId\" IS NULL AND \"ContextVersion\" IS NULL) OR (\"ContextId\" IS NOT NULL AND \"ContextVersion\" IS NOT NULL AND \"OrganizationId\" IS NOT NULL)"));
        model.Entity<AssignmentScope>(e =>
        {
            e.ToTable("AssignmentScopes"); e.HasKey(x => x.AssignmentId); e.Property(x => x.AssignmentId).ValueGeneratedNever();
            e.HasOne<Lms.Domain.ProblemAssignment>().WithMany().HasForeignKey(x => new { x.AssignmentId, x.StudentId })
                .HasPrincipalKey(x => new { x.Id, x.StudentId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OrganizationMembership>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.StudentId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.ActorId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CourseGroup>().WithMany().HasForeignKey(x => new { x.CourseId, x.GroupId }).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_AssignmentScope_Generation", "\"MembershipGeneration\" > 0 AND \"EnrollmentGeneration\" > 0"));
        });
        model.Entity<AuthorityHistory>(e =>
        {
            e.ToTable("AuthorityHistory"); e.HasKey(x => x.Id); e.Property(x => x.Snapshot).HasColumnType("jsonb");
            e.HasOne<OrganizationMembership>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ActorId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.ActorId }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.OrganizationId, x.ActorId });
        });
        foreach (var foreignKey in model.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
    }
}
