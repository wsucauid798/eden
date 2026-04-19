using Eden.Shared.Wire.Formatters;
using MessagePack;
using MessagePack.Resolvers;

namespace Eden.Shared.Wire;

/// <summary>
/// Wire-format serialisation for Eden messages. MessagePack over the QUIC
/// transport. <see cref="EdenResolver"/> provides array-keyed formatters for
/// every domain record in <c>Eden.Shared</c> so records stay pure (no
/// <c>[Key]</c> attributes) while the wire payload carries no property-name
/// overhead. Contractless resolver handles anything the Eden resolver doesn't.
/// </summary>
public static class WireFormat
{
    private static readonly IFormatterResolver Resolver =
        CompositeResolver.Create(
            EdenResolver.Instance,
            ContractlessStandardResolver.Instance);

    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard
            .WithResolver(Resolver)
            .WithCompression(MessagePackCompression.None);

    public static byte[] Serialize<T>(T value)
        => MessagePackSerializer.Serialize(value, Options);

    public static T Deserialize<T>(ReadOnlyMemory<byte> bytes)
        => MessagePackSerializer.Deserialize<T>(bytes, Options);
}
