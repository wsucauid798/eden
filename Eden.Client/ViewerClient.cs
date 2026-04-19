using System.Collections.Concurrent;
using Eden.Shared;
using Eden.Shared.Entities;
using Eden.Shared.Ids;
using Eden.Shared.Transport;
using Eden.Shared.Wire;
using Eden.Shared.Wire.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Eden.Client;

/// <summary>
/// The client-side counterpart to <c>EdenServer</c>. Handles the handshake,
/// runs an incoming-message loop, and maintains a local mirror of the
/// world state — a viewer (Godot, CLI, test) just subscribes to events and
/// reads <see cref="RemoteAvatars"/>.
/// </summary>
public sealed class ViewerClient : IAsyncDisposable
{
    private readonly ITransport _transport;
    private readonly ILogger<ViewerClient> _logger;
    private readonly ConcurrentDictionary<EdenId<UserTag>, AvatarState> _remoteAvatars = new();
    private readonly ConcurrentDictionary<EdenId<PrimTag>, PrimState>   _remotePrims   = new();
    private readonly CancellationTokenSource _cts = new();

    private ServerHello? _session;
    private Task? _receiveLoop;

    public ViewerClient(ITransport transport, ILogger<ViewerClient>? logger = null)
    {
        _transport = transport;
        _logger    = logger ?? NullLogger<ViewerClient>.Instance;
    }

    /// <summary>
    /// The <see cref="ServerHello"/> returned by the server, or <c>null</c>
    /// before <see cref="ConnectAsync"/> completes.
    /// </summary>
    public ServerHello? Session => _session;

    /// <summary>
    /// The local avatar's user ID (assigned by the server). Empty before
    /// <see cref="ConnectAsync"/>.
    /// </summary>
    public EdenId<UserTag> MyUserId => _session?.UserId ?? default;

    /// <summary>
    /// Read-only view of every other avatar the server has told us about.
    /// Updates are pushed via <see cref="AvatarUpdated"/>; removals via
    /// <see cref="AvatarLeft"/>. Thread-safe.
    /// </summary>
    public IReadOnlyDictionary<EdenId<UserTag>, AvatarState> RemoteAvatars => _remoteAvatars;

    /// <summary>Read-only view of every prim the server has told us about.
    /// Updates are pushed via <see cref="PrimUpdated"/>.</summary>
    public IReadOnlyDictionary<EdenId<PrimTag>, PrimState> RemotePrims => _remotePrims;

    /// <summary>Raised when an avatar's state arrives (join or movement).</summary>
    public event Action<AvatarState>? AvatarUpdated;

    /// <summary>Raised when an avatar disconnects.</summary>
    public event Action<EdenId<UserTag>>? AvatarLeft;

    /// <summary>Raised when a prim's state arrives (spawn or mutation).</summary>
    public event Action<PrimState>? PrimUpdated;

    /// <summary>
    /// Complete the handshake with the server and start the background
    /// receive loop.
    /// </summary>
    public async Task ConnectAsync(string clientName, CancellationToken ct = default)
    {
        var hello = new ClientHello(
            ClientName:    clientName,
            ClientVersion: EdenVersion.Release,
            WireProtocol:  EdenVersion.WireProtocol,
            AuthToken:     null);

        await _transport.SendAsync(Envelope.Encode(MessageKind.ClientHello, hello), ct)
                        .ConfigureAwait(false);

        // Consume frames until ServerHello. Any AvatarUpdate frames that
        // arrive before ServerHello (shouldn't happen under current server
        // but be defensive) are applied to the mirror.
        while (true)
        {
            var frame = await _transport.ReceiveAsync(ct).ConfigureAwait(false);
            if (frame is null)
                throw new InvalidOperationException("Transport closed during handshake.");

            var kind = Envelope.PeekKind(frame.Value);
            if (kind == MessageKind.ServerHello)
            {
                _session = Envelope.DecodePayload<ServerHello>(frame.Value);
                if (_session.Value.RejectReason is not null)
                {
                    _logger.LogError("Server rejected connection: {Reason}", _session.Value.RejectReason);
                    throw new InvalidOperationException($"Server rejected connection: {_session.Value.RejectReason}");
                }
                _logger.LogInformation("Connected as user {UserId} to world {WorldId}",
                    _session.Value.UserId, _session.Value.WorldId);
                break;
            }

            ApplyFrame(kind, frame.Value);
        }

        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
    }

    /// <summary>Send the local avatar's state to the server.</summary>
    public Task SendAvatarUpdateAsync(AvatarState state, CancellationToken ct = default)
        => _transport.SendAsync(Envelope.Encode(MessageKind.AvatarUpdate, new AvatarUpdate(state)), ct).AsTask();

    /// <summary>Tell the server this avatar touched the given prim. The
    /// server dispatches to the prim's behavior (if any) — no reply.</summary>
    public Task TouchPrimAsync(EdenId<PrimTag> primId, CancellationToken ct = default)
        => _transport.SendAsync(
            Envelope.Encode(MessageKind.ClientTouchPrim, new ClientTouchPrim(primId)),
            ct).AsTask();

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var frame = await _transport.ReceiveAsync(ct).ConfigureAwait(false);
                if (frame is null) return;

                ApplyFrame(Envelope.PeekKind(frame.Value), frame.Value);
            }
        }
        catch (OperationCanceledException) { /* expected on dispose */ }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Receive loop ended unexpectedly");
        }
    }

    private void ApplyFrame(MessageKind kind, ReadOnlyMemory<byte> frame)
    {
        switch (kind)
        {
            case MessageKind.AvatarUpdate:
                var update = Envelope.DecodePayload<AvatarUpdate>(frame);
                if (update.State.UserId == MyUserId) return; // ignore echoes of ourselves
                _remoteAvatars[update.State.UserId] = update.State;
                AvatarUpdated?.Invoke(update.State);
                break;

            case MessageKind.AvatarLeft:
                var left = Envelope.DecodePayload<AvatarLeft>(frame);
                _remoteAvatars.TryRemove(left.UserId, out _);
                AvatarLeft?.Invoke(left.UserId);
                break;

            case MessageKind.PrimUpdate:
                var prim = Envelope.DecodePayload<PrimUpdate>(frame);
                _remotePrims[prim.State.Id] = prim.State;
                PrimUpdated?.Invoke(prim.State);
                break;

            // Ping/Pong and other kinds not handled by the mirror.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _transport.DisposeAsync().ConfigureAwait(false); } catch { }
        if (_receiveLoop is not null)
            try { await _receiveLoop.ConfigureAwait(false); } catch { }
        _cts.Dispose();
    }
}
