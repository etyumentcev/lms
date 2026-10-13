using Lms.Application;
using Lms.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure;

// Isolated synthetic opt-in. Parent authorization remains on the accepted work boundary;
// attachment authorization reuses S3 ownership and policy without enabling files globally in A1.
public sealed class PostgresProtectedPhotoQueries(LmsDbContext db, IProtectedWorkQueries works,
    IScopedAuthorityReader resources, ICurrentProcessingGate processing, IAttachmentStorage storage) : IProtectedPhotoQueries
{
    public const int MaxBytes = 10 * 1024 * 1024;
    private static void Stage(ApplicationDeadline d)
    { if (!d.Check(ApplicationAction.StartStage).Allowed) throw new OperationCanceledException(); }
    private async Task<bool> Allows(TokenAuthority token, PhotoSelection s, Guid id, ApplicationDeadline d, CancellationToken ct)
    {
        Stage(d);
        var owner = await resources.ResolveResource(new(ResourceKind.Attachment, id, s.Work.CourseId), ct);
        if (owner == null || owner.PendingAttachment || owner.WorkId != s.Work.WorkId || owner.SubmissionId != s.SubmissionId ||
            owner.UploaderActorId != owner.StudentId || !ScopePolicy.Allows(token.Authority, owner, ScopeAction.Read, token.Identity.ActorId)) return false;
        Stage(d);
        if (!await processing.Allows(owner, token.Identity.ActorId, ScopeAction.Read, ct)) return false;
        Stage(d);
        // Upload metadata is inserted only after Store succeeds. Claimed expiry is no longer
        // pending-upload TTL; immutable submission linkage supplies historical eligibility.
        return await db.StoredAttachments.AsNoTracking().AnyAsync(x => x.Id == id && !x.Deleted &&
            x.SubmissionId == s.SubmissionId && x.MessageId == null && x.OwnerId == owner.UploaderActorId &&
            x.Size > 0 && x.Size <= MaxBytes && (x.MediaType == "image/png" || x.MediaType == "image/jpeg"), ct);
    }
    public async Task<bool> CanReturn(TokenAuthority token, ProtectedPhotoRead read, ApplicationDeadline d, CancellationToken ct)
    {
        if (!await works.CanReturn(token, read.Work, d, ct)) return false;
        foreach (var id in read.AttachmentIds) if (!await Allows(token, read.Selection, id, d, ct)) return false;
        return true;
    }
    public async Task<ProtectedPhotoRead> Read(TokenAuthority token, PhotoSelection s, ApplicationDeadline d, CancellationToken ct)
    {
        if (s.Work.WorkId == null) throw new UseCaseException("Forbidden");
        var work = await works.Read(token, s.Work, d, ct);
        if (work.Value is not ProtectedWorkDto dto || !dto.Attempts.Any(a => a.Id == s.SubmissionId)) throw new UseCaseException("Forbidden");
        Stage(d);
        var ids = await db.Set<Submission>().AsNoTracking().Where(x => x.Id == s.SubmissionId && x.ProblemWorkId == s.Work.WorkId)
            .Select(x => x.AttachmentIds).SingleAsync(ct);
        if (ids.Count > 10 || ids.Distinct().Count() != ids.Count) throw new UseCaseException("Forbidden");
        var selected = s.AttachmentId is { } id ? ids.Contains(id) ? new[] { id } : throw new UseCaseException("Forbidden") : ids.ToArray();
        foreach (var item in selected) if (!await Allows(token, s, item, d, ct)) throw new UseCaseException("Forbidden");
        // The read facade supplies locally validated token authority and original ingress.
        // Read-only capabilities do not reserve a write attempt, but still span private I/O.
        using var teacherOperation = token.Authority.Membership.Role == ScopeRole.Teacher
            ? new TeacherOperationContext(token.Identity.ActorId, s.Work.WorkId.Value, "ReadPhoto", d) : null;
        teacherOperation?.EnsureActive();
        object value;
        if (s.AttachmentId is { } attachment)
        {
            Stage(d);
            var record = await db.StoredAttachments.AsNoTracking().SingleAsync(x => x.Id == attachment, ct);
            Stage(d);
            // Bounded private buffer: no DB transaction/connection or filesystem handle during delivery.
            await using var file = await storage.Open(record.StorageKey, ct);
            if (!file.CanSeek || file.Length != record.Size) throw new IOException("Unavailable original");
            var bytes = new byte[checked((int)record.Size)];
            await file.ReadExactlyAsync(bytes, ct);
            if (file.ReadByte() != -1) throw new IOException("Unavailable original");
            Stage(d);
            try { ImageUploadValidator.Validate(bytes, record.MediaType); }
            catch (UseCaseException) { throw new IOException("Unavailable original"); }
            value = new PhotoBytes(bytes, record.MediaType);
        }
        else
        {
            var root = $"/api/v1/courses/{Uri.EscapeDataString(s.Work.CourseId)}/versions/{s.Work.VersionId}/lessons/{Uri.EscapeDataString(s.Work.LessonId)}/works/{s.Work.WorkId}/submissions/{s.SubmissionId}/photos";
            value = new WorkPhotosDto(selected.Select(i => new WorkPhotoDto(i, root + "/" + i)).ToArray());
        }
        teacherOperation?.EnsureActive();
        return new(value, work, s, selected);
    }
}
