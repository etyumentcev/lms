using Lms.Application;
using Lms.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure;

public sealed class AttachmentService(LmsDbContext db, IAttachmentStorage storage, IAccessPolicy access,
    AttachmentOptions options, TimeProvider time, ILogger<AttachmentService> logger, ApplicationDeadline? deadline = null)
{
    public async Task<AttachmentResult> Upload(Actor actor, string name, string mediaType, Stream source, CancellationToken ct)
    {
        EnsureStudent(actor);
        EnsureDeadline();
        using var timeout = deadline == null ? null : new DeadlineCancellation(deadline, time, ct);
        ct = timeout?.Token ?? ct;
        using var content = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            if (content.Length + read > options.MaxBytes) throw new UseCaseException("AttachmentTooLarge");
            await content.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        if (content.Length == 0) throw new UseCaseException("MalformedAttachment");
        ImageUploadValidator.Validate(content.ToArray(), mediaType);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 512 || name.Any(char.IsControl))
            throw new UseCaseException("MalformedAttachment");
        var record = new StoredAttachment { Id = Guid.NewGuid(), OwnerId = actor.Id, FileName = name,
            MediaType = mediaType, Size = content.Length, StorageKey = Guid.NewGuid().ToString("N"), CreatedAt = time.GetUtcNow() };
        content.Position = 0;
        await storage.Store(record.StorageKey, content, ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            EnsureDeadline();
            db.AttachmentMetadata.Add(new AttachmentAccess { Id = record.Id, OwnerId = actor.Id,
                IsEligible = true, ExpiresAt = DateTimeOffset.MaxValue });
            db.StoredAttachments.Add(record);
            await db.SaveChangesAsync(ct);
            ct.ThrowIfCancellationRequested();
            EnsureDeadline();
        }
        catch
        {
            // SELECT-not-found is not proof that an in-flight commit cannot finish later.
            // Keep the unique private original for explicit reconciliation; never replay here.
            logger.LogWarning("Attachment metadata outcome requires reconciliation for {Id}; original retained", record.Id);
            throw;
        }
        return new(record.Id, name, mediaType, record.Size, record.CreatedAt, $"/api/v1/attachments/{record.Id}/content");
    }

    public async Task<(Stream Content, string MediaType)> Open(Actor actor, Guid id, CancellationToken ct)
    {
        EnsureStudent(actor);
        EnsureDeadline();
        var item = await db.StoredAttachments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && !x.Deleted, ct)
            ?? throw new UseCaseException("NotFound");
        if (item.SubmissionId is null && item.MessageId is null)
        {
            if (item.OwnerId != actor.Id) throw new UseCaseException("NotFound");
        }
        else
        {
            // Once attached, access follows the parent work even for the uploader.
            // Ownership must not bypass revoked enrollment, availability or teacher scope.
            Guid workId;
            if (item.SubmissionId is { } submission)
                workId = await db.Set<Submission>().Where(x => x.Id == submission).Select(x => x.ProblemWorkId).SingleAsync(ct);
            else workId = await db.Set<Message>().Where(x => x.Id == item.MessageId).Select(x => x.ProblemWorkId).SingleAsync(ct);
            var assignmentId = await db.Works.Where(x => x.Id == workId).Select(x => x.ProblemAssignmentId).SingleAsync(ct);
            var assignment = await db.Assignments.AsNoTracking().SingleAsync(x => x.Id == assignmentId, ct);
            try
            {
                if (actor.Role == AuthorRole.Student) await access.EnsureCanSubmit(actor, assignment, ct);
                else await access.EnsureCanReview(actor, assignment, ct);
            }
            catch (Exception e) when (e is UseCaseException or DomainException)
            { throw new UseCaseException("NotFound"); }
        }
        EnsureDeadline();
        try
        {
            var stream = await storage.Open(item.StorageKey, ct);
            try { ct.ThrowIfCancellationRequested(); EnsureDeadline(); return (stream, item.MediaType); }
            catch { await stream.DisposeAsync(); throw; }
        }
        catch (FileNotFoundException) { throw new UseCaseException("ContentUnavailable"); }
    }

    public async Task Delete(Actor actor, Guid id, CancellationToken ct)
    {
        EnsureStudent(actor);
        EnsureDeadline();
        using var timeout = deadline == null ? null : new DeadlineCancellation(deadline, time, ct);
        ct = timeout?.Token ?? ct;
        // Row lock serializes delete against transactionally claiming this upload.
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var item = await db.StoredAttachments.FromSqlInterpolated($"SELECT * FROM \"StoredAttachments\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
            if (item is null || item.OwnerId != actor.Id) throw new UseCaseException("NotFound");
            if (item.SubmissionId != null || item.MessageId != null) throw new UseCaseException("AttachmentAlreadyAttached");
            EnsureDeadline();
            item.Deleted = true;
            await db.AttachmentMetadata.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsEligible, false), ct);
            await db.SaveChangesAsync(ct);
            EnsureDeadline();
            await tx.CommitAsync(ct);
            ct.ThrowIfCancellationRequested();
            EnsureDeadline();
            // A tombstone remains even when physical deletion fails; retry DELETE is safe.
            await storage.Delete(item.StorageKey, ct);
            ct.ThrowIfCancellationRequested();
            EnsureDeadline();
        }
    }
    // Legacy files have no parent-bound durable intent or trusted teacher admission.
    // Teachers must use PostgresTeacherCommands; a deadline alone is not authority.
    private static void EnsureStudent(Actor actor)
    {
        if (actor.Id == Guid.Empty || actor.Role != AuthorRole.Student) throw new UseCaseException("Forbidden");
    }
    private void EnsureDeadline()
    {
        if (deadline != null && !deadline.Check(ApplicationAction.StartStage).Allowed)
            throw new OperationCanceledException("Original attachment deadline expired.");
    }

}
