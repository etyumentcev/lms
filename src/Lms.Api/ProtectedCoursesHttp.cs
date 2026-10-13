using System.Data.Common;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Lms.Application;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Lms.Api;

// Explicit opt-in only: Program.cs registers this scheme as part of the
// TeacherRevocation host when the secure cutover configuration is enabled.
public sealed class CourseBearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, ComponentJwt validator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "CourseComponentBearer";
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var headers = Request.Headers.Authorization;
        if (headers.Count == 0) return Task.FromResult(AuthenticateResult.NoResult());
        if (headers.Count != 1 || !System.Net.Http.Headers.AuthenticationHeaderValue.TryParse(headers[0], out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(header.Parameter))
            return Task.FromResult(AuthenticateResult.Fail("Invalid bearer"));
        var bearer = new AccessToken(header.Parameter);
        var authority = validator.Validate(bearer);
        if (authority == null) return Task.FromResult(AuthenticateResult.Fail("Invalid bearer"));
        Context.Features.Set(new ValidatedCourseBearer(bearer, authority));
        var claims = new List<Claim> {
            new(ClaimTypes.NameIdentifier, authority.Identity.UserId.ToString()),
            new("sub", authority.Identity.ActorId.ToString()),
            new("organization_id", authority.Authority.Membership.OrganizationId.ToString()),
            new("organization_permissions", authority.Authority.Membership.Permissions.ToString())
        };
        if (authority.Authority.Membership.Role is { } role) claims.Add(new("role", role.ToString()));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
    }
}
internal sealed record ValidatedCourseBearer(AccessToken Bearer, TokenAuthority Authority);

// Install first in the isolated pipeline, before authentication/routing. This is the
// earliest application-controlled ingress; upstream queue/header timing is unproven.
public sealed class CourseRequestIngress(RequestDelegate next, QualifiedAuthorityClock clock)
    : TrustedApplicationIngressAdapter(clock)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var originalAbort = context.RequestAborted;
        var deadline = Capture(ApplicationRequestKind.Ordinary, originalAbort);
        context.Features.Set(deadline);
        using var cancellation = new DeadlineCancellation(deadline, TimeProvider.System, originalAbort);
        context.RequestAborted = cancellation.Token;
        try { await next(context); } // never detach the still-running handler on timeout
        finally { context.RequestAborted = originalAbort; }
    }
}

public static class ProtectedCoursesHttp
{
    public static void MapProtectedCourses(this WebApplication app) =>
        app.MapGet("/api/v1/courses", Execute).RequireAuthorization(policy =>
            policy.AddAuthenticationSchemes(CourseBearerHandler.SchemeName).RequireAuthenticatedUser());

    private static Task Execute(HttpContext context, ComponentJwt validator,
        ICourseNavigationQueries queries, ComponentAuthorization authorization)
    {
        CourseNavigationDto? result = null;
        return ExecuteBuffered(context, validator,
            async (token, _) => result = await queries.Courses(token, context.RequestAborted),
            async token =>
            {
                foreach (var item in result!.Items)
                    if (!await authorization.Allows(token, new(ResourceKind.Course, CourseId: item.Course.Id),
                        ScopeAction.Read, context.RequestAborted)) return false;
                return true;
            });
    }

    // Shared transport lifecycle; resource-specific reading and final gates stay in each adapter.
    internal static async Task ExecuteBuffered(HttpContext context, ComponentJwt validator,
        Func<TokenAuthority, ApplicationDeadline, Task<object>> read, Func<TokenAuthority, Task<bool>> canReturn,
        JsonSerializerOptions? serializerOptions = null)
    {
        context.Response.Headers.CacheControl = "no-store";
        var deadline = context.Features.Get<ApplicationDeadline>();
        var accepted = context.Features.Get<ValidatedCourseBearer>();
        if (accepted == null) { context.Response.StatusCode = 401; return; }
        if (deadline == null || !deadline.Check(ApplicationAction.Admission).Allowed)
        { context.Response.StatusCode = 503; return; }
        try
        {
            var result = await read(accepted.Authority, deadline);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(result,
                serializerOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (!await canReturn(accepted.Authority)) { context.Response.StatusCode = 403; return; }
            if (validator.Validate(accepted.Bearer) == null)
            { context.Response.StatusCode = 401; return; }
            if (!deadline.Check(ApplicationAction.ResponseEnqueue).Allowed)
            { context.Response.StatusCode = 503; return; }
            context.Response.ContentType = "application/json";
            await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
        }
        catch (UseCaseException) { context.Response.StatusCode = 403; }
        catch (DbException) { context.Response.StatusCode = 503; }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested ||
            !deadline.Check(ApplicationAction.StartStage).Allowed)
        { if (!context.Response.HasStarted) context.Response.StatusCode = 503; }
    }
}
