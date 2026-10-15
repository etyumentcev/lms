namespace Lms.Infrastructure;

public sealed class CourseWorkload
{
    public string CourseId { get; set; } = null!;
    public Guid OrganizationId { get; set; }
    public int TotalAcademicHours { get; set; }
    public int ScheduledAcademicHours { get; set; }
    public int IndependentAcademicHours { get; set; }
    public int AcademicHourMinutes { get; set; }
    public long Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid UpdatedBy { get; set; }
}

public sealed class CourseScheduleRule
{
    public Guid RuleId { get; set; }
    public string CourseId { get; set; } = null!;
    public Guid OrganizationId { get; set; }
    public int IsoDayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int AcademicHours { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly EffectiveTo { get; set; }
    public string TimeZoneId { get; set; } = null!;
    public string DeliveryMode { get; set; } = null!;
    public string? Location { get; set; }
    public string? OnlineUrl { get; set; }
    public string EventKind { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string? LessonId { get; set; }
    public bool IsActive { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid UpdatedBy { get; set; }
}

public sealed class CourseScheduleException
{
    public Guid ExceptionId { get; set; }
    public Guid RuleId { get; set; }
    public string CourseId { get; set; } = null!;
    public Guid OrganizationId { get; set; }
    public DateOnly OriginalDate { get; set; }
    public string Status { get; set; } = null!;
    public DateOnly? NewDate { get; set; }
    public TimeOnly? NewStartTime { get; set; }
    public TimeOnly? NewEndTime { get; set; }
    public int? AcademicHours { get; set; }
    public string? DeliveryMode { get; set; }
    public string? Location { get; set; }
    public string? OnlineUrl { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid UpdatedBy { get; set; }
}

public sealed class CourseScheduleCommandReceipt
{
    public Guid AdministratorActorId { get; set; }
    public string IdempotencyKey { get; set; } = null!;
    public Guid OrganizationId { get; set; }
    public string CourseId { get; set; } = null!;
    public string Kind { get; set; } = null!;
    public string PayloadDigest { get; set; } = null!;
    public string ResultJson { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CourseScheduleAudit
{
    public Guid AuditId { get; set; }
    public Guid OrganizationId { get; set; }
    public string CourseId { get; set; } = null!;
    public Guid ActorId { get; set; }
    public string Action { get; set; } = null!;
    public string ResourceId { get; set; } = null!;
    public long Version { get; set; }
    public string TraceId { get; set; } = null!;
    public DateTimeOffset OccurredAt { get; set; }
}
