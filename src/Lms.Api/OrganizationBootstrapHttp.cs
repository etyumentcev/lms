using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Application;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Lms.Api;

public static class OrganizationBootstrapHttp
{
    public const string PlatformBearerScheme = "PlatformBootstrapBearer";
    public const string PlatformAudience = "sapientum-platform";
    public const string BootstrapCapability = "organization:bootstrap";
    public const string ActivationRatePolicy = "M1FirstAdminActivation";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    public sealed record BootstrapBody(Guid CommandId, string OrganizationName, string AdminUserName);
    public sealed record ActivationBody(Guid OrganizationId, string UserName, string ActivationCode, string Password);

    public static void MapOrganizationBootstrap(this WebApplication app, string allowedOrigin)
    {
        app.MapPost("/api/v1/platform/organizations", async (HttpContext context, IOrganizationBootstrap bootstrap) =>
        {
            if (!ValidOrigin(context, allowedOrigin)) { await ApiErrors.Write(context, "Forbidden", 403); return; }
            if (!HasCapability(context.User, BootstrapCapability) || context.User.FindFirstValue("sub") is not { Length: > 0 } subject)
            { await ApiErrors.Write(context, "Forbidden", 403); return; }
            try
            {
                var body = await Read<BootstrapBody>(context);
                var operatorHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subject)));
                var result = await bootstrap.Create(new(body.CommandId, body.OrganizationName, body.AdminUserName,
                    operatorHash, context.TraceIdentifier), context.RequestAborted);
                context.Response.StatusCode = result.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK;
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsJsonAsync(new { result.CommandId, result.OrganizationId,
                    result.OrganizationName, result.AdminUserId, result.AdminActorId, result.ChallengeId,
                    result.Created, activationCode = result.ActivationCode?.Reveal() }, Json, context.RequestAborted);
            }
            catch (Exception exception) when (exception is UseCaseException or JsonException)
            { await WriteError(context, exception); }
        }).RequireAuthorization(policy => policy.AddAuthenticationSchemes(PlatformBearerScheme)
            .RequireAuthenticatedUser());

        app.MapPost("/api/v1/auth/activate-first-admin", async (HttpContext context, IAntiforgery csrf,
            IOrganizationBootstrap bootstrap) =>
        {
            if (!ValidOrigin(context, allowedOrigin)) { await ApiErrors.Write(context, "Forbidden", 403); return; }
            try
            {
                await csrf.ValidateRequestAsync(context);
                var body = await Read<ActivationBody>(context);
                await bootstrap.Activate(new(body.OrganizationId, body.UserName, body.ActivationCode,
                    body.Password, context.TraceIdentifier), context.RequestAborted);
                context.Response.Headers.CacheControl = "no-store";
                context.Response.StatusCode = StatusCodes.Status204NoContent;
            }
            catch (Exception exception) when (exception is UseCaseException or AntiforgeryValidationException or JsonException)
            { await WriteError(context, exception); }
        }).RequireRateLimiting(ActivationRatePolicy);

        app.MapGet("/api/v1/admin/organization", async (HttpContext context,
            IOrganizationAdministrationQueries queries) =>
        {
            var token = AdminToken(context);
            if (token == null) { await ApiErrors.Write(context, "Forbidden", 403); return; }
            var result = await queries.Overview(token.Authority.Membership.OrganizationId,
                token.Identity.ActorId, context.RequestAborted);
            if (result == null) { await ApiErrors.Write(context, "Forbidden", 403); return; }
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(result, Json, context.RequestAborted);
        }).RequireAuthorization(policy => policy.AddAuthenticationSchemes(CourseBearerHandler.SchemeName)
            .RequireAuthenticatedUser());

        app.MapGet("/api/v1/admin/audit", async (HttpContext context,
            IOrganizationAdministrationQueries queries) =>
        {
            var token = AdminToken(context);
            if (token == null) { await ApiErrors.Write(context, "Forbidden", 403); return; }
            var limit = int.TryParse(context.Request.Query["limit"], out var value) ? value : 20;
            var result = await queries.Audit(token.Authority.Membership.OrganizationId,
                token.Identity.ActorId, limit, context.RequestAborted);
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new { items = result }, Json, context.RequestAborted);
        }).RequireAuthorization(policy => policy.AddAuthenticationSchemes(CourseBearerHandler.SchemeName)
            .RequireAuthenticatedUser());
    }

    private static TokenAuthority? AdminToken(HttpContext context)
    {
        var accepted = context.Features.Get<ValidatedCourseBearer>()?.Authority;
        return accepted != null && OrganizationPermissionPolicy.CanManageOrganization(accepted.Authority.Membership)
            ? accepted : null;
    }
    private static bool HasCapability(ClaimsPrincipal principal, string capability)
        => principal.FindAll("scope").SelectMany(x => x.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Concat(principal.FindAll("scp").SelectMany(x => x.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .Contains(capability, StringComparer.Ordinal);
    private static bool ValidOrigin(HttpContext context, string allowedOrigin)
        => context.Request.IsHttps && context.Request.Headers.Origin.Count == 1 &&
           string.Equals(context.Request.Headers.Origin[0], allowedOrigin, StringComparison.Ordinal);
    private static async Task<T> Read<T>(HttpContext context)
        => await JsonSerializer.DeserializeAsync<T>(context.Request.Body, Json, context.RequestAborted)
            ?? throw new JsonException();
    private static Task WriteError(HttpContext context, Exception exception)
    {
        var (code, status) = exception switch
        {
            AntiforgeryValidationException => ("Forbidden", 403),
            JsonException => ("ValidationFailed", 400),
            UseCaseException useCase => ApiErrors.Map(useCase.Code),
            _ => ("InternalError", 500)
        };
        return ApiErrors.Write(context, code, status);
    }
}
