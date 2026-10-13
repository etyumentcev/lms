using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Lms.Application;
using Lms.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Tests;

public sealed partial class IsolatedProcessingDeploymentTests
{
    private sealed class AfterContent(IWorkspaceContentQueries inner, Func<Task> after) : IWorkspaceContentQueries
    {
        public async Task<ContentRead> Read(TokenAuthority token, ContentSelection s, ApplicationDeadline deadline, CancellationToken ct)
        { var result = await inner.Read(token, s, deadline, ct); await after(); return result; }
        public Task<bool> CanReturn(TokenAuthority token, ContentRead result, CancellationToken ct) => inner.CanReturn(token, result, ct);
    }
    private async Task<Guid> WorkspaceSeed(HttpClock time)
    {
        var id = Guid.NewGuid();
        await using var db = Open(owner);
        db.Add(new PublishedContentVersion { Id = id, CourseId = "http-own", VersionNumber = 2, SourceRevision = "synthetic-workspace",
            PublishedAt = time.At, ContentJson = JsonSerializer.Serialize(new PublishedCourseContent("Real course", "Course description", [
                new("lesson", 1, "Real lesson", "Lesson summary", [new("problem", 1, "Real problem", "Solve $x+1=2$." )]),
                new("locked", 2, "HIDDEN LOCKED", "HIDDEN SUMMARY", [new("secret", 1, "HIDDEN PROBLEM", "HIDDEN STATEMENT")]),
                new("scheduled", 3, "HIDDEN SCHEDULED", null, []),
                new("other-lesson", 4, "Other lesson", null, [new("other-problem", 1, "Other problem", "Other statement")])
            ]), new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
        foreach (var lesson in new[] { "lesson", "locked", "scheduled", "other-lesson" })
            db.Add(new LessonAccess { CourseId = "http-own", CourseVersionId = id, LessonId = lesson,
                IsPublished = lesson != "locked", AvailableFrom = lesson == "scheduled" ? time.At.AddHours(1) : null });
        await db.SaveChangesAsync();
        return id;
    }
    private static string WorkspacePath(Guid version, int level) => level switch
    {
        0 => "/api/v1/courses/http-own",
        1 => $"/api/v1/courses/http-own/versions/{version}/lessons/lesson",
        _ => $"/api/v1/courses/http-own/versions/{version}/lessons/lesson/problems/problem"
    };
    [Theory]
    [InlineData(ScopeRole.Student)] [InlineData(ScopeRole.Teacher)]
    public async Task Http_workspace_navigation_real_content_and_delayed_revoke(ScopeRole role)
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "http", "http", clock); var seed = await HttpSeed(role, time);
        var version = await WorkspaceSeed(time); var token = await HttpIssue(seed.User, seed.Session, clock, jwt);
        Assert.NotNull(token); await using var app = await HttpHost(clock, jwt);
        using var courses = JsonDocument.Parse(await HttpGet(app, token, HttpStatusCode.OK));
        var id = Assert.Single(courses.RootElement.GetProperty("items").EnumerateArray()).GetProperty("course").GetProperty("id").GetString();
        var courseBody = await HttpGet(app, token, HttpStatusCode.OK, "/api/v1/courses/" + id);
        using var course = JsonDocument.Parse(courseBody);
        Assert.Equal(new[] { "id", "title", "description", "lessons" }, course.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Real course", course.RootElement.GetProperty("title").GetString());
        Assert.Equal(2, course.RootElement.GetProperty("lessons").GetArrayLength()); Assert.DoesNotContain("HIDDEN", courseBody);
        var lessonLink = course.RootElement.GetProperty("lessons")[0].GetProperty("href").GetString()!;
        Assert.Equal(WorkspacePath(version, 1), lessonLink);
        using var lesson = JsonDocument.Parse(await HttpGet(app, token, HttpStatusCode.OK, lessonLink));
        Assert.Equal(new[] { "courseId", "versionId", "id", "order", "title", "summary", "problems" }, lesson.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Lesson summary", lesson.RootElement.GetProperty("summary").GetString());
        var problemLink = Assert.Single(lesson.RootElement.GetProperty("problems").EnumerateArray()).GetProperty("href").GetString()!;
        using var problem = JsonDocument.Parse(await HttpGet(app, token, HttpStatusCode.OK, problemLink));
        Assert.Equal(new[] { "courseId", "versionId", "lessonId", "id", "order", "title", "statementMarkdown" }, problem.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Solve $x+1=2$.", problem.RootElement.GetProperty("statementMarkdown").GetString());
        await ChangeRestricted(seed.Grant with { Operation = AuthorityOperation.Revoke, ExpectedVersion = 1 });
        Assert.Null(await HttpIssue(seed.User, seed.Session, clock, jwt));
        for (var level = 0; level < 3; level++) await HttpGet(app, token, HttpStatusCode.OK, WorkspacePath(version, level));
        time.Elapsed = TimeSpan.FromMinutes(15);
        for (var level = 0; level < 3; level++) await HttpGet(app, token, HttpStatusCode.Unauthorized, WorkspacePath(version, level));
    }
    [Theory]
    [InlineData(ScopeRole.Student)] [InlineData(ScopeRole.Teacher)]
    public async Task Http_workspace_foreign_selectors_parent_substitution_and_locked_content(ScopeRole role)
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "http", "http", clock); var seed = await HttpSeed(role, time);
        var version = await WorkspaceSeed(time); var token = await HttpIssue(seed.User, seed.Session, clock, jwt);
        await using var app = await HttpHost(clock, jwt);
        for (var level = 0; level < 3; level++)
        {
            var path = WorkspacePath(version, level);
            await HttpGet(app, token, HttpStatusCode.OK, path);
            foreach (var course in new[] { "http-other", "http-foreign", "absent" })
                await HttpGet(app, token, HttpStatusCode.Forbidden, path.Replace("http-own", course));
            if (level == 0) continue;
            foreach (var replacement in new[] { Guid.NewGuid().ToString(), "invalid" })
                await HttpGet(app, token, HttpStatusCode.Forbidden, path.Replace(version.ToString(), replacement));
            foreach (var lesson in new[] { "absent", "locked", "scheduled" })
                await HttpGet(app, token, HttpStatusCode.Forbidden, path.Replace("/lessons/lesson", "/lessons/" + lesson));
        }
        await HttpGet(app, token, HttpStatusCode.Forbidden, WorkspacePath(version, 2).Replace("/problems/problem", "/problems/other-problem"));
        await HttpGet(app, token, HttpStatusCode.Forbidden, WorkspacePath(version, 2).Replace("/lessons/lesson", "/lessons/other-lesson"));
        var original = await HttpGet(app, token, HttpStatusCode.OK, WorkspacePath(version, 2));
        Assert.Equal(original, await HttpGet(app, token, HttpStatusCode.OK, WorkspacePath(version, 2) + "?groupId=h&studentId=forged&role=Teacher"));
        // No legacy work/assignment/file endpoints are registered in this host.
        foreach (var path in new[] { "/api/v1/problem-assignments/forged/workspace", "/api/v1/problem-works/forged", "/api/v1/attachments/forged/content" })
            await HttpGet(app, token, HttpStatusCode.NotFound, path);
    }
    public static IEnumerable<object[]> WorkspaceFailures()
    {
        foreach (var level in new[] { 0, 1, 2 })
            foreach (var late in new[] { false, true })
                foreach (var fault in new[] { "subject", "context-wide", "closed", "suspended", "unknown", "expiry", "deadline" })
                    yield return new object[] { level, late, fault };
    }
    [Theory] [MemberData(nameof(WorkspaceFailures))]
    public async Task Http_workspace_fresh_processing_time_and_original_deadline(int level, bool late, string fault)
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "http", "http", clock); var seed = await HttpSeed(ScopeRole.Student, time);
        var version = await WorkspaceSeed(time); var token = await HttpIssue(seed.User, seed.Session, clock, jwt);
        async Task Mutate()
        {
            if (fault == "unknown") { time.Current = false; return; }
            if (fault == "expiry") { time.Elapsed = TimeSpan.FromMinutes(15); return; }
            if (fault == "deadline") { time.Elapsed += TimeSpan.FromSeconds(60); return; }
            await using var db = Open(processing);
            await new PostgresProcessingStateAdministration(db).Change(new(existingOrg, Guid.NewGuid(), 2, fault switch
            {
                "subject" => new AddProcessingRestriction(Guid.NewGuid(), seed.Context, 1, seed.Actor, 1),
                "context-wide" => new AddProcessingRestriction(Guid.NewGuid(), seed.Context, 1, null, 1),
                "suspended" => new SetProcessingOrganizationState("Suspended"),
                _ => new CloseProcessingBinding("http-own", time.At.AddMinutes(-30))
            }));
            if (fault == "closed") time.Elapsed = TimeSpan.FromSeconds(5);
        }
        await using (var positive = await HttpHost(clock, jwt)) await HttpGet(positive, token, HttpStatusCode.OK, WorkspacePath(version, level));
        if (!late && fault != "deadline") await Mutate();
        await using var app = await HttpHost(clock, jwt, ingress: !late && fault == "deadline" ? _ => time.Elapsed += TimeSpan.FromSeconds(60) : null,
            afterContent: late ? Mutate : null);
        var expected = fault == "deadline" ? HttpStatusCode.ServiceUnavailable : !late && fault is "expiry" or "unknown" ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        await HttpGet(app, token, expected, WorkspacePath(version, level));
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Http_workspace_missing_processing_and_late_publication_withdrawal(int level)
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "http", "http", clock); var seed = await HttpSeed(ScopeRole.Teacher, time);
        var version = await WorkspaceSeed(time); var token = await HttpIssue(seed.User, seed.Session, clock, jwt);
        await using var app = await HttpHost(clock, jwt, afterContent: async () =>
        {
            await using var db = Open(owner);
            await db.Lessons.Where(l => l.CourseId == "http-own" && l.LessonId == "lesson").ExecuteUpdateAsync(s => s.SetProperty(l => l.IsPublished, false));
        });
        await HttpGet(app, token, HttpStatusCode.Forbidden, WorkspacePath(version, level));
        // A genuinely unbound course; never delete immutable processing history.
        await ChangeRestricted(seed.Grant with { CourseId = "http-other", GroupId = null, GrantId = Guid.NewGuid() });
        var unboundVersion = Guid.NewGuid();
        await using (var db = Open(owner))
        {
            var published = await db.ContentVersions.AsNoTracking().SingleAsync(v => v.Id == version);
            db.Add(new PublishedContentVersion { Id = unboundVersion, CourseId = "http-other", VersionNumber = 1,
                PublishedAt = time.At, SourceRevision = "synthetic-unbound", ContentJson = published.ContentJson });
            db.Add(new LessonAccess { CourseId = "http-other", CourseVersionId = unboundVersion, LessonId = "lesson", IsPublished = true });
            await db.SaveChangesAsync();
        }
        var unboundToken = await HttpIssue(seed.User, seed.Session, clock, jwt);
        Assert.NotNull(unboundToken);
        Assert.Contains("http-other", jwt.Validate(unboundToken)!.Authority.Grants.Select(g => g.CourseId));
        await using var missing = await HttpHost(clock, jwt);
        var unboundPath = WorkspacePath(unboundVersion, level).Replace("http-own", "http-other");
        await HttpGet(missing, unboundToken, HttpStatusCode.Forbidden, unboundPath);
        await using (var db = Open(processing))
            await new PostgresProcessingStateAdministration(db).Change(new(existingOrg, Guid.NewGuid(), 2,
                new OpenProcessingBinding("http-other", seed.Context, 1, time.At.AddMinutes(-30))));
        await HttpGet(missing, unboundToken, HttpStatusCode.OK, unboundPath);
    }
    [Theory] [InlineData(ScopeRole.Student)] [InlineData(ScopeRole.Teacher)]
    public async Task Http_workspace_never_reads_private_tables_and_invalid_group_cannot_issue(ScopeRole role)
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "http", "http", clock); var seed = await HttpSeed(role, time);
        var version = await WorkspaceSeed(time); var token = await HttpIssue(seed.User, seed.Session, clock, jwt);
        var identity = await IdentitySeed();
        await ChangeRestricted(new(AuthorityKind.Membership, AuthorityOperation.Grant, existingOrg, identity.Actor, 0, Role: role));
        await Assert.ThrowsAsync<UseCaseException>(() => ChangeRestricted(seed.Grant with
            { ActorId = identity.Actor, GroupId = "absent", GrantId = role == ScopeRole.Teacher ? Guid.NewGuid() : null }));
        SessionResult login;
        await using (var db = Open(runtime)) login = await new PostgresIdentitySessions(db, new HttpTimeProvider(time), true).Authenticate(identity.Name, identity.Password);
        var denied = await HttpIssue(identity.User, login.Identity!.SessionId, clock, jwt);
        Assert.Null(denied);
        await Sql(owner, "REVOKE SELECT ON \"ProblemAssignments\",\"ProblemWorks\",\"Submissions\",\"Reviews\",\"Messages\",\"StoredAttachments\" FROM c2h_runtime");
        await using var app = await HttpHost(clock, jwt);
        for (var level = 0; level < 3; level++)
        {
            await HttpGet(app, token, HttpStatusCode.OK, WorkspacePath(version, level));
            await HttpGet(app, denied, HttpStatusCode.Unauthorized, WorkspacePath(version, level));
        }
    }

    [Fact]
    public async Task Http_workspace_each_navigation_request_has_fresh_original_deadline()
    {
        var time = new HttpClock(); var clock = time.Create(); using var key = RSA.Create(2048);
        var jwt = new ComponentJwt(key, "http", "http", clock); var seed = await HttpSeed(ScopeRole.Student, time);
        var version = await WorkspaceSeed(time); var token = await HttpIssue(seed.User, seed.Session, clock, jwt);
        var deadlines = new List<ApplicationDeadline>();
        await using var app = await HttpHost(clock, jwt,
            ingress: ctx => deadlines.Add(ctx.Features.Get<ApplicationDeadline>()!),
            afterContent: () => { time.Elapsed += TimeSpan.FromSeconds(59); return Task.CompletedTask; });
        for (var level = 0; level < 3; level++) await HttpGet(app, token, HttpStatusCode.OK, WorkspacePath(version, level));
        Assert.Equal(3, deadlines.Distinct().Count());
        Assert.False(deadlines[0].Check(ApplicationAction.ResponseEnqueue).Allowed);
        Assert.True(deadlines[2].Check(ApplicationAction.ResponseEnqueue).Allowed);
    }

}
