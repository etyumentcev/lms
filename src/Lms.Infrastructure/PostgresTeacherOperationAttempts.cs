using Lms.Application;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure;

// Minimal at-most-once attempt reservation. It grants NO domain permission, does not
// execute/resume a command, and never resolves an unknown commit as rollback. The
// first caller may execute only after this ACK and a fresh gate; every retry is status
// only, even after restart. There is intentionally no reset/renew/replacement/delete.
// Opt-in component: not registered in the legacy or public HTTP write pipeline.
public sealed class PostgresTeacherOperationAttempts(LmsDbContext db)
{
    public async Task<TeacherOperationAttempt> Reserve(TeacherOperationIntent intent,
        ApplicationDeadline original, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(original);
        if (intent.OrganizationId == Guid.Empty || intent.ActorId == Guid.Empty || intent.CommandId == Guid.Empty ||
            string.IsNullOrWhiteSpace(intent.Kind) || intent.Kind.Length > 64 ||
            string.IsNullOrWhiteSpace(intent.Resource) || intent.Resource.Length > 512 || intent.ExpectedVersion < 0 ||
            (intent.ExpectedVersion.HasValue == intent.IntentId.HasValue) || intent.IntentId == Guid.Empty ||
            intent.Fingerprint is not { Length: 64 } || intent.Fingerprint.Any(c => !char.IsAsciiHexDigit(c) || char.IsLower(c)))
            throw new UseCaseException("ValidationFailed");
        if (db.Database.CurrentTransaction != null || db.ChangeTracker.Entries().Any())
            throw new InvalidOperationException("Attempt reservation requires a dedicated context.");
        EnsureOpen(original);
        using var cancellation = new DeadlineCancellation(original, TimeProvider.System, ct);
        ct = cancellation.Token;
        var expiry = original.ExpiresAt!.Value.ToUniversalTime();
        // Round toward denial to PostgreSQL microsecond resolution, never extend the cutoff.
        expiry = new DateTimeOffset(expiry.Ticks - expiry.Ticks % 10, TimeSpan.Zero);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Existing org serialization boundary; no process-local or distributed lock service.
        var org = await db.Set<Organization>().FromSqlInterpolated(
            $"""SELECT * FROM "Organizations" WHERE "OrganizationId"={intent.OrganizationId} FOR UPDATE""").AsNoTracking().SingleOrDefaultAsync(ct);
        if (org is not { State: "Enabled", SyntheticOnly: true }) throw new UseCaseException("Forbidden");
        EnsureOpen(original);
        var previous = await db.Database.SqlQuery<AttemptRow>($"""
            SELECT "OperationId", "CommandId", "Kind", "Resource", "ExpectedVersion", "IntentId", "Fingerprint", "Deadline"
            FROM public."TeacherOperationAttempts"
            WHERE "OrganizationId"={intent.OrganizationId} AND "ActorId"={intent.ActorId}
            AND ("CommandId"={intent.CommandId} OR
              ("Kind"={intent.Kind} AND "Resource"={intent.Resource} AND
                (("ExpectedVersion" IS NOT NULL AND "ExpectedVersion"={intent.ExpectedVersion}) OR
                 ("IntentId" IS NOT NULL AND "IntentId"={intent.IntentId}))))
            """).ToListAsync(ct);
        if (previous.Count != 0)
        {
            if (previous.Count != 1 || previous[0].Kind != intent.Kind || previous[0].Resource != intent.Resource ||
                previous[0].ExpectedVersion != intent.ExpectedVersion || previous[0].IntentId != intent.IntentId || previous[0].Fingerprint != intent.Fingerprint)
                throw new UseCaseException("ConcurrencyConflict");
            ct.ThrowIfCancellationRequested();
            EnsureOpen(original);
            return new(previous[0].OperationId, previous[0].Deadline, false);
        }
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public."TeacherOperationAttempts"
            ("OperationId","OrganizationId","ActorId","CommandId","Kind","Resource","ExpectedVersion","IntentId","Fingerprint","Deadline")
            VALUES ({id},{intent.OrganizationId},{intent.ActorId},{intent.CommandId},{intent.Kind},{intent.Resource},
              {intent.ExpectedVersion},{intent.IntentId},{intent.Fingerprint},{expiry})
            """, ct);
        EnsureOpen(original);
        await tx.CommitAsync(ct);
        ct.ThrowIfCancellationRequested();
        EnsureOpen(original); // a late ACK never authorizes the first executor
        return new(id, expiry, true);
    }
    private static void EnsureOpen(ApplicationDeadline d)
    {
        var gate = d.Check(ApplicationAction.StartStage);
        if (!gate.Allowed || d.ExpiresAt == null || gate.Remaining > RevocationPolicy.MaximumOperationLifetime)
            throw new OperationCanceledException("Original operation deadline is unavailable or expired.");
    }
    private sealed class AttemptRow
    {
        public Guid OperationId { get; set; }
        public Guid CommandId { get; set; }
        public string Kind { get; set; } = "";
        public string Resource { get; set; } = "";
        public long? ExpectedVersion { get; set; }
        public Guid? IntentId { get; set; }
        public string Fingerprint { get; set; } = "";
        public DateTimeOffset Deadline { get; set; }
    }
}
