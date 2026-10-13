using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Tests;

public sealed partial class ContractApiTests
{
    [Fact]
    public async Task Complete_HTTP_workflow_preserves_versions_content_history_queue_and_progress()
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed(studentGroup: "group-a", scopedTeacher: true);
        await Publish(app, a);
        using var student = app.Client(ApiFactory.Student);
        using var teacher = await app.SecureClient();
        var initial = await Get(student, Workspace(a.Id));
        Fields(initial, "assignment", "course", "lesson", "problem", "availability", "work", "hints", "history", "usefulMaterials", "permissions");
        Fields(initial.GetProperty("assignment"), "id", "isActive");
        Fields(initial.GetProperty("course"), "id", "title", "publishedVersionId", "description", "gradeLabel", "teacher");
        Fields(initial.GetProperty("lesson"), "id", "courseVersionId", "order", "title", "summary");
        Fields(initial.GetProperty("problem"), "id", "courseVersionId", "order", "title", "statementMarkdown");
        Fields(initial.GetProperty("availability"), "isAvailable", "availableFrom", "reason");
        Fields(initial.GetProperty("work"), "problemWorkId", "status", "attemptCount", "version", "latestSubmission", "latestReview", "latestTeacherFeedback");
        Fields(initial.GetProperty("history"), "submissions", "reviews", "messages");
        PermissionFields(initial.GetProperty("permissions"));
        var summary = initial.GetProperty("work");
        Assert.Equal("NotStarted", summary.GetProperty("status").GetString());
        Assert.Equal(0, summary.GetProperty("attemptCount").GetInt32());
        foreach (var field in new[] { "problemWorkId", "version", "latestSubmission", "latestReview", "latestTeacherFeedback" })
            Assert.Equal(JsonValueKind.Null, summary.GetProperty(field).ValueKind);
        foreach (var field in new[] { "submissions", "reviews", "messages" })
            Assert.Empty(initial.GetProperty("history").GetProperty(field).EnumerateArray());
        Assert.True(initial.GetProperty("permissions").GetProperty("canSubmit").GetBoolean());
        Assert.False(initial.GetProperty("permissions").GetProperty("canPostMessage").GetBoolean());
        await using (var db = app.Open())
        {
            Assert.False(await db.Works.AnyAsync());
            Assert.False(await db.Set<Submission>().AnyAsync());
            Assert.False(await db.Events.AnyAsync());
        }
        var attachment = await app.Attachment(ApiFactory.Student);
        const string originalBody = "Proof $x^2$\n<script>untrusted source text</script>";
        var first = await Json(await student.PostAsJsonAsync(SubmitPath(a.Id),
            new { bodyMarkdown = originalBody, attachmentIds = new[] { attachment } }), 201);
        Fields(first, "problemWorkId", "submissionId", "attemptNumber", "status", "version");
        var id = first.GetProperty("problemWorkId").GetGuid();
        Assert.Equal(1, first.GetProperty("version").GetInt64());
        Assert.Equal(1, first.GetProperty("attemptNumber").GetInt32());
        var submitted = await Get(student, Workspace(a.Id));
        Assert.Equal(id, submitted.GetProperty("work").GetProperty("problemWorkId").GetGuid());
        Assert.Equal("Submitted", submitted.GetProperty("work").GetProperty("status").GetString());
        Assert.Equal(1, submitted.GetProperty("work").GetProperty("version").GetInt64());
        Assert.Equal(1, submitted.GetProperty("work").GetProperty("attemptCount").GetInt32());
        Assert.False(submitted.GetProperty("permissions").GetProperty("canSubmit").GetBoolean());
        var originalSubmission = submitted.GetProperty("work").GetProperty("latestSubmission");
        SubmissionFields(originalSubmission);
        Assert.Equal(originalBody, originalSubmission.GetProperty("bodyMarkdown").GetString());
        Assert.Equal(attachment, Assert.Single(originalSubmission.GetProperty("attachmentIds").EnumerateArray()).GetGuid());

