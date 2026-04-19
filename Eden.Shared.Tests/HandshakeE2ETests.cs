using Eden.Shared.Ids;
using Eden.Shared.Transport;
using Eden.Shared.Wire;
using Eden.Shared.Wire.Messages;

namespace Eden.Shared.Tests;

/// <summary>
/// End-to-end: wire a client and server through an in-memory transport and
/// exchange a handshake. Proves the transport + envelope + domain types work
/// together with no special-casing between solo and multiplayer paths.
/// </summary>
public class HandshakeE2ETests
{
    [Fact]
    public async Task Client_And_Server_Complete_Handshake_Over_InMemoryTransport()
    {
        var (clientSide, serverSide) = InMemoryTransport.CreatePair();

        // --- Server task: wait for a ClientHello, reply with ServerHello.
        var worldId    = EdenId<WorldTag>.New();
        var sessionId  = EdenId<SessionTag>.New();
        var userId     = EdenId<UserTag>.New();

        var serverTask = Task.Run(async () =>
        {
            var frame = await serverSide.ReceiveAsync();
            Assert.NotNull(frame);
            Assert.Equal(MessageKind.ClientHello, Envelope.PeekKind(frame!.Value));

            var hello = Envelope.DecodePayload<ClientHello>(frame.Value);
            Assert.Equal("0.1", hello.WireProtocol);

            var reply = new ServerHello(sessionId, userId, "0.1", worldId, RejectReason: null);
            await serverSide.SendAsync(Envelope.Encode(MessageKind.ServerHello, reply));
        });

        // --- Client: send ClientHello, expect ServerHello back.
        var clientHello = new ClientHello("Eden.Viewer", "0.0.1", "0.1", AuthToken: null);
        await clientSide.SendAsync(Envelope.Encode(MessageKind.ClientHello, clientHello));

        var response = await clientSide.ReceiveAsync();
        Assert.NotNull(response);
        Assert.Equal(MessageKind.ServerHello, Envelope.PeekKind(response!.Value));

        var serverHello = Envelope.DecodePayload<ServerHello>(response.Value);
        Assert.Equal(sessionId, serverHello.SessionId);
        Assert.Equal(userId,    serverHello.UserId);
        Assert.Equal(worldId,   serverHello.WorldId);
        Assert.Null(serverHello.RejectReason);

        await serverTask;
    }
}
