using System.Diagnostics;
using Lms.Infrastructure;

namespace Lms.Infrastructure.Tests;

// G02 fault-injection evidence, not an authorization or production-filesystem test.
public sealed class AttachmentDeadlineBoundaryTests
{
    [LinuxFact]
    public async Task Cancellation_does_not_bound_an_already_entered_storage_open()
    {
        var root = Path.Combine(Path.GetTempPath(), "lms-s4-deadline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var key = Guid.NewGuid().ToString("N");
        var path = Path.Combine(root, key);
        using var mkfifo = Process.Start(new ProcessStartInfo("mkfifo")
        {
            ArgumentList = { path }, UseShellExecute = false
        })!;
        await mkfifo.WaitForExitAsync();
        Assert.Equal(0, mkfifo.ExitCode);
        var storage = new LocalFileAttachmentStorage(new() { Root = root });
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opening = Task.Run(async () =>
        {
            entered.SetResult();
            return await storage.Open(key, cancellation.Token);
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // With no FIFO writer, the native read-open cannot return.
            await Task.Delay(200);
            Assert.False(opening.IsCompleted);
            cancellation.Cancel();
            var observation = Task.Delay(300);
            Assert.Same(observation, await Task.WhenAny(opening, observation));
            Assert.True(cancellation.IsCancellationRequested);
        }
        finally
        {
            // O_RDWR opens the synthetic FIFO without waiting for a peer and releases
            // the read-open. Always release and join; never leave a blocked worker.
            using var release = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            try { using var opened = await opening.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
            Directory.Delete(root, recursive: true);
        }
    }
}

public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute()
    {
        if (!OperatingSystem.IsLinux()) Skip = "Requires the Linux mkfifo fault fixture.";
    }
}
