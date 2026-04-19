using Eden.Shared.Entities;
using Eden.Shared.Ids;
using Eden.Shared.Math;
using Eden.Shared.Wire.Messages;
using MessagePack;
using MessagePack.Formatters;

namespace Eden.Shared.Wire.Formatters;

public sealed class EdenResolver : IFormatterResolver
{
    public static readonly EdenResolver Instance = new();

    private EdenResolver() { }

    public IMessagePackFormatter<T>? GetFormatter<T>() => FormatterCache<T>.Formatter;

    private static class FormatterCache<T>
    {
        public static readonly IMessagePackFormatter<T>? Formatter = Find();

        private static IMessagePackFormatter<T>? Find()
        {
            var t = typeof(T);

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(EdenId<>))
            {
                var tag = t.GetGenericArguments()[0];
                var formatterType = typeof(EdenIdFormatter<>).MakeGenericType(tag);
                var instance = formatterType
                    .GetField(nameof(EdenIdFormatter<UserTag>.Instance))!
                    .GetValue(null);
                return (IMessagePackFormatter<T>)instance!;
            }

            object? f =
                t == typeof(Vector3)      ? Vector3Formatter.Instance :
                t == typeof(Quaternion)   ? QuaternionFormatter.Instance :
                t == typeof(Color)        ? ColorFormatter.Instance :
                t == typeof(Transform)    ? TransformFormatter.Instance :
                t == typeof(AvatarState)  ? AvatarStateFormatter.Instance :
                t == typeof(PrimState)    ? PrimStateFormatter.Instance :
                t == typeof(ClientHello)  ? ClientHelloFormatter.Instance :
                t == typeof(ServerHello)  ? ServerHelloFormatter.Instance :
                t == typeof(Ping)         ? PingFormatter.Instance :
                t == typeof(Pong)         ? PongFormatter.Instance :
                t == typeof(AvatarUpdate) ? AvatarUpdateFormatter.Instance :
                t == typeof(PrimUpdate)   ? PrimUpdateFormatter.Instance :
                t == typeof(AvatarLeft)   ? AvatarLeftFormatter.Instance :
                t == typeof(ChatMessage)  ? ChatMessageFormatter.Instance :
                null;

            return f is null ? null : (IMessagePackFormatter<T>)f;
        }
    }
}
