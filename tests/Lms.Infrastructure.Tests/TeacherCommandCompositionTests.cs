using System.Security.Cryptography;
using Lms.Application;
using Lms.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Lms.Infrastructure.Tests;

public sealed partial class ScopedAuthorityTests
{
    private sealed class TeacherContexts(string connection, params IInterceptor[] interceptors) : IDbContextFactory<LmsDbContext>
    {
        public LmsDbContext CreateDbContext() => PostgresFixture.Open(connection, interceptors);
    }
    private async Task<string> TeacherCommandSeed()
    {
        var cs = await Seed();
        await using var db = PostgresFixture.Open(cs);
        await new PostgresProcessingStateAdministration(db).Change(new(A, Guid.NewGuid(), 0,
            new OpenProcessingBinding("a", Context, 1, At.AddHours(-1))));
        return cs;
    }
    private async Task<AccessToken> CommandToken(string cs, ComponentJwt jwt, Guid? actor = null)
        => jwt.Issue(new(Guid.NewGuid(), actor ?? T, Guid.NewGuid(), At.AddHours(12)),
            (await Current(cs, actor ?? T))!, [null])!;
    private PostgresTeacherCommands Commands(string cs, ComponentJwt jwt, IAuthorityClock authorityClock,
        IAttachmentStorage? storage = null, params IInterceptor[] interceptors)
        => new(new TeacherContexts(cs, interceptors), jwt, authorityClock, storage ?? new NoTeacherStorage(), new(), clock);
    private sealed class NoTeacherStorage : IAttachmentStorage
    {
        public Task Store(string key, Stream source, CancellationToken ct) => throw new InvalidOperationException("Unexpected file effect");
        public Task<Stream> Open(string key, CancellationToken ct) => throw new InvalidOperationException("Unexpected file effect");
        public Task Delete(string key, CancellationToken ct) => throw new InvalidOperationException("Unexpected file effect");
    }

