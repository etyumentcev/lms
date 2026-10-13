using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Lms.Application;
using Lms.Domain;
using Lms.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Tests;

// These tests inspect the HTTP wire contract, not serialization of the same DTO on both sides.
public sealed partial class ContractApiTests(ApiPostgresFixture postgres) : IClassFixture<ApiPostgresFixture>
{
    private static string Workspace(Guid id) => $"/api/v1/problem-assignments/{id}/workspace";
    private static string SubmitPath(Guid id) => $"/api/v1/problem-assignments/{id}/submissions";
    private static string WorkPath(Guid id) => $"/api/v1/problem-works/{id}";
    private static string CoursePath(ProblemAssignment a) => $"/api/v1/courses/{a.CourseId}/workspace";
    private static string LessonPath(ProblemAssignment a) => $"/api/v1/courses/{a.CourseId}/lessons/{a.Content.LessonId}/workspace";
    private const string QueuePath = "/api/v1/teacher/review-queue";
    private static object Solution(long? version = null, string body = "Solution")
        => new { bodyMarkdown = body, attachmentIds = Array.Empty<Guid>(), expectedVersion = version };
    private static object ReviewBody(JsonElement work, string? feedback = "Explain the proof")
        => new { commandId = Guid.NewGuid(), submissionId = work.GetProperty("currentSubmission").GetProperty("id").GetGuid(),
            expectedVersion = work.GetProperty("version").GetInt64(), feedback };
    private static object MessageBody => new { bodyMarkdown = "Discussion", attachmentIds = Array.Empty<Guid>() };
    private static object TeacherMessageBody => new { commandId = Guid.NewGuid(), intentId = Guid.NewGuid(),
        body = "Discussion", replyTo = (Guid?)null, attachments = Array.Empty<Guid>() };
    private static async Task<JsonElement> Json(HttpResponseMessage response, int status = 200)
    {
        using (response)
        {
            Assert.True((int)response.StatusCode == status, await response.Content.ReadAsStringAsync());
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
    }
    private static async Task<JsonElement> Get(HttpClient client, string path) => await Json(await client.GetAsync(path));
    private static void Fields(JsonElement value, params string[] fields)
        => Assert.Equal(fields.OrderBy(x => x), value.EnumerateObject().Select(p => p.Name).OrderBy(x => x));
    private static void PermissionFields(JsonElement value)
        => Fields(value, "canSubmit", "canPostMessage", "canAccept", "canRequestRevision", "canViewAsTeacher");
    private static void SubmissionFields(JsonElement value)
    {
        Fields(value, "id", "attemptNumber", "bodyMarkdown", "submittedAt", "attachmentIds");
        Assert.Equal(TimeSpan.Zero, value.GetProperty("submittedAt").GetDateTimeOffset().Offset);
    }
    private static async Task Error(HttpResponseMessage response, int status, string code)
    {
        using (response)
        {
            Assert.Equal(status, (int)response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var value = await response.Content.ReadFromJsonAsync<JsonElement>();
            Fields(value, "type", "title", "status", "instance", "code", "traceId");
            Assert.Equal(status, value.GetProperty("status").GetInt32());
            Assert.Equal(code, value.GetProperty("code").GetString());
            Assert.Equal("urn:lms:error:" + code, value.GetProperty("type").GetString());
            Assert.Equal(response.RequestMessage!.RequestUri!.AbsolutePath, value.GetProperty("instance").GetString());
            Assert.False(string.IsNullOrWhiteSpace(value.GetProperty("traceId").GetString()));
        }
    }
    private static async Task Publish(ApiFactory app, ProblemAssignment a, string title = "Contract course", int number = 1, string statement = "Original $x$ statement")
    {
        await using var db = app.Open();
        db.ContentVersions.Add(new PublishedContentVersion { Id = a.Content.CourseVersionId, CourseId = a.CourseId,
            VersionNumber = number, SourceRevision = "c4-fixture", PublishedAt = DateTimeOffset.UtcNow,
            ContentJson = JsonSerializer.Serialize(new PublishedCourseContent(title, null,
                [new(a.Content.LessonId, 1, "Lesson", null, [new(a.Content.ProblemId, 1, "Problem", statement)])]), ApiFactory.Json) });
        await db.SaveChangesAsync();
    }
    private static async Task<Guid> Submit(HttpClient student, ProblemAssignment a)
        => (await Json(await student.PostAsJsonAsync(SubmitPath(a.Id), Solution()), 201)).GetProperty("problemWorkId").GetGuid();

    [Theory]
    [InlineData("submit")]
    [InlineData("message")]
    [InlineData("revision")]
    [InlineData("accept")]
    [InlineData("work")]
    [InlineData("queue")]
    [InlineData("course")]
    [InlineData("lesson")]
    [InlineData("assignment")]
    public async Task Every_endpoint_requires_authentication_with_problem_details(string endpoint)
    {
        await using var app = await postgres.CreateApp();
        using var client = app.Client();
        var id = Guid.NewGuid();
        var (method, path) = endpoint switch
        {
            "submit" => ("POST", SubmitPath(id)),
            "message" => ("POST", WorkPath(id) + "/messages"),
            "revision" => ("POST", WorkPath(id) + "/reviews/needs-revision"),
            "accept" => ("POST", WorkPath(id) + "/reviews/accept"),
            "work" => ("GET", WorkPath(id)),
            "queue" => ("GET", QueuePath),
            "course" => ("GET", "/api/v1/courses/course/workspace"),
            "lesson" => ("GET", "/api/v1/courses/course/lessons/lesson/workspace"),
            _ => ("GET", Workspace(id))
        };
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST") request.Content = JsonContent.Create(new { });
        await Error(await client.SendAsync(request), 401, "Unauthenticated");
    }

    [Theory]
    [InlineData("submissions")]
    [InlineData("messages")]
    [InlineData("reviews/needs-revision")]
    [InlineData("reviews/accept")]
    public async Task Every_command_rejects_invalid_body_and_missing_resource(string suffix)
    {
        await using var app = await postgres.CreateApp();
        var submit = suffix == "submissions";
        using var client = submit || suffix == "messages" ? app.Client(ApiFactory.Student) : await app.SecureClient();
        var route = submit ? SubmitPath(Guid.NewGuid()) : WorkPath(Guid.NewGuid()) + "/" + suffix;
        foreach (var body in new[] { "{", "{}", "{\"actorId\":\"forged\"}" })
            await Error(await client.PostAsync(route, new StringContent(body, Encoding.UTF8, "application/json")), 400, "ValidationFailed");
        object valid = submit ? Solution() : suffix == "messages" ? MessageBody
            : new { commandId = Guid.NewGuid(), submissionId = Guid.NewGuid(), feedback = "Feedback", expectedVersion = 1 };
        await Error(await client.PostAsJsonAsync(route, valid), submit || suffix == "messages" ? 404 : 403,
            submit || suffix == "messages" ? "NotFound" : "Forbidden");
    }

    [Theory]
    [InlineData("stranger")]
    [InlineData("inactive-enrollment")]
    [InlineData("withdrawn")]
    [InlineData("unpublished")]
    [InlineData("scheduled")]
    public async Task Student_read_and_command_access_remain_consistent_after_access_changes(string scenario)
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed(studentGroup: "A", scopedTeacher: true);
        await Publish(app, a);
        using var owner = app.Client(ApiFactory.Student);
        var id = await Submit(owner, a);
        await using (var db = app.Open())
        {
            if (scenario == "inactive-enrollment")
            {
                var enrollment = await db.Set<EnrollmentScope>().AsNoTracking().SingleAsync(x => x.StudentId == ApiFactory.Student && x.CourseId == a.CourseId);
                await new PostgresScopedAuthorityAdministration(db).Change(new(AuthorityKind.Enrollment,
                    AuthorityOperation.Revoke, app.OrganizationId, ApiFactory.Student, enrollment.Version,
                    enrollment.MembershipGeneration, a.CourseId, enrollment.GroupId));
            }
            if (scenario == "withdrawn") (await db.Assignments.SingleAsync()).Withdraw();
            if (scenario == "unpublished") (await db.Lessons.SingleAsync()).IsPublished = false;
            if (scenario == "scheduled") (await db.Lessons.SingleAsync()).AvailableFrom = DateTimeOffset.UtcNow.AddDays(1);
            await db.SaveChangesAsync();
        }
        using var student = app.Client(scenario == "stranger" ? Guid.NewGuid() : ApiFactory.Student);
        await Error(await student.GetAsync(Workspace(a.Id)), 403, "Forbidden");
        await Error(await student.GetAsync(WorkPath(id)), 403, "Forbidden");
        await Error(await student.PostAsJsonAsync(SubmitPath(a.Id), Solution(1)), 403, "Forbidden");
        await Error(await student.PostAsJsonAsync(WorkPath(id) + "/messages", MessageBody), 403, "Forbidden");
        if (scenario is "stranger" or "inactive-enrollment")
        {
            await Error(await student.GetAsync(CoursePath(a)), 403, "Forbidden");
            await Error(await student.GetAsync(LessonPath(a)), 403, "Forbidden");
        }
        else if (scenario is "unpublished" or "scheduled")
            await Error(await student.GetAsync(LessonPath(a)), 403, "Forbidden");
        var work = await app.Reload(id);
        Assert.Equal(1, work.Version);
        Assert.Empty(work.Messages);
        Assert.Single(work.Submissions);
    }

    [Fact]
    public async Task Wrong_roles_cannot_call_student_only_or_teacher_only_endpoints()
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed();
        await Publish(app, a);
        using var student = app.Client(ApiFactory.Student);
        using var teacher = await app.SecureClient();
        using var scopedStudent = await app.SecureClient("Student");
        var id = await Submit(student, a);
        var work = await Get(teacher, WorkPath(id));
        await Error(await teacher.GetAsync(Workspace(a.Id)), 403, "Forbidden");
        await Error(await teacher.PostAsJsonAsync(SubmitPath(a.Id), Solution()), 403, "Forbidden");
        await Error(await student.GetAsync(QueuePath), 403, "Forbidden");
        foreach (var action in new[] { "accept", "needs-revision" })
            await Error(await scopedStudent.PostAsJsonAsync(WorkPath(id) + "/reviews/" + action, ReviewBody(work)), 403, "Forbidden");
        Assert.Equal(1, (await app.Reload(id)).Version);
    }

