using System.Threading.Channels;

namespace Eden.Shared.Transport;

/// <summary>
/// An in-process two-way transport. Used when viewer and server live in the
/// same address space — the &quot;solo / Just me&quot; mode of Eden's launcher.
/// No serialisation, no sockets; just two bounded channels.
/// </summary>
public sealed class InMemoryTransport : ITransport
{
    private readonly Channel<ReadOnlyMemory<byte>> _outbound;
    private readonly Channel<ReadOnlyMemory<byte>> _inbound;

    private InMemoryTransport(
        Channel<ReadOnlyMemory<byte>> outbound,
        Channel<ReadOnlyMemory<byte>> inbound)
    {
        _outbound = outbound;
        _inbound  = inbound;
    }

    /// <summary>
    /// Create a connected pair of transports. Anything sent on <c>a</c>
    /// appears on <c>b.ReceiveAsync</c>, and vice versa.
    /// </summary>
    /// <param name="capacity">
    /// Max number of pending frames in each direction before <c>SendAsync</c>
    /// starts applying backpressure.
    /// </param>
    public static (ITransport a, ITransport b) CreatePair(int capacity = 256)
    {
        var options = new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode     = BoundedChannelFullMode.Wait,
        };
        var aToB = Channel.CreateBounded<ReadOnlyMemory<byte>>(options);
        var bToA = Channel.CreateBounded<ReadOnlyMemory<byte>>(options);

        return (
            new InMemoryTransport(outbound: aToB, inbound: bToA),
            new InMemoryTransport(outbound: bToA, inbound: aToB));
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        => _outbound.Writer.WriteAsync(frame, ct);

    public async ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken ct = default)
    {
        if (!await _inbound.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            return null;

        return _inbound.Reader.TryRead(out var frame) ? frame : null;
    }

    public ValueTask DisposeAsync()
    {
        _outbound.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
