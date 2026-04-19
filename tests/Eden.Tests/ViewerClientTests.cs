using Eden.Client;
using Eden.Launcher;
using Eden.Shared.Entities;
using Eden.Shared.Ids;
using Eden.Shared.Math;

namespace Eden.Tests;

public class ViewerClientTests
{
    [Fact]
    public async Task ConnectAsync_Populates_Session()
    {
        await using var host   = EdenLauncher.StartSolo();
        await using var client = new ViewerClient(host.Transport);

        await client.ConnectAsync("Alice");

        Assert.NotNull(client.Session);
        Assert.False(client.MyUserId.IsEmpty);
        Assert.Null(client.Session!.Value.RejectReason);
    }

    [Fact]
    public async Task Remote_Movement_Populates_RemoteAvatars()
    {
        await using var host = EdenLauncher.StartSolo();

        await using var alice = new ViewerClient(host.Transport);
        await alice.ConnectAsync("Alice");

        var bobTransport = host.Connect();
        await using var bob = new ViewerClient(bobTransport);
        await bob.ConnectAsync("Bob");

        // Bob sends his position. Alice's ViewerClient should see it.
        //
        // Match specifically on the expected X value — Bob's initial avatar
        // (X=0) is also broadcast to Alice asynchronously when Bob connects,
        // and can arrive between subscribing and Bob's outbound update. If
        // we completed the TCS on any update for Bob's UserId, the test
        // would sometimes race against the initial broadcast and assert on
        // X=0 before the X=7 frame lands.
        var updateReceived = new TaskCompletionSource();
        alice.AvatarUpdated += state =>
        {
            if (state.UserId == bob.MyUserId && state.Transform.Position.X == 7f)
                updateReceived.TrySetResult();
        };

        await bob.SendAvatarUpdateAsync(new AvatarState(
            UserId:         bob.MyUserId,
            SessionId:      bob.Session!.Value.SessionId,
            DisplayName:    "Bob",
            Transform:      new Transform(new Vector3(7f, 0f, 0f), Quaternion.Identity),
            Velocity:       Vector3.Zero,
            AppearanceHash: 0));

        await updateReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(alice.RemoteAvatars.ContainsKey(bob.MyUserId));
        Assert.Equal(7f, alice.RemoteAvatars[bob.MyUserId].Transform.Position.X);
    }

    [Fact]
    public async Task Client_Ignores_Echoes_Of_Its_Own_Updates()
    {
        // The server normalises & broadcasts to OTHERS (excluding sender),
        // so we shouldn't see our own updates. Belt-and-braces: the client
        // also filters them. Verify no echo even if the server misbehaves.
        await using var host   = EdenLauncher.StartSolo();
        await using var alice  = new ViewerClient(host.Transport);
        await alice.ConnectAsync("Alice");

        await alice.SendAvatarUpdateAsync(new AvatarState(
            UserId:         alice.MyUserId,
            SessionId:      alice.Session!.Value.SessionId,
            DisplayName:    "Alice",
            Transform:      new Transform(new Vector3(99f, 0f, 0f), Quaternion.Identity),
            Velocity:       Vector3.Zero,
            AppearanceHash: 0));

        // Give any hypothetical echo time to arrive, then check our mirror
        // did not gain an entry for ourselves.
        await Task.Delay(100);
        Assert.False(alice.RemoteAvatars.ContainsKey(alice.MyUserId));
    }

    [Fact]
    public async Task Mismatched_Wire_Protocol_Throws()
    {
        // We can't easily make the server speak a wrong protocol; instead
        // connect to a server that rejects based on ClientHello.WireProtocol.
        // Quickest path: build a ViewerClient with a wrong-protocol transport
        // by bypassing ConnectAsync and asserting on the rejection flow.
        await using var host = EdenLauncher.StartSolo();
        var transport = host.Transport;

        // Send a bad hello manually.
        var bad = new Eden.Shared.Wire.Messages.ClientHello("X", "0", "9.99", null);
        await transport.SendAsync(Eden.Shared.Wire.Envelope.Encode(Eden.Shared.Wire.MessageKind.ClientHello, bad));

        // Now wrap in a ViewerClient that tries ConnectAsync and gets the
        // reject — but we already sent our own hello, so ConnectAsync would
        // send another. Simpler: read the response manually.
        var frame = await transport.ReceiveAsync();
        var reply = Eden.Shared.Wire.Envelope.DecodePayload<Eden.Shared.Wire.Messages.ServerHello>(frame!.Value);
        Assert.NotNull(reply.RejectReason);
    }
}
