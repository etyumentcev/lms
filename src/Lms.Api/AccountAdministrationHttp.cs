using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Application;
using Microsoft.AspNetCore.Antiforgery;

namespace Lms.Api;

public static class AccountAdministrationHttp
{
    public const string AccountSecurityCapability = "account-security:approve";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
      Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public sealed record ProvisionBody(string ExternalRosterKey, string DisplayName, string UserName,
        AccountRole Role, string IdempotencyKey);
    public sealed record IssueBody(string IdempotencyKey);
    public sealed record ActivateBody(Guid OrganizationId, string UserName, string Code, string Password);
    public sealed record RecoveryRequestBody(string VerificationRef, string IdempotencyKey);
    public sealed record RecoveryConsumeBody(Guid OrganizationId, string UserName, string Code, string NewPassword);
    public sealed record ApprovalBody(string VerificationRef, bool Approve);

    public static void MapAccountAdministration(this WebApplication app, string allowedOrigin)
    {
        app.MapGet("/api/v1/admin/accounts", async (HttpContext context, IAccountAdministration accounts) =>
        {
            var token = Admin(context); if (token == null) { await ApiErrors.Write(context, "Forbidden", 403); return; }
            AccountRole? role = Enum.TryParse<AccountRole>(context.Request.Query["role"], true, out var parsed) ? parsed : null;
            var offset = int.TryParse(context.Request.Query["offset"], out var o) ? o : 0;
            var limit = int.TryParse(context.Request.Query["limit"], out var l) ? l : 50;
            var result = await accounts.List(new(token.Authority.Membership.OrganizationId, token.Identity.ActorId,
                context.Request.Query["search"], role, context.Request.Query["state"], offset, limit), context.RequestAborted);
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(result, Json, context.RequestAborted);
        }).RequireAuthorization(p => p.AddAuthenticationSchemes(CourseBearerHandler.SchemeName).RequireAuthenticatedUser());

        app.MapGet("/api/v1/admin/accounts/{actorId:guid}", async (HttpContext context, Guid actorId, IAccountAdministration accounts) =>
        {
            var token = Admin(context); if (token == null) { await ApiErrors.Write(context, "Forbidden", 403); return; }
            var result = await accounts.Get(token.Authority.Membership.OrganizationId, token.Identity.ActorId, actorId, context.RequestAborted);
            if (result == null) { await ApiErrors.Write(context, "NotFound", 404); return; }
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(result, Json, context.RequestAborted);
        }).RequireAuthorization(p => p.AddAuthenticationSchemes(CourseBearerHandler.SchemeName).RequireAuthenticatedUser());

        app.MapPost("/api/v1/admin/accounts", (HttpContext context, IAntiforgery csrf, IAccountAdministration accounts) =>
            AdminWrite(context, csrf, allowedOrigin, async token =>
            {
                var body = await Read<ProvisionBody>(context);
                var result = await accounts.Provision(new(token.Authority.Membership.OrganizationId,
                    token.Identity.ActorId, body.ExternalRosterKey, body.DisplayName, body.UserName,
                    body.Role, body.IdempotencyKey, context.TraceIdentifier), context.RequestAborted);
                context.Response.StatusCode = result.Created ? 201 : 200;
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsJsonAsync(new { result.UserId, result.ActorId, result.OrganizationId,
                    result.DisplayName, result.UserName, result.Role, result.State, result.Created,
                    result.ChallengeId, activationCode = result.ActivationCode?.Reveal(), result.ApprovalRequestId }, Json, context.RequestAborted);
            })).RequireAuthorization(p => p.AddAuthenticationSchemes(CourseBearerHandler.SchemeName).RequireAuthenticatedUser());

        app.MapPost("/api/v1/admin/accounts/{actorId:guid}/activation", (HttpContext context, Guid actorId,
            IAntiforgery csrf, IAccountAdministration accounts) => AdminWrite(context, csrf, allowedOrigin, async token =>
            {
                var body = await Read<IssueBody>(context);
                var result = await accounts.IssueActivation(new(token.Authority.Membership.OrganizationId,
                    token.Identity.ActorId, actorId, body.IdempotencyKey, context.TraceIdentifier), context.RequestAborted);
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsJsonAsync(new { result.ChallengeId, result.Created,
                    activationCode = result.Code?.Reveal(), result.ExpiresAt }, Json, context.RequestAborted);
            })).RequireAuthorization(p => p.AddAuthenticationSchemes(CourseBearerHandler.SchemeName).RequireAuthenticatedUser());

        app.MapPost("/api/v1/admin/accounts/{actorId:guid}/recovery", (HttpContext context, Guid actorId,
            IAntiforgery csrf, IAccountAdministration accounts) => AdminWrite(context, csrf, allowedOrigin, async token =>
            {
                var body = await Read<RecoveryRequestBody>(context);
                var result = await accounts.RequestRecovery(new(token.Authority.Membership.OrganizationId,
                    token.Identity.ActorId, actorId, body.VerificationRef, body.IdempotencyKey,
                    context.TraceIdentifier), context.RequestAborted);
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsJsonAsync(result, Json, context.RequestAborted);
            })).RequireAuthorization(p => p.AddAuthenticationSchemes(CourseBearerHandler.SchemeName).RequireAuthenticatedUser());

        app.MapPost("/api/v1/auth/activate", (HttpContext context, IAntiforgery csrf, IAccountAdministration accounts) =>
            PublicWrite(context, csrf, allowedOrigin, async () =>
            {
                var body = await Read<ActivateBody>(context);
                await accounts.Activate(new(body.OrganizationId, body.UserName, body.Code, body.Password,
                    context.TraceIdentifier), context.RequestAborted);
                context.Response.Headers.CacheControl = "no-store"; context.Response.StatusCode = 204;
            })).RequireRateLimiting(OrganizationBootstrapHttp.ActivationRatePolicy);

        app.MapPost("/api/v1/auth/recover", (HttpContext context, IAntiforgery csrf, IAccountAdministration accounts) =>
            PublicWrite(context, csrf, allowedOrigin, async () =>
            {
                var body = await Read<RecoveryConsumeBody>(context);
                await accounts.ConsumeRecovery(new(body.OrganizationId, body.UserName, body.Code, body.NewPassword,
                    context.TraceIdentifier), context.RequestAborted);
                context.Response.Headers.CacheControl = "no-store"; context.Response.StatusCode = 204;
            })).RequireRateLimiting(OrganizationBootstrapHttp.ActivationRatePolicy);

        app.MapGet("/api/v1/platform/account-security/requests", async (HttpContext context, IAccountSecurityOperations security) =>
        {
            if (!Platform(context, out var hash)) { await ApiErrors.Write(context, "Forbidden", 403); return; }
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new { items = await security.Pending(hash!, context.RequestAborted) }, Json, context.RequestAborted);
        }).RequireAuthorization(p => p.AddAuthenticationSchemes(OrganizationBootstrapHttp.PlatformBearerScheme).RequireAuthenticatedUser());

