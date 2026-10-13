using Lms.Domain;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Lms.Infrastructure")]
[assembly: InternalsVisibleTo("Lms.Application.Tests")]
[assembly: InternalsVisibleTo("Lms.Infrastructure.Tests")]

namespace Lms.Application;

// Process-local capability, never a DTO or an ambient AsyncLocal. Only the trusted
// infrastructure creates it after JWT/scope/current-processing admission; writes also
// require acknowledged first durable reservation. Read-only file operations need no attempt.
// It cannot be serialized, renewed or reused by another handler/job/request.
public sealed class TeacherOperationContext : IDisposable
{
    private readonly Guid actorId;
    private readonly Guid workId;
    private readonly string kind;
    private int claimed;
    private int closed;
    private int persisted;
    internal ApplicationDeadline Deadline { get; }
    internal TeacherOperationContext(Guid actorId, Guid workId, string kind, ApplicationDeadline deadline)
    {
        this.actorId = actorId; this.workId = workId; this.kind = kind; Deadline = deadline;
        EnsureActive();
    }
    internal void Enter(Actor actor, Guid work, string operation)
    {
        EnsureActive();
        if (actor.Role != AuthorRole.Teacher || actor.Id != actorId || work != workId || operation != kind ||
            Interlocked.CompareExchange(ref claimed, 1, 0) != 0) throw new UseCaseException("Forbidden");
    }
    internal void EnsureActive(ApplicationAction action = ApplicationAction.StartStage)
    {
        if (Volatile.Read(ref closed) != 0) throw new UseCaseException("Forbidden");
        var check = Deadline.Check(action);
        if (!check.Allowed || check.Remaining > RevocationPolicy.MaximumOperationLifetime)
            throw new OperationCanceledException("Original teacher operation deadline is closed.");
    }
    internal void EnterPersistence(ProblemWork work)
    {
        EnsureActive();
        if (work.Id != workId || Volatile.Read(ref claimed) != 1 ||
            Interlocked.CompareExchange(ref persisted, 1, 0) != 0)
            throw new UseCaseException("Forbidden");
    }
    public void Dispose() => Interlocked.Exchange(ref closed, 1);
}
