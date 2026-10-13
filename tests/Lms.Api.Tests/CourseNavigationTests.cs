using System.Net.Http.Json;
using Lms.Application;
using Lms.Domain;
using Lms.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Tests;

// Existing legacy HTTP compatibility only. Does not certify protected S4 HTTP.
public sealed partial class ContractApiTests
{
    [Fact]
    public async Task Navigation_teacher_capability_survives_terminal_state_and_absent_work_never_has_work_id()
    {
        await using var app = await postgres.CreateApp(); var a = await app.Seed(studentGroup: "A", scopedTeacher: true); await Publish(app, a);
        using var teacher = await app.SecureClient(); using var student = app.Client(ApiFactory.Student);
        var before = (await Get(teacher, LessonPath(a))).GetProperty("problems")[0];
        Assert.True(before.GetProperty("permissions").GetProperty("canViewAsTeacher").GetBoolean());
        Assert.False(before.GetProperty("permissions").GetProperty("canAccept").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, before.GetProperty("work").GetProperty("problemWorkId").ValueKind);
        Assert.False((await Get(student, LessonPath(a))).GetProperty("problems")[0].GetProperty("permissions").GetProperty("canViewAsTeacher").GetBoolean());
        var submitted = await Json(await student.PostAsJsonAsync(SubmitPath(a.Id), new { bodyMarkdown = "Synthetic solution", attachmentIds = Array.Empty<Guid>() }), 201);
        var workId = submitted.GetProperty("problemWorkId").GetGuid();
        var path = $"/api/v1/teacher/problem-works/{workId}";
        var work = await Get(teacher, path);
        Assert.True(work.GetProperty("permissions").GetProperty("canViewAsTeacher").GetBoolean());
        await Json(await teacher.PostAsJsonAsync(WorkPath(workId) + "/reviews/accept", ReviewBody(work)));
        var accepted = (await Get(teacher, LessonPath(a))).GetProperty("problems")[0];
        Assert.Equal("Accepted", accepted.GetProperty("work").GetProperty("status").GetString());
        Assert.True(accepted.GetProperty("permissions").GetProperty("canViewAsTeacher").GetBoolean());
        Assert.False(accepted.GetProperty("permissions").GetProperty("canAccept").GetBoolean());
        Assert.Single((await Get(teacher, path)).GetProperty("submissions").EnumerateArray());
        using var outsider = app.Client(Guid.NewGuid(), "Teacher");
        await Error(await outsider.GetAsync(LessonPath(a)), 403, "Forbidden");
        await Error(await outsider.GetAsync(path), 403, "Forbidden");
        await Error(await student.GetAsync(path), 403, "Forbidden");
        TeacherAssignmentScope scope;
        await using (var db = app.Open())
            scope = await db.Set<TeacherAssignmentScope>().SingleAsync(x => x.CourseId == a.CourseId);
        await using (var administration = app.Open())
            await new PostgresScopedAuthorityAdministration(administration).Change(new(AuthorityKind.Teacher,
                AuthorityOperation.Revoke, app.OrganizationId, ApiFactory.Teacher, scope.Version,
                scope.MembershipGeneration, a.CourseId, scope.GroupId, scope.GrantId));
        // Reads recheck current scoped authority and may revoke earlier than the <=15 minute
        // JWT ceiling. Fresh issue also excludes the grant and cannot admit another operation.
        await Error(await teacher.GetAsync(path), 403, "Forbidden");
        using var fresh = await app.SecureClient();
        await Error(await fresh.PostAsJsonAsync(WorkPath(workId) + "/messages", TeacherMessageBody), 403, "Forbidden");
    }
}
