using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lms.Application;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Lms.Api.Tests;

public sealed class CourseEditorAuthorityBrowserTests(ApiPostgresFixture fixture) : IClassFixture<ApiPostgresFixture>
{
    private sealed class BrowserClock : TimeProvider, IQualifiedClockSource
    {
        private readonly DateTimeOffset started = DateTimeOffset.UtcNow;
        private readonly long timestamp = Stopwatch.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => started + Stopwatch.GetElapsedTime(timestamp);
        public QualifiedClockReading Read()
        {
            var elapsed = Stopwatch.GetElapsedTime(timestamp);
            var now = started + elapsed;
            return new("m1-11-fix-01-browser", true, new(now, now), elapsed, elapsed);
        }
    }

    [BrowserReadTheory]
    [InlineData(true)]
    public async Task Real_chromium_admin_assigns_editor_then_teacher_publishes_only_for_G1(bool browser)
    {
        Assert.True(browser);
        await using var database = await fixture.CreateApp();
        await using var databaseContext = database.Open();
        var connectionString = databaseContext.Database.GetConnectionString()!;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Lms.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var web = Path.Combine(root.FullName, "src/Lms.Web/dist");
        Assert.True(File.Exists(Path.Combine(web, "index.html")), "Build the production React UI first");

        var clock = new BrowserClock();
        var options = new OrganizationBootstrapOptions
        {
            ChallengeKey = Enumerable.Range(121, 32).Select(x => (byte)x).ToArray(),
            KeyVersion = 21
        };
        const string adminPassword = "Synthetic-Fix01-Admin-2039!";
        const string participantPassword = "Synthetic-Fix01-Participant-2039!";
        const string teacherDisplay = "T-A";
        const string courseTitle = "Подготовка к олимпиаде Эйлера";
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var adminUser = "fix01-admin-" + suffix;
        var teacherUser = "fix01-teacher-" + suffix;
        var studentUser = "fix01-student-" + suffix;

        BootstrapOrganizationResult organization;
        await using (var create = database.Open())
            organization = await new PostgresOrganizationBootstrap(create, clock, options).Create(new(
                Guid.NewGuid(), "Синтетическая школа FIX-01", adminUser, new string('D', 64), "m1-11-fix-01"));
        await using (var activate = database.Open())
            await new PostgresOrganizationBootstrap(activate, clock, options).Activate(new(
                organization.OrganizationId, adminUser, organization.ActivationCode!.Reveal(),
                adminPassword, "m1-11-fix-01"));

        async Task<ProvisionAccountResult> Participant(string roster, string display, string user, AccountRole role)
        {
            await using var provision = database.Open();
            var result = await new PostgresAccountAdministration(provision, clock, options).Provision(new(
                organization.OrganizationId, organization.AdminActorId, roster, display, user, role,
                Guid.NewGuid().ToString(), "m1-11-fix-01"));
            await using var activate = database.Open();
            await new PostgresAccountAdministration(activate, clock, options).Activate(new(
                organization.OrganizationId, user, result.ActivationCode!.Reveal(),
                participantPassword, "m1-11-fix-01"));
            return result;
        }

        var teacher = await Participant("fix01-teacher", teacherDisplay, teacherUser, AccountRole.Teacher);
        var student = await Participant("fix01-student", "P01", studentUser, AccountRole.Student);

        CourseCreated course;
        GroupCreated first;
        GroupCreated second;
        await using (var db = database.Open())
        {
            var service = new PostgresCourseRosterAdministration(db, clock);
            course = await service.CreateCourse(new(organization.OrganizationId, organization.AdminActorId,
                courseTitle, "Синтетический курс FIX-01", Guid.NewGuid().ToString(), "m1-11-fix-01"));
            first = await service.CreateGroup(new(organization.OrganizationId, organization.AdminActorId,
                course.CourseId, "G1", "Основная группа", Guid.NewGuid().ToString(), "m1-11-fix-01"));
            second = await service.CreateGroup(new(organization.OrganizationId, organization.AdminActorId,
                course.CourseId, "G2", "Контрольная группа", Guid.NewGuid().ToString(), "m1-11-fix-01"));
            await service.Enroll(new(organization.OrganizationId, organization.AdminActorId, course.CourseId,
                first.GroupId, [student.ActorId], Guid.NewGuid().ToString(), "m1-11-fix-01"));
            await service.Assign(new(organization.OrganizationId, organization.AdminActorId, course.CourseId,
                first.GroupId, teacher.ActorId, Guid.NewGuid().ToString(), "m1-11-fix-01"));
        }

        var contextId = Guid.NewGuid();
        await using (var db = database.Open())
            await new PostgresProcessingStateAdministration(db).Change(new(
                organization.OrganizationId, organization.AdminActorId, 0,
                new AddProcessingContext(contextId, 1, clock.GetUtcNow().AddMinutes(-1))));
        await using (var db = database.Open())
        {
            db.Add(new CourseContextBinding
            {
                CourseId = course.CourseId,
                OrganizationId = organization.OrganizationId,
                ContextId = contextId,
                ContextVersion = 1,
                EffectiveFrom = clock.GetUtcNow().AddMinutes(-1)
            });
            await db.SaveChangesAsync();
        }

        using var tls = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=localhost", tls,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Loopback);
        certificateRequest.CertificateExtensions.Add(san.Build());
        using var certificate = certificateRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var origin = $"https://127.0.0.1:{port}";
        var authorityClock = new QualifiedAuthorityClock(clock, TimeSpan.Zero, TimeSpan.Zero);
        using var componentKey = RSA.Create(2048);
        var componentJwt = new ComponentJwt(componentKey,
            "m1-11-fix-01-browser", "m1-11-fix-01-browser", authorityClock);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, port,
            listen => listen.UseHttps(certificate)));
        builder.Services.AddSingleton(clock);
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton(authorityClock);
        builder.Services.AddSingleton<IAuthorityClock>(authorityClock);
        builder.Services.AddSingleton(componentJwt);
        builder.Services.AddDbContextFactory<LmsDbContext>(db => db.UseNpgsql(connectionString));
        builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<LmsDbContext>>().CreateDbContext());
        builder.Services.AddSingleton(options);
        builder.Services.AddScoped<IOrganizationAdministrationQueries, PostgresOrganizationAdministrationQueries>();
        builder.Services.AddScoped<PostgresAccountAdministration>();
        builder.Services.AddScoped<IAccountAdministration>(sp => sp.GetRequiredService<PostgresAccountAdministration>());
        builder.Services.AddScoped<IAccountSecurityOperations>(sp => sp.GetRequiredService<PostgresAccountAdministration>());
        builder.Services.AddScoped<ICourseRosterAdministration, PostgresCourseRosterAdministration>();
        builder.Services.AddSingleton(new ContentImportRuntimeOptions
        {
            RepositoryRoot = root.FullName,
            PythonPath = "/usr/bin/python3",
            NodePath = "/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node",
            KatexPath = Path.Combine(root.FullName, "src/Lms.Web/node_modules/katex"),
            StorageRoot = Path.Combine(database.StorageRoot, "m1-11-fix-01-import"),
            MaxZipBytes = 20_000_000
        });
        builder.Services.AddScoped<IContentPublication, PostgresContentPublication>();
        builder.Services.AddScoped<IWorkStore, PostgresWorkStore>();
        builder.Services.AddScoped<IAccessPolicy, PostgresAccessPolicy>();
        builder.Services.AddScoped<IImmutableContentResolver, PostgresContentResolver>();
        builder.Services.AddScoped<IWorkspaceQueries, PostgresWorkspaceQueries>();
        builder.Services.AddScoped<IProgressHistoryQueries, PostgresProgressHistoryQueries>();
        builder.Services.AddScoped<ProblemWorkService>();
        builder.Services.AddSingleton<IActorMapper, ClaimsActorMapper>();
        builder.Services.AddScoped<PostgresTeacherCommands>();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddTeacherRevocationBrowserProtection();
        builder.Services.AddAuthentication(CourseBearerHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, CourseBearerHandler>(
                CourseBearerHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(QueriesController).Assembly)
            .AddJsonOptions(json =>
            {
                json.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                json.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            });

        await using var app = builder.Build();
        app.UseMiddleware<ApiExceptionMiddleware>();
        app.UseMiddleware<CourseRequestIngress>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapTeacherRevocation(origin);
        app.MapAccountAdministration(origin);
        app.MapCourseRoster(origin);
        app.MapContentPublication(origin, 20_000_000);
        app.MapProgressHistory();
        app.MapControllers();
        app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(web) });
        app.MapFallback(async context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = 404;
                return;
            }
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(Path.Combine(web, "index.html"));
        });
        await app.StartAsync();

        var evidence = Environment.GetEnvironmentVariable("LMS_M1_11_FIX_01_EVIDENCE")
            ?? Path.Combine(root.FullName, "codex/evidence/M1-11-FIX-01");
        Directory.CreateDirectory(evidence);
        var node = Environment.GetEnvironmentVariable("LMS_BROWSER_NODE") ?? "node";
        var start = new ProcessStartInfo(node)
        {
            WorkingDirectory = root.FullName,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        var executable = Environment.GetEnvironmentVariable("LMS_BROWSER_EXECUTABLE");
        if (string.IsNullOrWhiteSpace(executable) && File.Exists("/opt/google/chrome/chrome"))
            executable = "/opt/google/chrome/chrome";
        if (!string.IsNullOrWhiteSpace(executable))
            start.Environment["LMS_BROWSER_EXECUTABLE"] = executable;
        start.ArgumentList.Add(Path.Combine(root.FullName, "tools/browser_read/m1-11-fix-01.mjs"));
        using var runner = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var errors = runner.StandardError.ReadToEndAsync(timeout.Token);
        await runner.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
        {
            origin,
            organizationId = organization.OrganizationId,
            adminUser,
            adminPassword,
            teacherUser,
            teacherDisplayName = teacherDisplay,
            studentUser,
            participantPassword,
            courseTitle,
            courseId = course.CourseId,
            groupId = first.GroupId,
            evidence,
            validZip = Path.Combine(root.FullName, "tests/fixtures/content/m1-06-olympiad.zip")
        }));
        var output = await runner.StandardOutput.ReadLineAsync(timeout.Token) ?? string.Empty;
        await runner.WaitForExitAsync(timeout.Token);
        var diagnostic = (await errors)
            .Replace(adminPassword, "[REDACTED]")
            .Replace(participantPassword, "[REDACTED]");
        Assert.True(runner.ExitCode == 0, diagnostic);
        using var result = JsonDocument.Parse(output);
        Assert.Equal("PASS", result.RootElement.GetProperty("status").GetString());

        await using var verify = database.Open();
        Assert.Single(await verify.Set<CourseEditorScope>().Where(x =>
            x.OrganizationId == organization.OrganizationId && x.CourseId == course.CourseId &&
            x.EditorId == teacher.ActorId && x.State == "Active").ToListAsync());
        var teacherGrants = await verify.Set<TeacherAssignmentScope>().Where(x =>
            x.OrganizationId == organization.OrganizationId && x.CourseId == course.CourseId &&
            x.TeacherId == teacher.ActorId && x.State == "Active").ToListAsync();
        var teacherGrant = Assert.Single(teacherGrants);
        Assert.Equal(first.GroupId, teacherGrant.GroupId);
        Assert.DoesNotContain(teacherGrants, grant => grant.GroupId == second.GroupId);
        var version = await verify.ContentVersions.SingleAsync(x => x.CourseId == course.CourseId);
        Assert.Equal(1, version.VersionNumber);
        var assignments = await verify.Assignments.Where(x => x.CourseId == course.CourseId).ToListAsync();
        Assert.Equal(5, assignments.Count);
        Assert.All(assignments, assignment => Assert.Equal(student.ActorId, assignment.StudentId));
        Assert.Equal("Published", (await verify.Set<CourseCatalogRecord>()
            .SingleAsync(x => x.CourseId == course.CourseId)).State);

        await File.WriteAllTextAsync(Path.Combine(evidence, "test-summary.json"), JsonSerializer.Serialize(new
        {
            passed = true,
            transport = "HTTPS",
            browser = "Chromium",
            productionReact = true,
            uiApiPostgresConsistent = true,
            courseEditorAssignedByAdminUi = true,
            editorCourseId = course.CourseId,
            teacherGroups = new[] { first.GroupId },
            g2AccessExpanded = false,
            publishedVersion = 1,
            assignedProblems = 5,
            student = "P01",
            screenshots = 3
        }, new JsonSerializerOptions { WriteIndented = true }));
        await app.StopAsync();
    }
}
