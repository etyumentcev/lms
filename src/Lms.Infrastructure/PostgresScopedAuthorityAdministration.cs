using System.Text.Json;
using Lms.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Lms.Infrastructure;

// Dedicated clean context; results are returned only after commit. No public DI registration.
// PostgreSQL owns the move protocol so the limited application role cannot bypass it with DML.
public sealed class PostgresScopedAuthorityAdministration(LmsDbContext db) : IScopedAuthorityAdministration
{
    public async Task<AuthorityChange> Change(AuthorityCommand command, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(command.Kind) || !Enum.IsDefined(command.Operation) ||
            command.Role != null && !Enum.IsDefined(command.Role.Value) || command.OrganizationId == Guid.Empty ||
            command.ActorId == Guid.Empty || command.ExpectedVersion < 0 || command.Permissions is < 0 or > 15 ||
            command.GroupId != null && string.IsNullOrWhiteSpace(command.GroupId)) throw new UseCaseException("invalid_scope");
        if (db.ChangeTracker.Entries().Any() || db.Database.CurrentTransaction != null)
            throw new InvalidOperationException("Scope mutations require a fresh dedicated DbContext.");
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var parameters = new[] {
                P("kind", NpgsqlDbType.Text, command.Kind.ToString()), P("op", NpgsqlDbType.Text, command.Operation.ToString()),
                P("org", NpgsqlDbType.Uuid, command.OrganizationId), P("actor", NpgsqlDbType.Uuid, command.ActorId),
                P("expected", NpgsqlDbType.Bigint, command.ExpectedVersion), P("membergen", NpgsqlDbType.Bigint, command.MembershipGeneration),
                P("course", NpgsqlDbType.Text, command.CourseId), P("grp", NpgsqlDbType.Text, command.GroupId),
                P("grant", NpgsqlDbType.Uuid, command.GrantId), P("role", NpgsqlDbType.Text, command.Role?.ToString()),
                P("permissions", NpgsqlDbType.Integer, command.Permissions)
            };
            var result = await db.Database.SqlQueryRaw<string>(
                "SELECT public.s3_change_authority(@kind,@op,@org,@actor,@expected,@membergen,@course,@grp,@grant,@role,@permissions)::text AS \"Value\"", parameters)
                .SingleAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            using var json = JsonDocument.Parse(result);
            var root = json.RootElement;
            return new(root.GetProperty("generation").GetInt64(), root.GetProperty("version").GetInt64(),
                root.GetProperty("deadline").ValueKind == JsonValueKind.Null ? null : root.GetProperty("deadline").GetDateTimeOffset(),
                root.GetProperty("grant").ValueKind == JsonValueKind.Null ? null : root.GetProperty("grant").GetGuid());
        }
        catch (PostgresException e) when (e.SqlState == "P0001") { throw new UseCaseException(e.MessageText); }
        catch (PostgresException e) when (e.SqlState is "40001" or "40P01" or "23505") { throw new UseCaseException("ConcurrencyConflict"); }
    }
    private static NpgsqlParameter P(string name, NpgsqlDbType type, object? value) => new(name, type) { Value = value ?? DBNull.Value };
}
