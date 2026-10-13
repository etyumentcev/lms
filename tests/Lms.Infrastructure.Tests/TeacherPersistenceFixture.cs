using Lms.Application;
using Lms.Domain;

namespace Lms.Infrastructure.Tests;

// Storage-only test composition with explicit synthetic authority. This does NOT prove
// admission, durable reservation or safe retry. Those are asserted by TeacherCommandCompositionTests.
// No runtime endpoint/config flag can construct this fixture.
internal static class TeacherPersistenceFixture
{
    private sealed class Source : IQualifiedClockSource
    {
        public QualifiedClockReading Read() => new("persistence-fixture", true,
            new(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch), TimeSpan.Zero, TimeSpan.Zero);
    }
    private sealed class Ingress() : TrustedApplicationIngressAdapter(new(new Source(), TimeSpan.Zero, TimeSpan.Zero))
    { public ApplicationDeadline Arrive() => Capture(ApplicationRequestKind.TeacherOperation); }
    public static async Task Save(LmsDbContext db, ProblemWork work, long? version, CancellationToken ct,
        ApplicationDeadline? deadline = null)
    {
        var teacherEvent = work.Events.Any(e => e is RevisionRequested or SubmissionAccepted ||
            e is MessagePosted m && work.Messages.Any(x => x.Id == m.MessageId && x.AuthorRole == AuthorRole.Teacher));
        if (!teacherEvent) { await new PostgresWorkStore(db, deadline).Save(work, version, ct); return; }
        deadline ??= new Ingress().Arrive();
        var actor = Guid.NewGuid();
        using var operation = new TeacherOperationContext(actor, work.Id, "StorageFixture", deadline);
        operation.Enter(new(actor, AuthorRole.Teacher), work.Id, "StorageFixture");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await new PostgresWorkStore(db, deadline).SaveInTransaction(work, version, operation, ct);
        Task? pending = null;
        if (!deadline.DispatchCommit(() => pending = tx.CommitAsync(ct)).Allowed) throw new OperationCanceledException();
        await pending!; deadline.AcknowledgeCommit();
        ct.ThrowIfCancellationRequested();
        if (!deadline.Check(ApplicationAction.ResponseEnqueue).Allowed) throw new OperationCanceledException();
    }
}
