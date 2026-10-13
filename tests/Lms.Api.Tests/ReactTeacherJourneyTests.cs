using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lms.Application;
using Lms.Domain;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Lms.Api.Tests;

// Production React bundle -> real Chromium -> HTTPS -> production controllers and
// protected teacher routes -> disposable PostgreSQL. Authentication tokens are issued
// out of band through the documented host adapter, so this is not browser-login acceptance.
[Collection("Teacher browser network isolation")]
public sealed class ReactTeacherJourneyTests(ApiPostgresFixture fixture) : IClassFixture<ApiPostgresFixture>
{
    private static readonly DateTimeOffset At = DateTimeOffset.UtcNow.AddMinutes(-1);
    private sealed class Clock : TimeProvider, IQualifiedClockSource
    {
        private readonly long started = Stopwatch.GetTimestamp();
        private readonly DateTimeOffset origin = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => origin + Stopwatch.GetElapsedTime(started);
        public QualifiedClockReading Read()
        {
            var elapsed = Stopwatch.GetElapsedTime(started); var now = origin + elapsed;
            return new("react-journey", true, new(now, now), elapsed, elapsed);
        }
    }
    private sealed class Ingress(QualifiedAuthorityClock clock) : TrustedApplicationIngressAdapter(clock)
    { public ApplicationDeadline Arrive() => Capture(ApplicationRequestKind.Ordinary); }

