using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Lms.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lms.Infrastructure.Tests;

public sealed partial class ScopedAuthorityTests
{
    [Fact]
    public async Task A2_wire_lost_PostgreSQL_commit_acknowledgement_never_returns_JWT()
    {
        var cs = await Seed(); var identity = await IssuingIdentity(cs); var time = new ComponentClock();
        using var key = RSA.Create(2048); var jwt = new ComponentJwt(key, "synthetic", "component", time);
        await using var proxy = new DropCommitAcknowledgement(cs);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var marker = new IssuanceBoundary("wire-loss", cs, cancellation);
        await using var db = PostgresFixture.Open(proxy.ConnectionString, marker);
        AccessToken? escaped = null;
        await Assert.ThrowsAnyAsync<Exception>(async () => escaped = await new PostgresComponentTokenIssuer(db, time, jwt)
            .Issue(identity.User, identity.Session, A, cancellation.Token));
        Assert.True(marker.BeforeReached); Assert.Null(escaped);
        await proxy.Dropped.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var verify = PostgresFixture.Open(cs);
        Assert.Equal(2, (await verify.Set<AccountSecurityState>().SingleAsync(x => x.UserId == identity.User)).Version);
        // The direct authoritative read sees committed data; the issuing connection never
        // received PostgreSQL's CommandComplete for COMMIT. No rollback/replay inference.
    }
}

// Synthetic single-connection PostgreSQL protocol relay. TLS disabled only in this test
// connection. Suppress COMMIT CommandComplete and close the connection before ReadyForQuery.
// No credentials, payloads, SQL or bearer values are logged by this fault injector.
internal sealed class DropCommitAcknowledgement : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new(TimeSpan.FromSeconds(30));
    private readonly Task relay;
    public TaskCompletionSource Dropped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string ConnectionString { get; }
    public DropCommitAcknowledgement(string original)
    {
        var target = new NpgsqlConnectionStringBuilder(original);
        listener.Start();
        ConnectionString = new NpgsqlConnectionStringBuilder(original)
        {
            Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port,
            SslMode = SslMode.Disable, Pooling = false, Timeout = 10, CommandTimeout = 10
        }.ConnectionString;
        relay = Run(target.Host!, target.Port);
    }
    private async Task Run(string host, int port)
    {
        using var client = await listener.AcceptTcpClientAsync(stop.Token);
        using var upstream = new TcpClient(); await upstream.ConnectAsync(host, port, stop.Token);
        var incoming = client.GetStream(); var outgoing = upstream.GetStream();
        // Startup packet has no type byte. All subsequent server/client packets do.
        var length = new byte[4]; await incoming.ReadExactlyAsync(length, stop.Token);
        var startup = new byte[Length(length) - 4]; await incoming.ReadExactlyAsync(startup, stop.Token);
        await outgoing.WriteAsync(length, stop.Token); await outgoing.WriteAsync(startup, stop.Token);
        var forward = CopyPackets(incoming, outgoing, false);
        var backward = CopyPackets(outgoing, incoming, true);
        try { await Task.WhenAny(forward, backward); }
        finally
        {
            stop.Cancel(); client.Close(); upstream.Close();
            // Observe both pumps; cancellation/disconnection is the intended injected fault.
            try { await Task.WhenAll(forward, backward); }
            catch (Exception e) when (e is OperationCanceledException or IOException or SocketException or ObjectDisposedException) { }
        }
    }
    private async Task CopyPackets(NetworkStream from, NetworkStream to, bool suppressCommit)
    {
        var header = new byte[5];
        while (!stop.IsCancellationRequested)
        {
            await from.ReadExactlyAsync(header, stop.Token);
            var payload = new byte[Length(header.AsSpan(1)) - 4]; await from.ReadExactlyAsync(payload, stop.Token);
            if (suppressCommit && header[0] == (byte)'C' && Encoding.ASCII.GetString(payload) == "COMMIT\0")
            { Dropped.TrySetResult(); return; }
            await to.WriteAsync(header, stop.Token); await to.WriteAsync(payload, stop.Token);
        }
    }
    private static int Length(ReadOnlySpan<byte> bytes)
    {
        var length = BinaryPrimitives.ReadInt32BigEndian(bytes);
        return length is >= 4 and <= 1024 * 1024 ? length : throw new IOException("Invalid synthetic protocol frame");
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); listener.Stop();
        try { await relay; }
        catch (Exception e) when (e is OperationCanceledException or IOException or SocketException or ObjectDisposedException) { }
        stop.Dispose();
    }
}
