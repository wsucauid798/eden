using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Eden.Scripting.Host;

/// <summary>
/// Live handle to one attached <see cref="EdenBehavior"/> instance. Dispose
/// to run <c>OnDisable</c> and remove the behavior from its host's dispatch
/// set. Idempotent — safe to dispose multiple times.
/// </summary>
public sealed class BehaviorHandle : IAsyncDisposable
{
    private readonly BehaviorHost _host;
    private readonly CancellationTokenSource _timerCts = new();
    private List<Task>? _timerTasks;
    private int _disposed;

    public EdenBehavior Behavior { get; }
    internal BehaviorDescriptor Descriptor { get; }
    internal ILogger Log { get; }

    /// <summary>Non-null when the behavior opted into <c>[SerializeHandlers]</c>.</summary>
    internal SemaphoreSlim? Gate { get; }

    /// <summary>Cancelled when the handle is disposed — used to tear down
    /// every <see cref="OnTimerAttribute"/> / <see cref="OnTickAttribute"/>
    /// loop this behavior started.</summary>
    internal CancellationToken TimerToken => _timerCts.Token;

    internal BehaviorHandle(
        BehaviorHost        host,
        EdenBehavior        behavior,
        BehaviorDescriptor  descriptor,
        ILogger             log)
    {
        _host       = host;
        Behavior    = behavior;
        Descriptor  = descriptor;
        Log         = log;
        Gate        = descriptor.SerializeHandlers ? new SemaphoreSlim(1, 1) : null;
    }

    internal void RegisterTimers(List<Task> tasks) => _timerTasks = tasks;

    /// <summary>Fire matching handlers on <em>this</em> behavior only. Used
    /// when an event targets a specific behavior (e.g. the prim that was
    /// actually touched) rather than fanning out to every attached behavior.</summary>
    public Task DispatchAsync(
        Type                         attributeType,
        object[]                     args,
        Func<EventAttribute, bool>?  filter = null,
        CancellationToken            ct = default)
    {
        ArgumentNullException.ThrowIfNull(attributeType);
        return BehaviorDispatch.InvokeAsync(this, attributeType, args, filter, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _host.Detach(this);

        // Cancel every [OnTimer]/[OnTick] loop and wait for them to exit
        // before we call OnDisable — guarantees no tick fires after disable.
        _timerCts.Cancel();
        if (_timerTasks is not null)
        {
            try { await Task.WhenAll(_timerTasks).ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
            catch (Exception ex) { Log.LogWarning(ex, "Timer loop threw on shutdown"); }
        }

        try
        {
            var task = (Task)Descriptor.OnDisableMethod.Invoke(Behavior, null)!;
            await task.ConfigureAwait(false);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            Log.LogWarning(tie.InnerException, "OnDisable threw in {Type}",
                Behavior.GetType().Name);
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "OnDisable threw in {Type}", Behavior.GetType().Name);
        }

        Gate?.Dispose();
        _timerCts.Dispose();
    }
}
