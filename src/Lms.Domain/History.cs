namespace Lms.Domain;

public enum ProblemWorkStatus { NotStarted, Submitted, NeedsRevision, Accepted }
public enum ReviewDecision { Accepted, NeedsRevision }
public enum AuthorRole { Student, Teacher }

public sealed class Submission
{
    private Submission() { AttachmentIds = Array.AsReadOnly(Array.Empty<Guid>()); }
    public Guid Id { get; } = Guid.NewGuid();
    public Guid ProblemWorkId { get; }
    public int AttemptNumber { get; }
    public string? BodyMarkdown { get; }
    public DateTimeOffset SubmittedAt { get; }
    public Guid SubmittedBy { get; }
    public IReadOnlyList<Guid> AttachmentIds { get; }

    internal Submission(Guid workId, int number, string? body, Guid[] attachments, Guid student, DateTimeOffset at)
    {
        ProblemWorkId = workId;
        AttemptNumber = number;
        BodyMarkdown = body;
        AttachmentIds = Array.AsReadOnly(attachments);
        SubmittedBy = student;
        SubmittedAt = at.ToUniversalTime();
    }
}

public sealed class Message
{
    private Message() { BodyMarkdown = null!; AttachmentIds = Array.AsReadOnly(Array.Empty<Guid>()); }
    public Guid Id { get; } = Guid.NewGuid();
    public Guid ProblemWorkId { get; }
    public Guid AuthorId { get; }
    public AuthorRole AuthorRole { get; }
    public string BodyMarkdown { get; }
    public Guid? ReplyToMessageId { get; }
    public DateTimeOffset CreatedAt { get; }
    public IReadOnlyList<Guid> AttachmentIds { get; }

    internal Message(Guid workId, Guid author, AuthorRole role, string body, Guid? replyTo, Guid[] attachments, DateTimeOffset at)
    {
        ProblemWorkId = workId;
        AuthorId = author;
        AuthorRole = role;
        BodyMarkdown = body;
        ReplyToMessageId = replyTo;
        AttachmentIds = Array.AsReadOnly(attachments);
        CreatedAt = at.ToUniversalTime();
    }
}

public sealed class Review
{
    private Review() { }
    public Guid Id { get; } = Guid.NewGuid();
    public Guid ProblemWorkId { get; }
    public Guid SubmissionId { get; }
    public Guid TeacherId { get; }
    public ReviewDecision Decision { get; }
    public Guid? MessageId { get; }
    public DateTimeOffset CreatedAt { get; }

    public IReadOnlyList<AnnotatedAttachment> Annotations { get; } = Array.AsReadOnly(Array.Empty<AnnotatedAttachment>());

    internal Review(Guid workId, Guid submissionId, Guid teacher, ReviewDecision decision, Guid? messageId, DateTimeOffset at, IReadOnlyList<AnnotatedAttachment> annotations)
    {
        Annotations = annotations;
        ProblemWorkId = workId;
        SubmissionId = submissionId;
        TeacherId = teacher;
        Decision = decision;
        MessageId = messageId;
        CreatedAt = at.ToUniversalTime();
    }
}

public abstract record DomainEvent(Guid ProblemWorkId, DateTimeOffset OccurredAt);
public sealed record SolutionSubmitted(Guid WorkId, Guid SubmissionId, Guid StudentId, int AttemptNumber, DateTimeOffset At)
    : DomainEvent(WorkId, At);
public sealed record RevisionRequested(Guid WorkId, Guid SubmissionId, Guid ReviewId, DateTimeOffset At)
    : DomainEvent(WorkId, At);
public sealed record SubmissionAccepted(Guid WorkId, Guid SubmissionId, Guid ReviewId, DateTimeOffset At)
    : DomainEvent(WorkId, At);
public sealed record MessagePosted(Guid WorkId, Guid MessageId, DateTimeOffset At)
    : DomainEvent(WorkId, At);
