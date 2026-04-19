using Eden.Shared.Ids;
using Eden.Shared.Wire;
using Eden.Shared.Wire.Messages;

namespace Eden.Shared.Tests;

public class EnvelopeTests
{
    [Fact]
    public void ClientHello_RoundTrips_Through_Envelope()
    {
        var hello = new ClientHello(
            ClientName:    "Eden.Viewer",
            ClientVersion: "0.0.1",
            WireProtocol:  "0.1",
            AuthToken:     null);

        var frame = Envelope.Encode(MessageKind.ClientHello, hello);

        Assert.Equal(MessageKind.ClientHello, Envelope.PeekKind(frame));
        Assert.Equal(hello, Envelope.DecodePayload<ClientHello>(frame));
    }

    [Fact]
    public void ServerHello_RoundTrips_Through_Envelope()
    {
        var sh = new ServerHello(
            SessionId:    EdenId<SessionTag>.New(),
            UserId:       EdenId<UserTag>.New(),
            WireProtocol: "0.1",
            WorldId:      EdenId<WorldTag>.New(),
            RejectReason: null);

        var frame = Envelope.Encode(MessageKind.ServerHello, sh);

        Assert.Equal(MessageKind.ServerHello, Envelope.PeekKind(frame));
        Assert.Equal(sh, Envelope.DecodePayload<ServerHello>(frame));
    }

    [Fact]
    public void Empty_Frame_Is_Rejected()
    {
        Assert.Throws<ArgumentException>(() => Envelope.PeekKind(ReadOnlyMemory<byte>.Empty));
        Assert.Throws<ArgumentException>(() => Envelope.DecodePayload<ClientHello>(ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void First_Byte_Is_The_Kind_Tag()
    {
        var frame = Envelope.Encode(MessageKind.Ping, 42);
        Assert.Equal((byte)MessageKind.Ping, frame[0]);
    }
}
