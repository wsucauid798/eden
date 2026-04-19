using MessagePack;
using MessagePack.Resolvers;

namespace Eden.Shared.Wire;

/// <summary>
/// Wire-format serialisation for Eden messages. MessagePack over the QUIC
/// transport. Uses the contractless resolver so domain types stay pure
/// records with no serialisation attributes bleeding in.
/// </summary>
public static class WireFormat
{
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard
            .WithResolver(ContractlessStandardResolver.Instance)
            .WithCompression(MessagePackCompression.None);

    public static byte[] Serialize<T>(T value)
        => MessagePackSerializer.Serialize(value, Options);

    public static T Deserialize<T>(ReadOnlyMemory<byte> bytes)
        => MessagePackSerializer.Deserialize<T>(bytes, Options);
}
