using Lms.Domain;

namespace Lms.Application;

public sealed record SubmissionResult(Guid ProblemWorkId, Guid SubmissionId, int AttemptNumber, ProblemWorkStatus Status, long Version);
public sealed record ReviewResult(Guid ReviewId, Guid? MessageId, ProblemWorkStatus Status, long Version);
public sealed record MessageResult(Guid MessageId, DateTimeOffset CreatedAt);

public sealed class ProblemWorkService(IWorkStore store, IAccessPolicy access, TimeProvider time)
{
    public async Task<SubmissionResult> SubmitSolution(Actor actor, Guid assignmentId, string? body,
        IReadOnlyList<Guid> attachmentIds, long? expectedVersion, CancellationToken cancellationToken = default)
    {
        EnsureRole(actor, AuthorRole.Student);
        var assignment = await Assignment(assignmentId, cancellationToken);
        assignment.EnsureCanSubmit(actor.Id);
        await access.EnsureCanSubmit(actor, assignment, cancellationToken);
        var ids = attachmentIds.ToArray();
        await access.EnsureCanUseAttachments(actor, ids, cancellationToken);
        var work = await store.FindByAssignment(assignmentId, cancellationToken);
        if (work is null)
        {
            if (expectedVersion.HasValue) throw new UseCaseException("ConcurrencyConflict");
            work = new ProblemWork(Guid.NewGuid(), assignment);
        }
        else CheckVersion(work, expectedVersion);
        var submission = work.SubmitSolution(assignment, actor.Id, body, ids, time.GetUtcNow());
        await store.Save(work, expectedVersion, cancellationToken);
        return new(work.Id, submission.Id, submission.AttemptNumber, work.Status, work.Version);
    }

    public Task<ReviewResult> RequestRevision(Actor actor, Guid workId, Guid submissionId,
        string feedback, long expectedVersion, CancellationToken cancellationToken = default)
        => Review(actor, workId, submissionId, feedback, expectedVersion, true, cancellationToken);

    public Task<ReviewResult> AcceptSubmission(Actor actor, Guid workId, Guid submissionId,
        string? feedback, long expectedVersion, CancellationToken cancellationToken = default)
        => Review(actor, workId, submissionId, feedback, expectedVersion, false, cancellationToken);

    private async Task<ReviewResult> Review(Actor actor, Guid workId, Guid submissionId,
        string? feedback, long expectedVersion, bool revision, CancellationToken cancellationToken)
    {
        EnsureRole(actor, AuthorRole.Teacher);
        var work = await Work(workId, cancellationToken);
        var assignment = await Assignment(work.ProblemAssignmentId, cancellationToken);
        await access.EnsureCanReview(actor, assignment, cancellationToken);
        CheckVersion(work, expectedVersion);
        if (revision && string.IsNullOrWhiteSpace(feedback)) throw new UseCaseException("FeedbackRequired");
        var review = revision
            ? work.RequestRevision(actor.Id, submissionId, feedback, time.GetUtcNow())
            : work.Accept(actor.Id, submissionId, feedback, time.GetUtcNow());
        await store.Save(work, expectedVersion, cancellationToken);
        return new(review.Id, review.MessageId, work.Status, work.Version);
    }

    public async Task<MessageResult> PostMessage(Actor actor, Guid workId, string body,
        Guid? replyTo, IReadOnlyList<Guid> attachmentIds, CancellationToken cancellationToken = default)
    {
        if (actor.Id == Guid.Empty || !Enum.IsDefined(actor.Role)) throw new UseCaseException("Unauthenticated");
        var work = await Work(workId, cancellationToken);
        var assignment = await Assignment(work.ProblemAssignmentId, cancellationToken);
        if (actor.Role == AuthorRole.Student) assignment.EnsureCanSubmit(actor.Id);
        await access.EnsureCanPostMessage(actor, assignment, cancellationToken);
        var ids = attachmentIds.ToArray();
        await access.EnsureCanUseAttachments(actor, ids, cancellationToken);
        var version = work.Version;
        var message = work.PostMessage(actor.Id, actor.Role, body, replyTo, ids, time.GetUtcNow());
        await store.Save(work, version, cancellationToken);
        return new(message.Id, message.CreatedAt);
    }

    private async Task<ProblemAssignment> Assignment(Guid id, CancellationToken cancellationToken)
        => await store.FindAssignment(id, cancellationToken) ?? throw new UseCaseException("AssignmentNotFound");
    private async Task<ProblemWork> Work(Guid id, CancellationToken cancellationToken)
        => await store.FindWork(id, cancellationToken) ?? throw new UseCaseException("ProblemWorkNotFound");
    private static void CheckVersion(ProblemWork work, long? version)
    {
        if (version != work.Version) throw new UseCaseException("ConcurrencyConflict");
    }
    private static void EnsureRole(Actor actor, AuthorRole role)
    {
        if (actor.Id == Guid.Empty) throw new UseCaseException("Unauthenticated");
        if (actor.Role != role) throw new UseCaseException(role == AuthorRole.Teacher ? "TeacherNotAuthorized" : "StudentNotAuthorized");
    }
}
