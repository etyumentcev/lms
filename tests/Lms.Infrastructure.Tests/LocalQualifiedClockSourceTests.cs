using Lms.Application;

namespace Lms.Infrastructure.Tests;

public sealed class LocalQualifiedClockSourceTests
{
    internal static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-11T00:00:00Z");
    internal sealed class Probe : ILocalClockProbe
    {
        public LocalClockObservation? Value = Observation(0);
        public bool Throw;
        public LocalClockObservation? Read() => Throw ? throw new IOException() : Value;
    }
    internal sealed class Verifier : ILocalClockQualificationVerifier
    {
        public bool Current = true, Confirmed = true, Throw;
        public Func<ClockQualificationRequest, ConfirmedClockEnvelope>? Factory;
        public ConfirmedClockEnvelope? Confirm(ClockQualificationRequest r) =>
            Throw ? throw new IOException() : !Confirmed ? null : Factory?.Invoke(r) ?? Envelope(r);
        public bool IsCurrent(ConfirmedClockEnvelope e, LocalClockObservation o) => Throw ? throw new IOException() : Current;
    }
    internal static LocalClockObservation Observation(long ticks) => new("synthetic-linux/tsc/time:1", "boot:1",
        new(At.AddTicks(ticks), At.AddTicks(ticks)), TimeSpan.FromTicks(ticks), TimeSpan.FromTicks(ticks),
        TimeSpan.FromTicks(ticks), TimeSpan.FromTicks(ticks));
    internal static ConfirmedClockEnvelope Envelope(ClockQualificationRequest r) => new(r.Nonce, "synthetic-evidence",
        r.Anchor.WallUtc, 0, TimeSpan.FromHours(1), At.AddHours(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    internal static QualifiedAuthorityClock Clock(IQualifiedClockSource s) => new(s, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

    [Fact]
    public void Untrusted_and_deployment_verifier_never_infer_qualification()
    {
        var source = new LocalQualifiedClockSource(new Probe(), new UnprovenLocalClockVerifier());
        Assert.Equal(ClockQualificationState.Untrusted, source.Snapshot().Status.State);
        Assert.Null(source.Read()); Assert.False(source.Qualify()); Assert.Null(Clock(source).Read());
        Assert.Equal(ClockQualificationState.Untrusted, source.Snapshot().Status.State);
    }

    [Fact]
    public void Interval_age_uncertainty_and_outward_rounding_are_explicit()
    {
        var probe = new Probe();
        var verifier = new Verifier { Factory = r => Envelope(r) with
            { UtcAtAnchor = new(At.AddTicks(-2), At.AddTicks(3)), MaximumRateErrorPpb = 100_000_000 } };
        var source = new LocalQualifiedClockSource(probe, verifier);
        Assert.True(source.Qualify());
        probe.Value = Observation(11);
        var snapshot = source.Snapshot(); var reading = snapshot.Reading!;
        // 11/1.1 = 10; ceil(11/0.9) = 13 ticks; UTC [At+8, At+16].
        Assert.Equal(TimeSpan.FromTicks(10), reading.MonotonicEarliest);
        Assert.Equal(TimeSpan.FromTicks(13), reading.MonotonicLatest);
        Assert.Equal(new AuthorityTime(At.AddTicks(8), At.AddTicks(16)), reading.Utc);
        Assert.Equal(TimeSpan.FromTicks(13), snapshot.Status.AgeUpper);
        Assert.Equal(TimeSpan.FromTicks(8), snapshot.Status.UtcWidth);
        Assert.Equal("synthetic-evidence", snapshot.Status.EvidenceId);
        Assert.Equal("boot:1", snapshot.Status.BootId);
        Assert.Equal(probe.Value.SourceId, snapshot.Status.SourceId);
        Assert.Equal(TimeSpan.FromHours(1), snapshot.Status.MaximumAge);
        Assert.Equal(At.AddHours(2), snapshot.Status.ValidUntil);
    }

    [Theory]
    [InlineData("age")] [InlineData("until")] [InlineData("utc-width")] [InlineData("elapsed-width")]
    public void Exact_qualification_limits_fail_closed(string limit)
    {
        var probe = new Probe(); var verifier = new Verifier { Factory = r => limit switch
        {
            "age" => Envelope(r) with { MaximumAge = TimeSpan.FromTicks(10) },
            "until" => Envelope(r) with { ValidUntil = At.AddTicks(10) },
            "utc-width" => Envelope(r) with { MaximumRateErrorPpb = 100_000_000, MaximumUtcWidth = TimeSpan.FromTicks(2) },
            _ => Envelope(r) with { MaximumRateErrorPpb = 100_000_000, MaximumElapsedWidth = TimeSpan.FromTicks(2) }
        } };
        var source = new LocalQualifiedClockSource(probe, verifier); Assert.True(source.Qualify());
        probe.Value = Observation(9);
        if (limit is "age" or "until") Assert.NotNull(source.Read());
        probe.Value = Observation(10); Assert.Null(source.Read());
        Assert.Equal(ClockQualificationState.Invalidated, source.Snapshot().Status.State);
        probe.Value = Observation(0); Assert.Null(source.Read());
    }

    [Theory]
    [InlineData("forward")] [InlineData("backward")] [InlineData("suspend")]
    [InlineData("reboot")] [InlineData("source")] [InlineData("missing")]
    [InlineData("ambiguous")] [InlineData("regression")] [InlineData("exception")]
    [InlineData("verifier-exception")] [InlineData("lost-trust")]
    [InlineData("inverted-wall")] [InlineData("inverted-mono")] [InlineData("negative-boot")]
    public void Faults_invalidate_active_deadline_and_do_not_auto_recover(string fault)
    {
        var probe = new Probe(); var verifier = new Verifier(); var source = new LocalQualifiedClockSource(probe, verifier);
        Assert.True(source.Qualify()); var clock = Clock(source); var deadline = clock.StartDeadline(TimeSpan.FromSeconds(60))!;
        probe.Value = Observation(100); Assert.NotNull(source.Read());
        var good = Observation(200);
        probe.Value = fault switch
        {
            "forward" => good with { WallUtc = new(At.AddSeconds(1), At.AddSeconds(1)) },
            "backward" => good with { WallUtc = new(At.AddSeconds(-1), At.AddSeconds(-1)) },
            "suspend" => good with { BootLower = TimeSpan.FromSeconds(1), BootUpper = TimeSpan.FromSeconds(1) },
            "reboot" => good with { BootId = "boot:2" },
            "source" => good with { SourceId = "other" },
            "missing" => null,
            "ambiguous" => good with { SourceId = "" },
            "regression" => Observation(99),
            "inverted-wall" => good with { WallUtc = new(At.AddTicks(2), At) },
            "inverted-mono" => good with { MonotonicLower = TimeSpan.FromSeconds(1) },
            "negative-boot" => good with { BootLower = TimeSpan.FromTicks(-1) },
            _ => good
        };
        probe.Throw = fault == "exception"; verifier.Throw = fault == "verifier-exception";
        verifier.Current = fault != "lost-trust";
        Assert.Null(deadline.Remaining()); Assert.Equal(ClockQualificationState.Invalidated, source.Snapshot().Status.State);
        probe.Value = good; probe.Throw = verifier.Throw = false; verifier.Current = true;
        Assert.Null(source.Read()); Assert.Null(clock.Read());
        verifier.Confirmed = false; Assert.False(source.Qualify()); Assert.Null(source.Read());
        verifier.Confirmed = true; Assert.True(source.Qualify()); Assert.NotNull(Clock(source).Read());
        Assert.Null(clock.Read()); Assert.Null(deadline.Remaining());
    }

    [Theory]
    [InlineData("nonce")] [InlineData("unknown")] [InlineData("rate-negative")] [InlineData("rate-one")]
    [InlineData("age-zero")] [InlineData("width-negative")] [InlineData("expired")] [InlineData("inverted")]
    public void Unconfirmed_or_invalid_envelopes_are_rejected(string fault)
    {
        var verifier = new Verifier { Factory = r => fault switch
        {
            "nonce" => Envelope(r) with { RequestNonce = "replayed" },
            "unknown" => Envelope(r) with { EvidenceId = "" },
            "rate-negative" => Envelope(r) with { MaximumRateErrorPpb = -1 },
            "rate-one" => Envelope(r) with { MaximumRateErrorPpb = 1_000_000_000 },
            "age-zero" => Envelope(r) with { MaximumAge = TimeSpan.Zero },
            "width-negative" => Envelope(r) with { MaximumUtcWidth = TimeSpan.FromTicks(-1) },
            "expired" => Envelope(r) with { ValidUntil = At },
            _ => Envelope(r) with { UtcAtAnchor = new(At.AddTicks(1), At) }
        } };
        var source = new LocalQualifiedClockSource(new Probe(), verifier);
        Assert.False(source.Qualify()); Assert.Null(source.Read());
    }

    [Fact]
    public void Requalification_changes_epoch_even_when_reader_missed_invalidation()
    {
        var source = new LocalQualifiedClockSource(new Probe(), new Verifier()); Assert.True(source.Qualify());
        var clock = Clock(source); var deadline = clock.StartDeadline(TimeSpan.FromSeconds(60))!;
        string epoch = source.Read()!.QualificationId;
        source.Invalidate("host-event"); Assert.True(source.Qualify());
        Assert.NotEqual(epoch, source.Read()!.QualificationId);
        Assert.Null(deadline.Remaining()); Assert.NotNull(Clock(source).Read());
    }

    [Fact]
    public void Confirmation_wait_is_not_a_new_anchor_and_cannot_extend_age()
    {
        var probe = new Probe(); var verifier = new Verifier { Factory = r =>
        {
            probe.Value = Observation(TimeSpan.FromSeconds(10).Ticks);
            return Envelope(r) with { MaximumAge = TimeSpan.FromSeconds(10) };
        } };
        var source = new LocalQualifiedClockSource(probe, verifier);
        Assert.False(source.Qualify()); Assert.Equal(ClockQualificationState.Invalidated, source.Snapshot().Status.State);
    }

    [Fact]
    public void Overflow_is_unavailable_not_an_exception_or_allow()
    {
        var probe = new Probe(); var verifier = new Verifier { Factory = r => Envelope(r) with
            { UtcAtAnchor = new(DateTimeOffset.MaxValue.AddTicks(-1), DateTimeOffset.MaxValue.AddTicks(-1)), ValidUntil = DateTimeOffset.MaxValue } };
        var source = new LocalQualifiedClockSource(probe, verifier); Assert.True(source.Qualify());
        probe.Value = Observation(2); Assert.Null(source.Read());
        Assert.Equal(ClockQualificationState.Invalidated, source.Snapshot().Status.State);
    }

    [Fact]
    public async Task Concurrent_readers_observe_atomic_state_and_terminal_old_deadlines()
    {
        var source = new LocalQualifiedClockSource(new Probe(), new Verifier()); Assert.True(source.Qualify());
        var clock = Clock(source); var old = clock.StartDeadline(TimeSpan.FromSeconds(60))!;
        var tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                var s = source.Snapshot();
                Assert.Equal(s.Status.State == ClockQualificationState.Qualified, s.Reading != null);
                if (s.Reading != null) Assert.Equal(s.Status.QualificationId, s.Reading.QualificationId);
            }
        })).ToArray();
        for (int i = 0; i < 20; i++) { source.Invalidate("test"); Assert.True(source.Qualify()); }
        await Task.WhenAll(tasks);
        Assert.Null(old.Remaining()); Assert.NotNull(Clock(source).Read());
    }

    [Fact]
    public void Linux_probe_smoke_is_only_observation_and_never_deployment_qualification()
    {
        var probe = new LinuxLocalClockProbe(); var value = probe.Read();
        // Supported test host must expose the profile. This test fails, rather than
        // silently passing a null observation on an incompatible test environment.
        Assert.NotNull(value); Assert.StartsWith(LinuxLocalClockProbe.Profile, value.SourceId);
        Assert.True(value.WallUtc.Valid); Assert.True(value.MonotonicUpper >= value.MonotonicLower);
        Assert.True(value.BootUpper >= value.BootLower);
        var source = new LocalQualifiedClockSource(probe, new UnprovenLocalClockVerifier());
        Assert.False(source.Qualify()); Assert.Null(source.Read());
    }
}
