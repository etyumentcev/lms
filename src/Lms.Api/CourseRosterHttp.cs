using System.Text.Json;
using System.Text.Json.Serialization;
using Lms.Application;
using Microsoft.AspNetCore.Antiforgery;

namespace Lms.Api;

internal sealed record CreateCourseBody(string Title, string? Description, string IdempotencyKey);
internal sealed record CreateGroupBody(string Name, string? Description, string IdempotencyKey);
internal sealed record EnrollStudentsBody(IReadOnlyList<Guid> StudentIds, string IdempotencyKey);
internal sealed record MoveStudentBody(string TargetGroupId, long ExpectedVersion, string IdempotencyKey);
internal sealed record RemoveStudentBody(long ExpectedVersion, string IdempotencyKey);
internal sealed record AssignTeacherBody(Guid TeacherId, string IdempotencyKey);
internal sealed record RemoveTeacherBody(Guid GrantId, long ExpectedVersion, string IdempotencyKey);
internal sealed record AssignCourseEditorBody(Guid EditorId, string IdempotencyKey);
internal sealed record RemoveCourseEditorBody(Guid GrantId, long ExpectedVersion, string IdempotencyKey);

public static class CourseRosterHttp
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void MapCourseRoster(this WebApplication app, string allowedOrigin)
    {
        var protectedApi = app.MapGroup("/api/v1").RequireAuthorization(policy => policy
            .AddAuthenticationSchemes(CourseBearerHandler.SchemeName).RequireAuthenticatedUser());

        protectedApi.MapGet("/admin/courses", async (HttpContext context, ICourseRosterAdministration courses) =>
        {
            var token = Admin(context);
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new { items = await courses.AdminCatalog(
                token.Authority.Membership.OrganizationId, token.Identity.ActorId, context.RequestAborted) }, Json, context.RequestAborted);
        });

        protectedApi.MapGet("/admin/courses/{courseId}", async (HttpContext context, string courseId,
            ICourseRosterAdministration courses) =>
        {
            var token = Admin(context);
            var result = await courses.AdminCourse(token.Authority.Membership.OrganizationId,
                token.Identity.ActorId, courseId, context.RequestAborted);
            if (result == null) { await ApiErrors.Write(context, "NotFound", 404); return; }
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(result, Json, context.RequestAborted);
        });

        protectedApi.MapPost("/admin/courses", (HttpContext context, IAntiforgery csrf,
            ICourseRosterAdministration courses) => Write(context, csrf, allowedOrigin, async token =>
        {
            var body = await Read<CreateCourseBody>(context);
            var result = await courses.CreateCourse(new(token.Authority.Membership.OrganizationId,
                token.Identity.ActorId, body.Title, body.Description, body.IdempotencyKey,
                context.TraceIdentifier), context.RequestAborted);
            context.Response.StatusCode = result.Created ? 201 : 200;
            await context.Response.WriteAsJsonAsync(result, Json, context.RequestAborted);
        }));

        protectedApi.MapPost("/admin/courses/{courseId}/groups", (HttpContext context, string courseId,
            IAntiforgery csrf, ICourseRosterAdministration courses) => Write(context, csrf, allowedOrigin, async token =>
        {
            var body = await Read<CreateGroupBody>(context);
            var result = await courses.CreateGroup(new(token.Authority.Membership.OrganizationId,
                token.Identity.ActorId, courseId, body.Name, body.Description, body.IdempotencyKey,
                context.TraceIdentifier), context.RequestAborted);
            context.Response.StatusCode = result.Created ? 201 : 200;
            await context.Response.WriteAsJsonAsync(result, Json, context.RequestAborted);
        }));

        protectedApi.MapPost("/admin/courses/{courseId}/groups/{groupId}/students", (HttpContext context,
            string courseId, string groupId, IAntiforgery csrf, ICourseRosterAdministration courses) =>
            Write(context, csrf, allowedOrigin, async token =>
            {
                var body = await Read<EnrollStudentsBody>(context);
                var result = await courses.Enroll(new(token.Authority.Membership.OrganizationId,
                    token.Identity.ActorId, courseId, groupId, body.StudentIds, body.IdempotencyKey,
                    context.TraceIdentifier), context.RequestAborted);
                await context.Response.WriteAsJsonAsync(result, Json, context.RequestAborted);
            }));

        protectedApi.MapPost("/admin/courses/{courseId}/students/{studentId:guid}/move", (HttpContext context,
            string courseId, Guid studentId, IAntiforgery csrf, ICourseRosterAdministration courses) =>
            Write(context, csrf, allowedOrigin, async token =>
            {
                var body = await Read<MoveStudentBody>(context);
                var result = await courses.Move(new(token.Authority.Membership.OrganizationId,
                    token.Identity.ActorId, courseId, studentId, body.TargetGroupId, body.ExpectedVersion,
                    body.IdempotencyKey, context.TraceIdentifier), context.RequestAborted);
                await context.Response.WriteAsJsonAsync(result, Json, context.RequestAborted);
            }));

        protectedApi.MapPost("/admin/courses/{courseId}/students/{studentId:guid}/remove", (HttpContext context,
            string courseId, Guid studentId, IAntiforgery csrf, ICourseRosterAdministration courses) =>
            Write(context, csrf, allowedOrigin, async token =>
            {
                var body = await Read<RemoveStudentBody>(context);
                var result = await courses.Remove(new(token.Authority.Membership.OrganizationId,
                    token.Identity.ActorId, courseId, studentId, body.ExpectedVersion,
                    body.IdempotencyKey, context.TraceIdentifier), context.RequestAborted);
                await context.Response.WriteAsJsonAsync(result, Json, context.RequestAborted);
            }));

        protectedApi.MapPost("/admin/courses/{courseId}/groups/{groupId}/teachers", (HttpContext context,
            string courseId, string groupId, IAntiforgery csrf, ICourseRosterAdministration courses) =>
            Write(context, csrf, allowedOrigin, async token =>
            {
                var body = await Read<AssignTeacherBody>(context);
                var result = await courses.Assign(new(token.Authority.Membership.OrganizationId,
                    token.Identity.ActorId, courseId, groupId, body.TeacherId, body.IdempotencyKey,
                    context.TraceIdentifier), context.RequestAborted);
                await context.Response.WriteAsJsonAsync(result, Json, context.RequestAborted);
            }));

        protectedApi.MapPost("/admin/courses/{courseId}/groups/{groupId}/teachers/{teacherId:guid}/remove",
            (HttpContext context, string courseId, string groupId, Guid teacherId, IAntiforgery csrf,
                ICourseRosterAdministration courses) => Write(context, csrf, allowedOrigin, async token =>
            {
                var body = await Read<RemoveTeacherBody>(context);
                var result = await courses.Unassign(new(token.Authority.Membership.OrganizationId,
                    token.Identity.ActorId, courseId, groupId, teacherId, body.GrantId,
                    body.ExpectedVersion, body.IdempotencyKey, context.TraceIdentifier), context.RequestAborted);
                await context.Response.WriteAsJsonAsync(result, Json, context.RequestAborted);
            }));

        protectedApi.MapPost("/admin/courses/{courseId}/editors", (HttpContext context,
            string courseId, IAntiforgery csrf, ICourseRosterAdministration courses) =>
            Write(context, csrf, allowedOrigin, async token =>
            {
                var body = await Read<AssignCourseEditorBody>(context);
                var result = await courses.AssignEditor(new(token.Authority.Membership.OrganizationId,
                    token.Identity.ActorId, courseId, body.EditorId, body.IdempotencyKey,
                    context.TraceIdentifier), context.RequestAborted);
                await context.Response.WriteAsJsonAsync(result, Json, context.RequestAborted);
            }));

        protectedApi.MapPost("/admin/courses/{courseId}/editors/{editorId:guid}/remove",
            (HttpContext context, string courseId, Guid editorId, IAntiforgery csrf,
                ICourseRosterAdministration courses) => Write(context, csrf, allowedOrigin, async token =>
            {
                var body = await Read<RemoveCourseEditorBody>(context);
                var result = await courses.UnassignEditor(new(token.Authority.Membership.OrganizationId,
                    token.Identity.ActorId, courseId, editorId, body.GrantId, body.ExpectedVersion,
                    body.IdempotencyKey, context.TraceIdentifier), context.RequestAborted);
                await context.Response.WriteAsJsonAsync(result, Json, context.RequestAborted);
            }));

        protectedApi.MapGet("/my/courses", async (HttpContext context, ICourseRosterAdministration courses) =>
        {
            var token = Educational(context);
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new { items = await courses.LearningCatalog(token,
                context.RequestAborted) }, Json, context.RequestAborted);
        });

        protectedApi.MapGet("/teacher/courses/{courseId}", async (HttpContext context, string courseId,
            ICourseRosterAdministration courses) =>
        {
            var token = Educational(context);
            var result = await courses.TeacherCourse(token, courseId, context.RequestAborted);
            if (result == null) { await ApiErrors.Write(context, "NotFound", 404); return; }
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(result, Json, context.RequestAborted);
        });
    }

    private static async Task Write(HttpContext context, IAntiforgery csrf, string origin,
        Func<TokenAuthority, Task> command)
    {
        try
        {
            if (!context.Request.IsHttps || context.Request.Headers.Origin.Count != 1 ||
                context.Request.Headers.Origin[0] != origin) throw new UseCaseException("Forbidden");
            await csrf.ValidateRequestAsync(context);
            context.Response.Headers.CacheControl = "no-store";
            await command(Admin(context));
        }
        catch (Exception error) when (error is JsonException or AntiforgeryValidationException)
        {
            var code = error is JsonException ? "ValidationFailed" : "Forbidden";
            await ApiErrors.Write(context, code, error is JsonException ? 400 : 403);
        }
    }

    private static TokenAuthority Admin(HttpContext context)
    {
        var token = context.Features.Get<ValidatedCourseBearer>()?.Authority;
        if (token == null || !OrganizationPermissionPolicy.CanManageOrganization(token.Authority.Membership))
            throw new UseCaseException("Forbidden");
        return token;
    }

    private static TokenAuthority Educational(HttpContext context)
    {
        var token = context.Features.Get<ValidatedCourseBearer>()?.Authority;
        if (token?.Authority.Membership.Role is null) throw new UseCaseException("Forbidden");
        return token;
    }

    private static async Task<T> Read<T>(HttpContext context) =>
        await JsonSerializer.DeserializeAsync<T>(context.Request.Body, Json, context.RequestAborted)
        ?? throw new JsonException();
}
