using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Lms.Api.Tests;

public sealed class OrganizationBootstrapHttpTests(ApiPostgresFixture fixture) : IClassFixture<ApiPostgresFixture>
{
    private static string PlatformToken(bool capability = true)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim> { new("sub", "synthetic-platform-operator") };
        if (capability) claims.Add(new("scope", OrganizationBootstrapHttp.BootstrapCapability));
        var token = new JwtSecurityToken("lms-local-development", OrganizationBootstrapHttp.PlatformAudience,
            claims, now, now.AddMinutes(10), new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.LocalSigningKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task SetCsrf(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/auth/csrf");
        response.EnsureSuccessStatusCode();
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';')[0];
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", body.GetProperty("token").GetString());
    }

    [Fact]
    public async Task Platform_bootstrap_activation_admin_login_and_authority_boundaries_work_over_http()
    {
        await using var app = await fixture.CreateApp(organizationBootstrap: true);
        using var client = app.CreateClient();
        client.BaseAddress = new("https://lms.test");
        client.DefaultRequestHeaders.Add("Origin", "https://lms.test");

        var commandId = Guid.NewGuid();
        var request = new { commandId, organizationName = "Синтетический лицей", adminUserName = "m1-first-admin" };
        client.DefaultRequestHeaders.Authorization = new("Bearer", PlatformToken());
        var created = await client.PostAsJsonAsync("/api/v1/platform/organizations", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("no-store", created.Headers.CacheControl!.ToString());
        var bootstrap = await created.Content.ReadFromJsonAsync<JsonElement>();
        var organizationId = bootstrap.GetProperty("organizationId").GetGuid();
        var activationCode = bootstrap.GetProperty("activationCode").GetString();
        Assert.NotNull(activationCode);

        var replay = await client.PostAsJsonAsync("/api/v1/platform/organizations", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(JsonValueKind.Null,
            (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("activationCode").ValueKind);

        client.DefaultRequestHeaders.Authorization = new("Bearer", PlatformToken(capability: false));
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/v1/platform/organizations", request with { commandId = Guid.NewGuid() })).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;

        await SetCsrf(client);
        var activated = await client.PostAsJsonAsync("/api/v1/auth/activate-first-admin", new
        {
            organizationId,
            userName = "m1-first-admin",
            activationCode,
            password = "Synthetic-First-Admin-2039!"
        });
        Assert.Equal(HttpStatusCode.NoContent, activated.StatusCode);

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            userName = "m1-first-admin",
            password = "Synthetic-First-Admin-2039!",
            organizationId
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(session.GetProperty("hasEducationalScope").GetBoolean());
        Assert.True(session.GetProperty("hasOrganizationAdminScope").GetBoolean());
        var adminToken = session.GetProperty("accessToken").GetString();
        Assert.NotNull(adminToken);
        client.DefaultRequestHeaders.Authorization = new("Bearer", adminToken);

        var overview = await client.GetAsync("/api/v1/admin/organization");
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        Assert.Equal(organizationId,
            (await overview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("organizationId").GetGuid());
        var audit = await client.GetFromJsonAsync<JsonElement>("/api/v1/admin/audit");
        Assert.Contains(audit.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("action").GetString() == "first_admin_activated");

        // An organization-admin component token is not a platform-operator token.
        var selfEscalation = await client.PostAsJsonAsync("/api/v1/platform/organizations",
            request with { commandId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Unauthorized, selfEscalation.StatusCode);

        // A platform token is not accepted by organization component endpoints.
        client.DefaultRequestHeaders.Authorization = new("Bearer", PlatformToken());
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/v1/admin/organization")).StatusCode);

        using var missingOrigin = app.CreateClient();
        missingOrigin.BaseAddress = new("https://lms.test");
        missingOrigin.DefaultRequestHeaders.Authorization = new("Bearer", PlatformToken());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await missingOrigin.PostAsJsonAsync("/api/v1/platform/organizations",
                request with { commandId = Guid.NewGuid() })).StatusCode);
    }
}
