using Microsoft.AspNetCore.TestHost;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Lms.Application;
using Lms.Domain;
using Lms.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lms.Api.Tests;
public sealed partial class IsolatedProcessingDeploymentTests
{
    private sealed class AfterPhoto(IProtectedPhotoQueries inner, Func<Task> after) : IProtectedPhotoQueries
    {
        public async Task<ProtectedPhotoRead> Read(TokenAuthority t, PhotoSelection s, ApplicationDeadline d, CancellationToken ct)
        { var read = await inner.Read(t, s, d, ct); await after(); return read; }
        public Task<bool> CanReturn(TokenAuthority t, ProtectedPhotoRead r, ApplicationDeadline d, CancellationToken ct) => inner.CanReturn(t, r, d, ct);
    }
    private static string Repository()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Lms.sln"))) root = root.Parent;
        return root!.FullName;
    }
    private sealed class ObservedStorage(IAttachmentStorage inner) : IAttachmentStorage
    {
        public int OpenCount { get; private set; }
        public Task Store(string key, Stream bytes, CancellationToken ct) => inner.Store(key, bytes, ct);
        public Task Delete(string key, CancellationToken ct) => inner.Delete(key, ct);
        public Task<Stream> Open(string key, CancellationToken ct) { OpenCount++; return inner.Open(key, ct); }
    }
    private sealed class PhotoFiles : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "lms-synthetic-photos-" + Guid.NewGuid().ToString("N"));
        public LocalFileAttachmentStorage Storage => new(new() { Root = Root });
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    private async Task<(Guid Student, Guid Teacher, ProblemWork Work)> PhotoSeed(Guid actor, ScopeRole role, Guid version, HttpClock time,
        Guid context, IAttachmentStorage storage, string group = "g")
    {
        var student = role == ScopeRole.Student ? actor : (await IdentitySeed()).Actor;
        var teacher = role == ScopeRole.Teacher ? actor : (await IdentitySeed()).Actor;
        if (student != actor) await ChangeRestricted(new(AuthorityKind.Membership, AuthorityOperation.Grant, existingOrg, student, 0, Role: ScopeRole.Student));
        if (teacher != actor) await ChangeRestricted(new(AuthorityKind.Membership, AuthorityOperation.Grant, existingOrg, teacher, 0, Role: ScopeRole.Teacher));
        if (student != actor) await ChangeRestricted(new(AuthorityKind.Enrollment, AuthorityOperation.Grant, existingOrg, student, 0, 1, "http-own", group));
        await using var db = Open(owner);
        var assignment = new ProblemAssignment(Guid.NewGuid(), student, "http-own", new(version, "lesson", "problem"));
        db.Add(assignment); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var upload = new AttachmentService(db, storage, new PostgresAccessPolicy(db, new HttpTimeProvider(time)), new(), new HttpTimeProvider(time), NullLogger<AttachmentService>.Instance);
        var ids = new List<Guid>();
        for (var i = 1; i <= 3; i++)
        {
            using var bytes = File.OpenRead(Path.Combine(Repository(), "tests/fixtures/photos", $"a4-{i}.png"));
            var result = await upload.Upload(new(student, AuthorRole.Student), $"synthetic-a4-{i}.png", "image/png", bytes, CancellationToken.None);
            ids.Add(result.AttachmentId);
            db.Add(new AttachmentScope { AttachmentId = result.AttachmentId, OrganizationId = existingOrg, CourseId = "http-own",
                UploaderActorId = student, ContextId = context, ContextVersion = 1 });
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        }
        var work = new ProblemWork(Guid.NewGuid(), assignment);
        var first = work.SubmitSolution(assignment, student, "First synthetic attempt", ids.Take(2), time.At);
        work.RequestRevision(teacher, first.Id, "First attempt: explain the calculation", time.At.AddSeconds(1));
        work.SubmitSolution(assignment, student, "Second synthetic attempt", ids.Skip(2), time.At.AddSeconds(2));
        // Synthetic historical fixture, not execution through a legacy teacher port.
        db.Add(work); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        return (student, teacher, work);
    }
    private static string PhotoRoot(Guid version, ProblemWork work, int attempt = 0) => WorkRoot(version) + $"/{work.Id}/submissions/{work.Submissions[attempt].Id}/photos";
    [Theory] [InlineData(ScopeRole.Student)] [InlineData(ScopeRole.Teacher)]
    public async Task Http_photo_originals_and_independent_parent_denials(ScopeRole role)
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048); using var files = new PhotoFiles();
        var jwt = new ComponentJwt(key, "photo", "photo", clock); var seed = await HttpSeed(role, time); var version = await WorkspaceSeed(time);
        var own = await PhotoSeed(seed.Actor, role, version, time, seed.Context, files.Storage);
        var outsider = (await IdentitySeed()).Actor;
        await ChangeRestricted(new(AuthorityKind.Membership, AuthorityOperation.Grant, existingOrg, outsider, 0, Role: ScopeRole.Teacher));
        var other = await PhotoSeed(outsider, ScopeRole.Teacher, version, time, seed.Context, files.Storage, "h");
        var token = await HttpIssue(seed.User, seed.Session, clock, jwt);
        var observed = new ObservedStorage(files.Storage);
        await using var app = await HttpHost(clock, jwt, workReads: true, photoStorage: observed);
        var root = PhotoRoot(version, own.Work); var id = own.Work.Submissions[0].AttachmentIds[0]; var path = root + "/" + id;
        using var manifest = JsonDocument.Parse(await HttpGet(app, token, HttpStatusCode.OK, root));
        Assert.Equal(own.Work.Submissions[0].AttachmentIds, manifest.RootElement.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()));
        using var client = app.GetTestClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", token!.Reveal());
        for (var attempt = 0; attempt < 2; attempt++)
        for (var page = 0; page < own.Work.Submissions[attempt].AttachmentIds.Count; page++)
        {
            var response = await client.GetAsync(PhotoRoot(version, own.Work, attempt) + "/" + own.Work.Submissions[attempt].AttachmentIds[page]);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
            Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(Repository(), "tests/fixtures/photos", $"a4-{(attempt == 0 ? page + 1 : 3)}.png")), await response.Content.ReadAsByteArrayAsync());
        }
        var readsBeforeDenials = observed.OpenCount;
        foreach (var invalid in new[] { root + "/" + other.Work.Submissions[0].AttachmentIds[0], PhotoRoot(version, other.Work) + "/" + other.Work.Submissions[0].AttachmentIds[0],
            PhotoRoot(version, own.Work, 1) + "/" + id, path.Replace(own.Work.Id.ToString(), other.Work.Id.ToString()),
            path.Replace("http-own", "http-other"), path.Replace("http-own", "http-foreign"), path.Replace("/lessons/lesson", "/lessons/absent"),
            path.Replace(version.ToString(), Guid.NewGuid().ToString()), path.Replace(id.ToString(), Guid.NewGuid().ToString()), root + "/bad-id" })
            await HttpGet(app, token, HttpStatusCode.Forbidden, invalid);
        await HttpGet(app, null, HttpStatusCode.Unauthorized, path); await HttpGet(app, new("forged"), HttpStatusCode.Unauthorized, path);
        await HttpGet(app, null, HttpStatusCode.NotFound, "/api/v1/attachments/" + id + "/content");
        Assert.Equal(readsBeforeDenials, observed.OpenCount);
        client.DefaultRequestHeaders.Range = new(0, 10);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
        await ChangeRestricted(seed.Grant with { Operation = AuthorityOperation.Revoke, ExpectedVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode); // accepted delayed revoke
        time.Elapsed = TimeSpan.FromMinutes(15); await HttpGet(app, token, HttpStatusCode.Unauthorized, path);
    }
    [Theory]
    [InlineData("missing", false)] [InlineData("corrupt", false)] [InlineData("same-length-corrupt", false)]
    [InlineData("quarantine", false)] [InlineData("scope", false)] [InlineData("processing-db", false)]
    [InlineData("restriction", false)] [InlineData("restriction", true)]
    [InlineData("expiry", true)] [InlineData("deadline", true)] [InlineData("clock", true)]
    public async Task Http_photo_faults_fail_closed(string fault, bool late)
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048); using var files = new PhotoFiles();
        var jwt = new ComponentJwt(key, "photo", "photo", clock); var seed = await HttpSeed(ScopeRole.Student, time); var version = await WorkspaceSeed(time);
        var own = await PhotoSeed(seed.Actor, ScopeRole.Student, version, time, seed.Context, files.Storage);
        var id = own.Work.Submissions[0].AttachmentIds[0]; var token = await HttpIssue(seed.User, seed.Session, clock, jwt);
        async Task Fault()
        {
            if (fault == "expiry") time.Elapsed = TimeSpan.FromMinutes(15);
            else if (fault == "deadline") time.Elapsed = TimeSpan.FromSeconds(60);
            else if (fault == "clock") time.Current = false;
            else if (fault == "restriction") { await using var db = Open(processing); await new PostgresProcessingStateAdministration(db).Change(new(existingOrg, Guid.NewGuid(), 2, new AddProcessingRestriction(Guid.NewGuid(), seed.Context, 1, own.Student, 1))); }
            else if (fault == "processing-db") await Sql(owner, "REVOKE SELECT ON \"ProcessingRestrictions\" FROM c2h_runtime");
            else if (fault == "quarantine") await Sql(owner, $"UPDATE \"AttachmentScopes\" SET \"Quarantined\"=true WHERE \"AttachmentId\"='{id}'");
            else if (fault == "scope") await Sql(owner, "REVOKE SELECT ON \"AttachmentScopes\" FROM c2h_runtime");
            else
            {
                await using var db = Open(owner); var record = await db.StoredAttachments.SingleAsync(x => x.Id == id);
                var path = Path.Combine(files.Root, record.StorageKey);
                if (fault == "missing") File.Delete(path);
                else await File.WriteAllBytesAsync(path, fault == "same-length-corrupt" ? new byte[record.Size] : [1, 2, 3]);
            }
        }
        if (!late) await Fault();
        await using var app = await HttpHost(clock, jwt, workReads: true, photoStorage: files.Storage, afterPhoto: late ? Fault : null);
        await HttpGet(app, token, fault is "restriction" or "quarantine" ? HttpStatusCode.Forbidden : HttpStatusCode.ServiceUnavailable,
            PhotoRoot(version, own.Work) + "/" + id);
    }
    [Fact]
    public async Task Http_photo_unqualified_history_and_pending_ids_do_not_become_readable()
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048); using var files = new PhotoFiles();
        var jwt = new ComponentJwt(key, "photo", "photo", clock); var seed = await HttpSeed(ScopeRole.Student, time); var version = await WorkspaceSeed(time);
        var data = await ProtectedWorkSeed(seed.Actor, ScopeRole.Student, version, time);
        var token = await HttpIssue(seed.User, seed.Session, clock, jwt);
        await using var app = await HttpHost(clock, jwt, workReads: true, photoStorage: files.Storage);
        await HttpGet(app, token, HttpStatusCode.Forbidden, PhotoRoot(version, data.Work));
        await HttpGet(app, token, HttpStatusCode.Forbidden, PhotoRoot(version, data.Work) + "/" + data.Work.Submissions[0].AttachmentIds[0]);
        await using var db = Open(owner);
        var upload = new AttachmentService(db, files.Storage, new PostgresAccessPolicy(db, new HttpTimeProvider(time)), new(), new HttpTimeProvider(time), NullLogger<AttachmentService>.Instance);
        var pending = await upload.Upload(new(seed.Actor, AuthorRole.Student), "synthetic.png", "image/png", new MemoryStream(AttachmentImages.Png), CancellationToken.None);
        await HttpGet(app, token, HttpStatusCode.Forbidden, PhotoRoot(version, data.Work) + "/" + pending.AttachmentId);
        var record = await db.StoredAttachments.AsNoTracking().SingleAsync(x => x.Id == pending.AttachmentId);
        await HttpGet(app, null, HttpStatusCode.NotFound, "/" + record.StorageKey);
    }
    [Fact]
    public async Task Http_photo_capacity_rejects_without_unbounded_wait_and_releases_after_completion()
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048); using var files = new PhotoFiles();
        var jwt = new ComponentJwt(key, "photo", "photo", clock); var seed = await HttpSeed(ScopeRole.Student, time); var version = await WorkspaceSeed(time);
        var data = await PhotoSeed(seed.Actor, ScopeRole.Student, version, time, seed.Context, files.Storage);
        var token = await HttpIssue(seed.User, seed.Session, clock, jwt);
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource(); var count = 0;
        await using var app = await HttpHost(clock, jwt, workReads: true, photoStorage: files.Storage, afterPhoto: async () => {
            if (Interlocked.Increment(ref count) == 4) entered.TrySetResult(); await release.Task;
        });
        var path = PhotoRoot(version, data.Work);
        var pending = Enumerable.Range(0, 4).Select(_ => HttpGet(app, token, HttpStatusCode.OK, path)).ToArray();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await HttpGet(app, token, HttpStatusCode.ServiceUnavailable, path);
        }
        finally { release.TrySetResult(); }
        await Task.WhenAll(pending);
        await HttpGet(app, token, HttpStatusCode.OK, path);
    }

    [Fact]
    public async Task Http_photo_same_group_peer_denied_and_owner_move_keeps_historical_photos()
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048); using var files = new PhotoFiles();
        var jwt = new ComponentJwt(key, "photo", "photo", clock); var seed = await HttpSeed(ScopeRole.Student, time); var version = await WorkspaceSeed(time);
        var own = await PhotoSeed(seed.Actor, ScopeRole.Student, version, time, seed.Context, files.Storage);
        var teacher = (await IdentitySeed()).Actor;
        await ChangeRestricted(new(AuthorityKind.Membership, AuthorityOperation.Grant, existingOrg, teacher, 0, Role: ScopeRole.Teacher));
        var peer = await PhotoSeed(teacher, ScopeRole.Teacher, version, time, seed.Context, files.Storage);
        var token = await HttpIssue(seed.User, seed.Session, clock, jwt);
        await using var app = await HttpHost(clock, jwt, workReads: true, photoStorage: files.Storage);
        await HttpGet(app, token, HttpStatusCode.Forbidden, PhotoRoot(version, peer.Work));
        await HttpGet(app, token, HttpStatusCode.Forbidden, PhotoRoot(version, peer.Work) + "/" + peer.Work.Submissions[0].AttachmentIds[0]);
        await ChangeRestricted(seed.Grant with { Operation = AuthorityOperation.Move, ExpectedVersion = 1, GroupId = "h" });
        var moved = await HttpIssue(seed.User, seed.Session, clock, jwt); Assert.NotNull(moved);
        using var client = app.GetTestClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", moved.Reveal());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(PhotoRoot(version, own.Work) + "/" + own.Work.Submissions[0].AttachmentIds[0])).StatusCode);
        await HttpGet(app, moved, HttpStatusCode.Forbidden, PhotoRoot(version, peer.Work));
    }

}
