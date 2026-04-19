namespace Eden.Shared.Wire.Messages;

/// <summary>
/// Client → server. Echoed back as <see cref="Pong"/>. Client-supplied ticks
/// let the client compute round-trip time without a separate clock sync.
/// </summary>
public readonly record struct Ping(long ClientTicks);

/// <summary>
/// Server → client reply to a <see cref="Ping"/>. Carries the client's
/// original ticks plus the server's own ticks (for drift / clock sync).
/// </summary>
public readonly record struct Pong(long ClientTicks, long ServerTicks);
