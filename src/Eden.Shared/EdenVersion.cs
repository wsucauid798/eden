namespace Eden.Shared;

/// <summary>
/// Brand and wire-protocol version markers shared by every Eden component.
/// Bump <see cref="WireProtocol"/> whenever the on-the-wire format changes
/// in a way that breaks older clients or servers.
/// </summary>
public static class EdenVersion
{
    public const string Product = "Eden";
    public const string Release = "0.0.1-alpha";

    /// <summary>Semver-style version string for the wire protocol.</summary>
    public const string WireProtocol = "0.2";
}
