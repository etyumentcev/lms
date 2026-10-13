using System.Security.Cryptography;
using Lms.Application;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Tests;

public sealed partial class ScopedAuthorityTests
{
    private async Task<(Guid User, Guid Session)> IssuingIdentity(string cs)
    {
        var user = Guid.NewGuid(); var session = Guid.NewGuid();
        await using (var db = PostgresFixture.Open(cs))
            await new PostgresIdentityMapping(db, clock).CreateInactive(user, S, A, "synthetic-a1");
        await using (var db = PostgresFixture.Open(cs))
        {
            (await db.Set<ActorIdentity>().SingleAsync(x => x.UserId == user)).IsActive = true;
            db.Add(new IdentitySession { UserId = user, SessionId = session, CreatedAt = At, LastSuccessfulRefreshAt = At,
                AbsoluteExpiresAt = At.AddHours(12), Generation = 1, Version = 1 });
            await db.SaveChangesAsync();
        }
        return (user, session);
    }
    [Fact]
    public async Task A1_issuer_switch_revoke_regrant_and_SecurityBlocked_use_existing_authority()
    {
        var cs = await Seed(); var identity = await IssuingIdentity(cs); var time = new ComponentClock();
        using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "synthetic", "component", time);
        async Task<AccessToken?> Issue(Guid org)
        {
            await using var db = PostgresFixture.Open(cs);
            return await new PostgresComponentTokenIssuer(db, time, jwt).Issue(identity.User, identity.Session, org);
        }
        var original = (await Issue(A))!; var a = jwt.Validate(original)!; var b = jwt.Validate((await Issue(B))!)!;
        Assert.Equal(identity.User, a.Identity.UserId); Assert.Equal(S, a.Identity.ActorId); Assert.Equal(identity.Session, a.Identity.SessionId);
        Assert.Equal(A, a.Authority.Membership.OrganizationId); Assert.Equal(B, b.Authority.Membership.OrganizationId);
        Assert.All(b.Authority.Grants, g => Assert.Equal("b", g.CourseId));
        Assert.Equal(At.AddMinutes(15), a.ExpiresAt);
        await Change(cs, Enroll() with { Operation = AuthorityOperation.Revoke, ExpectedVersion = 1 });
        Assert.Null(await Issue(A)); Assert.NotNull(await Issue(B));
        await Change(cs, Enroll() with { ExpectedVersion = 2 });
        var regrant = jwt.Validate((await Issue(A))!)!;
        Assert.True(regrant.Authority.Grants[0].Generation > a.Authority.Grants[0].Generation);
        Assert.False(ScopePolicy.Allows(a.Authority, new(A, "a", "g", S, MembershipGeneration: 1,
            EnrollmentGeneration: regrant.Authority.Grants[0].Generation), ScopeAction.Read, S));
        await using (var db = PostgresFixture.Open(cs))
        {
            var state = await db.Set<AccountSecurityState>().SingleAsync(); state.State = "SecurityBlocked"; state.Version++;
            await db.SaveChangesAsync();
        }
        Assert.Null(await Issue(A)); Assert.Null(await Issue(B));
        // Pre-block JWT remains a locally valid component credential under unchanged E03.
        Assert.NotNull(jwt.Validate(original));
    }
    [Fact]
    public async Task A1_waiting_issue_observes_committed_revoke()
    {
        var cs = await Seed(); var identity = await IssuingIdentity(cs); var time = new ComponentClock();
        using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "synthetic", "component", time);
        await using var blocker = PostgresFixture.Open(cs); await using var tx = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM "Organizations" WHERE "OrganizationId"={A} FOR UPDATE""");
        var observed = new ObserveOrganizationLock();
        await using var issuing = PostgresFixture.Open(cs, observed);
        var pending = new PostgresComponentTokenIssuer(issuing, time, jwt).Issue(identity.User, identity.Session, A);
        await observed.Started.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(pending.IsCompleted);
        await blocker.Database.ExecuteSqlInterpolatedAsync($"""SELECT public.s3_change_authority('Enrollment','Revoke',{A},{S},1,1,'a','g',NULL,NULL,0)""");
        await tx.CommitAsync(); Assert.Null(await pending);
    }
    [Theory]
    [InlineData("unknown-clock")] [InlineData("idle-expired")] [InlineData("absolute-expired")]
    [InlineData("wrong-user")] [InlineData("wrong-session")] [InlineData("unknown-org")]
    public async Task A1_issuance_denies_missing_identity_expiry_and_clock(string state)
    {
        var cs = await Seed(); var identity = await IssuingIdentity(cs); var time = new ComponentClock();
        using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "synthetic", "component", time);
        if (state == "unknown-clock") time.Time = null;
        if (state == "idle-expired") time.Time = new(At.AddHours(1), At.AddHours(1));
        if (state == "absolute-expired") time.Time = new(At.AddHours(12), At.AddHours(12));
        await using var db = PostgresFixture.Open(cs);
        Assert.Null(await new PostgresComponentTokenIssuer(db, time, jwt).Issue(
            state == "wrong-user" ? Guid.NewGuid() : identity.User,
            state == "wrong-session" ? Guid.NewGuid() : identity.Session,
            state == "unknown-org" ? Guid.NewGuid() : A));
    }
    [Fact]
    public async Task A1_issue_waits_for_security_lock_and_denies_committed_block()
    {
        var cs = await Seed(); var identity = await IssuingIdentity(cs); var time = new ComponentClock();
        using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "synthetic", "component", time);
        await using var blocker = PostgresFixture.Open(cs); await using var tx = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "AccountSecurityState" SET "State"='SecurityBlocked', "Version"="Version"+1 WHERE "UserId"={identity.User}""");
        var observer = new ObserveSecurityLock(); await using var db = PostgresFixture.Open(cs, observer);
        var pending = new PostgresComponentTokenIssuer(db, time, jwt).Issue(identity.User, identity.Session, A);
        await observer.Started.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(pending.IsCompleted);
        await tx.CommitAsync(); Assert.Null(await pending);
    }
    [Fact]
    public async Task A1_issuer_rechecks_time_after_waiting_for_organization()
    {
        var cs = await Seed(); var identity = await IssuingIdentity(cs); var time = new ComponentClock();
        using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "synthetic", "component", time);
        await using var blocker = PostgresFixture.Open(cs); await using var tx = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM "Organizations" WHERE "OrganizationId"={A} FOR UPDATE""");
        var observer = new ObserveOrganizationLock(); await using var db = PostgresFixture.Open(cs, observer);
        var pending = new PostgresComponentTokenIssuer(db, time, jwt).Issue(identity.User, identity.Session, A);
        await observer.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        time.Time = new(At.AddHours(1), At.AddHours(1));
        await tx.CommitAsync(); Assert.Null(await pending);
    }
    [Fact]
    public async Task A1_processing_current_restrictions_missing_context_and_uncertainty_fail_closed()
    {
        var cs = await Seed(); var time = new ComponentClock();
        async Task<bool> Allow()
        {
            await using var db = PostgresFixture.Open(cs);
            return await new PostgresCurrentProcessingGate(db, time).Allows(new(A, "a", "g", S), S, ScopeAction.Read, default);
        }
        Assert.False(await Allow());
        await using (var db = PostgresFixture.Open(cs))
        {
            db.Add(new CourseContextBinding { OrganizationId = A, CourseId = "a", ContextId = Context, ContextVersion = 1, EffectiveFrom = At.AddHours(-1) });
            await db.SaveChangesAsync();
        }
        Assert.True(await Allow());
        await using (var db = PostgresFixture.Open(cs))
        {
            db.Add(new ProcessingRestriction { RestrictionId = Guid.NewGuid(), OrganizationId = A, ContextId = Context,
                OriginVersion = 1, SubjectActorId = S, Operations = 1, State = "Active", Version = 1 });
            await db.SaveChangesAsync();
        }
        Assert.False(await Allow()); time.Time = null; Assert.False(await Allow());
        await using var unavailable = PostgresFixture.Open("Host=127.0.0.1;Port=1;Database=synthetic;Username=synthetic;Timeout=1");
        time.Time = new(At, At);
        await Assert.ThrowsAnyAsync<Exception>(() => new PostgresCurrentProcessingGate(unavailable, time).Allows(new(A, "a"), S, ScopeAction.Read, default));
    }
}
public sealed class ObserveOrganizationLock : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
        System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData data,
        Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("Organizations") && command.CommandText.Contains("FOR UPDATE")) Started.TrySetResult();
        return ValueTask.FromResult(result);
    }
}
