using Eden.Shared.Ids;

namespace Eden.Shared.Wire.Messages;

/// <summary>
/// Probe frame sent before the handshake. Lets ops / tests / tooling verify
/// the server is accepting connections and responsive without opening a full
/// session. The server always accepts this even without a <see cref="ClientHello"/>.
/// </summary>
public readonly record struct Healthcheck();

/// <summary>
/// Reply to <see cref="Healthcheck"/>. Carries enough context to be useful in
/// a load balancer, an uptime probe, or a smoke test: who the server is,
/// what wire protocol it speaks, how long it's been running, and how many
/// sessions it's currently hosting.
/// </summary>
public readonly record struct HealthcheckReply(
    string           Product,
    string           Release,
    string           WireProtocol,
    long             UptimeSeconds,
    int              SessionCount,
    EdenId<WorldTag> WorldId);
