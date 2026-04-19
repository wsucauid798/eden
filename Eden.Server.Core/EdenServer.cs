using System.Collections.Concurrent;
using Eden.Shared;
using Eden.Shared.Ids;
using Eden.Shared.Transport;
using Eden.Shared.Wire;
using Eden.Shared.Wire.Messages;

namespace Eden.Server.Core;

/// <summary>
/// The transport-agnostic Eden server. One instance owns the authoritative
/// world and handles an arbitrary number of connected clients. Each client's
/// transport is driven by its own message loop via
/// <see cref="HandleClientAsync"/>; the server fan-outs to all connected
/// clients via <see cref="BroadcastAsync"/>.
/// </summary>
public sealed class EdenServer
{
    private readonly EdenId<WorldTag> _worldId;
    private readonly ConcurrentDictionary<EdenId<SessionTag>, ClientSession> _sessions = new();

    public EdenServer(EdenId<WorldTag> worldId)
    {
        _worldId = worldId;
    }

    /// <summary>Currently connected sessions.</summary>
    public int SessionCount => _sessions.Count;

    /// <summary>
    /// Run a message loop against one client's transport until it closes or
    /// <paramref name="ct"/> fires. Register the session on <c>ClientHello</c>
    /// and unregister on exit.
    /// </summary>
    public async Task HandleClientAsync(ITransport transport, CancellationToken ct = default)
    {
        EdenId<SessionTag>? sessionId = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var frame = await transport.ReceiveAsync(ct).ConfigureAwait(false);
                if (frame is null) return;

                var kind = Envelope.PeekKind(frame.Value);
                switch (kind)
                {
                    case MessageKind.ClientHello:
                        sessionId = await OnClientHello(transport, Envelope.DecodePayload<ClientHello>(frame.Value), ct);
                        break;

                    case MessageKind.Ping:
                        var ping = Envelope.DecodePayload<Ping>(frame.Value);
                        await transport.SendAsync(
                            Envelope.Encode(MessageKind.Pong, new Pong(ping.ClientTicks, DateTime.UtcNow.Ticks)),
                            ct).ConfigureAwait(false);
                        break;

                    // Other kinds are dropped silently for now; protocol-strictness
                    // pass lands when the full message surface is in place.
                    default:
                        break;
                }
            }
        }
        finally
        {
            if (sessionId is { } id)
                _sessions.TryRemove(id, out _);
        }
    }

    /// <summary>Send a frame to every currently connected client.</summary>
    public async Task BroadcastAsync<T>(MessageKind kind, T payload, CancellationToken ct = default)
    {
        var frame = Envelope.Encode(kind, payload);
        // ConcurrentDictionary.Values is a snapshot — safe to iterate while
        // the set mutates. A session whose transport has dropped will throw;
        // we swallow and let its own loop handle teardown.
        foreach (var session in _sessions.Values)
        {
            try { await session.Transport.SendAsync(frame, ct).ConfigureAwait(false); }
            catch { /* drop — session loop will clean up */ }
        }
    }

    private async Task<EdenId<SessionTag>?> OnClientHello(ITransport transport, ClientHello hello, CancellationToken ct)
    {
        if (hello.WireProtocol != EdenVersion.WireProtocol)
        {
            await transport.SendAsync(
                Envelope.Encode(MessageKind.ServerHello,
                    new ServerHello(default, EdenVersion.WireProtocol, default,
                        $"Unsupported wire protocol {hello.WireProtocol}; server speaks {EdenVersion.WireProtocol}")),
                ct).ConfigureAwait(false);
            return null;
        }

        var sessionId = EdenId<SessionTag>.New();
        _sessions[sessionId] = new ClientSession(sessionId, transport);

        await transport.SendAsync(
            Envelope.Encode(MessageKind.ServerHello,
                new ServerHello(sessionId, EdenVersion.WireProtocol, _worldId, RejectReason: null)),
            ct).ConfigureAwait(false);

        return sessionId;
    }
}

internal sealed record class ClientSession(EdenId<SessionTag> Id, ITransport Transport);
