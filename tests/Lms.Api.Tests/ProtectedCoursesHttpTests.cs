using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Lms.Api;
using Lms.Application;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lms.Api.Tests;

public sealed partial class IsolatedProcessingDeploymentTests
{
    // Only in the test assembly. Zero-error synthetic observations are NOT a
    // production qualification or evidence about this machine's clock.
    private sealed class HttpClock : ILocalClockProbe, ILocalClockQualificationVerifier
    {
        public readonly DateTimeOffset At = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        public TimeSpan Elapsed;
        public bool Current = true;
        public LocalClockObservation Read() => new("synthetic-http", "synthetic-boot",
            new(At + Elapsed, At + Elapsed), Elapsed, Elapsed, Elapsed, Elapsed);
        public ConfirmedClockEnvelope Confirm(ClockQualificationRequest r) => new(r.Nonce, "synthetic-only",
            r.Anchor.WallUtc, 0, TimeSpan.FromHours(1), At.AddHours(2), TimeSpan.Zero, TimeSpan.Zero);
        public bool IsCurrent(ConfirmedClockEnvelope e, LocalClockObservation o) => Current;
        public QualifiedAuthorityClock Create()
        {
            var source = new LocalQualifiedClockSource(this, this);
            Assert.True(source.Qualify());
            return new(source, TimeSpan.Zero, TimeSpan.Zero);
        }
    }
    private sealed class HttpTimeProvider(HttpClock time) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => time.At + time.Elapsed; }
    private sealed class AfterCourses(ICourseNavigationQueries inner, Func<Task> after) : ICourseNavigationQueries
    {
        public async Task<CourseNavigationDto> Courses(TokenAuthority token, CancellationToken cancellationToken = default)
        { var result = await inner.Courses(token, cancellationToken); await after(); return result; }
    }
    private async Task<WebApplication> HttpHost(QualifiedAuthorityClock clock, ComponentJwt jwt,
        Func<Task>? after = null, Action<Microsoft.AspNetCore.Http.HttpContext>? ingress = null, CancellationToken aborted = default, Func<Task>? afterContent = null, string? browserRoot = null, bool workReads = false, Func<Task>? afterWork = null, IAttachmentStorage? photoStorage = null, Func<Task>? afterPhoto = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        if (browserRoot == null) builder.WebHost.UseTestServer();
        else builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSingleton(clock); builder.Services.AddSingleton(jwt);
        builder.Services.AddAuthentication(CourseBearerHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, CourseBearerHandler>(CourseBearerHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        // Separate contexts, including current-processing transactions; all use the
        // restricted runtime login. DI owns/disposes every context per request.
        builder.Services.AddScoped(_ => new HttpDatabases(runtime));
        builder.Services.AddScoped(sp => new ComponentAuthorization(
            new PostgresScopedAuthorityReader(sp.GetRequiredService<HttpDatabases>().Resources, TimeProvider.System),
            new PostgresCurrentProcessingGate(sp.GetRequiredService<HttpDatabases>().Processing, clock), clock));
        builder.Services.AddScoped<ICourseNavigationQueries>(sp => new AfterCourses(new PostgresCourseNavigationQueries(
            sp.GetRequiredService<HttpDatabases>().Content, sp.GetRequiredService<ComponentAuthorization>(), clock), after ?? (() => Task.CompletedTask)));
        builder.Services.AddScoped<IWorkspaceContentQueries>(sp => new AfterContent(new PostgresWorkspaceContentQueries(
            sp.GetRequiredService<HttpDatabases>().Content, sp.GetRequiredService<ComponentAuthorization>(), clock),
            afterContent ?? (() => Task.CompletedTask)));
        if (workReads) builder.Services.AddScoped<IProtectedWorkQueries>(sp => new AfterWork(new PostgresProtectedWorkQueries(
            sp.GetRequiredService<HttpDatabases>().Content, sp.GetRequiredService<ComponentAuthorization>(),
            new PostgresScopedAuthorityReader(sp.GetRequiredService<HttpDatabases>().Resources, TimeProvider.System),
            new PostgresCurrentProcessingGate(sp.GetRequiredService<HttpDatabases>().Processing, clock), photoStorage != null), afterWork ?? (() => Task.CompletedTask)));
        if (photoStorage != null)
        {
            builder.Services.AddSingleton<PhotoReadCapacity>();
            builder.Services.AddScoped<IProtectedPhotoQueries>(sp => new AfterPhoto(new PostgresProtectedPhotoQueries(
                sp.GetRequiredService<HttpDatabases>().Content, sp.GetRequiredService<IProtectedWorkQueries>(),
                new PostgresScopedAuthorityReader(sp.GetRequiredService<HttpDatabases>().Resources, TimeProvider.System),
                new PostgresCurrentProcessingGate(sp.GetRequiredService<HttpDatabases>().Processing, clock), photoStorage), afterPhoto ?? (() => Task.CompletedTask)));
        }
        var app = builder.Build();
        if (aborted.CanBeCanceled) app.Use(async (ctx, next) => { ctx.RequestAborted = aborted; await next(ctx); });
        app.UseMiddleware<CourseRequestIngress>();
        app.Use(async (ctx, next) => { ingress?.Invoke(ctx); await next(ctx); });
        app.UseAuthentication(); app.UseAuthorization(); app.MapProtectedCourses(); app.MapProtectedWorkspace();
        if (workReads) app.MapProtectedWorkReads();
        if (photoStorage != null) app.MapProtectedPhotoReads();
        if (browserRoot != null)
        {
            // Public build assets only; no repo files, fixtures, keys or auth/control endpoint.
            app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(browserRoot) });
            app.MapFallback(async ctx => {
                if (ctx.Request.Path.StartsWithSegments("/api") || !ctx.Request.Path.StartsWithSegments("/courses"))
                { ctx.Response.StatusCode = 404; return; }
                ctx.Response.ContentType = "text/html; charset=utf-8";
                await Microsoft.AspNetCore.Http.SendFileResponseExtensions.SendFileAsync(ctx.Response,
                    Path.Combine(browserRoot, "browser-read", "index.html"));
            });
        }
        await app.StartAsync(); return app;
    }
    private sealed class HttpDatabases(string cs) : IAsyncDisposable
    {
        public LmsDbContext Resources { get; } = Open(cs);
        public LmsDbContext Processing { get; } = Open(cs);
        public LmsDbContext Content { get; } = Open(cs);
        public async ValueTask DisposeAsync()
        { await Resources.DisposeAsync(); await Processing.DisposeAsync(); await Content.DisposeAsync(); }
    }
    private async Task<(Guid User, Guid Actor, Guid Session, AuthorityCommand Grant, Guid Context)> HttpSeed(ScopeRole role, HttpClock time)
    {
        var identity = await IdentitySeed();
        SessionResult login;
        await using (var db = Open(runtime)) login = await new PostgresIdentitySessions(db, new HttpTimeProvider(time), true).Authenticate(identity.Name, identity.Password);
        Assert.Equal(SessionOutcome.Success, login.Outcome);
        await ChangeRestricted(new(AuthorityKind.Membership, AuthorityOperation.Grant, existingOrg, identity.Actor, 0, Role: role));
        await Sql(owner, $"INSERT INTO \"CourseRegistry\" VALUES('http-own','{existingOrg}','Verified'),('http-other','{existingOrg}','Verified'); INSERT INTO \"CourseGroups\" VALUES('http-own','g'),('http-own','h')");
        var grant = new AuthorityCommand(role == ScopeRole.Student ? AuthorityKind.Enrollment : AuthorityKind.Teacher,
            AuthorityOperation.Grant, existingOrg, identity.Actor, 0, 1, "http-own", "g", role == ScopeRole.Teacher ? Guid.NewGuid() : null);
        await ChangeRestricted(grant);
        var foreign = Guid.NewGuid();
        await Sql(owner, $"INSERT INTO \"Organizations\" VALUES('{foreign}','Enabled',true); INSERT INTO \"CourseRegistry\" VALUES('http-foreign','{foreign}','Verified')");
        var context = Guid.NewGuid();
        await using (var db = Open(processing))
            await new PostgresProcessingStateAdministration(db).Change(new(existingOrg, Guid.NewGuid(), 0,
                new AddProcessingContext(context, 1, time.At.AddHours(-1))));
        await using (var db = Open(processing))
            await new PostgresProcessingStateAdministration(db).Change(new(existingOrg, Guid.NewGuid(), 1,
                new OpenProcessingBinding("http-own", context, 1, time.At.AddMinutes(-30))));
        await using (var db = Open(owner))
        {
            db.Add(new PublishedContentVersion { Id = Guid.NewGuid(), CourseId = "http-own", VersionNumber = 1,
                SourceRevision = "synthetic-http", PublishedAt = time.At,
                ContentJson = JsonSerializer.Serialize(new PublishedCourseContent("HTTP private title", "Private description", []), new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
            await db.SaveChangesAsync();
        }
        return (identity.User, identity.Actor, login.Identity!.SessionId, grant, context);
    }
    private async Task<AccessToken?> HttpIssue(Guid user, Guid session, QualifiedAuthorityClock clock, ComponentJwt jwt, Guid? org = null)
    { await using var db = Open(runtime); return await new PostgresComponentTokenIssuer(db, clock, jwt, true).Issue(user, session, org ?? existingOrg); }
    private static async Task<string> HttpGet(WebApplication app, AccessToken? token, HttpStatusCode expected,
        string path = "/api/v1/courses")
    {
        using var client = app.GetTestClient();
        if (token != null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Reveal());
        client.DefaultRequestHeaders.Add("X-Role", "Teacher"); client.DefaultRequestHeaders.Add("X-OrganizationId", Guid.NewGuid().ToString());
        var response = await client.GetAsync(path); Assert.Equal(expected, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        if (expected != HttpStatusCode.OK) Assert.Equal("", body);
        else Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        return body;
    }
    [Theory]
    [InlineData(ScopeRole.Student)] [InlineData(ScopeRole.Teacher)]
    public async Task Http_courses_positive_scope_and_delayed_grant_session_revocation(ScopeRole role)
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "http", "http", clock); var seed = await HttpSeed(role, time);
        var token = await HttpIssue(seed.User, seed.Session, clock, jwt); Assert.NotNull(token);
        await using var app = await HttpHost(clock, jwt);
        var body = await HttpGet(app, token, HttpStatusCode.OK, "/api/v1/courses?courseId=http-other&role=Teacher");
        using var doc = JsonDocument.Parse(body); var row = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("HTTP private title", row.GetProperty("course").GetProperty("title").GetString());
        Assert.Equal(role.ToString(), row.GetProperty("role").GetString()); Assert.Equal(JsonValueKind.Null, row.GetProperty("progress").ValueKind);
        Assert.Equal(body, await HttpGet(app, token, HttpStatusCode.OK, "/api/v1/courses?courseId=http-foreign&organizationId=" + Guid.NewGuid()));
        Assert.DoesNotContain("http-foreign", body); Assert.DoesNotContain("http-other", body); Assert.DoesNotContain("count", body);
        await ChangeRestricted(seed.Grant with { Operation = AuthorityOperation.Revoke, ExpectedVersion = 1 });
        Assert.Null(await HttpIssue(seed.User, seed.Session, clock, jwt));
        Assert.Equal(body, await HttpGet(app, token, HttpStatusCode.OK));
        await using (var db = Open(runtime)) await new PostgresIdentitySessions(db, TimeProvider.System, true).Revoke(seed.User, seed.Session);
        Assert.Null(await HttpIssue(seed.User, seed.Session, clock, jwt));
        Assert.Equal(body, await HttpGet(app, token, HttpStatusCode.OK));
        time.Elapsed = TimeSpan.FromMinutes(15);
        await HttpGet(app, token, HttpStatusCode.Unauthorized);
    }
    [Theory]
    [InlineData("missing")] [InlineData("malformed")] [InlineData("signature")]
    [InlineData("issuer")] [InlineData("audience")] [InlineData("expiry")] [InlineData("unknown")] [InlineData("unproven")]
    public async Task Http_courses_rejects_untrusted_credentials(string fault)
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048); using var other = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "http", "http", clock); var seed = await HttpSeed(ScopeRole.Student, time);
        var token = await HttpIssue(seed.User, seed.Session, clock, jwt); Assert.NotNull(token);
        if (fault == "missing") token = null;
        if (fault == "malformed") token = new("bad");
        if (fault == "signature") jwt = new(other, "http", "http", clock);
        if (fault == "issuer") jwt = new(key, "other", "http", clock);
        if (fault == "audience") jwt = new(key, "http", "other", clock);
        if (fault == "expiry") time.Elapsed = TimeSpan.FromMinutes(15);
        if (fault == "unknown") time.Current = false;
        if (fault == "unproven")
        {
            var source = new LocalQualifiedClockSource(time, new UnprovenLocalClockVerifier());
            Assert.False(source.Qualify());
            clock = new(source, TimeSpan.Zero, TimeSpan.Zero);
            jwt = new(key, "http", "http", clock);
        }
        await using var app = await HttpHost(clock, jwt);
        await HttpGet(app, token, HttpStatusCode.Unauthorized);
    }
    [Theory]
    [InlineData("restriction", false)] [InlineData("context", false)] [InlineData("suspended", false)]
    [InlineData("restriction", true)] [InlineData("context", true)] [InlineData("suspended", true)]
    public async Task Http_courses_current_processing_and_ownership_before_read_and_response(string fault, bool late)
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "http", "http", clock); var seed = await HttpSeed(ScopeRole.Student, time);
        var token = await HttpIssue(seed.User, seed.Session, clock, jwt); Assert.NotNull(token);
        async Task Mutate()
        {
            if (fault == "suspended")
            {
                await using var db = Open(processing);
                await new PostgresProcessingStateAdministration(db).Change(new(existingOrg, Guid.NewGuid(), 2,
                    new SetProcessingOrganizationState("Suspended")));
            }
            else
            {
                await using var db = Open(processing);
                await new PostgresProcessingStateAdministration(db).Change(new(existingOrg, Guid.NewGuid(), 2,
                    fault == "restriction" ? new AddProcessingRestriction(Guid.NewGuid(), seed.Context, 1, seed.Actor, 1)
                    : new CloseProcessingBinding("http-own", time.At.AddMinutes(-30))));
                // CloseBinding uses DB time; advance past that boundary coherently.
                if (fault == "context") time.Elapsed = TimeSpan.FromSeconds(5);
            }
        }
        if (!late) await Mutate();
        await using var app = await HttpHost(clock, jwt, late ? Mutate : null);
        var body = await HttpGet(app, token, late ? HttpStatusCode.Forbidden : HttpStatusCode.OK);
        Assert.DoesNotContain("HTTP private title", body); Assert.DoesNotContain("description", body); Assert.DoesNotContain("progress", body);
        if (!late) Assert.Equal("{\"items\":[]}", body);
    }
    [Theory]
    [InlineData("before")] [InlineData("exact")] [InlineData("after")] [InlineData("split")]
    [InlineData("invalidated")] [InlineData("unknown-late")] [InlineData("expiry-late")]
    public async Task Http_courses_same_ingress_deadline_and_final_time_gates(string boundary)
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "http", "http", clock); var seed = await HttpSeed(ScopeRole.Student, time);
        var token = await HttpIssue(seed.User, seed.Session, clock, jwt); Assert.NotNull(token);
        if (boundary == "expiry-late") time.Elapsed = TimeSpan.FromSeconds(899);
        ApplicationDeadline? captured = null;
        await using var app = await HttpHost(clock, jwt, () =>
        {
            if (boundary == "unknown-late") time.Current = false;
            else if (boundary == "invalidated") captured!.Invalidate();
            else time.Elapsed += boundary switch { "before" => TimeSpan.FromSeconds(60) - TimeSpan.FromTicks(1),
                "after" => TimeSpan.FromSeconds(61), "split" => TimeSpan.FromSeconds(30), "expiry-late" => TimeSpan.FromSeconds(1), _ => TimeSpan.FromSeconds(60) };
            return Task.CompletedTask;
        }, ctx => { captured = ctx.Features.Get<ApplicationDeadline>(); if (boundary == "split") time.Elapsed += TimeSpan.FromSeconds(30); });
        var expected = boundary == "before" ? HttpStatusCode.OK : boundary is "unknown-late" or "expiry-late" ? HttpStatusCode.Forbidden : HttpStatusCode.ServiceUnavailable;
        await HttpGet(app, token, expected);
        Assert.NotNull(captured);
    }
    [Theory]
    [InlineData("no-grant")] [InlineData("foreign-org")] [InlineData("admin")]
    [InlineData("wrong-role")] [InlineData("missing-group")]
    public async Task Http_courses_no_eligible_server_scope_cannot_be_replaced_by_client_flags(string fault)
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "http", "http", clock); var seed = await HttpSeed(ScopeRole.Student, time);
        var identity = await IdentitySeed();
        await ChangeRestricted(new(AuthorityKind.Membership, AuthorityOperation.Grant, existingOrg, identity.Actor, 0,
            Role: fault == "admin" ? null : ScopeRole.Student, Permissions: fault == "admin" ? 15 : 0));
        if (fault is "wrong-role" or "missing-group")
            await Assert.ThrowsAsync<UseCaseException>(() => ChangeRestricted(seed.Grant with
            { ActorId = identity.Actor, Kind = fault == "wrong-role" ? AuthorityKind.Teacher : AuthorityKind.Enrollment,
                GroupId = fault == "missing-group" ? "absent" : "g", GrantId = fault == "wrong-role" ? Guid.NewGuid() : null }));
        SessionResult login;
        await using (var db = Open(runtime)) login = await new PostgresIdentitySessions(db, new HttpTimeProvider(time), true).Authenticate(identity.Name, identity.Password);
        Assert.Equal(SessionOutcome.Success, login.Outcome);
        AccessToken? token;
        if (fault == "foreign-org")
        {
            // Independent positive control: same user/session has valid own-org scope.
            Assert.NotNull(await HttpIssue(seed.User, seed.Session, clock, jwt));
            await using var db = Open(runtime);
            var foreign = await db.Set<CourseRegistration>().Where(c => c.CourseId == "http-foreign")
                .Select(c => c.OrganizationId).SingleAsync();
            token = await HttpIssue(seed.User, seed.Session, clock, jwt, foreign);
        }
        else token = await HttpIssue(identity.User, login.Identity!.SessionId, clock, jwt);
        Assert.Null(token);
        await using var app = await HttpHost(clock, jwt);
        await HttpGet(app, token, HttpStatusCode.Unauthorized, "/api/v1/courses?courseId=http-own&role=Teacher");
    }
    [Theory]
    [InlineData("key")] [InlineData("database")] [InlineData("aborted")]
    public async Task Http_courses_response_denies_lost_trust_unavailable_processing_and_cancellation(string fault)
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048);
        var keys = new ComponentSigningKeys(); var kid = keys.Rotate(key);
        var jwt = new ComponentJwt(keys, "http", "http", clock); var seed = await HttpSeed(ScopeRole.Student, time);
        var token = await HttpIssue(seed.User, seed.Session, clock, jwt); Assert.NotNull(token);
        using var cancellation = new CancellationTokenSource();
        await using var app = await HttpHost(clock, jwt, async () =>
        {
            if (fault == "key") keys.RemoveTrust(kid);
            if (fault == "database") await Sql(owner, "REVOKE SELECT ON \"ProcessingRestrictions\" FROM c2h_runtime");
            if (fault == "aborted") cancellation.Cancel();
        }, aborted: cancellation.Token);
        await HttpGet(app, token, fault == "key" ? HttpStatusCode.Unauthorized : HttpStatusCode.ServiceUnavailable);
    }

}
