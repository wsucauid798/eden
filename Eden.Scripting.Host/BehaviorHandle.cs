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
    private int _disposed;

    public EdenBehavior Behavior { get; }
    internal BehaviorDescriptor Descriptor { get; }
    internal ILogger Log { get; }

    /// <summary>Non-null when the behavior opted into <c>[SerializeHandlers]</c>.</summary>
    internal SemaphoreSlim? Gate { get; }

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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _host.Detach(this);

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
    }
}
