using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Lms.Application;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Lms.Api.Tests;

public sealed class ContentPublicationBrowserTests(ApiPostgresFixture fixture) : IClassFixture<ApiPostgresFixture>
{
    private sealed class BrowserClock : TimeProvider, IQualifiedClockSource
    {
        private readonly DateTimeOffset started = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => started + Stopwatch.GetElapsedTime(timestamp);
        private readonly long timestamp = Stopwatch.GetTimestamp();
        public QualifiedClockReading Read()
        {
            var elapsed = Stopwatch.GetElapsedTime(timestamp); var now = started + elapsed;
            return new("m1-06-browser", true, new(now, now), elapsed, elapsed);
        }
    }

    [BrowserReadTheory]
    [InlineData(true)]
    public async Task Real_chromium_https_imports_previews_publishes_assigns_and_preserves_v1(bool browser)
    {
        Assert.True(browser);
        await using var database = await fixture.CreateApp(organizationBootstrap: true);
        await database.InitializeTeacherSecurity();
        await using var databaseContext = database.Open();
        var connectionString = databaseContext.Database.GetConnectionString()!;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Lms.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var web = Path.Combine(root.FullName, "src/Lms.Web/dist");
        Assert.True(File.Exists(Path.Combine(web, "index.html")), "Build the production React UI first");

        var courseId = "m1-05-browser-draft-" + Guid.NewGuid().ToString("N");
        const string groupId = "G1";
        const string courseTitle = "Олимпиадная математика · M1-06";
        var now = DateTimeOffset.UtcNow;
        await using (var seed = database.Open())
        {
            seed.AddRange(
                new CourseRegistration { CourseId = courseId, OrganizationId = database.OrganizationId, MappingState = "Verified" },
                new CourseGroup { CourseId = courseId, GroupId = groupId },
                new CourseCatalogRecord { CourseId = courseId, OrganizationId = database.OrganizationId,
                    Title = courseTitle, Description = "Draft-only курс, созданный цепочкой M1-05",
                    State = "Draft", Version = 1, CreatedAt = now },
                new CourseGroupCatalogRecord { CourseId = courseId, GroupId = groupId, Name = groupId,
                    Description = "Синтетическая группа", Version = 1, CreatedAt = now });
            await seed.SaveChangesAsync();
            seed.Add(new CourseContextBinding { CourseId = courseId, OrganizationId = database.OrganizationId,
                ContextId = database.ProcessingContextId, ContextVersion = 1, EffectiveFrom = now.AddMinutes(-1) });
            await seed.SaveChangesAsync();
        }
        await using (var grants = database.Open())
            await new PostgresScopedAuthorityAdministration(grants).Change(new(AuthorityKind.Teacher,
                AuthorityOperation.Grant, database.OrganizationId, ApiFactory.Teacher, 0, 1, courseId, null, Guid.NewGuid()));
        await using (var grants = database.Open())
            await new PostgresScopedAuthorityAdministration(grants).Change(new(AuthorityKind.Enrollment,
                AuthorityOperation.Grant, database.OrganizationId, ApiFactory.Student, 0, 1, courseId, groupId));

        using var tls = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=localhost", tls, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(IPAddress.Loopback);
        certificateRequest.CertificateExtensions.Add(san.Build());
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var origin = $"https://127.0.0.1:{port}";
        var time = new BrowserClock(); var authorityClock = new QualifiedAuthorityClock(time, TimeSpan.Zero, TimeSpan.Zero);
        using var componentKey = RSA.Create(2048);
        var componentJwt = new ComponentJwt(componentKey, "m1-06-browser", "m1-06-browser", authorityClock);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, port, listen => listen.UseHttps(certificate)));
        builder.Services.AddSingleton(time); builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddSingleton(authorityClock); builder.Services.AddSingleton<IAuthorityClock>(authorityClock);
        builder.Services.AddSingleton(componentJwt);
        builder.Services.AddDbContextFactory<LmsDbContext>(options => options.UseNpgsql(connectionString));
        builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<LmsDbContext>>().CreateDbContext());
        builder.Services.AddScoped<ICourseRosterAdministration, PostgresCourseRosterAdministration>();
        builder.Services.AddSingleton(new ContentImportRuntimeOptions
        {
            RepositoryRoot = root.FullName,
            PythonPath = "/usr/bin/python3",
            NodePath = "/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node",
            KatexPath = Path.Combine(root.FullName, "src/Lms.Web/node_modules/katex"),
            StorageRoot = Path.Combine(database.StorageRoot, "m1-06-browser-import"),
            MaxZipBytes = 20_000_000
        });
        builder.Services.AddScoped<IContentPublication, PostgresContentPublication>();
        builder.Services.AddScoped<PostgresTeacherCommands>();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddTeacherRevocationBrowserProtection();
        builder.Services.AddAuthentication(CourseBearerHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, CourseBearerHandler>(CourseBearerHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();

        await using var app = builder.Build();
        app.UseMiddleware<ApiExceptionMiddleware>(); app.UseMiddleware<CourseRequestIngress>();
        app.UseAuthentication(); app.UseAuthorization();
        app.MapTeacherRevocation(origin); app.MapCourseRoster(origin); app.MapContentPublication(origin, 20_000_000);
        app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(web) });
        app.MapFallback(async context =>
        {
            if (context.Request.Path.StartsWithSegments("/api")) { context.Response.StatusCode = 404; return; }
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.Body.WriteAsync(await File.ReadAllBytesAsync(Path.Combine(web, "index.html"), context.RequestAborted),
                context.RequestAborted);
        });
        await app.StartAsync();

        using (var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true })
        using (var probe = new HttpClient(handler) { BaseAddress = new Uri(origin) })
        {
            var index = await probe.GetAsync("/");
            var indexHtml = await index.Content.ReadAsStringAsync();
            Assert.True(index.IsSuccessStatusCode && indexHtml.Contains("id=\"root\"", StringComparison.Ordinal),
                $"HTTPS index probe failed: {(int)index.StatusCode} {indexHtml}");
            var asset = indexHtml.Split("src=\"", StringSplitOptions.RemoveEmptyEntries)[1].Split('"')[0];
            var script = await probe.GetAsync(asset);
            Assert.True(script.IsSuccessStatusCode, $"HTTPS asset probe failed: {(int)script.StatusCode} {asset}");
        }

        var evidence = Path.Combine(root.FullName, "codex/evidence/M1-06-CONTENT-PUBLICATION");
        Directory.CreateDirectory(evidence);
        var node = Environment.GetEnvironmentVariable("LMS_BROWSER_NODE") ?? "node";
        var start = new ProcessStartInfo(node) { WorkingDirectory = root.FullName, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        var executable = Environment.GetEnvironmentVariable("LMS_BROWSER_EXECUTABLE");
        if (string.IsNullOrWhiteSpace(executable) && File.Exists("/usr/bin/google-chrome")) executable = "/usr/bin/google-chrome";
        if (!string.IsNullOrWhiteSpace(executable)) start.Environment["LMS_BROWSER_EXECUTABLE"] = executable;
        start.ArgumentList.Add(Path.Combine(root.FullName, "tools/browser_read/content-publication.mjs"));
        using var runner = Process.Start(start)!; using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var errors = runner.StandardError.ReadToEndAsync(timeout.Token);
        await runner.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
        {
            origin, organizationId = database.OrganizationId, teacherUser = database.TeacherUserName,
            studentUser = database.StudentUserName, password = ApiFactory.SecurePassword, courseTitle, evidence,
            invalidZip = Path.Combine(root.FullName, "tests/fixtures/content/m1-06-invalid.zip"),
            validZip = Path.Combine(root.FullName, "tests/fixtures/content/m1-06-olympiad.zip"),
            versionTwoZip = Path.Combine(root.FullName, "tests/fixtures/content/m1-06-olympiad-v2.zip")
        }));
        var passed = await runner.StandardOutput.ReadLineAsync(timeout.Token) == "PASS";
        await runner.WaitForExitAsync(timeout.Token);
        var diagnostic = (await errors).Replace(ApiFactory.SecurePassword, "[REDACTED]");
        Assert.True(passed && runner.ExitCode == 0, diagnostic);

        await using var verify = database.Open();
        var versions = await verify.ContentVersions.Where(x => x.CourseId == courseId).OrderBy(x => x.VersionNumber).ToListAsync();
        Assert.Equal([1, 2], versions.Select(x => x.VersionNumber).ToArray());
        var historical = await verify.Assignments.Where(x => x.CourseId == courseId).ToListAsync();
        Assert.Equal(5, historical.Count); Assert.All(historical, x => Assert.Equal(versions[0].Id, x.Content.CourseVersionId));
        var v1Artifact = await verify.ContentArtifacts.SingleAsync(x => x.ContentVersionId == versions[0].Id);
        Assert.Contains("M1_TEACHER_ONLY_SOLUTION_v1", v1Artifact.TeacherArtifactJson);
        Assert.DoesNotContain("M1_TEACHER_ONLY", v1Artifact.StudentArtifactJson);
        Assert.Equal("Published", (await verify.Set<CourseCatalogRecord>().SingleAsync(x => x.CourseId == courseId)).State);
        Assert.Equal(7, Directory.GetFiles(evidence, "*.png").Length);
        await File.WriteAllTextAsync(Path.Combine(evidence, "test-summary.json"), JsonSerializer.Serialize(new
        {
            passed = true, transport = "HTTPS", browser = "Chromium", imports = 3, versions = 2,
            historicalAssignments = 5, historicalVersion = 1, roleIsolation = true, unknownResultAutoReplay = false,
            screenshots = 7
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
