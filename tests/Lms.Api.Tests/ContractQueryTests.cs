using System.Net.Http.Json;
using Lms.Domain;
using Lms.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Tests;

public sealed partial class ContractApiTests
{
    [Fact]
    public async Task Queue_search_and_attempt_filters_exclude_earlier_rows_before_pagination()
    {
        await using var app = await postgres.CreateApp();
        using var student = app.Client(ApiFactory.Student);
        using var teacher = app.Client(ApiFactory.Teacher, "Teacher");
        var expectedFirst = new List<Guid>();
        var expectedRevision = new List<Guid>();
        var reviewed = 0;
        // Earlier nonmatching rows make page-first filtering observably wrong.
        foreach (var title in new[] { "Other", "Needle", "Needle", "Needle", "Needle", "Needle", "Needle" })
        {
            var a = await app.Seed(studentGroup: "A", scopedTeacher: true);
            await Publish(app, a, title);
            var id = await Submit(student, a);
            if (title == "Other") continue;
            var work = await Get(teacher, WorkPath(id));
            if (expectedFirst.Count < 2) expectedFirst.Add(id);
            else if (expectedRevision.Count < 2)
            {
                using var command = await app.SecureClient();
                var revision = await Json(await command.PostAsJsonAsync(WorkPath(id) + "/reviews/needs-revision", ReviewBody(work)));
                await Json(await student.PostAsJsonAsync(SubmitPath(a.Id), Solution(revision.GetProperty("version").GetInt64())), 201);
                expectedRevision.Add(id);
            }
            else
            {
                // Both Accepted and NeedsRevision must be absent even though search matches.
                var decision = reviewed++ == 0 ? "accept" : "needs-revision";
                using var command = await app.SecureClient();
                await Json(await command.PostAsJsonAsync(WorkPath(id) + "/reviews/" + decision, ReviewBody(work)));
            }
        }
        // Explicitly verify the expected current submissions on both filtered pages.
        foreach (var (filter, expected, attempt) in new[] { ("first", expectedFirst, 1), ("revision", expectedRevision, 2) })
        {
            var seen = new List<Guid>();
            for (var page = 1; page <= 2; page++)
            {
                var queue = await Get(teacher, QueuePath + $"?search=nEeDlE&attemptKind={filter}&pageSize=1&page={page}");
                Assert.Equal(2, queue.GetProperty("total").GetInt32());
                var summary = queue.GetProperty("summary");
                Assert.Equal(2, summary.GetProperty("total").GetInt32());
                Assert.Equal(filter == "first" ? 2 : 0, summary.GetProperty("firstAttempts").GetInt32());
                Assert.Equal(filter == "revision" ? 2 : 0, summary.GetProperty("revisions").GetInt32());
                Assert.Equal(2, summary.GetProperty("byCourse").EnumerateArray().Sum(c => c.GetProperty("count").GetInt32()));
                var item = Assert.Single(queue.GetProperty("items").EnumerateArray());
                var id = item.GetProperty("problemWorkId").GetGuid();
                seen.Add(id);
                Assert.Equal(attempt, item.GetProperty("attemptNumber").GetInt32());
                var current = (await Get(teacher, WorkPath(id))).GetProperty("currentSubmission");
                Assert.Equal(current.GetProperty("id").GetGuid(), item.GetProperty("submissionId").GetGuid());
            }
            Assert.Equal(expected, seen);
        }
        var all = await Get(teacher, QueuePath + "?search=Needle&pageSize=1");
        Assert.Equal(4, all.GetProperty("total").GetInt32());
        Assert.Equal(4, all.GetProperty("summary").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Progress_counts_only_accepted_active_assignments_through_revision_and_withdrawal()
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed(studentGroup: "A", scopedTeacher: true);
        await Publish(app, a);
        var second = new ProblemAssignment(Guid.NewGuid(), a.StudentId, a.CourseId, a.Content);
        await using (var db = app.Open()) { db.Assignments.Add(second); await db.SaveChangesAsync(); }
        using var student = app.Client(ApiFactory.Student);
        using var teacher = await app.SecureClient();
        var firstId = await Submit(student, a);
        var first = await Get(teacher, WorkPath(firstId));
        await Json(await teacher.PostAsJsonAsync(WorkPath(firstId) + "/reviews/accept", ReviewBody(first, null)));
        var id = await Submit(student, second);
        var work = await Get(teacher, WorkPath(id));
        var submitted = await Get(student, LessonPath(a));
        Assert.Equal(50, submitted.GetProperty("progress").GetProperty("percent").GetDecimal());
        await Json(await teacher.PostAsJsonAsync(WorkPath(id) + "/reviews/needs-revision", ReviewBody(work)));
        var revision = await Get(student, LessonPath(a));
        Assert.Equal(submitted.GetProperty("progress").GetRawText(), revision.GetProperty("progress").GetRawText());
        Assert.Equal("InProgress", revision.GetProperty("progress").GetProperty("status").GetString());
        Assert.Equal(0, (await Get(student, CoursePath(a))).GetProperty("progress").GetProperty("percent").GetDecimal());
        await using (var db = app.Open())
        {
            (await db.Assignments.SingleAsync(x => x.Id == a.Id)).Withdraw();
            await db.SaveChangesAsync();
        }
        var remaining = await Get(student, LessonPath(a));
        Assert.Equal(0, remaining.GetProperty("progress").GetProperty("acceptedProblems").GetInt32());
        Assert.Equal(1, remaining.GetProperty("progress").GetProperty("totalAssignedProblems").GetInt32());
        Assert.Equal(0, remaining.GetProperty("progress").GetProperty("percent").GetDecimal());
        Assert.Equal("NeedsRevision", Assert.Single(remaining.GetProperty("problems").EnumerateArray()).GetProperty("work").GetProperty("status").GetString());
        Assert.Equal(ProblemWorkStatus.Accepted, (await app.Reload(firstId)).Status); // History preserved, excluded from progress.
    }

    [Theory]
    [InlineData("accept", "matching-group")]
    [InlineData("needs-revision", "matching-group")]
    [InlineData("accept", "withdrawn")]
    [InlineData("needs-revision", "unavailable")]
    public async Task Teacher_capability_matches_review_authorization_including_historical_access(string decision, string scenario)
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed(teacherGroup: scenario == "matching-group" ? "A" : null, studentGroup: "A", scopedTeacher: true);
        await Publish(app, a);
        using var student = app.Client(ApiFactory.Student);
        using var teacher = await app.SecureClient();
        var id = await Submit(student, a);
        await using (var db = app.Open())
        {
            if (scenario == "withdrawn") (await db.Assignments.SingleAsync()).Withdraw();
            if (scenario == "unavailable") (await db.Lessons.SingleAsync()).IsPublished = false;
            await db.SaveChangesAsync();
        }
        var work = await Get(teacher, WorkPath(id));
        Assert.True(work.GetProperty("permissions").GetProperty(decision == "accept" ? "canAccept" : "canRequestRevision").GetBoolean());
        Assert.False(work.GetProperty("permissions").GetProperty("canSubmit").GetBoolean());
        Assert.Equal(id, Assert.Single((await Get(teacher, QueuePath)).GetProperty("items").EnumerateArray()).GetProperty("problemWorkId").GetGuid());
        if (scenario != "withdrawn")
        {
            var row = Assert.Single((await Get(teacher, LessonPath(a))).GetProperty("problems").EnumerateArray());
            Assert.True(row.GetProperty("permissions").GetProperty(decision == "accept" ? "canAccept" : "canRequestRevision").GetBoolean());
        }
        await Json(await teacher.PostAsJsonAsync(WorkPath(id) + "/reviews/" + decision, ReviewBody(work)));
        Assert.Empty((await Get(teacher, QueuePath)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Publication_B_never_changes_previously_read_assignment_A_or_teacher_work()
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed();
        await Publish(app, a, statement: "Statement A");
        using var student = app.Client(ApiFactory.Student);
        using var teacher = app.Client(ApiFactory.Teacher, "Teacher");
        var before = (await Get(student, Workspace(a.Id))).GetProperty("problem");
        Assert.Equal("Statement A", before.GetProperty("statementMarkdown").GetString());
        var id = await Submit(student, a);
        var b = new ProblemAssignment(Guid.NewGuid(), a.StudentId, a.CourseId,
            new(Guid.NewGuid(), a.Content.LessonId, a.Content.ProblemId));
        await Publish(app, b, number: 2, statement: "Statement B");
        await using (var db = app.Open())
        {
            db.Assignments.Add(b);
            db.Lessons.Add(new LessonAccess { CourseId = b.CourseId, CourseVersionId = b.Content.CourseVersionId,
                LessonId = b.Content.LessonId, IsPublished = true });
            await db.SaveChangesAsync();
        }
        Assert.Equal("Statement B", (await Get(student, Workspace(b.Id))).GetProperty("problem").GetProperty("statementMarkdown").GetString());
        Assert.Equal(before.GetRawText(), (await Get(student, Workspace(a.Id))).GetProperty("problem").GetRawText());
        Assert.Equal(before.GetRawText(), (await Get(teacher, WorkPath(id))).GetProperty("problem").GetRawText());
    }

    [Fact]
    public async Task Shared_course_never_exposes_other_students_rows_or_other_teacher_groups()
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed(studentGroup: "A", teacherGroup: "A");
        await Publish(app, a);
        var b = new ProblemAssignment(Guid.NewGuid(), Guid.NewGuid(), a.CourseId, a.Content);
        await using (var db = app.Open())
        {
            db.Assignments.Add(b);
            db.Enrollments.Add(new EnrollmentAccess { StudentId = b.StudentId, CourseId = b.CourseId, IsActive = true, GroupId = "B" });
            await db.SaveChangesAsync();
        }
        using var studentA = app.Client(a.StudentId);
        using var studentB = app.Client(b.StudentId);
        using var teacher = app.Client(ApiFactory.Teacher, "Teacher");
        var workA = await Submit(studentA, a);
        var workB = await Submit(studentB, b);
        foreach (var (client, own, foreign, foreignWork) in new[] { (studentA, a, b, workB), (studentB, b, a, workA) })
        {
            var row = Assert.Single((await Get(client, LessonPath(a))).GetProperty("problems").EnumerateArray());
            Assert.Equal(own.Id, row.GetProperty("problemAssignmentId").GetGuid());
            var course = await Get(client, CoursePath(a));
            Assert.Equal(own.StudentId, course.GetProperty("enrollment").GetProperty("studentId").GetGuid());
            Assert.Equal(1, Assert.Single(course.GetProperty("lessons").EnumerateArray()).GetProperty("progress").GetProperty("totalAssignedProblems").GetInt32());
            await Error(await client.GetAsync(Workspace(foreign.Id)), 403, "Forbidden");
            await Error(await client.GetAsync(WorkPath(foreignWork)), 403, "Forbidden");
        }
        Assert.Equal(a.Id, Assert.Single((await Get(teacher, LessonPath(a))).GetProperty("problems").EnumerateArray()).GetProperty("problemAssignmentId").GetGuid());
        var queue = await Get(teacher, QueuePath + "?pageSize=1");
        Assert.Equal(1, queue.GetProperty("total").GetInt32());
        Assert.Equal(1, queue.GetProperty("summary").GetProperty("total").GetInt32());
        Assert.Equal(workA, Assert.Single(queue.GetProperty("items").EnumerateArray()).GetProperty("problemWorkId").GetGuid());
        await Error(await teacher.GetAsync(WorkPath(workB)), 403, "Forbidden");
    }

}
