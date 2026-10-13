using Lms.Application;
using Lms.Domain;
using Lms.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lms.Api.Tests;

public sealed class AttachmentConsistencyTests(ApiPostgresFixture fixture) : IClassFixture<ApiPostgresFixture>
{
    private static AttachmentService Service(LmsDbContext db, IAttachmentStorage storage) => new(db, storage,
        new PostgresAccessPolicy(db, TimeProvider.System), new AttachmentOptions(), TimeProvider.System, NullLogger<AttachmentService>.Instance);
    private static LocalFileAttachmentStorage Storage(ApiFactory app) => new(new LocalAttachmentStorageOptions { Root = app.StorageRoot });
    private static Task<AttachmentResult> Upload(AttachmentService service) => service.Upload(new(ApiFactory.Student, AuthorRole.Student),
        "photo.png", "image/png", new MemoryStream(AttachmentImages.Png), default);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Database_failure_retains_original_without_assuming_rollback_or_replaying(bool afterCommit)
    {
        await using var app = await fixture.CreateApp(); await using var source = app.Open();
        await using var db = new LmsDbContext(new DbContextOptionsBuilder<LmsDbContext>().UseNpgsql(source.Database.GetConnectionString())
            .AddInterceptors(new FailSave(afterCommit)).Options);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Upload(Service(db, Storage(app))));
        Assert.Equal(afterCommit ? 1 : 0, await source.StoredAttachments.CountAsync());
        Assert.Single(Directory.GetFiles(app.StorageRoot));
    }

    [Fact]
    public async Task File_failure_does_not_publish_metadata()
    {
        await using var app = await fixture.CreateApp(); await using var db = app.Open();
        await Assert.ThrowsAsync<IOException>(() => Upload(Service(db, new FaultStorage(Storage(app)) { FailStore = true })));
        Assert.Empty(await db.StoredAttachments.ToListAsync()); Assert.Empty(await db.AttachmentMetadata.ToListAsync());
    }

    [Fact]
    public async Task Failed_physical_delete_is_ineligible_and_retryable()
    {
        await using var app = await fixture.CreateApp(); await using var db = app.Open(); var storage = Storage(app);
        var item = await Upload(Service(db, storage));
        var service = Service(db, new FaultStorage(storage) { FailDelete = true });
        await Assert.ThrowsAsync<IOException>(() => service.Delete(new(ApiFactory.Student, AuthorRole.Student), item.AttachmentId, default));
        await using var verify = app.Open(); Assert.True((await verify.StoredAttachments.SingleAsync()).Deleted);
        Assert.False((await verify.AttachmentMetadata.SingleAsync()).IsEligible); Assert.Single(Directory.GetFiles(app.StorageRoot));
        await Service(verify, storage).Delete(new(ApiFactory.Student, AuthorRole.Student), item.AttachmentId, default);
        Assert.Empty(Directory.GetFiles(app.StorageRoot));
    }

    [Fact]
    public async Task Failure_after_submission_writes_rolls_back_claim_history_work_and_events()
    {
        await using var app = await fixture.CreateApp(); var a = await app.Seed(); var id = await app.Attachment(ApiFactory.Student);
        await using var source = app.Open();
        await using var db = new LmsDbContext(new DbContextOptionsBuilder<LmsDbContext>().UseNpgsql(source.Database.GetConnectionString())
            .AddInterceptors(new FailSave(true)).Options);
        var service = new ProblemWorkService(new PostgresWorkStore(db), new PostgresAccessPolicy(db, TimeProvider.System), TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitSolution(new(ApiFactory.Student, AuthorRole.Student), a.Id, null, [id], null));
        Assert.Null((await source.StoredAttachments.SingleAsync()).SubmissionId);
        Assert.Empty(await source.Works.ToListAsync()); Assert.Empty(await source.Set<Submission>().ToListAsync()); Assert.Empty(await source.Events.ToListAsync());
    }

    [Fact]
    public async Task Storage_keys_reject_traversal_and_never_overwrite_an_original()
    {
        await using var app = await fixture.CreateApp(); var storage = Storage(app);
        await Assert.ThrowsAsync<ArgumentException>(() => storage.Store("../escape", new MemoryStream(AttachmentImages.Png), default));
        var key = Guid.NewGuid().ToString("N"); await storage.Store(key, new MemoryStream(AttachmentImages.Png), default);
        await Assert.ThrowsAsync<IOException>(() => storage.Store(key, new MemoryStream(AttachmentImages.Jpeg), default));
        Assert.Equal(AttachmentImages.Png, await File.ReadAllBytesAsync(Path.Combine(app.StorageRoot, key)));
        Assert.Single(Directory.GetFiles(app.StorageRoot));
    }

    [Fact]
    public async Task Attached_metadata_is_immutable_even_through_direct_sql()
    {
        await using var app = await fixture.CreateApp(); var a = await app.Seed(); var id = await app.Attachment(ApiFactory.Student);
        await using var db = app.Open(); var service = new ProblemWorkService(new PostgresWorkStore(db), new PostgresAccessPolicy(db, TimeProvider.System), TimeProvider.System);
        await service.SubmitSolution(new(ApiFactory.Student, AuthorRole.Student), a.Id, null, [id], null);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.StoredAttachments.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SubmissionId, (Guid?)null)));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.StoredAttachments.Where(x => x.Id == id).ExecuteDeleteAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.StoredAttachments.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.OwnerId, Guid.NewGuid())));
    }

    private sealed class FailSave(bool after) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => after ? ValueTask.FromResult(result) : throw new InvalidOperationException("Injected before metadata write");
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Injected after database writes");
    }
    private sealed class FaultStorage(IAttachmentStorage inner) : IAttachmentStorage
    {
        public bool FailStore { get; init; }
        public bool FailDelete { get; init; }
        public Task Store(string key, Stream content, CancellationToken ct) => FailStore ? throw new IOException("Injected file failure") : inner.Store(key, content, ct);
        public Task<Stream> Open(string key, CancellationToken ct) => inner.Open(key, ct);
        public Task Delete(string key, CancellationToken ct) => FailDelete ? throw new IOException("Injected delete failure") : inner.Delete(key, ct);
    }
}
