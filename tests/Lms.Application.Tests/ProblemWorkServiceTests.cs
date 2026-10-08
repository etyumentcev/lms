using Lms.Domain;

namespace Lms.Application.Tests;

public sealed class ProblemWorkServiceTests
{
    [Fact]
    public async Task Annotation_validation_precedes_save_and_review_uses_one_save()
    {
        var f = new Fixture(); var attachment = Guid.NewGuid();
        var first = await f.Service.SubmitSolution(f.Student, f.Assignment.Id, null, [attachment], null);
        var pages = new[] { new AnnotatedAttachment(Guid.NewGuid(), [new(Guid.NewGuid(), "pen", "#dc2626", .003, [new(.2, .3)])]) };
        await Assert.ThrowsAsync<DomainException>(() => f.Service.AcceptSubmission(f.Teacher, first.ProblemWorkId, first.SubmissionId, null, first.Version, annotations: pages));
        Assert.Equal(1, f.Store.SaveCalls); Assert.Empty(f.Store.Work!.Reviews);
        pages[0] = pages[0] with { AttachmentId = attachment };
        await f.Service.AcceptSubmission(f.Teacher, first.ProblemWorkId, first.SubmissionId, null, first.Version, annotations: pages);
        Assert.Equal(2, f.Store.SaveCalls); Assert.Single(f.Store.Work.Reviews[0].Annotations);
    }

