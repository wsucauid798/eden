using Eden.Launcher;
using Eden.Shared;
using Eden.Shared.Transport;
using Eden.Shared.Wire;
using Eden.Shared.Wire.Messages;

namespace Eden.Launcher.Tests;

/// <summary>
/// Full solo-mode exercise: launcher spins up an in-process Eden server and
/// returns a transport. Client-side code (here: a test) runs against it as
/// if it were a remote server. Proves the solo path and the multiplayer path
/// use the same message code.
/// </summary>
public class SoloLauncherTests
{
    private static async Task<ServerHello> HandshakeAsync(ITransport transport)
    {
        var hello = new ClientHello("TestClient", "0.0.1", EdenVersion.WireProtocol, AuthToken: null);
        await transport.SendAsync(Envelope.Encode(MessageKind.ClientHello, hello));
        var frame = await transport.ReceiveAsync();
        return Envelope.DecodePayload<ServerHello>(frame!.Value);
    }

    [Fact]
    public async Task Solo_Server_Responds_To_ClientHello()
    {
        await using var handle = EdenLauncher.StartSolo();
        var reply = await HandshakeAsync(handle.Transport);

        Assert.Equal(EdenVersion.WireProtocol, reply.WireProtocol);
        Assert.Null(reply.RejectReason);
        Assert.False(reply.SessionId.IsEmpty);
        Assert.False(reply.WorldId.IsEmpty);
    }

    [Fact]
    public async Task Solo_Server_Rejects_Mismatched_Wire_Protocol()
    {
        await using var handle = EdenLauncher.StartSolo();

        var hello = new ClientHello("TestClient", "0.0.1", WireProtocol: "9.99", AuthToken: null);
        await handle.Transport.SendAsync(Envelope.Encode(MessageKind.ClientHello, hello));

        var frame = await handle.Transport.ReceiveAsync();
        var reply = Envelope.DecodePayload<ServerHello>(frame!.Value);

        Assert.NotNull(reply.RejectReason);
        Assert.Contains("Unsupported wire protocol", reply.RejectReason!);
    }

    [Fact]
    public async Task Ping_Is_Answered_With_Pong()
    {
        await using var handle = EdenLauncher.StartSolo();
        await HandshakeAsync(handle.Transport);

        var sent = new Ping(ClientTicks: 12345);
        await handle.Transport.SendAsync(Envelope.Encode(MessageKind.Ping, sent));

        // Post-handshake the server has broadcast existing prims + initial
        // WorldStateUpdate into the pipe. Skip past them to find our Pong.
        Pong? pong = null;
        for (int i = 0; i < 10 && pong is null; i++)
        {
            var frame = await handle.Transport.ReceiveAsync();
            Assert.NotNull(frame);
            if (Envelope.PeekKind(frame.Value) == MessageKind.Pong)
                pong = Envelope.DecodePayload<Pong>(frame.Value);
        }

        Assert.NotNull(pong);
        Assert.Equal(sent.ClientTicks, pong.Value.ClientTicks);
        Assert.True(pong.Value.ServerTicks > 0);
    }

    [Fact]
    public async Task Two_Clients_Each_Get_Own_Session()
    {
        await using var handle = EdenLauncher.StartSolo();
        var clientA = handle.Transport;   // first (primary)
        var clientB = handle.Connect();   // second

        var replyA = await HandshakeAsync(clientA);
        var replyB = await HandshakeAsync(clientB);

        Assert.NotEqual(replyA.SessionId, replyB.SessionId);
        Assert.Equal(replyA.WorldId, replyB.WorldId); // same world, different sessions
    }

    [Fact]
    public async Task Dispose_Stops_The_Server_Cleanly()
    {
        var handle = EdenLauncher.StartSolo();
        await handle.DisposeAsync();
    }
}
