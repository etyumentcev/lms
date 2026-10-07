using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Lms.Api.Tests;

public sealed class LocalDevelopmentTests(ApiPostgresFixture postgres) : IClassFixture<ApiPostgresFixture>
{
    private const string Key = "local-integration-test-key-at-least-32-bytes";

    [Theory]
    [InlineData("Development", true, "valid", 200)]
    [InlineData("Development", false, "valid", 401)]
    [InlineData("Production", true, "valid", 401)]
    [InlineData("Staging", true, "valid", 401)]
    [InlineData("Development", true, "signature", 401)]
    [InlineData("Development", true, "expired", 401)]
    [InlineData("Development", true, "anonymous", 401)]
    [InlineData("Development", true, "foreign", 403)]
    public async Task Local_signing_requires_explicit_development_and_preserves_access(string environment, bool enabled, string scenario, int status)
    {
        await using var app = await postgres.CreateApp(testAuthentication: false);
        var assignment = await app.Seed();
        await using (var db = app.Open())
        {
            db.ContentVersions.Add(new PublishedContentVersion { Id = assignment.Content.CourseVersionId, CourseId = assignment.CourseId,
                VersionNumber = 1, SourceRevision = "local-auth-test", PublishedAt = DateTimeOffset.UtcNow,
                ContentJson = JsonSerializer.Serialize(new PublishedCourseContent("Course", null,
                    [new(assignment.Content.LessonId, 1, "Lesson", null, [new(assignment.Content.ProblemId, 1, "Problem", "Statement")])]), ApiFactory.Json) });
            await db.SaveChangesAsync();
        }
        await using var configured = app.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("LocalDemo:Enabled", enabled.ToString());
            builder.UseSetting("LocalDemo:SigningKey", Key);
        });
        using var client = configured.CreateClient();
        if (scenario != "anonymous")
        {
            var token = new JwtSecurityToken("lms-local-development", "lms-local",
                [new Claim("sub", (scenario == "foreign" ? Guid.NewGuid() : ApiFactory.Student).ToString()), new Claim("role", "Student")],
                DateTime.UtcNow.AddHours(-2), scenario == "expired" ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddHours(1),
                new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(scenario == "signature" ? Key + "wrong" : Key)), SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        }
        // Real immutable content and access facts exercise the existing authorization boundary.
        var response = await client.GetAsync($"/api/v1/courses/{assignment.CourseId}/workspace");
        Assert.Equal(status, (int)response.StatusCode);
        using var health = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }
}
