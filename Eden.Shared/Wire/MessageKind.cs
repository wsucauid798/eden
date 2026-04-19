namespace Eden.Shared.Wire;

/// <summary>
/// The discriminator for every message on the wire. One byte; leaves room for
/// 255 message types. Values 0 and 255 are reserved (0 = invalid/EOF sentinel,
/// 255 = reserved for future protocol extension).
/// </summary>
public enum MessageKind : byte
{
    Invalid      = 0,

    // --- Handshake ---
    ClientHello  = 1,
    ServerHello  = 2,

    // --- World state ---
    AvatarUpdate = 10,
    PrimUpdate   = 11,
    AvatarLeft   = 12,

    // --- Chat ---
    ChatMessage  = 20,

    // --- Keepalive ---
    Ping         = 250,
    Pong         = 251,

    Reserved     = 255,
}
