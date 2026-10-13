using System.Diagnostics;
using System.Text.Json;

namespace Lms.Infrastructure;

public sealed class ContentImportRuntimeOptions
{
    public string RepositoryRoot { get; init; } = string.Empty;
    public string PythonPath { get; init; } = "/usr/bin/python3";
    public string NodePath { get; init; } = string.Empty;
    public string KatexPath { get; init; } = string.Empty;
    public string StorageRoot { get; init; } = string.Empty;
    public long MaxZipBytes { get; init; } = 20_000_000;

    public void Validate()
    {
        foreach (var path in new[] { RepositoryRoot, PythonPath, NodePath, KatexPath, StorageRoot })
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                throw new InvalidOperationException("ContentImport paths must be absolute.");
        if (MaxZipBytes is <= 0 or > 100_000_000) throw new InvalidOperationException("Invalid ContentImport MaxZipBytes.");
    }
}

internal sealed record ImportArtifactWire(string Json, Dictionary<string, string> Assets);
internal sealed record ImportWire(string Status, JsonElement[] Diagnostics, JsonElement? Receipt,
    ImportArtifactWire? Student, ImportArtifactWire? Teacher);
internal sealed record ImportedArtifact(string Json, IReadOnlyDictionary<string, byte[]> Assets);
internal sealed record ImportExecution(string Status, JsonElement[] Diagnostics, string? ReceiptJson,
    ImportedArtifact? Student, ImportedArtifact? Teacher);

internal sealed class ContentImportRunner(ContentImportRuntimeOptions options)
{
    private const int OutputLimit = 64_000_000;

    public async Task<ImportExecution> Run(string zipPath, string sourceRevision, CancellationToken ct)
    {
        options.Validate();
        var bridge = Path.Combine(options.RepositoryRoot, "tools", "content_import", "server_bridge.py");
        if (!File.Exists(bridge) || !File.Exists(options.PythonPath) || !File.Exists(options.NodePath) ||
            !File.Exists(Path.Combine(options.KatexPath, "package.json")))
            throw new InvalidOperationException("Content import runtime is unavailable.");
        var start = new ProcessStartInfo(options.PythonPath)
        {
            WorkingDirectory = options.RepositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-B"); start.ArgumentList.Add(bridge); start.ArgumentList.Add(zipPath);
        start.ArgumentList.Add(options.NodePath); start.ArgumentList.Add(options.KatexPath); start.ArgumentList.Add(sourceRevision);
        start.Environment["PYTHONPATH"] = options.RepositoryRoot;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Content import runtime did not start.");
        var outputTask = ReadBounded(process.StandardOutput.BaseStream, OutputLimit, ct);
        var errorTask = Drain(process.StandardError.BaseStream, ct);
        try { await process.WaitForExitAsync(ct); }
        catch
        {
            try { process.Kill(true); } catch { }
            throw;
        }
        var bytes = await outputTask; await errorTask;
        if (process.ExitCode != 0) throw new InvalidOperationException("Content import runtime failed.");
        var wire = JsonSerializer.Deserialize<ImportWire>(bytes, PostgresContentResolver.Json)
            ?? throw new InvalidOperationException("Content import returned no result.");
        static ImportedArtifact? Decode(ImportArtifactWire? value) => value == null ? null : new(
            System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value.Json)),
            value.Assets.ToDictionary(x => x.Key, x => Convert.FromBase64String(x.Value), StringComparer.Ordinal));
        return new(wire.Status, wire.Diagnostics, wire.Receipt?.GetRawText(), Decode(wire.Student), Decode(wire.Teacher));
    }

    private static async Task<byte[]> ReadBounded(Stream stream, int limit, CancellationToken ct)
    {
        using var output = new MemoryStream(); var buffer = new byte[65536];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct); if (read == 0) break;
            if (output.Length + read > limit) throw new InvalidOperationException("Content import output exceeded its limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return output.ToArray();
    }

    private static async Task Drain(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[4096]; while (await stream.ReadAsync(buffer, ct) != 0) { }
    }
}
