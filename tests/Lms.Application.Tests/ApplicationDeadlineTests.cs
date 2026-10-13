using Lms.Application;

namespace Lms.Application.Tests;

public sealed class ApplicationDeadlineTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
    private sealed class Source : IQualifiedClockSource
    {
        public long Ticks;
        public long Width;
        public string Epoch = "synthetic-epoch";
        public bool Unknown;
        public bool Fail;
        public QualifiedClockReading? Read()
        {
            if (Fail) throw new IOException("Synthetic source failure");
            if (Unknown) return null;
            var t = Interlocked.Read(ref Ticks);
            return new(Epoch, true, new(At.AddTicks(t), At.AddTicks(t + Width)),
                TimeSpan.FromTicks(t), TimeSpan.FromTicks(t + Width));
        }
        public void Advance(TimeSpan time) => Interlocked.Add(ref Ticks, time.Ticks);
    }
    private sealed class SyntheticIngress(QualifiedAuthorityClock clock) : TrustedApplicationIngressAdapter(clock)
    {
        public ApplicationDeadline Arrive(ApplicationRequestKind kind = ApplicationRequestKind.Ordinary,
            CancellationToken aborted = default) => Capture(kind, aborted);
    }
    private static SyntheticIngress Ingress(Source source) => new(new(source, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
    private static ApplicationGateDecision Check(ApplicationDeadline d) => d.Check(ApplicationAction.StartStage);

    [Theory]
    [InlineData(ApplicationRequestKind.TeacherOperation, 1800)]
    [InlineData(ApplicationRequestKind.Ordinary, 60)]
    [InlineData(ApplicationRequestKind.DesignatedUpload, 480)]
    [InlineData(ApplicationRequestKind.DesignatedDownload, 480)]
    public void Original_ingress_budget_covers_queue_body_retry_and_stages(ApplicationRequestKind kind, int seconds)
    {
        var source = new Source(); var operation = Ingress(source).Arrive(kind);
        Assert.Equal(TimeSpan.FromSeconds(seconds), Check(operation).Remaining);
        source.Advance(TimeSpan.FromSeconds(20)); // queue before header/body/admission
        source.Advance(TimeSpan.FromSeconds(10)); // body
        Assert.Equal(TimeSpan.FromSeconds(seconds - 30), operation.Check(ApplicationAction.Admission).Remaining);
        source.Advance(TimeSpan.FromSeconds(5));
        foreach (var action in Enum.GetValues<ApplicationAction>())
            Assert.Equal(TimeSpan.FromSeconds(seconds - 35), operation.Check(action).Remaining);
        for (var retry = 0; retry < 3; retry++)
        {
            source.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(TimeSpan.FromSeconds(seconds - 36 - retry), Check(operation).Remaining);
        }
    }

    public static IEnumerable<object[]> Boundaries() =>
        from kind in Enum.GetValues<ApplicationRequestKind>()
        from action in Enum.GetValues<ApplicationAction>()
        from offset in new[] { -1L, 0L, 1L }
        select new object[] { kind, action, offset };

    [Theory] [MemberData(nameof(Boundaries))]
    public void All_action_boundaries_are_strict(ApplicationRequestKind kind, ApplicationAction action, long offset)
    {
        var source = new Source(); var operation = Ingress(source).Arrive(kind);
        source.Ticks = TimeSpan.FromSeconds(kind == ApplicationRequestKind.TeacherOperation ? 1800 : kind == ApplicationRequestKind.Ordinary ? 60 : 480).Ticks + offset;
        var result = operation.Check(action);
        Assert.Equal(offset < 0 ? ApplicationGateStatus.Allowed : ApplicationGateStatus.DeadlineExpired, result.Status);
        Assert.Equal(TimeSpan.FromTicks(offset < 0 ? 1 : 0), result.Remaining);
    }

    [Fact]
    public void Conservative_upper_endpoint_denies_before_nominal_deadline()
    {
        var source = new Source { Width = 10 }; var operation = Ingress(source).Arrive();
        Assert.Equal(TimeSpan.FromSeconds(60) - TimeSpan.FromTicks(10), Check(operation).Remaining);
        source.Ticks = TimeSpan.FromSeconds(60).Ticks - 10;
        Assert.Equal(ApplicationGateStatus.DeadlineExpired, Check(operation).Status);
    }

    [Fact]
    public void Queue_can_exhaust_entire_budget_before_admission()
    {
        var source = new Source(); var operation = Ingress(source).Arrive();
        source.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(ApplicationGateStatus.DeadlineExpired, operation.Check(ApplicationAction.Admission).Status);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void Unknown_or_source_failure_latches_closed(bool throws)
    {
        var source = new Source(); var operation = Ingress(source).Arrive();
        Assert.True(Check(operation).Allowed);
        source.Fail = throws; source.Unknown = !throws;
        Assert.Equal(ApplicationGateStatus.ClockUnqualified, Check(operation).Status);
        source.Fail = source.Unknown = false;
        Assert.Equal(ApplicationGateStatus.ClockUnqualified, Check(operation).Status);
    }

    [Fact]
    public void Unknown_at_ingress_cannot_be_repaired_by_later_source_recovery()
    {
        var source = new Source { Unknown = true }; var operation = Ingress(source).Arrive();
        source.Unknown = false;
        Assert.Equal(ApplicationGateStatus.ClockUnqualified, Check(operation).Status);
    }

    [Fact]
    public void Epoch_change_closes_old_operation_even_when_new_clock_is_qualified()
    {
        var source = new Source(); var operation = Ingress(source).Arrive();
        source.Epoch = "new-epoch";
        Assert.Equal(ApplicationGateStatus.ClockUnqualified, Check(operation).Status);
        Assert.True(Check(Ingress(source).Arrive()).Allowed);
        Assert.Equal(ApplicationGateStatus.ClockUnqualified, Check(operation).Status);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void Explicit_or_existing_cancellation_invalidates_after_allowed_stage(bool cancellation)
    {
        var source = new Source(); using var aborted = new CancellationTokenSource();
        var operation = Ingress(source).Arrive(aborted: aborted.Token);
        Assert.True(Check(operation).Allowed);
        if (cancellation) aborted.Cancel(); else operation.Invalidate();
        foreach (var action in Enum.GetValues<ApplicationAction>())
            Assert.Equal(ApplicationGateStatus.OperationInvalidated, operation.Check(action).Status);
        Assert.Equal(CommitOutcome.NotIssued, Check(operation).Outcome);
    }

    [Fact]
    public async Task Concurrent_reads_of_same_handle_are_monotone_and_terminal()
    {
        var source = new Source(); var operation = Ingress(source).Arrive();
        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            var previous = TimeSpan.FromSeconds(60);
            for (var i = 0; i < 100; i++)
            {
                source.Advance(TimeSpan.FromMilliseconds(50));
                var result = Check(operation);
                Assert.NotNull(result.Remaining);
                Assert.True(result.Remaining <= previous);
                previous = result.Remaining.Value;
            }
        })));
        Assert.Equal(ApplicationGateStatus.DeadlineExpired, Check(operation).Status);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void Pause_after_advisory_check_cannot_reuse_permission_at_dispatch(bool invalidate)
    {
        var source = new Source(); var operation = Ingress(source).Arrive();
        Assert.True(operation.Check(ApplicationAction.CommitDispatch).Allowed);
        if (invalidate) operation.Invalidate(); else source.Advance(TimeSpan.FromSeconds(60));
        var invoked = false;
        var result = operation.DispatchCommit(() => invoked = true);
        Assert.False(result.Allowed); Assert.False(invoked);
        Assert.Equal(CommitOutcome.NotIssued, result.Outcome);
    }

    [Fact]
    public void Lost_ack_is_unknown_not_rollback_and_never_replays()
    {
        var source = new Source(); var operation = Ingress(source).Arrive(); var sends = 0;
        Assert.True(operation.Check(ApplicationAction.DomainDecision).Allowed);
        Assert.Throws<IOException>(() => operation.DispatchCommit(() => { sends++; throw new IOException(); }));
        operation.Invalidate();
        foreach (var action in Enum.GetValues<ApplicationAction>())
        {
            var result = operation.Check(action);
            Assert.Equal(ApplicationGateStatus.ExternalOutcomeUnknown, result.Status);
            Assert.Equal(ApplicationGateStatus.OperationInvalidated, result.Closure);
            Assert.Equal(CommitOutcome.Unknown, result.Outcome);
        }
        Assert.False(operation.DispatchCommit(() => sends++).Allowed);
        Assert.Equal(1, sends);
    }

    [Fact]
    public void Pause_inside_dispatch_can_finish_late_but_ack_cannot_reopen_response()
    {
        var source = new Source(); var operation = Ingress(source).Arrive();
        Assert.True(operation.Check(ApplicationAction.DomainDecision).Allowed);
        Assert.True(operation.DispatchCommit(() => source.Advance(TimeSpan.FromSeconds(61))).Allowed);
        var pending = operation.Check(ApplicationAction.ResponseEnqueue);
        Assert.Equal(ApplicationGateStatus.ExternalOutcomeUnknown, pending.Status);
        Assert.Equal(ApplicationGateStatus.DeadlineExpired, pending.Closure);
        operation.AcknowledgeCommit();
        var afterAck = operation.Check(ApplicationAction.ResponseEnqueue);
        Assert.Equal(CommitOutcome.AcknowledgedCommitted, afterAck.Outcome);
        Assert.Equal(ApplicationGateStatus.DeadlineExpired, afterAck.Status);
        Assert.False(operation.Check(ApplicationAction.DomainDecision).Allowed);
    }

    [Fact]
    public void Timely_ack_allows_response_but_never_another_mutation_or_dispatch()
    {
        var source = new Source(); var operation = Ingress(source).Arrive();
        Assert.True(operation.DispatchCommit(() => { }).Allowed);
        Assert.False(operation.Check(ApplicationAction.ResponseEnqueue).Allowed);
        operation.AcknowledgeCommit();
        Assert.True(operation.Check(ApplicationAction.ResponseEnqueue).Allowed);
        Assert.False(operation.Check(ApplicationAction.DomainDecision).Allowed);
        Assert.False(operation.DispatchCommit(() => throw new Exception("replay")).Allowed);
        source.Advance(TimeSpan.FromSeconds(60));
        // Same enqueue gate is required for success body, JWT, cookie and every stream chunk.
        Assert.Equal(ApplicationGateStatus.DeadlineExpired, operation.Check(ApplicationAction.ResponseEnqueue).Status);
    }

    [Fact]
    public async Task Concurrent_dispatch_has_one_winner_and_invalidation_does_not_wait_for_io()
    {
        var source = new Source(); var operation = Ingress(source).Arrive(); var sends = 0;
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var first = Task.Run(() => operation.DispatchCommit(() =>
        {
            Interlocked.Increment(ref sends); entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
        }));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            var attempts = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
                operation.DispatchCommit(() => Interlocked.Increment(ref sends)))));
            Assert.All(attempts, result => Assert.Equal(ApplicationGateStatus.ExternalOutcomeUnknown, result.Status));
            operation.Invalidate();
            Assert.Equal(ApplicationGateStatus.OperationInvalidated, Check(operation).Closure);
        }
        finally { release.Set(); }
        Assert.True((await first).Allowed); Assert.Equal(1, sends);
        operation.AcknowledgeCommit();
        Assert.False(operation.Check(ApplicationAction.ResponseEnqueue).Allowed);
    }

    [Theory]
    [InlineData(ApplicationAction.DomainDecision)]
    [InlineData(ApplicationAction.ResponseEnqueue)]
    public void Wait_after_allowed_check_requires_fresh_gate(ApplicationAction action)
    {
        var source = new Source(); var operation = Ingress(source).Arrive();
        Assert.True(operation.Check(action).Allowed);
        source.Advance(TimeSpan.FromSeconds(60)); // lock wait or pause before enqueue
        Assert.Equal(ApplicationGateStatus.DeadlineExpired, operation.Check(action).Status);
    }

    [Fact]
    public void Late_ack_without_intermediate_poll_still_denies_success_and_credentials()
    {
        var source = new Source(); var operation = Ingress(source).Arrive();
        operation.DispatchCommit(() => { });
        source.Advance(TimeSpan.FromSeconds(60));
        operation.AcknowledgeCommit();
        Assert.Equal(ApplicationGateStatus.DeadlineExpired, operation.Check(ApplicationAction.ResponseEnqueue).Status);
    }

    [Fact]
    public void Unknown_commit_and_clock_failure_remain_independent_after_ack()
    {
        var source = new Source(); var operation = Ingress(source).Arrive();
        operation.DispatchCommit(() => { }); source.Fail = true;
        var pending = operation.Check(ApplicationAction.ResponseEnqueue);
        Assert.Equal(ApplicationGateStatus.ExternalOutcomeUnknown, pending.Status);
        Assert.Equal(ApplicationGateStatus.ClockUnqualified, pending.Closure);
        operation.AcknowledgeCommit(); source.Fail = false;
        Assert.Equal(ApplicationGateStatus.ClockUnqualified, operation.Check(ApplicationAction.ResponseEnqueue).Status);
    }

    [Fact]
    public void Invalid_contract_values_and_ack_before_dispatch_are_rejected()
    {
        var ingress = Ingress(new Source()); var operation = ingress.Arrive();
        Assert.Throws<ArgumentOutOfRangeException>(() => ingress.Arrive((ApplicationRequestKind)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => operation.Check((ApplicationAction)999));
        Assert.Throws<InvalidOperationException>(operation.AcknowledgeCommit);
        Assert.Throws<ArgumentNullException>(() => operation.DispatchCommit(null!));
        Assert.True(Check(operation).Allowed);
    }
}
