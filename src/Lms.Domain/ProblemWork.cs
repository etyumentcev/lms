namespace Lms.Domain;

public sealed class ProblemWork
{
    private ProblemWork() { }
    private readonly List<Submission> submissions = [];
    private readonly List<Review> reviews = [];
    private readonly List<Message> messages = [];
    private readonly List<DomainEvent> events = [];

    public Guid Id { get; }
    public Guid ProblemAssignmentId { get; }
    public Guid StudentId { get; }
    public ProblemWorkStatus Status { get; private set; } = ProblemWorkStatus.NotStarted;
    public long Version { get; private set; }
    public int AttemptCount => submissions.Count;
    public DateTimeOffset? LastSubmissionAt => submissions.LastOrDefault()?.SubmittedAt;
    public DateTimeOffset? LastReviewedAt => reviews.LastOrDefault()?.CreatedAt;
    public DateTimeOffset? AcceptedAt { get; private set; }
    public IReadOnlyList<Submission> Submissions => submissions.AsReadOnly();
    public IReadOnlyList<Review> Reviews => reviews.AsReadOnly();
    public IReadOnlyList<Message> Messages => messages.AsReadOnly();
    public IReadOnlyList<DomainEvent> Events => events.AsReadOnly();

    public ProblemWork(Guid id, ProblemAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        Id = Guard.Id(id);
        ProblemAssignmentId = assignment.Id;
        StudentId = assignment.StudentId;
    }

    public Submission SubmitSolution(ProblemAssignment assignment, Guid studentId, string? body,
        IEnumerable<Guid> attachmentIds, DateTimeOffset at)
    {
        if (assignment.Id != ProblemAssignmentId || assignment.StudentId != StudentId)
            throw new DomainException("AssignmentMismatch");
        assignment.EnsureCanSubmit(studentId);
        if (Status == ProblemWorkStatus.Submitted) throw new DomainException("WorkAlreadySubmitted");
        if (Status == ProblemWorkStatus.Accepted) throw new DomainException("WorkAlreadyAccepted");
        var attachments = ValidateAttachments(attachmentIds);
        if (string.IsNullOrWhiteSpace(body) && attachments.Length == 0) throw new DomainException("EmptySubmission");
        var submission = new Submission(Id, AttemptCount + 1, body, attachments, studentId, at);
        submissions.Add(submission);
        Status = ProblemWorkStatus.Submitted;
        Version++;
        events.Add(new SolutionSubmitted(Id, submission.Id, StudentId, submission.AttemptNumber, submission.SubmittedAt));
        return submission;
    }

    public Review RequestRevision(Guid teacherId, Guid submissionId, string? feedback, DateTimeOffset at)
        => ReviewCurrent(teacherId, submissionId, feedback, ReviewDecision.NeedsRevision, at);

    public Review Accept(Guid teacherId, Guid submissionId, string? feedback, DateTimeOffset at)
        => ReviewCurrent(teacherId, submissionId, feedback, ReviewDecision.Accepted, at);

    private Review ReviewCurrent(Guid teacherId, Guid submissionId, string? feedback, ReviewDecision decision, DateTimeOffset at)
    {
        Guard.Id(teacherId);
        if (Status != ProblemWorkStatus.Submitted) throw new DomainException("WorkNotSubmitted");
        if (submissions[^1].Id != submissionId) throw new DomainException("SubmissionIsNotCurrent");
        if (reviews.Any(r => r.SubmissionId == submissionId)) throw new DomainException("SubmissionAlreadyReviewed");
        if (decision == ReviewDecision.NeedsRevision && string.IsNullOrWhiteSpace(feedback))
            throw new DomainException("FeedbackRequired");
        Message? message = null;
        if (!string.IsNullOrWhiteSpace(feedback))
            message = AddMessage(teacherId, AuthorRole.Teacher, feedback, null, [], at);
        var review = new Review(Id, submissionId, teacherId, decision, message?.Id, at);
        reviews.Add(review);
        Status = decision == ReviewDecision.Accepted ? ProblemWorkStatus.Accepted : ProblemWorkStatus.NeedsRevision;
        if (Status == ProblemWorkStatus.Accepted) AcceptedAt = review.CreatedAt;
        Version++;
        events.Add(decision == ReviewDecision.Accepted
            ? new SubmissionAccepted(Id, submissionId, review.Id, review.CreatedAt)
            : new RevisionRequested(Id, submissionId, review.Id, review.CreatedAt));
        return review;
    }

    public Message PostMessage(Guid authorId, AuthorRole role, string body, Guid? replyTo,
        IEnumerable<Guid> attachmentIds, DateTimeOffset at)
    {
        Guard.Id(authorId);
        if (!Enum.IsDefined(role)) throw new DomainException("InvalidAuthorRole");
        if (role == AuthorRole.Student && authorId != StudentId) throw new DomainException("StudentNotAuthorized");
        if (string.IsNullOrWhiteSpace(body)) throw new DomainException("EmptyMessage");
        if (replyTo.HasValue && !messages.Any(m => m.Id == replyTo)) throw new DomainException("InvalidReplyTarget");
        var attachments = ValidateAttachments(attachmentIds);
        var message = AddMessage(authorId, role, body, replyTo, attachments, at);
        Version++;
        return message;
    }

    private Message AddMessage(Guid authorId, AuthorRole role, string body, Guid? replyTo, Guid[] attachments, DateTimeOffset at)
    {
        var message = new Message(Id, authorId, role, body, replyTo, attachments, at);
        messages.Add(message);
        events.Add(new MessagePosted(Id, message.Id, message.CreatedAt));
        return message;
    }

    private static Guid[] ValidateAttachments(IEnumerable<Guid> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var ids = values.ToArray();
        if (ids.Any(x => x == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw new DomainException("InvalidAttachment");
        return ids;
    }
}
