using System.Text.Json;
using Lms.Application;
using Lms.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure;

// A compiled, student-safe immutable content artifact. Never place teacher solutions here.
// Provisioning inserts a complete version atomically; there is no mutable current-content row.
public sealed class PublishedContentVersion
{
    public Guid Id { get; set; }
    public string CourseId { get; set; } = null!;
    public int VersionNumber { get; set; }
    public string SourceRevision { get; set; } = null!;
    public DateTimeOffset PublishedAt { get; set; }
    public string ContentJson { get; set; } = null!;
}
public sealed record PublishedCourseContent(string Title, string? Description, IReadOnlyList<PublishedLessonContent> Lessons);
public sealed record PublishedLessonContent(string Id, int Order, string Title, string? Summary, IReadOnlyList<PublishedProblemContent> Problems);
public sealed record PublishedProblemContent(string Id, int Order, string? Title, string StatementMarkdown);

public sealed class PostgresContentResolver(LmsDbContext db) : IImmutableContentResolver
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal async Task<(PublishedContentVersion Version, PublishedCourseContent Content)> Version(string courseId, Guid id, CancellationToken ct)
    {
        var version = await db.ContentVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.CourseId == courseId, ct)
            ?? throw new UseCaseException("ContentUnavailable");
        var content = JsonSerializer.Deserialize<PublishedCourseContent>(version.ContentJson, Json)
            ?? throw new UseCaseException("ContentUnavailable");
        return (version, content);
    }
    public async Task<ResolvedProblemContent> Resolve(string courseId, ProblemContentReference reference, CancellationToken ct)
    {
        var (_, content) = await Version(courseId, reference.CourseVersionId, ct);
        var lesson = content.Lessons.SingleOrDefault(x => x.Id == reference.LessonId)
            ?? throw new UseCaseException("ContentUnavailable");
        var problem = lesson.Problems.SingleOrDefault(x => x.Id == reference.ProblemId)
            ?? throw new UseCaseException("ContentUnavailable");
        return new(new(courseId, content.Title, reference.CourseVersionId, content.Description),
            new(lesson.Id, reference.CourseVersionId, lesson.Order, lesson.Title, lesson.Summary),
            new(problem.Id, reference.CourseVersionId, problem.Order, problem.Title, problem.StatementMarkdown));
    }
}