    [BrowserReadTheory]
    [InlineData(true)]
    public async Task React_Submit_Review_Revise_Accept_photos_annotations_revoke_and_cross_org(bool browser)
    {
        Assert.True(browser);
        await using var database = await fixture.CreateApp();
        await using var db = database.Open();
        var connection = db.Database.GetConnectionString()!;
        var org = Guid.NewGuid(); var teacher = Guid.NewGuid(); var student = Guid.NewGuid(); var context = Guid.NewGuid();
        const string course = "react-journey"; const string group = "g";
        db.Add(new Organization { OrganizationId = org, State = "Enabled", SyntheticOnly = true });
        db.AddRange(new ActorRecord { ActorId = teacher }, new ActorRecord { ActorId = student });
        db.Add(new CourseRegistration { CourseId = course, OrganizationId = org, MappingState = "Verified" });
        db.Add(new CourseGroup { CourseId = course, GroupId = group });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var teacherGrant = new AuthorityCommand(AuthorityKind.Teacher, AuthorityOperation.Grant, org, teacher, 0, 1,
            course, group, Guid.NewGuid());
        var admin = new PostgresScopedAuthorityAdministration(db);
        await admin.Change(new(AuthorityKind.Membership, AuthorityOperation.Grant, org, teacher, 0, Role: ScopeRole.Teacher));
        await admin.Change(new(AuthorityKind.Membership, AuthorityOperation.Grant, org, student, 0, Role: ScopeRole.Student));
        await admin.Change(teacherGrant);
        await admin.Change(new(AuthorityKind.Enrollment, AuthorityOperation.Grant, org, student, 0, 1, course, group));
        await new PostgresProcessingStateAdministration(db).Change(new(org, Guid.NewGuid(), 0,
            new AddProcessingContext(context, 1, At.AddDays(-1))));
        await new PostgresProcessingStateAdministration(db).Change(new(org, Guid.NewGuid(), 1,
            new OpenProcessingBinding(course, context, 1, At.AddHours(-1))));

        var assignment = new ProblemAssignment(Guid.NewGuid(), student, course,
            new(Guid.NewGuid(), "lesson", "problem"));
        var seededWork = new ProblemWork(Guid.NewGuid(), assignment);
        db.Add(assignment); db.Add(seededWork);
        db.Add(new LessonAccess { CourseId = course, CourseVersionId = assignment.Content.CourseVersionId,
            LessonId = "lesson", IsPublished = true });
        db.Add(new PublishedContentVersion { Id = assignment.Content.CourseVersionId, CourseId = course,
            VersionNumber = 1, SourceRevision = "react-teacher-journey", PublishedAt = At,
            ContentJson = JsonSerializer.Serialize(new PublishedCourseContent("Journey course", "Real React E2E", [
                new("lesson", 1, "Journey lesson", null, [new("problem", 1, "Journey problem", "Solve the journey problem.")])
            ]), new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Set<AssignmentScope>().CountAsync(x => x.AssignmentId == assignment.Id));

        var outsiderOrg = Guid.NewGuid(); var outsider = Guid.NewGuid(); var outsiderContext = Guid.NewGuid();
        db.Add(new Organization { OrganizationId = outsiderOrg, State = "Enabled", SyntheticOnly = true });
        db.Add(new ActorRecord { ActorId = outsider });
        db.Add(new CourseRegistration { CourseId = "outsider-course", OrganizationId = outsiderOrg, MappingState = "Verified" });
        db.Add(new CourseGroup { CourseId = "outsider-course", GroupId = group });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var outsiderAdmin = new PostgresScopedAuthorityAdministration(db);
        await outsiderAdmin.Change(new(AuthorityKind.Membership, AuthorityOperation.Grant, outsiderOrg, outsider, 0, Role: ScopeRole.Teacher));
        await outsiderAdmin.Change(new(AuthorityKind.Teacher, AuthorityOperation.Grant, outsiderOrg, outsider, 0, 1,
            "outsider-course", group, Guid.NewGuid()));
        await new PostgresProcessingStateAdministration(db).Change(new(outsiderOrg, Guid.NewGuid(), 0,
            new AddProcessingContext(outsiderContext, 1, At.AddDays(-1))));
        await new PostgresProcessingStateAdministration(db).Change(new(outsiderOrg, Guid.NewGuid(), 1,
            new OpenProcessingBinding("outsider-course", outsiderContext, 1, At.AddHours(-1))));

        var source = new Clock(); var clock = new QualifiedAuthorityClock(source, TimeSpan.Zero, TimeSpan.Zero);
        using var signing = RSA.Create(2048); var jwt = new ComponentJwt(signing, "journey", "journey", clock);
        async Task<string> Token(Guid actor, Guid organization)
        {
            await using var tokenDb = database.Open();
            var userId = Guid.NewGuid(); var name = "journey-" + actor.ToString("N");
            await new PostgresIdentityMapping(tokenDb, source).CreateInactive(userId, actor, organization, name);
            (await tokenDb.Set<ActorIdentity>().SingleAsync(x => x.UserId == userId)).IsActive = true;
            var user = await tokenDb.Users.SingleAsync(x => x.Id == userId);
            user.PasswordHash = new PasswordHasher<IdentityUser<Guid>>().HashPassword(user, "Synthetic-password-123");
            await tokenDb.SaveChangesAsync(); tokenDb.ChangeTracker.Clear();
            return (await new PostgresComponentTokenIssuer(tokenDb, clock, jwt).Login(name, "Synthetic-password-123",
                organization, new Ingress(clock).Arrive())).AccessToken!.Reveal();
        }
        var studentToken = await Token(student, org); var teacherToken = await Token(teacher, org);
        var outsiderToken = await Token(outsider, outsiderOrg);
        var issuedTeacher = jwt.Validate(new AccessToken(teacherToken));
        Assert.NotNull(issuedTeacher); Assert.Equal(teacher, issuedTeacher.Identity.ActorId);
        Assert.Equal(ScopeRole.Teacher, issuedTeacher.Authority.Membership.Role);
        Assert.Contains(issuedTeacher.Authority.Grants, grant => grant.Kind == AuthorityKind.Teacher &&
            grant.CourseId == course && grant.GroupId == group);
        await using (var policyDb = database.Open())
        {
            var persistedAssignment = await policyDb.Assignments.SingleAsync(x => x.Id == assignment.Id);
            await new PostgresAccessPolicy(policyDb, source).EnsureCanReview(
                new(teacher, AuthorRole.Teacher), persistedAssignment, default);
        }

        using var tls = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=localhost", tls, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(IPAddress.Loopback);
        certificateRequest.CertificateExtensions.Add(san.Build());
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var origin = $"https://127.0.0.1:{port}";
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Lms.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var web = Path.Combine(root.FullName, "src/Lms.Web/dist");
        Assert.True(File.Exists(Path.Combine(web, "index.html")), "Build the production React UI first");

        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, port, l => l.UseHttps(certificate)));
        builder.Services.AddSingleton(clock); builder.Services.AddSingleton<IAuthorityClock>(clock);
        builder.Services.AddSingleton<TimeProvider>(source); builder.Services.AddSingleton(jwt);
        builder.Services.AddDbContextFactory<LmsDbContext>(o => o.UseNpgsql(connection));
        builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<LmsDbContext>>().CreateDbContext());
        builder.Services.AddSingleton(new AttachmentOptions { MaxBytes = 1024 * 1024 });
        builder.Services.AddSingleton<IAttachmentStorage>(new LocalFileAttachmentStorage(new() { Root = database.StorageRoot }));
        builder.Services.AddScoped<AttachmentService>();
        builder.Services.AddScoped<IWorkStore, PostgresWorkStore>();
        builder.Services.AddScoped<IAccessPolicy, PostgresAccessPolicy>();
        builder.Services.AddScoped<ProblemWorkService>();
        builder.Services.AddScoped<IImmutableContentResolver, PostgresContentResolver>();
        builder.Services.AddScoped<IWorkspaceQueries, PostgresWorkspaceQueries>();
        builder.Services.AddSingleton<IActorMapper, ClaimsActorMapper>();
        builder.Services.AddScoped<PostgresTeacherCommands>();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddTeacherRevocationBrowserProtection();
        builder.Services.AddAuthentication(CourseBearerHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, CourseBearerHandler>(CourseBearerHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(QueriesController).Assembly).AddJsonOptions(options => {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        });
        await using var app = builder.Build();
        app.UseMiddleware<ApiExceptionMiddleware>();
        app.UseMiddleware<CourseRequestIngress>();
        app.UseAuthentication(); app.UseAuthorization();
        app.MapControllers(); app.MapTeacherRevocation(origin);
        app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(web) });
        app.MapFallback(async http => {
            if (http.Request.Path.StartsWithSegments("/api")) { http.Response.StatusCode = 404; return; }
            http.Response.ContentType = "text/html; charset=utf-8";
            await Microsoft.AspNetCore.Http.SendFileResponseExtensions.SendFileAsync(http.Response,
                Path.Combine(web, "index.html"));
        });
        await app.StartAsync();
        using (var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true })
        using (var probe = new HttpClient(handler))
        {
            probe.DefaultRequestHeaders.Authorization = new("Bearer", teacherToken);
            using var response = await probe.GetAsync($"{origin}/api/v1/teacher/problem-works/{seededWork.Id}");
            Assert.True(response.IsSuccessStatusCode,
                $"Teacher read preflight failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        var evidence = Path.Combine(Environment.GetEnvironmentVariable("LMS_BROWSER_EVIDENCE_ROOT") ??
            Path.Combine(root.FullName, "codex/evidence"), "TEACHER-REVOCATION-REACT-E2E");
        Directory.CreateDirectory(evidence);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("LMS_BROWSER_NODE") ?? "node") {
            WorkingDirectory = root.FullName, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(Path.Combine(root.FullName, "tools/browser_read/react-teacher-journey.mjs"));
        using var runner = Process.Start(start)!; using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var errors = runner.StandardError.ReadToEndAsync(timeout.Token);
        await runner.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { origin, studentToken, teacherToken,
            outsiderToken, assignmentId = assignment.Id, workId = seededWork.Id, evidence,
            pngBase64 = Convert.ToBase64String(AttachmentImages.Png) }));
        var passed = false;
        try
        {
            while (await runner.StandardOutput.ReadLineAsync(timeout.Token) is { } command)
            {
                if (command == "PASS") { passed = true; break; }
                if (command != "revoke") throw new InvalidOperationException("Unknown browser journey command");
                await using var revokeDb = database.Open();
                await new PostgresScopedAuthorityAdministration(revokeDb).Change(
                    teacherGrant with { Operation = AuthorityOperation.Revoke, ExpectedVersion = 1 });
                await runner.StandardInput.WriteLineAsync("{}");
            }
            await runner.WaitForExitAsync(timeout.Token);
            var diagnostic = (await errors).Replace(studentToken, "[REDACTED]")
                .Replace(teacherToken, "[REDACTED]").Replace(outsiderToken, "[REDACTED]");
            Assert.True(passed && runner.ExitCode == 0, diagnostic);
        }
        finally { if (!runner.HasExited) runner.Kill(true); }

        await using var verify = database.Open();
        var work = await verify.Works.Include(x => x.Submissions).Include(x => x.Reviews).Include(x => x.Messages)
            .SingleAsync();
        Assert.Equal(ProblemWorkStatus.Accepted, work.Status);
        Assert.Equal(2, work.Submissions.Count); Assert.Equal(2, work.Reviews.Count);
        Assert.Equal(3, work.Messages.Count);
        var annotatedReview = work.Reviews.Single(x => x.Decision == ReviewDecision.NeedsRevision);
        Assert.Single(annotatedReview.Annotations);
        Assert.Single(annotatedReview.Annotations[0].Strokes);
        Assert.Equal(3, await verify.AttachmentMetadata.CountAsync());
        Assert.Equal("Revoked", (await verify.Set<TeacherAssignmentScope>()
            .SingleAsync(x => x.TeacherId == teacher && x.CourseId == course)).State);
    }
}
