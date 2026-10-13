using Lms.Application;
using Lms.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lms.Infrastructure.Tests;

public sealed partial class ScopedAuthorityTests
{
    private sealed class AfterBoundedSave(Action after) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) { after(); return ValueTask.FromResult(result); }
    }
    [Fact]
    public async Task Bounded_work_deadline_before_commit_rolls_back_domain_history_together()
    {
        var cs = await Seed(); var (_, work) = await Work(cs); var version = work.Version;
        var source = new RevocationClock(); var deadline = new RevocationIngress(source.Create()).Arrive(ApplicationRequestKind.TeacherOperation);
        work.Accept(T, work.Submissions[0].Id, "accepted", At);
        await using (var db = PostgresFixture.Open(cs, new AfterBoundedSave(() => source.Elapsed = TimeSpan.FromMinutes(30))))
            await Assert.ThrowsAsync<OperationCanceledException>(() => TeacherPersistenceFixture.Save(db, work, version, default, deadline));
        await using var verify = PostgresFixture.Open(cs);
        Assert.Equal(ProblemWorkStatus.Submitted, (await verify.Works.SingleAsync()).Status);
        Assert.Empty(await verify.Set<Review>().ToListAsync());
        Assert.Equal(CommitOutcome.NotIssued, deadline.Check(ApplicationAction.ResponseEnqueue).Outcome);
    }
    [Fact]
    public async Task Bounded_work_late_commit_is_recorded_but_never_confirmed_as_timely_success()
    {
        var cs = await Seed(); var (_, work) = await Work(cs); var version = work.Version;
        var source = new RevocationClock(); var deadline = new RevocationIngress(source.Create()).Arrive(ApplicationRequestKind.TeacherOperation);
        work.Accept(T, work.Submissions[0].Id, "accepted", At);
        await using (var db = PostgresFixture.Open(cs, new AfterRefreshCommit(() => source.Elapsed = TimeSpan.FromMinutes(30))))
            await Assert.ThrowsAsync<OperationCanceledException>(() => TeacherPersistenceFixture.Save(db, work, version, default, deadline));
        await using var verify = PostgresFixture.Open(cs);
        Assert.Equal(ProblemWorkStatus.Accepted, (await verify.Works.SingleAsync()).Status);
        Assert.Single(await verify.Set<Review>().ToListAsync());
        var decision = deadline.Check(ApplicationAction.ResponseEnqueue);
        Assert.False(decision.Allowed); Assert.Equal(CommitOutcome.AcknowledgedCommitted, decision.Outcome);
        await Assert.ThrowsAsync<OperationCanceledException>(() => TeacherPersistenceFixture.Save(verify, work, version, default, deadline));
        // Deliberately out-of-envelope ACK injection: safety evidence, NOT a physical-bound PASS.
    }
    [Fact]
    public async Task Bounded_work_normal_commit_succeeds_once_and_cannot_reuse_original_handle()
    {
        var cs = await Seed(); var (_, work) = await Work(cs); var version = work.Version;
        var deadline = new RevocationIngress(new RevocationClock().Create()).Arrive(ApplicationRequestKind.TeacherOperation);
        work.Accept(T, work.Submissions[0].Id, "accepted", At);
        await using var db = PostgresFixture.Open(cs);
        await TeacherPersistenceFixture.Save(db, work, version, default, deadline);
        Assert.True(deadline.Check(ApplicationAction.ResponseEnqueue).Allowed);
        await Assert.ThrowsAsync<OperationCanceledException>(() => TeacherPersistenceFixture.Save(db, work, version, default, deadline));
        Assert.Single(await db.Set<Review>().ToListAsync());
    }

    private sealed class AfterBoundedStore(IAttachmentStorage inner, Action after) : IAttachmentStorage
    {
        public async Task Store(string key, Stream content, CancellationToken ct) { await inner.Store(key, content, ct); after(); }
        public Task<Stream> Open(string key, CancellationToken ct) => inner.Open(key, ct);
        public Task Delete(string key, CancellationToken ct) => inner.Delete(key, ct);
    }
    [Fact]
    public async Task Bounded_upload_finishing_after_deadline_never_publishes_eligible_metadata()
    {
        var cs = await Seed(); var source = new RevocationClock();
        var deadline = new RevocationIngress(source.Create()).Arrive(ApplicationRequestKind.TeacherOperation);
        var root = Path.Combine(Path.GetTempPath(), "bounded-upload-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new AfterBoundedStore(new LocalFileAttachmentStorage(new() { Root = root }),
                () => source.Elapsed = TimeSpan.FromMinutes(30));
            await using var db = PostgresFixture.Open(cs);
            var service = new AttachmentService(db, storage, new PostgresAccessPolicy(db, clock), new(), clock,
                NullLogger<AttachmentService>.Instance, deadline);
            await using var photo = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/fixtures/photos/a4-1.png"));
            await Assert.ThrowsAsync<OperationCanceledException>(() => service.Upload(new(S, AuthorRole.Student), "a.png", "image/png", photo, default));
            Assert.Empty(await db.StoredAttachments.ToListAsync()); Assert.Empty(await db.AttachmentMetadata.ToListAsync());
            Assert.Single(Directory.GetFiles(root)); // unknown/private original retained, never compensation-deleted
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
