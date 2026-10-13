namespace Lms.Application;

// Trusted internal system capability, never an educational-role or HTTP authorization port.
// Adapters must not infer permission to call this from Teacher/OrgAdmin/JWT claims.
public interface IProcessingStateAdministration
{
    Task<long> Change(ProcessingStateCommand command, CancellationToken cancellationToken = default);
    // Null means no committed journal entry observed, NOT proof the original executor has stopped.
    Task<long?> FindCommittedResult(ProcessingStateCommand command, CancellationToken cancellationToken = default);
}

public sealed record ProcessingStateCommand(Guid OrganizationId, Guid OperationId, long ExpectedRevision, ProcessingStateMutation Mutation);
public abstract record ProcessingStateMutation;
public sealed record AddProcessingContext(Guid ContextId, long Version, DateTimeOffset EffectiveFrom,
    string ApprovalState = "SyntheticOnly", string? PurposeRef = null, string? RetentionPolicyRef = null) : ProcessingStateMutation;
public sealed record OpenProcessingBinding(string CourseId, Guid ContextId, long ContextVersion, DateTimeOffset EffectiveFrom) : ProcessingStateMutation;
public sealed record CloseProcessingBinding(string CourseId, DateTimeOffset ExpectedBindingFrom) : ProcessingStateMutation;
public sealed record ReplaceProcessingBinding(string CourseId, DateTimeOffset ExpectedBindingFrom, Guid ContextId, long ContextVersion) : ProcessingStateMutation;
public sealed record AddProcessingRestriction(Guid RestrictionId, Guid ContextId, long ContextVersion,
    Guid? SubjectActorId, int Operations, string? ResourceRef = null) : ProcessingStateMutation;
public sealed record WithdrawProcessingRestriction(Guid RestrictionId, long ExpectedVersion) : ProcessingStateMutation;
// SyntheticOnly=false is deliberately not representable. Existing S1 restriction remains in force.
public sealed record SetProcessingOrganizationState(string State) : ProcessingStateMutation;

// Server-captured processing evidence, not a client authority assertion. Authentication, owner
// authorization and complete source/subject resolution remain the caller's separate obligations.
public sealed record ProcessingWriteContext(Guid OrganizationId, string CourseId, Guid ContextId,
    long ContextVersion, DateTimeOffset BindingFrom, IReadOnlyList<Guid> Subjects);
