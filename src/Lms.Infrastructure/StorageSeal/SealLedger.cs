using Npgsql;

namespace Lms.Infrastructure.StorageSeal;

// Isolated trusted infrastructure only. No domain credentials, grants or HTTP binding.
internal sealed record SealReservation(Guid Operation, Guid Generation, Guid Organization, long Epoch);
internal sealed record SealProof(long Length, string Sha256, string Version);
internal sealed record SealEntry(SealReservation Reservation, string State, bool PhysicalPending,
    SealProof? Proof);

internal sealed class SealLedger(string connectionString, Action<string>? acknowledgementFault = null)
{
    internal const long MaximumOriginalBytes = 10 * 1024 * 1024;
    internal const long ReservedBytes = 12 * 1024 * 1024; // includes bounded verity/metadata allowance
    internal const int GlobalSlots = 64, OrganizationSlots = 16;

    // Explicit isolated installation; deliberately NOT an EF migration or startup hook.
    internal const string Schema = """
        CREATE SCHEMA storage_seal;
        REVOKE ALL ON SCHEMA storage_seal FROM PUBLIC;
        CREATE TABLE storage_seal.control (id integer PRIMARY KEY CHECK(id=1), epoch bigint NOT NULL);
        INSERT INTO storage_seal.control VALUES (1,0);
        CREATE TABLE storage_seal.original (
          operation uuid PRIMARY KEY, generation uuid NOT NULL UNIQUE, organization uuid NOT NULL,
          epoch bigint NOT NULL, reserved_bytes bigint NOT NULL CHECK(reserved_bytes=12582912),
          state text NOT NULL CHECK(state IN ('PhysicalPending','OutcomeUnknown','Quarantined','Sealed')),
          physical_pending boolean NOT NULL,
          length bigint, sha256 text, version text,
          CHECK ((length IS NULL AND sha256 IS NULL AND version IS NULL) OR
            (length IS NOT NULL AND sha256 IS NOT NULL AND version IS NOT NULL AND length BETWEEN 1 AND 10485760 AND sha256 ~ '^[0-9A-F]{64}$' AND version ~ '^[0-9A-F]{64}$')),
          CHECK(state <> 'Sealed' OR (length IS NOT NULL AND NOT physical_pending))
        );
        CREATE FUNCTION storage_seal.guard() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP IN ('DELETE','TRUNCATE') THEN RAISE EXCEPTION 'seal ledger retention'; END IF;
          IF (NEW.operation,NEW.generation,NEW.organization,NEW.epoch,NEW.reserved_bytes)
             IS DISTINCT FROM (OLD.operation,OLD.generation,OLD.organization,OLD.epoch,OLD.reserved_bytes)
             OR (OLD.length IS NOT NULL AND (NEW.length,NEW.sha256,NEW.version)
               IS DISTINCT FROM (OLD.length,OLD.sha256,OLD.version))
             OR (OLD.state <> 'PhysicalPending' AND NEW.state <> OLD.state AND NEW.state <> 'Quarantined')
             OR (OLD.state <> 'PhysicalPending' AND OLD.length IS NULL AND NEW.length IS NOT NULL)
             OR (OLD.physical_pending AND NOT NEW.physical_pending AND NEW.state <> 'Sealed') THEN
            RAISE EXCEPTION 'seal ledger terminal or immutable';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER seal_guard BEFORE UPDATE OR DELETE ON storage_seal.original
          FOR EACH ROW EXECUTE FUNCTION storage_seal.guard();
        CREATE TRIGGER seal_truncate BEFORE TRUNCATE ON storage_seal.original
          FOR EACH STATEMENT EXECUTE FUNCTION storage_seal.guard();
        REVOKE ALL ON ALL TABLES IN SCHEMA storage_seal FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA storage_seal FROM PUBLIC;
        """;

    private async Task<NpgsqlConnection> Connect(CancellationToken ct)
    {
        var c = new NpgsqlConnection(connectionString);
        try { await c.OpenAsync(ct); return c; }
        catch { await c.DisposeAsync(); throw; }
    }

