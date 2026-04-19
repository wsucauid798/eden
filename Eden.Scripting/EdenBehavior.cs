using Microsoft.Extensions.Logging;

namespace Eden.Scripting;

/// <summary>
/// Base class for Eden in-world scripts. A behavior is attached to a prim
/// and reacts to events declared via attributes (<see cref="OnTouchAttribute"/>,
/// <see cref="OnCollisionStartAttribute"/>, …). Behaviors speak to the world
/// through the injected <see cref="Self"/>, <see cref="World"/>, and
/// <see cref="Log"/> capabilities — no global functions, no ambient state.
/// See <c>_docs/_design/scripting-model.md</c> for the shape rationale.
/// </summary>
public abstract class EdenBehavior
{
    /// <summary>The prim this behavior is attached to.</summary>
    public ISelfContext Self { get; internal set; } = null!;

    /// <summary>The surrounding world — inventory, avatars, physics, lookups.</summary>
    public IWorldContext World { get; internal set; } = null!;

    /// <summary>Structured logger scoped to this behavior.</summary>
    public ILogger Log { get; internal set; } = null!;

    /// <summary>Called once after the behavior is attached and its context is
    /// wired, and again on every world load. Override for setup work.</summary>
    protected virtual Task OnEnable() => Task.CompletedTask;

    /// <summary>Called when the behavior is detached or the world is shutting
    /// down. Override for cleanup work.</summary>
    protected virtual Task OnDisable() => Task.CompletedTask;
}
