using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lms.Application;
using Lms.Domain;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api;

// Public only through the explicitly enabled Program composition. Teacher writes/files
// never fall back to legacy actors; student message/content behavior remains available.
public static class TeacherRevocationHttp
{
    private const string RefreshCookie = "__Host-LmsRefresh";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
    public sealed record LoginBody(string UserName, string Password, Guid OrganizationId);
    public sealed record OrganizationBody(Guid OrganizationId);
    public sealed record ReviewBody(Guid CommandId, Guid SubmissionId, long ExpectedVersion, string? Feedback, AnnotatedAttachment[]? Annotations);
    public sealed record MessageBody(Guid CommandId, Guid IntentId, string Body, Guid? ReplyTo, Guid[] Attachments);

    public static IServiceCollection AddTeacherRevocationBrowserProtection(this IServiceCollection services)
    {
        services.AddAntiforgery(o => {
            o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.Name = "__Host-LmsCsrf";
            o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Strict; o.Cookie.Path = "/";
        });
        return services;
    }

    public static void MapTeacherRevocation(this WebApplication app, string allowedOrigin,
        bool restrictedDatabaseCapabilities = false)
    {
        if (!Uri.TryCreate(allowedOrigin, UriKind.Absolute, out var origin) || origin.Scheme != "https" ||
            origin.GetLeftPart(UriPartial.Authority) != allowedOrigin) throw new ArgumentException("One exact HTTPS origin required.");

        app.MapGet("/api/v1/auth/csrf", async (HttpContext context, IAntiforgery csrf) => {
            context.Response.Headers.CacheControl = "no-store";
            if (!context.Request.IsHttps || context.Features.Get<ApplicationDeadline>() is not { } d ||
                !d.Check(ApplicationAction.ResponseEnqueue).Allowed)
            { context.Response.StatusCode = 503; return; }
            if (context.Request.Headers.Authorization.Count > 0)
            {
                var authenticated = await context.AuthenticateAsync(CourseBearerHandler.SchemeName);
                if (!authenticated.Succeeded || authenticated.Principal == null) { context.Response.StatusCode = 401; return; }
                context.User = authenticated.Principal;
            }
            await context.Response.WriteAsJsonAsync(new { token = csrf.GetAndStoreTokens(context).RequestToken },
                Json, context.RequestAborted);
        });

        foreach (var action in new[] { "login", "refresh", "switch-organization" })
            app.MapPost("/api/v1/auth/" + action, (HttpContext context, IAntiforgery csrf,
                IDbContextFactory<LmsDbContext> contexts, IAuthorityClock clock, ComponentJwt jwt) =>
                Run(context, csrf, allowedOrigin, async d => {
                    await using var db = await contexts.CreateDbContextAsync(context.RequestAborted);
                    var issuer = new PostgresComponentTokenIssuer(db, clock, jwt, restrictedDatabaseCapabilities);
                    SessionResult result;
                    if (action == "login")
                    {
                        var body = await Body<LoginBody>(context);
                        result = await issuer.Login(body.UserName, body.Password, body.OrganizationId, d, context.RequestAborted);
                    }
                    else
                    {
                        var body = await Body<OrganizationBody>(context);
                        if (!context.Request.Cookies.TryGetValue(RefreshCookie, out var refresh))
                            throw new UseCaseException("Forbidden");
                        result = await issuer.Refresh(new(refresh), body.OrganizationId, d, context.RequestAborted);
                    }
                    if (result.Outcome != SessionOutcome.Success)
                    {
                        var status = result.Outcome == SessionOutcome.RefreshAlreadyRotated ? 409 : 401;
                        await ApiErrors.Write(context, result.ErrorCode ?? (status == 409 ? "refresh_already_rotated" : "Unauthenticated"), status);
                        return;
                    }
                    var authority = result.AccessToken is { } issued ? jwt.Validate(issued) : null;
                    var hasEducationalScope = authority?.Authority.Membership.Role is ScopeRole.Student or ScopeRole.Teacher &&
                        authority.Authority.Grants.Count > 0;
                    var hasOrganizationAdminScope = authority != null &&
                        OrganizationPermissionPolicy.CanManageOrganization(authority.Authority.Membership);
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(new {
                        accessToken = result.AccessToken?.Reveal(), hasEducationalScope, hasOrganizationAdminScope
                    }, Json);
                    Gate(d);
                    context.Response.OnStarting(() => {
                        if (result.AccessToken is { } access && jwt.Validate(access) == null)
                            throw new OperationCanceledException();
                        return Task.CompletedTask;
                    });
                    context.Response.Cookies.Append(RefreshCookie, result.RefreshToken!.Reveal(), new CookieOptions {
                        HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/",
                        Expires = result.Identity!.AbsoluteExpiresAt
                    });
                    context.Response.ContentType = "application/json";
                    await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
                }));

        app.MapPost("/api/v1/auth/logout", (HttpContext context, IAntiforgery csrf,
            IDbContextFactory<LmsDbContext> contexts, IAuthorityClock clock) =>
            Run(context, csrf, allowedOrigin, async d => {
                Gate(d);
                if (context.Request.Cookies.TryGetValue(RefreshCookie, out var refresh))
                {
                    await using var db = await contexts.CreateDbContextAsync(context.RequestAborted);
                    var provider = new AuthorityTimeProvider(clock);
                    await new PostgresIdentitySessions(db, provider, restrictedDatabaseCapabilities)
                        .Revoke(new(refresh), context.RequestAborted);
                }
                Gate(d);
                context.Response.Cookies.Delete(RefreshCookie, new CookieOptions {
                    HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/"
                });
                context.Response.StatusCode = StatusCodes.Status204NoContent;
            }));

        foreach (var revision in new[] { true, false })
            app.MapPost("/api/v1/problem-works/{workId:guid}/reviews/" + (revision ? "needs-revision" : "accept"),
                (HttpContext context, Guid workId, IAntiforgery csrf, PostgresTeacherCommands commands) =>
                    Run(context, csrf, allowedOrigin, async d => {
                        var body = await Body<ReviewBody>(context);
                        if (body.CommandId == Guid.Empty || body.SubmissionId == Guid.Empty || body.ExpectedVersion < 0)
                            throw new UseCaseException("ValidationFailed");
                        var result = await commands.Review(Bearer(context), d, body.CommandId, workId,
                            body.SubmissionId, body.ExpectedVersion, revision, body.Feedback, body.Annotations,
                            context.RequestAborted);
                        await Write(context, d, result);
                    })).RequireAuthorization(p => p.AddAuthenticationSchemes(CourseBearerHandler.SchemeName)
                        .RequireAuthenticatedUser());

        app.MapPost("/api/v1/problem-works/{workId:guid}/messages",
            async (HttpContext context, Guid workId, IAntiforgery csrf, PostgresTeacherCommands commands,
                [FromServices] ProblemWorkService service, [FromServices] IActorMapper actors) =>
            {
                var actor = await AuthenticateActor(context, actors);
                if (actor == null) return;
                if (actor.Role == AuthorRole.Teacher)
                {
                    if (context.Features.Get<ValidatedCourseBearer>() == null)
                    { await ApiErrors.Write(context, "Forbidden", 403); return; }
                    await Run(context, csrf, allowedOrigin, async d => {
                        var body = await Body<MessageBody>(context);
                        if (body.CommandId == Guid.Empty || body.IntentId == Guid.Empty || body.Attachments == null)
                            throw new UseCaseException("ValidationFailed");
                        await Write(context, d, await commands.Message(Bearer(context), d, body.CommandId,
                            body.IntentId, workId, body.Body, body.ReplyTo, body.Attachments,
                            context.RequestAborted));
                    });
                    return;
                }
                await Legacy(context, async () => {
                    var body = await Body<MessageRequest>(context);
                    if (body.AttachmentIds == null || body.BodyMarkdown == null)
                        throw new UseCaseException("ValidationFailed");
                    var result = await service.PostMessage(actor, workId, body.BodyMarkdown,
                        body.ReplyToMessageId, body.AttachmentIds, context.RequestAborted);
                    await context.Response.WriteAsJsonAsync(result, Json, context.RequestAborted);
                });
            });

        app.MapPost("/api/v1/problem-works/{workId:guid}/uploads/{intentId:guid}",
            (HttpContext context, Guid workId, Guid intentId, IAntiforgery csrf,
                PostgresTeacherCommands commands) => Run(context, csrf, allowedOrigin, async d => {
                if (!Guid.TryParse(context.Request.Headers["X-Command-Id"], out var command))
                    throw new UseCaseException("ValidationFailed");
                var encodedName = context.Request.Headers["X-File-Name"].ToString();
                var fileName = Uri.UnescapeDataString(encodedName);
                var result = await commands.Upload(Bearer(context), d, command, intentId, workId,
                    fileName, context.Request.ContentType ?? "",
                    context.Request.Headers["X-Content-SHA256"].ToString(), context.Request.Body,
                    context.RequestAborted);
                await Write(context, d, result);
            }, 21 * 1024 * 1024)).RequireAuthorization(p =>
                p.AddAuthenticationSchemes(CourseBearerHandler.SchemeName).RequireAuthenticatedUser());

        app.MapDelete("/api/v1/problem-works/{workId:guid}/attachments/{id:guid}",
            (HttpContext context, Guid workId, Guid id, IAntiforgery csrf,
                PostgresTeacherCommands commands) => Run(context, csrf, allowedOrigin, async d => {
                if (!Guid.TryParse(context.Request.Headers["X-Command-Id"], out var command))
                    throw new UseCaseException("ValidationFailed");
                await commands.Delete(Bearer(context), d, command, workId, id, context.RequestAborted);
                Gate(d); context.Response.StatusCode = 204;
            })).RequireAuthorization(p => p.AddAuthenticationSchemes(CourseBearerHandler.SchemeName)
                .RequireAuthenticatedUser());

        app.MapGet("/api/v1/attachments/{id:guid}/content",
            async (HttpContext context, Guid id, IAntiforgery csrf, PostgresTeacherCommands commands,
                [FromServices] AttachmentService service, [FromServices] IActorMapper actors) =>
            {
                var actor = await AuthenticateActor(context, actors);
                if (actor == null) return;
                if (actor.Role == AuthorRole.Teacher)
                {
                    if (context.Features.Get<ValidatedCourseBearer>() == null)
                    { await ApiErrors.Write(context, "Forbidden", 403); return; }
                    await Run(context, csrf, allowedOrigin, async d => {
                        var file = await commands.ReadAttachment(Bearer(context), d, id, context.RequestAborted);
                        Gate(d); context.Response.ContentType = file.MediaType;
                        context.Response.Headers.XContentTypeOptions = "nosniff";
                        await context.Response.Body.WriteAsync(file.Bytes, context.RequestAborted);
                    }, safe: true);
                    return;
                }
                await Legacy(context, async () => {
                    var file = await service.Open(actor, id, context.RequestAborted);
                    context.Response.Headers.CacheControl = "private, no-store";
                    context.Response.Headers.XContentTypeOptions = "nosniff";
                    context.Response.ContentType = file.MediaType;
                    await using var content = file.Content;
                    await content.CopyToAsync(context.Response.Body, context.RequestAborted);
                });
            });
    }

