using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Lms.Api.Tests;

public sealed class BearerAuthenticationTests(ApiPostgresFixture postgres) : IClassFixture<ApiPostgresFixture>
{
    [Theory]
    [InlineData("valid", 201)]
    [InlineData("signature", 401)]
    [InlineData("unsigned", 401)]
    [InlineData("issuer", 401)]
    [InlineData("audience", 401)]
    [InlineData("expired", 401)]
    [InlineData("future", 401)]
    [InlineData("overlong", 401)]
    [InlineData("just-expired", 401)]
    [InlineData("no-start", 401)]
    [InlineData("subject", 403)]
    [InlineData("role", 403)]
    public async Task Real_bearer_handler_validates_token_before_actor_mapping(string scenario, int status)
    {
        await using var app = await postgres.CreateApp(testAuthentication: false);
        var assignment = await app.Seed();
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        // Only replace remote IdP discovery with local test signing material. Real Bearer handler stays installed.
        await using var authenticatedApp = app.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters.ValidIssuer = "https://issuer.test";
                options.TokenValidationParameters.ValidAudience = "lms-api";
                options.TokenValidationParameters.IssuerSigningKey = key;
                // Exercise the host's zero-skew policy, not a test-only override.
            })));
        using var client = authenticatedApp.CreateClient();
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: scenario == "issuer" ? "https://foreign.test" : "https://issuer.test",
            audience: scenario == "audience" ? "other-api" : "lms-api",
            claims: [new Claim("sub", scenario == "subject" ? "unmapped-subject" : ApiFactory.Student.ToString()),
                new Claim("role", scenario == "role" ? "Admin" : "Student")],
            notBefore: scenario == "no-start" ? null : scenario == "future" ? now.AddMinutes(1) :
                scenario == "expired" ? now.AddMinutes(-20) : scenario == "just-expired" ? now.AddMinutes(-10) : now.AddSeconds(-1),
            expires: scenario == "expired" ? now.AddMinutes(-10) : scenario == "just-expired" ? now.AddSeconds(-1) :
                scenario == "overlong" ? now.AddMinutes(16) : now.AddMinutes(10),
            signingCredentials: scenario == "unsigned" ? null : new SigningCredentials(scenario == "signature"
                ? new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)) : key, SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        var response = await client.PostAsJsonAsync($"/api/v1/problem-assignments/{assignment.Id}/submissions",
            new { bodyMarkdown = "solution", attachmentIds = Array.Empty<Guid>(), expectedVersion = (long?)null });
        Assert.Equal(status, (int)response.StatusCode);
        if (response.StatusCode != HttpStatusCode.Created)
        {
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(status == 401 ? "Unauthenticated" : "Forbidden", problem.GetProperty("code").GetString());
        }
    }
}
