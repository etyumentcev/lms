using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Lms.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Lms.Api.Tests;

public sealed class ProcessingContextAdministrationHttpTests(ApiPostgresFixture fixture)
    : IClassFixture<ApiPostgresFixture>
{
    private static string PlatformToken()
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken("lms-local-development",
            OrganizationBootstrapHttp.PlatformAudience,
            [new Claim("sub", "synthetic-processing-operator"),
                new Claim("scope", OrganizationBootstrapHttp.BootstrapCapability)],
            now, now.AddMinutes(10), new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.LocalSigningKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task SetCsrf(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/csrf");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            var cookie = Assert.Single(cookies).Split(';')[0];
            client.DefaultRequestHeaders.Remove("Cookie");
            client.DefaultRequestHeaders.Add("Cookie", cookie);
        }
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", body.GetProperty("token").GetString());
    }

    private static async Task<(HttpClient Client, Guid OrganizationId)> Administrator(ApiFactory app)
    {
        var client = app.CreateClient(new() { BaseAddress = new("https://lms.test") });
        client.DefaultRequestHeaders.Add("Origin", "https://lms.test");
        client.DefaultRequestHeaders.Authorization = new("Bearer", PlatformToken());
        using var created = await client.PostAsJsonAsync("/api/v1/platform/organizations", new
        {
            commandId = Guid.NewGuid(), organizationName = "Контекст-тест",
            adminUserName = "processing-admin-" + Guid.NewGuid().ToString("N")
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var bootstrap = await created.Content.ReadFromJsonAsync<JsonElement>();
        var organizationId = bootstrap.GetProperty("organizationId").GetGuid();
        var userName = bootstrap.GetProperty("organizationName").GetString() == null
            ? throw new InvalidOperationException() : string.Empty;
        // Read the requested login from the immutable bootstrap response's related receipt.
        await using var db = app.Open();
        var adminUserId = bootstrap.GetProperty("adminUserId").GetGuid();
        userName = (await db.Users.AsNoTracking().SingleAsync(x => x.Id == adminUserId)).UserName!;
        client.DefaultRequestHeaders.Authorization = null;
        await SetCsrf(client);
        using var activated = await client.PostAsJsonAsync("/api/v1/auth/activate-first-admin", new
        {
            organizationId, userName,
            activationCode = bootstrap.GetProperty("activationCode").GetString(),
            password = "Synthetic-Processing-Admin-2039!"
        });
        Assert.Equal(HttpStatusCode.NoContent, activated.StatusCode);
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            organizationId, userName, password = "Synthetic-Processing-Admin-2039!"
        });
        login.EnsureSuccessStatusCode();
        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            session.GetProperty("accessToken").GetString());
        await SetCsrf(client);
        return (client, organizationId);
    }

    [Fact]
    public async Task Admin_creates_versions_and_same_org_binding_with_replay_CAS_and_database_consistency()
    {
        await using var app = await fixture.CreateApp(organizationBootstrap: true);
        var (client, organizationId) = await Administrator(app);
        using (client)
        {
            using var createdCourse = await client.PostAsJsonAsync("/api/v1/admin/courses", new
            {
                title = "Олимпиадная математика", description = "Synthetic-only",
                idempotencyKey = Guid.NewGuid().ToString("N")
            });
            Assert.Equal(HttpStatusCode.Created, createdCourse.StatusCode);
            var courseId = (await createdCourse.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("courseId").GetString()!;

            var initial = await client.GetFromJsonAsync<JsonElement>("/api/v1/admin/processing-context");
            Assert.Equal(0, initial.GetProperty("revision").GetInt64());
            Assert.Equal(JsonValueKind.Null, initial.GetProperty("currentContext").ValueKind);
            var operation = Guid.NewGuid(); var effective = DateTimeOffset.UtcNow;
            var createBody = new
            {
                operationId = operation, expectedRevision = 0,
                expectedContextId = (Guid?)null, expectedContextVersion = (long?)null,
                effectiveFrom = effective
            };
            using var createdContext = await client.PostAsJsonAsync(
                "/api/v1/admin/processing-context/versions", createBody);
            Assert.Equal(HttpStatusCode.Created, createdContext.StatusCode);
            var contextResult = await createdContext.Content.ReadFromJsonAsync<JsonElement>();
            var context = contextResult.GetProperty("state").GetProperty("currentContext");
            Assert.Equal(operation, context.GetProperty("contextId").GetGuid());
            Assert.Equal(1, context.GetProperty("version").GetInt64());
            Assert.True(context.GetProperty("syntheticOnly").GetBoolean());
            Assert.Equal("SyntheticOnly", context.GetProperty("approvalState").GetString());

            using var replay = await client.PostAsJsonAsync(
                "/api/v1/admin/processing-context/versions", createBody);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.False((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("changed").GetBoolean());

            var bindingOperation = Guid.NewGuid(); var bindingAt = DateTimeOffset.UtcNow;
            var bindingBody = new
            {
                operationId = bindingOperation, expectedRevision = 1, contextId = operation,
                contextVersion = 1L, expectedBindingFrom = (DateTimeOffset?)null,
                effectiveFrom = (DateTimeOffset?)bindingAt
            };
            using var bound = await client.PostAsJsonAsync(
                $"/api/v1/admin/courses/{courseId}/processing-context", bindingBody);
            Assert.Equal(HttpStatusCode.Created, bound.StatusCode);
            var boundState = (await bound.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state");
            Assert.Equal(2, boundState.GetProperty("revision").GetInt64());
            Assert.Equal(1, Assert.Single(boundState.GetProperty("courses").EnumerateArray())
                .GetProperty("binding").GetProperty("contextVersion").GetInt64());

            using var stale = await client.PostAsJsonAsync(
                $"/api/v1/admin/courses/{courseId}/processing-context", bindingBody with
                { operationId = Guid.NewGuid() });
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal("ConcurrencyConflict", (await stale.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code").GetString());

            var version2At = DateTimeOffset.UtcNow.AddMilliseconds(10);
            using var version2 = await client.PostAsJsonAsync("/api/v1/admin/processing-context/versions", new
            {
                operationId = Guid.NewGuid(), expectedRevision = 2, expectedContextId = operation,
                expectedContextVersion = 1L, effectiveFrom = version2At
            });
            Assert.Equal(HttpStatusCode.Created, version2.StatusCode);
            var v2State = (await version2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state");
            Assert.Equal(2, v2State.GetProperty("currentContext").GetProperty("version").GetInt64());
            Assert.Equal(2, v2State.GetProperty("currentContext").GetProperty("versionCount").GetInt32());

            var priorBinding = Assert.Single(v2State.GetProperty("courses").EnumerateArray())
                .GetProperty("binding").GetProperty("effectiveFrom").GetDateTimeOffset();
            using var rebound = await client.PostAsJsonAsync(
                $"/api/v1/admin/courses/{courseId}/processing-context", new
                {
                    operationId = Guid.NewGuid(), expectedRevision = 3, contextId = operation,
                    contextVersion = 2L, expectedBindingFrom = (DateTimeOffset?)priorBinding,
                    effectiveFrom = (DateTimeOffset?)null
                });
            Assert.Equal(HttpStatusCode.Created, rebound.StatusCode);

            await using var verify = app.Open();
            Assert.Equal(2, await verify.Set<ProcessingContextVersion>().CountAsync(x =>
                x.OrganizationId == organizationId && x.ContextId == operation));
            var versions = await verify.Set<ProcessingContextVersion>().Where(x =>
                x.OrganizationId == organizationId && x.ContextId == operation)
                .OrderBy(x => x.Version).Select(x => x.Version).ToArrayAsync();
            Assert.Equal(new long[] { 1, 2 }, versions);
            Assert.Equal(2, await verify.Set<CourseContextBinding>().CountAsync(x =>
                x.OrganizationId == organizationId && x.CourseId == courseId));
            Assert.Single(await verify.Set<CourseContextBinding>().Where(x =>
                x.OrganizationId == organizationId && x.CourseId == courseId && x.EffectiveTo == null)
                .ToListAsync());
            Assert.Equal(4, await verify.Database.SqlQueryRaw<long>(
                "SELECT \"Revision\" AS \"Value\" FROM \"ProcessingGuards\" WHERE \"OrganizationId\"={0}",
                organizationId).SingleAsync());
            Assert.Equal(4, await verify.Database.SqlQueryRaw<long>(
                "SELECT count(*) AS \"Value\" FROM \"ProcessingWriteJournal\" WHERE \"OrganizationId\"={0}",
                organizationId).SingleAsync());
        }
    }

    [Fact]
    public async Task Student_teacher_cross_org_course_and_forged_context_are_denied()
    {
        await using var app = await fixture.CreateApp(organizationBootstrap: true);
        var (admin, organizationId) = await Administrator(app);
        using (admin)
        {
            var contextId = Guid.NewGuid();
            using var context = await admin.PostAsJsonAsync("/api/v1/admin/processing-context/versions", new
            {
                operationId = contextId, expectedRevision = 0, expectedContextId = (Guid?)null,
                expectedContextVersion = (long?)null, effectiveFrom = DateTimeOffset.UtcNow
            });
            Assert.Equal(HttpStatusCode.Created, context.StatusCode);

            var (foreignAdmin, foreignOrganizationId) = await Administrator(app);
            string otherCourse;
            using (foreignAdmin)
            {
                var foreignInitial = await foreignAdmin.GetFromJsonAsync<JsonElement>(
                    "/api/v1/admin/processing-context");
                Assert.Equal(JsonValueKind.Null,
                    foreignInitial.GetProperty("currentContext").ValueKind);
                var foreignContextId = Guid.NewGuid();
                using var foreignContext = await foreignAdmin.PostAsJsonAsync(
                    "/api/v1/admin/processing-context/versions", new
                    {
                        operationId = foreignContextId, expectedRevision = 0,
                        expectedContextId = (Guid?)null, expectedContextVersion = (long?)null,
                        effectiveFrom = DateTimeOffset.UtcNow
                    });
                Assert.Equal(HttpStatusCode.Created, foreignContext.StatusCode);
                using var foreignCourse = await foreignAdmin.PostAsJsonAsync("/api/v1/admin/courses", new
                {
                    title = "Чужой курс", description = "Tenant isolation",
                    idempotencyKey = Guid.NewGuid().ToString("N")
                });
                Assert.Equal(HttpStatusCode.Created, foreignCourse.StatusCode);
                otherCourse = (await foreignCourse.Content.ReadFromJsonAsync<JsonElement>())
                    .GetProperty("courseId").GetString()!;
            }

            var ownState = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/processing-context");
            Assert.Equal(contextId, ownState.GetProperty("currentContext")
                .GetProperty("contextId").GetGuid());
            using var foreignCourseAttempt = await admin.PostAsJsonAsync(
                $"/api/v1/admin/courses/{otherCourse}/processing-context", new
                {
                    operationId = Guid.NewGuid(), expectedRevision = 1, contextId, contextVersion = 1L,
                    expectedBindingFrom = (DateTimeOffset?)null,
                    effectiveFrom = (DateTimeOffset?)DateTimeOffset.UtcNow
                });
            Assert.Equal(HttpStatusCode.NotFound, foreignCourseAttempt.StatusCode);

            using var forgedContext = await admin.PostAsJsonAsync(
                "/api/v1/admin/courses/missing-course/processing-context", new
                {
                    operationId = Guid.NewGuid(), expectedRevision = 1, contextId = Guid.NewGuid(),
                    contextVersion = 1L, expectedBindingFrom = (DateTimeOffset?)null,
                    effectiveFrom = (DateTimeOffset?)DateTimeOffset.UtcNow
                });
            Assert.Equal(HttpStatusCode.Conflict, forgedContext.StatusCode);
            Assert.Equal("ProcessingContextMissing",
                (await forgedContext.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

            await using var verify = app.Open();
            Assert.Empty(await verify.Set<CourseContextBinding>().Where(x =>
                x.OrganizationId == organizationId || x.CourseId == otherCourse).ToListAsync());
            Assert.Single(await verify.Set<ProcessingContextVersion>().Where(x =>
                x.OrganizationId == foreignOrganizationId).ToListAsync());
        }

        using var teacher = await app.SecureClient("Teacher");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.GetAsync("/api/v1/admin/processing-context")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.PostAsJsonAsync("/api/v1/admin/processing-context/versions", new
            {
                operationId = Guid.NewGuid(), expectedRevision = 0,
                expectedContextId = (Guid?)null, expectedContextVersion = (long?)null,
                effectiveFrom = DateTimeOffset.UtcNow
            })).StatusCode);
        using var student = await app.SecureClient("Student");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await student.PostAsJsonAsync("/api/v1/admin/processing-context/versions", new
            {
                operationId = Guid.NewGuid(), expectedRevision = 0,
                expectedContextId = (Guid?)null, expectedContextVersion = (long?)null,
                effectiveFrom = DateTimeOffset.UtcNow
            })).StatusCode);
    }
}
