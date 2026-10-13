using Lms.Application;

namespace Lms.Infrastructure;

public sealed class LocalAttachmentStorageOptions
{
    public string Root { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lms", "attachments");
}

public sealed class LocalFileAttachmentStorage(LocalAttachmentStorageOptions options) : IAttachmentStorage
{
    private string Location(string key)
    {
        if (!Guid.TryParseExact(key, "N", out _)) throw new ArgumentException("Invalid storage key", nameof(key));
        return Path.Combine(Path.GetFullPath(options.Root), key);
    }

    public async Task Store(string key, Stream content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = Location(key);
        var directory = Path.GetDirectoryName(target)!;
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var fileOptions = new FileStreamOptions {
                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                BufferSize = 81920, Options = FileOptions.Asynchronous | FileOptions.WriteThrough };
            if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var file = new FileStream(temporary, fileOptions))
            {
                await content.CopyToAsync(file, cancellationToken);
                await file.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: false);
        }
        // Cancelled staging remains private for separately bounded cleanup; do not
        // start a new filesystem delete after the caller's execution cutoff.
        finally { if (!cancellationToken.IsCancellationRequested && File.Exists(temporary)) File.Delete(temporary); }
    }

    public Task<Stream> Open(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(new FileStream(Location(key), FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Delete, 81920, FileOptions.Asynchronous));
    }

    public Task Delete(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(Location(key));
        return Task.CompletedTask;
    }
}
