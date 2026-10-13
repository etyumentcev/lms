using System.Data;
using Lms.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Lms.Infrastructure;

// Future private writer composition point, not ImportAttempt storage or an authorization adapter.
// The callback must use this DbContext, perform DB-only work, and obey the same lock order.
// Any SQL function exposed to its DB principal must independently enforce the protocol: a CLR
// callback is not a privilege boundary. No production registration, no automatic retries.
public sealed class PostgresProcessingWriteBoundary(LmsDbContext db)
{
    public async Task<T> Execute<T>(ProcessingWriteContext context, long expectedRevision,
        Func<CancellationToken, Task<T>> mutate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(mutate);
        if (context.OrganizationId == Guid.Empty || context.ContextId == Guid.Empty || context.ContextVersion <= 0 ||
            string.IsNullOrWhiteSpace(context.CourseId) || expectedRevision < 0 || context.Subjects is not { Count: > 0 and <= 256 } ||
            context.Subjects.Any(s => s == Guid.Empty)) throw new ArgumentException("Incomplete processing evidence.", nameof(context));
        RequireClean(db);
        var subjects = context.Subjects.Distinct().ToArray();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SELECT public.processing_begin_write(@org,@expected)",
            [new NpgsqlParameter("org", context.OrganizationId), new NpgsqlParameter("expected", expectedRevision)], cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SELECT public.processing_assert_current(@org,@course,@context,@version,@binding,@subjects)",
            [new NpgsqlParameter("org", context.OrganizationId), new NpgsqlParameter("course", context.CourseId),
             new NpgsqlParameter("context", context.ContextId), new NpgsqlParameter("version", context.ContextVersion),
             new NpgsqlParameter("binding", NpgsqlDbType.TimestampTz) { Value = context.BindingFrom.ToUniversalTime() },
             new NpgsqlParameter("subjects", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = subjects }], cancellationToken);
        var result = await mutate(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return result;
    }

    internal static void RequireClean(LmsDbContext db)
    {
        if (db.Database.CurrentTransaction != null || db.ChangeTracker.Entries().Any())
            throw new InvalidOperationException("Processing writes require a fresh dedicated DbContext.");
    }
}
