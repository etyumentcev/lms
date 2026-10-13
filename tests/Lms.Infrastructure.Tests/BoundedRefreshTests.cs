using System.Security.Cryptography;
using Lms.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace Lms.Infrastructure.Tests;

public sealed partial class ScopedAuthorityTests
{
    private sealed class RevocationClock : IQualifiedClockSource
    {
        public TimeSpan Elapsed;
        public TimeSpan UtcOffset;
        public QualifiedClockReading Read() => new("bounded-test", true,
            new((At + Elapsed).ToOffset(UtcOffset), (At + Elapsed).ToOffset(UtcOffset)), Elapsed, Elapsed);
        public QualifiedAuthorityClock Create() => new(this, TimeSpan.Zero, TimeSpan.Zero);
    }
    private sealed class RevocationIngress(QualifiedAuthorityClock time) : TrustedApplicationIngressAdapter(time)
    {
        public ApplicationDeadline Arrive(ApplicationRequestKind kind = ApplicationRequestKind.Ordinary) => Capture(kind);
    }
    private async Task<SessionResult> TeacherSession(string cs)
    {
        var userId = Guid.NewGuid();
        await using (var db = PostgresFixture.Open(cs))
            await new PostgresIdentityMapping(db, clock).CreateInactive(userId, T, A, "bounded-teacher");
        await using (var db = PostgresFixture.Open(cs))
        {
            (await db.Set<ActorIdentity>().SingleAsync(x => x.UserId == userId)).IsActive = true;
            var user = await db.Users.SingleAsync(x => x.Id == userId);
            user.PasswordHash = new PasswordHasher<IdentityUser<Guid>>().HashPassword(user, "Synthetic-bounded-password-123");
            await db.SaveChangesAsync();
        }
        await using var login = PostgresFixture.Open(cs);
        return await new PostgresIdentitySessions(login, clock).Authenticate("bounded-teacher", "Synthetic-bounded-password-123");
    }
    private async Task<AuthorityCommand> RevokeTeacherCommand(string cs, bool membership)
    {
        if (membership) return new(AuthorityKind.Membership, AuthorityOperation.Revoke, A, T, 1);
        await using var db = PostgresFixture.Open(cs);
        var grant = await db.Set<TeacherAssignmentScope>().SingleAsync(x => x.OrganizationId == A && x.TeacherId == T);
        return new(AuthorityKind.Teacher, AuthorityOperation.Revoke, A, T, grant.Version,
            grant.MembershipGeneration, grant.CourseId, grant.GroupId, grant.GrantId);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Bounded_refresh_excludes_revoked_teacher_or_membership_and_keeps_other_org(bool membership)
    {
        var cs = await Seed(); var session = await TeacherSession(cs);
        await Change(cs, Teacher(T, "g") with { OrganizationId = B, CourseId = "b" });
        var source = new RevocationClock(); var time = source.Create(); var ingress = new RevocationIngress(time);
        using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "test", "test", time);
        await using var issuing = PostgresFixture.Open(cs);
        var old = await new PostgresComponentTokenIssuer(issuing, time, jwt).Issue(session.Identity!.UserId, session.Identity.SessionId, A);
        Assert.NotNull(old);
        var revoke = await RevokeTeacherCommand(cs, membership);
        await Change(cs, revoke);
        await using var db = PostgresFixture.Open(cs);
        var refreshed = await new PostgresComponentTokenIssuer(db, time, jwt).Refresh(session.RefreshToken!, A, ingress.Arrive());
        Assert.Equal(SessionOutcome.Success, refreshed.Outcome);
        Assert.NotNull(refreshed.RefreshToken); Assert.Null(refreshed.AccessToken);
        Assert.False(refreshed.HasEducationalScope); Assert.NotNull(jwt.Validate(old!));
        await using var other = PostgresFixture.Open(cs);
        var switched = await new PostgresComponentTokenIssuer(other, time, jwt).Refresh(refreshed.RefreshToken!, B, ingress.Arrive());
        Assert.Equal(B, jwt.Validate(switched.AccessToken!)!.Authority.Membership.OrganizationId);
        source.Elapsed = TimeSpan.FromMinutes(15);
        Assert.Null(jwt.Validate(old!));
    }

