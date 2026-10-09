using Lms.Application;
using Lms.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Lms.Infrastructure.Tests;

public sealed class PersistenceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateTimeOffset At = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Teacher = Guid.NewGuid();
    private static ProblemAssignment Assignment() => new(Guid.NewGuid(), Guid.NewGuid(), "course",
        new ProblemContentReference(Guid.NewGuid(), "lesson", "problem"));
    private static async Task Seed(string cs, ProblemAssignment assignment)
    {
        await using var db = PostgresFixture.Open(cs);
        db.Assignments.Add(assignment); await db.SaveChangesAsync();
    }
    private static ProblemWork Submit(ProblemAssignment assignment, params Guid[] attachments)
    {
        var work = new ProblemWork(Guid.NewGuid(), assignment);
        work.SubmitSolution(assignment, assignment.StudentId, "first", attachments, At);
        return work;
    }
    private static async Task Save(string cs, ProblemWork work, long? version)
    {
        await using var db = PostgresFixture.Open(cs);
        await new PostgresWorkStore(db).Save(work, version, Ct);
    }
    private static async Task<ProblemWork> Load(string cs, Guid id)
    {
        await using var db = PostgresFixture.Open(cs);
        return (await new PostgresWorkStore(db).FindWork(id, Ct))!;
    }

    [Fact]
    public async Task Legacy_review_survives_annotation_migration_with_empty_array()
    {
        var cs = await fixture.CreateDatabase("20261008013249_AttachmentStorage"); var a = Assignment(); await Seed(cs, a);
        var work = Submit(a); await Save(cs, work, null);
        await using (var db = PostgresFixture.Open(cs))
        {
            // Insert the pre-Annotations shape directly. The current EF model cannot
            // write a column that did not exist yet; never downgrade through M1.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Reviews" ("Id", "ProblemWorkId", "SubmissionId", "TeacherId", "Decision", "CreatedAt")
                VALUES ({Guid.NewGuid()}, {work.Id}, {work.Submissions[0].Id}, {Teacher}, 'Accepted', {At});
                UPDATE "ProblemWorks" SET "Status"='Accepted', "Version"=2, "AcceptedAt"={At} WHERE "Id"={work.Id};
                """);
            await db.Database.MigrateAsync();
        }
        var loaded = await Load(cs, work.Id);
        Assert.Equal(ProblemWorkStatus.Accepted, loaded.Status); Assert.Empty(loaded.Reviews[0].Annotations);
    }

    [Fact]
    public async Task Initial_migration_first_submit_and_isolated_roundtrip()
    {
        var cs = await fixture.CreateDatabase(); var a = Assignment(); await Seed(cs, a);
        await using var db = PostgresFixture.Open(cs); var store = new PostgresWorkStore(db);
        Assert.Null(await store.FindByAssignment(a.Id, Ct));
        var restored = await store.FindAssignment(a.Id, Ct);
        Assert.Equal(a.Content, restored!.Content); Assert.Equal(a.StudentId, restored.StudentId);
        var attachment = Guid.NewGuid(); var work = Submit(a, attachment); await store.Save(work, null, Ct);
        var first = (await store.FindByAssignment(a.Id, Ct))!;
        var second = (await store.FindWork(work.Id, Ct))!;
        Assert.NotSame(first, second); Assert.Empty(first.Events);
        Assert.Equal(1, first.Version); Assert.Equal(ProblemWorkStatus.Submitted, first.Status);
        Assert.Equal(work.Submissions[0].Id, first.Submissions[0].Id);
        Assert.Equal(new[] { attachment }, first.Submissions[0].AttachmentIds);
        Assert.Equal(At, first.Submissions[0].SubmittedAt);
        first.PostMessage(a.StudentId, AuthorRole.Student, "hello", null, [], At);
        Assert.Empty(second.Messages);
        Assert.Single(await db.Events.ToListAsync());
    }

    [Fact]
    public async Task Concurrent_first_submit_has_one_winner_and_no_loser_history_or_events()
    {
        var cs = await fixture.CreateDatabase(); var a = Assignment(); await Seed(cs, a);
        var barrier = new TransactionBarrier();
        await using var left = PostgresFixture.Open(cs, barrier); await using var right = PostgresFixture.Open(cs, barrier);
        var ls = new PostgresWorkStore(left); var rs = new PostgresWorkStore(right);
        Assert.Null(await ls.FindByAssignment(a.Id, Ct)); Assert.Null(await rs.FindByAssignment(a.Id, Ct));
        var results = await Task.WhenAll(Record.ExceptionAsync(() => ls.Save(Submit(a), null, Ct)),
            Record.ExceptionAsync(() => rs.Save(Submit(a), null, Ct)));
        Assert.Single(results, x => x is null);
        Assert.Equal("ConcurrencyConflict", Assert.IsType<UseCaseException>(Assert.Single(results, x => x is not null)).Code);
        await using var verify = PostgresFixture.Open(cs);
        Assert.Equal(1, await verify.Works.CountAsync()); Assert.Equal(1, await verify.Set<Submission>().CountAsync());
        Assert.Equal(1, await verify.Events.CountAsync());
    }

    [Fact]
    public async Task Workflow_preserves_snapshots_accept_is_terminal_but_messages_are_allowed()
    {
        var cs = await fixture.CreateDatabase(); var a = Assignment(); await Seed(cs, a);
        var work = Submit(a, Guid.NewGuid()); await Save(cs, work, null);
        var initial = work.Submissions[0];
        await using (var db = PostgresFixture.Open(cs))
        {
            var service = new ProblemWorkService(new PostgresWorkStore(db), new AllowedAccess(), TimeProvider.System);
            await service.RequestRevision(new(Teacher, AuthorRole.Teacher), work.Id, initial.Id, "explain", 1);
            await service.SubmitSolution(new(a.StudentId, AuthorRole.Student), a.Id, "second", [], 2);
        }
        work = await Load(cs, work.Id);
        Assert.Equal(2, work.AttemptCount); Assert.Equal("first", work.Submissions[0].BodyMarkdown);
        Assert.Equal(initial.Id, work.Submissions[0].Id);
        Assert.Equal(initial.AttachmentIds, work.Submissions[0].AttachmentIds);
        Assert.Equal(initial.SubmittedAt, work.Submissions[0].SubmittedAt);
        Assert.Equal(initial.Id, work.Reviews[0].SubmissionId);
        Assert.Equal(work.Messages[0].Id, work.Reviews[0].MessageId);
        Assert.Equal("SubmissionIsNotCurrent", Assert.Throws<DomainException>(() => work.Accept(Teacher, initial.Id, null, At)).Code);
        work.Accept(Teacher, work.Submissions[1].Id, null, At.AddHours(1)); await Save(cs, work, 3);
        work = await Load(cs, work.Id);
        Assert.Equal(ProblemWorkStatus.Accepted, work.Status); Assert.NotNull(work.AcceptedAt);
        Assert.Throws<DomainException>(() => work.SubmitSolution(a, a.StudentId, "third", [], At));
        Assert.Throws<DomainException>(() => work.RequestRevision(Teacher, work.Submissions[1].Id, "again", At));
        Assert.Throws<DomainException>(() => work.Accept(Teacher, work.Submissions[1].Id, null, At));
        work.PostMessage(a.StudentId, AuthorRole.Student, "thanks", work.Messages[0].Id, [Guid.NewGuid()], At.AddHours(2));
        await Save(cs, work, 4);
        work = await Load(cs, work.Id);
        Assert.Equal(ProblemWorkStatus.Accepted, work.Status); Assert.Equal(2, work.AttemptCount);
        Assert.Equal(2, work.Reviews.Count); Assert.Equal(2, work.Messages.Count);
        Assert.Equal(At.AddHours(1), work.LastReviewedAt);
        await using var verify = PostgresFixture.Open(cs); Assert.Equal(6, await verify.Events.CountAsync());
    }

    [Fact]
    public async Task Concurrent_reviews_and_stale_review_cannot_overwrite_next_attempt()
    {
        var cs = await fixture.CreateDatabase(); var a = Assignment(); await Seed(cs, a);
        var attachment = Guid.NewGuid(); var work = Submit(a, attachment); await Save(cs, work, null);
        var one = await Load(cs, work.Id); var two = await Load(cs, work.Id); var stale = await Load(cs, work.Id);
        AnnotatedAttachment[] Annotations() => [new(attachment, [new(Guid.NewGuid(), "pen", "#dc2626", .003, [new(.2, .3)])])];
        one.RequestRevision(Teacher, one.Submissions[0].Id, "left", At, Annotations());
        two.RequestRevision(Teacher, two.Submissions[0].Id, "right", At, Annotations());
        stale.Accept(Teacher, stale.Submissions[0].Id, "stale", At, Annotations());
        var barrier = new TransactionBarrier();
        await using var left = PostgresFixture.Open(cs, barrier); await using var right = PostgresFixture.Open(cs, barrier);
        var results = await Task.WhenAll(Record.ExceptionAsync(() => new PostgresWorkStore(left).Save(one, 1, Ct)),
            Record.ExceptionAsync(() => new PostgresWorkStore(right).Save(two, 1, Ct)));
        Assert.Single(results, x => x is null);
        Assert.Equal("ConcurrencyConflict", Assert.IsType<UseCaseException>(Assert.Single(results, x => x is not null)).Code);
        work = await Load(cs, work.Id); work.SubmitSolution(a, a.StudentId, "second", [], At); await Save(cs, work, 2);
        Assert.Equal("ConcurrencyConflict", (await Assert.ThrowsAsync<UseCaseException>(() => Save(cs, stale, 1))).Code);
        work = await Load(cs, work.Id);
        Assert.Equal(3, work.Version); Assert.Equal(ProblemWorkStatus.Submitted, work.Status);
        Assert.Equal(2, work.AttemptCount); Assert.Single(work.Reviews); Assert.Single(work.Messages);
        var winner = work.Messages[0].BodyMarkdown == "left" ? one : two;
        Assert.Equal(winner.Reviews[0].Annotations[0].Strokes[0].Id, work.Reviews[0].Annotations[0].Strokes[0].Id);
        await using var verify = PostgresFixture.Open(cs); Assert.Equal(4, await verify.Events.CountAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Failure_after_all_writes_rolls_back_state_history_and_events(bool firstSubmit)
    {
        var cs = await fixture.CreateDatabase(); var a = Assignment(); await Seed(cs, a);
        var attachment = Guid.NewGuid();
        var work = Submit(a, attachment);
        if (!firstSubmit)
        {
            await Save(cs, work, null); work = await Load(cs, work.Id);
            work.RequestRevision(Teacher, work.Submissions[0].Id, "feedback", At, [new(attachment, [new(Guid.NewGuid(), "pen", "#dc2626", .003, [new(.2, .3)])])]);
        }
        await using (var failed = PostgresFixture.Open(cs, new FailAfterWrites()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => new PostgresWorkStore(failed).Save(work, firstSubmit ? null : 1, Ct));
        await using var verify = PostgresFixture.Open(cs);
        Assert.Equal(firstSubmit ? 0 : 1, await verify.Works.CountAsync());
        Assert.Equal(firstSubmit ? 0 : 1, await verify.Set<Submission>().CountAsync());
        Assert.Empty(await verify.Set<Review>().ToListAsync()); Assert.Empty(await verify.Set<Message>().ToListAsync());
        Assert.Equal(firstSubmit ? 0 : 1, await verify.Events.CountAsync());
        if (!firstSubmit) Assert.Equal(1, (await Load(cs, work.Id)).Version);
        // Failed saves do not acknowledge the pending events.
        await Save(cs, work, firstSubmit ? null : 1);
        Assert.Equal(firstSubmit ? 1 : 3, await verify.Events.CountAsync());
        if (!firstSubmit) Assert.Single((await Load(cs, work.Id)).Reviews[0].Annotations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Commit_failure_rolls_back_and_retry_keeps_attempt_and_annotations(bool firstSubmit)
    {
        var cs = await fixture.CreateDatabase(); var a = Assignment(); await Seed(cs, a);
        var attachment = Guid.NewGuid(); var work = Submit(a, attachment);
        if (!firstSubmit)
        {
            await Save(cs, work, null); work = await Load(cs, work.Id);
            work.RequestRevision(Teacher, work.Submissions[0].Id, "feedback", At,
                [new(attachment, [new(Guid.NewGuid(), "pen", "#dc2626", .003, [new(.2, .3)])])]);
        }
        await using (var db = PostgresFixture.Open(cs, new FailAtCommit()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => new PostgresWorkStore(db).Save(work, firstSubmit ? null : 1, Ct));
        await using (var verify = PostgresFixture.Open(cs))
        {
            Assert.Equal(firstSubmit ? 0 : 1, await verify.Works.CountAsync());
            Assert.Equal(firstSubmit ? 0 : 1, await verify.Set<Submission>().CountAsync());
            Assert.Empty(await verify.Set<Review>().ToArrayAsync()); Assert.Empty(await verify.Set<Message>().ToArrayAsync());
            Assert.Equal(firstSubmit ? 0 : 1, await verify.Events.CountAsync());
        }
        await Save(cs, work, firstSubmit ? null : 1);
        var restored = await Load(cs, work.Id); Assert.Equal(1, restored.AttemptCount);
        Assert.Equal(firstSubmit ? 1 : 2, restored.Version);
        if (!firstSubmit) Assert.Single(Assert.Single(restored.Reviews).Annotations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Split_history_load_keeps_one_snapshot_when_review_commits_between_queries(bool existingTransaction)
    {
        var cs = await fixture.CreateDatabase(); var a = Assignment(); await Seed(cs, a);
        var original = Submit(a); await Save(cs, original, null);
        var changed = await Load(cs, original.Id); changed.RequestRevision(Teacher, changed.Submissions[0].Id, "concurrent", At);
        var duringRead = new BetweenHistoryQueries(() => Save(cs, changed, 1));
        await using (var db = PostgresFixture.Open(cs, duringRead))
        {
            await using var tx = existingTransaction ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead) : null;
            var read = (await new PostgresWorkStore(db).FindByAssignment(a.Id, Ct))!;
            Assert.True(duringRead.Triggered); Assert.Equal(1, read.Version); Assert.Equal(ProblemWorkStatus.Submitted, read.Status);
            Assert.Single(read.Submissions); Assert.Empty(read.Reviews); Assert.Empty(read.Messages);
        }
        var fresh = await Load(cs, original.Id); Assert.Equal(2, fresh.Version);
        Assert.Single(fresh.Reviews); Assert.Single(fresh.Messages);
    }

    [Fact]
    public async Task Committed_events_are_not_replayed_on_same_instance_or_reload()
    {
        var cs = await fixture.CreateDatabase(); var a = Assignment(); await Seed(cs, a);
        await using var db = PostgresFixture.Open(cs); var store = new PostgresWorkStore(db); var work = Submit(a);
        await store.Save(work, null, Ct); await store.Save(work, 1, Ct);
        work.RequestRevision(Teacher, work.Submissions[0].Id, "Explain step 2", At);
        await store.Save(work, 1, Ct); await store.Save(work, 2, Ct);
        await Save(cs, work, 2); // A fresh unit of work must not re-enqueue the old event list.
        var loaded = (await store.FindWork(work.Id, Ct))!;
        Assert.Empty(loaded.Events);
        var feedback = Assert.Single(loaded.Messages);
        Assert.Equal("Explain step 2", feedback.BodyMarkdown);
        Assert.Equal(feedback.Id, loaded.Reviews[0].MessageId);
        await store.Save(loaded, 2, Ct);
        Assert.Equal(3, await db.Events.CountAsync());
        var payload = (await db.Events.SingleAsync(x => x.Type == nameof(SolutionSubmitted))).Payload;
        Assert.Contains(work.Submissions[0].Id.ToString(), payload);
    }

    [Fact]
    public async Task Database_enforces_attempt_uniqueness_review_linkage_and_immutable_history()
    {
        var cs = await fixture.CreateDatabase(); var a = Assignment(); var b = Assignment();
        await Seed(cs, a); await Seed(cs, b); var work = Submit(a); var other = Submit(b);
        await Save(cs, work, null); await Save(cs, other, null);
        await using var db = PostgresFixture.Open(cs);
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Submissions" ("Id", "ProblemWorkId", "AttemptNumber", "BodyMarkdown", "SubmittedAt", "SubmittedBy", "AttachmentIds")
            VALUES ({Guid.NewGuid()}, {work.Id}, 1, 'duplicate', {At}, {a.StudentId}, ARRAY[]::uuid[])
            """));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        var foreign = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Reviews" ("Id", "ProblemWorkId", "SubmissionId", "TeacherId", "Decision", "CreatedAt")
            VALUES ({Guid.NewGuid()}, {work.Id}, {other.Submissions[0].Id}, {Teacher}, 'Accepted', {At})
            """));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, foreign.SqlState);
        var immutable = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Submissions" SET "BodyMarkdown" = 'tampered' WHERE "Id" = {work.Submissions[0].Id}
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, immutable.SqlState);
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM "Submissions" WHERE "Id" = {work.Submissions[0].Id}
            """));
        Assert.Equal("first", (await Load(cs, work.Id)).Submissions[0].BodyMarkdown);
    }

    [Fact]
    public async Task Assignment_reference_is_immutable_withdrawal_roundtrips_and_enums_are_constrained()
    {
        var cs = await fixture.CreateDatabase(); var a = Assignment(); await Seed(cs, a);
        await using var db = PostgresFixture.Open(cs);
        var reference = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "ProblemAssignments" SET "CourseVersionId" = {Guid.NewGuid()} WHERE "Id" = {a.Id}
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, reference.SqlState);
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "ProblemAssignments" SET "Status" = 'Invalid' WHERE "Id" = {a.Id}
            """));
        var tracked = await db.Assignments.SingleAsync(); tracked.Withdraw(); await db.SaveChangesAsync();
        var restored = (await new PostgresWorkStore(db).FindAssignment(a.Id, Ct))!;
        Assert.Equal(AssignmentStatus.Withdrawn, restored.Status); Assert.Equal(a.Content, restored.Content);
        Assert.Throws<DomainException>(() => Submit(restored));
    }

    [Fact]
    public async Task Database_rejects_cross_work_message_reply_and_review_feedback()
    {
        var cs = await fixture.CreateDatabase(); var a = Assignment(); var b = Assignment();
        await Seed(cs, a); await Seed(cs, b); var work = Submit(a); var other = Submit(b);
        other.PostMessage(b.StudentId, AuthorRole.Student, "other", null, [], At);
        await Save(cs, work, null); await Save(cs, other, null);
        await using var db = PostgresFixture.Open(cs);
        var reply = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Messages" ("Id", "ProblemWorkId", "AuthorId", "AuthorRole", "BodyMarkdown", "ReplyToMessageId", "CreatedAt", "AttachmentIds")
            VALUES ({Guid.NewGuid()}, {work.Id}, {a.StudentId}, 'Student', 'reply', {other.Messages[0].Id}, {At}, ARRAY[]::uuid[])
            """));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, reply.SqlState);
        var review = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Reviews" ("Id", "ProblemWorkId", "SubmissionId", "TeacherId", "Decision", "MessageId", "CreatedAt")
            VALUES ({Guid.NewGuid()}, {work.Id}, {work.Submissions[0].Id}, {Teacher}, 'Accepted', {other.Messages[0].Id}, {At})
            """));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, review.SqlState);
    }

    private sealed class AllowedAccess : IAccessPolicy
    {
        public Task EnsureCanSubmit(Actor actor, ProblemAssignment assignment, CancellationToken ct) => Task.CompletedTask;
        public Task EnsureCanReview(Actor actor, ProblemAssignment assignment, CancellationToken ct) => Task.CompletedTask;
        public Task EnsureCanPostMessage(Actor actor, ProblemAssignment assignment, CancellationToken ct) => Task.CompletedTask;
        public Task EnsureCanUseAttachments(Actor actor, IReadOnlyList<Guid> ids, CancellationToken ct) => Task.CompletedTask;
    }
}
