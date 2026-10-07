using Lms.Domain;

namespace Lms.Domain.Tests;

public sealed class ProblemWorkTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 10, 0, 0, TimeSpan.FromHours(6));
    private static readonly Guid Teacher = Guid.NewGuid();
    private static ProblemAssignment Assignment() => new(Guid.NewGuid(), Guid.NewGuid(), "course", new(Guid.NewGuid(), "lesson", "problem"));
    private static Submission Submit(ProblemWork work, ProblemAssignment assignment) => work.SubmitSolution(assignment, assignment.StudentId, "original $x$", [], Now);
    private static void Error(string code, Action action) => Assert.Equal(code, Assert.Throws<DomainException>(action).Code);

    [Fact]
    public void Complete_cycle_preserves_history_content_and_events()
    {
        var assignment = Assignment();
        var content = assignment.Content;
        var work = new ProblemWork(Guid.NewGuid(), assignment);
        Assert.Equal(ProblemWorkStatus.NotStarted, work.Status);
        Assert.Empty(work.Submissions);
        var first = Submit(work, assignment);
        Assert.Equal(ProblemWorkStatus.Submitted, work.Status);
        var revision = work.RequestRevision(Teacher, first.Id, "Explain step 2", Now.AddHours(1));
        Assert.Equal(ProblemWorkStatus.NeedsRevision, work.Status);
        Assert.Null(work.AcceptedAt);
        var message = work.Messages.Single();
        Assert.Equal(message.Id, revision.MessageId);
        work.PostMessage(assignment.StudentId, AuthorRole.Student, "Question", message.Id, [], Now.AddHours(2));
        Assert.Equal(ProblemWorkStatus.NeedsRevision, work.Status);
        Assert.Equal(1, work.AttemptCount);
        var second = work.SubmitSolution(assignment, assignment.StudentId, "revised", [], Now.AddHours(3));
        var accepted = work.Accept(Teacher, second.Id, null, Now.AddHours(4));
        Assert.Equal(ProblemWorkStatus.Accepted, work.Status);
        Assert.Equal(2, work.AttemptCount);
        Assert.Equal(new[] { 1, 2 }, work.Submissions.Select(s => s.AttemptNumber));
        Assert.Same(first, work.Submissions[0]);
        Assert.Equal("original $x$", first.BodyMarkdown);
        Assert.Same(content, assignment.Content);
        Assert.Null(accepted.MessageId);
        Assert.Equal(TimeSpan.Zero, first.SubmittedAt.Offset);
        Assert.Equal(Now.AddHours(4).ToUniversalTime(), work.AcceptedAt);
        Assert.Equal(5, work.Version);
        Assert.Equal(2, work.Events.OfType<SolutionSubmitted>().Count());
        Assert.Single(work.Events.OfType<RevisionRequested>());
        Assert.Single(work.Events.OfType<SubmissionAccepted>());
        Assert.Equal(2, work.Events.OfType<MessagePosted>().Count());
    }

    [Theory]
    [InlineData(ProblemWorkStatus.NotStarted, true)]
    [InlineData(ProblemWorkStatus.Submitted, false)]
    [InlineData(ProblemWorkStatus.NeedsRevision, true)]
    [InlineData(ProblemWorkStatus.Accepted, false)]
    public void Submit_is_allowed_only_in_two_states(ProblemWorkStatus state, bool allowed)
    {
        var assignment = Assignment();
        var work = AtState(assignment, state);
        var count = work.AttemptCount;
        var version = work.Version;
        if (allowed)
        {
            Submit(work, assignment);
            Assert.Equal(count + 1, work.AttemptCount);
            Assert.Equal(ProblemWorkStatus.Submitted, work.Status);
        }
        else
        {
            Error(state == ProblemWorkStatus.Accepted ? "WorkAlreadyAccepted" : "WorkAlreadySubmitted", () => Submit(work, assignment));
            Assert.Equal(count, work.AttemptCount);
            Assert.Equal(version, work.Version);
        }
    }

    [Theory]
    [InlineData(ProblemWorkStatus.NotStarted, false)]
    [InlineData(ProblemWorkStatus.NeedsRevision, false)]
    [InlineData(ProblemWorkStatus.Accepted, false)]
    [InlineData(ProblemWorkStatus.NotStarted, true)]
    [InlineData(ProblemWorkStatus.NeedsRevision, true)]
    [InlineData(ProblemWorkStatus.Accepted, true)]
    public void Review_requires_submitted_state(ProblemWorkStatus state, bool accept)
    {
        var work = AtState(Assignment(), state);
        var version = work.Version;
        Error("WorkNotSubmitted", () =>
        {
            if (accept) work.Accept(Teacher, Guid.NewGuid(), null, Now);
            else work.RequestRevision(Teacher, Guid.NewGuid(), "feedback", Now);
        });
        Assert.Equal(version, work.Version);
        Assert.Equal(state, work.Status);
    }

    [Fact]
    public void Old_submission_cannot_be_reviewed_after_resubmit()
    {
        var assignment = Assignment();
        var work = new ProblemWork(Guid.NewGuid(), assignment);
        var first = Submit(work, assignment);
        work.RequestRevision(Teacher, first.Id, "revise", Now);
        Submit(work, assignment);
        Error("SubmissionIsNotCurrent", () => work.Accept(Teacher, first.Id, null, Now));
        Error("SubmissionIsNotCurrent", () => work.RequestRevision(Teacher, first.Id, "revise", Now));
        Assert.Single(work.Reviews);
        Assert.Equal(ProblemWorkStatus.Submitted, work.Status);
    }

    [Fact]
    public void Empty_submission_leaves_aggregate_unchanged_but_photo_only_is_valid()
    {
        var assignment = Assignment();
        var work = new ProblemWork(Guid.NewGuid(), assignment);
        Error("EmptySubmission", () => work.SubmitSolution(assignment, assignment.StudentId, "  ", [], Now));
        Assert.Equal(0, work.Version);
        Assert.Empty(work.Events);
        var ids = new[] { Guid.NewGuid() };
        var original = ids[0];
        var submission = work.SubmitSolution(assignment, assignment.StudentId, null, ids, Now);
        ids[0] = Guid.NewGuid();
        Assert.Equal(original, submission.AttachmentIds[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<Guid>)submission.AttachmentIds).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<Submission>)work.Submissions).Clear());
    }

    [Fact]
    public void Ownership_withdrawal_and_mismatched_assignment_are_rejected()
    {
        var assignment = Assignment();
        var work = new ProblemWork(Guid.NewGuid(), assignment);
        Error("AssignmentDoesNotBelongToStudent", () => work.SubmitSolution(assignment, Guid.NewGuid(), "body", [], Now));
        Error("AssignmentMismatch", () => Submit(work, Assignment()));
        assignment.Withdraw();
        Error("AssignmentInactive", () => Submit(work, assignment));
        Assert.Empty(work.Submissions);
    }

    [Fact]
    public void Reply_target_must_belong_to_work_and_author_must_be_owner()
    {
        var assignment = Assignment();
        var work = new ProblemWork(Guid.NewGuid(), assignment);
        var other = new ProblemWork(Guid.NewGuid(), Assignment());
        var foreignMessage = other.PostMessage(Teacher, AuthorRole.Teacher, "foreign", null, [], Now);
        Error("InvalidReplyTarget", () => work.PostMessage(assignment.StudentId, AuthorRole.Student, "reply", foreignMessage.Id, [], Now));
        Error("StudentNotAuthorized", () => work.PostMessage(Guid.NewGuid(), AuthorRole.Student, "reply", null, [], Now));
        Assert.Empty(work.Messages);
        Assert.Equal(0, work.Version);
    }

    [Fact]
    public void Failed_attachment_validation_creates_no_attempt()
    {
        var assignment = Assignment();
        var work = new ProblemWork(Guid.NewGuid(), assignment);
        var id = Guid.NewGuid();
        Error("InvalidAttachment", () => work.SubmitSolution(assignment, assignment.StudentId, "body", [id, id], Now));
        Error("InvalidAttachment", () => work.SubmitSolution(assignment, assignment.StudentId, "body", [Guid.Empty], Now));
        Assert.Empty(work.Submissions);
        Assert.Empty(work.Events);
    }

    [Fact]
    public void Domain_optional_revision_feedback_does_not_invent_message()
    {
        var assignment = Assignment();
        var work = new ProblemWork(Guid.NewGuid(), assignment);
        var submission = Submit(work, assignment);
        var review = work.RequestRevision(Teacher, submission.Id, null, Now);
        Assert.Null(review.MessageId);
        Assert.Empty(work.Messages);
        Assert.Equal(ProblemWorkStatus.NeedsRevision, work.Status);
    }

    private static ProblemWork AtState(ProblemAssignment assignment, ProblemWorkStatus state)
    {
        var work = new ProblemWork(Guid.NewGuid(), assignment);
        if (state == ProblemWorkStatus.NotStarted) return work;
        var submission = Submit(work, assignment);
        if (state == ProblemWorkStatus.NeedsRevision) work.RequestRevision(Teacher, submission.Id, "revise", Now);
        if (state == ProblemWorkStatus.Accepted) work.Accept(Teacher, submission.Id, null, Now);
        return work;
    }
}
