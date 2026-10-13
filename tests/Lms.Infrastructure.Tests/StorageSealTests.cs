using System.Diagnostics;
using System.Security.Cryptography;
using Lms.Infrastructure.StorageSeal;
using Microsoft.Win32.SafeHandles;
using Npgsql;
using Xunit.Abstractions;

namespace Lms.Infrastructure.Tests;

// Model positives test the protocol, not fs-verity support or power-loss durability.
public sealed class StorageSealTests(PostgresFixture postgres, ITestOutputHelper output) : IClassFixture<PostgresFixture>
{
    private sealed class ModelKernel : IOriginalSealKernel
    {
        public Action? AtEnable { get; set; }
        public Action? AtSync { get; set; }
        public string Version { get; set; } = new('A', 64);
        public void Enable(SafeFileHandle file) => AtEnable?.Invoke();
        public string Measure(SafeFileHandle file) => Version;
        public void Sync(SafeFileHandle file) => AtSync?.Invoke();
        public void SyncDirectory(string directory) { }
    }
    private sealed class Rig(string cs, string root, SealLedger ledger, long epoch, IOriginalSealKernel kernel) : IDisposable
    {
        internal string ConnectionString => cs;
        internal string Root => root;
        internal SealLedger Ledger => ledger;
        internal long Epoch => epoch;
        internal PrivateOriginalStore Store => new(ledger, kernel, root, epoch);
        public void Dispose() => Directory.Delete(root, true); // TEST fixture teardown only
        internal async Task<string> PathFor(Guid op, Guid org)
        {
            var row = (await ledger.Find(op, org))!;
            return Path.Combine(root, op.ToString("N") + "-" + row.Reservation.Generation.ToString("N"), "original");
        }
    }
    private async Task<Rig> Create(IOriginalSealKernel? kernel = null, string? temporaryBase = null)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var cs = await postgres.CreateDatabase(); // full existing migration chain, disposable PG
        await Sql(cs, SealLedger.Schema);
        var root = Path.Combine(temporaryBase ?? Path.GetTempPath(), "lms-seal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var ledger = new SealLedger(cs);
        return new(cs, root, ledger, await ledger.Recover(), kernel ?? new ModelKernel());
    }
    private static async Task<int> Sql(string cs, string text)
    {
        await using var c = new NpgsqlConnection(cs);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(text, c);
        return await cmd.ExecuteNonQueryAsync();
    }
    private static MemoryStream Page(int number = 1) => new(System.Text.Encoding.UTF8.GetBytes("SYNTHETIC PAGE " + number));

    [Fact]
    public async Task Model_three_pages_have_unique_generations_and_exact_hash_length()
    {
        using var rig = await Create();
        var org = Guid.NewGuid();
        var paths = new HashSet<string>();
        for (var page = 1; page <= 3; page++)
        {
            var op = Guid.NewGuid();
            using var source = Page(page);
            var expected = SHA256.HashData(source.ToArray());
            var proof = await rig.Store.Write(op, org, source);
            Assert.Equal(source.Length, proof.Length);
            Assert.Equal(Convert.ToHexString(expected), proof.Sha256);
            Assert.Equal(OriginalCondition.Confirmed, (await rig.Store.Inspect(op, org)).Condition);
            Assert.True(paths.Add(await rig.PathFor(op, org)));
            Assert.Equal(OriginalCondition.Unverified, (await rig.Store.Inspect(op, Guid.NewGuid())).Condition);
            await Assert.ThrowsAsync<PostgresException>(() => rig.Store.Write(op, org, Page()));
        }
        Assert.Equal(3, Directory.GetDirectories(rig.Root).Length);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("length")]
    [InlineData("version")]
    [InlineData("missing")]
    [InlineData("quarantine")]
    public async Task Model_confirmed_original_fails_closed_when_evidence_changes(string fault)
    {
        var kernel = new ModelKernel();
        using var rig = await Create(kernel);
        var op = Guid.NewGuid(); var org = Guid.NewGuid();
        await rig.Store.Write(op, org, Page());
        var path = await rig.PathFor(op, org);
        switch (fault)
        {
            case "hash": await File.WriteAllTextAsync(path, "SYNTHETIC PAGE 2"); break;
            case "length": await File.AppendAllTextAsync(path, "corrupt"); break;
            case "version": kernel.Version = new('B', 64); break;
            case "missing": File.Delete(path); break;
            case "quarantine": await rig.Ledger.Mark(op, "Quarantined"); break;
        }
        var expected = fault switch { "missing" => OriginalCondition.Missing, "quarantine" => OriginalCondition.Quarantined, _ => OriginalCondition.Damaged };
        Assert.Equal(expected, (await rig.Store.Inspect(op, org)).Condition);
    }

