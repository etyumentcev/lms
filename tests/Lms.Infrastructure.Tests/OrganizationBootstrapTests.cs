using System.Text.Json;
using Lms.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lms.Infrastructure.Tests;

public sealed class OrganizationBootstrapTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private const string Password = "Synthetic-First-Admin-2039!";
    private const string OperatorHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private readonly TestClock clock = new();
    private static readonly OrganizationBootstrapOptions Options = new()
    {
        ChallengeKey = Enumerable.Range(0, 32).Select(x => (byte)x).ToArray(),
        KeyVersion = 7
    };

    private async Task<BootstrapOrganizationResult> Create(string connectionString, Guid commandId,
        string name = "Синтетическая школа", string login = "synthetic-admin")
    {
        await using var db = PostgresFixture.Open(connectionString);
        return await new PostgresOrganizationBootstrap(db, clock, Options).Create(
            new(commandId, name, login, OperatorHash, "m1-03-test"));
    }

    private async Task Activate(string connectionString, BootstrapOrganizationResult result, string code,
        string password = Password)
    {
        await using var db = PostgresFixture.Open(connectionString);
        await new PostgresOrganizationBootstrap(db, clock, Options).Activate(
            new(result.OrganizationId, "synthetic-admin", code, password, "m1-03-test"));
    }

    [Fact]
    public async Task Create_is_atomic_idempotent_and_never_rediscloses_activation_secret()
    {
        var connectionString = await fixture.CreateDatabase();
        var commandId = Guid.NewGuid();
        var first = await Create(connectionString, commandId);
        var code = Assert.IsType<ActivationCode>(first.ActivationCode).Reveal();
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{4}(-[0-9A-HJKMNP-TV-Z]{4}){3}$", code);
        Assert.True(first.Created);

        // Simulate a lost successful response and a process restart: only the receipt survives.
        var replay = await Create(connectionString, commandId);
        Assert.False(replay.Created);
        Assert.Null(replay.ActivationCode);
        Assert.Equal(first with { Created = false, ActivationCode = null }, replay);

        var conflict = await Assert.ThrowsAsync<UseCaseException>(() =>
            Create(connectionString, commandId, "Другая школа"));
        Assert.Equal("IdempotencyConflict", conflict.Message);

        await using var check = PostgresFixture.Open(connectionString);
        Assert.Single(await check.Set<Organization>().ToListAsync());
        Assert.Single(await check.Set<OrganizationProfile>().ToListAsync());
        Assert.Single(await check.Users.ToListAsync());
        Assert.Single(await check.Set<ActorIdentity>().ToListAsync());
        var membership = Assert.Single(await check.Set<OrganizationMembership>().ToListAsync());
        Assert.Null(membership.EducationalRole);
        Assert.Equal((int)OrganizationPermission.ManageOrganization, membership.PermissionFlags);
        Assert.Empty(await check.Set<EnrollmentScope>().ToListAsync());
        Assert.Empty(await check.Set<TeacherAssignmentScope>().ToListAsync());
        var auditJson = JsonSerializer.Serialize(await check.Set<SecurityAudit>().AsNoTracking().ToListAsync());
        Assert.DoesNotContain(code.Replace("-", "", StringComparison.Ordinal), auditJson,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(code, auditJson, StringComparison.OrdinalIgnoreCase);

        var immutable = await Assert.ThrowsAsync<PostgresException>(() => check.Database.ExecuteSqlRawAsync(
            "UPDATE \"OrganizationBootstrapReceipts\" SET \"PayloadDigest\"=repeat('B',64)"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, immutable.SqlState);
    }

    [Fact]
    public async Task Activation_is_single_use_enables_login_and_yields_admin_only_authority()
    {
        var connectionString = await fixture.CreateDatabase();
        var created = await Create(connectionString, Guid.NewGuid());
        var code = created.ActivationCode!.Reveal();

        await using (var before = PostgresFixture.Open(connectionString))
        {
            var denied = await new PostgresIdentitySessions(before, clock)
                .Authenticate("synthetic-admin", Password);
            Assert.Equal(SessionOutcome.Denied, denied.Outcome);
        }

        await Activate(connectionString, created, code);
        var replay = await Assert.ThrowsAsync<UseCaseException>(() => Activate(connectionString, created, code));
        Assert.Equal("ActivationUnavailable", replay.Message);

        SessionResult login;
        await using (var sessionDb = PostgresFixture.Open(connectionString))
            login = await new PostgresIdentitySessions(sessionDb, clock)
                .Authenticate("synthetic-admin", Password);
        Assert.Equal(SessionOutcome.Success, login.Outcome);
        Assert.Equal(created.AdminActorId, login.Identity!.ActorId);

        await using var authorityDb = PostgresFixture.Open(connectionString);
        var authority = await new PostgresScopedAuthorityReader(authorityDb, clock)
            .ResolveCurrent(created.OrganizationId, created.AdminActorId);
        Assert.NotNull(authority);
        Assert.Null(authority!.Membership.Role);
        Assert.True(OrganizationPermissionPolicy.CanManageOrganization(authority.Membership));
        Assert.Empty(authority.Grants);
        Assert.False(ScopePolicy.Allows(authority,
            new(created.OrganizationId, "synthetic-course"), ScopeAction.Read, created.AdminActorId));

        var admin = new PostgresOrganizationAdministrationQueries(authorityDb);
        Assert.Equal("Синтетическая школа",
            (await admin.Overview(created.OrganizationId, created.AdminActorId))!.OrganizationName);
        Assert.Contains(await admin.Audit(created.OrganizationId, created.AdminActorId, 100),
            x => x.Action == "first_admin_activated" && x.Result == "success");
        Assert.Null(await admin.Overview(Guid.NewGuid(), created.AdminActorId));
        var foreign = await Assert.ThrowsAsync<UseCaseException>(() =>
            admin.Audit(Guid.NewGuid(), created.AdminActorId, 100));
        Assert.Equal("Forbidden", foreign.Message);
    }

    [Fact]
    public async Task Five_invalid_codes_lock_activation_and_are_audited()
    {
        var connectionString = await fixture.CreateDatabase();
        var created = await Create(connectionString, Guid.NewGuid());
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var denied = await Assert.ThrowsAsync<UseCaseException>(() =>
                Activate(connectionString, created, "0000-0000-0000-0000"));
            Assert.Equal(attempt == 5 ? "ActivationLocked" : "InvalidActivationCode", denied.Message);
        }
        var correct = await Assert.ThrowsAsync<UseCaseException>(() =>
            Activate(connectionString, created, created.ActivationCode!.Reveal()));
        Assert.Equal("ActivationUnavailable", correct.Message);

        await using var check = PostgresFixture.Open(connectionString);
        var challenge = await check.Set<AccessChallenge>().SingleAsync();
        Assert.Equal("Locked", challenge.State);
        Assert.Equal(5, challenge.FailedAttempts);
        Assert.False((await check.Set<ActorIdentity>().SingleAsync()).IsActive);
        Assert.Equal(6, await check.Set<SecurityAudit>()
            .CountAsync(x => x.Action == "first_admin_activation_denied"));
    }

    [Fact]
    public async Task Exact_expiry_boundary_denies_and_closes_challenge()
    {
        var connectionString = await fixture.CreateDatabase();
        var created = await Create(connectionString, Guid.NewGuid());
        clock.Advance(TimeSpan.FromHours(24));
        var expired = await Assert.ThrowsAsync<UseCaseException>(() =>
            Activate(connectionString, created, created.ActivationCode!.Reveal()));
        Assert.Equal("ActivationExpired", expired.Message);

        await using var check = PostgresFixture.Open(connectionString);
        Assert.Equal("Expired", (await check.Set<AccessChallenge>().SingleAsync()).State);
        Assert.False((await check.Set<ActorIdentity>().SingleAsync()).IsActive);
    }
}
