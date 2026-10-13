using System.Security.Cryptography;
using Lms.Application;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Tests;

public sealed partial class ScopedAuthorityTests
{
    [Fact]
    public async Task Teacher_login_reads_fresh_scope_and_keeps_identity_without_revoked_grants()
    {
        var cs = await Seed(); await TeacherSession(cs); var time = new RevocationClock().Create();
        using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "login", "login", time);
        await using (var db = PostgresFixture.Open(cs))
        {
            var result = await new PostgresComponentTokenIssuer(db, time, jwt).Login("bounded-teacher", "Synthetic-bounded-password-123",
                A, new RevocationIngress(time).Arrive());
            Assert.Equal(SessionOutcome.Success, result.Outcome); Assert.NotNull(result.AccessToken);
        }
        await Change(cs, await RevokeTeacherCommand(cs, false));
        await using var fresh = PostgresFixture.Open(cs);
        var after = await new PostgresComponentTokenIssuer(fresh, time, jwt).Login("bounded-teacher", "Synthetic-bounded-password-123",
            A, new RevocationIngress(time).Arrive());
        Assert.Equal(SessionOutcome.Success, after.Outcome); Assert.Null(after.AccessToken); Assert.NotNull(after.RefreshToken);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Teacher_login_late_or_unknown_commit_returns_no_credentials(bool unknown)
    {
        var cs = await Seed(); await TeacherSession(cs); var source = new RevocationClock(); var time = source.Create();
        using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "login", "login", time);
        await using (var db = PostgresFixture.Open(cs, new AfterRefreshCommit(() => {
            if (unknown) throw new IOException("Synthetic unknown login commit");
            source.Elapsed = TimeSpan.FromMinutes(1);
        })))
        {
            Task<SessionResult> Login() => new PostgresComponentTokenIssuer(db, time, jwt).Login("bounded-teacher",
                "Synthetic-bounded-password-123", A, new RevocationIngress(time).Arrive());
            if (unknown) await Assert.ThrowsAsync<IOException>(Login);
            else await Assert.ThrowsAsync<OperationCanceledException>(Login);
        }
        await using var verify = PostgresFixture.Open(cs);
        Assert.Equal(2, await verify.Set<IdentitySession>().CountAsync());
    }
}
