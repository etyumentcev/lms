using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Lms.Infrastructure.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17.6-alpine").Build();
    public Task InitializeAsync() => container.StartAsync();
    public Task DisposeAsync() => container.DisposeAsync().AsTask();
    public async Task<string> CreateDatabase(string? migration = null)
    {
        var name = "c1_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
        await command.ExecuteNonQueryAsync();
        var connectionString = new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = name }.ConnectionString;
        await using var db = Open(connectionString);
        await db.GetService<IMigrator>().MigrateAsync(migration);
        if (migration is null) Assert.False(db.Database.HasPendingModelChanges());
        return connectionString;
    }
    public async Task<string> Backup(string connectionString)
    {
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database!;
        Assert.StartsWith("c1_", database);
        var path = "/tmp/" + database + ".dump";
        var result = await container.ExecAsync(["pg_dump", "-U", "postgres", "-Fc", "-f", path, database]);
        Assert.True(result.ExitCode == 0, result.Stderr);
        return path;
    }
    public async Task<string> Restore(string backupPath)
    {
        Assert.StartsWith("/tmp/c1_", backupPath);
        var name = "c1_" + Guid.NewGuid().ToString("N");
        var created = await container.ExecAsync(["createdb", "-U", "postgres", name]);
        Assert.True(created.ExitCode == 0, created.Stderr);
        var restored = await container.ExecAsync(["pg_restore", "-U", "postgres", "--exit-on-error", "-d", name, backupPath]);
        Assert.True(restored.ExitCode == 0, restored.Stderr);
        return new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = name }.ConnectionString;
    }
    public static LmsDbContext Open(string connectionString, params IInterceptor[] interceptors)
        => new(new DbContextOptionsBuilder<LmsDbContext>().UseNpgsql(connectionString).AddInterceptors(interceptors).Options);
}

// Both independent connections must have begun real transactions before either can write.
public sealed class TransactionBarrier : DbTransactionInterceptor
{
    private int arrivals;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
        TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        // Synchronize write transactions only; aggregate reads now own a snapshot.
        if (result.IsolationLevel == System.Data.IsolationLevel.RepeatableRead) return result;
        if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        return result;
    }
}

public sealed class FailAfterWrites : SaveChangesInterceptor
{
    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default) => throw new InvalidOperationException("Injected before commit");
}

// Acknowledgement-loss after commit is a different case; this injects before DB commit.
public sealed class FailAtCommit : DbTransactionInterceptor
{
    public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
        TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Injected at commit boundary");
}

public sealed class BetweenHistoryQueries(Func<Task> write) : DbCommandInterceptor
{
    private int queries;
    public bool Triggered { get; private set; }
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (++queries == 2) { await write(); Triggered = true; }
        return result;
    }
}
