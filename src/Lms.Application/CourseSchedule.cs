namespace Lms.Application;

public enum CourseEventKind { Lesson, Consultation, ExtraLesson }
public enum CourseDeliveryMode { InPerson, Online, Hybrid }
public enum CourseOccurrenceStatus { Scheduled, Moved, Cancelled }

public sealed record CourseWorkloadDto(string CourseId, int TotalAcademicHours,
    int ScheduledAcademicHours, int IndependentAcademicHours, int AcademicHourMinutes,
    int CalendarPlannedAcademicHours, int? ConductedAcademicHours, int RemainingPlannedAcademicHours,
    bool CalendarPlanMismatch, long Version);

public sealed record CourseScheduleRuleDto(Guid RuleId, string CourseId, int IsoDayOfWeek,
    string StartTime, string EndTime, int AcademicHours, DateOnly EffectiveFrom, DateOnly EffectiveTo,
    string TimeZoneId, CourseDeliveryMode DeliveryMode, string? Location, string? OnlineUrl,
    CourseEventKind EventKind, string Title, string? Description, string? LessonId, bool IsActive, long Version);

public sealed record CourseScheduleEventDto(string OccurrenceId, Guid RuleId, string CourseId,
    DateOnly OriginalDate, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string TimeZoneId,
    int AcademicHours, CourseDeliveryMode DeliveryMode, string? Location, string? OnlineUrl,
    CourseEventKind EventKind, string Title, string? Description, string? LessonId,
    CourseOccurrenceStatus Status, DateTimeOffset? OriginalStartsAt, long? ExceptionVersion);

public sealed record CourseScheduleDto(CourseWorkloadDto? Workload,
    IReadOnlyList<CourseScheduleRuleDto> Rules);

public sealed record SetCourseWorkloadCommand(Guid OrganizationId, Guid AdministratorActorId,
    string CourseId, int TotalAcademicHours, int ScheduledAcademicHours,
    int IndependentAcademicHours, int AcademicHourMinutes, long ExpectedVersion,
    string IdempotencyKey, string TraceId);

public sealed record SaveCourseScheduleRuleCommand(Guid OrganizationId, Guid AdministratorActorId,
    string CourseId, Guid? RuleId, int IsoDayOfWeek, string StartTime, string EndTime,
    int AcademicHours, DateOnly EffectiveFrom, DateOnly EffectiveTo, string TimeZoneId,
    CourseDeliveryMode DeliveryMode, string? Location, string? OnlineUrl, CourseEventKind EventKind,
    string Title, string? Description, string? LessonId, bool IsActive, long ExpectedVersion,
    string IdempotencyKey, string TraceId);

public sealed record ChangeCourseOccurrenceCommand(Guid OrganizationId, Guid AdministratorActorId,
    string CourseId, Guid RuleId, DateOnly OriginalDate, CourseOccurrenceStatus Status,
    DateOnly? NewDate, string? NewStartTime, string? NewEndTime, int? AcademicHours,
    CourseDeliveryMode? DeliveryMode, string? Location, string? OnlineUrl,
    long ExpectedVersion, string IdempotencyKey, string TraceId);

public sealed record ScheduleMutationResult(bool Changed, Guid? RuleId, long Version);

public interface ICourseScheduleService
{
    Task<CourseScheduleDto?> Schedule(TokenAuthority token, string courseId,
        CancellationToken ct = default);
    Task<CourseWorkloadDto?> Workload(TokenAuthority token, string courseId,
        CancellationToken ct = default);
    Task<IReadOnlyList<CourseScheduleEventDto>> Events(TokenAuthority token, string courseId,
        DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<CourseScheduleEventDto>> Upcoming(TokenAuthority token, string courseId,
        int limit = 5, CancellationToken ct = default);
    Task<CourseScheduleDto?> AdminSchedule(Guid organizationId, Guid administratorActorId,
        string courseId, CancellationToken ct = default);
    Task<ScheduleMutationResult> SetWorkload(SetCourseWorkloadCommand command,
        CancellationToken ct = default);
    Task<ScheduleMutationResult> SaveRule(SaveCourseScheduleRuleCommand command,
        CancellationToken ct = default);
    Task<ScheduleMutationResult> ChangeOccurrence(ChangeCourseOccurrenceCommand command,
        CancellationToken ct = default);
}
