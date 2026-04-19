using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Eden.Scripting.Host;

/// <summary>
/// Shared handler-invocation logic used by both broadcast dispatch
/// (<see cref="BehaviorHost.DispatchAsync"/>) and targeted dispatch
/// (<see cref="BehaviorHandle.DispatchAsync"/>). Respects <c>[SerializeHandlers]</c>
/// and swallows handler exceptions with a warning so a misbehaving script
/// cannot break its peers or crash the server.
/// </summary>
internal static class BehaviorDispatch
{
    public static async Task InvokeAsync(
        BehaviorHandle               handle,
        Type                         attributeType,
        object[]                     args,
        Func<EventAttribute, bool>?  filter,
        CancellationToken            ct)
    {
        if (!handle.Descriptor.Handlers.TryGetValue(attributeType, out var bindings))
            return;

        if (handle.Gate is not null)
        {
            await handle.Gate.WaitAsync(ct).ConfigureAwait(false);
            try     { await InvokeBindingsAsync(handle, bindings, args, filter).ConfigureAwait(false); }
            finally { handle.Gate.Release(); }
        }
        else
        {
            await InvokeBindingsAsync(handle, bindings, args, filter).ConfigureAwait(false);
        }
    }

    private static async Task InvokeBindingsAsync(
        BehaviorHandle                 handle,
        IReadOnlyList<HandlerBinding>  bindings,
        object[]                       args,
        Func<EventAttribute, bool>?    filter)
    {
        foreach (var binding in bindings)
        {
            if (filter is not null && !filter(binding.Attribute)) continue;

            try
            {
                var task = (Task)binding.Method.Invoke(handle.Behavior, args)!;
                await task.ConfigureAwait(false);
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                handle.Log.LogWarning(tie.InnerException,
                    "Handler {Method} on {Type} threw",
                    binding.Method.Name, handle.Behavior.GetType().Name);
            }
            catch (Exception ex)
            {
                handle.Log.LogWarning(ex,
                    "Handler {Method} on {Type} threw",
                    binding.Method.Name, handle.Behavior.GetType().Name);
            }
        }
    }
}
