using Lms.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lms.Infrastructure.Tests;
public sealed partial class ScopedAuthorityTests
{
    [Theory]
    [InlineData(AuthorityOperation.Move)][InlineData(AuthorityOperation.Revoke)][InlineData(AuthorityOperation.Grant)]
    public async Task Concurrent_move_with_move_revoke_regrant_has_one_winner(AuthorityOperation competing)
    {
        var cs = await Seed(); var barrier = new TransactionBarrier();
        async Task<string> Run(AuthorityCommand command)
        { try { await Change(cs, command, barrier); return "ok"; } catch (UseCaseException e) { return e.Code; } }
        var results = await Task.WhenAll(Run(Move()), Run(Enroll() with { Operation = competing, ExpectedVersion = 1, GroupId = "h" }));
        Assert.Single(results, x => x == "ok"); Assert.Single(results, x => x == "ConcurrencyConflict");
        await using var db = PostgresFixture.Open(cs);
        var s = await db.Set<EnrollmentScope>().SingleAsync(x => x.StudentId == S && x.CourseId == "a"); var e = await db.Enrollments.SingleAsync(x => x.StudentId == S && x.CourseId == "a");
        Assert.Equal(s.GroupId, e.GroupId); Assert.Equal(s.State == "Active", e.IsActive);
    }
    [Fact]
    public async Task Failed_commit_rolls_back_both_representations_history_and_audit()
    {
        var cs = await Seed(); var before = await Snapshot(cs, AuthorityTables);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Change(cs, Move(), new FailAtCommit())); Assert.Equal(before, await Snapshot(cs, AuthorityTables));
    }
    [Fact]
    public async Task Failed_audit_rolls_back_move_after_all_writes()
    {
        var cs = await Seed(); var before = await Snapshot(cs, AuthorityTables);
        await Sql(cs, """
            CREATE FUNCTION reject_s3_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'audit_fault'; END $$;
            CREATE TRIGGER reject_s3_audit BEFORE INSERT ON "SecurityAudit" FOR EACH ROW EXECUTE FUNCTION reject_s3_audit();
            """);
        Assert.Equal("audit_fault", (await Assert.ThrowsAsync<UseCaseException>(() => Change(cs, Move()))).Code); Assert.Equal(before, await Snapshot(cs, AuthorityTables));
    }
    [Fact]
    public async Task Ownership_follows_submission_review_parent_and_denies_foreign_student()
    {
        var cs = await Seed(); var w = await Work(cs, review: true);
        foreach (var r in new[] { new ResourceReference(ResourceKind.Assignment, w.Assignment.Id), new(ResourceKind.Work, w.Work.Id), new(ResourceKind.Submission, w.Work.Submissions[0].Id), new(ResourceKind.Review, w.Work.Reviews[0].Id) })
        {
            var root = await Resource(cs, r); Assert.Equal(A, root!.OrganizationId); Assert.Equal(S, root.StudentId); Assert.Equal("g", root.GroupId);
            Assert.True(await Can(cs, S, r)); Assert.True(await Can(cs, T, r)); Assert.False(await Can(cs, Other, r)); Assert.False(await Can(cs, TH, r)); Assert.False(await Can(cs, S, r, org: B));
            Assert.Null(await Resource(cs, r with { CourseId = "b" }));
        }
    }
    [Fact]
    public async Task Submit_and_review_require_current_grant_and_server_workflow()
    {
        var cs = await Seed(); var pending = await Work(cs, submit: false);
        Assert.True(await Can(cs, S, new(ResourceKind.Work, pending.Work.Id), ScopeAction.Submit)); Assert.False(await Can(cs, Other, new(ResourceKind.Work, pending.Work.Id), ScopeAction.Submit)); Assert.False(await Can(cs, T, new(ResourceKind.Work, pending.Work.Id), ScopeAction.Submit));
        var submitted = await Work(cs);
        Assert.False(await Can(cs, S, new(ResourceKind.Work, submitted.Work.Id), ScopeAction.Submit)); Assert.True(await Can(cs, T, new(ResourceKind.Submission, submitted.Work.Submissions[0].Id), ScopeAction.Review)); Assert.False(await Can(cs, TH, new(ResourceKind.Submission, submitted.Work.Submissions[0].Id), ScopeAction.Review));
        var accepted = await Work(cs, review: true); Assert.False(await Can(cs, T, new(ResourceKind.Submission, accepted.Work.Submissions[0].Id), ScopeAction.Review));
    }
    [Fact]
    public async Task Pending_attachment_is_uploader_only_attached_file_uses_parent_scope()
    {
        var cs = await Seed(); var id = await Upload(cs); var r = new ResourceReference(ResourceKind.Attachment, id);
        Assert.True(await Can(cs, S, r)); Assert.False(await Can(cs, T, r)); Assert.False(await Can(cs, Other, r));
        await Work(cs, attachment: id); Assert.True(await Can(cs, S, r)); Assert.True(await Can(cs, T, r)); Assert.False(await Can(cs, TH, r));
        await Change(cs, Enroll() with { Operation = AuthorityOperation.Revoke, ExpectedVersion = 1 }); Assert.False(await Can(cs, S, r));
        Assert.Null(await Resource(cs, new(ResourceKind.Attachment, await Upload(cs, false))));
    }
    [Fact]
    public async Task Unknown_legacy_resources_are_never_classified_by_read_or_new_membership()
    {
        var cs = await Seed(false); var w = await Work(cs); var before = await Snapshot(cs, LearningTables);
        Assert.Null(await Resource(cs, new(ResourceKind.Work, w.Work.Id))); await Change(cs, Enroll()); Assert.Null(await Resource(cs, new(ResourceKind.Work, w.Work.Id))); Assert.Equal(before, await Snapshot(cs, LearningTables));
        await Sql(cs, $"""INSERT INTO "AccessEnrollments" VALUES ('{S}','a2','z',true)""");
        Assert.Equal("legacy_scope_unclassified", (await Assert.ThrowsAsync<UseCaseException>(() => Change(cs, Enroll() with { CourseId = "a2", GroupId = "z" }))).Code);
    }
    [Fact]
    public async Task History_and_affiliations_reject_rewrite_and_mismatched_sql_is_rejected_at_commit()
    {
        var cs = await Seed(); await Work(cs); var before = await Snapshot(cs, AuthorityTables);
        foreach (var sql in new[] { """UPDATE "AuthorityHistory" SET "Kind"='tamper'""", """DELETE FROM "AuthorityHistory" WHERE true""", """UPDATE "AssignmentScopes" SET "GroupId"='h'""",
            """UPDATE "AccessEnrollments" SET "GroupId"='h' WHERE "CourseId"='a' AND "GroupId"='g'""", """UPDATE "EnrollmentScopes" SET "GroupId"='h',"Version"="Version"+1 WHERE "CourseId"='a' AND "GroupId"='g'""" })
            Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => Sql(cs, sql))).SqlState);
        Assert.Equal(before, await Snapshot(cs, AuthorityTables));
    }
    [Fact]
    public async Task Limited_application_role_can_execute_protocol_but_not_direct_DML_or_DDL()
    {
        var cs = await Seed(); var role = "s3_app_" + Guid.NewGuid().ToString("N");
        // Only a fresh disposable test database/container, never ambient/prod roles.
        await Sql(cs, $"""
            CREATE ROLE {role} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
            GRANT USAGE ON SCHEMA public TO {role};
            GRANT EXECUTE ON FUNCTION public.s3_change_authority(text,text,uuid,uuid,bigint,bigint,text,text,uuid,text,integer) TO {role};
            """);
        foreach (var sql in new[] { """UPDATE "AccessEnrollments" SET "GroupId"='h'""", """UPDATE "EnrollmentScopes" SET "Version"="Version"+1""", """DELETE FROM "AuthorityHistory" WHERE true""",
            """ALTER TABLE "EnrollmentScopes" DISABLE TRIGGER ALL""", "CREATE TABLE public.bypass(id int)", """INSERT INTO "AssignmentScopes" DEFAULT VALUES""", """UPDATE "OrganizationMemberships" SET "Generation"="Generation"+1""" })
            Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => Sql(cs, $"SET ROLE {role}; {sql}"))).SqlState);
        await Sql(cs, $"""
            SET ROLE {role};
            SELECT public.s3_change_authority('Enrollment','Move','{A}','{S}',1,1,'a','h',NULL,NULL,0);
            """);
        Assert.Equal("h", Assert.Single((await Current(cs))!.Grants).GroupId);
    }
}