    [Fact]
    public async Task Teacher_composition_review_message_and_retry_preserve_history_and_one_attempt()
    {
        var cs = await TeacherCommandSeed(); var (_, work) = await Work(cs);
        var time = new RevocationClock().Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "commands", "commands", time); var token = await CommandToken(cs, jwt);
        var commands = Commands(cs, jwt, time); var ingress = new RevocationIngress(time);
        var result = await commands.Review(token, ingress.Arrive(ApplicationRequestKind.TeacherOperation), Guid.NewGuid(),
            work.Id, work.Submissions[0].Id, work.Version, true, "Revise", null);
        Assert.Equal(ProblemWorkStatus.NeedsRevision, result.Status);
        await Assert.ThrowsAsync<UseCaseException>(() => commands.Review(token, ingress.Arrive(), Guid.NewGuid(),
            work.Id, work.Submissions[0].Id, work.Version, true, "Revise", null));
        var intent = Guid.NewGuid();
        var message = await commands.Message(token, ingress.Arrive(), Guid.NewGuid(), intent, work.Id, "Question", null, []);
        await Assert.ThrowsAsync<UseCaseException>(() => commands.Message(token, ingress.Arrive(), Guid.NewGuid(), intent, work.Id, "Question", null, []));
        await using var verify = PostgresFixture.Open(cs);
        Assert.Single(await verify.Set<Review>().ToListAsync());
        Assert.Equal(2, await verify.Set<Message>().CountAsync());
        Assert.True(await verify.Set<Message>().AnyAsync(m => m.Id == message.MessageId));
    }

    [Theory]
    [InlineData("student")][InlineData("group")][InlineData("forged")][InlineData("processing")][InlineData("parent")][InlineData("org")]
    public async Task Teacher_composition_denies_before_reservation_or_domain_effect(string denial)
    {
        var cs = await TeacherCommandSeed(); var attachment = await Upload(cs); var (_, work) = await Work(cs, attachment: attachment);
        var time = new RevocationClock().Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "commands", "commands", time);
        var token = denial == "forged" ? new AccessToken("forged") : await CommandToken(cs, jwt, denial == "student" ? S : denial == "group" ? TH : T);
        if (denial == "org")
        {
            await Change(cs, Teacher(T, "g") with { OrganizationId = B, CourseId = "b" });
            token = jwt.Issue(new(Guid.NewGuid(), T, Guid.NewGuid(), At.AddHours(12)), (await Current(cs, T, B))!, [null])!;
        }
        if (denial == "processing")
        {
            await using var restriction = PostgresFixture.Open(cs);
            await new PostgresProcessingStateAdministration(restriction).Change(new(A, Guid.NewGuid(), 1,
                new AddProcessingRestriction(Guid.NewGuid(), Context, 1, S, 1)));
        }
        await Assert.ThrowsAsync<UseCaseException>(() => Commands(cs, jwt, time).Review(token,
            new RevocationIngress(time).Arrive(), Guid.NewGuid(), denial == "parent" ? Guid.NewGuid() : work.Id,
            work.Submissions[0].Id, work.Version, false, null, null));
        var commands = Commands(cs, jwt, time); var ingress = new RevocationIngress(time);
        var workId = denial == "parent" ? Guid.NewGuid() : work.Id;
        await Assert.ThrowsAsync<UseCaseException>(() => commands.Message(token, ingress.Arrive(), Guid.NewGuid(), Guid.NewGuid(), workId, "Question", null, []));
        await Assert.ThrowsAsync<UseCaseException>(() => commands.Upload(token, ingress.Arrive(), Guid.NewGuid(), Guid.NewGuid(), workId, "a.png", "image/png", new string('A',64), new MemoryStream()));
        await Assert.ThrowsAsync<UseCaseException>(() => commands.Delete(token, ingress.Arrive(), Guid.NewGuid(), workId, attachment));
        await Assert.ThrowsAsync<UseCaseException>(() => commands.ReadAttachment(token, ingress.Arrive(), denial == "parent" ? Guid.NewGuid() : attachment));
        await using var verify = PostgresFixture.Open(cs);
        Assert.Empty(await verify.Set<Review>().ToListAsync());
        Assert.Equal(0, await verify.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM public."TeacherOperationAttempts"
            """).SingleAsync());
    }

    [Fact]
    public async Task Teacher_composition_late_commit_cannot_confirm_or_retry_with_new_deadline()
    {
        var cs = await TeacherCommandSeed(); var (_, work) = await Work(cs); var source = new RevocationClock(); var time = source.Create();
        using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "commands", "commands", time);
        var token = await CommandToken(cs, jwt); var commits = 0;
        var interceptor = new AfterRefreshCommit(() => { if (++commits == 2) source.Elapsed = TimeSpan.FromMinutes(30); });
        var deadline = new RevocationIngress(time).Arrive(ApplicationRequestKind.TeacherOperation);
        await Assert.ThrowsAsync<OperationCanceledException>(() => Commands(cs, jwt, time, null, interceptor).Review(token,
            deadline, Guid.NewGuid(), work.Id, work.Submissions[0].Id, work.Version, false, null, null));
        Assert.Equal(CommitOutcome.AcknowledgedCommitted, deadline.Check(ApplicationAction.ResponseEnqueue).Outcome);
        Assert.False(deadline.Check(ApplicationAction.ResponseEnqueue).Allowed);
        token = await CommandToken(cs, jwt);
        await Assert.ThrowsAsync<UseCaseException>(() => Commands(cs, jwt, time).Review(token,
            new RevocationIngress(time).Arrive(), Guid.NewGuid(), work.Id, work.Submissions[0].Id, work.Version, false, null, null));
        await using var verify = PostgresFixture.Open(cs);
        Assert.Single(await verify.Set<Review>().ToListAsync());
    }
    [Fact]
    public async Task Teacher_composition_upload_private_read_message_claim_and_delete_are_bounded_and_durable()
    {
        var cs = await TeacherCommandSeed(); var (_, work) = await Work(cs); var time = new RevocationClock().Create();
        using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "commands", "commands", time); var token = await CommandToken(cs, jwt);
        var root = Path.Combine(Path.GetTempPath(), "teacher-files-" + Guid.NewGuid().ToString("N"));
        try
        {
            var commands = Commands(cs, jwt, time, new LocalFileAttachmentStorage(new() { Root = root }));
            var ingress = new RevocationIngress(time);
            var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/fixtures/photos/a4-1.png"));
            var hash = Convert.ToHexString(SHA256.HashData(bytes)); var intent = Guid.NewGuid();
            async Task<AttachmentResult> Upload(Guid id) => await commands.Upload(token, ingress.Arrive(), Guid.NewGuid(), id,
                work.Id, "a.png", "image/png", hash, new MemoryStream(bytes));
            var upload = await Upload(intent);
            Assert.Equal(bytes, (await commands.ReadAttachment(token, ingress.Arrive(), upload.AttachmentId)).Bytes);
            await Assert.ThrowsAsync<UseCaseException>(() => Upload(intent)); Assert.Single(Directory.GetFiles(root));
            var message = await commands.Message(token, ingress.Arrive(), Guid.NewGuid(), Guid.NewGuid(), work.Id, "Photo", null, [upload.AttachmentId]);
            Assert.Equal(bytes, (await commands.ReadAttachment(token, ingress.Arrive(), upload.AttachmentId)).Bytes);
            await Assert.ThrowsAsync<UseCaseException>(() => commands.Delete(token, ingress.Arrive(), Guid.NewGuid(), work.Id, upload.AttachmentId));
            var disposable = await Upload(Guid.NewGuid());
            await commands.Delete(token, ingress.Arrive(), Guid.NewGuid(), work.Id, disposable.AttachmentId);
            await Assert.ThrowsAsync<UseCaseException>(() => commands.Delete(token, ingress.Arrive(), Guid.NewGuid(), work.Id, disposable.AttachmentId));
            await Assert.ThrowsAsync<UseCaseException>(() => commands.ReadAttachment(token, ingress.Arrive(), disposable.AttachmentId));
            Assert.Single(Directory.GetFiles(root));
            await using var verify = PostgresFixture.Open(cs);
            Assert.Equal(message.MessageId, (await verify.StoredAttachments.SingleAsync(x => x.Id == upload.AttachmentId)).MessageId);
            Assert.True((await verify.StoredAttachments.SingleAsync(x => x.Id == disposable.AttachmentId)).Deleted);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class AfterFirstCommit(Func<Task> after) : DbTransactionInterceptor
    {
        private int commits;
        public override async Task TransactionCommittedAsync(System.Data.Common.DbTransaction transaction,
            TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { if (++commits == 1) await after(); }
    }
    [Fact]
    public async Task Teacher_composition_revocation_during_operation_keeps_original_deadline_and_refresh_cannot_reexecute()
    {
        var cs = await TeacherCommandSeed(); var (_, work) = await Work(cs); var source = new RevocationClock(); var time = source.Create();
        using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "commands", "commands", time); var token = await CommandToken(cs, jwt);
        var revoke = await RevokeTeacherCommand(cs, false); var deadline = new RevocationIngress(time).Arrive(ApplicationRequestKind.TeacherOperation);
        var cutoff = deadline.ExpiresAt;
        var commands = Commands(cs, jwt, time, null, new AfterFirstCommit(async () => {
            await Change(cs, revoke); source.Elapsed = TimeSpan.FromMinutes(16);
        }));
        var result = await commands.Review(token, deadline, Guid.NewGuid(), work.Id, work.Submissions[0].Id, work.Version, false, null, null);
        Assert.Equal(ProblemWorkStatus.Accepted, result.Status); Assert.Equal(cutoff, deadline.ExpiresAt);
        Assert.Null(jwt.Validate(token));
        Assert.Empty((await Current(cs, T))!.Grants);
        await Assert.ThrowsAsync<UseCaseException>(() => commands.Review(token, new RevocationIngress(time).Arrive(),
            Guid.NewGuid(), work.Id, work.Submissions[0].Id, work.Version, false, null, null));
    }

    [Fact]
    public async Task Teacher_composition_unknown_domain_commit_has_no_second_executor()
    {
        var cs = await TeacherCommandSeed(); var (_, work) = await Work(cs); var time = new RevocationClock().Create();
        using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "commands", "commands", time); var token = await CommandToken(cs, jwt);
        var commits = 0; var deadline = new RevocationIngress(time).Arrive();
        var commands = Commands(cs, jwt, time, null, new AfterRefreshCommit(() => {
            if (++commits == 2) throw new IOException("Synthetic lost domain commit acknowledgement");
        }));
        await Assert.ThrowsAsync<IOException>(() => commands.Review(token, deadline, Guid.NewGuid(), work.Id,
            work.Submissions[0].Id, work.Version, false, null, null));
        Assert.Equal(CommitOutcome.Unknown, deadline.Check(ApplicationAction.ResponseEnqueue).Outcome);
        await Assert.ThrowsAsync<UseCaseException>(() => Commands(cs, jwt, time).Review(token, new RevocationIngress(time).Arrive(),
            Guid.NewGuid(), work.Id, work.Submissions[0].Id, work.Version, false, null, null));
        await using var verify = PostgresFixture.Open(cs); Assert.Single(await verify.Set<Review>().ToListAsync());
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Teacher_composition_file_unknown_commit_never_reexecutes(bool delete)
    {
        var cs = await TeacherCommandSeed(); var (_, work) = await Work(cs); var time = new RevocationClock().Create();
        using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "files", "files", time); var token = await CommandToken(cs, jwt);
        var root = Path.Combine(Path.GetTempPath(), "teacher-unknown-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new LocalFileAttachmentStorage(new() { Root = root }); var ingress = new RevocationIngress(time);
            var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/fixtures/photos/a4-1.png"));
            var hash = Convert.ToHexString(SHA256.HashData(bytes)); var intent = Guid.NewGuid(); var normal = Commands(cs, jwt, time, storage);
            Guid id = Guid.Empty;
            if (delete) id = (await normal.Upload(token, ingress.Arrive(), Guid.NewGuid(), intent, work.Id, "a.png", "image/png", hash, new MemoryStream(bytes))).AttachmentId;
            var commits = 0; var broken = Commands(cs, jwt, time, storage, new AfterRefreshCommit(() => {
                if (++commits == 2) throw new IOException("Synthetic unknown file metadata COMMIT");
            }));
            Task Execute(PostgresTeacherCommands commands, ApplicationDeadline deadline) => delete
                ? commands.Delete(token, deadline, Guid.NewGuid(), work.Id, id)
                : commands.Upload(token, deadline, Guid.NewGuid(), intent, work.Id, "a.png", "image/png", hash, new MemoryStream(bytes));
            var original = ingress.Arrive(); await Assert.ThrowsAsync<IOException>(() => Execute(broken, original));
            Assert.Equal(CommitOutcome.Unknown, original.Check(ApplicationAction.ResponseEnqueue).Outcome);
            await Assert.ThrowsAsync<UseCaseException>(() => Execute(normal, ingress.Arrive()));
            Assert.Single(Directory.GetFiles(root));
            await using var verify = PostgresFixture.Open(cs); var row = await verify.StoredAttachments.SingleAsync();
            Assert.Equal(delete, row.Deleted);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Teacher_composition_invalid_review_preserves_intent_for_valid_input()
    {
        var cs = await TeacherCommandSeed(); var (_, work) = await Work(cs); var time = new RevocationClock().Create();
        using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "commands", "commands", time); var token = await CommandToken(cs, jwt);
        var commands = Commands(cs, jwt, time); var ingress = new RevocationIngress(time);
        await Assert.ThrowsAsync<DomainException>(() => commands.Review(token, ingress.Arrive(), Guid.NewGuid(), work.Id,
            work.Submissions[0].Id, work.Version, true, "", null));
        Assert.Equal(ProblemWorkStatus.NeedsRevision, (await commands.Review(token, ingress.Arrive(), Guid.NewGuid(), work.Id,
            work.Submissions[0].Id, work.Version, true, "Clarify", null)).Status);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Teacher_composition_annotations_require_independent_file_provenance(bool scoped)
    {
        var cs = await TeacherCommandSeed(); var attachment = await Upload(cs, scoped); var (_, work) = await Work(cs, attachment: attachment);
        var time = new RevocationClock().Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "annotation", "annotation", time); var token = await CommandToken(cs, jwt);
        var commands = Commands(cs, jwt, time);
        Task<ReviewResult> ExecuteReview() => commands.Review(token, new RevocationIngress(time).Arrive(), Guid.NewGuid(), work.Id,
            work.Submissions[0].Id, work.Version, false, null, [new(attachment, [])]);
        if (scoped) Assert.Equal(ProblemWorkStatus.Accepted, (await ExecuteReview()).Status);
        else
        {
            await Assert.ThrowsAsync<UseCaseException>(ExecuteReview);
            await using var verify = PostgresFixture.Open(cs); Assert.Empty(await verify.Set<Review>().ToListAsync());
        }
    }

}
