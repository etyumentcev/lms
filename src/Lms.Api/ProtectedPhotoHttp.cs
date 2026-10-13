using System.Data.Common;
using System.Text.Json;
using Lms.Application;
using Lms.Infrastructure;

namespace Lms.Api;

// Install only in the isolated synthetic host. Four concurrent bounded photo requests;
// no queue and no automatic retries. Not a durable upload/recovery capacity manager.
public sealed class PhotoReadCapacity : IDisposable
{
    internal SemaphoreSlim Slots { get; } = new(4, 4);
    public void Dispose() => Slots.Dispose();
}
public static class ProtectedPhotoHttp
{
    public static void MapProtectedPhotoReads(this WebApplication app)
    {
        const string root = "/api/v1/courses/{courseId}/versions/{versionId}/lessons/{lessonId}/works/{workId}/submissions/{submissionId}/photos";
        foreach (var route in new[] { root, root + "/{attachmentId}" })
            app.MapGet(route, Execute).RequireAuthorization(p => p.AddAuthenticationSchemes(CourseBearerHandler.SchemeName).RequireAuthenticatedUser());
    }
    private static async Task Execute(HttpContext context, ComponentJwt jwt, IProtectedPhotoQueries queries, PhotoReadCapacity capacity)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        var deadline = context.Features.Get<ApplicationDeadline>();
        var accepted = context.Features.Get<ValidatedCourseBearer>();
        if (accepted == null) { context.Response.StatusCode = 401; return; }
        if (deadline == null || !deadline.Check(ApplicationAction.Admission).Allowed || !capacity.Slots.Wait(0))
        { context.Response.StatusCode = 503; return; }
        try
        {
            string? Route(string n) => context.Request.RouteValues[n]?.ToString();
            if (!Guid.TryParse(Route("versionId"), out var version) || !Guid.TryParse(Route("workId"), out var work) ||
                !Guid.TryParse(Route("submissionId"), out var submission) || Route("attachmentId") != null && !Guid.TryParse(Route("attachmentId"), out _))
                throw new UseCaseException("Forbidden");
            var read = await queries.Read(accepted.Authority, new(new(Route("courseId")!, version, Route("lessonId")!, WorkId: work),
                submission, Route("attachmentId") == null ? null : Guid.Parse(Route("attachmentId")!)), deadline, context.RequestAborted);
            if (!await queries.CanReturn(accepted.Authority, read, deadline, context.RequestAborted)) { context.Response.StatusCode = 403; return; }
            if (jwt.Validate(accepted.Bearer) == null) { context.Response.StatusCode = 401; return; }
            var bytes = read.Value is PhotoBytes photo ? photo.Bytes : JsonSerializer.SerializeToUtf8Bytes(read.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (!deadline.Check(ApplicationAction.ResponseEnqueue).Allowed) { context.Response.StatusCode = 503; return; }
            context.Response.ContentType = read.Value is PhotoBytes p ? p.MediaType : "application/json";
            // Range is deliberately ignored: full bounded response, with all gates, never a bypass.
            context.Response.ContentLength = bytes.Length;
            await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
        }
        catch (UseCaseException) { context.Response.StatusCode = 403; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DbException)
        { if (!context.Response.HasStarted) context.Response.StatusCode = 503; }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested || !deadline.Check(ApplicationAction.StartStage).Allowed)
        { if (!context.Response.HasStarted) context.Response.StatusCode = 503; }
        finally { capacity.Slots.Release(); }
    }
}
