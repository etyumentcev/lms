using System.ComponentModel.DataAnnotations;
using Lms.Application;
using Lms.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lms.Api;

public sealed record SubmitRequest(string? BodyMarkdown, [Required] Guid[] AttachmentIds, long? ExpectedVersion);
public sealed record MessageRequest([Required] string BodyMarkdown, Guid? ReplyToMessageId, [Required] Guid[] AttachmentIds);
public sealed record ReviewRequest([Required] Guid? SubmissionId, string? FeedbackMarkdown, [Required] long? ExpectedVersion, AnnotatedAttachment[]? Annotations = null);

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class CommandsController(ProblemWorkService service, IActorMapper actors) : ControllerBase
{
    [HttpPost("problem-assignments/{assignmentId}/submissions")]
    public async Task<ActionResult<SubmissionResult>> Submit(Guid assignmentId, SubmitRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await service.SubmitSolution(actors.Map(User), assignmentId,
            request.BodyMarkdown, request.AttachmentIds, request.ExpectedVersion, cancellationToken));

    [HttpPost("problem-works/{problemWorkId}/messages")]
    public async Task<ActionResult<MessageResult>> Message(Guid problemWorkId, MessageRequest request, CancellationToken cancellationToken)
        => Ok(await service.PostMessage(actors.Map(User), problemWorkId, request.BodyMarkdown,
            request.ReplyToMessageId, request.AttachmentIds, cancellationToken));

    [RequestSizeLimit(AnnotationRules.MaxRequestBytes)]
    [HttpPost("problem-works/{problemWorkId}/reviews/needs-revision")]
    public async Task<ActionResult<ReviewResult>> Revise(Guid problemWorkId, ReviewRequest request, CancellationToken cancellationToken)
        => Ok(await service.RequestRevision(actors.Map(User), problemWorkId, request.SubmissionId!.Value,
            request.FeedbackMarkdown ?? "", request.ExpectedVersion!.Value, cancellationToken, request.Annotations));

    [RequestSizeLimit(AnnotationRules.MaxRequestBytes)]
    [HttpPost("problem-works/{problemWorkId}/reviews/accept")]
    public async Task<ActionResult<ReviewResult>> Accept(Guid problemWorkId, ReviewRequest request, CancellationToken cancellationToken)
        => Ok(await service.AcceptSubmission(actors.Map(User), problemWorkId, request.SubmissionId!.Value,
            request.FeedbackMarkdown, request.ExpectedVersion!.Value, cancellationToken, request.Annotations));
}
