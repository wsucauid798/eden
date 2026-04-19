using System.Collections.Concurrent;
using System.Reflection;

namespace Eden.Scripting.Host;

/// <summary>A handler method paired with the attribute instance that declared
/// it. Keeping the instance lets the host filter on properties like
/// <c>Channel</c> on <see cref="OnChatAttribute"/>.</summary>
public sealed record HandlerBinding(MethodInfo Method, EventAttribute Attribute);

/// <summary>
/// Cached reflection metadata for an <see cref="EdenBehavior"/> subclass:
/// its lifecycle <see cref="MethodInfo"/>s and a map of event-attribute type
/// → handler methods. One descriptor per behavior type; constructed on first
/// attachment and reused thereafter.
/// </summary>
internal sealed class BehaviorDescriptor
{
    private static readonly ConcurrentDictionary<Type, BehaviorDescriptor> Cache = new();

    public Type BehaviorType { get; }
    public MethodInfo OnEnableMethod { get; }
    public MethodInfo OnDisableMethod { get; }

    /// <summary>Handler bindings keyed by attribute type. Each binding keeps
    /// the attribute <em>instance</em> so dispatch can filter on its properties
    /// (e.g. <c>[OnChat(Channel = 5)]</c>).</summary>
    public IReadOnlyDictionary<Type, IReadOnlyList<HandlerBinding>> Handlers { get; }
    public bool SerializeHandlers { get; }

    private BehaviorDescriptor(
        Type type,
        MethodInfo onEnable,
        MethodInfo onDisable,
        IReadOnlyDictionary<Type, IReadOnlyList<HandlerBinding>> handlers,
        bool serializeHandlers)
    {
        BehaviorType      = type;
        OnEnableMethod    = onEnable;
        OnDisableMethod   = onDisable;
        Handlers          = handlers;
        SerializeHandlers = serializeHandlers;
    }

    public static BehaviorDescriptor For(Type type) => Cache.GetOrAdd(type, Build);

    private static BehaviorDescriptor Build(Type type)
    {
        if (!typeof(EdenBehavior).IsAssignableFrom(type) || type.IsAbstract)
            throw new ArgumentException(
                $"{type.FullName} must be a concrete subclass of EdenBehavior.", nameof(type));

        var baseFlags  = BindingFlags.Instance | BindingFlags.NonPublic;
        var onEnable   = typeof(EdenBehavior).GetMethod("OnEnable",  baseFlags)!;
        var onDisable  = typeof(EdenBehavior).GetMethod("OnDisable", baseFlags)!;

        var handlers = new Dictionary<Type, IReadOnlyList<HandlerBinding>>();
        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            foreach (var attr in method.GetCustomAttributes(inherit: true).OfType<EventAttribute>())
            {
                var attrType = attr.GetType();
                var binding  = new HandlerBinding(method, attr);
                if (!handlers.TryGetValue(attrType, out var existing))
                    handlers[attrType] = new List<HandlerBinding> { binding };
                else
                    ((List<HandlerBinding>)existing).Add(binding);
            }
        }

        var serialize = type.GetCustomAttribute<SerializeHandlersAttribute>() is not null;

        return new BehaviorDescriptor(type, onEnable, onDisable, handlers, serialize);
    }
}
