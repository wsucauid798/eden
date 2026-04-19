using Eden.Launcher;
using Eden.Shared;
using Eden.Shared.Wire;
using Eden.Shared.Wire.Messages;

namespace Eden.Server.Tests;

public class HealthcheckTests
{
    [Fact]
    public async Task Healthcheck_Works_Pre_Handshake_Over_InMemory()
    {
        // A probe should be answerable without any ClientHello.
        await using var host = EdenLauncher.StartSolo();

        // Solo's Connect() pre-attaches the host's own seat. Add a second
        // viewer-side transport and send the probe straight from it without
        // doing a handshake.
        var probe = host.Connect();
        await probe.SendAsync(
            Envelope.Encode(MessageKind.Healthcheck, new Healthcheck()));

        var frame = await probe.ReceiveAsync();
        Assert.NotNull(frame);
        Assert.Equal(MessageKind.HealthcheckReply, Envelope.PeekKind(frame.Value));

        var reply = Envelope.DecodePayload<HealthcheckReply>(frame.Value);
        Assert.Equal(EdenVersion.Product,      reply.Product);
        Assert.Equal(EdenVersion.Release,      reply.Release);
        Assert.Equal(EdenVersion.WireProtocol, reply.WireProtocol);
        Assert.True(reply.UptimeSeconds >= 0);
        Assert.False(reply.WorldId.IsEmpty);
    }

    [Fact]
    public async Task Healthcheck_Works_Pre_Handshake_Over_Quic()
    {
        await using var host = await EdenLauncher.StartHostAsync(port: 0);
        var port = host.LocalEndPoint.Port;

        var reply = await EdenLauncher.CheckHealthAsync(
            "localhost", port,
            timeout: TimeSpan.FromSeconds(5));

        Assert.Equal(EdenVersion.Product,      reply.Product);
        Assert.Equal(EdenVersion.WireProtocol, reply.WireProtocol);
        Assert.Equal(0, reply.SessionCount); // the probe disconnects before being counted
    }
}
