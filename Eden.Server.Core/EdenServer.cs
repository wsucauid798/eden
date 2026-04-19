using Eden.Shared;
using Eden.Shared.Ids;
using Eden.Shared.Transport;
using Eden.Shared.Wire;
using Eden.Shared.Wire.Messages;

namespace Eden.Server.Core;

/// <summary>
/// The transport-agnostic Eden server. One instance handles one client's
/// message loop. Higher layers (Kestrel / Launcher / something else) are
/// responsible for creating the <see cref="ITransport"/> and calling
/// <see cref="RunAsync"/>.
/// </summary>
public sealed class EdenServer
{
    private readonly EdenId<WorldTag> _worldId;

    public EdenServer(EdenId<WorldTag> worldId)
    {
        _worldId = worldId;
    }

    /// <summary>
    /// Run the message loop on this transport until the peer closes or the
    /// <paramref name="ct"/> fires. Returns when the transport has no more
    /// frames to deliver.
    /// </summary>
    public async Task RunAsync(ITransport transport, CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var frame = await transport.ReceiveAsync(ct).ConfigureAwait(false);
            if (frame is null)
                return;

            await HandleFrame(transport, frame.Value, ct).ConfigureAwait(false);
        }
    }

    private async Task HandleFrame(ITransport transport, ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        var kind = Envelope.PeekKind(frame);
        switch (kind)
        {
            case MessageKind.ClientHello:
                await OnClientHello(transport, Envelope.DecodePayload<ClientHello>(frame), ct);
                break;

            // TODO: AvatarUpdate, ChatMessage, Ping — added as server capability lands.
            default:
                // Unknown or not-yet-implemented kinds are ignored for now, not
                // fatal. A future protocol-strictness pass may reject.
                break;
        }
    }

    private async Task OnClientHello(ITransport transport, ClientHello hello, CancellationToken ct)
    {
        var reply = hello.WireProtocol == EdenVersion.WireProtocol
            ? new ServerHello(EdenId<SessionTag>.New(), EdenVersion.WireProtocol, _worldId, RejectReason: null)
            : new ServerHello(default, EdenVersion.WireProtocol, default,
                              $"Unsupported wire protocol {hello.WireProtocol}; server speaks {EdenVersion.WireProtocol}");

        await transport.SendAsync(Envelope.Encode(MessageKind.ServerHello, reply), ct);
    }
}
