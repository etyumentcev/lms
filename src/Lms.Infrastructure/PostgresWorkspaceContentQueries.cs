using Lms.Application;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure;

// Uses only compiled student-safe publications and trusted lesson availability.
// Deliberately never invokes legacy IWorkspaceQueries or reads assignments/works.
public sealed class PostgresWorkspaceContentQueries(LmsDbContext db, ComponentAuthorization authorization, IAuthorityClock clock)
    : IWorkspaceContentQueries
{
    private async Task<bool> Allowed(TokenAuthority token, ContentSelection s, CancellationToken ct)
    {
        if (!await authorization.Allows(token, new(ResourceKind.Course, CourseId: s.CourseId), ScopeAction.Read, ct)) return false;
        if (s.LessonId == null) return s.VersionId == null && s.ProblemId == null;
        var time = clock.Read();
        return s.VersionId != null && time is { Valid: true } && await db.Lessons.AsNoTracking().AnyAsync(l =>
            l.CourseId == s.CourseId && l.CourseVersionId == s.VersionId && l.LessonId == s.LessonId &&
            l.IsPublished && (l.AvailableFrom == null || l.AvailableFrom <= time.Earliest), ct);
    }
    public async Task<bool> CanReturn(TokenAuthority token, ContentRead result, CancellationToken ct)
    {
        foreach (var s in result.Resources) if (!await Allowed(token, s, ct)) return false;
        return true;
    }
    private static void Stage(ApplicationDeadline deadline)
    {
        if (!deadline.Check(ApplicationAction.StartStage).Allowed) throw new OperationCanceledException();
    }
    private static string LessonHref(ContentSelection s) => $"/api/v1/courses/{Uri.EscapeDataString(s.CourseId)}/versions/{s.VersionId}/lessons/{Uri.EscapeDataString(s.LessonId!)}";
    public async Task<ContentRead> Read(TokenAuthority token, ContentSelection s, ApplicationDeadline deadline, CancellationToken ct)
    {
        Stage(deadline);
        if (!await Allowed(token, s, ct)) throw new UseCaseException("Forbidden");
        Stage(deadline);
        var resolver = new PostgresContentResolver(db);
        if (s.LessonId == null)
        {
            var candidates = await db.Lessons.AsNoTracking().Where(l => l.CourseId == s.CourseId)
                .OrderBy(l => l.CourseVersionId).ThenBy(l => l.LessonId)
                .Select(l => new ContentSelection(l.CourseId, l.CourseVersionId, l.LessonId, null)).ToListAsync(ct);
            var links = new List<ContentLinkDto>();
            var resources = new List<ContentSelection> { s };
            foreach (var child in candidates)
            {
                Stage(deadline);
                if (!await Allowed(token, child, ct)) continue;
                Stage(deadline);
                var (_, snapshot) = await resolver.Version(child.CourseId, child.VersionId!.Value, ct);
                var lesson = snapshot.Lessons.SingleOrDefault(l => l.Id == child.LessonId)
                    ?? throw new UseCaseException("Forbidden");
                links.Add(new(lesson.Id, child.VersionId.Value, lesson.Order, lesson.Title, LessonHref(child)));
                resources.Add(child);
            }
            // Course metadata uses the same latest-publication convention as My Courses.
            if (!await Allowed(token, s, ct)) throw new UseCaseException("Forbidden");
            Stage(deadline);
            var version = await db.ContentVersions.AsNoTracking().Where(v => v.CourseId == s.CourseId)
                .OrderByDescending(v => v.VersionNumber).Select(v => (Guid?)v.Id).FirstOrDefaultAsync(ct);
            var content = version is { } id ? (await resolver.Version(s.CourseId, id, ct)).Content : null;
            return new(new CourseContentDto(s.CourseId, content?.Title, content?.Description,
                links.OrderBy(l => l.Order).ThenBy(l => l.Id).ThenBy(l => l.VersionId).ToArray()), resources);
        }
        var (_, published) = await resolver.Version(s.CourseId, s.VersionId!.Value, ct);
        var selected = published.Lessons.SingleOrDefault(l => l.Id == s.LessonId) ?? throw new UseCaseException("Forbidden");
        if (s.ProblemId == null)
            return new(new LessonContentDto(s.CourseId, s.VersionId.Value, selected.Id, selected.Order, selected.Title, selected.Summary,
                selected.Problems.OrderBy(p => p.Order).ThenBy(p => p.Id).Select(p => new ContentLinkDto(p.Id, s.VersionId.Value,
                    p.Order, p.Title, LessonHref(s) + "/problems/" + Uri.EscapeDataString(p.Id))).ToArray()), [s]);
        var problem = selected.Problems.SingleOrDefault(p => p.Id == s.ProblemId) ?? throw new UseCaseException("Forbidden");
        return new(new ProblemContentDto(s.CourseId, s.VersionId.Value, s.LessonId, problem.Id, problem.Order,
            problem.Title, problem.StatementMarkdown), [s]);
    }
}
