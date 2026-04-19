namespace Eden.Shared.Wire;

/// <summary>
/// The on-the-wire envelope: a one-byte <see cref="MessageKind"/> discriminator
/// followed by the MessagePack-encoded payload. The transport layer delivers
/// one envelope per frame, so no explicit length prefix is needed — MessagePack
/// is self-framing within a given payload.
/// </summary>
public static class Envelope
{
    /// <summary>Encode a payload into an envelope frame.</summary>
    public static byte[] Encode<T>(MessageKind kind, T payload)
    {
        var body = WireFormat.Serialize(payload);
        var frame = new byte[1 + body.Length];
        frame[0] = (byte)kind;
        body.AsSpan().CopyTo(frame.AsSpan(1));
        return frame;
    }

    /// <summary>Peek the kind tag without decoding the payload.</summary>
    public static MessageKind PeekKind(ReadOnlyMemory<byte> frame)
    {
        if (frame.IsEmpty)
            throw new ArgumentException("Empty frame — no kind tag.", nameof(frame));
        return (MessageKind)frame.Span[0];
    }

    /// <summary>Decode the payload as <typeparamref name="T"/>. Caller must have
    /// already checked the kind tag.</summary>
    public static T DecodePayload<T>(ReadOnlyMemory<byte> frame)
    {
        if (frame.IsEmpty)
            throw new ArgumentException("Empty frame.", nameof(frame));
        return WireFormat.Deserialize<T>(frame[1..]);
    }
}
