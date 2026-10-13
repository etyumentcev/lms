namespace Lms.Application;

// Trusted host input only: derived actor/org, canonical resource, action, aggregate
// expectedVersion (or a stable intent ID for unversioned commands) and SHA-256 of the canonical command. These are NOT request DTOs
// or authorization. Resource/processing checks must precede reservation and still run
// in the transaction performing the domain mutation.
public sealed record TeacherOperationIntent(Guid OrganizationId, Guid ActorId, Guid CommandId,
    string Kind, string Resource, long? ExpectedVersion, string Fingerprint, Guid? IntentId = null);
public sealed record TeacherOperationAttempt(Guid OperationId, DateTimeOffset Deadline, bool FirstAttempt);

// Unversioned messages/uploads use a stable intent ID belonging to the draft/upload,
// distinct from transport CommandId. A retry must preserve IntentId even with a new
// request key. A new intentional message/upload has a new intent; equal payloads are
// not necessarily the same user intent. Delete uses the attachment ID as IntentId.
// Exactly one of ExpectedVersion and IntentId is required; version=0 is not a substitute.