    [Fact]
    public async Task Full_slice_uses_same_work_and_expected_versions()
    {
        var f = new Fixture();
        var first = await f.Service.SubmitSolution(f.Student, f.Assignment.Id, null, [Guid.NewGuid()], null);
        Assert.Equal(1, first.AttemptNumber);
        Assert.Equal(1, f.Policy.SubmitChecks);
        Assert.Equal(1, f.Policy.AttachmentChecks);
        var revision = await f.Service.RequestRevision(f.Teacher, first.ProblemWorkId, first.SubmissionId, "explain", first.Version);
        Assert.Equal(ProblemWorkStatus.NeedsRevision, revision.Status);
        var second = await f.Service.SubmitSolution(f.Student, f.Assignment.Id, "revised", [], revision.Version);
        var accepted = await f.Service.AcceptSubmission(f.Teacher, second.ProblemWorkId, second.SubmissionId, null, second.Version);
        Assert.Equal(ProblemWorkStatus.Accepted, accepted.Status);
        Assert.Equal(first.ProblemWorkId, second.ProblemWorkId);
        Assert.Equal(2, second.AttemptNumber);
        Assert.Equal(new long?[] { null, 1, 2, 3 }, f.Store.ExpectedVersions);
        Assert.Equal(2, f.Policy.ReviewChecks);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public async Task Revision_feedback_is_validated_before_domain_mutation_or_save(string? feedback)
    {
        var f = new Fixture();
        var first = await f.Submit();
        var work = f.Store.Work!;
        var submissions = work.Submissions.ToArray();
        var events = work.Events.ToArray();
        // Deliberately exercise runtime null despite the non-nullable command contract.
        await Error("FeedbackRequired", () => f.Service.RequestRevision(f.Teacher, first.ProblemWorkId, first.SubmissionId, feedback!, first.Version));
        Assert.Single(f.Store.ExpectedVersions);
        Assert.Equal(1, f.Store.SaveCalls);
        Assert.Equal(ProblemWorkStatus.Submitted, work.Status);
        Assert.Equal(first.Version, work.Version);
        Assert.Equal(submissions, work.Submissions);
        Assert.Equal(events, work.Events);
        Assert.Empty(work.Reviews);
        Assert.Empty(work.Messages);
        Assert.Null(work.AcceptedAt);
    }

    [Fact]
    public async Task Unauthorized_teacher_cannot_mutate_or_save()
    {
        var f = new Fixture();
        var first = await f.Submit();
        f.Policy.DenyReview = true;
        await Error("TeacherNotAuthorized", () => f.Service.AcceptSubmission(f.Teacher, first.ProblemWorkId, first.SubmissionId, null, first.Version));
        Assert.Empty(f.Store.Work!.Reviews);
        Assert.Single(f.Store.ExpectedVersions);
    }

    [Fact]
    public async Task Other_student_cannot_submit_even_if_policy_is_permissive()
    {
        var f = new Fixture();
        var foreign = new Actor(Guid.NewGuid(), AuthorRole.Student);
        var error = await Assert.ThrowsAsync<DomainException>(() => f.Service.SubmitSolution(foreign, f.Assignment.Id, "body", [], null));
        Assert.Equal("AssignmentDoesNotBelongToStudent", error.Code);
        Assert.Null(f.Store.Work);
        Assert.Empty(f.Store.ExpectedVersions);
    }

    [Fact]
    public async Task Access_policy_checks_enrollment_and_lesson_before_creating_work()
    {
        var f = new Fixture();
        f.Policy.DenySubmit = true;
        await Error("LessonUnavailable", () => f.Submit());
        Assert.Null(f.Store.Work);
        Assert.Empty(f.Store.ExpectedVersions);
    }

    [Fact]
    public async Task Invalid_attachment_permission_prevents_submission()
    {
        var f = new Fixture();
        f.Policy.DenyAttachments = true;
        await Error("InvalidAttachment", () => f.Submit());
        Assert.Null(f.Store.Work);
        Assert.Empty(f.Store.ExpectedVersions);
    }

    [Fact]
    public async Task Stale_review_version_does_not_mutate_work()
    {
        var f = new Fixture();
        var first = await f.Submit();
        await Error("ConcurrencyConflict", () => f.Service.AcceptSubmission(f.Teacher, first.ProblemWorkId, first.SubmissionId, null, 0));
        Assert.Single(f.Store.ExpectedVersions);
        Assert.Empty(f.Store.Work!.Reviews);
    }

    [Fact]
    public async Task First_submit_requires_absent_version_and_resubmit_requires_current_version()
    {
        var f = new Fixture();
        await Error("ConcurrencyConflict", () => f.Service.SubmitSolution(f.Student, f.Assignment.Id, "body", [], 0));
        Assert.Null(f.Store.Work);
        var first = await f.Submit();
        await f.Service.RequestRevision(f.Teacher, first.ProblemWorkId, first.SubmissionId, "revise", first.Version);
        await Error("ConcurrencyConflict", () => f.Service.SubmitSolution(f.Student, f.Assignment.Id, "revision", [], null));
        Assert.Equal(1, f.Store.Work!.AttemptCount);
    }

    [Fact]
    public async Task Save_conflict_is_propagated_without_retry()
    {
        var f = new Fixture();
        f.Store.FailSave = true;
        await Error("ConcurrencyConflict", () => f.Submit());
        Assert.Equal(1, f.Store.SaveCalls);
        Assert.Null(f.Store.Work);
    }

    [Fact]
    public async Task Post_message_checks_access_and_saves_without_creating_attempt()
    {
        var f = new Fixture();
        var first = await f.Submit();
        var result = await f.Service.PostMessage(f.Student, first.ProblemWorkId, "question", null, [], default);
        Assert.Equal(1, f.Policy.MessageChecks);
        Assert.Equal(result.MessageId, f.Store.Work!.Messages.Single().Id);
        Assert.Equal(ProblemWorkStatus.Submitted, f.Store.Work.Status);
        Assert.Single(f.Store.Work.Submissions);
        Assert.Equal(1, f.Store.ExpectedVersions.Last());
        f.Policy.DenyMessage = true;
        await Error("StudentNotAuthorized", () => f.Service.PostMessage(f.Student, first.ProblemWorkId, "blocked", null, []));
        Assert.Single(f.Store.Work.Messages);
    }

    [Fact]
    public async Task Missing_assignment_and_work_have_explicit_errors()
    {
        var f = new Fixture();
        await Error("AssignmentNotFound", () => f.Service.SubmitSolution(f.Student, Guid.NewGuid(), "body", [], null));
        await Error("ProblemWorkNotFound", () => f.Service.AcceptSubmission(f.Teacher, Guid.NewGuid(), Guid.NewGuid(), null, 0));
    }

    [Fact]
    public async Task Student_cannot_call_review_use_case()
    {
        var f = new Fixture();
        var first = await f.Submit();
        await Error("TeacherNotAuthorized", () => f.Service.AcceptSubmission(f.Student, first.ProblemWorkId, first.SubmissionId, null, first.Version));
        Assert.Empty(f.Store.Work!.Reviews);
    }

    private static async Task Error(string code, Func<Task> operation)
        => Assert.Equal(code, (await Assert.ThrowsAsync<UseCaseException>(operation)).Code);

    private sealed class Fixture
    {
        public Actor Student { get; } = new(Guid.NewGuid(), AuthorRole.Student);
        public Actor Teacher { get; } = new(Guid.NewGuid(), AuthorRole.Teacher);
        public ProblemAssignment Assignment { get; }
        public StoreSpy Store { get; }
        public AccessStub Policy { get; } = new();
        public ProblemWorkService Service { get; }
        public Fixture()
        {
            Assignment = new(Guid.NewGuid(), Student.Id, "course", new(Guid.NewGuid(), "lesson", "problem"));
            Store = new(Assignment);
            Service = new(Store, Policy, new FixedTime());
        }
        public Task<SubmissionResult> Submit() => Service.SubmitSolution(Student, Assignment.Id, "body", [], null);
    }

    // Interaction spy only. This is not a persistence adapter or a concurrency simulation.
    private sealed class StoreSpy(ProblemAssignment assignment) : IWorkStore
    {
        public ProblemWork? Work { get; private set; }
        public List<long?> ExpectedVersions { get; } = [];
        public bool FailSave { get; set; }
        public int SaveCalls { get; private set; }
        public Task<ProblemAssignment?> FindAssignment(Guid id, CancellationToken ct) => Task.FromResult(id == assignment.Id ? assignment : null);
        public Task<ProblemWork?> FindByAssignment(Guid id, CancellationToken ct) => Task.FromResult(Work?.ProblemAssignmentId == id ? Work : null);
        public Task<ProblemWork?> FindWork(Guid id, CancellationToken ct) => Task.FromResult(Work?.Id == id ? Work : null);
        public Task Save(ProblemWork work, long? expected, CancellationToken ct)
        {
            SaveCalls++;
            if (FailSave) throw new UseCaseException("ConcurrencyConflict");
            ExpectedVersions.Add(expected);
            Work = work;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class AccessStub : IAccessPolicy
    {
        public int SubmitChecks { get; private set; }
        public int ReviewChecks { get; private set; }
        public int AttachmentChecks { get; private set; }
        public int MessageChecks { get; private set; }
        public bool DenySubmit { get; set; }
        public bool DenyReview { get; set; }
        public bool DenyAttachments { get; set; }
        public bool DenyMessage { get; set; }
        public Task EnsureCanSubmit(Actor actor, ProblemAssignment assignment, CancellationToken ct)
        { SubmitChecks++; if (DenySubmit) throw new UseCaseException("LessonUnavailable"); return Task.CompletedTask; }
        public Task EnsureCanReview(Actor actor, ProblemAssignment assignment, CancellationToken ct)
        { ReviewChecks++; if (DenyReview) throw new UseCaseException("TeacherNotAuthorized"); return Task.CompletedTask; }
        public Task EnsureCanPostMessage(Actor actor, ProblemAssignment assignment, CancellationToken ct)
        { MessageChecks++; if (DenyMessage) throw new UseCaseException("StudentNotAuthorized"); return Task.CompletedTask; }
        public Task EnsureCanUseAttachments(Actor actor, IReadOnlyList<Guid> ids, CancellationToken ct)
        { AttachmentChecks++; if (DenyAttachments) throw new UseCaseException("InvalidAttachment"); return Task.CompletedTask; }
    }
}
