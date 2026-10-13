using System.Text.Json;
using System.Text.Json.Serialization;
using Lms.Application;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;

namespace Lms.Api;

internal sealed record PublishContentBody(long ExpectedImportVersion, long ExpectedCourseVersion, string IdempotencyKey);
internal sealed record OpenAssignBody(Guid ContentVersionId, string LessonId, string GroupId,
    IReadOnlyList<string> ProblemIds, long ExpectedCourseVersion, string IdempotencyKey);

public static class ContentPublicationHttp
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void MapContentPublication(this WebApplication app, string allowedOrigin, long maxZipBytes)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization(policy => policy
            .AddAuthenticationSchemes(CourseBearerHandler.SchemeName).RequireAuthenticatedUser());

        api.MapPost("/teacher/courses/{courseId}/imports", (HttpContext context, string courseId,
            IAntiforgery csrf, IContentPublication content) => Write(context, csrf, allowedOrigin, async token =>
        {
            var form = await context.Request.ReadFormAsync(new FormOptions
            { MultipartBodyLengthLimit = maxZipBytes + 64_000, ValueLengthLimit = 512 }, context.RequestAborted);
            if (form.Files.Count != 1 || form["idempotencyKey"].Count != 1 || form["sourceRevision"].Count != 1)
                throw new UseCaseException("ValidationFailed");
            var file = form.Files[0];
            if (file.Name != "file" || file.Length <= 0 || file.Length > maxZipBytes)
                throw new UseCaseException("ValidationFailed");
            await using var stream = file.OpenReadStream();
            var result = await content.Import(token, courseId, stream, file.Length, file.ContentType,
                form["idempotencyKey"].ToString(), form["sourceRevision"].ToString(), context.RequestAborted);
            context.Response.StatusCode = 201;
            await context.Response.WriteAsJsonAsync(result, Json, context.RequestAborted);
        }));

        api.MapGet("/teacher/courses/{courseId}/imports/{importId:guid}", async (HttpContext context,
            string courseId, Guid importId, IContentPublication content) =>
        {
            NoStore(context); var result = await content.ImportStatus(Token(context), courseId, importId, context.RequestAborted);
            if (result == null) { await ApiErrors.Write(context, "NotFound", 404); return; }
            await context.Response.WriteAsJsonAsync(result, Json, context.RequestAborted);
        });

        api.MapGet("/teacher/courses/{courseId}/imports/{importId:guid}/preview/{audience}", async
            (HttpContext context, string courseId, Guid importId, string audience, IContentPublication content) =>
        {
            NoStore(context); var result = await content.PreviewImport(Token(context), courseId, importId, audience, context.RequestAborted);
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(result.ArtifactJson, context.RequestAborted);
        });

        api.MapGet("/teacher/courses/{courseId}/imports/{importId:guid}/preview/{audience}/assets/{**assetPath}", async
            (HttpContext context, string courseId, Guid importId, string audience, string assetPath, IContentPublication content) =>
        {
            NoStore(context); var result = await content.PreviewAsset(Token(context), courseId, importId, audience, assetPath, context.RequestAborted);
            context.Response.Headers.ETag = '"' + result.Sha256 + '"'; context.Response.ContentType = result.MediaType;
            await context.Response.Body.WriteAsync(result.Bytes, context.RequestAborted);
        });

        api.MapPost("/teacher/courses/{courseId}/imports/{importId:guid}/publish", (HttpContext context,
            string courseId, Guid importId, IAntiforgery csrf, IContentPublication content) =>
            Write(context, csrf, allowedOrigin, async token =>
            {
                var body = await Read<PublishContentBody>(context);
                var result = await content.Publish(token, courseId, importId, body.ExpectedImportVersion,
                    body.ExpectedCourseVersion, body.IdempotencyKey, context.RequestAborted);
                await context.Response.WriteAsJsonAsync(result, Json, context.RequestAborted);
            }));

        api.MapPost("/teacher/courses/{courseId}/open-and-assign", (HttpContext context, string courseId,
            IAntiforgery csrf, IContentPublication content) => Write(context, csrf, allowedOrigin, async token =>
        {
            var body = await Read<OpenAssignBody>(context);
            var result = await content.OpenAndAssign(token, courseId, body.ContentVersionId, body.LessonId,
                body.GroupId, body.ProblemIds, body.ExpectedCourseVersion, body.IdempotencyKey, context.RequestAborted);
            await context.Response.WriteAsJsonAsync(result, Json, context.RequestAborted);
        }));

        api.MapGet("/my/courses/{courseId}/assignments", async (HttpContext context, string courseId,
            IContentPublication content) =>
        {
            NoStore(context); var items = await content.StudentAssignments(Token(context), courseId, context.RequestAborted);
            await context.Response.WriteAsJsonAsync(new { items }, Json, context.RequestAborted);
        });

        api.MapGet("/my/courses/{courseId}/versions/{contentVersionId:guid}/content", async (HttpContext context,
            string courseId, Guid contentVersionId, IContentPublication content) =>
        {
            NoStore(context); var result = await content.StudentPublishedContent(Token(context), courseId,
                contentVersionId, context.RequestAborted);
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(result.ArtifactJson, context.RequestAborted);
        });

        api.MapGet("/my/courses/{courseId}/versions/{contentVersionId:guid}/assets/{**assetPath}", async
            (HttpContext context, string courseId, Guid contentVersionId, string assetPath, IContentPublication content) =>
        {
            NoStore(context); var result = await content.StudentPublishedAsset(Token(context), courseId,
                contentVersionId, assetPath, context.RequestAborted);
            context.Response.Headers.ETag = '"' + result.Sha256 + '"'; context.Response.ContentType = result.MediaType;
            await context.Response.Body.WriteAsync(result.Bytes, context.RequestAborted);
        });
    }

    private static async Task Write(HttpContext context, IAntiforgery csrf, string origin,
        Func<TokenAuthority, Task> command)
    {
        if (!context.Request.IsHttps || context.Request.Headers.Origin.Count != 1 || context.Request.Headers.Origin[0] != origin)
            throw new UseCaseException("Forbidden");
        try { await csrf.ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { throw new UseCaseException("Forbidden"); }
        NoStore(context);
        try { await command(Token(context)); }
        catch (JsonException) { throw new UseCaseException("ValidationFailed"); }
    }

    private static TokenAuthority Token(HttpContext context) =>
        context.Features.Get<ValidatedCourseBearer>()?.Authority ?? throw new UseCaseException("Forbidden");
    private static async Task<T> Read<T>(HttpContext context) =>
        await JsonSerializer.DeserializeAsync<T>(context.Request.Body, Json, context.RequestAborted) ?? throw new JsonException();
    private static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store, private";
}
