using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Lms.Infrastructure.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17.6-alpine").Build();
    public Task InitializeAsync() => container.StartAsync();
    public Task DisposeAsync() => container.DisposeAsync().AsTask();
    public async Task<string> CreateDatabase()
    {
        var name = "c1_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
        await command.ExecuteNonQueryAsync();
        var connectionString = new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = name }.ConnectionString;
        await using var db = Open(connectionString);
        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        return connectionString;
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
