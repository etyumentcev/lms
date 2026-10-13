using Lms.Application;
using Lms.Infrastructure;

namespace Lms.Api;

// Explicit isolated opt-in; no production registration or legacy protocol changes.
public static class ProtectedWorkHttp
{
    public static void MapProtectedWorkReads(this WebApplication app)
    {
        const string root = "/api/v1/courses/{courseId}/versions/{versionId}/lessons/{lessonId}";
        foreach (var route in new[] { root + "/works", root + "/problems/{problemId}/works", root + "/works/{workId}" })
            app.MapGet(route, Execute).RequireAuthorization(p => p.AddAuthenticationSchemes(CourseBearerHandler.SchemeName).RequireAuthenticatedUser());
    }
    private static Task Execute(HttpContext context, ComponentJwt jwt, IProtectedWorkQueries queries)
    {
        ProtectedWorkRead? read = null;
        ApplicationDeadline? original = null;
        return ProtectedCoursesHttp.ExecuteBuffered(context, jwt, async (token, deadline) => {
            original = deadline;
            string? Route(string name) => context.Request.RouteValues[name]?.ToString();
            if (!Guid.TryParse(Route("versionId"), out var version) || Route("workId") != null && !Guid.TryParse(Route("workId"), out _))
                throw new UseCaseException("Forbidden");
            read = await queries.Read(token, new(Route("courseId")!, version, Route("lessonId")!, Route("problemId"),
                Route("workId") == null ? null : Guid.Parse(Route("workId")!)), deadline, context.RequestAborted);
            return read.Value;
        }, token => queries.CanReturn(token, read!, original!, context.RequestAborted));
    }
}
