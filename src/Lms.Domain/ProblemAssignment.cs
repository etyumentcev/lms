namespace Lms.Domain;

public sealed class DomainException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

internal static class Guard
{
    internal static Guid Id(Guid value) => value != Guid.Empty ? value : throw new DomainException("InvalidId");
    internal static string Text(string value) => !string.IsNullOrWhiteSpace(value) ? value : throw new DomainException("EmptyValue");
}

public sealed record ProblemContentReference
{
    public Guid CourseVersionId { get; }
    public string LessonId { get; }
    public string ProblemId { get; }

    public ProblemContentReference(Guid courseVersionId, string lessonId, string problemId)
    {
        CourseVersionId = Guard.Id(courseVersionId);
        LessonId = Guard.Text(lessonId);
        ProblemId = Guard.Text(problemId);
    }
}

public enum AssignmentStatus { Active, Withdrawn }

public sealed class ProblemAssignment
{
    private ProblemAssignment() { CourseId = null!; Content = null!; }
    public Guid Id { get; }
    public Guid StudentId { get; }
    public string CourseId { get; }
    public ProblemContentReference Content { get; }
    public AssignmentStatus Status { get; private set; } = AssignmentStatus.Active;

    public ProblemAssignment(Guid id, Guid studentId, string courseId, ProblemContentReference content)
    {
        Id = Guard.Id(id);
        StudentId = Guard.Id(studentId);
        CourseId = Guard.Text(courseId);
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public void Withdraw() => Status = AssignmentStatus.Withdrawn;

    public void EnsureCanSubmit(Guid studentId)
    {
        if (studentId != StudentId) throw new DomainException("AssignmentDoesNotBelongToStudent");
        if (Status != AssignmentStatus.Active) throw new DomainException("AssignmentInactive");
    }
}
