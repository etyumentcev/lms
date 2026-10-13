using System.Security.Cryptography;
using System.Text;
using Lms.Application;
using Lms.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Lms.Infrastructure.Tests;

public sealed partial class ScopedAuthorityTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-000000000001"), B = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid S = Guid.Parse("00000000-0000-0000-0000-000000000011"), Other = Guid.Parse("00000000-0000-0000-0000-000000000012");
    private static readonly Guid T = Guid.Parse("00000000-0000-0000-0000-000000000013"), TH = Guid.Parse("00000000-0000-0000-0000-000000000014");
    private static readonly Guid Admin = Guid.Parse("00000000-0000-0000-0000-000000000015"), Context = Guid.Parse("00000000-0000-0000-0000-000000000016");
    private static readonly DateTimeOffset At = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private sealed class Clock : TimeProvider { public DateTimeOffset Now { get; set; } = At; public override DateTimeOffset GetUtcNow() => Now; }
    private readonly Clock clock = new();
    private async Task<string> Seed(bool grants = true)
    {
        var cs = await fixture.CreateDatabase();
        await using (var db = PostgresFixture.Open(cs))
        {
            db.AddRange(new Organization { OrganizationId = A, State = "Enabled", SyntheticOnly = true }, new Organization { OrganizationId = B, State = "Enabled", SyntheticOnly = true });
            foreach (var actor in new[] { S, Other, T, TH, Admin }) db.Add(new ActorRecord { ActorId = actor });
            db.AddRange(new CourseRegistration { CourseId = "a", OrganizationId = A, MappingState = "Verified" }, new CourseRegistration { CourseId = "a2", OrganizationId = A, MappingState = "Verified" },
                new CourseRegistration { CourseId = "b", OrganizationId = B, MappingState = "Verified" }, new CourseRegistration { CourseId = "legacy", MappingState = "Unknown" });
            foreach (var (course, group) in new[] { ("a","g"), ("a","h"), ("a2","z"), ("b","g"), ("b","foreign") }) db.Add(new CourseGroup { CourseId = course, GroupId = group });
            db.Add(new ProcessingContextVersion { ContextId = Context, Version = 1, OrganizationId = A, SyntheticOnly = true, ApprovalState = "SyntheticOnly", EffectiveFrom = At.AddDays(-1) });
            await db.SaveChangesAsync();
        }
        foreach (var org in new[] { A, B })
        foreach (var actor in new[] { S, Other, T, TH, Admin })
            await Change(cs, new(AuthorityKind.Membership, AuthorityOperation.Grant, org, actor, 0, Role: actor == Admin ? null : actor == S || actor == Other ? ScopeRole.Student : ScopeRole.Teacher, Permissions: actor == Admin ? 15 : 0));
        if (grants)
        {
            await Change(cs, Enroll()); await Change(cs, Enroll() with { ActorId = Other, GroupId = "h" });
            await Change(cs, Enroll() with { OrganizationId = B, CourseId = "b" });
            await Change(cs, Teacher(T, "g")); await Change(cs, Teacher(TH, "h"));
        }
        return cs;
    }
    private static AuthorityCommand Enroll() => new(AuthorityKind.Enrollment, AuthorityOperation.Grant, A, S, 0, 1, "a", "g");
    private static AuthorityCommand Move(long version = 1) => Enroll() with { Operation = AuthorityOperation.Move, ExpectedVersion = version, GroupId = "h" };
    private static AuthorityCommand Teacher(Guid actor, string? group) => new(AuthorityKind.Teacher, AuthorityOperation.Grant, A, actor, 0, 1, "a", group, Guid.NewGuid());
    private async Task<AuthorityChange> Change(string cs, AuthorityCommand c, params IInterceptor[] interceptors)
    {
        await using var db = PostgresFixture.Open(cs, interceptors); return await new PostgresScopedAuthorityAdministration(db).Change(c);
    }
    private async Task<CurrentAuthority?> Current(string cs, Guid? actor = null, Guid? org = null)
    {
        await using var db = PostgresFixture.Open(cs); return await new PostgresScopedAuthorityReader(db, clock).ResolveCurrent(org ?? A, actor ?? S);
    }
    private async Task<ResourceOwnership?> Resource(string cs, ResourceReference r)
    {
        await using var db = PostgresFixture.Open(cs); return await new PostgresScopedAuthorityReader(db, clock).ResolveResource(r);
    }
    private async Task<bool> Can(string cs, Guid actor, ResourceReference r, ScopeAction action = ScopeAction.Read, Guid? org = null)
    {
        await using var db = PostgresFixture.Open(cs); return await new PostgresScopedAuthorityReader(db, clock).CanAccess(org ?? A, actor, r, action);
    }
    private static async Task Sql(string cs, string sql) { await using var db = PostgresFixture.Open(cs); await db.Database.ExecuteSqlRawAsync(sql); }
    private static async Task<string> Snapshot(string cs, params string[] tables)
    {
        await using var connection = new NpgsqlConnection(cs); await connection.OpenAsync(); var values = new List<string>();
        foreach (var table in tables)
        {
            Assert.Contains(table, AuthorityTables.Concat(LearningTables));
            await using var command = new NpgsqlCommand($"SELECT COALESCE(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text)::text,'[]') FROM \"{table}\" t", connection);
            values.Add((string)(await command.ExecuteScalarAsync())!);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', values))));
    }
    private static readonly string[] AuthorityTables = ["OrganizationMemberships", "EnrollmentScopes", "AccessEnrollments", "TeacherAssignmentScopes", "AccessTeachers", "AuthorityHistory", "SecurityAudit"];
    private static readonly string[] LearningTables = ["ProblemAssignments", "AssignmentScopes", "ProblemWorks", "Submissions", "Reviews", "Messages", "DomainEvents", "StoredAttachments", "AttachmentScopes"];
    private async Task<(ProblemAssignment Assignment, ProblemWork Work)> Work(string cs, bool submit = true, bool review = false, Guid? attachment = null, string course = "a")
    {
        var assignment = new ProblemAssignment(Guid.NewGuid(), S, course, new(Guid.NewGuid(), "lesson", "problem"));
        await using var db = PostgresFixture.Open(cs); db.Add(assignment);
        db.Add(new LessonAccess { CourseId = course, CourseVersionId = assignment.Content.CourseVersionId, LessonId = "lesson", IsPublished = true });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var work = new ProblemWork(Guid.NewGuid(), assignment);
        if (submit) work.SubmitSolution(assignment, S, "synthetic work", attachment.HasValue ? [attachment.Value] : [], At);
        if (review) work.Accept(T, work.Submissions[0].Id, "synthetic review", At.AddMinutes(1));
        // Reader-policy fixtures may include historical teacher decisions. Seed those
        // explicitly; executing a new teacher command must use the trusted executor.
        if (review) { db.Add(work); await db.SaveChangesAsync(); db.ChangeTracker.Clear(); }
        else await new PostgresWorkStore(db).Save(work, null, CancellationToken.None);
        return (assignment, work);
    }
    private async Task<Guid> Upload(string cs, bool scoped = true)
    {
        var id = Guid.NewGuid(); await using var db = PostgresFixture.Open(cs);
        db.AddRange(new AttachmentAccess { Id = id, OwnerId = S, IsEligible = true, ExpiresAt = At.AddDays(1) },
            new StoredAttachment { Id = id, OwnerId = S, FileName = "synthetic.bin", MediaType = "application/octet-stream", Size = 1, StorageKey = id.ToString(), CreatedAt = At });
        await db.SaveChangesAsync();
        if (scoped) { db.Add(new AttachmentScope { AttachmentId = id, OrganizationId = A, CourseId = "a", UploaderActorId = S, ContextId = Context, ContextVersion = 1 }); await db.SaveChangesAsync(); }
        return id;
    }
    [Fact]
    public async Task Multi_org_identity_memberships_are_independent_without_implicit_grants()
    {
        var cs = await Seed(false);
        await using (var db = PostgresFixture.Open(cs)) await new PostgresIdentityMapping(db, clock).CreateInactive(Guid.NewGuid(), S, A, "synthetic-shared-s3");
        Assert.Empty((await Current(cs))!.Grants); Assert.Empty((await Current(cs, org: B))!.Grants);
        Assert.False(await Can(cs, S, new(ResourceKind.Course, CourseId: "a"))); Assert.False(await Can(cs, Admin, new(ResourceKind.Course, CourseId: "a")));
        await Change(cs, Enroll()); Assert.Single((await Current(cs))!.Grants); Assert.Empty((await Current(cs, org: B))!.Grants);
        await Change(cs, new(AuthorityKind.Membership, AuthorityOperation.Revoke, A, S, 1));
        Assert.Null(await Current(cs)); Assert.NotNull(await Current(cs, org: B));
    }
    [Fact]
    public async Task Move_creates_generation_preserves_old_work_group_and_entire_learning_history()
    {
        var cs = await Seed(); var file = await Upload(cs); var old = await Work(cs, review: true, attachment: file);
        var before = await Snapshot(cs, LearningTables); var oldAuthority = (await Current(cs))!; var oldGrant = Assert.Single(oldAuthority.Grants);
        var result = await Change(cs, Move()); Assert.Equal(2, result.Generation); Assert.Equal(3, result.Version); Assert.Equal(before, await Snapshot(cs, LearningTables));
        Assert.Equal("g", (await Resource(cs, new(ResourceKind.Work, old.Work.Id)))!.GroupId);
        Assert.True(await Can(cs, T, new(ResourceKind.Work, old.Work.Id))); Assert.False(await Can(cs, TH, new(ResourceKind.Work, old.Work.Id)));
        Assert.True(await Can(cs, S, new(ResourceKind.Work, old.Work.Id))); Assert.True(await Can(cs, T, new(ResourceKind.Attachment, file))); Assert.False(await Can(cs, TH, new(ResourceKind.Attachment, file)));
        Assert.DoesNotContain(oldGrant, (await Current(cs))!.Grants);
        var fresh = await Work(cs); var freshRoot = (await Resource(cs, new(ResourceKind.Submission, fresh.Work.Submissions[0].Id)))!; Assert.Equal("h", freshRoot.GroupId);
        Assert.False(ScopePolicy.Allows(oldAuthority, freshRoot, ScopeAction.Read, S));
        Assert.False(await Can(cs, T, new(ResourceKind.Work, fresh.Work.Id))); Assert.True(await Can(cs, TH, new(ResourceKind.Work, fresh.Work.Id)));
        await using var db = PostgresFixture.Open(cs); Assert.Equal("h", (await db.Enrollments.SingleAsync(x => x.StudentId == S && x.CourseId == "a")).GroupId);
        Assert.Contains(await db.Set<AuthorityHistory>().Where(x => x.Kind == "EnrollmentScopes" && x.ActorId == S).ToListAsync(), x => x.Snapshot.Contains("Revoked"));
    }
    [Theory]
    [InlineData("z")][InlineData("foreign")][InlineData("missing")]
    public async Task Move_rejects_group_of_other_course_or_organization(string group)
    {
        var cs = await Seed(); var before = await Snapshot(cs, AuthorityTables);
        Assert.Equal("scope_denied", (await Assert.ThrowsAsync<UseCaseException>(() => Change(cs, Move() with { GroupId = group }))).Code);
        Assert.Equal(before, await Snapshot(cs, AuthorityTables));
    }
    [Fact]
    public async Task Cross_org_assignments_and_unknown_registry_are_denied()
    {
        var cs = await Seed(false);
        foreach (var cmd in new[] { Enroll() with { CourseId = "b" }, Enroll() with { CourseId = "legacy", GroupId = null }, Teacher(T, "g") with { OrganizationId = B } })
            Assert.Equal("scope_denied", (await Assert.ThrowsAsync<UseCaseException>(() => Change(cs, cmd))).Code);
        Assert.False(await Can(cs, S, new(ResourceKind.Group, CourseId: "b", GroupId: "g"))); Assert.Null(await Resource(cs, new(ResourceKind.Course, CourseId: "legacy")));
    }
    [Fact]
    public async Task Repeated_move_and_stale_version_are_conflicts_without_extra_history()
    {
        var cs = await Seed(); await Change(cs, Move()); var before = await Snapshot(cs, AuthorityTables);
        Assert.Equal("ConcurrencyConflict", (await Assert.ThrowsAsync<UseCaseException>(() => Change(cs, Move()))).Code); Assert.Equal(before, await Snapshot(cs, AuthorityTables));
    }
    [Fact]
    public async Task Membership_revoke_regrant_does_not_revive_old_scopes_or_move_stale_generation()
    {
        var cs = await Seed(); var revoke = await Change(cs, new(AuthorityKind.Membership, AuthorityOperation.Revoke, A, S, 1)); Assert.InRange(revoke.AccessValidUntil!.Value, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<UseCaseException>(() => Change(cs, Move())); Assert.True(await Can(cs, S, new(ResourceKind.Course, CourseId: "b"), org: B));
        await Change(cs, new(AuthorityKind.Membership, AuthorityOperation.Grant, A, S, revoke.Version, Role: ScopeRole.Student)); Assert.Empty((await Current(cs))!.Grants);
        await Assert.ThrowsAsync<UseCaseException>(() => Change(cs, Move() with { MembershipGeneration = 2 }));
        await Assert.ThrowsAsync<UseCaseException>(() => Change(cs, Enroll() with { ExpectedVersion = 1 }));
        await Change(cs, Enroll() with { ExpectedVersion = 1, MembershipGeneration = 2 }); Assert.Equal(2, Assert.Single((await Current(cs))!.Grants).Generation);
    }
    [Fact]
    public async Task Enrollment_revoke_regrant_retains_deadline_and_history()
    {
        var cs = await Seed(); var cmd = Enroll() with { Operation = AuthorityOperation.Revoke, ExpectedVersion = 1 };
        var revoked = await Change(cs, cmd); var count = await Snapshot(cs, AuthorityTables); clock.Now = At.AddMinutes(20);
        Assert.Equal(revoked, await Change(cs, cmd with { ExpectedVersion = revoked.Version })); Assert.Equal(count, await Snapshot(cs, AuthorityTables)); Assert.Empty((await Current(cs))!.Grants);
        await Assert.ThrowsAsync<UseCaseException>(() => Change(cs, Move(revoked.Version)));
        var fresh = await Change(cs, Enroll() with { ExpectedVersion = revoked.Version }); Assert.Equal(2, fresh.Generation); Assert.Null(fresh.AccessValidUntil);
        await using var db = PostgresFixture.Open(cs);
        Assert.Contains(await db.Set<AuthorityHistory>().Where(x => x.Kind == "EnrollmentScopes").ToListAsync(), x => x.Snapshot.Contains("Revoked") && x.Snapshot.Contains("AccessValidUntil"));
    }
    [Fact]
    public async Task Teacher_regrant_and_whole_course_are_explicit_no_role_fallback()
    {
        var cs = await Seed(false); var cmd = Teacher(T, "g"); await Change(cs, cmd);
        Assert.True(await Can(cs, T, new(ResourceKind.Course, CourseId: "a"))); Assert.True(await Can(cs, T, new(ResourceKind.Group, CourseId: "a", GroupId: "g")));
        Assert.False(await Can(cs, T, new(ResourceKind.Group, CourseId: "a", GroupId: "h")));
        var revoked = await Change(cs, cmd with { Operation = AuthorityOperation.Revoke, ExpectedVersion = 1 }); Assert.Empty((await Current(cs, T))!.Grants);
        Assert.Equal(2, (await Change(cs, cmd with { ExpectedVersion = revoked.Version })).Generation); await Change(cs, Teacher(T, null));
        Assert.True(await Can(cs, T, new(ResourceKind.Group, CourseId: "a", GroupId: "h"))); Assert.False(await Can(cs, T, new(ResourceKind.Course, CourseId: "a2"))); Assert.False(await Can(cs, Admin, new(ResourceKind.Course, CourseId: "a")));
    }
}
