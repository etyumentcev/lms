using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.RateLimiting;
using Lms.Application;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Lms.Api.Tests;

public sealed class CourseRosterBrowserTests(ApiPostgresFixture fixture) : IClassFixture<ApiPostgresFixture>
{
    private sealed class BrowserClock : TimeProvider, IQualifiedClockSource
    {
        private readonly DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public QualifiedClockReading Read() => new("m1-05-browser", true, new(now, now), TimeSpan.Zero, TimeSpan.Zero);
    }

    [BrowserReadTheory]
    [InlineData(true)]
    public async Task Real_chromium_admin_builds_roster_and_role_catalogs_are_isolated(bool browser)
    {
        Assert.True(browser);
        await using var database = await fixture.CreateApp(); await using var databaseContext = database.Open();
        var connectionString = databaseContext.Database.GetConnectionString()!;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Lms.sln"))) root = root.Parent;
        Assert.NotNull(root); var web = Path.Combine(root.FullName, "src/Lms.Web/dist");
        Assert.True(File.Exists(Path.Combine(web, "index.html")), "Build the production React UI first");

        var time = new BrowserClock();
        var options = new OrganizationBootstrapOptions { ChallengeKey = Enumerable.Range(90, 32).Select(x => (byte)x).ToArray(), KeyVersion = 12 };
        const string adminPassword = "Synthetic-Browser-Admin-2039!";
        const string participantPassword = "Synthetic-Browser-Participant-2039!";
        var adminUser = "m105-browser-admin-" + Guid.NewGuid().ToString("N")[..6];
        var foreignAdminUser = "m105-browser-foreign-" + Guid.NewGuid().ToString("N")[..6];
        async Task<BootstrapOrganizationResult> Organization(string name, string user)
        {
            await using var create = database.Open();
            var result = await new PostgresOrganizationBootstrap(create, time, options).Create(new(Guid.NewGuid(),
                name, user, new string('B', 64), "m1-05-browser"));
            await using var activate = database.Open();
            await new PostgresOrganizationBootstrap(activate, time, options).Activate(new(result.OrganizationId,
                user, result.ActivationCode!.Reveal(), adminPassword, "m1-05-browser"));
            return result;
        }
        var organization = await Organization("Синтетический центр Эйлера", adminUser);
        var foreignOrganization = await Organization("Синтетический чужой центр", foreignAdminUser);
        async Task<ProvisionAccountResult> Participant(string roster, string display, string user, AccountRole role)
        {
            await using var provision = database.Open();
            var result = await new PostgresAccountAdministration(provision, time, options).Provision(new(
                organization.OrganizationId, organization.AdminActorId, roster, display, user, role,
                Guid.NewGuid().ToString(), "m1-05-browser"));
            await using var activate = database.Open();
            await new PostgresAccountAdministration(activate, time, options).Activate(new(organization.OrganizationId,
                user, result.ActivationCode!.Reveal(), participantPassword, "m1-05-browser"));
            return result;
        }
        var studentUsers = new List<string>(); var studentDisplays = new List<string>();
        for (var index = 1; index <= 12; index++)
        {
            var user = $"m105-student-{index:00}-" + Guid.NewGuid().ToString("N")[..5];
            var display = $"Синтетический ученик {index:00}";
            await Participant($"m105-student-{index:00}", display, user, AccountRole.Student);
            studentUsers.Add(user); studentDisplays.Add(display);
        }
        ProvisionAccountResult sharedLink;
        await using (var link = database.Open())
            sharedLink = await new PostgresAccountAdministration(link, time, options).Provision(new(
                foreignOrganization.OrganizationId, foreignOrganization.AdminActorId, "m105-shared-switch",
                studentDisplays[0], studentUsers[0], AccountRole.Student, Guid.NewGuid().ToString(), "m1-05-browser"));
        Assert.Equal("AwaitingIndependentApproval", sharedLink.State); Assert.NotNull(sharedLink.ApprovalRequestId);
        await using (var approve = database.Open())
        {
            var approved = await new PostgresAccountAdministration(approve, time, options).Decide(new(
                sharedLink.ApprovalRequestId!.Value, "synthetic-shared-switch-check", new string('C', 64),
                true, "m1-05-browser"));
            Assert.Equal("Approved", approved.State);
        }
        var teacherUser = "m105-teacher-" + Guid.NewGuid().ToString("N")[..6];
        const string teacherDisplay = "Синтетический преподаватель Эйлера";
        await Participant("m105-teacher-01", teacherDisplay, teacherUser, AccountRole.Teacher);
        var unassignedTeacherUser = "m105-unassigned-" + Guid.NewGuid().ToString("N")[..6];
        await Participant("m105-teacher-02", "Синтетический неназначенный преподаватель", unassignedTeacherUser, AccountRole.Teacher);

        using var tls = RSA.Create(2048); var certificateRequest = new CertificateRequest("CN=localhost", tls,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(IPAddress.Loopback); certificateRequest.CertificateExtensions.Add(san.Build());
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var origin = $"https://127.0.0.1:{port}";
        var authorityClock = new QualifiedAuthorityClock(time, TimeSpan.Zero, TimeSpan.Zero);
        using var componentKey = RSA.Create(2048); var componentJwt = new ComponentJwt(componentKey, "m1-05-browser", "m1-05-browser", authorityClock);
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, port, listen => listen.UseHttps(certificate)));
        builder.Services.AddSingleton(time); builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddSingleton(authorityClock); builder.Services.AddSingleton<IAuthorityClock>(authorityClock); builder.Services.AddSingleton(componentJwt);
        builder.Services.AddDbContextFactory<LmsDbContext>(db => db.UseNpgsql(connectionString));
        builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<LmsDbContext>>().CreateDbContext());
        builder.Services.AddSingleton(options);
        builder.Services.AddScoped<IOrganizationBootstrap, PostgresOrganizationBootstrap>();
        builder.Services.AddScoped<IOrganizationAdministrationQueries, PostgresOrganizationAdministrationQueries>();
        builder.Services.AddScoped<PostgresAccountAdministration>();
        builder.Services.AddScoped<IAccountAdministration>(sp => sp.GetRequiredService<PostgresAccountAdministration>());
        builder.Services.AddScoped<IAccountSecurityOperations>(sp => sp.GetRequiredService<PostgresAccountAdministration>());
        builder.Services.AddScoped<ICourseRosterAdministration, PostgresCourseRosterAdministration>();
        builder.Services.AddScoped<PostgresTeacherCommands>(); builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddTeacherRevocationBrowserProtection();
        builder.Services.AddRateLimiter(rate => rate.AddPolicy(OrganizationBootstrapHttp.ActivationRatePolicy,
            _ => RateLimitPartition.GetFixedWindowLimiter("browser", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 100, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
        builder.Services.AddAuthentication(CourseBearerHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, CourseBearerHandler>(CourseBearerHandler.SchemeName, _ => { })
            .AddJwtBearer(OrganizationBootstrapHttp.PlatformBearerScheme, jwt => jwt.TokenValidationParameters = new()
            {
                ValidIssuer = "unused-m1-05-browser", ValidAudience = OrganizationBootstrapHttp.PlatformAudience,
                IssuerSigningKey = new SymmetricSecurityKey(Enumerable.Repeat((byte)7, 32).ToArray()),
                ValidateIssuer = true, ValidateAudience = true, ValidateIssuerSigningKey = true,
                ValidateLifetime = true, RequireSignedTokens = true, ClockSkew = TimeSpan.Zero
            });
        builder.Services.AddAuthorization();

        await using var app = builder.Build(); app.UseMiddleware<ApiExceptionMiddleware>(); app.UseMiddleware<CourseRequestIngress>();
        app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter(); app.MapTeacherRevocation(origin);
        app.MapOrganizationBootstrap(origin); app.MapAccountAdministration(origin); app.MapCourseRoster(origin);
        app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(web) });
        app.MapFallback(async context =>
        {
            if (context.Request.Path.StartsWithSegments("/api")) { context.Response.StatusCode = 404; return; }
            context.Response.ContentType = "text/html; charset=utf-8";
            await Microsoft.AspNetCore.Http.SendFileResponseExtensions.SendFileAsync(context.Response, Path.Combine(web, "index.html"));
        });
        await app.StartAsync();

        var evidence = Path.Combine(root.FullName, "codex/evidence/M1-05-COURSE-ROSTER"); Directory.CreateDirectory(evidence);
        var node = Environment.GetEnvironmentVariable("LMS_BROWSER_NODE") ?? "node";
        var start = new ProcessStartInfo(node) { WorkingDirectory = root.FullName, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        var executable = Environment.GetEnvironmentVariable("LMS_BROWSER_EXECUTABLE");
        if (string.IsNullOrWhiteSpace(executable) && File.Exists("/usr/bin/google-chrome")) executable = "/usr/bin/google-chrome";
        if (!string.IsNullOrWhiteSpace(executable)) start.Environment["LMS_BROWSER_EXECUTABLE"] = executable;
        start.ArgumentList.Add(Path.Combine(root.FullName, "tools/browser_read/course-roster.mjs"));
        using var runner = Process.Start(start)!; using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var errors = runner.StandardError.ReadToEndAsync(timeout.Token);
        await runner.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { origin,
            organizationId = organization.OrganizationId, foreignOrganizationId = foreignOrganization.OrganizationId,
            adminUser, foreignAdminUser, adminPassword, participantPassword, teacherUser,
            teacherDisplayName = teacherDisplay, unassignedTeacherUser, studentUsers,
            studentDisplayNames = studentDisplays, courseTitle = "Подготовка к олимпиаде Эйлера", evidence }));
        var passed = await runner.StandardOutput.ReadLineAsync(timeout.Token) == "PASS"; await runner.WaitForExitAsync(timeout.Token);
        var diagnostic = (await errors).Replace(adminPassword, "[REDACTED]").Replace(participantPassword, "[REDACTED]");
        Assert.True(passed && runner.ExitCode == 0, diagnostic);

        await using var verify = database.Open();
        var course = await verify.Set<CourseCatalogRecord>().SingleAsync(x => x.OrganizationId == organization.OrganizationId);
        Assert.Equal("Draft", course.State); Assert.Equal(2, await verify.Set<CourseGroupCatalogRecord>().CountAsync(x => x.CourseId == course.CourseId));
        Assert.Equal(11, await verify.Set<EnrollmentScope>().CountAsync(x => x.CourseId == course.CourseId && x.State == "Active"));
        Assert.Equal(1, await verify.Set<EnrollmentScope>().CountAsync(x => x.CourseId == course.CourseId && x.State == "Revoked"));
        Assert.Single(await verify.Set<TeacherAssignmentScope>().Where(x => x.CourseId == course.CourseId && x.State == "Active").ToListAsync());
        Assert.Empty(await verify.Set<CourseCatalogRecord>().Where(x => x.OrganizationId == foreignOrganization.OrganizationId).ToListAsync());
        await File.WriteAllTextAsync(Path.Combine(evidence, "test-summary.json"), JsonSerializer.Serialize(new
        { passed = true, course = course.Title, state = course.State, groups = 2, activeStudents = 11, removedStudents = 1,
            organizationSwitchIsolated = true, screenshots = 10 }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
