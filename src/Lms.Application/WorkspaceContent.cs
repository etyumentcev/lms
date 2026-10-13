namespace Lms.Application;

// Content selectors only. No assignment, student, work or teacher-private source.
public sealed record ContentSelection(string CourseId, Guid? VersionId = null, string? LessonId = null, string? ProblemId = null);
public sealed record ContentLinkDto(string Id, Guid VersionId, int Order, string? Title, string Href);
public sealed record CourseContentDto(string Id, string? Title, string? Description, IReadOnlyList<ContentLinkDto> Lessons);
public sealed record LessonContentDto(string CourseId, Guid VersionId, string Id, int Order, string? Title,
    string? Summary, IReadOnlyList<ContentLinkDto> Problems);
public sealed record ProblemContentDto(string CourseId, Guid VersionId, string LessonId, string Id,
    int Order, string? Title, string StatementMarkdown);
public sealed record ContentRead(object Value, IReadOnlyList<ContentSelection> Resources);
public interface IWorkspaceContentQueries
{
    Task<ContentRead> Read(TokenAuthority token, ContentSelection selection, ApplicationDeadline deadline, CancellationToken ct);
    Task<bool> CanReturn(TokenAuthority token, ContentRead result, CancellationToken ct);
}