    // Startup serializes with reserve/confirmation. Old workers may still finish PRIVATE I/O,
    // but epoch/CAS prevents confirmation. Unknown bytes/slots are never released.
    internal async Task<long> Recover(CancellationToken ct = default)
    {
        await using var c = await Connect(ct);
        await using var tx = await c.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        await using var durability = new NpgsqlCommand("SET LOCAL synchronous_commit=on", c, tx);
        await durability.ExecuteNonQueryAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            UPDATE storage_seal.control SET epoch=epoch+1 WHERE id=1 RETURNING epoch;
            """, c, tx);
        var epoch = (long)(await cmd.ExecuteScalarAsync(ct))!;
        await using var mark = new NpgsqlCommand("""
            UPDATE storage_seal.original SET state='OutcomeUnknown'
            WHERE state='PhysicalPending';
            """, c, tx);
        await mark.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return epoch;
    }

    internal async Task<SealReservation> Reserve(Guid operation, Guid organization, long epoch, CancellationToken ct)
    {
        if (operation == Guid.Empty || organization == Guid.Empty) throw new ArgumentException("Empty identity");
        await using var c = await Connect(ct);
        await using var tx = await c.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        await using var durability = new NpgsqlCommand("SET LOCAL synchronous_commit=on", c, tx);
        await durability.ExecuteNonQueryAsync(ct);
        await using var control = new NpgsqlCommand("SELECT epoch FROM storage_seal.control WHERE id=1 FOR UPDATE", c, tx);
        if ((long)(await control.ExecuteScalarAsync(ct))! != epoch) throw new IOException("Stale storage epoch");
        await using var counts = new NpgsqlCommand("""
            SELECT count(*), count(*) FILTER (WHERE organization=@org),
              count(*) FILTER (WHERE organization=@org AND state IN ('OutcomeUnknown','Quarantined'))
            FROM storage_seal.original
            """, c, tx);
        counts.Parameters.AddWithValue("org", organization);
        await using (var r = await counts.ExecuteReaderAsync(ct))
        {
            await r.ReadAsync(ct);
            if (r.GetInt64(0) >= GlobalSlots || r.GetInt64(1) >= OrganizationSlots || r.GetInt64(2) != 0)
                throw new IOException("Storage capacity unavailable");
        }
        var result = new SealReservation(operation, Guid.NewGuid(), organization, epoch);
        await using var insert = new NpgsqlCommand("""
            INSERT INTO storage_seal.original VALUES (@op,@gen,@org,@epoch,12582912,'PhysicalPending',true,NULL,NULL,NULL)
            """, c, tx);
        insert.Parameters.AddWithValue("op", operation);
        insert.Parameters.AddWithValue("gen", result.Generation);
        insert.Parameters.AddWithValue("org", organization);
        insert.Parameters.AddWithValue("epoch", epoch);
        await insert.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct); // no file side effects until positive ACK
        acknowledgementFault?.Invoke("reservation");
        return result;
    }

    internal async Task Confirm(SealReservation reservation, SealProof proof, CancellationToken ct)
    {
        await using var c = await Connect(ct);
        await using var tx = await c.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        await using var durability = new NpgsqlCommand("SET LOCAL synchronous_commit=on", c, tx);
        await durability.ExecuteNonQueryAsync(ct);
        await using var control = new NpgsqlCommand("SELECT epoch FROM storage_seal.control WHERE id=1 FOR UPDATE", c, tx);
        if ((long)(await control.ExecuteScalarAsync(ct))! != reservation.Epoch) throw new IOException("Stale storage epoch");
        await using var cmd = new NpgsqlCommand("""
            UPDATE storage_seal.original SET state='Sealed',physical_pending=false,length=@length,sha256=@hash,version=@version
            WHERE operation=@op AND generation=@gen AND organization=@org AND epoch=@epoch AND state='PhysicalPending'
            """, c, tx);
        cmd.Parameters.AddWithValue("op", reservation.Operation);
        cmd.Parameters.AddWithValue("gen", reservation.Generation);
        cmd.Parameters.AddWithValue("org", reservation.Organization);
        cmd.Parameters.AddWithValue("epoch", reservation.Epoch);
        cmd.Parameters.AddWithValue("length", proof.Length);
        cmd.Parameters.AddWithValue("hash", proof.Sha256);
        cmd.Parameters.AddWithValue("version", proof.Version);
        if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new IOException("Original cannot be confirmed");
        await tx.CommitAsync(ct);
        acknowledgementFault?.Invoke("confirmation");
    }

    internal async Task Mark(Guid operation, string state, CancellationToken ct = default)
    {
        if (state is not ("OutcomeUnknown" or "Quarantined")) throw new ArgumentException("Terminal state required");
        await using var c = await Connect(ct);
        await using var cmd = new NpgsqlCommand("""
            UPDATE storage_seal.original SET state=@state WHERE operation=@op
              AND (state='PhysicalPending' OR @state='Quarantined')
            """, c);
        cmd.Parameters.AddWithValue("op", operation);
        cmd.Parameters.AddWithValue("state", state);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    internal async Task<SealEntry?> Find(Guid operation, Guid organization, CancellationToken ct = default)
    {
        await using var c = await Connect(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT generation,epoch,state,physical_pending,length,sha256,version
            FROM storage_seal.original WHERE operation=@op AND organization=@org
            """, c);
        cmd.Parameters.AddWithValue("op", operation);
        cmd.Parameters.AddWithValue("org", organization);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null; // includes legacy: never infer confirmation
        return new(new(operation, r.GetGuid(0), organization, r.GetInt64(1)), r.GetString(2), r.GetBoolean(3),
            r.IsDBNull(4) ? null : new(r.GetInt64(4), r.GetString(5), r.GetString(6)));
    }
}
