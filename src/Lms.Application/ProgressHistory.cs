using Lms.Domain;
using System.Text.Json.Serialization;

namespace Lms.Application;

public sealed record ProgressHistoryQuery(string? CourseId = null, string? GroupId = null,
    Guid? StudentId = null, string? Status = null, int Page = 1, int PageSize = 50);

public sealed record ProgressAssignmentDto(Guid AssignmentId, Guid? ProblemWorkId, Guid CourseVersionId,
    string CourseId, string? CourseTitle, string LessonId, string? LessonTitle, string ProblemId,
    string? ProblemTitle, ProblemWorkStatus Status, int AttemptCount, bool IsActive,
    DateTimeOffset? LastActivityAt);

public sealed record StudentProgressRowDto(Guid StudentId, string? StudentDisplayName, string CourseId,
    string? CourseTitle, string? GroupId, string? GroupTitle, int AssignedCount,
    IReadOnlyList<ProgressAssignmentDto> Assignments);

public sealed record ProgressSummaryDto(int StudentCount, int AssignedCount, int NotStarted,
    int Submitted, int NeedsRevision, int Accepted);

public sealed record TeacherProgressResponseDto(IReadOnlyList<StudentProgressRowDto> Items, int Page,
    int PageSize, int Total, ProgressSummaryDto Summary,
    [property: JsonIgnore] IReadOnlyList<Guid>? AuthorizationProof = null);

public sealed record StudentProgressResponseDto(IReadOnlyList<ProgressAssignmentDto> Items, int Page,
    int PageSize, int Total, ProgressSummaryDto Summary,
    [property: JsonIgnore] IReadOnlyList<Guid>? AuthorizationProof = null);

public sealed record ReviewHistoryQuery(string? CourseId = null, string? GroupId = null,
    Guid? StudentId = null, string? Decision = null, int Page = 1, int PageSize = 50);

public sealed record ReviewHistoryItemDto(Guid ReviewId, Guid ProblemWorkId, Guid SubmissionId,
    Guid StudentId, string? StudentDisplayName, string CourseId, string? CourseTitle,
    string LessonId, string? LessonTitle, string? GroupId, string? GroupTitle,
    string ProblemId, string? ProblemTitle, Guid CourseVersionId, int AttemptNumber,
    int AttachmentCount, ReviewDecision Decision, ProblemWorkStatus WorkStatus,
    DateTimeOffset CreatedAt);

public sealed record ReviewHistorySummaryDto(int Total, int NeedsRevision, int Accepted);
public sealed record ReviewHistoryResponseDto(IReadOnlyList<ReviewHistoryItemDto> Items, int Page,
    int PageSize, int Total, ReviewHistorySummaryDto Summary,
    [property: JsonIgnore] IReadOnlyList<Guid>? AuthorizationProof = null);

public interface IProgressHistoryQueries
{
    Task<TeacherProgressResponseDto> TeacherProgress(TokenAuthority token, ProgressHistoryQuery query,
        CancellationToken cancellationToken = default);
    Task<StudentProgressResponseDto> StudentProgress(TokenAuthority token, ProgressHistoryQuery query,
        CancellationToken cancellationToken = default);
    Task<ReviewHistoryResponseDto> TeacherReviewHistory(TokenAuthority token, ReviewHistoryQuery query,
        CancellationToken cancellationToken = default);
    Task<bool> CanReturn(TokenAuthority token, TeacherProgressResponseDto response,
        CancellationToken cancellationToken = default);
    Task<bool> CanReturn(TokenAuthority token, StudentProgressResponseDto response,
        CancellationToken cancellationToken = default);
    Task<bool> CanReturn(TokenAuthority token, ReviewHistoryResponseDto response,
        CancellationToken cancellationToken = default);
}
