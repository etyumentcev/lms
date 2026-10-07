using Lms.Domain;

namespace Lms.Application;

public sealed class UseCaseException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

// Constructed by a trusted host adapter from authenticated identity, never request-body IDs.
public sealed record Actor(Guid Id, AuthorRole Role);

public interface IWorkStore
{
    Task<ProblemAssignment?> FindAssignment(Guid assignmentId, CancellationToken cancellationToken);
    Task<ProblemWork?> FindByAssignment(Guid assignmentId, CancellationToken cancellationToken);
    Task<ProblemWork?> FindWork(Guid workId, CancellationToken cancellationToken);

    // Atomically persist the aggregate and new events. null means INSERT with a unique assignment ID;
    // otherwise compare persisted version. On failure throw ConcurrencyConflict; commit nothing.
    // Loaded aggregates must be isolated per unit of work, not shared mutable cached instances.
    Task Save(ProblemWork work, long? expectedPersistedVersion, CancellationToken cancellationToken);
}

public interface IAccessPolicy
{
    // Active enrollment, active assignment, available lesson, ownership.
    Task EnsureCanSubmit(Actor actor, ProblemAssignment assignment, CancellationToken cancellationToken);
    // Teacher course/group permission for this student's assignment.
    Task EnsureCanReview(Actor actor, ProblemAssignment assignment, CancellationToken cancellationToken);
    // Own student work + active enrollment/assignment, or authorized teacher access.
    Task EnsureCanPostMessage(Actor actor, ProblemAssignment assignment, CancellationToken cancellationToken);
    // Validate all temporary attachment IDs, ownership, validity and eligibility for linking.
    Task EnsureCanUseAttachments(Actor actor, IReadOnlyList<Guid> ids, CancellationToken cancellationToken);
}
