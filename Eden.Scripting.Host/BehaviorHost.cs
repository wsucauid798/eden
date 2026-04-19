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

        return handle;
    }

    /// <summary>Fire every matching handler on every attached behavior.
    /// Behaviors with no handler for <paramref name="attributeType"/> are
    /// skipped. Per-behavior handler order is declaration order; across
    /// behaviors the order is undefined.</summary>
    public async Task DispatchAsync(
        Type               attributeType,
        object[]           args,
        CancellationToken  ct = default)
    {
        ArgumentNullException.ThrowIfNull(attributeType);

        foreach (var handle in _attached.Keys)
        {
            if (!handle.Descriptor.Handlers.TryGetValue(attributeType, out var methods))
                continue;

            if (handle.Gate is not null)
            {
                await handle.Gate.WaitAsync(ct).ConfigureAwait(false);
                try     { await InvokeHandlersAsync(handle, methods, args).ConfigureAwait(false); }
                finally { handle.Gate.Release(); }
            }
            else
            {
                await InvokeHandlersAsync(handle, methods, args).ConfigureAwait(false);
            }
        }
    }

    private static async Task InvokeHandlersAsync(
        BehaviorHandle          handle,
        IReadOnlyList<MethodInfo> methods,
        object[]                args)
    {
        foreach (var method in methods)
        {
            try
            {
                var task = (Task)method.Invoke(handle.Behavior, args)!;
                await task.ConfigureAwait(false);
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                handle.Log.LogWarning(tie.InnerException,
                    "Handler {Method} on {Type} threw",
                    method.Name, handle.Behavior.GetType().Name);
            }
            catch (Exception ex)
            {
                handle.Log.LogWarning(ex,
                    "Handler {Method} on {Type} threw",
                    method.Name, handle.Behavior.GetType().Name);
            }
        }
    }

    internal void Detach(BehaviorHandle handle) => _attached.TryRemove(handle, out _);
}
