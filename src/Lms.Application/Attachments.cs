namespace Lms.Application;

// Binary storage is independent of HTTP, EF and the domain aggregate.
public interface IAttachmentStorage
{
    Task Store(string key, Stream content, CancellationToken cancellationToken);
    Task<Stream> Open(string key, CancellationToken cancellationToken);
    Task Delete(string key, CancellationToken cancellationToken);
}

public sealed class AttachmentOptions
{
    public long MaxBytes { get; set; } = 10 * 1024 * 1024;
}

public sealed record AttachmentResult(Guid AttachmentId, string FileName, string MediaType,
    long Size, DateTimeOffset CreatedAt, string PreviewUrl);
