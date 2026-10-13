using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Npgsql;
namespace Lms.Api.Tests;
// Disposable wire fault injector, adapted from the existing S4 component fixture.
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
