using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Lms.Infrastructure.Tests;

public sealed class QualifiedClockTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-09T12:00:00Z");
    private sealed class Source : IQualifiedClockSource
    {
        public QualifiedClockReading? Value = new("synthetic-epoch", true, new(At, At), TimeSpan.Zero, TimeSpan.Zero);
        public bool Throw;
        public QualifiedClockReading? Read() => Throw ? throw new InvalidOperationException("synthetic failure") : Value;
    }
    [Fact]
    public void Deadline_counts_uncertain_monotonic_waits_and_never_restarts()
    {
        var source = new Source(); var clock = new QualifiedAuthorityClock(source, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
        var deadline = clock.StartDeadline(TimeSpan.FromSeconds(10))!;
        source.Value = source.Value! with { Utc = new(At.AddSeconds(8), At.AddSeconds(9)), MonotonicEarliest = TimeSpan.FromSeconds(8), MonotonicLatest = TimeSpan.FromSeconds(9) };
        Assert.Equal(TimeSpan.FromSeconds(1), deadline.Remaining());
        // UTC correction cannot restore budget.
        source.Value = source.Value with { Utc = new(At.AddSeconds(8), At.AddSeconds(10)), MonotonicEarliest = TimeSpan.FromSeconds(9), MonotonicLatest = TimeSpan.FromSeconds(10) };
        Assert.Equal(TimeSpan.Zero, deadline.Remaining());
        Assert.Null(clock.StartDeadline(TimeSpan.Zero));
    }
    [Theory]
    [InlineData("missing")] [InlineData("exception")] [InlineData("unqualified")] [InlineData("epoch")]
    [InlineData("utc-width")] [InlineData("utc-inverted")] [InlineData("mono-width")]
    [InlineData("mono-negative")] [InlineData("mono-inverted")] [InlineData("mono-regression")] [InlineData("utc-regression")] [InlineData("utc-stalled")] [InlineData("utc-jump")]
    public void Loss_of_qualification_latches_closed_and_invalidates_deadline(string fault)
    {
        var source = new Source(); source.Value = source.Value! with { MonotonicEarliest = TimeSpan.FromSeconds(2), MonotonicLatest = TimeSpan.FromSeconds(2) };
        var clock = new QualifiedAuthorityClock(source, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
        var deadline = clock.StartDeadline(TimeSpan.FromSeconds(10))!; var good = source.Value;
        source.Value = fault switch
        {
            "missing" => null,
            "unqualified" => good with { Qualified = false },
            "epoch" => good with { QualificationId = "new" },
            "utc-width" => good with { Utc = new(At, At.AddSeconds(3)) },
            "utc-inverted" => good with { Utc = new(At.AddTicks(1), At) },
            "mono-width" => good with { MonotonicLatest = TimeSpan.FromSeconds(4) },
            "mono-negative" => good with { MonotonicEarliest = TimeSpan.FromTicks(-1) },
            "mono-inverted" => good with { MonotonicLatest = TimeSpan.FromSeconds(1) },
            "mono-regression" => good with { MonotonicEarliest = TimeSpan.FromSeconds(1), MonotonicLatest = TimeSpan.FromSeconds(1) },
            "utc-stalled" => good with { MonotonicEarliest = TimeSpan.FromSeconds(3), MonotonicLatest = TimeSpan.FromSeconds(3) },
            "utc-jump" => good with { Utc = new(At.AddSeconds(1), At.AddSeconds(1)) },
            "utc-regression" => good with { Utc = new(At.AddTicks(-1), At.AddTicks(-1)) },
            _ => good
        };
        source.Throw = fault == "exception";
        Assert.Null(clock.Read()); Assert.Null(deadline.Remaining());
        source.Throw = false; source.Value = good;
        Assert.Null(clock.Read()); Assert.Null(clock.StartDeadline(TimeSpan.FromSeconds(1)));
    }
    [Fact]
    public void Expiry_handles_extremes_and_exact_AccessValidUntil_cutoff()
    {
        Assert.Null(ComponentExpiry.Calculate(new(DateTimeOffset.MaxValue, DateTimeOffset.MaxValue), DateTimeOffset.MaxValue, []));
        Assert.Null(ComponentExpiry.Calculate(new(At, At), At.AddHours(1), [At.AddMinutes(10)]));
        Assert.Null(ComponentExpiry.Calculate(new(At, At), At.AddHours(1), [At.AddMinutes(10).AddTicks(1)]));
        Assert.Equal(At.AddSeconds(1), ComponentExpiry.Calculate(new(At, At), At.AddHours(1), [At.AddMinutes(10).AddSeconds(1)]));
        Assert.Null(ComponentExpiry.Calculate(new(At, At.AddSeconds(1)), At.AddHours(1), [At.AddMinutes(10).AddSeconds(1)]));
    }
}

public sealed partial class ScopedAuthorityTests
{
    [Theory]
    [InlineData("rollback")] [InlineData("before-commit")] [InlineData("backend-termination")]
    [InlineData("lost-ack")] [InlineData("cancel-after-commit")]
    public async Task A2_signed_token_never_escapes_failed_transaction_boundary(string failure)
    {
        var cs = await Seed(); var identity = await IssuingIdentity(cs); var time = new ComponentClock();
        using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "synthetic", "component", time);
        using var cancel = new CancellationTokenSource();
        var fault = new IssuanceBoundary(failure, cs, cancel);
        await using var db = PostgresFixture.Open(cs, fault);
        AccessToken? escaped = null;
        await Assert.ThrowsAnyAsync<Exception>(async () => escaped = await new PostgresComponentTokenIssuer(db, time, jwt)
            .Issue(identity.User, identity.Session, A, cancel.Token));
        Assert.True(fault.BeforeReached); Assert.Null(escaped);
        // Marker written in the same transaction distinguishes committed-but-no-result
        // from rollback. Unknown must never be equated with rolled back.
        await using var read = PostgresFixture.Open(cs);
        var version = (await read.Set<AccountSecurityState>().SingleAsync(x => x.UserId == identity.User)).Version;
        Assert.Equal(failure is "lost-ack" or "cancel-after-commit" ? 2 : 1, version);
    }
    [Theory]
    [InlineData("clock")] [InlineData("expired")] [InlineData("key")]
    public async Task A2_rechecks_clock_lifetime_and_trust_after_commit(string failure)
    {
        var cs = await Seed(); var identity = await IssuingIdentity(cs); var time = new ComponentClock();
        using var key = RSA.Create(2048); var keys = new ComponentSigningKeys(); var id = keys.Rotate(key);
        var jwt = new ComponentJwt(keys, "synthetic", "component", time);
        var observer = new AfterIssuanceCommit(() =>
        {
            if (failure == "clock") time.Time = null;
            if (failure == "expired") time.Time = new(At.AddMinutes(15), At.AddMinutes(15));
            if (failure == "key") keys.RemoveTrust(id);
        });
        await using var db = PostgresFixture.Open(cs, observer);
        Assert.Null(await new PostgresComponentTokenIssuer(db, time, jwt).Issue(identity.User, identity.Session, A));
        Assert.True(observer.Reached);
    }
    [Fact]
    public async Task A2_result_waits_for_commit_completion()
    {
        var cs = await Seed(); var identity = await IssuingIdentity(cs); var time = new ComponentClock();
        using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "synthetic", "component", time);
        var barrier = new IssuanceCommitBarrier(); await using var db = PostgresFixture.Open(cs, barrier);
        var pending = new PostgresComponentTokenIssuer(db, time, jwt).Issue(identity.User, identity.Session, A);
        try { await barrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(pending.IsCompleted); }
        finally { barrier.Release.TrySetResult(); }
        Assert.NotNull(await pending);
    }
    [Fact]
    public async Task A2_key_rotation_overlap_stop_signing_and_terminal_distrust()
    {
        var cs = await Seed(); var identity = await IssuingIdentity(cs); var time = new ComponentClock();
        using var first = RSA.Create(2048); using var second = RSA.Create(2048);
        var keys = new ComponentSigningKeys(); var firstId = keys.Rotate(first); var jwt = new ComponentJwt(keys, "synthetic", "component", time);
        async Task<AccessToken?> Issue()
        {
            await using var db = PostgresFixture.Open(cs);
            return await new PostgresComponentTokenIssuer(db, time, jwt).Issue(identity.User, identity.Session, A);
        }
        var old = (await Issue())!; var secondId = keys.Rotate(second); var current = (await Issue())!;
        Assert.NotEqual(firstId, secondId); Assert.NotNull(jwt.Validate(old)); Assert.NotNull(jwt.Validate(current));
        keys.StopSigning(); Assert.Null(await Issue()); Assert.NotNull(jwt.Validate(current));
        keys.RemoveTrust(firstId); Assert.Null(jwt.Validate(old)); Assert.NotNull(jwt.Validate(current));
        Assert.Throws<InvalidOperationException>(() => keys.Rotate(first));
        keys.RemoveTrust(secondId); Assert.Null(jwt.Validate(current)); Assert.Null(await Issue());
        Assert.DoesNotContain("PRIVATE", JsonSerializer.Serialize(keys));
    }
    [Fact]
    public async Task A2_missing_unknown_or_substituted_kid_and_disposed_key_deny()
    {
        var cs = await Seed(); var identity = await IssuingIdentity(cs); var time = new ComponentClock();
        using var key = RSA.Create(2048); using var other = RSA.Create(2048);
        var keys = new ComponentSigningKeys(); keys.Rotate(key); var jwt = new ComponentJwt(keys, "synthetic", "component", time);
        await using var db = PostgresFixture.Open(cs);
        var original = (await new PostgresComponentTokenIssuer(db, time, jwt).Issue(identity.User, identity.Session, A))!;
        var parts = original.Reveal().Split('.');
        foreach (var header in new object[] { new { alg = "RS256", typ = "JWT" },
            new { alg = "RS256", typ = "JWT", kid = ComponentSigningKeys.KeyId(other) },
            new { alg = "HS256", typ = "JWT", kid = ComponentSigningKeys.KeyId(key) },
            new { alg = "RS256", typ = "JWT", kid = ComponentSigningKeys.KeyId(key), jku = "https://invalid.invalid" } })
        {
            var data = B64A2(JsonSerializer.SerializeToUtf8Bytes(header)) + "." + parts[1];
            var token = new AccessToken(data + "." + B64A2(key.SignData(Encoding.ASCII.GetBytes(data), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
            Assert.Null(jwt.Validate(token));
        }
        key.Dispose(); Assert.Null(jwt.Validate(original));
        await using var fresh = PostgresFixture.Open(cs);
        Assert.Null(await new PostgresComponentTokenIssuer(fresh, time, jwt).Issue(identity.User, identity.Session, A));
    }
    private static string B64A2(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal sealed class IssuanceBoundary(string failure, string cs, CancellationTokenSource cancel) : DbTransactionInterceptor
{
    public bool BeforeReached;
    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
        TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        BeforeReached = true;
        await using var marker = transaction.Connection!.CreateCommand(); marker.Transaction = transaction;
        marker.CommandText = "UPDATE \"AccountSecurityState\" SET \"Version\"=\"Version\"+1";
        await marker.ExecuteNonQueryAsync(cancellationToken);
        if (failure == "rollback") { await transaction.RollbackAsync(cancellationToken); throw new IOException("Synthetic rollback after signing"); }
        if (failure == "before-commit") throw new IOException("Synthetic commit failure");
        if (failure == "backend-termination")
        {
            var pid = ((NpgsqlConnection)transaction.Connection!).ProcessID;
            await using var killer = new NpgsqlConnection(cs); await killer.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT pg_terminate_backend(@pid)", killer); command.Parameters.AddWithValue("pid", pid);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        return result;
    }
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        // Real PG commit, synthetic loss of acknowledgement at the application boundary.
        if (failure == "lost-ack") throw new IOException("Synthetic lost commit acknowledgement; outcome unknown to issuer");
        if (failure == "cancel-after-commit") cancel.Cancel();
        return Task.CompletedTask;
    }
}
internal sealed class AfterIssuanceCommit(Action action) : DbTransactionInterceptor
{
    public bool Reached;
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default) { Reached = true; action(); return Task.CompletedTask; }
}
internal sealed class IssuanceCommitBarrier : DbTransactionInterceptor
{
    public TaskCompletionSource Reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
        TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    { Reached.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken); return result; }
}
