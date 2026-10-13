using Lms.Application;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Tests;

public sealed partial class ScopedAuthorityTests
{
    private static TeacherOperationIntent Intent() => new(A, T, Guid.NewGuid(), "Review", "work:synthetic", 1, new string('A',64));
    [Fact]
    public async Task Bounded_retry_new_request_restart_new_key_and_refresh_never_create_second_attempt()
    {
        var cs = await Seed(); var source = new RevocationClock(); var time = source.Create(); var ingress = new RevocationIngress(time);
        var intent = Intent();
        async Task<TeacherOperationAttempt> Reserve(TeacherOperationIntent candidate)
        {
            await using var db = PostgresFixture.Open(cs); // fresh context also models a restarted caller
            return await new PostgresTeacherOperationAttempts(db).Reserve(candidate, ingress.Arrive(ApplicationRequestKind.TeacherOperation));
        }
        var original = await Reserve(intent); Assert.True(original.FirstAttempt);
        source.Elapsed = TimeSpan.FromMinutes(20);
        foreach (var request in new[] { intent, intent with { CommandId = Guid.NewGuid() } })
        {
            var retry = await Reserve(request); Assert.False(retry.FirstAttempt);
            Assert.Equal(original.OperationId, retry.OperationId); Assert.Equal(original.Deadline, retry.Deadline);
        }
        source.Elapsed = TimeSpan.FromMinutes(31); // a new token or deadline cannot renew this logical intent
        var late = await Reserve(intent); Assert.False(late.FirstAttempt); Assert.Equal(original.Deadline, late.Deadline);
        await Assert.ThrowsAsync<UseCaseException>(() => Reserve(intent with { Fingerprint = new string('B',64) }));
    }
    [Fact]
    public async Task Bounded_parallel_attempts_have_one_executor_and_immutable_identity()
    {
        var cs = await Seed(); var time = new RevocationClock().Create(); var intent = Intent();
        async Task<TeacherOperationAttempt> Reserve()
        {
            await using var db = PostgresFixture.Open(cs);
            return await new PostgresTeacherOperationAttempts(db).Reserve(intent, new RevocationIngress(time).Arrive(ApplicationRequestKind.TeacherOperation));
        }
        var results = await Task.WhenAll(Reserve(), Reserve()); Assert.Single(results, x => x.FirstAttempt);
        Assert.Equal(results[0].OperationId, results[1].OperationId);
        await using var mutation = PostgresFixture.Open(cs);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => mutation.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE public."TeacherOperationAttempts" SET "Deadline"="Deadline"+interval '1 hour' WHERE "OperationId"={results[0].OperationId}"""));
    }
    [Fact]
    public async Task Bounded_attempt_late_commit_ack_never_starts_executor_or_reopens_on_retry()
    {
        var cs = await Seed(); var source = new RevocationClock(); var time = source.Create(); var intent = Intent();
        await using (var db = PostgresFixture.Open(cs, new AfterRefreshCommit(() => source.Elapsed = TimeSpan.FromMinutes(30))))
            await Assert.ThrowsAsync<OperationCanceledException>(() => new PostgresTeacherOperationAttempts(db)
                .Reserve(intent, new RevocationIngress(time).Arrive(ApplicationRequestKind.TeacherOperation)));
        await using var retry = PostgresFixture.Open(cs);
        var result = await new PostgresTeacherOperationAttempts(retry).Reserve(intent,
            new RevocationIngress(time).Arrive(ApplicationRequestKind.TeacherOperation));
        Assert.False(result.FirstAttempt); Assert.Equal(At.AddMinutes(30), result.Deadline);
    }
    [Fact]
    public async Task Bounded_attempt_lost_commit_ack_does_not_replay_after_reconnect()
    {
        var cs = await Seed(); var time = new RevocationClock().Create(); var intent = Intent();
        await using (var proxy = new DropCommitAcknowledgement(cs))
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await using var db = PostgresFixture.Open(proxy.ConnectionString);
            await Assert.ThrowsAnyAsync<Exception>(() => new PostgresTeacherOperationAttempts(db)
                .Reserve(intent, new RevocationIngress(time).Arrive(ApplicationRequestKind.TeacherOperation), cancellation.Token));
            await proxy.Dropped.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        await using var retry = PostgresFixture.Open(cs);
        var result = await new PostgresTeacherOperationAttempts(retry).Reserve(intent,
            new RevocationIngress(time).Arrive(ApplicationRequestKind.TeacherOperation));
        Assert.False(result.FirstAttempt);
    }
    [Fact]
    public async Task Bounded_migration_preserves_old_authority_history_and_uses_no_new_lease()
    {
        var cs = await fixture.CreateDatabase("20261011120000_ProcessingWriterGuardC2G");
        await using (var seed = PostgresFixture.Open(cs))
        {
            seed.Add(new Organization { OrganizationId = A, State = "Enabled", SyntheticOnly = true });
            seed.Add(new ActorRecord { ActorId = T }); await seed.SaveChangesAsync();
        }
        var membership = new AuthorityCommand(AuthorityKind.Membership, AuthorityOperation.Grant, A, T, 0, Role: ScopeRole.Teacher);
        await Change(cs, membership);
        var old = await Change(cs, membership with { Operation = AuthorityOperation.Revoke, ExpectedVersion = 1 });
        var before = await Snapshot(cs, "OrganizationMemberships", "AuthorityHistory", "SecurityAudit");
        await using (var migrate = PostgresFixture.Open(cs)) await migrate.Database.MigrateAsync();
        Assert.Equal(before, await Snapshot(cs, "OrganizationMemberships", "AuthorityHistory", "SecurityAudit"));
        await Change(cs, membership with { ExpectedVersion = 2 });
        var current = await Change(cs, membership with { Operation = AuthorityOperation.Revoke, ExpectedVersion = 3 });
        await using var verify = PostgresFixture.Open(cs);
        var row = await verify.Set<OrganizationMembership>().SingleAsync();
        Assert.Equal(row.RevocationRequestedAt, current.AccessValidUntil);
        Assert.True(old.AccessValidUntil > current.AccessValidUntil!.Value.AddMinutes(119));
        Assert.Equal(current, await Change(cs, membership with { Operation = AuthorityOperation.Revoke, ExpectedVersion = 4 }));
    }

    [Fact]
    public async Task Bounded_attempt_persists_absolute_instant_not_local_clock_ticks()
    {
        var cs = await Seed(); var source = new RevocationClock { UtcOffset = TimeSpan.FromHours(6) };
        var deadline = new RevocationIngress(source.Create()).Arrive(ApplicationRequestKind.TeacherOperation);
        await using var db = PostgresFixture.Open(cs);
        var result = await new PostgresTeacherOperationAttempts(db).Reserve(Intent(), deadline);
        Assert.True(result.FirstAttempt); Assert.Equal(At.AddMinutes(30), result.Deadline);
        Assert.Equal(TimeSpan.Zero, result.Deadline.Offset);
    }

}
