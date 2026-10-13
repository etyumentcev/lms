using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Lms.Application;
using Lms.Infrastructure;

namespace Lms.Api.Tests;
public sealed partial class IsolatedProcessingDeploymentTests
{
    [BrowserReadTheory] [InlineData(ScopeRole.Student)] [InlineData(ScopeRole.Teacher)]
    public async Task Browser_photo_real_student_teacher_A4_and_denials(ScopeRole role)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Lms.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "browser-work", "browser-work", clock);
        var seed = await HttpSeed(role, time); var version = await WorkspaceSeed(time);
        using var files = new PhotoFiles();
        var data = await PhotoSeed(seed.Actor, role, version, time, seed.Context, files.Storage);
        var outsider = (await IdentitySeed()).Actor;
        await ChangeRestricted(new(AuthorityKind.Membership, AuthorityOperation.Grant, existingOrg, outsider, 0, Role: ScopeRole.Teacher));
        var hidden = await PhotoSeed(outsider, ScopeRole.Teacher, version, time, seed.Context, files.Storage, "h");
        var token = await HttpIssue(seed.User, seed.Session, clock, jwt); Assert.NotNull(token);
        var evidence = Path.Combine(Environment.GetEnvironmentVariable("LMS_BROWSER_EVIDENCE_ROOT") ?? Path.Combine(root.FullName, "codex/evidence"), "MVP-PHOTO-READ", role.ToString()); Directory.CreateDirectory(evidence);
        await using var app = await HttpHost(clock, jwt, browserRoot: Path.Combine(root.FullName, "src/Lms.Web/dist-browser-read"), workReads: true, photoStorage: files.Storage);
        var before = await Scalar<string>(owner, "SELECT md5(string_agg(row_to_json(s)::text, '' ORDER BY \"Id\")) FROM \"Submissions\" s");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("LMS_BROWSER_NODE") ?? "node") {
            WorkingDirectory = root.FullName, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(Path.Combine(root.FullName, "tools/browser_read/photos.mjs"));
        using var runner = Process.Start(start)!; using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var errors = runner.StandardError.ReadToEndAsync(timeout.Token);
        await runner.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { origin = Assert.Single(app.Urls), token = token.Reveal(), version,
            work = data.Work.Id, hidden = hidden.Work.Id, attachment = data.Work.Submissions[0].AttachmentIds[0], submission = data.Work.Submissions[0].Id, latest = data.Work.Submissions[1].Id, hiddenAttachment = hidden.Work.Submissions[0].AttachmentIds[0], role = role.ToString(), evidence }));
        var passed = false; long revision = 2; Guid restriction = Guid.NewGuid();
        try
        {
            while (await runner.StandardOutput.ReadLineAsync(timeout.Token) is { } command)
            {
                if (command == "PASS") { passed = true; break; }
                if (command is "missing" or "corrupt")
                {
                    await using var db = Open(owner);
                    var id = data.Work.Submissions[0].AttachmentIds[0];
                    var record = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.StoredAttachments, x => x.Id == id);
                    var file = Path.Combine(files.Root, record.StorageKey);
                    if (command == "missing") File.Delete(file); else await File.WriteAllBytesAsync(file, new byte[record.Size]);
                }
                else if (command == "expire") time.Elapsed = TimeSpan.FromMinutes(15);
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
