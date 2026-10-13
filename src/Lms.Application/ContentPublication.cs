namespace Lms.Application;

public sealed record ContentDiagnostic(string Severity, string Code, string File, int Line, int Column,
    string Message);

public sealed record ContentImportResult(Guid ImportId, string State, long Version, string CourseId,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, IReadOnlyList<ContentDiagnostic> Diagnostics,
    string? StudentProjectionHash, string? PackageHash, Guid? PublishedVersionId);

public sealed record ContentPreview(string Audience, string ArtifactJson);
public sealed record ContentAsset(string MediaType, byte[] Bytes, string Sha256);
public sealed record ContentPublicationResult(Guid ContentVersionId, int VersionNumber, long CourseVersion,
    bool Created);
public sealed record ContentAssignmentResult(Guid ContentVersionId, string LessonId, string GroupId,
    int ProblemCount, int StudentCount, int AssignmentCount, bool Created);
public sealed record StudentAssignmentItem(Guid AssignmentId, Guid ContentVersionId, int VersionNumber,
    string LessonId, string LessonTitle, string ProblemId, string? ProblemTitle, int ProblemOrder);

public interface IContentPublication
{
    Task<ContentImportResult> Import(TokenAuthority token, string courseId, Stream zip, long length,
        string mediaType, string idempotencyKey, string sourceRevision, CancellationToken ct = default);
    Task<ContentImportResult?> ImportStatus(TokenAuthority token, string courseId, Guid importId,
        CancellationToken ct = default);
    Task<ContentPreview> PreviewImport(TokenAuthority token, string courseId, Guid importId,
        string audience, CancellationToken ct = default);
    Task<ContentAsset> PreviewAsset(TokenAuthority token, string courseId, Guid importId,
        string audience, string assetPath, CancellationToken ct = default);
    Task<ContentPublicationResult> Publish(TokenAuthority token, string courseId, Guid importId,
        long expectedImportVersion, long expectedCourseVersion, string idempotencyKey,
        CancellationToken ct = default);
    Task<ContentAssignmentResult> OpenAndAssign(TokenAuthority token, string courseId, Guid contentVersionId,
        string lessonId, string groupId, IReadOnlyList<string> problemIds, long expectedCourseVersion,
        string idempotencyKey, CancellationToken ct = default);
    Task<IReadOnlyList<StudentAssignmentItem>> StudentAssignments(TokenAuthority token, string courseId,
        CancellationToken ct = default);
    Task<ContentPreview> StudentPublishedContent(TokenAuthority token, string courseId, Guid contentVersionId,
        CancellationToken ct = default);
    Task<ContentAsset> StudentPublishedAsset(TokenAuthority token, string courseId, Guid contentVersionId,
        string assetPath, CancellationToken ct = default);
}
