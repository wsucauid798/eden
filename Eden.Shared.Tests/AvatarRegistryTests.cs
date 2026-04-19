using Eden.Launcher;
using Eden.Shared;
using Eden.Shared.Entities;
using Eden.Shared.Math;
using Eden.Shared.Transport;
using Eden.Shared.Wire;
using Eden.Shared.Wire.Messages;

namespace Eden.Shared.Tests;

/// <summary>
/// Multi-client world-state exercise. Each client connects, the server
/// tracks avatars, and state changes fan-out to the other clients.
/// </summary>
public class AvatarRegistryTests
{
    private static async Task<ServerHello> HandshakeAsync(ITransport transport, string clientName = "Client")
    {
        var hello = new ClientHello(clientName, "0.0.1", EdenVersion.WireProtocol, AuthToken: null);
        await transport.SendAsync(Envelope.Encode(MessageKind.ClientHello, hello));

        // Skip past any broadcast frames until we see the ServerHello.
        while (true)
        {
            var frame = await transport.ReceiveAsync();
            Assert.NotNull(frame);
            if (Envelope.PeekKind(frame!.Value) == MessageKind.ServerHello)
                return Envelope.DecodePayload<ServerHello>(frame.Value);
        }
    }

    private static async Task<(MessageKind kind, ReadOnlyMemory<byte> frame)> ReceiveOneAsync(ITransport transport)
    {
        var frame = await transport.ReceiveAsync();
        Assert.NotNull(frame);
        return (Envelope.PeekKind(frame!.Value), frame.Value);
    }

    [Fact]
    public async Task Joining_Client_Sees_Existing_Avatars()
    {
        await using var handle = EdenLauncher.StartSolo();

        // Client A joins first.
        var clientA = handle.Transport;
        var replyA  = await HandshakeAsync(clientA, "Alice");

        // Client B joins and completes its handshake.
        var clientB = handle.Connect();
        var replyB  = await HandshakeAsync(clientB, "Bob");

        // After ServerHello, the server syncs existing avatars. Expect one
        // AvatarUpdate for Alice.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        bool sawAlice = false;
        while (!sawAlice && !cts.IsCancellationRequested)
        {
            var frame = await clientB.ReceiveAsync(cts.Token);
            if (frame is null) break;
            if (Envelope.PeekKind(frame.Value) == MessageKind.AvatarUpdate)
            {
                var update = Envelope.DecodePayload<AvatarUpdate>(frame.Value);
                if (update.State.UserId == replyA.UserId) sawAlice = true;
            }
        }

        Assert.True(sawAlice, "Bob should have received Alice's AvatarUpdate during sync.");
    }

    [Fact]
    public async Task Movement_From_One_Client_Reaches_The_Other()
    {
        await using var handle = EdenLauncher.StartSolo();

        var clientA = handle.Transport;
        var replyA  = await HandshakeAsync(clientA, "Alice");

        var clientB = handle.Connect();
        var replyB  = await HandshakeAsync(clientB, "Bob");

        // Bob drains any pending join-announcement frames for Alice.
        // (Bob's frame queue may carry one AvatarUpdate for Alice's arrival.)
        await DrainAsync(clientB, timeoutMs: 50);

        // Alice moves.
        var moved = new AvatarState(
            UserId:         replyA.UserId,
            SessionId:      replyA.SessionId,
            DisplayName:    "Alice",
            Transform:      new Transform(new Vector3(42f, 0f, 0f), Quaternion.Identity),
            Velocity:       new Vector3(1f, 0f, 0f),
            AppearanceHash: 0);
        await clientA.SendAsync(Envelope.Encode(MessageKind.AvatarUpdate, new AvatarUpdate(moved)));

        // Bob should receive it.
        AvatarUpdate? receivedForAlice = null;
        while (receivedForAlice is null)
        {
            var (kind, frame) = await ReceiveOneAsync(clientB);
            if (kind == MessageKind.AvatarUpdate)
            {
                var update = Envelope.DecodePayload<AvatarUpdate>(frame);
                if (update.State.UserId == replyA.UserId)
                    receivedForAlice = update;
            }
        }

        Assert.Equal(moved.Transform.Position, receivedForAlice.Value.State.Transform.Position);
    }

    [Fact]
    public async Task Client_Cannot_Spoof_Another_Users_Avatar()
    {
        await using var handle = EdenLauncher.StartSolo();

        var clientA = handle.Transport;
        var replyA  = await HandshakeAsync(clientA, "Alice");

        var clientB = handle.Connect();
        var replyB  = await HandshakeAsync(clientB, "Bob");

        await DrainAsync(clientB, timeoutMs: 50);

        // Alice sends an AvatarUpdate claiming to be Bob.
        var spoofed = new AvatarState(
            UserId:         replyB.UserId,   // <-- wrong!
            SessionId:      replyB.SessionId,
            DisplayName:    "HaxorAlice",
            Transform:      new Transform(new Vector3(999f, 0f, 0f), Quaternion.Identity),
            Velocity:       Vector3.Zero,
            AppearanceHash: 0);
        await clientA.SendAsync(Envelope.Encode(MessageKind.AvatarUpdate, new AvatarUpdate(spoofed)));

        // The broadcast Bob receives should carry Alice's UserId, not Bob's.
        AvatarUpdate? broadcast = null;
        while (broadcast is null)
        {
            var (kind, frame) = await ReceiveOneAsync(clientB);
            if (kind == MessageKind.AvatarUpdate)
                broadcast = Envelope.DecodePayload<AvatarUpdate>(frame);
        }

        Assert.Equal(replyA.UserId, broadcast.Value.State.UserId);   // normalised
        Assert.NotEqual(replyB.UserId, broadcast.Value.State.UserId);
    }

    private static async Task DrainAsync(ITransport transport, int timeoutMs)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            while (!cts.IsCancellationRequested)
                await transport.ReceiveAsync(cts.Token);
        }
        catch (OperationCanceledException) { /* expected */ }
    }
}