    [Theory]
    [InlineData("durability")]
    [InlineData("retained-writer")]
    [InlineData("before-confirmation")]
    public async Task Model_fault_never_confirms_and_never_releases_reservation(string fault)
    {
        var kernel = new ModelKernel();
        if (fault == "durability") kernel.AtSync = () => throw new IOException("injected fsync failure");
        else kernel.AtEnable = () => throw new IOException(fault);
        using var rig = await Create(kernel);
        var op = Guid.NewGuid(); var org = Guid.NewGuid();
        await Assert.ThrowsAsync<IOException>(() => rig.Store.Write(op, org, Page()));
        var entry = (await rig.Ledger.Find(op, org))!;
        Assert.Equal("OutcomeUnknown", entry.State);
        Assert.True(entry.PhysicalPending);
        Assert.Null(entry.Proof);
        await rig.Ledger.Recover();
        Assert.Equal(OriginalCondition.Unknown, (await rig.Store.Inspect(op, org)).Condition);
        await Assert.ThrowsAsync<IOException>(() => rig.Ledger.Reserve(Guid.NewGuid(), org, rig.Epoch + 1, default));
        Assert.Single(Directory.GetDirectories(rig.Root));
        Assert.Single(Directory.GetFiles(rig.Root, "original", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Model_retained_writer_is_closed_before_kernel_enable_and_postseal_write_is_not_issued()
    {
        var kernel = new ModelKernel();
        using var rig = await Create(kernel);
        var enabled = false;
        kernel.AtEnable = () =>
        {
            var path = Directory.GetFiles(rig.Root, "original", SearchOption.AllDirectories).Single();
            // .NET FileShare lock proves only this implementation closed its writer;
            // the separate native profile probe covers why this alone is not a seal.
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            enabled = true;
        };
        var op = Guid.NewGuid(); var org = Guid.NewGuid();
        await rig.Store.Write(op, org, Page());
        Assert.True(enabled);
        await Assert.ThrowsAsync<PostgresException>(() => rig.Store.Write(op, org, Page(2)));
        Assert.Equal("SYNTHETIC PAGE 1", await File.ReadAllTextAsync(await rig.PathFor(op, org)));
    }

    [Fact]
    public async Task Restart_fences_old_worker_and_unknown_cannot_be_promoted_even_with_plausible_proof()
    {
        using var rig = await Create();
        var op = Guid.NewGuid(); var org = Guid.NewGuid();
        var reservation = await rig.Ledger.Reserve(op, org, rig.Epoch, default);
        Assert.Equal(OriginalCondition.Pending, (await rig.Store.Inspect(op, org)).Condition);
        await rig.Ledger.Recover();
        var proof = new SealProof(5, new('A', 64), new('B', 64));
        await Assert.ThrowsAsync<IOException>(() => rig.Ledger.Confirm(reservation, proof, default));
        await Assert.ThrowsAsync<PostgresException>(() => Sql(rig.ConnectionString,
            $"UPDATE storage_seal.original SET state='Sealed',physical_pending=false,length=5,sha256=repeat('A',64),version=repeat('B',64) WHERE operation='{op}'"));
        await Assert.ThrowsAsync<PostgresException>(() => Sql(rig.ConnectionString, "DELETE FROM storage_seal.original"));
        await Assert.ThrowsAsync<PostgresException>(() => Sql(rig.ConnectionString, "TRUNCATE storage_seal.original"));
        Assert.Equal(OriginalCondition.Unknown, (await rig.Store.Inspect(op, org)).Condition);
        Assert.Empty(Directory.GetFileSystemEntries(rig.Root));
    }

    [Fact]
    public async Task Model_restart_after_confirmed_commit_preserves_fact_and_immutable_receipt()
    {
        using var rig = await Create();
        var op = Guid.NewGuid(); var org = Guid.NewGuid();
        var receipt = await rig.Store.Write(op, org, Page());
        // Models process loss/ACK loss AFTER authoritative COMMIT, not a rollback.
        await rig.Ledger.Recover();
        var restarted = new PrivateOriginalStore(new SealLedger(rig.ConnectionString), new ModelKernel(), rig.Root, rig.Epoch + 1);
        Assert.Equal(receipt, (await restarted.Inspect(op, org)).Proof);
        await Assert.ThrowsAsync<PostgresException>(() => Sql(rig.ConnectionString,
            "UPDATE storage_seal.original SET sha256=repeat('B',64)"));
        await Assert.ThrowsAsync<PostgresException>(() => Sql(rig.ConnectionString,
            "UPDATE storage_seal.original SET generation=gen_random_uuid()"));
        Assert.Equal(OriginalCondition.Confirmed, (await restarted.Inspect(op, org)).Condition);
    }

    [Fact]
    public async Task Concurrent_confirm_and_claim_probe_only_observe_atomic_confirmed_receipt()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var kernel = new ModelKernel { AtEnable = () => { entered.SetResult(); Assert.True(release.Wait(TimeSpan.FromSeconds(15))); } };
        using var rig = await Create(kernel);
        var op = Guid.NewGuid(); var org = Guid.NewGuid();
        var writing = Task.Run(() => rig.Store.Write(op, org, Page()));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await using var c = new NpgsqlConnection(rig.ConnectionString);
            await c.OpenAsync();
            await using var tx = await c.BeginTransactionAsync();
            await using var claim = new NpgsqlCommand("""
                SELECT count(*) FROM (SELECT operation FROM storage_seal.original
                  WHERE state='Sealed' AND NOT physical_pending AND length IS NOT NULL FOR UPDATE) eligible
                """, c, tx);
            Assert.Equal(0L, await claim.ExecuteScalarAsync());
            await tx.CommitAsync();
        }
        finally { release.Set(); }
        var proof = await writing;
        Assert.Equal(proof, (await rig.Store.Inspect(op, org)).Proof);
        var row = (await rig.Ledger.Find(op, org))!;
        await Assert.ThrowsAsync<IOException>(() => rig.Ledger.Confirm(row.Reservation, proof, default));
        // No Submission/Message claim is implemented in this task.
    }

    [Fact]
    public async Task Reservations_are_global_per_org_durable_and_concurrent_admission_is_bounded()
    {
        using var rig = await Create();
        var org = Guid.NewGuid();
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 24).Select(async _ =>
        {
            try { await rig.Ledger.Reserve(Guid.NewGuid(), org, rig.Epoch, default); return true; }
            catch (IOException) { return false; }
        }));
        Assert.Equal(16, outcomes.Count(x => x));
        for (var i = 0; i < 48; i++) await rig.Ledger.Reserve(Guid.NewGuid(), Guid.NewGuid(), rig.Epoch, default);
        await Assert.ThrowsAsync<IOException>(() => rig.Ledger.Reserve(Guid.NewGuid(), Guid.NewGuid(), rig.Epoch, default));
        await rig.Ledger.Recover();
        await Assert.ThrowsAsync<IOException>(() => rig.Ledger.Reserve(Guid.NewGuid(), Guid.NewGuid(), rig.Epoch + 1, default));
        await using var c = new NpgsqlConnection(rig.ConnectionString); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*),sum(reserved_bytes),count(*) FILTER(WHERE physical_pending),count(*) FILTER(WHERE state='OutcomeUnknown') FROM storage_seal.original", c);
        await using var r = await cmd.ExecuteReaderAsync(); await r.ReadAsync();
        Assert.Equal(64, r.GetInt64(0)); Assert.Equal(64m * SealLedger.ReservedBytes, r.GetDecimal(1));
        Assert.Equal(64, r.GetInt64(2)); Assert.Equal(64, r.GetInt64(3));
        Assert.Empty(Directory.GetFileSystemEntries(rig.Root));
    }

    [Fact]
    public async Task Legacy_and_unavailable_confirmation_never_become_readable()
    {
        using var rig = await Create();
        var op = Guid.NewGuid(); var org = Guid.NewGuid();
        await File.WriteAllTextAsync(Path.Combine(rig.Root, op.ToString("N")), "unverified synthetic legacy original");
        Assert.Equal(OriginalCondition.Unverified, (await rig.Store.Inspect(op, org)).Condition);
        await Sql(rig.ConnectionString, "ALTER TABLE storage_seal.original RENAME TO unavailable");
        await Assert.ThrowsAsync<PostgresException>(() => rig.Store.Inspect(op, org));
        await Assert.ThrowsAsync<PostgresException>(() => rig.Store.Write(Guid.NewGuid(), org, Page()));
        Assert.Single(Directory.GetFileSystemEntries(rig.Root));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10485761)]
    public async Task Empty_or_oversized_original_is_never_confirmed(int length)
    {
        using var rig = await Create();
        var op = Guid.NewGuid(); var org = Guid.NewGuid();
        using var source = new MemoryStream(new byte[length]);
        await Assert.ThrowsAsync<IOException>(() => rig.Store.Write(op, org, source));
        Assert.Equal(OriginalCondition.Unknown, (await rig.Store.Inspect(op, org)).Condition);
        Assert.True(new FileInfo(await rig.PathFor(op, org)).Length <= SealLedger.MaximumOriginalBytes);
    }

    [Theory]
    [InlineData("/tmp")]
    [InlineData("/var/tmp")]
    public async Task Real_local_profile_either_enforces_kernel_seal_or_reports_explicit_blocker(string temporaryBase)
    {
        var kernel = new LinuxVerity();
        using var rig = await Create(kernel, temporaryBase);
        var op = Guid.NewGuid(); var org = Guid.NewGuid();
        try
        {
            await rig.Store.Write(op, org, Page());
        }
        catch (IOException e) when (e.Message.Contains("errno=25") || e.Message.Contains("errno=95"))
        {
            output.WriteLine("PHYSICAL PROFILE BLOCKED: " + e.Message + "; positive physical seal and retained-handle proof NOT RUN.");
            Assert.Equal(OriginalCondition.Unknown, (await rig.Store.Inspect(op, org)).Condition);
            return; // passing DENIAL test, never a physical-seal PASS
        }
        var path = await rig.PathFor(op, org);
        Assert.Equal(OriginalCondition.Confirmed, (await rig.Store.Inspect(op, org)).Condition);
        Assert.ThrowsAny<Exception>(() => File.OpenWrite(path));
        output.WriteLine("Kernel seal positive observed; power-loss profile still requires qualification.");
    }
    [Fact]
    public async Task Killed_private_worker_leaves_pending_bytes_accounted_after_restart()
    {
        using var rig = await Create();
        var op = Guid.NewGuid(); var org = Guid.NewGuid();
        var reservation = await rig.Ledger.Reserve(op, org, rig.Epoch, default);
        var path = await rig.PathFor(op, org);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var start = new ProcessStartInfo("python3") { RedirectStandardOutput = true, UseShellExecute = false };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("import os,sys,time; f=open(sys.argv[1],'xb'); f.write(b'SYNTHETIC CRASH'); f.flush(); os.fsync(f.fileno()); print('issued',flush=True); time.sleep(60)");
        start.ArgumentList.Add(path);
        using var worker = Process.Start(start)!;
        try
        {
            Assert.Equal("issued", await worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            worker.Kill();
            await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var epoch = await rig.Ledger.Recover();
            var restarted = new PrivateOriginalStore(rig.Ledger, new ModelKernel(), rig.Root, epoch);
            Assert.Equal(OriginalCondition.Unknown, (await restarted.Inspect(op, org)).Condition);
            Assert.True(File.Exists(path));
            Assert.True((await rig.Ledger.Find(op, org))!.PhysicalPending);
            await Assert.ThrowsAsync<IOException>(() => restarted.Write(Guid.NewGuid(), org, Page()));
            Assert.Single(Directory.GetDirectories(rig.Root));
        }
        finally
        {
            if (!worker.HasExited) { worker.Kill(); await worker.WaitForExitAsync(); }
        }
    }

    [Theory]
    [InlineData("reservation")]
    [InlineData("confirmation")]
    public async Task Model_lost_ack_does_not_replay_or_confuse_committed_with_aborted(string phase)
    {
        using var rig = await Create();
        var ledger = new SealLedger(rig.ConnectionString, at => { if (at == phase) throw new IOException("ACK lost"); });
        var store = new PrivateOriginalStore(ledger, new ModelKernel(), rig.Root, rig.Epoch);
        var op = Guid.NewGuid(); var org = Guid.NewGuid();
        await Assert.ThrowsAsync<IOException>(() => store.Write(op, org, Page()));
        if (phase == "reservation")
        {
            Assert.Empty(Directory.GetFileSystemEntries(rig.Root));
            Assert.Equal("PhysicalPending", (await ledger.Find(op, org))!.State);
            await ledger.Recover();
            Assert.Equal(OriginalCondition.Unknown, (await store.Inspect(op, org)).Condition);
        }
        else
        {
            Assert.Equal("Sealed", (await ledger.Find(op, org))!.State);
            await ledger.Recover();
            Assert.Equal(OriginalCondition.Confirmed, (await store.Inspect(op, org)).Condition);
            Assert.Single(Directory.GetDirectories(rig.Root));
        }
        await Assert.ThrowsAsync<IOException>(() => store.Write(op, org, Page())); // stale epoch; no replay
    }

    [Fact]
    public async Task Untrusted_database_role_cannot_forge_or_read_confirmation()
    {
        using var rig = await Create();
        var role = "untrusted_seal_" + Guid.NewGuid().ToString("N");
        await Sql(rig.ConnectionString, $"CREATE ROLE {role} NOLOGIN");
        foreach (var statement in new[] { "SELECT * FROM storage_seal.original", "UPDATE storage_seal.original SET state='Sealed'", "DELETE FROM storage_seal.original" })
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(() => Sql(rig.ConnectionString, $"SET ROLE {role}; {statement}"));
            Assert.Equal("42501", denied.SqlState);
        }
        var coordinator = "coordinator_seal_" + Guid.NewGuid().ToString("N");
        await Sql(rig.ConnectionString, $"CREATE ROLE {coordinator} NOLOGIN; GRANT USAGE ON SCHEMA storage_seal TO {coordinator}; GRANT SELECT,INSERT,UPDATE ON ALL TABLES IN SCHEMA storage_seal TO {coordinator}");
        var domainDenied = await Assert.ThrowsAsync<PostgresException>(() => Sql(rig.ConnectionString, $"SET ROLE {coordinator}; SELECT * FROM \"StoredAttachments\""));
        Assert.Equal("42501", domainDenied.SqlState);
    }

    [Fact]
    public async Task In_flight_org_capacity_has_no_waiting_queue_and_cancellation_cannot_seal()
    {
        var started = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        var kernel = new ModelKernel { AtEnable = () => { started.Signal(); Assert.True(release.Wait(TimeSpan.FromSeconds(15))); } };
        using var rig = await Create(kernel);
        var org = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var first = Task.Run(() => rig.Store.Write(Guid.NewGuid(), org, Page(), cancellation.Token));
        var second = Task.Run(() => rig.Store.Write(Guid.NewGuid(), org, Page(), cancellation.Token));
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(15)));
            await Assert.ThrowsAsync<IOException>(() => rig.Store.Write(Guid.NewGuid(), org, Page()));
            Assert.Equal(2, Directory.GetDirectories(rig.Root).Length);
            cancellation.Cancel();
        }
        finally { release.Set(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        await rig.Ledger.Recover();
        await using var c = new NpgsqlConnection(rig.ConnectionString); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM storage_seal.original WHERE state='Sealed'", c);
        Assert.Equal(0L, await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Model_competing_seals_have_one_winner_and_claim_probe_serializes_quarantine()
    {
        using var rig = await Create();
        var op = Guid.NewGuid(); var org = Guid.NewGuid();
        var reservation = await rig.Ledger.Reserve(op, org, rig.Epoch, default);
        var proof = new SealProof(1, new('A', 64), new('B', 64));
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { await rig.Ledger.Confirm(reservation, proof, default); return true; }
            catch (IOException) { return false; }
        }));
        Assert.Single(results, x => x);
        // Test-only claim predicate: no real submission, parent, grant or domain mutation.
        await using var c = new NpgsqlConnection(rig.ConnectionString); await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await using var probe = new NpgsqlCommand("SELECT state FROM storage_seal.original FOR UPDATE", c, tx);
        Assert.Equal("Sealed", await probe.ExecuteScalarAsync());
        var quarantine = rig.Ledger.Mark(op, "Quarantined");
        // A real conflicting UPDATE must wait for the candidate claim transaction.
        await using var observer = new NpgsqlConnection(rig.ConnectionString); await observer.OpenAsync();
        var blocked = false;
        for (var attempt = 0; attempt < 100 && !blocked; attempt++)
        {
            await using var locks = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock')", observer);
            blocked = (bool)(await locks.ExecuteScalarAsync())!;
            if (!blocked) await Task.Delay(10);
        }
        Assert.True(blocked);
        await tx.CommitAsync();
        await quarantine;
        Assert.Equal(OriginalCondition.Quarantined, (await rig.Store.Inspect(op, org)).Condition);
        await Assert.ThrowsAsync<IOException>(() => rig.Ledger.Confirm(reservation, proof, default));
    }

}
