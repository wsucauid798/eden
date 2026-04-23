using System.Collections.Concurrent;
using Eden.Scripting;
using Eden.Scripting.Host;
using Eden.Server.Physics;
using Eden.Server.Prims;
using Eden.Shared;
using Eden.Shared.Entities;
using Eden.Shared.Ids;
using Eden.Shared.Math;
using Eden.Shared.Transport;
using Eden.Shared.Wire;
using Eden.Shared.Wire.Messages;
using JoltPhysicsSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Eden.Server;

/// <summary>
/// The transport-agnostic Eden server. Owns the authoritative world state
/// and handles an arbitrary number of connected clients. Each client's
/// transport is driven by its own loop via <see cref="HandleClientAsync"/>;
/// the server fans out to every session via <see cref="BroadcastAsync"/>
/// and <see cref="BroadcastExceptAsync"/>.
/// </summary>
public sealed class EdenServer : IAsyncDisposable
{
    /// <summary>Fixed physics step. 60 Hz. Jolt is happiest with a fixed
    /// <c>dt</c>; caller's wall-clock drift is absorbed by the timer.</summary>
    private static readonly TimeSpan PhysicsStep = TimeSpan.FromMilliseconds(1000.0 / 60.0);

    /// <summary>World-clock tick period. 1 Hz — time of day, weather, wind
    /// all evolve slowly enough that 1 Hz broadcasts are plenty. Finer
    /// viewer interpolation is a viewer concern.</summary>
    private static readonly TimeSpan WorldClockStep = TimeSpan.FromSeconds(1);

    private readonly EdenId<WorldTag> _worldId;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<EdenServer> _logger;
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private readonly BehaviorHost _behaviorHost;
    private readonly IWorldContext _worldContext;
    private readonly PhysicsWorld _physics;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly Task _physicsLoop;
    private readonly Task _worldClockLoop;
    private readonly float _dayLengthSeconds;
    private readonly object _worldLock = new();
    private WorldState _world;
    private readonly ConcurrentDictionary<EdenId<SessionTag>, ClientSession> _sessions = new();
    private readonly ConcurrentDictionary<EdenId<UserTag>,    AvatarState>   _avatars  = new();
    private readonly ConcurrentDictionary<EdenId<PrimTag>,    ServerPrim>    _prims    = new();
    private int _disposed;

    public EdenServer(
        EdenId<WorldTag>  worldId,
        ILoggerFactory?   loggerFactory = null,
        WorldConfig?      worldConfig   = null)
    {
        _worldId       = worldId;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger        = _loggerFactory.CreateLogger<EdenServer>();
        _behaviorHost  = new BehaviorHost(_loggerFactory);
        _physics       = new PhysicsWorld(_loggerFactory.CreateLogger<PhysicsWorld>());

        var cfg = worldConfig ?? WorldConfig.Default;
        _dayLengthSeconds = cfg.DayLengthSeconds > 0 ? cfg.DayLengthSeconds : 24f * 60f;
        var terrain = cfg.InitialTerrain.Normalized();
        var environment = cfg.InitialEnvironment.NormalizedFor(cfg.InitialWeather);
        _world = new WorldState(
            WorldId:        worldId,
            Name:           cfg.Name,
            TimeOfDayHours: cfg.StartHoursOfDay,
            Wind:           cfg.InitialWind,
            Weather:        cfg.InitialWeather,
            Gravity:        cfg.Gravity,
            Terrain:        terrain,
            Environment:    environment);
        _worldContext   = new ServerWorldContext(() => WorldSnapshot);

        _physicsLoop    = Task.Run(() => PhysicsLoopAsync(_shutdownCts.Token));
        _worldClockLoop = Task.Run(() => WorldClockLoopAsync(_shutdownCts.Token));
    }

    /// <summary>Read-only snapshot of the current authoritative world state.
    /// Callers outside the server (tests, admin tools) use this; scripts
    /// read via <c>IWorldContext.World</c>.</summary>
    public WorldState WorldSnapshot
    {
        get { lock (_worldLock) return _world; }
    }

    public int SessionCount => _sessions.Count;
    public int AvatarCount  => _avatars.Count;
    public int PrimCount    => _prims.Count;

