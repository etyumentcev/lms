using System.Data.Common;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.Json;
using Lms.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Lms.Infrastructure.Tests;

public sealed class IdentitySessionTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly Guid OrgA = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid OrgB = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid User = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid Actor = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private const string Password = "Synthetic-Password-Canary-2039!";
    private readonly TestClock clock = new();
    private async Task<string> Seed(bool active = true, string? migration = null)
    {
        var cs = await fixture.CreateDatabase(migration);
        await using (var db = PostgresFixture.Open(cs))
        {
            db.AddRange(new Organization { OrganizationId = OrgA, State = "Enabled", SyntheticOnly = true },
                new Organization { OrganizationId = OrgB, State = "Enabled", SyntheticOnly = true });
            await db.SaveChangesAsync();
        }
        await using (var db = PostgresFixture.Open(cs))
            await new PostgresIdentityMapping(db, clock).CreateInactive(User, Actor, OrgA, "synthetic-shared");
        if (active)
        {
            // Synthetic fixture activation ONLY. S6 workflow intentionally not implemented.
            await using var db = PostgresFixture.Open(cs);
            var user = await db.Users.SingleAsync();
            user.PasswordHash = new PasswordHasher<IdentityUser<Guid>>().HashPassword(user, Password);
            (await db.Set<ActorIdentity>().SingleAsync()).IsActive = true;
            await db.SaveChangesAsync();
        }
        return cs;
    }
    private async Task<SessionResult> Login(string cs, params IInterceptor[] interceptors)
    {
        await using var db = PostgresFixture.Open(cs, interceptors);
        return await new PostgresIdentitySessions(db, clock).Authenticate("synthetic-shared", Password);
    }
    private async Task<SessionResult> Rotate(string cs, RefreshSecret token, params IInterceptor[] interceptors)
    {
        await using var db = PostgresFixture.Open(cs, interceptors);
        return await new PostgresIdentitySessions(db, clock).Rotate(token);
    }
    private async Task Revoke(string cs, Guid sid, bool all = false, params IInterceptor[] interceptors)
    {
        await using var db = PostgresFixture.Open(cs, interceptors);
        var sessions = new PostgresIdentitySessions(db, clock);
        if (all) await sessions.RevokeAll(User); else await sessions.Revoke(User, sid);
    }
    private async Task<SessionIdentity?> Read(string cs, Guid sid, Guid? user = null)
    {
        await using var db = PostgresFixture.Open(cs);
        return await new PostgresIdentitySessions(db, clock).Read(user ?? User, sid);
    }
    private static void Denied(SessionResult result)
    {
        Assert.Equal(SessionOutcome.Denied, result.Outcome);
        Assert.Null(result.RefreshToken); Assert.Null(result.Identity);
    }
    private static void Conflict(SessionResult result)
    {
        Assert.Equal(SessionOutcome.RefreshAlreadyRotated, result.Outcome);
        Assert.Equal("refresh_already_rotated", result.ErrorCode);
        Assert.Null(result.RefreshToken); Assert.Null(result.Identity);
    }

    [Fact]
    public async Task Mapping_is_stable_inactive_and_has_no_implicit_grants()
    {
        var cs = await Seed(false);
        await using var db = PostgresFixture.Open(cs);
        var link = await new PostgresIdentityMapping(db, clock).Find(User);
        Assert.Equal(new IdentityLink(User, Actor, false), link);
        Assert.Null(await new PostgresIdentityMapping(db, clock).Find(Guid.NewGuid()));
        Assert.Null((await db.Users.SingleAsync()).PasswordHash);
        Assert.Empty(await db.Set<OrganizationMembership>().ToListAsync());
        Assert.Empty(await db.Set<EnrollmentScope>().ToListAsync());
        Assert.Empty(await db.Set<TeacherAssignmentScope>().ToListAsync());
        Denied(await Login(cs));
    }

    [Fact]
    public async Task Duplicate_actor_and_username_and_malformed_mapping_cannot_commit_partial_identity()
    {
        var cs = await Seed();
        foreach (var actor in new[] { Actor, Guid.NewGuid() })
        {
            await using var db = PostgresFixture.Open(cs);
            await Assert.ThrowsAsync<DbUpdateException>(() => new PostgresIdentityMapping(db, clock)
                .CreateInactive(Guid.NewGuid(), actor, OrgA, actor == Actor ? "other-synthetic" : "SYNTHETIC-SHARED"));
        }
        await using (var db = PostgresFixture.Open(cs))
            await Assert.ThrowsAsync<UseCaseException>(() => new PostgresIdentityMapping(db, clock)
                .CreateInactive(Guid.Empty, Guid.NewGuid(), OrgA, "invalid"));
        await using var check = PostgresFixture.Open(cs);
        Assert.Single(await check.Users.ToListAsync()); Assert.Single(await check.Set<ActorRecord>().ToListAsync());
        Assert.Single(await check.Set<SecurityAudit>().ToListAsync());
    }

    [Fact]
    public async Task Mapping_audit_and_identity_rollback_together()
    {
        var cs = await Seed();
        await using (var db = PostgresFixture.Open(cs, new FailAtCommit()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => new PostgresIdentityMapping(db, clock)
                .CreateInactive(Guid.NewGuid(), Guid.NewGuid(), OrgA, "synthetic-rollback"));
        await using var check = PostgresFixture.Open(cs);
        Assert.Single(await check.Users.ToListAsync()); Assert.Single(await check.Set<ActorRecord>().ToListAsync());
        Assert.Single(await check.Set<SecurityAudit>().ToListAsync());
    }

    [Fact]
    public async Task Shared_identity_in_A_and_B_gets_no_org_or_educational_authority_from_session()
    {
        var cs = await Seed();
        await using (var db = PostgresFixture.Open(cs))
        {
            foreach (var org in new[] { OrgA, OrgB })
                db.Add(new OrganizationMembership { OrganizationId = org, ActorId = Actor, State = "Active", Generation = 1, Version = 1 });
            await db.SaveChangesAsync();
        }
        var login = await Login(cs);
        Assert.Equal(User, login.Identity!.UserId); Assert.Equal(Actor, login.Identity.ActorId);
        Assert.NotEqual(User, login.Identity.ActorId);
        Assert.Null(await Read(cs, login.Identity.SessionId, Guid.NewGuid()));
        Assert.Equal(login.Identity, await Read(cs, login.Identity.SessionId));
        await using var check = PostgresFixture.Open(cs);
        Assert.Equal(2, await check.Set<OrganizationMembership>().CountAsync());
        Assert.Empty(await check.Set<EnrollmentScope>().ToListAsync()); Assert.Empty(await check.Set<TeacherAssignmentScope>().ToListAsync());
        Assert.DoesNotContain("org", JsonSerializer.Serialize(login.Identity), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("blocked")]
    [InlineData("inactive")]
    [InlineData("lockout")]
    public async Task Account_state_denies_login_read_and_refresh(string state)
    {
        var cs = await Seed(); var login = await Login(cs);
        await using (var db = PostgresFixture.Open(cs))
        {
            if (state == "blocked")
            {
                var security = await db.Set<AccountSecurityState>().SingleAsync();
                security.State = "SecurityBlocked"; security.Version++;
            }
            else if (state == "inactive") (await db.Set<ActorIdentity>().SingleAsync()).IsActive = false;
            else { var user = await db.Users.SingleAsync(); user.LockoutEnabled = true; user.LockoutEnd = clock.GetUtcNow().AddHours(1); }
            await db.SaveChangesAsync();
        }
        Denied(await Login(cs)); Assert.Null(await Read(cs, login.Identity!.SessionId));
        Denied(await Rotate(cs, login.RefreshToken!));
        await using var check = PostgresFixture.Open(cs);
        Assert.NotNull((await check.Set<IdentitySession>().SingleAsync()).RevokedAt);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_user_never_create_session()
    {
        var cs = await Seed();
        await using var db = PostgresFixture.Open(cs);
        var service = new PostgresIdentitySessions(db, clock);
        Denied(await service.Authenticate("missing-synthetic", Password));
        Denied(await service.Authenticate("synthetic-shared", new string('x', 11)));
        Denied(await service.Authenticate("synthetic-shared", new string('x', 129)));
        Denied(await service.Authenticate("synthetic-shared", "Incorrect-synthetic-password"));
        Assert.Empty(await db.Set<IdentitySession>().ToListAsync());
    }

    [Fact]
    public async Task Rotation_preserves_absolute_life_consumes_once_and_read_does_not_touch_idle()
    {
        var cs = await Seed(); var login = await Login(cs);
        clock.Advance(TimeSpan.FromMinutes(59));
        Assert.NotNull(await Read(cs, login.Identity!.SessionId));
        var next = await Rotate(cs, login.RefreshToken!);
        Assert.Equal(SessionOutcome.Success, next.Outcome);
        Assert.Equal(login.Identity, next.Identity); Assert.NotEqual(login.RefreshToken!.Reveal(), next.RefreshToken!.Reveal());
        await using var check = PostgresFixture.Open(cs);
        var session = await check.Set<IdentitySession>().SingleAsync();
        Assert.Equal(2, session.Generation); Assert.Equal(clock.GetUtcNow(), session.LastSuccessfulRefreshAt);
        Assert.Equal(session.CreatedAt.AddHours(12), session.AbsoluteExpiresAt);
        Assert.Single(await check.Set<RefreshVerifier>().Where(x => x.ConsumedAt == null).ToListAsync());
        Assert.Equal(2, await check.Set<RefreshVerifier>().CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_idle_and_absolute_boundaries_deny_and_cannot_resurrect(bool absolute)
    {
        var cs = await Seed(); var current = await Login(cs);
        if (absolute)
            for (var i = 0; i < 23; i++) { clock.Advance(TimeSpan.FromMinutes(30)); current = await Rotate(cs, current.RefreshToken!); Assert.Equal(SessionOutcome.Success, current.Outcome); }
        clock.Advance(TimeSpan.FromMinutes(absolute ? 30 : 60));
        Assert.Null(await Read(cs, current.Identity!.SessionId));
        Denied(await Rotate(cs, current.RefreshToken!));
        clock.Advance(TimeSpan.FromHours(-1)); Denied(await Rotate(cs, current.RefreshToken!));
    }

    [Fact]
    public async Task Two_real_concurrent_connections_have_one_successor_and_one_conflict()
    {
        var cs = await Seed(); var login = await Login(cs); var barrier = new TransactionBarrier();
        var results = await Task.WhenAll(Rotate(cs, login.RefreshToken!, barrier), Rotate(cs, login.RefreshToken!, barrier));
        var winner = Assert.Single(results, x => x.Outcome == SessionOutcome.Success);
        Conflict(Assert.Single(results, x => x.Outcome != SessionOutcome.Success));
        Assert.Equal(SessionOutcome.Success, (await Rotate(cs, winner.RefreshToken!)).Outcome);
        await using var check = PostgresFixture.Open(cs);
        Assert.Equal(3, await check.Set<RefreshVerifier>().CountAsync());
        Assert.Null((await check.Set<IdentitySession>().SingleAsync()).RevokedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    [InlineData(30)]
    public async Task Accepted_replay_window_is_inclusive_and_does_not_advance_activity(int seconds)
    {
        var cs = await Seed(); var login = await Login(cs); await Rotate(cs, login.RefreshToken!);
        var activity = clock.GetUtcNow(); clock.Advance(TimeSpan.FromSeconds(seconds));
        Conflict(await Rotate(cs, login.RefreshToken!));
        await using var check = PostgresFixture.Open(cs);
        var s = await check.Set<IdentitySession>().SingleAsync();
        Assert.Null(s.RevokedAt); Assert.Equal(activity, s.LastSuccessfulRefreshAt); Assert.Equal(2, s.Generation);
    }

    [Fact]
    public async Task Late_replay_revokes_only_target_and_audits_without_blocking_account()
    {
        var cs = await Seed(); var first = await Login(cs); var other = await Login(cs);
        var next = await Rotate(cs, first.RefreshToken!); clock.Advance(TimeSpan.FromSeconds(30.001));
        Denied(await Rotate(cs, first.RefreshToken!)); Denied(await Rotate(cs, next.RefreshToken!));
        Assert.Equal(SessionOutcome.Success, (await Rotate(cs, other.RefreshToken!)).Outcome);
        await using var check = PostgresFixture.Open(cs);
        Assert.Equal("Enabled", (await check.Set<AccountSecurityState>().SingleAsync()).State);
        Assert.Single(await check.Set<SecurityAudit>().Where(x => x.Action == "refresh_replay").ToListAsync());
    }

    [Fact]
    public async Task Lost_committed_response_never_rediscloses_successor_and_requires_new_login()
    {
        var cs = await Seed(); var login = await Login(cs);
        _ = await Rotate(cs, login.RefreshToken!); // Drop the entire committed response, including successor.
        Conflict(await Rotate(cs, login.RefreshToken!));
        clock.Advance(TimeSpan.FromSeconds(31)); Denied(await Rotate(cs, login.RefreshToken!));
        Assert.Equal(SessionOutcome.Success, (await Login(cs)).Outcome);
    }

    [Fact]
    public async Task Simulated_tabs_and_both_response_orders_never_replace_cookie_with_conflict()
    {
        var cs = await Seed(); var login = await Login(cs); var barrier = new TransactionBarrier();
        var responses = await Task.WhenAll(Rotate(cs, login.RefreshToken!, barrier), Rotate(cs, login.RefreshToken!, barrier));
        var successor = responses.Single(x => x.Outcome == SessionOutcome.Success).RefreshToken!;
        // Contract-level response projection, not an HTTP/browser acceptance test.
        foreach (var order in new[] { responses, responses.Reverse().ToArray() })
        {
            var cookie = login.RefreshToken!;
            foreach (var response in order) if (response.RefreshToken is { } newCookie) cookie = newCookie;
            Assert.Same(successor, cookie);
        }
        var tabB = await Rotate(cs, successor); // Another org's tab uses shared current cookie.
        Assert.Equal(SessionOutcome.Success, tabB.Outcome);
        Assert.Equal(Actor, tabB.Identity!.ActorId);
    }

    [Fact]
    public async Task Logout_and_revoke_all_are_idempotent_and_irreversible()
    {
        var cs = await Seed(); var one = await Login(cs); var two = await Login(cs);
        await Revoke(cs, one.Identity!.SessionId); await Revoke(cs, one.Identity.SessionId);
        Denied(await Rotate(cs, one.RefreshToken!)); Assert.NotNull(await Read(cs, two.Identity!.SessionId));
        await Revoke(cs, two.Identity.SessionId, true); await Revoke(cs, two.Identity.SessionId, true);
        Denied(await Rotate(cs, two.RefreshToken!));
        await using var check = PostgresFixture.Open(cs);
        Assert.All(await check.Set<IdentitySession>().ToListAsync(), s => Assert.NotNull(s.RevokedAt));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Revoke_serializes_with_rotation_and_no_returned_token_survives(bool all)
    {
        var cs = await Seed(); var login = await Login(cs); var barrier = new TransactionBarrier();
        var rotate = Rotate(cs, login.RefreshToken!, barrier);
        await Task.WhenAll(rotate, Revoke(cs, login.Identity!.SessionId, all, barrier));
        if ((await rotate).RefreshToken is { } token) Denied(await Rotate(cs, token));
        Assert.Null(await Read(cs, login.Identity.SessionId));
    }

    [Theory]
    [InlineData("create")]
    [InlineData("rotate")]
    [InlineData("replay")]
    [InlineData("revoke")]
    public async Task Commit_failure_rolls_back_session_verifiers_and_audit(string operation)
    {
        var cs = await Seed(); var login = await Login(cs);
        if (operation == "replay") { await Rotate(cs, login.RefreshToken!); clock.Advance(TimeSpan.FromSeconds(31)); }
        string before;
        await using (var db = PostgresFixture.Open(cs)) before = await Snapshot(db);
        var failure = new FailAtCommit();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (operation == "create") await Login(cs, failure);
            else if (operation == "revoke") await Revoke(cs, login.Identity!.SessionId, false, failure);
            else await Rotate(cs, login.RefreshToken!, failure);
        });
        await using var check = PostgresFixture.Open(cs);
        Assert.Equal(before, await Snapshot(check));
        if (operation == "rotate") Assert.Equal(SessionOutcome.Success, (await Rotate(cs, login.RefreshToken!)).Outcome);
    }
    private static async Task<string> Snapshot(LmsDbContext db) => JsonSerializer.Serialize(new
    {
        Sessions = await db.Set<IdentitySession>().OrderBy(x => x.SessionId).AsNoTracking().ToArrayAsync(),
        Verifiers = await db.Set<RefreshVerifier>().OrderBy(x => x.SessionId).ThenBy(x => x.Generation).AsNoTracking().ToArrayAsync(),
        Audit = await db.Set<SecurityAudit>().OrderBy(x => x.AuditId).AsNoTracking().ToArrayAsync()
    });

    [Fact]
    public async Task Forged_token_cannot_revoke_and_secrets_never_reach_sql_logs_audit_or_serialization()
    {
        var cs = await Seed(); var log = new List<string>(); var parameters = new CaptureParameters();
        var options = new DbContextOptionsBuilder<LmsDbContext>().UseNpgsql(cs).LogTo(log.Add).AddInterceptors(parameters).Options;
        SessionResult login;
        await using (var db = new LmsDbContext(options)) login = await new PostgresIdentitySessions(db, clock).Authenticate("synthetic-shared", Password);
        SessionResult next;
        await using (var db = new LmsDbContext(options)) next = await new PostgresIdentitySessions(db, clock).Rotate(login.RefreshToken!);
        Denied(await Rotate(cs, new RefreshSecret(new string('A', 64))));
        Denied(await Rotate(cs, new RefreshSecret("malformed-secret-canary")));
        await using var check = PostgresFixture.Open(cs);
        var snapshot = await Snapshot(check);
        var all = string.Join('\n', log.Concat(parameters.Values)) + snapshot + JsonSerializer.Serialize(login) + login.RefreshToken;
        foreach (var secret in new[] { Password, login.RefreshToken!.Reveal(), next.RefreshToken!.Reveal() })
            Assert.DoesNotContain(secret, all);
        Assert.NotEqual(login.RefreshToken.Reveal(), Convert.ToHexString((await check.Set<RefreshVerifier>().FirstAsync()).Verifier));
        Assert.Null((await check.Set<IdentitySession>().SingleAsync()).RevokedAt);
    }

    [Fact]
    public async Task Audit_failure_after_consumption_rolls_back_predecessor()
    {
        var cs = await Seed(); var login = await Login(cs);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Rotate(cs, login.RefreshToken!, new RejectAuditWrites()));
        Assert.Equal(SessionOutcome.Success, (await Rotate(cs, login.RefreshToken!)).Outcome);
        await using var check = PostgresFixture.Open(cs);
        Assert.Equal(2, await check.Set<RefreshVerifier>().CountAsync());
        Assert.Single(await check.Set<SecurityAudit>().Where(x => x.Action == "session_rotated").ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Waiting_login_or_refresh_observes_committed_SecurityBlocked(bool loginOperation)
    {
        var cs = await Seed(); var login = await Login(cs);
        await using var blocker = PostgresFixture.Open(cs);
        await using var tx = await blocker.Database.BeginTransactionAsync();
        // Synthetic state writer; not an implementation of the S6 operator workflow.
        await blocker.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "AccountSecurityState" SET "State"='SecurityBlocked', "Version"="Version"+1 WHERE "UserId"={User}""");
        var observer = new ObserveSecurityLock();
        var pending = loginOperation ? Login(cs, observer) : Rotate(cs, login.RefreshToken!, observer);
        await observer.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(pending.IsCompleted);
        await tx.CommitAsync();
        Denied(await pending);
    }

    [Fact]
    public async Task Additive_S2_upgrade_preserves_existing_sessions_and_verifier_history()
    {
        var cs = await Seed(migration: "20261009094756_SchemaFoundationsM1");
        var login = await Login(cs); var next = await Rotate(cs, login.RefreshToken!);
        await using var db = PostgresFixture.Open(cs);
        var before = await Snapshot(db);
        await db.Database.MigrateAsync(); await db.Database.MigrateAsync();
        Assert.Equal(before, await Snapshot(db));
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Contains("20261009101331_SessionVerifierLookupS2", await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(SessionOutcome.Success, (await Rotate(cs, next.RefreshToken!)).Outcome);
    }

    [Fact]
    public async Task Stale_tracked_security_state_is_rejected_instead_of_bypassing_block()
    {
        var cs = await Seed();
        await using var db = PostgresFixture.Open(cs);
        await db.Set<AccountSecurityState>().SingleAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PostgresIdentitySessions(db, clock).Authenticate("synthetic-shared", Password));
    }
}

public sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan duration) => now += duration;
}

public sealed class SessionPolicyTests
{
    [Theory]
    [InlineData(11, false)] [InlineData(12, true)] [InlineData(128, true)] [InlineData(129, false)]
    public void Password_bounds(int length, bool expected) => Assert.Equal(expected, SessionPolicy.ValidPasswordLength(new string('x', length)));
    [Fact]
    public void Clock_rollback_and_exact_boundaries_fail_closed()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(SessionPolicy.Expired(now.AddTicks(-1), now, now.AddHours(12)));
        Assert.True(SessionPolicy.Expired(now.AddMinutes(60), now, now.AddHours(12)));
        Assert.True(SessionPolicy.Expired(now.AddHours(12), now.AddHours(11.5), now.AddHours(12)));
        Assert.False(SessionPolicy.Expired(now.AddMinutes(60).AddTicks(-1), now, now.AddHours(12)));
        Assert.Equal(TimeSpan.FromMinutes(15), SessionPolicy.MaximumAccessLifetime);
    }
    [Fact]
    public void Secret_is_redacted_in_ToString_and_default_json()
    {
        var token = new RefreshSecret("synthetic-secret-canary");
        Assert.DoesNotContain(token.Reveal(), token.ToString());
        Assert.DoesNotContain(token.Reveal(), JsonSerializer.Serialize(token));
    }
}

// Deliberately capture actual command parameters as a canary check, even though
// production EF sensitive-data logging is disabled. Never persist this collector.
public sealed class CaptureParameters : DbCommandInterceptor
{
    public List<string> Values { get; } = [];
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Values.AddRange(command.Parameters.Cast<DbParameter>().Select(x => x.Value?.ToString() ?? ""));
        return ValueTask.FromResult(result);
    }
}
public sealed class RejectAuditWrites : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context!.ChangeTracker.Entries<SecurityAudit>().Any(x => x.State == EntityState.Added))
            throw new InvalidOperationException("Injected audit failure");
        return ValueTask.FromResult(result);
    }
}
public sealed class ObserveSecurityLock : DbCommandInterceptor
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("AccountSecurityState") && command.CommandText.Contains("FOR UPDATE")) Started.TrySetResult();
        return ValueTask.FromResult(result);
    }
}
