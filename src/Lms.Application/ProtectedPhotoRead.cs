namespace Lms.Application;

public sealed record PhotoSelection(WorkSelection Work, Guid SubmissionId, Guid? AttachmentId = null);
public sealed record WorkPhotoDto(Guid Id, string Href);
public sealed record WorkPhotosDto(IReadOnlyList<WorkPhotoDto> Items);
public sealed record ProtectedPhotoRead(object Value, ProtectedWorkRead Work, PhotoSelection Selection,
    IReadOnlyList<Guid> AttachmentIds);
public sealed record PhotoBytes(byte[] Bytes, string MediaType);
public interface IProtectedPhotoQueries
{
    Task<ProtectedPhotoRead> Read(TokenAuthority token, PhotoSelection selection, ApplicationDeadline deadline, CancellationToken ct);
    Task<bool> CanReturn(TokenAuthority token, ProtectedPhotoRead read, ApplicationDeadline deadline, CancellationToken ct);
}
