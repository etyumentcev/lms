using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Application;
using Lms.Domain;
using Lms.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Tests;

public sealed class ProgressHistoryHttpTests(ApiPostgresFixture fixture) : IClassFixture<ApiPostgresFixture>
{
    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(expected, response.StatusCode);
        if (response.IsSuccessStatusCode)
        {
            if (expected == HttpStatusCode.OK) Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            return JsonDocument.Parse(text).RootElement.Clone();
        }
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task Publish(ApiFactory app, ProblemAssignment assignment, int version, string suffix)
    {
        await using var db = app.Open();
        db.ContentVersions.Add(new PublishedContentVersion
        {
            Id = assignment.Content.CourseVersionId, CourseId = assignment.CourseId,
            VersionNumber = version, SourceRevision = "m1-10-" + suffix, PublishedAt = DateTimeOffset.UtcNow,
            ContentJson = JsonSerializer.Serialize(new PublishedCourseContent("M1-10 course", "Synthetic only", [
                new(assignment.Content.LessonId, 1, "История и прогресс", null,
                    [new(assignment.Content.ProblemId, 1, "Задача " + suffix, "Условие " + suffix)])
            ]), ApiFactory.Json)
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Progress_history_move_exact_versions_counts_roles_groups_and_processing_are_server_enforced()
    {
        await using var app = await fixture.CreateApp(organizationBootstrap: true);
        var first = await app.Seed(scopedTeacher: true, teacherGroup: "G1", studentGroup: "G1");
        await Publish(app, first, 1, "v1");
        await using (var db = app.Open())
        {
            db.AddRange(
                new CourseGroup { CourseId = first.CourseId, GroupId = "G2" },
                new CourseCatalogRecord { CourseId = first.CourseId, OrganizationId = app.OrganizationId,
                    Title = "M1-10 course", Description = "Synthetic only", State = "Published", Version = 1,
                    CreatedAt = DateTimeOffset.UtcNow },
                new CourseGroupCatalogRecord { CourseId = first.CourseId, GroupId = "G1", Name = "Первая группа",
                    Description = "", Version = 1, CreatedAt = DateTimeOffset.UtcNow },
                new CourseGroupCatalogRecord { CourseId = first.CourseId, GroupId = "G2", Name = "Вторая группа",
                    Description = "", Version = 1, CreatedAt = DateTimeOffset.UtcNow },
                new OrganizationParticipantProfile { OrganizationId = app.OrganizationId, ActorId = ApiFactory.Student,
                    ExternalRosterKey = "m1-10-student", DisplayName = "Ученик M1-10", CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        using var student = await app.SecureClient("Student");
        using var teacher = await app.SecureClient();
        var initial = await Json(await student.GetAsync($"/api/v1/student/progress-history?courseId={first.CourseId}"));
        Assert.Equal(1, initial.GetProperty("summary").GetProperty("notStarted").GetInt32());
        Assert.Equal(first.Content.CourseVersionId,
            initial.GetProperty("items")[0].GetProperty("courseVersionId").GetGuid());

        var submitted = await Json(await student.PostAsJsonAsync(
            $"/api/v1/problem-assignments/{first.Id}/submissions",
            new { bodyMarkdown = "Синтетическое решение", attachmentIds = Array.Empty<Guid>(), expectedVersion = (long?)null }),
            HttpStatusCode.Created);
        var workId = submitted.GetProperty("problemWorkId").GetGuid();
        var submissionId = submitted.GetProperty("submissionId").GetGuid();
        var version = submitted.GetProperty("version").GetInt64();

        var group = await Json(await teacher.GetAsync(
            $"/api/v1/teacher/group-progress?courseId={first.CourseId}&groupId=G1&pageSize=1"));
        Assert.Equal(1, group.GetProperty("summary").GetProperty("studentCount").GetInt32());
        Assert.Equal(1, group.GetProperty("summary").GetProperty("assignedCount").GetInt32());
        Assert.Equal(1, group.GetProperty("summary").GetProperty("submitted").GetInt32());
        Assert.Equal("Submitted", group.GetProperty("items")[0].GetProperty("assignments")[0]
            .GetProperty("status").GetString());
        Assert.False(group.GetProperty("items")[0].GetProperty("assignments")[0]
            .TryGetProperty("lastSubmittedAt", out _));

        await Json(await teacher.PostAsJsonAsync($"/api/v1/problem-works/{workId}/reviews/accept",
            new { commandId = Guid.NewGuid(), submissionId, expectedVersion = version, feedback = "Принято" }));
        var accepted = await Json(await teacher.GetAsync(
            $"/api/v1/teacher/group-progress?courseId={first.CourseId}&groupId=G1&status=Accepted"));
        Assert.Equal(1, accepted.GetProperty("summary").GetProperty("accepted").GetInt32());
        Assert.Equal(1, accepted.GetProperty("total").GetInt32());
        var history = await Json(await teacher.GetAsync(
            $"/api/v1/teacher/review-history?courseId={first.CourseId}&groupId=G1&decision=Accepted"));
        Assert.Equal(1, history.GetProperty("summary").GetProperty("accepted").GetInt32());
        Assert.Equal(workId, history.GetProperty("items")[0].GetProperty("problemWorkId").GetGuid());
        Assert.Equal(first.Content.CourseVersionId,
            history.GetProperty("items")[0].GetProperty("courseVersionId").GetGuid());

        await using (var db = app.Open())
        {
            var admin = new PostgresScopedAuthorityAdministration(db);
            await admin.Change(new(AuthorityKind.Enrollment, AuthorityOperation.Move, app.OrganizationId,
                ApiFactory.Student, 1, 1, first.CourseId, "G2"));
        }
        var second = new ProblemAssignment(Guid.NewGuid(), ApiFactory.Student, first.CourseId,
            new(Guid.NewGuid(), first.Content.LessonId, "problem-v2"));
        await using (var db = app.Open())
        {
            db.Add(second);
            db.Add(new LessonAccess { CourseId = second.CourseId, CourseVersionId = second.Content.CourseVersionId,
                LessonId = second.Content.LessonId, IsPublished = true });
            await db.SaveChangesAsync();
        }
        await Publish(app, second, 2, "v2");

        using var movedStudent = await app.SecureClient("Student");
        var moved = await Json(await movedStudent.GetAsync(
            $"/api/v1/student/progress-history?courseId={first.CourseId}"));
        Assert.Equal(2, moved.GetProperty("summary").GetProperty("assignedCount").GetInt32());
        var versions = moved.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("courseVersionId").GetGuid()).ToHashSet();
        Assert.Contains(first.Content.CourseVersionId, versions);
        Assert.Contains(second.Content.CourseVersionId, versions);
        var original = moved.GetProperty("items").EnumerateArray().Single(x =>
            x.GetProperty("assignmentId").GetGuid() == first.Id);
        Assert.Equal("Accepted", original.GetProperty("status").GetString());
        var recentPath = $"/api/v1/student/progress-history?courseId={first.CourseId}&sort=lastSubmissionDesc";
        var beforeSecondSubmission = await Json(await movedStudent.GetAsync($"{recentPath}&page=1&pageSize=1"));
        Assert.Equal(1, beforeSecondSubmission.GetProperty("total").GetInt32());
        Assert.Equal(first.Id, beforeSecondSubmission.GetProperty("items")[0].GetProperty("assignmentId").GetGuid());
        Assert.Equal(JsonValueKind.String,
            beforeSecondSubmission.GetProperty("items")[0].GetProperty("lastSubmittedAt").ValueKind);
        Assert.False(beforeSecondSubmission.GetProperty("items")[0].GetProperty("lastSubmittedAt").GetString() ==
            beforeSecondSubmission.GetProperty("items")[0].GetProperty("lastActivityAt").GetString());

        await Json(await movedStudent.PostAsJsonAsync(
            $"/api/v1/problem-assignments/{second.Id}/submissions",
            new { bodyMarkdown = "Второе синтетическое решение", attachmentIds = Array.Empty<Guid>(),
                expectedVersion = (long?)null }), HttpStatusCode.Created);
        var newest = await Json(await movedStudent.GetAsync($"{recentPath}&page=1&pageSize=1"));
        Assert.Equal(2, newest.GetProperty("total").GetInt32());
        Assert.Equal(1, newest.GetProperty("items").GetArrayLength());
        Assert.Equal(second.Id, newest.GetProperty("items")[0].GetProperty("assignmentId").GetGuid());
        Assert.Equal("Submitted", newest.GetProperty("items")[0].GetProperty("status").GetString());
        var older = await Json(await movedStudent.GetAsync($"{recentPath}&page=2&pageSize=1"));
        Assert.Equal(first.Id, older.GetProperty("items")[0].GetProperty("assignmentId").GetGuid());
        Assert.Equal("Accepted", older.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Equal(2, older.GetProperty("total").GetInt32());

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(recentPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await movedStudent.GetAsync(
            $"{recentPath}&groupId=G1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await movedStudent.GetAsync(
            $"{recentPath}&studentId={Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await movedStudent.GetAsync(
            "/api/v1/student/progress-history?courseId=forged-course&sort=lastSubmissionDesc")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await movedStudent.GetAsync(
            $"/api/v1/student/progress-history?courseId={first.CourseId}&sort=unapproved")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await movedStudent.GetAsync(
            "/api/v1/student/progress-history?sort=lastSubmissionDesc")).StatusCode);

        var oldGroup = await Json(await teacher.GetAsync(
            $"/api/v1/teacher/group-progress?courseId={first.CourseId}&groupId=G1"));
        Assert.Equal(1, oldGroup.GetProperty("summary").GetProperty("assignedCount").GetInt32());
        Assert.Equal(first.Id, oldGroup.GetProperty("items")[0].GetProperty("assignments")[0]
            .GetProperty("assignmentId").GetGuid());

        var wrongGroup = await Json(await teacher.GetAsync(
            $"/api/v1/teacher/group-progress?courseId={first.CourseId}&groupId=G2"), HttpStatusCode.Forbidden);
        Assert.Equal("Forbidden", wrongGroup.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync(
            $"/api/v1/teacher/group-progress?courseId={first.CourseId}&groupId=G1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(
            $"/api/v1/student/progress-history?courseId={first.CourseId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(
            "/api/v1/teacher/group-progress?courseId=forged-course")).StatusCode);

        var foreignOrganization = Guid.NewGuid();
        const string foreignCourse = "m1-10-foreign-course";
        await using (var db = app.Open())
        {
            db.AddRange(new Organization { OrganizationId = foreignOrganization, State = "Enabled", SyntheticOnly = true },
                new CourseRegistration { CourseId = foreignCourse, OrganizationId = foreignOrganization, MappingState = "Verified" },
                new CourseGroup { CourseId = foreignCourse, GroupId = "G1" });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(
            $"/api/v1/teacher/group-progress?courseId={foreignCourse}&groupId=G1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await movedStudent.GetAsync(
            $"/api/v1/student/progress-history?courseId={foreignCourse}&sort=lastSubmissionDesc")).StatusCode);

        Guid teacherGrantId;
        await using (var db = app.Open())
            teacherGrantId = await db.Set<TeacherAssignmentScope>().Where(x => x.CourseId == first.CourseId &&
                x.TeacherId == ApiFactory.Teacher).Select(x => x.GrantId).SingleAsync();
        await using (var db = app.Open())
            await new PostgresScopedAuthorityAdministration(db).Change(new(AuthorityKind.Teacher,
                AuthorityOperation.Revoke, app.OrganizationId, ApiFactory.Teacher, 1, 1,
                first.CourseId, "G1", teacherGrantId));
        Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync(
            $"/api/v1/teacher/group-progress?courseId={first.CourseId}&groupId=G1")).StatusCode);
        using var revokedTeacher = await app.SecureClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await revokedTeacher.GetAsync(
            $"/api/v1/teacher/group-progress?courseId={first.CourseId}&groupId=G1")).StatusCode);