    /// <summary>Create a new prim in the world registry. <see cref="PrimFlags.Physical"/>
    /// makes the body dynamic (falls, collides, has velocity); otherwise static.
    /// Optional <paramref name="behavior"/> is attached at the same time and its
    /// handle is returned (null if no behavior was provided).</summary>
    public async Task<(EdenId<PrimTag> PrimId, BehaviorHandle? Handle)> SpawnPrimAsync(
        Transform          pose,
        Vector3            scale,
        PrimFlags          flags,
        EdenBehavior?      behavior = null,
        CancellationToken  ct = default)
    {
        var primId = EdenId<PrimTag>.New();
        var prim   = new ServerPrim(primId, EdenId<UserTag>.Empty)
        {
            Pose  = pose,
            Scale = scale,
            Flags = flags,
        };

        // Register a box body sized by Scale (half-extent = scale / 2).
        var motion = flags.HasFlag(PrimFlags.Physical) ? MotionType.Dynamic : MotionType.Static;
        prim.BodyId = _physics.AddBox(
            center:     pose.Position,
            halfExtent: scale * 0.5f,
            rotation:   pose.Rotation,
            motion:     motion);

        _prims[primId] = prim;

        BehaviorHandle? handle = null;
        if (behavior is not null)
        {
            var self = new ServerSelfContext(prim, BroadcastPrimUpdateAsync);
            handle = await _behaviorHost.AttachAsync(behavior, self, _worldContext, ct)
                .ConfigureAwait(false);
            prim.Behavior = handle;
        }

        await BroadcastPrimUpdateAsync(prim.ToPrimState()).ConfigureAwait(false);

        _logger.LogInformation("Spawned prim {PrimId} ({Motion}){Behavior}",
            primId, motion, behavior is null ? "" : $" with behavior {behavior.GetType().Name}");
        return (primId, handle);
    }

    /// <summary>Back-compat helper: spawn a static unit-scale prim at the
    /// origin with a behavior. New callers should prefer <see cref="SpawnPrimAsync"/>.</summary>
    public async Task<(EdenId<PrimTag> PrimId, BehaviorHandle Handle)> SpawnPrimWithBehaviorAsync(
        EdenBehavior       behavior,
        CancellationToken  ct = default)
    {
        var (id, handle) = await SpawnPrimAsync(
            Transform.Identity, Vector3.One, PrimFlags.None, behavior, ct)
            .ConfigureAwait(false);
        return (id, handle!);
    }

    private Task BroadcastPrimUpdateAsync(PrimState state) =>
        BroadcastAsync(MessageKind.PrimUpdate, new PrimUpdate(state));

    /// <summary>Read a prim's current pose. Throws if the prim id is unknown.</summary>
    public Transform GetPrimPose(EdenId<PrimTag> id) =>
        _prims.TryGetValue(id, out var prim)
            ? prim.Pose
            : throw new KeyNotFoundException($"No prim with id {id}");

    internal HealthcheckReply BuildHealthReply() => new(
        Product:       EdenVersion.Product,
        Release:       EdenVersion.Release,
        WireProtocol:  EdenVersion.WireProtocol,
        UptimeSeconds: (long)(DateTime.UtcNow - _startedUtc).TotalSeconds,
        SessionCount:  _sessions.Count,
        WorldId:       _worldId);

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

                    case MessageKind.Healthcheck:
                        await transport.SendAsync(
                            Envelope.Encode(MessageKind.HealthcheckReply, BuildHealthReply()),
                            ct).ConfigureAwait(false);
                        break;

                    case MessageKind.AvatarUpdate when session is not null:
                        var update = Envelope.DecodePayload<AvatarUpdate>(frame.Value);
                        await OnAvatarUpdate(session, update, ct);
                        break;

                    case MessageKind.ClientTouchPrim when session is not null:
                        var touch = Envelope.DecodePayload<ClientTouchPrim>(frame.Value);
                        await OnClientTouchPrim(session, touch, ct);
                        break;

                    case MessageKind.ChatMessage when session is not null:
                        var chat = Envelope.DecodePayload<ChatMessage>(frame.Value);
                        await OnChatMessage(session, chat, ct);
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
        //    avatar already present — *excluding* their own — plus one
        //    PrimUpdate per prim in the registry).
        foreach (var existing in _avatars.Values)
        {
            if (existing.UserId == userId) continue;
            await transport.SendAsync(
                Envelope.Encode(MessageKind.AvatarUpdate, new AvatarUpdate(existing)),
                ct).ConfigureAwait(false);
        }
        foreach (var prim in _prims.Values)
        {
            await transport.SendAsync(
                Envelope.Encode(MessageKind.PrimUpdate, new PrimUpdate(prim.ToPrimState())),
                ct).ConfigureAwait(false);
        }

