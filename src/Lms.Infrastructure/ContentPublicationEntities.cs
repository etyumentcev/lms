namespace Lms.Infrastructure;

public sealed class ContentImportAttempt
{
    public Guid ImportId { get; set; }
    public Guid OrganizationId { get; set; }
    public string CourseId { get; set; } = null!;
    public Guid OwnerActorId { get; set; }
    public string IdempotencyKey { get; set; } = null!;
    public string RequestDigest { get; set; } = null!;
    public string SourceRevision { get; set; } = null!;
    public string State { get; set; } = null!;
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string DiagnosticsJson { get; set; } = "[]";
    public string? StudentArtifactJson { get; set; }
    public string? TeacherArtifactJson { get; set; }
    public string? ReceiptJson { get; set; }
    public string? StudentProjectionHash { get; set; }
    public string? PackageHash { get; set; }
    public string? TeacherArtifactHash { get; set; }
    public Guid? PublishedVersionId { get; set; }
}

public sealed class ContentImportAsset
{
    public Guid ImportId { get; set; }
    public string Audience { get; set; } = null!;
    public string AssetPath { get; set; } = null!;
    public string MediaType { get; set; } = null!;
    public string Sha256 { get; set; } = null!;
    public byte[] Bytes { get; set; } = null!;
}

public sealed class PublishedContentArtifact
{
    public Guid ContentVersionId { get; set; }
    public Guid ImportId { get; set; }
    public string StudentArtifactJson { get; set; } = null!;
    public string TeacherArtifactJson { get; set; } = null!;
    public string StudentProjectionHash { get; set; } = null!;
    public string PackageHash { get; set; } = null!;
    public string TeacherArtifactHash { get; set; } = null!;
}

public sealed class PublishedContentAsset
{
    public Guid ContentVersionId { get; set; }
    public string Audience { get; set; } = null!;
    public string AssetPath { get; set; } = null!;
    public string MediaType { get; set; } = null!;
    public string Sha256 { get; set; } = null!;
    public byte[] Bytes { get; set; } = null!;
}

public sealed class LessonGroupOpening
{
    public Guid OrganizationId { get; set; }
    public string CourseId { get; set; } = null!;
    public string GroupId { get; set; } = null!;
    public Guid ContentVersionId { get; set; }
    public string LessonId { get; set; } = null!;
    public DateTimeOffset OpenedAt { get; set; }
    public Guid OpenedBy { get; set; }
}

public sealed class ContentOperationReceipt
{
    public Guid ActorId { get; set; }
    public string IdempotencyKey { get; set; } = null!;
    public Guid OrganizationId { get; set; }
    public string CourseId { get; set; } = null!;
    public string Kind { get; set; } = null!;
    public string PayloadDigest { get; set; } = null!;
    public string ResultJson { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}
