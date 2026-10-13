using System.Text.Json;
using Lms.Application;

namespace Lms.Infrastructure.Tests;

public sealed partial class ScopedAuthorityTests
{
    [Fact]
    public async Task Navigation_list_enforces_scope_processing_and_delayed_revocation_without_counts_leakage()
    {
        var cs = await Seed(); var time = new ComponentClock();
        await using (var db = PostgresFixture.Open(cs))
        {
            db.Add(new CourseContextBinding { OrganizationId = A, CourseId = "a", ContextId = Context, ContextVersion = 1, EffectiveFrom = At.AddHours(-1) });
            db.Add(new PublishedContentVersion { Id = Guid.NewGuid(), CourseId = "a", VersionNumber = 1, SourceRevision = "synthetic-nav", PublishedAt = At,
                ContentJson = JsonSerializer.Serialize(new PublishedCourseContent("Course A", "Synthetic description", []), new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
            await db.SaveChangesAsync();
        }
        await using var resources = PostgresFixture.Open(cs);
        await using var processing = PostgresFixture.Open(cs);
        await using var content = PostgresFixture.Open(cs);
        var policy = new ComponentAuthorization(new PostgresScopedAuthorityReader(resources, clock), new PostgresCurrentProcessingGate(processing, time), time);
        var query = new PostgresCourseNavigationQueries(content, policy, time);
        async Task<TokenAuthority> Token(Guid actor) => new(new(Guid.NewGuid(), actor, Guid.NewGuid(), At.AddHours(1)), (await Current(cs, actor))!, At, At.AddMinutes(15));
        var student = await Token(S); var teacher = await Token(T);
        foreach (var (token, role) in new[] { (student, "Student"), (teacher, "Teacher") })
        {
            var row = Assert.Single((await query.Courses(token)).Items);
            Assert.Equal("a", row.Course.Id); Assert.Equal("Course A", row.Course.Title);
            Assert.Equal("Synthetic description", row.Course.Description); Assert.Equal(role, row.Role); Assert.Null(row.Progress);
        }
        // Independently constructed invalid/foreign scopes must not disclose metadata.
        Assert.Empty((await query.Courses(student with { Authority = student.Authority with { Grants = [] } })).Items);
        Assert.Empty((await query.Courses(teacher with { Authority = teacher.Authority with { Grants = [] } })).Items);
        Assert.Empty((await query.Courses(student with { Authority = student.Authority with { Membership = student.Authority.Membership with { OrganizationId = B } } })).Items);
        foreach (var course in new[] { "a2", "b", "legacy", "missing" })
            Assert.Empty((await query.Courses(student with { Authority = student.Authority with { Grants = [student.Authority.Grants[0] with { CourseId = course }] } })).Items);
        var restriction = Guid.NewGuid();
        await using (var db = PostgresFixture.Open(cs))
        {
            db.Add(new ProcessingRestriction { RestrictionId = restriction, OrganizationId = A, ContextId = Context, OriginVersion = 1,
                SubjectActorId = S, Operations = 1, State = "Active", Version = 1 });
            await db.SaveChangesAsync();
        }
        Assert.Empty((await query.Courses(student)).Items);
        Assert.Single((await query.Courses(teacher)).Items); // Course metadata has no student-derived aggregates.
        var grant = teacher.Authority.Grants[0];
        await Change(cs, new(AuthorityKind.Teacher, AuthorityOperation.Revoke, A, T, grant.Version, 1, "a", grant.GroupId, grant.GrantId));
        Assert.Empty((await query.Courses(await Token(T))).Items);
        Assert.Single((await query.Courses(teacher)).Items); // already issued token, accepted delayed revoke
        time.Time = new(At.AddMinutes(15), At.AddMinutes(15));
        Assert.Equal("Forbidden", (await Assert.ThrowsAsync<UseCaseException>(() => query.Courses(teacher))).Code);
        time.Time = null;
        await Assert.ThrowsAsync<UseCaseException>(() => query.Courses(student));
    }
}
