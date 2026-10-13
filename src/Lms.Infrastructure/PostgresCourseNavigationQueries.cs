using Lms.Application;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure;

// No legacy workspace adapter: its aggregates do not enforce S3/current processing.
// Only course metadata is projected. Progress remains absent until a scoped aggregate exists.
public sealed class PostgresCourseNavigationQueries(LmsDbContext db, ComponentAuthorization authorization, IAuthorityClock clock)
    : ICourseNavigationQueries
{
    public async Task<CourseNavigationDto> Courses(TokenAuthority token, CancellationToken cancellationToken = default)
    {
        EnsureTime(token);
        var rows = new List<CourseNavigationItemDto>();
        var role = token.Authority.Membership.Role switch
        {
            ScopeRole.Student => "Student", ScopeRole.Teacher => "Teacher", _ => null
        };
        if (role == null || token.Identity.ActorId != token.Authority.Membership.ActorId)
            throw new UseCaseException("Forbidden");
        // Candidate identifiers come only from validated server-issued scope. Each candidate
        // still resolves its current ownership and processing gate BEFORE reading metadata.
        foreach (var courseId in token.Authority.Grants.Select(g => g.CourseId).Distinct().Order(StringComparer.Ordinal))
        {
            var resource = new ResourceReference(ResourceKind.Course, CourseId: courseId);
            if (!await authorization.Allows(token, resource, ScopeAction.Read, cancellationToken)) continue;
            var version = await db.ContentVersions.AsNoTracking().Where(v => v.CourseId == courseId)
                .OrderByDescending(v => v.VersionNumber).Select(v => (Guid?)v.Id).FirstOrDefaultAsync(cancellationToken);
            CourseDto course;
            if (version is { } id)
            {
                var (_, content) = await new PostgresContentResolver(db).Version(courseId, id, cancellationToken);
                course = new(courseId, content.Title, id, content.Description);
            }
            else course = new(courseId, null, null);
            if (!await authorization.Allows(token, resource, ScopeAction.Read, cancellationToken))
                throw new UseCaseException("Forbidden");
            rows.Add(new(course, role, null));
        }
        EnsureTime(token);
        return new(rows);
    }
    private void EnsureTime(TokenAuthority token)
    {
        var time = clock.Read();
        if (time is not { Valid: true } || time.Earliest < token.NotBefore || time.Latest >= token.ExpiresAt)
            throw new UseCaseException("Forbidden");
    }
}
