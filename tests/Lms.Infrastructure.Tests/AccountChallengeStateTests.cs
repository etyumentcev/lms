using Lms.Application;

namespace Lms.Infrastructure.Tests;

public sealed class AccountChallengeStateTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private readonly TestClock clock = new();
    private static readonly OrganizationBootstrapOptions Options = new()
    { ChallengeKey = Enumerable.Range(80, 32).Select(x => (byte)x).ToArray(), KeyVersion = 11 };

    private async Task<(string Cs, Guid Org, Guid Admin)> Setup()
    {
        var cs = await fixture.CreateDatabase();
        BootstrapOrganizationResult bootstrap;
        await using (var db = PostgresFixture.Open(cs)) bootstrap = await new PostgresOrganizationBootstrap(db, clock, Options).Create(new(
            Guid.NewGuid(), "Синтетическая школа кодов", "challenge-admin",
            "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC", "m1-04-test"));
        await using (var db = PostgresFixture.Open(cs)) await new PostgresOrganizationBootstrap(db, clock, Options).Activate(new(
            bootstrap.OrganizationId, "challenge-admin", bootstrap.ActivationCode!.Reveal(),
            "Synthetic-Challenge-Admin-2039!", "m1-04-test"));
        return (cs, bootstrap.OrganizationId, bootstrap.AdminActorId);
    }

    private async Task<ProvisionAccountResult> Student(string cs, Guid org, Guid admin, string suffix)
    {
        await using var db = PostgresFixture.Open(cs);
        return await new PostgresAccountAdministration(db, clock, Options).Provision(new(org, admin,
            "roster-" + suffix, "Ученик " + suffix, "student-" + suffix, AccountRole.Student,
            Guid.NewGuid().ToString(), "m1-04-test"));
    }

    [Fact]
    public async Task Activation_reports_expired_locked_and_one_concurrent_winner()
    {
        var setup = await Setup();
        var expired = await Student(setup.Cs, setup.Org, setup.Admin, "expired");
        clock.Advance(TimeSpan.FromHours(24));
        await using (var db = PostgresFixture.Open(setup.Cs))
        {
            var error = await Assert.ThrowsAsync<UseCaseException>(() => new PostgresAccountAdministration(db, clock, Options).Activate(new(
                setup.Org, expired.UserName, expired.ActivationCode!.Reveal(), "Synthetic-Expired-Password-2039!", "m1-04-test")));
            Assert.Equal("ActivationExpired", error.Message);
        }

        var locked = await Student(setup.Cs, setup.Org, setup.Admin, "locked");
        for (var i = 1; i <= 5; i++)
        {
            await using var db = PostgresFixture.Open(setup.Cs);
            var error = await Assert.ThrowsAsync<UseCaseException>(() => new PostgresAccountAdministration(db, clock, Options).Activate(new(
                setup.Org, locked.UserName, "0000-0000-0000-0000", "Synthetic-Locked-Password-2039!", "m1-04-test")));
            Assert.Equal(i == 5 ? "ActivationLocked" : "ActivationInvalid", error.Message);
        }

        var race = await Student(setup.Cs, setup.Org, setup.Admin, "race");
        async Task<string> Consume()
        {
            await using var db = PostgresFixture.Open(setup.Cs);
            try
            {
                await new PostgresAccountAdministration(db, clock, Options).Activate(new(setup.Org, race.UserName,
                    race.ActivationCode!.Reveal(), "Synthetic-Race-Password-2039!", "m1-04-test"));
                return "success";
            }
            catch (UseCaseException e) { return e.Message; }
        }
        var outcomes = await Task.WhenAll(Consume(), Consume());
        Assert.Contains("success", outcomes); Assert.Contains("ActivationUsed", outcomes);
    }
}
