using Eden.Scripting;
using Eden.Scripting.Host;

namespace Eden.Scripting.Tests;

/// <summary>
/// Exercises the periodic-timer scheduler: <c>[OnTimer(seconds)]</c> fires
/// repeatedly; disposing the handle stops firing; <c>[OnTick]</c> receives
/// a sensible <see cref="TimeSpan"/> delta.
/// </summary>
public class TimerSchedulerTests
{
    [Fact]
    public async Task OnTimer_Fires_Repeatedly_At_Configured_Interval()
    {
        var host = new BehaviorHost();
        var ticker = new Ticker();
        await using var _ = await host.AttachAsync(ticker, new MockSelf(), new MockWorld());

        // At 50ms period, 250ms should yield roughly 5 fires. Allow headroom.
        await Task.Delay(TimeSpan.FromMilliseconds(250));

        Assert.InRange(ticker.Count, 3, 10);
    }

    [Fact]
    public async Task Disposing_Handle_Stops_Timer_Firing()
    {
        var host = new BehaviorHost();
        var ticker = new Ticker();
        var handle = await host.AttachAsync(ticker, new MockSelf(), new MockWorld());

        await Task.Delay(TimeSpan.FromMilliseconds(150));
        await handle.DisposeAsync();

        var stopped = ticker.Count;
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        Assert.Equal(stopped, ticker.Count);
        Assert.True(ticker.Disabled, "OnDisable should run after the timer stops");
    }

    [Fact]
    public async Task OnTick_Receives_Nonzero_Delta()
    {
        var host = new BehaviorHost();
        var tocker = new Tocker();
        await using var _ = await host.AttachAsync(tocker, new MockSelf(), new MockWorld());

        await Task.Delay(TimeSpan.FromMilliseconds(200));

        Assert.True(tocker.Count >= 1, "expected at least one tick");
        Assert.True(tocker.LastDelta > TimeSpan.Zero, "delta should be positive");
    }

    [Fact]
    public async Task Two_OnTimer_Attributes_Fire_Independently()
    {
        // Behavior with two [OnTimer] methods at different periods — each
        // fires on its own timer, they don't cross-trigger.
        var host = new BehaviorHost();
        var dual = new DualTimer();
        await using var _ = await host.AttachAsync(dual, new MockSelf(), new MockWorld());

        await Task.Delay(TimeSpan.FromMilliseconds(250));

        Assert.True(dual.FastCount >= 3, $"fast count was {dual.FastCount}");
        Assert.True(dual.SlowCount >= 1, $"slow count was {dual.SlowCount}");
        Assert.True(dual.FastCount > dual.SlowCount,
            $"fast ({dual.FastCount}) should out-fire slow ({dual.SlowCount})");
    }

    public class Ticker : EdenBehavior
    {
        public int  Count    { get; private set; }
        public bool Disabled { get; private set; }

        [OnTimer(seconds: 0.05)]
        public Task Tick() { Count++; return Task.CompletedTask; }

        protected override Task OnDisable() { Disabled = true; return Task.CompletedTask; }
    }

    public class Tocker : EdenBehavior
    {
        public int      Count     { get; private set; }
        public TimeSpan LastDelta { get; private set; }

        [OnTick(seconds: 0.05)]
        public Task Tick(TimeSpan delta)
        {
            Count++;
            LastDelta = delta;
            return Task.CompletedTask;
        }
    }

    public class DualTimer : EdenBehavior
    {
        public int FastCount { get; private set; }
        public int SlowCount { get; private set; }

        [OnTimer(seconds: 0.05)]
        public Task Fast() { FastCount++; return Task.CompletedTask; }

        [OnTimer(seconds: 0.15)]
        public Task Slow() { SlowCount++; return Task.CompletedTask; }
    }
}