        // Current world state so the viewer can paint sky/weather immediately.
        await transport.SendAsync(
            Envelope.Encode(MessageKind.WorldStateUpdate, new WorldStateUpdate(WorldSnapshot)),
            ct).ConfigureAwait(false);

        // 3) Announce the newcomer to everyone else.
        await BroadcastExceptAsync(sessionId, MessageKind.AvatarUpdate, new AvatarUpdate(initialAvatar), ct);

        return session;
    }

    private async Task OnChatMessage(ClientSession session, ChatMessage chat, CancellationToken ct)
    {
        // Normalise: server-authoritative From; don't trust the client's field.
        var normalised = chat with { From = session.UserId };

        // 1) Broadcast to every connected viewer (including the sender, so the
        //    sender can render their own bubble from the same wire event).
        await BroadcastAsync(MessageKind.ChatMessage, normalised, ct).ConfigureAwait(false);

        // 2) Dispatch to every attached behavior with a matching [OnChat(Channel=)].
        var state  = _avatars.GetValueOrDefault(session.UserId);
        var avatar = new Avatar(
            UserId:      session.UserId,
            DisplayName: state.DisplayName ?? string.Empty,
            Pose:        state.Transform);
        await _behaviorHost.RaiseChatAsync(avatar, normalised.Text, normalised.Channel, ct)
            .ConfigureAwait(false);
    }

    private async Task OnClientTouchPrim(ClientSession session, ClientTouchPrim touch, CancellationToken ct)
    {
        if (!_prims.TryGetValue(touch.PrimId, out var prim))
        {
            _logger.LogDebug("Touch on unknown prim {PrimId} from session {SessionId}",
                touch.PrimId, session.Id);
            return;
        }

        if (prim.Behavior is null) return;

        var state  = _avatars.GetValueOrDefault(session.UserId);
        var avatar = new Avatar(
            UserId:      session.UserId,
            DisplayName: state.DisplayName ?? string.Empty,
            Pose:        state.Transform);

        await prim.Behavior.DispatchAsync(typeof(OnTouchAttribute), [avatar], ct: ct)
            .ConfigureAwait(false);
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

    private async Task WorldClockLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(WorldClockStep);
        // Real seconds per game-hour: dayLength / 24. Invert for game-hours per real-second.
        var gameHoursPerRealSecond = 24f / _dayLengthSeconds;
        var stepHours = gameHoursPerRealSecond * (float)WorldClockStep.TotalSeconds;

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                WorldState snapshot;
                lock (_worldLock)
                {
                    var t = _world.TimeOfDayHours + stepHours;
                    if (t >= 24f) t -= 24f;
                    _world = _world with { TimeOfDayHours = t };
                    snapshot = _world;
                }

                try
                {
                    await BroadcastAsync(MessageKind.WorldStateUpdate, new WorldStateUpdate(snapshot), ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "WorldState broadcast failed");
                }
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "World-clock loop crashed");
        }
    }

    private async Task PhysicsLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PhysicsStep);
        var dt = (float)PhysicsStep.TotalSeconds;

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                _physics.Step(dt);
                await SyncDynamicPrimsAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Physics loop crashed");
        }
    }

    private async Task SyncDynamicPrimsAsync(CancellationToken ct)
    {
        foreach (var prim in _prims.Values)
        {
            if (!prim.Flags.HasFlag(PrimFlags.Physical) || prim.BodyId is not { } bodyId)
                continue;

            var newPose = _physics.GetTransform(bodyId);
            if (newPose == prim.Pose) continue; // bit-exact no-op

            prim.Pose = newPose;
            try
            {
                await BroadcastAsync(MessageKind.PrimUpdate, new PrimUpdate(prim.ToPrimState()), ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PrimUpdate broadcast failed for prim {PrimId}", prim.Id);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _shutdownCts.Cancel();
        try { await _physicsLoop.ConfigureAwait(false); }    catch (OperationCanceledException) { }
        try { await _worldClockLoop.ConfigureAwait(false); } catch (OperationCanceledException) { }

        // Detach every attached behavior before tearing down the physics world
        // so their OnDisable runs against a still-valid context.
        foreach (var prim in _prims.Values)
        {
            if (prim.Behavior is { } b)
                try { await b.DisposeAsync().ConfigureAwait(false); } catch { }
        }

        _physics.Dispose();
        _shutdownCts.Dispose();
    }
}

internal sealed record class ClientSession(
    EdenId<SessionTag> Id,
    EdenId<UserTag>    UserId,
    ITransport         Transport);
