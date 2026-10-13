namespace Lms.Application;

public sealed record WorkSelection(string CourseId, Guid VersionId, string LessonId, string? ProblemId = null, Guid? WorkId = null);
public sealed record WorkLinkDto(Guid Id, string ProblemId, string Status, string Href);
public sealed record WorkListDto(IReadOnlyList<WorkLinkDto> Items);
public sealed record WorkAttemptDto(Guid Id, int Number, string? BodyMarkdown, DateTimeOffset SubmittedAt,
    int PhotoCount, string PhotoAvailability, IReadOnlyList<WorkReviewDto> Reviews);
public sealed record WorkReviewDto(Guid Id, string Decision, string? FeedbackMarkdown, DateTimeOffset CreatedAt);
public sealed record WorkMessageDto(Guid Id, string Role, string BodyMarkdown, DateTimeOffset CreatedAt, int PhotoCount);
public sealed record ProtectedWorkDto(Guid Id, string Status, string StatementHref,
    IReadOnlyList<WorkAttemptDto> Attempts, IReadOnlyList<WorkMessageDto> Messages);
// Captures only server-resolved resource/subject evidence, never client authority.
public sealed record WorkReadGate(ResourceReference Resource, Guid? Subject = null, long? WorkVersion = null);
public sealed record ProtectedWorkRead(object Value, WorkSelection Selection, IReadOnlyList<WorkReadGate> Gates);
public interface IProtectedWorkQueries
{
    Task<ProtectedWorkRead> Read(TokenAuthority token, WorkSelection selection, ApplicationDeadline deadline, CancellationToken ct);
    Task<bool> CanReturn(TokenAuthority token, ProtectedWorkRead read, ApplicationDeadline deadline, CancellationToken ct);
}