        app.MapPost("/api/v1/platform/account-security/requests/{requestId:guid}", async (HttpContext context,
            Guid requestId, IAccountSecurityOperations security) =>
        {
            if (!ValidOrigin(context, allowedOrigin) || !Platform(context, out var hash)) { await ApiErrors.Write(context, "Forbidden", 403); return; }
            try
            {
                var body = await Read<ApprovalBody>(context);
                var result = await security.Decide(new(requestId, body.VerificationRef, hash!, body.Approve,
                    context.TraceIdentifier), context.RequestAborted);
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsJsonAsync(new { result.RequestId, result.State, result.ChallengeId,
                    recoveryCode = result.RecoveryCode?.Reveal(), result.ExpiresAt }, Json, context.RequestAborted);
            }
            catch (Exception e) when (e is UseCaseException or JsonException) { await Error(context, e); }
        }).RequireAuthorization(p => p.AddAuthenticationSchemes(OrganizationBootstrapHttp.PlatformBearerScheme).RequireAuthenticatedUser());
    }

    private static async Task AdminWrite(HttpContext context, IAntiforgery csrf, string origin,
        Func<TokenAuthority, Task> work)
    {
        try
        {
            if (!ValidOrigin(context, origin)) throw new UseCaseException("Forbidden");
            await csrf.ValidateRequestAsync(context);
            var token = Admin(context) ?? throw new UseCaseException("Forbidden");
            await work(token);
        }
        catch (Exception e) when (e is UseCaseException or JsonException or AntiforgeryValidationException) { await Error(context, e); }
    }
    private static async Task PublicWrite(HttpContext context, IAntiforgery csrf, string origin, Func<Task> work)
    {
        try
        {
            if (!ValidOrigin(context, origin)) throw new UseCaseException("Forbidden");
            await csrf.ValidateRequestAsync(context); await work();
        }
        catch (Exception e) when (e is UseCaseException or JsonException or AntiforgeryValidationException) { await Error(context, e); }
    }
    private static TokenAuthority? Admin(HttpContext context)
    {
        var token = context.Features.Get<ValidatedCourseBearer>()?.Authority;
        return token != null && OrganizationPermissionPolicy.CanManageOrganization(token.Authority.Membership) ? token : null;
    }
    private static bool Platform(HttpContext context, out string? hash)
    {
        hash = null;
        if (!HasCapability(context.User, AccountSecurityCapability) || context.User.FindFirstValue("sub") is not { Length: > 0 } subject) return false;
        hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subject))); return true;
    }
    private static bool HasCapability(ClaimsPrincipal principal, string capability) => principal.FindAll("scope")
        .Concat(principal.FindAll("scp")).SelectMany(x => x.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .Contains(capability, StringComparer.Ordinal);
    private static bool ValidOrigin(HttpContext context, string allowed) => context.Request.IsHttps &&
        context.Request.Headers.Origin.Count == 1 && context.Request.Headers.Origin[0] == allowed;
    private static async Task<T> Read<T>(HttpContext context) => await JsonSerializer.DeserializeAsync<T>(
        context.Request.Body, Json, context.RequestAborted) ?? throw new JsonException();
    private static Task Error(HttpContext context, Exception exception)
    {
        var (code, status) = exception switch
        {
            AntiforgeryValidationException => ("Forbidden", 403), JsonException => ("ValidationFailed", 400),
            UseCaseException useCase => ApiErrors.Map(useCase.Code), _ => ("InternalError", 500)
        };
        return ApiErrors.Write(context, code, status);
    }
}