    [Fact]
    public async Task Bounded_refresh_waiting_for_organization_observes_committed_revoke()
    {
        var cs = await Seed(); var session = await TeacherSession(cs); var revoke = await RevokeTeacherCommand(cs, false);
        var time = new RevocationClock().Create(); using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "test", "test", time);
        await using var blocker = PostgresFixture.Open(cs); await using var tx = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM "Organizations" WHERE "OrganizationId"={A} FOR UPDATE""");
        var observed = new ObserveOrganizationLock(); await using var db = PostgresFixture.Open(cs, observed);
        var pending = new PostgresComponentTokenIssuer(db, time, jwt).Refresh(session.RefreshToken!, A, new RevocationIngress(time).Arrive());
        await observed.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await blocker.Database.ExecuteSqlInterpolatedAsync($"SELECT public.s3_change_authority('Teacher','Revoke',{A},{T},1,1,'a','g',{revoke.GrantId},NULL,0)");
        await tx.CommitAsync(); var result = await pending;
        Assert.Equal(SessionOutcome.Success, result.Outcome); Assert.Null(result.AccessToken);
    }

    [Fact]
    public async Task Bounded_refresh_conflict_never_issues_second_secret_or_token()
    {
        var cs = await Seed(); var session = await TeacherSession(cs); var time = new RevocationClock().Create();
        using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "test", "test", time);
        async Task<SessionResult> Refresh()
        {
            await using var db = PostgresFixture.Open(cs);
            return await new PostgresComponentTokenIssuer(db, time, jwt).Refresh(session.RefreshToken!, A, new RevocationIngress(time).Arrive());
        }
        var results = await Task.WhenAll(Refresh(), Refresh());
        var winner = Assert.Single(results, x => x.Outcome == SessionOutcome.Success);
        Assert.NotNull(winner.AccessToken); Assert.NotNull(winner.RefreshToken);
        var loser = Assert.Single(results, x => x.Outcome == SessionOutcome.RefreshAlreadyRotated);
        Assert.Null(loser.AccessToken); Assert.Null(loser.RefreshToken);
    }

    [Fact]
    public async Task Bounded_refresh_commit_failure_rolls_back_rotation_and_fresh_token_together()
    {
        var cs = await Seed(); var session = await TeacherSession(cs); var time = new RevocationClock().Create();
        using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "test", "test", time);
        await using (var db = PostgresFixture.Open(cs, new FailAtCommit()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => new PostgresComponentTokenIssuer(db, time, jwt)
                .Refresh(session.RefreshToken!, A, new RevocationIngress(time).Arrive()));
        await using var retry = PostgresFixture.Open(cs);
        var result = await new PostgresComponentTokenIssuer(retry, time, jwt).Refresh(session.RefreshToken!, A, new RevocationIngress(time).Arrive());
        Assert.Equal(SessionOutcome.Success, result.Outcome); Assert.NotNull(result.AccessToken);
    }

    private sealed class AfterRefreshCommit(Action after) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default) { after(); return Task.CompletedTask; }
    }
    [Fact]
    public async Task Bounded_refresh_late_ack_returns_no_credentials_and_does_not_replay()
    {
        var cs = await Seed(); var session = await TeacherSession(cs); var source = new RevocationClock(); var time = source.Create();
        using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "test", "test", time);
        var original = new RevocationIngress(time).Arrive();
        await using (var db = PostgresFixture.Open(cs, new AfterRefreshCommit(() => source.Elapsed = TimeSpan.FromMinutes(1))))
            await Assert.ThrowsAsync<OperationCanceledException>(() => new PostgresComponentTokenIssuer(db, time, jwt)
                .Refresh(session.RefreshToken!, A, original));
        await using var verify = PostgresFixture.Open(cs);
        Assert.Equal(2, (await verify.Set<IdentitySession>().SingleAsync()).Generation);
        Assert.Equal(1, await verify.Set<RefreshVerifier>().CountAsync(x => x.ConsumedAt == null));
        Assert.False(original.Check(ApplicationAction.ResponseEnqueue).Allowed);
    }

    [Fact]
    public async Task Bounded_old_token_operation_can_finish_after_revoke_but_refresh_does_not_extend_it()
    {
        var cs = await Seed(); var session = await TeacherSession(cs); var source = new RevocationClock(); var time = source.Create();
        using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "test", "test", time); var ingress = new RevocationIngress(time);
        await using var issuing = PostgresFixture.Open(cs);
        var token = await new PostgresComponentTokenIssuer(issuing, time, jwt).Issue(session.Identity!.UserId, session.Identity.SessionId, A);
        var early = ingress.Arrive(ApplicationRequestKind.TeacherOperation);
        await Change(cs, await RevokeTeacherCommand(cs, false));
        Assert.True(early.Check(ApplicationAction.StartStage).Allowed); // ordinary revoke does not cancel a handle
        source.Elapsed = TimeSpan.FromMinutes(15) - TimeSpan.FromTicks(1);
        Assert.NotNull(jwt.Validate(token!));
        var operation = ingress.Arrive(ApplicationRequestKind.TeacherOperation); var cutoff = operation.ExpiresAt;
        source.Elapsed += TimeSpan.FromMinutes(1);
        await using var refresh = PostgresFixture.Open(cs);
        var result = await new PostgresComponentTokenIssuer(refresh, time, jwt).Refresh(session.RefreshToken!, A, ingress.Arrive());
        Assert.Null(result.AccessToken); Assert.Equal(cutoff, operation.ExpiresAt);
        source.Elapsed = TimeSpan.FromMinutes(45) - TimeSpan.FromTicks(2);
        Assert.True(operation.Check(ApplicationAction.ResponseEnqueue).Allowed);
        source.Elapsed += TimeSpan.FromTicks(1);
        Assert.False(operation.Check(ApplicationAction.StartStage).Allowed);
        Assert.False(operation.Check(ApplicationAction.ResponseEnqueue).Allowed);
        Assert.True(source.Elapsed < RevocationPolicy.MaximumRevocationDelay);
        // Logical gates only: physical executor bounds are a separate deployment qualification.
    }
    [Fact]
    public async Task Bounded_refresh_lost_commit_ack_never_rediscloses_successor_or_access()
    {
        var cs = await Seed(); var session = await TeacherSession(cs); var source = new RevocationClock(); var time = source.Create();
        using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "test", "test", time); var ingress = new RevocationIngress(time);
        await using (var proxy = new DropCommitAcknowledgement(cs))
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await using var db = PostgresFixture.Open(proxy.ConnectionString);
            // The synthetic relay accepts one connection. Keep verifier discovery and
            // the subsequent transaction on it (production pooling is independent).
            await db.Database.OpenConnectionAsync(cancel.Token);
            await Assert.ThrowsAnyAsync<Exception>(() => new PostgresComponentTokenIssuer(db, time, jwt)
                .Refresh(session.RefreshToken!, A, ingress.Arrive(), cancel.Token));
            await proxy.Dropped.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        await using (var retry = PostgresFixture.Open(cs))
        {
            var result = await new PostgresComponentTokenIssuer(retry, time, jwt).Refresh(session.RefreshToken!, A, ingress.Arrive());
            Assert.Equal(SessionOutcome.RefreshAlreadyRotated, result.Outcome);
            Assert.Null(result.AccessToken); Assert.Null(result.RefreshToken);
        }
        source.Elapsed = TimeSpan.FromSeconds(31);
        await using var late = PostgresFixture.Open(cs);
        var denied = await new PostgresComponentTokenIssuer(late, time, jwt).Refresh(session.RefreshToken!, A, ingress.Arrive());
        Assert.Equal(SessionOutcome.Denied, denied.Outcome); Assert.Null(denied.AccessToken); Assert.Null(denied.RefreshToken);
        Assert.NotNull((await late.Set<IdentitySession>().AsNoTracking().SingleAsync()).RevokedAt);
    }

}
