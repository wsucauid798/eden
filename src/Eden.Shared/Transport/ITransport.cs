namespace Eden.Shared.Transport;

/// <summary>
/// A two-way, message-oriented transport between a viewer and a server.
/// Concrete implementations: <c>InMemoryTransport</c> (solo / same-process)
/// and a QUIC / WebTransport variant for network play.
/// </summary>
/// <remarks>
/// Messages are opaque byte frames at this layer — the <c>Eden.Shared.Wire</c>
/// layer encodes / decodes domain types. That way a transport is never coupled
/// to a specific payload format.
/// </remarks>
public interface ITransport : IAsyncDisposable
{
    /// <summary>
    /// Send a single frame. Applies backpressure if the outbound buffer is
    /// full — callers must <c>await</c>.
    /// </summary>
    ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default);

    /// <summary>
    /// Wait for the next inbound frame. Returns <c>null</c> when the peer
    /// has closed and there will be no more frames.
    /// </summary>
    ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken ct = default);
}
