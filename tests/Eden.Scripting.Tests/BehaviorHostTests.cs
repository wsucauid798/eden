using Eden.Scripting;
using Eden.Scripting.Host;
using Eden.Shared.Ids;
using Eden.Shared.Math;

namespace Eden.Scripting.Tests;

public class BehaviorHostTests
{
    private static Avatar MakeAvatar(string name) =>
        new(EdenId<UserTag>.New(), name, Transform.Identity);

    [Fact]
    public async Task Attach_Wires_Context_And_Invokes_OnEnable()
    {
        var host = new BehaviorHost();
        var behavior = new Counter();
        await using var handle = await host.AttachAsync(behavior, new MockSelf(), new MockWorld());

        Assert.True(behavior.Enabled);
        Assert.NotNull(behavior.Self);
        Assert.NotNull(behavior.World);
        Assert.NotNull(behavior.Log);
    }

    [Fact]
    public async Task Dispose_Handle_Calls_OnDisable()
    {
        var host = new BehaviorHost();
        var behavior = new Counter();
        var handle = await host.AttachAsync(behavior, new MockSelf(), new MockWorld());
        await handle.DisposeAsync();
        Assert.True(behavior.Disabled);
    }

    [Fact]
    public async Task Dispatch_Touch_Fires_Matching_Handler_With_Args()
    {
        var host = new BehaviorHost();
        var behavior = new Counter();
        await using var _ = await host.AttachAsync(behavior, new MockSelf(), new MockWorld());

        var alice = MakeAvatar("Alice");
        await host.DispatchAsync(typeof(OnTouchAttribute), [alice]);

        Assert.Equal(1, behavior.TouchCount);
        Assert.Equal("Alice", behavior.LastToucher?.DisplayName);
    }

    [Fact]
    public async Task Dispatch_Skips_Behaviors_With_No_Matching_Handler()
    {
        var host = new BehaviorHost();
        var touchOnly = new Counter();
        var chatOnly  = new ChatOnly();
        await using var _ = await host.AttachAsync(touchOnly, new MockSelf(), new MockWorld());
        await using var __ = await host.AttachAsync(chatOnly,  new MockSelf(), new MockWorld());

        await host.DispatchAsync(typeof(OnTouchAttribute), [MakeAvatar("Eve")]);

        Assert.Equal(1, touchOnly.TouchCount);
        Assert.Equal(0, chatOnly.ChatCount);
    }

    [Fact]
    public async Task Dispatch_Fans_Out_To_All_Attached_Behaviors()
    {
        var host = new BehaviorHost();
        var a = new Counter();
        var b = new Counter();
        await using var _ = await host.AttachAsync(a, new MockSelf(), new MockWorld());
        await using var __ = await host.AttachAsync(b, new MockSelf(), new MockWorld());

        await host.DispatchAsync(typeof(OnTouchAttribute), [MakeAvatar("X")]);

        Assert.Equal(1, a.TouchCount);
        Assert.Equal(1, b.TouchCount);
    }

    [Fact]
    public async Task Handler_Exception_Is_Logged_And_Swallowed()
    {
        var host = new BehaviorHost();
        var bad  = new Thrower();
        var good = new Counter();
        await using var _ = await host.AttachAsync(bad,  new MockSelf(), new MockWorld());
        await using var __ = await host.AttachAsync(good, new MockSelf(), new MockWorld());

        // Must not throw; the good behavior must still receive the event.
        await host.DispatchAsync(typeof(OnTouchAttribute), [MakeAvatar("Mallory")]);

        Assert.Equal(1, good.TouchCount);
    }

    [Fact]
    public async Task Detached_Behavior_No_Longer_Receives_Events()
    {
        var host = new BehaviorHost();
        var behavior = new Counter();
        var handle = await host.AttachAsync(behavior, new MockSelf(), new MockWorld());

        await host.DispatchAsync(typeof(OnTouchAttribute), [MakeAvatar("A")]);
        await handle.DisposeAsync();
        await host.DispatchAsync(typeof(OnTouchAttribute), [MakeAvatar("B")]);

        Assert.Equal(1, behavior.TouchCount);
    }

    [Fact]
    public void Discover_Finds_EdenBehavior_Subclasses()
    {
        var types = BehaviorHost.Discover(typeof(Counter).Assembly);
        Assert.Contains(typeof(Counter), types);
        Assert.Contains(typeof(ChatOnly), types);
    }

    // --- Test behaviors ---

    public class Counter : EdenBehavior
    {
        public int     TouchCount { get; private set; }
        public Avatar? LastToucher { get; private set; }
        public bool    Enabled  { get; private set; }
        public bool    Disabled { get; private set; }

        protected override Task OnEnable()  { Enabled  = true; return Task.CompletedTask; }
        protected override Task OnDisable() { Disabled = true; return Task.CompletedTask; }

        [OnTouch]
        public Task Touch(Avatar who)
        {
            TouchCount++;
            LastToucher = who;
            return Task.CompletedTask;
        }
    }

    public class ChatOnly : EdenBehavior
    {
        public int ChatCount { get; private set; }

        [OnChat]
        public Task Heard(Avatar from, string text) { ChatCount++; return Task.CompletedTask; }
    }

    public class Thrower : EdenBehavior
    {
        [OnTouch]
        public Task Boom(Avatar who) => throw new InvalidOperationException("kaboom");
    }
}