        var queue = await Get(teacher, QueuePath);
        Fields(queue, "items", "page", "pageSize", "total", "summary");
        Fields(queue.GetProperty("summary"), "total", "firstAttempts", "revisions", "averageWaitingSeconds", "byCourse");
        var item = Assert.Single(queue.GetProperty("items").EnumerateArray());
        Fields(item, "problemWorkId", "submissionId", "studentId", "studentDisplayName", "courseId", "courseTitle",
            "lessonId", "lessonTitle", "groupId", "groupTitle", "problemId", "problemTitle", "attemptNumber", "submittedAt", "attachmentCount", "waitingDurationSeconds", "version");
        Assert.Equal(first.GetProperty("submissionId").GetGuid(), item.GetProperty("submissionId").GetGuid());
        Assert.Equal(id, item.GetProperty("problemWorkId").GetGuid());
        var work = await Get(teacher, WorkPath(id));
        Fields(work, "id", "problemAssignmentId", "status", "version", "attemptCount", "student", "course", "lesson", "problem",
            "currentSubmission", "submissions", "reviews", "messages", "permissions");
        Assert.Equal(originalSubmission.GetRawText(), work.GetProperty("currentSubmission").GetRawText());
        Assert.Equal(initial.GetProperty("problem").GetRawText(), work.GetProperty("problem").GetRawText());
        Assert.True(work.GetProperty("permissions").GetProperty("canRequestRevision").GetBoolean());
        var revision = await Json(await teacher.PostAsJsonAsync(WorkPath(id) + "/reviews/needs-revision", ReviewBody(work)));
        Fields(revision, "reviewId", "messageId", "status", "version");
        Assert.Equal("NeedsRevision", revision.GetProperty("status").GetString());
        Assert.Equal(2, revision.GetProperty("version").GetInt64());
        Assert.Empty((await Get(teacher, QueuePath)).GetProperty("items").EnumerateArray());
        var needsRevision = await Get(student, Workspace(a.Id));
        Assert.Equal("NeedsRevision", needsRevision.GetProperty("work").GetProperty("status").GetString());
        var feedback = needsRevision.GetProperty("work").GetProperty("latestTeacherFeedback");
        Fields(feedback, "id", "author", "authorRole", "bodyMarkdown", "replyToMessageId", "createdAt", "attachmentIds");
        Fields(feedback.GetProperty("author"), "id", "displayName", "avatarUrl");
        Assert.Equal("Explain the proof", feedback.GetProperty("bodyMarkdown").GetString());
        Assert.Equal(revision.GetProperty("messageId").GetGuid(), feedback.GetProperty("id").GetGuid());
        Assert.True(needsRevision.GetProperty("permissions").GetProperty("canSubmit").GetBoolean());
        var second = await Json(await student.PostAsJsonAsync(SubmitPath(a.Id),
            Solution(needsRevision.GetProperty("work").GetProperty("version").GetInt64(), "Improved proof")), 201);
        Assert.Equal(2, second.GetProperty("attemptNumber").GetInt32());
        Assert.Equal(3, second.GetProperty("version").GetInt64());
        item = Assert.Single((await Get(teacher, QueuePath)).GetProperty("items").EnumerateArray());
        Assert.Equal(2, item.GetProperty("attemptNumber").GetInt32());
        Assert.Equal(second.GetProperty("submissionId").GetGuid(), item.GetProperty("submissionId").GetGuid());
        work = await Get(teacher, WorkPath(id));
        Assert.True(work.GetProperty("permissions").GetProperty("canAccept").GetBoolean());
        var accepted = await Json(await teacher.PostAsJsonAsync(WorkPath(id) + "/reviews/accept", ReviewBody(work, null)));
        Fields(accepted, "reviewId", "messageId", "status", "version");
        Assert.Equal("Accepted", accepted.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, accepted.GetProperty("messageId").ValueKind);
        Assert.Equal(4, accepted.GetProperty("version").GetInt64());
        var final = await Get(student, Workspace(a.Id));
        Assert.Equal("Accepted", final.GetProperty("work").GetProperty("status").GetString());
        Assert.Equal(4, final.GetProperty("work").GetProperty("version").GetInt64());
        Assert.Equal(2, final.GetProperty("work").GetProperty("attemptCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, final.GetProperty("work").GetProperty("latestTeacherFeedback").ValueKind);
        var submissions = final.GetProperty("history").GetProperty("submissions").EnumerateArray().ToArray();
        Assert.Equal(new[] { 2, 1 }, submissions.Select(s => s.GetProperty("attemptNumber").GetInt32()));
        Assert.Equal(originalSubmission.GetRawText(), submissions[1].GetRawText());
        var reviews = final.GetProperty("history").GetProperty("reviews").EnumerateArray().ToArray();
        Assert.Equal(new[] { "Accepted", "NeedsRevision" }, reviews.Select(r => r.GetProperty("decision").GetString()));
        foreach (var review in reviews) Fields(review, "id", "submissionId", "teacherId", "decision", "messageId", "createdAt", "annotations");
        Assert.Equal(second.GetProperty("submissionId").GetGuid(), reviews[0].GetProperty("submissionId").GetGuid());
        Assert.Equal(first.GetProperty("submissionId").GetGuid(), reviews[1].GetProperty("submissionId").GetGuid());
        Assert.Equal(feedback.GetProperty("id").GetGuid(), reviews[1].GetProperty("messageId").GetGuid());
        Assert.Equal(feedback.GetRawText(), Assert.Single(final.GetProperty("history").GetProperty("messages").EnumerateArray()).GetRawText());
        Assert.False(final.GetProperty("permissions").GetProperty("canSubmit").GetBoolean());
        queue = await Get(teacher, QueuePath);
        Assert.Empty(queue.GetProperty("items").EnumerateArray());
        Assert.Equal(0, queue.GetProperty("total").GetInt32());
        Assert.Equal(0, queue.GetProperty("summary").GetProperty("total").GetInt32());
        var persisted = await app.Reload(id);
        Assert.Equal(2, persisted.AttemptCount);
        Assert.Equal(originalBody, persisted.Submissions[0].BodyMarkdown);
        Assert.Equal(attachment, Assert.Single(persisted.Submissions[0].AttachmentIds));

        var lesson = await Get(student, LessonPath(a));
        Fields(lesson, "course", "lesson", "contentVersions", "availability", "progress", "problems", "materials");
        Fields(lesson.GetProperty("progress"), "status", "acceptedProblems", "totalAssignedProblems", "percent");
        var problem = Assert.Single(lesson.GetProperty("problems").EnumerateArray());
        Fields(problem, "problemAssignmentId", "studentId", "problem", "availability", "work", "permissions");
        PermissionFields(problem.GetProperty("permissions"));
        Assert.Equal(100, lesson.GetProperty("progress").GetProperty("percent").GetDecimal());
        var course = await Get(student, CoursePath(a));
        Fields(course, "course", "enrollment", "teacherContext", "progress", "lessons", "materials");
        Fields(course.GetProperty("progress"), "completedLessons", "availableLessons", "totalLessons", "percent");
        Fields(course.GetProperty("enrollment"), "id", "studentId", "courseId", "groupId", "status");
        Fields(Assert.Single(course.GetProperty("lessons").EnumerateArray()), "lesson", "availability", "progress", "courseVersionIds");
        Assert.Equal(100, course.GetProperty("progress").GetProperty("percent").GetDecimal());
        var teacherCourse = await Get(teacher, CoursePath(a));
        Assert.Equal(JsonValueKind.Null, teacherCourse.GetProperty("enrollment").ValueKind);
        Fields(teacherCourse.GetProperty("teacherContext"), "teacherId", "canReviewCourse", "groupIds");
    }

    [Theory]
    [InlineData("Student", "Submitted")]
    [InlineData("Student", "NeedsRevision")]
    [InlineData("Student", "Accepted")]
    [InlineData("Teacher", "Submitted")]
    [InlineData("Teacher", "NeedsRevision")]
    [InlineData("Teacher", "Accepted")]
    public async Task Message_capability_allows_command_and_fresh_GET_returns_incremented_version(string role, string state)
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed(studentGroup: "group-a", scopedTeacher: true);
        await Publish(app, a);
        using var student = app.Client(ApiFactory.Student);
        using var teacher = await app.SecureClient();
        var id = await Submit(student, a);
        if (state != "Submitted")
        {
            var work = await Get(teacher, WorkPath(id));
            await Json(await teacher.PostAsJsonAsync(WorkPath(id) + "/reviews/" + (state == "Accepted" ? "accept" : "needs-revision"), ReviewBody(work)));
        }
        using var actor = role == "Student" ? app.Client(ApiFactory.Student) : await app.SecureClient();
        var before = await Get(actor, WorkPath(id));
        Assert.True(before.GetProperty("permissions").GetProperty("canPostMessage").GetBoolean());
        var lesson = await Get(actor, LessonPath(a));
        Assert.True(Assert.Single(lesson.GetProperty("problems").EnumerateArray()).GetProperty("permissions").GetProperty("canPostMessage").GetBoolean());
        var result = await Json(await actor.PostAsJsonAsync(WorkPath(id) + "/messages",
            role == "Student" ? MessageBody : TeacherMessageBody));
        Fields(result, "messageId", "createdAt"); // Accepted Feature UC-A06: no response version.
        Assert.Equal(TimeSpan.Zero, result.GetProperty("createdAt").GetDateTimeOffset().Offset);
        var after = await Get(actor, WorkPath(id));
        Assert.Equal(before.GetProperty("version").GetInt64() + 1, after.GetProperty("version").GetInt64());
        Assert.Equal(state, after.GetProperty("status").GetString());
        Assert.Equal(1, after.GetProperty("attemptCount").GetInt32());
        Assert.Contains(after.GetProperty("messages").EnumerateArray(), m => m.GetProperty("id").GetGuid() == result.GetProperty("messageId").GetGuid());
        Assert.Equal(before.GetProperty("submissions").GetRawText(), after.GetProperty("submissions").GetRawText());
    }

    [Fact]
    public async Task Concurrent_duplicate_teacher_message_returns_one_conflict_without_partial_history()
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed(studentGroup: "group-a", scopedTeacher: true);
        using var student = app.Client(ApiFactory.Student);
        using var teacherA = await app.SecureClient();
        using var teacherB = await app.SecureClient();
        var id = await Submit(student, a);
        var command = TeacherMessageBody;
        app.Barrier.Armed = true;
        var responses = await Task.WhenAll(teacherA.PostAsJsonAsync(WorkPath(id) + "/messages", command),
            teacherB.PostAsJsonAsync(WorkPath(id) + "/messages", command));
        await Json(Assert.Single(responses, r => (int)r.StatusCode == 200));
        await Error(Assert.Single(responses, r => (int)r.StatusCode == 409), 409, "OperationAlreadyAttempted");
        var work = await app.Reload(id);
        Assert.Equal(2, work.Version);
        Assert.Single(work.Messages);
        Assert.Single(work.Submissions);
        Assert.Equal(ProblemWorkStatus.Submitted, work.Status);
        await using var db = app.Open();
        Assert.Equal(2, await db.Events.CountAsync());
    }
}
