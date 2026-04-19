using System.Buffers;
using Eden.Shared.Entities;
using Eden.Shared.Ids;
using Eden.Shared.Math;
using Eden.Shared.Wire.Messages;
using MessagePack;
using MessagePack.Formatters;

namespace Eden.Shared.Wire.Formatters;

public sealed class Vector3Formatter : IMessagePackFormatter<Vector3>
{
    public static readonly Vector3Formatter Instance = new();

    public void Serialize(ref MessagePackWriter writer, Vector3 value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(3);
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    public Vector3 Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadArrayHeader();
        if (count != 3)
            throw new MessagePackSerializationException($"Vector3 expects 3 elements, got {count}");
        return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }
}

public sealed class QuaternionFormatter : IMessagePackFormatter<Quaternion>
{
    public static readonly QuaternionFormatter Instance = new();

    public void Serialize(ref MessagePackWriter writer, Quaternion value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(4);
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
        writer.Write(value.W);
    }

    public Quaternion Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadArrayHeader();
        if (count != 4)
            throw new MessagePackSerializationException($"Quaternion expects 4 elements, got {count}");
        return new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }
}

public sealed class ColorFormatter : IMessagePackFormatter<Color>
{
    public static readonly ColorFormatter Instance = new();

    public void Serialize(ref MessagePackWriter writer, Color value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(4);
        writer.Write(value.R);
        writer.Write(value.G);
        writer.Write(value.B);
        writer.Write(value.A);
    }

    public Color Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadArrayHeader();
        if (count != 4)
            throw new MessagePackSerializationException($"Color expects 4 elements, got {count}");
        return new Color(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
    }
}

public sealed class TransformFormatter : IMessagePackFormatter<Transform>
{
    public static readonly TransformFormatter Instance = new();

    public void Serialize(ref MessagePackWriter writer, Transform value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(2);
        Vector3Formatter.Instance.Serialize(ref writer, value.Position, options);
        QuaternionFormatter.Instance.Serialize(ref writer, value.Rotation, options);
    }

    public Transform Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadArrayHeader();
        if (count != 2)
            throw new MessagePackSerializationException($"Transform expects 2 elements, got {count}");
        var pos = Vector3Formatter.Instance.Deserialize(ref reader, options);
        var rot = QuaternionFormatter.Instance.Deserialize(ref reader, options);
        return new Transform(pos, rot);
    }
}

public sealed class EdenIdFormatter<TTag> : IMessagePackFormatter<EdenId<TTag>>
{
    public static readonly EdenIdFormatter<TTag> Instance = new();

    public void Serialize(ref MessagePackWriter writer, EdenId<TTag> value, MessagePackSerializerOptions options)
    {
        var bytes = new byte[16];
        if (!value.Value.TryWriteBytes(bytes))
            throw new MessagePackSerializationException("EdenId: Guid.TryWriteBytes failed");
        writer.Write(bytes);
    }

    public EdenId<TTag> Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var seq = reader.ReadBytes();
        if (seq is null)
            throw new MessagePackSerializationException("EdenId: expected non-nil binary");
        if (seq.Value.Length != 16)
            throw new MessagePackSerializationException($"EdenId: expected 16 bytes, got {seq.Value.Length}");
        return new EdenId<TTag>(new Guid(seq.Value.ToArray()));
    }
}

public sealed class AvatarStateFormatter : IMessagePackFormatter<AvatarState>
{
    public static readonly AvatarStateFormatter Instance = new();

    public void Serialize(ref MessagePackWriter writer, AvatarState value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(6);
        EdenIdFormatter<UserTag>.Instance.Serialize(ref writer, value.UserId, options);
        EdenIdFormatter<SessionTag>.Instance.Serialize(ref writer, value.SessionId, options);
        writer.Write(value.DisplayName);
        TransformFormatter.Instance.Serialize(ref writer, value.Transform, options);
        Vector3Formatter.Instance.Serialize(ref writer, value.Velocity, options);
        writer.Write(value.AppearanceHash);
    }

    public AvatarState Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadArrayHeader();
        if (count != 6)
            throw new MessagePackSerializationException($"AvatarState expects 6 elements, got {count}");
        return new AvatarState(
            EdenIdFormatter<UserTag>.Instance.Deserialize(ref reader, options),
            EdenIdFormatter<SessionTag>.Instance.Deserialize(ref reader, options),
            reader.ReadString() ?? string.Empty,
            TransformFormatter.Instance.Deserialize(ref reader, options),
            Vector3Formatter.Instance.Deserialize(ref reader, options),
            reader.ReadUInt32());
    }
}

