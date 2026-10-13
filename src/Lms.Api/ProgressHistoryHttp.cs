using Lms.Application;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lms.Api;

public static class ProgressHistoryHttp
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };
    public static void MapProgressHistory(this WebApplication app)
    {
        app.MapGet("/api/v1/teacher/group-progress",
            (HttpContext context, ComponentJwt jwt, IProgressHistoryQueries queries) =>
                Buffered(context, jwt,
                    token => queries.TeacherProgress(token, ProgressQuery(context), context.RequestAborted),
                    (token, response) => queries.CanReturn(token, response, context.RequestAborted)))
            .RequireAuthorization(ComponentPolicy);

        app.MapGet("/api/v1/student/progress-history",
            (HttpContext context, ComponentJwt jwt, IProgressHistoryQueries queries) =>
                Buffered(context, jwt,
                    token => queries.StudentProgress(token, ProgressQuery(context), context.RequestAborted),
                    (token, response) => queries.CanReturn(token, response, context.RequestAborted)))
            .RequireAuthorization(ComponentPolicy);

        app.MapGet("/api/v1/teacher/review-history",
            (HttpContext context, ComponentJwt jwt, IProgressHistoryQueries queries) =>
                Buffered(context, jwt,
                    token => queries.TeacherReviewHistory(token, ReviewQuery(context), context.RequestAborted),
                    (token, response) => queries.CanReturn(token, response, context.RequestAborted)))
            .RequireAuthorization(ComponentPolicy);
    }

    private static async Task Buffered<T>(HttpContext context, ComponentJwt jwt,
        Func<TokenAuthority, Task<T>> read, Func<TokenAuthority, T, Task<bool>> canReturn)
    {
        T? result = default;
        await ProtectedCoursesHttp.ExecuteBuffered(context, jwt, async (token, _) =>
        {
            result = await read(token);
            return result!;
        }, token => canReturn(token, result!), Json);
    }

    private static void ComponentPolicy(AuthorizationPolicyBuilder policy) =>
        policy.AddAuthenticationSchemes(CourseBearerHandler.SchemeName).RequireAuthenticatedUser();

    private static ProgressHistoryQuery ProgressQuery(HttpContext context) => new(
        Text(context, "courseId"), Text(context, "groupId"), GuidValue(context, "studentId"),
        Text(context, "status"), IntValue(context, "page", 1), IntValue(context, "pageSize", 50));

    private static ReviewHistoryQuery ReviewQuery(HttpContext context) => new(
        Text(context, "courseId"), Text(context, "groupId"), GuidValue(context, "studentId"),
        Text(context, "decision"), IntValue(context, "page", 1), IntValue(context, "pageSize", 50));

    private static string? Text(HttpContext context, string name)
    {
        if (!context.Request.Query.TryGetValue(name, out var values)) return null;
        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
            throw new UseCaseException("ValidationFailed");
        return values[0];
    }

    private static Guid? GuidValue(HttpContext context, string name)
    {
        var value = Text(context, name);
        if (value == null) return null;
        if (!Guid.TryParse(value, out var parsed) || parsed == Guid.Empty)
            throw new UseCaseException("ValidationFailed");
        return parsed;
    }

    private static int IntValue(HttpContext context, string name, int fallback)
    {
        var value = Text(context, name);
        if (value == null) return fallback;
        if (!int.TryParse(value, out var parsed)) throw new UseCaseException("ValidationFailed");
        return parsed;
    }
}
