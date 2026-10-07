using System.Text.Json;
using Lms.Api;
using Lms.Application;
using Lms.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lms.Api.Tests;

public sealed class ErrorMappingContractTests
{
    // Middleware contract test, not a fabricated HTTP route or invalid persisted aggregate.
    // SubmissionAlreadyReviewed is defensive: valid workflows hit version/state/current guards first.
    [Theory]
    [InlineData("ValidationFailed", 400, "ValidationFailed")]
    [InlineData("Forbidden", 403, "Forbidden")]
    [InlineData("AssignmentNotFound", 404, "NotFound")]
    [InlineData("ProblemWorkNotFound", 404, "NotFound")]
    [InlineData("AssignmentInactive", 403, "Forbidden")]
    [InlineData("AssignmentDoesNotBelongToStudent", 403, "Forbidden")]
    [InlineData("StudentNotAuthorized", 403, "Forbidden")]
    [InlineData("TeacherNotAuthorized", 403, "Forbidden")]
    [InlineData("ConcurrencyConflict", 409, "ConcurrencyConflict")]
    [InlineData("WorkAlreadySubmitted", 409, "WorkAlreadySubmitted")]
    [InlineData("WorkAlreadyAccepted", 409, "WorkAlreadyAccepted")]
    [InlineData("WorkNotSubmitted", 409, "WorkNotSubmitted")]
    [InlineData("SubmissionIsNotCurrent", 409, "SubmissionIsNotCurrent")]
    [InlineData("SubmissionAlreadyReviewed", 409, "SubmissionAlreadyReviewed")]
    [InlineData("EmptySubmission", 422, "EmptySubmission")]
    [InlineData("FeedbackRequired", 422, "FeedbackRequired")]
    [InlineData("AttachmentNotOwned", 403, "AttachmentNotOwned")]
    [InlineData("InvalidReplyTarget", 422, "InvalidReplyTarget")]
    [InlineData("EmptyMessage", 422, "ValidationFailed")]
    [InlineData("InvalidAttachment", 422, "ValidationFailed")]
    [InlineData("ContentUnavailable", 503, "ContentUnavailable")]
    public async Task Known_application_and_domain_failures_have_stable_problem_details(string input, int status, string code)
    {
        using var services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        foreach (var exception in new Exception[] { new DomainException(input), new UseCaseException(input) })
        {
            var context = new DefaultHttpContext { RequestServices = services };
            context.Request.Path = "/api/v1/contract-check";
            await using var body = new MemoryStream();
            context.Response.Body = body;
            var middleware = new ApiExceptionMiddleware(_ => Task.FromException(exception), NullLogger<ApiExceptionMiddleware>.Instance);
            await middleware.InvokeAsync(context);
            Assert.Equal(status, context.Response.StatusCode);
            Assert.StartsWith("application/problem+json", context.Response.ContentType);
            body.Position = 0;
            using var json = await JsonDocument.ParseAsync(body);
            Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
            Assert.Equal(status, json.RootElement.GetProperty("status").GetInt32());
            Assert.Equal("urn:lms:error:" + code, json.RootElement.GetProperty("type").GetString());
            Assert.Equal(context.Request.Path.Value, json.RootElement.GetProperty("instance").GetString());
        }
    }
}
