using Eden.Shared.Transport;

namespace Eden.Tests;

public class InMemoryTransportTests
{
    [Fact]
    public async Task Pair_Delivers_Frame_Each_Way()
    {
        var (a, b) = InMemoryTransport.CreatePair();

        await a.SendAsync(new byte[] { 1, 2, 3 });
        var received = await b.ReceiveAsync();
        Assert.Equal(new byte[] { 1, 2, 3 }, received!.Value.ToArray());

        await b.SendAsync(new byte[] { 4, 5 });
        var roundTrip = await a.ReceiveAsync();
        Assert.Equal(new byte[] { 4, 5 }, roundTrip!.Value.ToArray());
    }

    [Fact]
    public async Task Frames_Arrive_In_FIFO_Order()
    {
        var (a, b) = InMemoryTransport.CreatePair();

        for (byte i = 0; i < 5; i++)
            await a.SendAsync(new byte[] { i });

        for (byte i = 0; i < 5; i++)
        {
            var frame = await b.ReceiveAsync();
            Assert.Equal(new byte[] { i }, frame!.Value.ToArray());
        }
    }

    [Fact]
    public async Task Receive_Returns_Null_After_Peer_Disposed()
    {
        var (a, b) = InMemoryTransport.CreatePair();

        await a.SendAsync(new byte[] { 1 });
        await a.DisposeAsync();

        // Any in-flight frame still arrives…
        var first = await b.ReceiveAsync();
        Assert.Equal(new byte[] { 1 }, first!.Value.ToArray());

        // …then the channel is closed.
        var closed = await b.ReceiveAsync();
        Assert.Null(closed);
    }

    [Fact]
    public async Task Receive_Honours_Cancellation()
    {
        var (a, _) = InMemoryTransport.CreatePair();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await a.ReceiveAsync(cts.Token));
    }
}
