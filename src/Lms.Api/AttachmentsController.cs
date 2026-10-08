using Lms.Application;
using Lms.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Lms.Api;

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class AttachmentsController(AttachmentService service, AttachmentOptions options, IActorMapper actors) : ControllerBase
{
    [HttpPost("uploads")]
    [AttachmentFormBoundary]
    public async Task<ActionResult<AttachmentResult>> Upload(CancellationToken ct)
    {
        var actor = actors.Map(User);
        if (!Request.HasFormContentType || !Request.ContentType!.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            throw new UseCaseException("UnsupportedAttachmentType");
        // The transport envelope is bounded independently of any claimed file length.
        var limit = checked(options.MaxBytes + 64 * 1024);
        var feature = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = limit;
        if (Request.ContentLength > limit) throw new UseCaseException("AttachmentTooLarge");
        IFormCollection form;
        try { form = await Request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = limit, ValueLengthLimit = 1024 }, ct); }
        catch (InvalidDataException) { throw new UseCaseException("MalformedAttachment"); }
        catch (IOException e) when (e is not BadHttpRequestException && !ct.IsCancellationRequested)
        { throw new UseCaseException("MalformedAttachment"); }
        if (form.Count != 0 || form.Files.Count != 1 || form.Files[0].Name != "file") throw new UseCaseException("MalformedAttachment");
        var file = form.Files[0];
        if (file.Length > options.MaxBytes) throw new UseCaseException("AttachmentTooLarge");
        await using var source = file.OpenReadStream();
        var result = await service.Upload(actor, file.FileName, file.ContentType, source, ct);
        return Created(result.PreviewUrl, result);
    }

    [HttpGet("attachments/{id:guid}/content")]
    public async Task<IActionResult> Content(Guid id, CancellationToken ct)
    {
        var result = await service.Open(actors.Map(User), id, ct);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(result.Content, result.MediaType);
    }

    [HttpDelete("attachments/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.Delete(actors.Map(User), id, ct);
        return NoContent();
    }
}

// MVC must not consume multipart before this action can apply its transport limit and
// error contract. Actor identity and the single file are read explicitly by the action.
[AttributeUsage(AttributeTargets.Method)]
public sealed class AttachmentFormBoundaryAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        for (var i = context.ValueProviderFactories.Count - 1; i >= 0; i--)
            if (context.ValueProviderFactories[i] is FormValueProviderFactory or FormFileValueProviderFactory or JQueryFormValueProviderFactory)
                context.ValueProviderFactories.RemoveAt(i);
    }
    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
