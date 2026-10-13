using System.Diagnostics;
using Lms.Infrastructure;

namespace Lms.Infrastructure.Tests;

// G02 fault-injection evidence, not an authorization or production-filesystem test.
public sealed class AttachmentDeadlineBoundaryTests
{
    // Candidate isolation probe only: FIFO sleep is interruptible, unlike arbitrary disk I/O.
    [LinuxFact]
    public async Task Isolated_FIFO_reader_can_be_killed_and_reaped_without_releasing_IO()
    {
        var root = Path.Combine(Path.GetTempPath(), "lms-s4-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "synthetic-fifo");
        Process? reader = null;
        try
        {
            using var mkfifo = Process.Start(new ProcessStartInfo("mkfifo")
            {
                ArgumentList = { path }, UseShellExecute = false
            })!;
            await mkfifo.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, mkfifo.ExitCode);
            reader = Process.Start(new ProcessStartInfo("cat")
            {
                ArgumentList = { path }, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true
            })!;
            // Observe entry into the kernel FIFO open, not merely process startup.
            var entry = Stopwatch.StartNew();
            while (!File.ReadAllText($"/proc/{reader.Id}/wchan").Contains("wait_for_partner"))
            {
                Assert.False(reader.HasExited);
                Assert.True(entry.Elapsed < TimeSpan.FromSeconds(5), "FIFO open was not observed");
                await Task.Delay(10);
            }
            var termination = Stopwatch.StartNew();
            reader.Kill();
            await reader.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(reader.HasExited);
            Assert.NotEqual(0, reader.ExitCode);
            Assert.True(termination.Elapsed < TimeSpan.FromSeconds(5));
            Assert.Empty(await reader.StandardOutput.ReadToEndAsync());
        }
        finally
        {
            if (reader is not null)
            {
                if (!reader.HasExited) reader.Kill();
                await reader.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                reader.Dispose();
            }
            Directory.Delete(root, recursive: true);
        }
    }

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
