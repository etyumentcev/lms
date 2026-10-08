using System.Text.Json;
using System.Data;
using Lms.Application;
using Lms.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lms.Infrastructure;

// One scoped store/context per unit of work. Never share concurrently.
public sealed class PostgresWorkStore(LmsDbContext db) : IWorkStore
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

    public async Task Save(ProblemWork work, long? expectedPersistedVersion, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
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
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
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

    private static Guid EventId(DomainEvent item) => item switch
    {
        SolutionSubmitted submitted => submitted.SubmissionId,
        RevisionRequested revision => revision.ReviewId,
        SubmissionAccepted accepted => accepted.ReviewId,
        MessagePosted message => message.MessageId,
        _ => throw new NotSupportedException($"Unmapped domain event: {item.GetType().Name}")
    };
}