    [Theory]
    [InlineData("missing-grant")]
    [InlineData("wrong-group")]
    [InlineData("revoked-grant")]
    public async Task Teacher_reads_and_both_reviews_share_the_same_resource_boundary(string scenario)
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed(teacherGrant: scenario != "missing-grant", studentGroup: "A",
            teacherGroup: scenario == "wrong-group" ? "B" : "A", scopedTeacher: true);
        await Publish(app, a);
        using var student = app.Client(ApiFactory.Student);
        var id = await Submit(student, a);
        var submissionId = (await app.Reload(id)).Submissions.Single().Id;
        object Body() => new { commandId = Guid.NewGuid(), submissionId, expectedVersion = 1, feedback = "Feedback" };
        if (scenario == "revoked-grant")
        {
            TeacherAssignmentScope grant;
            await using (var db = app.Open())
                grant = await db.Set<TeacherAssignmentScope>().SingleAsync(x => x.CourseId == a.CourseId && x.TeacherId == ApiFactory.Teacher);
            await using var administration = app.Open();
            await new PostgresScopedAuthorityAdministration(administration).Change(new(AuthorityKind.Teacher,
                AuthorityOperation.Revoke, app.OrganizationId, ApiFactory.Teacher, grant.Version,
                grant.MembershipGeneration, a.CourseId, grant.GroupId, grant.GrantId));
        }
        using var teacher = await app.SecureClient();
        await Error(await teacher.GetAsync(WorkPath(id)), 403, "Forbidden");
        foreach (var action in new[] { "accept", "needs-revision" })
            await Error(await teacher.PostAsJsonAsync(WorkPath(id) + "/reviews/" + action, Body()), 403, "Forbidden");
        await Error(await teacher.PostAsJsonAsync(WorkPath(id) + "/messages", TeacherMessageBody), 403, "Forbidden");
        var queue = await Get(teacher, QueuePath);
        Assert.Empty(queue.GetProperty("items").EnumerateArray());
        Assert.Equal(0, queue.GetProperty("summary").GetProperty("total").GetInt32());
        if (scenario is "missing-grant" or "revoked-grant")
        {
            await Error(await teacher.GetAsync(CoursePath(a)), 403, "Forbidden");
            await Error(await teacher.GetAsync(LessonPath(a)), 403, "Forbidden");
        }
        else
        {
            Assert.Empty((await Get(teacher, LessonPath(a))).GetProperty("problems").EnumerateArray());
            var course = await Get(teacher, CoursePath(a));
            Assert.Equal(JsonValueKind.Null, course.GetProperty("enrollment").ValueKind);
            Assert.Equal(0, course.GetProperty("progress").GetProperty("completedLessons").GetInt32());
        }
        Assert.Equal(1, (await app.Reload(id)).Version);
    }

    [Fact]
    public async Task Read_errors_use_codes_including_unavailable_historical_content()
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed();
        using var student = app.Client(ApiFactory.Student);
        using var teacher = app.Client(ApiFactory.Teacher, "Teacher");
        await Error(await student.GetAsync(Workspace(a.Id)), 503, "ContentUnavailable");
        var id = await Submit(student, a);
        await Error(await teacher.GetAsync(WorkPath(id)), 503, "ContentUnavailable");
        await Error(await student.GetAsync(CoursePath(a)), 503, "ContentUnavailable");
        await Error(await student.GetAsync(LessonPath(a)), 503, "ContentUnavailable");
        await Error(await student.GetAsync(Workspace(Guid.NewGuid())), 404, "NotFound");
        await Error(await student.GetAsync(WorkPath(Guid.NewGuid())), 404, "NotFound");
        await Error(await student.GetAsync($"/api/v1/courses/{a.CourseId}/lessons/absent/workspace"), 404, "NotFound");
        await Error(await student.GetAsync("/api/v1/problem-works/not-a-uuid"), 400, "ValidationFailed");
        await Error(await teacher.GetAsync(QueuePath + "?page=0"), 400, "ValidationFailed");
        await Error(await teacher.GetAsync(QueuePath + "?studentId=bad"), 400, "ValidationFailed");
        await Error(await teacher.GetAsync(QueuePath + "?search=" + new string('x', 201)), 400, "ValidationFailed");
    }
}
