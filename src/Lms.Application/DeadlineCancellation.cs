namespace Lms.Application;

// Cooperative cancellation inside the documented bounded execution envelope. Disposal
// is not completion evidence: the caller still awaits its executor and releases resources.
public sealed class DeadlineCancellation : IDisposable
{
    private readonly CancellationTokenSource timer;
    private readonly CancellationTokenSource linked;
    private readonly CancellationTokenRegistration registration;
    public CancellationToken Token => linked.Token;
    public DeadlineCancellation(ApplicationDeadline deadline, TimeProvider time, CancellationToken aborted = default)
    {
        var gate = deadline.Check(ApplicationAction.StartStage);
        var remaining = gate.Allowed ? gate.Remaining!.Value - RevocationPolicy.CompletionReserve : TimeSpan.Zero;
        timer = new CancellationTokenSource(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, time);
        if (remaining <= TimeSpan.Zero) timer.Cancel();
        linked = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, aborted);
        registration = linked.Token.Register(deadline.Invalidate);
    }
    public void Dispose() { registration.Dispose(); linked.Dispose(); timer.Dispose(); }
}
