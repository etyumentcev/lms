using Lms.Application;

namespace Lms.Application.Tests;

public sealed class BoundedExecutionTests
{
    private sealed class Time : TimeProvider, IQualifiedClockSource
    {
        private static readonly DateTimeOffset At = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
        public TimeSpan Elapsed;
        private readonly List<Timer> timers = [];
        public QualifiedClockReading Read() => new("test", true, new(At + Elapsed, At + Elapsed), Elapsed, Elapsed);
        public override DateTimeOffset GetUtcNow() => At + Elapsed;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { var timer = new Timer(this, callback, state); timers.Add(timer); timer.Change(dueTime, period); return timer; }
        public void Advance(TimeSpan elapsed)
        { Elapsed += elapsed; foreach (var timer in timers.ToArray()) timer.Fire(); }
        private sealed class Timer(Time owner, TimerCallback callback, object? state) : ITimer
        {
            private TimeSpan? due;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            { due = dueTime == Timeout.InfiniteTimeSpan ? null : owner.Elapsed + dueTime; return true; }
            public void Fire() { if (due <= owner.Elapsed) { due = null; callback(state); } }
            public void Dispose() => due = null;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    private sealed class Ingress(QualifiedAuthorityClock clock) : TrustedApplicationIngressAdapter(clock)
    { public ApplicationDeadline Arrive() => Capture(ApplicationRequestKind.TeacherOperation); }
    private static ApplicationDeadline Start(Time time) => new Ingress(new(time, TimeSpan.Zero, TimeSpan.Zero)).Arrive();

    [Fact]
    public async Task Cooperative_executor_is_joined_at_thirty_minutes_and_cannot_publish_success()
    {
        var time = new Time(); var deadline = Start(time); var published = false;
        using var cancel = new DeadlineCancellation(deadline, time);
        async Task Execute()
        { await Task.Delay(Timeout.InfiniteTimeSpan, cancel.Token); published = true; }
        var execution = Execute(); time.Advance(TimeSpan.FromMinutes(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        Assert.True(execution.IsCompleted); Assert.False(published);
        Assert.False(deadline.Check(ApplicationAction.ResponseEnqueue).Allowed);
    }
    [Fact]
    public async Task Cancellation_of_noncooperative_work_is_not_reported_as_physical_completion()
    {
        var time = new Time(); var deadline = Start(time); using var cancel = new DeadlineCancellation(deadline, time);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Execute() { await release.Task; return deadline.Check(ApplicationAction.ResponseEnqueue).Allowed; }
        var execution = Execute(); time.Advance(TimeSpan.FromMinutes(30));
        Assert.True(cancel.Token.IsCancellationRequested); Assert.False(execution.IsCompleted);
        release.SetResult(); Assert.False(await execution); // join the executor; no orphan task
    }
    [Fact]
    public void Creating_cancellation_after_wait_uses_only_the_original_remaining_budget()
    {
        var time = new Time(); var deadline = Start(time); var cutoff = deadline.ExpiresAt;
        time.Advance(TimeSpan.FromMinutes(29)); using var cancel = new DeadlineCancellation(deadline, time);
        Assert.False(cancel.Token.IsCancellationRequested);
        time.Advance(TimeSpan.FromMinutes(1)); Assert.True(cancel.Token.IsCancellationRequested);
        Assert.Equal(cutoff, deadline.ExpiresAt);
        Assert.Equal(TimeSpan.FromMinutes(45), RevocationPolicy.LatestOldAuthorityCompletion);
        Assert.True(RevocationPolicy.LatestOldAuthorityCompletion < RevocationPolicy.MaximumRevocationDelay);
    }
    [Fact]
    public async Task Bounded_teardown_reserve_is_inside_thirty_minutes_not_an_extra_tail()
    {
        var time = new Time(); var deadline = Start(time); using var cancel = new DeadlineCancellation(deadline, time);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Execute()
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancel.Token); }
            finally { await release.Task; }
        }
        var execution = Execute();
        time.Advance(TimeSpan.FromMinutes(29) + TimeSpan.FromSeconds(30));
        Assert.True(cancel.Token.IsCancellationRequested);
        Assert.False(deadline.Check(ApplicationAction.ResponseEnqueue).Allowed);
        Assert.False(execution.IsCompleted); // cancellation has NOT yet finished teardown
        time.Advance(TimeSpan.FromSeconds(30)); release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        Assert.True(execution.IsCompleted); Assert.Equal(TimeSpan.FromMinutes(30), time.Elapsed);
    }

}
