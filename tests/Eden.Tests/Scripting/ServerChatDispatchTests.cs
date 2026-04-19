using Eden.Client;
using Eden.Launcher;
using Eden.Scripting;
using Eden.Shared.Wire.Messages;

namespace Eden.Tests.Scripting;

/// <summary>
/// End-to-end: a viewer sends <c>ChatMessage</c>. The server normalises it,
/// broadcasts it to every viewer, and dispatches to any attached behavior
/// with a matching <c>[OnChat(Channel=)]</c>. Sender sees their own echo.
/// </summary>
public class ServerChatDispatchTests
{
    [Fact]
    public async Task Chat_Is_Broadcast_Back_To_Sender()
    {
        await using var host = EdenLauncher.StartSolo();

        await using var alice = new ViewerClient(host.Transport);
        await alice.ConnectAsync("Alice");

        var tcs = new TaskCompletionSource<ChatMessage>();
        alice.ChatReceived += msg => tcs.TrySetResult(msg);

        await alice.SendChatAsync("hello world", channel: 0);

        var msg = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal("hello world",  msg.Text);
        Assert.Equal(0,              msg.Channel);
        Assert.Equal(alice.MyUserId, msg.From);
    }

    [Fact]
    public async Task Chat_Dispatches_To_Matching_OnChat_Handler_On_Server_Prim()
    {
        await using var host = EdenLauncher.StartSolo();

        var listener = new ChannelListener();
        var (_, _) = await host.Server.SpawnPrimWithBehaviorAsync(listener);

        await using var alice = new ViewerClient(host.Transport);
        await alice.ConnectAsync("Alice");

        await alice.SendChatAsync("secret",  channel: 5);
        await alice.SendChatAsync("public",  channel: 0);
        await alice.SendChatAsync("secret2", channel: 5);

        var fired = await WaitForAsync(() => listener.WhisperCount == 2, TimeSpan.FromSeconds(1));
        Assert.True(fired, $"Expected whisper=2, got {listener.WhisperCount}");
        Assert.Equal(1, listener.PublicCount);
    }

    [Fact]
    public async Task Server_Overrides_Client_From_Field()
    {
        // The client can't spoof someone else's identity by putting a foreign
        // UserId in the From field — the server rewrites it.
        await using var host = EdenLauncher.StartSolo();

        await using var alice = new ViewerClient(host.Transport);
        await alice.ConnectAsync("Alice");

        var tcs = new TaskCompletionSource<ChatMessage>();
        alice.ChatReceived += msg => tcs.TrySetResult(msg);

        // Bypass SendChatAsync to put a bogus From on the wire.
        var bogus = new ChatMessage(
            From:    Eden.Shared.Ids.EdenId<Eden.Shared.Ids.UserTag>.New(),
            Channel: 0,
            Text:    "impersonation attempt");
        await host.Transport.SendAsync(
            Eden.Shared.Wire.Envelope.Encode(
                Eden.Shared.Wire.MessageKind.ChatMessage, bogus));

        var msg = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(alice.MyUserId, msg.From); // server overrode the client's field
    }

    public class ChannelListener : EdenBehavior
    {
        public int PublicCount  { get; private set; }
        public int WhisperCount { get; private set; }

        [OnChat(Channel = 0)]
        public Task Pub(Avatar from, string text) { PublicCount++;  return Task.CompletedTask; }

        [OnChat(Channel = 5)]
        public Task Whisper(Avatar from, string text) { WhisperCount++; return Task.CompletedTask; }
    }

    private static async Task<bool> WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return true;
            await Task.Delay(10);
        }
        return predicate();
    }
}
