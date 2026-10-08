namespace Lms.Infrastructure;

public sealed class StoredAttachment
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string FileName { get; set; } = null!;
    public string MediaType { get; set; } = null!;
    public long Size { get; set; }
    public string StorageKey { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? SubmissionId { get; set; }
    public Guid? MessageId { get; set; }
    public bool Deleted { get; set; }
}
