using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Eden.Scripting.Host;

/// <summary>
/// Runtime for <see cref="EdenBehavior"/> instances. Attaches behaviors
/// to contexts, invokes lifecycle, and dispatches events to handler methods
/// discovered by attribute. Thread-safe: attach/detach/dispatch may be
/// called concurrently. Handler exceptions are logged and swallowed so one
/// bad behavior cannot break its peers.
/// </summary>
public sealed class BehaviorHost
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<BehaviorHandle, byte> _attached = new();

    public BehaviorHost(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

    /// <summary>Reflect an assembly for concrete <see cref="EdenBehavior"/>
    /// subclasses. Abstract classes and the base itself are skipped.</summary>
    public static IReadOnlyList<Type> Discover(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(EdenBehavior).IsAssignableFrom(t))
            .ToList();

    /// <summary>Attach an already-constructed behavior, wire its context,
    /// and invoke <c>OnEnable</c>. The returned handle removes the behavior
    /// from dispatch when disposed.</summary>
    public async Task<BehaviorHandle> AttachAsync(
        EdenBehavior       behavior,
        ISelfContext       self,
        IWorldContext      world,
        CancellationToken  ct = default)
    {
        ArgumentNullException.ThrowIfNull(behavior);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(world);

        var descriptor = BehaviorDescriptor.For(behavior.GetType());
        var logger     = _loggerFactory.CreateLogger(behavior.GetType());

        behavior.Self  = self;
        behavior.World = world;
        behavior.Log   = logger;

        var handle = new BehaviorHandle(this, behavior, descriptor, logger);
        _attached.TryAdd(handle, 0);

        try
        {
            var task = (Task)descriptor.OnEnableMethod.Invoke(behavior, null)!;
            await task.ConfigureAwait(false);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            logger.LogWarning(tie.InnerException,
                "OnEnable threw in {Type}; leaving behavior attached",
                behavior.GetType().Name);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "OnEnable threw in {Type}; leaving behavior attached",
                behavior.GetType().Name);
        }

        handle.RegisterTimers(StartTimerLoops(handle));
        return handle;
    }

    private static List<Task> StartTimerLoops(BehaviorHandle handle)
    {
        var tasks = new List<Task>();
        foreach (var (attrType, bindings) in handle.Descriptor.Handlers)
        {
            foreach (var binding in bindings)
            {
                switch (binding.Attribute)
                {
                    case OnTimerAttribute t:
                        tasks.Add(RunTimerLoopAsync(handle, binding, t.Seconds, passDelta: false, handle.TimerToken));
                        break;
                    case OnTickAttribute tick:
                        tasks.Add(RunTimerLoopAsync(handle, binding, tick.Seconds, passDelta: true, handle.TimerToken));
                        break;
                }
            }
        }
        return tasks;
    }

    private static async Task RunTimerLoopAsync(
        BehaviorHandle     handle,
        HandlerBinding     binding,
        double             seconds,
        bool               passDelta,
        CancellationToken  ct)
    {
        if (seconds <= 0) return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
        var lastTick = DateTime.UtcNow;

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                object[] args;
                if (passDelta)
                {
                    var now = DateTime.UtcNow;
                    args = [now - lastTick];
                    lastTick = now;
                }
                else
                {
                    args = [];
                }

                // Match by attribute-instance reference so other handlers for
                // the same event type (e.g. another [OnTimer(5)] method) do
                // not fire on this timer's tick.
                await BehaviorDispatch.InvokeAsync(
                    handle,
                    binding.Attribute.GetType(),
                    args,
                    filter: attr => ReferenceEquals(attr, binding.Attribute),
                    ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* disposal */ }
    }

    /// <summary>Fire every matching handler on every attached behavior.
    /// <paramref name="filter"/> lets callers narrow on attribute properties
    /// (e.g. <c>[OnChat(Channel = 5)]</c> only matches when channel 5 is
    /// dispatched). Null filter matches every handler of
    /// <paramref name="attributeType"/>. For targeted dispatch to one
    /// behavior, use <see cref="BehaviorHandle.DispatchAsync"/>.</summary>
    public async Task DispatchAsync(
        Type                         attributeType,
        object[]                     args,
        Func<EventAttribute, bool>?  filter = null,
        CancellationToken            ct = default)
    {
        ArgumentNullException.ThrowIfNull(attributeType);

        foreach (var handle in _attached.Keys)
            await BehaviorDispatch.InvokeAsync(handle, attributeType, args, filter, ct)
                .ConfigureAwait(false);
    }

    /// <summary>Typed convenience for chat. Fires only handlers whose
    /// <c>[OnChat(Channel = …)]</c> matches <paramref name="channel"/>.</summary>
    public Task RaiseChatAsync(
        Avatar             from,
        string             text,
        int                channel = 0,
        CancellationToken  ct      = default)
        => DispatchAsync(
            typeof(OnChatAttribute),
            [from, text],
            attr => ((OnChatAttribute)attr).Channel == channel,
            ct);

    internal void Detach(BehaviorHandle handle) => _attached.TryRemove(handle, out _);
}
