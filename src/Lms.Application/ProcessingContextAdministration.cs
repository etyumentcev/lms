namespace Lms.Application;

public sealed record ProcessingContextSummary(Guid ContextId, long Version, DateTimeOffset EffectiveFrom,
    string ApprovalState, bool SyntheticOnly, string PurposeRef, string RetentionPolicyRef, int VersionCount);

public sealed record CourseContextBindingSummary(Guid ContextId, long ContextVersion,
    DateTimeOffset EffectiveFrom);

public sealed record CourseProcessingContextSummary(string CourseId, string Title, string State,
    CourseContextBindingSummary? Binding);

public sealed record ProcessingContextAdministrationState(long Revision,
    ProcessingContextSummary? CurrentContext, bool HasActiveRestrictions,
    IReadOnlyList<CourseProcessingContextSummary> Courses);

public sealed record CreateSyntheticProcessingContextCommand(Guid OrganizationId,
    Guid AdministratorActorId, Guid OperationId, long ExpectedRevision,
    Guid? ExpectedContextId, long? ExpectedContextVersion, DateTimeOffset EffectiveFrom);

public sealed record BindCourseProcessingContextCommand(Guid OrganizationId,
    Guid AdministratorActorId, string CourseId, Guid OperationId, long ExpectedRevision,
    Guid ContextId, long ContextVersion, DateTimeOffset? ExpectedBindingFrom,
    DateTimeOffset? EffectiveFrom);

public sealed record ProcessingContextAdministrationResult(bool Changed,
    ProcessingContextAdministrationState State);

public interface IProcessingContextAdministration
{
    Task<ProcessingContextAdministrationState> State(Guid organizationId, Guid administratorActorId,
        CancellationToken cancellationToken = default);
    Task<ProcessingContextAdministrationResult> CreateVersion(
        CreateSyntheticProcessingContextCommand command, CancellationToken cancellationToken = default);
    Task<ProcessingContextAdministrationResult> BindCourse(
        BindCourseProcessingContextCommand command, CancellationToken cancellationToken = default);
}
