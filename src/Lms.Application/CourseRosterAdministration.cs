namespace Lms.Application;

public sealed record CreateCourseCommand(Guid OrganizationId, Guid AdministratorActorId,
    string Title, string? Description, string IdempotencyKey, string TraceId);

public sealed record CreateGroupCommand(Guid OrganizationId, Guid AdministratorActorId,
    string CourseId, string Name, string? Description, string IdempotencyKey, string TraceId);

public sealed record EnrollmentBatchCommand(Guid OrganizationId, Guid AdministratorActorId,
    string CourseId, string GroupId, IReadOnlyList<Guid> StudentIds, string IdempotencyKey, string TraceId);

public sealed record MoveStudentCommand(Guid OrganizationId, Guid AdministratorActorId,
    string CourseId, Guid StudentId, string TargetGroupId, long ExpectedVersion,
    string IdempotencyKey, string TraceId);

public sealed record RemoveStudentCommand(Guid OrganizationId, Guid AdministratorActorId,
    string CourseId, Guid StudentId, long ExpectedVersion, string IdempotencyKey, string TraceId);

public sealed record AssignTeacherCommand(Guid OrganizationId, Guid AdministratorActorId,
    string CourseId, string GroupId, Guid TeacherId, string IdempotencyKey, string TraceId);

public sealed record RemoveTeacherCommand(Guid OrganizationId, Guid AdministratorActorId,
    string CourseId, string GroupId, Guid TeacherId, Guid GrantId, long ExpectedVersion,
    string IdempotencyKey, string TraceId);

public sealed record AssignCourseEditorCommand(Guid OrganizationId, Guid AdministratorActorId,
    string CourseId, Guid EditorId, string IdempotencyKey, string TraceId);

public sealed record RemoveCourseEditorCommand(Guid OrganizationId, Guid AdministratorActorId,
    string CourseId, Guid EditorId, Guid GrantId, long ExpectedVersion,
    string IdempotencyKey, string TraceId);

public sealed record CourseCatalogItem(string CourseId, string Title, string Description, string State,
    long Version, int GroupCount, int StudentCount, int TeacherCount);

public sealed record GroupCatalogItem(string GroupId, string Name, string Description, long Version,
    int StudentCount, int TeacherCount);

public sealed record StudentRosterItem(Guid ActorId, string DisplayName, string ExternalRosterKey,
    string GroupId, string GroupName, long EnrollmentGeneration, long Version);

public sealed record TeacherRosterItem(Guid ActorId, string DisplayName, string ExternalRosterKey,
    Guid GrantId, string GroupId, string GroupName, long Generation, long Version);

public sealed record CourseEditorRosterItem(Guid ActorId, string DisplayName, string ExternalRosterKey,
    Guid GrantId, long Generation, long Version);

public sealed record CourseRosterDetail(CourseCatalogItem Course, IReadOnlyList<GroupCatalogItem> Groups,
    IReadOnlyList<StudentRosterItem> Students, IReadOnlyList<TeacherRosterItem> Teachers,
    IReadOnlyList<CourseEditorRosterItem> Editors, bool CanEditContent = false);

public sealed record CourseCreated(string CourseId, string Title, string Description, string State,
    long Version, bool Created);

public sealed record GroupCreated(string CourseId, string GroupId, string Name, string Description,
    long Version, bool Created);

public sealed record RosterMutationResult(bool Changed, int Affected, long? Version = null,
    long? Generation = null, Guid? GrantId = null);

public sealed record LearningGroupItem(string GroupId, string Name, int StudentCount);

public sealed record LearningCourseItem(string CourseId, string Title, string Description, string State,
    ScopeRole Role, IReadOnlyList<LearningGroupItem> Groups, bool CanEditContent = false);

public interface ICourseRosterAdministration
{
    Task<CourseCreated> CreateCourse(CreateCourseCommand command, CancellationToken ct = default);
    Task<GroupCreated> CreateGroup(CreateGroupCommand command, CancellationToken ct = default);
    Task<IReadOnlyList<CourseCatalogItem>> AdminCatalog(Guid organizationId, Guid administratorActorId,
        CancellationToken ct = default);
    Task<CourseRosterDetail?> AdminCourse(Guid organizationId, Guid administratorActorId, string courseId,
        CancellationToken ct = default);
    Task<RosterMutationResult> Enroll(EnrollmentBatchCommand command, CancellationToken ct = default);
    Task<RosterMutationResult> Move(MoveStudentCommand command, CancellationToken ct = default);
    Task<RosterMutationResult> Remove(RemoveStudentCommand command, CancellationToken ct = default);
    Task<RosterMutationResult> Assign(AssignTeacherCommand command, CancellationToken ct = default);
    Task<RosterMutationResult> Unassign(RemoveTeacherCommand command, CancellationToken ct = default);
    Task<RosterMutationResult> AssignEditor(AssignCourseEditorCommand command, CancellationToken ct = default);
    Task<RosterMutationResult> UnassignEditor(RemoveCourseEditorCommand command, CancellationToken ct = default);
    Task<IReadOnlyList<LearningCourseItem>> LearningCatalog(TokenAuthority token, CancellationToken ct = default);
    Task<CourseRosterDetail?> TeacherCourse(TokenAuthority token, string courseId, CancellationToken ct = default);
}
