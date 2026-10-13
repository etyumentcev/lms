using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Lms.Application;
using Lms.Domain;
using Lms.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Tests;

public sealed class AttachmentApiTests(ApiPostgresFixture fixture) : IClassFixture<ApiPostgresFixture>
{
    private static Task<HttpResponseMessage> Upload(HttpClient client, byte[]? bytes = null, string type = "image/png", string name = "лист.png")
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes ?? AttachmentImages.Png);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        form.Add(content, "file", name);
        return client.PostAsync("/api/v1/uploads", form);
    }
    private static async Task<AttachmentResult> Uploaded(HttpClient client, byte[]? bytes = null, string type = "image/png")
    {
        var response = await Upload(client, bytes, type);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AttachmentResult>())!;
    }
    private static async Task<AttachmentResult> ProtectedUploaded(HttpClient client, Guid workId,
        byte[]? bytes = null, string type = "image/png", string name = "teacher-note.png")
    {
        bytes ??= AttachmentImages.Png;
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/problem-works/{workId}/uploads/{Guid.NewGuid()}") { Content = content };
        request.Headers.Add("X-Command-Id", Guid.NewGuid().ToString());
        request.Headers.TryAddWithoutValidation("X-File-Name", Uri.EscapeDataString(name));
        request.Headers.Add("X-Content-SHA256", Convert.ToHexString(SHA256.HashData(bytes)));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AttachmentResult>())!;
    }
    private static async Task Error(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var value = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(code, value.GetProperty("code").GetString());
    }
    private static Task<HttpResponseMessage> Submit(HttpClient client, Guid assignment, params Guid[] ids)
        => client.PostAsJsonAsync($"/api/v1/problem-assignments/{assignment}/submissions", new { attachmentIds = ids });

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    public async Task Upload_preserves_original_metadata_and_does_not_create_work(string type)
    {
        await using var app = await fixture.CreateApp(); using var client = app.Client(ApiFactory.Student);
        var bytes = type == "image/jpeg" ? AttachmentImages.Jpeg : AttachmentImages.Png;
        var item = await Uploaded(client, bytes, type);
        Assert.Equal(type, item.MediaType); Assert.Equal(bytes.Length, item.Size);
        Assert.Equal(bytes, await client.GetByteArrayAsync(item.PreviewUrl));
        await using var db = app.Open(); var stored = await db.StoredAttachments.SingleAsync();
        Assert.Equal(ApiFactory.Student, stored.OwnerId); Assert.Null(stored.SubmissionId);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(app.StorageRoot, stored.StorageKey)));
        Assert.Empty(await db.Works.ToListAsync()); Assert.Empty(await db.Set<Submission>().ToListAsync());
    }

    [Theory]
    [InlineData("unsupported", 415, "UnsupportedAttachmentType")]
    [InlineData("fake", 400, "InvalidAttachment")]
    [InlineData("empty", 400, "InvalidAttachment")]
    [InlineData("oversized", 413, "AttachmentTooLarge")]
    [InlineData("truncated", 400, "InvalidAttachment")]
    [InlineData("png-crc", 400, "InvalidAttachment")]
    public async Task Invalid_uploads_leave_no_metadata_or_files(string scenario, int status, string code)
    {
        await using var app = await fixture.CreateApp(); using var client = app.Client(ApiFactory.Student);
        var bytes = scenario switch { "empty" => [], "oversized" => new byte[1025], "truncated" => AttachmentImages.Jpeg[..^4],
            "fake" => "not an image"u8.ToArray(), _ => AttachmentImages.Png.ToArray() };
        if (scenario == "png-crc") bytes[30] ^= 1;
        await Error(await Upload(client, bytes, scenario == "unsupported" ? "application/pdf" : scenario == "truncated" ? "image/jpeg" : "image/png", "photo.jpg"), status, code);
        await using var db = app.Open(); Assert.Empty(await db.StoredAttachments.ToListAsync());
        Assert.False(Directory.Exists(app.StorageRoot));
    }

    [Fact]
    public async Task Unknown_fields_multiple_files_and_malformed_multipart_rejected()
    {
        await using var app = await fixture.CreateApp(); using var client = app.Client(ApiFactory.Student);
        using var form = new MultipartFormDataContent(); form.Add(new StringContent(Guid.NewGuid().ToString()), "ownerId");
        await Error(await client.PostAsync("/api/v1/uploads", form), 400, "InvalidAttachment");
        using var malformed = new StringContent("bad multipart"); malformed.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=broken");
        await Error(await client.PostAsync("/api/v1/uploads", malformed), 400, "InvalidAttachment");
        using var multi = new MultipartFormDataContent(); multi.Add(new ByteArrayContent(AttachmentImages.Png), "file", "a.png"); multi.Add(new ByteArrayContent(AttachmentImages.Png), "file", "b.png");
        await Error(await client.PostAsync("/api/v1/uploads", multi), 400, "InvalidAttachment");
    }

    [Fact]
    public async Task Anonymous_upload_download_and_delete_require_authentication()
    {
        await using var app = await fixture.CreateApp(); using var client = app.Client();
        await Error(await Upload(client), 401, "Unauthenticated");
        await Error(await client.GetAsync($"/api/v1/attachments/{Guid.NewGuid()}/content"), 401, "Unauthenticated");
        await Error(await client.DeleteAsync($"/api/v1/attachments/{Guid.NewGuid()}"), 401, "Unauthenticated");
    }

    [Fact]
    public async Task Foreign_and_unknown_resources_are_indistinguishable_and_pending_teacher_access_denied()
    {
        await using var app = await fixture.CreateApp(); using var owner = app.Client(ApiFactory.Student);
        var item = await Uploaded(owner);
        using (var client = app.Client(Guid.NewGuid()))
        foreach (var id in new[] { item.AttachmentId, Guid.NewGuid() })
        {
            await Error(await client.GetAsync($"/api/v1/attachments/{id}/content"), 404, "NotFound");
            await Error(await client.DeleteAsync($"/api/v1/attachments/{id}"), 404, "NotFound");
        }
        using var teacher = app.Client(ApiFactory.Teacher, "Teacher");
        foreach (var id in new[] { item.AttachmentId, Guid.NewGuid() })
        {
            await Error(await teacher.GetAsync($"/api/v1/attachments/{id}/content"), 403, "Forbidden");
            await Error(await teacher.DeleteAsync($"/api/v1/attachments/{id}"), 403, "Forbidden");
        }
    }

    [Fact]
    public async Task Owner_deletes_pending_bytes_and_tombstone_blocks_future_submit()
    {
        await using var app = await fixture.CreateApp(); using var owner = app.Client(ApiFactory.Student);
        var a = await app.Seed(); var item = await Uploaded(owner);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/v1/attachments/{item.AttachmentId}")).StatusCode);
        Assert.Empty(Directory.GetFiles(app.StorageRoot));
        await Error(await owner.GetAsync(item.PreviewUrl), 404, "NotFound");
        await Error(await Submit(owner, a.Id, item.AttachmentId), 403, "AttachmentNotOwned");
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/v1/attachments/{item.AttachmentId}")).StatusCode);
    }

    [Fact]
    public async Task Successful_submission_claims_all_pages_in_request_order_and_history_remains_readable()
    {
        await using var app = await fixture.CreateApp(); using var owner = app.Client(ApiFactory.Student);
        var a = await app.Seed(studentGroup: "group", teacherGroup: "group", scopedTeacher: true);
        var first = await Uploaded(owner); var second = await Uploaded(owner);
        var response = await Submit(owner, a.Id, second.AttachmentId, first.AttachmentId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<SubmissionResult>(ApiFactory.Json))!;
        var work = await app.Reload(result.ProblemWorkId);
        Assert.Equal(new[] { second.AttachmentId, first.AttachmentId }, work.Submissions[0].AttachmentIds);
        await using var db = app.Open(); Assert.All(await db.StoredAttachments.ToListAsync(), x => Assert.Equal(result.SubmissionId, x.SubmissionId));
        await Error(await owner.DeleteAsync($"/api/v1/attachments/{first.AttachmentId}"), 409, "AttachmentAlreadyAttached");
        var another = await app.Seed(); await Error(await Submit(owner, another.Id, first.AttachmentId), 409, "AttachmentAlreadyAttached");
        using var teacher = await app.SecureClient();
        Assert.Equal(AttachmentImages.Png, await teacher.GetByteArrayAsync(first.PreviewUrl));
        Assert.Equal(AttachmentImages.Png, await owner.GetByteArrayAsync(first.PreviewUrl));
        using var outsider = app.Client(Guid.NewGuid(), "Teacher");
        await Error(await outsider.GetAsync(first.PreviewUrl), 403, "Forbidden");
        Assert.Equal(HttpStatusCode.OK, (await teacher.PostAsJsonAsync($"/api/v1/problem-works/{work.Id}/reviews/accept",
            new { commandId = Guid.NewGuid(), submissionId = result.SubmissionId, expectedVersion = 1, feedback = (string?)null })).StatusCode);
        Assert.Equal(AttachmentImages.Png, await teacher.GetByteArrayAsync(first.PreviewUrl));
    }

    [Fact]
    public async Task Foreign_attachment_and_failed_submit_do_not_claim_any_pages()
    {
        await using var app = await fixture.CreateApp(); using var owner = app.Client(ApiFactory.Student);
        using var foreign = app.Client(Guid.NewGuid()); var a = await app.Seed();
        var mine = await Uploaded(owner); var theirs = await Uploaded(foreign);
        await Error(await Submit(owner, a.Id, mine.AttachmentId, theirs.AttachmentId), 403, "AttachmentNotOwned");
        await Error(await owner.PostAsJsonAsync($"/api/v1/problem-assignments/{a.Id}/submissions",
            new { attachmentIds = new[] { mine.AttachmentId }, expectedVersion = 9 }), 409, "ConcurrencyConflict");
        await using var db = app.Open(); Assert.All(await db.StoredAttachments.ToListAsync(), x => Assert.Null(x.SubmissionId));
        Assert.Empty(await db.Works.ToListAsync());
    }

    [Theory]
    [InlineData("../../outside.png")]
    [InlineData("C:\\temp\\photo.png")]
    [InlineData("Лист №1 — 数学 <script> &.exe")]
    public async Task Filename_is_metadata_only(string name)
    {
        await using var app = await fixture.CreateApp(); using var owner = app.Client(ApiFactory.Student);
        var response = await Upload(owner, name: name); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var item = (await response.Content.ReadFromJsonAsync<AttachmentResult>())!;
        Assert.Equal(name, item.FileName);
        var path = Assert.Single(Directory.GetFiles(app.StorageRoot)); Assert.True(Guid.TryParseExact(Path.GetFileName(path), "N", out _));
        Assert.Equal(AttachmentImages.Png, await owner.GetByteArrayAsync(item.PreviewUrl));
    }

    [Fact]
    public async Task Concurrent_submissions_have_one_claim_and_loser_rolls_back_work()
    {
        await using var app = await fixture.CreateApp(); using var owner = app.Client(ApiFactory.Student);
        var a = await app.Seed(); var b = await app.Seed(); var item = await Uploaded(owner);
        app.Barrier.Armed = true;
        var responses = await Task.WhenAll(Submit(owner, a.Id, item.AttachmentId), Submit(owner, b.Id, item.AttachmentId));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        await Error(Assert.Single(responses, r => r.StatusCode != HttpStatusCode.Created), 409, "AttachmentAlreadyAttached");
        await using var db = app.Open(); Assert.Single(await db.Works.ToListAsync()); Assert.Single(await db.Set<Submission>().ToListAsync());
        Assert.NotNull((await db.StoredAttachments.SingleAsync()).SubmissionId);
    }

    [Fact]
    public async Task Delete_racing_submit_never_removes_a_committed_history_file()
    {
        await using var app = await fixture.CreateApp(); using var owner = app.Client(ApiFactory.Student);
        var a = await app.Seed(); var item = await Uploaded(owner);
        app.Barrier.Armed = true;
        var submit = Submit(owner, a.Id, item.AttachmentId);
        var delete = owner.DeleteAsync($"/api/v1/attachments/{item.AttachmentId}");
        var outcomes = await Task.WhenAll(submit, delete);
        if (outcomes[0].StatusCode == HttpStatusCode.Created)
        {
            await Error(outcomes[1], 409, "AttachmentAlreadyAttached");
            Assert.Equal(AttachmentImages.Png, await owner.GetByteArrayAsync(item.PreviewUrl));
        }
        else
        {
            await Error(outcomes[0], 403, "AttachmentNotOwned");
            Assert.Equal(HttpStatusCode.NoContent, outcomes[1].StatusCode);
            await using var db = app.Open(); Assert.Empty(await db.Works.ToListAsync());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Submitted_content_honors_teacher_group_access(bool matching)
    {
        await using var app = await fixture.CreateApp(); using var owner = app.Client(ApiFactory.Student);
        var a = await app.Seed(teacherGroup: matching ? "group" : "other", studentGroup: "group", scopedTeacher: true);
        var item = await Uploaded(owner); Assert.Equal(HttpStatusCode.Created, (await Submit(owner, a.Id, item.AttachmentId)).StatusCode);
        using var teacher = await app.SecureClient();
        Assert.Equal(matching ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await teacher.GetAsync(item.PreviewUrl)).StatusCode);
    }

    [Fact]
    public async Task Metadata_without_real_stored_resource_is_not_eligible()
    {
        await using var app = await fixture.CreateApp(); using var owner = app.Client(ApiFactory.Student);
        var a = await app.Seed(); var id = Guid.NewGuid(); await using var db = app.Open();
        db.AttachmentMetadata.Add(new AttachmentAccess { Id = id, OwnerId = ApiFactory.Student, IsEligible = true, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) });
        await db.SaveChangesAsync(); await Error(await Submit(owner, a.Id, id), 403, "AttachmentNotOwned");
    }

    [Fact]
    public async Task Teacher_group_scope_and_message_history_are_protected()
    {
        await using var app = await fixture.CreateApp(); using var owner = app.Client(ApiFactory.Student);
        var a = await app.Seed(teacherGroup: "other", studentGroup: "group", scopedTeacher: true); var item = await Uploaded(owner);
        var submitted = (await (await owner.PostAsJsonAsync($"/api/v1/problem-assignments/{a.Id}/submissions", new { bodyMarkdown = "text", attachmentIds = Array.Empty<Guid>() })).Content.ReadFromJsonAsync<SubmissionResult>(ApiFactory.Json))!;
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/v1/problem-works/{submitted.ProblemWorkId}/messages", new { bodyMarkdown = "photo", attachmentIds = new[] { item.AttachmentId } })).StatusCode);
        await Error(await owner.DeleteAsync($"/api/v1/attachments/{item.AttachmentId}"), 409, "AttachmentAlreadyAttached");
        using var teacher = await app.SecureClient(); await Error(await teacher.GetAsync(item.PreviewUrl), 403, "Forbidden");
    }
    [Theory]
    [InlineData("enrollment")]
    [InlineData("lesson")]
    [InlineData("assignment")]
    public async Task Attached_original_rechecks_student_access_after_revocation(string reason)
    {
        await using var app = await fixture.CreateApp(); using var student = app.Client(ApiFactory.Student);
        var a = await app.Seed(); var item = await Uploaded(student);
        Assert.Equal(HttpStatusCode.Created, (await Submit(student, a.Id, item.AttachmentId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await student.GetAsync(item.PreviewUrl)).StatusCode);
        await using (var db = app.Open())
        {
            if (reason == "enrollment") (await db.Enrollments.SingleAsync()).IsActive = false;
            if (reason == "lesson") (await db.Lessons.SingleAsync()).IsPublished = false;
            if (reason == "assignment") (await db.Assignments.SingleAsync()).Withdraw();
            await db.SaveChangesAsync();
        }
        await Error(await student.GetAsync(item.PreviewUrl), 404, "NotFound");
    }

    [Fact]
    public async Task Teacher_owned_message_original_rechecks_revoked_grant()
    {
        await using var app = await fixture.CreateApp(); using var student = app.Client(ApiFactory.Student);
        var a = await app.Seed(studentGroup: "group", teacherGroup: "group", scopedTeacher: true);
        using var teacher = await app.SecureClient();
        var submitted = (await (await student.PostAsJsonAsync($"/api/v1/problem-assignments/{a.Id}/submissions",
            new { bodyMarkdown = "text", attachmentIds = Array.Empty<Guid>() })).Content.ReadFromJsonAsync<SubmissionResult>(ApiFactory.Json))!;
        var item = await ProtectedUploaded(teacher, submitted.ProblemWorkId);
        (await teacher.PostAsJsonAsync($"/api/v1/problem-works/{submitted.ProblemWorkId}/messages",
            new { commandId = Guid.NewGuid(), intentId = Guid.NewGuid(), body = "feedback photo",
                replyTo = (Guid?)null, attachments = new[] { item.AttachmentId } })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await student.GetAsync(item.PreviewUrl)).StatusCode);
        using var other = app.Client(Guid.NewGuid()); await Error(await other.GetAsync(item.PreviewUrl), 404, "NotFound");
        TeacherAssignmentScope grant;
        await using (var db = app.Open())
            grant = await db.Set<TeacherAssignmentScope>().SingleAsync(x => x.CourseId == a.CourseId && x.TeacherId == ApiFactory.Teacher);
        await using (var administration = app.Open())
            await new PostgresScopedAuthorityAdministration(administration).Change(new(AuthorityKind.Teacher,
                AuthorityOperation.Revoke, app.OrganizationId, ApiFactory.Teacher, grant.Version,
                grant.MembershipGeneration, a.CourseId, grant.GroupId, grant.GrantId));
        using var fresh = await app.SecureClient();
        await Error(await fresh.GetAsync(item.PreviewUrl), 403, "Forbidden");
    }

}
