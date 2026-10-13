using Lms.Domain;

namespace Lms.Application.Tests;

public sealed class TeacherOperationContextTests
{
    private sealed class Source : IQualifiedClockSource
    {
        public TimeSpan Elapsed;
        public QualifiedClockReading Read() => new("context-test", true,
            new(DateTimeOffset.UnixEpoch + Elapsed, DateTimeOffset.UnixEpoch + Elapsed), Elapsed, Elapsed);
    }
    private sealed class Ingress(Source source) : TrustedApplicationIngressAdapter(new(source, TimeSpan.Zero, TimeSpan.Zero))
    { public ApplicationDeadline Arrive() => Capture(ApplicationRequestKind.TeacherOperation); }
    private sealed class NoEffects : IWorkStore, IAccessPolicy
    {
        public Task<ProblemAssignment?> FindAssignment(Guid id, CancellationToken ct) => throw new Exception("Unexpected read");
        public Task<ProblemWork?> FindWork(Guid id, CancellationToken ct) => throw new Exception("Unexpected read");
        public Task<ProblemWork?> FindByAssignment(Guid id, CancellationToken ct) => throw new Exception("Unexpected read");
        public Task Save(ProblemWork work, long? version, CancellationToken ct) => throw new Exception("Unexpected effect");
        public Task EnsureCanSubmit(Actor a, ProblemAssignment p, CancellationToken ct) => throw new Exception("Unexpected policy");
        public Task EnsureCanReview(Actor a, ProblemAssignment p, CancellationToken ct) => throw new Exception("Unexpected policy");
        public Task EnsureCanPostMessage(Actor a, ProblemAssignment p, CancellationToken ct) => throw new Exception("Unexpected policy");
        public Task EnsureCanUseAttachments(Actor a, IReadOnlyList<Guid> ids, CancellationToken ct) => throw new Exception("Unexpected policy");
    }
    [Theory]
    [InlineData("review")][InlineData("revise")][InlineData("message")]
    public async Task Missing_context_denies_before_any_dependency(string command)
    {
        var dependencies = new NoEffects(); var service = new ProblemWorkService(dependencies, dependencies, TimeProvider.System);
        var actor = new Actor(Guid.NewGuid(), AuthorRole.Teacher); var work = Guid.NewGuid();
        Task Call() => command switch {
            "review" => service.AcceptSubmission(actor, work, Guid.NewGuid(), null, 1),
            "revise" => service.RequestRevision(actor, work, Guid.NewGuid(), "feedback", 1),
            _ => service.PostMessage(actor, work, "message", null, [])
        };
        Assert.Equal("Forbidden", (await Assert.ThrowsAsync<UseCaseException>(Call)).Code);
    }
    [Theory]
    [InlineData("actor")][InlineData("work")][InlineData("kind")][InlineData("closed")][InlineData("expired")][InlineData("reused")]
    public async Task Invalid_context_denies_before_any_dependency(string denial)
    {
        var source = new Source(); var actor = new Actor(Guid.NewGuid(), AuthorRole.Teacher); var work = Guid.NewGuid();
        using var operation = new TeacherOperationContext(actor.Id, work, "Review", new Ingress(source).Arrive());
        if (denial == "closed") operation.Dispose();
        if (denial == "expired") source.Elapsed = TimeSpan.FromMinutes(30);
        if (denial == "reused") operation.Enter(actor, work, "Review");
        var dependencies = new NoEffects(); var service = new ProblemWorkService(dependencies, dependencies, TimeProvider.System, operation);
        Task Call() => denial == "kind" ? service.PostMessage(actor, work, "message", null, []) :
            service.AcceptSubmission(denial == "actor" ? new(Guid.NewGuid(), AuthorRole.Teacher) : actor,
                denial == "work" ? Guid.NewGuid() : work, Guid.NewGuid(), null, 1);
        if (denial == "expired") await Assert.ThrowsAsync<OperationCanceledException>(Call);
        else Assert.Equal("Forbidden", (await Assert.ThrowsAsync<UseCaseException>(Call)).Code);
    }
}
