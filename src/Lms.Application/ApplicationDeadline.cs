namespace Lms.Application;

public enum ApplicationRequestKind { Ordinary, DesignatedUpload, DesignatedDownload, TeacherOperation }
public enum ApplicationAction { Admission, StartStage, DomainDecision, CommitDispatch, ResponseEnqueue }
public enum ApplicationGateStatus { Allowed, DeadlineExpired, ClockUnqualified, OperationInvalidated, ExternalOutcomeUnknown }
public enum CommitOutcome { NotIssued, Unknown, AcknowledgedCommitted }

// Status is the action decision; Closure and Outcome are independent facts. Neither
// cancellation nor a closed delivery lane proves rollback or physical completion.
public sealed record ApplicationGateDecision(ApplicationGateStatus Status, TimeSpan? Remaining,
    ApplicationGateStatus? Closure, CommitOutcome Outcome)
{
    public bool Allowed => Status == ApplicationGateStatus.Allowed;
}

// Trusted server composition boundary only. Capture once at earliest controlled ingress,
// before queue/header/body, and carry the SAME handle through retry and internal stages.
// Do not bind request kind to client input. There is deliberately no supplied t0/budget.
public abstract class TrustedApplicationIngressAdapter(QualifiedAuthorityClock clock)
{
    protected ApplicationDeadline Capture(ApplicationRequestKind kind, CancellationToken aborted = default)
    {
        var budget = kind switch
        {
            ApplicationRequestKind.TeacherOperation => RevocationPolicy.MaximumOperationLifetime,
            ApplicationRequestKind.Ordinary => TimeSpan.FromSeconds(60),
            ApplicationRequestKind.DesignatedUpload or ApplicationRequestKind.DesignatedDownload => TimeSpan.FromSeconds(480),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return new ApplicationDeadline(clock.StartDeadline(budget), aborted);
    }
}

// Isolated temporal gate, NOT authority, domain authorization or a transaction protocol.
// No timer, cancellation source, background continuation, restart or late-dispatch bypass.
public sealed class ApplicationDeadline
{
    private readonly object sync = new();
    private readonly QualifiedAuthorityClock.Deadline? deadline;
    private readonly CancellationToken aborted;
    private ApplicationGateStatus? closure;
    private CommitOutcome outcome;

    internal ApplicationDeadline(QualifiedAuthorityClock.Deadline? deadline, CancellationToken aborted)
    {
        this.deadline = deadline;
        this.aborted = aborted;
        if (deadline == null) closure = ApplicationGateStatus.ClockUnqualified;
    }

    // Persist only this original conservative cutoff; a retry must never capture a replacement.
    public DateTimeOffset? ExpiresAt => deadline?.UtcCutoff;

    public void Invalidate()
    {
        lock (sync) closure ??= ApplicationGateStatus.OperationInvalidated;
    }

    // An Allowed result is an observation, never a reusable permit. Call again after
    // waits/locks and immediately before dispatch/enqueue. U<exp and U<D remain the
    // existing authority contract; domain/processing checks must share the q transaction.
    public ApplicationGateDecision Check(ApplicationAction action)
    {
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        lock (sync) return CheckLocked(action);
    }

    private ApplicationGateDecision CheckLocked(ApplicationAction action)
    {
        TimeSpan? remaining = null;
        if (aborted.IsCancellationRequested) closure ??= ApplicationGateStatus.OperationInvalidated;
        if (closure == null)
        {
            remaining = deadline!.Remaining();
            if (remaining == null) closure = ApplicationGateStatus.ClockUnqualified;
            else if (remaining <= TimeSpan.Zero) closure = ApplicationGateStatus.DeadlineExpired;
        }
        if (closure == ApplicationGateStatus.DeadlineExpired) remaining = TimeSpan.Zero;
        var status = outcome == CommitOutcome.Unknown ? ApplicationGateStatus.ExternalOutcomeUnknown
            : closure ?? (outcome != CommitOutcome.NotIssued && action != ApplicationAction.ResponseEnqueue
                ? ApplicationGateStatus.OperationInvalidated : ApplicationGateStatus.Allowed);
        return new(status, remaining, closure, outcome);
    }

    // dispatch must synchronously hand the one already fixed atomic set to its transport;
    // it must not queue a future task, retry, mutate or authorize. This is NOT q. A fresh
    // check here closes a pause after an earlier advisory check. A scheduler pause after
    // this final check remains a deployment obligation, not an atomic clock/send promise.
    // The returned Allowed means ONLY that dispatch was invoked. Exceptions leave Unknown.
    public ApplicationGateDecision DispatchCommit(Action dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ApplicationGateDecision decision;
        lock (sync)
        {
            decision = CheckLocked(ApplicationAction.CommitDispatch);
            if (!decision.Allowed) return decision;
            outcome = CommitOutcome.Unknown;
        }
        // Never hold the gate lock across external I/O: invalidation stays observable.
        dispatch();
        return decision;
    }

    // Trusted transport ACK only, not inferred from timeout/cancellation/SELECT-not-found.
    // Records an external fact even after closure; never reopens the operation. The
    // caller must use ResponseEnqueue for success/JWT/cookie after ACK. NR1 late visibility
    // eligibility is route-specific; this method grants no exception to issuance/grants.
    public void AcknowledgeCommit()
    {
        lock (sync)
        {
            if (outcome == CommitOutcome.NotIssued)
                throw new InvalidOperationException("No COMMIT dispatch was attempted.");
            outcome = CommitOutcome.AcknowledgedCommitted;
        }
    }
}
