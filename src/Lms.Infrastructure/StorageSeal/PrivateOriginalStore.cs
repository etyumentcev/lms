using System.Security.Cryptography;

namespace Lms.Infrastructure.StorageSeal;

internal enum OriginalCondition { Unverified, Pending, Unknown, Quarantined, Missing, Damaged, Confirmed }
internal sealed record OriginalInspection(OriginalCondition Condition, SealProof? Proof = null);

// Deliberately internal, not IAttachmentStorage and not a user upload/read API.
// Physical confirmation conveys ZERO authority. No path/bytes/download URL is returned.
internal sealed class PrivateOriginalStore
{
    // No in-memory waiting queue; stalled I/O retains a slot. Single-coordinator profile.
    private static readonly object AdmissionLock = new();
    private static readonly Dictionary<Guid, int> ActiveOrganizations = new();
    private static int active;
    private sealed class Admission(Guid organization) : IDisposable
    {
        public void Dispose()
        {
            lock (AdmissionLock)
            {
                active--;
                if (--ActiveOrganizations[organization] == 0) ActiveOrganizations.Remove(organization);
            }
        }
    }
    private static IDisposable Enter(Guid organization)
    {
        lock (AdmissionLock)
        {
            ActiveOrganizations.TryGetValue(organization, out var count);
            if (active >= 8 || count >= 2) throw new IOException("Storage executor capacity unavailable");
            active++; ActiveOrganizations[organization] = count + 1;
            return new Admission(organization);
        }
    }
    private readonly SealLedger ledger;
    private readonly IOriginalSealKernel kernel;
    private readonly string root;
    private readonly long epoch;
    internal PrivateOriginalStore(SealLedger ledger, IOriginalSealKernel kernel, string root, long epoch)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        this.root = Path.GetFullPath(root);
        var dir = new DirectoryInfo(this.root);
        if (!dir.Exists || dir.LinkTarget != null || File.GetUnixFileMode(this.root) !=
            (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute))
            throw new IOException("Pre-provisioned private 0700 root required");
        this.ledger = ledger; this.kernel = kernel; this.epoch = epoch;
    }
    private string DirectoryFor(SealReservation r) => Path.Combine(root, r.Operation.ToString("N") + "-" + r.Generation.ToString("N"));
    internal async Task<SealProof> Write(Guid operation, Guid organization, Stream source, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        using var admission = Enter(organization);
        // Only the positive durable reservation ACK permits filesystem operations.
        var r = await ledger.Reserve(operation, organization, epoch, ct);
        var directory = DirectoryFor(r);
        try
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            kernel.SyncDirectory(root);
            var path = Path.Combine(directory, "original");
            // Exactly one owned writer, sequential awaited writes; no writable handle escapes.
            await using (var writer = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.Asynchronous))
            {
                var buffer = new byte[65536];
                long length = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) != 0)
                {
                    if (read > SealLedger.MaximumOriginalBytes - length) throw new IOException("Original too large");
                    await writer.WriteAsync(buffer.AsMemory(0, read), ct);
                    length += read;
                }
                if (length == 0) throw new IOException("Empty original");
                await writer.FlushAsync(ct);
                ct.ThrowIfCancellationRequested();
                kernel.Sync(writer.SafeFileHandle);
            } // awaited disposal; no subsequent write tasks
            ct.ThrowIfCancellationRequested();
            using var reader = Open(path);
            kernel.Enable(reader.SafeFileHandle); // kernel rejects retained writable fd/mmap
            ct.ThrowIfCancellationRequested();
            kernel.Sync(reader.SafeFileHandle);
            ct.ThrowIfCancellationRequested();
            kernel.SyncDirectory(directory);
            var proof = new SealProof(reader.Length, Convert.ToHexString(await SHA256.HashDataAsync(reader, ct)),
                kernel.Measure(reader.SafeFileHandle));
            ct.ThrowIfCancellationRequested();
            await ledger.Confirm(r, proof, ct); // no PG transaction held over filesystem work
            return proof;
        }
        catch
        {
            // No delete, timeout release or new generation. If marking is unavailable,
            // durable PhysicalPending remains deny and startup marks it OutcomeUnknown.
            // A lost confirmation ACK does not erase an already committed Sealed fact.
            try { await ledger.Mark(operation, "OutcomeUnknown", ct); } catch { /* accounting remains reserved */ }
            throw;
        }
    }
    private static FileStream Open(string path)
    {
        var info = new FileInfo(path);
        if (info.LinkTarget != null) throw new IOException("Symlink denied");
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
    }
    internal async Task<OriginalInspection> Inspect(Guid operation, Guid organization, CancellationToken ct = default)
    {
        using var admission = Enter(organization);
        // DB unavailable throws: callers MUST deny, never substitute local metadata.
        var item = await ledger.Find(operation, organization, ct);
        if (item is null) return new(OriginalCondition.Unverified);
        if (item.State != "Sealed") return new(item.State switch
        {
            "PhysicalPending" => OriginalCondition.Pending,
            "OutcomeUnknown" => OriginalCondition.Unknown,
            _ => OriginalCondition.Quarantined
        });
        if (item.Proof is null || item.PhysicalPending) return new(OriginalCondition.Damaged);
        try
        {
            using var reader = Open(Path.Combine(DirectoryFor(item.Reservation), "original"));
            if (reader.Length != item.Proof.Length || kernel.Measure(reader.SafeFileHandle) != item.Proof.Version ||
                Convert.ToHexString(await SHA256.HashDataAsync(reader, ct)) != item.Proof.Sha256)
                return new(OriginalCondition.Damaged);
            return new(OriginalCondition.Confirmed, item.Proof);
        }
        catch (FileNotFoundException) { return new(OriginalCondition.Missing); }
        catch (DirectoryNotFoundException) { return new(OriginalCondition.Missing); }
        catch (IOException) { return new(OriginalCondition.Damaged); }
        catch (UnauthorizedAccessException) { return new(OriginalCondition.Damaged); }
    }
}
