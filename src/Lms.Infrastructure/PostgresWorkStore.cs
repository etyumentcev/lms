using System.Text.Json;
using System.Data;
using Lms.Application;
using Lms.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lms.Infrastructure;

// One scoped store/context per unit of work. Never share concurrently.
public sealed class PostgresWorkStore(LmsDbContext db, ApplicationDeadline? deadline = null, TimeProvider? time = null) : IWorkStore
{
    public Task<ProblemAssignment?> FindAssignment(Guid assignmentId, CancellationToken cancellationToken)
        => db.Assignments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == assignmentId, cancellationToken);

    // Separate collection queries avoid multiplying submissions × reviews × messages.
    // Every load shares a repeatable-read snapshot (or the caller's existing read transaction).
    private IQueryable<ProblemWork> Aggregate => db.Works.AsNoTracking().AsSplitQuery()
        .Include(x => x.Submissions.OrderBy(s => s.AttemptNumber))
        .Include(x => x.Reviews.OrderBy(r => db.Set<Submission>().Where(s => s.Id == r.SubmissionId).Select(s => s.AttemptNumber).First()))
        .Include(x => x.Messages.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id));

    public Task<ProblemWork?> FindByAssignment(Guid assignmentId, CancellationToken cancellationToken)
        => Load(Aggregate.Where(x => x.ProblemAssignmentId == assignmentId), cancellationToken);
    public Task<ProblemWork?> FindWork(Guid workId, CancellationToken cancellationToken)
        => Load(Aggregate.Where(x => x.Id == workId), cancellationToken);

    private async Task<ProblemWork?> Load(IQueryable<ProblemWork> query, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) return await query.SingleOrDefaultAsync(ct);
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        return await query.SingleOrDefaultAsync(ct);
    }

    public Task Save(ProblemWork work, long? expectedPersistedVersion, CancellationToken cancellationToken)
        => SaveCore(work, expectedPersistedVersion, cancellationToken, null);

    internal Task SaveInTransaction(ProblemWork work, long? expectedPersistedVersion, TeacherOperationContext operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!ReferenceEquals(deadline, operation.Deadline)) throw new UseCaseException("Forbidden");
        return SaveCore(work, expectedPersistedVersion, cancellationToken, operation);
    }

    private async Task SaveCore(ProblemWork work, long? expectedPersistedVersion, CancellationToken cancellationToken, TeacherOperationContext? operation)
    {
        var externalTransaction = operation != null;
        operation?.EnterPersistence(work);
        // A domain aggregate is not proof of teacher authorization. Even internal direct
        // Save callers cannot persist newly created teacher history via the student port.
        if (!externalTransaction && work.Events.Any(e => e is RevisionRequested or SubmissionAccepted ||
            e is MessagePosted m && work.Messages.Any(x => x.Id == m.MessageId && x.AuthorRole == AuthorRole.Teacher)))
            throw new UseCaseException("Forbidden");
        EnsureDeadline(ApplicationAction.DomainDecision);
        using var timeout = deadline == null ? null : new DeadlineCancellation(deadline, TimeProvider.System, cancellationToken);
        cancellationToken = timeout?.Token ?? cancellationToken;
        if (externalTransaction && (db.Database.CurrentTransaction == null || deadline == null))
            throw new InvalidOperationException("Trusted transaction and original deadline required.");
        await using var transaction = externalTransaction ? null : await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (expectedPersistedVersion is null)
                db.Works.Add(work);
            else
            {
                // UPDATE takes the row lock; PostgreSQL rechecks the predicate after a concurrent commit.
                var changed = await db.Works.Where(x => x.Id == work.Id && x.Version == expectedPersistedVersion)
                    .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, work.Status)
                        .SetProperty(x => x.Version, work.Version).SetProperty(x => x.AcceptedAt, work.AcceptedAt), cancellationToken);
                if (changed != 1) throw new UseCaseException("ConcurrencyConflict");
                var submissions = await db.Set<Submission>().Where(x => x.ProblemWorkId == work.Id).Select(x => x.Id).ToListAsync(cancellationToken);
                var reviews = await db.Set<Review>().Where(x => x.ProblemWorkId == work.Id).Select(x => x.Id).ToListAsync(cancellationToken);
                var messages = await db.Set<Message>().Where(x => x.ProblemWorkId == work.Id).Select(x => x.Id).ToListAsync(cancellationToken);
                db.AddRange(work.Submissions.Where(x => !submissions.Contains(x.Id)));
                db.AddRange(work.Messages.Where(x => !messages.Contains(x.Id)));
                db.AddRange(work.Reviews.Where(x => !reviews.Contains(x.Id)));
            }
            // Each event is emitted once for its immutable entity. Stable identity also handles
            // saving the same aggregate through a fresh store, without modifying the domain list.
            var persistedEvents = await db.Events.Where(x => x.ProblemWorkId == work.Id).Select(x => x.Id).ToListAsync(cancellationToken);
            var newEvents = work.Events.Where(x => !persistedEvents.Contains(EventId(x))).ToArray();
            for (var i = 0; i < newEvents.Length; i++)
            {
                var item = newEvents[i];
                db.Events.Add(new StoredDomainEvent
                {
                    Id = EventId(item), ProblemWorkId = work.Id, Version = work.Version, Ordinal = i,
                    Type = item.GetType().Name, Payload = JsonSerializer.Serialize(item, item.GetType()), OccurredAt = item.OccurredAt
                });
            }
            await CaptureAttachmentScopes(work, newEvents, cancellationToken);
            EnsureDeadline(ApplicationAction.DomainDecision);
            await db.SaveChangesAsync(cancellationToken);
            if (externalTransaction) return; // caller owns the only COMMIT and result gate
            if (deadline == null) await transaction!.CommitAsync(cancellationToken);
            else
            {
                Task? pending = null;
                var dispatch = deadline.DispatchCommit(() => pending = transaction!.CommitAsync(cancellationToken));
                if (!dispatch.Allowed) throw new OperationCanceledException("Operation deadline closed before COMMIT.");
                await pending!; // never detach COMMIT or infer rollback from cancellation
                deadline.AcknowledgeCommit();
            }
            cancellationToken.ThrowIfCancellationRequested();
            EnsureDeadline(ApplicationAction.ResponseEnqueue);
            // No external publication; only committed journal rows are visible to future readers.
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "P0001" } pg &&
            pg.MessageText is "AttachmentNotOwned" or "AttachmentAlreadyAttached")
        {
            throw new UseCaseException(((PostgresException)ex.InnerException!).MessageText);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new UseCaseException("ConcurrencyConflict");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new UseCaseException("ConcurrencyConflict");
        }
        finally
        {
            db.ChangeTracker.Clear(); // Discard tracked state after success or rollback.
        }
    }

    private async Task CaptureAttachmentScopes(ProblemWork work, IReadOnlyList<DomainEvent> newEvents, CancellationToken ct)
    {
        var claims = new Dictionary<Guid, Guid>();
        foreach (var item in newEvents)
        {
            if (item is SolutionSubmitted submitted)
            {
                var submission = work.Submissions.Single(x => x.Id == submitted.SubmissionId);
                foreach (var attachment in submission.AttachmentIds) claims.Add(attachment, submission.SubmittedBy);
            }
            else if (item is MessagePosted posted)
            {
                var message = work.Messages.Single(x => x.Id == posted.MessageId);
                foreach (var attachment in message.AttachmentIds) claims.Add(attachment, message.AuthorId);
            }
        }
        if (claims.Count == 0) return;

        // Scope promotion is an explicitly composed current-protocol capability. Direct
        // legacy stores do not receive a qualified TimeProvider and remain fail-closed;
        // in particular, merely claiming an unscoped file cannot invent provenance.
        if (time == null) return;

        // Historical migration regression tests intentionally execute this store against
        // pre-M1 schemas. Attachment scope promotion was introduced by SchemaFoundationsM1;
        // keep the legacy aggregate path byte-for-byte compatible until that table exists.
        var scopeSchemaExists = await db.Database.SqlQueryRaw<bool>(
            "SELECT to_regclass('public.\"AttachmentScopes\"') IS NOT NULL AS \"Value\"")
            .SingleAsync(ct);
        if (!scopeSchemaExists) return;

        var scoped = await db.Set<AttachmentScope>().Where(x => claims.Keys.Contains(x.AttachmentId))
            .Select(x => x.AttachmentId).ToListAsync(ct);
        foreach (var id in scoped) claims.Remove(id);
        if (claims.Count == 0) return;

        // New scoped assignments can safely promote a student-owned pending upload at claim
        // time: every affiliation below is derived from immutable/server-owned rows. Legacy
        // assignments remain untouched and are never made visible to the protected read path.
        var assignment = await db.Set<AssignmentScope>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.AssignmentId == work.ProblemAssignmentId, ct);
        if (assignment == null) return;
        var now = time.GetUtcNow();
        var bindings = await (from candidate in db.Set<CourseContextBinding>().AsNoTracking()
            join context in db.Set<ProcessingContextVersion>().AsNoTracking()
                on new { candidate.ContextId, Version = candidate.ContextVersion, candidate.OrganizationId }
                equals new { context.ContextId, context.Version, context.OrganizationId }
            where candidate.CourseId == assignment.CourseId && candidate.OrganizationId == assignment.OrganizationId &&
                candidate.EffectiveFrom <= now && (candidate.EffectiveTo == null || candidate.EffectiveTo > now) &&
                context.SyntheticOnly && context.ApprovalState == "SyntheticOnly"
            select candidate).ToListAsync(ct);
        if (bindings.Count != 1) throw new UseCaseException("Forbidden");

        var owners = await db.StoredAttachments.AsNoTracking().Where(x => claims.Keys.Contains(x.Id) && !x.Deleted)
            .Select(x => new { x.Id, x.OwnerId }).ToListAsync(ct);
        if (owners.Count != claims.Count || owners.Any(x => claims[x.Id] != x.OwnerId))
            throw new UseCaseException("AttachmentNotOwned");
        var binding = bindings[0];
        db.AddRange(claims.Select(x => new AttachmentScope
        {
            AttachmentId = x.Key,
            OrganizationId = assignment.OrganizationId,
            CourseId = assignment.CourseId,
            UploaderActorId = x.Value,
            ContextId = binding.ContextId,
            ContextVersion = binding.ContextVersion
        }));
    }

    private void EnsureDeadline(ApplicationAction action)
    {
        if (deadline != null && !deadline.Check(action).Allowed)
            throw new OperationCanceledException("Original operation deadline expired.");
    }

    private static Guid EventId(DomainEvent item) => item switch
    {
        SolutionSubmitted submitted => submitted.SubmissionId,
        RevisionRequested revision => revision.ReviewId,
        SubmissionAccepted accepted => accepted.ReviewId,
        MessagePosted message => message.MessageId,
        _ => throw new NotSupportedException($"Unmapped domain event: {item.GetType().Name}")
    };
}
