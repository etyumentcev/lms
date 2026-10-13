using Lms.Infrastructure;
namespace Lms.Infrastructure.Tests;
public sealed class PrivatePhotoStorageTests
{
    private sealed class InterruptedSource(TaskCompletionSource entered, TaskCompletionSource release) : MemoryStream(new byte[] { 1, 2, 3 })
    {
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken ct)
        { await destination.WriteAsync(new byte[] { 1 }, ct); entered.SetResult(); await release.Task; throw new IOException("synthetic write failure"); }
    }
    [Fact]
    public async Task Unfinished_or_failed_upload_never_opens_and_existing_key_never_overwrites()
    {
        var root = Path.Combine(Path.GetTempPath(), "private-photo-" + Guid.NewGuid().ToString("N"));
        var storage = new LocalFileAttachmentStorage(new() { Root = root }); var key = Guid.NewGuid().ToString("N");
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        try
        {
            using var source = new InterruptedSource(entered, release);
            var pending = storage.Store(key, source, CancellationToken.None); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<FileNotFoundException>(() => storage.Open(key, CancellationToken.None));
            release.SetResult(); await Assert.ThrowsAsync<IOException>(() => pending);
            Assert.Empty(Directory.GetFiles(root));
            await storage.Store(key, new MemoryStream([7, 8]), CancellationToken.None);
            await Assert.ThrowsAsync<IOException>(() => storage.Store(key, new MemoryStream([9]), CancellationToken.None));
            await using var opened = await storage.Open(key, CancellationToken.None); using var bytes = new MemoryStream(); await opened.CopyToAsync(bytes);
            Assert.Equal(new byte[] { 7, 8 }, bytes.ToArray()); Assert.Single(Directory.GetFiles(root));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(root));
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(root, key)));
            }
            await Assert.ThrowsAsync<ArgumentException>(() => storage.Open("../escape", CancellationToken.None));
        }
        finally { release.TrySetResult(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class CancelAfterCopy(CancellationTokenSource stop) : MemoryStream(new byte[] { 1, 2, 3 })
    {
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken ct)
        { await base.CopyToAsync(destination, bufferSize, ct); stop.Cancel(); }
    }
    [Fact]
    public async Task Cancelled_staging_is_not_published_or_deleted_by_the_expired_operation()
    {
        var root = Path.Combine(Path.GetTempPath(), "cancelled-photo-" + Guid.NewGuid().ToString("N"));
        var storage = new LocalFileAttachmentStorage(new() { Root = root }); var key = Guid.NewGuid().ToString("N");
        using var stop = new CancellationTokenSource(); using var source = new CancelAfterCopy(stop);
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.Store(key, source, stop.Token));
            Assert.False(File.Exists(Path.Combine(root, key)));
            Assert.Single(Directory.GetFiles(root, "*.tmp"));
            await Assert.ThrowsAsync<FileNotFoundException>(() => storage.Open(key, CancellationToken.None));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

}
