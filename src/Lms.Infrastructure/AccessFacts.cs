namespace Lms.Infrastructure;

// Trusted provisioning inputs for IAccessPolicy; no public write endpoints.
public sealed class EnrollmentAccess
{
    public Guid StudentId { get; set; }
    public string CourseId { get; set; } = null!;
    public string? GroupId { get; set; }
    public bool IsActive { get; set; }
}

public sealed class LessonAccess
{
    public string CourseId { get; set; } = null!;
    public Guid CourseVersionId { get; set; }
    public string LessonId { get; set; } = null!;
    public bool IsPublished { get; set; }
    public DateTimeOffset? AvailableFrom { get; set; }
}

public sealed class TeacherAccess
{
    public Guid Id { get; set; }
    public Guid TeacherId { get; set; }
    public string CourseId { get; set; } = null!;
    // null grants the whole course; otherwise only students enrolled in this group.
    public string? GroupId { get; set; }
}

public sealed class AttachmentAccess
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    // Only a trusted, validating storage producer may mark a stored upload eligible.
    public bool IsEligible { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
