using Lms.Application;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Tests;

public sealed partial class ScopedAuthorityTests
{
    [Theory]
    [InlineData("Message")]
    [InlineData("Upload")]
    [InlineData("Delete")]
    public async Task Unversioned_intent_retry_new_key_and_expired_deadline_never_reexecute(string kind)
    {
        var cs = await Seed(); var source = new RevocationClock(); var ingress = new RevocationIngress(source.Create());
        var intent = Intent() with { Kind = kind, ExpectedVersion = null, IntentId = Guid.NewGuid() };
        async Task<TeacherOperationAttempt> Reserve(TeacherOperationIntent candidate)
        {
            await using var db = PostgresFixture.Open(cs);
            return await new PostgresTeacherOperationAttempts(db).Reserve(candidate, ingress.Arrive(ApplicationRequestKind.TeacherOperation));
        }
        var first = await Reserve(intent); Assert.True(first.FirstAttempt);
        source.Elapsed = TimeSpan.FromMinutes(31);
        var retry = await Reserve(intent with { CommandId = Guid.NewGuid() });
        Assert.False(retry.FirstAttempt); Assert.Equal(first.OperationId, retry.OperationId); Assert.Equal(first.Deadline, retry.Deadline);
        await Assert.ThrowsAsync<UseCaseException>(() => Reserve(intent with { CommandId = Guid.NewGuid(), Fingerprint = new string('B',64) }));
        // Equal content can be an intentionally different message/upload. Identity is not payload deduplication.
        Assert.True((await Reserve(intent with { CommandId = Guid.NewGuid(), IntentId = Guid.NewGuid() })).FirstAttempt);
    }

    [Fact]
    public async Task Unversioned_intent_requires_exactly_one_identity_and_database_enforces_it()
    {
        var cs = await Seed(); var ingress = new RevocationIngress(new RevocationClock().Create());
        await using var db = PostgresFixture.Open(cs);
        foreach (var invalid in new[] { Intent() with { ExpectedVersion = null }, Intent() with { IntentId = Guid.NewGuid() },
            Intent() with { ExpectedVersion = null, IntentId = Guid.Empty } })
            await Assert.ThrowsAsync<UseCaseException>(() => new PostgresTeacherOperationAttempts(db).Reserve(invalid, ingress.Arrive(ApplicationRequestKind.TeacherOperation)));
        Assert.Equal(0, await db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM public."TeacherOperationAttempts"
            """).SingleAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public."TeacherOperationAttempts"
            ("OperationId","OrganizationId","ActorId","CommandId","Kind","Resource","ExpectedVersion","Fingerprint","Deadline")
            VALUES ({Guid.NewGuid()},{A},{T},{Guid.NewGuid()},'Message','work',NULL,{new string('A',64)},{At.AddMinutes(30)})
            """));
    }

    [Fact]
    public async Task Unversioned_intent_unknown_registration_commit_cannot_execute_after_reconnect()
    {
        var cs = await Seed(); var ingress = new RevocationIngress(new RevocationClock().Create());
        var intent = Intent() with { ExpectedVersion = null, IntentId = Guid.NewGuid(), Kind = "Upload" };
        await using (var proxy = new DropCommitAcknowledgement(cs))
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await using var db = PostgresFixture.Open(proxy.ConnectionString);
            await Assert.ThrowsAnyAsync<Exception>(() => new PostgresTeacherOperationAttempts(db).Reserve(intent,
                ingress.Arrive(ApplicationRequestKind.TeacherOperation), cancellation.Token));
            await proxy.Dropped.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        await using var retry = PostgresFixture.Open(cs);
        var result = await new PostgresTeacherOperationAttempts(retry).Reserve(intent with { CommandId = Guid.NewGuid() },
            ingress.Arrive(ApplicationRequestKind.TeacherOperation));
        Assert.False(result.FirstAttempt);
    }
}
