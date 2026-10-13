using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Lms.Application;
using Lms.Infrastructure;

namespace Lms.Api.Tests;
public sealed partial class IsolatedProcessingDeploymentTests
{
    [BrowserReadTheory] [InlineData(ScopeRole.Student)] [InlineData(ScopeRole.Teacher)]
    public async Task Browser_work_real_student_teacher_history_and_denials(ScopeRole role)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Lms.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "browser-work", "browser-work", clock);
        var seed = await HttpSeed(role, time); var version = await WorkspaceSeed(time);
        var data = await ProtectedWorkSeed(seed.Actor, role, version, time);
        var outsider = (await IdentitySeed()).Actor;
        await ChangeRestricted(new(AuthorityKind.Membership, AuthorityOperation.Grant, existingOrg, outsider, 0, Role: ScopeRole.Teacher));
        var hidden = await ProtectedWorkSeed(outsider, ScopeRole.Teacher, version, time, group: "h");
        var token = await HttpIssue(seed.User, seed.Session, clock, jwt); Assert.NotNull(token);
        var evidence = Path.Combine(Environment.GetEnvironmentVariable("LMS_BROWSER_EVIDENCE_ROOT") ?? Path.Combine(root.FullName, "codex/evidence"), "MVP-PROTECTED-WORK-READ-01", role.ToString()); Directory.CreateDirectory(evidence);
        await using var app = await HttpHost(clock, jwt, browserRoot: Path.Combine(root.FullName, "src/Lms.Web/dist-browser-read"), workReads: true);
        var before = await Scalar<string>(owner, "SELECT md5(string_agg(row_to_json(s)::text, '' ORDER BY \"Id\")) FROM \"Submissions\" s");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("LMS_BROWSER_NODE") ?? "node") {
            WorkingDirectory = root.FullName, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(Path.Combine(root.FullName, "tools/browser_read/work.mjs"));
        using var runner = Process.Start(start)!; using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var errors = runner.StandardError.ReadToEndAsync(timeout.Token);
        await runner.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { origin = Assert.Single(app.Urls), token = token.Reveal(), version,
            work = data.Work.Id, hidden = hidden.Work.Id, attachment = data.Work.Submissions[0].AttachmentIds[0], role = role.ToString(), evidence }));
        var passed = false; long revision = 2; Guid restriction = Guid.NewGuid();
        try
        {
            while (await runner.StandardOutput.ReadLineAsync(timeout.Token) is { } command)
            {
                if (command == "PASS") { passed = true; break; }
                if (command == "expire") time.Elapsed = TimeSpan.FromMinutes(15);
                else
                {
                    await using var db = Open(processing);
                    revision = await new PostgresProcessingStateAdministration(db).Change(new(existingOrg, Guid.NewGuid(), revision, command switch {
                        "restrict" => new AddProcessingRestriction(restriction, seed.Context, 1, data.Student, 1),
                        "withdraw" => new WithdrawProcessingRestriction(restriction, 1),
                        _ => throw new InvalidOperationException("Unexpected runner command") }));
                }
                await runner.StandardInput.WriteLineAsync("{}");
            }
            await runner.WaitForExitAsync(timeout.Token);
            var diagnostic = System.Text.RegularExpressions.Regex.Replace(await errors, @"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", "[REDACTED]");
            Assert.True(passed && runner.ExitCode == 0, diagnostic);
            Assert.Equal(before, await Scalar<string>(owner, "SELECT md5(string_agg(row_to_json(s)::text, '' ORDER BY \"Id\")) FROM \"Submissions\" s"));
        }
        finally { if (!runner.HasExited) runner.Kill(entireProcessTree: true); }
    }
}
