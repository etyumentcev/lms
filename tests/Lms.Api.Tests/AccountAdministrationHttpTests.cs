using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Lms.Api.Tests;

public sealed class AccountAdministrationHttpTests(ApiPostgresFixture fixture) : IClassFixture<ApiPostgresFixture>
{
    private static string PlatformToken(string scope)
    {
        var now = DateTime.UtcNow;
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("lms-local-development",
            OrganizationBootstrapHttp.PlatformAudience,
            [new Claim("sub", "synthetic-independent-operator"), new Claim("scope", scope)],
            now, now.AddMinutes(10), new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.LocalSigningKey)), SecurityAlgorithms.HmacSha256)));
    }

    private static async Task Csrf(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/auth/csrf"); response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
    }

    [Fact]
    public async Task Admin_provisions_lists_activates_and_logs_student_in_over_http()
    {
        await using var app = await fixture.CreateApp(organizationBootstrap: true);
        using var bootstrap = app.CreateClient(); bootstrap.BaseAddress = new("https://lms.test");
        bootstrap.DefaultRequestHeaders.Add("Origin", "https://lms.test");
        bootstrap.DefaultRequestHeaders.Authorization = new("Bearer", PlatformToken(OrganizationBootstrapHttp.BootstrapCapability));
        var adminName = "m1-admin-" + Guid.NewGuid().ToString("N")[..8];
        var created = await bootstrap.PostAsJsonAsync("/api/v1/platform/organizations", new
        { commandId = Guid.NewGuid(), organizationName = "Синтетическая академия", adminUserName = adminName });
        created.EnsureSuccessStatusCode(); var createdJson = await created.Content.ReadFromJsonAsync<JsonElement>();
        var organizationId = createdJson.GetProperty("organizationId").GetGuid();
        bootstrap.DefaultRequestHeaders.Authorization = null; await Csrf(bootstrap);
        Assert.Equal(HttpStatusCode.NoContent, (await bootstrap.PostAsJsonAsync("/api/v1/auth/activate-first-admin", new
        { organizationId, userName = adminName, activationCode = createdJson.GetProperty("activationCode").GetString(), password = "Synthetic-Admin-Password-2039!" })).StatusCode);

        using var admin = app.CreateClient(); admin.BaseAddress = new("https://lms.test"); admin.DefaultRequestHeaders.Add("Origin", "https://lms.test"); await Csrf(admin);
        var login = await admin.PostAsJsonAsync("/api/v1/auth/login", new { userName = adminName,
            password = "Synthetic-Admin-Password-2039!", organizationId }); login.EnsureSuccessStatusCode();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        await Csrf(admin);
        var studentName = "m1-student-" + Guid.NewGuid().ToString("N")[..8];
        var provision = await admin.PostAsJsonAsync("/api/v1/admin/accounts", new { externalRosterKey = "student-http-001",
            displayName = "Синтетический Ученик", userName = studentName, role = "Student", idempotencyKey = Guid.NewGuid().ToString() });
        Assert.Equal(HttpStatusCode.Created, provision.StatusCode); Assert.Equal("no-store", provision.Headers.CacheControl!.ToString());
        var participant = await provision.Content.ReadFromJsonAsync<JsonElement>();
        var actorId = participant.GetProperty("actorId").GetGuid(); var code = participant.GetProperty("activationCode").GetString();
        var listed = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/accounts?role=Student&state=PendingActivation&search=student-http");
        Assert.Equal(1, listed.GetProperty("total").GetInt32());
        Assert.Equal(actorId, listed.GetProperty("items")[0].GetProperty("actorId").GetGuid());

        using var participantClient = app.CreateClient(); participantClient.BaseAddress = new("https://lms.test");
        participantClient.DefaultRequestHeaders.Add("Origin", "https://lms.test"); await Csrf(participantClient);
        var invalid = await participantClient.PostAsJsonAsync("/api/v1/auth/activate", new { organizationId,
            userName = studentName, code = "0000-0000-0000-0000", password = "Synthetic-Student-Password-2039!" });
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        Assert.Equal("ActivationInvalid", (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await participantClient.PostAsJsonAsync("/api/v1/auth/activate", new
        { organizationId, userName = studentName, code, password = "Synthetic-Student-Password-2039!" })).StatusCode);
        var used = await participantClient.PostAsJsonAsync("/api/v1/auth/activate", new
        { organizationId, userName = studentName, code, password = "Synthetic-Student-Password-2039!" });
        Assert.Equal(HttpStatusCode.Conflict, used.StatusCode);
        Assert.Equal("ActivationUsed", (await used.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var studentLogin = await participantClient.PostAsJsonAsync("/api/v1/auth/login", new
        { userName = studentName, password = "Synthetic-Student-Password-2039!", organizationId });
        studentLogin.EnsureSuccessStatusCode();
        Assert.False((await studentLogin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hasEducationalScope").GetBoolean());

    }
}
