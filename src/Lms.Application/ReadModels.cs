using Lms.Domain;

namespace Lms.Application;

public sealed record UserSummaryDto(Guid Id, string? DisplayName = null, string? AvatarUrl = null);
public sealed record CourseDto(string Id, string? Title, Guid? PublishedVersionId, string? Description = null,
    string? GradeLabel = null, UserSummaryDto? Teacher = null);
public sealed record LessonDto(string Id, Guid CourseVersionId, int Order, string? Title, string? Summary = null);
public sealed record ProblemDto(string Id, Guid CourseVersionId, int Order, string? Title, string StatementMarkdown);
public sealed record AvailabilityDto(bool IsAvailable, DateTimeOffset? AvailableFrom, string Reason);
public sealed record PermissionsDto(bool CanSubmit, bool CanPostMessage, bool CanAccept, bool CanRequestRevision, bool CanViewAsTeacher = false);
public sealed record SubmissionDto(Guid Id, int AttemptNumber, string? BodyMarkdown, DateTimeOffset SubmittedAt,
    IReadOnlyList<Guid> AttachmentIds);
public sealed record ReviewDto(Guid Id, Guid SubmissionId, Guid TeacherId, ReviewDecision Decision, Guid? MessageId, DateTimeOffset CreatedAt, IReadOnlyList<AnnotatedAttachment>? Annotations = null);
public sealed record MessageDto(Guid Id, UserSummaryDto Author, AuthorRole AuthorRole, string BodyMarkdown,
    Guid? ReplyToMessageId, DateTimeOffset CreatedAt, IReadOnlyList<Guid> AttachmentIds);
public sealed record HistoryDto(IReadOnlyList<SubmissionDto> Submissions, IReadOnlyList<ReviewDto> Reviews, IReadOnlyList<MessageDto> Messages);
public sealed record WorkSummaryDto(Guid? ProblemWorkId, ProblemWorkStatus Status, int AttemptCount, long? Version,
    SubmissionDto? LatestSubmission, ReviewDto? LatestReview, MessageDto? LatestTeacherFeedback);
public sealed record AssignmentDto(Guid Id, bool IsActive);
public sealed record MaterialDto(string Id, string Title, string Kind, string? Description, string Url);
public sealed record HintDto(string Id, int Order, string? Title, string BodyMarkdown);
public sealed record StudentProblemWorkspaceDto(AssignmentDto Assignment, CourseDto Course, LessonDto Lesson,
    ProblemDto Problem, AvailabilityDto Availability, WorkSummaryDto Work, IReadOnlyList<HintDto> Hints,
    HistoryDto History, IReadOnlyList<MaterialDto> UsefulMaterials, PermissionsDto Permissions);
public sealed record ProblemWorkDetailsDto(Guid Id, Guid ProblemAssignmentId, ProblemWorkStatus Status, long Version,
    int AttemptCount, UserSummaryDto Student, CourseDto Course, LessonDto Lesson, ProblemDto Problem,
    SubmissionDto? CurrentSubmission, IReadOnlyList<SubmissionDto> Submissions, IReadOnlyList<ReviewDto> Reviews,
    IReadOnlyList<MessageDto> Messages, PermissionsDto Permissions);
public sealed record LessonProgressDto(string Status, int AcceptedProblems, int TotalAssignedProblems, decimal Percent);
public sealed record CourseProgressDto(int CompletedLessons, int AvailableLessons, int TotalLessons, decimal Percent);
public sealed record EnrollmentDto(Guid? Id, Guid StudentId, string CourseId, string? GroupId, string Status);
public sealed record TeacherContextDto(Guid TeacherId, bool CanReviewCourse, IReadOnlyList<string?> GroupIds);
public sealed record CourseLessonRowDto(LessonDto Lesson, AvailabilityDto Availability, LessonProgressDto Progress, IReadOnlyList<Guid> CourseVersionIds);
public sealed record CourseWorkspaceDto(CourseDto Course, EnrollmentDto? Enrollment, TeacherContextDto? TeacherContext,
    CourseProgressDto Progress, IReadOnlyList<CourseLessonRowDto> Lessons, IReadOnlyList<MaterialDto> Materials);
public sealed record LessonProblemRowDto(Guid ProblemAssignmentId, Guid StudentId, ProblemDto Problem,
    AvailabilityDto Availability, WorkSummaryDto Work, PermissionsDto Permissions);
public sealed record LessonWorkspaceDto(CourseDto Course, LessonDto Lesson, IReadOnlyList<LessonDto> ContentVersions, AvailabilityDto Availability,
    LessonProgressDto Progress, IReadOnlyList<LessonProblemRowDto> Problems, IReadOnlyList<MaterialDto> Materials);
public sealed record ReviewQueueQuery(string? CourseId = null, string? GroupId = null, Guid? StudentId = null,
    string AttemptKind = "all", string? Search = null, int Page = 1, int PageSize = 50);
public sealed record ReviewQueueItemDto(Guid ProblemWorkId, Guid SubmissionId, Guid StudentId, string? StudentDisplayName,
    string CourseId, string? CourseTitle, string LessonId, string? LessonTitle, string? GroupId, string? GroupTitle,
    string ProblemId, string? ProblemTitle, int AttemptNumber, DateTimeOffset SubmittedAt, double WaitingDurationSeconds, long Version);
public sealed record CourseQueueCountDto(string CourseId, string? CourseTitle, int Count);
public sealed record ReviewQueueSummaryDto(int Total, int FirstAttempts, int Revisions, double? AverageWaitingSeconds,
    IReadOnlyList<CourseQueueCountDto> ByCourse);
public sealed record ReviewQueueResponseDto(IReadOnlyList<ReviewQueueItemDto> Items, int Page, int PageSize, int Total, ReviewQueueSummaryDto Summary);

public interface IWorkspaceQueries
{
    Task<StudentProblemWorkspaceDto> StudentProblem(Actor actor, Guid assignmentId, CancellationToken ct);
    Task<ProblemWorkDetailsDto> ProblemWork(Actor actor, Guid workId, CancellationToken ct);
    Task<CourseWorkspaceDto> Course(Actor actor, string courseId, CancellationToken ct);
    Task<LessonWorkspaceDto> Lesson(Actor actor, string courseId, string lessonId, CancellationToken ct);
    Task<ReviewQueueResponseDto> ReviewQueue(Actor actor, ReviewQueueQuery query, CancellationToken ct);
}

public sealed record ResolvedProblemContent(CourseDto Course, LessonDto Lesson, ProblemDto Problem);
public interface IImmutableContentResolver
{
    Task<ResolvedProblemContent> Resolve(string courseId, ProblemContentReference reference, CancellationToken ct);
}
