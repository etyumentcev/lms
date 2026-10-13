using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Lms.Application;
using Lms.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Tests;

public sealed class CommandApiTests(ApiPostgresFixture postgres) : IClassFixture<ApiPostgresFixture>
{
    private static string SubmitRoute(Guid id) => $"/api/v1/problem-assignments/{id}/submissions";
    private static string MessageRoute(Guid id) => $"/api/v1/problem-works/{id}/messages";
    private static string ReviewRoute(Guid id, string decision = "needs-revision") => $"/api/v1/problem-works/{id}/reviews/{decision}";
    private static object Solution(long? version = null, string? body = "solution", params Guid[] attachments)
        => new { bodyMarkdown = body, attachmentIds = attachments, expectedVersion = version };
    private static object Review(SubmissionResult submission, long version, string? feedback = "Explain step 2")
        => new { commandId = Guid.NewGuid(), submissionId = submission.SubmissionId, feedback, expectedVersion = version };
    private static object Message(Guid? reply = null, params Guid[] attachments)
        => new { bodyMarkdown = "A question", replyToMessageId = reply, attachmentIds = attachments };
    private static object TeacherMessage(Guid? reply = null, params Guid[] attachments)
        => new { commandId = Guid.NewGuid(), intentId = Guid.NewGuid(), body = "A question", replyTo = reply, attachments };
    private static async Task<T> Success<T>(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        Assert.True(response.StatusCode == status, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(ApiFactory.Json))!;
    }
    private static async Task Error(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(status, json.GetProperty("status").GetInt32());
        Assert.Equal(code, json.GetProperty("code").GetString());
        Assert.StartsWith("urn:lms:error:", json.GetProperty("type").GetString());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("traceId").GetString()));
    }
    private static async Task<SubmissionResult> Submit(ApiFactory app, ProblemAssignment assignment)
    {
        using var client = app.Client(ApiFactory.Student);
        return await Success<SubmissionResult>(await client.PostAsJsonAsync(SubmitRoute(assignment.Id), Solution()), HttpStatusCode.Created);
    }

    [Fact]
    public async Task Full_flow_preserves_history_and_message_does_not_change_status_or_attempt_count()
    {
        await using var app = await postgres.CreateApp();
        var assignment = await app.Seed(studentGroup: "group-a", scopedTeacher: true);
        using var student = app.Client(ApiFactory.Student);
        using var teacher = await app.SecureClient();
        var first = await Submit(app, assignment);
        Assert.Equal(1, first.AttemptNumber);
        Assert.Equal(1, first.Version);
        Assert.Equal(ProblemWorkStatus.Submitted, first.Status);
        await Error(await student.PostAsJsonAsync(SubmitRoute(assignment.Id), Solution(1)), 409, "WorkAlreadySubmitted");
        var revision = await Success<ReviewResult>(await teacher.PostAsJsonAsync(ReviewRoute(first.ProblemWorkId), Review(first, 1)));
        Assert.Equal(ProblemWorkStatus.NeedsRevision, revision.Status);
        Assert.NotNull(revision.MessageId);
        var message = await Success<MessageResult>(await student.PostAsJsonAsync(MessageRoute(first.ProblemWorkId), Message(revision.MessageId)));
        var afterMessage = await app.Reload(first.ProblemWorkId);
        Assert.Equal(ProblemWorkStatus.NeedsRevision, afterMessage.Status);
        Assert.Equal(1, afterMessage.AttemptCount);
        Assert.Equal(3, afterMessage.Version);
        Assert.Contains(afterMessage.Messages, m => m.Id == message.MessageId && m.AuthorId == ApiFactory.Student);
        await Error(await student.PostAsJsonAsync(SubmitRoute(assignment.Id), Solution()), 409, "ConcurrencyConflict");
        await Error(await student.PostAsJsonAsync(SubmitRoute(assignment.Id), Solution(2)), 409, "ConcurrencyConflict");
        var second = await Success<SubmissionResult>(await student.PostAsJsonAsync(SubmitRoute(assignment.Id), Solution(3, "revision")), HttpStatusCode.Created);
        Assert.Equal(2, second.AttemptNumber);
        await Error(await teacher.PostAsJsonAsync(ReviewRoute(first.ProblemWorkId, "accept"), Review(first, second.Version)), 409, "SubmissionIsNotCurrent");
        var accepted = await Success<ReviewResult>(await teacher.PostAsJsonAsync(ReviewRoute(second.ProblemWorkId, "accept"), Review(second, second.Version, null)));
        Assert.Null(accepted.MessageId);
        Assert.Equal(ProblemWorkStatus.Accepted, accepted.Status);
        await Error(await teacher.PostAsJsonAsync(ReviewRoute(second.ProblemWorkId), Review(second, second.Version)), 409, "ConcurrencyConflict");
        await Error(await teacher.PostAsJsonAsync(ReviewRoute(second.ProblemWorkId), Review(second, accepted.Version)), 409, "WorkNotSubmitted");
        await Error(await student.PostAsJsonAsync(SubmitRoute(assignment.Id), Solution(accepted.Version)), 409, "WorkAlreadyAccepted");
        await Success<MessageResult>(await teacher.PostAsJsonAsync(MessageRoute(first.ProblemWorkId), TeacherMessage()));
        var final = await app.Reload(first.ProblemWorkId);
        Assert.Equal(ProblemWorkStatus.Accepted, final.Status);
        Assert.Equal(2, final.Submissions.Count);
        Assert.Equal(2, final.Reviews.Count);
        Assert.Equal(first.SubmissionId, final.Submissions[0].Id);
        Assert.Equal("solution", final.Submissions[0].BodyMarkdown);
        Assert.Equal(3, final.Messages.Count);
        Assert.All(final.Reviews, r => Assert.Equal(ApiFactory.Teacher, r.TeacherId));
    }

    [Fact]
    public async Task First_submit_rejects_non_null_version_without_creating_work()
    {
        await using var app = await postgres.CreateApp();
        var assignment = await app.Seed();
        using var student = app.Client(ApiFactory.Student);
        await Error(await student.PostAsJsonAsync(SubmitRoute(assignment.Id), Solution(0)), 409, "ConcurrencyConflict");
        await using var db = app.Open();
        Assert.False(await db.Works.AnyAsync());
        Assert.False(await db.Events.AnyAsync());
    }

    [Theory]
    [InlineData("stranger")]
    [InlineData("inactive")]
    [InlineData("unpublished")]
    [InlineData("scheduled")]
    [InlineData("unenrolled")]
    [InlineData("missing-facts")]
    public async Task Student_access_is_authoritative(string scenario)
    {
        await using var app = await postgres.CreateApp();
        var assignment = await app.Seed(active: scenario != "inactive", published: scenario != "unpublished",
            enrolled: scenario != "unenrolled", availableFrom: scenario == "scheduled" ? DateTimeOffset.UtcNow.AddDays(1) : null);
        if (scenario == "missing-facts")
        {
            await using var setup = app.Open();
            await setup.Enrollments.ExecuteDeleteAsync();
            await setup.Lessons.ExecuteDeleteAsync();
        }
        using var client = app.Client(scenario == "stranger" ? Guid.NewGuid() : ApiFactory.Student);
        await Error(await client.PostAsJsonAsync(SubmitRoute(assignment.Id), Solution()), 403, "Forbidden");
        await using var db = app.Open();
        Assert.False(await db.Works.AnyAsync());
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("other-owner")]
    [InlineData("expired")]
    [InlineData("ineligible")]
    public async Task Attachment_access_is_enforced_for_submission_and_message(string scenario)
    {
        await using var app = await postgres.CreateApp();
        var assignment = await app.Seed();
        var id = scenario == "unknown" ? Guid.NewGuid() : await app.Attachment(
            scenario == "other-owner" ? Guid.NewGuid() : ApiFactory.Student, scenario != "ineligible", scenario == "expired");
        using var client = app.Client(ApiFactory.Student);
        await Error(await client.PostAsJsonAsync(SubmitRoute(assignment.Id), Solution(null, null, id)), 403, "AttachmentNotOwned");
        var work = await Submit(app, assignment);
        await Error(await client.PostAsJsonAsync(MessageRoute(work.ProblemWorkId), Message(null, id)), 403, "AttachmentNotOwned");
        Assert.Empty((await app.Reload(work.ProblemWorkId)).Messages);
    }

    [Fact]
    public async Task Eligible_owned_attachment_only_solution_succeeds()
    {
        await using var app = await postgres.CreateApp();
        var assignment = await app.Seed();
        var id = await app.Attachment(ApiFactory.Student);
        using var client = app.Client(ApiFactory.Student);
        var submitted = await Success<SubmissionResult>(await client.PostAsJsonAsync(SubmitRoute(assignment.Id), Solution(null, null, id)), HttpStatusCode.Created);
        Assert.Equal([id], (await app.Reload(submitted.ProblemWorkId)).Submissions.Single().AttachmentIds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Revision_requires_feedback(string? feedback)
    {
        await using var app = await postgres.CreateApp();
        var submitted = await Submit(app, await app.Seed(studentGroup: "group-a", scopedTeacher: true));
        using var teacher = await app.SecureClient();
        await Error(await teacher.PostAsJsonAsync(ReviewRoute(submitted.ProblemWorkId), Review(submitted, 1, feedback)), 422, "FeedbackRequired");
        var work = await app.Reload(submitted.ProblemWorkId);
        Assert.Empty(work.Reviews);
        Assert.Empty(work.Messages);
        Assert.Equal(1, work.Version);
    }

    [Theory]
    [InlineData(false, null, null, false)]
    [InlineData(true, "group-a", "group-b", false)]
    [InlineData(true, "group-a", null, false)]
    [InlineData(true, "group-a", "group-a", true)]
    [InlineData(true, null, "group-a", true)]
    public async Task Teacher_review_and_message_require_course_or_matching_group(bool grant, string? teacherGroup, string? studentGroup, bool allowed)
    {
        await using var app = await postgres.CreateApp();
        var submitted = await Submit(app, await app.Seed(teacherGrant: grant, teacherGroup: teacherGroup,
            studentGroup: studentGroup, scopedTeacher: true));
        using var teacher = await app.SecureClient();
        var review = await teacher.PostAsJsonAsync(ReviewRoute(submitted.ProblemWorkId), Review(submitted, 1));
        var message = await teacher.PostAsJsonAsync(MessageRoute(submitted.ProblemWorkId), TeacherMessage());
        if (allowed)
        {
            await Success<ReviewResult>(review);
            await Success<MessageResult>(message);
        }
        else
        {
            await Error(review, 403, "Forbidden");
            await Error(message, 403, "Forbidden");
            Assert.Equal(1, (await app.Reload(submitted.ProblemWorkId)).Version);
        }
    }

    [Fact]
    public async Task Student_cannot_message_foreign_work_or_review_own_work()
    {
        await using var app = await postgres.CreateApp();
        var submitted = await Submit(app, await app.Seed(studentGroup: "group-a", scopedTeacher: true));
        using var stranger = app.Client(Guid.NewGuid());
        using var owner = await app.SecureClient("Student");
        await Error(await stranger.PostAsJsonAsync(MessageRoute(submitted.ProblemWorkId), Message()), 403, "Forbidden");
        await Error(await owner.PostAsJsonAsync(ReviewRoute(submitted.ProblemWorkId), Review(submitted, 1)), 403, "Forbidden");
    }

    [Fact]
    public async Task Reply_target_must_belong_to_same_work()
    {
        await using var app = await postgres.CreateApp();
        var first = await Submit(app, await app.Seed());
        var second = await Submit(app, await app.Seed());
        using var client = app.Client(ApiFactory.Student);
        var message = await Success<MessageResult>(await client.PostAsJsonAsync(MessageRoute(first.ProblemWorkId), Message()));
        await Error(await client.PostAsJsonAsync(MessageRoute(second.ProblemWorkId), Message(message.MessageId)), 422, "InvalidReplyTarget");
        Assert.Empty((await app.Reload(second.ProblemWorkId)).Messages);
    }

    [Theory]
    [InlineData("{", 400, "ValidationFailed")]
    [InlineData("{}", 400, "ValidationFailed")]
    [InlineData("{\"attachmentIds\":null}", 400, "ValidationFailed")]
    [InlineData("{\"attachmentIds\":[\"bad-guid\"]}", 400, "ValidationFailed")]
    [InlineData("{\"attachmentIds\":[],\"bodyMarkdown\":\" \"}", 422, "EmptySubmission")]
    [InlineData("{\"attachmentIds\":[],\"expectedVersion\":\"no\"}", 400, "ValidationFailed")]
    [InlineData("{\"attachmentIds\":[],\"bodyMarkdown\":\"text\",\"actorId\":\"forged\"}", 400, "ValidationFailed")]
    public async Task Uniform_validation_contract(string json, int status, string code)
    {
        await using var app = await postgres.CreateApp();
        var assignment = await app.Seed();
        using var client = app.Client(ApiFactory.Student);
        await Error(await client.PostAsync(SubmitRoute(assignment.Id), new StringContent(json, Encoding.UTF8, "application/json")), status, code);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"submissionId\":null,\"expectedVersion\":1}")]
    [InlineData("{\"submissionId\":\"11111111-1111-1111-1111-111111111111\",\"expectedVersion\":null}")]
    public async Task Review_required_fields_are_not_defaulted(string body)
    {
        await using var app = await postgres.CreateApp();
        using var teacher = await app.SecureClient();
        await Error(await teacher.PostAsync(ReviewRoute(Guid.NewGuid()), new StringContent(body, Encoding.UTF8, "application/json")), 400, "ValidationFailed");
    }

    [Fact]
    public async Task Framework_and_missing_entity_errors_share_contract()
    {
        await using var app = await postgres.CreateApp();
        using var anonymous = app.Client();
        using var student = app.Client(ApiFactory.Student);
        await Error(await anonymous.PostAsJsonAsync(SubmitRoute(Guid.NewGuid()), Solution()), 401, "Unauthenticated");
        await Error(await student.PostAsJsonAsync(SubmitRoute(Guid.NewGuid()), Solution()), 404, "NotFound");
        await Error(await student.PostAsJsonAsync(MessageRoute(Guid.NewGuid()), Message()), 404, "NotFound");
        await Error(await student.PostAsJsonAsync("/api/v1/problem-assignments/bad/submissions", Solution()), 400, "ValidationFailed");
        await Error(await student.GetAsync("/api/v1/absent"), 404, "NotFound");
        await Error(await student.GetAsync(SubmitRoute(Guid.NewGuid())), 405, "MethodNotAllowed");
        await Error(await student.PostAsync(SubmitRoute(Guid.NewGuid()), new StringContent("text")), 415, "ValidationFailed");
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("0")]
    [InlineData("Student,Teacher")]
    [InlineData("")]
    public async Task Unsupported_or_ambiguous_roles_fail_closed(string role)
    {
        await using var app = await postgres.CreateApp();
        var assignment = await app.Seed();
        using var client = app.Client(ApiFactory.Student, role);
        await Error(await client.PostAsJsonAsync(SubmitRoute(assignment.Id), Solution()), 403, "Forbidden");
    }

    [Fact]
    public async Task Actor_cannot_be_overridden_by_query_or_headers()
    {
        await using var app = await postgres.CreateApp();
        var assignment = await app.Seed();
        using var stranger = app.Client(Guid.NewGuid());
        stranger.DefaultRequestHeaders.Add("X-Actor-Id", ApiFactory.Student.ToString());
        await Error(await stranger.PostAsJsonAsync(SubmitRoute(assignment.Id) + $"?studentId={ApiFactory.Student}&role=Student", Solution()), 403, "Forbidden");
    }

    [Fact]
    public async Task Production_authentication_rejects_forged_tokens_and_test_headers()
    {
        await using var app = await postgres.CreateApp(testAuthentication: false);
        using var client = app.Client(ApiFactory.Student);
        var route = SubmitRoute(Guid.NewGuid());
        await Error(await client.PostAsJsonAsync(route, Solution()), 401, "Unauthenticated");
        client.DefaultRequestHeaders.Authorization = new("Bearer", "eyJhbGciOiJub25lIn0.eyJzdWIiOiJhZG1pbiJ9.");
        var response = await client.PostAsJsonAsync(route, Solution());
        await Error(response, 401, "Unauthenticated");
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Concurrent_first_submissions_have_one_winner_and_stable_conflict()
    {
        await using var app = await postgres.CreateApp();
        var assignment = await app.Seed();
        using var a = app.Client(ApiFactory.Student);
        using var b = app.Client(ApiFactory.Student);
        app.Barrier.Armed = true;
        var responses = await Task.WhenAll(a.PostAsJsonAsync(SubmitRoute(assignment.Id), Solution()), b.PostAsJsonAsync(SubmitRoute(assignment.Id), Solution()));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        await Error(Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict), 409, "ConcurrencyConflict");
        await using var db = app.Open();
        Assert.Equal(1, await db.Works.CountAsync());
        Assert.Equal(1, await db.Events.CountAsync());
    }

    [Fact]
    public async Task Concurrent_reviews_have_one_winner_and_no_partial_writes()
    {
        await using var app = await postgres.CreateApp();
        var submitted = await Submit(app, await app.Seed(studentGroup: "group-a", scopedTeacher: true));
        using var a = await app.SecureClient();
        using var b = await app.SecureClient();
        app.Barrier.Armed = true;
        var responses = await Task.WhenAll(a.PostAsJsonAsync(ReviewRoute(submitted.ProblemWorkId), Review(submitted, 1)),
            b.PostAsJsonAsync(ReviewRoute(submitted.ProblemWorkId, "accept"), Review(submitted, 1, null)));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        await Error(Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict), 409, "ConcurrencyConflict");
        var work = await app.Reload(submitted.ProblemWorkId);
        Assert.Single(work.Reviews);
        Assert.Equal(2, work.Version);
        Assert.Equal(work.Status == ProblemWorkStatus.NeedsRevision ? 1 : 0, work.Messages.Count);
    }
}
