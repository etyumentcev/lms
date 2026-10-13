using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Lms.Application;
using Lms.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Tests;

// Explicit opt-in because the browser and separate test UI build are external test dependencies.
public sealed class BrowserReadTheoryAttribute : TheoryAttribute
{
    public BrowserReadTheoryAttribute()
    { if (Environment.GetEnvironmentVariable("LMS_BROWSER_READ") != "1") Skip = "Build test UI and set LMS_BROWSER_READ=1; see tools/browser_read/README.md"; }
}
public sealed partial class IsolatedProcessingDeploymentTests
{
    [BrowserReadTheory]
    [InlineData(ScopeRole.Student)] [InlineData(ScopeRole.Teacher)]
    public async Task Browser_read_real_http_postgres_navigation_and_denials(ScopeRole role)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Lms.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var web = Path.Combine(root.FullName, "src/Lms.Web/dist-browser-read");
        Assert.True(File.Exists(Path.Combine(web, "browser-read/index.html")), "Build the test-only UI first");
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "browser-synthetic", "browser-synthetic", clock);
        var seed = await HttpSeed(role, time);
        var version = Guid.NewGuid();
        // Unique canary comes only from PostgreSQL; never baked into the frontend bundle.
        var canary = "Synthetic statement " + Guid.NewGuid().ToString("N");
        await using (var db = Open(owner))
        {
            var content = new PublishedContentVersion { Id = version, CourseId = "http-own", VersionNumber = 2,
                SourceRevision = "synthetic-browser", PublishedAt = time.At, ContentJson = JsonSerializer.Serialize(new PublishedCourseContent("Real course", "Course description", [
                new("lesson", 1, "Real lesson", "Published **lesson** summary", [new("problem", 1, "Real problem",
                    "## Mathematics\n\n" + canary + "\n\nSolve $x+1=2$.\n\n$$x=1$$\n\n<script>window.pwned=true</script>\n\n![No remote image](https://example.invalid/private.png)\n\n[Unsafe](javascript:alert(1))")]),
                new("locked", 2, "HIDDEN LOCKED", "HIDDEN SUMMARY", [new("secret", 1, "HIDDEN PROBLEM", "HIDDEN STATEMENT")]),
                new("other-lesson", 4, "Empty lesson", null, [])
            ]), new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
            db.Add(content);
            foreach (var lesson in new[] { "lesson", "locked", "other-lesson" })
                db.Add(new LessonAccess { CourseId = "http-own", CourseVersionId = version, LessonId = lesson, IsPublished = lesson != "locked" });
            await db.SaveChangesAsync();
        }
        var token = await HttpIssue(seed.User, seed.Session, clock, jwt); Assert.NotNull(token);
        var evidence = Path.Combine(Environment.GetEnvironmentVariable("LMS_BROWSER_EVIDENCE_ROOT") ?? Path.Combine(root.FullName, "codex/evidence"), "MVP-BROWSER-READ-01", role.ToString());
        Directory.CreateDirectory(evidence);
        // Serialized processing mutations use the accepted adapter/revision protocol.
        long revision = 2; Guid? restriction = null;
        async Task Restrict()
        {
            restriction = Guid.NewGuid();
            await using var db = Open(processing);
            revision = await new PostgresProcessingStateAdministration(db).Change(new(existingOrg, Guid.NewGuid(), revision,
                new AddProcessingRestriction(restriction.Value, seed.Context, 1, seed.Actor, 1)));
        }
        var restrictAfterRead = false;
        await using var app = await HttpHost(clock, jwt, afterContent: async () => {
            if (restrictAfterRead) { restrictAfterRead = false; await Restrict(); }
        }, browserRoot: web);
        var origin = Assert.Single(app.Urls);
        Assert.StartsWith("http://127.0.0.1:", origin);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("LMS_BROWSER_NODE") ?? "node") {
            WorkingDirectory = root.FullName, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(Path.Combine(root.FullName, "tools/browser_read/run.mjs"));
        using var runner = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var stderr = runner.StandardError.ReadToEndAsync(timeout.Token);
        // Anonymous pipe only: no argv/env/file/URL token, no public fixture endpoint.
        await runner.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { origin, token = token.Reveal(), version,
            canary, evidence, role = role.ToString(), privateMarkers = new[] { seed.User, seed.Actor, seed.Session, seed.Context, existingOrg } }));
        var passed = false;
        try
        {
            while (await runner.StandardOutput.ReadLineAsync(timeout.Token) is { } command)
            {
                if (command == "PASS") { passed = true; break; }
                switch (command)
                {
                    case "restrict": await Restrict(); break;
                    case "restrict-after-read": restrictAfterRead = true; break;
                    case "withdraw":
                        Assert.NotNull(restriction);
                        await using (var db = Open(processing)) revision = await new PostgresProcessingStateAdministration(db).Change(
                            new(existingOrg, Guid.NewGuid(), revision, new WithdrawProcessingRestriction(restriction.Value, 1)));
                        restriction = null; break;
                    case "hide-lessons":
                        await using (var db = Open(owner)) await db.Lessons.Where(l => l.CourseId == "http-own").ExecuteUpdateAsync(s => s.SetProperty(l => l.IsPublished, false));
                        break;
                    case "show-lessons":
                        await using (var db = Open(owner)) await db.Lessons.Where(l => l.CourseId == "http-own" && l.LessonId != "locked").ExecuteUpdateAsync(s => s.SetProperty(l => l.IsPublished, true));
                        break;
                    case "database-down": await Sql(owner, "REVOKE SELECT ON \"ProcessingRestrictions\" FROM c2h_runtime"); break;
                    case "database-up": await Sql(owner, "GRANT SELECT ON \"ProcessingRestrictions\" TO c2h_runtime"); break;
                    case "expire": time.Elapsed = TimeSpan.FromMinutes(15); break;
                    case "reissue":
                        token = await HttpIssue(seed.User, seed.Session, clock, jwt); Assert.NotNull(token);
                        await runner.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { token = token.Reveal() })); continue;
                    case "clock-lost": time.Current = false; break;
                    default: throw new InvalidOperationException("Unknown browser harness command (details suppressed)");
                }
                await runner.StandardInput.WriteLineAsync("{}");
            }
            await runner.WaitForExitAsync(timeout.Token);
            var errors = await stderr;
            // On failure print diagnostics only after secret scrubbing; no HAR/trace/video is recorded.
            errors = System.Text.RegularExpressions.Regex.Replace(errors, @"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", "[REDACTED]");
            Assert.True(passed && runner.ExitCode == 0, "Browser E2E failed: " + errors);
        }
        finally { if (!runner.HasExited) runner.Kill(entireProcessTree: true); }
    }
}
