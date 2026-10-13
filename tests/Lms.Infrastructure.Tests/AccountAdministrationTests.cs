using System.Text.Json;
using Lms.Application;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Tests;

public sealed class AccountAdministrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private const string OperatorHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private readonly TestClock clock = new();
    private static readonly OrganizationBootstrapOptions Options = new()
    {
        ChallengeKey = Enumerable.Range(40, 32).Select(x => (byte)x).ToArray(), KeyVersion = 9
    };

    private async Task<(Guid Org, Guid Admin)> Admin(string cs, string suffix)
    {
        await using var create = PostgresFixture.Open(cs);
        var result = await new PostgresOrganizationBootstrap(create, clock, Options).Create(new(
            Guid.NewGuid(), "Синтетическая школа " + suffix, "admin-" + suffix, OperatorHash, "m1-04-test"));
        await using var activate = PostgresFixture.Open(cs);
        await new PostgresOrganizationBootstrap(activate, clock, Options).Activate(new(result.OrganizationId,
            "admin-" + suffix, result.ActivationCode!.Reveal(), "Synthetic-Admin-Password-2039!", "m1-04-test"));
        return (result.OrganizationId, result.AdminActorId);
    }

    private async Task<ProvisionAccountResult> Provision(string cs, Guid org, Guid admin, string roster,
        string login, AccountRole role)
    {
        await using var db = PostgresFixture.Open(cs);
        return await new PostgresAccountAdministration(db, clock, Options).Provision(new(org, admin, roster,
            role == AccountRole.Student ? "Синтетический Ученик" : "Синтетический Преподаватель",
            login, role, Guid.NewGuid().ToString(), "m1-04-test"));
    }

    [Fact]
    public async Task Student_teacher_activation_listing_errors_and_cross_org_isolation_work()
    {
        var cs = await fixture.CreateDatabase();
        var a = await Admin(cs, "a"); var b = await Admin(cs, "b");
        var student = await Provision(cs, a.Org, a.Admin, "student-001", "student.one", AccountRole.Student);
        var teacher = await Provision(cs, a.Org, a.Admin, "teacher-001", "teacher.one", AccountRole.Teacher);
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{4}(-[0-9A-HJKMNP-TV-Z]{4}){3}$", student.ActivationCode!.Reveal());

        await using (var listDb = PostgresFixture.Open(cs))
        {
            var accounts = new PostgresAccountAdministration(listDb, clock, Options);
            var students = await accounts.List(new(a.Org, a.Admin, "Ученик", AccountRole.Student,
                "PendingActivation", 0, 10));
            Assert.Single(students.Items); Assert.Equal(1, students.Total);
            var foreign = await Assert.ThrowsAsync<UseCaseException>(() => accounts.List(new(a.Org, b.Admin,
                null, null, null, 0, 10)));
            Assert.Equal("Forbidden", foreign.Message);
        }

        await using (var invalidDb = PostgresFixture.Open(cs))
        {
            var accounts = new PostgresAccountAdministration(invalidDb, clock, Options);
            var invalid = await Assert.ThrowsAsync<UseCaseException>(() => accounts.Activate(new(a.Org,
                "student.one", "0000-0000-0000-0000", "Synthetic-Student-Password-2039!", "m1-04-test")));
            Assert.Equal("ActivationInvalid", invalid.Message);
        }
        await using (var activationDb = PostgresFixture.Open(cs))
            await new PostgresAccountAdministration(activationDb, clock, Options).Activate(new(a.Org,
                "student.one", student.ActivationCode.Reveal(), "Synthetic-Student-Password-2039!", "m1-04-test"));
        await using (var usedDb = PostgresFixture.Open(cs))
        {
            var used = await Assert.ThrowsAsync<UseCaseException>(() => new PostgresAccountAdministration(usedDb, clock, Options)
                .Activate(new(a.Org, "student.one", student.ActivationCode.Reveal(),
                    "Synthetic-Student-Password-2039!", "m1-04-test")));
            Assert.Equal("ActivationUsed", used.Message);
        }
        await using (var teacherDb = PostgresFixture.Open(cs))
            await new PostgresAccountAdministration(teacherDb, clock, Options).Activate(new(a.Org,
                "teacher.one", teacher.ActivationCode!.Reveal(), "Synthetic-Teacher-Password-2039!", "m1-04-test"));

        await using var check = PostgresFixture.Open(cs);
        Assert.Equal(2, await check.Set<OrganizationParticipantProfile>().CountAsync(x => x.OrganizationId == a.Org));
        Assert.Empty(await check.Set<EnrollmentScope>().Where(x => x.OrganizationId == a.Org).ToListAsync());
        Assert.Empty(await check.Set<TeacherAssignmentScope>().Where(x => x.OrganizationId == a.Org).ToListAsync());
        var audit = JsonSerializer.Serialize(await check.Set<SecurityAudit>().Where(x => x.OrganizationId == a.Org).ToListAsync());
        Assert.DoesNotContain(student.ActivationCode.Reveal(), audit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Synthetic-Student-Password", audit, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Recovery_requires_independent_approval_revokes_sessions_and_shared_identity_is_linked_not_duplicated()
    {
        var cs = await fixture.CreateDatabase(); var a = await Admin(cs, "ra"); var b = await Admin(cs, "rb");
        var student = await Provision(cs, a.Org, a.Admin, "shared-001", "shared.student", AccountRole.Student);
        await using (var activationDb = PostgresFixture.Open(cs))
            await new PostgresAccountAdministration(activationDb, clock, Options).Activate(new(a.Org,
                student.UserName, student.ActivationCode!.Reveal(), "Synthetic-Old-Password-2039!", "m1-04-test"));

        SessionResult oldSession;
        await using (var loginDb = PostgresFixture.Open(cs)) oldSession = await new PostgresIdentitySessions(loginDb, clock)
            .Authenticate(student.UserName, "Synthetic-Old-Password-2039!");
        Assert.Equal(SessionOutcome.Success, oldSession.Outcome);

        var link = await Provision(cs, b.Org, b.Admin, "shared-b-001", student.UserName, AccountRole.Student);
        Assert.Equal("AwaitingIndependentApproval", link.State); Assert.NotNull(link.ApprovalRequestId);
        const string securityHash = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        await using (var approveLinkDb = PostgresFixture.Open(cs))
        {
            var security = new PostgresAccountAdministration(approveLinkDb, clock, Options);
            var approved = await security.Decide(new(link.ApprovalRequestId!.Value, "synthetic-link-check",
                securityHash, true, "m1-04-test"));
            Assert.Equal("Approved", approved.State);
        }

        clock.Advance(TimeSpan.FromSeconds(61));
        Guid requestId;
        await using (var requestDb = PostgresFixture.Open(cs))
        {
            var result = await new PostgresAccountAdministration(requestDb, clock, Options).RequestRecovery(new(
                a.Org, a.Admin, student.ActorId, "synthetic-recipient-check", Guid.NewGuid().ToString(), "m1-04-test"));
            requestId = result.RequestId; Assert.True(result.RequiresIndependentApproval); Assert.Null(result.RecoveryCode);
        }
        string recoveryCode;
        await using (var approvalDb = PostgresFixture.Open(cs))
        {
            var operations = new PostgresAccountAdministration(approvalDb, clock, Options);
            Assert.Contains(await operations.Pending(securityHash), x => x.RequestId == requestId);
            var approval = await operations.Decide(new(requestId, "synthetic-independent-check", securityHash,
                true, "m1-04-test"));
            recoveryCode = approval.RecoveryCode!.Reveal();
        }
        await using (var consumeDb = PostgresFixture.Open(cs))
            await new PostgresAccountAdministration(consumeDb, clock, Options).ConsumeRecovery(new(a.Org,
                student.UserName, recoveryCode, "Synthetic-New-Password-2039!", "m1-04-test"));

        await using (var oldDb = PostgresFixture.Open(cs)) Assert.Equal(SessionOutcome.Denied,
            (await new PostgresIdentitySessions(oldDb, clock).Authenticate(student.UserName, "Synthetic-Old-Password-2039!")).Outcome);
        await using (var newDb = PostgresFixture.Open(cs)) Assert.Equal(SessionOutcome.Success,
            (await new PostgresIdentitySessions(newDb, clock).Authenticate(student.UserName, "Synthetic-New-Password-2039!")).Outcome);
        await using var check = PostgresFixture.Open(cs);
        Assert.Equal(1, await check.Users.CountAsync(x => x.Id == student.UserId));
        Assert.Equal(2, await check.Set<OrganizationMembership>().CountAsync(x => x.ActorId == student.ActorId && x.State == "Active"));
        Assert.NotNull((await check.Set<IdentitySession>().SingleAsync(x => x.SessionId == oldSession.Identity!.SessionId)).RevokedAt);
        Assert.Equal("Fulfilled", (await check.Set<RecoveryRequest>().SingleAsync(x => x.RequestId == requestId)).State);
    }
}
