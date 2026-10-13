using Lms.Application;
using Lms.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lms.Infrastructure.Tests;

public sealed partial class ScopedAuthorityTests
{
    [Theory]
    [InlineData("upload")][InlineData("open")][InlineData("delete")]
    public async Task Legacy_teacher_file_service_denies_before_io_even_with_live_deadline(string operation)
    {
        // Unreachable DB and storage that always throw independently assert pre-effect denial.
        await using var db = PostgresFixture.Open("Host=127.0.0.1;Port=1;Database=unreachable");
        var deadline = new RevocationIngress(new RevocationClock().Create()).Arrive();
        var service = new AttachmentService(db, new NoTeacherStorage(), new PostgresAccessPolicy(db, clock), new(), clock,
            NullLogger<AttachmentService>.Instance, deadline);
        var actor = new Actor(T, AuthorRole.Teacher);
        Task Call() => operation switch {
            "upload" => service.Upload(actor, "a.png", "image/png", Stream.Null, default),
            "open" => service.Open(actor, Guid.NewGuid(), default),
            _ => service.Delete(actor, Guid.NewGuid(), default)
        };
        Assert.Equal("Forbidden", (await Assert.ThrowsAsync<UseCaseException>(Call)).Code);
    }
    [Theory]
    [InlineData("review")][InlineData("message")]
    public async Task Direct_store_teacher_history_cannot_bypass_executor(string operation)
    {
        var cs = await Seed(); var (_, original) = await Work(cs);
        await using var db = PostgresFixture.Open(cs);
        var work = (await new PostgresWorkStore(db).FindWork(original.Id, default))!;
        if (operation == "review") work.Accept(T, work.Submissions[0].Id, "Feedback", At);
        else work.PostMessage(T, AuthorRole.Teacher, "Message", null, [], At);
        Assert.Equal("Forbidden", (await Assert.ThrowsAsync<UseCaseException>(() =>
            new PostgresWorkStore(db).Save(work, original.Version, default))).Code);
        Assert.Empty(await db.Set<Review>().ToListAsync()); Assert.Empty(await db.Set<Message>().ToListAsync());
        Assert.Single(await db.Events.ToListAsync());
        Assert.Equal(original.Version, (await db.Works.AsNoTracking().SingleAsync()).Version);
    }
    [Fact]
    public async Task Transaction_store_cannot_replace_context_deadline()
    {
        var source = new RevocationClock(); var ingress = new RevocationIngress(source.Create());
        var assignment = new ProblemAssignment(Guid.NewGuid(), S, "a", new(Guid.NewGuid(), "lesson", "problem"));
        var work = new ProblemWork(Guid.NewGuid(), assignment);
        work.SubmitSolution(assignment, S, "first", [], At);
        work.Accept(T, work.Submissions[0].Id, "accepted", At);
        using var operation = new TeacherOperationContext(T, work.Id, "Review", ingress.Arrive());
        operation.Enter(new(T, AuthorRole.Teacher), work.Id, "Review");
        await using var db = PostgresFixture.Open("Host=127.0.0.1;Port=1;Database=unreachable");
        Assert.Equal("Forbidden", (await Assert.ThrowsAsync<UseCaseException>(() =>
            new PostgresWorkStore(db, ingress.Arrive()).SaveInTransaction(work, 1, operation, default))).Code);
    }

    private sealed class DisposeDeadlineFiles(Action afterDispose) : IAttachmentStorage
    {
        private byte[] bytes = [];
        public bool Disposed { get; private set; }
        public async Task Store(string key, Stream source, CancellationToken ct)
        { using var copy = new MemoryStream(); await source.CopyToAsync(copy, ct); bytes = copy.ToArray(); }
        public Task<Stream> Open(string key, CancellationToken ct) => Task.FromResult<Stream>(new DisposeDeadlineStream(bytes, () => {
            Disposed = true; afterDispose();
        }));
        public Task Delete(string key, CancellationToken ct) => throw new InvalidOperationException("Unexpected delete");
    }
    private sealed class DisposeDeadlineStream(byte[] bytes, Action afterDispose) : MemoryStream(bytes)
    {
        public override async ValueTask DisposeAsync() { await base.DisposeAsync(); afterDispose(); }
    }
    [Fact]
    public async Task Teacher_private_read_rechecks_original_deadline_after_stream_disposal()
    {
        var cs = await TeacherCommandSeed(); var (_, work) = await Work(cs);
        var source = new RevocationClock(); var clockSource = source.Create();
        using var key = System.Security.Cryptography.RSA.Create(2048);
        var jwt = new ComponentJwt(key, "commands", "commands", clockSource); var token = await CommandToken(cs, jwt);
        var files = new DisposeDeadlineFiles(() => source.Elapsed = TimeSpan.FromMinutes(30));
        var commands = Commands(cs, jwt, clockSource, files); var ingress = new RevocationIngress(clockSource);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/fixtures/photos/a4-1.png"));
        var uploaded = await commands.Upload(token, ingress.Arrive(), Guid.NewGuid(), Guid.NewGuid(), work.Id,
            "a.png", "image/png", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), new MemoryStream(bytes));
        await Assert.ThrowsAsync<OperationCanceledException>(() => commands.ReadAttachment(token,
            ingress.Arrive(ApplicationRequestKind.TeacherOperation), uploaded.AttachmentId));
        Assert.True(files.Disposed);
    }

}
