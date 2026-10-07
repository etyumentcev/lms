using Lms.Application;
using Lms.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Lms.Api;

public static class ApiErrors
{
    public static ProblemDetails Create(HttpContext context, string code, int status)
        => new()
        {
            Type = "urn:lms:error:" + code,
            Title = code,
            Status = status,
            Instance = context.Request.Path,
            Extensions = { ["code"] = code, ["traceId"] = context.TraceIdentifier }
        };

    public static (string Code, int Status) Map(string code) => code switch
    {
        "Unauthenticated" => (code, 401),
        "AssignmentNotFound" or "ProblemWorkNotFound" or "NotFound" => ("NotFound", 404),
        "AssignmentDoesNotBelongToStudent" or "AssignmentInactive" or "StudentNotAuthorized" or
            "TeacherNotAuthorized" or "Forbidden" => ("Forbidden", 403),
        "AttachmentNotOwned" => (code, 403),
        "ConcurrencyConflict" or "WorkAlreadySubmitted" or "WorkAlreadyAccepted" or "WorkNotSubmitted" or
            "SubmissionIsNotCurrent" or "SubmissionAlreadyReviewed" => (code, 409),
        "EmptySubmission" or "FeedbackRequired" or "InvalidReplyTarget" => (code, 422),
        "InvalidId" or "EmptyValue" or "EmptyMessage" or "InvalidAttachment" or "InvalidAuthorRole" => ("ValidationFailed", 422),
        "ValidationFailed" => (code, 400),
        _ => ("InternalError", 500)
    };

    public static Task Write(HttpContext context, string code, int status)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(Create(context, code, status),
            options: null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
    }
}

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            var (code, status) = exception switch
            {
                UseCaseException e => ApiErrors.Map(e.Code),
                DomainException e => ApiErrors.Map(e.Code),
                BadHttpRequestException e => ("ValidationFailed", e.StatusCode),
                _ => ("InternalError", 500)
            };
            if (status == 500) logger.LogError(exception, "Unhandled command failure");
            await ApiErrors.Write(context, code, status);
        }
    }
}
