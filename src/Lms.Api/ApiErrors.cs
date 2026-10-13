using Lms.Application;
using Lms.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
        "ContentUnavailable" or "ContentImportFailed" => (code, 503),
        "AssignmentNotFound" or "ProblemWorkNotFound" or "NotFound" => ("NotFound", 404),
        "AssignmentDoesNotBelongToStudent" or "AssignmentInactive" or "StudentNotAuthorized" or
            "TeacherNotAuthorized" or "Forbidden" => ("Forbidden", 403),
        "AttachmentTooLarge" => (code, 413),
        "UnsupportedAttachmentType" => (code, 415),
        "MalformedAttachment" => ("InvalidAttachment", 400),
        "AttachmentAlreadyAttached" => (code, 409),
        "IdempotencyConflict" or "DuplicateIdentity" or "DuplicateMembership" or "ActivationUnavailable" or "ActivationLocked" or "RecoveryUnavailable" or "RecoveryLocked" or "ApprovalUnavailable" or
            "ProcessingContextConflict" or "ProcessingContextMissing" => (code, 409),
        "ActivationExpired" or "RecoveryExpired" => (code, 410),
        "ActivationUsed" or "RecoveryUsed" => (code, 409),
        "ChallengeRateLimited" or "ChallengeCooldown" => (code, 429),
        "IdentityAmbiguous" or "IndependentApprovalRequired" => (code, 403),
        "InvalidActivationCode" or "ActivationInvalid" or "RecoveryInvalid" => (code, 401),
        "AttachmentNotOwned" => (code, 403),
        "OperationAlreadyAttempted" or "ConcurrencyConflict" or "WorkAlreadySubmitted" or "WorkAlreadyAccepted" or "WorkNotSubmitted" or
            "SubmissionIsNotCurrent" or "SubmissionAlreadyReviewed" => (code, 409),
        "ProcessingRestricted" => (code, 403),
        "InvalidAnnotations" or "EmptySubmission" or "FeedbackRequired" or "InvalidReplyTarget" => (code, 422),
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
                DbUpdateConcurrencyException => ApiErrors.Map("ConcurrencyConflict"),
                DbUpdateException { InnerException: PostgresException { SqlState: "40001" or "40P01" or "23505" } } =>
                    ApiErrors.Map("ConcurrencyConflict"),
                InvalidOperationException { InnerException: PostgresException { SqlState: "40001" or "40P01" or "23505" } } =>
                    ApiErrors.Map("ConcurrencyConflict"),
                PostgresException { SqlState: "40001" or "40P01" or "23505" } => ApiErrors.Map("ConcurrencyConflict"),
                BadHttpRequestException { StatusCode: 413 } when context.Request.Path == "/api/v1/uploads" => ("AttachmentTooLarge", 413),
                BadHttpRequestException e => ("ValidationFailed", e.StatusCode),
                _ => ("InternalError", 500)
            };
            if (status == 500) logger.LogError(exception, "Unhandled command failure");
            await ApiErrors.Write(context, code, status);
        }
    }
}
