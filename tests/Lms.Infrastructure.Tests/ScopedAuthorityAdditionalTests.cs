using Lms.Application;
using Lms.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lms.Infrastructure.Tests;
public sealed partial class ScopedAuthorityTests
{
    [Fact]
    public async Task Course_enrollment_is_explicit_and_does_not_imply_named_group()
    {
        var cs = await Seed(false); await Change(cs, Enroll() with { GroupId = null });
        Assert.True(await Can(cs, S, new(ResourceKind.Course, CourseId: "a")));
        Assert.False(await Can(cs, S, new(ResourceKind.Group, CourseId: "a", GroupId: "g")));
        var w = await Work(cs); await Change(cs, Teacher(T, "g"));
        Assert.False(await Can(cs, T, new(ResourceKind.Work, w.Work.Id)));
        await Change(cs, Teacher(T, null)); Assert.True(await Can(cs, T, new(ResourceKind.Work, w.Work.Id)));
        await Assert.ThrowsAsync<UseCaseException>(() => Change(cs, Move()));
    }
    [Fact]
    public async Task New_assignment_capture_serializes_with_group_move()
    {
        var cs = await Seed();
        await using var holder = PostgresFixture.Open(cs); await using var tx = await holder.Database.BeginTransactionAsync();
        await holder.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Organizations\" WHERE \"OrganizationId\"={A} FOR UPDATE");
        var moving = Change(cs, Move());
        var assignment = new ProblemAssignment(Guid.NewGuid(), S, "a", new(Guid.NewGuid(), "l", "p"));
        holder.Add(assignment); await holder.SaveChangesAsync(); await tx.CommitAsync();
        await moving;
        Assert.Equal("g", (await Resource(cs, new(ResourceKind.Assignment, assignment.Id)))!.GroupId);
        Assert.Equal("h", Assert.Single((await Current(cs))!.Grants).GroupId);
    }
    [Fact]
    public async Task Concurrent_membership_revoke_and_move_never_leave_effective_old_membership_grants()
    {
        var cs = await Seed(); var barrier = new TransactionBarrier();
        var revoking = Change(cs, new(AuthorityKind.Membership, AuthorityOperation.Revoke, A, S, 1), barrier);
        async Task<string> Moving()
        { try { await Change(cs, Move(), barrier); return "ok"; } catch (UseCaseException e) { return e.Code; } }
        var moving = Moving(); await Task.WhenAll(revoking, moving);
        Assert.Contains(await moving, new[] { "ok", "scope_denied" }); Assert.Null(await Current(cs)); Assert.NotNull(await Current(cs, org: B));
    }
    [Theory]
    [InlineData(AuthorityKind.Membership)][InlineData(AuthorityKind.Teacher)]
    public async Task Revoke_regrant_races_preserve_history_and_one_version_winner(AuthorityKind kind)
    {
        var cs = await Seed(false); var grant = kind == AuthorityKind.Membership
            ? new AuthorityCommand(kind, AuthorityOperation.Grant, A, S, 1, Role: ScopeRole.Student)
            : Teacher(T, "g");
        if (kind == AuthorityKind.Teacher) { await Change(cs, grant); grant = grant with { ExpectedVersion = 1 }; }
        var barrier = new TransactionBarrier();
        async Task<string> Run(AuthorityCommand c)
        { try { await Change(cs, c, barrier); return "ok"; } catch (UseCaseException e) { return e.Code; } }
        var results = await Task.WhenAll(Run(grant), Run(grant with { Operation = AuthorityOperation.Revoke }));
        Assert.Single(results, x => x == "ok"); Assert.Single(results, x => x == "ConcurrencyConflict");
    }
    [Fact]
    public async Task Suspended_org_quarantine_expired_missing_context_deny()
    {
        var cs = await Seed(); var expired = await Upload(cs);
        clock.Now = At.AddDays(2); Assert.Null(await Resource(cs, new(ResourceKind.Attachment, expired))); clock.Now = At;
        await Sql(cs, $"""UPDATE "AttachmentScopes" SET "Quarantined"=true WHERE "AttachmentId"='{expired}'""");
        Assert.Null(await Resource(cs, new(ResourceKind.Attachment, expired)));
        var noScope = await Upload(cs, false); Assert.Null(await Resource(cs, new(ResourceKind.Attachment, noScope)));
        await Sql(cs, $"""UPDATE "Organizations" SET "State"='Suspended' WHERE "OrganizationId"='{A}'""");
        Assert.Null(await Current(cs)); Assert.Null(await Resource(cs, new(ResourceKind.Course, CourseId: "a")));
        await Assert.ThrowsAsync<UseCaseException>(() => Change(cs, Move())); Assert.NotNull(await Current(cs, org: B));
    }
    [Fact]
    public async Task Database_foreign_keys_prevent_cross_org_scope_even_for_privileged_DML()
    {
        var cs = await Seed(false);
        await Sql(cs, $"""INSERT INTO "AccessEnrollments" VALUES ('{S}','a','g',true)""");
        var error = await Assert.ThrowsAsync<PostgresException>(() => Sql(cs, $"""
            INSERT INTO "EnrollmentScopes" ("StudentId","CourseId","OrganizationId","GroupId","MembershipGeneration","EnrollmentGeneration","State","Version")
            VALUES ('{S}','a','{B}','g',1,1,'Active',1)
            """));
        Assert.Equal("23503", error.SqlState);
    }
    [Fact]
    public async Task Limited_ungranted_role_cannot_execute_protocol()
    {
        var cs = await Seed(); var role = "s3_reader_" + Guid.NewGuid().ToString("N");
        await Sql(cs, $"CREATE ROLE {role} NOLOGIN NOSUPERUSER NOCREATEROLE; GRANT USAGE ON SCHEMA public TO {role}");
        var error = await Assert.ThrowsAsync<PostgresException>(() => Sql(cs, $"""
            SET ROLE {role}; SELECT public.s3_change_authority('Enrollment','Move','{A}','{S}',1,1,'a','h',NULL,NULL,0);
            """));
        Assert.Equal("42501", error.SqlState); Assert.Equal("g", Assert.Single((await Current(cs))!.Grants).GroupId);
    }
}