    private static async Task<Actor?> AuthenticateActor(HttpContext context, IActorMapper actors)
    {
        var component = await context.AuthenticateAsync(CourseBearerHandler.SchemeName);
        if (component.Succeeded && component.Principal != null)
        {
            context.User = component.Principal;
            return actors.Map(component.Principal);
        }
        var fallback = await context.AuthenticateAsync();
        if (!fallback.Succeeded || fallback.Principal == null)
        { context.Response.StatusCode = 401; return null; }
        context.User = fallback.Principal;
        return actors.Map(fallback.Principal);
    }

    private static AccessToken Bearer(HttpContext context) =>
        context.Features.Get<ValidatedCourseBearer>()?.Bearer ?? throw new UseCaseException("Forbidden");

    private static async Task<T> Body<T>(HttpContext context) =>
        await context.Request.ReadFromJsonAsync<T>(Json, context.RequestAborted) ??
        throw new UseCaseException("ValidationFailed");

    private static void Gate(ApplicationDeadline deadline)
    {
        if (!deadline.Check(ApplicationAction.ResponseEnqueue).Allowed)
            throw new OperationCanceledException();
    }

    private static async Task Write<T>(HttpContext context, ApplicationDeadline deadline, T result)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result, Json);
        Gate(deadline);
        context.Response.ContentType = "application/json";
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
    }

    private static async Task Legacy(HttpContext context, Func<Task> run)
    {
        try { await run(); }
        catch (Exception exception) when (exception is UseCaseException or DomainException or JsonException)
        {
            if (context.Response.HasStarted) { context.Abort(); return; }
            var (code, status) = exception switch {
                UseCaseException useCase => ApiErrors.Map(useCase.Code),
                DomainException domain => ApiErrors.Map(domain.Code),
                _ => ("ValidationFailed", 400)
            };
            await ApiErrors.Write(context, code, status);
        }
    }

    private static async Task Run(HttpContext context, IAntiforgery csrf, string origin,
        Func<ApplicationDeadline, Task> run, long maxBytes = AnnotationRules.MaxRequestBytes, bool safe = false)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (context.Features.Get<ApplicationDeadline>() is not { } deadline)
        { context.Response.StatusCode = 503; return; }
        try
        {
            Gate(deadline);
            context.Response.OnStarting(() => { Gate(deadline); return Task.CompletedTask; });
            if (!context.Request.IsHttps || !safe &&
                (context.Request.Headers.Origin.Count != 1 || context.Request.Headers.Origin[0] != origin))
            { context.Response.StatusCode = 403; return; }
            var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = maxBytes;
            if (context.Request.ContentLength > maxBytes)
            { context.Response.StatusCode = 413; return; }
            if (!safe) await csrf.ValidateRequestAsync(context);
            await run(deadline);
        }
        catch (Exception exception) when (exception is UseCaseException or DomainException or
            AntiforgeryValidationException or JsonException or DbException or IOException or OperationCanceledException)
        {
            if (context.Response.HasStarted) { context.Abort(); return; }
            context.Response.Headers.Remove("Set-Cookie");
            var (code, status) = exception switch
            {
                UseCaseException useCase => ApiErrors.Map(useCase.Code),
                DomainException domain => ApiErrors.Map(domain.Code),
                AntiforgeryValidationException => ("Forbidden", 403),
                JsonException => ("ValidationFailed", 400),
                _ => ("OperationOutcomeUnknown", 503)
            };
            await ApiErrors.Write(context, code, status);
        }
    }

    private sealed class AuthorityTimeProvider(IAuthorityClock clock) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => clock.Read() is { Valid: true } time ? time.Latest :
            throw new OperationCanceledException("Authority clock unavailable");
    }
}
