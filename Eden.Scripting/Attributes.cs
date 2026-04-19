namespace Eden.Scripting;

/// <summary>Base class for event-handler attributes on <see cref="EdenBehavior"/> methods.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public abstract class EventAttribute : Attribute { }

/// <summary>Handler signature: <c>Task (Avatar who)</c>.</summary>
public sealed class OnTouchAttribute : EventAttribute { }

/// <summary>Handler signature: <c>Task (ICollision with)</c>.</summary>
public sealed class OnCollisionStartAttribute : EventAttribute { }

/// <summary>Handler signature: <c>Task (ICollision with)</c>.</summary>
public sealed class OnCollisionEndAttribute : EventAttribute { }

/// <summary>Fires when an avatar enters the region the prim sits in.
/// Handler signature: <c>Task (Avatar who)</c>.</summary>
public sealed class OnEnterAttribute : EventAttribute { }

/// <summary>Fires when an avatar leaves the region the prim sits in.
/// Handler signature: <c>Task (Avatar who)</c>.</summary>
public sealed class OnLeaveAttribute : EventAttribute { }

/// <summary>Handler signature: <c>Task (Avatar from, string text)</c>.
/// <see cref="Channel"/> defaults to 0 (public chat).</summary>
public sealed class OnChatAttribute : EventAttribute
{
    public int Channel { get; init; } = 0;
}

/// <summary>Handler signature: <c>Task (Avatar buyer, int coins)</c>.</summary>
public sealed class OnPaymentAttribute : EventAttribute { }

/// <summary>Periodic timer. Handler signature: <c>Task ()</c>.</summary>
public sealed class OnTimerAttribute(double seconds) : EventAttribute
{
    public double Seconds { get; } = seconds;
}

/// <summary>Per-tick callback, opt-in. Handler signature:
/// <c>Task (TimeSpan delta)</c>. Default 30 Hz.</summary>
public sealed class OnTickAttribute(double seconds = 1.0 / 30.0) : EventAttribute
{
    public double Seconds { get; } = seconds;
}

/// <summary>Marks a property as a user-editable script parameter. The
/// world-editor UI surfaces these; changing one takes effect on reload.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ConfigurableAttribute : Attribute { }

/// <summary>Marks a property to be serialized into the world save. Restored
/// on world load before <see cref="EdenBehavior.OnEnable"/> runs.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PersistentAttribute : Attribute { }

/// <summary>Applied to an <see cref="EdenBehavior"/> subclass: forces the
/// host to dispatch event handlers one at a time (single-file) instead of
/// concurrently. Use when the behavior's state isn't reentrant-safe.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SerializeHandlersAttribute : Attribute { }
