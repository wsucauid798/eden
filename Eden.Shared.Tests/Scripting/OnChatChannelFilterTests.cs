using Eden.Scripting;
using Eden.Scripting.Host;
using Eden.Shared.Ids;
using Eden.Shared.Math;

namespace Eden.Shared.Tests.Scripting;

public class OnChatChannelFilterTests
{
    private static Avatar MakeAvatar() => new(EdenId<UserTag>.New(), "Alice", Transform.Identity);

    [Fact]
    public async Task Each_Channel_Handler_Fires_Only_On_Its_Own_Channel()
    {
        var host     = new BehaviorHost();
        var listener = new ChannelListener();
        await using var _ = await host.AttachAsync(listener, new MockSelf(), new MockWorld());

        // One raise per channel; each handler should fire exactly once.
        await host.RaiseChatAsync(MakeAvatar(), "public",  channel: 0);
        await host.RaiseChatAsync(MakeAvatar(), "whisper", channel: 5);

        Assert.Equal(1, listener.PublicCount);
        Assert.Equal(1, listener.WhisperCount);
    }

    [Fact]
    public async Task Channel_5_Handler_Fires_Only_On_Channel_5()
    {
        var host     = new BehaviorHost();
        var listener = new ChannelListener();
        await using var _ = await host.AttachAsync(listener, new MockSelf(), new MockWorld());

        await host.RaiseChatAsync(MakeAvatar(), "whisper-a", channel: 5);
        await host.RaiseChatAsync(MakeAvatar(), "whisper-b", channel: 5);
        await host.RaiseChatAsync(MakeAvatar(), "public",    channel: 0);

        Assert.Equal(1, listener.PublicCount);
        Assert.Equal(2, listener.WhisperCount);
    }

    [Fact]
    public async Task Unfiltered_Dispatch_Still_Fires_All_Chat_Handlers()
    {
        // DispatchAsync without a filter matches every handler of the type —
        // regression guard so the filter parameter is truly opt-in.
        var host     = new BehaviorHost();
        var listener = new ChannelListener();
        await using var _ = await host.AttachAsync(listener, new MockSelf(), new MockWorld());

        await host.DispatchAsync(typeof(OnChatAttribute), [MakeAvatar(), "hello"]);

        Assert.Equal(1, listener.PublicCount);
        Assert.Equal(1, listener.WhisperCount);
    }

    public class ChannelListener : EdenBehavior
    {
        public int PublicCount  { get; private set; }
        public int WhisperCount { get; private set; }

        [OnChat(Channel = 0)]
        public Task Public(Avatar from, string text) { PublicCount++;  return Task.CompletedTask; }

        [OnChat(Channel = 5)]
        public Task Whisper(Avatar from, string text) { WhisperCount++; return Task.CompletedTask; }
    }
}
