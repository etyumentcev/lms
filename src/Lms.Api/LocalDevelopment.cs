using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Lms.Application;
using Lms.Domain;
using Lms.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Lms.Api;

// Explicit local Compose tooling, never enabled outside Development.
internal static class LocalDevelopment
{
    internal const string Issuer = "lms-local-development";
    internal const string Audience = "lms-local";
    internal static readonly Guid Student = Guid.Parse("10000000-0000-0000-0000-000000000001");
    internal static readonly Guid Teacher = Guid.Parse("10000000-0000-0000-0000-000000000002");
    internal static bool Enabled(WebApplicationBuilder builder) => builder.Environment.IsDevelopment()
        && builder.Configuration.GetValue<bool>("LocalDemo:Enabled");
    internal static SymmetricSecurityKey Key(IConfiguration configuration)
    {
        var key = configuration["LocalDemo:SigningKey"];
        if (key is null || Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("LocalDemo requires a development signing key of at least 32 bytes.");
        return new(Encoding.UTF8.GetBytes(key));
    }
    internal static string Token(IConfiguration configuration, string role)
    {
        var id = role switch { "Student" => Student, "Teacher" => Teacher,
            _ => throw new ArgumentException("Use Student or Teacher.") };
        var token = new JwtSecurityToken(Issuer, Audience,
            [new Claim("sub", id.ToString()), new Claim("role", role)],
            DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow.AddHours(8),
            new SigningCredentials(Key(configuration), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    internal static async Task Seed(LmsDbContext db)
    {
        const string course = "local-demo";
        var version = Guid.Parse("20000000-0000-0000-0000-000000000001");
        // Atomic, deterministic initial provisioning. Re-running never overwrites learner work.
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (await db.ContentVersions.AnyAsync(x => x.Id == version)) return;
        var problems = Enumerable.Range(1, 4).Select(n => new PublishedProblemContent($"problem-{n}", n,
            new[] { "Делимость суммы", "Остатки", "Последняя цифра", "Доказательство делимости" }[n - 1],
            $"Задача {n}. Докажите утверждение о делимости.")).ToArray();
        db.ContentVersions.Add(new PublishedContentVersion { Id = version, CourseId = course, VersionNumber = 1,
            SourceRevision = "local-demo-v1", PublishedAt = DateTimeOffset.UtcNow,
            ContentJson = JsonSerializer.Serialize(new PublishedCourseContent("Математический кружок", "Локальный учебный курс",
                [new("divisibility", 1, "Делимость и остатки", "Знакомство с задачами на делимость", problems)]), new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
        db.Enrollments.Add(new EnrollmentAccess { StudentId = Student, CourseId = course, IsActive = true });
        db.TeacherGrants.Add(new TeacherAccess { Id = Guid.Parse("30000000-0000-0000-0000-000000000001"), TeacherId = Teacher, CourseId = course });
        db.Lessons.Add(new LessonAccess { CourseId = course, CourseVersionId = version, LessonId = "divisibility", IsPublished = true });
        for (var n = 1; n <= 4; n++) db.Assignments.Add(new ProblemAssignment(
            Guid.Parse($"40000000-0000-0000-0000-{n:000000000000}"), Student, course, new(version, "divisibility", $"problem-{n}")));
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        // Only NotStarted is seeded. All workflow states are verified by contract/feature tests.
    }
}
