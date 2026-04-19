using Eden.Shared.Ids;

namespace Eden.Shared.Wire.Messages;

/// <summary>
/// First message a viewer sends after opening a transport to a server.
/// Declares who they are and which wire-protocol version they speak.
/// </summary>
public readonly record struct ClientHello(
    string  ClientName,
    string  ClientVersion,
    string  WireProtocol,
    string? AuthToken);

/// <summary>
/// Server's reply to <see cref="ClientHello"/>. Either accepts (non-empty
/// <see cref="SessionId"/>) or rejects (sets <see cref="RejectReason"/>).
/// </summary>
public readonly record struct ServerHello(
    EdenId<SessionTag> SessionId,
    string             WireProtocol,
    EdenId<WorldTag>   WorldId,
    string?            RejectReason);
