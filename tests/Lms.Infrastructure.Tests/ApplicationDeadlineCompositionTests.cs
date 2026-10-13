using Lms.Application;
using static Lms.Infrastructure.Tests.LocalQualifiedClockSourceTests;

namespace Lms.Infrastructure.Tests;

public sealed class ApplicationDeadlineCompositionTests
{
    private sealed class SyntheticIngress(QualifiedAuthorityClock clock) : TrustedApplicationIngressAdapter(clock)
    {
        public ApplicationDeadline Arrive(ApplicationRequestKind kind) => Capture(kind);
    }

    [Theory]
    [InlineData(ApplicationRequestKind.Ordinary, 60)]
    [InlineData(ApplicationRequestKind.DesignatedUpload, 480)]
    [InlineData(ApplicationRequestKind.DesignatedDownload, 480)]
    public void Clock01_qualified_source_composes_with_same_application_deadline(ApplicationRequestKind kind, int budget)
    {
        var probe = new Probe(); var source = new LocalQualifiedClockSource(probe, new Verifier());
        Assert.True(source.Qualify());
        var operation = new SyntheticIngress(Clock(source)).Arrive(kind);
        probe.Value = Observation(TimeSpan.FromSeconds(30).Ticks);
        var admitted = operation.Check(ApplicationAction.Admission);
        Assert.True(admitted.Allowed); Assert.Equal(TimeSpan.FromSeconds(budget - 30), admitted.Remaining);
        probe.Value = Observation(TimeSpan.FromSeconds(budget).Ticks - 1);
        Assert.True(operation.Check(ApplicationAction.DomainDecision).Allowed);
        probe.Value = Observation(TimeSpan.FromSeconds(budget).Ticks);
        Assert.Equal(ApplicationGateStatus.DeadlineExpired, operation.Check(ApplicationAction.CommitDispatch).Status);
        Assert.False(operation.DispatchCommit(() => throw new Exception("Expired dispatch")).Allowed);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void Requalification_does_not_revive_old_handle_even_without_intermediate_read(bool sampleInvalidation)
    {
        var source = new LocalQualifiedClockSource(new Probe(), new Verifier()); Assert.True(source.Qualify());
        var operation = new SyntheticIngress(Clock(source)).Arrive(ApplicationRequestKind.Ordinary);
        source.Invalidate("synthetic-discontinuity");
        if (sampleInvalidation) Assert.Equal(ApplicationGateStatus.ClockUnqualified, operation.Check(ApplicationAction.StartStage).Status);
        Assert.True(source.Qualify());
        Assert.Equal(ApplicationGateStatus.ClockUnqualified, operation.Check(ApplicationAction.StartStage).Status);
        var fresh = new SyntheticIngress(Clock(source)).Arrive(ApplicationRequestKind.Ordinary);
        Assert.True(fresh.Check(ApplicationAction.Admission).Allowed);
    }

    [Fact]
    public void Unproven_deployment_verifier_cannot_admit_application_actions()
    {
        var source = new LocalQualifiedClockSource(new Probe(), new UnprovenLocalClockVerifier());
        Assert.False(source.Qualify());
        var operation = new SyntheticIngress(Clock(source)).Arrive(ApplicationRequestKind.Ordinary);
        foreach (var action in Enum.GetValues<ApplicationAction>())
            Assert.Equal(ApplicationGateStatus.ClockUnqualified, operation.Check(action).Status);
    }
}
