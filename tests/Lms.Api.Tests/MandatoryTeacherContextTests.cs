using System.Net;
using System.Net.Http.Json;
using Lms.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Tests;

public sealed class MandatoryTeacherContextTests(ApiPostgresFixture postgres) : IClassFixture<ApiPostgresFixture>
{
    [Theory]
    [InlineData("reviews/accept")][InlineData("reviews/needs-revision")][InlineData("messages")]
    [InlineData("upload")][InlineData("delete")][InlineData("file")]
    public async Task Registered_legacy_teacher_paths_deny_without_context_and_leave_no_effects(string route)
    {
        await using var app = await postgres.CreateApp(); var assignment = await app.Seed();
        using var student = app.Client(ApiFactory.Student);
        var submitted = await student.PostAsJsonAsync($"/api/v1/problem-assignments/{assignment.Id}/submissions",
            new { bodyMarkdown = "Student positive control", attachmentIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        var work = (await submitted.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("problemWorkId").GetGuid();
        var file = await app.Attachment(ApiFactory.Student);
        using var teacher = app.Client(ApiFactory.Teacher, "Teacher");
        var loaded = await app.Reload(work);
        HttpResponseMessage response;
        if (route == "upload") response = await teacher.PostAsync("/api/v1/uploads", new ByteArrayContent([1, 2, 3]));
        else if (route == "delete") response = await teacher.DeleteAsync($"/api/v1/attachments/{file}");
        else if (route == "file") response = await teacher.GetAsync($"/api/v1/attachments/{file}/content");
        else if (route == "messages") response = await teacher.PostAsJsonAsync($"/api/v1/problem-works/{work}/messages",
            new { bodyMarkdown = "Teacher", attachmentIds = Array.Empty<Guid>() });
        else response = await teacher.PostAsJsonAsync($"/api/v1/problem-works/{work}/{route}",
            new { submissionId = loaded.Submissions[0].Id, expectedVersion = loaded.Version, feedbackMarkdown = "Teacher" });
        using (response) Assert.Equal(route.StartsWith("reviews/", StringComparison.Ordinal)
            ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        await using var db = app.Open();
        Assert.Empty(await db.Set<Review>().ToListAsync()); Assert.Empty(await db.Set<Message>().ToListAsync());
        Assert.Single(await db.Events.ToListAsync()); Assert.Single(await db.StoredAttachments.ToListAsync());
        Assert.False((await db.StoredAttachments.SingleAsync()).Deleted);
        Assert.Equal(HttpStatusCode.OK, (await student.GetAsync($"/api/v1/attachments/{file}/content")).StatusCode);
    }
}
