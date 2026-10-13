using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Lms.Application;
using Lms.Domain;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

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
            return new("m1-11-fix-02-browser", true, new(now, now), elapsed, elapsed);
        }
    }

    [BrowserReadTheory]
    [InlineData(true)]
    public async Task Real_chromium_bootstraps_context_then_editor_publishes_only_for_G1(bool browser)
    {
        Assert.True(browser);
        await using var database = await fixture.CreateApp(testAuthentication: false, organizationBootstrap: true);
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
            KeyVersion = 22
        };
        const string adminPassword = "Synthetic-Fix02-Admin-2039!";
        const string participantPassword = "Synthetic-Fix02-Participant-2039!";
        const string teacherDisplay = "T-A";
        const string courseTitle = "Подготовка к олимпиаде Эйлера";
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var adminUser = "fix02-admin-" + suffix;
        var teacherUser = "fix02-teacher-" + suffix;
        var studentUser = "fix02-student-" + suffix;

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
            "m1-11-fix-02-browser", "m1-11-fix-02-browser", authorityClock);
        var platformKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.LocalSigningKey));

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
        builder.Services.AddScoped<IOrganizationBootstrap, PostgresOrganizationBootstrap>();
        builder.Services.AddScoped<IOrganizationAdministrationQueries, PostgresOrganizationAdministrationQueries>();
        builder.Services.AddScoped<PostgresAccountAdministration>();
        builder.Services.AddScoped<IAccountAdministration>(sp => sp.GetRequiredService<PostgresAccountAdministration>());
        builder.Services.AddScoped<IAccountSecurityOperations>(sp => sp.GetRequiredService<PostgresAccountAdministration>());
        builder.Services.AddScoped<ICourseRosterAdministration, PostgresCourseRosterAdministration>();
        builder.Services.AddScoped<IProcessingContextAdministration, PostgresProcessingContextAdministration>();
        builder.Services.AddSingleton(new ContentImportRuntimeOptions
        {
            RepositoryRoot = root.FullName,
            PythonPath = "/usr/bin/python3",
            NodePath = "/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node",
            KatexPath = Path.Combine(root.FullName, "src/Lms.Web/node_modules/katex"),
            StorageRoot = Path.Combine(database.StorageRoot, "m1-11-fix-02-import"),
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
        builder.Services.AddRateLimiter(rate => rate.AddPolicy(OrganizationBootstrapHttp.ActivationRatePolicy,
            _ => RateLimitPartition.GetFixedWindowLimiter("m1-11-fix-02-browser",
                _ => new FixedWindowRateLimiterOptions
                    { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
        builder.Services.AddAuthentication(CourseBearerHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, CourseBearerHandler>(
                CourseBearerHandler.SchemeName, _ => { })
            .AddJwtBearer(OrganizationBootstrapHttp.PlatformBearerScheme, jwt =>
            {
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new()
                {
                    ValidIssuer = "m1-11-fix-02-platform",
                    ValidAudience = OrganizationBootstrapHttp.PlatformAudience,
                    IssuerSigningKey = platformKey,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.Zero
                };
            });
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
        app.UseRateLimiter();
        app.MapGet("/api/health", async (LmsDbContext db, CancellationToken ct) =>
            await db.Database.CanConnectAsync(ct) ? Results.Ok() : Results.StatusCode(503));
        app.MapTeacherRevocation(origin);
        app.MapOrganizationBootstrap(origin);
        app.MapAccountAdministration(origin);
        app.MapCourseRoster(origin);
        app.MapProcessingContextAdministration(origin);
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

        var now = DateTime.UtcNow;
        var platformToken = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            "m1-11-fix-02-platform", OrganizationBootstrapHttp.PlatformAudience,
            [new Claim("sub", "synthetic-platform-operator"),
             new Claim("scope", OrganizationBootstrapHttp.BootstrapCapability)],
            now, now.AddMinutes(30), new SigningCredentials(platformKey, SecurityAlgorithms.HmacSha256)));
        var evidence = Environment.GetEnvironmentVariable("LMS_M1_11_FIX_02_EVIDENCE")
            ?? Path.Combine(root.FullName, "codex/evidence/M1-11-FIX-02");
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
        if (!string.IsNullOrWhiteSpace(executable)) start.Environment["LMS_BROWSER_EXECUTABLE"] = executable;
        start.ArgumentList.Add(Path.Combine(root.FullName, "tools/browser_read/m1-11-fix-02.mjs"));
        using var runner = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(7));
        var errors = runner.StandardError.ReadToEndAsync(timeout.Token);
        await runner.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
        {
            origin,
            platformToken,
            organizationName = "Синтетическая школа FIX-02",
            adminUser,
            adminPassword,
            teacherUser,
            teacherDisplayName = teacherDisplay,
            studentUser,
            studentDisplayName = "P01",
            participantPassword,
            courseTitle,
            evidence,
            validZip = Path.Combine(root.FullName, "tests/fixtures/content/m1-06-olympiad.zip")
        }));
        var output = await runner.StandardOutput.ReadLineAsync(timeout.Token) ?? string.Empty;
        await runner.WaitForExitAsync(timeout.Token);
        var diagnostic = (await errors)
            .Replace(platformToken, "[REDACTED]")
            .Replace(adminPassword, "[REDACTED]")
            .Replace(participantPassword, "[REDACTED]");
        Assert.True(runner.ExitCode == 0, diagnostic);
        using var result = JsonDocument.Parse(output);
        Assert.Equal("PASS", result.RootElement.GetProperty("status").GetString());
        var organizationId = result.RootElement.GetProperty("organizationId").GetGuid();
        var courseId = result.RootElement.GetProperty("courseId").GetString()!;
        var firstGroupId = result.RootElement.GetProperty("groupId").GetString()!;
        var secondGroupId = result.RootElement.GetProperty("g2Id").GetString()!;

        await using var verify = database.Open();
        var teacher = await verify.Set<OrganizationParticipantProfile>().SingleAsync(x =>
            x.OrganizationId == organizationId && x.ExternalRosterKey == teacherUser);
        var student = await verify.Set<OrganizationParticipantProfile>().SingleAsync(x =>
            x.OrganizationId == organizationId && x.ExternalRosterKey == studentUser);
        Assert.Single(await verify.Set<ProcessingContextVersion>().Where(x =>
            x.OrganizationId == organizationId && x.SyntheticOnly &&
            x.ApprovalState == "SyntheticOnly").ToListAsync());
        Assert.Single(await verify.Set<CourseContextBinding>().Where(x =>
            x.OrganizationId == organizationId && x.CourseId == courseId &&
            x.EffectiveTo == null).ToListAsync());
        Assert.Single(await verify.Set<CourseEditorScope>().Where(x =>
            x.OrganizationId == organizationId && x.CourseId == courseId &&
            x.EditorId == teacher.ActorId && x.State == "Active").ToListAsync());
        var teacherGrants = await verify.Set<TeacherAssignmentScope>().Where(x =>
            x.OrganizationId == organizationId && x.CourseId == courseId &&
            x.TeacherId == teacher.ActorId && x.State == "Active").ToListAsync();
        var teacherGrant = Assert.Single(teacherGrants);
        Assert.Equal(firstGroupId, teacherGrant.GroupId);
        Assert.DoesNotContain(teacherGrants, grant => grant.GroupId == secondGroupId);
        var version = await verify.ContentVersions.SingleAsync(x => x.CourseId == courseId);
        Assert.Equal(1, version.VersionNumber);
        var assignments = await verify.Assignments.Where(x => x.CourseId == courseId).ToListAsync();
        Assert.Equal(5, assignments.Count);
        Assert.All(assignments, assignment => Assert.Equal(student.ActorId, assignment.StudentId));
        Assert.Equal("Published", (await verify.Set<CourseCatalogRecord>()
            .SingleAsync(x => x.CourseId == courseId)).State);

        await File.WriteAllTextAsync(Path.Combine(evidence, "test-summary.json"), JsonSerializer.Serialize(new
        {
            passed = true,
            transport = "HTTPS",
            browser = "Chromium",
            productionReact = true,
            uiApiPostgresConsistent = true,
            cleanBootstrapViaUi = true,
            processingContextCreatedViaAdminUi = true,
            courseBoundViaAdminUi = true,
            courseEditorAssignedByAdminUi = true,
            courseId,
            contextVersions = 1,
            activeBindings = 1,
            teacherGroups = new[] { firstGroupId },
            excludedGroup = secondGroupId,
            g2AccessExpanded = false,
            publishedVersion = 1,
            assignedProblems = 5,
            student = "P01",
            screenshots = 4
        }, new JsonSerializerOptions { WriteIndented = true }));
        await app.StopAsync();
    }
}
