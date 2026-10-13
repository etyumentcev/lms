using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Lms.Domain;

namespace Lms.Api.Tests;

// Legacy compatibility through in-process TestServer, not S3/S4 HTTP acceptance.
public sealed partial class ContractApiTests
{
    [Fact]
    public async Task Photo_revision_journey_reopens_originals_history_and_progress_in_fresh_scopes()
    {
        await using var app = await postgres.CreateApp();
        var a = await app.Seed(studentGroup: "A", scopedTeacher: true);
        await Publish(app, a, statement: "Synthetic immutable problem: prove x = x.");
        using var student = app.Client(ApiFactory.Student);
        using var teacher = await app.SecureClient();
        await Get(teacher, CoursePath(a));
        await Get(teacher, LessonPath(a));
        async Task<Guid> Upload(byte[] bytes, string mime)
        {
            using var form = new MultipartFormDataContent();
            using var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new(mime);
            form.Add(file, "file", mime == "image/png" ? "synthetic.png" : "synthetic.jpg");
            return (await Json(await student.PostAsync("/api/v1/uploads", form), 201)).GetProperty("attachmentId").GetGuid();
        }
        var pages = new[] { await Upload(AttachmentImages.Png, "image/png"), await Upload(AttachmentImages.Jpeg, "image/jpeg") };
        var first = await Json(await student.PostAsJsonAsync(SubmitPath(a.Id), new { attachmentIds = pages }), 201);
        var id = first.GetProperty("problemWorkId").GetGuid();
        var teacherPath = $"/api/v1/teacher/problem-works/{id}";
        var before = await Get(teacher, teacherPath);
        Assert.Equal("Synthetic immutable problem: prove x = x.", before.GetProperty("problem").GetProperty("statementMarkdown").GetString());
        var original = before.GetProperty("currentSubmission").GetRawText();
        var revision = await Json(await teacher.PostAsJsonAsync(WorkPath(id) + "/reviews/needs-revision", ReviewBody(before, "Explain step two.")));
        var returned = await Get(student, Workspace(a.Id));
        Assert.Equal("Explain step two.", returned.GetProperty("work").GetProperty("latestTeacherFeedback").GetProperty("bodyMarkdown").GetString());
        var corrected = await Upload(AttachmentImages.Jpeg, "image/jpeg");
        await Json(await student.PostAsJsonAsync(SubmitPath(a.Id), new {
            attachmentIds = new[] { corrected }, expectedVersion = returned.GetProperty("work").GetProperty("version").GetInt64()
        }), 201);
        var resubmitted = await Get(teacher, teacherPath);
        Assert.Equal(2, resubmitted.GetProperty("attemptCount").GetInt32());
        Assert.Equal("Explain step two.", Assert.Single(resubmitted.GetProperty("messages").EnumerateArray()).GetProperty("bodyMarkdown").GetString());
        await Json(await teacher.PostAsJsonAsync(WorkPath(id) + "/reviews/accept", ReviewBody(resubmitted, "Accepted correction.")));
        // Fresh HTTP clients and DbContexts exclude tracked aggregate/browser cache as evidence.
        using var reopenedStudent = app.Client(ApiFactory.Student);
        using var reopenedTeacher = await app.SecureClient();
        var final = await Get(reopenedStudent, Workspace(a.Id));
        var history = final.GetProperty("history");
        Assert.Equal("Accepted", final.GetProperty("work").GetProperty("status").GetString());
        Assert.Equal(original, history.GetProperty("submissions")[1].GetRawText());
        Assert.Equal(corrected, Assert.Single(history.GetProperty("submissions")[0].GetProperty("attachmentIds").EnumerateArray()).GetGuid());
        Assert.Equal(2, history.GetProperty("reviews").GetArrayLength());
        Assert.Equal(revision.GetProperty("reviewId").GetGuid(), history.GetProperty("reviews")[1].GetProperty("id").GetGuid());
        Assert.Equal(new[] { "Explain step two.", "Accepted correction." }, history.GetProperty("messages").EnumerateArray().Select(x => x.GetProperty("bodyMarkdown").GetString()));
        Assert.Equal(history.GetProperty("submissions").GetRawText(), (await Get(reopenedTeacher, teacherPath)).GetProperty("submissions").GetRawText());
        foreach (var client in new[] { reopenedStudent, reopenedTeacher })
        foreach (var (photo, bytes) in new[] { (pages[0], AttachmentImages.Png), (pages[1], AttachmentImages.Jpeg), (corrected, AttachmentImages.Jpeg) })
            Assert.Equal(bytes, await client.GetByteArrayAsync($"/api/v1/attachments/{photo}/content"));
        Assert.Equal(100, (await Get(reopenedStudent, CoursePath(a))).GetProperty("progress").GetProperty("percent").GetDecimal());
        Assert.Equal(0, (await Get(reopenedTeacher, QueuePath)).GetProperty("total").GetInt32());
        await using (var db = app.Open())
        {
            Assert.Equal(2, await db.Set<Submission>().CountAsync());
            Assert.Equal(2, await db.Set<Review>().CountAsync());
            Assert.Equal(2, await db.Set<Message>().CountAsync());
            Assert.Equal(6, await db.Events.CountAsync());
            Assert.All(await db.StoredAttachments.ToListAsync(), photo => Assert.NotNull(photo.SubmissionId));
        }
        foreach (var role in new[] { "Student", "Teacher" })
        {
            using var outsider = app.Client(Guid.NewGuid(), role);
            await Error(await outsider.GetAsync(WorkPath(id)), 403, "Forbidden");
            foreach (var photo in pages.Append(corrected))
                await Error(await outsider.GetAsync($"/api/v1/attachments/{photo}/content"), role == "Teacher" ? 403 : 404,
                    role == "Teacher" ? "Forbidden" : "NotFound");
        }
        await Error(await reopenedStudent.GetAsync(teacherPath), 403, "Forbidden");
        using var scopedStudent = await app.SecureClient("Student");
        await Error(await scopedStudent.PostAsJsonAsync(WorkPath(id) + "/reviews/accept", ReviewBody(resubmitted)), 403, "Forbidden");
    }
}
