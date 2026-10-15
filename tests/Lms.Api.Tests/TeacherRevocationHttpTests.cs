using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Lms.Application;
using Lms.Domain;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lms.Api.Tests;

public sealed class TeacherRevocationHttpTests(ApiPostgresFixture fixture) : IClassFixture<ApiPostgresFixture>
{
    private static readonly DateTimeOffset At = new(2026, 10, 12, 6, 0, 0, TimeSpan.Zero);
    private sealed class Clock : TimeProvider, IQualifiedClockSource
    {
        public override DateTimeOffset GetUtcNow() => At;
        public QualifiedClockReading Read() => new("http-teacher-test", true, new(At, At), TimeSpan.Zero, TimeSpan.Zero);
    }
    private static async Task<WebApplication> Host(string connection, RSA key, bool ingress = true)
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        var source = new Clock(); var clock = new QualifiedAuthorityClock(source, TimeSpan.Zero, TimeSpan.Zero);
        builder.Services.AddSingleton(clock); builder.Services.AddSingleton<IAuthorityClock>(clock); builder.Services.AddSingleton<TimeProvider>(source);
        builder.Services.AddSingleton(new ComponentJwt(key, "teacher-http", "teacher-http", clock));
        builder.Services.AddDbContextFactory<LmsDbContext>(o => o.UseNpgsql(connection));
        builder.Services.AddSingleton(new AttachmentOptions());
        builder.Services.AddSingleton<IAttachmentStorage>(new LocalFileAttachmentStorage(new() { Root = Path.Combine(Path.GetTempPath(), "teacher-http-unused") }));
        builder.Services.AddTransient<PostgresTeacherCommands>();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddTeacherRevocationBrowserProtection();
        builder.Services.AddAuthentication(CourseBearerHandler.SchemeName).AddScheme<AuthenticationSchemeOptions, CourseBearerHandler>(CourseBearerHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        var app = builder.Build(); if (ingress) app.UseMiddleware<CourseRequestIngress>();
        app.UseAuthentication(); app.UseAuthorization(); app.MapTeacherRevocation("https://teacher.test"); await app.StartAsync(); return app;
    }
    private static async Task<string> Csrf(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/auth/csrf"); response.EnsureSuccessStatusCode();
        var cookie = response.Headers.TryGetValues("Set-Cookie", out var headers) ? headers.Single() : null;
        if (cookie != null)
        {
            Assert.Contains("secure", cookie); Assert.Contains("httponly", cookie); Assert.DoesNotContain("domain=", cookie);
            client.DefaultRequestHeaders.Remove("Cookie"); client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        }
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", json.GetProperty("token").GetString());
        return cookie?.Split(';')[0] ?? client.DefaultRequestHeaders.GetValues("Cookie").Single();
    }
    [Theory]
    [InlineData("origin")][InlineData("csrf")][InlineData("deadline")]
    public async Task Teacher_http_denies_missing_trusted_boundary_before_database(string missing)
    {
        using var key = RSA.Create(2048); await using var app = await Host("Host=127.0.0.1;Port=1;Database=unreachable", key, missing != "deadline");
        using var client = app.GetTestClient(); client.BaseAddress = new("https://teacher.test");
        if (missing != "deadline") await Csrf(client);
        if (missing != "origin") client.DefaultRequestHeaders.Add("Origin", "https://teacher.test");
        if (missing == "csrf") client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = "synthetic", password = "Synthetic-password-123", organizationId = Guid.NewGuid() });
        Assert.Equal(missing == "deadline" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }
    [Fact]
    public async Task Teacher_http_login_review_replay_refresh_revoke_and_cookie_security()
    {
        await using var database = await fixture.CreateApp(); await using var seed = database.Open(); var connection = seed.Database.GetConnectionString()!;
        var org = Guid.NewGuid(); var teacher = Guid.NewGuid(); var student = Guid.NewGuid(); var context = Guid.NewGuid(); var userId = Guid.NewGuid();
        var source = new Clock();
        seed.Add(new Organization { OrganizationId = org, State = "Enabled", SyntheticOnly = true });
        seed.Add(new OrganizationProfile { OrganizationId = org, Name = "Синтетический лицей" });
        seed.AddRange(new ActorRecord { ActorId = teacher }, new ActorRecord { ActorId = student });
        seed.Add(new OrganizationParticipantProfile { OrganizationId = org, ActorId = teacher,
            ExternalRosterKey = "teacher-http", DisplayName = "Тюменцев Евгений", CreatedAt = At });
        seed.Add(new CourseRegistration { CourseId = "teacher-http", OrganizationId = org, MappingState = "Verified" });
        seed.Add(new CourseGroup { CourseId = "teacher-http", GroupId = "g" }); await seed.SaveChangesAsync(); seed.ChangeTracker.Clear();
        var administration = new PostgresScopedAuthorityAdministration(seed);
        await administration.Change(new(AuthorityKind.Membership, AuthorityOperation.Grant, org, teacher, 0, Role: ScopeRole.Teacher));
        await administration.Change(new(AuthorityKind.Membership, AuthorityOperation.Grant, org, student, 0, Role: ScopeRole.Student));
        var grant = Guid.NewGuid();
        await administration.Change(new(AuthorityKind.Teacher, AuthorityOperation.Grant, org, teacher, 0, 1, "teacher-http", "g", grant));
        await administration.Change(new(AuthorityKind.Enrollment, AuthorityOperation.Grant, org, student, 0, 1, "teacher-http", "g"));
        await new PostgresProcessingStateAdministration(seed).Change(new(org, Guid.NewGuid(), 0, new AddProcessingContext(context, 1, At.AddDays(-1))));
        await new PostgresProcessingStateAdministration(seed).Change(new(org, Guid.NewGuid(), 1, new OpenProcessingBinding("teacher-http", context, 1, At.AddHours(-1))));
        await new PostgresIdentityMapping(seed, source).CreateInactive(userId, teacher, org, "teacher-http");
        (await seed.Set<ActorIdentity>().SingleAsync(x => x.UserId == userId)).IsActive = true;
        var user = await seed.Users.SingleAsync(x => x.Id == userId);
        user.PasswordHash = new PasswordHasher<IdentityUser<Guid>>().HashPassword(user, "Synthetic-password-123");
        await seed.SaveChangesAsync(); seed.ChangeTracker.Clear();
        var assignment = new ProblemAssignment(Guid.NewGuid(), student, "teacher-http", new(Guid.NewGuid(), "lesson", "problem"));
        seed.Add(assignment); await seed.SaveChangesAsync(); seed.ChangeTracker.Clear();
        var work = new ProblemWork(Guid.NewGuid(), assignment); work.SubmitSolution(assignment, student, "Answer", [], At);
        await new PostgresWorkStore(seed).Save(work, null, default);
        using var key = RSA.Create(2048); await using var app = await Host(connection, key);
        using var client = app.GetTestClient(); client.BaseAddress = new("https://teacher.test"); client.DefaultRequestHeaders.Add("Origin", "https://teacher.test");
        var csrf = await Csrf(client);
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = "teacher-http", password = "Synthetic-password-123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var refreshHeader = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("__Host-LmsRefresh=", refreshHeader); Assert.Contains("secure", refreshHeader); Assert.Contains("httponly", refreshHeader);
        Assert.Contains("samesite=lax", refreshHeader); Assert.DoesNotContain("domain=", refreshHeader);
        var loginBody = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(org, loginBody.GetProperty("organizationId").GetGuid());
        Assert.Equal("Синтетический лицей", loginBody.GetProperty("organizationName").GetString());
        Assert.Equal("Тюменцев Евгений", loginBody.GetProperty("displayName").GetString());
        Assert.Single(loginBody.GetProperty("organizations").EnumerateArray());
        var token = loginBody.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await Csrf(client); // bind the request token to the authenticated identity
        var body = new { commandId = Guid.NewGuid(), submissionId = work.Submissions[0].Id, expectedVersion = work.Version, feedback = "Accepted", annotations = Array.Empty<object>() };
        var path = $"/api/v1/problem-works/{work.Id}/reviews/accept";
        var reviewed = await client.PostAsJsonAsync(path, body);
        Assert.Equal(HttpStatusCode.OK, reviewed.StatusCode);
        Assert.Equal("Accepted", (await reviewed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path, body)).StatusCode);
        await administration.Change(new(AuthorityKind.Teacher, AuthorityOperation.Revoke, org, teacher, 1, 1, "teacher-http", "g", grant));
        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Remove("Cookie"); client.DefaultRequestHeaders.Add("Cookie", csrf);
        await Csrf(client); // anonymous cookie-only refresh request
        client.DefaultRequestHeaders.Remove("Cookie"); client.DefaultRequestHeaders.Add("Cookie", csrf + "; " + refreshHeader.Split(';')[0]);
        var refreshed = await client.PostAsJsonAsync("/api/v1/auth/switch-organization", new { organizationId = org });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await refreshed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").ValueKind);
        var successorHeader = Assert.Single(refreshed.Headers.GetValues("Set-Cookie"));
        var replay = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { organizationId = org });
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode); Assert.False(replay.Headers.Contains("Set-Cookie"));
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", csrf + "; " + successorHeader.Split(';')[0]);
        using var logout = await client.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith("__Host-LmsRefresh=", StringComparison.Ordinal) && value.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        await using var check = database.Open();
        Assert.NotNull((await check.Set<IdentitySession>().SingleAsync()).RevokedAt);
        Assert.Equal("refresh_already_rotated", (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Browser_login_hides_organizations_until_credentials_then_selects_by_server_membership()
    {
        await using var database = await fixture.CreateApp();
        await using var connectionContext = database.Open();
        var connection = connectionContext.Database.GetConnectionString()!;
        var orgA = Guid.NewGuid(); var orgB = Guid.NewGuid();
        var multiActor = Guid.NewGuid(); var emptyActor = Guid.NewGuid();
        const string password = "Synthetic-password-123";
        await using (var setup = database.Open())
        {
            setup.AddRange(
                new Organization { OrganizationId = orgA, State = "Enabled", SyntheticOnly = true },
                new Organization { OrganizationId = orgB, State = "Enabled", SyntheticOnly = true },
                new OrganizationProfile { OrganizationId = orgA, Name = "Лицей Эйлера" },
                new OrganizationProfile { OrganizationId = orgB, Name = "Школа Лобачевского" },
                new ActorRecord { ActorId = multiActor }, new ActorRecord { ActorId = emptyActor },
                new OrganizationParticipantProfile { OrganizationId = orgA, ActorId = multiActor,
                    ExternalRosterKey = "multi-a", DisplayName = "Иванов Иван", CreatedAt = At },
                new OrganizationParticipantProfile { OrganizationId = orgB, ActorId = multiActor,
                    ExternalRosterKey = "multi-b", DisplayName = "Петров Пётр", CreatedAt = At });
            await setup.SaveChangesAsync();
        }
        await using (var authority = database.Open())
        {
            var administration = new PostgresScopedAuthorityAdministration(authority);
            await administration.Change(new(AuthorityKind.Membership, AuthorityOperation.Grant, orgA, multiActor, 0, Permissions: (int)OrganizationPermission.ManageOrganization));
            await administration.Change(new(AuthorityKind.Membership, AuthorityOperation.Grant, orgB, multiActor, 0, Permissions: (int)OrganizationPermission.ManageOrganization));
        }
        async Task CreateIdentity(Guid actor, string name)
        {
            var userId = Guid.NewGuid();
            await using (var mapping = database.Open())
                await new PostgresIdentityMapping(mapping, new Clock()).CreateInactive(userId, actor, orgA, name);
            await using var activation = database.Open();
            (await activation.Set<ActorIdentity>().SingleAsync(x => x.UserId == userId)).IsActive = true;
            var user = await activation.Users.SingleAsync(x => x.Id == userId);
            user.PasswordHash = new PasswordHasher<IdentityUser<Guid>>().HashPassword(user, password);
            await activation.SaveChangesAsync();
        }
        await CreateIdentity(multiActor, "multi-browser");
        await CreateIdentity(emptyActor, "empty-browser");

        using var key = RSA.Create(2048); await using var app = await Host(connection, key);
        using var client = app.GetTestClient(); client.BaseAddress = new("https://teacher.test");
        client.DefaultRequestHeaders.Add("Origin", "https://teacher.test"); var csrfCookie = await Csrf(client);
        var invalid = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = "multi-browser", password = "Wrong-password-123" });
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode); Assert.False(invalid.Headers.Contains("Set-Cookie"));
        Assert.DoesNotContain("Лицей Эйлера", await invalid.Content.ReadAsStringAsync());
        var unknown = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = "unknown-browser", password });
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode); Assert.False(unknown.Headers.Contains("Set-Cookie"));
        var unknownBody = await unknown.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Лицей Эйлера", unknownBody); Assert.DoesNotContain("Школа Лобачевского", unknownBody);

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = "multi-browser", password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginJson = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, loginJson.GetProperty("accessToken").ValueKind);
        Assert.True(loginJson.GetProperty("selectionRequired").GetBoolean());
        Assert.Equal(JsonValueKind.Null, loginJson.GetProperty("displayName").ValueKind);
        Assert.Equal(new[] { "Лицей Эйлера", "Школа Лобачевского" },
            loginJson.GetProperty("organizations").EnumerateArray().Select(x => x.GetProperty("name").GetString()));
        var refresh = Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';')[0];
        client.DefaultRequestHeaders.Remove("Cookie"); client.DefaultRequestHeaders.Add("Cookie", csrfCookie + "; " + refresh);
        var forgedOrganization = Guid.NewGuid();
        var forged = await client.PostAsJsonAsync("/api/v1/auth/switch-organization", new { organizationId = forgedOrganization });
        Assert.Equal(HttpStatusCode.OK, forged.StatusCode);
        var forgedJson = await forged.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, forgedJson.GetProperty("accessToken").ValueKind);
        Assert.True(forgedJson.GetProperty("selectionRequired").GetBoolean());
        Assert.DoesNotContain(forgedJson.GetProperty("organizations").EnumerateArray(),
            x => x.GetProperty("organizationId").GetGuid() == forgedOrganization);
        var successor = Assert.Single(forged.Headers.GetValues("Set-Cookie")).Split(';')[0];
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", csrfCookie + "; " + successor);
        var selected = await client.PostAsJsonAsync("/api/v1/auth/switch-organization", new { organizationId = orgB });
        Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
        var selectedJson = await selected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(orgB, selectedJson.GetProperty("organizationId").GetGuid());
        Assert.Equal("Школа Лобачевского", selectedJson.GetProperty("organizationName").GetString());
        Assert.Equal("Петров Пётр", selectedJson.GetProperty("displayName").GetString());
        Assert.DoesNotContain("Иванов Иван", await selected.Content.ReadAsStringAsync());
        Assert.NotNull(selectedJson.GetProperty("accessToken").GetString());
        var selectedRefresh = Assert.Single(selected.Headers.GetValues("Set-Cookie")).Split(';')[0];
        await using (var revoke = database.Open())
        {
            var administration = new PostgresScopedAuthorityAdministration(revoke);
            await administration.Change(new(AuthorityKind.Membership, AuthorityOperation.Revoke, orgA, multiActor, 1));
            await administration.Change(new(AuthorityKind.Membership, AuthorityOperation.Revoke, orgB, multiActor, 1));
        }
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", csrfCookie + "; " + selectedRefresh);
        var revoked = await client.PostAsJsonAsync("/api/v1/auth/switch-organization", new { organizationId = orgB });
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        Assert.False(revoked.Headers.Contains("Set-Cookie"));
        Assert.Equal("no_available_organizations",
            (await revoked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        using var empty = app.GetTestClient(); empty.BaseAddress = new("https://teacher.test");
        empty.DefaultRequestHeaders.Add("Origin", "https://teacher.test"); await Csrf(empty);
        var unavailable = await empty.PostAsJsonAsync("/api/v1/auth/login", new { userName = "empty-browser", password });
        Assert.Equal(HttpStatusCode.Forbidden, unavailable.StatusCode); Assert.False(unavailable.Headers.Contains("Set-Cookie"));
        Assert.Equal("no_available_organizations",
            (await unavailable.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
}
