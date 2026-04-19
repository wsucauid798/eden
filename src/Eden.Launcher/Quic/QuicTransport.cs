using System.Buffers.Binary;
using System.Net.Quic;
using System.Threading.Channels;
using Eden.Shared.Transport;

namespace Eden.Launcher.Quic;

/// <summary>
/// <see cref="ITransport"/> over a single QUIC bidirectional stream. Messages
/// are framed as a 4-byte big-endian length prefix followed by the payload.
/// Optionally owns the underlying <see cref="QuicConnection"/> — if set, the
/// connection is closed on disposal (used for the client side where we open a
/// stream on a connection we also created).
/// </summary>
internal sealed class QuicTransport : ITransport
{
    private const int MaxFrameSize = 16 * 1024 * 1024; // 16 MiB — hard cap against hostile peers

    private readonly QuicStream _stream;
    private readonly QuicConnection? _ownedConnection;
    private readonly Channel<ReadOnlyMemory<byte>> _outbound;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _sendLoop;

    public QuicTransport(QuicStream stream, QuicConnection? ownedConnection = null)
    {
        _stream          = stream;
        _ownedConnection = ownedConnection;
        _outbound = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode     = BoundedChannelFullMode.Wait,
        });
        _sendLoop = Task.Run(SendLoopAsync);
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        => _outbound.Writer.WriteAsync(frame, ct);

    public async ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken ct = default)
    {
        try
        {
            var lenBuf = new byte[4];
            var ok = await ReadExactlyOrEof(lenBuf, ct).ConfigureAwait(false);
            if (!ok) return null;

            int length = BinaryPrimitives.ReadInt32BigEndian(lenBuf);
            if (length < 0 || length > MaxFrameSize)
                throw new InvalidDataException($"Frame length {length} out of range.");

            var payload = new byte[length];
            ok = await ReadExactlyOrEof(payload, ct).ConfigureAwait(false);
            if (!ok) return null;

            return payload;
        }
        catch (QuicException ex) when (ex.QuicError is QuicError.ConnectionAborted or QuicError.OperationAborted)
        {
            return null;
        }
    }

    private async ValueTask<bool> ReadExactlyOrEof(byte[] buffer, CancellationToken ct)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            var n = await _stream.ReadAsync(buffer.AsMemory(total), ct).ConfigureAwait(false);
            if (n == 0) return total == 0 ? false : throw new EndOfStreamException("Stream closed mid-frame.");
            total += n;
        }
        return true;
    }

    private async Task SendLoopAsync()
    {
        try
        {
            var lenBuf = new byte[4];
            await foreach (var frame in _outbound.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                BinaryPrimitives.WriteInt32BigEndian(lenBuf, frame.Length);
                await _stream.WriteAsync(lenBuf, _cts.Token).ConfigureAwait(false);
                await _stream.WriteAsync(frame, _cts.Token).ConfigureAwait(false);
                await _stream.FlushAsync(_cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* expected on dispose */ }
        catch (QuicException) { /* peer closed while we were writing */ }
    }

    public async ValueTask DisposeAsync()
    {
        _outbound.Writer.TryComplete();
        _cts.Cancel();
        try { await _sendLoop.ConfigureAwait(false); } catch { }
        try { await _stream.DisposeAsync().ConfigureAwait(false); } catch { }
        if (_ownedConnection is not null)
        {
            try { await _ownedConnection.CloseAsync(0).ConfigureAwait(false); } catch { }
            try { await _ownedConnection.DisposeAsync().ConfigureAwait(false); } catch { }
        }
        _cts.Dispose();
    }
}
