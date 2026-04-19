using Eden.Launcher;
using Eden.Shared;
using Eden.Shared.Wire;
using Eden.Shared.Wire.Messages;

namespace Eden.Shared.Tests;

/// <summary>
/// Full solo-mode exercise: launcher spins up an in-process Eden server and
/// returns a transport. Client-side code (here: a test) runs a handshake
/// against it as if it were a remote server. Proves the solo path and the
/// multiplayer path use the same message code.
/// </summary>
public class SoloLauncherTests
{
    [Fact]
    public async Task Solo_Server_Responds_To_ClientHello()
    {
        await using var handle = EdenLauncher.StartSolo();

        var hello = new ClientHello("TestClient", "0.0.1", EdenVersion.WireProtocol, AuthToken: null);
        await handle.Transport.SendAsync(Envelope.Encode(MessageKind.ClientHello, hello));

        var frame = await handle.Transport.ReceiveAsync();
        Assert.NotNull(frame);
        Assert.Equal(MessageKind.ServerHello, Envelope.PeekKind(frame!.Value));

        var reply = Envelope.DecodePayload<ServerHello>(frame.Value);
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
    public async Task Dispose_Stops_The_Server_Cleanly()
    {
        var handle = EdenLauncher.StartSolo();
        await handle.DisposeAsync();
        // If the server task deadlocked or threw unexpectedly, disposal would
        // hang or throw. Reaching this line is the assertion.
    }
}
