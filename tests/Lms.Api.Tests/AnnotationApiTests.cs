using System.Net.Http.Json;
using Lms.Domain;
using Microsoft.EntityFrameworkCore;
namespace Lms.Api.Tests;
public sealed partial class ContractApiTests
{
    [Theory] [InlineData("accept")] [InlineData("needs-revision")]
    public async Task Annotation_commands_are_authorized_atomic_and_readable_in_history(string decision)
    {
        await using var app = await postgres.CreateApp(); var assignment = await app.Seed(); await Publish(app, assignment);
        using var student = app.Client(ApiFactory.Student); using var teacher = app.Client(ApiFactory.Teacher, "Teacher");
        var attachment = await app.Attachment(ApiFactory.Student);
        var submitted = await Json(await student.PostAsJsonAsync(SubmitPath(assignment.Id), new { attachmentIds = new[] { attachment } }), 201);
        var workId = submitted.GetProperty("problemWorkId").GetGuid(); var submissionId = submitted.GetProperty("submissionId").GetGuid();
        var annotations = new[] { new AnnotatedAttachment(attachment, [new(Guid.NewGuid(), "pen", "#dc2626", .003, [new(.2, .3, .6), new(.8, .9)])]) };
        object Body(Guid sub, long version, AnnotatedAttachment[] pages) => new { submissionId = sub, expectedVersion = version, feedbackMarkdown = "Fix this", annotations = pages };
        var path = WorkPath(workId) + "/reviews/" + decision;
        await Error(await student.PostAsJsonAsync(path, Body(submissionId, 1, annotations)), 403, "Forbidden");
        using var other = app.Client(Guid.NewGuid(), "Teacher");
        await Error(await other.PostAsJsonAsync(path, Body(submissionId, 1, annotations)), 403, "Forbidden");
        await Error(await teacher.PostAsJsonAsync(path, Body(Guid.NewGuid(), 1, annotations)), 409, "SubmissionIsNotCurrent");
        await Error(await teacher.PostAsJsonAsync(path, Body(submissionId, 0, annotations)), 409, "ConcurrencyConflict");
        await Error(await teacher.PostAsJsonAsync(path, Body(submissionId, 1, [annotations[0] with { AttachmentId = Guid.NewGuid() }])), 422, "InvalidAnnotations");
        await using (var db = app.Open()) { Assert.Empty(await db.Set<Review>().ToArrayAsync()); Assert.Equal(ProblemWorkStatus.Submitted, (await db.Works.SingleAsync()).Status); }
        await Json(await teacher.PostAsJsonAsync(path, Body(submissionId, 1, annotations)));
        var read = await Get(student, Workspace(assignment.Id));
        var review = Assert.Single(read.GetProperty("history").GetProperty("reviews").EnumerateArray());
        var page = Assert.Single(review.GetProperty("annotations").EnumerateArray());
        Assert.Equal(attachment, page.GetProperty("attachmentId").GetGuid());
        Assert.Equal(.2, page.GetProperty("strokes")[0].GetProperty("points")[0].GetProperty("x").GetDouble());
        await Error(await teacher.PostAsJsonAsync(path, Body(submissionId, 1, annotations)), 409, "ConcurrencyConflict");
        await using (var db = app.Open())
        {
            var persisted = await db.Set<Review>().SingleAsync(); Assert.Equal(annotations[0].Strokes[0].Id, persisted.Annotations[0].Strokes[0].Id);
            await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE \"Reviews\" SET \"Annotations\" = '[]'::jsonb"));
        }
    }
}