public sealed class PrimStateFormatter : IMessagePackFormatter<PrimState>
{
    public static readonly PrimStateFormatter Instance = new();

    public void Serialize(ref MessagePackWriter writer, PrimState value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(7);
        EdenIdFormatter<PrimTag>.Instance.Serialize(ref writer, value.Id, options);
        EdenIdFormatter<UserTag>.Instance.Serialize(ref writer, value.OwnerId, options);
        TransformFormatter.Instance.Serialize(ref writer, value.Transform, options);
        Vector3Formatter.Instance.Serialize(ref writer, value.Scale, options);
        EdenIdFormatter<AssetTag>.Instance.Serialize(ref writer, value.ShapeAssetId, options);
        ColorFormatter.Instance.Serialize(ref writer, value.TintColor, options);
        writer.Write((uint)value.Flags);
    }

    public PrimState Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadArrayHeader();
        if (count != 7)
            throw new MessagePackSerializationException($"PrimState expects 7 elements, got {count}");
        return new PrimState(
            EdenIdFormatter<PrimTag>.Instance.Deserialize(ref reader, options),
            EdenIdFormatter<UserTag>.Instance.Deserialize(ref reader, options),
            TransformFormatter.Instance.Deserialize(ref reader, options),
            Vector3Formatter.Instance.Deserialize(ref reader, options),
            EdenIdFormatter<AssetTag>.Instance.Deserialize(ref reader, options),
            ColorFormatter.Instance.Deserialize(ref reader, options),
            (PrimFlags)reader.ReadUInt32());
    }
}

public sealed class ClientHelloFormatter : IMessagePackFormatter<ClientHello>
{
    public static readonly ClientHelloFormatter Instance = new();

    public void Serialize(ref MessagePackWriter writer, ClientHello value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(4);
        writer.Write(value.ClientName);
        writer.Write(value.ClientVersion);
        writer.Write(value.WireProtocol);
        writer.Write(value.AuthToken);
    }

    public ClientHello Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadArrayHeader();
        if (count != 4)
            throw new MessagePackSerializationException($"ClientHello expects 4 elements, got {count}");
        return new ClientHello(
            reader.ReadString() ?? string.Empty,
            reader.ReadString() ?? string.Empty,
            reader.ReadString() ?? string.Empty,
            reader.ReadString());
    }
}

public sealed class ServerHelloFormatter : IMessagePackFormatter<ServerHello>
{
    public static readonly ServerHelloFormatter Instance = new();

    public void Serialize(ref MessagePackWriter writer, ServerHello value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(5);
        EdenIdFormatter<SessionTag>.Instance.Serialize(ref writer, value.SessionId, options);
        EdenIdFormatter<UserTag>.Instance.Serialize(ref writer, value.UserId, options);
        writer.Write(value.WireProtocol);
        EdenIdFormatter<WorldTag>.Instance.Serialize(ref writer, value.WorldId, options);
        writer.Write(value.RejectReason);
    }

    public ServerHello Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadArrayHeader();
        if (count != 5)
            throw new MessagePackSerializationException($"ServerHello expects 5 elements, got {count}");
        return new ServerHello(
            EdenIdFormatter<SessionTag>.Instance.Deserialize(ref reader, options),
            EdenIdFormatter<UserTag>.Instance.Deserialize(ref reader, options),
            reader.ReadString() ?? string.Empty,
            EdenIdFormatter<WorldTag>.Instance.Deserialize(ref reader, options),
            reader.ReadString());
    }
}

public sealed class PingFormatter : IMessagePackFormatter<Ping>
{
    public static readonly PingFormatter Instance = new();

    public void Serialize(ref MessagePackWriter writer, Ping value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(1);
        writer.Write(value.ClientTicks);
    }

    public Ping Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadArrayHeader();
        if (count != 1)
            throw new MessagePackSerializationException($"Ping expects 1 element, got {count}");
        return new Ping(reader.ReadInt64());
    }
}

public sealed class PongFormatter : IMessagePackFormatter<Pong>
{
    public static readonly PongFormatter Instance = new();

    public void Serialize(ref MessagePackWriter writer, Pong value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(2);
        writer.Write(value.ClientTicks);
        writer.Write(value.ServerTicks);
    }

