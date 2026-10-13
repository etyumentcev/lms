using System.Text.Json;
using System.Text.Json.Serialization;
using Lms.Application;
using Microsoft.AspNetCore.Antiforgery;

namespace Lms.Api;

internal sealed record CreateProcessingContextBody(Guid OperationId, long ExpectedRevision,
    Guid? ExpectedContextId, long? ExpectedContextVersion, DateTimeOffset EffectiveFrom);

internal sealed record BindProcessingContextBody(Guid OperationId, long ExpectedRevision,
    Guid ContextId, long ContextVersion, DateTimeOffset? ExpectedBindingFrom,
    DateTimeOffset? EffectiveFrom);

public static class ProcessingContextAdministrationHttp
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void MapProcessingContextAdministration(this WebApplication app, string allowedOrigin)
    {
        var api = app.MapGroup("/api/v1/admin").RequireAuthorization(policy => policy
            .AddAuthenticationSchemes(CourseBearerHandler.SchemeName).RequireAuthenticatedUser());

        api.MapGet("/processing-context", async (HttpContext context,
            IProcessingContextAdministration processing) =>
        {
            var token = Admin(context);
            var result = await processing.State(token.Authority.Membership.OrganizationId,
                token.Identity.ActorId, context.RequestAborted);
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(result, Json, context.RequestAborted);
        });

        api.MapPost("/processing-context/versions", (HttpContext context, IAntiforgery csrf,
            IProcessingContextAdministration processing) => Write(context, csrf, allowedOrigin,
            async token =>
            {
                var body = await Read<CreateProcessingContextBody>(context);
                var result = await processing.CreateVersion(new(
                    token.Authority.Membership.OrganizationId, token.Identity.ActorId,
                    body.OperationId, body.ExpectedRevision, body.ExpectedContextId,
                    body.ExpectedContextVersion, body.EffectiveFrom), context.RequestAborted);
                context.Response.StatusCode = result.Changed ? StatusCodes.Status201Created : StatusCodes.Status200OK;
                await context.Response.WriteAsJsonAsync(result, Json, context.RequestAborted);
            }));

        api.MapPost("/courses/{courseId}/processing-context", (HttpContext context,
            string courseId, IAntiforgery csrf, IProcessingContextAdministration processing) =>
            Write(context, csrf, allowedOrigin, async token =>
            {
                var body = await Read<BindProcessingContextBody>(context);
                var result = await processing.BindCourse(new(
                    token.Authority.Membership.OrganizationId, token.Identity.ActorId, courseId,
                    body.OperationId, body.ExpectedRevision, body.ContextId, body.ContextVersion,
                    body.ExpectedBindingFrom, body.EffectiveFrom), context.RequestAborted);
                context.Response.StatusCode = result.Changed ? StatusCodes.Status201Created : StatusCodes.Status200OK;
                await context.Response.WriteAsJsonAsync(result, Json, context.RequestAborted);
            }));
    }

    private static async Task Write(HttpContext context, IAntiforgery csrf, string allowedOrigin,
        Func<TokenAuthority, Task> command)
    {
        try
        {
            if (!context.Request.IsHttps || context.Request.Headers.Origin.Count != 1 ||
                context.Request.Headers.Origin[0] != allowedOrigin)
                throw new UseCaseException("Forbidden");
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
        if (token == null || !OrganizationPermissionPolicy.CanManageOrganization(
                token.Authority.Membership))
            throw new UseCaseException("Forbidden");
        return token;
    }

    private static async Task<T> Read<T>(HttpContext context) =>
        await JsonSerializer.DeserializeAsync<T>(context.Request.Body, Json, context.RequestAborted)
        ?? throw new JsonException();
}
