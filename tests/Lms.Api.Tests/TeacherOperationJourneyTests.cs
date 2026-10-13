using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Lms.Application;
using Lms.Domain;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lms.Api.Tests;

// Testcontainers network creation can make Chromium abort a loopback request with
// ERR_NETWORK_CHANGED. This collection runs without sibling Docker fixture mutations.
[CollectionDefinition("Teacher browser network isolation", DisableParallelization = true)]
public sealed class TeacherBrowserNetworkIsolation { }

// Loopback-only protocol harness: real Chromium -> HTTPS -> existing application/
// teacher executor -> disposable PostgreSQL. This is not production UI acceptance.
[Collection("Teacher browser network isolation")]
public sealed class TeacherOperationJourneyTests(ApiPostgresFixture fixture) : IClassFixture<ApiPostgresFixture>
{
    private static readonly DateTimeOffset At = new(2026, 10, 12, 6, 0, 0, TimeSpan.Zero);
    private sealed class Clock : TimeProvider, IQualifiedClockSource
    {
        public override DateTimeOffset GetUtcNow() => At;
        public QualifiedClockReading Read() => new("journey", true, new(At, At), TimeSpan.Zero, TimeSpan.Zero);
    }
    private sealed class Ingress(QualifiedAuthorityClock clock) : TrustedApplicationIngressAdapter(clock)
    { public ApplicationDeadline Arrive() => Capture(ApplicationRequestKind.Ordinary); }
    public sealed record StudentBody(string Body, long? Version);
    [BrowserReadTheory]
    [InlineData(true)]
    public async Task Browser_Submit_Review_Revise_Accept_with_mandatory_teacher_context(bool browser)
    {
        Assert.True(browser);
        await using var database = await fixture.CreateApp(); await using var db = database.Open();
        var connection = db.Database.GetConnectionString()!;
        var org = Guid.NewGuid(); var teacher = Guid.NewGuid(); var student = Guid.NewGuid(); var context = Guid.NewGuid();
        db.Add(new Organization { OrganizationId = org, State = "Enabled", SyntheticOnly = true });
        db.AddRange(new ActorRecord { ActorId = teacher }, new ActorRecord { ActorId = student });
        db.Add(new CourseRegistration { CourseId = "context-journey", OrganizationId = org, MappingState = "Verified" });
        db.Add(new CourseGroup { CourseId = "context-journey", GroupId = "g" }); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var admin = new PostgresScopedAuthorityAdministration(db);
        await admin.Change(new(AuthorityKind.Membership, AuthorityOperation.Grant, org, teacher, 0, Role: ScopeRole.Teacher));
        await admin.Change(new(AuthorityKind.Membership, AuthorityOperation.Grant, org, student, 0, Role: ScopeRole.Student));
        await admin.Change(new(AuthorityKind.Teacher, AuthorityOperation.Grant, org, teacher, 0, 1, "context-journey", "g", Guid.NewGuid()));
        await admin.Change(new(AuthorityKind.Enrollment, AuthorityOperation.Grant, org, student, 0, 1, "context-journey", "g"));
        await new PostgresProcessingStateAdministration(db).Change(new(org, Guid.NewGuid(), 0, new AddProcessingContext(context, 1, At.AddDays(-1))));
        await new PostgresProcessingStateAdministration(db).Change(new(org, Guid.NewGuid(), 1, new OpenProcessingBinding("context-journey", context, 1, At.AddHours(-1))));
        var assignment = new ProblemAssignment(Guid.NewGuid(), student, "context-journey", new(Guid.NewGuid(), "lesson", "problem"));
        db.Add(assignment); db.Add(new LessonAccess { CourseId = assignment.CourseId, CourseVersionId = assignment.Content.CourseVersionId, LessonId = "lesson", IsPublished = true });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var source = new Clock(); var clock = new QualifiedAuthorityClock(source, TimeSpan.Zero, TimeSpan.Zero);
        using var signing = RSA.Create(2048); var jwt = new ComponentJwt(signing, "journey", "journey", clock);
        async Task<string> Token(Guid actor)
        {
            await using var tokenDb = database.Open();
            var userId = Guid.NewGuid(); var name = "journey-" + actor.ToString("N");
            await new PostgresIdentityMapping(tokenDb, source).CreateInactive(userId, actor, org, name);
            (await tokenDb.Set<ActorIdentity>().SingleAsync(x => x.UserId == userId)).IsActive = true;
            var user = await tokenDb.Users.SingleAsync(x => x.Id == userId);
            user.PasswordHash = new PasswordHasher<IdentityUser<Guid>>().HashPassword(user, "Synthetic-password-123");
            await tokenDb.SaveChangesAsync(); tokenDb.ChangeTracker.Clear();
            return (await new PostgresComponentTokenIssuer(tokenDb, clock, jwt).Login(name, "Synthetic-password-123", org,
                new Ingress(clock).Arrive())).AccessToken!.Reveal();
        }
        var studentToken = await Token(student); var teacherToken = await Token(teacher);
        using var tls = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", tls, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(IPAddress.Loopback); request.CertificateExtensions.Add(san.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var origin = $"https://127.0.0.1:{port}";
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, port, l => l.UseHttps(certificate)));
        builder.Services.AddSingleton(clock); builder.Services.AddSingleton<IAuthorityClock>(clock); builder.Services.AddSingleton<TimeProvider>(source); builder.Services.AddSingleton(jwt);
        builder.Services.AddDbContextFactory<LmsDbContext>(o => o.UseNpgsql(connection));
        builder.Services.AddSingleton(new AttachmentOptions()); builder.Services.AddSingleton<IAttachmentStorage>(new LocalFileAttachmentStorage(new() { Root = database.StorageRoot }));
        builder.Services.AddTransient<PostgresTeacherCommands>(); builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider(); builder.Services.AddTeacherRevocationBrowserProtection();
        builder.Services.AddAuthentication(CourseBearerHandler.SchemeName).AddScheme<AuthenticationSchemeOptions, CourseBearerHandler>(CourseBearerHandler.SchemeName, _ => { }); builder.Services.AddAuthorization();
        await using var app = builder.Build(); app.UseMiddleware<CourseRequestIngress>(); app.UseAuthentication(); app.UseAuthorization(); app.MapTeacherRevocation(origin);
        app.MapGet("/", () => Microsoft.AspNetCore.Http.Results.Content(Page, "text/html"));
        app.MapPost("/test/student/submit", async (Microsoft.AspNetCore.Http.HttpContext http, StudentBody body, IDbContextFactory<LmsDbContext> contexts) => {
            var token = jwt.Validate(new AccessToken(http.Request.Headers.Authorization.ToString()["Bearer ".Length..]));
            if (token?.Authority.Membership.Role != ScopeRole.Student || token.Identity.ActorId != student)
                return Microsoft.AspNetCore.Http.Results.Forbid();
            await using var studentDb = await contexts.CreateDbContextAsync(http.RequestAborted);
            var service = new ProblemWorkService(new PostgresWorkStore(studentDb), new PostgresAccessPolicy(studentDb, source), source);
            var result = await service.SubmitSolution(new(student, AuthorRole.Student), assignment.Id, body.Body, [], body.Version, http.RequestAborted);
            return Microsoft.AspNetCore.Http.Results.Json(result, ApiFactory.Json);
        }).RequireAuthorization();
        await app.StartAsync();
        var root = new DirectoryInfo(AppContext.BaseDirectory); while (root != null && !File.Exists(Path.Combine(root.FullName, "Lms.sln"))) root = root.Parent; Assert.NotNull(root);
        var evidence = Path.Combine(Environment.GetEnvironmentVariable("LMS_BROWSER_EVIDENCE_ROOT") ?? Path.GetTempPath(), "mandatory-teacher-journey"); Directory.CreateDirectory(evidence);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("LMS_BROWSER_NODE") ?? "node") { WorkingDirectory = root.FullName,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(Path.Combine(root.FullName, "tools/browser_read/teacher-journey.mjs"));
        using var runner = Process.Start(start)!; using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var output = runner.StandardOutput.ReadToEndAsync(timeout.Token); var errors = runner.StandardError.ReadToEndAsync(timeout.Token);
        try {
            await runner.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { origin, studentToken, teacherToken, evidence })); runner.StandardInput.Close();
            await runner.WaitForExitAsync(timeout.Token);
            var diagnostic = (await errors).Replace(studentToken, "[REDACTED]").Replace(teacherToken, "[REDACTED]");
            Assert.True(runner.ExitCode == 0, diagnostic); Assert.Contains("PASS", await output);
        } finally { if (!runner.HasExited) runner.Kill(true); }
        await using var verify = database.Open();
        Assert.Equal(ProblemWorkStatus.Accepted, (await verify.Works.SingleAsync()).Status);
        Assert.Equal(2, await verify.Set<Submission>().CountAsync()); Assert.Equal(2, await verify.Set<Review>().CountAsync());
        Assert.Equal(2, await verify.Set<Message>().CountAsync()); Assert.Equal(6, await verify.Events.CountAsync());
        Assert.Equal(2, await verify.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM public.\"TeacherOperationAttempts\"").SingleAsync());
    }
    private const string Page = """
        <!doctype html><html lang="en"><meta charset="utf-8"><title>Synthetic teacher protocol journey</title>
        <h1>Synthetic teacher protocol journey</h1><p>Isolated HTTPS harness; no production activation.</p>
        <button id="submit">Submit</button><button id="review">Review</button><button id="revise">Revise</button><button id="accept">Accept</button><button id="retry">Retry accept</button>
        <pre id="state">Ready</pre><script>
        let work, submission, version, last;
        async function call(path, token, body, teacher=false) {
          let csrf;
          if(teacher) csrf=(await (await fetch('/api/v1/auth/csrf',{headers:{Authorization:'Bearer '+token}})).json()).token;
          const response=await fetch(path,{method:'POST',headers:{'Content-Type':'application/json',Authorization:'Bearer '+token,...(csrf?{'X-CSRF-TOKEN':csrf}:{})},body:JSON.stringify(body)});
          if(!response.ok) {document.querySelector('#state').textContent='HTTP '+response.status;return;}
          const r=await response.json();work=r.problemWorkId||work;submission=r.submissionId||submission;version=r.version;
          document.querySelector('#state').textContent=r.status;
        }
        document.querySelector('#submit').onclick=()=>call('/test/student/submit',window.session.studentToken,{body:'First answer',version:null});
        document.querySelector('#revise').onclick=()=>call('/test/student/submit',window.session.studentToken,{body:'Revised answer',version});
        async function review(revision){last={path:'/api/v1/problem-works/'+work+'/reviews/'+(revision?'needs-revision':'accept'),body:{commandId:crypto.randomUUID(),submissionId:submission,expectedVersion:version,feedback:revision?'Explain step two.':'Accepted correction.',annotations:[]}};await call(last.path,window.session.teacherToken,last.body,true);}
        document.querySelector('#review').onclick=()=>review(true);document.querySelector('#accept').onclick=()=>review(false);
        document.querySelector('#retry').onclick=()=>call(last.path,window.session.teacherToken,{...last.body,commandId:crypto.randomUUID()},true);
        </script></html>
        """;
}
