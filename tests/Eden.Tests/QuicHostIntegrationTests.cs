using System.Net.Quic;
using Eden.Launcher;
using Eden.Shared;
using Eden.Shared.Wire;
using Eden.Shared.Wire.Messages;

namespace Eden.Tests;

/// <summary>
/// Real-QUIC end-to-end test. Starts a host on an ephemeral port and connects
/// a client to it. Skipped on platforms without MsQuic.
/// </summary>
public class QuicHostIntegrationTests
{
    [SkippableFact]
    public async Task Host_And_Client_Complete_Handshake_Over_Quic()
    {
        Skip.IfNot(QuicListener.IsSupported && QuicConnection.IsSupported,
            "QUIC is not supported on this runtime / OS (MsQuic missing).");

        await using var host = await EdenLauncher.StartHostAsync(port: 0);
        var port = host.LocalEndPoint.Port;

        await using var transport = (IAsyncDisposable)await EdenLauncher.ConnectAsync("localhost", port);
        var t = (Eden.Shared.Transport.ITransport)transport;

        var hello = new ClientHello("Test", "0.0.1", EdenVersion.WireProtocol, AuthToken: null);
        await t.SendAsync(Envelope.Encode(MessageKind.ClientHello, hello));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        ServerHello? reply = null;
        while (reply is null)
        {
            var frame = await t.ReceiveAsync(cts.Token);
            Assert.NotNull(frame);
            if (Envelope.PeekKind(frame!.Value) == MessageKind.ServerHello)
                reply = Envelope.DecodePayload<ServerHello>(frame.Value);
        }

        Assert.Equal(EdenVersion.WireProtocol, reply.Value.WireProtocol);
        Assert.Null(reply.Value.RejectReason);
        Assert.False(reply.Value.SessionId.IsEmpty);
        Assert.False(reply.Value.UserId.IsEmpty);
    }
}