        long restrictedRevision;
        await using (var db = app.Open())
            restrictedRevision = await new PostgresProcessingStateAdministration(db).Change(new(app.OrganizationId, Guid.NewGuid(), 1,
                new AddProcessingRestriction(Guid.NewGuid(), app.ProcessingContextId, 1,
                    ApiFactory.Teacher, 1)));
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(
            $"/api/v1/teacher/group-progress?courseId={first.CourseId}&groupId=G1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(
            $"/api/v1/teacher/review-history?courseId={first.CourseId}&groupId=G1")).StatusCode);
        await using (var db = app.Open())
            await new PostgresProcessingStateAdministration(db).Change(new(app.OrganizationId,
                Guid.NewGuid(), restrictedRevision, new AddProcessingRestriction(Guid.NewGuid(),
                    app.ProcessingContextId, 1, ApiFactory.Student, 1)));
        Assert.Equal(HttpStatusCode.Forbidden, (await movedStudent.GetAsync(recentPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync(recentPath)).StatusCode);
    }

    [Fact]
    public async Task Recent_submissions_deny_when_course_processing_binding_is_closed_even_if_empty()
    {
        await using var app = await fixture.CreateApp(organizationBootstrap: true);
        var assignment = await app.Seed(scopedTeacher: true, teacherGroup: "G1", studentGroup: "G1");
        await Publish(app, assignment, 1, "binding");
        using var student = await app.SecureClient("Student");
        var path = $"/api/v1/student/progress-history?courseId={assignment.CourseId}&sort=lastSubmissionDesc&pageSize=5";
        var before = await Json(await student.GetAsync(path));
        Assert.Equal(0, before.GetProperty("total").GetInt32());
        await using (var db = app.Open())
        {
            var bindingFrom = await db.Set<CourseContextBinding>().Where(x => x.CourseId == assignment.CourseId)
                .Select(x => x.EffectiveFrom).SingleAsync();
            await new PostgresProcessingStateAdministration(db).Change(new(app.OrganizationId,
                Guid.NewGuid(), 1, new CloseProcessingBinding(assignment.CourseId, bindingFrom)));
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync(path)).StatusCode);
    }

}
