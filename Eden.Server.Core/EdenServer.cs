using System.Collections.Concurrent;
using Eden.Shared;
using Eden.Shared.Entities;
using Eden.Shared.Ids;
using Eden.Shared.Math;
using Eden.Shared.Transport;
using Eden.Shared.Wire;
using Eden.Shared.Wire.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Eden.Server.Core;

/// <summary>
/// The transport-agnostic Eden server. Owns the authoritative world state
/// and handles an arbitrary number of connected clients. Each client's
/// transport is driven by its own loop via <see cref="HandleClientAsync"/>;
/// the server fans out to every session via <see cref="BroadcastAsync"/>
/// and <see cref="BroadcastExceptAsync"/>.
/// </summary>
public sealed class EdenServer
{
    private readonly EdenId<WorldTag> _worldId;
    private readonly ILogger<EdenServer> _logger;
    private readonly ConcurrentDictionary<EdenId<SessionTag>, ClientSession> _sessions = new();
    private readonly ConcurrentDictionary<EdenId<UserTag>,    AvatarState>   _avatars  = new();

    public EdenServer(EdenId<WorldTag> worldId, ILogger<EdenServer>? logger = null)
    {
        _worldId = worldId;
        _logger  = logger ?? NullLogger<EdenServer>.Instance;
    }

    public int SessionCount => _sessions.Count;
    public int AvatarCount  => _avatars.Count;

    /// <summary>
    /// Run a message loop against one client's transport until it closes or
    /// <paramref name="ct"/> fires. Registers the session on
    /// <c>ClientHello</c>, propagates state changes, and unregisters on exit
    /// (broadcasting <see cref="AvatarLeft"/>).
    /// </summary>
    public async Task HandleClientAsync(ITransport transport, CancellationToken ct = default)
    {
        ClientSession? session = null;
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
                        session = await OnClientHello(transport, Envelope.DecodePayload<ClientHello>(frame.Value), ct);
                        break;

                    case MessageKind.Ping:
                        var ping = Envelope.DecodePayload<Ping>(frame.Value);
                        await transport.SendAsync(
                            Envelope.Encode(MessageKind.Pong, new Pong(ping.ClientTicks, DateTime.UtcNow.Ticks)),
                            ct).ConfigureAwait(false);
                        break;

                    case MessageKind.AvatarUpdate when session is not null:
                        var update = Envelope.DecodePayload<AvatarUpdate>(frame.Value);
                        await OnAvatarUpdate(session, update, ct);
                        break;

                    default:
                        _logger.LogDebug("Ignoring frame of kind {Kind} on session {SessionId}",
                            kind, session?.Id);
                        break;
                }
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Client loop ended on session {SessionId}", session?.Id);
        }
        finally
        {
            if (session is not null)
                await DisconnectSession(session);
        }
    }

    /// <summary>Send to every currently connected client.</summary>
    public Task BroadcastAsync<T>(MessageKind kind, T payload, CancellationToken ct = default)
        => FanOut(Envelope.Encode(kind, payload), skip: null, ct);

    /// <summary>Send to every connected client except the given session.</summary>
    public Task BroadcastExceptAsync<T>(EdenId<SessionTag> skip, MessageKind kind, T payload, CancellationToken ct = default)
        => FanOut(Envelope.Encode(kind, payload), skip, ct);

    private async Task FanOut(byte[] frame, EdenId<SessionTag>? skip, CancellationToken ct)
    {
        foreach (var session in _sessions.Values)
        {
            if (skip is { } s && session.Id == s) continue;
            try
            {
                await session.Transport.SendAsync(frame, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Broadcast to session {SessionId} failed; session loop will tear down",
                    session.Id);
            }
        }
    }

    private async Task<ClientSession?> OnClientHello(ITransport transport, ClientHello hello, CancellationToken ct)
    {
        if (hello.WireProtocol != EdenVersion.WireProtocol)
        {
            _logger.LogWarning("Rejecting client {ClientName} on wire protocol {ClientProto}; server speaks {ServerProto}",
                hello.ClientName, hello.WireProtocol, EdenVersion.WireProtocol);
            await transport.SendAsync(
                Envelope.Encode(MessageKind.ServerHello,
                    new ServerHello(default, default, EdenVersion.WireProtocol, default,
                        $"Unsupported wire protocol {hello.WireProtocol}; server speaks {EdenVersion.WireProtocol}")),
                ct).ConfigureAwait(false);
            return null;
        }

        var sessionId = EdenId<SessionTag>.New();
        var userId    = EdenId<UserTag>.New();
        var session   = new ClientSession(sessionId, userId, transport);
        _sessions[sessionId] = session;
        _logger.LogInformation("Session {SessionId} joined as user {UserId} ({ClientName})",
            sessionId, userId, hello.ClientName);

        // Initial avatar state at origin, empty appearance. Clients update
        // once they have a proper position from input.
        var initialAvatar = new AvatarState(
            UserId:         userId,
            SessionId:      sessionId,
            DisplayName:    hello.ClientName,
            Transform:      Transform.Identity,
            Velocity:       Vector3.Zero,
            AppearanceHash: 0);
        _avatars[userId] = initialAvatar;

        // 1) ServerHello reply.
        await transport.SendAsync(
            Envelope.Encode(MessageKind.ServerHello,
                new ServerHello(sessionId, userId, EdenVersion.WireProtocol, _worldId, RejectReason: null)),
            ct).ConfigureAwait(false);

        // 2) Sync existing world to the newcomer (one AvatarUpdate per
        //    avatar already present — *excluding* their own).
        foreach (var existing in _avatars.Values)
        {
            if (existing.UserId == userId) continue;
            await transport.SendAsync(
                Envelope.Encode(MessageKind.AvatarUpdate, new AvatarUpdate(existing)),
                ct).ConfigureAwait(false);
        }

        // 3) Announce the newcomer to everyone else.
        await BroadcastExceptAsync(sessionId, MessageKind.AvatarUpdate, new AvatarUpdate(initialAvatar), ct);

        return session;
    }

    private async Task OnAvatarUpdate(ClientSession session, AvatarUpdate update, CancellationToken ct)
    {
        // Trust the client's own state record only for its own avatar; ignore
        // attempts to spoof someone else. Normalise to the session's UserId.
        var state = update.State with { UserId = session.UserId, SessionId = session.Id };
        _avatars[session.UserId] = state;

        await BroadcastExceptAsync(session.Id, MessageKind.AvatarUpdate, new AvatarUpdate(state), ct);
    }

    private async Task DisconnectSession(ClientSession session)
    {
        _sessions.TryRemove(session.Id, out _);
        _avatars.TryRemove(session.UserId, out _);
        _logger.LogInformation("Session {SessionId} (user {UserId}) disconnected", session.Id, session.UserId);

        try
        {
            await BroadcastAsync(MessageKind.AvatarLeft, new AvatarLeft(session.UserId));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AvatarLeft broadcast failed for user {UserId}", session.UserId);
        }
    }
}

internal sealed record class ClientSession(
    EdenId<SessionTag> Id,
    EdenId<UserTag>    UserId,
    ITransport         Transport);
