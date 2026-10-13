using System.Security.Cryptography;
using System.Text.Json;
using Lms.Application;
using Lms.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure;

// Isolated internal composition. The ONLY authority input is a locally validated JWT;
// no Actor, role, org, processing context or deadline can be taken from a request DTO.
// No DI/endpoint registration in Program.cs, no retries, no background continuation.
public sealed class PostgresTeacherCommands(IDbContextFactory<LmsDbContext> contexts, ComponentJwt jwt,
    IAuthorityClock clock, IAttachmentStorage storage, AttachmentOptions options, TimeProvider time)
{
    public Task<ReviewResult> Review(AccessToken bearer, ApplicationDeadline deadline, Guid commandId,
        Guid workId, Guid submissionId, long expectedVersion, bool revision, string? feedback,
        IReadOnlyList<AnnotatedAttachment>? annotations, CancellationToken ct = default)
        => Execute(bearer, deadline, commandId, new(ResourceKind.Work, workId), "Review", expectedVersion, null,
            new { workId, submissionId, expectedVersion, revision, feedback, annotations }, async (db, token, resource, operation, cancellation) =>
            {
                await AuthorizeAnnotations(db, token, resource, submissionId, annotations, cancellation);
                var service = Service(db, token, resource, operation);
                var actor = new Actor(token.Identity.ActorId, AuthorRole.Teacher);
                var result = revision
                    ? await service.RequestRevision(actor, workId, submissionId, feedback ?? "", expectedVersion, cancellation, annotations)
                    : await service.AcceptSubmission(actor, workId, submissionId, feedback, expectedVersion, cancellation, annotations);
                await AuthorizeAnnotations(db, token, resource, submissionId, annotations, cancellation);
                return result;
            }, ct, validate: async (db, token, resource, cancellation) =>
            {
                var work = await new PostgresWorkStore(db).FindWork(workId, cancellation) ?? throw new UseCaseException("Forbidden");
                if (work.Version != expectedVersion) throw new UseCaseException("ConcurrencyConflict");
                // Domain validation on an isolated, untracked copy has no durable effects.
                // Malformed feedback/annotations must not consume the versioned intent.
                if (revision) work.RequestRevision(token.Identity.ActorId, submissionId, feedback, time.GetUtcNow(), annotations);
                else work.Accept(token.Identity.ActorId, submissionId, feedback, time.GetUtcNow(), annotations);
                await AuthorizeAnnotations(db, token, resource, submissionId, annotations, cancellation);
            });

    public Task<MessageResult> Message(AccessToken bearer, ApplicationDeadline deadline, Guid commandId,
        Guid intentId, Guid workId, string body, Guid? replyTo, IReadOnlyList<Guid> attachments, CancellationToken ct = default)
        => Execute(bearer, deadline, commandId, new(ResourceKind.Work, workId), "Message", null, intentId,
            new { workId, body, replyTo, attachments }, (db, token, resource, operation, cancellation) =>
                Service(db, token, resource, operation).PostMessage(new(token.Identity.ActorId, AuthorRole.Teacher),
                    workId, body, replyTo, attachments, cancellation), ct);

    public async Task<AttachmentResult> Upload(AccessToken bearer, ApplicationDeadline deadline, Guid commandId,
        Guid intentId, Guid workId, string name, string mediaType, string sha256, Stream source, CancellationToken ct = default)
    {
        if (sha256 is not { Length: 64 } || sha256.Any(c => !char.IsAsciiHexDigit(c) || char.IsLower(c)) ||
            string.IsNullOrWhiteSpace(name) || name.Length > 512 || name.Any(char.IsControl)) throw new UseCaseException("MalformedAttachment");
        var record = new StoredAttachment { Id = Guid.NewGuid(), FileName = name, MediaType = mediaType,
            StorageKey = Guid.NewGuid().ToString("N"), CreatedAt = time.GetUtcNow() };
        return await Execute(bearer, deadline, commandId, new(ResourceKind.Work, workId), "Upload", null, intentId,
            new { workId, name, mediaType, sha256 }, async (db, token, resource, operation, cancellation) =>
            {
                record.OwnerId = token.Identity.ActorId;
                var now = clock.Read() ?? throw new OperationCanceledException();
                var binding = await db.Set<CourseContextBinding>().AsNoTracking().SingleAsync(x =>
                    x.CourseId == resource.CourseId && x.OrganizationId == resource.OrganizationId &&
                    x.EffectiveFrom <= now.Earliest && (x.EffectiveTo == null || x.EffectiveTo > now.Latest), cancellation);
                db.StoredAttachments.Add(record);
                db.AttachmentMetadata.Add(new AttachmentAccess { Id = record.Id, OwnerId = record.OwnerId,
                    IsEligible = true, ExpiresAt = DateTimeOffset.MaxValue });
                db.Add(new AttachmentScope { AttachmentId = record.Id, OrganizationId = resource.OrganizationId,
                    CourseId = resource.CourseId, UploaderActorId = record.OwnerId, ContextId = binding.ContextId,
                    ContextVersion = binding.ContextVersion });
                await db.SaveChangesAsync(cancellation);
                return new AttachmentResult(record.Id, name, mediaType, record.Size, record.CreatedAt,
                    $"/api/v1/attachments/{record.Id}/content");
            }, ct, async cancellation =>
            {
                using var content = new MemoryStream(); var buffer = new byte[81920]; int count;
                while ((count = await source.ReadAsync(buffer, cancellation)) > 0)
                {
                    Stage(deadline);
                    if (content.Length + count > options.MaxBytes) throw new UseCaseException("AttachmentTooLarge");
                    await content.WriteAsync(buffer.AsMemory(0, count), cancellation);
                }
                var bytes = content.ToArray();
                if (Convert.ToHexString(SHA256.HashData(bytes)) != sha256) throw new UseCaseException("MalformedAttachment");
                ImageUploadValidator.Validate(bytes, mediaType);
                Stage(deadline); record.Size = content.Length; content.Position = 0;
                // No DB transaction spans filesystem I/O. Unknown/private originals are
                // retained; a new request cannot run this prepare callback again.
                await storage.Store(record.StorageKey, content, cancellation);
                Stage(deadline);
            });
    }

    public async Task Delete(AccessToken bearer, ApplicationDeadline deadline, Guid commandId,
        Guid workId, Guid attachmentId, CancellationToken ct = default)
    {
        using var cancellation = new DeadlineCancellation(deadline, time, ct);
        ct = cancellation.Token;
        await Execute(bearer, deadline, commandId, new(ResourceKind.Work, workId), "Delete", null, attachmentId,
            new { workId, attachmentId }, async (db, token, resource, operation, tokenCancellation) =>
            {
                var item = await db.StoredAttachments.FromSqlInterpolated($"""
                    SELECT * FROM "StoredAttachments" WHERE "Id"={attachmentId} FOR UPDATE
                    """).SingleOrDefaultAsync(tokenCancellation);
                var scope = await db.Set<AttachmentScope>().AsNoTracking().SingleOrDefaultAsync(x => x.AttachmentId == attachmentId, tokenCancellation);
                if (item == null || item.OwnerId != token.Identity.ActorId || scope == null || scope.Quarantined ||
                    scope.OrganizationId != resource.OrganizationId || scope.CourseId != resource.CourseId ||
                    scope.UploaderActorId != item.OwnerId) throw new UseCaseException("NotFound");
                if (item.SubmissionId != null || item.MessageId != null) throw new UseCaseException("AttachmentAlreadyAttached");
                item.Deleted = true;
                await db.AttachmentMetadata.Where(x => x.Id == attachmentId).ExecuteUpdateAsync(x => x.SetProperty(a => a.IsEligible, false), tokenCancellation);
                await db.SaveChangesAsync(tokenCancellation);
                return item.StorageKey;
            }, ct, durableResource: "Attachment:" + attachmentId, finalize: async (key, operation, cancellation) =>
            {
                // ACK is required; the same capability remains alive until physical I/O
                // completes. No compensation or second executor after unknown COMMIT.
                operation.EnsureActive(ApplicationAction.ResponseEnqueue);
                await storage.Delete(key, cancellation);
                cancellation.ThrowIfCancellationRequested();
                operation.EnsureActive(ApplicationAction.ResponseEnqueue);
            });
    }

    public async Task<PhotoBytes> ReadAttachment(AccessToken bearer, ApplicationDeadline deadline, Guid id, CancellationToken ct = default)
    {
        using var cancellation = new DeadlineCancellation(deadline, time, ct); ct = cancellation.Token;
        await using var db = await contexts.CreateDbContextAsync(ct);
        Guid admittedActor = Guid.Empty;
        async Task Authorize()
        {
            Stage(deadline);
            var token = jwt.Validate(bearer);
            var resource = await new PostgresScopedAuthorityReader(db, time).ResolveResource(new(ResourceKind.Attachment, id), ct);
            if (token?.Authority.Membership.Role != ScopeRole.Teacher || resource == null ||
                !ScopePolicy.Allows(token.Authority, resource, ScopeAction.Read, token.Identity.ActorId) ||
                !await new PostgresCurrentProcessingGate(db, clock).Allows(resource, token.Identity.ActorId, ScopeAction.Read, ct))
                throw new UseCaseException("Forbidden");
            if (resource.UploaderActorId is { } uploader && uploader != token.Identity.ActorId &&
                !await new PostgresCurrentProcessingGate(db, clock).Allows(resource, uploader, ScopeAction.Read, ct))
                throw new UseCaseException("Forbidden");
            admittedActor = token.Identity.ActorId;
        }
        await Authorize();
        using var operation = new TeacherOperationContext(admittedActor, Guid.Empty, "ReadAttachment", deadline);
        operation.EnsureActive();
        var item = await db.StoredAttachments.AsNoTracking().SingleAsync(x => x.Id == id && !x.Deleted, ct);
        byte[] result;
        await using (var source = await storage.Open(item.StorageKey, ct))
        {
            using var bytes = new MemoryStream(); var buffer = new byte[81920]; int count;
            while ((count = await source.ReadAsync(buffer, ct)) > 0)
            {
                operation.EnsureActive();
                if (bytes.Length + count > options.MaxBytes) throw new UseCaseException("AttachmentTooLarge");
                await bytes.WriteAsync(buffer.AsMemory(0, count), ct);
            }
            result = bytes.ToArray();
        } // join file disposal before the final authority/result gate
        await Authorize(); // parent/current processing and token still required for read publication
        await db.DisposeAsync();
        operation.EnsureActive();
        return new(result, item.MediaType);
    }

    private async Task AuthorizeAnnotations(LmsDbContext db, TokenAuthority token, ResourceOwnership work,
        Guid submissionId, IReadOnlyList<AnnotatedAttachment>? annotations, CancellationToken ct)
    {
        foreach (var page in annotations ?? [])
        {
            var resource = await new PostgresScopedAuthorityReader(db, time).ResolveResource(new(ResourceKind.Attachment, page.AttachmentId), ct);
            if (resource == null || resource.WorkId != work.WorkId || resource.SubmissionId != submissionId ||
                resource.OrganizationId != work.OrganizationId || resource.CourseId != work.CourseId ||
                !ScopePolicy.Allows(token.Authority, resource, ScopeAction.Read, token.Identity.ActorId)) throw new UseCaseException("Forbidden");
            var gate = new PostgresCurrentProcessingGate(db, clock);
            var allowed = db.Database.CurrentTransaction == null
                ? await gate.Allows(resource, token.Identity.ActorId, ScopeAction.Read, ct)
                : await gate.AllowsInTransaction(resource, token.Identity.ActorId, ScopeAction.Read, ct);
            if (!allowed) throw new UseCaseException("Forbidden");
        }
    }

    private ProblemWorkService Service(LmsDbContext db, TokenAuthority token, ResourceOwnership resource, TeacherOperationContext operation)
        => new(new TransactionStore(new PostgresWorkStore(db, operation.Deadline), operation),
            new WriteAccess(db, token, resource, clock, time), time, operation);

    private async Task<T> Execute<T>(AccessToken bearer, ApplicationDeadline deadline, Guid commandId,
        ResourceReference reference, string kind, long? version, Guid? intentId, object payload,
        Func<LmsDbContext, TokenAuthority, ResourceOwnership, TeacherOperationContext, CancellationToken, Task<T>> mutate,
        CancellationToken ct, Func<CancellationToken, Task>? prepare = null, string? durableResource = null,
        Func<LmsDbContext, TokenAuthority, ResourceOwnership, CancellationToken, Task>? validate = null,
        Func<T, TeacherOperationContext, CancellationToken, Task>? finalize = null)
    {
        ArgumentNullException.ThrowIfNull(deadline);
        Stage(deadline);
        using var cancellation = new DeadlineCancellation(deadline, time, ct);
        ct = cancellation.Token;
        var token = jwt.Validate(bearer);
        if (token?.Authority.Membership.Role != ScopeRole.Teacher) throw new UseCaseException("Forbidden");
        await using var db = await contexts.CreateDbContextAsync(ct);
        if (db.Database.CreateExecutionStrategy().RetriesOnFailure) throw new InvalidOperationException("Automatic retries are not allowed for teacher writes.");
        var resources = new PostgresScopedAuthorityReader(db, time);
        var resource = await resources.ResolveResource(reference, ct) ?? throw new UseCaseException("Forbidden");
        var action = reference.Kind == ResourceKind.Attachment ? ScopeAction.Read : ScopeAction.Review;
        if (!ScopePolicy.Allows(token.Authority, resource, action, token.Identity.ActorId) ||
            !await new PostgresCurrentProcessingGate(db, clock).Allows(resource, token.Identity.ActorId, action, ct))
            throw new UseCaseException("Forbidden");
        if (validate != null)
        {
            // Preflight materializes history only under the same conservative processing
            // lock and complete author checks. It has no durable domain effects.
            await using var validation = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT public.processing_lock({resource.OrganizationId})", ct);
            await Authorize();
            await validate(db, token, resource, ct);
        }
        // Revalidate after admission DB waits, before durable reservation. The admitted
        // authority then stays fixed for this operation; refresh never replaces it.
        if (jwt.Validate(bearer) == null) throw new UseCaseException("Forbidden");
        Stage(deadline);
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload)));
        await using (var attempts = await contexts.CreateDbContextAsync(ct))
        {
            var reserved = await new PostgresTeacherOperationAttempts(attempts).Reserve(new(resource.OrganizationId,
                token.Identity.ActorId, commandId, kind, durableResource ?? reference.Kind + ":" + reference.Id,
                version, fingerprint, intentId), deadline, ct);
            if (!reserved.FirstAttempt) throw new UseCaseException("OperationAlreadyAttempted");
        }
        using var operation = new TeacherOperationContext(token.Identity.ActorId, resource.WorkId ?? Guid.Empty, kind, deadline);
        operation.EnsureActive();
        if (prepare != null) await prepare(ct);
        operation.EnsureActive();
        Stage(deadline);
        // Org -> processing guard -> aggregate CAS / attachment claim. Existing processing
        // writers share these locks. Fresh conservative processing checks occur inside
        // this transaction both before mutation and immediately before COMMIT dispatch.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT public.processing_lock({resource.OrganizationId})", ct);
        async Task Authorize()
        {
            Stage(deadline);
            var current = await resources.ResolveResource(reference, ct);
            if (current != resource || !ScopePolicy.Allows(token.Authority, current, action, token.Identity.ActorId) ||
                !await new PostgresCurrentProcessingGate(db, clock).AllowsInTransaction(current, token.Identity.ActorId, action, ct))
                throw new UseCaseException("Forbidden");
            if (resource.WorkId is { } id)
            {
                var authors = await db.Set<Message>().Where(m => m.ProblemWorkId == id).Select(m => m.AuthorId)
                    .Union(db.Set<Review>().Where(x => x.ProblemWorkId == id).Select(x => x.TeacherId)).Distinct().ToListAsync(ct);
                foreach (var author in authors)
                    if (!await db.Set<OrganizationMembership>().AsNoTracking().AnyAsync(m =>
                            m.OrganizationId == resource.OrganizationId && m.ActorId == author, ct) ||
                        !await new PostgresCurrentProcessingGate(db, clock).AllowsInTransaction(current, author, action, ct))
                        throw new UseCaseException("Forbidden");
            }
        }
        await Authorize();
        var result = await mutate(db, token, resource, operation, ct);
        await Authorize();
        ct.ThrowIfCancellationRequested();
        Task? pending = null;
        if (!deadline.DispatchCommit(() => pending = tx.CommitAsync(ct)).Allowed) throw new OperationCanceledException();
        await pending!;
        deadline.AcknowledgeCommit();
        ct.ThrowIfCancellationRequested();
        if (!deadline.Check(ApplicationAction.ResponseEnqueue).Allowed) throw new OperationCanceledException();
        await tx.DisposeAsync(); // release the DB transaction before physical file finalization
        await db.DisposeAsync(); // include context/connection disposal before publishing success
        if (finalize != null) await finalize(result, operation, ct);
        operation.EnsureActive(ApplicationAction.ResponseEnqueue);
        return result;
    }

    private static void Stage(ApplicationDeadline deadline)
    {
        var gate = deadline.Check(ApplicationAction.StartStage);
        if (!gate.Allowed || gate.Remaining > RevocationPolicy.MaximumOperationLifetime) throw new OperationCanceledException();
    }

    private sealed class TransactionStore(PostgresWorkStore store, TeacherOperationContext operation) : IWorkStore
    {
        public Task<ProblemAssignment?> FindAssignment(Guid id, CancellationToken ct) => store.FindAssignment(id, ct);
        public Task<ProblemWork?> FindByAssignment(Guid id, CancellationToken ct) => store.FindByAssignment(id, ct);
        public Task<ProblemWork?> FindWork(Guid id, CancellationToken ct) => store.FindWork(id, ct);
        public Task Save(ProblemWork work, long? version, CancellationToken ct) => store.SaveInTransaction(work, version, operation, ct);
    }
    private sealed class WriteAccess(LmsDbContext db, TokenAuthority token, ResourceOwnership resource,
        IAuthorityClock clock, TimeProvider time) : IAccessPolicy
    {
        public Task EnsureCanSubmit(Actor actor, ProblemAssignment assignment, CancellationToken ct) => throw new UseCaseException("Forbidden");
        public async Task EnsureCanReview(Actor actor, ProblemAssignment assignment, CancellationToken ct)
        {
            if (actor.Id != token.Identity.ActorId || actor.Role != AuthorRole.Teacher || assignment.Id != resource.AssignmentId ||
                !ScopePolicy.Allows(token.Authority, resource, ScopeAction.Review, actor.Id) ||
                !await new PostgresCurrentProcessingGate(db, clock).AllowsInTransaction(resource, actor.Id, ScopeAction.Review, ct))
                throw new UseCaseException("Forbidden");
        }
        public Task EnsureCanPostMessage(Actor actor, ProblemAssignment assignment, CancellationToken ct) => EnsureCanReview(actor, assignment, ct);
        public async Task EnsureCanUseAttachments(Actor actor, IReadOnlyList<Guid> ids, CancellationToken ct)
        {
            await new PostgresAccessPolicy(db, time).EnsureCanUseAttachments(actor, ids, ct);
            foreach (var id in ids)
            {
                var attachment = await new PostgresScopedAuthorityReader(db, time).ResolveResource(new(ResourceKind.Attachment, id), ct);
                if (attachment == null || attachment.OrganizationId != resource.OrganizationId || attachment.CourseId != resource.CourseId ||
                    !attachment.PendingAttachment || attachment.UploaderActorId != actor.Id ||
                    !await new PostgresCurrentProcessingGate(db, clock).AllowsInTransaction(attachment, actor.Id, ScopeAction.Read, ct))
                    throw new UseCaseException("AttachmentNotOwned");
            }
        }
    }
}