    public Pong Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadArrayHeader();
        if (count != 2)
            throw new MessagePackSerializationException($"Pong expects 2 elements, got {count}");
        return new Pong(reader.ReadInt64(), reader.ReadInt64());
    }
}

public sealed class AvatarUpdateFormatter : IMessagePackFormatter<AvatarUpdate>
{
    public static readonly AvatarUpdateFormatter Instance = new();

    public void Serialize(ref MessagePackWriter writer, AvatarUpdate value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(1);
        AvatarStateFormatter.Instance.Serialize(ref writer, value.State, options);
    }

    public AvatarUpdate Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadArrayHeader();
        if (count != 1)
            throw new MessagePackSerializationException($"AvatarUpdate expects 1 element, got {count}");
        return new AvatarUpdate(AvatarStateFormatter.Instance.Deserialize(ref reader, options));
    }
}

public sealed class PrimUpdateFormatter : IMessagePackFormatter<PrimUpdate>
{
    public static readonly PrimUpdateFormatter Instance = new();

    public void Serialize(ref MessagePackWriter writer, PrimUpdate value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(1);
        PrimStateFormatter.Instance.Serialize(ref writer, value.State, options);
    }

    public PrimUpdate Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadArrayHeader();
        if (count != 1)
            throw new MessagePackSerializationException($"PrimUpdate expects 1 element, got {count}");
        return new PrimUpdate(PrimStateFormatter.Instance.Deserialize(ref reader, options));
    }
}

public sealed class AvatarLeftFormatter : IMessagePackFormatter<AvatarLeft>
{
    public static readonly AvatarLeftFormatter Instance = new();

    public void Serialize(ref MessagePackWriter writer, AvatarLeft value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(1);
        EdenIdFormatter<UserTag>.Instance.Serialize(ref writer, value.UserId, options);
    }

    public AvatarLeft Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadArrayHeader();
        if (count != 1)
            throw new MessagePackSerializationException($"AvatarLeft expects 1 element, got {count}");
        return new AvatarLeft(EdenIdFormatter<UserTag>.Instance.Deserialize(ref reader, options));
    }
}

public sealed class HealthcheckFormatter : IMessagePackFormatter<Healthcheck>
{
    public static readonly HealthcheckFormatter Instance = new();

    public void Serialize(ref MessagePackWriter writer, Healthcheck value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(0);
    }

    public Healthcheck Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadArrayHeader();
        if (count != 0)
            throw new MessagePackSerializationException($"Healthcheck expects 0 elements, got {count}");
        return new Healthcheck();
    }
}

public sealed class HealthcheckReplyFormatter : IMessagePackFormatter<HealthcheckReply>
{
    public static readonly HealthcheckReplyFormatter Instance = new();

    public void Serialize(ref MessagePackWriter writer, HealthcheckReply value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(6);
        writer.Write(value.Product);
        writer.Write(value.Release);
        writer.Write(value.WireProtocol);
        writer.Write(value.UptimeSeconds);
        writer.Write(value.SessionCount);
        EdenIdFormatter<WorldTag>.Instance.Serialize(ref writer, value.WorldId, options);
    }

    public HealthcheckReply Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadArrayHeader();
        if (count != 6)
            throw new MessagePackSerializationException($"HealthcheckReply expects 6 elements, got {count}");
        return new HealthcheckReply(
            reader.ReadString() ?? string.Empty,
            reader.ReadString() ?? string.Empty,
            reader.ReadString() ?? string.Empty,
            reader.ReadInt64(),
            reader.ReadInt32(),
            EdenIdFormatter<WorldTag>.Instance.Deserialize(ref reader, options));
    }
}

public sealed class ChatMessageFormatter : IMessagePackFormatter<ChatMessage>
{
    public static readonly ChatMessageFormatter Instance = new();

    public void Serialize(ref MessagePackWriter writer, ChatMessage value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(3);
        EdenIdFormatter<UserTag>.Instance.Serialize(ref writer, value.From, options);
        writer.Write(value.Channel);
        writer.Write(value.Text);
    }

    public ChatMessage Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadArrayHeader();
        if (count != 3)
            throw new MessagePackSerializationException($"ChatMessage expects 3 elements, got {count}");
        return new ChatMessage(
            EdenIdFormatter<UserTag>.Instance.Deserialize(ref reader, options),
            reader.ReadInt32(),
            reader.ReadString() ?? string.Empty);
    }
}
