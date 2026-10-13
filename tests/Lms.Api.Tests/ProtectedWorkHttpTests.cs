using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Lms.Application;
using Lms.Domain;
using Lms.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Tests;

public sealed partial class IsolatedProcessingDeploymentTests
{
    private sealed class AfterWork(IProtectedWorkQueries inner, Func<Task> after) : IProtectedWorkQueries
    {
        public async Task<ProtectedWorkRead> Read(TokenAuthority t, WorkSelection s, ApplicationDeadline d, CancellationToken ct)
        { var read = await inner.Read(t, s, d, ct); await after(); return read; }
        public Task<bool> CanReturn(TokenAuthority t, ProtectedWorkRead r, ApplicationDeadline d, CancellationToken ct) => inner.CanReturn(t, r, d, ct);
    }
    private async Task<(Guid Student, Guid Teacher, ProblemWork Work)> ProtectedWorkSeed(Guid actor, ScopeRole role, Guid version, HttpClock time,
        string group = "g", string course = "http-own", Guid? org = null, bool photos = true)
    {
        var organization = org ?? existingOrg;
        var student = role == ScopeRole.Student ? actor : (await IdentitySeed()).Actor;
        var teacher = role == ScopeRole.Teacher ? actor : (await IdentitySeed()).Actor;
        if (student != actor || organization != existingOrg)
            await ChangeRestricted(new(AuthorityKind.Membership, AuthorityOperation.Grant, organization, student, 0, Role: ScopeRole.Student));
        if (teacher != actor || organization != existingOrg)
            await ChangeRestricted(new(AuthorityKind.Membership, AuthorityOperation.Grant, organization, teacher, 0, Role: ScopeRole.Teacher));
        if (student != actor || course != "http-own")
            await ChangeRestricted(new(AuthorityKind.Enrollment, AuthorityOperation.Grant, organization, student, 0, 1, course, group));
        await using var db = Open(owner);
        var assignment = new ProblemAssignment(Guid.NewGuid(), student, course, new(version, "lesson", "problem"));
        db.Add(assignment); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var ids = photos ? new[] { Guid.NewGuid(), Guid.NewGuid() } : [];
        foreach (var id in ids) db.Add(new AttachmentAccess { Id = id, OwnerId = student, IsEligible = true, ExpiresAt = time.At.AddDays(1) });
        await db.SaveChangesAsync();
        foreach (var id in ids)
            db.Add(new StoredAttachment { Id = id, OwnerId = student, FileName = "UNVERIFIED.png", MediaType = "image/png", Size = 100,
                StorageKey = Guid.NewGuid().ToString("N"), CreatedAt = time.At });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var work = new ProblemWork(Guid.NewGuid(), assignment);
        var first = work.SubmitSolution(assignment, student, "First synthetic attempt", ids, time.At);
        work.RequestRevision(teacher, first.Id, "First attempt: explain the calculation", time.At.AddSeconds(1));
        work.SubmitSolution(assignment, student, "Second synthetic attempt", [], time.At.AddSeconds(2));
        // Synthetic historical fixture, not execution through a legacy teacher port.
        db.Add(work); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        return (student, teacher, work);
    }
    private static string WorkRoot(Guid version) => WorkspacePath(version, 1) + "/works";
    [Theory] [InlineData(ScopeRole.Student)] [InlineData(ScopeRole.Teacher)]
    public async Task Http_work_reads_own_history_exact_dto_and_no_unverified_files(ScopeRole role)
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "work", "work", clock); var seed = await HttpSeed(role, time); var version = await WorkspaceSeed(time);
        var data = await ProtectedWorkSeed(seed.Actor, role, version, time);
        var token = await HttpIssue(seed.User, seed.Session, clock, jwt);
        await using var app = await HttpHost(clock, jwt, workReads: true);
        foreach (var listPath in new[] { WorkRoot(version), WorkspacePath(version, 2) + "/works" })
        {
            using var list = JsonDocument.Parse(await HttpGet(app, token, HttpStatusCode.OK, listPath));
            var item = Assert.Single(list.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(data.Work.Id, item.GetProperty("id").GetGuid());
            Assert.Equal(WorkRoot(version) + "/" + data.Work.Id, item.GetProperty("href").GetString());
        }
        var path = WorkRoot(version) + "/" + data.Work.Id;
        var body = await HttpGet(app, token, HttpStatusCode.OK, path);
        using var result = JsonDocument.Parse(body);
        Assert.Equal(new[] { "id", "status", "statementHref", "attempts", "messages" }, result.RootElement.EnumerateObject().Select(p => p.Name));
        var attempts = result.RootElement.GetProperty("attempts"); Assert.Equal(2, attempts.GetArrayLength());
        Assert.Equal(1, attempts[0].GetProperty("number").GetInt32()); Assert.Equal(2, attempts[0].GetProperty("photoCount").GetInt32());
        Assert.Equal("unavailable", attempts[0].GetProperty("photoAvailability").GetString());
        Assert.Equal("First attempt: explain the calculation", attempts[0].GetProperty("reviews")[0].GetProperty("feedbackMarkdown").GetString());
        Assert.Empty(attempts[1].GetProperty("reviews").EnumerateArray());
        foreach (var secret in new[] { seed.Actor.ToString(), data.Student.ToString(), data.Teacher.ToString(), "storageKey", "UNVERIFIED", "permissions", "annotations" }) Assert.DoesNotContain(secret, body);
        Assert.Equal(body, await HttpGet(app, token, HttpStatusCode.OK, path + "?studentId=forged&role=Teacher&groupId=h"));
        foreach (var id in data.Work.Submissions[0].AttachmentIds)
            await HttpGet(app, token, HttpStatusCode.NotFound, $"/api/v1/attachments/{id}/content");
        await HttpGet(app, token, HttpStatusCode.OK, result.RootElement.GetProperty("statementHref").GetString()!);
        await ChangeRestricted(seed.Grant with { Operation = AuthorityOperation.Revoke, ExpectedVersion = 1 });
        Assert.Null(await HttpIssue(seed.User, seed.Session, clock, jwt));
        Assert.Equal(body, await HttpGet(app, token, HttpStatusCode.OK, path));
        time.Elapsed = TimeSpan.FromMinutes(15);
        await HttpGet(app, token, HttpStatusCode.Unauthorized, path);
    }
    [Theory] [InlineData(ScopeRole.Student)] [InlineData(ScopeRole.Teacher)]
    public async Task Http_work_negative_student_teacher_group_course_org_and_parent(ScopeRole role)
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "work", "work", clock); var seed = await HttpSeed(role, time); var version = await WorkspaceSeed(time);
        var own = await ProtectedWorkSeed(seed.Actor, role, version, time);
        var outsider = (await IdentitySeed()).Actor;
        await ChangeRestricted(new(AuthorityKind.Membership, AuthorityOperation.Grant, existingOrg, outsider, 0, Role: ScopeRole.Teacher));
        var other = await ProtectedWorkSeed(outsider, ScopeRole.Teacher, version, time, group: "h");
        var token = await HttpIssue(seed.User, seed.Session, clock, jwt);
        await using var app = await HttpHost(clock, jwt, workReads: true);
        var path = WorkRoot(version) + "/" + own.Work.Id;
        await HttpGet(app, token, HttpStatusCode.OK, path);
        await HttpGet(app, token, HttpStatusCode.Forbidden, WorkRoot(version) + "/" + other.Work.Id);
        using var list = JsonDocument.Parse(await HttpGet(app, token, HttpStatusCode.OK, WorkRoot(version)));
        Assert.Equal(own.Work.Id, Assert.Single(list.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        foreach (var invalid in new[] { path.Replace("http-own", "http-other"), path.Replace("http-own", "http-foreign"),
            path.Replace(version.ToString(), Guid.NewGuid().ToString()), path.Replace("/lessons/lesson", "/lessons/other-lesson"),
            WorkRoot(version) + "/" + Guid.NewGuid(), WorkRoot(version) + "/invalid" })
            await HttpGet(app, token, HttpStatusCode.Forbidden, invalid);
        await HttpGet(app, null, HttpStatusCode.Unauthorized, path);
        await HttpGet(app, new("forged"), HttpStatusCode.Unauthorized, path);
        await HttpGet(app, token, HttpStatusCode.Forbidden, WorkspacePath(version, 2).Replace("/problems/problem", "/problems/absent") + "/works");
    }
    public static IEnumerable<object[]> WorkFailures()
    {
        foreach (var detail in new[] { false, true }) foreach (var late in new[] { false, true })
            foreach (var fault in new[] { "student", "reader", "context", "expiry", "deadline", "unknown" })
                yield return new object[] { detail, late, fault };
    }
    [Theory] [MemberData(nameof(WorkFailures))]
    public async Task Http_work_processing_jwt_deadline_before_and_after_materialization(bool detail, bool late, string fault)
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "work", "work", clock); var seed = await HttpSeed(ScopeRole.Teacher, time); var version = await WorkspaceSeed(time);
        var data = await ProtectedWorkSeed(seed.Actor, ScopeRole.Teacher, version, time);
        var token = await HttpIssue(seed.User, seed.Session, clock, jwt); var path = WorkRoot(version) + (detail ? "/" + data.Work.Id : "");
        await using (var positive = await HttpHost(clock, jwt, workReads: true)) await HttpGet(positive, token, HttpStatusCode.OK, path);
        async Task Mutate()
        {
            if (fault == "expiry") { time.Elapsed = TimeSpan.FromMinutes(15); return; }
            if (fault == "deadline") { time.Elapsed += TimeSpan.FromSeconds(60); return; }
            if (fault == "unknown") { time.Current = false; return; }
            await using var db = Open(processing);
            await new PostgresProcessingStateAdministration(db).Change(new(existingOrg, Guid.NewGuid(), 2,
                new AddProcessingRestriction(Guid.NewGuid(), seed.Context, 1, fault == "context" ? null : fault == "reader" ? seed.Actor : data.Student, 1)));
        }
        if (!late && fault != "deadline") await Mutate();
        await using var app = await HttpHost(clock, jwt, workReads: true, afterWork: late ? Mutate : null,
            ingress: !late && fault == "deadline" ? _ => time.Elapsed += TimeSpan.FromSeconds(60) : null);
        var expected = fault == "deadline" || late && fault is "expiry" or "unknown" ? HttpStatusCode.ServiceUnavailable : !late && fault is "expiry" or "unknown" ? HttpStatusCode.Unauthorized :
            !late && !detail && fault == "student" ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
        var body = await HttpGet(app, token, expected, path);
        if (expected == HttpStatusCode.OK) Assert.Equal("{\"items\":[]}", body);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Http_work_embedded_teacher_subject_is_checked_even_for_student(bool late)
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "work", "work", clock); var seed = await HttpSeed(ScopeRole.Student, time); var version = await WorkspaceSeed(time);
        var data = await ProtectedWorkSeed(seed.Actor, ScopeRole.Student, version, time); var token = await HttpIssue(seed.User, seed.Session, clock, jwt);
        var path = WorkRoot(version) + "/" + data.Work.Id;
        await using (var positive = await HttpHost(clock, jwt, workReads: true)) await HttpGet(positive, token, HttpStatusCode.OK, path);
        async Task Restrict()
        {
            await using var db = Open(processing);
            await new PostgresProcessingStateAdministration(db).Change(new(existingOrg, Guid.NewGuid(), 2,
                new AddProcessingRestriction(Guid.NewGuid(), seed.Context, 1, data.Teacher, 1)));
        }
        if (!late) await Restrict();
        await using var app = await HttpHost(clock, jwt, workReads: true, afterWork: late ? Restrict : null);
        await HttpGet(app, token, HttpStatusCode.Forbidden, path);
    }
    [Theory] [InlineData(59.999, 200)] [InlineData(60, 503)] [InlineData(60.001, 503)]
    public async Task Http_work_deadline_boundaries(double seconds, int status)
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "work", "work", clock); var seed = await HttpSeed(ScopeRole.Student, time); var version = await WorkspaceSeed(time);
        var data = await ProtectedWorkSeed(seed.Actor, ScopeRole.Student, version, time); var token = await HttpIssue(seed.User, seed.Session, clock, jwt);
        await using var app = await HttpHost(clock, jwt, workReads: true, afterWork: () => { time.Elapsed += TimeSpan.FromSeconds(seconds); return Task.CompletedTask; });
        await HttpGet(app, token, (HttpStatusCode)status, WorkRoot(version) + "/" + data.Work.Id);
    }
    [Theory] [InlineData("version")] [InlineData("processing-db")] [InlineData("missing-author")]
    public async Task Http_work_final_failure_and_missing_subject_evidence(string fault)
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "work", "work", clock); var seed = await HttpSeed(ScopeRole.Student, time); var version = await WorkspaceSeed(time);
        var data = await ProtectedWorkSeed(seed.Actor, ScopeRole.Student, version, time); var token = await HttpIssue(seed.User, seed.Session, clock, jwt);
        var path = WorkRoot(version) + "/" + data.Work.Id;
        await using (var positive = await HttpHost(clock, jwt, workReads: true)) await HttpGet(positive, token, HttpStatusCode.OK, path);
        async Task Append(bool unknown)
        {
            await using var db = Open(owner); var store = new PostgresWorkStore(db);
            var work = (await store.FindWork(data.Work.Id, CancellationToken.None))!; var v = work.Version;
            // Deliberately inject corrupt/concurrent history as the fixture owner. A
            // teacher executor must reject the unknown author and cannot set this up.
            var message = work.PostMessage(unknown ? Guid.NewGuid() : data.Teacher, AuthorRole.Teacher, "Concurrent private feedback", null, [], time.At.AddSeconds(4));
            await using var tx = await db.Database.BeginTransactionAsync();
            Assert.Equal(1, await db.Works.Where(w => w.Id == work.Id && w.Version == v)
                .ExecuteUpdateAsync(set => set.SetProperty(w => w.Version, work.Version)));
            db.Add(message); await db.SaveChangesAsync(); await tx.CommitAsync();
        }
        if (fault == "missing-author") await Append(true);
        await using var app = await HttpHost(clock, jwt, workReads: true, afterWork: fault == "missing-author" ? null : async () => {
            if (fault == "version") await Append(false);
            else await Sql(owner, "REVOKE SELECT ON \"ProcessingRestrictions\" FROM c2h_runtime");
        });
        await HttpGet(app, token, fault == "processing-db" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Forbidden, path);
        if (fault != "processing-db")
        {
            await using var verify = Open(owner);
            Assert.Equal(data.Work.Version + 1, await verify.Works.Where(w => w.Id == data.Work.Id).Select(w => w.Version).SingleAsync());
        }
    }
    [Fact]
    public async Task Http_work_student_move_preserves_historical_ownership_and_never_reads_storage()
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "work", "work", clock); var seed = await HttpSeed(ScopeRole.Student, time); var version = await WorkspaceSeed(time);
        var data = await ProtectedWorkSeed(seed.Actor, ScopeRole.Student, version, time); var token = await HttpIssue(seed.User, seed.Session, clock, jwt);
        await using var app = await HttpHost(clock, jwt, workReads: true);
        var path = WorkRoot(version) + "/" + data.Work.Id;
        var before = await HttpGet(app, token, HttpStatusCode.OK, path);
        await ChangeRestricted(seed.Grant with { Operation = AuthorityOperation.Move, ExpectedVersion = 1, GroupId = "h" });
        var moved = await HttpIssue(seed.User, seed.Session, clock, jwt); Assert.NotNull(moved);
        await Sql(owner, "REVOKE SELECT ON \"StoredAttachments\",\"AttachmentScopes\",\"AccessAttachments\" FROM c2h_runtime");
        Assert.Equal(before, await HttpGet(app, moved, HttpStatusCode.OK, path));
        Assert.Equal(before, await HttpGet(app, token, HttpStatusCode.OK, path));
    }

    [Theory] [InlineData(ScopeRole.Student, "http-other")] [InlineData(ScopeRole.Teacher, "http-other")]
    [InlineData(ScopeRole.Student, "http-foreign")] [InlineData(ScopeRole.Teacher, "http-foreign")]
    public async Task Http_work_existing_foreign_resources_cannot_enter_list_or_detail(ScopeRole role, string course)
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "work", "work", clock); var seed = await HttpSeed(role, time); var version = await WorkspaceSeed(time);
        var own = await ProtectedWorkSeed(seed.Actor, role, version, time);
        Guid org; Guid foreignVersion = Guid.NewGuid();
        await using (var db = Open(owner))
        {
            org = await db.Set<CourseRegistration>().Where(c => c.CourseId == course).Select(c => c.OrganizationId).SingleAsync() ?? throw new InvalidOperationException();
            var publication = await db.ContentVersions.AsNoTracking().SingleAsync(v => v.Id == version);
            db.Add(new PublishedContentVersion { Id = foreignVersion, CourseId = course, VersionNumber = 1, SourceRevision = "synthetic-foreign-work",
                PublishedAt = time.At, ContentJson = publication.ContentJson }); await db.SaveChangesAsync();
        }
        await Sql(owner, $"INSERT INTO \"CourseGroups\" VALUES('{course}','g')");
        var foreignTeacher = (await IdentitySeed()).Actor;
        if (org == existingOrg) await ChangeRestricted(new(AuthorityKind.Membership, AuthorityOperation.Grant, org, foreignTeacher, 0, Role: ScopeRole.Teacher));
        var foreign = await ProtectedWorkSeed(foreignTeacher, ScopeRole.Teacher, foreignVersion, time, course: course, org: org);
        var token = await HttpIssue(seed.User, seed.Session, clock, jwt); await using var app = await HttpHost(clock, jwt, workReads: true);
        await HttpGet(app, token, HttpStatusCode.OK, WorkRoot(version) + "/" + own.Work.Id);
        var foreignRoot = WorkRoot(foreignVersion).Replace("http-own", course);
        await HttpGet(app, token, HttpStatusCode.Forbidden, foreignRoot);
        await HttpGet(app, token, HttpStatusCode.Forbidden, foreignRoot + "/" + foreign.Work.Id);
        await HttpGet(app, token, HttpStatusCode.Forbidden, WorkRoot(version) + "/" + foreign.Work.Id);
        Assert.DoesNotContain(foreign.Work.Id.ToString(), await HttpGet(app, token, HttpStatusCode.OK, WorkRoot(version)));
    }
    [Fact]
    public async Task Http_work_student_cannot_read_peer_in_same_group()
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "work", "work", clock); var seed = await HttpSeed(ScopeRole.Student, time); var version = await WorkspaceSeed(time);
        var own = await ProtectedWorkSeed(seed.Actor, ScopeRole.Student, version, time);
        var teacher = (await IdentitySeed()).Actor;
        await ChangeRestricted(new(AuthorityKind.Membership, AuthorityOperation.Grant, existingOrg, teacher, 0, Role: ScopeRole.Teacher));
        var peer = await ProtectedWorkSeed(teacher, ScopeRole.Teacher, version, time);
        var token = await HttpIssue(seed.User, seed.Session, clock, jwt); await using var app = await HttpHost(clock, jwt, workReads: true);
        await HttpGet(app, token, HttpStatusCode.OK, WorkRoot(version) + "/" + own.Work.Id);
        await HttpGet(app, token, HttpStatusCode.Forbidden, WorkRoot(version) + "/" + peer.Work.Id);
        Assert.DoesNotContain(peer.Work.Id.ToString(), await HttpGet(app, token, HttpStatusCode.OK, WorkRoot(version)));
    }

}
