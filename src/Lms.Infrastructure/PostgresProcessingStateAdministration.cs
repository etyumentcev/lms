using System.Data;
using System.Text.Json;
using Lms.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Lms.Infrastructure;

// Isolated capability: intentionally absent from production DI. No implicit retries, including
// transport errors at COMMIT. Reconcile OperationId before retrying an unknown outcome.
public sealed class PostgresProcessingStateAdministration(LmsDbContext db) : IProcessingStateAdministration
{
    public async Task<long> Change(ProcessingStateCommand command, CancellationToken cancellationToken = default)
    {
        var (action, payload) = Describe(command);
        PostgresProcessingWriteBoundary.RequireClean(db);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var revision = await db.Database.SqlQueryRaw<long>(
            "SELECT public.processing_change(@org,@operation,@expected,@action,@payload) AS \"Value\"",
            new NpgsqlParameter("org", command.OrganizationId), new NpgsqlParameter("operation", command.OperationId),
            new NpgsqlParameter("expected", command.ExpectedRevision), new NpgsqlParameter("action", action),
            new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = payload }).SingleAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return revision;
    }
    public async Task<long?> FindCommittedResult(ProcessingStateCommand command, CancellationToken cancellationToken = default)
    {
        var (action, payload) = Describe(command);
        PostgresProcessingWriteBoundary.RequireClean(db);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var revision = await db.Database.SqlQueryRaw<long?>(
            "SELECT public.processing_find_command(@org,@operation,@expected,@action,@payload) AS \"Value\"",
            new NpgsqlParameter("org", command.OrganizationId), new NpgsqlParameter("operation", command.OperationId),
            new NpgsqlParameter("expected", command.ExpectedRevision), new NpgsqlParameter("action", action),
            new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = payload }).SingleAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return revision;
    }

    private static (string Action, string Payload) Describe(ProcessingStateCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.OrganizationId == Guid.Empty || command.OperationId == Guid.Empty || command.ExpectedRevision < 0)
            throw new ArgumentException("Invalid processing command.", nameof(command));
        var action = command.Mutation switch
        {
            AddProcessingContext => "AddContext",
            OpenProcessingBinding => "OpenBinding",
            CloseProcessingBinding => "CloseBinding",
            ReplaceProcessingBinding => "ReplaceBinding",
            AddProcessingRestriction => "AddRestriction",
            WithdrawProcessingRestriction => "WithdrawRestriction",
            SetProcessingOrganizationState => "SetOrganizationState",
            _ => throw new ArgumentException("Unsupported processing mutation.", nameof(command))
        };

        // Runtime type serialization avoids silently dropping derived record fields.
        return (action, JsonSerializer.Serialize(command.Mutation, command.Mutation.GetType()));
    }

}
