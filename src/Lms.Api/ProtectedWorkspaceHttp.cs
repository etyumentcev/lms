using Lms.Application;
using Lms.Infrastructure;

namespace Lms.Api;

// Explicit isolated opt-in. Never registered by production Program.cs.
public static class ProtectedWorkspaceHttp
{
    public static void MapProtectedWorkspace(this WebApplication app)
    {
        foreach (var route in new[] { "/api/v1/courses/{courseId}",
            "/api/v1/courses/{courseId}/versions/{versionId}/lessons/{lessonId}",
            "/api/v1/courses/{courseId}/versions/{versionId}/lessons/{lessonId}/problems/{problemId}" })
            app.MapGet(route, Execute).RequireAuthorization(policy =>
                policy.AddAuthenticationSchemes(CourseBearerHandler.SchemeName).RequireAuthenticatedUser());
    }
    private static Task Execute(HttpContext context, ComponentJwt validator, IWorkspaceContentQueries queries)
    {
        ContentRead? result = null;
        return ProtectedCoursesHttp.ExecuteBuffered(context, validator, async (token, deadline) =>
        {
            string? Route(string name) => context.Request.RouteValues[name]?.ToString();
            var version = Route("versionId");
            if (version != null && !Guid.TryParse(version, out _)) throw new UseCaseException("Forbidden");
            var selection = new ContentSelection(Route("courseId")!, version == null ? null : Guid.Parse(version),
                Route("lessonId"), Route("problemId"));
            result = await queries.Read(token, selection, deadline, context.RequestAborted);
            return result.Value;
        }, token => queries.CanReturn(token, result!, context.RequestAborted));
    }
}
